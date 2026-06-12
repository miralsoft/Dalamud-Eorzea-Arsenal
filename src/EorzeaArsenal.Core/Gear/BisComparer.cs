using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>Per-slot comparison outcome of live gear against a BiS target.</summary>
public enum SlotMatch
{
    /// <summary>The equipped item id matches the target (check <see cref="SlotComparison.MateriaMatch"/> too).</summary>
    Match,

    /// <summary>An item is equipped but its id differs from the target (an upgrade/change).</summary>
    ItemDiffers,

    /// <summary>No item is equipped in this slot.</summary>
    MissingCurrent,
}

/// <summary>The comparison of one equipment slot against its BiS target.</summary>
/// <param name="Slot">The slot key.</param>
/// <param name="CurrentItemId">The equipped item id, or <see langword="null"/> if empty.</param>
/// <param name="TargetItemId">The BiS target item id.</param>
/// <param name="Status">Whether the item matches.</param>
/// <param name="MateriaMatch">Whether the melded materia match (only meaningful when item ids match).</param>
public readonly record struct SlotComparison(
    string Slot,
    int? CurrentItemId,
    int TargetItemId,
    SlotMatch Status,
    bool MateriaMatch);

/// <summary>The comparison of one gearset against its BiS target.</summary>
public sealed class GearsetComparison
{
    /// <summary>The in-game gearset index.</summary>
    public required int GearIndex { get; init; }

    /// <summary>The job code.</summary>
    public required string Job { get; init; }

    /// <summary>The target's name, if any.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the player actually has a matching live gearset.</summary>
    public required bool HasLiveGearset { get; init; }

    /// <summary>Per-slot comparisons.</summary>
    public required IReadOnlyList<SlotComparison> Slots { get; init; }

    /// <summary>Number of slots that fully match (item id and materia).</summary>
    public int FullyMatchedSlots => Slots.Count(s => s.Status == SlotMatch.Match && s.MateriaMatch);

    /// <summary>Whether every slot matches item and materia.</summary>
    public bool IsComplete => Slots.Count > 0 && Slots.All(s => s.Status == SlotMatch.Match && s.MateriaMatch);
}

/// <summary>
/// Computes the per-slot diff of the player's live gear against the BiS targets from
/// <c>GET /gear/bis</c>. Pure and unit-tested. Targets are matched to live gearsets by
/// <c>gear_index</c> (+ <c>job</c>); rings are interchangeable (left/right) and materia order is
/// irrelevant, per the API contract.
/// </summary>
public static class BisComparer
{
    private const string RingLeft = "RingLeft";
    private const string RingRight = "RingRight";

    /// <summary>Compares the live gear against the BiS targets, one entry per resolvable target.</summary>
    /// <param name="live">The player's current (sanitized) gear.</param>
    /// <param name="targets">The BiS targets from the API.</param>
    /// <returns>One <see cref="GearsetComparison"/> per target.</returns>
    public static IReadOnlyList<GearsetComparison> Compare(GearData live, IReadOnlyList<BisGearset> targets)
    {
        var liveByKey = new Dictionary<(int, string), GearsetDto>();
        foreach (var set in live.Gearsets)
        {
            liveByKey[(set.GearIndex, set.Job)] = set;
        }

        var result = new List<GearsetComparison>(targets.Count);
        foreach (var target in targets)
        {
            liveByKey.TryGetValue((target.GearIndex, target.Job), out var liveSet);
            result.Add(new GearsetComparison
            {
                GearIndex = target.GearIndex,
                Job = target.Job,
                Name = target.Name,
                HasLiveGearset = liveSet is not null,
                Slots = CompareSlots(target.Items, liveSet?.Items),
            });
        }

        return result;
    }

    private static List<SlotComparison> CompareSlots(
        Dictionary<string, ItemDto> targetItems,
        Dictionary<string, ItemDto>? liveItems)
    {
        var slots = new List<SlotComparison>(targetItems.Count);

        // Non-ring slots: direct key comparison.
        foreach (var (slot, target) in targetItems)
        {
            if (slot is RingLeft or RingRight)
            {
                continue;
            }

            slots.Add(CompareOne(slot, target, Lookup(liveItems, slot)));
        }

        // Rings: interchangeable left/right — match target rings to the current ring pool by id.
        AddRingComparisons(targetItems, liveItems, slots);
        return slots;
    }

    private static void AddRingComparisons(
        Dictionary<string, ItemDto> targetItems,
        Dictionary<string, ItemDto>? liveItems,
        List<SlotComparison> slots)
    {
        var pool = new List<ItemDto>();
        if (Lookup(liveItems, RingLeft) is { } l)
        {
            pool.Add(l);
        }

        if (Lookup(liveItems, RingRight) is { } r)
        {
            pool.Add(r);
        }

        foreach (var slot in new[] { RingLeft, RingRight })
        {
            if (Lookup(targetItems, slot) is not { } target)
            {
                continue;
            }

            var idx = pool.FindIndex(p => p.Id == target.Id);
            if (idx >= 0)
            {
                var current = pool[idx];
                pool.RemoveAt(idx);
                slots.Add(new SlotComparison(slot, current.Id, target.Id, SlotMatch.Match, MateriaEqual(current.Materia, target.Materia)));
            }
            else if (pool.Count > 0)
            {
                var current = pool[0];
                pool.RemoveAt(0);
                slots.Add(new SlotComparison(slot, current.Id, target.Id, SlotMatch.ItemDiffers, false));
            }
            else
            {
                slots.Add(new SlotComparison(slot, null, target.Id, SlotMatch.MissingCurrent, false));
            }
        }
    }

    private static SlotComparison CompareOne(string slot, ItemDto target, ItemDto? current)
    {
        if (current is null)
        {
            return new SlotComparison(slot, null, target.Id, SlotMatch.MissingCurrent, false);
        }

        return current.Id == target.Id
            ? new SlotComparison(slot, current.Id, target.Id, SlotMatch.Match, MateriaEqual(current.Materia, target.Materia))
            : new SlotComparison(slot, current.Id, target.Id, SlotMatch.ItemDiffers, false);
    }

    private static ItemDto? Lookup(Dictionary<string, ItemDto>? items, string slot) =>
        items is not null && items.TryGetValue(slot, out var item) ? item : null;

    private static bool MateriaEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var sortedA = a.Order().ToArray();
        var sortedB = b.Order().ToArray();
        for (var i = 0; i < sortedA.Length; i++)
        {
            if (sortedA[i] != sortedB[i])
            {
                return false;
            }
        }

        return true;
    }
}
