using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The absence checks of server 1.5, made in the plugin before anything is sent.</summary>
public sealed class AbsenceRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    [Theory]
    [InlineData("2026-11-21", "2026-11-21", null)]
    [InlineData("2026-11-20", "2026-11-21", AbsenceInputError.OutOfWindow)]
    [InlineData("2026-11-22", "2027-12-27", null)]
    [InlineData("2026-11-22", "2027-12-28", AbsenceInputError.OutOfWindow)]
    [InlineData("2026-11-23", "2026-11-22", AbsenceInputError.RangeInvalid)]
    [InlineData("2026-11-31", "2026-12-01", AbsenceInputError.InvalidDate)]
    public void Dates_run_from_yesterday_to_400_days_ahead(string from, string to, AbsenceInputError? expected) =>
        Assert.Equal(expected, AbsenceRules.Validate(from, to, null, null, AbsenceRules.LevelAway, Today));

    [Theory]
    [InlineData("19:00", "23:00", null)]
    [InlineData("22:00", "02:00", null)]
    [InlineData("18:00", "24:00", null)]
    [InlineData("19:00", null, AbsenceInputError.InvalidTimes)]
    [InlineData(null, "23:00", AbsenceInputError.InvalidTimes)]
    [InlineData("20:00", "20:00", AbsenceInputError.InvalidTimes)]
    [InlineData("24:00", "02:00", AbsenceInputError.InvalidTimes)]
    [InlineData("7pm", "23:00", AbsenceInputError.InvalidTimes)]
    public void Times_are_both_or_neither_and_only_an_end_may_be_24_00(string? from, string? to, AbsenceInputError? expected) =>
        Assert.Equal(expected, AbsenceRules.Validate("2026-11-22", "2026-11-22", from, to, AbsenceRules.LevelAway, Today));

    [Theory]
    [InlineData("x", null)]
    [InlineData("2", null)]
    [InlineData("1", null)]
    [InlineData("0", AbsenceInputError.InvalidLevel)]
    public void The_level_is_one_of_three(string level, AbsenceInputError? expected) =>
        Assert.Equal(expected, AbsenceRules.Validate("2026-11-22", "2026-11-22", null, null, level, Today));

    [Fact]
    public void Only_not_there_counts_a_player_out_and_an_older_server_sends_no_level()
    {
        Assert.True(AbsenceRules.CountsAsAway("x"));
        Assert.True(AbsenceRules.CountsAsAway(null));
        Assert.False(AbsenceRules.CountsAsAway("2"));
        Assert.False(AbsenceRules.CountsAsAway("1"));
    }

    [Fact]
    public void The_body_leaves_out_what_the_server_assumes_and_cuts_the_note()
    {
        var note = new string('a', 200);
        var request = AbsenceRules.BuildRequest("2026-11-22", "2026-11-22", null, null, "x", $"  {note}  ", withTimesAndLevel: true);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, EorzeaJson.Options));
        var root = json.RootElement;

        Assert.Equal(AbsenceRules.MaxNote, root.GetProperty("note").GetString()!.Length);
        Assert.False(root.TryGetProperty("level", out _));
        Assert.False(root.TryGetProperty("from_time", out _));
        Assert.False(root.TryGetProperty("to_time", out _));
    }

    [Fact]
    public void Hours_and_a_level_go_as_the_contract_names_them()
    {
        var request = AbsenceRules.BuildRequest("2026-11-22", "2026-11-23", "22:00", "02:00", "1", "", withTimesAndLevel: true);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, EorzeaJson.Options));
        var root = json.RootElement;

        Assert.Equal("22:00", root.GetProperty("from_time").GetString());
        Assert.Equal("02:00", root.GetProperty("to_time").GetString());
        Assert.Equal("1", root.GetProperty("level").GetString());
        Assert.False(root.TryGetProperty("note", out _));
        Assert.True(AbsenceRules.RunsPastMidnight(request.FromTime, request.ToTime));
    }

    [Fact]
    public void Before_server_1_5_neither_hours_nor_a_level_are_sent()
    {
        var request = AbsenceRules.BuildRequest("2026-11-22", "2026-11-22", "19:00", "23:00", "2", null, withTimesAndLevel: false);

        Assert.Null(request.FromTime);
        Assert.Null(request.ToTime);
        Assert.Null(request.Level);
    }

    [Fact]
    public void An_absence_of_server_1_5_reads_its_new_fields()
    {
        const string body = """
            [{ "id": 7, "user_id": 5, "team_id": 0, "from_date": "2026-10-30", "to_date": "2026-10-30",
               "from_time": "19:00", "to_time": "23:00", "level": "2", "timezone": "Europe/Berlin", "note": null }]
            """;

        var absence = Assert.Single(JsonSerializer.Deserialize<AbsencesResponse>(body, EorzeaJson.Options)!.Items);

        Assert.Equal(0, absence.TeamId);
        Assert.Equal("19:00", absence.FromTime);
        Assert.Equal("23:00", absence.ToTime);
        Assert.Equal("2", absence.Level);
        Assert.Equal("Europe/Berlin", absence.Timezone);
    }

    [Fact]
    public void The_half_hours_offer_24_00_only_as_an_end()
    {
        Assert.Equal(48, AbsenceRules.HalfHours(asEnd: false).Count);
        Assert.Equal("23:30", AbsenceRules.HalfHours(asEnd: false)[^1]);
        Assert.Equal("24:00", AbsenceRules.HalfHours(asEnd: true)[^1]);
    }
}
