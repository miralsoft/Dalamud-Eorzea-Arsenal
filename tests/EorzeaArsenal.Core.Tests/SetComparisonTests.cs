using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Two sets, slot by slot. The point of this being three states and not two is the middle one: the server
/// compares the piece and not what is melded into it, so a pair that differs only in its materia comes
/// back as a full match and is still not the same set.
/// </summary>
public sealed class SetComparisonTests
{
    private static Dictionary<string, ItemDto> Set(params (string Slot, int Id, int[] Materia)[] pieces)
    {
        var items = new Dictionary<string, ItemDto>(StringComparer.Ordinal);
        foreach (var (slot, id, materia) in pieces)
        {
            items[slot] = new ItemDto { Id = id, Materia = [.. materia] };
        }

        return items;
    }

    [Fact]
    public void TheSameItemWithTheSameMateriaAgrees()
    {
        var pairs = SetComparison.Compare(
            Set(("Weapon", 44123, [41777, 41778])),
            Set(("Weapon", 44123, [41777, 41778])));

        Assert.Equal(SlotAgreement.Same, Assert.Single(pairs).Agreement);
    }

    /// <summary>
    /// Meld order is not a difference in game, so it is not one here either. Getting this wrong would
    /// paint a set amber against its own copy.
    /// </summary>
    [Fact]
    public void MateriaOrderIsNotADifference()
    {
        var pairs = SetComparison.Compare(
            Set(("Head", 44100, [41777, 41778, 41779])),
            Set(("Head", 44100, [41779, 41777, 41778])));

        Assert.Equal(SlotAgreement.Same, Assert.Single(pairs).Agreement);
    }

    /// <summary>
    /// The case the server's numbers cannot express. It counts this slot as matched, which is correct for
    /// what it measures and wrong for what a player is deciding.
    /// </summary>
    [Fact]
    public void TheSameItemWithOtherMateriaIsItsOwnState()
    {
        var pairs = SetComparison.Compare(
            Set(("Body", 44101, [41777, 41777])),
            Set(("Body", 44101, [41777, 41778])));

        Assert.Equal(SlotAgreement.MateriaDiffers, Assert.Single(pairs).Agreement);
    }

    /// <summary>Two of one materia is not one of it, even though the same ids appear on both sides.</summary>
    [Fact]
    public void HowOftenAMateriaIsMeldedCounts()
    {
        var pairs = SetComparison.Compare(
            Set(("Legs", 44104, [41777, 41777])),
            Set(("Legs", 44104, [41777])));

        Assert.Equal(SlotAgreement.MateriaDiffers, Assert.Single(pairs).Agreement);
    }

    [Fact]
    public void ADifferentItemIsADifference()
    {
        var pairs = SetComparison.Compare(
            Set(("Hands", 44102, [])),
            Set(("Hands", 51010, [])));

        Assert.Equal(SlotAgreement.Different, Assert.Single(pairs).Agreement);
    }

    /// <summary>
    /// A slot only one side fills is a difference and keeps its column, so the two strips stay in step.
    /// The side that has nothing is what tells the reader which way round it is.
    /// </summary>
    [Fact]
    public void ASlotOnlyOneSideFillsKeepsItsColumn()
    {
        var pairs = SetComparison.Compare(
            Set(("Weapon", 44123, []), ("OffHand", 44124, [])),
            Set(("Weapon", 44123, [])));

        Assert.Equal(2, pairs.Count);
        var offHand = Assert.Single(pairs, p => p.Slot == "OffHand");
        Assert.Equal(SlotAgreement.Different, offHand.Agreement);
        Assert.NotNull(offHand.Mine);
        Assert.Null(offHand.Theirs);
    }

    /// <summary>
    /// A slot neither side fills is left out. Counting it as agreement would make every pair look better
    /// the less gear it had.
    /// </summary>
    [Fact]
    public void ASlotNeitherSideFillsIsNotAComparison()
    {
        var pairs = SetComparison.Compare(Set(("Weapon", 44123, [])), Set(("Weapon", 44123, [])));

        Assert.Equal(["Weapon"], pairs.Select(p => p.Slot));
    }

    /// <summary>A placeholder id is an empty slot, not a piece nobody can name.</summary>
    [Fact]
    public void AZeroIdIsAnEmptySlot()
    {
        var pairs = SetComparison.Compare(
            Set(("Neck", 0, [])),
            Set(("Neck", 0, [])));

        Assert.Empty(pairs);
    }

    /// <summary>
    /// The one sentence this window must not get wrong. Both sets read as a full match on the server's
    /// count, and calling that a copy would tell somebody to delete a set whose melds they still want.
    /// </summary>
    [Fact]
    public void APairThatDiffersOnlyInMateriaIsNotACopy()
    {
        var pairs = SetComparison.Compare(
            Set(("Weapon", 44123, [41777]), ("Head", 44100, [41777])),
            Set(("Weapon", 44123, [41777]), ("Head", 44100, [41778])));

        Assert.Equal((1, 1, 0), SetComparison.Counts(pairs));
        Assert.False(SetComparison.IsCopy(pairs));
    }

    [Fact]
    public void APairThatAgreesEverywhereIsACopy()
    {
        var pairs = SetComparison.Compare(
            Set(("Weapon", 44123, [41777]), ("Head", 44100, [41777])),
            Set(("Weapon", 44123, [41777]), ("Head", 44100, [41777])));

        Assert.True(SetComparison.IsCopy(pairs));
    }

    /// <summary>Nothing to compare is not a copy, however true it is that nothing differs.</summary>
    [Fact]
    public void NothingToCompareIsNotACopy()
    {
        Assert.False(SetComparison.IsCopy(SetComparison.Compare(null, null)));
    }

    /// <summary>
    /// A slot key this build has never heard of is shown at the end rather than dropped. The server may
    /// add one, and a comparison that silently omits it would say two sets agree about something it never
    /// looked at.
    /// </summary>
    [Fact]
    public void AnUnknownSlotIsAppendedRatherThanDropped()
    {
        var pairs = SetComparison.Compare(
            Set(("Weapon", 44123, []), ("Wings", 90001, [])),
            Set(("Weapon", 44123, [])));

        Assert.Equal(["Weapon", "Wings"], pairs.Select(p => p.Slot));
    }
}
