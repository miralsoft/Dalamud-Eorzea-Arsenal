namespace EorzeaArsenal.Model;

/// <summary>
/// Response of <c>GET /me/goals?cid_hash=&amp;lang=</c> (server 1.5, <c>goals-progress</c>): the character's goals
/// with every number worked out by the server. The plugin shows it and computes nothing.
/// </summary>
public sealed class MyGoalsResponse
{
    /// <summary>The goals and the plan.</summary>
    public MyGoals? Data { get; init; }
}

/// <summary>The character's goals, newest first, and "Was noch fehlt".</summary>
public sealed class MyGoals
{
    /// <summary>The server's id of the character, or <see langword="null"/> when there is none to read.</summary>
    public string? CharacterId { get; init; }

    /// <summary>The goals, newest first.</summary>
    public List<Goal> Goals { get; init; } = [];

    /// <summary>Every open goal's costs summed per item, the most missing first.</summary>
    public List<GoalPlanRow> Plan { get; init; } = [];

    /// <summary>The open goals of the character's teams, each with this character's state only (<c>team-goals</c>).</summary>
    public List<TeamGoal> TeamGoals { get; init; } = [];

    /// <summary>The teams where the player may set a goal of the team; empty: "Als Teamziel festlegen" is not offered.</summary>
    public List<TeamRef> TeamGoalTeams { get; init; } = [];

    /// <summary>Every active team of the player, to name the teams of an own goal.</summary>
    public List<TeamRef> MyTeams { get; init; } = [];
}

/// <summary>A team by id and name.</summary>
public sealed class TeamRef
{
    /// <summary>The team id (a string of digits in the answer).</summary>
    public long Id { get; init; }

    /// <summary>The team's name.</summary>
    public string? Name { get; init; }
}

/// <summary>One goal of the character.</summary>
public sealed class Goal
{
    /// <summary>The goal's id.</summary>
    public long Id { get; init; }

    /// <summary>The item wanted.</summary>
    public GoalItem? Item { get; init; }

    /// <summary><c>gear</c>, <c>glam</c>, <c>mount</c>, <c>minion</c>, <c>orchestrion</c>, <c>card</c> or <c>other</c>.</summary>
    public string? Area { get; init; }

    /// <summary>How many are wanted.</summary>
    public long Qty { get; init; }

    /// <summary>How many the character holds, retainers included.</summary>
    public long Held { get; init; }

    /// <summary>The teams the player shows this goal to, chosen on the website (<c>team-goals</c>).</summary>
    public List<long> Teams { get; init; } = [];

    /// <summary>The names of the teams that have this item as a goal of the team too (<c>team-goals</c>).</summary>
    public List<string> AlsoTeam { get; init; } = [];

    /// <summary>Whether the item unlocks something to learn.</summary>
    public bool Learnable { get; init; }

    /// <summary>Whether that unlock is learned.</summary>
    public bool Learned { get; init; }

    /// <summary>Held but not used yet: nothing left to pay for it.</summary>
    public bool Unused { get; init; }

    /// <summary><c>learn</c> or <c>collect</c> for an item to learn (<c>unlock-kinds</c>); <see langword="null"/> otherwise or from an older server.</summary>
    public string? Purpose { get; init; }

    /// <summary>Why and when the goal is done, or <see langword="null"/> while it is open.</summary>
    public GoalDone? Done { get; init; }

    /// <summary>The first way that costs something, or <see langword="null"/> once done, held but not used, or without such a way.</summary>
    public GoalProgress? Progress { get; init; }

    /// <summary>When the goal was made.</summary>
    public string? CreatedAt { get; init; }
}

/// <summary>The item of a goal, named in the language asked.</summary>
public sealed class GoalItem
{
    /// <summary>The item id.</summary>
    public long Id { get; init; }

    /// <summary>The item's name.</summary>
    public string? Name { get; init; }

    /// <summary>The item level, where the catalog knows one.</summary>
    public int? Ilvl { get; init; }
}

