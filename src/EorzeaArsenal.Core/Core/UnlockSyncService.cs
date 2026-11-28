using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What asked for an unlock report. A later member outranks an earlier one when two coalesce.</summary>
public enum UnlockTrigger
{
    /// <summary>The game said something was learned. Held to one send every five minutes.</summary>
    Change,

    /// <summary>An inventory sync of the character just went through.</summary>
    AfterInventorySync,

    /// <summary>A gear sync just went through; the only trigger that sends again after a 404.</summary>
    AfterGearSync,
}

/// <summary>The classified outcome of an unlock report attempt.</summary>
public enum UnlockOutcome
{
    /// <summary>The report was stored.</summary>
    Sent,

    /// <summary>Equal to the last report the server took (or refused) for this character.</summary>
    SkippedUnchanged,

    /// <summary>A change, but within five minutes of the previous send.</summary>
    SkippedThrottled,

    /// <summary>The hourly limit is reached, here or on the server.</summary>
    SkippedBackoff,

    /// <summary>The key was refused (401/403); nothing is sent until another key is connected.</summary>
    Stopped,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>No character logged in, or nothing could be read.</summary>
    NotLoggedIn,

    /// <summary>The server lists no unlock items (no such goals, or a server without goals).</summary>
    Nothing,

    /// <summary>The server does not know the character yet (404); waiting for a gear sync.</summary>
    WaitingForGear,

    /// <summary>The server refused this payload (400/422); the same report is not sent again.</summary>
    Rejected,

    /// <summary>Any other failure; a later trigger tries again.</summary>
    Failed,
}

/// <summary>The result of one unlock report attempt.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="Checked">On <see cref="UnlockOutcome.Sent"/>: how many ids the server kept.</param>
/// <param name="ErrorKind">On a server error: its kind.</param>
public readonly record struct UnlockReport(UnlockOutcome Outcome, int? Checked = null, ApiErrorKind? ErrorKind = null);

/// <summary>
/// Reports which goal items' unlocks the character has learned, via <c>PUT /me/unlocks</c> (server 1.5,
/// <c>contract-goals.md</c>): the mount, minion, orchestrion roll or card an item of the player's goals
/// unlocks, so the website ticks the goal off by itself.
/// </summary>
/// <remarks>
/// <para>
/// The same rhythm as the currency sync: after a gear or inventory sync at once, after something was learned
/// at most every <see cref="ChangeInterval"/>, and never a report equal to the last one the server took for
/// that character. A 404 waits for the next gear sync and sends once more; a 429 waits for its
/// <c>Retry-After</c>; a 401 or 403 stops until another key is connected; a 400 or 422 is not sent again.
/// The limit of 30 an hour is counted here too (R23).
/// </para>
/// <para>Single in-flight (P11): requests that arrive during a send coalesce into one more run.</para>
/// </remarks>
public sealed class UnlockSyncService : IDisposable
{
    private readonly IUnlockSource _source;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<int>> _unlockIds;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _accepted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rejected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _awaitingGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retriedAfterGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gaveUp = new(StringComparer.Ordinal);
    private DateTimeOffset _lastSendUtc = DateTimeOffset.MinValue;
    private string? _stoppedForKey;
    private bool _running;
    private UnlockTrigger? _queued;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="source">Asks the game what is learned.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="unlockIds">The goal items the server lists under <c>unlocks</c>, read at each attempt.</param>
    /// <param name="log">Diagnostics sink.</param>
    public UnlockSyncService(IUnlockSource source, IApiClient api, ITokenStore tokens, IClock clock, Func<IReadOnlyList<int>> unlockIds, ILog? log = null)
    {
        _source = source;
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _unlockIds = unlockIds;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The least time between two sends for a change (default five minutes).</summary>
    public TimeSpan ChangeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The hourly limit of <c>PUT /me/unlocks</c>, shared with the collection (the server counts both together).</summary>
    public UnlockCallBudget Budget { get; set; } = new();

    /// <summary>How long a 429 without <c>Retry-After</c> holds every send (default one minute).</summary>
    public TimeSpan DefaultBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Asks for an attempt. Returns at once; the game is read and the report sent in the background.</summary>
    /// <param name="trigger">What asked.</param>
    public void Request(UnlockTrigger trigger)
    {
        lock (_gate)
        {
            if (_running)
            {
                if (_queued is null || trigger > _queued)
                {
                    _queued = trigger;
                }

                return;
            }

            _running = true;
        }

        _ = Task.Run(() => RunAsync(trigger));
    }

    /// <summary>A new game session: forgets what was sent, so the first sync of the session reports once.</summary>
    public void StartSession()
    {
        lock (_gate)
        {
            _accepted.Clear();
            _rejected.Clear();
            _awaitingGear.Clear();
            _retriedAfterGear.Clear();
            _gaveUp.Clear();
        }
    }

    /// <summary>One attempt, awaited; for tests. <see cref="Request"/> is the plugin's entry point.</summary>
    /// <param name="trigger">What asked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<UnlockReport> SyncAsync(UnlockTrigger trigger, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new UnlockReport(UnlockOutcome.NotConnected);
        }

        lock (_gate)
        {
            if (_stoppedForKey is not null)
            {
                if (string.Equals(_stoppedForKey, key, StringComparison.Ordinal))
                {
                    return new UnlockReport(UnlockOutcome.Stopped);
                }

                _stoppedForKey = null;
            }
        }

        var ids = _unlockIds();
        if (ids.Count == 0)
        {
            return new UnlockReport(UnlockOutcome.Nothing);
        }

        if (Budget.IsHeld(_clock.UtcNow))
        {
            return new UnlockReport(UnlockOutcome.SkippedBackoff);
        }

