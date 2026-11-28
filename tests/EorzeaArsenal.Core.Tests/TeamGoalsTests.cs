using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The goals of a team (<c>team-goals</c>), held to <c>contract-team-goals.md</c>.</summary>
public sealed class TeamGoalsTests
{
    // GET /me/goals with team-goals, in the server's own shape: ids of teams as strings, an own goal shown in one
    // team and a goal of a second team too, one goal of a team with the character's own state.
    private const string MineJson = """
        { "data": {
            "character_id": "29",
            "goals": [
              { "id": 17, "item": { "id": 44505, "name": "Pfeife" }, "area": "mount", "qty": 1, "held": 0,
                "teams": ["6"], "learnable": true, "kind": "mount", "purpose": "learn", "learned": false, "unused": false,
                "done": null, "progress": null, "created_at": "…", "also_team": ["FC-Runde"] } ],
            "plan": [ { "id": 45691, "name": "Totem", "need": 99, "have": 37, "missing": 62, "goals": ["Flügel (Static A)"] } ],
            "team_goals": [ {
              "team": { "id": "6", "name": "Static A" },
              "id": "12", "item": { "id": 52270, "name": "Flügel" }, "area": "mount", "kind": "mount",
              "purpose": "learn", "qty": 1,
              "way": { "kind": "trade", "cost": [ { "id": 45691, "name": "Totem", "count": 99 } ] },
              "where": { "duty": { "id": 1001, "name": "Die Ruinen" }, "content": null, "catalog_id": null },
              "closed": false, "created_at": "…",
              "participants": [ { "character_id": "29", "roster_entry_id": null, "name": "Sanaka", "member": "Sanaka",
                "user_id": "38", "state": "open", "mark": null, "held": 0, "counts_hidden": false,
                "progress": { "kind": "trade", "cost": [ { "id": 45691, "name": "Totem", "count": 99, "need": 99, "have": 37, "missing": 62 } ] } } ] } ],
            "team_goal_teams": [ { "id": "6", "name": "Static A" }, { "id": "9", "name": "FC-Runde" }, { "id": "11", "name": "Dritte" } ],
            "my_teams": [ { "id": "6", "name": "Static A" }, { "id": "9", "name": "FC-Runde" }, { "id": "11", "name": "Dritte" } ]
        } }
        """;

    // GET /teams/{id}/team-goals: the contract's example, with a placeholder, a hidden player and an answer.
    private const string BoardJson = """
        { "data": {
            "team_id": "6", "may_manage": false,
            "goals": [ {
              "id": "12", "item": { "id": 52270, "name": "Flügel" }, "area": "mount", "kind": "mount", "purpose": "learn", "qty": 1,
              "way": null, "where": null, "closed": false, "created_at": "…",
              "needed": 2, "counted": 3, "reached": false,
              "participants": [
                { "character_id": "29", "roster_entry_id": null, "name": "Sanaka", "member": "Sanaka", "user_id": "38",
                  "state": "open", "mark": null, "held": 0, "counts_hidden": false, "progress": null },
                { "character_id": "31", "roster_entry_id": null, "name": "Zweitcharakter", "member": "Sanaka", "user_id": "38",
                  "state": "have", "mark": { "mark": "have", "by": "Anna", "at": "…" }, "held": 0, "counts_hidden": false, "progress": null },
                { "character_id": "40", "roster_entry_id": null, "name": "Anna", "member": "Anna", "user_id": "41",
                  "state": "open", "mark": null, "held": null, "counts_hidden": true, "progress": null },
                { "character_id": null, "roster_entry_id": "4", "name": "Heiler 2", "member": null, "user_id": null,
                  "state": "no_interest", "mark": { "mark": "no_interest", "by": "Anna", "at": "…" }, "held": null, "counts_hidden": false, "progress": null } ] } ] } }
        """;

    [Fact]
    public void The_goals_answer_reads_the_teams_with_ids_as_strings()
    {
        var mine = Mine();

        var own = Assert.Single(mine.Goals);
        Assert.Equal([6L], own.Teams);
        Assert.Equal(["FC-Runde"], own.AlsoTeam);

        var team = Assert.Single(mine.TeamGoals);
        Assert.Equal((6L, "Static A", 12L), (team.Team!.Id, team.Team.Name, team.Id));
        Assert.True(team.ToLearn);
        Assert.Equal(1001, team.Where!.Duty!.Id);
        var me = Assert.Single(team.Participants);
        Assert.Equal((29L, 38L, TeamGoalStates.Open), (me.CharacterId, me.UserId, me.State));
        Assert.Equal(62, Assert.Single(me.Progress!.Cost).Missing);
        Assert.Equal([6L, 9L, 11L], mine.TeamGoalTeams.Select(t => t.Id));
    }

