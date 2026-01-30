using Gallery.Domain.Enums;
using Gallery.Domain.Models;

namespace Gallery.Application.Interfaces;

/// <summary>
/// Manages indexed media items.
/// </summary>
public interface IMediaItemStore
{
    Task<MediaItem?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<MediaItem?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<MediaItem>> GetAllAsync(int limit = 1000, int offset = 0, CancellationToken ct = default);
    Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MediaItem>> SearchAsync(string query, CancellationToken ct = default);
    Task<long> UpsertAsync(MediaItem item, CancellationToken ct = default);
    Task UpdateThumbPathAsync(long id, ThumbSize size, string thumbPath, CancellationToken ct = default);
    Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken ct = default);
    Task SetRatingAsync(long id, int rating, CancellationToken ct = default);
    Task DeleteByPathAsync(string path, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
}