/// <summary>Why a goal is done.</summary>
public sealed class GoalDone
{
    /// <summary><c>hand</c>, <c>learned</c> or <c>held</c>.</summary>
    public string? By { get; init; }

    /// <summary>When, where known.</summary>
    public string? At { get; init; }
}

/// <summary>The first way of a goal that costs something, with what is held of each cost.</summary>
public sealed class GoalProgress
{
    /// <summary>The kind of the way, such as <c>trade</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The costs.</summary>
    public List<GoalCost> Cost { get; init; } = [];
}

/// <summary>One cost of a goal's way.</summary>
public sealed class GoalCost
{
    /// <summary>The item id of the cost.</summary>
    public long Id { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>What one costs.</summary>
    public long Count { get; init; }

    /// <summary>What is needed for what is still wanted.</summary>
    public long Need { get; init; }

    /// <summary>What the character holds of it (bags, retainers, chests, the wallet).</summary>
    public long Have { get; init; }

    /// <summary>What is still missing.</summary>
    public long Missing { get; init; }
}

/// <summary>One row of "Was noch fehlt".</summary>
public sealed class GoalPlanRow
{
    /// <summary>The item id of the cost.</summary>
    public long Id { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>What every open goal needs of it together.</summary>
    public long Need { get; init; }

    /// <summary>What is held, counted once.</summary>
    public long Have { get; init; }

    /// <summary>What is still missing.</summary>
    public long Missing { get; init; }

    /// <summary>The names of the goals it serves.</summary>
    public List<string> Goals { get; init; } = [];
}

/// <summary>Body of <c>POST /me/goals</c> (server 1.5, <c>goals-from-plugin</c>): add an item as a goal from the game.</summary>
/// <remarks>The server picks the area from the item; quantity and sharing stay the website's defaults.</remarks>
public sealed class GoalAddRequest
{
    /// <summary>The logged-in character, by its <c>cid_hash</c>.</summary>
    public required GoalCharacter Character { get; init; }

    /// <summary>The item to want.</summary>
    public required long ItemId { get; init; }

    /// <summary>How many are wanted, for something collected in numbers; left out for one.</summary>
    public long? Qty { get; init; }

    /// <summary><c>collect</c> for an item to learn wanted in numbers (to sell), only when the player chose it; else left out.</summary>
    public string? Purpose { get; init; }
}

/// <summary>A goal's purpose (<c>unlock-kinds</c>): an item to learn is a goal to learn or one to collect.</summary>
public static class GoalPurposes
{
    /// <summary>Done once learned, one of it.</summary>
    public const string Learn = "learn";

    /// <summary>Done when as many are held as wanted.</summary>
    public const string Collect = "collect";
}

/// <summary>The character a goal is for: only <c>cid_hash</c> is read.</summary>
public sealed class GoalCharacter
{
    /// <summary>The character's <c>cid_hash</c>.</summary>
    public required string CidHash { get; init; }
}

/// <summary>
/// Response of <c>GET /teams/{id}/goals?lang=</c> (server 1.5): the goals the team's members share, per item.
/// It names members and characters, so it is held in memory only.
/// </summary>
public sealed class SharedGoalsResponse
{
    /// <summary>The team's shared goals.</summary>
    public SharedGoals? Data { get; init; }
}

/// <summary>The shared goals of a team, the items most members still want first.</summary>
public sealed class SharedGoals
{
    /// <summary>The team id.</summary>
    public string? TeamId { get; init; }

    /// <summary>One entry per item.</summary>
    public List<SharedGoalItem> Items { get; init; } = [];
}

/// <summary>One item the team's members want.</summary>
public sealed class SharedGoalItem
{
    /// <summary>The item.</summary>
    public GoalItem? Item { get; init; }

    /// <summary>The goal's area.</summary>
    public string? Area { get; init; }

    /// <summary>The item's first way that costs something, or <see langword="null"/>.</summary>
    public TeamGoalWay? Way { get; init; }

    /// <summary>Who wants it.</summary>
    public List<SharedGoalWant> Wanted { get; init; } = [];
}

/// <summary>The first way of an item that costs something.</summary>
public sealed class TeamGoalWay
{
    /// <summary>The kind of the way.</summary>
    public string? Kind { get; init; }

