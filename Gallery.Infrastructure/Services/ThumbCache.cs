using Gallery.Application.Interfaces;
using Gallery.Domain.Enums;

namespace Gallery.Infrastructure.Services;

/// <summary>
/// File-based thumbnail cache with stable paths.
/// Layout: {CacheDirectory}/{aa}/{bb}/{cacheKey}_{size}.jpg
/// </summary>
public sealed class ThumbCache : IThumbCache
{
    public string CacheDirectory { get; }

    public ThumbCache(string? cacheDirectory = null)
    {
        CacheDirectory = cacheDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NextGallery",
                "thumbs");

        Directory.CreateDirectory(CacheDirectory);
    }

    public string GetThumbPath(string cacheKey, ThumbSize size)
    {
        // Use first 4 chars as subdirectory structure (2 levels)
        var subDir1 = cacheKey[..2];
        var subDir2 = cacheKey.Substring(2, 2);
        var fileName = $"{cacheKey}_{(int)size}.jpg";

        return Path.Combine(CacheDirectory, subDir1, subDir2, fileName);
    }

    public bool Exists(string thumbPath)
    {
        return File.Exists(thumbPath);
    }

    public async Task WriteAsync(string thumbPath, byte[] jpegBytes, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(thumbPath)!;
        Directory.CreateDirectory(directory);

        // Atomic write: write to temp, then move
        var tempPath = thumbPath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, jpegBytes, ct);
            File.Move(tempPath, thumbPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public void Delete(string thumbPath)
    {
        try
        {
            File.Delete(thumbPath);
        }
        catch (FileNotFoundException)
        {
            // Already deleted
        }
    }
}
