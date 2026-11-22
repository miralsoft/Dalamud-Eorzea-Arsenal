using System.Globalization;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The outcome of one attempt to send collected reports.</summary>
public enum ReportOutcome
{
    /// <summary>A batch went out and was answered with 202.</summary>
    Sent,

    /// <summary>Nothing collected.</summary>
    Nothing,

    /// <summary>Too soon after the last send (and not forced).</summary>
    NotDue,

    /// <summary>Held back after a 429 or a closed intake.</summary>
    Holding,

    /// <summary>The key was refused (401/403); nothing goes until another key is connected.</summary>
    Stopped,

    /// <summary>The intake is closed (409): everything collected was dropped.</summary>
    IntakeClosed,

    /// <summary>The batch was refused as a whole (400/413/422) and dropped.</summary>
    Refused,

    /// <summary>Any other failure; the batch stays for a later attempt.</summary>
    Failed,
}

/// <summary>
/// Collects reports of one kind and sends them now and then, as both report contracts of server 1.5 ask:
/// at most every few minutes and when a duty ends (or at logout), at most a set number a call.
/// </summary>
/// <remarks>
/// <para>
/// The answers are handled the same for drops and spots: a 202 removes the batch, the refused entries
/// included, since a refused report is never sent again; a 409 means the site takes no reports now, so
/// everything collected is dropped and nothing is tried for a day; a 429 holds every send for an hour; a
/// 401 or 403 stops until another key is connected; a 400, 413 or 422 drops the batch, since the same
/// payload would be refused again. A network failure keeps the batch for the next attempt.
/// </para>
/// <para>
/// The collected reports are handed to <c>persist</c> after every change, so the plugin can keep them on
/// disk while playing. Reports older than seven days are dropped before sending, since the server refuses
/// them. Reports with the same <c>key</c> go out as one, built by <c>combine</c> (drops add up their quantity;
/// a spot is kept once). They are kept apart until a batch is built, so what a 202 removes is exactly what
/// went out, even when more of the same arrived while the call was running.
/// </para>
/// </remarks>
/// <typeparam name="TReport">The report type.</typeparam>
public sealed class ReportUploader<TReport>
    where TReport : class
{
    private readonly Func<string, IReadOnlyList<TReport>, CancellationToken, Task<ApiResult<ReportAck>>> _send;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly Func<TReport, string> _day;
    private readonly Func<TReport, string> _key;
    private readonly Func<TReport, TReport, TReport> _combine;
    private readonly Action<IReadOnlyList<TReport>> _persist;
    private readonly int _maxPerCall;
    private readonly string _name;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private List<TReport> _pending;
    private DateTimeOffset _lastSendUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _holdUntilUtc = DateTimeOffset.MinValue;
    private string? _stoppedForKey;
    private int _sending;

    /// <summary>Creates the uploader.</summary>
    /// <param name="name">What it sends, for the log (<c>drop</c>, <c>world</c>).</param>
    /// <param name="send">Sends one batch with the key.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="maxPerCall">The most reports one call takes.</param>
    /// <param name="day">The report's day, <c>YYYY-MM-DD</c>.</param>
    /// <param name="key">What makes two reports the same.</param>
    /// <param name="combine">Builds one report out of two with the same key.</param>
    /// <param name="persist">Keeps the collected reports (on disk).</param>
    /// <param name="restored">What was collected before a restart.</param>
    /// <param name="log">Diagnostics sink.</param>
    public ReportUploader(
        string name,
        Func<string, IReadOnlyList<TReport>, CancellationToken, Task<ApiResult<ReportAck>>> send,
        ITokenStore tokens,
        IClock clock,
        int maxPerCall,
        Func<TReport, string> day,
        Func<TReport, string> key,
        Func<TReport, TReport, TReport> combine,
        Action<IReadOnlyList<TReport>> persist,
        IEnumerable<TReport>? restored = null,
        ILog? log = null)
    {
        _name = name;
        _send = send;
        _tokens = tokens;
        _clock = clock;
        _maxPerCall = maxPerCall;
        _day = day;
        _key = key;
        _combine = combine;
        _persist = persist;
        _pending = [.. restored ?? []];
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The least time between two sends that are not forced (default five minutes).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a 429 holds every send (default one hour).</summary>
    public TimeSpan RateLimitHold { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a closed intake holds every send (default one day).</summary>
    public TimeSpan ClosedHold { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How many reports wait.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Whether a closed intake or a limit holds the sends right now; collecting is pointless then.</summary>
    public bool IsHolding => _clock.UtcNow < _holdUntilUtc;

    /// <summary>Adds reports to what waits.</summary>
    /// <param name="reports">The new reports.</param>
    public void Add(IEnumerable<TReport> reports)
    {
        List<TReport> snapshot;
        lock (_gate)
        {
            var before = _pending.Count;
            _pending.AddRange(reports);
            if (_pending.Count == before)
            {
                return;
            }

            snapshot = [.. _pending];
        }

        _persist(snapshot);
    }

    /// <summary>Forgets everything collected (the setting was turned off).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            _pending = [];
        }

        _persist([]);
    }

    /// <summary>Sends one batch when one is due.</summary>
    /// <param name="force">Send now regardless of the interval (a duty ended, a logout).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<ReportOutcome> SendIfDueAsync(bool force, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return ReportOutcome.Stopped;
        }

        if (Interlocked.Exchange(ref _sending, 1) != 0)
        {
            return ReportOutcome.NotDue;
        }

        try
        {
            List<TReport> batch;
            List<TReport> members;
            var now = _clock.UtcNow;
            lock (_gate)
            {
                if (_stoppedForKey is not null)
                {
                    if (string.Equals(_stoppedForKey, key, StringComparison.Ordinal))
                    {
                        return ReportOutcome.Stopped;
                    }

                    _stoppedForKey = null;
                }

                if (now < _holdUntilUtc)
                {
                    return ReportOutcome.Holding;
                }

                var oldest = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _pending.RemoveAll(r => string.CompareOrdinal(_day(r), oldest) < 0);
                if (_pending.Count == 0)
                {
                    return ReportOutcome.Nothing;
                }

                if (!force && now - _lastSendUtc < Interval)
                {
                    return ReportOutcome.NotDue;
                }

                var groups = _pending.GroupBy(_key, StringComparer.Ordinal).Take(_maxPerCall).ToList();
                batch = [.. groups.Select(g => g.Aggregate(_combine))];
                members = [.. groups.SelectMany(g => g)];
                _lastSendUtc = now;
            }

            var result = await _send(key, batch, ct).ConfigureAwait(false);
            return Handle(result, batch, members, key);
        }
        finally
        {
            Volatile.Write(ref _sending, 0);
        }
    }

    private ReportOutcome Handle(ApiResult<ReportAck> result, List<TReport> batch, List<TReport> members, string key)
    {
        if (result.IsSuccess)
        {
            Remove(members);
            var rejected = result.Value?.Rejected ?? [];
            var reasons = rejected.Count == 0 ? string.Empty : $", refused: {string.Join(", ", rejected.Select(r => r.Reason).Distinct())}";
            _log.Info($"Reports ({_name}): {result.Value?.Accepted ?? 0} taken of {batch.Count}{reasons}.");
            return ReportOutcome.Sent;
        }

        var error = result.Error!;
        var tail = error.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
        switch (error.Kind)
        {
            case ApiErrorKind.Conflict:
                Clear();
                Hold(ClosedHold);
                _log.Info($"Reports ({_name}): the site takes none now; dropped what was collected, trying again in a day{tail}.");
                return ReportOutcome.IntakeClosed;

            case ApiErrorKind.RateLimited:
                Hold(error.RetryAfter is { } retry && retry > RateLimitHold ? retry : RateLimitHold);
                _log.Info($"Reports ({_name}): limit reached, waiting an hour{tail}.");
                return ReportOutcome.Holding;

            case ApiErrorKind.Unauthorized or ApiErrorKind.Forbidden:
                lock (_gate)
                {
                    _stoppedForKey = key;
                }

                _log.Info($"Reports ({_name}): key refused ({error.StatusCode}); stopped until another key is connected{tail}.");
                return ReportOutcome.Stopped;

            case ApiErrorKind.BadRequest or ApiErrorKind.Validation:
                Remove(members);
                _log.Warning($"Reports ({_name}): batch refused ({error.StatusCode}) and dropped{tail}.");
                return ReportOutcome.Refused;

            default:
                // 413 has no kind of its own; it is a refusal of the batch like 400 and 422.
                if (error.StatusCode == 413)
                {
                    Remove(members);
                    _log.Warning($"Reports ({_name}): batch too large and dropped{tail}.");
                    return ReportOutcome.Refused;
                }

                _log.Info($"Reports ({_name}): not sent ({error.Kind}); kept for later{tail}.");
                return ReportOutcome.Failed;
        }
    }

    private void Hold(TimeSpan span)
    {
        lock (_gate)
        {
            _holdUntilUtc = _clock.UtcNow + span;
        }
    }

    private void Remove(List<TReport> batch)
    {
        List<TReport> snapshot;
        lock (_gate)
        {
            var sent = new HashSet<TReport>(batch, ReferenceEqualityComparer.Instance);
            _pending.RemoveAll(sent.Contains);
            snapshot = [.. _pending];
        }

        _persist(snapshot);
    }
}
