using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The classified outcome of a Teams poll.</summary>
public enum TeamsPollOutcome
{
    /// <summary>The poll ran and the cache was refreshed.</summary>
    Ok,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>Within the poll interval / back-off window; skipped.</summary>
    Skipped,

    /// <summary>The key is missing the <c>teams:read</c> scope — the user must reconnect once.</summary>
    ScopeMissing,

    /// <summary>A transport/server error; the last-good cache is kept.</summary>
    Failed,
}

/// <summary>A toast to surface in-game: a new team notification (loot / reminder / event).</summary>
/// <param name="Title">Short title.</param>
/// <param name="Body">One-line body.</param>
/// <param name="Link">Relative app path to open (the plugin prefixes the web-app URL), or <see langword="null"/>.</param>
/// <param name="Type">The notification type (e.g. <c>team.loot</c>).</param>
public readonly record struct TeamToast(string Title, string Body, string? Link, string Type);

/// <summary>
/// Orchestrates the read-only Teams companion: it polls <c>/me/calendar</c> and <c>/notifications</c>
/// on a gentle cadence (≥ 5 min), caches the last good calendar for the UI, and raises a
/// <see cref="Toast"/> for each genuinely new team notification (loot, reminder, planned event) —
/// exactly once, deduped by the monotonic notification id and persisted across sessions so a relog
/// never re-toasts the backlog. On the first successful fetch it seeds the watermark silently. All
/// heavier reads (mit sheet, content hub, farm, logs, absences) and the two writes (own RSVP, own
/// absence) are fetched on demand through this service so the secret key never leaves the core. Pure
/// and unit-tested; all HTTP goes through <see cref="IApiClient"/>.
/// </summary>
public sealed class TeamsService : IDisposable
{
    /// <summary>Notification types surfaced as in-game toasts (loot, event reminders, planned events).</summary>
    private static readonly HashSet<string> ToastTypes = new(StringComparer.Ordinal)
    {
        TeamsProtocol.NotifLoot,
        TeamsProtocol.NotifReminder,
        "team.event",
    };

    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ITeamsSeenStore _seen;
    private readonly IClock _clock;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private bool _running;
    private bool _pending;
    private bool _pendingForce;
    private bool _seeded;

