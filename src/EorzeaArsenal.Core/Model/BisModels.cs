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

    /// <summary>The in-game gearset slot the target maps to (match by this + <see cref="Job"/>).</summary>
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

    /// <summary>Target items keyed by the 12 PascalCase slot keys, each <c>{ id, materia, source? }</c>.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];
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
}
