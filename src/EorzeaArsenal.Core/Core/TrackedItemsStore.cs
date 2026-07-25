using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>One display group of the active tier's tracked items (materials, stone, books).</summary>
/// <param name="Kind">The group kind as the server names it: <c>material</c>, <c>stone</c> or <c>book</c>.</param>
/// <param name="ItemIds">The group's item ids, in the server's display order.</param>
public sealed record TrackedStockGroup(string Kind, IReadOnlyList<int> ItemIds);

/// <summary>
/// The tracked consumable ids (raid books/tokens, upgrade materials), from
/// <c>GET /gear/tracked-items</c>. The inventory scan reports these alongside gear + coffers so the
/// server's holdings — and therefore the "have / need" view — have their counts. Server-curated and
/// bounded on purpose, so a tier rotation updates the set without a plugin release. Thread-safe: both
/// the set and the groups are swapped whole, read by the framework-thread scan and the UI.
/// </summary>
public sealed class TrackedItemsStore
{
    private volatile HashSet<int> _ids = [];
    private volatile IReadOnlyList<TrackedStockGroup> _groups = [];

    /// <summary>Whether any ids are tracked yet (the list may not have loaded).</summary>
    public bool HasAny => _ids.Count > 0;

    /// <summary>
    /// The active tier's items grouped for display, in the server's order. Empty until the list has
    /// loaded, or on a server that does not send groups — the stock view then simply stays hidden.
    /// </summary>
    public IReadOnlyList<TrackedStockGroup> Groups => _groups;

    /// <summary>Whether an item id is one the plugin should report for owned-count coverage.</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns><see langword="true"/> when tracked.</returns>
    public bool Contains(int itemId) => _ids.Contains(itemId);

    /// <summary>Replaces the tracked set (whole-swap, so a concurrent read sees a consistent list).</summary>
    /// <param name="ids">The new ids (non-positive and out-of-range values are dropped).</param>
    public void Set(IEnumerable<long> ids) =>
        _ids = ids.Where(id => id is > 0 and <= int.MaxValue).Select(id => (int)id).ToHashSet();

    /// <summary>
    /// Replaces the display groups (whole-swap). Groups without a kind or without usable ids are
    /// dropped, so the stock view never renders an empty heading.
    /// </summary>
    /// <param name="groups">The groups as sent by the server; <see langword="null"/> clears them.</param>
    public void SetGroups(IEnumerable<TrackedItemGroup>? groups)
    {
        if (groups is null)
        {
            _groups = [];
            return;
        }

        var built = new List<TrackedStockGroup>();
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Kind) || group.Ids is null)
            {
                continue;
            }

            var ids = group.Ids.Where(id => id is > 0 and <= int.MaxValue).Select(id => (int)id).ToList();
            if (ids.Count > 0)
            {
                built.Add(new TrackedStockGroup(group.Kind, ids));
            }
        }

        _groups = built;
    }
}