    private DateTimeOffset _lastPollUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _backoffUntilUtc = DateTimeOffset.MinValue;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="seen">Persists the notification watermark.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public TeamsService(IApiClient api, ITokenStore tokens, ITeamsSeenStore seen, IClock clock, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _seen = seen;
        _clock = clock;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised after a poll refreshes the cached calendar (on a background thread).</summary>
    public event Action? CalendarUpdated;

    /// <summary>Raised for each genuinely new toastable notification (on a background thread).</summary>
    public event Action<TeamToast>? Toast;

    /// <summary>Raised after every poll attempt with its outcome (on a background thread).</summary>
    public event Action<TeamsPollOutcome>? PollCompleted;

    /// <summary>The last good calendar occurrences (empty until the first successful poll).</summary>
    public IReadOnlyList<CalendarOccurrence> Calendar { get; private set; } = [];

    /// <summary>The unread-notification count from the last poll.</summary>
    public int UnreadNotifications { get; private set; }

    /// <summary>The classified outcome of the last poll.</summary>
    public TeamsPollOutcome LastOutcome { get; private set; }

    /// <summary>When the last successful poll happened, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastPollUtc => _lastPollUtc == DateTimeOffset.MinValue ? null : _lastPollUtc;

    /// <summary>Minimum time between polls (proactive rate-limit guard, R23).</summary>
    public TimeSpan MinPollInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Back-off after a 429/network error before retrying.</summary>
    public TimeSpan Backoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Requests a calendar + notifications poll. Returns immediately; runs off-thread. Coalesces into
    /// a single pending run if one is already going. A non-forcing poll self-throttles to
    /// <see cref="MinPollInterval"/>; <paramref name="force"/> bypasses the interval (but never the
    /// seen-set, so a manual refresh still never re-toasts).
    /// </summary>
    /// <param name="force">Bypass the poll interval (e.g. a manual refresh).</param>
    public void RequestPoll(bool force = false)
    {
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
        var token = _cts.Token;
        while (true)
        {
            try
            {
                await PollOnceAsync(force, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LastOutcome = TeamsPollOutcome.Failed;
            }
            catch (Exception ex)
            {
                _log.Error($"Unexpected teams poll error: {ex.GetType().Name}.");
                LastOutcome = TeamsPollOutcome.Failed;
            }

            try
            {
                PollCompleted?.Invoke(LastOutcome);
            }
            catch (Exception ex)
            {
                _log.Error($"PollCompleted handler threw: {ex.GetType().Name}.");
            }

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

    private async Task PollOnceAsync(bool force, CancellationToken ct)
    {
        if (!_tokens.HasKey)
        {
            LastOutcome = TeamsPollOutcome.NotConnected;
            return;
        }

        var now = _clock.UtcNow;
        if (!force && (now < _backoffUntilUtc || (LastPollUtc is { } last && now - last < MinPollInterval)))
        {
            LastOutcome = TeamsPollOutcome.Skipped;
            return;
        }

        var apiKey = _tokens.ApiKey!;
        var calendar = await _api.GetCalendarAsync(apiKey, null, null, ct).ConfigureAwait(false);
        var notifications = await _api.GetNotificationsAsync(apiKey, ct).ConfigureAwait(false);

        // A missing scope on either read is the actionable "reconnect once" case.
        if (IsScopeError(calendar.Error) || IsScopeError(notifications.Error))
        {
            LastOutcome = TeamsPollOutcome.ScopeMissing;
            return;
        }

        var anySuccess = false;

        if (calendar.IsSuccess)
        {
            Calendar = calendar.Value!.Data ?? [];
            anySuccess = true;
            RaiseCalendarUpdated();
        }
        else
        {
            Backoffable(calendar.Error!);
        }

        if (notifications.IsSuccess)
        {
            HandleNotifications(notifications.Value!);
            anySuccess = true;
        }
        else
        {
            Backoffable(notifications.Error!);
        }

        if (anySuccess)
        {
            _lastPollUtc = now;
            LastOutcome = TeamsPollOutcome.Ok;
        }
        else
        {
            LastOutcome = TeamsPollOutcome.Failed;
        }
    }

    private void HandleNotifications(NotificationsResponse response)
    {
        UnreadNotifications = response.Unread;
        var entries = response.Data;
        if (entries is null || entries.Count == 0)
        {
            _seeded = true;
            return;
        }

        var watermark = _seen.LastNotificationId;
        var maxId = watermark;
        foreach (var entry in entries)
        {
            if (entry.Id > maxId)
            {
                maxId = entry.Id;
            }
        }

        // First-ever successful fetch (or a fresh install): seed the watermark, never toast a backlog.
        if (_seeded || watermark != 0)
        {
            foreach (var entry in entries)
            {
                if (entry.Id > watermark && entry.Type is { } type && ToastTypes.Contains(type))
                {
                    RaiseToast(new TeamToast(entry.Title ?? string.Empty, entry.Body ?? string.Empty, entry.Link, type));
                }
            }
        }

        if (maxId != _seen.LastNotificationId)
        {
            _seen.LastNotificationId = maxId;
            _seen.Save();
        }

        _seeded = true;
    }

    private void Backoffable(ApiError error)
    {
        if (error.Kind is ApiErrorKind.RateLimited or ApiErrorKind.Network)
        {
            var backoff = error.Kind == ApiErrorKind.RateLimited ? error.RetryAfter ?? Backoff : Backoff;
            _backoffUntilUtc = _clock.UtcNow + backoff;
            _log.Warning($"Teams poll backing off for {backoff.TotalSeconds:F0}s after {error.Kind}.");
        }
        else
        {
            _log.Warning($"Teams poll read failed: {error.Kind} (HTTP {error.StatusCode}). request_id={error.RequestId}.");
        }
    }

    private static bool IsScopeError(ApiError? error) =>
        error?.Kind == ApiErrorKind.Forbidden &&
        error.Message?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true;

    // --- On-demand reads/writes (key stays in the core) -------------------------------------------

    /// <summary>Reads the player's teams + active mit plans.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The teams, or an error (including <see cref="ApiErrorKind.Unauthorized"/> when not connected).</returns>
    public Task<ApiResult<TeamsResponse>> GetTeamsAsync(CancellationToken ct) =>
        WithKey(key => _api.GetTeamsAsync(key, ct));

    /// <summary>Reads a mit cheat sheet.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="planId">The plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sheet, or an error.</returns>
    public Task<ApiResult<MitSheetResponse>> GetMitSheetAsync(long teamId, long planId, CancellationToken ct) =>
        WithKey(key => _api.GetMitSheetAsync(key, teamId, planId, ct));

    /// <summary>Reads the content hub.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hub, or an error.</returns>
    public Task<ApiResult<ContentSheetResponse>> GetContentSheetAsync(long teamId, CancellationToken ct) =>
        WithKey(key => _api.GetContentSheetAsync(key, teamId, ct));

    /// <summary>Streams a resource file (image bytes / pdf bytes).</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="resourceId">The resource id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bytes + MIME, or an error.</returns>
    public Task<ApiResult<ResourceFile>> GetResourceFileAsync(long teamId, long resourceId, CancellationToken ct) =>
        WithKey(key => _api.GetResourceFileAsync(key, teamId, resourceId, ct));

    /// <summary>Reads the who-needs-what farm overview.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The farm entries, or an error.</returns>
    public Task<ApiResult<FarmResponse>> GetFarmAsync(long teamId, CancellationToken ct) =>
        WithKey(key => _api.GetFarmAsync(key, teamId, ct));

    /// <summary>Reads the FFLogs mirror (best-effort).</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The logs, or an error.</returns>
    public Task<ApiResult<LogsResponse>> GetLogsAsync(long teamId, CancellationToken ct) =>
        WithKey(key => _api.GetLogsAsync(key, teamId, ct));

    /// <summary>Reads absence ranges for a team.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The absences, or an error.</returns>
    public Task<ApiResult<AbsencesResponse>> GetAbsencesAsync(long teamId, CancellationToken ct) =>
        WithKey(key => _api.GetAbsencesAsync(key, teamId, ct));

    /// <summary>Sets the player's own RSVP for an occurrence.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="request">The occurrence date + status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ack, or an error (403 capability / 404 not a member — surface it).</returns>
    public Task<ApiResult<StatusAck>> SetAttendanceAsync(long teamId, long eventId, AttendanceRequest request, CancellationToken ct) =>
        WithKey(key => _api.PostAttendanceAsync(key, teamId, eventId, request, ct));

    /// <summary>Reports the player's own absence range.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="request">The date range + optional note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created id, or an error.</returns>
    public Task<ApiResult<AbsenceCreateResponse>> CreateAbsenceAsync(long teamId, AbsenceCreateRequest request, CancellationToken ct) =>
        WithKey(key => _api.PostAbsenceAsync(key, teamId, request, ct));

    /// <summary>Deletes one of the player's own absences.</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="absenceId">The absence id (must be the player's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    public Task<ApiResult<bool>> DeleteAbsenceAsync(long teamId, long absenceId, CancellationToken ct) =>
        WithKey(key => _api.DeleteAbsenceAsync(key, teamId, absenceId, ct));

    private Task<ApiResult<T>> WithKey<T>(Func<string, Task<ApiResult<T>>> call)
    {
        var key = _tokens.ApiKey;
        return string.IsNullOrEmpty(key)
            ? Task.FromResult(ApiResult<T>.Fail(new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." }))
            : call(key);
    }

    private void RaiseCalendarUpdated()
    {
        try
        {
            CalendarUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"CalendarUpdated handler threw: {ex.GetType().Name}.");
        }
    }

    private void RaiseToast(TeamToast toast)
    {
        try
        {
            Toast?.Invoke(toast);
        }
        catch (Exception ex)
        {
            _log.Error($"Toast handler threw: {ex.GetType().Name}.");
        }
    }

    /// <summary>Cancels any in-flight poll on unload (P3); single-use afterwards.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
