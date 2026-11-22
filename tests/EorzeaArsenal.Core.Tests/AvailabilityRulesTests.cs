using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Times, polls and a team's evening, held to <c>contract-team-availability.md</c>.</summary>
public sealed class AvailabilityRulesTests
{
    private const string TimesJson = """
        { "data": {
          "this_week": "2026-10-19",
          "scopes": [{
            "team_id": null, "timezone": "Europe/Berlin", "ask_weekly": false,
            "rhythm": [12, 13], "rhythm_start": "2026-11-02",
            "plans": [{ "id": 12, "name": "Früh", "is_active": true, "ends_on": null, "cells": "" },
                      { "id": 13, "name": "Spät", "is_active": false, "ends_on": null, "cells": "" }],
            "weeks": [{ "week": "2026-11-16", "plan_id": 13 }] },
            { "team_id": 3, "timezone": "Europe/Berlin", "ask_weekly": true, "rhythm": [], "rhythm_start": null,
              "plans": [{ "id": 20, "name": "Static", "is_active": true, "ends_on": null, "cells": "" }],
              "weeks": [] }],
          "absences": [{ "id": 7, "team_id": null, "from_date": "2026-10-30", "to_date": "2026-10-30",
                          "from_time": "19:00", "to_time": "23:00", "level": "x", "timezone": "Europe/Berlin", "note": null }],
          "teams": [{ "id": 3, "name": "Static", "own_times": true }, { "id": 4, "name": "Alt", "own_times": false }]
        }}
        """;

    private static MyAvailability Times() => JsonSerializer.Deserialize<MyAvailabilityResponse>(TimesJson, EorzeaJson.Options)!.Data!;

    [Fact]
    public void The_answer_of_the_contract_reads_as_written()
    {
        var times = Times();
        Assert.Equal("2026-10-19", times.ThisWeek);
        Assert.Equal(2, times.Scopes.Count);
        Assert.Null(times.Scopes[0].TeamId);
        Assert.Equal([12L, 13L], times.Scopes[0].Rhythm!);
        Assert.Null(Assert.Single(times.Absences).TeamId);
    }

    [Fact]
    public void A_team_with_times_of_its_own_reads_them_and_every_other_team_reads_the_own_times()
    {
        var times = Times();
        Assert.Equal(3, AvailabilityRules.ScopeFor(times, 3)!.TeamId);
        Assert.Null(AvailabilityRules.ScopeFor(times, 4)!.TeamId);
        Assert.Null(AvailabilityRules.ScopeFor(times, null)!.TeamId);
    }

