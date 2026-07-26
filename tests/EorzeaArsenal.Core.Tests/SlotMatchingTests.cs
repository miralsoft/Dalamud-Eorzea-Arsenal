using EorzeaArsenal.Gear;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The one slot rule that keeps being re-derived and keeps being got wrong: rings are
/// interchangeable. A player wearing both BiS rings on the "wrong" fingers is fully geared, not two
/// pieces short — the game itself makes no distinction.
/// </summary>
public sealed class SlotMatchingTests
{
    private const long RingA = 49730;
    private const long RingB = 49653;

    private static Func<string, long> Wearing(long left, long right) => slot => slot switch
    {
        SlotMatching.RingLeft => left,
        SlotMatching.RingRight => right,
        "Body" => 49604,
        _ => 0,
    };

    [Fact]
    public void ASwappedRingPairStillCountsAsWorn()
    {
        // Target: A on the left, B on the right. Worn: exactly the other way round.
        var swapped = Wearing(left: RingB, right: RingA);

        Assert.True(SlotMatching.IsWorn(SlotMatching.RingLeft, RingA, swapped));
        Assert.True(SlotMatching.IsWorn(SlotMatching.RingRight, RingB, swapped));
    }

    [Fact]
    public void ARingThatIsNotWornAtAllIsStillMissing()
    {
        var wearing = Wearing(left: RingA, right: 0);

        Assert.True(SlotMatching.IsWorn(SlotMatching.RingRight, RingA, wearing)); // on the other finger
        Assert.False(SlotMatching.IsWorn(SlotMatching.RingLeft, RingB, wearing)); // nowhere
    }

    [Fact]
    public void EverySlotButTheRingsComparesDirectly()
    {
        var wearing = Wearing(left: RingA, right: RingB);

        Assert.True(SlotMatching.IsWorn("Body", 49604, wearing));
        Assert.False(SlotMatching.IsWorn("Body", 49629, wearing));

        // A body piece is not satisfied by wearing it somewhere else.
        Assert.False(SlotMatching.IsWorn("Head", 49604, wearing));
    }

    [Fact]
    public void AnEmptyTargetIsNeverWorn() =>
        Assert.False(SlotMatching.IsWorn(SlotMatching.RingLeft, 0, Wearing(RingA, RingB)));

    [Theory]
    [InlineData("RingLeft", true)]
    [InlineData("RingRight", true)]
    [InlineData("Body", false)]
    [InlineData(null, false)]
    public void RingSlotsAreRecognised(string? slot, bool expected) =>
        Assert.Equal(expected, SlotMatching.IsRingSlot(slot));
}
