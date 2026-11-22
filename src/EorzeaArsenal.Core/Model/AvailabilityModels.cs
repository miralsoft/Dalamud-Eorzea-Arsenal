using System.Text.Json.Serialization;

namespace EorzeaArsenal.Model;

// --- GET /me/availability (server 1.5) --------------------------------------------------------------

/// <summary>Response of <c>GET /me/availability</c>: the player's own times.</summary>
public sealed class MyAvailabilityResponse
{
    /// <summary>The times.</summary>
    public MyAvailability? Data { get; init; }
}

/// <summary>The player's scopes, absences and teams.</summary>
public sealed class MyAvailability
{
    /// <summary>The Monday of the current week, <c>YYYY-MM-DD</c>.</summary>
    public string? ThisWeek { get; init; }

    /// <summary>The own times for every team (<c>team_id</c> null) and other times for single teams.</summary>
    public List<AvailabilityScope> Scopes { get; init; } = [];

    /// <summary>The player's absences that end yesterday or later.</summary>
    public List<Absence> Absences { get; init; } = [];

    /// <summary>The player's teams and whether each has times of its own.</summary>
    public List<AvailabilityTeam> Teams { get; init; } = [];
}

/// <summary>One scope of times: its plans, the weeks set by hand and how it reads its weeks.</summary>
public sealed class AvailabilityScope
{
    /// <summary><see langword="null"/> for the times of every team, else the team they are for.</summary>
    public long? TeamId { get; init; }

    /// <summary>The clock of the plans.</summary>
    public string? Timezone { get; init; }

    /// <summary>Whether the player chooses a plan every week; a week not chosen is then unclear.</summary>
    public bool AskWeekly { get; init; }

    /// <summary>Plans in turn, one per week from <see cref="RhythmStart"/>; empty or null for none.</summary>
    public List<long>? Rhythm { get; init; }

    /// <summary>The Monday the rhythm starts on.</summary>
    public string? RhythmStart { get; init; }

    /// <summary>The scope's plans; one is active.</summary>
    public List<AvailabilityPlan> Plans { get; init; } = [];

    /// <summary>Weeks set to a plan by hand, this week and later.</summary>
    public List<AvailabilityWeek> Weeks { get; init; } = [];
}

/// <summary>One plan of a scope. Its name is private: no team call ever names it.</summary>
public sealed class AvailabilityPlan
{
    /// <summary>The plan id.</summary>
    public long Id { get; init; }

    /// <summary>The player's own name for it.</summary>
    public string? Name { get; init; }

    /// <summary>Whether it is the scope's active plan, the "Grundplan".</summary>
    public bool IsActive { get; init; }

    /// <summary>The date after which it gives way to the active plan; never set on the active one.</summary>
    public string? EndsOn { get; init; }

    /// <summary>The week grid, 336 characters.</summary>
    public string? Cells { get; init; }
}

/// <summary>A week set to a plan by hand.</summary>
public sealed class AvailabilityWeek
{
    /// <summary>The Monday, <c>YYYY-MM-DD</c>.</summary>
    public string? Week { get; init; }

    /// <summary>The plan.</summary>
    public long? PlanId { get; init; }
}

/// <summary>One of the player's teams in the times answer.</summary>
public sealed class AvailabilityTeam
{
    /// <summary>The team id.</summary>
    public long Id { get; init; }

    /// <summary>The team name.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the player keeps other times for this team.</summary>
    public bool OwnTimes { get; init; }
}

/// <summary>Body of <c>PUT /me/availability/weeks</c>: set a week to a plan, or take it back with a null plan.</summary>
public sealed class AvailabilityWeekRequest
{
    /// <summary>Any date of the week; stored as its Monday.</summary>
    public required string Week { get; init; }

    /// <summary>The plan, or <see langword="null"/> to take the week back. Always written, null included.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? PlanId { get; init; }

    /// <summary>The scope's team as <c>my_open_weeks</c> names it; left out for the times of every team.</summary>
    public long? TeamId { get; init; }
}

/// <summary>Body of <c>POST /me/availability/not-tonight</c>.</summary>
public sealed class NotTonightRequest
{
    /// <summary>One team, or left out for every team.</summary>
    public long? TeamId { get; init; }
}

// --- Polls (server 1.5) -------------------------------------------------------------------------------

/// <summary>
/// Response of <c>GET /me/polls</c>. The contract writes it as <c>{ polls: [..] }</c>; the members of both
/// shapes are read, with or without a <c>data</c> envelope, so either form works.
/// </summary>
public sealed class MyPollsResponse
{
    /// <summary>The polls, when the answer carries them at the top.</summary>
    public List<Poll>? Polls { get; init; }

    /// <summary>The polls, when the answer wraps them in <c>data</c>.</summary>
    public MyPollsData? Data { get; init; }

    /// <summary>Whichever of the two the answer used.</summary>
    [JsonIgnore]
    public IReadOnlyList<Poll> All => Polls ?? Data?.Polls ?? [];
}

