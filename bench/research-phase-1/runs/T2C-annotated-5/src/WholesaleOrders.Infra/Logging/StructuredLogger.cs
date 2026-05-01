namespace WholesaleOrders.Infra.Logging;

public interface IStructuredLogger
{
    void Info(string message, object? context = null);
    void Warn(string message, object? context = null);
    void Error(string message, Exception? exception = null, object? context = null);
}

public class StructuredLogger : IStructuredLogger
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Info(string message, object? context = null) =>
        Append("INFO", message, exception: null, context);

    public void Warn(string message, object? context = null) =>
        Append("WARN", message, exception: null, context);

    public void Error(string message, Exception? exception = null, object? context = null) =>
        Append("ERROR", message, exception, context);

    private void Append(string level, string message, Exception? exception, object? context)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, message, exception?.ToString(), context);
        lock (_lock) _entries.Add(entry);
    }
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Exception,
    object? Context);
