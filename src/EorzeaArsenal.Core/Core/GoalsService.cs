using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// The goals of server 1.5 to read (<c>goals-progress</c> in <c>contract-goals.md</c>): the character's goals,
/// their progress and "Was noch fehlt" from <c>GET /me/goals</c>, read when the view opens and after a sync,
/// never on a timer. Every number is the server's; this class computes nothing. It adds and removes an own goal
/// (<c>goals-from-plugin</c>), and for goals of a team (<c>team-goals</c>) reads a team's list, sets an item as a
/// goal of one team and answers for a character; ticking and changing a goal stay on the website.
/// </summary>
public sealed class GoalsService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IApiSettings _settings;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private MyGoals? _mine;
    private string? _mineKey;
    private bool _stale = true;
    private int _loading;

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="settings">The base URL, so what was read is kept per address.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GoalsService(IApiClient api, ITokenStore tokens, IApiSettings settings, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _settings = settings;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The goals last read, or <see langword="null"/> before the first read.</summary>
    public MyGoals? Mine
    {
        get
        {
            lock (_gate)
            {
                return _mine;
            }
        }
    }

    /// <summary>The error of the last read, or <see langword="null"/> after a success.</summary>
    public ApiError? LastError { get; private set; }

    /// <summary>Whether a read is running.</summary>
    public bool Loading => Volatile.Read(ref _loading) != 0;

    /// <summary>Whether the view should read again: it opened, a sync went through, or the character or language changed.</summary>
    /// <param name="language">The view's language.</param>
    /// <param name="cidHash">The logged-in character.</param>
    /// <returns><see langword="true"/> when a read is due.</returns>
    public bool NeedsRead(string language, string cidHash)
    {
        lock (_gate)
        {
            return _stale || _mineKey != MineKey(language, cidHash);
        }
    }

    /// <summary>Marks the goals as out of date: the view opened, or a sync of the character went through.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _stale = true;
        }
    }

    /// <summary>Lets go of what was read, after the key went away.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _mine = null;
            _mineKey = null;
            _stale = true;
        }

        LastError = null;
    }

    /// <summary>Reads the goals. One read at a time; a second call while one runs returns at once.</summary>
    /// <param name="language"><c>de</c> or <c>en</c>, the language of the names.</param>
    /// <param name="cidHash">The logged-in character.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the read is done.</returns>
    public async Task RefreshAsync(string language, string cidHash, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(cidHash) || Interlocked.Exchange(ref _loading, 1) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                _stale = false;
            }

            var result = await _api.GetMyGoalsAsync(key, cidHash, language, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LastError = result.Error ?? new ApiError { Kind = ApiErrorKind.Unexpected, Message = "No answer." };
                var tail = result.Error?.RequestId is null ? string.Empty : $", request_id {result.Error.RequestId}";
                _log.Info($"Goals read failed: {LastError.Kind}{tail}.");
                return;
            }

            var read = result.Value?.Data ?? new MyGoals();
            lock (_gate)
            {
                _mine = read;
                _mineKey = MineKey(language, cidHash);
            }

            LastError = null;
            _log.Info($"Goals read: {read.Goals.Count} goal(s), {read.Goals.Count(g => g.Done is null)} open, {read.Plan.Count} cost(s) missing.");
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _log.Error($"Goals read failed: {ex.GetType().Name}.");
            LastError = new ApiError { Kind = ApiErrorKind.Unexpected, Message = ex.GetType().Name };
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    /// <summary>
    /// What the game's context menu of an item offers (<c>goals-from-plugin</c>), decided from the goals last
    /// read, never from a read per right-click; before the first read nothing is offered.
    /// </summary>
    /// <remarks>
    /// The rule agreed with the operator (2026-11-28): a goal is something still to get. So:
    /// <list type="bullet">
    /// <item>An item that is a goal offers "remove", wherever it is clicked.</item>
    /// <item>Something to learn (<c>unlock-kinds</c>, "learn or collect"): to learn while not learned, but not
    /// from the player's own storage, where it is held and simply to be used; to collect, with a quantity,
    /// whenever it sells on the market board, learned or not. Learned and not sellable: nothing more to get.</item>
    /// <item>Without <c>unlock-kinds</c> something learned is never a goal.</item>
    /// <item>Something else one collects more of (it stacks: totems, dyes, materials) may be a goal anywhere,
    /// with a quantity, since one unit is reached at once by what is held.</item>
    /// <item>A single thing (gear) is a goal only where it is not the player's own: there it is held already.</item>
    /// </list>
    /// </remarks>
    /// <param name="itemId">The item, without the HQ or collectable offset.</param>
    /// <param name="facts">What the game says about the item and where it was clicked.</param>
    /// <param name="learnOrCollect">Whether the server takes a goal's purpose (<c>unlock-kinds</c>).</param>
    /// <returns>The entries to offer, in order; empty for none.</returns>
    public IReadOnlyList<GoalMenuChoice> ChoicesFor(long itemId, GoalItemFacts facts, bool learnOrCollect)
    {
        var mine = Mine;
        if (mine is null || itemId <= 0)
        {
            return [];
        }

        if (mine.Goals.FirstOrDefault(g => g.Item?.Id == itemId) is { } goal)
        {
            return [new GoalMenuChoice(GoalMenuAction.Remove, goal)];
        }

        if (facts.Unlockable && learnOrCollect)
        {
            var choices = new List<GoalMenuChoice>(2);
            if (!facts.Learned && !facts.OwnStorage)
            {
                choices.Add(new GoalMenuChoice(GoalMenuAction.Add, null));
            }

            if (facts.Sellable)
            {
                choices.Add(new GoalMenuChoice(GoalMenuAction.AddToCollect, null));
            }

            return choices;
        }

        if (facts.Learned)
        {
            return [];
        }

        if (facts.Stacks && !facts.Unlockable)
        {
            return [new GoalMenuChoice(GoalMenuAction.AddWithQuantity, null)];
        }

        return facts.OwnStorage ? [] : [new GoalMenuChoice(GoalMenuAction.Add, null)];
    }

    /// <summary>Adds an item as a goal of the logged-in character; the answer replaces what was read.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many are wanted, or <see langword="null"/> for the website's default of one.</param>
    /// <param name="collect">A goal to collect an item to learn (<c>purpose: collect</c>), only when the player chose it.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="cidHash">The logged-in character.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="null"/> on success, else the error.</returns>
    public async Task<ApiError?> AddAsync(long itemId, long? quantity, bool collect, string language, string cidHash, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." };
        }

        var request = new GoalAddRequest
        {
            Character = new GoalCharacter { CidHash = cidHash },
            ItemId = itemId,
            Qty = quantity is > 0 ? quantity : null,
            Purpose = collect ? GoalPurposes.Collect : null,
        };
        var result = await _api.AddGoalAsync(key, request, language, ct).ConfigureAwait(false);
        return Took(result, language, cidHash, "add");
    }

    /// <summary>Removes a goal of the logged-in character; the answer replaces what was read.</summary>
    /// <param name="goalId">The goal's id.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="cidHash">The logged-in character.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="null"/> on success, else the error.</returns>
    public async Task<ApiError?> RemoveAsync(long goalId, string language, string cidHash, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." };
        }

        var result = await _api.RemoveGoalAsync(key, goalId, language, ct).ConfigureAwait(false);
        return Took(result, language, cidHash, "remove");
    }

    /// <summary>
    /// The teams "Als Teamziel festlegen" offers for an item (<c>team-goals</c>), from the goals last read: the
    /// teams where the player holds the right (<c>team_goal_teams</c>), less those where the item is an open goal
    /// of the team already. Nothing for gil and currencies, which are no goal of a team (agreed with the operator,
    /// 2026-11-29: any other item, wherever clicked, learned or not, since the team needs it all the same).
    /// </summary>
    /// <param name="itemId">The item, without the HQ or collectable offset.</param>
    /// <param name="currency">Whether the item is gil or a currency.</param>
    /// <returns>The teams to offer, in the server's order; empty for none.</returns>
    public IReadOnlyList<TeamRef> TeamChoicesFor(long itemId, bool currency)
    {
        var mine = Mine;
        if (mine is null || itemId <= 0 || currency || mine.TeamGoalTeams.Count == 0)
        {
            return [];
        }

        // An item that is an own goal too is not in team_goals: its own goal names the teams (by name) instead.
        var alsoTeam = mine.Goals.FirstOrDefault(g => g.Item?.Id == itemId)?.AlsoTeam ?? [];
        return mine.TeamGoalTeams
            .Where(t => !mine.TeamGoals.Any(g => g.Item?.Id == itemId && g.Team?.Id == t.Id))
            .Where(t => t.Name is null || !alsoTeam.Contains(t.Name, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>Reads the goals of one team with every character's state (<c>GET /teams/{id}/team-goals</c>).</summary>
    /// <param name="teamId">The team.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The team's goals, or the error.</returns>
    public async Task<ApiResult<TeamGoalsResponse>> ReadTeamGoalsAsync(long teamId, string language, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return ApiResult<TeamGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." });
        }

        var result = await _api.GetTeamGoalsAsync(key, teamId, language, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            var goals = result.Value?.Data?.Goals ?? [];
            _log.Info($"Team goals read: {goals.Count} goal(s), {goals.Count(g => !g.Closed && !g.Reached)} open.");
        }
        else
        {
            Failed(result.Error, "Team goals read");
        }

        return result;
    }

    /// <summary>
    /// Sets an item as a goal of one team (<c>POST /teams/{id}/team-goals</c>). The own goals are read again
    /// afterwards: <c>team_goals</c> and <c>also_team</c> follow, and a 403 means the right is gone, so the menu
    /// entry goes with the next read.
    /// </summary>
    /// <param name="teamId">The team, one of <c>team_goal_teams</c>.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The team's goals after it, or the error.</returns>
    public async Task<ApiResult<TeamGoalsResponse>> AddTeamGoalAsync(long teamId, long itemId, string language, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return ApiResult<TeamGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." });
        }

        var result = await _api.AddTeamGoalAsync(key, teamId, new TeamGoalAddRequest { ItemId = itemId }, language, ct).ConfigureAwait(false);
        AfterTeamWrite(result, "Team goal set");
        return result;
    }

    /// <summary>
    /// Answers for a character or a placeholder (<c>PUT /teams/{id}/team-goals/{goalId}/marks</c>): "have",
    /// "no_interest", or <see langword="null"/> to take the answer back. The own goals are read again afterwards.
    /// </summary>
    /// <param name="teamId">The team.</param>
    /// <param name="goalId">The goal of the team.</param>
    /// <param name="participant">Whom the answer is for.</param>
    /// <param name="mark"><see cref="TeamGoalStates.Have"/>, <see cref="TeamGoalStates.NoInterest"/> or <see langword="null"/>.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The team's goals after it, or the error.</returns>
    public async Task<ApiResult<TeamGoalsResponse>> MarkTeamGoalAsync(long teamId, long goalId, TeamGoalParticipant participant, string? mark, string language, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return ApiResult<TeamGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." });
        }

        var request = participant.CharacterId is { } character
            ? new TeamGoalMarkRequest { CharacterId = character, Mark = mark }
            : new TeamGoalMarkRequest { RosterEntryId = participant.RosterEntryId, Mark = mark };
        var result = await _api.MarkTeamGoalAsync(key, teamId, goalId, request, language, ct).ConfigureAwait(false);
        AfterTeamWrite(result, $"Team goal answer '{mark ?? "none"}'");
        return result;
    }

    private void AfterTeamWrite(ApiResult<TeamGoalsResponse> result, string what)
    {
        if (result.IsSuccess)
        {
            _log.Info($"{what}: {result.Value?.Data?.Goals.Count ?? 0} goal(s) in the team now.");
        }
        else
        {
            Failed(result.Error, what);
        }

        // Whatever the answer, what was read of the own goals may be out of date now: a write changed the
        // character's state, a 403 took a right away, a 409 met a goal that was closed meanwhile.
        Invalidate();
    }

    private void Failed(ApiError? error, string what)
    {
        var tail = error?.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
        var capability = error?.MissingCapability is { } missing ? $", missing {missing}" : string.Empty;
        _log.Info($"{what} failed: {error?.Kind} ({error?.StatusCode}){capability}{tail}.");
    }

    private ApiError? Took(ApiResult<MyGoalsResponse> result, string language, string cidHash, string what)
    {
        if (!result.IsSuccess)
        {
            var error = result.Error ?? new ApiError { Kind = ApiErrorKind.Unexpected, Message = "No answer." };
            var tail = error.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
            _log.Info($"Goal {what} failed: {error.Kind} ({error.StatusCode}){tail}.");
            return error;
        }

        // The answer is the goals as they are now, the same shape as the read: no second read needed.
        var now = result.Value?.Data ?? new MyGoals();
        lock (_gate)
        {
            _mine = now;
            _mineKey = MineKey(language, cidHash);
            _stale = false;
        }

        _log.Info($"Goal {what}: {now.Goals.Count} goal(s) now.");
        return null;
    }

    private string MineKey(string language, string cidHash) => $"{_settings.BaseUrl.TrimEnd('/')}|{language}|{cidHash}";
}

