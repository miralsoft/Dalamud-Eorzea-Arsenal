using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Which own absence counts the player out of an event, as the server's <c>AbsenceSpan::awayFor</c> does.</summary>
public sealed class AbsenceCoverageTests
{
    private static Absence Day(string from, string to, long? team = null, string? level = "x") =>
        new() { Id = 1, FromDate = from, ToDate = to, TeamId = team, Level = level };

    private static Absence Hours(string date, string fromTime, string toTime, long? team = null) =>
        new() { Id = 2, FromDate = date, ToDate = date, FromTime = fromTime, ToTime = toTime, TeamId = team, Level = "x" };

    [Fact]
    public void A_whole_day_not_there_covers_every_event_of_the_day_in_every_team()
    {
        Absence[] mine = [Day("2026-11-22", "2026-11-22")];

        Assert.NotNull(AbsenceCoverage.AwayAt(mine, 3, "2026-11-22", "20:00", "23:00"));
        Assert.NotNull(AbsenceCoverage.AwayAt(mine, 4, "2026-11-22", null, null));
        Assert.Null(AbsenceCoverage.AwayAt(mine, 3, "2026-11-23", "20:00", "23:00"));
    }

    [Fact]
    public void Only_not_there_counts_and_only_for_its_own_team()
    {
        Assert.Null(AbsenceCoverage.AwayAt([Day("2026-11-22", "2026-11-22", level: "2")], 3, "2026-11-22", "20:00", null));
        Assert.Null(AbsenceCoverage.AwayAt([Day("2026-11-22", "2026-11-22", team: 4)], 3, "2026-11-22", "20:00", null));
        Assert.NotNull(AbsenceCoverage.AwayAt([Day("2026-11-22", "2026-11-22", team: 0)], 3, "2026-11-22", "20:00", null));
        Assert.NotNull(AbsenceCoverage.AwayAt([Day("2026-11-22", "2026-11-22", level: null)], 3, "2026-11-22", "20:00", null));
    }

    [Theory]
    [InlineData("19:00", "23:00", "20:00", "23:00", true)]
    [InlineData("18:00", "20:00", "20:00", "23:00", false)]
    [InlineData("22:30", "24:00", "20:00", "23:00", true)]
    [InlineData("23:00", "01:00", "00:30", "02:00", false)] // the event is on the next day's date, read below
    [InlineData("19:00", "21:00", "20:00", null, true)]
    public void Hours_count_when_they_meet_the_event(string fromTime, string toTime, string time, string? end, bool away) =>
        Assert.Equal(away, AbsenceCoverage.AwayAt([Hours("2026-11-22", fromTime, toTime)], 3, "2026-11-22", time, end) is not null);

    [Fact]
    public void Hours_past_midnight_reach_into_the_next_date()
    {
        Absence[] mine = [Hours("2026-11-22", "23:00", "02:00")];

        Assert.NotNull(AbsenceCoverage.AwayAt(mine, 3, "2026-11-23", "00:30", "02:00"));
        Assert.Null(AbsenceCoverage.AwayAt(mine, 3, "2026-11-23", "20:00", "23:00"));
        Assert.Null(AbsenceCoverage.AwayAt(mine, 3, "2026-11-22", null, null));
    }
}
