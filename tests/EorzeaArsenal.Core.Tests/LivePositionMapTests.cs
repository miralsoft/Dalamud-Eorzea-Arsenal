using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The table that says which identity sits at which live position. Everything the interface prints as a
/// set number, and every BiS target it draws against a gearset, comes through here.
/// </summary>
public sealed class LivePositionMapTests
{
    private const string UidA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string UidB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static GearsetDto Set(int index) => new() { GearIndex = index, Job = "DRK", Name = "Dunkelritter" };

    private static GearsetIdentityMatch Found(string uid) => new(uid, "exact", false);

    [Fact]
    public void EachSetGetsItsIdentityAndBothTablesAgree()
    {
        var byIndex = new Dictionary<int, string> { [0] = UidA, [4] = UidB };

        var tables = LivePositionMap.Build([Set(0), Set(4)], s => Found(byIndex[s.GearIndex]));

        Assert.Equal(UidA, tables.UidByIndex[0]);
        Assert.Equal(UidB, tables.UidByIndex[4]);
        Assert.Equal(0, tables.IndexByUid[UidA]);
        Assert.Equal(4, tables.IndexByUid[UidB]);
        Assert.Empty(tables.Undecided);
    }

    /// <summary>
    /// The mapping's refusal is carried through rather than swallowed, because an empty comparison with no
    /// stated reason reads as a fault.
    /// </summary>
    [Fact]
    public void ARefusalIsRecordedAsUndecided()
    {
        var tables = LivePositionMap.Build(
            [Set(0), Set(4)],
            s => s.GearIndex == 4 ? GearsetIdentityMatch.Ambiguous : Found(UidA));

        Assert.Equal([4], tables.Undecided);
        Assert.False(tables.UidByIndex.ContainsKey(4));
        Assert.Equal(UidA, tables.UidByIndex[0]);
    }

    /// <summary>
    /// A miss with nothing found is not a doubt. The set simply has no identity yet, which is the ordinary
    /// state of a gearset made since the last push, and saying "cannot tell these apart" about it would be
    /// a different and untrue statement.
    /// </summary>
    [Fact]
    public void APlainMissIsNotCalledUndecided()
    {
        var tables = LivePositionMap.Build([Set(0)], _ => GearsetIdentityMatch.None);

        Assert.Empty(tables.Undecided);
        Assert.Empty(tables.UidByIndex);
        Assert.Empty(tables.IndexByUid);
    }

    /// <summary>
    /// The case this class was pulled out for. Copy a gearset in game and the copy is indistinguishable
    /// from the original until the next push tells the server it exists, so the mapping answers both with
    /// the same identity and reports no doubt either time: from where it stands, those are two separate
    /// questions with one confident answer each.
    /// </summary>
    /// <remarks>
    /// The table used to write the second over the first. It stayed full, pointed at whichever set came
    /// last, and a wrong set number with somebody else's BiS target beside it read exactly like a right
    /// one. One identity cannot sit in two places, so neither claim survives and both positions say so.
    /// </remarks>
    [Fact]
    public void AnIdentityClaimedTwiceIsWithdrawnFromBoth()
    {
        var tables = LivePositionMap.Build([Set(0), Set(4)], _ => Found(UidA));

        Assert.False(tables.IndexByUid.ContainsKey(UidA));
        Assert.False(tables.UidByIndex.ContainsKey(0));
        Assert.False(tables.UidByIndex.ContainsKey(4));
        Assert.Equal([0, 4], tables.Undecided.OrderBy(i => i));
    }

    /// <summary>
    /// And a third set with the same identity does not put it back. Withdrawing on the second claim and
    /// re-adding on the third would leave the table full again, pointing at the last one, which is the
    /// fault this guard removes.
    /// </summary>
    [Fact]
    public void AThirdClaimDoesNotRestoreTheIdentity()
    {
        var tables = LivePositionMap.Build([Set(0), Set(4), Set(9)], _ => Found(UidA));

        Assert.Empty(tables.IndexByUid);
        Assert.Empty(tables.UidByIndex);
        Assert.Equal([0, 4, 9], tables.Undecided.OrderBy(i => i));
    }

    /// <summary>
    /// Withdrawing one identity leaves every other one standing. A single copied set must not empty the
    /// window.
    /// </summary>
    [Fact]
    public void TheRestOfTheTableSurvivesOneDoubledIdentity()
    {
        var tables = LivePositionMap.Build(
            [Set(0), Set(4), Set(9)],
            s => Found(s.GearIndex == 9 ? UidB : UidA));

        Assert.Equal(9, tables.IndexByUid[UidB]);
        Assert.Equal(UidB, tables.UidByIndex[9]);
        Assert.Single(tables.IndexByUid);
        Assert.Equal([0, 4], tables.Undecided.OrderBy(i => i));
    }
}