    /// <summary>What one costs.</summary>
    public List<TeamGoalWayCost> Cost { get; init; } = [];
}

/// <summary>One cost of an item's way.</summary>
public sealed class TeamGoalWayCost
{
    /// <summary>The item id of the cost.</summary>
    public long Id { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }

    /// <summary>What one costs.</summary>
    public long Count { get; init; }
}

/// <summary>One member's goal for a shared item.</summary>
public sealed class SharedGoalWant
{
    /// <summary>The character's name.</summary>
    public string? Character { get; init; }

    /// <summary>The member's name on the website.</summary>
    public string? Member { get; init; }

    /// <summary>How many are wanted.</summary>
    public long Qty { get; init; }

    /// <summary><c>learn</c> or <c>collect</c>.</summary>
    public string? Purpose { get; init; }

    /// <summary>Why and when it is done, or <see langword="null"/> while open.</summary>
    public GoalDone? Done { get; init; }

    /// <summary>How many the character holds, or <see langword="null"/> where the member hides it.</summary>
    public long? Held { get; init; }

    /// <summary>Held but not used yet.</summary>
    public bool Unused { get; init; }

    /// <summary>What the character holds of each cost of the way; a cost of a kind the member hides is left out.</summary>
    public List<TeamGoalCostHave> CostHave { get; init; } = [];

    /// <summary>Some of the member's numbers are hidden: show "offen", never a 0.</summary>
    public bool CountsHidden { get; init; }
}

/// <summary>
/// Response of <c>GET /teams/{id}/team-goals?lang=</c> and of the writes on it (<c>team-goals</c>): the goals a team
/// set for every character in it, with each character's state. Names members: memory only.
/// </summary>
public sealed class TeamGoalsResponse
{
    /// <summary>The team's goals.</summary>
    public TeamGoalBoard? Data { get; init; }
}

/// <summary>The goals of one team, newest first, closed ones last.</summary>
public sealed class TeamGoalBoard
{
    /// <summary>The team id.</summary>
    public long TeamId { get; init; }

    /// <summary>The caller holds "Teamziele verwalten" in this team: may answer for anybody.</summary>
    public bool MayManage { get; init; }

    /// <summary>The goals.</summary>
    public List<TeamGoal> Goals { get; init; } = [];
}

/// <summary>
/// One goal of a team. In <c>GET /me/goals</c> it carries <see cref="Team"/> and one participant, the character
/// itself, and no <see cref="Needed"/>, <see cref="Counted"/> or <see cref="Reached"/>.
/// </summary>
public sealed class TeamGoal
{
    /// <summary>The team, in <c>GET /me/goals</c> only.</summary>
    public TeamRef? Team { get; init; }

    /// <summary>The goal's id.</summary>
    public long Id { get; init; }

    /// <summary>The item.</summary>
    public GoalItem? Item { get; init; }

    /// <summary>The area, as for an own goal.</summary>
    public string? Area { get; init; }

    /// <summary>One of the kinds of unlocks, or <see langword="null"/> when nothing is to learn.</summary>
    public string? Kind { get; init; }

    /// <summary><c>learn</c> or <c>collect</c>.</summary>
    public string? Purpose { get; init; }

    /// <summary>How many each character is to have.</summary>
    public long Qty { get; init; }

    /// <summary>The item's first way that costs something, or <see langword="null"/>.</summary>
    public TeamGoalWay? Way { get; init; }

    /// <summary>Where it comes from: the duty of its first drop, the team's content of it.</summary>
    public TeamGoalWhere? Where { get; init; }

    /// <summary>Whether the team closed it.</summary>
    public bool Closed { get; init; }

    /// <summary>When it was set.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>Characters still open of those that count.</summary>
    public int Needed { get; init; }

    /// <summary>Characters that count (not "Kein Interesse").</summary>
    public int Counted { get; init; }

    /// <summary>Every character that counts has it.</summary>
    public bool Reached { get; init; }

