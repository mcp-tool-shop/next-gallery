using Gallery.Application.Interfaces;
using Gallery.Domain.Enums;
using Gallery.Domain.Models;
using Microsoft.Data.Sqlite;

namespace Gallery.Infrastructure.Data;

public sealed class ThumbJobStore : IThumbJobStore
{
    private readonly GalleryDatabase _db;

    public ThumbJobStore(GalleryDatabase db)
    {
        _db = db;
    }

    public async Task EnqueueAsync(long itemId, ThumbSize size, int priority = 0, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO thumb_jobs (item_id, size, priority, status, attempts, created_at, updated_at)
            VALUES (@item_id, @size, @priority, 0, 0, @now, @now)
            ON CONFLICT(item_id, size) DO UPDATE SET
                priority = MAX(excluded.priority, thumb_jobs.priority),
                status = CASE WHEN thumb_jobs.status = 2 THEN thumb_jobs.status ELSE 0 END,
                updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@item_id", itemId);
        cmd.Parameters.AddWithValue("@size", (int)size);
        cmd.Parameters.AddWithValue("@priority", priority);
        cmd.Parameters.AddWithValue("@now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ThumbJob?> DequeueAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conn = _db.GetConnection();

        // Find next pending job
        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = """
            SELECT id, item_id, size, priority, status, attempts, error_message, created_at, updated_at
            FROM thumb_jobs
            WHERE status = 0
            ORDER BY priority DESC, created_at ASC
            LIMIT 1
            """;

        await using var reader = await selectCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var job = new ThumbJob
        {
            Id = reader.GetInt64(0),
            ItemId = reader.GetInt64(1),
            Size = (ThumbSize)reader.GetInt32(2),
            Priority = reader.GetInt32(3),
            Status = ThumbJobStatus.InProgress,
            Attempts = reader.GetInt32(5) + 1,
            ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            UpdatedAt = now
        };
        reader.Close();

        // Mark as in progress
        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = """
            UPDATE thumb_jobs
            SET status = 1, attempts = @attempts, updated_at = @now
            WHERE id = @id
            """;
        updateCmd.Parameters.AddWithValue("@id", job.Id);
        updateCmd.Parameters.AddWithValue("@attempts", job.Attempts);
        updateCmd.Parameters.AddWithValue("@now", now.ToString("o"));
        await updateCmd.ExecuteNonQueryAsync(ct);

        return job;
    }

    public async Task MarkCompletedAsync(long jobId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE thumb_jobs SET status = 2, updated_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(long jobId, string errorMessage, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE thumb_jobs
            SET status = CASE WHEN attempts >= 3 THEN 3 ELSE 0 END,
                error_message = @error,
                updated_at = @now
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@error", errorMessage);
        cmd.Parameters.AddWithValue("@now", now.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM thumb_jobs WHERE status = 0";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ClearCompletedAsync(CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM thumb_jobs WHERE status = 2";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
