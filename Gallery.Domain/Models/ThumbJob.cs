using Gallery.Domain.Enums;

namespace Gallery.Domain.Models;

/// <summary>
/// A queued job to generate a thumbnail.
/// </summary>
public sealed record ThumbJob
{
    public long Id { get; init; }
    public long ItemId { get; init; }
    public ThumbSize Size { get; init; }
    public int Priority { get; init; }
    public ThumbJobStatus Status { get; init; }
    public int Attempts { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
