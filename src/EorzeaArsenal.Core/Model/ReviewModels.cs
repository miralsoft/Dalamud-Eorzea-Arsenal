namespace EorzeaArsenal.Model;

/// <summary>
/// The closed set of decision verbs. It grows only with a version, because an interface can only offer
/// what it knows, and a verb the plugin has never heard of would be silently missing from the dialog.
/// </summary>
public static class ReviewAction
{
    /// <summary>
    /// The newcomer IS that row: its uid, pinned target and team share stay, and the contents, name and
    /// position come from the newcomer. On a hand-made row this is <b>adoption</b>, which also flips it to
    /// plugin-governed and active, and is a one-way door the dialog has to name.
    /// </summary>
    public const string Link = "link";

    /// <summary>
    /// Genuinely different. The held row becomes active, and its candidates are left exactly as they were,
    /// pin and share included, because those belonged to the older set and the player just said the new one
    /// is not it.
    /// </summary>
    public const string New = "new";

    /// <summary>Stop asking about a row. It stays, and it stays a candidate.</summary>
    public const string Ignore = "ignore";

    /// <summary>Take an ignore back, because one click put it aside and one click is a misclick.</summary>
    public const string Reopen = "reopen";

    /// <summary>
    /// Remove the row and what hangs on it. Refused on a hand-made row, always, share or no share; on a
    /// plugin row with an active team share it is performed as <see cref="Release"/> rather than refused.
    /// </summary>
    public const string Delete = "delete";

    /// <summary>
    /// The row leaves plugin governance: it becomes hand-made and put aside, keeps its pin and its share,
    /// is never parked again and raises no question. The way out for somebody who uninstalls.
    /// </summary>
    public const string Release = "release";

    /// <summary>The two the bulk call accepts. Every other verb is rejected there.</summary>
    /// <param name="action">The verb.</param>
    /// <returns><see langword="true"/> for a link or a new.</returns>
    public static bool IsAttribution(string? action) =>
        string.Equals(action, Link, StringComparison.Ordinal) ||
        string.Equals(action, New, StringComparison.Ordinal);
}

/// <summary>
/// What the server proposes for one held newcomer, as part of a mapping that assigns each row at most
/// once.
/// </summary>
public sealed class ReviewProposal
{
    /// <summary>The action proposed: a link, or new when the mapping has nothing left for this one.</summary>
    public string? Action { get; init; }

    /// <summary>The row it proposes linking to, or <see langword="null"/> for new.</summary>
    public string? TargetUid { get; init; }

    /// <summary>
    /// The server judgement: probability at least 90 <b>and</b> at least 20 above the next best candidate.
    /// With exactly one candidate the distance clause falls away and the 90 alone decides.
    /// </summary>
    public bool Confident { get; init; }
}

/// <summary>
/// One stored row offered as an answer to a held newcomer. Only rows whose job is compatible appear here,
/// so the offer set and the rule are the same thing: an incompatible row is not a weak candidate, it is
/// not a candidate at all.
/// </summary>
public sealed class ReviewCandidate
{
    /// <summary>The stored row identity.</summary>
    public string? SetUid { get; init; }

