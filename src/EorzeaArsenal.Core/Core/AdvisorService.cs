using System.Collections.Concurrent;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Reads and writes the player's saved purchase plans ("Kaufberater") via <c>/me/advisor-plan</c>,
/// cached per (character, job, target set) so switching sets does not re-fetch. A cached
/// <see langword="null"/> is a <b>loaded</b> "no plan saved" — a normal state that means "show the
/// recommendation instead", not an error. Writes only ever happen on a deliberate user action; there
/// is no background sync, because a plan is an explicit choice rather than observed state. No game
/// dependencies, so it lives in Core and is unit-tested; never throws (R8/P2).
/// </summary>
public sealed class AdvisorService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ILog _log;

    // Cache key → the stored plan, or null when the server answered "no plan for this set".
    private readonly ConcurrentDictionary<string, AdvisorPlan?> _plans = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AdvisorOptions> _options = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    /// <summary>Creates the advisor service.</summary>
    /// <param name="api">HTTP client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="log">Diagnostics sink.</param>
    public AdvisorService(IApiClient api, ITokenStore tokens, ILog log)
    {
        _api = api;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>Whether any read or write is currently running (for a "loading…" hint).</summary>
    public bool IsBusy => !_inFlight.IsEmpty;

    /// <summary>The last failure's kind, so the UI can explain a missing scope or a network problem.</summary>
    public ApiErrorKind? LastErrorKind { get; private set; }

    /// <summary>The cache key identifying one plan.</summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code (case-insensitive).</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <returns>A stable key.</returns>
    public static string CacheKey(long characterId, string job, string target) =>
        $"{characterId}|{job.ToLowerInvariant()}|{target}";

    /// <summary>
    /// The plan for a set, when it has been read. <paramref name="plan"/> is <see langword="null"/>
    /// together with a <see langword="true"/> return when the set has no saved plan.
    /// </summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code.</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <param name="plan">The stored plan, or <see langword="null"/> when none is saved.</param>
    /// <returns><see langword="true"/> when the set has been read (with or without a plan).</returns>
    public bool TryGet(long characterId, string job, string target, out AdvisorPlan? plan) =>
        _plans.TryGetValue(CacheKey(characterId, job, target), out plan);

    /// <summary>
    /// Reads a set's plan unless it is already cached or in flight. Never throws; a failure leaves the
    /// set uncached so the next call retries.
    /// </summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code.</param>
    /// <param name="target">The target set's apiPath / shortlink (a blank target does nothing).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureAsync(long characterId, string job, string target, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || characterId <= 0 || string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var cacheKey = CacheKey(characterId, job, target);
        if (_plans.ContainsKey(cacheKey) || !_inFlight.TryAdd(cacheKey, 0))
        {
            return;
        }

        try
        {
            var result = await _api.GetAdvisorPlanAsync(key, characterId, job, target, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // data: null is a valid answer — remember it, so we do not ask again every frame.
                _plans[cacheKey] = result.Value?.Data;
                LastErrorKind = null;
            }
            else
            {
                LastErrorKind = result.Error?.Kind;
                _log.Info($"Advisor plan read failed: {result.Error?.Kind}.");
            }
        }
        catch (OperationCanceledException)
        {
            // Not a failure; the set stays uncached for a later retry.
        }
        catch (Exception ex)
        {
            _log.Error($"Advisor plan read failed: {ex.GetType().Name}.");
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>The advice for a set + ranking, when it has been read.</summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code.</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <param name="sort">The ranking (<c>power</c>, <c>value</c>, <c>cheap</c>).</param>
    /// <param name="options">The computed advice.</param>
    /// <returns><see langword="true"/> when the advice is cached.</returns>
    public bool TryGetOptions(long characterId, string job, string target, string sort, out AdvisorOptions? options) =>
        _options.TryGetValue(OptionsKey(characterId, job, target, sort), out options);

    /// <summary>
    /// Reads a set's advice unless it is already cached or in flight. Unlike a plan, the advice depends
    /// on the character's current gear and holdings, so it is dropped by <see cref="InvalidateOptions"/>
    /// after anything that changes those. Never throws.
    /// </summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code.</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <param name="gearIndex">Optional gearset index (disambiguates two sets of the same job).</param>
    /// <param name="sort">The ranking to compute.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureOptionsAsync(long characterId, string job, string target, int? gearIndex, string sort, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || characterId <= 0 || string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var cacheKey = OptionsKey(characterId, job, target, sort);
        if (_options.ContainsKey(cacheKey) || !_inFlight.TryAdd(cacheKey, 0))
        {
            return;
        }

        try
        {
            var result = await _api.GetAdvisorOptionsAsync(key, characterId, job, target, gearIndex, sort, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Value?.Data is { } data)
            {
                _options[cacheKey] = data;
                LastErrorKind = null;
            }
            else if (!result.IsSuccess)
            {
                LastErrorKind = result.Error?.Kind;
                _log.Info($"Advisor options read failed: {result.Error?.Kind}.");
            }
        }
        catch (OperationCanceledException)
        {
            // Not a failure; the set stays uncached for a later retry.
        }
        catch (Exception ex)
        {
            _log.Error($"Advisor options read failed: {ex.GetType().Name}.");
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// Drops the cached advice, so the next read recomputes it. Call after anything that changes the
    /// inputs — an inventory sync, a gear change, a tomestone push — or on an explicit refresh. Plans
    /// are untouched: they are stored values, not derived ones.
    /// </summary>
    public void InvalidateOptions() => _options.Clear();

    private static string OptionsKey(long characterId, string job, string target, string sort) =>
        $"{characterId}|{job.ToLowerInvariant()}|{target}|{sort}";

    /// <summary>
    /// Saves a plan for a set and updates the cache from the server's echo. An empty
    /// <paramref name="items"/> map clears the plan server-side.
    /// </summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code (sent lower-case).</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <param name="targetName">Optional display name of the target set.</param>
    /// <param name="items">Slot → item id for the intended layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server stored it.</returns>
    public async Task<bool> SaveAsync(
        long characterId,
        string job,
        string target,
        string? targetName,
        IReadOnlyDictionary<string, long> items,
        CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || characterId <= 0 || string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var cacheKey = CacheKey(characterId, job, target);
        _inFlight.TryAdd(cacheKey, 0);
        try
        {
            var request = new AdvisorPlanRequest
            {
                CharacterId = characterId,
                Job = job.ToLowerInvariant(),
                Target = target,
                TargetName = targetName,
                Items = items
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => new AdvisorPlanItem { Id = kv.Value }, StringComparer.Ordinal),
            };

            var result = await _api.PutAdvisorPlanAsync(key, request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LastErrorKind = result.Error?.Kind;
                _log.Info($"Advisor plan save failed: {result.Error?.Kind}.");
                return false;
            }

            LastErrorKind = null;

            // Trust the echo when there is one, so the view shows exactly what the server sanitised to;
            // an echo-less success still leaves the cache consistent with what we sent.
            _plans[cacheKey] = result.Value?.Data ?? new AdvisorPlan
            {
                Job = request.Job,
                Target = target,
                TargetName = targetName,
                Items = request.Items,
            };
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Advisor plan save failed: {ex.GetType().Name}.");
            return false;
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>Drops a set's saved plan, so the view falls back to the recommendation.</summary>
    /// <param name="characterId">The caller's own server character id.</param>
    /// <param name="job">The job code.</param>
    /// <param name="target">The target set's apiPath / shortlink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when the server accepted the delete.</returns>
    public async Task<bool> DeleteAsync(long characterId, string job, string target, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || characterId <= 0 || string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var cacheKey = CacheKey(characterId, job, target);
        _inFlight.TryAdd(cacheKey, 0);
        try
        {
            var request = new AdvisorPlanDeleteRequest
            {
                CharacterId = characterId,
                Job = job.ToLowerInvariant(),
                Target = target,
            };

            var result = await _api.DeleteAdvisorPlanAsync(key, request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LastErrorKind = result.Error?.Kind;
                _log.Info($"Advisor plan delete failed: {result.Error?.Kind}.");
                return false;
            }

            LastErrorKind = null;
            _plans[cacheKey] = null; // deleted = "no plan saved", already known — no re-read needed
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Advisor plan delete failed: {ex.GetType().Name}.");
            return false;
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>Drops every cached plan and advice, so the next read hits the server (e.g. after reconnecting).</summary>
    public void Invalidate()
    {
        _plans.Clear();
        _options.Clear();
        _inFlight.Clear();
        LastErrorKind = null;
    }
}
