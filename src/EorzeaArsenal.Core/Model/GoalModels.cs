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

    /// <summary>Whether the goal is shared with the character's teams.</summary>
    public bool Shared { get; init; }

    /// <summary>Whether the item unlocks something to learn.</summary>
    public bool Learnable { get; init; }

    /// <summary>Whether that unlock is learned.</summary>
    public bool Learned { get; init; }

    /// <summary>Held but not used yet: nothing left to pay for it.</summary>
    public bool Unused { get; init; }

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
public sealed class TeamGoalsResponse
{
    /// <summary>The team's shared goals.</summary>
    public TeamGoals? Data { get; init; }
}

/// <summary>The shared goals of a team, the items most members still want first.</summary>
public sealed class TeamGoals
{
    /// <summary>The team id.</summary>
    public string? TeamId { get; init; }

    /// <summary>One entry per item.</summary>
    public List<TeamGoalItem> Items { get; init; } = [];
}

/// <summary>One item the team's members want.</summary>
public sealed class TeamGoalItem
{
    /// <summary>The item.</summary>
    public GoalItem? Item { get; init; }

    /// <summary>The goal's area.</summary>
    public string? Area { get; init; }

    /// <summary>The item's first way that costs something, or <see langword="null"/>.</summary>
    public TeamGoalWay? Way { get; init; }

    /// <summary>Who wants it.</summary>
    public List<TeamGoalWant> Wanted { get; init; } = [];
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
public sealed class TeamGoalWant
{
    /// <summary>The character's name.</summary>
    public string? Character { get; init; }

    /// <summary>The member's name on the website.</summary>
    public string? Member { get; init; }

    /// <summary>How many are wanted.</summary>
    public long Qty { get; init; }

    /// <summary>Why and when it is done, or <see langword="null"/> while open.</summary>
    public GoalDone? Done { get; init; }

    /// <summary>How many the character holds.</summary>
    public long Held { get; init; }

    /// <summary>Held but not used yet.</summary>
    public bool Unused { get; init; }

    /// <summary>What the character holds of each cost of the way.</summary>
    public List<TeamGoalCostHave> CostHave { get; init; } = [];
}

/// <summary>What a member holds of one cost.</summary>
public sealed class TeamGoalCostHave
{
    /// <summary>The item id of the cost.</summary>
    public long Id { get; init; }

    /// <summary>What is held.</summary>
    public long Have { get; init; }
}
