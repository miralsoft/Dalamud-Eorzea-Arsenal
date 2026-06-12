using Dalamud.Configuration;
using EorzeaArsenal.Localization;

namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>
/// Persisted plugin configuration. Carries a schema <see cref="Version"/> so future format
/// changes can migrate without ever wiping the user's API key or base URL (P12). The API key is
/// a secret stored only here, in the local Dalamud config (plaintext on the user's own machine is
/// the accepted trust boundary, P10); it is never logged or committed (R19/R20).
/// </summary>
[Serializable]
public sealed class PluginConfig : IPluginConfiguration
{
    /// <summary>The current configuration schema version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The default dev base URL; always user-editable (P9).</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:8080/api/v1";

    /// <inheritdoc />
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The full API base URL including <c>/api/v1</c> (P9). User-configurable.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>The stored API key (secret). <see langword="null"/> when disconnected.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Master opt-in: while false the plugin never contacts the API (R36).</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the user has acknowledged the third-party-tool ToS notice (R36).</summary>
    public bool TosAccepted { get; set; }

    /// <summary>UI language code (<c>"de"</c>/<c>"en"</c>).</summary>
    public string Language { get; set; } = Localizer.English;

    /// <summary>Whether to push automatically (throttled) when gear changes.</summary>
    public bool AutoPush { get; set; }

    /// <summary>Whether to push once on login.</summary>
    public bool PushOnLogin { get; set; }

    /// <summary>Minimum minutes between automatic pushes (proactive rate-limit guard, R23).</summary>
    public int AutoPushIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Migrates an older config in place to <see cref="CurrentVersion"/>. Always additive so no
    /// stored value is lost (P12). Returns whether anything changed (so the caller can re-save).
    /// </summary>
    /// <returns><see langword="true"/> if the config was upgraded.</returns>
    public bool Migrate()
    {
        var changed = false;

        // v0 → v1: stamp the version on configs created before versioning existed.
        if (Version < 1)
        {
            Version = 1;
            changed = true;
        }

        // Future migrations append here, each guarded by `if (Version < n)`.
        return changed;
    }
}
