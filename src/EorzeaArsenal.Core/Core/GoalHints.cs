using System.Globalization;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>How a goal line in an item's tooltip is coloured.</summary>
public enum GoalHintTone
{
    /// <summary>The tooltip's own text colour.</summary>
    Plain,

    /// <summary>Reached, enough, done.</summary>
    Good,

    /// <summary>Still missing.</summary>
    Missing,
}

/// <summary>One line of the goals block in an item's tooltip.</summary>
/// <param name="Text">The line, already translated.</param>
/// <param name="Tone">Its colour.</param>
public sealed record GoalHintLine(string Text, GoalHintTone Tone);

/// <summary>
/// What an item's tooltip says about the player's goals (agreed with the operator, 2026-11-28): for an item
/// that is a goal, that it is one and how far it is; for an item one of the open goals costs (the totem the
/// wings are bought with), which goals need it and what is still missing. Every number is the server's, from
/// the goals last read; nothing is worked out here.
/// </summary>
public static class GoalHints
{
    /// <summary>The lines for one item, empty when it is neither a goal nor a cost of one.</summary>
    /// <param name="mine">The goals last read, or <see langword="null"/>.</param>
    /// <param name="itemId">The hovered item, without the HQ or collectable offset.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <returns>The lines, in order.</returns>
    public static IReadOnlyList<GoalHintLine> For(MyGoals? mine, long itemId, Localizer localizer)
    {
        if (mine is null || itemId <= 0)
        {
            return [];
        }

        var culture = localizer.Language == Localizer.German ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-US");
        string N(long value) => value.ToString("N0", culture);

        var lines = new List<GoalHintLine>();
        if (mine.Goals.FirstOrDefault(g => g.Item?.Id == itemId) is { } goal)
        {
            var collect = string.Equals(goal.Purpose, GoalPurposes.Collect, StringComparison.Ordinal);
            if (goal.Done is { } done)
            {
                var why = done.By switch
                {
                    "hand" => localizer.Get(LocKeys.GoalDoneHand),
                    "learned" => localizer.Get(LocKeys.GoalDoneLearned),
                    "held" => localizer.Get(LocKeys.GoalDoneHeld),
                    _ => null,
                };
                var text = localizer.Get(LocKeys.GoalTooltipDone);
                lines.Add(new GoalHintLine(why is null ? text : $"{text} · {why}", GoalHintTone.Good));
                return WithCost(lines, mine, itemId, localizer, N);
            }

            var head = localizer.Get(LocKeys.GoalTooltipIsGoal);
            lines.Add(new GoalHintLine(collect ? $"{head} · {localizer.Get(LocKeys.GoalPurposeCollect)}" : head, GoalHintTone.Plain));

            if (goal.Unused)
            {
                lines.Add(new GoalHintLine(localizer.Get(LocKeys.GoalUnused), GoalHintTone.Good));
            }
            else if ((collect || !goal.Learnable) && goal.Qty > 1)
            {
                lines.Add(new GoalHintLine(
                    localizer.Get(LocKeys.GoalHeldOf, N(goal.Held), N(goal.Qty)),
                    goal.Held >= goal.Qty ? GoalHintTone.Good : GoalHintTone.Missing));
            }

            foreach (var cost in goal.Progress?.Cost ?? [])
            {
                lines.Add(CostLine($"{N(cost.Have)} / {N(cost.Need)} {cost.Name}", cost.Missing, localizer, N));
            }

            // The item is a goal of a team too: the server shows it once, on the own goal.
            if (goal.AlsoTeam.Count > 0)
            {
                lines.Add(new GoalHintLine(localizer.Get(LocKeys.GoalAlsoTeam, string.Join(", ", goal.AlsoTeam)), GoalHintTone.Plain));
            }
        }

        // A goal of the character's teams (agreed with the operator, 2026-11-29): the team and this character's state.
        foreach (var team in mine.TeamGoals.Where(t => t.Item?.Id == itemId))
        {
            var participant = team.Participants.FirstOrDefault();
            var name = team.Team?.Name ?? string.Empty;
            (string Text, GoalHintTone Tone)? state = participant is null ? null : TeamGoalText.State(participant, localizer);
            var head = localizer.Get(LocKeys.GoalTeamOf, name);
            lines.Add(new GoalHintLine(state is null ? head : $"{head} · {state.Value.Text}", state?.Tone ?? GoalHintTone.Plain));
            if (participant is not null && string.Equals(participant.State, TeamGoalStates.Open, StringComparison.Ordinal))
            {
                foreach (var cost in participant.Progress?.Cost ?? [])
                {
                    lines.Add(CostLine($"{N(cost.Have)} / {N(cost.Need)} {cost.Name}", cost.Missing, localizer, N));
                }
            }
        }

        return WithCost(lines, mine, itemId, localizer, N);
    }

    /// <summary>"Needed for: the wings", and what is held of it against what every open goal needs.</summary>
    private static List<GoalHintLine> WithCost(List<GoalHintLine> lines, MyGoals mine, long itemId, Localizer localizer, Func<long, string> n)
    {
        if (mine.Plan.FirstOrDefault(r => r.Id == itemId) is { } row)
        {
            var goals = row.Goals.Count > 0 ? string.Join(", ", row.Goals) : string.Empty;
            lines.Add(new GoalHintLine(localizer.Get(LocKeys.GoalTooltipNeededFor, goals), GoalHintTone.Plain));
            lines.Add(CostLine($"{n(row.Have)} / {n(row.Need)}", row.Missing, localizer, n));
        }

        return lines;
    }

    private static GoalHintLine CostLine(string counts, long missing, Localizer localizer, Func<long, string> n) =>
        missing > 0
            ? new GoalHintLine($"{counts}, {localizer.Get(LocKeys.GoalMissing, n(missing))}", GoalHintTone.Missing)
            : new GoalHintLine($"{counts}, {localizer.Get(LocKeys.GoalEnough)}", GoalHintTone.Good);
}

/// <summary>The words for a character's state on a goal of the team, shared by the windows and the tooltip.</summary>
public static class TeamGoalText
{
    /// <summary>The state as the player reads it, and its colour.</summary>
    /// <param name="participant">The character or placeholder.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <returns>The words; "Offen" for an unknown state.</returns>
    public static (string Text, GoalHintTone Tone) State(TeamGoalParticipant participant, Localizer localizer) => participant.State switch
    {
        TeamGoalStates.Learned => (localizer.Get(LocKeys.GoalDoneLearned), GoalHintTone.Good),
        TeamGoalStates.Held => (localizer.Get(LocKeys.GoalDoneHeld), GoalHintTone.Good),
        TeamGoalStates.Have => (localizer.Get(LocKeys.TeamGoalStateHave), GoalHintTone.Good),
        TeamGoalStates.NoInterest => (localizer.Get(LocKeys.TeamGoalStateNoInterest), GoalHintTone.Plain),
        _ => (localizer.Get(LocKeys.TeamGoalStateOpen), GoalHintTone.Missing),
    };

    /// <summary>"gesetzt von Anna", or <see langword="null"/> without an answer.</summary>
    /// <param name="participant">The character or placeholder.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <returns>The words, or <see langword="null"/>.</returns>
    public static string? MarkedBy(TeamGoalParticipant participant, Localizer localizer) =>
        participant.Mark is { Mark: not null, By: { Length: > 0 } by } ? localizer.Get(LocKeys.TeamGoalMarkedBy, by) : null;
}
