using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>The state of the last BiS fetch.</summary>
public enum BisFetchStatus
{
    /// <summary>Nothing fetched yet.</summary>
    Idle,

    /// <summary>Targets fetched successfully.</summary>
    Ok,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>Not logged in / gear not readable.</summary>
    NotLoggedIn,

    /// <summary>The server returned no BiS targets.</summary>
    Empty,

    /// <summary>403 — the key lacks <c>gear:read</c> (reconnect needed).</summary>
    Forbidden,

    /// <summary>404 — no BiS for this character.</summary>
    NotFound,

    /// <summary>Any other error.</summary>
    Error,
}

/// <summary>A slot in a gearset whose BiS target is a given item.</summary>
/// <param name="Job">The job code.</param>
/// <param name="GearIndex">
/// The gearset index to show. Resolved to the <b>live</b> position where the identity is known, so the
/// number in the overlay is the one the player sees in game rather than the one the server last stored.
/// </param>
/// <param name="Name">The target's name, if any.</param>
/// <param name="Slot">The slot key.</param>
public readonly record struct BisHit(string Job, int GearIndex, string? Name, string Slot);

/// <summary>
/// Fetches and caches the player's BiS targets once, so both the BiS window and the hover overlay
/// can read them without re-querying. Owns the live-vs-BiS comparison and a reverse lookup
/// (item id → which gearset slots want it as BiS). Plugin-side orchestration; the pure comparison
/// lives in <see cref="BisComparer"/> (tested in the core).
/// </summary>
public sealed class BisService
{
    private readonly IApiClient _api;
    private readonly IGearSource _gearSource;
    private readonly ITokenStore _tokens;
    private readonly ILog _log;
    private readonly GearsetMappingService _mapping;

    private volatile bool _loading;
    private volatile GearsetComparison[] _comparisons = [];
    private volatile BisGearset[] _targets = [];
    private DateTimeOffset _fetchedUtc = DateTimeOffset.MinValue;

    // The live list as it stood when the targets were fetched, both ways round. This is what re-keys
    // the interface: a caller knows the position it is drawing, and everything downstream needs the
    // identity. Rebuilt on every fetch, empty when no identity could be resolved — in which case the
    // position fallback below applies, and only then.
    private volatile Dictionary<int, string> _uidByLiveIndex = new();
    private volatile Dictionary<string, int> _liveIndexByUid = new(StringComparer.Ordinal);

    /// <summary>Creates the service.</summary>
    /// <param name="api">API client.</param>
    /// <param name="gearSource">Live gear source.</param>
    /// <param name="tokens">Token store.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="mapping">The gearset identity cache the comparison is keyed on.</param>
    public BisService(
        IApiClient api,
        IGearSource gearSource,
        ITokenStore tokens,
        ILog log,
        GearsetMappingService mapping)
    {
        _api = api;
        _gearSource = gearSource;
        _tokens = tokens;
        _log = log;
        _mapping = mapping;
    }

    /// <summary>The status of the most recent fetch.</summary>
    public BisFetchStatus Status { get; private set; } = BisFetchStatus.Idle;

    /// <summary>The error kind when <see cref="Status"/> is <see cref="BisFetchStatus.Error"/>.</summary>
    public ApiErrorKind? LastErrorKind { get; private set; }

    /// <summary>Whether a fetch is in progress.</summary>
    public bool IsLoading => _loading;

    /// <summary>The live-vs-BiS comparisons from the last successful fetch.</summary>
    public IReadOnlyList<GearsetComparison> Comparisons => _comparisons;

    /// <summary>Whether the cache is older than the given age (or never fetched).</summary>
    /// <param name="maxAge">The maximum acceptable age.</param>
    /// <returns><see langword="true"/> if a refresh is due.</returns>
    public bool IsStale(TimeSpan maxAge) => DateTimeOffset.UtcNow - _fetchedUtc > maxAge;

    /// <summary>Fetches the BiS targets for the current character and updates the cache.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the fetch finishes.</returns>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            if (!_tokens.HasKey)
            {
                SetStatus(BisFetchStatus.NotConnected);
                return;
            }

            var live = await _gearSource.ReadAsync(ct).ConfigureAwait(false);
            if (live is null)
            {
                SetStatus(BisFetchStatus.NotLoggedIn);
                return;
            }

            var clean = GearSanitizer.Sanitize(live);

            // Make sure the identities are known before comparing. Reading them is a read; learning
            // them by pushing would be a write in order to answer a question.
            await _mapping.EnsureMappingAsync(clean.Character.CidHash, ct).ConfigureAwait(false);

            var result = await _api.GetBisAsync(_tokens.ApiKey!, clean.Character.CidHash, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LastErrorKind = result.Error!.Kind;
                SetStatus(result.Error.Kind switch
                {
                    ApiErrorKind.Forbidden => BisFetchStatus.Forbidden,
                    ApiErrorKind.NotFound => BisFetchStatus.NotFound,
                    _ => BisFetchStatus.Error,
                });
                return;
            }

