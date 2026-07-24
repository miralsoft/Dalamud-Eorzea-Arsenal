namespace EorzeaArsenal.Core;

/// <summary>
/// The active tier's tracked consumable ids (raid books/tokens, upgrade materials), from
/// <c>GET /gear/tracked-items</c>. The inventory scan reports these alongside gear + coffers so the
/// server's holdings — and therefore the "have / need" view — have their counts. Server-curated and
/// bounded on purpose, so a tier rotation updates the set without a plugin release. Thread-safe: the
/// set is swapped whole, read by the framework-thread inventory scan.
/// </summary>
public sealed class TrackedItemsStore
{
    private volatile HashSet<int> _ids = [];

    /// <summary>Whether any ids are tracked yet (the list may not have loaded).</summary>
    public bool HasAny => _ids.Count > 0;

    /// <summary>Whether an item id is one the plugin should report for owned-count coverage.</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns><see langword="true"/> when tracked.</returns>
    public bool Contains(int itemId) => _ids.Contains(itemId);

    /// <summary>Replaces the tracked set (whole-swap, so a concurrent read sees a consistent list).</summary>
    /// <param name="ids">The new ids (non-positive and out-of-range values are dropped).</param>
    public void Set(IEnumerable<long> ids) =>
        _ids = ids.Where(id => id is > 0 and <= int.MaxValue).Select(id => (int)id).ToHashSet();
}
