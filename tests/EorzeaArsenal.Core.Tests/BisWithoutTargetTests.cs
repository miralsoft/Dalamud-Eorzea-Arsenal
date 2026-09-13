using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Which live gearsets no target claims. This exists because <see cref="BisComparer.Compare"/> walks the
/// targets: a live set with no target produces no entry and would simply be absent from the window, and a
/// set that vanishes gets reported as a bug rather than understood.
/// </summary>
public sealed class BisWithoutTargetTests
{
    [Fact]
    public void ASetWithNoTargetIsReported()
    {
        var live = Live(("DRK", 0), ("CRP", 1));

        var without = BisComparer.WithoutTarget(live, [Target("DRK", 0)]);

        var only = Assert.Single(without);
        Assert.Equal("CRP", only.Job);
    }

    [Fact]
    public void NothingIsReportedWhenEveryLiveSetHasATarget()
    {
        var live = Live(("DRK", 0), ("WHM", 1));

        var without = BisComparer.WithoutTarget(live, [Target("DRK", 0), Target("WHM", 1)]);

        Assert.Empty(without);
    }

    [Fact]
    public void ThePlayersOrderIsKept()
    {
        var live = Live(("CRP", 0), ("DRK", 1), ("MIN", 2), ("WHM", 3), ("BSM", 4));

        var without = BisComparer.WithoutTarget(live, [Target("DRK", 1), Target("WHM", 3)]);

        Assert.Equal(["CRP", "MIN", "BSM"], without.Select(s => s.Job));
    }

    /// <summary>
    /// The identity decides, not the position, and this is the case that made the whole rewrite
    /// necessary: the target names a uid, the live set sits somewhere else entirely, and it is still
    /// claimed. Keyed on the position it would be reported as having no target while its target sat right
    /// there.
    /// </summary>
    [Fact]
    public void AReorderedSetIsClaimedByItsIdentityRatherThanItsPosition()
    {
        var live = Live(("DRK", 7), ("CRP", 0));
        var target = new BisGearset { Job = "DRK", GearIndex = 0, Name = "BiS", SetUid = "abc", Items = [] };

        var without = BisComparer.WithoutTarget(live, [target], set => set.Job == "DRK" ? "abc" : null);

        var only = Assert.Single(without);
        Assert.Equal("CRP", only.Job);
    }

    /// <summary>
    /// A target carrying an identity never falls back to the position. So a target whose uid matches
    /// nothing leaves the live set at that position unclaimed, which is honest: nothing about that set is
    /// known, and attaching it to a guess is the bug this replaces.
    /// </summary>
    [Fact]
    public void AnIdentityThatMatchesNothingClaimsNothing()
    {
        var live = Live(("DRK", 0));
        var target = new BisGearset { Job = "DRK", GearIndex = 0, Name = "BiS", SetUid = "gone", Items = [] };

        var without = BisComparer.WithoutTarget(live, [target], _ => "other");

        Assert.Single(without);
    }

    /// <summary>
    /// Two live sets of the same job, one target: the other one has no target and is reported, rather
    /// than both being treated as covered because the job matched.
    /// </summary>
    [Fact]
    public void OnlyTheClaimedSetOfAJobCounts()
    {
        var live = Live(("DRK", 0), ("DRK", 3));

        var without = BisComparer.WithoutTarget(live, [Target("DRK", 0)]);

        var only = Assert.Single(without);
        Assert.Equal(3, only.GearIndex);
    }

    /// <summary>
    /// Consistency with the comparison itself: a set the comparer attached a target to is never also
    /// reported as having none. The two answers come from one decision, which is why it lives in one
    /// place.
    /// </summary>
    [Fact]
    public void AClaimedSetIsNeverBothComparedAndUnclaimed()
    {
        var live = Live(("DRK", 0), ("CRP", 1), ("WHM", 2));
        BisGearset[] targets = [Target("DRK", 0), Target("WHM", 2)];

        var compared = BisComparer.Compare(live, targets);
        var without = BisComparer.WithoutTarget(live, targets);

        Assert.All(compared, c => Assert.True(c.HasLiveGearset));
        Assert.Equal(2, compared.Count);
        Assert.Single(without);
        Assert.DoesNotContain("DRK", without.Select(s => s.Job));
    }

    /// <summary>
    /// Two live gearsets under one identity, which is what a freshly copied set looks like until the next
    /// push tells the server it exists. The key used to go to whichever came last, so the target drew
    /// itself against the copy while every number beside it named the original. Neither can claim it now,
    /// and both are reported as having no target, which is what they have.
    /// </summary>
    [Fact]
    public void AnIdentityTwoLiveSetsClaimIsClaimedByNeither()
    {
        var live = Live(("DRK", 0), ("DRK", 5));
        var target = new BisGearset { Job = "DRK", GearIndex = 0, Name = "BiS", SetUid = "abc", Items = [] };

        var without = BisComparer.WithoutTarget(live, [target], _ => "abc");

        Assert.Equal([0, 5], without.Select(s => s.GearIndex).OrderBy(i => i));
    }

    /// <summary>
    /// And a third claimant does not put the key back, which a plain removal on collision would allow.
    /// </summary>
    [Fact]
    public void AThirdClaimantDoesNotRestoreTheIdentity()
    {
        var live = Live(("DRK", 0), ("DRK", 5), ("DRK", 9));
        var target = new BisGearset { Job = "DRK", GearIndex = 0, Name = "BiS", SetUid = "abc", Items = [] };

        var without = BisComparer.WithoutTarget(live, [target], _ => "abc");

        Assert.Equal(3, without.Count);
    }

    private static GearData Live(params (string Job, int Index)[] sets) => new()
    {
        Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
        Gearsets = [.. sets.Select(s => new GearsetDto
        {
            GearIndex = s.Index,
            Job = s.Job,
            Name = s.Job + " set",
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
        })],
    };

    private static BisGearset Target(string job, int index) => new()
    {
        Job = job,
        GearIndex = index,
        Name = job + " BiS",
        Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
    };
}