    /// <summary>Its job code.</summary>
    public string? Job { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Whether it came from a push or from the web editor. This is what turns an ordinary link into an
    /// adoption in the dialog, and it is the field to label a row by, never the absence of a state.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>How likely the server thinks this pairing is, 0 to 100.</summary>
    public int Probability { get; init; }

    /// <summary>Slots where both sets carry the same item.</summary>
    public int MatchedSlots { get; init; }

    /// <summary>
    /// Slots occupied in either set. Never a constant: a crafter set that fills both hands counts both, an
    /// incomplete row counts what it has, so the denominator always came from the data in front of you.
    /// </summary>
    public int TotalSlots { get; init; }

    /// <summary>Whether the jobs are literally the same, which reads differently from GLA to PLD.</summary>
    public bool SameJob { get; init; }

    /// <summary>
    /// What the row is: parked, ignored, or <see langword="null"/> for a hand-made row nobody put aside.
    /// Never active and never held, since both belong to a live set already and a claimed row is not
    /// offered.
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Whether the server proposes this candidate for this newcomer. <b>Preselect by this, never by
    /// <see cref="Probability"/>:</b> the server assigns each row at most once across the whole mapping, so
    /// the pairing it proposes is not always the highest scoring one, and preselecting by score names a
    /// pair it never proposed and earns a 409 for a player who did nothing wrong.
    /// </summary>
    public bool Proposed { get; init; }

    /// <summary>
    /// The row that took this candidate away when a higher score lost to the mapping, so the honest
    /// sentence can be concrete rather than vague.
    /// </summary>
    public string? BlockedBy { get; init; }

    /// <summary>Whether a BiS target is pinned on it, which is worth naming before it is answered away.</summary>
    public bool HasPin { get; init; }

    /// <summary>Whether other people are following this row.</summary>
    public bool HasTeamShare { get; init; }

    /// <summary>Who they are, so a warning can name them.</summary>
    public List<string> TeamNames { get; init; } = [];

    /// <summary>
    /// Whether its owner hid it on the website. The one field here that says nothing about matching: it is
    /// for explaining a set that exists in game and cannot be found on the site.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>Its gear, by slot. Ids only; names get resolved in the player language.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>
    /// When a push last claimed this row, in server time, or <see langword="null"/> when none ever did.
    /// Null on a row the web editor made, which was never in game and therefore has nothing to have seen.
    /// </summary>
    public string? LastSeenAt { get; init; }

    /// <summary>
    /// When this row left plugin governance, in server time, or <see langword="null"/> when it never did.
    /// </summary>
    /// <remarks>
    /// <b>Unlike <see cref="LastSeenAt"/>, the absence here is a statement.</b> Null means this row was not
    /// released, which is true of every hand-made row and of every row a push governs, so reading it is not
    /// the inference from an absence the other field forbids.
    /// </remarks>
    public string? ReleasedAt { get; init; }

    /// <summary>Where to see it. Read back, never composed.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// A gearset that was written and marked, waiting for somebody to say which stored row it is. Its gear is
/// current rather than week-old: a player who leaves the question for a week has fresh values and a marker.
/// </summary>
public sealed class HeldGearset
{
    /// <summary>The uid the server minted for it immediately.</summary>
    public string? SetUid { get; init; }

    /// <summary>Its job code.</summary>
    public string? Job { get; init; }

    /// <summary>Its name in game.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The position the server stored last, which is not necessarily where the set sits right now. A client
    /// holding the live list shows the lived position and treats this as a value it happens to know.
    /// </summary>
    public int GearIndex { get; init; }

    /// <summary>Its gear, by slot.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>What the server proposes, which is what a dialog preselects.</summary>
    public ReviewProposal? Proposal { get; init; }

    /// <summary>
    /// Every unclaimed compatible row, never empty: a newcomer is only held while such a row exists, and
    /// one with none is new and asks nothing at all.
    /// </summary>
    public List<ReviewCandidate> Candidates { get; init; } = [];

    /// <summary>Where to see it. Read back, never composed.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// A row that is still there and that an orphan resembles, as <c>orphans[].similar[]</c> delivers it.
/// </summary>
/// <remarks>
/// <para>
/// The question about a row the game stopped reporting is not "what was this" but "do I still need it",
/// and the answer is usually standing in another row. A set that shares every slot with one that is still
/// there is that one's copy, and delete-or-keep is answered without anybody remembering a name from
/// months ago.
/// </para>
/// <para>
/// It carries fewer fields than a <see cref="ReviewCandidate"/> and that is deliberate: this is not an
/// answer to a question, it is a comparison. There is no <c>proposed</c> on it, nothing here is being
/// attributed, and the target may itself be a row that is still under question.
/// </para>
/// <para>
/// What it does carry, since 2026-09-12, is what the row <b>is</b>. Without that the card made its case
/// the same way whether the set it pointed at was standing in the list or was itself long gone, and a
/// resemblance between two dead rows is not an argument for deleting either.
/// </para>
/// </remarks>
public sealed class SimilarSet
{
    /// <summary>The row it resembles.</summary>
    public string? SetUid { get; init; }

    /// <summary>Its job code.</summary>
    public string? Job { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>Whether it came from a push or from the web editor.</summary>
    public string? Source { get; init; }

    /// <summary>How much of the pair the server counts as shared, 0 to 100. Shown, never recomputed.</summary>
    public int Probability { get; init; }

    /// <summary>Slots where both carry the same item id. Note: the item, not the materia.</summary>
    public int MatchedSlots { get; init; }

    /// <summary>Slots occupied on either side, which is the denominator of the pair and not a constant.</summary>
    public int TotalSlots { get; init; }

    /// <summary>Its gear. The number says how much two sets share; only the pieces say where they differ.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>
    /// What the row is: one of <see cref="RowState"/>, or <see langword="null"/> on a hand-made row.
    /// </summary>
    /// <remarks>
    /// The field that decides how the whole comparison reads. <c>active</c> and <c>held</c> belong to a
    /// gearset the player can look at; <c>parked</c> and <c>ignored</c> do not, and a card that points at
    /// one of those is comparing an orphan to another orphan.
    /// </remarks>
    public string? State { get; init; }

    /// <summary>
    /// Where the server last recorded it, or <see langword="null"/> when it has no place in the list.
    /// </summary>
    /// <remarks>
    /// Null for <c>parked</c> and for a hand-made row, which is a statement rather than a gap. On a row
    /// that is in game this is a value the client happens to know: the position the player sees comes from
    /// the live list, and this one is only worth showing where the live list cannot answer.
    /// </remarks>
    public int? GearIndex { get; init; }

    /// <summary>
    /// When a push last claimed it, in server time, or <see langword="null"/> when none ever did.
    /// </summary>
    /// <remarks>
    /// Evidence, the same way it is on a candidate: a row a sync reported this morning and one nobody has
    /// seen since spring are not equally good reasons to delete the set being asked about.
    /// </remarks>
    public string? LastSeenAt { get; init; }

    /// <summary>Where to see it.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// A row no live gearset occupies and that somebody may still have to act on: a plugin row a sync stopped
/// reporting, or a row of either kind that was put aside.
/// </summary>
/// <remarks>
/// A hand-made row nobody put aside is <b>not</b> one of these. There is nothing to act on, and it is
/// offered wherever it is relevant anyway.
/// </remarks>
public sealed class OrphanRow
{
    /// <summary>The stored row identity.</summary>
    public string? SetUid { get; init; }

    /// <summary>Its job code.</summary>
    public string? Job { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>Parked, or ignored. Read the state, never the index band.</summary>
    public string? State { get; init; }

    /// <summary>
    /// Which kind of row it is, which decides what may be offered on it: delete is rejected on a hand-made
    /// one, and release does not apply to one that is already hand-made.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// When a push last reported it, or <see langword="null"/> when none ever did. Null on a hand-made row
    /// that was never in game; a <i>released</i> row keeps the timestamp it had, because the game did
    /// report it and when it last did is a fact.
    /// </summary>
    public string? LastSeenAt { get; init; }

    /// <summary>
    /// When this row left plugin governance, in server time, or <see langword="null"/> when it never did.
    /// A released row is <c>manual</c> like a hand-made one; this is what tells the two apart.
    /// </summary>
    /// <remarks>
    /// <b>Unlike <see cref="LastSeenAt"/>, the absence here is a statement.</b> Null means the row was not
    /// released. It is also what decides whether <see cref="ReviewAction.Reopen"/> is a way back at all: on
    /// a released row it undoes the release outright, and on any other put-aside row it does not.
    /// </remarks>
    public string? ReleasedAt { get; init; }

    /// <summary>Whether this row was handed to the website rather than built there.</summary>
    public bool WasReleased => ReleasedAt is { Length: > 0 };

    /// <summary>Whether a BiS target is pinned on it.</summary>
    public bool HasPin { get; init; }

    /// <summary>Whether other people are following it.</summary>
    public bool HasTeamShare { get; init; }

    /// <summary>Who they are.</summary>
    public List<string> TeamNames { get; init; } = [];

    /// <summary>Whether its owner hid it on the website, which changes what deleting it means.</summary>
    public bool Hidden { get; init; }

    /// <summary>Its gear. Seeing what is in a row is what makes delete-or-keep answerable.</summary>
    public Dictionary<string, ItemDto> Items { get; init; } = [];

    /// <summary>
    /// Which of the sets that are still there this row looks like, strongest first, at most three, and
    /// only pairs sharing at least one slot. Empty when nothing resembles it, which is itself an answer.
    /// </summary>
    public List<SimilarSet> Similar { get; init; } = [];

    /// <summary>Where to see it.</summary>
    public string? Url { get; init; }

    /// <summary>Whether this row came from a push rather than from the web editor.</summary>
    public bool IsFromPlugin => string.Equals(Source, GearsetSource.Plugin, StringComparison.Ordinal);

    /// <summary>Whether somebody has already put this row aside.</summary>
    public bool IsPutAside => string.Equals(State, RowState.Ignored, StringComparison.Ordinal);
}

/// <summary>
/// The open questions and the inventory for one character, as <c>GET /gear/review</c> returns them and as
/// every decision answer repeats them.
/// </summary>
public class ReviewState
{
    /// <summary>
    /// The fingerprint these lists describe. Always present, including when both lists are empty, so a
    /// client can tell "nothing is open" from "I have no token".
    /// </summary>
    public string? StateToken { get; init; }

    /// <summary>The gearsets waiting for an attribution decision, most likely first.</summary>
    public List<HeldGearset> Held { get; init; } = [];

    /// <summary>The inventory of rows no live gearset occupies.</summary>
    public List<OrphanRow> Orphans { get; init; } = [];

    /// <summary>
    /// The rows that moved under the caller, on a stale-token answer. Empty on a normal one.
    /// </summary>
    /// <remarks>
    /// A conflict is not a separate shape: the same body comes back with these named, so there is one
    /// parser and one renderer, and the sentence a player reads rests on what the server knows rather than
    /// on a diff computed here. Non-empty means <b>nothing was applied</b> and the state alongside it is
    /// the one to render, not the one that was being held.
    /// </remarks>
    public List<string> Conflicts { get; init; } = [];

    /// <summary>Whether this answer is a refusal because the state moved.</summary>
    public bool IsConflict => Conflicts.Count > 0;

    /// <summary>Orphans nobody has put aside, which is what a window offers first.</summary>
    public IEnumerable<OrphanRow> OpenOrphans => Orphans.Where(o => !o.IsPutAside);

    /// <summary>Orphans already put aside, foldable rather than hidden.</summary>
    public IEnumerable<OrphanRow> PutAsideOrphans => Orphans.Where(o => o.IsPutAside);
}

/// <summary>What one decision did.</summary>
public sealed class ReviewResult
{
    /// <summary>The row the decision named.</summary>
    public string? SetUid { get; init; }

    /// <summary>
    /// The action that was <b>performed</b>. It differs from <see cref="Requested"/> in exactly one place
    /// today, a delete on a shared plugin row, and the window shows this one: saying "deleted" about a row
    /// that is still there would be a lie in the direction that matters.
    /// </summary>
    public string? Action { get; init; }

    /// <summary>What was asked for.</summary>
    public string? Requested { get; init; }

    /// <summary>
    /// Which uid survived. After a link it is the <i>target</i> row, which carries the pin, the share and
    /// the history; the held row uid ceases to exist and belongs out of the cache.
    /// </summary>
    public string? SurvivingUid { get; init; }

    /// <summary>
    /// What the row is afterwards, <see langword="null"/> included: a reopen on a hand-made row leaves it
    /// in no state at all. Modelled as non-nullable, that one call breaks.
    /// </summary>
    public string? State { get; init; }

    /// <summary>Whether the server did something other than what was asked.</summary>
    public bool WasConverted =>
        Requested is not null && Action is not null && !string.Equals(Action, Requested, StringComparison.Ordinal);
}

/// <summary>
/// The answer to one decision: what it did, what it settled as a side effect, and the fresh state.
/// </summary>
public sealed class ReviewDecisionResponse : ReviewState
{
    /// <summary>What the decision itself did.</summary>
    public ReviewResult? Result { get; init; }

    /// <summary>
    /// Held rows that stopped being held <b>as a consequence</b>, so a window can say "and one more
    /// question settled itself" instead of quietly showing one fewer.
    /// </summary>
    /// <remarks>
    /// Always present, empty when nothing did, and it only ever names rows that <i>settled</i>. Since a
    /// held row can also leave the list because its set vanished from the game, a client diffing two lists
    /// could not tell the two apart and would report that a question settled itself over a set the player
    /// deleted a minute ago. They cannot arrive in the same answer, because a park moves the fingerprint
    /// and the decision would be a 409.
    /// </remarks>
    public List<string> AlsoResolved { get; init; } = [];
}

/// <summary>The answer to a whole mapping accepted at once.</summary>
public sealed class ReviewAcceptResponse : ReviewState
{
    /// <summary>One entry per pair, in the same shape as a single decision. Correlate by uid, not position.</summary>
    public List<ReviewResult> Results { get; init; } = [];

    /// <summary>Held rows that settled as a consequence.</summary>
    public List<string> AlsoResolved { get; init; } = [];
}


/// <summary>One decision, as the request body carries it.</summary>
public sealed class ReviewDecision
{
    /// <summary>The row being decided about, which must be one that is open to a decision.</summary>
    public required string SetUid { get; init; }

    /// <summary>The verb.</summary>
    public required string Action { get; init; }

    /// <summary>
    /// The row a link points at, read out of the candidate list. Null for every other verb. This is a
    /// different question from which row the decision is <i>about</i>.
    /// </summary>
    public string? TargetUid { get; init; }
}
