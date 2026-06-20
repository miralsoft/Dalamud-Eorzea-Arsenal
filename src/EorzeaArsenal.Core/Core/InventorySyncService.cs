using System.Security.Cryptography;
using System.Text;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What caused an inventory upload to be requested.</summary>
public enum InventoryTrigger
{
    /// <summary>The user pressed "sync inventory". Forces a send of the character scope.</summary>
    Manual,

    /// <summary>The character logged in.</summary>
    Login,

    /// <summary>The periodic auto-sync timer fired (throttled, skips unchanged).</summary>
    Auto,

    /// <summary>A retainer's inventory was scanned (forces a send of that retainer scope).</summary>
    Retainer,
}

/// <summary>The classified outcome of an inventory upload attempt.</summary>
public enum InventoryOutcome
{
    /// <summary>Items were sent and accepted.</summary>
    Sent,

    /// <summary>Nothing changed since the last successful upload of the scanned scopes.</summary>
    SkippedUnchanged,

    /// <summary>Too soon after the previous auto-sync.</summary>
    SkippedThrottled,

    /// <summary>Currently backing off after a 429.</summary>
    SkippedBackoff,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>Not logged in / inventory not readable.</summary>
    NotLoggedIn,

    /// <summary>There was nothing to send.</summary>
    Nothing,

    /// <summary>The local data failed client-side validation; nothing was sent (R18).</summary>
    InvalidLocal,

    /// <summary>The server rejected the upload (see <see cref="InventoryReport.ErrorKind"/>).</summary>
    Failed,
}

/// <summary>The result of an inventory upload attempt, surfaced via <see cref="InventorySyncService.SyncCompleted"/>.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="ItemCount">Number of items accepted (on <see cref="InventoryOutcome.Sent"/>).</param>
/// <param name="ScopeCount">Number of scopes sent (on <see cref="InventoryOutcome.Sent"/>).</param>
/// <param name="ErrorKind">The API error kind (on <see cref="InventoryOutcome.Failed"/>).</param>
/// <param name="RequestId">Server correlation id, if any — safe to log/show.</param>
/// <param name="Detail">Optional extra detail (never a secret/body).</param>
public readonly record struct InventoryReport(
    InventoryOutcome Outcome,
    int? ItemCount = null,
    int? ScopeCount = null,
    ApiErrorKind? ErrorKind = null,
    string? RequestId = null,
    string? Detail = null);

/// <summary>
/// Orchestrates reading owned items and uploading them scope-accurately via <c>POST /inventory</c>.
/// Enforces the same hard rules as the gear push: a single in-flight upload (P11) with scope-keyed
/// coalescing (the latest scan of a scope wins, multiple scopes bundle into one upload), proactive
/// limits (auto-syncs throttled, unchanged scopes skipped, a 429 sets a back-off honored by all),
/// and validate-before-send (R18). Each scope is atomic across requests — chunking never splits a
/// scope — so a later request can never wipe what an earlier one stored. All game reads are
/// marshalled inside the <see cref="IInventorySource"/>; this service is pure and unit-tested.
/// </summary>
public sealed class InventorySyncService : IDisposable
{
    private readonly IInventorySource _source;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, PendingScope> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sentHashes = new(StringComparer.Ordinal);
    private CharacterDto? _pendingCharacter;
    private bool _running;