/// <summary>What the context menu of an item offers for goals.</summary>
public enum GoalMenuAction
{
    /// <summary>Nothing: no goals read yet, or the item's unlock is learned.</summary>
    None,

    /// <summary>"Als Ziel hinzufügen", one of it.</summary>
    Add,

    /// <summary>"Als Ziel hinzufügen" for something collected in numbers: the player says how many first.</summary>
    AddWithQuantity,

    /// <summary>"Als Sammelziel hinzufügen": an item to learn wanted to collect (to sell), with a quantity; sends <c>purpose: collect</c>.</summary>
    AddToCollect,

    /// <summary>"Ziel entfernen".</summary>
    Remove,
}

/// <summary>The context menu's choice for one item.</summary>
/// <param name="Action">What to offer.</param>
/// <param name="Goal">The goal to remove, for <see cref="GoalMenuAction.Remove"/>.</param>
public sealed record GoalMenuChoice(GoalMenuAction Action, Goal? Goal)
{
    /// <summary>Nothing to offer.</summary>
    public static readonly GoalMenuChoice None = new(GoalMenuAction.None, null);
}

/// <summary>What the game says about an item, and where the player clicked it, for <see cref="GoalsService.ChoicesFor"/>.</summary>
/// <param name="Unlockable">The item unlocks something when used.</param>
/// <param name="Learned">That unlock is learned.</param>
/// <param name="Sellable">The item sells on the market board.</param>
/// <param name="Stacks">The item stacks, so more of it can be collected.</param>
/// <param name="OwnStorage">The menu is an inventory slot of the player's own (bags, armoury, saddlebag, retainer).</param>
public sealed record GoalItemFacts(bool Unlockable, bool Learned, bool Sellable, bool Stacks, bool OwnStorage);

