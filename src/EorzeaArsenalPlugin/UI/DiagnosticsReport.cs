#if EORZEA_ARSENAL_DEVTOOLS
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The developer report, as plain lines. Compiled in only under <c>EORZEA_ARSENAL_DEVTOOLS</c>.
/// </summary>
/// <remarks>
/// <para>
/// Text first, drawing second, and deliberately: the panel renders exactly these lines and the copy
/// button hands over exactly these lines. A window that draws one thing and copies another is a window
/// that sends somebody a report of a state they never saw.
/// </para>
/// <para>
/// What it is for is a specific failure. Two gearsets shared a job and a name, one identity swallowed
/// both, and two different windows printed the same set number for two different sets. Every value
/// needed to see that was in memory and none of it was anywhere a person could read: the position table,
/// what each live set resolves to, and what the server actually answered. Chasing it cost a dozen
/// guesses. Nothing here is new information; it is the information that already existed, written down.
/// </para>
/// <para>
/// Never the API key, and never anything derived from it. The character hash is printed short, because
/// it is what every other diagnostic already quotes and a report without it cannot be matched to a
/// server row.
/// </para>
/// </remarks>
public static class DiagnosticsReport
{
    /// <summary>What the identity cache thinks, and what each live gearset resolves to.</summary>
    /// <param name="mapping">The identity cache.</param>
    /// <param name="view">The last sample, which is where the per-gearset rows come from.</param>
    /// <param name="cidHash">The character on screen.</param>
    /// <param name="withdrawn">
    /// The live positions the position table declined to attribute, so a row the mapping resolved and the
    /// table then dropped is not printed as if it stood.
    /// </param>
    /// <param name="withdrawnAsOf">When that table was last rebuilt, so a stale sample is not judged against it.</param>
    /// <returns>The section, one line per entry.</returns>
    public static IReadOnlyList<string> Identity(
        GearsetMappingService mapping,
        GearsetDebugView view,
        string? cidHash,
        IReadOnlySet<int> withdrawn,
        DateTimeOffset withdrawnAsOf)
    {
        var lines = new List<string>
        {
            "== identity ==",

            // First, because every other number here is about one character and the report never said
            // which. A live list that looks like somebody else's is exactly the moment that matters.
            $"character        : {Short(cidHash)}",
            $"server mints uids : {mapping.ServerMintsUids}",
            $"mapping status    : {mapping.MappingStatus(cidHash)}",
            $"cached rows       : {mapping.CachedCount(cidHash)}",
            CachedKeyLine(mapping, cidHash),
            $"last read         : {Stamp(mapping.LastMappingReadUtc(cidHash))}",
            $"uncertain / ambiguous / positional : " +
                $"{mapping.UncertainMatches(cidHash).Count} / {mapping.AmbiguousMatches(cidHash)} / " +
                $"{mapping.PositionalMatches(cidHash)}",
            $"contested         : {mapping.ContestedMatches(cidHash)}",
            $"held uids         : {mapping.HeldUids(cidHash).Count}",
            $"foreign dropped   : {mapping.ForeignRowsDropped(cidHash)}",
        };

        foreach (var pair in mapping.UncertainMatches(cidHash))
        {
            lines.Add($"  uncertain {Short(pair.Key)} on rung {pair.Value}");
        }

        // Named separately from the uncertain rungs above, because the two say different things. Those are
        // the server reporting its own confidence; this is what came back when that was checked against
        // what this side remembered, and it is the only line here the server could not have produced.
        foreach (var uid in mapping.ContestedUids(cidHash))
        {
            lines.Add($"  contested {Short(uid)}: the push and the cache before it disagree");
        }

        lines.Add(string.Empty);
        lines.Add($"-- live gearsets read from the game ({view.Rows.Count}, sampled {Stamp(view.SampledUtc)}) --");

        if (view.Note is { Length: > 0 } note)
        {
            lines.Add($"  note: {note}");
        }

        if (view.Rows.Count == 0)
        {
            lines.Add("  (no sample yet: press \"sample\")");
            return lines;
        }

        // Two counts, because they answer two questions and reading one as the other cost an exchange.
        // The mapping is asked about one gearset at a time and cannot see that another just got the same
        // answer, so it can honestly report every set resolved while the position table has since dropped
        // three of those answers. The line said only the first half and read as "all is well".
        //
        // Only where the two are of the same age. This sample is taken when somebody presses the button
        // and the position table is rebuilt on every BiS fetch, so marking an older sample against a newer
        // table reports withdrawals that have since been replaced by real identities.
        var comparable = view.SampledUtc >= withdrawnAsOf;
        var pulled = comparable ? view.Rows.Count(r => withdrawn.Contains(r.GearIndex)) : 0;
        lines.Add(
            $"  {view.Resolved} of {view.Rows.Count} resolved by the mapping, {view.Ambiguous} ambiguous" +
            (pulled > 0 ? $", {pulled} withdrawn afterwards" : string.Empty) +
            (comparable ? string.Empty : $" (older than the position table of {Stamp(withdrawnAsOf)})"));
        lines.Add("  idx  game  job   uid       found by      server rung     name");

        foreach (var row in view.Rows)
        {
            var mark = comparable && withdrawn.Contains(row.GearIndex) ? "!" : " ";
            lines.Add(
                $" {mark}{row.GearIndex,-4} #{row.GearIndex + 1,-4} {row.Job,-5} " +
                $"{Short(row.SetUid),-9} {row.LocalRung,-13} {row.Rung,-15} {row.Name}");
        }

        if (pulled > 0)
        {
            lines.Add("  ! the position table withdrew this claim; see \"duplicates\" for why");
        }

        return lines;
    }

