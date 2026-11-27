using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The goals to read of server 1.5 (<c>goals-progress</c>), held to <c>contract-goals.md</c>.</summary>
public sealed class GoalsServiceTests
{
    // The contract's example, plus a done goal and one held but not used, in the server's own shape.
    private const string MineJson = """
        { "data": {
            "character_id": "1234",
            "goals": [
              { "id": 17, "item": { "id": 44505, "name": "Pfeife", "ilvl": 1 }, "area": "mount",
                "qty": 1, "held": 0, "shared": true, "learnable": true, "learned": false, "unused": false,
                "done": null,
                "progress": { "kind": "trade", "cost": [
                  { "id": 46720, "name": "Ritterinnen-Totem", "count": 99, "need": 99, "have": 37, "missing": 62 } ] },
                "created_at": "2026-11-27 20:00:00" },
              { "id": 16, "item": { "id": 52034, "name": "Zero-Karte", "ilvl": null }, "area": "card",
                "qty": 1, "held": 0, "shared": false, "learnable": true, "learned": true, "unused": false,
                "done": { "by": "learned", "at": "2026-11-27 22:34:41" }, "progress": null, "created_at": "…" },
              { "id": 15, "item": { "id": 6001, "name": "Horn" }, "area": "mount",
                "qty": 1, "held": 1, "shared": false, "learnable": true, "learned": false, "unused": true,
                "done": null, "progress": null, "created_at": "…", "something_new": 1 } ],
            "plan": [ { "id": 46720, "name": "Ritterinnen-Totem", "need": 99, "have": 37, "missing": 62, "goals": ["Pfeife"] } ]
        } }
        """;

    private const string TeamJson = """
        { "data": { "team_id": "7", "items": [
            { "item": { "id": 44505, "name": "Pfeife" }, "area": "mount",
              "way": { "kind": "trade", "cost": [ { "id": 46720, "name": "Ritterinnen-Totem", "count": 99 } ] },
              "wanted": [
                { "character": "Sanaka Sundream", "member": "Sanaka", "qty": 1, "done": null, "held": 0, "unused": false,
                  "cost_have": [ { "id": 46720, "have": 37 } ] },
                { "character": "Other", "member": "Other", "qty": 1, "done": { "by": "hand", "at": null }, "held": 0,
                  "unused": false, "cost_have": [ { "id": 46720, "have": 0 } ] } ] },
            { "item": { "id": 9001, "name": "Karte" }, "area": "card", "way": null, "wanted": [] } ] } }
        """;

    [Fact]
    public void The_goals_answer_reads_as_the_server_sends_it()
    {
        var mine = JsonSerializer.Deserialize<MyGoalsResponse>(MineJson, EorzeaJson.Options)!.Data!;

        Assert.Equal("1234", mine.CharacterId);
        Assert.Equal(3, mine.Goals.Count);

        var open = mine.Goals[0];
        Assert.Equal(44505, open.Item!.Id);
        Assert.Equal("mount", open.Area);
        Assert.True(open.Shared);
        Assert.Null(open.Done);
        var cost = Assert.Single(open.Progress!.Cost);
        Assert.Equal((99, 99, 37, 62), (cost.Count, cost.Need, cost.Have, cost.Missing));

        Assert.Equal("learned", mine.Goals[1].Done!.By);
        Assert.Null(mine.Goals[1].Item!.Ilvl);
        Assert.Null(mine.Goals[1].Progress);
        Assert.True(mine.Goals[2].Unused);

        var row = Assert.Single(mine.Plan);
        Assert.Equal(62, row.Missing);
        Assert.Equal(["Pfeife"], row.Goals);
    }

    [Fact]
    public void The_team_answer_reads_as_the_server_sends_it()
    {
        var team = JsonSerializer.Deserialize<TeamGoalsResponse>(TeamJson, EorzeaJson.Options)!.Data!;

        Assert.Equal("7", team.TeamId);
        var first = team.Items[0];
        Assert.Equal(99, Assert.Single(first.Way!.Cost).Count);
        Assert.Equal(2, first.Wanted.Count);
        Assert.Equal(37, Assert.Single(first.Wanted[0].CostHave).Have);
        Assert.Equal("hand", first.Wanted[1].Done!.By);
        Assert.Null(team.Items[1].Way);
    }

    [Fact]
    public async Task A_read_asks_for_the_character_and_language_and_waits_for_the_next_reason()
    {
        var api = new FakeApiClient
        {
            MyGoalsResult = ApiResult<MyGoalsResponse>.Ok(JsonSerializer.Deserialize<MyGoalsResponse>(MineJson, EorzeaJson.Options)!),
        };
        var service = Service(api);

        Assert.True(service.NeedsRead("de", "cid"));
        await service.RefreshAsync("de", "cid", CancellationToken.None);

        Assert.Equal([("cid", "de")], api.MyGoalsReads);
        Assert.Equal(3, service.Mine!.Goals.Count);
        Assert.False(service.NeedsRead("de", "cid"));

        // Another language or character, or a sync, is a reason to read again; time alone is not.
        Assert.True(service.NeedsRead("en", "cid"));
        Assert.True(service.NeedsRead("de", "other"));
        service.Invalidate();
        Assert.True(service.NeedsRead("de", "cid"));
    }

