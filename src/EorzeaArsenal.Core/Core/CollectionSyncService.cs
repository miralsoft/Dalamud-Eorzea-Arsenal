using System.Globalization;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What asked for a collection sync. A later member outranks an earlier one when two coalesce.</summary>
public enum CollectionTrigger
{
    /// <summary>The game said something was learned: only the changed items, at most every five minutes.</summary>
    Change,

    /// <summary>The regular look: once a session per character, the server's counts decide which kinds go in full.</summary>
    Check,

    /// <summary>A gear sync went through; a character the server did not know is tried once more.</summary>
    AfterGearSync,

    /// <summary>Every kind in full: the collection was just turned on, or "Sammlung neu abgleichen".</summary>
    Full,
}

/// <summary>The classified outcome of a collection sync attempt.</summary>
public enum CollectionOutcome
{
    /// <summary>At least one report was stored.</summary>
    Sent,

    /// <summary>Nothing was due.</summary>
    NothingDue,

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

    /// <summary>The server does not know the character yet (404); waiting for a gear sync.</summary>
    WaitingForGear,

    /// <summary>Any other failure; a later look tries again.</summary>
    Failed,
}

/// <summary>
/// The whole collection (server 1.5, <c>unlock-collection</c> in <c>contract-goals.md</c>): every item of the
/// game that unlocks a mount, minion, orchestrion roll or Triple Triad card, learned or not, one kind a call.
/// Only with its own consent; without it the goals mode (<see cref="UnlockSyncService"/>) reports the goals.
/// </summary>
/// <remarks>
/// <para>
/// A kind goes in full (<c>full: true</c>) when this machine has no full report of it for the character, at
/// the first look of a UTC day, when the player asks for it, and when the server's count differs from the
/// one it answered after this machine's last report (the website changed something). Between those, only
/// the items whose learned state changed go up, with <c>kind</c> and without <c>full</c>, at most every
/// <see cref="ChangeInterval"/>. "Changed" is measured against the learned ids the server last accepted
/// from this machine (<see cref="CollectionKindRecord.Learned"/>, kept in the config), so the session's
/// first look also sends what was learned while the plugin was off or before a restart.
/// </para>
/// <para>
/// The answers follow the contract: a 404 waits for the next gear sync and is tried once more, never in a
/// loop; a 429 waits for its <c>Retry-After</c>; a 401 or 403 stops until another key is connected; a 400 or
/// 422 skips that kind for the day. The limit of 30 calls an hour is counted here too (R23). Single
/// in-flight (P11).
/// </para>
/// <para>
/// The plugin never deletes the collection on the server, also not when the player turns it off (the
/// maintainer's decision, 2026-11-27): <c>DELETE /me/unlocks</c> belongs to the website's "Sammlung löschen"
/// and answers a key with 403.
/// </para>
/// </remarks>
public sealed class CollectionSyncService : IDisposable
{
    private readonly ICollectionSource _source;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly Func<Dictionary<string, Dictionary<string, CollectionKindRecord>>> _records;
    private readonly Action _persist;
    private readonly ILog _log;

