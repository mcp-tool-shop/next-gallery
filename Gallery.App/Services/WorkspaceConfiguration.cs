namespace Gallery.App.Services;

/// <summary>
/// Holds the active workspace path for CodeComfy mode.
/// Set during app initialization based on launch parameters.
/// </summary>
public sealed class WorkspaceConfiguration
{
    /// <summary>
    /// The workspace root path for CodeComfy mode.
    /// Null if app is not in CodeComfy mode.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Whether the app is running in CodeComfy mode.
    /// </summary>
    public bool IsCodeComfyMode => WorkspacePath != null;
}