    [Fact]
    public void The_board_reads_states_answers_placeholders_and_hidden_numbers()
    {
        var board = JsonSerializer.Deserialize<TeamGoalsResponse>(BoardJson, EorzeaJson.Options)!.Data!;

        Assert.Equal(6, board.TeamId);
        Assert.False(board.MayManage);
        var goal = Assert.Single(board.Goals);
        Assert.Equal((2, 3, false), (goal.Needed, goal.Counted, goal.Reached));
        Assert.Equal("Anna", goal.Participants[1].Mark!.By);
        Assert.Null(goal.Participants[2].Held);
        Assert.True(goal.Participants[2].CountsHidden);
        Assert.Equal((null, 4L), (goal.Participants[3].CharacterId, goal.Participants[3].RosterEntryId));
    }

    [Fact]
    public void An_answer_names_the_character_or_the_placeholder_and_always_sends_the_mark()
    {
        static JsonElement Json(TeamGoalMarkRequest request) =>
            JsonDocument.Parse(JsonSerializer.Serialize(request, EorzeaJson.Options)).RootElement.Clone();

        var taken = Json(new TeamGoalMarkRequest { CharacterId = 29, Mark = null });
        Assert.Equal(29, taken.GetProperty("character_id").GetInt64());
        Assert.Equal(JsonValueKind.Null, taken.GetProperty("mark").ValueKind);
        Assert.False(taken.TryGetProperty("roster_entry_id", out _));

        var placeholder = Json(new TeamGoalMarkRequest { RosterEntryId = 4, Mark = TeamGoalStates.NoInterest });
        Assert.Equal(4, placeholder.GetProperty("roster_entry_id").GetInt64());
        Assert.Equal("no_interest", placeholder.GetProperty("mark").GetString());
        Assert.False(placeholder.TryGetProperty("character_id", out _));
    }

    [Fact]
    public async Task The_menu_offers_the_teams_with_the_right_less_those_that_have_the_item_already()
    {
        var service = await Read(Mine());

        // Static A has the wings as its goal, the FC has the whistle (named on the own goal): neither is offered.
        Assert.Equal([9L, 11L], service.TeamChoicesFor(52270, currency: false).Select(t => t.Id));
        Assert.Equal([6L, 11L], service.TeamChoicesFor(44505, currency: false).Select(t => t.Id));
        Assert.Equal([6L, 9L, 11L], service.TeamChoicesFor(12345, currency: false).Select(t => t.Id));

        // Gil and currencies are no goal of a team.
        Assert.Empty(service.TeamChoicesFor(28, currency: true));
    }

    [Fact]
    public async Task Without_the_right_in_any_team_or_before_a_read_nothing_is_offered()
    {
        Assert.Empty(Service(new FakeApiClient()).TeamChoicesFor(12345, currency: false));

        var service = await Read(new MyGoals { MyTeams = [new TeamRef { Id = 6, Name = "Static A" }] });
        Assert.Empty(service.TeamChoicesFor(12345, currency: false));
    }

    [Fact]
    public void Own_characters_and_their_player_may_answer_others_only_with_the_right()
    {
        var goal = Board().Goals[0];
        var own = new HashSet<long> { 29 };
        var users = TeamGoalRules.OwnUsers([goal], own);
        Assert.Equal([38L], users);

        // The second character of the same player was never logged in here: it is theirs all the same.
        Assert.True(TeamGoalRules.MayAnswer(goal.Participants[0], mayManage: false, own, users));
        Assert.True(TeamGoalRules.MayAnswer(goal.Participants[1], mayManage: false, own, users));
        Assert.False(TeamGoalRules.MayAnswer(goal.Participants[2], mayManage: false, own, users));
        Assert.False(TeamGoalRules.MayAnswer(goal.Participants[3], mayManage: false, own, users));

        Assert.True(TeamGoalRules.MayAnswer(goal.Participants[2], mayManage: true, own, users));
        Assert.True(TeamGoalRules.MayAnswer(goal.Participants[3], mayManage: true, own, users));
    }

