using System.Security.Cryptography;
using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;

namespace EorzeaArsenal.Core;

/// <summary>What caused a push to be requested.</summary>
public enum PushTrigger
{
    /// <summary>The user ran <c>/bisexport</c>. Bypasses the unchanged/throttle guards (but not back-off).</summary>
    Manual,

    /// <summary>The character logged in.</summary>
    Login,

    /// <summary>A gearset changed in-game.</summary>
    GearsetChange,

    /// <summary>The periodic auto-push timer fired.</summary>
    Auto,
}

/// <summary>The classified outcome of a push attempt.</summary>
public enum PushOutcome
{
    /// <summary>Gear was sent and accepted.</summary>
    Sent,

    /// <summary>Nothing changed since the last successful push.</summary>
    SkippedUnchanged,

    /// <summary>Too soon after the previous push (automatic trigger).</summary>
    SkippedThrottled,

    /// <summary>Currently backing off after a 429.</summary>
    SkippedBackoff,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>Not logged in / gear not readable.</summary>
    NotLoggedIn,

    /// <summary>There were no gearsets to send.</summary>
    Nothing,

    /// <summary>The local data failed client-side validation; nothing was sent (R18).</summary>
    InvalidLocal,

    /// <summary>The server rejected the push (see <see cref="PushReport.ErrorKind"/>).</summary>
    Failed,
}

/// <summary>The result of a push attempt, surfaced via <see cref="GearSyncService.PushCompleted"/>.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="GearsetCount">Number of gearsets accepted (on <see cref="PushOutcome.Sent"/>).</param>
/// <param name="ErrorKind">The API error kind (on <see cref="PushOutcome.Failed"/>).</param>
/// <param name="RequestId">Server correlation id, if any — safe to log/show.</param>
/// <param name="Detail">Optional extra detail (never a secret/body).</param>
public readonly record struct PushReport(
    PushOutcome Outcome,
    int? GearsetCount = null,
    ApiErrorKind? ErrorKind = null,
    string? RequestId = null,
    string? Detail = null);

/// <summary>
/// Orchestrates reading gear and pushing it, enforcing the plugin's hard runtime rules:
/// <list type="bullet">
/// <item><b>P11 — single in-flight push.</b> At most one <c>PUT /gear</c> runs at a time;
/// overlapping triggers coalesce into one pending push and the latest snapshot wins.</item>
/// <item><b>R23 — proactive limits.</b> Automatic triggers are throttled to at most one push per
/// <see cref="MinAutoPushInterval"/>, unchanged data is not re-sent, and a 429 sets a back-off
/// window honored by all triggers.</item>
/// <item><b>R18 — validate before sending.</b> The sanitized payload must pass
/// <see cref="GearValidator"/> or nothing is sent.</item>
/// </list>
/// All work runs off the framework thread; the game read is marshalled inside the
/// <see cref="IGearSource"/> implementation (P1).
/// </summary>
public sealed class GearSyncService : IDisposable
{
    private readonly IGearSource _gearSource;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private bool _running;
    private bool _pending;
    private PushTrigger _pendingTrigger;

