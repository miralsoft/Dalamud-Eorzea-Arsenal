using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>The severity of a captured log line.</summary>
public enum LogLevel
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,
}

/// <summary>A single captured log entry.</summary>
/// <param name="Time">Local timestamp.</param>
/// <param name="Level">Severity.</param>
/// <param name="Message">The message (already free of secrets/bodies, R22).</param>
public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Message);

/// <summary>
/// An <see cref="ILog"/> that keeps the most recent messages in a bounded in-memory ring buffer,
/// so they can be shown and copied in the diagnostics window. Thread-safe (logs arrive from
/// background push threads). Captures everything regardless of the log-verbosity setting — that
/// setting only controls what reaches the Dalamud log. Never receives secrets or bodies (R22).
/// </summary>
public sealed class LogBuffer : ILog
{
    private const int Capacity = 300;

    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new();

    /// <inheritdoc />
    public void Info(string message) => Add(LogLevel.Info, message);

    /// <inheritdoc />
    public void Warning(string message) => Add(LogLevel.Warning, message);

    /// <inheritdoc />
    public void Error(string message) => Add(LogLevel.Error, message);

    /// <summary>Returns a snapshot of the current entries, oldest first.</summary>
    /// <returns>The captured entries.</returns>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>Clears all captured entries.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>Renders all entries as a single plain-text block for the clipboard.</summary>
    /// <returns>One line per entry: <c>[HH:mm:ss] LEVEL message</c>.</returns>
    public string ToText()
    {
        lock (_gate)
        {
            return string.Join('\n', _entries.Select(e => $"[{e.Time:HH:mm:ss}] {Tag(e.Level)} {e.Message}"));
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "ERR ",
        LogLevel.Warning => "WARN",
        _ => "INFO",
    };

    private void Add(LogLevel level, string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new LogEntry(DateTime.Now, level, message));
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }
        }
    }
}
