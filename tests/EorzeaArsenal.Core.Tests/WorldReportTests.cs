using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Spot reports of server 1.5, held to <c>contract-world-reports.md</c>.</summary>
public sealed class WorldReportTests
{
    private static readonly DateOnly Day = new(2026, 11, 22);
    private static readonly MapScale Field = new(100, 0, 0);

    private static WorldSpotter Spotter()
    {
        var spotter = new WorldSpotter();
        spotter.SetWanted(new WorldWanted { Npc = [1052611], Mob = [300], Fate = [1700] });
        return spotter;
    }

    [Theory]
    [InlineData(0f, 0, 100, 21.5)]
    [InlineData(-1024f, 0, 100, 1.0)]
    [InlineData(1024f, 0, 100, 42.0)]
    [InlineData(0f, 0, 200, 11.3)]
    [InlineData(-100f, 100, 100, 21.5)]
    public void World_positions_turn_into_the_map_coordinates_the_game_shows(float world, short offset, ushort size, double expected) =>
        Assert.Equal(expected, WorldSpotter.ToMap(world, offset, size));

    [Fact]
    public void Only_what_the_list_names_is_reported_once_per_zone_and_day()
    {
        var spotter = Spotter();

        var first = spotter.See(WorldKinds.Npc, 1052611, 4508, 0f, 0f, Field, Day);
        Assert.Null(spotter.See(WorldKinds.Npc, 1052611, 4508, 10f, 10f, Field, Day));
        Assert.Null(spotter.See(WorldKinds.Npc, 1000146, 4508, 0f, 0f, Field, Day));
        Assert.Null(spotter.See(WorldKinds.Mob, 1052611, 4508, 0f, 0f, Field, Day));

        Assert.Equal(new WorldReport { Kind = "npc", Id = 1052611, ZoneId = 4508, X = 21.5, Y = 21.5, Day = "2026-11-22" }, first);
        Assert.NotNull(spotter.See(WorldKinds.Npc, 1052611, 52, 0f, 0f, Field, Day));
        Assert.NotNull(spotter.See(WorldKinds.Npc, 1052611, 4508, 0f, 0f, Field, Day.AddDays(1)));
    }

    [Fact]
    public void A_spot_off_the_map_or_without_a_list_is_not_reported()
    {
        var spotter = Spotter();
        Assert.Null(spotter.See(WorldKinds.Mob, 300, 4508, 5000f, 0f, Field, Day));

        var empty = new WorldSpotter();
        Assert.False(empty.HasList);
        Assert.Null(empty.See(WorldKinds.Mob, 300, 4508, 0f, 0f, Field, Day));
    }

    [Fact]
    public void The_body_speaks_the_contract()
    {
        var request = new WorldReportRequest
        {
            InstallationId = "5f0c2a7e9b8d4c1fa36e0b2d7c9e4a18",
            GameVersion = "2026.11.13.0000.0000",
            Reports = [new WorldReport { Kind = "npc", Id = 1052611, ZoneId = 4508, X = 12.4, Y = 8.7, Day = "2026-11-06" }],
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, EorzeaJson.Options));
        var report = json.RootElement.GetProperty("reports")[0];
        Assert.Equal("npc", report.GetProperty("kind").GetString());
        Assert.Equal(1052611, report.GetProperty("id").GetInt32());
        Assert.Equal(4508, report.GetProperty("zone_id").GetInt32());
        Assert.Equal(12.4, report.GetProperty("x").GetDouble());
        Assert.Equal(8.7, report.GetProperty("y").GetDouble());
        Assert.Equal("2026-11-06", report.GetProperty("day").GetString());
    }

    [Fact]
    public void The_wanted_list_reads_as_written()
    {
        var list = JsonSerializer.Deserialize<WorldWantedResponse>(
            """{ "version": "3f2a9c0d1b7e4a55", "wanted": { "npc": [1000146, 1052611], "mob": [300, 4521], "fate": [1700] } }""",
            EorzeaJson.Options)!;

        Assert.Equal("3f2a9c0d1b7e4a55", list.Version);
        Assert.Equal([300, 4521], list.Wanted!.Mob);
    }

    [Fact]
    public async Task The_wanted_list_is_read_at_most_once_a_day_and_kept()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_test_key");
        var clock = new TestClock();
        var kept = new List<WorldWantedResponse>();
        var cache = new WorldWantedCache(api, tokens, clock, (list, _) => kept.Add(list));

        Assert.True(await cache.RefreshIfDueAsync(CancellationToken.None));
        Assert.False(await cache.RefreshIfDueAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromHours(23));
        Assert.False(await cache.RefreshIfDueAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(await cache.RefreshIfDueAsync(CancellationToken.None));

        Assert.Equal(2, api.WorldWantedReads);
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public async Task A_list_kept_from_before_waits_its_day_and_a_closed_intake_says_so()
    {
        var api = new FakeApiClient { WorldWantedResult = ApiResult<WorldWantedResponse>.Fail(new ApiError { Kind = ApiErrorKind.Conflict, StatusCode = 409, Message = "x" }) };
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_test_key");
        var clock = new TestClock();
        var cache = new WorldWantedCache(api, tokens, clock, (_, _) => { }, new WorldWantedResponse { Version = "v0" }, clock.UtcNow - TimeSpan.FromHours(2));

        Assert.False(await cache.RefreshIfDueAsync(CancellationToken.None));
        Assert.Equal(0, api.WorldWantedReads);

        clock.Advance(TimeSpan.FromHours(22));
        Assert.False(await cache.RefreshIfDueAsync(CancellationToken.None));
        Assert.True(cache.IntakeClosed);

        // A closed intake is looked at again within the hour, not after a day.
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(await cache.RefreshIfDueAsync(CancellationToken.None));
        Assert.Equal(1, api.WorldWantedReads);
        clock.Advance(TimeSpan.FromMinutes(1));
        await cache.RefreshIfDueAsync(CancellationToken.None);
        Assert.Equal(2, api.WorldWantedReads);

        cache.Forget();
        Assert.Null(cache.Current);
        Assert.False(cache.IntakeClosed);
    }
}