/// <summary>The <c>data</c> envelope of <see cref="MyPollsResponse"/>.</summary>
public sealed class MyPollsData
{
    /// <summary>The polls.</summary>
    public List<Poll>? Polls { get; init; }
}

/// <summary>One poll for a date.</summary>
public sealed class Poll
{
    /// <summary>The poll id.</summary>
    public long Id { get; init; }

    /// <summary>The team (on <c>GET /me/polls</c>).</summary>
    public long TeamId { get; init; }

    /// <summary>The team's name (on <c>GET /me/polls</c>).</summary>
    public string? TeamName { get; init; }

    /// <summary>The title.</summary>
    public string? Title { get; init; }

    /// <summary>An optional note.</summary>
    public string? Note { get; init; }

    /// <summary>When it closes, <c>YYYY-MM-DD HH:MM</c> on the team's clock.</summary>
    public string? ClosesAt { get; init; }

    /// <summary>Whether it takes answers.</summary>
    public bool Open { get; init; }

    /// <summary>Whether it is about several dates.</summary>
    public bool Multiple { get; init; }

    /// <summary>Whether a time was taken from it.</summary>
    public bool Decided { get; init; }

    /// <summary>Every candidate is past and none was chosen; such a poll is left out.</summary>
    public bool AllPast { get; init; }

    /// <summary>Participants with at least one answer.</summary>
    public int Answered { get; init; }

    /// <summary>Every participant.</summary>
    public int Of { get; init; }

    /// <summary>The candidates, by start date and time.</summary>
    public List<PollOption> Options { get; init; } = [];

    /// <summary>Weeks of the candidates the player left open; asked about first.</summary>
    public List<OpenWeek>? MyOpenWeeks { get; init; }
}

/// <summary>One candidate time of a poll.</summary>
public sealed class PollOption
{
    /// <summary>The candidate id.</summary>
    public long Id { get; init; }

    /// <summary><c>oneoff</c> or <c>weekly</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The first date it falls on.</summary>
    public string? FirstDate { get; init; }

    /// <summary>Its first date is behind us; left out unless chosen.</summary>
    public bool Past { get; init; }

    /// <summary>For a weekly one: the weekdays, 0 Sunday to 6 Saturday.</summary>
    public List<int>? Weekdays { get; init; }

    /// <summary>Start, <c>HH:MM</c>.</summary>
    public string? Time { get; init; }

    /// <summary>End, <c>HH:MM</c>, if any.</summary>
    public string? EndTime { get; init; }

    /// <summary>For a weekly one: days at another time, keyed by weekday (0 Sunday).</summary>
    public Dictionary<string, PollDayTime>? DayTimes { get; init; }

    /// <summary>The answer counts.</summary>
    public PollCounts? Counts { get; init; }

    /// <summary>Fewer yes than the poll's minimum turnout.</summary>
    public bool BelowMin { get; init; }

    /// <summary>The most yes, then the fewest no, then the most maybe; ties all count.</summary>
    public bool Best { get; init; }

    /// <summary>The player's answer: <c>yes</c>, <c>maybe</c>, <c>no</c> or <see langword="null"/>.</summary>
    public string? Mine { get; init; }

    /// <summary>This candidate became an event; it takes no more answers.</summary>
    public bool Chosen { get; init; }

    /// <summary>What the player's own times say about it, while the poll is open.</summary>
    public PollFit? MyFit { get; init; }
}

/// <summary>A day of a weekly candidate at its own time.</summary>
public sealed class PollDayTime
{
    /// <summary>Start, <c>HH:MM</c>.</summary>
    public string? Time { get; init; }

    /// <summary>End, <c>HH:MM</c>, if any.</summary>
    public string? EndTime { get; init; }
}

/// <summary>The answer counts of a candidate.</summary>
public sealed class PollCounts
{
    /// <summary>Yes.</summary>
    public int Yes { get; init; }

    /// <summary>Maybe.</summary>
    public int Maybe { get; init; }

    /// <summary>No.</summary>
    public int No { get; init; }
}

/// <summary>What the player's times say about a candidate.</summary>
public sealed class PollFit
{
    /// <summary><c>yes</c>, <c>maybe</c>, <c>no</c>, <c>unclear</c> or <c>mixed</c>.</summary>
    public string? State { get; init; }

    /// <summary>How many of its dates suit.</summary>
    public int Fits { get; init; }

    /// <summary>Of how many.</summary>
    public int Of { get; init; }
}

/// <summary>A week the player left open, with the plans to choose from.</summary>
public sealed class OpenWeek
{
    /// <summary>The Monday.</summary>
    public string? Week { get; init; }

    /// <summary>The scope's team, passed back as is.</summary>
    public long? TeamId { get; init; }

    /// <summary>The plans to choose from.</summary>
    public List<OpenWeekPlan> Plans { get; init; } = [];
}

/// <summary>A plan offered for an open week.</summary>
public sealed class OpenWeekPlan
{
    /// <summary>The plan id.</summary>
    public long Id { get; init; }

    /// <summary>The player's name for it.</summary>
    public string? Name { get; init; }
}

