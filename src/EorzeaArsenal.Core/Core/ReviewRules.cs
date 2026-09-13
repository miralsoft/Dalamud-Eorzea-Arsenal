using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;


/// <summary>
/// Where a row in the inventory came from, as far as the data actually says. The card turns this into a
/// sentence, and the point of naming the third case is that it stays a third case: the plugin printed
/// "never in game" whenever a timestamp was missing, which built a claim out of an absence and told
/// eight players' worth of rows the opposite of the truth.
/// </summary>
public enum OrphanOrigin
{
    /// <summary>Made in the web editor. It never existed in game, and that is a fact, not an inference.</summary>
    MadeOnSite,

    /// <summary>
    /// It came from the game and was handed to the website. Told apart from a hand-made row by
    /// <see cref="OrphanRow.ReleasedAt"/> and by nothing else: both are <c>manual</c>, and until the mark
    /// existed the two were one case, which cost a row the only button that could bring it back.
    /// </summary>
    Released,

    /// <summary>A push reported it, and the server says when. The date is what turns a mystery into a memory.</summary>
    LastReported,

    /// <summary>It came from a push and no date is known. Say that, rather than inventing one end of it.</summary>
    Unknown,
}

/// <summary>
/// One thing a verb does to a row that cannot be taken back, named rather than phrased. The window turns
/// each into a sentence; keeping the two apart is what lets the rule be tested.
/// </summary>
public enum ReviewConsequence
{
    /// <summary>
    /// The delete is carried out as a release, because deleting would remove the set for other people on
    /// one person's decision. The row survives, and the window has to say so before the word "delete"
    /// promises otherwise.
    /// </summary>
    DeleteBecomesRelease,

    /// <summary>The teams following this row go on seeing it, frozen as it is now.</summary>
    TeamKeepsSeeingIt,

    /// <summary>The row survives, so the target pinned to it survives with it.</summary>
    KeepsItsPin,

    /// <summary>The row goes, and the target pinned to it goes with it.</summary>
    LosesItsPin,

    /// <summary>
    /// The set still exists in game, so a later sync writes a new row for it. What does not come back is
    /// the pinned target, because that hung on the row rather than on the set.
    /// </summary>
    ComesBackWithoutItsPin,

    /// <summary>
    /// What a delete actually does: the stored row goes, and the gearset in game is not touched.
    /// </summary>
    /// <remarks>
    /// Obvious to whoever wrote it and not to whoever reads it. On a row with no pin and no share this
    /// used to leave the confirmation with a single line, about a BiS target the row did not have, and
    /// nothing at all about the row being removed. Under a button labelled "delete", "does this take my
    /// gearset with it" is the question somebody actually has.
    /// </remarks>
    RowIsRemoved,

    /// <summary>
    /// What a release does: the row stays and stops being the plugin's. Nothing in game is touched, and
    /// nothing in game reaches it any more.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="RowIsRemoved"/> and missing for the same reason. On a row with a team
    /// the confirmation said only what the team would keep seeing; on a row with neither team nor pin it
    /// said nothing at all and fell back to repeating the button label. Both left out the one thing the
    /// press actually does.
    /// </remarks>
    LeavesPluginGovernance,
}

/// <summary>
/// What a window may offer, and what it must preselect. Pure decisions over one answer, kept apart from
/// the service that fetches so they can be read and tested on their own.
/// </summary>
/// <remarks>
/// Every rule here exists because the server would otherwise have to refuse something a player was
/// offered. An offer the server rejects is worse than a missing offer: the player did nothing wrong and
/// gets an error for it.
/// </remarks>
public static class ReviewRules
{