        if (!_source.IsAvailable)
        {
            return new UnlockReport(UnlockOutcome.NotLoggedIn);
        }

        UnlockSnapshot? snapshot;
        try
        {
            snapshot = await _source.ReadAsync(ids, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Unlock read failed: {ex.GetType().Name}.");
            return new UnlockReport(UnlockOutcome.NotLoggedIn);
        }

        if (snapshot is null || string.IsNullOrEmpty(snapshot.Character.CidHash) || snapshot.Checked.Count == 0)
        {
            return new UnlockReport(snapshot is null ? UnlockOutcome.NotLoggedIn : UnlockOutcome.Nothing);
        }

        var cid = snapshot.Character.CidHash;
        var checkedIds = snapshot.Checked.Where(id => id > 0).Distinct().Order().Take(UnlockProtocol.MaxChecked).ToList();
        var inChecked = checkedIds.ToHashSet();
        var learned = snapshot.Learned.Where(inChecked.Contains).Distinct().Order().ToList();
        var fingerprint = $"{string.Join(',', checkedIds)}|{string.Join(',', learned)}";
        var now = _clock.UtcNow;

        lock (_gate)
        {
            if (_gaveUp.Contains(cid))
            {
                return new UnlockReport(UnlockOutcome.WaitingForGear);
            }

            if (_awaitingGear.Contains(cid))
            {
                if (trigger != UnlockTrigger.AfterGearSync)
                {
                    return new UnlockReport(UnlockOutcome.WaitingForGear);
                }

                _awaitingGear.Remove(cid);
                _retriedAfterGear.Add(cid);
            }

            if ((_accepted.TryGetValue(cid, out var taken) && taken == fingerprint) ||
                (_rejected.TryGetValue(cid, out var refused) && refused == fingerprint))
            {
                return new UnlockReport(UnlockOutcome.SkippedUnchanged);
            }

            if (trigger == UnlockTrigger.Change && now - _lastSendUtc < ChangeInterval)
            {
                return new UnlockReport(UnlockOutcome.SkippedThrottled);
            }

            if (!Budget.TryTake(now))
            {
                return new UnlockReport(UnlockOutcome.SkippedBackoff);
            }

            _lastSendUtc = now;
        }

        var request = new UnlocksRequest { Character = snapshot.Character, Checked = checkedIds, Learned = learned };
        var result = await _api.PutUnlocksAsync(key, request, ct).ConfigureAwait(false);
        return Handle(result, cid, fingerprint, key, trigger, learned.Count, checkedIds.Count);
    }

    private UnlockReport Handle(ApiResult<UnlocksResult> result, string cid, string fingerprint, string key, UnlockTrigger trigger, int learned, int looked)
    {
        if (result.IsSuccess)
        {
            lock (_gate)
            {
                _accepted[cid] = fingerprint;
                _rejected.Remove(cid);
                _retriedAfterGear.Remove(cid);
            }

            _log.Info($"Unlocks: {learned} of {looked} goal item(s) learned, {result.Value?.Checked ?? 0} kept ({trigger}).");
            return new UnlockReport(UnlockOutcome.Sent, Checked: result.Value?.Checked);
        }

        var error = result.Error!;
        var tail = error.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
        switch (error.Kind)
        {
            case ApiErrorKind.Unauthorized or ApiErrorKind.Forbidden:
                lock (_gate)
                {
                    _stoppedForKey = key;
                }

                _log.Warning($"Unlocks stopped: {error.StatusCode} until another key is connected{tail}.");
                return new UnlockReport(UnlockOutcome.Stopped, ErrorKind: error.Kind);

            case ApiErrorKind.NotFound:
                bool gaveUp;
                lock (_gate)
                {
                    gaveUp = _retriedAfterGear.Remove(cid);
                    if (gaveUp)
                    {
                        _gaveUp.Add(cid);
                    }
                    else
                    {
                        _awaitingGear.Add(cid);
                    }
                }

                _log.Info(gaveUp
                    ? $"Unlocks: the server still does not know the character after a gear sync; not trying again this session{tail}."
                    : $"Unlocks: the server does not know the character yet; sending once more after the next gear sync{tail}.");
                return new UnlockReport(UnlockOutcome.WaitingForGear, ErrorKind: error.Kind);

            case ApiErrorKind.RateLimited:
                var wait = error.RetryAfter ?? DefaultBackoff;
                Budget.HoldUntil(_clock.UtcNow + wait);

                _log.Info($"Unlocks: limit reached, waiting {wait.TotalSeconds:0} s{tail}.");
                return new UnlockReport(UnlockOutcome.SkippedBackoff, ErrorKind: error.Kind);

            case ApiErrorKind.BadRequest or ApiErrorKind.Validation:
                lock (_gate)
                {
                    _rejected[cid] = fingerprint;
                }

                _log.Warning($"Unlocks refused: {error.StatusCode}; the same report is not sent again{tail}.");
                return new UnlockReport(UnlockOutcome.Rejected, ErrorKind: error.Kind);

            default:
                _log.Info($"Unlocks failed: {error.Kind}{tail}.");
                return new UnlockReport(UnlockOutcome.Failed, ErrorKind: error.Kind);
        }
    }

    private async Task RunAsync(UnlockTrigger trigger)
    {
        var token = _cts.Token;
        while (true)
        {
            try
            {
                await SyncAsync(trigger, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // closing down
            }
            catch (Exception ex)
            {
                // P2: never let an exception escape a background task.
                _log.Error($"Unexpected unlock report error: {ex.GetType().Name}.");
            }

            lock (_gate)
            {
                if (_queued is not { } next || token.IsCancellationRequested)
                {
                    _queued = null;
                    _running = false;
                    return;
                }

                trigger = next;
                _queued = null;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