    [Theory]
    [InlineData("2026-10-26", 12L, WeekPlanSource.Grundplan)] // before the rhythm starts
    [InlineData("2026-11-02", 12L, WeekPlanSource.Rhythm)]
    [InlineData("2026-11-09", 13L, WeekPlanSource.Rhythm)]
    [InlineData("2026-11-16", 13L, WeekPlanSource.SetByHand)]
    [InlineData("2026-11-23", 13L, WeekPlanSource.Rhythm)]
    [InlineData("2026-11-30", 12L, WeekPlanSource.Rhythm)]
    public void A_week_reads_by_hand_then_rhythm_then_the_grundplan(string monday, long plan, WeekPlanSource source)
    {
        var week = AvailabilityRules.PlanForWeek(Times().Scopes[0], DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(plan, week.Plan!.Id);
        Assert.Equal(source, week.Source);
    }

    [Fact]
    public void A_scope_that_asks_every_week_is_unclear_until_chosen()
    {
        var week = AvailabilityRules.PlanForWeek(Times().Scopes[1], new DateOnly(2026, 11, 23));
        Assert.Equal(WeekPlanSource.Unclear, week.Source);
        Assert.Null(week.Plan);
    }

    [Fact]
    public void A_plan_past_its_end_gives_way_to_the_grundplan()
    {
        var scope = new AvailabilityScope
        {
            Plans =
            [
                new AvailabilityPlan { Id = 1, Name = "Normal", IsActive = true },
                new AvailabilityPlan { Id = 2, Name = "Ferien", EndsOn = "2026-11-10" },
            ],
            Weeks = [new AvailabilityWeek { Week = "2026-11-16", PlanId = 2 }, new AvailabilityWeek { Week = "2026-11-09", PlanId = 2 }],
        };

        Assert.Equal(2, AvailabilityRules.PlanForWeek(scope, new DateOnly(2026, 11, 9)).Plan!.Id);
        var after = AvailabilityRules.PlanForWeek(scope, new DateOnly(2026, 11, 16));
        Assert.Equal(1, after.Plan!.Id);
        Assert.Equal(WeekPlanSource.Grundplan, after.Source);
    }

    [Fact]
    public void A_week_is_named_by_its_monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), AvailabilityRules.MondayOf(new DateOnly(2026, 11, 22)));
        Assert.Equal(new DateOnly(2026, 11, 16), AvailabilityRules.MondayOf(new DateOnly(2026, 11, 16)));
    }

    [Theory]
    [InlineData("yes", "yes")]
    [InlineData("maybe", "maybe")]
    [InlineData("mixed", "maybe")]
    [InlineData("no", "no")]
    [InlineData("unclear", null)]
    [InlineData(null, null)]
    public void The_times_suggest_an_answer_the_way_the_site_does(string? state, string? expected) =>
        Assert.Equal(expected, AvailabilityRules.SuggestedAnswer(state is null ? null : new PollFit { State = state }));

    private const string PollsJson = """
        { "polls": [
          { "id": 4, "team_id": 3, "team_name": "Static", "title": "Prog", "closes_at": "2026-10-25 20:00", "open": true,
            "multiple": false, "decided": false, "all_past": false, "answered": 5, "of": 8,
            "options": [
              { "id": 11, "kind": "oneoff", "first_date": "2026-10-27", "past": false, "time": "20:00", "end_time": "23:00",
                "counts": { "yes": 4, "maybe": 1, "no": 0 }, "best": true, "mine": null, "chosen": false,
                "my_fit": { "state": "yes", "fits": 1, "of": 1 } },
              { "id": 12, "kind": "weekly", "first_date": "2026-10-28", "past": false, "weekdays": [1, 3], "time": "20:00",
                "end_time": "23:00", "day_times": { "0": { "time": "18:00", "end_time": "21:00" } },
                "counts": { "yes": 1, "maybe": 0, "no": 2 }, "mine": null, "chosen": false,
                "my_fit": { "state": "mixed", "fits": 2, "of": 6 } },
              { "id": 13, "kind": "oneoff", "first_date": "2026-10-29", "past": false, "time": "20:00",
                "mine": "no", "chosen": false, "my_fit": { "state": "yes", "fits": 1, "of": 1 } },
              { "id": 14, "kind": "oneoff", "first_date": "2026-10-20", "past": true, "time": "20:00",
                "mine": null, "chosen": false, "my_fit": { "state": "yes", "fits": 1, "of": 1 } },
              { "id": 15, "kind": "oneoff", "first_date": "2026-10-30", "past": false, "time": "20:00",
                "mine": null, "chosen": false, "my_fit": { "state": "unclear", "fits": 0, "of": 1 } }
            ],
            "my_open_weeks": [{ "week": "2026-10-26", "team_id": null, "plans": [{ "id": 12, "name": "Früh" }] }] },
          { "id": 5, "team_id": 3, "title": "Alt", "open": false, "all_past": true, "options": [] }
        ] }
        """;

    private static IReadOnlyList<Poll> Polls() => JsonSerializer.Deserialize<MyPollsResponse>(PollsJson, EorzeaJson.Options)!.All;

    [Fact]
    public void The_polls_read_with_or_without_a_data_envelope()
    {
        Assert.Equal(2, Polls().Count);
        var wrapped = JsonSerializer.Deserialize<MyPollsResponse>("""{ "data": { "polls": [ { "id": 1, "open": true } ] } }""", EorzeaJson.Options)!;
        Assert.Single(wrapped.All);

        var option = Polls()[0].Options[1];
        Assert.Equal("18:00", option.DayTimes!["0"].Time);
        Assert.Equal([1, 3], option.Weekdays!);
        Assert.Single(Polls()[0].MyOpenWeeks!);
    }

    [Fact]
    public void A_poll_whose_candidates_are_all_past_is_left_out_and_so_is_a_past_candidate()
    {
        Assert.True(AvailabilityRules.Shows(Polls()[0]));
        Assert.False(AvailabilityRules.Shows(Polls()[1]));
        Assert.DoesNotContain(AvailabilityRules.VisibleOptions(Polls()[0]), o => o.Id == 14);
    }

    [Fact]
    public void Answering_from_the_times_leaves_answered_past_and_unclear_candidates_alone()
    {
        var answers = AvailabilityRules.AnswersFromTimes(Polls()[0]);
        Assert.Equal([(11L, "yes"), (12L, "maybe")], answers);
    }

    [Fact]
    public void A_take_back_sends_a_null_answer_and_a_week_back_a_null_plan()
    {
        using var answer = JsonDocument.Parse(JsonSerializer.Serialize(new PollAnswerRequest { OptionId = 11, Answer = null }, EorzeaJson.Options));
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("answer").ValueKind);

        using var week = JsonDocument.Parse(JsonSerializer.Serialize(new AvailabilityWeekRequest { Week = "2026-10-26", PlanId = null }, EorzeaJson.Options));
        Assert.Equal(JsonValueKind.Null, week.RootElement.GetProperty("plan_id").ValueKind);
        Assert.False(week.RootElement.TryGetProperty("team_id", out _));

        using var tonight = JsonDocument.Parse(JsonSerializer.Serialize(new NotTonightRequest(), EorzeaJson.Options));
        Assert.False(tonight.RootElement.TryGetProperty("team_id", out _));
    }

    private static TeamWeek Week()
    {
        var can = new int[336];
        var maybe = new int[336];
        var unclear = new int[336];
        var cannot = new int[336];
        var tuesday20 = (1 * 48) + 40;
        can[tuesday20] = 5;
        maybe[tuesday20] = 1;
        unclear[tuesday20] = 1;
        cannot[tuesday20] = 1;
        return new TeamWeek
        {
            Week = "2026-10-19",
            Days = ["2026-10-19", "2026-10-20", "2026-10-21", "2026-10-22", "2026-10-23", "2026-10-24", "2026-10-25"],
            Groups = new Dictionary<string, AvailabilityGroup>
            {
                ["core"] = new() { Size = 8, Can = [.. can], Maybe = [.. maybe], Unclear = [.. unclear], Cannot = [.. cannot] },
            },
            Events =
            [
                new TeamWeekEvent { EventId = 1, Date = "2026-10-22", Title = "Later", Time = "20:00" },
                new TeamWeekEvent { EventId = 2, Date = "2026-10-20", Title = "Savage", Time = "20:00", EndTime = "23:00" },
                new TeamWeekEvent { EventId = 3, Date = "2026-10-19", Title = "Over", Time = "20:00", EndTime = "23:00" },
            ],
        };
    }

    [Fact]
    public void The_next_evening_is_the_first_event_not_over_yet()
    {
        Assert.Equal(2, AvailabilityRules.NextEvent(Week(), new DateTime(2026, 10, 20, 9, 0, 0))!.EventId);
        Assert.Equal(2, AvailabilityRules.NextEvent(Week(), new DateTime(2026, 10, 20, 22, 0, 0))!.EventId);
        Assert.Equal(1, AvailabilityRules.NextEvent(Week(), new DateTime(2026, 10, 20, 23, 30, 0))!.EventId);
        Assert.Null(AvailabilityRules.NextEvent(Week(), new DateTime(2026, 10, 23, 9, 0, 0)));
    }

    [Fact]
    public void An_evening_is_counted_at_its_start_half_hour()
    {
        var week = Week();
        var counts = AvailabilityRules.AtStart(week, week.Events[1]);
        Assert.Equal(new HalfHourCounts(5, 1, 1, 1, 8), counts);
        Assert.Null(AvailabilityRules.AtStart(week, week.Events[1], "all"));
    }

    [Fact]
    public async Task The_waiting_counts_are_read_at_most_once_a_minute_and_a_429_holds_the_next_read()
    {
        var api = new FakeApiClient
        {
            MyWaitingResult = ApiResult<MyWaitingResponse>.Ok(JsonSerializer.Deserialize<MyWaitingResponse>(
                """{ "data": { "teams": { "7": { "members": 0, "zeiten": 2, "polls": 1, "ready": 1, "termine": 1, "total": 3 } }, "total": 3 } }""",
                EorzeaJson.Options)!),
        };
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_test_key");
        var clock = new TestClock();
        var counter = new WaitingCounter(api, tokens, clock);

        await counter.RefreshIfDueAsync(CancellationToken.None);
        await counter.RefreshIfDueAsync(CancellationToken.None);
        Assert.Equal(1, api.MyWaitingReads);
        Assert.Equal(3, counter.Total);
        Assert.Equal(2, counter.For(7)!.Zeiten);
        Assert.Null(counter.For(8));

        api.MyWaitingResult = ApiResult<MyWaitingResponse>.Fail(new ApiError { Kind = ApiErrorKind.RateLimited, Message = "x", RetryAfter = TimeSpan.FromMinutes(3) });
        clock.Advance(TimeSpan.FromMinutes(1));
        await counter.RefreshIfDueAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        await counter.RefreshIfDueAsync(CancellationToken.None);
        Assert.Equal(2, api.MyWaitingReads);
        Assert.Equal(3, counter.Total);

        counter.Expire();
        await counter.RefreshIfDueAsync(CancellationToken.None);
        Assert.Equal(3, api.MyWaitingReads);
    }
}
