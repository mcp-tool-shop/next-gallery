namespace Gallery.Domain.Routing;

/// <summary>
/// Result of handling an activation request.
/// </summary>
public sealed class ActivationResult
{
    public List<ActivationOutcome> Outcomes { get; init; } = new();
    public string? NavigatedTo { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsError => Outcomes.Count == 1 && Outcomes[0] >= ActivationOutcome.ErrorInvalidMessage;

    public static ActivationResult Success(params ActivationOutcome[] outcomes) => new()
    {
        Outcomes = outcomes.ToList()
    };

    public static ActivationResult SuccessWithNavigation(string view, params ActivationOutcome[] outcomes) => new()
    {
        Outcomes = outcomes.ToList(),
        NavigatedTo = view
    };

    public static ActivationResult Error(ActivationOutcome error, string message) => new()
    {
        Outcomes = new List<ActivationOutcome> { error },
        ErrorMessage = message
    };
}

/// <summary>
/// Possible outcomes from handling activation.
/// Success outcomes can combine; error outcomes are terminal.
/// </summary>
public enum ActivationOutcome
{
    // Success outcomes (can combine)
    BroughtToFront,           // Window was background, now foreground
    AlreadyForeground,        // Window was already foreground
    RestoredFromMinimized,    // Window was minimized, now restored + foreground
    NavigatedToView,          // Navigated to requested view
    RefreshedIndex,           // Index was refreshed
    TaskbarFlashed,           // Taskbar icon flashed for attention

    // Error outcomes (terminal - no other outcomes)
    ErrorInvalidMessage = 100,      // Message parsing failed
    ErrorUnsupportedVersion,        // Protocol version not supported
    ErrorWindowUnavailable,         // Window handle invalid
    ErrorWorkspaceKeyMismatch,      // workspace_key doesn't match this instance
    ErrorMessageTooLarge,           // Message exceeded 64KB
    ErrorInvalidKeyFormat,          // workspace_key not 32 lowercase hex
}

/// <summary>
/// Abstraction for window management operations.
/// </summary>
public interface IWindowManager
{
    bool IsMinimized { get; }
    bool IsForeground { get; }
    bool IsValid { get; }

    void BringToFront();
    void RestoreFromMinimized();
    void FlashTaskbar();
    void NavigateTo(string view);
}

/// <summary>
/// Abstraction for index loading operations.
/// </summary>
public interface IIndexLoader
{
    void Refresh();
}

/// <summary>
/// Core activation logic - testable, deterministic, no UI calls.
/// </summary>
public static class ActivationHandler
{
    /// <summary>
    /// Handle a second instance activation request.
    /// Returns intent (outcomes) without making actual UI calls.
    /// </summary>
    public static ActivationResult HandleSecondInstanceActivation(
        ActivationRequestPayload request,
        string expectedWorkspaceKey,
        IWindowManager windowManager,
        IIndexLoader indexLoader)
    {
        // Validate window is available
        if (!windowManager.IsValid)
        {
            return ActivationResult.Error(
                ActivationOutcome.ErrorWindowUnavailable,
                "Window handle is invalid");
        }

        var outcomes = new List<ActivationOutcome>();

        // Handle window state
        if (windowManager.IsMinimized)
        {
            windowManager.RestoreFromMinimized();
            outcomes.Add(ActivationOutcome.RestoredFromMinimized);
            windowManager.FlashTaskbar();
            outcomes.Add(ActivationOutcome.TaskbarFlashed);
        }
        else if (!windowManager.IsForeground)
        {
            windowManager.BringToFront();
            outcomes.Add(ActivationOutcome.BroughtToFront);
        }
        else
        {
            outcomes.Add(ActivationOutcome.AlreadyForeground);
        }

        // Navigate if requested
        string? navigatedTo = null;
        if (!string.IsNullOrEmpty(request.RequestedView))
        {
            windowManager.NavigateTo(request.RequestedView);
            outcomes.Add(ActivationOutcome.NavigatedToView);
            navigatedTo = request.RequestedView;
        }

        // Always refresh index
        indexLoader.Refresh();
        outcomes.Add(ActivationOutcome.RefreshedIndex);

        return new ActivationResult
        {
            Outcomes = outcomes,
            NavigatedTo = navigatedTo
        };
    }

    /// <summary>
    /// Convert ActivationResult to response payload.
    /// </summary>
    public static ActivationResponsePayload ToResponsePayload(ActivationResult result)
    {
        if (result.IsError)
        {
            return new ActivationResponsePayload
            {
                Status = "error",
                Error = result.ErrorMessage
            };
        }

        var windowState = DetermineWindowState(result.Outcomes);

        return new ActivationResponsePayload
        {
            Status = "activated",
            WindowState = windowState,
            NavigatedTo = result.NavigatedTo
        };
    }

    private static string DetermineWindowState(List<ActivationOutcome> outcomes)
    {
        if (outcomes.Contains(ActivationOutcome.RestoredFromMinimized))
            return "restored";
        if (outcomes.Contains(ActivationOutcome.BroughtToFront))
            return "restored";
        if (outcomes.Contains(ActivationOutcome.AlreadyForeground))
            return "already_foreground";
        return "unknown";
    }
}
