using System.Globalization;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// The drops of one duty (server 1.5, <c>contract-item-sources.md</c>): everything in the loot chest, once
/// per piece, as <c>chest</c>, and what the inventory gained without a chest as <c>received</c>.
/// </summary>
/// <remarks>
/// <para>
/// A chest piece is known by its chest and its place in it, so the same item in two chests is two drops.
/// When the player wins a piece it lands in the inventory; that gain is netted against the chest pieces of
/// the same item, piece by piece, so it is never reported twice.
/// </para>
/// <para>
/// What arrived without a chest is the inventory's gain between entering and leaving the duty, per item,
/// less the chest pieces of that item. Using, discarding or moving items never adds to a gain, so the only
/// way to get a received drop is to receive something. Pure: the game is read by the plugin.
/// </para>
/// </remarks>
public sealed class DropLedger
{
    private readonly HashSet<(uint Chest, uint Index)> _seen = [];
    private readonly Dictionary<int, int> _chestPieces = [];
    private Dictionary<int, int> _start = [];

    /// <summary>The duty being watched, or <see langword="null"/> outside one.</summary>
    public int? ContentId { get; private set; }

    /// <summary>Starts a duty: forgets the last one and keeps the inventory as it is now.</summary>
    /// <param name="contentId">The duty's InstanceContent row id.</param>
    /// <param name="inventory">Count per item id as the duty starts.</param>
    public void Start(int contentId, IReadOnlyDictionary<int, int> inventory)
    {
        ContentId = contentId;
        _seen.Clear();
        _chestPieces.Clear();
        _start = new Dictionary<int, int>(inventory);
    }

    /// <summary>A piece seen in the loot chest. Reported the first time only.</summary>
    /// <param name="chestObjectId">The chest.</param>
    /// <param name="chestItemIndex">Its place in the chest.</param>
    /// <param name="rawItemId">The item id as the game has it (an HQ offset is stripped).</param>
    /// <param name="count">How many lie there.</param>
    /// <param name="dayUtc">Today in UTC.</param>
    /// <returns>The report, or <see langword="null"/> when it was seen already or there is no duty.</returns>
    public DropReport? SeeChestItem(uint chestObjectId, uint chestItemIndex, uint rawItemId, int count, DateOnly dayUtc)
    {
        // Event and key items sit at 2,000,000 and above; they are no item of the catalog.
        var itemId = rawItemId >= 2_000_000 ? 0 : ItemIdNormalizer.Normalize(rawItemId);
        if (ContentId is not { } content || itemId <= 0 || !_seen.Add((chestObjectId, chestItemIndex)))
        {
            return null;
        }

        var qty = Math.Clamp(count, 1, 999);
        _chestPieces[itemId] = _chestPieces.GetValueOrDefault(itemId) + qty;
        return new DropReport { ItemId = itemId, Qty = qty, ContentId = content, Day = Day(dayUtc), Seen = DropSeen.Chest };
    }

    /// <summary>Ends the duty: what the inventory gained without a chest, then forgets the duty.</summary>
    /// <param name="inventory">Count per item id as the duty ends.</param>
    /// <param name="dayUtc">Today in UTC.</param>
    /// <returns>The received drops, one per item.</returns>
    public IReadOnlyList<DropReport> Finish(IReadOnlyDictionary<int, int> inventory, DateOnly dayUtc)
    {
        if (ContentId is not { } content)
        {
            return [];
        }

        var reports = new List<DropReport>();
        foreach (var (itemId, now) in inventory.OrderBy(kv => kv.Key))
        {
            var gain = now - _start.GetValueOrDefault(itemId) - _chestPieces.GetValueOrDefault(itemId);
            if (itemId > 0 && gain > 0)
            {
                reports.Add(new DropReport { ItemId = itemId, Qty = Math.Min(gain, 999), ContentId = content, Day = Day(dayUtc), Seen = DropSeen.Received });
            }
        }

        Abandon();
        return reports;
    }

    /// <summary>Forgets the duty without reporting what it gained (the setting was turned off, or a logout).</summary>
    public void Abandon()
    {
        ContentId = null;
        _seen.Clear();
        _chestPieces.Clear();
        _start = [];
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>How reports of each kind are told apart and combined before they are sent.</summary>
public static class ReportKeys
{
    /// <summary>A drop is the same when item, duty, day and where it was seen match.</summary>
    /// <param name="report">The drop.</param>
    /// <returns>The key.</returns>
    public static string Drop(DropReport report) => $"{report.ItemId}|{report.ContentId}|{report.Day}|{report.Seen}";

    /// <summary>Two drops with the same key add up, at most 999.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>One drop with both quantities.</returns>
    public static DropReport CombineDrops(DropReport a, DropReport b) => a with { Qty = Math.Min(a.Qty + b.Qty, 999) };

    /// <summary>A spot is the same when kind, id, zone and day match; one a day is enough.</summary>
    /// <param name="report">The spot.</param>
    /// <returns>The key.</returns>
    public static string World(WorldReport report) => $"{report.Kind}|{report.Id}|{report.ZoneId}|{report.Day}";

    /// <summary>Of two spots with the same key the first is kept, as the server does.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>The first.</returns>
    public static WorldReport KeepFirst(WorldReport a, WorldReport b) => a;
}