    /// <summary>Each character's state.</summary>
    public List<TeamGoalParticipant> Participants { get; init; } = [];

    /// <summary>Whether it is a goal to learn something (one of it, done once learned).</summary>
    public bool ToLearn => Kind is not null && !string.Equals(Purpose, GoalPurposes.Collect, StringComparison.Ordinal);
}

/// <summary>Where a goal of a team comes from.</summary>
public sealed class TeamGoalWhere
{
    /// <summary>The duty of its first drop.</summary>
    public TeamGoalNamed? Duty { get; init; }

    /// <summary>The team's content of that duty, when it has one.</summary>
    public TeamGoalNamed? Content { get; init; }
}

/// <summary>Something named by id.</summary>
public sealed class TeamGoalNamed
{
    /// <summary>Its id.</summary>
    public long Id { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }
}

/// <summary>One character's state for a goal of the team: a character, or a placeholder of the line-up.</summary>
public sealed class TeamGoalParticipant
{
    /// <summary>The character's id, or <see langword="null"/> for a placeholder.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The line-up entry of a placeholder, or <see langword="null"/>.</summary>
    public long? RosterEntryId { get; init; }

    /// <summary>The character's name.</summary>
    public string? Name { get; init; }

    /// <summary>The member's name on the website.</summary>
    public string? Member { get; init; }

    /// <summary>The member's user id, or <see langword="null"/> for a placeholder.</summary>
    public long? UserId { get; init; }

    /// <summary><c>open</c>, <c>learned</c>, <c>held</c>, <c>have</c> or <c>no_interest</c>.</summary>
    public string? State { get; init; }

    /// <summary>The answer given, with who gave it, or <see langword="null"/>.</summary>
    public TeamGoalMark? Mark { get; init; }

    /// <summary>How many the character holds, or <see langword="null"/> where the player hides it.</summary>
    public long? Held { get; init; }

    /// <summary>Some of the player's numbers are hidden: show "offen", never a 0.</summary>
    public bool CountsHidden { get; init; }

    /// <summary>The costs with what is held of each, or <see langword="null"/>.</summary>
    public GoalProgress? Progress { get; init; }
}

/// <summary>An answer for a character.</summary>
public sealed class TeamGoalMark
{
    /// <summary><c>have</c> or <c>no_interest</c>.</summary>
    public string? Mark { get; init; }

    /// <summary>Who gave it.</summary>
    public string? By { get; init; }

    /// <summary>When.</summary>
    public string? At { get; init; }
}

/// <summary>The states and answers of a goal of the team, as the contract names them.</summary>
public static class TeamGoalStates
{
    /// <summary>Not had yet, no answer.</summary>
    public const string Open = "open";

    /// <summary>Learned, from the character's data.</summary>
    public const string Learned = "learned";

    /// <summary>Held, from the character's data.</summary>
    public const string Held = "held";

    /// <summary>"Habe ich schon".</summary>
    public const string Have = "have";

    /// <summary>"Kein Interesse".</summary>
    public const string NoInterest = "no_interest";
}

/// <summary>Body of <c>POST /teams/{id}/team-goals</c>: an item as a goal of the team.</summary>
public sealed class TeamGoalAddRequest
{
    /// <summary>The item.</summary>
    public required long ItemId { get; init; }
}

/// <summary>Body of <c>PUT /teams/{id}/team-goals/{goalId}/marks</c>: an answer for a character, or taking it back.</summary>
public sealed class TeamGoalMarkRequest
{
    /// <summary>The character, or <see langword="null"/> for a placeholder.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The placeholder's line-up entry, or <see langword="null"/> for a character.</summary>
    public long? RosterEntryId { get; init; }

    /// <summary><c>have</c>, <c>no_interest</c>, or <see langword="null"/> to take the answer back: always sent.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? Mark { get; init; }
}

/// <summary>What a member holds of one cost.</summary>
public sealed class TeamGoalCostHave
{
    /// <summary>The item id of the cost.</summary>
    public long Id { get; init; }

    /// <summary>What is held.</summary>
    public long Have { get; init; }
}
