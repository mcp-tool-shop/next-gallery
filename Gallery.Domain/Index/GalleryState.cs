namespace Gallery.Domain.Index;

/// <summary>
/// Gallery UI state - discriminated union via abstract class.
/// </summary>
public abstract class GalleryState
{
    private GalleryState() { }

    /// <summary>Shown briefly while reading index.</summary>
    public sealed class Loading : GalleryState
    {
        public static readonly Loading Instance = new();
    }

    /// <summary>No jobs yet or index doesn't exist.</summary>
    public sealed class Empty : GalleryState
    {
        public string Title { get; init; } = "Waiting for first generation...";
        public string Subtitle { get; init; } = "No jobs yet. Run a generation to see results here.";
    }

    /// <summary>Normal display with job list.</summary>
    public sealed class List : GalleryState
    {
        public required IReadOnlyList<JobRow> Items { get; init; }
    }

    /// <summary>Full-screen fatal error - cannot proceed.</summary>
    public sealed class Fatal : GalleryState
    {
        public required string Message { get; init; }
        public FatalReason Reason { get; init; }
    }
}

/// <summary>
/// Reason for fatal error - determines available actions.
/// </summary>
public enum FatalReason
{
    WorkspaceNotFound,      // W1: Path doesn't exist
    WorkspaceNotDirectory,  // W2: Path is a file
    UnsupportedVersion,     // I7: schema_version >= 1.0
}

/// <summary>
/// Non-fatal warning banner info.
/// </summary>
public sealed class BannerInfo
{
    public BannerSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public int SkippedCount { get; init; }

    public static readonly BannerInfo None = new() { Severity = BannerSeverity.None };

    public static BannerInfo Warning(string message) => new()
    {
        Severity = BannerSeverity.Warning,
        Message = message
    };

    public static BannerInfo Skipped(int count) => new()
    {
        Severity = BannerSeverity.Info,
        Message = $"{count} item{(count == 1 ? "" : "s")} couldn't be displayed",
        SkippedCount = count
    };
}

public enum BannerSeverity
{
    None,
    Info,     // Skipped entries (non-blocking)
    Warning   // Index corrupt, permission denied
}

/// <summary>
/// Complete result of index load operation.
/// </summary>
public sealed class IndexLoadResult
{
    public required GalleryState State { get; init; }
    public BannerInfo Banner { get; init; } = BannerInfo.None;

    /// <summary>
    /// Cached data from last successful load.
    /// Used to maintain display during transient errors.
    /// </summary>
    public IReadOnlyList<JobRow>? LastKnownGood { get; init; }
}

/// <summary>
/// Single job entry for display.
/// </summary>
public sealed class JobRow
{
    public required string JobId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required JobKind Kind { get; init; }
    public required IReadOnlyList<FileRef> Files { get; init; }
    public required long Seed { get; init; }

    // Optional fields with fallbacks
    public string Prompt { get; init; } = "(no prompt)";
    public string? NegativePrompt { get; init; }
    public string PresetId { get; init; } = "unknown";
    public double? ElapsedSeconds { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool Favorite { get; init; }
    public string Notes { get; init; } = "";
}

public enum JobKind
{
    Image,
    Video
}

/// <summary>
/// File reference within a job.
/// </summary>
public sealed class FileRef
{
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }

    // Optional metadata
    public string? ContentType { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public long? SizeBytes { get; init; }
}
