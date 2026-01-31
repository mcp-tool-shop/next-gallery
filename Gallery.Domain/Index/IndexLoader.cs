using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gallery.Domain.Index;

/// <summary>
/// Loads gallery index from workspace.
/// Pure function: reads one file, returns deterministic state.
/// </summary>
public sealed partial class IndexLoader
{
    private readonly IFileReader _fileReader;

    public IndexLoader(IFileReader? fileReader = null)
    {
        _fileReader = fileReader ?? RealFileReader.Instance;
    }

    /// <summary>
    /// Load index from workspace.
    /// </summary>
    /// <param name="workspaceRoot">Workspace directory path</param>
    /// <param name="lastKnownGood">Previous valid items (for transient error display)</param>
    public IndexLoadResult Load(string workspaceRoot, IReadOnlyList<JobRow>? lastKnownGood = null)
    {
        // W1: Workspace doesn't exist
        if (!_fileReader.PathExists(workspaceRoot))
        {
            return new IndexLoadResult
            {
                State = new GalleryState.Fatal
                {
                    Message = $"Cannot access workspace: {workspaceRoot}",
                    Reason = FatalReason.WorkspaceNotFound
                }
            };
        }

        // W2: Workspace is not a directory
        if (!_fileReader.DirectoryExists(workspaceRoot))
        {
            return new IndexLoadResult
            {
                State = new GalleryState.Fatal
                {
                    Message = $"Workspace is not a directory: {workspaceRoot}",
                    Reason = FatalReason.WorkspaceNotDirectory
                }
            };
        }

        // Build index path
        var indexPath = Path.Combine(workspaceRoot, ".codecomfy", "outputs", "index.json");

        // W3, W4, W5, I1: Index file doesn't exist
        if (!_fileReader.FileExists(indexPath))
        {
            return new IndexLoadResult
            {
                State = new GalleryState.Empty()
            };
        }

        // Read file
        byte[] bytes;
        try
        {
            bytes = _fileReader.ReadAllBytes(indexPath);
        }
        catch (UnauthorizedAccessException)
        {
            // I6: Permission denied
            return WithWarningOrLastKnown(
                "Cannot read index: permission denied",
                lastKnownGood);
        }
        catch (IOException ex)
        {
            return WithWarningOrLastKnown(
                $"Cannot read index: {ex.Message}",
                lastKnownGood);
        }

        // I2: 0 bytes = corrupt (writer crash mid-write)
        if (bytes.Length == 0)
        {
            return WithWarningOrLastKnown(
                "Index is empty/corrupt",
                lastKnownGood);
        }

        // Parse JSON
        IndexFile? indexFile;
        try
        {
            indexFile = JsonSerializer.Deserialize<IndexFile>(bytes);
            if (indexFile == null)
                throw new JsonException("Deserialized to null");
        }
        catch (JsonException)
        {
            // I3: Truncated/invalid JSON
            return WithWarningOrLastKnown(
                "Index is corrupt",
                lastKnownGood);
        }

        // Version check
        var version = ParseVersion(indexFile.SchemaVersion);

        // I7: Major version >= 1 and unsupported
        if (version.Major >= 1)
        {
            return new IndexLoadResult
            {
                State = new GalleryState.Fatal
                {
                    Message = "This gallery version needs an update to read this index",
                    Reason = FatalReason.UnsupportedVersion
                }
            };
        }

        // Parse items with validation
        var (validItems, skippedCount) = ParseItems(indexFile.Items);

        // I4: Empty items array
        if (validItems.Count == 0 && skippedCount == 0)
        {
            return new IndexLoadResult
            {
                State = new GalleryState.Empty()
            };
        }

        // Build banner if entries were skipped
        var banner = skippedCount > 0
            ? BannerInfo.Skipped(skippedCount)
            : BannerInfo.None;

        // E10: Some valid, some skipped
        if (validItems.Count == 0)
        {
            // All entries were malformed
            return WithWarningOrLastKnown(
                $"All {skippedCount} entries in index are malformed",
                lastKnownGood);
        }

        // Reverse for display (newest first)
        var displayOrder = validItems.AsEnumerable().Reverse().ToList();

        return new IndexLoadResult
        {
            State = new GalleryState.List { Items = displayOrder },
            Banner = banner,
            LastKnownGood = displayOrder
        };
    }

    private static IndexLoadResult WithWarningOrLastKnown(
        string message,
        IReadOnlyList<JobRow>? lastKnownGood)
    {
        if (lastKnownGood != null && lastKnownGood.Count > 0)
        {
            // Keep showing last known good list with warning banner
            return new IndexLoadResult
            {
                State = new GalleryState.List { Items = lastKnownGood },
                Banner = BannerInfo.Warning(message),
                LastKnownGood = lastKnownGood
            };
        }

        // No last known good - show empty with warning
        return new IndexLoadResult
        {
            State = new GalleryState.Empty(),
            Banner = BannerInfo.Warning(message)
        };
    }