    private readonly SemaphoreSlim _single = new(1, 1);
    private readonly Lock _gate = new();
    private readonly HashSet<string> _lookedThisSession = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedToday = new(StringComparer.Ordinal);
    private readonly HashSet<string> _awaitingGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retriedAfterGear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gaveUp = new(StringComparer.Ordinal);
    private DateTimeOffset _lastChangeUtc = DateTimeOffset.MinValue;
    private string? _stoppedForKey;
    private bool _running;
    private CollectionTrigger? _queued;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="source">Reads the collection from the game.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="records">What this machine remembers per character and kind (kept in the config).</param>
    /// <param name="persist">Saves <paramref name="records"/> after a change.</param>
    /// <param name="log">Diagnostics sink.</param>
    public CollectionSyncService(
        ICollectionSource source,
        IApiClient api,
        ITokenStore tokens,
        IClock clock,
        Func<Dictionary<string, Dictionary<string, CollectionKindRecord>>> records,
        Action persist,
        ILog? log = null)
    {
        _source = source;
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _records = records;
        _persist = persist;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The least time between two sends for a change (default five minutes).</summary>
    public TimeSpan ChangeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The hourly limit of <c>PUT /me/unlocks</c>, shared with the goals mode (the server counts both together).</summary>
    public UnlockCallBudget Budget { get; set; } = new();

    /// <summary>How long a 429 without <c>Retry-After</c> holds every call (default one minute).</summary>
    public TimeSpan DefaultBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The kinds the server takes: the first four, or all ten where it lists <c>unlock-kinds</c>. A kind new to
    /// this machine has no record yet, so its first look sends it in full.
    /// </summary>
    public Func<IReadOnlyList<string>> Kinds { get; set; } = () => UnlockKinds.All;

    /// <summary>Asks for an attempt; returns at once.</summary>
    /// <param name="trigger">What asked.</param>
    public void Request(CollectionTrigger trigger)
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

    /// <summary>A new game session: every character gets its look again, the first one of the day decides.</summary>
    public void StartSession()
    {
        lock (_gate)
        {
            _lookedThisSession.Clear();
            _rejectedToday.Clear();
            _awaitingGear.Clear();
            _retriedAfterGear.Clear();
            _gaveUp.Clear();
        }
    }

    /// <summary>
    /// Forgets what this machine remembers, never anything on the server. Called when another key is connected
    /// (a new pairing) and when the collection is turned off, so the next time it runs every character gets a
    /// first sync again, as the contract asks after pairing and after turning it on. What was reported stays on
    /// the server: an unlock once learned stays learned, and deleting it is the player's, on the website.
    /// </summary>
    public void ForgetLocalState()
    {
        StartSession();
        _records().Clear();
        _persist();
    }

    /// <summary>One attempt, awaited; for tests. <see cref="Request"/> is the plugin's entry point.</summary>
    /// <param name="trigger">What asked.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<CollectionOutcome> SyncAsync(CollectionTrigger trigger, CancellationToken ct)
    {
        await _single.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SyncCoreAsync(trigger, ct).ConfigureAwait(false);
        }
        finally
        {
            _single.Release();
        }
    }

    private async Task<CollectionOutcome> SyncCoreAsync(CollectionTrigger trigger, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return CollectionOutcome.NotConnected;
        }

        lock (_gate)
        {
            if (_stoppedForKey is not null)
            {
                if (string.Equals(_stoppedForKey, key, StringComparison.Ordinal))
                {
                    return CollectionOutcome.Stopped;
                }

                _stoppedForKey = null;
            }
        }

        if (Budget.IsHeld(_clock.UtcNow))
        {
            return CollectionOutcome.SkippedBackoff;
        }

        if (!_source.IsAvailable)
        {
            return CollectionOutcome.NotLoggedIn;
        }

        // Which character: the snapshot names it, so the first read takes every kind (cheap, no network).
        CollectionSnapshot? snapshot;
        try
        {
            snapshot = await _source.ReadAsync(Kinds(), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Collection read failed: {ex.GetType().Name}.");
            return CollectionOutcome.NotLoggedIn;
        }

        if (snapshot is null || string.IsNullOrEmpty(snapshot.Character.CidHash))
        {
            return CollectionOutcome.NotLoggedIn;
        }

        var cid = snapshot.Character.CidHash;
        lock (_gate)
        {
            if (_gaveUp.Contains(cid))
            {
                return CollectionOutcome.WaitingForGear;
            }

            if (_awaitingGear.Contains(cid))
            {
                if (trigger != CollectionTrigger.AfterGearSync)
                {
                    return CollectionOutcome.WaitingForGear;
                }

                _awaitingGear.Remove(cid);
                _retriedAfterGear.Add(cid);
                _lookedThisSession.Remove(cid);
            }
        }

        if (trigger == CollectionTrigger.Change)
        {
            return await SendChangesAsync(key, snapshot, skipKinds: [], throttled: true, ct).ConfigureAwait(false);
        }

        bool firstLook;
        lock (_gate)
        {
            firstLook = !_lookedThisSession.Contains(cid);
        }

        var due = await KindsDueAsync(key, cid, trigger, ct).ConfigureAwait(false);
        if (due is null)
        {
            return OutcomeOfLastHold(cid);
        }

        var sent = false;
        foreach (var kind in due)
        {
            if (!snapshot.Kinds.TryGetValue(kind, out var read) || read.Items.Count == 0)
            {
                continue;
            }

            var result = await SendAsync(key, snapshot.Character, kind, read.Items, read.Learned, full: true, ct).ConfigureAwait(false);
            if (result is null)
            {
                return OutcomeOfLastHold(cid);
            }

            sent |= result.Value;
        }

        // A look whose counts matched is the session's look too; without this the next minute asks again.
        lock (_gate)
        {
            _lookedThisSession.Add(cid);
        }

        // Every look also sends what the game learned since the server last accepted a kind from here, the kinds
        // not just sent in full, changes only. The session's first look at once (learned while the plugin was off,
        // or before a restart); the later ones of every minute within the five minutes, which is what carries a
        // change that came too soon after the previous one.
        var throttled = !firstLook && trigger != CollectionTrigger.Full;
        var changes = await SendChangesAsync(key, snapshot, skipKinds: due, throttled, ct).ConfigureAwait(false);
        if (changes is not (CollectionOutcome.Sent or CollectionOutcome.NothingDue))
        {
            return changes;
        }

        return sent || changes == CollectionOutcome.Sent ? CollectionOutcome.Sent : CollectionOutcome.NothingDue;
    }

