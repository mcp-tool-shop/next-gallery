namespace Gallery.Domain.Routing;

/// <summary>
/// Logging interface for routing operations.
/// Makes "drop + log" assertions testable.
/// </summary>
public interface IRoutingLog
{
    void Warning(string message);
    void Error(string message);
    void Info(string message);
    void Debug(string message);
}

/// <summary>
/// Null logger for production (or when logging is not needed).
/// </summary>
public sealed class NullRoutingLog : IRoutingLog
{
    public static readonly NullRoutingLog Instance = new();
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Info(string message) { }
    public void Debug(string message) { }
}

/// <summary>
/// In-memory logger for testing assertions.
/// </summary>
public sealed class TestRoutingLog : IRoutingLog
{
    public List<LogEntry> Entries { get; } = new();

    public void Warning(string message) => Entries.Add(new LogEntry(LogLevel.Warning, message));
    public void Error(string message) => Entries.Add(new LogEntry(LogLevel.Error, message));
    public void Info(string message) => Entries.Add(new LogEntry(LogLevel.Info, message));
    public void Debug(string message) => Entries.Add(new LogEntry(LogLevel.Debug, message));

    public bool HasWarning(string contains) =>
        Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(contains));

    public bool HasError(string contains) =>
        Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains(contains));

    public void Clear() => Entries.Clear();
}

public enum LogLevel { Debug, Info, Warning, Error }

public record LogEntry(LogLevel Level, string Message);
