using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// What waits in the player's teams, from <c>GET /me/waiting</c> (server 1.5), for the count on the Teams
/// entry and the "Zeiten" tab. Read at most once a minute, as the site does; the site and the plugin share
/// a limit of ten reads a minute, and a 429 holds the next read for its <c>Retry-After</c>.
/// </summary>
public sealed class WaitingCounter
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly ILog _log;
    private volatile MyWaiting? _current;
    private DateTimeOffset _nextReadUtc = DateTimeOffset.MinValue;
    private int _reading;

    /// <summary>Creates the counter.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public WaitingCounter(IApiClient api, ITokenStore tokens, IClock clock, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The least time between two reads (default one minute).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The sum over every team, 0 before the first read.</summary>
    public int Total => _current?.Total ?? 0;

    /// <summary>What waits in one team, or <see langword="null"/> when nothing does or nothing was read.</summary>
    /// <param name="teamId">The team.</param>
    /// <returns>The counts.</returns>
    public WaitingCounts? For(long teamId) =>
        _current?.Teams.TryGetValue(teamId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var counts) == true ? counts : null;

    /// <summary>Forgets the counts, for a server without the call or a player who turned the Teams off.</summary>
    public void Clear() => _current = null;

    /// <summary>Lets the next <see cref="RefreshIfDueAsync"/> read at once, after the player answered something.</summary>
    public void Expire() => _nextReadUtc = DateTimeOffset.MinValue;

    /// <summary>Reads again when the interval has passed; otherwise does nothing.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the read is done or skipped.</returns>
    public async Task RefreshIfDueAsync(CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        var now = _clock.UtcNow;
        if (string.IsNullOrEmpty(key) || now < _nextReadUtc || Interlocked.Exchange(ref _reading, 1) != 0)
        {
            return;
        }

        try
        {
            _nextReadUtc = now + Interval;
            var result = await _api.GetMyWaitingAsync(key, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _current = result.Value?.Data ?? new MyWaiting();
                return;
            }

            if (result.Error?.Kind == ApiErrorKind.RateLimited)
            {
                _nextReadUtc = now + (result.Error.RetryAfter ?? Interval);
            }

            _log.Info($"Waiting counts not read: {result.Error?.Kind}.");
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _log.Error($"Waiting counts failed: {ex.GetType().Name}.");
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }
}
