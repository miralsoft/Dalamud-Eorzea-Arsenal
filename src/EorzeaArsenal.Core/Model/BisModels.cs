namespace EorzeaArsenal.Model;

/// <summary>One resolved BiS target gearset from <c>GET /gear/bis</c>.</summary>
public sealed class BisGearset
{
    /// <summary>The character hash this target belongs to.</summary>
    public string? CidHash { get; init; }

    /// <summary>Uppercase 3-letter job code.</summary>
    public required string Job { get; init; }

    /// <summary>The in-game gearset slot the target maps to (match by this + <see cref="Job"/>).</summary>
    public int GearIndex { get; init; }

    /// <summary>Optional target name.</summary>
    public string? Name { get; init; }

    /// <summary>Optional set-level source (used as a fallback when an item has no own source).</summary>
    public string? Source { get; init; }

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
