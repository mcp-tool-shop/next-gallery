namespace Gallery.Domain.Index;

/// <summary>
/// Abstraction for file I/O operations.
/// Enables testing without touching disk.
/// </summary>
public interface IFileReader
{
    /// <summary>
    /// Check if a path exists and is a directory.
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Check if a path exists and is a file.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Check if path exists (file or directory).
    /// </summary>
    bool PathExists(string path);

    /// <summary>
    /// Read all bytes from a file.
    /// Throws FileNotFoundException if file doesn't exist.
    /// </summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Get file size without reading content.
    /// </summary>
    long GetFileSize(string path);

    /// <summary>
    /// Get last write time for cache invalidation.
    /// </summary>
    DateTime GetLastWriteTimeUtc(string path);
}

/// <summary>
/// Real file system implementation.
/// </summary>
public sealed class RealFileReader : IFileReader
{
    public static readonly RealFileReader Instance = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public bool PathExists(string path) => Path.Exists(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public long GetFileSize(string path) => new FileInfo(path).Length;
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}

/// <summary>
/// In-memory file system for testing.
/// </summary>
public sealed class FakeFileReader : IFileReader
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastWriteTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _permissionDenied = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Add a file to the fake filesystem.</summary>
    public FakeFileReader AddFile(string path, byte[] content)
    {
        _files[Normalize(path)] = content;
        return this;
    }

    /// <summary>Add a file with string content (UTF-8).</summary>
    public FakeFileReader AddFile(string path, string content)
    {
        return AddFile(path, System.Text.Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Add a directory.</summary>
    public FakeFileReader AddDirectory(string path)
    {
        _directories.Add(Normalize(path));
        return this;
    }

    /// <summary>Mark a file as permission denied.</summary>
    public FakeFileReader DenyPermission(string path)
    {
        _permissionDenied.Add(Normalize(path));
        return this;
    }

    /// <summary>Set last write time for a file.</summary>
    public FakeFileReader SetLastWriteTime(string path, DateTime utc)
    {
        _lastWriteTimes[Normalize(path)] = utc;
        return this;
    }

    public bool DirectoryExists(string path)
    {
        var norm = Normalize(path);
        return _directories.Contains(norm);
    }

    public bool FileExists(string path)
    {
        var norm = Normalize(path);
        return _files.ContainsKey(norm);
    }

    public bool PathExists(string path)
    {
        var norm = Normalize(path);
        return _files.ContainsKey(norm) || _directories.Contains(norm);
    }

    public byte[] ReadAllBytes(string path)
    {
        var norm = Normalize(path);

        if (_permissionDenied.Contains(norm))
            throw new UnauthorizedAccessException($"Access denied: {path}");

        if (!_files.TryGetValue(norm, out var content))
            throw new FileNotFoundException($"File not found: {path}", path);

        return content;
    }

    public long GetFileSize(string path)
    {
        var norm = Normalize(path);
        if (!_files.TryGetValue(norm, out var content))
            throw new FileNotFoundException($"File not found: {path}", path);
        return content.Length;
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        var norm = Normalize(path);
        return _lastWriteTimes.GetValueOrDefault(norm, DateTime.UtcNow);
    }

    private static string Normalize(string path) =>
        path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
}
