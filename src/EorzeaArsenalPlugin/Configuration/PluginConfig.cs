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

    /// <summary>
    /// The default production API base URL; always user-editable (P9). For local development,
    /// change it to the launcher's URL (e.g. <c>http://127.0.0.1:8080/api/v1</c>).
    /// </summary>
    public const string DefaultBaseUrl = "https://xivarsenal.app/api/v1";

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

    /// <summary>Whether to push (debounced) when a gearset changes in-game.</summary>
    public bool PushOnGearsetChange { get; set; }

    /// <summary>Whether to show a game toast on push outcomes (in addition to chat).</summary>
    public bool UseToasts { get; set; } = true;

    /// <summary>Whether to show the BiS hover overlay over equipment items.</summary>
    public bool ShowBisTooltip { get; set; } = true;

    /// <summary>BiS window: show all gearsets (<see langword="true"/>) or only the current one.</summary>
    public bool BisShowAllSets { get; set; }

    /// <summary>BiS window slot filter: 0 = all, 1 = incomplete only, 2 = materia issues only.</summary>
    public int BisFilter { get; set; }

    /// <summary>BiS window: show the aggregated "shopping list" (still-needed items + materia) instead of per-set slots.</summary>
    public bool BisShoppingList { get; set; }

    /// <summary>How verbose the Dalamud log output is.</summary>
    public LogVerbosity Verbosity { get; set; } = LogVerbosity.Normal;

    /// <summary>Optional web app URL for the "Open web app" button. If empty it is derived from the base URL.</summary>
    public string? WebAppUrl { get; set; }

    /// <summary>
    /// Per-character push opt-in, keyed by the character's <c>cid_hash</c>. A character not in the
    /// map defaults to allowed; the user can disable specific characters (briefing §7).
    /// </summary>
    public Dictionary<string, CharacterOptIn> Characters { get; set; } = new();

    /// <summary>Whether the character with the given hash may be pushed (unknown = allowed).</summary>
    /// <param name="cidHash">The character's <c>cid_hash</c>.</param>
    /// <returns><see langword="true"/> unless the character is known and explicitly disabled.</returns>
    public bool IsCharacterEnabled(string cidHash) =>
        !Characters.TryGetValue(cidHash, out var entry) || entry.Enabled;

    /// <summary>Records a character (enabled by default) if it is not yet known.</summary>
    /// <param name="cidHash">The character's <c>cid_hash</c>.</param>
    /// <param name="name">Character name (display only).</param>
    /// <param name="world">Home world (display only).</param>
    /// <returns><see langword="true"/> if a new entry was added.</returns>
    public bool RecordCharacter(string cidHash, string name, string world)
    {
        if (Characters.ContainsKey(cidHash))
        {
            return false;
        }

        Characters[cidHash] = new CharacterOptIn { Name = name, World = world, Enabled = true };
        return true;
    }

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