    [Fact]
    public void Open_offers_both_answers_an_answer_offers_taking_it_back_and_data_offers_nothing()
    {
        var goal = Board().Goals[0];
        Assert.Equal([TeamGoalAnswer.Have, TeamGoalAnswer.NoInterest], TeamGoalRules.Answers(goal, goal.Participants[0], mayAnswer: true));
        Assert.Equal([TeamGoalAnswer.TakeBack], TeamGoalRules.Answers(goal, goal.Participants[1], mayAnswer: true));
        Assert.Empty(TeamGoalRules.Answers(goal, goal.Participants[0], mayAnswer: false));

        var learned = new TeamGoalParticipant { CharacterId = 29, State = TeamGoalStates.Learned };
        Assert.Empty(TeamGoalRules.Answers(goal, learned, mayAnswer: true));

        var closed = new TeamGoal { Closed = true };
        Assert.Empty(TeamGoalRules.Answers(closed, goal.Participants[0], mayAnswer: true));

        Assert.Equal([TeamGoalStates.Have, TeamGoalStates.NoInterest, null], new[] { TeamGoalAnswer.Have, TeamGoalAnswer.NoInterest, TeamGoalAnswer.TakeBack }.Select(TeamGoalRules.MarkOf));
    }

    [Fact]
    public void Only_have_on_a_goal_to_learn_asks_twice()
    {
        var learn = new TeamGoal { Kind = "mount", Purpose = GoalPurposes.Learn };
        var collect = new TeamGoal { Kind = "mount", Purpose = GoalPurposes.Collect };
        var item = new TeamGoal { Kind = null, Purpose = GoalPurposes.Collect };

        Assert.True(TeamGoalRules.AsksTwice(learn, TeamGoalAnswer.Have));
        Assert.False(TeamGoalRules.AsksTwice(learn, TeamGoalAnswer.NoInterest));
        Assert.False(TeamGoalRules.AsksTwice(learn, TeamGoalAnswer.TakeBack));
        Assert.False(TeamGoalRules.AsksTwice(collect, TeamGoalAnswer.Have));
        Assert.False(TeamGoalRules.AsksTwice(item, TeamGoalAnswer.Have));
    }

