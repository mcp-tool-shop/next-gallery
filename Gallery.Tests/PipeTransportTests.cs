using System.Text.Json;
using Gallery.Domain.Routing;

namespace Gallery.Tests;

/// <summary>
/// Loopback tests for PipeTransport.
/// Tests server/client communication in same process.
/// </summary>
[TestFixture]
[Platform("Win")]  // Named pipes with message mode are Windows-only
public class PipeTransportTests
{
    private const string TestWorkspaceKey = "88b49a59944589bd4779b7931d127abc";

    [Test]
    public void DerivePipeName_MatchesSpec()
    {
        // S16: Pipe name derivation
        var pipeName = PipeTransport.DerivePipeName(TestWorkspaceKey);
        Assert.That(pipeName, Is.EqualTo($"codecomfy.nextgallery.{TestWorkspaceKey}"));
    }

    [Test]
    public async Task LoopbackTest_SendActivationRequest_ReceivesResponse()
    {
        var serverLog = new TestRoutingLog();
        var clientLog = new TestRoutingLog();

        using var server = new PipeTransport(TestWorkspaceKey, serverLog);
        using var client = new PipeTransport(TestWorkspaceKey, clientLog);

        MessageEnvelope? receivedRequest = null;

        // Start server with handler that echoes back
        server.MessageReceived += msg => receivedRequest = msg;
        server.StartServer(request =>
        {
            // Create activation_response
            return new MessageEnvelope
            {
                ProtocolVersion = EnvelopeValidator.SupportedProtocolVersion,
                MessageType = MessageTypes.ActivationResponse,
                WorkspaceKey = TestWorkspaceKey,
                Payload = JsonSerializer.SerializeToElement(new ActivationResponsePayload
                {
                    Status = "activated",
                    WindowState = "restored",
                    NavigatedTo = "jobs"
                }),
                Timestamp = DateTime.UtcNow.ToString("O")
            };
        });

        // Give server time to start
        await Task.Delay(100);

        // Send activation request
        var result = await client.SendActivationRequestAsync(
            new ActivationRequestPayload
            {
                WorkspacePath = @"C:\Projects\MyApp",
                RequestedView = "jobs"
            },
            connectTimeout: TimeSpan.FromSeconds(2),
            sendTimeout: TimeSpan.FromSeconds(1),
            receiveTimeout: TimeSpan.FromSeconds(5));

        await server.StopServerAsync();

        // Assertions
        Assert.That(result.IsSuccess, Is.True, $"Expected success, got {result.Outcome}");
        Assert.That(result.Response, Is.Not.Null);
        Assert.That(result.Response!.MessageType, Is.EqualTo(MessageTypes.ActivationResponse));

        var responsePayload = result.Response.Payload!.Value.Deserialize<ActivationResponsePayload>();
        Assert.That(responsePayload!.Status, Is.EqualTo("activated"));
        Assert.That(responsePayload.WindowState, Is.EqualTo("restored"));

        // Verify server received the request
        Assert.That(receivedRequest, Is.Not.Null);
        Assert.That(receivedRequest!.MessageType, Is.EqualTo(MessageTypes.ActivationRequest));
    }

    [Test]
    public async Task NoServer_ConnectTimeout()
    {
        var clientLog = new TestRoutingLog();
        using var client = new PipeTransport("nonexistent1234567890abcdef12345", clientLog);

        var result = await client.SendActivationRequestAsync(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            connectTimeout: TimeSpan.FromMilliseconds(500),
            sendTimeout: TimeSpan.FromSeconds(1),
            receiveTimeout: TimeSpan.FromSeconds(1));

        Assert.That(result.Outcome, Is.EqualTo(ActivationClientOutcome.ConnectTimeout));
        Assert.That(clientLog.HasWarning("timeout"), Is.True);
    }

    [Test]
    public async Task UnsupportedVersion_ServerRespondsWithError()
    {
        // S19 variant: Server receives message with unsupported version
        var serverLog = new TestRoutingLog();
        using var server = new PipeTransport(TestWorkspaceKey, serverLog);

        server.StartServer(_ => null); // No normal response needed

        await Task.Delay(100);

        // Manually send message with unsupported version using raw pipe
        using var rawClient = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            PipeTransport.DerivePipeName(TestWorkspaceKey),
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

        await rawClient.ConnectAsync(2000);

        var badEnvelope = new
        {
            protocol_version = "99",  // Unsupported
            message_type = "activation_request",
            workspace_key = TestWorkspaceKey,
            payload = new { workspace_path = "C:\\Test" },
            timestamp = DateTime.UtcNow.ToString("O")
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(badEnvelope);
        await rawClient.WriteAsync(bytes);
        await rawClient.FlushAsync();

        // Server should respond with error
        var responseBuffer = new byte[64 * 1024];
        var bytesRead = await rawClient.ReadAsync(responseBuffer);

        await server.StopServerAsync();

        Assert.That(bytesRead, Is.GreaterThan(0), "Server should respond with error");

        var response = JsonSerializer.Deserialize<MessageEnvelope>(
            responseBuffer.AsSpan(0, bytesRead));
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.MessageType, Is.EqualTo(MessageTypes.ActivationResponse));

        var payload = response.Payload!.Value.Deserialize<ActivationResponsePayload>();
        Assert.That(payload!.Status, Is.EqualTo("error"));
        Assert.That(payload.Error, Does.Contain("Unsupported protocol version"));
    }

