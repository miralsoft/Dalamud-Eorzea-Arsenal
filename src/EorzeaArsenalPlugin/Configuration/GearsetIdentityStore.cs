using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>
/// Adapts the persisted <see cref="PluginConfig"/> to the core's <see cref="IGearsetIdentityStore"/>, so
/// the gearset mapping survives a restart and the in-game comparison has an answer before the first
/// push of a session rather than only after it.
/// </summary>
/// <remarks>
/// What is stored is a cache of the server's decisions — a <c>set_uid</c> plus the job, name and item
/// key needed to find the gearset again. No secrets (R19/R20), and nothing here is ever treated as
/// authoritative: the server mints identities, this only remembers them.
/// </remarks>
public sealed class GearsetIdentityStore : IGearsetIdentityStore
{
    private readonly PluginConfig _config;
    private readonly Action _save;

    /// <summary>Creates the store.</summary>
    /// <param name="config">The live config instance.</param>
    /// <param name="save">Callback that persists the config.</param>
    public GearsetIdentityStore(PluginConfig config, Action save)
    {
        _config = config;
        _save = save;
    }

    /// <inheritdoc />
    public Dictionary<string, List<CachedGearsetIdentity>> Identities
    {
        get => _config.GearsetIdentities;
        set => _config.GearsetIdentities = value;
    }

    /// <inheritdoc />
    public void Save() => _save();
}
