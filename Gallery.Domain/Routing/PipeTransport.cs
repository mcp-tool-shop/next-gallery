using System.IO.Pipes;
using System.Text.Json;

namespace Gallery.Domain.Routing;

/// <summary>
/// Named pipe transport for single-instance routing.
/// Implements timeout behavior per routing protocol v0.1.
/// </summary>
public sealed class PipeTransport : IDisposable
{
    private readonly string _pipeName;
    private readonly string _workspaceKey;
    private readonly IRoutingLog _log;
    private NamedPipeServerStream? _server;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    /// <summary>
    /// Derive pipe name from workspace key.
    /// Formula: \\.\pipe\codecomfy.nextgallery.{workspace_key}
    /// </summary>
    public static string DerivePipeName(string workspaceKey) =>
        $"codecomfy.nextgallery.{workspaceKey}";

    public PipeTransport(string workspaceKey, IRoutingLog? log = null)
    {
        _workspaceKey = workspaceKey;
        _pipeName = DerivePipeName(workspaceKey);
        _log = log ?? NullRoutingLog.Instance;
    }

    public string PipeName => _pipeName;
    public string WorkspaceKey => _workspaceKey;

    /// <summary>
    /// Event raised when a valid message is received.
    /// </summary>
    public event Action<MessageEnvelope>? MessageReceived;

    /// <summary>
    /// Start the pipe server (called by primary instance).
    /// </summary>
    public void StartServer(Func<MessageEnvelope, MessageEnvelope?> messageHandler)
    {
        if (_server != null)
            throw new InvalidOperationException("Server already started");

        _serverCts = new CancellationTokenSource();
        _serverTask = RunServerAsync(messageHandler, _serverCts.Token);
    }

    private async Task RunServerAsync(Func<MessageEnvelope, MessageEnvelope?> messageHandler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1, // maxNumberOfServerInstances
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: EnvelopeValidator.MaxMessageSizeBytes,
                    outBufferSize: EnvelopeValidator.MaxMessageSizeBytes);

                _log.Debug($"Waiting for connection on pipe: {_pipeName}");
                await _server.WaitForConnectionAsync(ct);
                _log.Debug("Client connected");