    [Test]
    public async Task InvalidKeyFormat_ServerDropsAndLogs()
    {
        // S20: Uppercase workspace_key should be dropped
        var serverLog = new TestRoutingLog();
        using var server = new PipeTransport(TestWorkspaceKey, serverLog);

        bool messageProcessed = false;
        server.MessageReceived += _ => messageProcessed = true;
        server.StartServer(_ => null);

        await Task.Delay(100);

        // Send message with uppercase key
        using var rawClient = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            PipeTransport.DerivePipeName(TestWorkspaceKey),
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

        await rawClient.ConnectAsync(2000);

        var badEnvelope = new
        {
            protocol_version = "1",
            message_type = "activation_request",
            workspace_key = "88B49A59944589BD4779B7931D127ABC",  // UPPERCASE - invalid
            payload = new { workspace_path = "C:\\Test" },
            timestamp = DateTime.UtcNow.ToString("O")
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(badEnvelope);
        await rawClient.WriteAsync(bytes);
        await rawClient.FlushAsync();

        // Server should drop (no response) - wait briefly then check
        var responseBuffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(500);
        int bytesRead = 0;
        try
        {
            bytesRead = await rawClient.ReadAsync(responseBuffer, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - no response
        }

        await server.StopServerAsync();

        Assert.That(bytesRead, Is.EqualTo(0), "Server should drop message (no response)");
        Assert.That(messageProcessed, Is.False, "Message should not be processed");
        Assert.That(serverLog.HasWarning("Invalid workspace_key format"), Is.True);
    }

    [Test]
    public async Task KeyMismatch_ServerDropsAndLogs()
    {
        // workspace_key in message doesn't match server's key
        var serverLog = new TestRoutingLog();
        using var server = new PipeTransport(TestWorkspaceKey, serverLog);

        server.StartServer(_ => null);
        await Task.Delay(100);

        using var rawClient = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            PipeTransport.DerivePipeName(TestWorkspaceKey),
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

        await rawClient.ConnectAsync(2000);

        var badEnvelope = new
        {
            protocol_version = "1",
            message_type = "activation_request",
            workspace_key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",  // Different key
            payload = new { workspace_path = "C:\\Test" },
            timestamp = DateTime.UtcNow.ToString("O")
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(badEnvelope);
        await rawClient.WriteAsync(bytes);
        await rawClient.FlushAsync();

        // Wait for possible response
        var responseBuffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(500);
        int bytesRead = 0;
        try
        {
            bytesRead = await rawClient.ReadAsync(responseBuffer, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await server.StopServerAsync();

        Assert.That(bytesRead, Is.EqualTo(0), "Server should drop message (no response)");
        Assert.That(serverLog.HasWarning("mismatch"), Is.True);
    }

    [Test]
    public async Task PingPong_ValidFlow()
    {
        var serverLog = new TestRoutingLog();
        using var server = new PipeTransport(TestWorkspaceKey, serverLog);

        server.StartServer(request =>
        {
            if (request.MessageType == MessageTypes.Ping)
            {
                return new MessageEnvelope
                {
                    ProtocolVersion = "1",
                    MessageType = MessageTypes.Pong,
                    WorkspaceKey = TestWorkspaceKey,
                    Payload = JsonSerializer.SerializeToElement(new PongPayload
                    {
                        ProcessId = Environment.ProcessId,
                        UptimeSeconds = 100
                    }),
                    Timestamp = DateTime.UtcNow.ToString("O")
                };
            }
            return null;
        });

        await Task.Delay(100);

        using var rawClient = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            PipeTransport.DerivePipeName(TestWorkspaceKey),
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

        await rawClient.ConnectAsync(2000);

        var pingEnvelope = new
        {
            protocol_version = "1",
            message_type = "ping",
            workspace_key = TestWorkspaceKey,
            payload = new { },
            timestamp = DateTime.UtcNow.ToString("O")
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pingEnvelope);
        await rawClient.WriteAsync(bytes);
        await rawClient.FlushAsync();

        var responseBuffer = new byte[64 * 1024];
        var bytesRead = await rawClient.ReadAsync(responseBuffer);

        await server.StopServerAsync();

        Assert.That(bytesRead, Is.GreaterThan(0));
        var response = JsonSerializer.Deserialize<MessageEnvelope>(
            responseBuffer.AsSpan(0, bytesRead));
        Assert.That(response!.MessageType, Is.EqualTo(MessageTypes.Pong));
    }
}
