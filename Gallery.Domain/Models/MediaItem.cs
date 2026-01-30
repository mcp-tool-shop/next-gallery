using Gallery.Domain.Enums;

namespace Gallery.Domain.Models;

/// <summary>
/// A media file indexed by the gallery.
/// </summary>
public sealed record MediaItem
{
    public long Id { get; init; }
    public required string Path { get; init; }
    public required string Extension { get; init; }
    public MediaType Type { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }
    public DateTimeOffset? TakenAt { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool IsFavorite { get; init; }
    public int Rating { get; init; }
    public string? ThumbSmallPath { get; init; }
    public string? ThumbLargePath { get; init; }
    public DateTimeOffset LastIndexedAt { get; init; }

    /// <summary>
    /// Computed cache key for thumbnail invalidation.
    /// Based on path + size + modified time.
    /// </summary>
    public string CacheKey => ComputeCacheKey(Path, SizeBytes, ModifiedAt);

    public static string ComputeCacheKey(string path, long sizeBytes, DateTimeOffset modifiedAt)
    {
        var input = $"{path}|{sizeBytes}|{modifiedAt.UtcTicks}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