    private DateTimeOffset _lastPushUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _backoffUntilUtc = DateTimeOffset.MinValue;
    private string? _lastSentHash;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="gearSource">Reads gear from the game.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GearSyncService(IGearSource gearSource, IApiClient api, ITokenStore tokens, IClock clock, ILog? log = null)
    {
        _gearSource = gearSource;
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised after each push attempt completes (on a background thread).</summary>
    public event Action<PushReport>? PushCompleted;

    /// <summary>The most recent push report, or <see langword="null"/> if nothing has run yet.</summary>
    public PushReport? LastReport { get; private set; }

    /// <summary>When the last <i>successful</i> push happened, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSuccessfulPushUtc =>
        _lastPushUtc == DateTimeOffset.MinValue ? null : _lastPushUtc;

    /// <summary>The instant until which pushes are backing off after a 429 (UTC).</summary>
    public DateTimeOffset BackoffUntilUtc => _backoffUntilUtc;

    /// <summary>Whether a 429 back-off window is currently active.</summary>
    public bool IsRateLimited => _clock.UtcNow < _backoffUntilUtc;

    /// <summary>
    /// Minimum spacing between <i>periodic auto-pushes</i> (default 5 minutes). Manual, login and
    /// gearset-change pushes bypass it and send promptly (the 429 back-off still applies).
    /// </summary>
    public TimeSpan MinAutoPushInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Default back-off when a 429 carries no <c>Retry-After</c> (default 5 minutes).</summary>
    public TimeSpan DefaultBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Requests a push. Returns immediately. If a push is already running, this coalesces into a
    /// single pending push (latest trigger wins) instead of starting a second one (P11).
    /// </summary>
    /// <param name="trigger">What caused the request.</param>
    public void RequestPush(PushTrigger trigger)
    {
        lock (_gate)
        {
            if (_running)
            {
                _pending = true;
                // Manual intent is "stickiest": once requested, keep it for the pending run.
                if (trigger == PushTrigger.Manual || _pendingTrigger == PushTrigger.Manual)
                {
                    _pendingTrigger = PushTrigger.Manual;
                }
                else
                {
                    _pendingTrigger = trigger;
                }

                return;
            }

            _running = true;
        }

        _ = Task.Run(() => RunLoopAsync(trigger));
    }

    private async Task RunLoopAsync(PushTrigger trigger)
    {
        var current = trigger;
        var token = _cts.Token; // captured once: stays cancelled after Dispose, never reads a disposed CTS
        while (true)
        {
            PushReport report;
            try
            {
                report = await PushOnceAsync(current, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report = new PushReport(PushOutcome.Failed, ErrorKind: ApiErrorKind.Network, Detail: "Cancelled.");
            }
            catch (Exception ex)
            {
                // P2: never let an exception escape into the game; report and continue draining.
                _log.Error($"Unexpected push error: {ex.GetType().Name}.");
                report = new PushReport(PushOutcome.Failed, ErrorKind: ApiErrorKind.Unexpected);
            }

            RaiseCompleted(report);

            lock (_gate)
            {
                if (_pending)
                {
                    _pending = false;
                    current = _pendingTrigger;
                    continue;
                }

                _running = false;
                return;
            }
        }
    }

    private async Task<PushReport> PushOnceAsync(PushTrigger trigger, CancellationToken ct)
    {
        // Event-driven triggers (manual, login, gearset change) push promptly; only the periodic
        // auto-push is throttled by MinAutoPushInterval. The 429 back-off still applies to all.
        var force = trigger != PushTrigger.Auto;
        var now = _clock.UtcNow;

        if (now < _backoffUntilUtc)
        {
            return new PushReport(PushOutcome.SkippedBackoff);
        }

        if (!force && now - _lastPushUtc < MinAutoPushInterval)
        {
            return new PushReport(PushOutcome.SkippedThrottled);
        }

        if (!_tokens.HasKey)
        {
            return new PushReport(PushOutcome.NotConnected);
        }

        if (!_gearSource.IsAvailable)
        {
            return new PushReport(PushOutcome.NotLoggedIn);
        }

        var snapshot = await _gearSource.ReadAsync(ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new PushReport(PushOutcome.NotLoggedIn);
        }

        var clean = GearSanitizer.Sanitize(snapshot);
        if (clean.Gearsets.Count == 0)
        {
            return new PushReport(PushOutcome.Nothing);
        }

        var payload = GearPayload.From(clean);
        var validation = GearValidator.Validate(payload);
        if (!validation.IsValid)
        {
            _log.Warning($"Local validation failed; not sending. {validation.Errors.Count} issue(s).");
            return new PushReport(PushOutcome.InvalidLocal, Detail: string.Join("; ", validation.Errors));
        }

        var hash = Fingerprint(clean);
        if (!force && hash == _lastSentHash)
        {
            return new PushReport(PushOutcome.SkippedUnchanged);
        }

        var result = await _api.PushGearAsync(_tokens.ApiKey!, payload, ct).ConfigureAwait(false);
        return HandleResult(result, hash);
    }

    private PushReport HandleResult(ApiResult<GearPushResult> result, string hash)
    {
        if (result.IsSuccess)
        {
            _lastPushUtc = _clock.UtcNow;
            _lastSentHash = hash;
            var count = result.Value!.Gearsets;
            _log.Info($"Push OK: {count} gearset(s).");
            return new PushReport(PushOutcome.Sent, GearsetCount: count);
        }

        var error = result.Error!;
        if (error.Kind == ApiErrorKind.RateLimited)
        {
            var backoff = error.RetryAfter ?? DefaultBackoff;
            _backoffUntilUtc = _clock.UtcNow + backoff;
            _log.Warning($"Rate limited; backing off for {backoff.TotalSeconds:F0}s. request_id={error.RequestId}.");
        }
        else
        {
            _log.Warning($"Push failed: {error.Kind} (HTTP {error.StatusCode}) {error.Endpoint}. request_id={error.RequestId}.");
        }

        return new PushReport(PushOutcome.Failed, ErrorKind: error.Kind, RequestId: error.RequestId, Detail: error.Message);
    }

    private void RaiseCompleted(PushReport report)
    {
        LastReport = report;
        try
        {
            PushCompleted?.Invoke(report);
        }
        catch (Exception ex)
        {
            _log.Error($"PushCompleted handler threw: {ex.GetType().Name}.");
        }
    }

    private static string Fingerprint(GearData data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, EorzeaJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Cancels any in-flight push on plugin unload (P3). The service is single-use after this — the
    /// token is <b>not</b> replaced, so a still-draining loop sees a cancelled token (clean stop)
    /// instead of continuing against a disposed <see cref="HttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