/// <summary>The website's addresses of the goals page, as the server's <c>deep-links.md</c> names them.</summary>
public static class GoalLinks
{
    /// <summary>The page's address in a language, at an area; an unknown or missing area is the overview.</summary>
    /// <param name="area">The goal's area (<c>gear</c>, <c>glam</c>, <c>mount</c>, <c>minion</c>, <c>orchestrion</c>, <c>card</c>, <c>other</c>).</param>
    /// <param name="german">Whether the German address is wanted.</param>
    /// <returns>A path such as <c>/ziele/reittiere</c> or <c>/goals/mounts</c>.</returns>
    public static string Path(string? area, bool german)
    {
        var page = german ? "/ziele" : "/goals";
        var part = area switch
        {
            "gear" => "gear",
            "glam" => "glam",
            "mount" => german ? "reittiere" : "mounts",
            "minion" => german ? "begleiter" : "minions",
            "orchestrion" => "orchestrion",
            "card" => "triple-triad",
            "unlock" => german ? "weiteres-lernbares" : "more-to-learn",
            "other" => german ? "sonstiges" : "other",
            _ => null,
        };

        return part is null ? page : $"{page}/{part}";
    }

    /// <summary>The website's page of a duty.</summary>
    /// <param name="dutyId">The duty's id.</param>
    /// <returns>A path such as <c>/duty/1001</c>.</returns>
    public static string Duty(long dutyId) => $"/duty/{dutyId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>An answer the player may give for a character on a goal of the team.</summary>
public enum TeamGoalAnswer
{
    /// <summary>"Habe ich schon".</summary>
    Have,

