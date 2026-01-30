using Gallery.Application.Interfaces;
using Gallery.Domain.Models;
using Microsoft.Data.Sqlite;

namespace Gallery.Infrastructure.Data;

public sealed class LibraryStore : ILibraryStore
{
    private readonly GalleryDatabase _db;

    public LibraryStore(GalleryDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LibraryFolder>> GetAllAsync(CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, is_enabled, added_at, last_scanned_at FROM library_folders ORDER BY path";

        var results = new List<LibraryFolder>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadFolder(reader));
        }
        return results;
    }

    public async Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, is_enabled, added_at, last_scanned_at FROM library_folders WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", path);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadFolder(reader) : null;
    }

    public async Task<LibraryFolder> AddAsync(string path, CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(path);
        var now = DateTimeOffset.UtcNow;

        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO library_folders (path, is_enabled, added_at)
            VALUES (@path, 1, @added_at)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("@path", normalizedPath);
        cmd.Parameters.AddWithValue("@added_at", now.ToString("o"));

        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return new LibraryFolder
        {
            Id = id,
            Path = normalizedPath,
            IsEnabled = true,
            AddedAt = now
        };
    }

    public async Task RemoveAsync(long id, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM library_folders WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE library_folders SET is_enabled = @enabled WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateLastScannedAsync(long id, DateTimeOffset scannedAt, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE library_folders SET last_scanned_at = @scanned_at WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@scanned_at", scannedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static LibraryFolder ReadFolder(SqliteDataReader reader)
    {
        return new LibraryFolder
        {
            Id = reader.GetInt64(0),
            Path = reader.GetString(1),
            IsEnabled = reader.GetInt32(2) == 1,
            AddedAt = DateTimeOffset.Parse(reader.GetString(3)),
            LastScannedAt = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4))
        };
    }
}
