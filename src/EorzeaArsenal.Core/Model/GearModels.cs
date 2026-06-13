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

    /// <summary>The character block.</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>All gearsets being upserted (max 200).</summary>
    public required IReadOnlyList<GearsetDto> Gearsets { get; init; }

    /// <summary>Wraps a <see cref="GearData"/> snapshot into a sendable payload.</summary>
    /// <param name="data">The snapshot read from the game.</param>
    /// <returns>A payload carrying the current protocol version.</returns>
    public static GearPayload From(GearData data) => new()
    {
        Character = data.Character,
        Gearsets = data.Gearsets,
    };
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
}

/// <summary>Protocol-wide constants shared across the core.</summary>
public static class ProtocolConstants
{
    /// <summary>The gear protocol version this plugin implements.</summary>
    public const int ProtocolVersion = 1;

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
