using System.Text.Json;

namespace Gallery.Domain.Routing;

/// <summary>
/// Result of instance routing decision.
/// </summary>
public sealed class RoutingResult
{
    public RouteAction Action { get; init; }
    public ActivationClientResult? ActivationResult { get; init; }
    public string? ErrorMessage { get; init; }

    public static RoutingResult CreateWindow() => new() { Action = RouteAction.CreateWindow };

    public static RoutingResult ActivateExisting(ActivationClientResult result) => new()
    {
        Action = RouteAction.ActivateExisting,
        ActivationResult = result
    };

    public static RoutingResult Error(string message) => new()
    {
        Action = RouteAction.CreateWindow,  // Degraded mode
        ErrorMessage = message
    };
}

/// <summary>
/// Action to take after routing decision.
/// </summary>
public enum RouteAction
{
    CreateWindow,     // This is the primary instance - create new window
    ActivateExisting, // Another instance exists - it was activated
    Exit              // Activation sent, exit this process
}

/// <summary>
/// Abstraction for mutex operations (for testability).
/// </summary>
public interface IMutexProvider
{
    /// <summary>
    /// Try to acquire the global mutex for this workspace.
    /// </summary>
    /// <returns>Handle if acquired, null if already held by another process</returns>
    IMutexHandle? TryAcquire(string mutexName);
}

/// <summary>
/// Handle to an acquired mutex.
/// </summary>
public interface IMutexHandle : IDisposable
{
    bool IsHeld { get; }
}

/// <summary>
/// Real Windows mutex implementation.
/// </summary>
public sealed class WindowsMutexProvider : IMutexProvider
{
    public IMutexHandle? TryAcquire(string mutexName)
    {
        try
        {
            // Global mutex name for cross-session visibility
            var fullName = $"Global\\{mutexName}";
            var mutex = new Mutex(initiallyOwned: true, name: fullName, createdNew: out bool createdNew);

            if (createdNew)
            {
                return new WindowsMutexHandle(mutex);
            }

            // Mutex exists - another instance holds it
            mutex.Dispose();
            return null;
        }
        catch (Exception)
        {
            // Mutex creation failed (e.g., access denied)
            return null;
        }
    }
}

/// <summary>
/// Handle wrapper for Windows mutex.
/// </summary>
public sealed class WindowsMutexHandle : IMutexHandle
{
    private readonly Mutex _mutex;
    private bool _disposed;

    public WindowsMutexHandle(Mutex mutex)
    {
        _mutex = mutex;
    }

    public bool IsHeld => !_disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // Ignore - may already be released
        }

        _mutex.Dispose();
    }
}

/// <summary>
/// Routes incoming launch requests to existing or new instances.
/// </summary>
public sealed class InstanceRouter : IDisposable
{
    private readonly string _workspaceKey;
    private readonly IMutexProvider _mutexProvider;
    private readonly IRoutingLog _log;
    private IMutexHandle? _mutexHandle;
    private PipeTransport? _pipeTransport;

    /// <summary>
    /// Derive mutex name from workspace key.
    /// Formula: NextGallery_{workspace_key}
    /// </summary>
    public static string DeriveMutexName(string workspaceKey) =>
        $"NextGallery_{workspaceKey}";

    public InstanceRouter(
        string workspaceKey,
        IMutexProvider? mutexProvider = null,
        IRoutingLog? log = null)
    {
        _workspaceKey = workspaceKey;
        _mutexProvider = mutexProvider ?? new WindowsMutexProvider();
        _log = log ?? NullRoutingLog.Instance;
    }

    /// <summary>
    /// Event raised when a secondary instance sends an activation request.
    /// Primary instance should handle this by bringing window to front.
    /// </summary>
    public event Func<ActivationRequestPayload, ActivationResponsePayload>? ActivationRequested;

    /// <summary>
    /// Attempt to become the primary instance, or activate existing instance.
    /// </summary>
    public async Task<RoutingResult> RouteAsync(
        string workspacePath,
        string? requestedView = null,
        CancellationToken ct = default)
    {
        var mutexName = DeriveMutexName(_workspaceKey);

        _log.Info($"Attempting to acquire mutex: {mutexName}");

        _mutexHandle = _mutexProvider.TryAcquire(mutexName);

        if (_mutexHandle != null)
        {
            // We are the primary instance
            _log.Info("Mutex acquired - this is the primary instance");
            StartPipeServer();
            return RoutingResult.CreateWindow();
        }

        // Another instance exists - try to activate it
        _log.Info("Mutex held by another process - attempting activation");

        return await TryActivateExistingAsync(workspacePath, requestedView, ct);
    }