    [Fact]
    public async Task A_failed_read_keeps_what_was_shown_and_names_the_error()
    {
        var api = new FakeApiClient();
        var service = Service(api);
        await service.RefreshAsync("de", "cid", CancellationToken.None);
        Assert.NotNull(service.Mine);

        api.MyGoalsResult = ApiResult<MyGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.NotFound, StatusCode = 404, Message = "x" });
        service.Invalidate();
        await service.RefreshAsync("de", "cid", CancellationToken.None);

        Assert.Equal(ApiErrorKind.NotFound, service.LastError?.Kind);
        Assert.NotNull(service.Mine);

        service.Forget();
        Assert.Null(service.Mine);
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task The_menu_offers_remove_for_a_goal_add_for_the_rest_and_nothing_before_a_read_or_once_learned()
    {
        var api = new FakeApiClient
        {
            MyGoalsResult = ApiResult<MyGoalsResponse>.Ok(JsonSerializer.Deserialize<MyGoalsResponse>(MineJson, EorzeaJson.Options)!),
        };
        var service = Service(api);
        Assert.Equal(GoalMenuAction.None, service.ChoiceFor(44505, learned: false, stacks: false, ownStorage: false).Action);

        await service.RefreshAsync("de", "cid", CancellationToken.None);

        // A goal offers "remove" wherever it is clicked, the player's own bags included.
        var remove = service.ChoiceFor(44505, learned: false, stacks: false, ownStorage: true);
        Assert.Equal(GoalMenuAction.Remove, remove.Action);
        Assert.Equal(17, remove.Goal!.Id);

        // A done goal is still a goal: it can be removed.
        Assert.Equal(GoalMenuAction.Remove, service.ChoiceFor(52034, learned: true, stacks: false, ownStorage: false).Action);
        Assert.Empty(api.MyGoalsReads.Skip(1));
    }

    [Fact]
    public async Task A_quantity_goes_with_the_goal_and_one_is_left_to_the_website()
    {
        var api = new FakeApiClient();
        var service = Service(api);

        await service.AddAsync(46720, 99, "de", "cid", CancellationToken.None);
        await service.AddAsync(44505, null, "de", "cid", CancellationToken.None);

        using var withQty = JsonDocument.Parse(JsonSerializer.Serialize(api.GoalsAdded[0], EorzeaJson.Options));
        Assert.Equal(99, withQty.RootElement.GetProperty("qty").GetInt64());
        using var without = JsonDocument.Parse(JsonSerializer.Serialize(api.GoalsAdded[1], EorzeaJson.Options));
        Assert.False(without.RootElement.TryGetProperty("qty", out _));
    }

    [Theory]
    [InlineData(false, false, false, GoalMenuAction.Add)]             // a single thing in a shop
    [InlineData(false, false, true, GoalMenuAction.None)]             // a single thing in one's own bags: held
    [InlineData(true, false, false, GoalMenuAction.None)]             // learned: nothing more to get
    [InlineData(true, false, true, GoalMenuAction.None)]
    [InlineData(false, true, true, GoalMenuAction.AddWithQuantity)]   // totems, dyes: collect more, say how many
    [InlineData(false, true, false, GoalMenuAction.AddWithQuantity)]
    public async Task The_menu_adds_only_what_is_still_to_get(bool learned, bool stacks, bool ownStorage, GoalMenuAction expected)
    {
        var service = Service(new FakeApiClient());
        await service.RefreshAsync("de", "cid", CancellationToken.None);

        Assert.Equal(expected, service.ChoiceFor(12345, learned, stacks, ownStorage).Action);
    }

    [Fact]
    public async Task Adding_and_removing_send_the_character_and_take_the_answer_as_the_new_state()
    {
        var api = new FakeApiClient
        {
            GoalWriteResult = ApiResult<MyGoalsResponse>.Ok(JsonSerializer.Deserialize<MyGoalsResponse>(MineJson, EorzeaJson.Options)!),
        };
        var service = Service(api);

        Assert.Null(await service.AddAsync(44505, null, "de", "cid", CancellationToken.None));
        var added = Assert.Single(api.GoalsAdded);
        Assert.Equal(("cid", 44505L), (added.Character.CidHash, added.ItemId));
        Assert.Equal(3, service.Mine!.Goals.Count);
        Assert.False(service.NeedsRead("de", "cid"));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(added, EorzeaJson.Options));
        Assert.Equal("cid", json.RootElement.GetProperty("character").GetProperty("cid_hash").GetString());
        Assert.Equal(44505, json.RootElement.GetProperty("item_id").GetInt64());

        Assert.Null(await service.RemoveAsync(17, "de", "cid", CancellationToken.None));
        Assert.Equal([17L], api.GoalsRemoved);

        api.GoalWriteResult = ApiResult<MyGoalsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Conflict, StatusCode = 409, Message = "Learned already" });
        Assert.Equal(ApiErrorKind.Conflict, (await service.AddAsync(6001, null, "de", "cid", CancellationToken.None))?.Kind);
        Assert.Equal(3, service.Mine!.Goals.Count);
    }

    [Theory]
    [InlineData("mount", true, "/ziele/reittiere")]
    [InlineData("mount", false, "/goals/mounts")]
    [InlineData("minion", true, "/ziele/begleiter")]
    [InlineData("card", false, "/goals/triple-triad")]
    [InlineData("other", true, "/ziele/sonstiges")]
    [InlineData("glam", false, "/goals/glam")]
    [InlineData(null, true, "/ziele")]
    [InlineData("unknown", false, "/goals")]
    public void The_links_follow_the_website(string? area, bool german, string expected) =>
        Assert.Equal(expected, GoalLinks.Path(area, german));

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
