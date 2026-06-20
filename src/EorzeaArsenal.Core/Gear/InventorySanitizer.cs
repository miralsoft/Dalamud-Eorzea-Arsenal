using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Cleans a freshly-scanned <see cref="InventoryData"/> snapshot so it passes validation and
/// matches what the server stores: it drops items with non-real ids, drops the forbidden
/// <see cref="InventoryProtocol.ScopeManual"/> scope, de-duplicates the scope list, aggregates
/// identical items (same id + container + source + hq) by summing quantities, and drops items
/// whose derived scope was not part of the scanned scopes (the server would ignore them anyway).
/// An <b>empty scope is preserved</b> on purpose — that is how a sold-out storage is cleared.
/// Keeping this game-agnostic cleanup pure makes it unit-testable.
/// </summary>
public static class InventorySanitizer
{
    /// <summary>Returns a sanitized copy of the snapshot, ready for validation and sending.</summary>
    /// <param name="data">The raw snapshot read from the game.</param>
    /// <returns>A cleaned snapshot; never <see langword="null"/>.</returns>
    public static InventoryData Sanitize(InventoryData data)
    {
        // De-duplicate scopes, drop the forbidden "manual" scope, keep input order otherwise.
        var scopes = new List<string>();
        var seenScopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in data.Scopes)
        {
            if (string.IsNullOrEmpty(scope) || scope == InventoryProtocol.ScopeManual)
            {
                continue;
            }

            if (seenScopes.Add(scope))
            {
                scopes.Add(scope);
            }
        }

        // Aggregate identical items; key by (item, container, source, hq). The server keys on the
        // same tuple, so summing here keeps qty correct and the payload compact.
        var aggregated = new Dictionary<(int Id, string Container, string SourceId, bool Hq), int>();
        var order = new List<(int Id, string Container, string SourceId, bool Hq)>();
        foreach (var item in data.Items)
        {
            if (!IsRealItemId(item.ItemId) || string.IsNullOrEmpty(item.Container))
            {
                continue;
            }

            // Drop items whose scope was not actually scanned this time.
            var dto = Normalize(item);
            if (!seenScopes.Contains(InventoryProtocol.ScopeForItem(dto)))
            {
                continue;
            }

            var key = (dto.ItemId, dto.Container, dto.SourceId, dto.Hq);
            if (aggregated.TryGetValue(key, out var qty))
            {
                aggregated[key] = qty + Math.Max(1, item.Qty);
            }
            else
            {
                aggregated[key] = Math.Max(1, item.Qty);
                order.Add(key);
            }
        }

        var items = new List<InventoryItemDto>(order.Count);
        foreach (var key in order)
        {
            items.Add(new InventoryItemDto
            {
                ItemId = key.Id,
                Container = key.Container,
                SourceId = key.SourceId,
                Hq = key.Hq,
                Qty = aggregated[key],
            });
        }

        return new InventoryData
        {
            Character = data.Character,
            Scopes = scopes,
            Items = items,
        };
    }

    private static InventoryItemDto Normalize(InventoryItemDto item)
    {
        // Retainer items must carry a source id; non-retainer items must not.
        var isRetainer = item.Container == InventoryContainers.Retainer;
        var sourceId = isRetainer ? item.SourceId : string.Empty;
        if (sourceId.Length > InventoryProtocol.MaxSourceIdLength)
        {
            sourceId = sourceId[..InventoryProtocol.MaxSourceIdLength];
        }

        return new InventoryItemDto
        {
            ItemId = item.ItemId,
            Container = item.Container,
            SourceId = sourceId,
            Hq = item.Hq,
            Qty = Math.Max(1, item.Qty),
        };
    }

    private static bool IsRealItemId(int id) =>
        id is >= ProtocolConstants.MinItemId and <= ProtocolConstants.MaxItemId;
}
