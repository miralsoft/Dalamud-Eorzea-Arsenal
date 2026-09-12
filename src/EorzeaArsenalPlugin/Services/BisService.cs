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

    // Which character the comparisons above were built for. IsProvisional is asked per frame about one
    // comparison, and a comparison does not carry its character; without this the question went to
    // whichever character the identity cache had heard from last.
    private volatile string? _comparedFor;
    private volatile GearsetDto[] _withoutTarget = [];
    private volatile BisGearset[] _targets = [];
    private DateTimeOffset _fetchedUtc = DateTimeOffset.MinValue;

    // The live list as it stood when the targets were fetched, both ways round. This is what re-keys
    // the interface: a caller knows the position it is drawing, and everything downstream needs the
    // identity. Rebuilt on every fetch, empty when no identity could be resolved — in which case the
    // position fallback below applies, and only then.
    private volatile IReadOnlyDictionary<int, string> _uidByLiveIndex = new Dictionary<int, string>();
    private volatile IReadOnlyDictionary<string, int> _liveIndexByUid = new Dictionary<string, int>(StringComparer.Ordinal);

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

    /// <summary>
    /// Live gearsets no BiS target claims, in the order the player has them. Not a fault and not a failed
    /// transfer: a crafter set, a gatherer set or a base class has no catalogue to compare against, and a
    /// combat set nobody pinned a target for has nothing to compare either. Shown rather than dropped,
    /// because a set that vanishes from the window gets reported as a bug.
    /// </summary>
    public IReadOnlyList<GearsetDto> WithoutTarget => _withoutTarget;

    /// <summary>Whether the cache is older than the given age (or never fetched).</summary>
    /// <param name="maxAge">The maximum acceptable age.</param>
    /// <returns><see langword="true"/> if a refresh is due.</returns>
    public bool IsStale(TimeSpan maxAge) => DateTimeOffset.UtcNow - _fetchedUtc > maxAge;

    /// <summary>
    /// The position table as it stands: which identity sits at which live position.
    /// </summary>
    /// <remarks>
    /// For diagnostics, and it earned the accessor. Two gearsets sharing a job and a name resolved to one
    /// identity, so this table held one of them and the other fell through to its stored index; both then
    /// displayed the same number, in two windows, and nothing on screen could show why. A table nobody can
    /// read is a table nobody can correct.
    /// </remarks>
    public IReadOnlyDictionary<string, int> LivePositions => _liveIndexByUid;

    /// <summary>When the targets were last fetched, or the minimum value if never.</summary>
    public DateTimeOffset FetchedUtc => _fetchedUtc;

    /// <summary>
    /// The live positions this side declined to identify because another gearset is indistinguishable
    /// from that one. Measured here rather than taken from a push answer: the server tells them apart by
    /// its own record and reports no doubt, so this is the only place the local blindness is visible.
    /// </summary>
    /// <remarks>
    /// Positions and not just a count, because the interface needs both. The status window says how many;
    /// the BiS window has to know <b>which</b>, since such a set falls into the "no target pinned" list
    /// for want of a match and would otherwise be told to pin a target it already has.
    /// </remarks>
    public IReadOnlySet<int> AmbiguousLive { get; private set; } = new HashSet<int>();

    /// <summary>
    /// Forgets which live position holds which gearset, because the list in game just changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="_uidByLiveIndex"/> is a photograph of the order at the moment the targets were fetched,
    /// and it was only ever rebuilt when a window opened. Reorder three sets and push, and the map still
    /// says position 8 belongs to the gearset that used to sit there: the tooltip asked for position 8 and
    /// was handed a neighbour's target, with nothing anywhere to suggest a doubt. Opening the gear window
    /// refreshed it and the same tooltip became right, which is what made it look like a display quirk
    /// rather than a stale key.
    /// </para>
    /// <para>
    /// Clearing is the safe half and it comes first: with no map, a position resolves to no identity, and
    /// a target that carries a uid is never reached through the position fallback. So between this call
    /// and the fetch that follows it, the tooltip says nothing instead of something wrong. That is the
    /// same rule as everywhere else here, and it is the one worth keeping when a refresh fails.
    /// </para>
    /// </remarks>
    public void Invalidate()
    {
        _uidByLiveIndex = new Dictionary<int, string>();
        _liveIndexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
        _comparisons = [];
        _comparedFor = null;
        _withoutTarget = [];
        _fetchedUtc = DateTimeOffset.MinValue;
        AmbiguousLive = new HashSet<int>();
    }

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
            _comparedFor = cidHash;
            var positions = LivePositionMap.Build(clean.Gearsets, set => _mapping.Resolve(cidHash, set));

            _uidByLiveIndex = positions.UidByIndex;
            _liveIndexByUid = positions.IndexByUid;
            AmbiguousLive = positions.Undecided;

            var targets = result.Value!.Data;
            _targets = targets.ToArray();
            _comparisons = BisComparer.Compare(clean, targets, set => Identify(cidHash, set)).ToArray();
            _withoutTarget = BisComparer.WithoutTarget(clean, targets, set => Identify(cidHash, set)).ToArray();
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


    /// <summary>
    /// The position the player will actually find a comparison at, which is not always the one the target
    /// carries.
    /// </summary>
    /// <param name="comparison">The comparison to place.</param>
    /// <returns>The live gearset index where it is known, the stored one otherwise.</returns>
    /// <remarks>
    /// A target index is the last display order the server stored and can sit in the 100 to 999 band,
    /// which is a number nobody can find in their list. The live list is the truth about position, and the
    /// identity is what connects the two.
    /// </remarks>
    public int DisplayIndex(GearsetComparison comparison)
    {
        var indexByUid = _liveIndexByUid;
        return comparison.SetUid is not null && indexByUid.TryGetValue(comparison.SetUid, out var live)
            ? live
            : comparison.GearIndex;
    }


    /// <summary>
    /// Whether this comparison rests on an attribution the server is still waiting to have confirmed.
    /// </summary>
    /// <param name="comparison">The comparison.</param>
    /// <returns><see langword="true"/> while the question about that gearset is open.</returns>
    /// <remarks>
    /// A held gearset gets the job default target like any new row, so it shows real numbers either way.
    /// The marker is what keeps those numbers from reading as settled: the target may change when the
    /// question is answered.
    /// </remarks>
    public bool IsProvisional(GearsetComparison comparison) =>
        _mapping.IsHeld(_comparedFor, comparison.SetUid);

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
    /// <summary>
    /// The identity to compare this live gearset under, or <see langword="null"/> where there is none to
    /// be had.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <param name="set">The live gearset.</param>
    /// <returns>The identity, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A withdrawn position counts as no identity. The mapping answers one gearset at a time and cannot
    /// see that another one just gave the same answer; the position table can, and does, and this used to
    /// ask past it straight back to the mapping. So the table would drop all the claims on a doubled
    /// identity while the comparison built from the same identity kept one of them, and a BiS target was
    /// drawn against one gearset and labelled with another one's number.
    /// </remarks>
    private string? Identify(string cidHash, GearsetDto set)
    {
        if (AmbiguousLive.Contains(set.GearIndex))
        {
            return null;
        }

        var match = _mapping.Resolve(cidHash, set);
        return match.IsResolved ? match.SetUid : null;
    }

    private void SetStatus(BisFetchStatus status) => Status = status;
}
