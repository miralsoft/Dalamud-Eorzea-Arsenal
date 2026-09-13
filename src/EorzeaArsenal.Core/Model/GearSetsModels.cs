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

    /// <summary>
    /// What the row currently is — see <see cref="RowState"/>. <see langword="null"/> on a hand-made row
    /// nobody put aside, and on a server that does not report it yet.
    /// </summary>
    /// <remarks>
    /// A field rather than a reading of <see cref="GearIndex"/>: the index band says the same thing by
    /// accident, and a band is storage mechanics rather than a statement about a row. Read the state.
    /// </remarks>
    public string? State { get; init; }

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

/// <summary>
/// What a row is, as <c>GET /gear/sets</c> and the reconciliation answers report it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="PushState"/>, even though both have a <c>held</c>. One says what
/// happened to a gearset that was just sent; this one says what a stored row currently is. Folding them
/// into one enum would make the overlap look like the same thing, and it is not.
/// </para>
/// <para>
/// This cycle describes rows a push created. A hand-made row lies outside it: it is never
/// <see cref="Active"/>, <see cref="Held"/> or <see cref="Parked"/>, and carries either no state at all or
/// <see cref="Ignored"/>. The invariant worth testing is that one, not the shorter "state is null exactly
/// where source is manual", which holds until the first ignored hand-made row and then quietly does not.
/// </para>
/// </remarks>
public static class RowState
{
    /// <summary>Reported by the last sync that could have reported it.</summary>
    public const string Active = "active";

    /// <summary>Written, and waiting for somebody to say which stored row it is.</summary>
    public const string Held = "held";

    /// <summary>Not reported by a sync that could have reported it.</summary>
    public const string Parked = "parked";

    /// <summary>Put aside: not asked about again, still offered as a candidate, taken back by a reopen.</summary>
    public const string Ignored = "ignored";

    /// <summary>
    /// Whether a row with this state belongs in the resolution cache, which holds what is <b>in game</b>.
    /// </summary>
    /// <param name="state">The row state, or <see langword="null"/> for a hand-made row.</param>
    /// <returns><see langword="true"/> for <see cref="Active"/> and <see cref="Held"/>.</returns>
    /// <remarks>
    /// One condition over one field. <see cref="Held"/> is the one that is easy to get wrong: the row
    /// belongs to a live gearset and only its attribution is open, so leaving it out would show the player
    /// nothing for the very set they are being asked about. <c>source</c> is not needed here at all,
    /// because a hand-made row can carry neither of those two.
    /// </remarks>
    public static bool BelongsInResolutionCache(string? state) =>
        string.Equals(state, Active, StringComparison.Ordinal) ||
        string.Equals(state, Held, StringComparison.Ordinal);
}

/// <summary>
/// What happened to one gearset a push just sent, reported per entry in the answer.
/// </summary>
/// <remarks>
/// Three outcomes and no more. The middle one is load-bearing: creating a gearset in game has to sync
/// without anybody clicking anything, so ambiguity raises a question and unfamiliarity does not.
/// </remarks>
public static class PushState
{
    /// <summary>Recognised as a row the server already had.</summary>
    public const string Resolved = "resolved";

    /// <summary>Genuinely new; a uid was minted and the gear written.</summary>
    public const string New = "new";

    /// <summary>Written and marked, with candidates offered: one question for the player.</summary>
    public const string Held = "held";
}