/// <summary>Body of <c>PUT /teams/{id}/polls/{pid}/answer</c>; a null answer takes it back.</summary>
public sealed class PollAnswerRequest
{
    /// <summary>The candidate.</summary>
    public required long OptionId { get; init; }

    /// <summary><c>yes</c>, <c>maybe</c>, <c>no</c>, or <see langword="null"/> to take the answer back. Always written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Answer { get; init; }
}

// --- GET /teams/{id}/availability (server 1.5) --------------------------------------------------------

/// <summary>Response of <c>GET /teams/{id}/availability</c>: a team's week in numbers.</summary>
public sealed class TeamAvailabilityResponse
{
    /// <summary>The week.</summary>
    public TeamWeek? Data { get; init; }
}

/// <summary>A team's week: counts per half hour, its events and who has no times.</summary>
public sealed class TeamWeek
{
    /// <summary>The Monday of the week shown.</summary>
    public string? Week { get; init; }

    /// <summary>The Monday of the current week.</summary>
    public string? ThisWeek { get; init; }

    /// <summary>The seven dates, Monday first.</summary>
    public List<string> Days { get; init; } = [];

    /// <summary>The team's clock.</summary>
    public string? Timezone { get; init; }

    /// <summary><c>core</c> (the Stamm) and <c>all</c> (with substitutes).</summary>
    public Dictionary<string, AvailabilityGroup> Groups { get; init; } = [];

    /// <summary>The week's events.</summary>
    public List<TeamWeekEvent> Events { get; init; } = [];

    /// <summary>Who has no times.</summary>
    public TeamWeekMissing? Missing { get; init; }
}

/// <summary>Counts per half hour for one group; 336 values, Monday 00:00 first.</summary>
public sealed class AvailabilityGroup
{
    /// <summary>The group's size.</summary>
    public int Size { get; init; }

    /// <summary>Can.</summary>
    public List<int> Can { get; init; } = [];

    /// <summary>If needed.</summary>
    public List<int> Maybe { get; init; } = [];

    /// <summary>The week is still open for them.</summary>
    public List<int> Unclear { get; init; } = [];

    /// <summary>Cannot.</summary>
    public List<int> Cannot { get; init; } = [];
}

/// <summary>An event in the team's week.</summary>
public sealed class TeamWeekEvent
{
    /// <summary>The event id.</summary>
    public long EventId { get; init; }

    /// <summary>The date, <c>YYYY-MM-DD</c>.</summary>
    public string? Date { get; init; }

    /// <summary>The title.</summary>
    public string? Title { get; init; }

    /// <summary>Start, <c>HH:MM</c>.</summary>
    public string? Time { get; init; }

    /// <summary>End, <c>HH:MM</c>, if any.</summary>
    public string? EndTime { get; init; }
}

/// <summary>Participants without times; names only where the reader may see them.</summary>
public sealed class TeamWeekMissing
{
    /// <summary>How many.</summary>
    public int Count { get; init; }

    /// <summary>Of every participant.</summary>
    public int Of { get; init; }

    /// <summary>The names, or <see langword="null"/> without the right to see them.</summary>
    public List<string>? Names { get; init; }
}

// --- GET /me/waiting and GET /teams/{id}/pulse (server 1.5) ---------------------------------------------

/// <summary>What waits in one team.</summary>
public sealed class WaitingCounts
{
    /// <summary>Join requests (owner and <c>manage_members</c> only).</summary>
    public int Members { get; init; }

    /// <summary>Open polls plus polls ready to settle: the count on the website's tab.</summary>
    public int Zeiten { get; init; }

    /// <summary>Open polls without an answer of the player.</summary>
    public int Polls { get; init; }

    /// <summary>Polls ready to settle (<c>manage_schedule</c> only).</summary>
    public int Ready { get; init; }

    /// <summary>One-off events of the next 14 days the player has not answered.</summary>
    public int Termine { get; init; }

    /// <summary><c>members + zeiten + termine</c> (on <c>/me/waiting</c>).</summary>
    public int Total { get; init; }
}

/// <summary>Response of <c>GET /me/waiting</c>.</summary>
public sealed class MyWaitingResponse
{
    /// <summary>The counts.</summary>
    public MyWaiting? Data { get; init; }
}

/// <summary>What waits per team; a team where nothing waits is left out.</summary>
public sealed class MyWaiting
{
    /// <summary>Counts keyed by team id.</summary>
    public Dictionary<string, WaitingCounts> Teams { get; init; } = [];

    /// <summary>The sum of the teams' totals.</summary>
    public int Total { get; init; }
}

/// <summary>Response of <c>GET /teams/{id}/pulse</c>.</summary>
public sealed class TeamPulseResponse
{
    /// <summary>The pulse.</summary>
    public TeamPulse? Data { get; init; }
}

/// <summary>A team's change mark and what waits in it.</summary>
public sealed class TeamPulse
{
    /// <summary>Changes whenever anything in the team changes; opaque, only compared.</summary>
    public string? Mark { get; init; }

    /// <summary>What waits.</summary>
    public WaitingCounts? Waiting { get; init; }
}
