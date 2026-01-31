using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gallery.Domain.Routing;

/// <summary>
/// Result of envelope validation.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public ValidationAction Action { get; init; }
    public string? ErrorReason { get; init; }
    public MessageEnvelope? Envelope { get; init; }

    public static ValidationResult Valid(MessageEnvelope envelope) => new()
    {
        IsValid = true,
        Action = ValidationAction.Process,
        Envelope = envelope
    };

    public static ValidationResult Drop(string reason) => new()
    {
        IsValid = false,
        Action = ValidationAction.Drop,
        ErrorReason = reason
    };

    public static ValidationResult RespondWithError(string reason) => new()
    {
        IsValid = false,
        Action = ValidationAction.RespondWithError,
        ErrorReason = reason
    };
}

/// <summary>
/// What to do with an invalid message.
/// </summary>
public enum ValidationAction
{
    Process,         // Valid - process normally
    Drop,            // Invalid - drop silently (log warning)
    RespondWithError // Invalid - respond with error (e.g., unsupported version)
}

/// <summary>
/// Validates message envelopes per single-instance-routing.v0.1 strictness rules.
/// Pure function, no I/O.
/// </summary>
public static partial class EnvelopeValidator
{
    public const string SupportedProtocolVersion = "1";
    public const int MaxMessageSizeBytes = 64 * 1024; // 64KB
    public const int MaxArgsLength = 100;
    public const int MaxWorkspacePathLength = 32 * 1024; // 32KB

    // workspace_key = /^[a-f0-9]{32}$/
    [GeneratedRegex(@"^[a-f0-9]{32}$", RegexOptions.Compiled)]
    private static partial Regex WorkspaceKeyPattern();

    /// <summary>
    /// Validate raw JSON bytes against envelope strictness rules.
    /// </summary>
    /// <param name="jsonBytes">Raw message bytes</param>
    /// <param name="expectedWorkspaceKey">The workspace key this pipe serves (for mismatch detection)</param>
    /// <returns>Validation result with action to take</returns>
    public static ValidationResult Validate(byte[] jsonBytes, int length, string expectedWorkspaceKey)
    {
        return Validate(jsonBytes.AsSpan(0, length), expectedWorkspaceKey);
    }

    /// <summary>
    /// Validate raw JSON bytes against envelope strictness rules.
    /// </summary>
    public static ValidationResult Validate(ReadOnlySpan<byte> jsonBytes, string expectedWorkspaceKey)
    {
        // Rule: Message exceeds 64KB → drop
        if (jsonBytes.Length > MaxMessageSizeBytes)
        {
            return ValidationResult.Drop($"Message exceeds {MaxMessageSizeBytes} bytes ({jsonBytes.Length} bytes)");
        }

        // Parse JSON
        MessageEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MessageEnvelope>(jsonBytes)
                ?? throw new JsonException("Deserialized to null");
        }
        catch (JsonException ex)
        {
            return ValidationResult.Drop($"Invalid JSON: {ex.Message}");
        }

        return ValidateEnvelope(envelope, expectedWorkspaceKey);
    }

    /// <summary>
    /// Validate a parsed envelope against strictness rules.
    /// </summary>
    public static ValidationResult ValidateEnvelope(MessageEnvelope envelope, string expectedWorkspaceKey)
    {
        // Rule: Required envelope field missing → drop
        if (string.IsNullOrEmpty(envelope.ProtocolVersion))
        {
            return ValidationResult.Drop("Missing required field: protocol_version");
        }

        if (string.IsNullOrEmpty(envelope.MessageType))
        {
            return ValidationResult.Drop("Missing required field: message_type");
        }

        if (string.IsNullOrEmpty(envelope.WorkspaceKey))
        {
            return ValidationResult.Drop("Missing required field: workspace_key");
        }

        if (envelope.Payload is null)
        {
            return ValidationResult.Drop("Missing required field: payload");
        }

        if (string.IsNullOrEmpty(envelope.Timestamp))
        {
            return ValidationResult.Drop("Missing required field: timestamp");
        }

        // Rule: protocol_version unsupported → respond with error
        if (envelope.ProtocolVersion != SupportedProtocolVersion)
        {
            return ValidationResult.RespondWithError($"Unsupported protocol version: {envelope.ProtocolVersion}");
        }

        // Rule: message_type unknown → drop
        if (!MessageTypes.Known.Contains(envelope.MessageType))
        {
            return ValidationResult.Drop($"Unknown message_type: {envelope.MessageType}");
        }

        // Rule: workspace_key invalid format → drop
        if (!WorkspaceKeyPattern().IsMatch(envelope.WorkspaceKey))
        {
            return ValidationResult.Drop($"Invalid workspace_key format: {envelope.WorkspaceKey}");
        }

        // Rule: workspace_key doesn't match pipe's key → drop (possible attack)
        if (envelope.WorkspaceKey != expectedWorkspaceKey)
        {
            return ValidationResult.Drop($"workspace_key mismatch: expected {expectedWorkspaceKey}, got {envelope.WorkspaceKey}");
        }

        return ValidationResult.Valid(envelope);
    }

    /// <summary>
    /// Create a valid activation_response envelope for errors.
    /// </summary>
    public static MessageEnvelope CreateErrorResponse(string workspaceKey, string errorMessage)
    {
        return new MessageEnvelope
        {
            ProtocolVersion = SupportedProtocolVersion,
            MessageType = MessageTypes.ActivationResponse,
            WorkspaceKey = workspaceKey,
            Payload = JsonSerializer.SerializeToElement(new ActivationResponsePayload
            {
                Status = "error",
                Error = errorMessage
            }),
            Timestamp = DateTime.UtcNow.ToString("O")
        };
    }

    /// <summary>
    /// Serialize an envelope to JSON bytes with size enforcement.
    /// </summary>
    public static byte[] SerializeEnvelope(MessageEnvelope envelope)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (bytes.Length > MaxMessageSizeBytes)
        {
            throw new InvalidOperationException($"Serialized message exceeds {MaxMessageSizeBytes} bytes");
        }
        return bytes;
    }
}
