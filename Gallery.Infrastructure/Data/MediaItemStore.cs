using Gallery.Application.Interfaces;
using Gallery.Domain.Enums;
using Gallery.Domain.Models;
using Microsoft.Data.Sqlite;

namespace Gallery.Infrastructure.Data;

public sealed class MediaItemStore : IMediaItemStore
{
    private readonly GalleryDatabase _db;

    public MediaItemStore(GalleryDatabase db)
    {
        _db = db;
    }

    public async Task<MediaItem?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadItem(reader) : null;
    }

    public async Task<MediaItem?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", path);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadItem(reader) : null;
    }

    public async Task<IReadOnlyList<MediaItem>> GetAllAsync(int limit = 1000, int offset = 0, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " ORDER BY modified_at DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        return await ReadAllAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE is_favorite = 1 ORDER BY modified_at DESC";

        return await ReadAllAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<MediaItem>> SearchAsync(string query, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE path LIKE @query ORDER BY modified_at DESC LIMIT 500";
        cmd.Parameters.AddWithValue("@query", $"%{query}%");

        return await ReadAllAsync(cmd, ct);
    }

    public async Task<long> UpsertAsync(MediaItem item, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items (path, extension, type, size_bytes, modified_at, taken_at, width, height, duration_ticks, is_favorite, rating, thumb_small_path, thumb_large_path, last_indexed_at)
            VALUES (@path, @extension, @type, @size_bytes, @modified_at, @taken_at, @width, @height, @duration_ticks, @is_favorite, @rating, @thumb_small_path, @thumb_large_path, @last_indexed_at)
            ON CONFLICT(path) DO UPDATE SET
                extension = excluded.extension,
                type = excluded.type,
                size_bytes = excluded.size_bytes,
                modified_at = excluded.modified_at,
                taken_at = excluded.taken_at,
                width = excluded.width,
                height = excluded.height,
                duration_ticks = excluded.duration_ticks,
                last_indexed_at = excluded.last_indexed_at
            RETURNING id
            """;

        cmd.Parameters.AddWithValue("@path", item.Path);
        cmd.Parameters.AddWithValue("@extension", item.Extension);
        cmd.Parameters.AddWithValue("@type", (int)item.Type);
        cmd.Parameters.AddWithValue("@size_bytes", item.SizeBytes);
        cmd.Parameters.AddWithValue("@modified_at", item.ModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@taken_at", item.TakenAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@width", item.Width ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@height", item.Height ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@duration_ticks", item.Duration?.Ticks ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@is_favorite", item.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@rating", item.Rating);
        cmd.Parameters.AddWithValue("@thumb_small_path", item.ThumbSmallPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@thumb_large_path", item.ThumbLargePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@last_indexed_at", item.LastIndexedAt.ToString("o"));

        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateThumbPathAsync(long id, ThumbSize size, string thumbPath, CancellationToken ct = default)
    {
        var column = size == ThumbSize.Small ? "thumb_small_path" : "thumb_large_path";
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE items SET {column} = @thumb_path WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@thumb_path", thumbPath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET is_favorite = @is_favorite WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@is_favorite", isFavorite ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetRatingAsync(long id, int rating, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET rating = @rating WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@rating", Math.Clamp(rating, 0, 5));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByPathAsync(string path, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private const string SelectColumns = """
        SELECT id, path, extension, type, size_bytes, modified_at, taken_at, width, height, duration_ticks, is_favorite, rating, thumb_small_path, thumb_large_path, last_indexed_at
        FROM items
        """;

    private static async Task<IReadOnlyList<MediaItem>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<MediaItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadItem(reader));
        }
        return results;
    }

    private static MediaItem ReadItem(SqliteDataReader reader)
    {
        return new MediaItem
        {
            Id = reader.GetInt64(0),
            Path = reader.GetString(1),
            Extension = reader.GetString(2),
            Type = (MediaType)reader.GetInt32(3),
            SizeBytes = reader.GetInt64(4),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(5)),
            TakenAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            Width = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            Height = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Duration = reader.IsDBNull(9) ? null : TimeSpan.FromTicks(reader.GetInt64(9)),
            IsFavorite = reader.GetInt32(10) == 1,
            Rating = reader.GetInt32(11),
            ThumbSmallPath = reader.IsDBNull(12) ? null : reader.GetString(12),
            ThumbLargePath = reader.IsDBNull(13) ? null : reader.GetString(13),
            LastIndexedAt = DateTimeOffset.Parse(reader.GetString(14))
        };
    }
}
