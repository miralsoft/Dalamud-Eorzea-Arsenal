using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Splits an <see cref="InventoryData"/> snapshot into one or more requests that each respect the
/// server limits (≤ 50 scopes, ≤ 4000 items, ≤ 256 KB). A <b>scope is never split across
/// requests</b>: the server replaces a scope's items per request that names it, so splitting a
/// scope would let a later request wipe what an earlier one stored. Whole scopes (including
/// intentionally empty ones, which clear a sold-out storage) are packed greedily; the rare scope
/// that is itself oversized has its items capped so a request is never rejected outright.
/// </summary>
public static class InventoryChunker
{
    // Conservative per-item byte estimate (real serialized items are smaller). Used only to pack
    // under the body limit without serializing repeatedly; the validator does the exact check.
    private const int EstimatedBytesPerItem = 100;
    private const int EstimatedBytesPerScope = 80;
    private const int EstimatedBaseBytes = 512;

    private static int BodyItemBudget =>
        Math.Max(1, (InventoryProtocol.MaxBodyBytes - EstimatedBaseBytes) / EstimatedBytesPerItem);

    /// <summary>Splits the snapshot into limit-respecting chunks (returns one chunk when it fits).</summary>
    /// <param name="data">The snapshot (already sanitized).</param>
    /// <returns>One or more <see cref="InventoryData"/> chunks; empty if there are no scopes.</returns>
    public static IReadOnlyList<InventoryData> Split(InventoryData data)
    {
        if (data.Scopes.Count == 0)
        {
            return [];
        }

        // Bucket items by scope, preserving order; ensure every scanned scope has a bucket so an
        // empty (cleared) scope still travels.
        var buckets = new Dictionary<string, List<InventoryItemDto>>(StringComparer.Ordinal);
        foreach (var scope in data.Scopes)
        {
            buckets[scope] = [];
        }

        foreach (var item in data.Items)
        {
            var scope = InventoryProtocol.ScopeForItem(item);
            if (buckets.TryGetValue(scope, out var list))
            {
                if (list.Count < InventoryProtocol.MaxItems && list.Count < BodyItemBudget)
                {
                    list.Add(item);
                }
            }
        }

        var chunks = new List<InventoryData>();
        var curScopes = new List<string>();
        var curItems = new List<InventoryItemDto>();

        void Flush()
        {
            if (curScopes.Count == 0)
            {
                return;
            }

            chunks.Add(new InventoryData
            {
                Character = data.Character,
                Scopes = curScopes,
                Items = curItems,
            });
            curScopes = [];
            curItems = [];
        }

        var bodyBudget = BodyItemBudget;
        foreach (var scope in data.Scopes)
        {
            var items = buckets[scope];

            var wouldExceed = curScopes.Count >= InventoryProtocol.MaxScopes ||
                curItems.Count + items.Count > InventoryProtocol.MaxItems ||
                curItems.Count + items.Count > bodyBudget;

            if (wouldExceed)
            {
                Flush();
            }

            curScopes.Add(scope);
            curItems.AddRange(items);
        }

        Flush();
        return chunks;
    }
}
