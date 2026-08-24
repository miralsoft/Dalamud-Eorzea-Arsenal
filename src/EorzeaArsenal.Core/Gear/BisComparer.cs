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
/// <param name="MissingMateria">
/// Target materia item ids not yet present — for a matching item, what to socket; for a
/// different/empty item, the target item's full materia.
/// </param>
/// <param name="ExtraMateria">
/// Equipped materia item ids that are wrong (present but not in the target) — what to remove.
/// Only populated when the item id matches.
/// </param>
public readonly record struct SlotComparison(
    string Slot,
    int? CurrentItemId,
    int TargetItemId,
    SlotMatch Status,
    bool MateriaMatch,
    IReadOnlyList<int> MissingMateria,
    IReadOnlyList<int> ExtraMateria);

/// <summary>The comparison of one gearset against its BiS target.</summary>
public sealed class GearsetComparison
{
    /// <summary>The in-game gearset index. <b>Display order</b> — not what the target was matched by.</summary>
    public required int GearIndex { get; init; }

    /// <summary>
    /// The server's identity for the gearset this target belongs to, when it sends one. This is what
    /// the match was made on; <see langword="null"/> means the server does not mint identities yet and
    /// <see cref="GearIndex"/> had to serve as the key.
    /// </summary>
    public string? SetUid { get; init; }

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
/// <c>GET /gear/bis</c>. Pure and unit-tested. Rings are interchangeable (left/right) and materia order
/// is irrelevant, per the API contract.
/// </summary>
/// <remarks>
/// Targets are matched to live gearsets by <c>set_uid</c>, the identity the server mints. The old key
/// was <c>(gear_index, job)</c> — the position — and it was silently wrong the moment anything
/// reordered the list: a tank set compared against a healer's target shows plausible, false numbers.
/// The position remains as a fallback for exactly one case, a server that does not send identities yet;
/// it is chosen per target, so a mixed response works too.
/// </remarks>
public static class BisComparer
{
    private const string RingLeft = "RingLeft";
    private const string RingRight = "RingRight";

    /// <summary>Compares the live gear against the BiS targets, one entry per resolvable target.</summary>
    /// <param name="live">The player's current (sanitized) gear.</param>
    /// <param name="targets">The BiS targets from the API.</param>
    /// <param name="identify">
    /// Resolves a live gearset to its <c>set_uid</c> — the mapping the server minted, read from a push
    /// response or from <c>GET /gear/sets</c>. Return <see langword="null"/> when it cannot be resolved;
    /// the target is then reported without a live gearset rather than attached to a guess.
    /// <see langword="null"/> for the whole delegate means "no identities available", which puts every
    /// target on the position fallback.
    /// </param>
    /// <returns>One <see cref="GearsetComparison"/> per target.</returns>
    public static IReadOnlyList<GearsetComparison> Compare(
        GearData live,
        IReadOnlyList<BisGearset> targets,
        Func<GearsetDto, string?>? identify = null)
    {
        var index = LiveIndex.Build(live, identify);

        var result = new List<GearsetComparison>(targets.Count);
        foreach (var target in targets)
        {
            var liveSet = index.Claim(target);
            result.Add(new GearsetComparison
            {
                GearIndex = target.GearIndex,
                SetUid = target.SetUid,
                Job = target.Job,
                Name = target.Name,
                HasLiveGearset = liveSet is not null,
                Slots = CompareSlots(target.Items, liveSet?.Items),
            });
        }

        return result;
    }

