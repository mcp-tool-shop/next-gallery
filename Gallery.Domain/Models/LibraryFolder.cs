namespace Gallery.Domain.Models;

/// <summary>
/// A folder being watched by the gallery.
/// </summary>
public sealed record LibraryFolder
{
    public long Id { get; init; }
    public required string Path { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastScannedAt { get; init; }
}
