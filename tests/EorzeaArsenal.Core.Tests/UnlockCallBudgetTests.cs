using EorzeaArsenal.Core;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The one hourly limit of <c>PUT /me/unlocks</c> the goals mode and the collection share.</summary>
public sealed class UnlockCallBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 28, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Thirty_calls_an_hour_and_then_a_hold_until_the_oldest_is_an_hour_old()
    {
        var budget = new UnlockCallBudget();
        for (var i = 0; i < 30; i++)
        {
            Assert.True(budget.TryTake(Start.AddMinutes(i)));
        }

        Assert.False(budget.TryTake(Start.AddMinutes(40)));
        Assert.Equal(Start.AddHours(1), budget.HeldUntil);
        Assert.True(budget.IsHeld(Start.AddMinutes(59)));
        Assert.True(budget.TryTake(Start.AddHours(1)));
    }

    [Fact]
    public void A_retry_after_holds_and_a_shorter_one_does_not_cut_it()
    {
        var budget = new UnlockCallBudget();
        budget.HoldUntil(Start.AddHours(1));
        budget.HoldUntil(Start.AddMinutes(1));

        Assert.False(budget.TryTake(Start.AddMinutes(30)));
        Assert.True(budget.TryTake(Start.AddHours(1)));
    }
}
