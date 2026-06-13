using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>Fans a single <see cref="ILog"/> call out to several sinks (e.g. the Dalamud log and the in-app buffer).</summary>
public sealed class CompositeLog : ILog
{
    private readonly ILog[] _targets;

    /// <summary>Creates a composite over the given log sinks.</summary>
    /// <param name="targets">The sinks to forward to, in order.</param>
    public CompositeLog(params ILog[] targets) => _targets = targets;

    /// <inheritdoc />
    public void Info(string message)
    {
        foreach (var target in _targets)
        {
            target.Info(message);
        }
    }

    /// <inheritdoc />
    public void Warning(string message)
    {
        foreach (var target in _targets)
        {
            target.Warning(message);
        }
    }

    /// <inheritdoc />
    public void Error(string message)
    {
        foreach (var target in _targets)
        {
            target.Error(message);
        }
    }
}
