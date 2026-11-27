using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// The goals of server 1.5 to read (<c>goals-progress</c> in <c>contract-goals.md</c>): the character's goals,
/// their progress and "Was noch fehlt" from <c>GET /me/goals</c>, read when the view opens and after a sync,
/// never on a timer. Every number is the server's; this class computes nothing. Making, ticking or removing a
/// goal is not here: that stays on the website until the server offers it to a key.
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
    /// What the game's context menu of an item offers (<c>goals-from-plugin</c>): "remove" for an item that is a
    /// goal, "add" for one that is not and, if it unlocks something, is not learned yet, else nothing. Decided
    /// from the goals last read, never from a read per right-click; before the first read nothing is offered.
    /// </summary>
    /// <param name="itemId">The item, without the HQ or collectable offset.</param>
    /// <param name="learned">Whether the item unlocks something the character has learned.</param>
    /// <returns>The choice, with the goal for "remove".</returns>
    public GoalMenuChoice ChoiceFor(long itemId, bool learned)
    {
        var mine = Mine;
        if (mine is null || itemId <= 0)
        {
            return GoalMenuChoice.None;
        }

        if (mine.Goals.FirstOrDefault(g => g.Item?.Id == itemId) is { } goal)
        {
            return new GoalMenuChoice(GoalMenuAction.Remove, goal);
        }

        return learned ? GoalMenuChoice.None : new GoalMenuChoice(GoalMenuAction.Add, null);
    }

    /// <summary>Adds an item as a goal of the logged-in character; the answer replaces what was read.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="cidHash">The logged-in character.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="null"/> on success, else the error.</returns>
    public async Task<ApiError?> AddAsync(long itemId, string language, string cidHash, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." };
        }

        var request = new GoalAddRequest { Character = new GoalCharacter { CidHash = cidHash }, ItemId = itemId };
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

    /// <summary>"Als Ziel hinzufügen".</summary>
    Add,

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
            "other" => german ? "sonstiges" : "other",
            _ => null,
        };

        return part is null ? page : $"{page}/{part}";
    }
}
