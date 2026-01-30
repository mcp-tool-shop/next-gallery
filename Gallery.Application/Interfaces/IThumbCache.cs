using Gallery.Domain.Enums;

namespace Gallery.Application.Interfaces;

/// <summary>
/// Manages thumbnail file paths and caching.
/// </summary>
public interface IThumbCache
{
    /// <summary>
    /// Get the cache path for a thumbnail.
    /// </summary>
    string GetThumbPath(string cacheKey, ThumbSize size);

    /// <summary>
    /// Check if a thumbnail exists at the cache path.
    /// </summary>
    bool Exists(string thumbPath);

    /// <summary>
    /// Write JPEG bytes to the cache atomically.
    /// </summary>
    Task WriteAsync(string thumbPath, byte[] jpegBytes, CancellationToken ct = default);

    /// <summary>
    /// Delete a cached thumbnail.
    /// </summary>
    void Delete(string thumbPath);

    /// <summary>
    /// Get the base cache directory.
    /// </summary>
    string CacheDirectory { get; }
}