    /// <summary>
    /// How much of what only a push can know is still cached, in one line.
    /// </summary>
    /// <param name="mapping">The identity cache.</param>
    /// <param name="cidHash">The character.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// A read carries no items, so both keys survive only as long as nothing writes over them. Which rung
    /// each row resolved on was already visible; whether the material for the better rung was still there
    /// was not, and that difference cost an evening.
    /// </remarks>
    private static string CachedKeyLine(GearsetMappingService mapping, string? cidHash)
    {
        var (items, gear, total) = mapping.CachedKeys(cidHash);
        return $"cached keys       : {items} with items, {gear} with gear, of {total}";
    }

    /// <summary>
    /// Where two gearsets cannot be told apart, which is the question behind most wrong attributions.
    /// </summary>
    /// <param name="view">The last sample.</param>
    /// <param name="withdrawn">
    /// The live positions the position table declined to attribute, so a doubled identity can be reported
    /// as handled rather than as a fault.
    /// </param>
    /// <param name="withdrawnAsOf">When that table was last rebuilt, so a stale sample is not judged against it.</param>
    /// <returns>The section.</returns>
    /// <remarks>
    /// This probe exists because working the same thing out by hand took a dozen exchanges. Three pairs
    /// of a player's gearsets shared a job, a name and their gear; the strong key matched both stored rows
    /// and handed back the first, so two live sets resolved to one identity and two windows printed the
    /// same number for two different sets. Every line of the evidence was already in the report, spread
    /// over forty rows where the eye had to find the repeats. Here it is the whole section.
    /// </remarks>
    public static IReadOnlyList<string> Duplicates(
        GearsetDebugView view,
        IReadOnlySet<int> withdrawn,
        DateTimeOffset withdrawnAsOf)
    {
        var lines = new List<string>
        {
            "== duplicates ==",
            $"from the sample of {Stamp(view.SampledUtc)}",
        };

        if (view.Rows.Count == 0)
        {
            lines.Add("  (no sample yet: press \"Sample gearsets\")");
            return lines;
        }

        var byName = view.Rows
            .GroupBy(r => $"{r.Job}|{r.Name}", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        lines.Add($"same job and name : {byName.Count} group(s)");
        foreach (var group in byName)
        {
            lines.Add($"  {group.Key}");
            foreach (var row in group)
            {
                lines.Add($"    idx {row.GearIndex,-4} game #{row.GearIndex + 1,-4} {Short(row.SetUid)} {row.LocalRung}");
            }
        }

        // One identity cannot sit at two positions, and this is where that shows up. It is not by itself a
        // fault, which the line used to claim: the mapping answers one gearset at a time and cannot see
        // that another just got the same answer, so a freshly copied set produces a group here as a matter
        // of course. What matters is what happened next. The position table withdraws every claim on a
        // doubled identity, so a group whose positions are all withdrawn is the machinery working and a
        // group with a position still standing is the fault this section was built to catch.
        var byUid = view.Rows
            .Where(r => r.SetUid is { Length: > 0 })
            .GroupBy(r => r.SetUid!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        // The verdict joins two things of different ages: this sample, taken when somebody pressed the
        // button, and the position table, rebuilt on every BiS fetch. Across that gap it says nothing. A
        // sample from before the table was rebuilt showed three claims the table had since replaced with
        // three separate identities, and reported all three as still standing. That is the loudest line
        // in the report and it was pure staleness. Where the ages disagree, give no verdict at all.
        var comparable = view.SampledUtc >= withdrawnAsOf;

        var unhandled = comparable
            ? byUid.Count(g => g.Any(r => !withdrawn.Contains(r.GearIndex)))
            : 0;

        lines.Add(
            $"same resolved uid : {byUid.Count} group(s)" +
            (unhandled > 0 ? $"  <-- {unhandled} NOT WITHDRAWN" : string.Empty));

        if (byUid.Count > 0 && !comparable)
        {
            lines.Add(
                $"  this sample predates the position table of {Stamp(withdrawnAsOf)}, so whether these " +
                "were withdrawn cannot be said; sample again");
        }

        foreach (var group in byUid)
        {
            lines.Add($"  {Short(group.Key)}");
            foreach (var row in group)
            {
                var state = !comparable ? "unknown  "
                    : withdrawn.Contains(row.GearIndex) ? "withdrawn"
                    : "STANDING ";
                lines.Add($"    idx {row.GearIndex,-4} game #{row.GearIndex + 1,-4} {state} {row.Job,-5} {row.Name}");
            }
        }

        var undecided = view.Rows.Where(r => !r.IsResolved).ToList();
        lines.Add($"not resolved      : {undecided.Count}");
        foreach (var row in undecided)
        {
            lines.Add(
                $"  idx {row.GearIndex,-4} game #{row.GearIndex + 1,-4} {row.Job,-5} " +
                $"{(row.WasAmbiguous ? "ambiguous" : "no match ")} {row.Name}");
        }

        return lines;
    }

    /// <summary>
    /// The table that turns an identity into the number a player sees, and what the BiS window makes of it.
    /// </summary>
    /// <param name="bis">The BiS service.</param>
    /// <returns>The section.</returns>
    public static IReadOnlyList<string> Positions(BisService bis)
    {
        var positions = bis.LivePositions;
        var lines = new List<string>
        {
            "== positions ==",
            $"bis status   : {bis.Status}{(bis.LastErrorKind is { } kind ? $" ({kind})" : string.Empty)}",
            $"fetched      : {Stamp(bis.FetchedUtc == DateTimeOffset.MinValue ? null : bis.FetchedUtc)}",
            $"live table   : {positions.Count} entr(y/ies) uid -> live index",
            $"undecidable  : {bis.AmbiguousLive.Count} live position(s): " +
                $"{(bis.AmbiguousLive.Count == 0 ? "-" : string.Join(", ", bis.AmbiguousLive.OrderBy(i => i).Select(i => $"#{i + 1}")))}",
        };

        foreach (var pair in positions.OrderBy(p => p.Value))
        {
            lines.Add($"  {Short(pair.Key)} -> idx {pair.Value} (game #{pair.Value + 1})");
        }

        lines.Add(string.Empty);
        lines.Add($"-- comparisons ({bis.Comparisons.Count}) --");
        lines.Add("  stored  shown  live?  job   uid       name");

        foreach (var comparison in bis.Comparisons)
        {
            var shown = bis.DisplayIndex(comparison);
            lines.Add(
                $"  {comparison.GearIndex,-7} {shown,-6} {(comparison.HasLiveGearset ? "yes" : "NO "),-6} " +
                $"{comparison.Job,-5} {Short(comparison.SetUid),-9} {comparison.Name}");
        }

        if (bis.WithoutTarget.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"-- live sets with no target ({bis.WithoutTarget.Count}) --");
            foreach (var set in bis.WithoutTarget)
            {
                lines.Add($"  idx {set.GearIndex,-4} (game #{set.GearIndex + 1,-4}) {set.Job,-5} {set.Name}");
            }
        }

        return lines;
    }

    /// <summary>The reconciliation state in full: what is being asked, and every answer offered.</summary>
    /// <param name="state">The state as last read, or nothing.</param>
    /// <param name="cidHash">The character the state was read for.</param>
    /// <returns>The section.</returns>
    public static IReadOnlyList<string> Review(ReviewState? state, string? cidHash)
    {
        var lines = new List<string> { "== review ==" };

        if (state is null)
        {
            lines.Add("  nothing read yet");
            return lines;
        }

        lines.Add($"read for    : {Short(cidHash)}");
        lines.Add($"state_token : {Short(state.StateToken)}");
        lines.Add($"held {state.Held.Count}, orphans {state.Orphans.Count} " +
                  $"({state.OpenOrphans.Count()} open, {state.PutAsideOrphans.Count()} put aside)");

        if (state.Conflicts.Count > 0)
        {
            lines.Add($"conflicts   : {string.Join(", ", state.Conflicts.Select(Short))}");
        }

        foreach (var held in state.Held)
        {
            lines.Add(string.Empty);
            lines.Add(
                $"-- held {Short(held.SetUid)} idx {held.GearIndex} (game #{held.GearIndex + 1}) " +
                $"{held.Job} \"{held.Name}\"");
            lines.Add(
                $"   proposal: {held.Proposal?.Action ?? "-"} -> {Short(held.Proposal?.TargetUid)} " +
                $"confident={held.Proposal?.Confident}  safeForBulk={QuestionAdvisor.SafeForBulk(held)}");

            foreach (var candidate in held.Candidates)
            {
                lines.Add(
                    $"   cand {Short(candidate.SetUid)} {candidate.Probability,3}% " +
                    $"proposed={candidate.Proposed,-5} blocked_by={Short(candidate.BlockedBy)} " +
                    $"src={candidate.Source} pin={candidate.HasPin} share={candidate.HasTeamShare} " +
                    $"\"{candidate.Name}\"");
            }
        }

        foreach (var row in state.Orphans)
        {
            lines.Add(string.Empty);
            lines.Add(
                $"-- orphan {Short(row.SetUid)} {row.Job} \"{row.Name}\" " +
                $"state={row.State} src={row.Source} hidden={row.Hidden} " +
                $"pin={row.HasPin} share={row.HasTeamShare}");
            lines.Add($"   last_seen={row.LastSeenAt ?? "-"} released={row.ReleasedAt ?? "-"}");

            foreach (var similar in row.Similar)
            {
                lines.Add(
                    $"   similar {Short(similar.SetUid)} {similar.Probability,3}% " +
                    $"{similar.MatchedSlots}/{similar.TotalSlots} src={similar.Source} \"{similar.Name}\"");
            }
        }

        return lines;
    }

    /// <summary>Every section, with a heading, ready for the clipboard.</summary>
    /// <param name="version">The plugin version, so a pasted report says which build it came from.</param>
    /// <param name="sections">The sections, in the order they are drawn.</param>
    /// <returns>One string.</returns>
    public static string Compose(string version, params IReadOnlyList<string>[] sections)
    {
        var all = new List<string>
        {
            $"Eorzea Arsenal {version} — diagnostics {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}",
            string.Empty,
        };

        foreach (var section in sections)
        {
            all.AddRange(section);
            all.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, all);
    }

    /// <summary>The first eight characters of an identity, which is what a person reads and compares.</summary>
    /// <param name="value">The uid or hash.</param>
    /// <returns>The stub, or a dash.</returns>
    private static string Short(string? value) =>
        value is { Length: > 0 } ? value[..Math.Min(8, value.Length)] : "-";

    /// <summary>A timestamp in local time, or a dash when there is none.</summary>
    /// <param name="when">The moment.</param>
    /// <returns>The stamp.</returns>
    private static string Stamp(DateTimeOffset? when) =>
        when is { } moment ? moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}
#endif
