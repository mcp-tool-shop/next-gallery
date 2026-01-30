using Gallery.Domain.Models;

namespace Gallery.Application.Interfaces;

/// <summary>
/// Scans folders and indexes media items.
/// </summary>
public interface IItemIndexService
{
    /// <summary>
    /// Scan a folder and index all media files.
    /// </summary>
    Task<int> ScanFolderAsync(LibraryFolder folder, IProgress<ScanProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Scan all enabled library folders.
    /// </summary>
    Task<int> ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default);
}

public sealed record ScanProgress(string CurrentFile, int FilesScanned, int FilesTotal);