    /// <summary>"Kein Interesse".</summary>
    NoInterest,

    /// <summary>"Zurücknehmen": takes the given answer back.</summary>
    TakeBack,
}

/// <summary>
/// Who may answer what on a goal of the team (<c>contract-team-goals.md</c>, "Answering for a character"): every
/// member for their own characters, a member with "Teamziele verwalten" for anybody, placeholders included. The
/// server checks again; this only decides which buttons show.
/// </summary>
public static class TeamGoalRules
{
    /// <summary>
    /// The players among the participants that are the reader: those with a character the plugin knows as its
    /// own. A player's other characters (never logged in on this machine) are then theirs too.
    /// </summary>
    /// <param name="goals">The goals read.</param>
    /// <param name="ownCharacters">The server ids of the characters this machine knows as the player's.</param>
    /// <returns>The user ids.</returns>
    public static IReadOnlySet<long> OwnUsers(IEnumerable<TeamGoal> goals, IReadOnlySet<long> ownCharacters)
    {
        var users = new HashSet<long>();
        foreach (var participant in goals.SelectMany(g => g.Participants))
        {
            if (participant is { CharacterId: { } character, UserId: { } user } && ownCharacters.Contains(character))
            {
                users.Add(user);
            }
        }

        return users;
    }

    /// <summary>Whether the reader may answer for this participant at all.</summary>
    /// <param name="participant">The character or placeholder.</param>
    /// <param name="mayManage">The reader holds "Teamziele verwalten" in the team.</param>
    /// <param name="ownCharacters">The server ids of the reader's characters known here.</param>
    /// <param name="ownUsers">The reader's user ids, from <see cref="OwnUsers"/>.</param>
    /// <returns><see langword="true"/> when the buttons may show.</returns>
    public static bool MayAnswer(TeamGoalParticipant participant, bool mayManage, IReadOnlySet<long> ownCharacters, IReadOnlySet<long> ownUsers)
    {
        if (mayManage)
        {
            return true;
        }

        if (participant.CharacterId is not { } character)
        {
            return false;
        }

        return ownCharacters.Contains(character) || (participant.UserId is { } user && ownUsers.Contains(user));
    }

