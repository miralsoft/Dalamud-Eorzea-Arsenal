using System.Collections.Concurrent;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Fetches and caches the caller's owned quantity per item from <c>GET /me/holdings</c> — the one count
/// a live game read cannot match, because it includes retainers (as of their last visit) from the last
/// inventory sync. Reads are batched (60 ids/call) and de-duplicated; the cache is cleared on
/// <see cref="Invalidate"/> (call it after an inventory sync) so counts refresh. No game dependencies,
/// so it lives in Core and is unit-tested; never throws — a failed fetch leaves ids uncached for retry
/// (R8/P2). Callers should fall back to the live count until a holdings count is present.
/// </summary>
public sealed class HoldingsService
{
    private const int MaxIdsPerCall = 60;

    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ILog _log;

    private readonly ConcurrentDictionary<long, int> _cache = new();
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    /// <summary>Creates the holdings service.</summary>
    /// <param name="api">HTTP client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="log">Diagnostics sink.</param>
    public HoldingsService(IApiClient api, ITokenStore tokens, ILog log)
    {
        _api = api;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>The cached owned quantity for an item, when it has been fetched.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="count">The owned quantity.</param>
    /// <returns><see langword="true"/> when a count is cached.</returns>
    public bool TryGet(long itemId, out int count) => _cache.TryGetValue(itemId, out count);

    /// <summary>
    /// Drops every cached count (and any in-flight claim), so the next prefetch re-reads them — call
    /// after an inventory sync completes, since holdings just changed.
    /// </summary>
    public void Invalidate()
    {
        _cache.Clear();
        _inFlight.Clear();
    }

    /// <summary>
    /// Ensures the given ids are counted, fetching only those neither cached nor in flight, in chunks
    /// of <see cref="MaxIdsPerCall"/>. Never throws.
    /// </summary>
    /// <param name="itemIds">The ids to count.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PrefetchAsync(IEnumerable<long> itemIds, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

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
        var result = await _api.GetHoldingsAsync(key, chunk, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _log.Info($"Holdings fetch failed for {chunk.Count} id(s): {result.Error?.Kind}.");
            return;
        }

        var data = result.Value?.Data;
        foreach (var id in chunk)
        {
            // An id the server answered but did not mention holds none.
            _cache[id] = data is not null && data.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var count)
                ? count
                : 0;
        }
    }
}
