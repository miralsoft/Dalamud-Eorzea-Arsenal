using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>
/// Bridges the core's <see cref="ILog"/> to Dalamud's <see cref="IPluginLog"/>, gated by the
/// user's <see cref="LogVerbosity"/> setting. Callers must never pass secrets or raw
/// request/response bodies (R22) — that discipline lives at the call sites in the core, which only
/// ever log status codes and <c>request_id</c>s.
/// </summary>
public sealed class PluginLogAdapter : ILog
{
    private readonly IPluginLog _log;
    private readonly Func<LogVerbosity> _verbosity;

    /// <summary>Creates the adapter.</summary>
    /// <param name="log">The injected Dalamud plugin log.</param>
    /// <param name="verbosity">Live accessor for the current verbosity setting.</param>
    public PluginLogAdapter(IPluginLog log, Func<LogVerbosity> verbosity)
    {
        _log = log;
        _verbosity = verbosity;
    }

    /// <inheritdoc />
    public void Info(string message)
    {
        if (_verbosity() >= LogVerbosity.Normal)
        {
            _log.Information(message);
        }
    }

    /// <inheritdoc />
    public void Warning(string message) => _log.Warning(message);

    /// <inheritdoc />
    public void Error(string message) => _log.Error(message);
}