    /// <summary>
    /// Compares one target against gear already known to belong to it, with no pairing step at all.
    /// </summary>
    /// <param name="target">The BiS target.</param>
    /// <param name="liveItems">The gear that belongs to it, by slot.</param>
    /// <returns>The comparison, always with a live gearset attached.</returns>
    /// <remarks>
    /// For a caller that has already established the pair by identity. Sending such a pair through
    /// <see cref="Compare"/> only gives the pairing a second chance to fail, and it did: a target
    /// carrying a <c>set_uid</c> is looked up by uid and never by position, so a caller that passes no
    /// resolver hands over a target that can match nothing, and every slot comes back as missing. That
    /// is what the in-game tooltip did from the day the server began minting identities.
    /// </remarks>
    public static GearsetComparison CompareKnownPair(BisGearset target, Dictionary<string, ItemDto> liveItems) => new()
    {
        GearIndex = target.GearIndex,
        SetUid = target.SetUid,
        Job = target.Job,
        Name = target.Name,
        HasLiveGearset = true,
        Slots = CompareSlots(target.Items, liveItems),
    };

    /// <summary>
    /// The live gearsets no target claims, in the order the player has them.
    /// </summary>
    /// <param name="live">The player current (sanitized) gear.</param>
    /// <param name="targets">The BiS targets from the API.</param>
    /// <param name="identify">Same resolver as <see cref="Compare"/>.</param>
    /// <returns>The unclaimed live gearsets.</returns>
    /// <remarks>
    /// These are not a fault and not a failed transfer. A crafter set, a gatherer set or a base class has
    /// no catalogue to compare against, and a set whose target nobody pinned has nothing to compare
    /// either. Both are synced perfectly well, and the reason this exists at all is that
    /// <see cref="Compare"/> walks the <i>targets</i>: a live set with no target produces no entry, so it
    /// would simply be absent from the window, and a set that vanishes gets reported as a bug.
    /// </remarks>
    public static IReadOnlyList<GearsetDto> WithoutTarget(
        GearData live,
        IReadOnlyList<BisGearset> targets,
        Func<GearsetDto, string?>? identify = null)
    {
        var index = LiveIndex.Build(live, identify);
        foreach (var target in targets)
        {
            index.Claim(target);
        }

        var unclaimed = new List<GearsetDto>();
        foreach (var set in live.Gearsets)
        {
            if (!index.IsClaimed(set))
            {
                unclaimed.Add(set);
            }
        }

        return unclaimed;
    }

    /// <summary>
    /// The live list keyed the two ways a target can point at it, so both callers make the same decision
    /// rather than two that drift apart.
    /// </summary>
    private sealed class LiveIndex
    {
        private readonly Dictionary<string, GearsetDto> _byUid = new(StringComparer.Ordinal);
        private readonly Dictionary<(int, string), GearsetDto> _byPosition = [];
        private readonly HashSet<GearsetDto> _claimed = [];

        public static LiveIndex Build(GearData live, Func<GearsetDto, string?>? identify)
        {
            var index = new LiveIndex();
            foreach (var set in live.Gearsets)
            {
                index._byPosition[(set.GearIndex, set.Job)] = set;

                var uid = identify?.Invoke(set);
                if (!string.IsNullOrEmpty(uid))
                {
                    index._byUid[uid!] = set;
                }
            }

            return index;
        }

        /// <summary>
        /// The live gearset a target points at, or <see langword="null"/>. Decided per target rather than
        /// per response: a server mid-migration can answer with identities on some rows and not on others,
        /// and each row deserves the best key it actually carries.
        /// </summary>
        public GearsetDto? Claim(BisGearset target)
        {
            GearsetDto? liveSet;
            if (!string.IsNullOrEmpty(target.SetUid))
            {
                // The target has an identity, so the position is not consulted at all. A miss here is a
                // real miss: falling back to the position would resurrect the bug this replaces.
                _byUid.TryGetValue(target.SetUid!, out liveSet);
            }
            else
            {
                _byPosition.TryGetValue((target.GearIndex, target.Job), out liveSet);
            }

            if (liveSet is not null)
            {
                _claimed.Add(liveSet);
            }

            return liveSet;
        }

        public bool IsClaimed(GearsetDto set) => _claimed.Contains(set);
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

