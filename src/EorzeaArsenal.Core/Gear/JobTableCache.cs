using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Holds the job table each server address published, and answers the only question the send path has:
/// which <see cref="JobPolicy"/> governs right now.
/// </summary>
/// <remarks>
/// <para>
/// Kept <b>per address</b>, never globally. Two machines, or one machine on two days, can talk to the
/// live instance and a test instance, and a table remembered globally would let the answer from one
/// decide what goes to the other. The key is the configured base address.
/// </para>
/// <para>
/// The rules it implements, all of them from the contract:
/// </para>
/// <list type="table">
///   <item><term>404</term><description>an old server; no table, and the floor governs</description></item>
///   <item><term>success</term><description>the fetched table governs, and the push says it covered everything</description></item>
///   <item><term>transient failure, table cached for this address</term><description>the cached one governs</description></item>
///   <item><term>transient failure, never seen from this address</term><description>the floor governs</description></item>
/// </list>
/// <para>
/// The last row is conservative on purpose: a floor push is honestly labelled, so it damages nothing and
/// syncs most of it, where a full push against a server that rejects a code is a 422 and syncs nothing.
/// It happens once per address and heals on the first successful fetch.
/// </para>
/// </remarks>
public sealed class JobTableCache
{
    /// <summary>
    /// How long a fetched table is used before it is read again, matching the
    /// <c>Cache-Control: max-age=86400</c> the route carries.
    /// </summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromHours(24);

    private readonly IApiClient _api;
    private readonly IApiSettings _settings;
    private readonly IClock _clock;
    private readonly ILog? _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _byAddress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the cache.</summary>
    /// <param name="api">The API client. <c>GET /gear/jobs</c> needs no key.</param>
    /// <param name="settings">Supplies the address the table belongs to.</param>
    /// <param name="clock">Clock, for freshness.</param>
    /// <param name="log">Optional log.</param>
    public JobTableCache(IApiClient api, IApiSettings settings, IClock clock, ILog? log = null)
    {
        _api = api;
        _settings = settings;
        _clock = clock;
        _log = log;
    }

    /// <summary>The table currently held for the configured address, or <see langword="null"/>.</summary>
    public JobTableResponse? Current
    {
        get
        {
            lock (_gate)
            {
                return _byAddress.TryGetValue(Address, out var entry) ? entry.Table : null;
            }
        }
    }

    private string Address => _settings.BaseUrl.TrimEnd('/');

    /// <summary>
    /// Returns the policy that governs a push to the configured address, fetching the table if none is
    /// held or the one held has gone stale.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The governing policy. Never throws for a missing or unreachable table.</returns>
    public async Task<JobPolicy> GetPolicyAsync(CancellationToken ct)
    {
        var address = Address;

        lock (_gate)
        {
            if (_byAddress.TryGetValue(address, out var held) && held.IsUsable(_clock.UtcNow))
            {
                return held.Policy;
            }
        }

        var result = await _api.GetJobTableAsync(ct).ConfigureAwait(false);

        if (result.IsSuccess && result.Value is not null)
        {
            var policy = JobPolicy.FromTable(result.Value);
            lock (_gate)
            {
                _byAddress[address] = Entry.Fetched(result.Value, policy, _clock.UtcNow);
            }

            _log?.Info($"Job table from {address}: {policy.AllowedCodes.Count} code(s), scope {policy.Scope}.");
            return policy;
        }

        // A 404 is an answer, not a failure: this server predates the route, so the floor governs and the
        // push says so. Remembered, so every push does not ask again.
        if (result.Error?.Kind == ApiErrorKind.NotFound)
        {
            lock (_gate)
            {
                _byAddress[address] = Entry.OldServer(_clock.UtcNow);
            }

            _log?.Info($"{address} has no job table (404); reporting the combat floor.");
            return JobPolicy.Floor;
        }

        // Anything else is transient. A table already held for this address keeps governing even when
        // stale — a day-old truth beats holding back rows the server has.
        lock (_gate)
        {
            if (_byAddress.TryGetValue(address, out var stale) && stale.Table is not null)
            {
                _log?.Info($"Job table for {address} could not be refreshed; using the one held.");
                return stale.Policy;
            }
        }

        _log?.Info($"No job table has ever been seen from {address}; reporting the combat floor.");
        return JobPolicy.Floor;
    }

    /// <summary>
    /// Discards the table held for the configured address, because the server rejected a code that
    /// table said it accepts.
    /// </summary>
    /// <remarks>
    /// The one case the freshness rule cannot cover: a server rolled back to a version that publishes
    /// fewer codes while its table is still held here. Without this the sync stays dead until the entry
    /// expires on its own. Only <c>error: job_unknown</c> triggers it — every other 422 leaves the table
    /// alone, or a client would refetch after every failed push and write about jobs into a log that has
    /// nothing to do with jobs.
    /// </remarks>
    /// <param name="codes">The codes the server named, for the log.</param>
    public void Invalidate(IReadOnlyList<string>? codes = null)
    {
        var address = Address;
        lock (_gate)
        {
            _byAddress.Remove(address);
        }

        var named = codes is { Count: > 0 } ? string.Join(", ", codes) : "none named";
        _log?.Warning($"{address} rejected job code(s) ({named}); table discarded, reporting the floor until a fresh one arrives.");
    }

    private sealed record Entry(JobTableResponse? Table, JobPolicy Policy, DateTimeOffset FetchedUtc)
    {
        public static Entry Fetched(JobTableResponse table, JobPolicy policy, DateTimeOffset now) =>
            new(table, policy, now);

        // An old server is remembered as "asked, and it has no table". Re-asked on the same schedule as a
        // real one, because a server gains the route by being deployed, not by being restarted.
        public static Entry OldServer(DateTimeOffset now) => new(null, JobPolicy.Floor, now);

        public bool IsUsable(DateTimeOffset now) => now - FetchedUtc < Freshness;
    }
}
