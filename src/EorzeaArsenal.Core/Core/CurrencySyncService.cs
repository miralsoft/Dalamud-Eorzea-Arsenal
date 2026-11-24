using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What asked for a currency snapshot. A later member outranks an earlier one when two coalesce.</summary>
public enum CurrencyTrigger
{
    /// <summary>The periodic look found a count that differs. Held to one send every five minutes.</summary>
    Change,

    /// <summary>An inventory sync of the character just went through.</summary>
    AfterInventorySync,

    /// <summary>
    /// A gear sync of the character just went through. The only trigger that sends again after a 404,
    /// because a gear sync is what creates the character on the server.
    /// </summary>
    AfterGearSync,
}

/// <summary>The classified outcome of a currency sync attempt.</summary>
public enum CurrencyOutcome
{
    /// <summary>The snapshot was stored.</summary>
    Sent,

    /// <summary>The counts equal the last snapshot the server took (or refused) for this character.</summary>
    SkippedUnchanged,

    /// <summary>A change, but within five minutes of the previous send.</summary>
    SkippedThrottled,

    /// <summary>The hourly limit is reached, here or on the server.</summary>
    SkippedBackoff,

    /// <summary>The key was refused (401/403); nothing is sent until another key is connected.</summary>
    Stopped,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>No character logged in, or the purse could not be read.</summary>
    NotLoggedIn,

    /// <summary>The server lists no currencies (not loaded yet, or a server before 1.5).</summary>
    Nothing,

    /// <summary>The server does not know the character yet (404); waiting for a gear sync to create it.</summary>
    WaitingForGear,

    /// <summary>The server refused this payload (400/422); the same counts are not sent again.</summary>
    Rejected,

    /// <summary>Any other failure (network, unexpected answer); a later trigger tries again.</summary>
    Failed,
}

/// <summary>The result of one currency sync attempt.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="Rows">On <see cref="CurrencyOutcome.Sent"/>: the ids the server stored with a count above 0.</param>
/// <param name="ErrorKind">On a server error: its kind.</param>
/// <param name="RequestId">The server's correlation id, if any; safe to log.</param>
public readonly record struct CurrencyReport(
    CurrencyOutcome Outcome,
    int? Rows = null,
    ApiErrorKind? ErrorKind = null,
    string? RequestId = null);

/// <summary>
/// Sends the currencies the server lists via <c>POST /inventory/currencies</c> (server 1.5).
/// </summary>
/// <remarks>
/// <para>
/// Every send is a full snapshot of the listed ids, zeros included, because the server replaces the whole
/// stock with it. A snapshot equal to the last one the server took for that character is never sent again,
/// so a timer only ever sends a real change, and a change is held to one send every
/// <see cref="ChangeInterval"/>. A sync of gear or inventory sends at once, since the player has just
/// asked for their data to go up.
/// </para>
/// <para>
/// The answers follow the contract: a 404 waits for the next gear sync and sends once more after it, never
/// in a loop; a 429 waits an hour; a 401 or 403 stops the sync until another key is connected; a 400 or
/// 422 is logged and the same counts are not sent again. The limit of 30 calls an hour is counted here
/// too (R23), so the server never has to say it.
/// </para>
/// <para>Single in-flight (P11): requests that arrive during a send coalesce into one more run.</para>
/// </remarks>
public sealed class CurrencySyncService : IDisposable
{
    private readonly ICurrencySource _source;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<int>> _currencyIds;
    private readonly ILog _log;
    private readonly CharacterDirectory? _directory;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _accepted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rejected = new(StringComparer.Ordinal);
    private readonly HashSet<string> _awaitingGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retriedAfterGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gaveUp = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _sends = new();
    private DateTimeOffset _lastSendUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _backoffUntilUtc = DateTimeOffset.MinValue;
    private string? _stoppedForKey;
    private bool _running;
    private CurrencyTrigger? _queued;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="source">Reads the counts from the game.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="currencyIds">The ids the server lists, read at each attempt (empty until loaded).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="directory">Optional registry that learns the character's server id from the answer.</param>
    public CurrencySyncService(
        ICurrencySource source,
        IApiClient api,
        ITokenStore tokens,
        IClock clock,
        Func<IReadOnlyList<int>> currencyIds,
        ILog? log = null,
        CharacterDirectory? directory = null)
    {
        _source = source;
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _currencyIds = currencyIds;
        _log = log ?? NullLog.Instance;
        _directory = directory;
    }