    /// <summary>
    /// The rows the player has not been shown yet: open questions and open inventory rows whose identity
    /// is not in the given set.
    /// </summary>
    /// <param name="state">The reconciliation state as last read.</param>
    /// <param name="alreadyShown">Identities already announced once. Pass a set, this is asked per row.</param>
    /// <returns>The new identities, questions first, in the order the server listed them.</returns>
    /// <remarks>
    /// Keyed on identity and never on a count. One row deleted and another appearing leaves the count
    /// where it was, and a count based rule would stay silent exactly when it should speak. A row put
    /// aside is not new either: it was shown once and decided, and announcing it again is how a marker
    /// becomes something people learn to ignore.
    /// </remarks>
    public static IReadOnlyList<string> Unannounced(ReviewState state, ICollection<string> alreadyShown)
    {
        var fresh = new List<string>();

        foreach (var held in state.Held)
        {
            Add(held.SetUid);
        }

        foreach (var row in state.OpenOrphans)
        {
            Add(row.SetUid);
        }

        return fresh;

        void Add(string? uid)
        {
            if (uid is { Length: > 0 } && !alreadyShown.Contains(uid) && !fresh.Contains(uid))
            {
                fresh.Add(uid);
            }
        }
    }
    /// <summary>The same question about a candidate, which carries the same two marks.</summary>
    /// <param name="candidate">The row being offered as an answer.</param>
    /// <returns>The origin, never inferred beyond what the fields carry.</returns>
    /// <remarks>
    /// Worth saying on a candidate for a different reason than on an orphan. There it explains why a row is
    /// in the list; here it is evidence: a row a push reported an hour ago and one nobody has seen since
    /// spring are not equally likely to be the set that just arrived, and the date is the only thing on the
    /// card that says which is which.
    /// </remarks>
    public static OrphanOrigin OriginOf(ReviewCandidate candidate) =>
        candidate.ReleasedAt is { Length: > 0 } ? OrphanOrigin.Released
        : !string.Equals(candidate.Source, GearsetSource.Plugin, StringComparison.Ordinal) ? OrphanOrigin.MadeOnSite
        : candidate.LastSeenAt is { Length: > 0 } ? OrphanOrigin.LastReported
        : OrphanOrigin.Unknown;

    /// <summary>The same question about a row an orphan resembles.</summary>
    /// <param name="similar">The row it resembles.</param>
    /// <returns>The origin, never inferred beyond what the fields carry.</returns>
    /// <remarks>
    /// One case short of the other two: nothing here can be <see cref="OrphanOrigin.Released"/>, because
    /// <see cref="SimilarSet"/> carries no release mark. A released row still reaches this list, and it
    /// reads as made on the site, which is where it now lives.
    /// </remarks>
    public static OrphanOrigin OriginOf(SimilarSet similar) =>
        !string.Equals(similar.Source, GearsetSource.Plugin, StringComparison.Ordinal) ? OrphanOrigin.MadeOnSite
        : similar.LastSeenAt is { Length: > 0 } ? OrphanOrigin.LastReported
        : OrphanOrigin.Unknown;

    /// <summary>What the data says about where an inventory row came from.</summary>
    /// <param name="row">The inventory row.</param>
    /// <returns>The origin, never inferred beyond what the fields carry.</returns>
    public static OrphanOrigin OriginOf(OrphanRow row) =>
        row.WasReleased ? OrphanOrigin.Released
        : !row.IsFromPlugin ? OrphanOrigin.MadeOnSite
        : row.LastSeenAt is { Length: > 0 } ? OrphanOrigin.LastReported
        : OrphanOrigin.Unknown;
    /// <summary>
    /// The inventory verbs that apply to one orphan row, in the order a window should show them.
    /// </summary>
    /// <param name="row">The orphan.</param>
    /// <returns>The verbs that may be offered. Never empty.</returns>
    /// <remarks>
    /// <para>
    /// On a hand-made row that was put aside there is exactly <b>one</b>: reopen. Delete is rejected on
    /// hand-made rows, release does not apply because it is already hand-made, and ignore is what put it
    /// there. So a window draws one button, not one live beside three dead ones.
    /// </para>
    /// <para>
    /// Attribution verbs never appear here. A link or a new answers a question about a set that exists;
    /// this is an inventory.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> OfferedVerbs(OrphanRow row)
    {
        if (!row.IsFromPlugin)
        {
            // Hand-made. It only appears in this list at all once somebody put it aside, so the way back
            // is the only thing left to offer.
            return row.IsPutAside ? [ReviewAction.Reopen] : [ReviewAction.Ignore];
        }

        // A row a team follows offers no delete either, for the same reason a hand-made one does not: the
        // server will not carry it out. It converts rather than refusing, so the press would work and do
        // something else, which is worse than a refusal, and the button that does that something else is
        // already on the card with the right word on it. Two buttons doing one thing, one of them lying
        // about it, is not a choice.
        //
        // Not a refusal by the back door: the contract rejected refusing because it left the player
        // un-sharing on the website first to get anywhere. Nothing here blocks anybody, the path is one
        // button to the left, and the card says why this one is missing.
        if (row.HasTeamShare)
        {
            return row.IsPutAside
                ? [ReviewAction.Reopen, ReviewAction.Release]
                : [ReviewAction.Ignore, ReviewAction.Release];
        }

        return row.IsPutAside
            ? [ReviewAction.Reopen, ReviewAction.Release, ReviewAction.Delete]
            : [ReviewAction.Ignore, ReviewAction.Release, ReviewAction.Delete];
    }

