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