    /// <summary>
    /// The answers to offer: on an open state "have" and "no interest"; where an answer is set, taking it back;
    /// nothing where the character's own data says it has it (learned, held), or on a closed goal.
    /// </summary>
    /// <param name="goal">The goal of the team.</param>
    /// <param name="participant">The character or placeholder.</param>
    /// <param name="mayAnswer">Result of <see cref="MayAnswer"/>.</param>
    /// <returns>The answers, in order.</returns>
    public static IReadOnlyList<TeamGoalAnswer> Answers(TeamGoal goal, TeamGoalParticipant participant, bool mayAnswer)
    {
        if (!mayAnswer || goal.Closed)
        {
            return [];
        }

        if (participant.Mark is { Mark: not null })
        {
            return [TeamGoalAnswer.TakeBack];
        }

        return string.Equals(participant.State, TeamGoalStates.Open, StringComparison.Ordinal)
            ? [TeamGoalAnswer.Have, TeamGoalAnswer.NoInterest]
            : [];
    }

    /// <summary>The value sent for an answer.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns><c>have</c>, <c>no_interest</c>, or <see langword="null"/> to take it back.</returns>
    public static string? MarkOf(TeamGoalAnswer answer) => answer switch
    {
        TeamGoalAnswer.Have => TeamGoalStates.Have,
        TeamGoalAnswer.NoInterest => TeamGoalStates.NoInterest,
        _ => null,
    };

    /// <summary>
    /// Whether "Habe ich schon" asks once more: on a goal to learn it also stores the item as learned in the
    /// character's collection, which taking the answer back from the plugin does not undo (agreed with the
    /// operator, 2026-11-29).
    /// </summary>
    /// <param name="goal">The goal of the team.</param>
    /// <param name="answer">The answer.</param>
    /// <returns><see langword="true"/> for a second click.</returns>
    public static bool AsksTwice(TeamGoal goal, TeamGoalAnswer answer) => answer == TeamGoalAnswer.Have && goal.ToLearn;
}