    private DateTimeOffset _lastPushUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _backoffUntilUtc = DateTimeOffset.MinValue;
    private CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="source">Reads owned items from the game.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public InventorySyncService(IInventorySource source, IApiClient api, ITokenStore tokens, IClock clock, ILog? log = null)
    {
        _source = source;
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised after each upload attempt completes (on a background thread).</summary>
    public event Action<InventoryReport>? SyncCompleted;

    /// <summary>The most recent upload report, or <see langword="null"/> if nothing has run yet.</summary>
    public InventoryReport? LastReport { get; private set; }

    /// <summary>When the last <i>successful</i> upload happened, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSuccessfulSyncUtc =>
        _lastPushUtc == DateTimeOffset.MinValue ? null : _lastPushUtc;

    /// <summary>Whether a 429 back-off window is currently active.</summary>
    public bool IsRateLimited => _clock.UtcNow < _backoffUntilUtc;

    /// <summary>Minimum spacing between periodic auto-syncs (default 15 minutes). Other triggers bypass it.</summary>
    public TimeSpan MinAutoSyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Default back-off when a 429 carries no <c>Retry-After</c> (default 5 minutes).</summary>
    public TimeSpan DefaultBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Back-off after a transient network failure before retrying (default 1 minute).</summary>
    public TimeSpan NetworkBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Requests an upload of the <c>character</c> scope (all locally readable storages). Returns
    /// immediately; reads the game off-thread, then coalesces into the single in-flight upload.
    /// </summary>
    /// <param name="trigger">What caused the request.</param>
    public void RequestCharacterSync(InventoryTrigger trigger) =>
        _ = Task.Run(() => PrepareCharacterAsync(trigger));

    /// <summary>
    /// Requests an upload of an already-scanned snapshot (e.g. an opened retainer). The snapshot is
    /// sanitized and forced (a retainer is only scanned when visited), then coalesced.
    /// </summary>
    /// <param name="data">The scanned snapshot (its scopes are never <c>manual</c>).</param>
    public void RequestScopeSync(InventoryData data)
    {
        if (!_tokens.HasKey)
        {
            Raise(new InventoryReport(InventoryOutcome.NotConnected));
            return;
        }

        var clean = InventorySanitizer.Sanitize(data);
        if (clean.Scopes.Count == 0)
        {
            Raise(new InventoryReport(InventoryOutcome.Nothing));
            return;
        }

        Enqueue(clean, force: true);
        Kick();
    }

    private async Task PrepareCharacterAsync(InventoryTrigger trigger)
    {
        var force = trigger != InventoryTrigger.Auto;
        var now = _clock.UtcNow;

        if (now < _backoffUntilUtc)
        {
            Raise(new InventoryReport(InventoryOutcome.SkippedBackoff));
            return;
        }

        if (!force && now - _lastPushUtc < MinAutoSyncInterval)
        {
            Raise(new InventoryReport(InventoryOutcome.SkippedThrottled));
            return;
        }

        if (!_tokens.HasKey)
        {
            Raise(new InventoryReport(InventoryOutcome.NotConnected));
            return;
        }

        if (!_source.IsAvailable)
        {
            Raise(new InventoryReport(InventoryOutcome.NotLoggedIn));
            return;
        }

        InventoryData? data;
        try
        {
            data = await _source.ReadCharacterAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // P2: never let an exception escape; report and move on.
            _log.Error($"Inventory read failed: {ex.GetType().Name}.");
            Raise(new InventoryReport(InventoryOutcome.Failed, ErrorKind: ApiErrorKind.Unexpected));
            return;
        }

        if (data is null)
        {
            Raise(new InventoryReport(InventoryOutcome.NotLoggedIn));
            return;
        }

        Enqueue(InventorySanitizer.Sanitize(data), force);
        Kick();
    }

    private void Enqueue(InventoryData data, bool force)
    {
        lock (_gate)
        {
            _pendingCharacter = data.Character;

            // Ensure every scanned scope has a bucket — even an empty one, so a sold-out storage
            // is still reported (and clears server-side).
            foreach (var scope in data.Scopes)
            {
                _pending[scope] = new PendingScope(force);
            }

            foreach (var item in data.Items)
            {
                var scope = InventoryProtocol.ScopeForItem(item);
                if (_pending.TryGetValue(scope, out var bucket))
                {
                    bucket.Items.Add(item);
                }
            }
        }
    }

    private void Kick()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _running = true;
        }

        _ = Task.Run(RunLoopAsync);
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            CharacterDto character;
            List<(string Scope, List<InventoryItemDto> Items, bool Force)> batch;

            lock (_gate)
            {
                if (_pending.Count == 0 || _pendingCharacter is null)
                {
                    _running = false;
                    return;
                }

                // Honor an active back-off (after a 429/network error): stop the loop and keep the
                // pending scopes for a later trigger, instead of tight-looping on the same failure.
                if (_clock.UtcNow < _backoffUntilUtc)
                {
                    _running = false;
                    return;
                }

                character = _pendingCharacter;
                batch = [.. _pending.Select(kv => (kv.Key, kv.Value.Items, kv.Value.Force))];
                _pending.Clear();
            }

