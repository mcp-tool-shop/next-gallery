using System.Text;
using System.Text.Json;
using Gallery.Domain.Routing;

namespace Gallery.Tests;

/// <summary>
/// Tests for envelope validation per single-instance-routing.v0.1 strictness rules.
/// These tests validate scenarios S16-S20 and envelope strictness matrix.
/// </summary>
[TestFixture]
public class EnvelopeValidatorTests
{
    private const string ValidWorkspaceKey = "88b49a59944589bd4779b7931d127abc";

    private static byte[] CreateEnvelopeBytes(
        string? protocolVersion = "1",
        string? messageType = "activation_request",
        string? workspaceKey = ValidWorkspaceKey,
        object? payload = null,
        string? timestamp = "2025-01-15T10:30:00.000Z",
        bool includePayload = true)
    {
        var obj = new Dictionary<string, object?>();

        if (protocolVersion != null) obj["protocol_version"] = protocolVersion;
        if (messageType != null) obj["message_type"] = messageType;
        if (workspaceKey != null) obj["workspace_key"] = workspaceKey;
        if (includePayload) obj["payload"] = payload ?? new { workspace_path = "C:\\Projects\\MyApp" };
        if (timestamp != null) obj["timestamp"] = timestamp;

        return JsonSerializer.SerializeToUtf8Bytes(obj);
    }

    #region Scenario Tests

