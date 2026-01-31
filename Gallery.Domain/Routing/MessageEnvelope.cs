using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gallery.Domain.Routing;

/// <summary>
/// Message envelope for single-instance routing protocol v0.1.
/// All IPC messages use this versioned envelope format.
/// </summary>
public sealed class MessageEnvelope
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "";

    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("workspace_key")]
    public string WorkspaceKey { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
}

/// <summary>
/// Known message types in protocol v1.
/// </summary>
public static class MessageTypes
{
    public const string ActivationRequest = "activation_request";
    public const string ActivationResponse = "activation_response";
    public const string Ping = "ping";
    public const string Pong = "pong";

    public static readonly HashSet<string> Known = new()
    {
        ActivationRequest,
        ActivationResponse,
        Ping,
        Pong
    };
}

/// <summary>
/// Payload for activation_request message.
/// </summary>
public sealed class ActivationRequestPayload
{
    [JsonPropertyName("workspace_path")]
    public string WorkspacePath { get; set; } = "";

    [JsonPropertyName("requested_view")]
    public string? RequestedView { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }
}

/// <summary>
/// Payload for activation_response message.
/// </summary>
public sealed class ActivationResponsePayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("window_state")]
    public string? WindowState { get; set; }

    [JsonPropertyName("navigated_to")]
    public string? NavigatedTo { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Payload for pong message.
/// </summary>
public sealed class PongPayload
{
    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("uptime_seconds")]
    public double UptimeSeconds { get; set; }
}
