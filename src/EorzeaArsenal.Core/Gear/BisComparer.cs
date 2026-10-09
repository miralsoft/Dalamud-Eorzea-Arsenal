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
/// <param name="HqMissing">
/// The right item is worn, but NQ where the target wants HQ. Not reached; a piece whose HQ state is not
/// known is never counted here.
/// </param>
/// <param name="Melds">
/// For a crafter or gatherer target whose item is worn: per target materia slot, in slot order, whether
/// it is filled. <see langword="null"/> for every other slot.
/// </param>
public readonly record struct SlotComparison(
    string Slot,
    int? CurrentItemId,
    int TargetItemId,
    SlotMatch Status,
    bool MateriaMatch,
    IReadOnlyList<int> MissingMateria,
    IReadOnlyList<int> ExtraMateria,
    bool HqMissing = false,
    IReadOnlyList<MeldSlot>? Melds = null)
{
    /// <summary>The right item, its materia done, and HQ where the target asks for it.</summary>
    public bool IsComplete => Status == SlotMatch.Match && MateriaMatch && !HqMissing;
}

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

    /// <summary>Number of slots that are complete (item, materia, and HQ where asked for).</summary>
    public int FullyMatchedSlots => Slots.Count(s => s.IsComplete);

    /// <summary>Whether every slot is complete.</summary>
    public bool IsComplete => Slots.Count > 0 && Slots.All(s => s.IsComplete);
}