    private (List<JobRow> valid, int skipped) ParseItems(List<IndexEntry>? items)
    {
        if (items == null || items.Count == 0)
            return (new List<JobRow>(), 0);

        var valid = new List<JobRow>();
        var skipped = 0;

        foreach (var entry in items)
        {
            var row = TryParseEntry(entry);
            if (row != null)
                valid.Add(row);
            else
                skipped++;
        }

        return (valid, skipped);
    }

    private static JobRow? TryParseEntry(IndexEntry entry)
    {
        // E1: Missing job_id
        if (string.IsNullOrEmpty(entry.JobId))
            return null;

        // E2, E3: Missing or invalid created_at
        if (string.IsNullOrEmpty(entry.CreatedAt))
            return null;

        if (!DateTimeOffset.TryParse(entry.CreatedAt, out var createdAt))
            return null;

        // E4, E5: Missing or invalid kind
        if (string.IsNullOrEmpty(entry.Kind))
            return null;

        if (!TryParseKind(entry.Kind, out var kind))
            return null;

        // E6: Empty files array
        if (entry.Files == null || entry.Files.Count == 0)
            return null;

        // E7: Missing seed
        if (entry.Seed == null)
            return null;

        // Parse files with validation
        var validFiles = new List<FileRef>();
        foreach (var file in entry.Files)
        {
            var fileRef = TryParseFile(file);
            if (fileRef != null)
                validFiles.Add(fileRef);
        }

        // Need at least one valid file
        if (validFiles.Count == 0)
            return null;

        // E8: All required fields present
        return new JobRow
        {
            JobId = entry.JobId,
            CreatedAt = createdAt,
            Kind = kind,
            Files = validFiles,
            Seed = entry.Seed.Value,
            // E9: Optional fields with fallbacks
            Prompt = string.IsNullOrEmpty(entry.Prompt) ? "(no prompt)" : entry.Prompt,
            NegativePrompt = entry.NegativePrompt,
            PresetId = string.IsNullOrEmpty(entry.PresetId) ? "unknown" : entry.PresetId,
            ElapsedSeconds = entry.ElapsedSeconds,
            Tags = entry.Tags?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Favorite = entry.Favorite ?? false,
            Notes = entry.Notes ?? ""
        };
    }

    [GeneratedRegex(@"^[a-fA-F0-9]{64}$")]
    private static partial Regex Sha256Pattern();

    private static FileRef? TryParseFile(IndexFileEntry file)
    {
        // F5: Empty path
        if (string.IsNullOrEmpty(file.Path))
            return null;

        // F3, F4: Traversal protection
        if (file.Path.Contains("..") || Path.IsPathRooted(file.Path))
            return null;

        // F6: Missing sha256
        if (string.IsNullOrEmpty(file.Sha256))
            return null;

        // Validate sha256 format (64 hex chars)
        if (!Sha256Pattern().IsMatch(file.Sha256))
            return null;

        return new FileRef
        {
            RelativePath = file.Path,
            Sha256 = file.Sha256.ToLowerInvariant(),
            ContentType = file.ContentType,
            Width = file.Width,
            Height = file.Height,
            SizeBytes = file.SizeBytes
        };
    }

    private static bool TryParseKind(string kind, out JobKind result)
    {
        result = kind.ToLowerInvariant() switch
        {
            "image" => JobKind.Image,
            "video" => JobKind.Video,
            _ => default
        };
        return kind.ToLowerInvariant() is "image" or "video";
    }

    private static (int Major, int Minor) ParseVersion(string? version)
    {
        // Missing version = assume 0.1
        if (string.IsNullOrEmpty(version))
            return (0, 1);

        var parts = version.Split('.');
        if (parts.Length < 2)
            return (0, 1);

        if (!int.TryParse(parts[0], out var major))
            return (0, 1);

        if (!int.TryParse(parts[1], out var minor))
            return (major, 0);

        return (major, minor);
    }
}

// JSON DTOs for index.json
internal sealed class IndexFile
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<IndexEntry>? Items { get; set; }
}

internal sealed class IndexEntry
{
    [JsonPropertyName("job_id")]
    public string? JobId { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("files")]
    public List<IndexFileEntry>? Files { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("negative_prompt")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    [JsonPropertyName("preset_id")]
    public string? PresetId { get; set; }

    [JsonPropertyName("elapsed_seconds")]
    public double? ElapsedSeconds { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("favorite")]
    public bool? Favorite { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

internal sealed class IndexFileEntry
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }
}
