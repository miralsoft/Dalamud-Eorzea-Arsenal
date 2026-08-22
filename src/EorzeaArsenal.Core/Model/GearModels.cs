namespace EorzeaArsenal.Model;

/// <summary>
/// The character identity block of a gear push. Only the minimum data leaves the machine
/// (rules R25/R27 data minimization): name, home world, the hashed character id, and the
/// optional public Lodestone id. The raw ContentId is <b>never</b> sent — see
/// <see cref="EorzeaArsenal.Gear.CidHash"/>.
/// </summary>
public sealed class CharacterDto
{
    /// <summary>Full character name, e.g. <c>"Sanaka Sundream"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Home world name, e.g. <c>"Twintania"</c>.</summary>
    public required string World { get; init; }

    /// <summary>
    /// Stable, salt-free lowercase-hex SHA-256 of the ContentId rendered as a decimal string
    /// (64 chars). Required for the plugin path; lets the server de-duplicate the character.
    /// </summary>
    public required string CidHash { get; init; }

    /// <summary>Optional public Lodestone id, if known.</summary>
    public long? LodestoneId { get; init; }
}

/// <summary>A single equipped item: its item id plus any melded materia item ids.</summary>
public sealed class ItemDto
{
    /// <summary>Real item id in the range 1..9,999,999.</summary>
    public required int Id { get; init; }

    /// <summary>Materia <i>item</i> ids melded into the piece; may be empty.</summary>
    public List<int> Materia { get; init; } = [];

    /// <summary>
    /// Optional item source as classified by the API (e.g. <c>raid</c>, <c>tome</c>, <c>crafted</c>,
    /// <c>relic</c>, <c>ultimate</c>). Returned by <c>GET /gear/bis</c>; never sent on push (R14
    /// forward-compatible). <see langword="null"/> when unknown.
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>One in-game gearset (one job loadout).</summary>
public sealed class GearsetDto
{
    /// <summary>The in-game gearset slot, 0..99. Idempotency key for the upsert.</summary>
    public required int GearIndex { get; init; }

    /// <summary>Optional gearset display name, ≤ 64 chars.</summary>
    public string? Name { get; init; }

    /// <summary>Uppercase 3-letter job code from the whitelist (see <see cref="EorzeaArsenal.Gear.JobMap"/>).</summary>
    public required string Job { get; init; }

    /// <summary>
    /// Equipped items keyed by slot name (<c>Weapon</c>, <c>Head</c>, …). Unknown keys are
    /// ignored by the server (forward-compatible).
    /// </summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>Optional food item id.</summary>
    public int? Food { get; init; }

    /// <summary>Optional item level of the set.</summary>
    public int? Ilvl { get; init; }
}

/// <summary>
/// A read of the player's gear as produced by an <see cref="EorzeaArsenal.Abstractions.IGearSource"/>.
/// It is wrapped into a <see cref="GearPayload"/> (adding the protocol version) before being sent.
/// </summary>
public sealed class GearData
{
    /// <summary>The character the gearsets belong to.</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>All in-game gearsets across all jobs.</summary>
    public required IReadOnlyList<GearsetDto> Gearsets { get; init; }
}

/// <summary>The wire body of <c>PUT /gear</c>.</summary>
public sealed class GearPayload
{
    /// <summary>Protocol version the plugin speaks. Always <c>1</c> today (R14).</summary>
    public int ProtocolVersion { get; init; } = ProtocolConstants.ProtocolVersion;

    /// <summary>The plugin build that produced this payload (see <see cref="ProtocolConstants.PluginVersion"/>).</summary>
    public string PluginVersion { get; init; } = ProtocolConstants.PluginVersion;

    /// <summary>The character block.</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>
    /// What this push covered — <see cref="JobScope.Combat"/> or <see cref="JobScope.All"/>. Sent on
    /// every push, including when the range is full: only then does its absence mean "an older client"
    /// rather than "this one held back". See <see cref="JobScope"/> for why a version could not carry
    /// this.
    ///
    /// <para>
    /// The default is the <i>narrow</i> value on purpose. A path that forgets to state its scope then
    /// under-claims, which costs a sync for some rows; over-claiming costs the player their rows, because
    /// the server parks what a full push did not report.
    /// </para>
    /// </summary>
    public string Scope { get; init; } = JobScope.Combat;

    /// <summary>All gearsets being upserted (max 200).</summary>
    public required IReadOnlyList<GearsetDto> Gearsets { get; init; }