    /// <summary>The kinds that go in full, or <see langword="null"/> when a hold or a 404 ended the look.</summary>
    private async Task<List<string>?> KindsDueAsync(string key, string cid, CollectionTrigger trigger, CancellationToken ct)
    {
        var today = Today();
        var records = _records();
        records.TryGetValue(cid, out var mine);
        lock (_gate)
        {
            if (trigger != CollectionTrigger.Full && _lookedThisSession.Contains(cid))
            {
                return [];
            }
        }

        if (trigger == CollectionTrigger.Full || mine is null || mine.Count == 0)
        {
            return [.. Kinds().Where(k => !_rejectedToday.Contains($"{cid}|{k}"))];
        }

        // The look of the session: the server's counts against those it answered after this machine's reports.
        var state = await _api.GetMyUnlocksAsync(key, cid, ct).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            HandleHold(state.Error!, key, "check", cid);
            return null;
        }

        var server = state.Value?.Data?.Kinds ?? [];
        var due = new List<string>();
        foreach (var kind in Kinds())
        {
            if (_rejectedToday.Contains($"{cid}|{kind}"))
            {
                continue;
            }

            var known = mine.TryGetValue(kind, out var record) ? record : null;
            var differs = !server.TryGetValue(kind, out var there) || known is null || there.Learned != known.ServerLearned;
            if (known is null || known.FullDay != today || differs)
            {
                due.Add(kind);
            }
        }

