using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>
/// Adapts the persisted <see cref="PluginConfig"/> to the core's <see cref="ITeamsSeenStore"/> so the
/// team-notification "already toasted" watermark survives restarts (only an id — no secrets, R19/R20).
/// </summary>
public sealed class TeamsSeenStore : ITeamsSeenStore
{
    private readonly PluginConfig _config;
    private readonly Action _save;

    /// <summary>Creates the store.</summary>
    /// <param name="config">The live config instance.</param>
    /// <param name="save">Callback that persists the config.</param>
    public TeamsSeenStore(PluginConfig config, Action save)
    {
        _config = config;
        _save = save;
    }

    /// <inheritdoc />
    public long LastNotificationId
    {
        get => _config.TeamsLastNotificationId;
        set => _config.TeamsLastNotificationId = value;
    }

    /// <inheritdoc />
    public void Save() => _save();
}
