using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>
/// Bridges the core's <see cref="ILog"/> to Dalamud's <see cref="IPluginLog"/>. Callers must
/// never pass secrets or raw request/response bodies (R22) — that discipline lives at the call
/// sites in the core, which only ever log status codes and <c>request_id</c>s.
/// </summary>
public sealed class PluginLogAdapter : ILog
{
    private readonly IPluginLog _log;

    /// <summary>Creates the adapter.</summary>
    /// <param name="log">The injected Dalamud plugin log.</param>
    public PluginLogAdapter(IPluginLog log) => _log = log;

    /// <inheritdoc />
    public void Info(string message) => _log.Information(message);

    /// <inheritdoc />
    public void Warning(string message) => _log.Warning(message);

    /// <inheritdoc />
    public void Error(string message) => _log.Error(message);
}
