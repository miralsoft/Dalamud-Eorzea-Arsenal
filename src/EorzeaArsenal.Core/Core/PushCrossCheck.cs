using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Reads a push answer against what this side remembered before it, and names the identities the two
/// accounts disagree about.
/// </summary>
/// <remarks>
/// <para>
/// The server owns identity and this does not overrule it. It exists because the server has one blind
/// spot this side does not. Its <c>name_ambiguous</c> rung means it searched by name, found more than one
/// candidate and broke the tie by position, and its <c>index</c> rung means name and gear both failed and
/// only the position was left. Both are guesses on the position, and the position is precisely what a
/// reorder just changed, so they are least reliable in exactly the situation this whole feature exists
/// for. The server cannot check itself there: from where it stands the two candidates are the same.
/// </para>
/// <para>
/// This side remembers something the server does not have: which gear sat under which identity at the
/// last push. Where a gearset's contents used to identify one uid on their own and the server has just
/// put them under a different one, the two accounts contradict each other. Neither is provably right, and
/// that is the point: an identity two accounts disagree about is one the interface must stop making
/// claims about, the same way it already stops when two remembered rows fit one live set.
/// </para>
/// </remarks>
public static class PushCrossCheck
{
    /// <summary>
    /// Finds the identities the push answer and the previous cache disagree about.
    /// </summary>
    /// <param name="previous">The cached rows as they stood before this push.</param>
    /// <param name="sent">The gearsets that were pushed, in the order they were sent.</param>
    /// <param name="assignments">The server's answer, index-aligned with <paramref name="sent"/>.</param>
    /// <returns>
    /// Every uid on either side of a disagreement, both the one the server named and the one this side
    /// remembered. Empty when the two accounts agree, which is the ordinary outcome.
    /// </returns>
    /// <remarks>
    /// Four conditions have to hold together, and each one removes a way of being wrong.
    /// <list type="number">
    /// <item>
    /// The server either guessed or declared the set new. Where it matched on the name or on the gear it
    /// had evidence of its own, and a disagreement there is far more likely to be a re-gear that made one
    /// set look like another used to than a mis-attribution.
    /// </item>
    /// <item>
    /// The gear identified exactly one remembered row. A fingerprint two rows already shared was never a
    /// distinction, so it cannot witness against anything now. This is why a pair of true copies stays
    /// silent: it has nothing to say, and saying it anyway would be noise on the one case that is honestly
    /// undecidable.
    /// </item>
    /// <item>
    /// The identity the server named is not the one this side remembered. Otherwise the two agree.
    /// </item>
    /// <item>
    /// The remembered identity did not itself keep that gear in this same push. This is what tells a copy
    /// apart from a mix-up: copy a gearset and the copy is minted new while the original keeps both its
    /// identity and its contents, so the gear now belongs to two sets rather than having moved. Without
    /// this, every copied set would be reported as a contradiction, which is both wrong and the most
    /// common thing a player does.
    /// </item>
    /// </list>
    /// </remarks>
    public static IReadOnlySet<string> Contested(
        IReadOnlyList<CachedGearsetIdentity> previous,
        IReadOnlyList<GearsetDto> sent,
        IReadOnlyList<GearsetAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentNullException.ThrowIfNull(assignments);

        var contested = new HashSet<string>(StringComparer.Ordinal);
        if (previous.Count == 0)
        {
            return contested;
        }

        // Only a fingerprint that stood for one row can witness against anything, so a gear key held by
        // two or more is struck out rather than being allowed to name the first of them.
        var rememberedBy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in previous)
        {
            if (row.GearKey is null)
            {
                continue;
            }

            rememberedBy[row.GearKey] = rememberedBy.ContainsKey(row.GearKey) ? null : row.SetUid;
        }

        // What each identity is holding after this push, so condition four can ask whether the remembered
        // one kept its contents. And how often each name occurs, for the first condition.
        var nowHolding = new Dictionary<string, string>(StringComparer.Ordinal);
        var timesNamed = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < assignments.Count && i < sent.Count; i++)
        {
            var uid = assignments[i].SetUid;
            if (!string.IsNullOrEmpty(uid))
            {
                nowHolding[uid!] = GearsetFingerprint.Gear(sent[i]);
            }

            var nameKey = GearsetFingerprint.NameKey(sent[i]);
            timesNamed[nameKey] = timesNamed.GetValueOrDefault(nameKey) + 1;
        }

        for (var i = 0; i < assignments.Count && i < sent.Count; i++)
        {
            var assignment = assignments[i];
            var uid = assignment.SetUid;
            if (string.IsNullOrEmpty(uid) ||
                !WorthChecking(assignment.MatchedBy, timesNamed.GetValueOrDefault(GearsetFingerprint.NameKey(sent[i]))))
            {
                continue;
            }

            var gear = GearsetFingerprint.Gear(sent[i]);
            if (!rememberedBy.TryGetValue(gear, out var remembered) ||
                remembered is null ||
                string.Equals(remembered, uid, StringComparison.Ordinal))
            {
                continue;
            }

            if (nowHolding.TryGetValue(remembered, out var stillHas) &&
                string.Equals(stillHas, gear, StringComparison.Ordinal))
            {
                continue;
            }

            contested.Add(uid!);
            contested.Add(remembered);
        }

        return contested;
    }

    /// <summary>
    /// Whether the server's own account of how it matched leaves room for this side to object.
    /// </summary>
    /// <param name="matchedBy">The rung from the push answer.</param>
    /// <param name="timesNamed">How many sets in this same push carry this one's job and name.</param>
    /// <returns><see langword="true"/> where the server guessed, found nothing, or only appears not to
    /// have guessed.</returns>
    /// <remarks>
    /// <para>
    /// The two uncertain rungs, plus <c>new</c>. A freshly minted uid is not a guess, it is the server
    /// stating it has never seen this set, and that is a claim gear this side remembers under another
    /// identity can contradict just as squarely.
    /// </para>
    /// <para>
    /// And any rung at all once a second set carries the same job and name, which is the case that made
    /// this check nearly useless when it was first written. The server's <c>exact</c> rung is job, name and
    /// position together, so for a set whose name is its own it is strong evidence, and for one of two
    /// sets sharing a name it is the position and nothing else. Swap two such sets and the server matches
    /// both on <c>exact</c>, confidently and the wrong way round, without ever reaching the rung it would
    /// have admitted to guessing on. Since the game names a new gearset after its job, two sets of one job
    /// share a name from the moment they are made, so this is not a corner: it is the ordinary shape of the
    /// problem, and skipping it left the check firing only where the server had already owned up.
    /// </para>
    /// </remarks>
    private static bool WorthChecking(string? matchedBy, int timesNamed) =>
        timesNamed > 1 ||
        MatchedBy.IsUncertain(matchedBy) ||
        string.Equals(matchedBy, MatchedBy.New, StringComparison.Ordinal);
}