            InventoryReport report;
            try
            {
                report = await PushBatchAsync(character, batch, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report = new InventoryReport(InventoryOutcome.Failed, ErrorKind: ApiErrorKind.Network, Detail: "Cancelled.");
            }
            catch (Exception ex)
            {
                _log.Error($"Unexpected inventory upload error: {ex.GetType().Name}.");
                report = new InventoryReport(InventoryOutcome.Failed, ErrorKind: ApiErrorKind.Unexpected);
            }

            Raise(report);
        }
    }

    private async Task<InventoryReport> PushBatchAsync(
        CharacterDto character,
        List<(string Scope, List<InventoryItemDto> Items, bool Force)> batch,
        CancellationToken ct)
    {
        if (!_tokens.HasKey)
        {
            return new InventoryReport(InventoryOutcome.NotConnected);
        }

        // Skip scopes whose contents are unchanged since the last successful upload (auto-sync
        // budget guard); forced scopes always send.
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var send = new List<(string Scope, List<InventoryItemDto> Items)>();
        foreach (var (scope, items, force) in batch)
        {
            var hash = HashScope(items);
            hashes[scope] = hash;
            if (!force && _sentHashes.TryGetValue(scope, out var prev) && prev == hash)
            {
                continue;
            }

            send.Add((scope, items));
        }

        if (send.Count == 0)
        {
            return new InventoryReport(InventoryOutcome.SkippedUnchanged);
        }

        var data = new InventoryData
        {
            Character = character,
            Scopes = [.. send.Select(s => s.Scope)],
            Items = [.. send.SelectMany(s => s.Items)],
        };

        var chunks = InventoryChunker.Split(data);
        var sentScopes = 0;
        var sentItems = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var payload = InventoryPayload.From(chunk);

            var validation = InventoryValidator.Validate(payload);
            if (!validation.IsValid)
            {
                // Terminal: the data will not become valid on retry, so drop it (don't requeue → no spin).
                _log.Warning($"Inventory validation failed; not sending. {validation.Errors.Count} issue(s).");
                return new InventoryReport(InventoryOutcome.InvalidLocal, Detail: string.Join("; ", validation.Errors));
            }

            var result = await _api.PushInventoryAsync(_tokens.ApiKey!, payload, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                var error = result.Error!;
                // Retry transient failures later (keep the data); drop terminal ones (auth/validation/
                // conflict/bad-request) so the loop cannot spin on an error retrying won't fix.
                if (error.Kind is ApiErrorKind.RateLimited or ApiErrorKind.Network)
                {
                    RequeueScopes(character, chunks, i);
                }

                return HandleError(error);
            }

            foreach (var scope in chunk.Scopes)
            {
                if (hashes.TryGetValue(scope, out var hash))
                {
                    _sentHashes[scope] = hash;
                }
            }

            sentScopes += chunk.Scopes.Count;
            sentItems += result.Value!.Items;
        }

        _lastPushUtc = _clock.UtcNow;
        _log.Info($"Inventory OK: {sentItems} item(s) across {sentScopes} scope(s).");
        return new InventoryReport(InventoryOutcome.Sent, ItemCount: sentItems, ScopeCount: sentScopes);
    }

    private InventoryReport HandleError(ApiError error)
    {
        if (error.Kind is ApiErrorKind.RateLimited or ApiErrorKind.Network)
        {
            var backoff = error.Kind == ApiErrorKind.RateLimited ? error.RetryAfter ?? DefaultBackoff : NetworkBackoff;
            _backoffUntilUtc = _clock.UtcNow + backoff;
            _log.Warning($"Inventory upload backing off for {backoff.TotalSeconds:F0}s after {error.Kind}. request_id={error.RequestId}.");
        }
        else
        {
            _log.Warning($"Inventory upload failed: {error.Kind} (HTTP {error.StatusCode}) {error.Endpoint}. request_id={error.RequestId}.");
        }

        return new InventoryReport(InventoryOutcome.Failed, ErrorKind: error.Kind, RequestId: error.RequestId, Detail: error.Message);
    }

    /// <summary>Re-buffers the scopes from <paramref name="fromChunk"/> onward so a later run retries them.</summary>
    private void RequeueScopes(CharacterDto character, IReadOnlyList<InventoryData> chunks, int fromChunk)
    {
        var remaining = new List<(string Scope, List<InventoryItemDto> Items, bool Force)>();
        for (var i = fromChunk; i < chunks.Count; i++)
        {
            foreach (var scope in chunks[i].Scopes)
            {
                var items = chunks[i].Items.Where(it => InventoryProtocol.ScopeForItem(it) == scope).ToList();
                remaining.Add((scope, items, true));
            }
        }

        Requeue(character, remaining);
    }

    /// <summary>Restores un-sent scopes to the pending buffer unless newer data already replaced them.</summary>
    private void Requeue(CharacterDto character, List<(string Scope, List<InventoryItemDto> Items, bool Force)> batch)
    {
        lock (_gate)
        {
            _pendingCharacter ??= character;
            foreach (var (scope, items, force) in batch)
            {
                if (_pending.ContainsKey(scope))
                {
                    continue; // a newer scan of this scope is already queued; don't clobber it
                }

                var bucket = new PendingScope(force);
                bucket.Items.AddRange(items);
                _pending[scope] = bucket;
            }
        }
    }

    private static string HashScope(List<InventoryItemDto> items)
    {
        // Order-independent: canonicalize each item, sort, then hash.
        var lines = items
            .Select(i => $"{i.ItemId}|{i.Container}|{i.SourceId}|{(i.Hq ? 1 : 0)}|{i.Qty}")
            .OrderBy(s => s, StringComparer.Ordinal);
        var joined = string.Join("\n", lines);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private void Raise(InventoryReport report)
    {
        LastReport = report;
        try
        {
            SyncCompleted?.Invoke(report);
        }
        catch (Exception ex)
        {
            _log.Error($"SyncCompleted handler threw: {ex.GetType().Name}.");
        }
    }

    /// <summary>Cancels any in-flight upload. Safe to call on plugin unload (P3).</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private sealed class PendingScope(bool force)
    {
        public List<InventoryItemDto> Items { get; } = [];

        public bool Force { get; } = force;
    }
}