/// <summary>
/// Computes the per-slot diff of the player's live gear against the BiS targets from
/// <c>GET /gear/bis</c>. Pure and unit-tested. Rings are interchangeable (left/right) and materia order
/// is irrelevant, per the API contract. A crafter or gatherer target judges its materia the way the web
/// does instead (<see cref="CraftStats.MeldMatch"/>), and on every target a worn NQ piece where HQ is
/// asked for is not reached.
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
    /// <param name="tables">
    /// The answer's <c>craft_tables</c>, which crafter and gatherer targets judge their materia with;
    /// <see langword="null"/> when it had none.
    /// </param>
    /// <returns>One <see cref="GearsetComparison"/> per target.</returns>
    public static IReadOnlyList<GearsetComparison> Compare(
        GearData live,
        IReadOnlyList<BisGearset> targets,
        Func<GearsetDto, string?>? identify = null,
        CraftTables? tables = null)
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
                Slots = CompareSlots(target, liveSet?.Items, tables),
            });
        }

        return result;
    }

    /// <summary>
    /// Compares one target against gear already known to belong to it, with no pairing step at all.
    /// </summary>
    /// <param name="target">The BiS target.</param>
    /// <param name="liveItems">The gear that belongs to it, by slot.</param>
    /// <param name="tables">The answer's <c>craft_tables</c>, or <see langword="null"/>.</param>
    /// <returns>The comparison, always with a live gearset attached.</returns>
    /// <remarks>
    /// For a caller that has already established the pair by identity. Sending such a pair through
    /// <see cref="Compare"/> only gives the pairing a second chance to fail, and it did: a target
    /// carrying a <c>set_uid</c> is looked up by uid and never by position, so a caller that passes no
    /// resolver hands over a target that can match nothing, and every slot comes back as missing. That
    /// is what the in-game tooltip did from the day the server began minting identities.
    /// </remarks>
    public static GearsetComparison CompareKnownPair(BisGearset target, Dictionary<string, ItemDto> liveItems, CraftTables? tables = null) => new()
    {
        GearIndex = target.GearIndex,
        SetUid = target.SetUid,
        Job = target.Job,
        Name = target.Name,
        HasLiveGearset = true,
        Slots = CompareSlots(target, liveItems, tables),
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
            var doubled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in live.Gearsets)
            {
                index._byPosition[(set.GearIndex, set.Job)] = set;

                var uid = identify?.Invoke(set);
                if (string.IsNullOrEmpty(uid) || doubled.Contains(uid!))
                {
                    continue;
                }

                // One identity cannot be two gearsets, so a second claimant does not win the key, it
                // empties it. Copy a gearset and the copy is indistinguishable from the original until the
                // next push tells the server it exists, so both come back under the same identity. This
                // was a plain assignment: the last one seen took the key, and a target then drew itself
                // against a set the player had just made while the number beside it named the original.
                // A target that points nowhere is drawn nowhere, which is the honest half.
                if (!index._byUid.TryAdd(uid!, set))
                {
                    index._byUid.Remove(uid!);
                    doubled.Add(uid!);
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

    /// <summary>
    /// How one worn piece's materia is judged against its target piece: the plain way for a combat target
    /// (the same materia, order irrelevant), the web's <c>meldMatch</c> for a crafter or gatherer target.
    /// </summary>
    /// <remarks>
    /// One judge per target, so both the ring passes and the other slots ask the same question. The crafter
    /// way is chosen only when the row says it is a crafter target and the answer carried the tables; a
    /// crafter row without tables, which a server should never send, falls back to the plain way rather
    /// than inventing caps.
    /// </remarks>
    private readonly struct MateriaJudge
    {
        private readonly CraftTables? _tables;

        private MateriaJudge(CraftTables? tables) => _tables = tables;

        public static MateriaJudge For(BisGearset target, CraftTables? tables) =>
            new(target.IsCraft ? tables : null);

        public (bool Done, List<int> Missing, List<int> Extra, IReadOnlyList<MeldSlot>? Melds) Judge(ItemDto target, ItemDto current)
        {
            if (_tables is null)
            {
                var (missing, extra) = MateriaDiff(current.Materia, target.Materia);
                return (missing.Count == 0 && extra.Count == 0, missing, extra, null);
            }

            var row = _tables.Items.GetValueOrDefault(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var melds = CraftStats.MeldMatch(row, target.Hq == true, target.Materia, current.Materia, _tables.Materia);
            var open = new List<int>();
            var wrong = new List<int>();
            for (var i = 0; i < melds.Count; i++)
            {
                if (melds[i].Filled)
                {
                    continue;
                }

                open.Add(target.Materia[i]);
                if (melds[i].Worn != 0)
                {
                    wrong.Add(melds[i].Worn);
                }
            }

            return (open.Count == 0, open, wrong, melds);
        }
    }

    private static List<SlotComparison> CompareSlots(
        BisGearset target,
        Dictionary<string, ItemDto>? liveItems,
        CraftTables? tables)
    {
        var judge = MateriaJudge.For(target, tables);
        var slots = new List<SlotComparison>(target.Items.Count);

        // Non-ring slots: direct key comparison.
        foreach (var (slot, piece) in target.Items)
        {
            if (slot is RingLeft or RingRight)
            {
                continue;
            }

            slots.Add(CompareOne(slot, piece, Lookup(liveItems, slot), judge));
        }

        // Rings: interchangeable left/right — match target rings to the current ring pool by id.
        AddRingComparisons(target.Items, liveItems, slots, judge);
        return slots;
    }

    private static void AddRingComparisons(
        Dictionary<string, ItemDto> targetItems,
        Dictionary<string, ItemDto>? liveItems,
        List<SlotComparison> slots,
        MateriaJudge judge)
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

        // Pass 1: claim the rings whose materia are already done first, so two same-id rings with
        // different materia each pair with the right one regardless of which finger they sit on.
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Item;
            var idx = pool.FindIndex(p => p.Id == target.Id && judge.Judge(target, p).Done);
            if (idx >= 0)
            {
                slots.Add(Worn(targets[i].Slot, target, pool[idx], judge));
                pool.RemoveAt(idx);
                resolved[i] = true;
            }
        }

        // Pass 2: same item id but materia not done yet → match, materia differs.
        for (var i = 0; i < targets.Count; i++)
        {
            if (resolved[i])
            {
                continue;
            }

            var idx = pool.FindIndex(p => p.Id == targets[i].Item.Id);
            if (idx >= 0)
            {
                slots.Add(Worn(targets[i].Slot, targets[i].Item, pool[idx], judge));
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

    private static SlotComparison CompareOne(string slot, ItemDto target, ItemDto? current, MateriaJudge judge)
    {
        if (current is null)
        {
            return new SlotComparison(slot, null, target.Id, SlotMatch.MissingCurrent, false, target.Materia.ToList(), []);
        }

        if (current.Id != target.Id)
        {
            return new SlotComparison(slot, current.Id, target.Id, SlotMatch.ItemDiffers, false, target.Materia.ToList(), []);
        }

        return Worn(slot, target, current, judge);
    }

    /// <summary>The comparison of a slot where the target's item is worn.</summary>
    private static SlotComparison Worn(string slot, ItemDto target, ItemDto current, MateriaJudge judge)
    {
        var (done, missing, extra, melds) = judge.Judge(target, current);

        // NQ where the target wants HQ is not reached. A piece whose HQ state is not known is not held
        // against the player: only a worn piece that says false counts.
        var hqMissing = target.Hq == true && current.Hq == false;
        return new SlotComparison(slot, current.Id, target.Id, SlotMatch.Match, done, missing, extra, hqMissing, melds);
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
}
