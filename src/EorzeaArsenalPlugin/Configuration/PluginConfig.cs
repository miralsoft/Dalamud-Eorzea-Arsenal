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

    /// <summary>
    /// The stored API key (secret) for the <b>current</b> <see cref="BaseUrl"/>.
    /// <see langword="null"/> when this address has no key.
    /// </summary>
    /// <remarks>
    /// Kept for the config format and as the live value; the per-address keys live in
    /// <see cref="ApiKeys"/>. Reading this directly is what the store does after resolving the host.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// One key per API address, keyed by host. A key issued by one environment is meaningless — and
    /// dangerous — on another: a test key must never reach production, and a production key must never
    /// be sent to a test server. There is deliberately <b>no fallback</b> for an unknown host; without
    /// that rule the map would be decoration and the first switch would leak the live key.
    /// </summary>
    public Dictionary<string, string> ApiKeys { get; set; } = [];

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

    /// <summary>Whether to show the compact status entry in the in-game server-info (DTR) bar.</summary>
    public bool ShowDtrBar { get; set; } = true;

    /// <summary>
    /// Opt-in: also upload the owned, equippable items (inventory) via <c>POST /inventory</c> so the
    /// web app can tick off what the player already has. Independent of the gear push; off by default.
    /// </summary>
    public bool SyncInventory { get; set; }

    /// <summary>
    /// Opt-in: when <see cref="SyncInventory"/> is on, also scan a retainer's gear when its bag is
    /// opened at a summoning bell. Off by default (retainers are only read when explicitly enabled).
    /// </summary>
    public bool SyncRetainers { get; set; }

    /// <summary>
    /// Opt-in: also upload the weekly checklist (tomestones acquired, weekly content done) via
    /// <c>PUT /characters/{id}/weekly</c> so the web app can track weekly progress without manual
    /// ticking. Independent of the gear push; off by default. Only fields read with confidence are
    /// sent, so it never overwrites a manual web-app entry.
    /// </summary>
    public bool SyncWeekly { get; set; }

    /// <summary>
    /// Opt-in: enable the read-only <b>Teams companion</b> (calendar, mit cheat sheets, content hub,
    /// farm, FFLogs) plus the two own-record writes (RSVP, absence) and in-game team notifications.
    /// Independent of the gear push; off by default. Needs a key with <c>teams:read</c>/<c>teams:write</c>.
    /// </summary>
    public bool SyncTeams { get; set; }

    /// <summary>The highest team-notification id already toasted (dedup watermark; persisted).</summary>
    public long TeamsLastNotificationId { get; set; }

    /// <summary>The team last selected in the Teams window (id), so it reopens where the user left off.</summary>
    public long TeamsLastTeamId { get; set; }

    /// <summary>Mit-sheet: show every job at once (<see langword="true"/>) instead of a single job.</summary>
    public bool TeamsShowAllJobs { get; set; }

    /// <summary>Remembered mit-sheet job per plan, keyed by <c>planId</c> (as a string).</summary>
    public Dictionary<string, string> TeamsPlanJob { get; set; } = new();

    /// <summary>Mit-sheet cooldown display: 0 = icon + name, 1 = icon only (name on hover), 2 = name only.</summary>
    public int TeamsMitDisplay { get; set; }

    /// <summary>Content-hub resource label: 0 = icon + text, 1 = icon only, 2 = text only.</summary>
    public int TeamsResourceDisplay { get; set; }

    /// <summary>Content hub: show note bodies inline (<see langword="true"/>) or just the title.</summary>
    public bool TeamsShowNotes { get; set; } = true;

    /// <summary>Mit-sheet default: show all jobs (<see langword="true"/>) or just the current/first job.</summary>
    public bool TeamsDefaultAllJobs { get; set; }

    /// <summary>Mit-sheet default: include the "other" mechanic tag (off by default = hide clutter).</summary>
    public bool TeamsDefaultShowOther { get; set; }

    /// <summary>Termine list: show already-elapsed occurrences too.</summary>
    public bool TeamsShowPastEvents { get; set; }

    /// <summary>Mit-sheet default: tick every phase (<see langword="true"/>) or just the first one.</summary>
    public bool TeamsDefaultAllPhases { get; set; } = true;

    /// <summary>Termine list: text scale (1.0 - 1.6).</summary>
    public float TeamsEventTextScale { get; set; } = 1.15f;

    /// <summary>Purchase advisor: text scale (1.0 - 1.6). Long item names need the room.</summary>
    public float AdvisorTextScale { get; set; } = 1.2f;

    /// <summary>The release-notes version the user has acknowledged; empty on a fresh install.</summary>
    public string LastSeenReleaseNotes { get; set; } = string.Empty;

    /// <summary>Open the what's-new window once after the plugin updated.</summary>
    public bool ShowWhatsNewOnUpdate { get; set; } = true;

    /// <summary>BiS window: show all gearsets (<see langword="true"/>) or only the current one.</summary>
    public bool BisShowAllSets { get; set; }

    /// <summary>BiS window slot filter: 0 = all, 1 = incomplete only, 2 = materia issues only.</summary>
    public int BisFilter { get; set; }

    /// <summary>BiS window: show the aggregated "shopping list" (still-needed items + materia) instead of per-set slots.</summary>
    public bool BisShoppingList { get; set; }

    /// <summary>BiS window: render the character-screen-style icon grid instead of the per-slot list.</summary>
    public bool BisGridView { get; set; }

    /// <summary>BiS window: show "how to get it" sourcing (savage/tome routes) on a piece's hover.</summary>
    public bool BisShowSourcing { get; set; } = true;

    /// <summary>How verbose the Dalamud log output is.</summary>
    public LogVerbosity Verbosity { get; set; } = LogVerbosity.Normal;

    /// <summary>Optional web app URL for the "Open web app" button. If empty it is derived from the base URL.</summary>
    public string? WebAppUrl { get; set; }

    /// <summary>
    /// Per-character push opt-in, keyed by the character's <c>cid_hash</c>. A character not in the
    /// map defaults to allowed; the user can disable specific characters (briefing §7).
    /// </summary>
    public Dictionary<string, CharacterOptIn> Characters { get; set; } = new();

    /// <summary>
    /// Learned mapping of <c>cid_hash → server character_id</c> (e.g. <c>"42"</c>), populated from
    /// gear/inventory push responses. Needed to address the per-character REST paths the weekly
    /// checklist uses; persisted so a character's id survives restarts.
    /// </summary>
    public Dictionary<string, string> CharacterIds { get; set; } = new();

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
