using System.Collections.Concurrent;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Fetches and caches impersonal "how to get it" sourcing for gear pieces from
/// <c>GET /gear/obtain</c>. The data is the same for every player and patch-static, so a single
/// process-lifetime cache is enough — a piece is fetched once and read from memory thereafter. Reads
/// are batched (the server caps a call at 60 ids) and de-duplicated: ids already cached or already in
/// flight are never requested twice. No game dependencies, so it lives in Core and is unit-tested; all
/// HTTP goes through <see cref="IApiClient"/>. Never throws — a failed fetch simply leaves ids
/// uncached, so a later prefetch retries them (R8/P2).
/// </summary>
public sealed class ObtainService
{
    private const int MaxIdsPerCall = 60;

    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ILog _log;

    // itemId -> its sourcing, or null for a curated "no info yet". A key's mere presence means
    // "resolved, don't ask again"; the value distinguishes has-info from no-info.
    private readonly ConcurrentDictionary<long, ObtainInfo?> _cache = new();

    // Ids with a request in flight, so concurrent prefetches don't duplicate a call.
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    /// <summary>Creates the obtain service.</summary>
    /// <param name="api">HTTP client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="log">Diagnostics sink.</param>
    public ObtainService(IApiClient api, ITokenStore tokens, ILog log)
    {
        _api = api;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>
    /// Returns the cached sourcing for an item. <paramref name="info"/> is <see langword="null"/> both
    /// when the id is not resolved yet and when the server has no info for it; the return value tells
    /// the two apart, so a caller can show "how to get it" only once an answer is actually in.
    /// </summary>
    /// <param name="itemId">The gear item id.</param>
    /// <param name="info">The sourcing, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the id has been resolved (info may still be <see langword="null"/>).</returns>
    public bool TryGet(long itemId, out ObtainInfo? info) => _cache.TryGetValue(itemId, out info);

    /// <summary>
    /// Ensures the given ids are resolved, fetching only those neither cached nor in flight, in chunks
    /// of <see cref="MaxIdsPerCall"/>. Safe to call often (e.g. per window open) — it is a no-op once
    /// everything is cached. Never throws.
    /// </summary>
    /// <param name="itemIds">The ids to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PrefetchAsync(IEnumerable<long> itemIds, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        // Claim the ids we will fetch: not cached, not already in flight, positive, de-duplicated.
        var todo = new List<long>();
        foreach (var id in itemIds)
        {
            if (id > 0 && !_cache.ContainsKey(id) && _inFlight.TryAdd(id, 0))
            {
                todo.Add(id);
            }
        }

        if (todo.Count == 0)
        {
            return;
        }

        try
        {
            for (var start = 0; start < todo.Count; start += MaxIdsPerCall)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = todo.GetRange(start, Math.Min(MaxIdsPerCall, todo.Count - start));
                await FetchChunkAsync(key, chunk, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure; the unresolved ids stay uncached for a later retry.
        }
        finally
        {
            foreach (var id in todo)
            {
                _inFlight.TryRemove(id, out _);
            }
        }
    }

    private async Task FetchChunkAsync(string key, List<long> chunk, CancellationToken ct)
    {
        var result = await _api.GetGearObtainAsync(key, chunk, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Info($"Obtain fetch failed for {chunk.Count} id(s): {result.Error?.Kind}.");
            return;
        }

        var data = result.Value?.Data;
        foreach (var id in chunk)
        {
            // A server that answered but omitted an id is treated as a curated "no info" (null), so we
            // do not hammer it for the same id every time the window opens.
            _cache[id] = data is not null && data.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var info)
                ? info
                : null;
        }
    }
}
