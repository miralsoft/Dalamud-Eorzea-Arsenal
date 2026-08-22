using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

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
    /// Whether a verb takes something away that this endpoint cannot give back, so a window has to ask
    /// twice.
    /// </summary>
    /// <param name="action">The verb.</param>
    /// <returns><see langword="true"/> for a delete or a release.</returns>
    /// <remarks>
    /// A delete removes the row and what hangs on it. A release hands the row to the web editor and freezes
    /// it for everybody who was following it. Neither has a way back through the deciding endpoint, and the
    /// contract asks for the warning to come <i>before</i> the click rather than as a label beside it.
    /// A link is irreversible too, but only onto a hand-made row, where <see cref="IsAdoption"/> says so.
    /// </remarks>
    public static bool IsIrreversible(string? action) =>
        string.Equals(action, ReviewAction.Delete, StringComparison.Ordinal) ||
        string.Equals(action, ReviewAction.Release, StringComparison.Ordinal);

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