        var targets = new List<(string Slot, ItemDto Item)>(2);
        foreach (var slot in new[] { RingLeft, RingRight })
        {
            if (Lookup(targetItems, slot) is { } target)
            {
                targets.Add((slot, target));
            }
        }

        var resolved = new bool[targets.Count];

        // Pass 1: claim exact matches (same id AND same materia) first, so two same-id rings with
        // different materia each pair with the right one regardless of which finger they sit on.
        for (var i = 0; i < targets.Count; i++)
        {
            var idx = pool.FindIndex(p => p.Id == targets[i].Item.Id && MateriaEqual(p.Materia, targets[i].Item.Materia));
            if (idx >= 0)
            {
                slots.Add(new SlotComparison(targets[i].Slot, pool[idx].Id, targets[i].Item.Id, SlotMatch.Match, true, [], []));
                pool.RemoveAt(idx);
                resolved[i] = true;
            }
        }

        // Pass 2: same item id but different materia → match, materia differs.
        for (var i = 0; i < targets.Count; i++)
        {
            if (resolved[i])
            {
                continue;
            }

            var idx = pool.FindIndex(p => p.Id == targets[i].Item.Id);
            if (idx >= 0)
            {
                var (missing, extra) = MateriaDiff(pool[idx].Materia, targets[i].Item.Materia);
                slots.Add(new SlotComparison(targets[i].Slot, pool[idx].Id, targets[i].Item.Id, SlotMatch.Match, false, missing, extra));
                pool.RemoveAt(idx);
                resolved[i] = true;
            }
        }

        // Pass 3: leftovers → a different ring is worn, or the slot is empty.
        for (var i = 0; i < targets.Count; i++)
        {
            if (resolved[i])
            {
                continue;
            }

            var targetMateria = targets[i].Item.Materia.ToList();
            if (pool.Count > 0)
            {
                slots.Add(new SlotComparison(targets[i].Slot, pool[0].Id, targets[i].Item.Id, SlotMatch.ItemDiffers, false, targetMateria, []));
                pool.RemoveAt(0);
            }
            else
            {
                slots.Add(new SlotComparison(targets[i].Slot, null, targets[i].Item.Id, SlotMatch.MissingCurrent, false, targetMateria, []));
            }
        }
    }

    private static SlotComparison CompareOne(string slot, ItemDto target, ItemDto? current)
    {
        if (current is null)
        {
            return new SlotComparison(slot, null, target.Id, SlotMatch.MissingCurrent, false, target.Materia.ToList(), []);
        }

        if (current.Id != target.Id)
        {
            return new SlotComparison(slot, current.Id, target.Id, SlotMatch.ItemDiffers, false, target.Materia.ToList(), []);
        }

        var (missing, extra) = MateriaDiff(current.Materia, target.Materia);
        return new SlotComparison(slot, current.Id, target.Id, SlotMatch.Match, missing.Count == 0 && extra.Count == 0, missing, extra);
    }

    /// <summary>
    /// Computes, as multisets, which target materia are missing from the current set (to socket)
    /// and which current materia are extra/wrong (to remove).
    /// </summary>
    private static (List<int> Missing, List<int> Extra) MateriaDiff(IReadOnlyList<int> current, IReadOnlyList<int> target)
    {
        var currentCounts = Counts(current);
        var targetCounts = Counts(target);

        var missing = new List<int>();
        foreach (var (id, count) in targetCounts)
        {
            for (var n = currentCounts.GetValueOrDefault(id); n < count; n++)
            {
                missing.Add(id);
            }
        }

        var extra = new List<int>();
        foreach (var (id, count) in currentCounts)
        {
            for (var n = targetCounts.GetValueOrDefault(id); n < count; n++)
            {
                extra.Add(id);
            }
        }

        return (missing, extra);
    }

    private static Dictionary<int, int> Counts(IReadOnlyList<int> values)
    {
        var counts = new Dictionary<int, int>();
        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
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