    /// <summary>
    /// Synchronous version for simpler scenarios.
    /// </summary>
    public RoutingResult Route(string workspacePath, string? requestedView = null)
    {
        return RouteAsync(workspacePath, requestedView).GetAwaiter().GetResult();
    }

    private void StartPipeServer()
    {
        _pipeTransport = new PipeTransport(_workspaceKey, _log);
        _pipeTransport.StartServer(HandleActivationRequest);
        _log.Info($"Pipe server started: {_pipeTransport.PipeName}");
    }

    private MessageEnvelope? HandleActivationRequest(MessageEnvelope request)
    {
        if (request.MessageType != MessageTypes.ActivationRequest)
        {
            // Ping/pong handled separately if needed
            if (request.MessageType == MessageTypes.Ping)
            {
                return new MessageEnvelope
                {
                    ProtocolVersion = EnvelopeValidator.SupportedProtocolVersion,
                    MessageType = MessageTypes.Pong,
                    WorkspaceKey = _workspaceKey,
                    Payload = JsonSerializer.SerializeToElement(new PongPayload
                    {
                        ProcessId = Environment.ProcessId,
                        UptimeSeconds = 0 // Could track actual uptime
                    }),
                    Timestamp = DateTime.UtcNow.ToString("O")
                };
            }
            return null;
        }

        // Deserialize request payload
        ActivationRequestPayload? payload = null;
        try
        {
            payload = request.Payload?.Deserialize<ActivationRequestPayload>();
        }
        catch
        {
            _log.Warning("Failed to deserialize activation request payload");
        }

        payload ??= new ActivationRequestPayload { WorkspacePath = "" };

        // Invoke handler
        ActivationResponsePayload response;
        if (ActivationRequested != null)
        {
            try
            {
                response = ActivationRequested(payload);
            }
            catch (Exception ex)
            {
                _log.Error($"Activation handler threw: {ex.Message}");
                response = new ActivationResponsePayload
                {
                    Status = "error",
                    Error = ex.Message
                };
            }
        }
        else
        {
            // No handler - return basic success
            response = new ActivationResponsePayload
            {
                Status = "activated",
                WindowState = "unknown"
            };
        }

        return new MessageEnvelope
        {
            ProtocolVersion = EnvelopeValidator.SupportedProtocolVersion,
            MessageType = MessageTypes.ActivationResponse,
            WorkspaceKey = _workspaceKey,
            Payload = JsonSerializer.SerializeToElement(response),
            Timestamp = DateTime.UtcNow.ToString("O")
        };
    }

    private async Task<RoutingResult> TryActivateExistingAsync(
        string workspacePath,
        string? requestedView,
        CancellationToken ct)
    {
        using var client = new PipeTransport(_workspaceKey, _log);

        var result = await client.SendActivationRequestAsync(
            new ActivationRequestPayload
            {
                WorkspacePath = workspacePath,
                RequestedView = requestedView
            },
            connectTimeout: TimeSpan.FromSeconds(2),  // Per spec
            sendTimeout: TimeSpan.FromSeconds(1),     // Per spec
            receiveTimeout: TimeSpan.FromSeconds(5),  // Per spec
            ct);

        switch (result.Outcome)
        {
            case ActivationClientOutcome.Success:
                _log.Info("Activation successful");
                return RoutingResult.ActivateExisting(result);

            case ActivationClientOutcome.ConnectTimeout:
                // S2 variant: No pipe listener despite mutex held
                // Could be orphan mutex, or instance crashed
                _log.Warning("Connect timeout - no pipe listener (orphan mutex or crash?)");
                // Per spec: create new window in degraded mode
                return RoutingResult.Error("Could not connect to existing instance");

            case ActivationClientOutcome.ReceiveTimeout:
                // S18: Pipe reachable but app hung
                // Per spec: "trust mutex, exit"
                _log.Warning("Receive timeout - existing instance may be handling activation");
                return RoutingResult.ActivateExisting(result);

            case ActivationClientOutcome.InvalidResponse:
                // S19: Pipe returns malformed JSON or wrong workspace_key
                _log.Error($"Invalid response from existing instance: {result.ErrorMessage}");
                return RoutingResult.Error("Invalid response from existing instance");

            default:
                _log.Error($"Activation failed: {result.Outcome}");
                return RoutingResult.Error($"Activation failed: {result.Outcome}");
        }
    }

    public void Dispose()
    {
        _pipeTransport?.Dispose();
        _mutexHandle?.Dispose();
    }
}