            var cidHash = clean.Character.CidHash;
            var uidByIndex = new Dictionary<int, string>();
            var indexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var set in clean.Gearsets)
            {
                var match = _mapping.Resolve(cidHash, set);
                if (!match.IsResolved)
                {
                    continue;
                }

                uidByIndex[set.GearIndex] = match.SetUid!;
                indexByUid[match.SetUid!] = set.GearIndex;
            }

            _uidByLiveIndex = uidByIndex;
            _liveIndexByUid = indexByUid;

            var targets = result.Value!.Data;
            _targets = targets.ToArray();
            _comparisons = BisComparer.Compare(clean, targets, set => Identify(cidHash, set)).ToArray();
            _fetchedUtc = DateTimeOffset.UtcNow;
            SetStatus(targets.Count == 0 ? BisFetchStatus.Empty : BisFetchStatus.Ok);
        }
        catch (Exception ex)
        {
            _log.Error($"BiS fetch failed: {ex.GetType().Name}.");
            SetStatus(BisFetchStatus.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Finds the gearset slots whose BiS target item id equals the given item.</summary>
    /// <param name="itemId">A normalized (HQ-stripped) item id.</param>
    /// <returns>Every slot that wants this item as its BiS target (may be empty).</returns>
    public IReadOnlyList<BisHit> FindForItem(int itemId)
    {
        var hits = new List<BisHit>();
        var indexByUid = _liveIndexByUid;
        foreach (var target in _targets)
        {
            foreach (var (slot, item) in target.Items)
            {
                if (item.Id != itemId)
                {
                    continue;
                }

                // Show the live position where it is known. The target's own index is the server's
                // last-stored display order and may sit in the 100–999 band, which would be a number
                // the player cannot find in their list.
                var index = target.SetUid is not null && indexByUid.TryGetValue(target.SetUid, out var live)
                    ? live
                    : target.GearIndex;

                hits.Add(new BisHit(target.Job, index, target.Name, slot));
            }
        }

        return hits;
    }

    /// <summary>Returns the live-vs-BiS comparison for one slot, if known.</summary>
    /// <param name="gearIndex">The <b>live</b> gearset index being drawn.</param>
    /// <param name="job">The job code.</param>
    /// <param name="slot">The slot key.</param>
    /// <returns>The slot comparison, or <see langword="null"/>.</returns>
    public SlotComparison? SlotStatus(int gearIndex, string job, string slot)
    {
        var comparison = ComparisonFor(gearIndex, job);
        if (comparison is null)
        {
            return null;
        }

        foreach (var slotComparison in comparison.Slots)
        {
            if (slotComparison.Slot == slot)
            {
                return slotComparison;
            }
        }

        return null;
    }

    /// <summary>Returns the full BiS target gearset for a live gearset index, if cached.</summary>
    /// <param name="gearIndex">The <b>live</b> gearset index.</param>
    /// <returns>The target gearset, or <see langword="null"/>.</returns>
    public BisGearset? TargetGearset(int gearIndex)
    {
        if (_uidByLiveIndex.TryGetValue(gearIndex, out var uid))
        {
            foreach (var target in _targets)
            {
                if (string.Equals(target.SetUid, uid, StringComparison.Ordinal))
                {
                    return target;
                }
            }

            // The identity is known and no target carries it: this gearset has no BiS pinned. Falling
            // back to the position here would show a neighbouring set's target as if it were this one.
            if (_mapping.ServerMintsUids)
            {
                return null;
            }
        }

        foreach (var target in _targets)
        {
            if (target.SetUid is null && target.GearIndex == gearIndex)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// The comparison belonging to a live gearset: by identity when both sides have one, by position
    /// only while the server does not mint identities at all.
    /// </summary>
    private GearsetComparison? ComparisonFor(int gearIndex, string job)
    {
        if (_uidByLiveIndex.TryGetValue(gearIndex, out var uid))
        {
            foreach (var comparison in _comparisons)
            {
                if (string.Equals(comparison.SetUid, uid, StringComparison.Ordinal))
                {
                    return comparison;
                }
            }

            if (_mapping.ServerMintsUids)
            {
                return null;
            }
        }

        foreach (var comparison in _comparisons)
        {
            if (comparison.SetUid is null && comparison.GearIndex == gearIndex && comparison.Job == job)
            {
                return comparison;
            }
        }

        return null;
    }

    /// <summary>The identity of a live gearset, for the comparer's key.</summary>
    private string? Identify(string cidHash, GearsetDto set)
    {
        var match = _mapping.Resolve(cidHash, set);
        return match.IsResolved ? match.SetUid : null;
    }

    private void SetStatus(BisFetchStatus status) => Status = status;
}