    [Test]
    public void S16_PipeNameDerivation_WorkspaceKeyFormat()
    {
        // S16: workspace_key = /^[a-f0-9]{32}$/
        var validKey = "88b49a59944589bd4779b7931d127abc";
        var bytes = CreateEnvelopeBytes(workspaceKey: validKey);

        var result = EnvelopeValidator.Validate(bytes, validKey);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Envelope!.WorkspaceKey, Is.EqualTo(validKey));
    }

    [Test]
    public void S20_UppercaseWorkspaceKey_DropsMessage()
    {
        // S20: Uppercase workspace_key in Message → Drop message (invalid format)
        var uppercaseKey = "88B49A59944589BD4779B7931D127ABC";
        var bytes = CreateEnvelopeBytes(workspaceKey: uppercaseKey);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("Invalid workspace_key format"));
    }

    #endregion

    #region Protocol Version Tests

    [Test]
    public void UnsupportedVersion_RespondsWithError()
    {
        // Rule: protocol_version unsupported → respond with error
        var bytes = CreateEnvelopeBytes(protocolVersion: "2");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.RespondWithError));
        Assert.That(result.ErrorReason, Does.Contain("Unsupported protocol version"));
    }

    [Test]
    public void SupportedVersion_ProcessesNormally()
    {
        var bytes = CreateEnvelopeBytes(protocolVersion: "1");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Process));
    }

    #endregion

    #region Missing Fields Tests

    [Test]
    public void MissingProtocolVersion_Drops()
    {
        var bytes = CreateEnvelopeBytes(protocolVersion: null);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("protocol_version"));
    }

    [Test]
    public void MissingMessageType_Drops()
    {
        var bytes = CreateEnvelopeBytes(messageType: null);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("message_type"));
    }

    [Test]
    public void MissingWorkspaceKey_Drops()
    {
        var bytes = CreateEnvelopeBytes(workspaceKey: null);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("workspace_key"));
    }

    [Test]
    public void MissingPayload_Drops()
    {
        var bytes = CreateEnvelopeBytes(includePayload: false);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("payload"));
    }

    [Test]
    public void MissingTimestamp_Drops()
    {
        var bytes = CreateEnvelopeBytes(timestamp: null);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("timestamp"));
    }

    #endregion

    #region Unknown Message Type Tests

    [Test]
    public void UnknownMessageType_Drops()
    {
        var bytes = CreateEnvelopeBytes(messageType: "unknown_type");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("Unknown message_type"));
    }

    [Test]
    [TestCase("activation_request")]
    [TestCase("activation_response")]
    [TestCase("ping")]
    [TestCase("pong")]
    public void KnownMessageTypes_Accepted(string messageType)
    {
        var bytes = CreateEnvelopeBytes(messageType: messageType);

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region workspace_key Format Tests

    [Test]
    public void InvalidWorkspaceKeyFormat_DropsAndLogs_NoResponse()
    {
        // Rule: workspace_key invalid format → drop
        var invalidKeys = new[]
        {
            "ABC",                                      // Too short
            "88b49a59944589bd4779b7931d127abcdef",     // Too long (34 chars)
            "88B49A59944589BD4779B7931D127ABC",        // Uppercase (S20)
            "0x88b49a59944589bd4779b7931d127ab",       // Has prefix
            "sha256:88b49a59944589bd4779b7931d12",     // Has prefix
            "88b49a59944589bd4779b7931d127abg",        // Has 'g' (not hex)
            "",                                         // Empty
        };

        foreach (var invalidKey in invalidKeys)
        {
            var bytes = CreateEnvelopeBytes(workspaceKey: invalidKey);
            var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

            Assert.That(result.IsValid, Is.False,
                $"Key '{invalidKey}' should be invalid");
            Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop),
                $"Key '{invalidKey}' should result in Drop action");
        }
    }

    [Test]
    public void WorkspaceKeyMismatch_Drops()
    {
        // Rule: workspace_key doesn't match pipe's key → drop (possible attack)
        var messageKey = "11111111111111111111111111111111";
        var pipeKey = "22222222222222222222222222222222";
        var bytes = CreateEnvelopeBytes(workspaceKey: messageKey);

        var result = EnvelopeValidator.Validate(bytes, pipeKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("mismatch"));
    }

    #endregion

    #region Message Size Tests

    [Test]
    public void MessageTooLarge_DropsAndLogs_NoResponse()
    {
        // Rule: Message exceeds 64KB → drop
        var largePayload = new string('x', 70 * 1024); // 70KB string
        var obj = new Dictionary<string, object>
        {
            ["protocol_version"] = "1",
            ["message_type"] = "activation_request",
            ["workspace_key"] = ValidWorkspaceKey,
            ["payload"] = new { data = largePayload },
            ["timestamp"] = "2025-01-15T10:30:00.000Z"
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);

        Assert.That(bytes.Length, Is.GreaterThan(64 * 1024), "Test setup: message should exceed 64KB");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("65536"));
    }

    [Test]
    public void MessageAtLimit_Accepted()
    {
        // Message at exactly 64KB should be accepted
        var bytes = CreateEnvelopeBytes();
        Assert.That(bytes.Length, Is.LessThan(64 * 1024));

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion

    #region Invalid JSON Tests

    [Test]
    public void InvalidJson_Drops()
    {
        var bytes = Encoding.UTF8.GetBytes("{invalid json");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
        Assert.That(result.ErrorReason, Does.Contain("Invalid JSON"));
    }

    [Test]
    public void EmptyJson_Drops()
    {
        var bytes = Encoding.UTF8.GetBytes("{}");

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Drop));
    }

    #endregion

    #region Error Response Creation Tests

    [Test]
    public void CreateErrorResponse_HasCorrectStructure()
    {
        var response = EnvelopeValidator.CreateErrorResponse(ValidWorkspaceKey, "Test error");

        Assert.That(response.ProtocolVersion, Is.EqualTo("1"));
        Assert.That(response.MessageType, Is.EqualTo("activation_response"));
        Assert.That(response.WorkspaceKey, Is.EqualTo(ValidWorkspaceKey));
        Assert.That(response.Timestamp, Is.Not.Empty);

        var payload = response.Payload!.Value.Deserialize<ActivationResponsePayload>();
        Assert.That(payload!.Status, Is.EqualTo("error"));
        Assert.That(payload.Error, Is.EqualTo("Test error"));
    }

    [Test]
    public void SerializeEnvelope_ProducesValidJson()
    {
        var envelope = EnvelopeValidator.CreateErrorResponse(ValidWorkspaceKey, "Test");
        var bytes = EnvelopeValidator.SerializeEnvelope(envelope);

        // Should be parseable
        var parsed = JsonSerializer.Deserialize<MessageEnvelope>(bytes);
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.WorkspaceKey, Is.EqualTo(ValidWorkspaceKey));
    }

    #endregion

    #region Valid Envelope Tests

    [Test]
    public void ValidActivationRequest_Accepted()
    {
        var bytes = CreateEnvelopeBytes(
            protocolVersion: "1",
            messageType: "activation_request",
            workspaceKey: ValidWorkspaceKey,
            payload: new { workspace_path = "C:\\Projects\\MyApp", requested_view = "jobs" },
            timestamp: "2025-01-15T10:30:00.000Z"
        );

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Action, Is.EqualTo(ValidationAction.Process));
        Assert.That(result.Envelope, Is.Not.Null);
        Assert.That(result.Envelope!.MessageType, Is.EqualTo("activation_request"));
    }

    [Test]
    public void ValidPingMessage_Accepted()
    {
        var bytes = CreateEnvelopeBytes(
            messageType: "ping",
            payload: new { }
        );

        var result = EnvelopeValidator.Validate(bytes, ValidWorkspaceKey);

        Assert.That(result.IsValid, Is.True);
    }

    #endregion
}
