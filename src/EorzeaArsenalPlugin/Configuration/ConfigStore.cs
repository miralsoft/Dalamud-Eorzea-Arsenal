using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>
/// Adapts the persisted <see cref="PluginConfig"/> to the core's <see cref="ITokenStore"/> and
/// <see cref="IApiSettings"/> seams. Writing the key or base URL persists immediately via the
/// injected save callback. This is the single place the secret key is read/written (R19/R20).
/// </summary>
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
    }

    /// <inheritdoc />
    public bool HasKey => !string.IsNullOrEmpty(_config.ApiKey);

    /// <inheritdoc />
    public string? ApiKey => _config.ApiKey;

    /// <summary>The base URL, trimmed of any trailing slash (P9).</summary>
    public string BaseUrl => _config.BaseUrl.Trim().TrimEnd('/');

    /// <inheritdoc />
    public void SetApiKey(string apiKey)
    {
        _config.ApiKey = apiKey.Trim();
        _save();
    }

    /// <inheritdoc />
    public void Clear()
    {
        _config.ApiKey = null;
        _save();
    }
}