    /// <summary>Wraps a <see cref="GearData"/> snapshot into a sendable payload.</summary>
    /// <param name="data">The snapshot read from the game, already filtered to what may be sent.</param>
    /// <param name="scope">
    /// What this push covers: <see cref="JobScope.Combat"/> or <see cref="JobScope.All"/>. Required and
    /// never defaulted, because a wrong value here is the one mistake that makes the server park rows the
    /// game still has. Whoever builds a payload states what went into it.
    /// </param>
    /// <returns>A payload carrying the current protocol version.</returns>
    public static GearPayload From(GearData data, string scope) => new()
    {
        Character = data.Character,
        Gearsets = data.Gearsets,
        Scope = scope,
    };
}

/// <summary>
/// How the server recognised a pushed gearset. Every rung requires the job to agree; a position that
/// changed jobs is a different gearset, which is what the old <c>gear_index</c> key got wrong. Names
/// come from the API and are matched case-sensitively — an unknown value is simply passed through.
/// </summary>
public static class MatchedBy
{
    /// <summary>Same job, same name, same position.</summary>
    public const string Exact = "exact";

    /// <summary>Same job, same name.</summary>
    public const string Name = "name";

    /// <summary>
    /// Same job and name, but more than one candidate on either side, paired in position order. The
    /// one value where the server <b>guessed</b>: deterministic and harmless to data, but it is what
    /// to warn about and what to quote in a bug report.
    /// </summary>
    public const string NameAmbiguous = "name_ambiguous";

    /// <summary>Same job, identical items — a renamed set.</summary>
    public const string Items = "items";

    /// <summary>
    /// Same job, same position; the last resort. It only fires when name <i>and</i> items both failed,
    /// so after a reorder it is the rung most likely to be wrong — worth a log line of its own.
    /// </summary>
    public const string Index = "index";

    /// <summary>Nothing matched: a new gearset with a freshly minted uid.</summary>
    public const string New = "new";

    /// <summary>Whether a value is one the server guessed rather than established.</summary>
    /// <param name="matchedBy">The value from the push response; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for the two rungs that can attach a set to the wrong row.</returns>
    public static bool IsUncertain(string? matchedBy) =>
        matchedBy is NameAmbiguous or Index;
}

/// <summary>
/// One line of the mapping a push answers with: which gearset the server recognised the entry as, and
/// on which rung of its ladder. The plugin never mints a <see cref="SetUid"/> — it only reads them.
/// </summary>
public sealed class GearsetAssignment
{
    /// <summary>The in-game position that was sent. Display order only; not an identity.</summary>
    public int GearIndex { get; init; }

    /// <summary>
    /// The server's identity for this gearset: 32 lowercase hex characters, opaque and stable for the
    /// life of the gearset. <see langword="null"/> on a server that does not mint them yet, which is
    /// the signal to fall back to the old <c>(gear_index, job)</c> path rather than to show nothing.
    /// </summary>
    public string? SetUid { get; init; }

    /// <summary>Which rung matched — see <see cref="MatchedBy"/>. <see langword="null"/> when unsaid.</summary>
    public string? MatchedBy { get; init; }
}

/// <summary>Success body of <c>PUT /gear</c>: <c>{ "status": "ok", "character_id": "42", "gearsets": 3 }</c>.</summary>
public sealed class GearPushResult
{
    /// <summary>Always <c>"ok"</c> on success.</summary>
    public string? Status { get; init; }

    /// <summary>Server-side character id the gear was linked to.</summary>
    public string? CharacterId { get; init; }

    /// <summary>Number of gearsets accepted.</summary>
    public int Gearsets { get; init; }

    /// <summary>
    /// The mapping, <b>index-aligned with the gearsets that were sent</b>. Empty on a server that does
    /// not answer with it — the plugin then keeps working the old way rather than losing its bearings.
    /// </summary>
    public List<GearsetAssignment> Sets { get; init; } = [];
}

/// <summary>Protocol-wide constants shared across the core.</summary>
public static class ProtocolConstants
{
    /// <summary>The gear protocol version this plugin implements.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// The plugin's own version, sent alongside <see cref="ProtocolVersion"/> on every write so a
    /// "it stopped working" report says which build produced it — and, once a test environment exists,
    /// whether someone synced to the wrong one. Purely informational; the server ignores it if unknown.
    /// </summary>
    /// <remarks>
    /// Taken from the release notes, whose newest entry is pinned by test to the version in the
    /// plugin's csproj — so this cannot drift from what Dalamud installs.
    /// </remarks>
    public static string PluginVersion => Localization.ReleaseNotes.Latest.Version;

    /// <summary>Maximum number of gearsets accepted in one push.</summary>
    public const int MaxGearsets = 200;

    /// <summary>Maximum serialized payload size accepted by the server (64 KB).</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    /// <summary>Inclusive lower bound for a real item id.</summary>
    public const int MinItemId = 1;

    /// <summary>Inclusive upper bound for a real item id.</summary>
    public const int MaxItemId = 9_999_999;

    /// <summary>Inclusive upper bound for an in-game gearset index.</summary>
    public const int MaxGearIndex = 99;

    /// <summary>Maximum length of a gearset name.</summary>
    public const int MaxGearsetNameLength = 64;

    /// <summary>The single scope the issued key carries (R17 least privilege).</summary>
    public const string RequiredScope = "gear:write";
}