                await HandleClientAsync(_server, messageHandler, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error($"Server error: {ex.Message}");
            }
            finally
            {
                _server?.Dispose();
                _server = null;
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream server,
        Func<MessageEnvelope, MessageEnvelope?> messageHandler,
        CancellationToken ct)
    {
        try
        {
            // Read message
            var buffer = new byte[EnvelopeValidator.MaxMessageSizeBytes];
            var bytesRead = await server.ReadAsync(buffer, ct);

            if (bytesRead == 0)
            {
                _log.Warning("Client disconnected without sending data");
                return;
            }

            // Validate envelope
            var validation = EnvelopeValidator.Validate(buffer, bytesRead, _workspaceKey);

            if (!validation.IsValid)
            {
                _log.Warning($"Invalid message: {validation.ErrorReason}");

                if (validation.Action == ValidationAction.RespondWithError)
                {
                    // Respond with error (e.g., unsupported protocol version)
                    var errorResponse = EnvelopeValidator.CreateErrorResponse(
                        _workspaceKey,
                        validation.ErrorReason!);
                    var responseBytes = EnvelopeValidator.SerializeEnvelope(errorResponse);
                    await server.WriteAsync(responseBytes, ct);
                }
                // else: Drop (no response)
                return;
            }

            // Process valid message
            MessageReceived?.Invoke(validation.Envelope!);

            var response = messageHandler(validation.Envelope!);
            if (response != null)
            {
                var responseBytes = EnvelopeValidator.SerializeEnvelope(response);
                await server.WriteAsync(responseBytes, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Error handling client: {ex.Message}");
        }
    }

    /// <summary>
    /// Send an activation request to an existing instance (called by secondary instance).
    /// </summary>
    public async Task<ActivationClientResult> SendActivationRequestAsync(
        ActivationRequestPayload payload,
        TimeSpan connectTimeout,
        TimeSpan sendTimeout,
        TimeSpan receiveTimeout,
        CancellationToken ct = default)
    {
        using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            // Connect with timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(connectTimeout);

            try
            {
                await client.ConnectAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.Warning($"Pipe connect timeout ({connectTimeout.TotalMilliseconds}ms)");
                return ActivationClientResult.ConnectTimeout();
            }

            // Create and send request
            var envelope = new MessageEnvelope
            {
                ProtocolVersion = EnvelopeValidator.SupportedProtocolVersion,
                MessageType = MessageTypes.ActivationRequest,
                WorkspaceKey = _workspaceKey,
                Payload = JsonSerializer.SerializeToElement(payload),
                Timestamp = DateTime.UtcNow.ToString("O")
            };

            var requestBytes = EnvelopeValidator.SerializeEnvelope(envelope);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(sendTimeout);

            try
            {
                await client.WriteAsync(requestBytes, sendCts.Token);
                await client.FlushAsync(sendCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.Warning($"Send timeout ({sendTimeout.TotalMilliseconds}ms)");
                return ActivationClientResult.SendTimeout();
            }

            // Receive response with timeout
            var responseBuffer = new byte[EnvelopeValidator.MaxMessageSizeBytes];

            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            receiveCts.CancelAfter(receiveTimeout);

            try
            {
                var bytesRead = await client.ReadAsync(responseBuffer, receiveCts.Token);

                if (bytesRead == 0)
                {
                    _log.Warning("Server closed connection without response");
                    return ActivationClientResult.NoResponse();
                }

                // Parse response
                var responseEnvelope = JsonSerializer.Deserialize<MessageEnvelope>(
                    responseBuffer.AsSpan(0, bytesRead));

                if (responseEnvelope == null)
                {
                    _log.Warning("Failed to parse response envelope");
                    return ActivationClientResult.InvalidResponse("Failed to parse response");
                }

                return ActivationClientResult.Success(responseEnvelope);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per spec: "Log warning, exit anyway (instance is handling it)"
                _log.Warning($"Receive timeout ({receiveTimeout.TotalMilliseconds}ms) - assuming instance is handling activation");
                return ActivationClientResult.ReceiveTimeout();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error($"Client error: {ex.Message}");
            return ActivationClientResult.Error(ex.Message);
        }
    }

    /// <summary>
    /// Stop the server.
    /// </summary>
    public async Task StopServerAsync()
    {
        if (_serverCts == null) return;

        _serverCts.Cancel();

        if (_serverTask != null)
        {
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                _log.Warning("Server task did not stop cleanly");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _serverCts.Dispose();
        _serverCts = null;
        _serverTask = null;
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _server?.Dispose();
        _serverCts?.Dispose();
    }
}

/// <summary>
/// Result of client activation attempt.
/// </summary>
public sealed class ActivationClientResult
{
    public ActivationClientOutcome Outcome { get; init; }
    public MessageEnvelope? Response { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsSuccess => Outcome == ActivationClientOutcome.Success;

    public static ActivationClientResult Success(MessageEnvelope response) => new()
    {
        Outcome = ActivationClientOutcome.Success,
        Response = response
    };

    public static ActivationClientResult ConnectTimeout() => new()
    {
        Outcome = ActivationClientOutcome.ConnectTimeout
    };

    public static ActivationClientResult SendTimeout() => new()
    {
        Outcome = ActivationClientOutcome.SendTimeout
    };

    public static ActivationClientResult ReceiveTimeout() => new()
    {
        Outcome = ActivationClientOutcome.ReceiveTimeout
    };

    public static ActivationClientResult NoResponse() => new()
    {
        Outcome = ActivationClientOutcome.NoResponse
    };

    public static ActivationClientResult InvalidResponse(string reason) => new()
    {
        Outcome = ActivationClientOutcome.InvalidResponse,
        ErrorMessage = reason
    };

    public static ActivationClientResult Error(string message) => new()
    {
        Outcome = ActivationClientOutcome.Error,
        ErrorMessage = message
    };
}

public enum ActivationClientOutcome
{
    Success,
    ConnectTimeout,     // No instance listening
    SendTimeout,        // Connected but send timed out
    ReceiveTimeout,     // Sent but no response (per spec: trust mutex, exit)
    NoResponse,         // Server closed without response
    InvalidResponse,    // Response was malformed
    Error               // Other error
}
