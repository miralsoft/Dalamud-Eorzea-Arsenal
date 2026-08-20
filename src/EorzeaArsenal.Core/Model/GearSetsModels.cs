namespace EorzeaArsenal.Model;

/// <summary>
/// One stored gearset as the server knows it, from <c>GET /gear/sets</c>. Flat — one row per gearset
/// across every character the key has access to — so the mapping can be indexed by
/// <see cref="SetUid"/> without walking a tree.
/// </summary>
public sealed class StoredGearset
{
    /// <summary>The server's numeric character id, sent as a string.</summary>
    public string? CharacterId { get; init; }

    /// <summary>The hashed character id this gearset belongs to.</summary>
    public string? CidHash { get; init; }

    /// <summary>The server's identity: 32 lowercase hex characters, opaque and stable.</summary>
    public string? SetUid { get; init; }

    /// <summary>
    /// Display order. Values above 99 are deliberate and must not be validated against the in-game
    /// range: 100–999 is where a row lands whose position was claimed by another gearset, and 1000+
    /// belongs to hand-made sets.
    /// </summary>
    public int GearIndex { get; init; }

    /// <summary>Uppercase 3-letter job code.</summary>
    public string? Job { get; init; }

    /// <summary>The gearset name as last pushed. May be empty; an empty name is real information.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Who created the row: <c>plugin</c> for one that came from a push, <c>manual</c> for one the web
    /// editor made. A manual set does not exist in game, so it is never looked for there and never sent
    /// back as ours.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>When the server last touched the row, as it formats it.</summary>
    public string? UpdatedAt { get; init; }

    /// <summary>Whether this row came from a push rather than from the web editor.</summary>
    public bool IsFromPlugin => string.Equals(Source, GearsetSource.Plugin, StringComparison.Ordinal);
}

/// <summary>The values <see cref="StoredGearset.Source"/> is known to take.</summary>
public static class GearsetSource
{
    /// <summary>Created by a gear push — exists in game.</summary>
    public const string Plugin = "plugin";

    /// <summary>Created in the web editor — does not exist in game.</summary>
    public const string Manual = "manual";
}

/// <summary>
/// Response of <c>GET /gear/sets</c>: the mapping, readable without writing. Reading it is how a cache
/// miss is answered — pushing to learn the mapping would be a write in order to read.
/// </summary>
public sealed class GearSetsResponse
{
    /// <summary>Every stored gearset the key can see, one row each.</summary>
    public List<StoredGearset> Data { get; init; } = [];
}