    /// <summary>
    /// The candidate a dialog should have selected when it opens, or <see langword="null"/> when the answer
    /// to preselect is "it is new".
    /// </summary>
    /// <param name="held">The question.</param>
    /// <returns>The proposed candidate, if the mapping proposes one.</returns>
    /// <remarks>
    /// By <see cref="ReviewCandidate.Proposed"/>, never by <see cref="ReviewCandidate.Probability"/>. The
    /// server assigns each row at most once across the whole mapping, so the pairing it proposes for one
    /// newcomer is not always that newcomer highest scoring candidate: two newcomers can fit row X best,
    /// X goes to one of them, the other gets Y. Both fields come out of one computation on one snapshot and
    /// are serialised together, so they cannot drift apart — but they can disagree, and the proposal is the
    /// one the accept call will accept.
    /// </remarks>
    public static ReviewCandidate? Preselected(HeldGearset held)
    {
        foreach (var candidate in held.Candidates)
        {
            if (candidate.Proposed)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this question is one a player can be shown without a second thought, because the server is
    /// sure enough to have preselected it.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <returns><see langword="true"/> when the proposal is confident.</returns>
    public static bool IsConfident(HeldGearset held) => held.Proposal?.Confident == true;

    /// <summary>
    /// Whether a link onto this candidate changes who governs the row from here on, which is a one-way door
    /// and has to be said in the dialog rather than discovered afterwards.
    /// </summary>
    /// <param name="candidate">The candidate a link would name.</param>
    /// <returns><see langword="true"/> for a hand-made row.</returns>
    public static bool IsAdoption(ReviewCandidate candidate) =>
        !string.Equals(candidate.Source, GearsetSource.Plugin, StringComparison.Ordinal);

    /// <summary>
    /// The inventory rows a window should offer, which is the open orphans minus the ones an unanswered
    /// question is already about.
    /// </summary>
    /// <param name="state">The reconciliation state as last read.</param>
    /// <returns>The rows to draw as cards, in the order the server sent them.</returns>
    /// <remarks>
    /// <para>
    /// A parked row is genuinely both things at once: an unclaimed compatible row, so a candidate, and a
    /// row no gearset occupies, so an orphan. The server is right to list it twice and the window was wrong
    /// to draw it twice: the same set appeared under the question with a full comparison and again below
    /// with another, and nothing said they were one row.
    /// </para>
    /// <para>
    /// The reason this is a rule and not a tidy-up: the inventory card offers <b>delete</b>. Delete the row
    /// there and then answer the question above with "that is the one", and the answer names a target that
    /// no longer exists. The window would have walked somebody into destroying the row it was about to
    /// reunite with its set, and on the observed case that row carried a pinned target.
    /// </para>
    /// <para>
    /// Hidden only while the question is open. Answer it and the row either becomes the set again through a
    /// <c>link</c>, or stays behind after a <c>new</c> and appears here on the next read, where it belongs.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<OrphanRow> InventoryToOffer(ReviewState state)
    {
        var asked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var held in state.Held)
        {
            foreach (var candidate in held.Candidates)
            {
                if (candidate.SetUid is { Length: > 0 } uid)
                {
                    asked.Add(uid);
                }
            }
        }

        return [.. state.OpenOrphans.Where(o => o.SetUid is not { Length: > 0 } uid || !asked.Contains(uid))];
    }

    /// <summary>
    /// What a verb about to be applied actually costs this row, as facts rather than as sentences, so the
    /// rule can be read and tested apart from the words that carry it.
    /// </summary>
    /// <param name="verb">The verb about to be applied.</param>
    /// <param name="row">The row.</param>
    /// <returns>The consequences, in the order they matter. Empty where the verb takes nothing away.</returns>
    /// <remarks>
    /// <para>
    /// This exists because the sentence in front of the one irreversible click was wrong. A delete
    /// confirmation said "keeps its pinned BiS set", which is true of a candidate somebody answers away
    /// with <c>new</c> and is the exact opposite of what a delete does. The reassuring half of a rule got
    /// reused where only the costly half applied, and the wrong half of it was the last thing anybody read
    /// before the row was gone.
    /// </para>
    /// <para>
    /// The distinction the old code missed: <b>a delete on a row with an active team share is performed as
    /// a release.</b> The row survives, so its pin survives with it, and there "keeps its pin" is right
    /// after all. Only a delete that really deletes takes the pin, which is why the two cases cannot share
    /// one sentence.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ReviewConsequence> ConsequencesOf(string? verb, OrphanRow row)
    {
        var consequences = new List<ReviewConsequence>();
        if (!NeedsConfirming(verb))
        {
            return consequences;
        }

        var deleting = string.Equals(verb, ReviewAction.Delete, StringComparison.Ordinal);
        var releasing = string.Equals(verb, ReviewAction.Release, StringComparison.Ordinal);
        var shared = row.HasTeamShare && row.TeamNames.Count > 0;

        // The order is the order somebody reads a confirmation in: what am I agreeing to, then what falls
        // out of it. It used to open with the team line, on the argument that the part reaching somebody
        // outside the room comes first. That argument belongs to a delete, where the word on the button is
        // wrong and has to be corrected before anything else; everywhere else it put the result above the
        // deed and left the reader to work backwards to what they had actually pressed.

        // First, and only here: the button says delete and the server will not delete. Nothing else in
        // the list means anything until that is out of the way.
        if (deleting && shared)
        {
            consequences.Add(ReviewConsequence.DeleteBecomesRelease);
        }

        // Then the deed itself, in one of its two forms. Whichever applies, it is the sentence the reader
        // came for, and it was missing from both: with no pin and no share a delete confirmation said
        // nothing about the row going away, and a release said nothing at all and fell back to repeating
        // the button label.
        if (deleting && !shared)
        {
            consequences.Add(ReviewConsequence.RowIsRemoved);
        }
        else if (releasing || deleting)
        {
            consequences.Add(ReviewConsequence.LeavesPluginGovernance);
        }

        // Then what it costs, starting with the part that reaches other people.
        if (shared)
        {
            consequences.Add(ReviewConsequence.TeamKeepsSeeingIt);
        }

        if (row.HasPin)
        {
            // Survives wherever the row survives, and only there.
            consequences.Add(deleting && !shared
                ? ReviewConsequence.LosesItsPin
                : ReviewConsequence.KeepsItsPin);
        }

        // Only where it is true: a row that was put aside was not being reported anyway, a hand-made one
        // cannot come back at all, and without a pin there is nothing for it to come back without. That
        // last condition was missing, so a row with no pin was warned about losing one.
        if (deleting && !shared && row.IsFromPlugin && !row.IsPutAside && row.HasPin)
        {
            consequences.Add(ReviewConsequence.ComesBackWithoutItsPin);
        }

        return consequences;
    }

    /// <summary>
    /// Whether a verb changes enough that a window should ask twice before applying it.
    /// </summary>
    /// <param name="action">The verb.</param>
    /// <returns><see langword="true"/> for a delete, a release or a link.</returns>
    /// <remarks>
    /// Wider than <see cref="IsIrreversible"/> on purpose. A release can be taken back, and it still moves
    /// a row out of the plugin's hands and freezes it for everybody following it; a link overwrites the
    /// target's contents whatever kind of row it is. Both are worth a sentence and a second click. What
    /// separates them is only what the sentence above may claim.
    /// </remarks>
    public static bool NeedsConfirming(string? action) =>
        string.Equals(action, ReviewAction.Delete, StringComparison.Ordinal) ||
        string.Equals(action, ReviewAction.Release, StringComparison.Ordinal) ||
        string.Equals(action, ReviewAction.Link, StringComparison.Ordinal);

    /// <summary>
    /// Whether a verb genuinely cannot be taken back, anywhere, so a dialog may say so.
    /// </summary>
    /// <param name="action">The verb.</param>
    /// <returns><see langword="true"/> for a delete, and for nothing else.</returns>
    /// <remarks>
    /// <para>
    /// One verb. A delete removes the row and everything hanging on it: the pinned target goes with it,
    /// and nothing anywhere brings the row back.
    /// </para>
    /// <para>
    /// <b>A release left this list</b> the day <c>released_at</c> arrived, because <c>reopen</c> on a
    /// released row puts it back to a plugin row, parked, with the mark cleared.
    /// </para>
    /// <para>
    /// <b>A link left it later, and for a different kind of reason.</b> A link is one-way as a verb: there
    /// is no un-link, the newcomer's row is gone and the target now carries the gear from the game. But the
    /// target itself survives with its uid, its pin and its shares, and what it used to hold is a set the
    /// person can build again in the web editor. "This cannot be undone" over that is a warning spending
    /// credit it did not earn, and every sentence like it teaches the reader to skim the next one. The verb
    /// still asks twice: see <see cref="NeedsConfirming"/>, which is the wider list and the right place for
    /// "look at this before you press it".
    /// </para>
    /// </remarks>
    public static bool IsIrreversible(string? action) =>
        string.Equals(action, ReviewAction.Delete, StringComparison.Ordinal);

    /// <summary>
    /// Whether answering this question away leaves something behind that is worth a sentence: a pin, or
    /// other people following the row.
    /// </summary>
    /// <param name="candidate">The candidate about to be answered away.</param>
    /// <returns><see langword="true"/> when there is something to name.</returns>
    /// <remarks>
    /// Only where there is something to say. A dialog that appears every time teaches people to click it
    /// away, including the times it matters.
    /// </remarks>
    public static bool LeavesSomethingBehind(ReviewCandidate candidate) =>
        candidate.HasPin || candidate.HasTeamShare;

    /// <summary>
    /// The mapping a bulk accept should send: every proposal the server composed, minus what the player
    /// struck out.
    /// </summary>
    /// <param name="state">The state the mapping was read from.</param>
    /// <param name="struckOut">The held rows the player took out, by uid.</param>
    /// <returns>The pairs to send, in the order the questions arrived.</returns>
    /// <remarks>
    /// <para>
    /// Composed from <see cref="HeldGearset.Proposal"/> and nothing else. The player may strike a pair out
    /// and may not add or re-point one: a pair that was in no proposal is refused, so offering one would
    /// put a decision in front of somebody that the server then has to reject.
    /// </para>
    /// <para>
    /// Striking a pair out is not answering it. It stays a question, and the player answers it on its own
    /// afterwards — which is also the only sequence that works: a correction has to go BEFORE the mapping,
    /// because a single decision mints a new token and makes the mapping in hand stale.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ReviewDecision> MappingToAccept(
        ReviewState state,
        IReadOnlySet<string>? struckOut = null)
    {
        var pairs = new List<ReviewDecision>(state.Held.Count);
        foreach (var held in state.Held)
        {
            if (held.SetUid is not { Length: > 0 } uid || held.Proposal?.Action is not { Length: > 0 } action)
            {
                continue;
            }

            if (struckOut is not null && struckOut.Contains(uid))
            {
                continue;
            }

            // The safety rule is applied here and not by whoever draws the list. It used to live in the
            // window, which computed the excluded set for the display and then handed the request builder
            // only the player's own strikes: the panel greyed a pairing out, said in words that it would
            // not be applied, listed two pairs in the confirmation, and sent three. A rule that decides
            // what is sent has to sit where the request is composed, or the screen and the wire are two
            // opinions that agree only by accident.
            if (!QuestionAdvisor.SafeForBulk(held))
            {
                continue;
            }

            if (!ReviewAction.IsAttribution(action))
            {
                // The bulk call takes attribution only. A proposal is never anything else, so this is a
                // guard against a future verb arriving in a field we read, not a case that happens today.
                continue;
            }

            pairs.Add(new ReviewDecision
            {
                SetUid = uid,
                Action = action,
                TargetUid = held.Proposal.TargetUid,
            });
        }

        return pairs;
    }