    /// <summary>Raised after each attempt made through <see cref="Request"/> (on a background thread).</summary>
    public event Action<CurrencyReport>? Completed;

    /// <summary>The least time between two sends for a change (default five minutes). Syncs are not held to it.</summary>
    public TimeSpan ChangeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a 429 holds every send (default one hour, as the contract says).</summary>
    public TimeSpan RateLimitWait { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Asks for an attempt. Returns at once; the game is read and the snapshot sent in the background.
    /// </summary>
    /// <param name="trigger">What asked.</param>
    public void Request(CurrencyTrigger trigger)
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

    /// <summary>
    /// A new game session: forgets what was sent, so the first look of the session sends once, and gives a
    /// character the server did not know another chance. A 429 wait and a refused key are kept.
    /// </summary>
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

    /// <summary>
    /// One attempt, awaited. <see cref="Request"/> is the entry point for the plugin; this one is for tests
    /// and must not run twice at the same time.
    /// </summary>
    /// <param name="trigger">What asked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<CurrencyReport> SyncAsync(CurrencyTrigger trigger, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new CurrencyReport(CurrencyOutcome.NotConnected);
        }

        lock (_gate)
        {
            if (_stoppedForKey is not null)
            {
                if (string.Equals(_stoppedForKey, key, StringComparison.Ordinal))
                {
                    return new CurrencyReport(CurrencyOutcome.Stopped);
                }

                _stoppedForKey = null; // another key is connected: start again
            }
        }

        var ids = _currencyIds();
        if (ids.Count == 0)
        {
            return new CurrencyReport(CurrencyOutcome.Nothing);
        }

        if (_clock.UtcNow < _backoffUntilUtc)
        {
            return new CurrencyReport(CurrencyOutcome.SkippedBackoff);
        }

        if (!_source.IsAvailable)
        {
            return new CurrencyReport(CurrencyOutcome.NotLoggedIn);
        }

