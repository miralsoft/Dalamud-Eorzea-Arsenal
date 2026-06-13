using EorzeaArsenal.Abstractions;
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
/// <param name="GearIndex">The gearset index.</param>
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

    private volatile bool _loading;
    private volatile GearsetComparison[] _comparisons = [];
    private volatile BisGearset[] _targets = [];
    private DateTimeOffset _fetchedUtc = DateTimeOffset.MinValue;

    /// <summary>Creates the service.</summary>
    /// <param name="api">API client.</param>
    /// <param name="gearSource">Live gear source.</param>
    /// <param name="tokens">Token store.</param>
    /// <param name="log">Diagnostics sink.</param>
    public BisService(IApiClient api, IGearSource gearSource, ITokenStore tokens, ILog log)
    {
        _api = api;
        _gearSource = gearSource;
        _tokens = tokens;
        _log = log;
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

            var targets = result.Value!.Data;
            _targets = targets.ToArray();
            _comparisons = BisComparer.Compare(clean, targets).ToArray();
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
        foreach (var target in _targets)
        {
            foreach (var (slot, item) in target.Items)
            {
                if (item.Id == itemId)
                {
                    hits.Add(new BisHit(target.Job, target.GearIndex, target.Name, slot));
                }
            }
        }

        return hits;
    }

    /// <summary>Returns the live-vs-BiS comparison for one slot, if known.</summary>
    /// <param name="gearIndex">The gearset index.</param>
    /// <param name="job">The job code.</param>
    /// <param name="slot">The slot key.</param>
    /// <returns>The slot comparison, or <see langword="null"/>.</returns>
    public SlotComparison? SlotStatus(int gearIndex, string job, string slot)
    {
        foreach (var comparison in _comparisons)
        {
            if (comparison.GearIndex != gearIndex || comparison.Job != job)
            {
                continue;
            }

            foreach (var slotComparison in comparison.Slots)
            {
                if (slotComparison.Slot == slot)
                {
                    return slotComparison;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the BiS target item for one slot of a gearset, if any.</summary>
    /// <param name="gearIndex">The gearset index.</param>
    /// <param name="slot">The slot key.</param>
    /// <returns>The target item, or <see langword="null"/>.</returns>
    public ItemDto? TargetForSlot(int gearIndex, string slot)
    {
        foreach (var target in _targets)
        {
            if (target.GearIndex == gearIndex && target.Items.TryGetValue(slot, out var item))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Returns the live-vs-BiS comparison for one slot of a gearset (by index), if known.</summary>
    /// <param name="gearIndex">The gearset index.</param>
    /// <param name="slot">The slot key.</param>
    /// <returns>The slot comparison, or <see langword="null"/>.</returns>
    public SlotComparison? SlotComparisonByIndex(int gearIndex, string slot)
    {
        foreach (var comparison in _comparisons)
        {
            if (comparison.GearIndex != gearIndex)
            {
                continue;
            }

            foreach (var slotComparison in comparison.Slots)
            {
                if (slotComparison.Slot == slot)
                {
                    return slotComparison;
                }
            }
        }

        return null;
    }

    private void SetStatus(BisFetchStatus status) => Status = status;
}
