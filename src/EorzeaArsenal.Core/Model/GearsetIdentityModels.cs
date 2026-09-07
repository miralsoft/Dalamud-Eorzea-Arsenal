namespace EorzeaArsenal.Model;

/// <summary>
/// One remembered line of the server's mapping: a gearset's <c>set_uid</c> and enough about the gearset
/// to find it again in the live list. Persisted, so the in-game comparison has an answer before the
/// first push of a session.
/// </summary>
/// <remarks>
/// Deliberately not a copy of the gearset. It holds only what the two keys are built from, because
/// anything more would be a second store of gear data that could disagree with the game.
/// </remarks>
[Serializable]
public sealed class CachedGearsetIdentity
{
    /// <summary>The server's identity. Opaque; never generated here.</summary>
    public string SetUid { get; set; } = string.Empty;

    /// <summary>Uppercase 3-letter job code.</summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>The gearset name as the server knows it. Empty is a real value, not a missing one.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The strong key over job, name and items, when it is known. <see langword="null"/> for a row
    /// learned from <c>GET /gear/sets</c>, which does not return items — such a row can only ever be
    /// matched on the weak key.
    /// </summary>
    public string? ItemsKey { get; set; }

    /// <summary>
    /// The gear alone, without the name, when it is known. Only a push can fill it, since
    /// <c>GET /gear/sets</c> returns no items.
    /// </summary>
    /// <remarks>
    /// Kept beside <see cref="ItemsKey"/> rather than replacing it, because the two answer different
    /// questions: that one asks whether this is the same set unchanged, this one asks whether it is the
    /// same gear whatever it is called now.
    /// </remarks>
    public string? GearKey { get; set; }

    /// <summary>
    /// Where the set sat in the player's list when this row was learned, or <see langword="null"/> when
    /// that is not known.
    /// </summary>
    /// <remarks>
    /// The one anchor in this row that came from an answer rather than from a guess: a push is
    /// index-aligned and the server confirms the position it answered for. Everything else is re-derived
    /// from what a live gearset looks like, and a gearset name is not something the player ever chose, so
    /// a cache that threw the position away was left telling two sets of one job apart by a name the game
    /// had written for both of them.
    /// </remarks>
    public int? GearIndex { get; set; }

    /// <summary>
    /// Which rung of the server's ladder produced this mapping, when it said so. Kept because the
    /// uncertain rungs are worth showing and worth quoting in a bug report — see
    /// <see cref="MatchedBy.IsUncertain"/>.
    /// </summary>
    public string? MatchedBy { get; set; }

    /// <summary>
    /// Whether the push that wrote this row disagreed with what this side remembered about the same gear.
    /// A contested row is never used to answer: it resolves as ambiguous, so the interface says it cannot
    /// tell rather than picking one of two accounts.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="Core.PushCrossCheck"/> and recomputed on every push, so it lasts exactly as long
    /// as the evidence for it. Persisted with the rest of the row, because the doubt outlives the session
    /// that found it.
    /// </remarks>
    public bool Contested { get; set; }
}

/// <summary>
/// One line of the diagnostics view over the mapping: a live gearset next to the identity it resolves
/// to. Built off the framework thread — resolving hashes every set — and then only read.
/// </summary>
/// <param name="GearIndex">The live position.</param>
/// <param name="Job">Job code.</param>
/// <param name="Name">The gearset name, as the game holds it.</param>
/// <param name="SetUid">The resolved identity, or <see langword="null"/> on a miss.</param>
/// <param name="MatchedBy">The rung the server reported, when it said.</param>
/// <param name="WasAmbiguous">Whether the cache declined because more than one row matched.</param>
/// <param name="By">Which of this side's own rungs answered, as opposed to what the server reported.</param>
public readonly record struct GearsetIdentityRow(
    int GearIndex,
    string Job,
    string? Name,
    string? SetUid,
    string? MatchedBy,
    bool WasAmbiguous,
    string? By = null)
{
    /// <summary>Whether an identity was resolved for this gearset.</summary>
    public bool IsResolved => !string.IsNullOrEmpty(SetUid);

    /// <summary>
    /// A short form of the uid for display. The full 32 characters say nothing a human needs; the first
    /// eight are enough to see that two dumps agree, which is the whole point of looking.
    /// </summary>
    public string ShortUid => SetUid is null or "" ? "—" : SetUid[..Math.Min(8, SetUid.Length)];

    /// <summary>What the server said when it minted this mapping, in one word.</summary>
    public string Rung => MatchedBy ?? (WasAmbiguous ? "ambiguous" : IsResolved ? "cached" : "unknown");

    /// <summary>
    /// Which of this side's rungs found it. A different question from <see cref="Rung"/>, and the one
    /// that matters while this ladder is being changed: that one repeats what the server said months ago.
    /// </summary>
    public string LocalRung => By ?? "?";
}

/// <summary>
/// What resolving a live gearset against the cache produced. A miss is a first-class answer here: the
/// caller is expected to show nothing rather than fall back to a guess.
/// </summary>
/// <param name="SetUid">The identity, or <see langword="null"/> when the cache cannot answer.</param>
/// <param name="MatchedBy">The rung the server reported for this mapping, if any.</param>
/// <param name="WasAmbiguous">
/// Whether the cache found more than one candidate and therefore refused to answer. Distinct from
/// "nothing found": it means the mapping exists but cannot be attributed, which is worth saying.
/// </param>
/// <param name="By">
/// Which of this side's own rungs produced the answer, for the diagnostics view. Distinct from
/// <paramref name="MatchedBy"/>, which is what the <b>server</b> said when it minted the mapping and is
/// carried along unchanged: reading one as the other makes it impossible to tell whether a set was found
/// by its contents or by where it sits, which is the only thing worth knowing while this is being tested.
/// </param>
public readonly record struct GearsetIdentityMatch(
    string? SetUid,
    string? MatchedBy,
    bool WasAmbiguous,
    string? By = null)
{
    /// <summary>A miss with nothing found at all.</summary>
    public static readonly GearsetIdentityMatch None = new(null, null, false, "none");

    /// <summary>A miss because more than one cached row carried the same weak key.</summary>
    public static readonly GearsetIdentityMatch Ambiguous = new(null, null, true, "ambiguous");

    /// <summary>Whether an identity was resolved.</summary>
    public bool IsResolved => !string.IsNullOrEmpty(SetUid);
}