        CurrencySnapshot? snapshot;
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
            _log.Error($"Currency read failed: {ex.GetType().Name}.");
            return new CurrencyReport(CurrencyOutcome.NotLoggedIn);
        }

        if (snapshot is null || string.IsNullOrEmpty(snapshot.Character.CidHash))
        {
            return new CurrencyReport(CurrencyOutcome.NotLoggedIn);
        }

        var cid = snapshot.Character.CidHash;
        var stock = BuildStock(ids, snapshot.Counts);
        var fingerprint = Fingerprint(stock);
        var now = _clock.UtcNow;

        lock (_gate)
        {
            if (_gaveUp.Contains(cid))
            {
                return new CurrencyReport(CurrencyOutcome.WaitingForGear);
            }

            if (_awaitingGear.Contains(cid))
            {
                if (trigger != CurrencyTrigger.AfterGearSync)
                {
                    return new CurrencyReport(CurrencyOutcome.WaitingForGear);
                }

                // The gear sync the 404 asked for has happened: this is the one more send.
                _awaitingGear.Remove(cid);
                _retriedAfterGear.Add(cid);
            }

            if ((_accepted.TryGetValue(cid, out var taken) && taken == fingerprint) ||
                (_rejected.TryGetValue(cid, out var refused) && refused == fingerprint))
            {
                return new CurrencyReport(CurrencyOutcome.SkippedUnchanged);
            }

            if (trigger == CurrencyTrigger.Change && now - _lastSendUtc < ChangeInterval)
            {
                return new CurrencyReport(CurrencyOutcome.SkippedThrottled);
            }

            while (_sends.Count > 0 && now - _sends.Peek() >= TimeSpan.FromHours(1))
            {
                _sends.Dequeue();
            }

            if (_sends.Count >= CurrencyProtocol.MaxCallsPerHour)
            {
                _backoffUntilUtc = _sends.Peek() + TimeSpan.FromHours(1);
                return new CurrencyReport(CurrencyOutcome.SkippedBackoff);
            }

            _sends.Enqueue(now);
            _lastSendUtc = now;
        }

        var request = new CurrencyStockRequest { Character = snapshot.Character, Stock = stock };
        var result = await _api.PushCurrenciesAsync(key, request, ct).ConfigureAwait(false);
        return Handle(result, cid, fingerprint, key, trigger);
    }

    private CurrencyReport Handle(ApiResult<CurrencyStockResult> result, string cid, string fingerprint, string key, CurrencyTrigger trigger)
    {
        if (result.IsSuccess)
        {
            lock (_gate)
            {
                _accepted[cid] = fingerprint;
                _rejected.Remove(cid);
                _retriedAfterGear.Remove(cid);
            }

            _directory?.Record(cid, result.Value?.CharacterId);
            var rows = result.Value?.Rows ?? 0;
            _log.Info($"Currency sync: stored, {rows} row(s) above zero ({trigger}).");
            return new CurrencyReport(CurrencyOutcome.Sent, Rows: rows);
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

                _log.Warning($"Currency sync stopped: {error.StatusCode} until another key is connected{tail}.");
                return new CurrencyReport(CurrencyOutcome.Stopped, ErrorKind: error.Kind, RequestId: error.RequestId);

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
                    ? $"Currency sync: the server still does not know the character after a gear sync; not trying again this session{tail}."
                    : $"Currency sync: the server does not know the character yet; sending once more after the next gear sync{tail}.");
                return new CurrencyReport(CurrencyOutcome.WaitingForGear, ErrorKind: error.Kind, RequestId: error.RequestId);

            case ApiErrorKind.RateLimited:
                var wait = error.RetryAfter is { } retry && retry > RateLimitWait ? retry : RateLimitWait;
                lock (_gate)
                {
                    _backoffUntilUtc = _clock.UtcNow + wait;
                }

                _log.Info($"Currency sync: limit reached, waiting {wait.TotalMinutes:0} min{tail}.");
                return new CurrencyReport(CurrencyOutcome.SkippedBackoff, ErrorKind: error.Kind, RequestId: error.RequestId);

            case ApiErrorKind.BadRequest or ApiErrorKind.Validation:
                lock (_gate)
                {
                    _rejected[cid] = fingerprint;
                }

                _log.Warning($"Currency sync refused: {error.StatusCode}; the same counts are not sent again{tail}.");
                return new CurrencyReport(CurrencyOutcome.Rejected, ErrorKind: error.Kind, RequestId: error.RequestId);

            default:
                _log.Info($"Currency sync failed: {error.Kind}{tail}.");
                return new CurrencyReport(CurrencyOutcome.Failed, ErrorKind: error.Kind, RequestId: error.RequestId);
        }
    }

    /// <summary>One row per listed id, ascending, a missing or negative count as 0, at most the server's row limit.</summary>
    private static List<CurrencyStockRow> BuildStock(IReadOnlyList<int> ids, IReadOnlyDictionary<int, int> counts) =>
        [.. ids.Where(id => id > 0)
            .Distinct()
            .Order()
            .Take(CurrencyProtocol.MaxRows)
            .Select(id => new CurrencyStockRow { ItemId = id, Count = counts.TryGetValue(id, out var n) && n > 0 ? n : 0 })];

    private static string Fingerprint(List<CurrencyStockRow> stock) =>
        string.Join(',', stock.Select(row => $"{row.ItemId}:{row.Count}"));

    private async Task RunAsync(CurrencyTrigger trigger)
    {
        var token = _cts.Token; // captured once: stays cancelled after Dispose
        while (true)
        {
            CurrencyReport? report;
            try
            {
                report = await SyncAsync(trigger, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report = null;
            }
            catch (Exception ex)
            {
                // P2: never let an exception escape a background task.
                _log.Error($"Unexpected currency sync error: {ex.GetType().Name}.");
                report = new CurrencyReport(CurrencyOutcome.Failed, ErrorKind: ApiErrorKind.Unexpected);
            }

            if (report is { } done)
            {
                try
                {
                    Completed?.Invoke(done);
                }
                catch (Exception ex)
                {
                    _log.Error($"Currency report handler failed: {ex.GetType().Name}.");
                }
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
