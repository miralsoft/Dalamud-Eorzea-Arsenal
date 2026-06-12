namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Minimal logging seam so the core can emit diagnostics without depending on Dalamud's
/// <c>IPluginLog</c>. The plugin adapts this to <c>IPluginLog</c>; tests use a no-op or capture.
/// Implementations must never receive secrets or raw request/response bodies (R22).
/// </summary>
public interface ILog
{
    /// <summary>Logs an informational message.</summary>
    /// <param name="message">The message (no secrets/PII).</param>
    void Info(string message);

    /// <summary>Logs a warning.</summary>
    /// <param name="message">The message (no secrets/PII).</param>
    void Warning(string message);

    /// <summary>Logs an error.</summary>
    /// <param name="message">The message (no secrets/PII).</param>
    void Error(string message);
}

/// <summary>An <see cref="ILog"/> that discards everything. Useful as a default and in tests.</summary>
public sealed class NullLog : ILog
{
    /// <summary>A shared instance.</summary>
    public static readonly NullLog Instance = new();

    /// <inheritdoc />
    public void Info(string message) { }

    /// <inheritdoc />
    public void Warning(string message) { }

    /// <inheritdoc />
    public void Error(string message) { }
}
