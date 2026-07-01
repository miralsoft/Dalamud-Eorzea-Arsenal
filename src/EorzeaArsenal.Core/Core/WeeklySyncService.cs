using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What caused a weekly-checklist sync to be requested.</summary>
public enum WeeklyTrigger
{
    /// <summary>The user pressed "sync weekly". Forces a resend of all known fields.</summary>
    Manual,

    /// <summary>The character logged in.</summary>
    Login,

    /// <summary>A gear push just linked/refreshed this character's server id.</summary>
    GearPush,

    /// <summary>The periodic auto-sync timer fired.</summary>
    Auto,
}

/// <summary>The classified outcome of a weekly-checklist sync attempt.</summary>
public enum WeeklyOutcome
{
    /// <summary>Changed fields were merged server-side.</summary>
    Sent,

    /// <summary>Every known field already matched the server; nothing was sent.</summary>
    SkippedUnchanged,

    /// <summary>Currently backing off after a 429/network error.</summary>
    SkippedBackoff,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>Not logged in / values not readable.</summary>
    NotLoggedIn,

    /// <summary>No field could be read with confidence (nothing to send).</summary>
    Nothing,

    /// <summary>The character's server id is not known yet (no gear/inventory push has recorded it).</summary>
    NotResolved,

    /// <summary>The server rejected the sync (see <see cref="WeeklyReport.ErrorKind"/>).</summary>
    Failed,
}

/// <summary>The result of a weekly-checklist sync attempt, surfaced via <see cref="WeeklySyncService.SyncCompleted"/>.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="FieldCount">Number of fields merged (on <see cref="WeeklyOutcome.Sent"/>).</param>
/// <param name="ErrorKind">The API error kind (on <see cref="WeeklyOutcome.Failed"/>).</param>
/// <param name="RequestId">Server correlation id, if any — safe to log/show.</param>
/// <param name="Detail">Optional extra detail (never a secret/body).</param>
public readonly record struct WeeklyReport(
    WeeklyOutcome Outcome,
    int? FieldCount = null,
    ApiErrorKind? ErrorKind = null,
    string? RequestId = null,
    string? Detail = null);

/// <summary>
/// Orchestrates reading the weekly checklist from the game and merging it via
/// <c>PUT /characters/{id}/weekly</c>. Best-effort and non-intrusive: it reads the server's current
/// values first and sends <b>only the confident fields that differ</b>, so it never overwrites a
/// value the user set manually in the web app and never wastes a request when nothing changed. The
/// character's numeric server id comes from the <see cref="CharacterDirectory"/> (populated by the
/// gear/inventory push); until it is known the sync quietly reports <see cref="WeeklyOutcome.NotResolved"/>.
/// A single sync runs at a time (P11); a 429/network error sets a back-off honored by all triggers.
/// This service is pure and unit-tested; all game reads live behind <see cref="IWeeklySource"/>.
/// </summary>
public sealed class WeeklySyncService : IDisposable
{
    private readonly IWeeklySource _source;
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly CharacterDirectory _directory;
    private readonly IClock _clock;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private bool _running;
    private bool _pending;
    private bool _pendingForce;

