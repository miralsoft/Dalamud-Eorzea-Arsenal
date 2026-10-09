namespace EorzeaArsenal.Model;

/// <summary>One resolved BiS target gearset from <c>GET /gear/bis</c>.</summary>
public sealed class BisGearset
{
    /// <summary>The character hash this target belongs to.</summary>
    public string? CidHash { get; init; }

    /// <summary>
    /// The server's numeric character id (sent as a string), which the personal advisor endpoints are
    /// keyed by — so no <c>cid_hash → id</c> lookup is needed. <see langword="null"/> on a server that
    /// does not send it, where the locally learned directory still fills in.
    /// </summary>
    public string? CharacterId { get; init; }

    /// <summary>Uppercase 3-letter job code.</summary>
    public required string Job { get; init; }

    /// <summary>
    /// The server's identity for the gearset this target is pinned to: 32 lowercase hex characters,
    /// opaque and stable. This is what the comparison keys on. <see langword="null"/> on a server that
    /// does not send it yet — then, and only then, <see cref="GearIndex"/> is used as the key again.
    /// </summary>
    public string? SetUid { get; init; }

    /// <summary>The in-game gearset slot the target maps to. <b>Display order</b>, not an identity.</summary>
    public int GearIndex { get; init; }

    /// <summary>Optional target name.</summary>
    public string? Name { get; init; }

    /// <summary>Optional set-level source (used as a fallback when an item has no own source).</summary>
    public string? Source { get; init; }

    /// <summary>
    /// The set's identity as the web app knows it — its apiPath / shortlink (e.g. <c>sl/&lt;uuid&gt;</c>
    /// or a catalog path). This is the key a purchase plan is stored under
    /// (<c>GET /me/advisor-plan?…&amp;target=</c>), so it must be read back from the set and never
    /// invented. <see langword="null"/> on a server that does not send it yet, which simply means no
    /// plan can be addressed for this set.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>Display name of the target set, when the server sends one alongside <see cref="Target"/>.</summary>
    public string? TargetName { get; init; }

    /// <summary>Target items keyed by the 12 PascalCase slot keys, each <c>{ id, materia, source?, hq? }</c>.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>
    /// What a crafter or gatherer target carries besides its pieces (server 1.4, revision 21), or
    /// <see langword="null"/> on a combat target. Its presence is what makes the materia comparison the
    /// web's <c>meldMatch</c> instead of the plain one.
    /// </summary>
    public CraftBlock? Craft { get; init; }

    /// <summary>Whether this is a crafter or gatherer target.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCraft => Craft is not null;
}

/// <summary>
/// Response of <c>GET /gear/bis</c>: the BiS targets for the caller's character(s). Gearsets with
/// no resolvable target are omitted, so <see cref="Data"/> may be shorter than the pushed gearsets.
/// </summary>
public sealed class BisResponse
{
    /// <summary>Protocol version the server speaks.</summary>
    public int ProtocolVersion { get; init; } = ProtocolConstants.ProtocolVersion;

    /// <summary>The resolved BiS targets.</summary>
    public List<BisGearset> Data { get; init; } = [];

    /// <summary>
    /// The stat rows of the crafter and gatherer target pieces and the materia table, once per answer
    /// that has such a row; <see langword="null"/> otherwise.
    /// </summary>
    public CraftTables? CraftTables { get; init; }
}
