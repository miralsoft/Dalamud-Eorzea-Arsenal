using System.Globalization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>Where the plan of a week comes from.</summary>
public enum WeekPlanSource
{
    /// <summary>The scope has no plan at all.</summary>
    None,

    /// <summary>The player set the week to a plan by hand.</summary>
    SetByHand,

    /// <summary>The scope's rhythm fills the week.</summary>
    Rhythm,

    /// <summary>The scope asks every week and the player has not chosen yet.</summary>
    Unclear,

    /// <summary>Nothing else applies, so the active plan, the "Grundplan", holds.</summary>
    Grundplan,
}

/// <summary>The plan a week uses and why.</summary>
/// <param name="Plan">The plan, or <see langword="null"/> when unclear or there is none.</param>
/// <param name="Source">Where it comes from.</param>
public sealed record WeekPlan(AvailabilityPlan? Plan, WeekPlanSource Source);

/// <summary>How many of a group stand where at one half hour.</summary>
/// <param name="Can">Can.</param>
/// <param name="Maybe">If needed.</param>
/// <param name="Unclear">The week is still open for them.</param>
/// <param name="Cannot">Cannot.</param>
/// <param name="Size">The group's size; the rest said nothing.</param>
public sealed record HalfHourCounts(int Can, int Maybe, int Unclear, int Cannot, int Size);

/// <summary>
/// The rules of the team availability contract (server 1.5) the plugin needs to show the times, answer
/// polls from them and put a team's evening in numbers. The server decides every count; these only read
/// the answers the way the contract describes.
/// </summary>
public static class AvailabilityRules
{
    /// <summary>A poll answer: can.</summary>
    public const string Yes = "yes";

    /// <summary>A poll answer: if needed.</summary>
    public const string Maybe = "maybe";

    /// <summary>A poll answer: cannot.</summary>
    public const string No = "no";

    /// <summary>Half hours per day in a week grid.</summary>
    public const int HalfHoursPerDay = 48;

    /// <summary>
    /// The scope whose times hold for a team: the team's own times where the player keeps them, else the
    /// times for every team.
    /// </summary>
    /// <param name="times">The player's times.</param>
    /// <param name="teamId">The team, or <see langword="null"/> for the times of every team.</param>
    /// <returns>The scope, or <see langword="null"/> when the player has none.</returns>
    public static AvailabilityScope? ScopeFor(MyAvailability times, long? teamId)
    {
        if (teamId is { } id && times.Teams.Any(t => t.Id == id && t.OwnTimes))
        {
            var own = times.Scopes.FirstOrDefault(s => s.TeamId == id);
            if (own is not null)
            {
                return own;
            }
        }

        return times.Scopes.FirstOrDefault(s => s.TeamId is null);
    }

    /// <summary>
    /// Which plan a week uses: the week set by hand, else the rhythm, else unclear when the scope asks every
    /// week, else the active plan. A plan past its <c>ends_on</c> gives way to the active one.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="monday">The week's Monday.</param>
    /// <returns>The plan and where it comes from.</returns>
    public static WeekPlan PlanForWeek(AvailabilityScope scope, DateOnly monday)
    {
        var active = scope.Plans.FirstOrDefault(p => p.IsActive);
        if (scope.Plans.Count == 0)
        {
            return new WeekPlan(null, WeekPlanSource.None);
        }

        var set = scope.Weeks.FirstOrDefault(w => TryDate(w.Week, out var d) && d == monday);
        if (set?.PlanId is { } setId && Find(scope, setId) is { } byHand)
        {
            return StillRunning(byHand, monday) ? new WeekPlan(byHand, WeekPlanSource.SetByHand) : Grundplan(active);
        }

        if (scope.Rhythm is { Count: > 0 } rhythm && TryDate(scope.RhythmStart, out var start) && monday >= start)
        {
            var weeks = (monday.DayNumber - start.DayNumber) / 7;
            if (Find(scope, rhythm[weeks % rhythm.Count]) is { } inTurn)
            {
                return StillRunning(inTurn, monday) ? new WeekPlan(inTurn, WeekPlanSource.Rhythm) : Grundplan(active);
            }
        }

        return scope.AskWeekly ? new WeekPlan(null, WeekPlanSource.Unclear) : Grundplan(active);
    }