        _log.Info(due.Count == 0
            ? "Collection: the server's counts match; nothing to sync in full."
            : $"Collection: syncing in full: {string.Join(", ", due)}.");
        return due;
    }

    /// <summary>Only what changed since the server last accepted a kind, one call per kind that changed.</summary>
    private async Task<CollectionOutcome> SendChangesAsync(string key, CollectionSnapshot snapshot, IReadOnlyCollection<string> skipKinds, bool throttled, CancellationToken ct)
    {
        var cid = snapshot.Character.CidHash;

        // What the server last accepted from this machine, kept in the config across sessions; a kind without
        // it waits for its next full report.
        if (!_records().TryGetValue(cid, out var mine))
        {
            return CollectionOutcome.NothingDue;
        }

        var pending = new List<(string Kind, List<int> Changed, List<int> Learned)>();
        foreach (var (kind, read) in snapshot.Kinds)
        {
            if (skipKinds.Contains(kind) || !mine.TryGetValue(kind, out var record) || record.Learned is not { } acceptedIds)
            {
                continue;
            }

            var before = acceptedIds.ToHashSet();
            var learnedNow = read.Learned.ToHashSet();
            var changed = read.Items.Where(id => learnedNow.Contains(id) != before.Contains(id)).ToList();
            if (changed.Count > 0)
            {
                pending.Add((kind, changed, [.. changed.Where(learnedNow.Contains)]));
            }
        }

        // The five minutes hold only a real change back; nothing changed is simply nothing to do.
        if (pending.Count == 0)
        {
            return CollectionOutcome.NothingDue;
        }

        var now = _clock.UtcNow;
        if (throttled && now - _lastChangeUtc < ChangeInterval)
        {
            return CollectionOutcome.SkippedThrottled;
        }

        var sent = false;
        foreach (var (kind, changed, learned) in pending)
        {
            _lastChangeUtc = now;
            var result = await SendAsync(key, snapshot.Character, kind, changed, learned, full: false, ct).ConfigureAwait(false);
            if (result is null)
            {
                return OutcomeOfLastHold(cid);
            }

            sent |= result.Value;
        }

        return sent ? CollectionOutcome.Sent : CollectionOutcome.NothingDue;
    }

    /// <summary>One report; <see langword="null"/> when a hold, a stop or a 404 ends the attempt.</summary>
    private async Task<bool?> SendAsync(string key, CharacterDto character, string kind, IReadOnlyList<int> items, IReadOnlyList<int> learned, bool full, CancellationToken ct)
    {
        if (!TakeCall())
        {
            return null;
        }

        var checkedIds = items.Where(id => id > 0).Distinct().Order().Take(UnlockProtocol.MaxChecked).ToList();
        var inChecked = checkedIds.ToHashSet();
        var learnedIds = learned.Where(inChecked.Contains).Distinct().Order().ToList();
        var request = new UnlocksRequest
        {
            Character = character,
            Kind = kind,
            Full = full ? true : null,
            Checked = checkedIds,
            Learned = learnedIds,
        };

        var cid = character.CidHash;
        var result = await _api.PutUnlocksAsync(key, request, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            lock (_gate)
            {
                _retriedAfterGear.Remove(cid);
            }

            var records = _records();
            if (!records.TryGetValue(cid, out var mine))
            {
                mine = [];
                records[cid] = mine;
            }

            var state = result.Value?.Kinds is { } answer && answer.TryGetValue(kind, out var found) ? found : null;
            var serverLearned = state?.Learned ?? learnedIds.Count;
            if (!mine.TryGetValue(kind, out var record))
            {
                record = new CollectionKindRecord();
                mine[kind] = record;
            }

            // What the server now holds from here: after a full report exactly what was sent, after a change the
            // kept set with the changed items applied (without a kept set the kind waits for its full report).
            if (full)
            {
                record.FullDay = Today();
                record.Learned = learnedIds;
            }
            else if (record.Learned is { } kept)
            {
                var set = kept.ToHashSet();
                var learnedSet = learnedIds.ToHashSet();
                foreach (var id in checkedIds)
                {
                    if (learnedSet.Contains(id))
                    {
                        set.Add(id);
                    }
                    else
                    {
                        set.Remove(id);
                    }
                }

                record.Learned = [.. set.Order()];
            }

            record.ServerLearned = serverLearned;
            _persist();
            // The answer's own counts too: how many ids the server kept and how many of the kind its catalog knows.
            var known = state is null ? "no state for the kind" : $"its catalog knows {state.Known}";
            _log.Info($"Collection: {kind} {(full ? "in full" : "changes")}, {learnedIds.Count} of {checkedIds.Count} learned, server kept {result.Value?.Checked ?? 0} and holds {serverLearned} ({known}).");
            return true;
        }

        var error = result.Error!;
        if (error.Kind is ApiErrorKind.BadRequest or ApiErrorKind.Validation)
        {
            lock (_gate)
            {
                _rejectedToday.Add($"{cid}|{kind}");
            }

            _log.Warning($"Collection: {kind} refused ({error.StatusCode}); not sent again today.");
            return false;
        }

        HandleHold(error, key, kind, cid);
        return null;
    }

    /// <summary>Counts a call against the hourly limit; <see langword="false"/> when the limit is reached.</summary>
    private bool TakeCall() => Budget.TryTake(_clock.UtcNow);

    private void HandleHold(ApiError error, string key, string what, string? cid = null)
    {
        var tail = error.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
        lock (_gate)
        {
            switch (error.Kind)
            {
                case ApiErrorKind.Unauthorized or ApiErrorKind.Forbidden:
                    _stoppedForKey = key;
                    break;
                case ApiErrorKind.RateLimited:
                    Budget.HoldUntil(_clock.UtcNow + (error.RetryAfter ?? DefaultBackoff));
                    break;
                case ApiErrorKind.NotFound when cid is not null:
                    if (_retriedAfterGear.Remove(cid))
                    {
                        _gaveUp.Add(cid);
                    }
                    else
                    {
                        _awaitingGear.Add(cid);
                    }

                    break;
            }
        }

        _log.Info($"Collection: {what} not done ({error.Kind}){tail}.");
    }

    private CollectionOutcome OutcomeOfLastHold(string cid)
    {
        lock (_gate)
        {
            return _stoppedForKey is not null ? CollectionOutcome.Stopped
                : _awaitingGear.Contains(cid) || _gaveUp.Contains(cid) ? CollectionOutcome.WaitingForGear
                : Budget.IsHeld(_clock.UtcNow) ? CollectionOutcome.SkippedBackoff
                : CollectionOutcome.Failed;
        }
    }

    private string Today() => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task RunAsync(CollectionTrigger trigger)
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
                _log.Error($"Unexpected collection sync error: {ex.GetType().Name}.");
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
        _single.Dispose();
    }
}
