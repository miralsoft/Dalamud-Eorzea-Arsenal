using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;

namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>
/// Adapts the persisted <see cref="PluginConfig"/> to the core's <see cref="ITokenStore"/> and
/// <see cref="IApiSettings"/> seams. Writing the key or base URL persists immediately via the
/// injected save callback. This is the single place the secret key is read/written (R19/R20).
/// </summary>
/// <remarks>
/// Keys are held <b>per API address</b>. One environment's key is meaningless on another, and sending
/// a production key to a test server (or the reverse) is the kind of mistake that is invisible until
/// it matters — so switching the base URL switches the key with it, and an address with no key of its
/// own reads as disconnected rather than borrowing one.
/// </remarks>
public sealed class ConfigStore : ITokenStore, IApiSettings
{
    private readonly PluginConfig _config;
    private readonly Action _save;

    /// <summary>Creates the store.</summary>
    /// <param name="config">The live config instance.</param>
    /// <param name="save">Callback that persists the config (e.g. SavePluginConfig).</param>
    public ConfigStore(PluginConfig config, Action save)
    {
        _config = config;
        _save = save;
        AdoptKeyForCurrentAddress();
    }

    /// <inheritdoc />
    public bool HasKey => !string.IsNullOrEmpty(ApiKey);

    /// <inheritdoc />
    public string? ApiKey => ApiKeyRing.KeyFor(_config.ApiKeys, BaseUrl);

    /// <summary>The base URL, trimmed of any trailing slash (P9).</summary>
    public string BaseUrl => _config.BaseUrl.Trim().TrimEnd('/');

    /// <summary>The host the current base URL points at — what a key is filed under.</summary>
    public string AddressKey => ApiKeyRing.HostOf(BaseUrl);

    /// <summary>Whether the plugin is pointed at something other than the shipped production address.</summary>
    public bool IsCustomAddress => ApiKeyRing.IsCustomAddress(BaseUrl, PluginConfig.DefaultBaseUrl);

    /// <inheritdoc />
    public void SetApiKey(string apiKey)
    {
        var trimmed = apiKey.Trim();
        _config.ApiKeys[AddressKey] = trimmed;
        _config.ApiKey = trimmed;
        _save();
    }

    /// <inheritdoc />
    public void Clear()
    {
        _config.ApiKeys.Remove(AddressKey);
        _config.ApiKey = null;
        _save();
    }

    /// <summary>
    /// Re-reads the key after the address changed, so the rest of the plugin sees the one belonging to
    /// where it is now pointed — or none at all.
    /// </summary>
    public void OnAddressChanged()
    {
        _config.ApiKey = ApiKey;
        _save();
    }

    /// <summary>
    /// Files a pre-existing single key under the address it was actually issued for. Runs once on
    /// load: before per-address keys existed there was one key and one URL, and that pairing is the
    /// only thing that can be known about it.
    /// </summary>
    private void AdoptKeyForCurrentAddress()
    {
        if (string.IsNullOrEmpty(_config.ApiKey) || _config.ApiKeys.ContainsKey(AddressKey))
        {
            return;
        }

        _config.ApiKeys[AddressKey] = _config.ApiKey;
        _save();
    }
}