    private DateTimeOffset _lastSyncUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _backoffUntilUtc = DateTimeOffset.MinValue;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="source">Reads weekly values from the game.</param>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="directory">Resolves <c>cid_hash → character_id</c>.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public WeeklySyncService(
        IWeeklySource source,
        IApiClient api,
        ITokenStore tokens,
        CharacterDirectory directory,
        IClock clock,
        ILog? log = null)
    {
        _source = source;
        _api = api;
        _tokens = tokens;
        _directory = directory;
        _clock = clock;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised after each sync attempt completes (on a background thread).</summary>
    public event Action<WeeklyReport>? SyncCompleted;

    /// <summary>The most recent sync report, or <see langword="null"/> if nothing has run yet.</summary>
    public WeeklyReport? LastReport { get; private set; }

    /// <summary>When the last <i>successful</i> sync happened, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSuccessfulSyncUtc =>
        _lastSyncUtc == DateTimeOffset.MinValue ? null : _lastSyncUtc;

    /// <summary>Whether a 429/network back-off window is currently active.</summary>
    public bool IsRateLimited => _clock.UtcNow < _backoffUntilUtc;

    /// <summary>Default back-off when a 429 carries no <c>Retry-After</c> (default 5 minutes).</summary>
    public TimeSpan DefaultBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Back-off after a transient network failure before retrying (default 1 minute).</summary>
    public TimeSpan NetworkBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Requests a weekly sync. Returns immediately; reads the game off-thread. If a sync is already
    /// running, coalesces into a single pending run (a forcing trigger stays forcing).
    /// </summary>
    /// <param name="trigger">What caused the request.</param>
    public void RequestSync(WeeklyTrigger trigger)
    {
        var force = trigger != WeeklyTrigger.Auto;
        lock (_gate)
        {
            if (_running)
            {
                _pending = true;
                _pendingForce |= force;
                return;
            }

            _running = true;
        }

        _ = Task.Run(() => RunLoopAsync(force));
    }

    private async Task RunLoopAsync(bool force)
    {
        var token = _cts.Token; // captured once: stays cancelled after Dispose, never reads a disposed CTS
        while (true)
        {
            WeeklyReport report;
            try
            {
                report = await SyncOnceAsync(force, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report = new WeeklyReport(WeeklyOutcome.Failed, ErrorKind: ApiErrorKind.Network, Detail: "Cancelled.");
            }
            catch (Exception ex)
            {
                // P2: never let an exception escape into the game; report and continue draining.
                _log.Error($"Unexpected weekly sync error: {ex.GetType().Name}.");
                report = new WeeklyReport(WeeklyOutcome.Failed, ErrorKind: ApiErrorKind.Unexpected);
            }

            Raise(report);

            lock (_gate)
            {
                if (_pending)
                {
                    _pending = false;
                    force = _pendingForce;
                    _pendingForce = false;
                    continue;
                }

                _running = false;
                return;
            }
        }
    }

    private async Task<WeeklyReport> SyncOnceAsync(bool force, CancellationToken ct)
    {
        if (_clock.UtcNow < _backoffUntilUtc)
        {
            return new WeeklyReport(WeeklyOutcome.SkippedBackoff);
        }

        if (!_tokens.HasKey)
        {
            return new WeeklyReport(WeeklyOutcome.NotConnected);
        }

        if (!_source.IsAvailable)
        {
            return new WeeklyReport(WeeklyOutcome.NotLoggedIn);
        }

        var data = await _source.ReadAsync(ct).ConfigureAwait(false);
        if (data is null)
        {
            return new WeeklyReport(WeeklyOutcome.NotLoggedIn);
        }

        if (data.Values.IsEmpty)
        {
            return new WeeklyReport(WeeklyOutcome.Nothing);
        }

        if (!_directory.TryGet(data.Character.CidHash, out var characterId))
        {
            // The server id is only learned from a gear/inventory push; retry after the next one.
            return new WeeklyReport(WeeklyOutcome.NotResolved);
        }

        // Read the server's current values so we send only the confident fields that actually differ
        // (never clobber a manual web-app entry, never spend a write when nothing changed).
        var apiKey = _tokens.ApiKey!;
        var getResult = await _api.GetWeeklyAsync(apiKey, characterId, ct).ConfigureAwait(false);

        Dictionary<string, JsonElement>? serverData;
        if (getResult.IsSuccess)
        {
            serverData = AsObjectMap(getResult.Value!.Data);
        }
        else if (getResult.Error!.Kind == ApiErrorKind.NotFound)
        {
            serverData = null; // no weekly row yet — send everything we know
        }
        else
        {
            return HandleError(getResult.Error);
        }

        var send = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in data.Values.Present())
        {
            if (force || !ServerMatches(serverData, key, value))
            {
                send[key] = value;
            }
        }

        if (send.Count == 0)
        {
            return new WeeklyReport(WeeklyOutcome.SkippedUnchanged);
        }

        var payload = new WeeklyPayload { Items = send };
        var putResult = await _api.PutWeeklyAsync(apiKey, characterId, payload, ct).ConfigureAwait(false);
        if (!putResult.IsSuccess)
        {
            return HandleError(putResult.Error!);
        }

        _lastSyncUtc = _clock.UtcNow;
        _log.Info($"Weekly OK: {send.Count} field(s) merged.");
        return new WeeklyReport(WeeklyOutcome.Sent, FieldCount: send.Count);
    }

    /// <summary>
    /// Converts the raw server <c>data</c> into a field map — but only when it is a JSON object. An
    /// absent value, <c>null</c>, or an <c>[]</c> "empty object" (a PHP <c>json_encode([])</c> quirk)
    /// yields <see langword="null"/>, meaning "no server data", so we send everything we know.
    /// </summary>
    private static Dictionary<string, JsonElement>? AsObjectMap(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value;
        }

        return map;
    }

    /// <summary>Whether the server's stored value for a field equals the locally read value.</summary>
    private static bool ServerMatches(Dictionary<string, JsonElement>? serverData, string key, object value)
    {
        if (serverData is null || !serverData.TryGetValue(key, out var element))
        {
            return false;
        }

        return value switch
        {
            int i => AsInt(element) == i,
            bool b => AsBool(element) == b,
            _ => false,
        };
    }

    private static int? AsInt(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(element.GetString(), out var n) => n,
        _ => null,
    };

    private static bool? AsBool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var n) => n != 0,
        JsonValueKind.String when bool.TryParse(element.GetString(), out var b) => b,
        _ => null,
    };

    private WeeklyReport HandleError(ApiError error)
    {
        if (error.Kind is ApiErrorKind.RateLimited or ApiErrorKind.Network)
        {
            var backoff = error.Kind == ApiErrorKind.RateLimited ? error.RetryAfter ?? DefaultBackoff : NetworkBackoff;
            _backoffUntilUtc = _clock.UtcNow + backoff;
            _log.Warning($"Weekly sync backing off for {backoff.TotalSeconds:F0}s after {error.Kind}. request_id={error.RequestId}.");
        }
        else
        {
            _log.Warning($"Weekly sync failed: {error.Kind} (HTTP {error.StatusCode}) {error.Endpoint}. {error.Message} request_id={error.RequestId}.");
        }

        return new WeeklyReport(WeeklyOutcome.Failed, ErrorKind: error.Kind, RequestId: error.RequestId, Detail: error.Message);
    }

    private void Raise(WeeklyReport report)
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

    /// <summary>Cancels any in-flight sync on plugin unload (P3); single-use afterwards (the token is not replaced).</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
