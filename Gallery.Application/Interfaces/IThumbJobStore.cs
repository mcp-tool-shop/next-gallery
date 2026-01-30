using Gallery.Domain.Enums;
using Gallery.Domain.Models;

namespace Gallery.Application.Interfaces;

/// <summary>
/// Manages the thumbnail generation job queue.
/// </summary>
public interface IThumbJobStore
{
    Task EnqueueAsync(long itemId, ThumbSize size, int priority = 0, CancellationToken ct = default);
    Task<ThumbJob?> DequeueAsync(CancellationToken ct = default);
    Task MarkCompletedAsync(long jobId, CancellationToken ct = default);
    Task MarkFailedAsync(long jobId, string errorMessage, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    Task ClearCompletedAsync(CancellationToken ct = default);
}