    /// <summary>The Monday of the week a date lies in.</summary>
    /// <param name="date">Any date.</param>
    /// <returns>Its Monday.</returns>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// The answer the player's times suggest for a candidate: <c>yes</c>, <c>maybe</c>, <c>no</c>; a weekly
    /// candidate that suits only some weeks (<c>mixed</c>) is <c>maybe</c>; an open week or nothing said is no
    /// suggestion. This is the site's "Nach meinen Zeiten antworten".
    /// </summary>
    /// <param name="fit">The candidate's <c>my_fit</c>.</param>
    /// <returns>The suggested answer, or <see langword="null"/>.</returns>
    public static string? SuggestedAnswer(PollFit? fit) => fit?.State switch
    {
        "yes" => Yes,
        "maybe" or "mixed" => Maybe,
        "no" => No,
        _ => null,
    };

    /// <summary>Whether a poll belongs in the view: open, and not every candidate past.</summary>
    /// <param name="poll">The poll.</param>
    /// <returns><see langword="true"/> when it is shown.</returns>
    public static bool Shows(Poll poll) => poll.Open && !poll.AllPast;

    /// <summary>The candidates to draw: a past one only when it was chosen.</summary>
    /// <param name="poll">The poll.</param>
    /// <returns>The candidates in the server's order.</returns>
    public static IEnumerable<PollOption> VisibleOptions(Poll poll) => poll.Options.Where(o => !o.Past || o.Chosen);

    /// <summary>Whether a candidate still takes an answer.</summary>
    /// <param name="option">The candidate.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool TakesAnswers(PollOption option) => !option.Past && !option.Chosen;

    /// <summary>
    /// The answers "answer from my times" sends: every candidate without an answer that still takes one and
    /// for which the times suggest something.
    /// </summary>
    /// <param name="poll">The poll.</param>
    /// <returns>Candidate and answer pairs.</returns>
    public static IReadOnlyList<(long OptionId, string Answer)> AnswersFromTimes(Poll poll) =>
        [.. poll.Options
            .Where(o => o.Mine is null && TakesAnswers(o))
            .Select(o => (o.Id, Answer: SuggestedAnswer(o.MyFit)))
            .Where(p => p.Answer is not null)
            .Select(p => (p.Id, p.Answer!))];

    /// <summary>The week's next event from now on: today's still running or later ones, then later days.</summary>
    /// <param name="week">The team's week.</param>
    /// <param name="now">Now on the team's clock.</param>
    /// <returns>The event, or <see langword="null"/> when none is left this week.</returns>
    public static TeamWeekEvent? NextEvent(TeamWeek week, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var clock = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        return week.Events
            .Where(e => TryDate(e.Date, out var d) &&
                        (d > today || (d == today && string.CompareOrdinal(e.EndTime ?? e.Time ?? "24:00", clock) >= 0)))
            .OrderBy(e => e.Date, StringComparer.Ordinal)
            .ThenBy(e => e.Time, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>How a group stands at an event's start half hour.</summary>
    /// <param name="week">The team's week.</param>
    /// <param name="ev">The event.</param>
    /// <param name="group"><c>core</c> (the Stamm) or <c>all</c>.</param>
    /// <returns>The counts, or <see langword="null"/> when the event is not in the week or the group is missing.</returns>
    public static HalfHourCounts? AtStart(TeamWeek week, TeamWeekEvent ev, string group = "core")
    {
        var day = week.Days.IndexOf(ev.Date ?? string.Empty);
        if (day < 0 || !week.Groups.TryGetValue(group, out var counts) ||
            !TimeOnly.TryParseExact(ev.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        var index = (day * HalfHoursPerDay) + (time.Hour * 2) + (time.Minute >= 30 ? 1 : 0);
        return new HalfHourCounts(At(counts.Can, index), At(counts.Maybe, index), At(counts.Unclear, index), At(counts.Cannot, index), counts.Size);
    }

    private static int At(List<int> values, int index) => index < values.Count ? values[index] : 0;

    private static AvailabilityPlan? Find(AvailabilityScope scope, long id) => scope.Plans.FirstOrDefault(p => p.Id == id);

    private static bool StillRunning(AvailabilityPlan plan, DateOnly monday) =>
        plan.IsActive || !TryDate(plan.EndsOn, out var ends) || ends >= monday;

    private static WeekPlan Grundplan(AvailabilityPlan? active) =>
        active is null ? new WeekPlan(null, WeekPlanSource.None) : new WeekPlan(active, WeekPlanSource.Grundplan);

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