    [Fact]
    public async Task Writes_send_the_team_and_make_the_own_goals_read_again()
    {
        var api = new FakeApiClient
        {
            TeamGoalsResult = ApiResult<TeamGoalsResponse>.Ok(JsonSerializer.Deserialize<TeamGoalsResponse>(BoardJson, EorzeaJson.Options)!),
        };
        var service = Service(api);
        api.MyGoalsResult = ApiResult<MyGoalsResponse>.Ok(new MyGoalsResponse { Data = Mine() });
        await service.RefreshAsync("de", "cid", CancellationToken.None);
        Assert.False(service.NeedsRead("de", "cid"));

        var set = await service.AddTeamGoalAsync(9, 52270, "de", CancellationToken.None);
        Assert.True(set.IsSuccess);
        Assert.Equal([(9L, 52270L)], api.TeamGoalsAdded);
        Assert.True(service.NeedsRead("de", "cid"));

        await service.RefreshAsync("de", "cid", CancellationToken.None);
        var board = Board();
        await service.MarkTeamGoalAsync(6, 12, board.Goals[0].Participants[3], null, "de", CancellationToken.None);
        var (team, goal, request) = Assert.Single(api.TeamGoalMarks);
        Assert.Equal((6L, 12L, null, 4L, null), (team, goal, request.CharacterId, request.RosterEntryId, request.Mark));
        Assert.True(service.NeedsRead("de", "cid"));

        // A right taken away: the next read decides what the menu offers.
        await service.RefreshAsync("de", "cid", CancellationToken.None);
        api.TeamGoalsResult = ApiResult<TeamGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, StatusCode = 403, Message = "x", MissingCapability = "manage_team_goals" });
        Assert.False((await service.AddTeamGoalAsync(9, 52270, "de", CancellationToken.None)).IsSuccess);
        Assert.True(service.NeedsRead("de", "cid"));
    }

    [Fact]
    public void The_tooltip_names_the_team_with_the_state_and_an_own_goal_names_its_teams_too()
    {
        var de = new Localizer(Localizer.German);
        var mine = Mine();

        Assert.Equal(
            [("Teamziel von Static A · Offen", GoalHintTone.Missing), ("37 / 99 Totem, fehlen 62", GoalHintTone.Missing)],
            GoalHints.For(mine, 52270, de).Select(l => (l.Text, l.Tone)));

        Assert.Contains(GoalHints.For(mine, 44505, de), l => l.Text == "Auch Teamziel von FC-Runde");
        Assert.Contains(GoalHints.For(mine, 45691, de), l => l.Text == "Gebraucht für: Flügel (Static A)");
    }

    [Fact]
    public void To_collect_is_named_only_for_an_item_to_learn()
    {
        // The server sends "collect" for everything that unlocks nothing: there it says nothing (operator, 2026-11-28).
        Assert.True(new TeamGoal { Kind = "mount", Purpose = GoalPurposes.Collect }.ToCollect);
        Assert.False(new TeamGoal { Kind = null, Purpose = GoalPurposes.Collect }.ToCollect);
        Assert.False(new TeamGoal { Kind = "mount", Purpose = GoalPurposes.Learn }.ToCollect);

        var de = new Localizer(Localizer.German);
        var scale = new MyGoals { Goals = [new Goal { Id = 1, Item = new GoalItem { Id = 7 }, Purpose = GoalPurposes.Collect, Learnable = false, Qty = 1 }] };
        Assert.Equal("Als Ziel gesetzt", GoalHints.For(scale, 7, de)[0].Text);

        var whistle = new MyGoals { Goals = [new Goal { Id = 2, Item = new GoalItem { Id = 8 }, Purpose = GoalPurposes.Collect, Learnable = true, Qty = 1 }] };
        Assert.Equal("Als Ziel gesetzt · Zum Sammeln", GoalHints.For(whistle, 8, de)[0].Text);
    }

    [Fact]
    public void The_state_reads_as_words_with_who_answered()
    {
        var de = new Localizer(Localizer.German);
        var goal = Board().Goals[0];

        Assert.Equal(("Hat es schon", GoalHintTone.Good), TeamGoalText.State(goal.Participants[1], de));
        Assert.Equal("gesetzt von Anna", TeamGoalText.MarkedBy(goal.Participants[1], de));
        Assert.Equal(("Kein Interesse", GoalHintTone.Plain), TeamGoalText.State(goal.Participants[3], de));
        Assert.Null(TeamGoalText.MarkedBy(goal.Participants[0], de));
    }

    [Fact]
    public void A_member_who_hides_their_numbers_reads_as_hidden_not_as_zero()
    {
        const string json = """
            { "data": { "team_id": "6", "items": [ { "item": { "id": 1 }, "way": null, "wanted": [
                { "character": "Anna", "member": "Anna", "qty": 5, "purpose": "collect", "done": null, "held": null,
                  "unused": false, "cost_have": [], "counts_hidden": true } ] } ] } }
            """;
        var want = Assert.Single(Assert.Single(JsonSerializer.Deserialize<SharedGoalsResponse>(json, EorzeaJson.Options)!.Data!.Items).Wanted);

        Assert.Null(want.Held);
        Assert.True(want.CountsHidden);
        Assert.Equal(GoalPurposes.Collect, want.Purpose);
    }

    private static MyGoals Mine() => JsonSerializer.Deserialize<MyGoalsResponse>(MineJson, EorzeaJson.Options)!.Data!;

    private static TeamGoalBoard Board() => JsonSerializer.Deserialize<TeamGoalsResponse>(BoardJson, EorzeaJson.Options)!.Data!;

    private static async Task<GoalsService> Read(MyGoals mine)
    {
        var api = new FakeApiClient { MyGoalsResult = ApiResult<MyGoalsResponse>.Ok(new MyGoalsResponse { Data = mine }) };
        var service = Service(api);
        await service.RefreshAsync("de", "cid", CancellationToken.None);
        return service;
    }

    private static GoalsService Service(FakeApiClient api)
    {
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_test_key");
        return new GoalsService(api, tokens, new StubSettings("https://dev.example.test/api/v1"));
    }

    private sealed class StubSettings(string baseUrl) : IApiSettings
    {
        public string BaseUrl { get; } = baseUrl;
    }
}
