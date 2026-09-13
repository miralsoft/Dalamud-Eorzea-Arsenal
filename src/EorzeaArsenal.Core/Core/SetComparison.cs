using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// How one slot of two sets relates. Three states rather than two, because the server compares the piece
/// and not what is melded into it.
/// </summary>
public enum SlotAgreement
{
    /// <summary>The same item, with the same materia in it.</summary>
    Same,

    /// <summary>
    /// The same item, other materia. The server counts this slot as matched and a player looking at the
    /// two sets does not, which is why the third state exists at all: a pair that differs only in melds
    /// reads 100 % and is still not the same set.
    /// </summary>
    MateriaDiffers,

    /// <summary>A different item, or nothing at all on one of the two sides.</summary>
    Different,
}

/// <summary>One slot, seen from both sets at once.</summary>
public sealed class SlotPair
{
    /// <summary>The API slot key, for example <c>Weapon</c>.</summary>
    public required string Slot { get; init; }

    /// <summary>What the set being decided about carries there, or <see langword="null"/> for nothing.</summary>
    public ItemDto? Mine { get; init; }

    /// <summary>What the set it is held against carries there, or <see langword="null"/> for nothing.</summary>
    public ItemDto? Theirs { get; init; }

    /// <summary>How the two relate.</summary>
    public required SlotAgreement Agreement { get; init; }
}

/// <summary>
/// Two sets, slot by slot. <b>This does not score anything.</b> How much a pair shares is
/// <c>probability</c>, <c>matched_slots</c> and <c>total_slots</c> from the server, and those are shown
/// as delivered: a second implementation of that sum would be a second answer to the same question and
/// the two can differ by a point. What is computed here is only <i>where</i> the two differ, which the
/// numbers do not say and which is the whole reason for putting the gear on screen.
/// </summary>
public static class SetComparison
{
    /// <summary>
    /// Both sets slot by slot, in the order a character sheet reads, for every slot either side fills.
    /// </summary>
    /// <param name="mine">The gear of the set being decided about.</param>
    /// <param name="theirs">The gear of the set it is held against.</param>
    /// <returns>One entry per occupied slot. Empty when neither side carries anything.</returns>
    /// <remarks>
    /// A slot neither side fills is left out rather than listed as agreeing. Two sets that both stop at
    /// the waist agree about the waist in a sense nobody needs a row for, and a comparison padded with
    /// empty agreement is a comparison that looks better than it is.
    /// </remarks>
    public static IReadOnlyList<SlotPair> Compare(
        IReadOnlyDictionary<string, ItemDto>? mine,
        IReadOnlyDictionary<string, ItemDto>? theirs)
    {
        var left = mine ?? new Dictionary<string, ItemDto>(StringComparer.Ordinal);
        var right = theirs ?? new Dictionary<string, ItemDto>(StringComparer.Ordinal);

        var pairs = new List<SlotPair>();
        foreach (var slot in SlotsIn(left, right))
        {
            var a = Occupied(left, slot);
            var b = Occupied(right, slot);
            if (a is null && b is null)
            {
                continue;
            }

            pairs.Add(new SlotPair { Slot = slot, Mine = a, Theirs = b, Agreement = AgreementOf(a, b) });
        }

        return pairs;
    }

    /// <summary>How many slots of a comparison fall into each state.</summary>
    /// <param name="pairs">The comparison.</param>
    /// <returns>The counts, in the order same, materia differs, different.</returns>
    public static (int Same, int MateriaDiffers, int Different) Counts(IReadOnlyList<SlotPair> pairs)
    {
        var same = 0;
        var materia = 0;
        var different = 0;
        foreach (var pair in pairs)
        {
            switch (pair.Agreement)
            {
                case SlotAgreement.Same:
                    same++;
                    break;
                case SlotAgreement.MateriaDiffers:
                    materia++;
                    break;
                default:
                    different++;
                    break;
            }
        }

        return (same, materia, different);
    }

    /// <summary>
    /// Whether every occupied slot agrees down to the materia, which is the one case where "this row is a
    /// copy of that one" is a statement rather than a guess.
    /// </summary>
    /// <param name="pairs">The comparison.</param>
    /// <returns><see langword="true"/> when nothing differs and there was something to compare.</returns>
    public static bool IsCopy(IReadOnlyList<SlotPair> pairs) =>
        pairs.Count > 0 && pairs.All(p => p.Agreement == SlotAgreement.Same);

    /// <summary>How two pieces in one slot relate.</summary>
    /// <param name="mine">One side, or <see langword="null"/> for an empty slot.</param>
    /// <param name="theirs">The other side, or <see langword="null"/> for an empty slot.</param>
    /// <returns>The state of that slot.</returns>
    public static SlotAgreement AgreementOf(ItemDto? mine, ItemDto? theirs)
    {
        if (mine is null || theirs is null || mine.Id != theirs.Id)
        {
            return SlotAgreement.Different;
        }

        return SameMateria(mine.Materia, theirs.Materia) ? SlotAgreement.Same : SlotAgreement.MateriaDiffers;
    }

    /// <summary>
    /// Whether two pieces carry the same melds. Order does not count: in game the sequence is not a
    /// difference, so two sets that meld the same three materia in another sequence are the same set.
    /// Multiplicity does count, because two of one materia is not the same as one.
    /// </summary>
    /// <param name="mine">One side's materia item ids.</param>
    /// <param name="theirs">The other side's materia item ids.</param>
    /// <returns><see langword="true"/> when the two are the same multiset.</returns>
    public static bool SameMateria(IReadOnlyList<int>? mine, IReadOnlyList<int>? theirs)
    {
        var a = mine ?? [];
        var b = theirs ?? [];
        if (a.Count != b.Count)
        {
            return false;
        }

        if (a.Count == 0)
        {
            return true;
        }

        var left = a.ToArray();
        var right = b.ToArray();
        Array.Sort(left);
        Array.Sort(right);
        return left.AsSpan().SequenceEqual(right);
    }

    /// <summary>
    /// The slots to walk, in the order the game shows them, with anything the server sends that this
    /// build does not know appended in a stable order rather than dropped.
    /// </summary>
    private static IEnumerable<string> SlotsIn(
        IReadOnlyDictionary<string, ItemDto> left,
        IReadOnlyDictionary<string, ItemDto> right)
    {
        foreach (var slot in EquipmentSlots.DisplayOrder)
        {
            yield return slot;
        }

        var extra = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var slot in left.Keys.Concat(right.Keys))
        {
            if (!EquipmentSlots.ValidKeys.Contains(slot))
            {
                extra.Add(slot);
            }
        }

        foreach (var slot in extra)
        {
            yield return slot;
        }
    }

    /// <summary>The piece in a slot, or null when the slot is absent or holds a placeholder.</summary>
    private static ItemDto? Occupied(IReadOnlyDictionary<string, ItemDto> items, string slot) =>
        items.TryGetValue(slot, out var item) && item.Id > 0 ? item : null;
}