    /// <summary>
    /// How many adoptions a mapping contains, so the dialog can name the number: each one is a row that
    /// starts following the game from then on, and that is a one-way door per row.
    /// </summary>
    /// <param name="state">The state the mapping was read from.</param>
    /// <param name="pairs">The pairs about to be sent.</param>
    /// <returns>The count of pairs that link onto a hand-made row.</returns>
    public static int AdoptionCount(ReviewState state, IReadOnlyList<ReviewDecision> pairs)
    {
        // Which offered rows are hand-made, gathered once. One row may be a candidate under any number of
        // questions — the contract says so outright — so walking the candidate lists per pair and counting
        // every appearance would say "three" about one adoption, in front of a press that cannot be undone.
        var handMade = new HashSet<string>(StringComparer.Ordinal);
        foreach (var held in state.Held)
        {
            foreach (var candidate in held.Candidates)
            {
                if (candidate.SetUid is { Length: > 0 } uid && IsAdoption(candidate))
                {
                    handMade.Add(uid);
                }
            }
        }

        var adoptions = 0;
        foreach (var pair in pairs)
        {
            if (string.Equals(pair.Action, ReviewAction.Link, StringComparison.Ordinal) &&
                pair.TargetUid is { Length: > 0 } target &&
                handMade.Contains(target))
            {
                adoptions++;
            }
        }

        return adoptions;
    }
}
