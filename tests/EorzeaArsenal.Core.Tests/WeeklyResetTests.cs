using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Verifies the weekly-reset boundary maths and the timestamp-based decode rules for <c>unreal</c>
/// and <c>wondrous</c>, using the exact captures validated in-game on 2026-07-05 (a Sunday; the
/// week's reset was Tuesday 2026-06-30 08:00 UTC = unix 1782806400).
/// </summary>
public sealed class WeeklyResetTests
{
    private const long Reset0630 = 1782806400; // Tue 2026-06-30 08:00 UTC (this week's reset)
    private const long Reset0707 = 1783411200; // Tue 2026-07-07 08:00 UTC (next reset)
    private const long Reset0714 = 1784016000; // Tue 2026-07-14 08:00 UTC (a this-week book's expiry)

    private static DateTimeOffset Utc(int y, int mo, int d, int h = 12) => new(y, mo, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MostRecent_returns_the_tuesday_before_a_midweek_instant()
    {
        Assert.Equal(Reset0630, WeeklyReset.MostRecent(Utc(2026, 7, 5, 15)).ToUnixTimeSeconds());
    }

    [Fact]
    public void MostRecent_on_reset_day_flips_at_0800_utc()
    {
        // 07:59 still belongs to the previous week; 08:00 is the new reset.
        Assert.Equal(Reset0630, WeeklyReset.MostRecent(Utc(2026, 7, 7, 7)).ToUnixTimeSeconds());
        Assert.Equal(Reset0707, WeeklyReset.MostRecent(Utc(2026, 7, 7, 8)).ToUnixTimeSeconds());
    }

    [Fact]
    public void Next_is_one_week_after_the_most_recent_reset()
    {
        Assert.Equal(Reset0707, WeeklyReset.Next(Utc(2026, 7, 5)).ToUnixTimeSeconds());
    }

    [Fact]
    public void Unreal_done_when_faux_timestamp_is_this_week()
    {
        // Live capture: fauxTs 1783253903 (mid-week) ≥ the 06-30 reset → done.
        Assert.True(WeeklyDecode.IsUnrealDone(1783253903, Utc(2026, 7, 5)));
    }

    [Fact]
    public void Unreal_not_done_when_faux_timestamp_is_before_the_reset_or_absent()
    {
        Assert.False(WeeklyDecode.IsUnrealDone(Reset0630 - 1, Utc(2026, 7, 5)));
        Assert.False(WeeklyDecode.IsUnrealDone(0, Utc(2026, 7, 5)));
    }

    [Fact]
    public void Wondrous_done_when_complete_and_book_bought_this_week()
    {
        // Live capture: 9/9 stickers, expiry 07-14 (this-week book) > next reset 07-07 → done.
        Assert.True(WeeklyDecode.IsWondrousDone(9, Reset0714, Utc(2026, 7, 5)));
    }

    [Fact]
    public void Wondrous_not_done_when_book_incomplete()
    {
        Assert.False(WeeklyDecode.IsWondrousDone(8, Reset0714, Utc(2026, 7, 5)));
    }

    [Fact]
    public void Wondrous_not_done_for_a_carried_over_book_from_last_week()
    {
        // A last-week book expires exactly at the next reset (07-07); "> next reset" excludes it even
        // when it is complete — it does not count for this week.
        Assert.False(WeeklyDecode.IsWondrousDone(9, Reset0707, Utc(2026, 7, 5)));
    }

    [Fact]
    public void Wondrous_stale_completion_does_not_leak_into_next_week()
    {
        // Regression for the whole design: the same completed book (expiry 07-14), read the FOLLOWING
        // week (reset now 07-14), must not report done — its expiry is no longer beyond the next reset.
        Assert.False(WeeklyDecode.IsWondrousDone(9, Reset0714, Utc(2026, 7, 9)));
    }
}
