using Microsoft.Data.Sqlite;

namespace Gallery.Infrastructure.Data;

/// <summary>
/// SQLite database initialization and migration.
/// </summary>
public sealed class GalleryDatabase : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public GalleryDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={databasePath}";
    }

    public SqliteConnection GetConnection()
    {
        if (_connection is null)
        {
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
            Initialize(_connection);
        }
        return _connection;
    }

    private static void Initialize(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    private const string Schema = """
        -- Library folders
        CREATE TABLE IF NOT EXISTS library_folders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            path TEXT NOT NULL UNIQUE,
            is_enabled INTEGER NOT NULL DEFAULT 1,
            added_at TEXT NOT NULL,
            last_scanned_at TEXT
        );

        -- Media items
        CREATE TABLE IF NOT EXISTS items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            path TEXT NOT NULL UNIQUE,
            extension TEXT NOT NULL,
            type INTEGER NOT NULL DEFAULT 0,
            size_bytes INTEGER NOT NULL,
            modified_at TEXT NOT NULL,
            taken_at TEXT,
            width INTEGER,
            height INTEGER,
            duration_ticks INTEGER,
            is_favorite INTEGER NOT NULL DEFAULT 0,
            rating INTEGER NOT NULL DEFAULT 0,
            thumb_small_path TEXT,
            thumb_large_path TEXT,
            last_indexed_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_items_path ON items(path);
        CREATE INDEX IF NOT EXISTS idx_items_modified_at ON items(modified_at);
        CREATE INDEX IF NOT EXISTS idx_items_is_favorite ON items(is_favorite);
        CREATE INDEX IF NOT EXISTS idx_items_type ON items(type);

        -- Thumbnail job queue
        CREATE TABLE IF NOT EXISTS thumb_jobs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            item_id INTEGER NOT NULL,
            size INTEGER NOT NULL,
            priority INTEGER NOT NULL DEFAULT 0,
            status INTEGER NOT NULL DEFAULT 0,
            attempts INTEGER NOT NULL DEFAULT 0,
            error_message TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE,
            UNIQUE(item_id, size)
        );

        CREATE INDEX IF NOT EXISTS idx_thumb_jobs_status ON thumb_jobs(status, priority DESC);
        """;

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
