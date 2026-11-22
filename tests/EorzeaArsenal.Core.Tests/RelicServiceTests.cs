using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The relic helper of server 1.5, held to <c>contract-relics.md</c>.</summary>
public sealed class RelicServiceTests
{
    private const string CatalogJson = """
        { "data": { "series": [
          { "key": "phantom", "kind": "combat", "expansion": "dt", "name": "Phantomwaffen",
            "jobs": ["PLD", "WAR", "VPR"],
            "stages": [
              { "key": "penumbrae", "ilvl": 745,
                "items": { "PLD": [ { "id": 47869, "name": "Schwert" }, { "id": 47890, "name": "Schild" } ],
                           "WAR": [ { "id": 47871, "name": "Axt" } ] } },
              { "key": "obscurum", "ilvl": 755,
                "items": { "PLD": [ { "id": 47900, "name": "Schwert II" }, { "id": 47901, "name": "Schild II" } ],
                           "WAR": [ { "id": 47902, "name": "Axt II" } ] } },
              { "key": "occultum", "ilvl": 765,
                "items": { "PLD": [ { "id": 47910, "name": "Schwert III" }, { "id": 47911, "name": "Schild III" } ],
                           "WAR": [ { "id": 47912, "name": "Axt III" } ] } }
            ] } ] } }
        """;

    private const string MineJson = """
        { "data": { "character_id": "1234", "series": [
          { "key": "phantom", "done": 1, "of": 2, "jobs": {
              "WAR": { "held": "occultum", "ticked": null, "reached": "occultum", "next": null, "done": true },
              "PLD": { "held": "penumbrae", "ticked": "obscurum", "reached": "obscurum", "next": "occultum", "done": false } } }
        ], "inventory_synced": true } }
        """;

    private static RelicCatalog Catalog() => JsonSerializer.Deserialize<RelicCatalogResponse>(CatalogJson, EorzeaJson.Options)!.Data!;

    private static MyRelics Mine() => JsonSerializer.Deserialize<MyRelicsResponse>(MineJson, EorzeaJson.Options)!.Data!;

    [Fact]
    public void The_answers_of_the_contract_read_as_written()
    {
        var series = Assert.Single(Catalog().Series);
        Assert.Equal("combat", series.Kind);
        Assert.Equal(2, series.Stages[0].Items["PLD"].Count);
        Assert.False(series.Stages[0].Items.ContainsKey("VPR"));

        var mine = Mine();
        Assert.Equal("1234", mine.CharacterId);
        Assert.True(mine.InventorySynced);
        Assert.Equal("obscurum", mine.Series[0].Jobs["PLD"].Ticked);
    }

    [Fact]
    public void Held_stages_ticked_stages_and_missing_ones_are_told_apart()
    {
        var rows = RelicService.Rows(Catalog().Series[0], Mine().Series[0]);

        var pld = rows.Single(r => r.Job == "PLD");
        Assert.Equal([RelicMark.Held, RelicMark.Ticked, RelicMark.Missing], pld.Stages.Select(s => s.Mark));
        Assert.Equal("occultum", pld.Next!.Key);
        Assert.Equal(["Schwert III", "Schild III"], pld.Next.Items.Select(i => i.Name));
        Assert.Equal("obscurum", pld.Ticked);
        Assert.False(pld.Done);

        var war = rows.Single(r => r.Job == "WAR");
        Assert.All(war.Stages, s => Assert.Equal(RelicMark.Held, s.Mark));
        Assert.Null(war.Next);
        Assert.True(war.Done);
    }

    [Fact]
    public void A_job_without_a_piece_in_any_stage_has_no_row_and_nothing_known_reads_as_missing()
    {
        var rows = RelicService.Rows(Catalog().Series[0], mine: null);

        Assert.DoesNotContain(rows, r => r.Job == "VPR");
        Assert.All(rows.SelectMany(r => r.Stages), s => Assert.Equal(RelicMark.Missing, s.Mark));
        Assert.All(rows, r => Assert.Null(r.Next));
    }

    [Fact]
    public async Task The_catalog_is_read_once_per_language_and_the_progress_whenever_it_is_due()
    {
        var api = new FakeApiClient
        {
            RelicCatalogResult = ApiResult<RelicCatalogResponse>.Ok(new RelicCatalogResponse { Data = Catalog() }),
            MyRelicsResult = ApiResult<MyRelicsResponse>.Ok(new MyRelicsResponse { Data = Mine() }),
        };
        var service = Service(api);

        Assert.True(service.NeedsRead("de", 1234));
        await service.RefreshAsync("de", 1234, CancellationToken.None);
        Assert.False(service.NeedsRead("de", 1234));
        Assert.NotNull(service.CatalogFor("de"));
        Assert.Null(service.CatalogFor("en"));

        service.Invalidate();
        Assert.True(service.NeedsRead("de", 1234));
        await service.RefreshAsync("de", 1234, CancellationToken.None);

        Assert.Equal(["de"], api.RelicCatalogReads);
        Assert.Equal([1234L, 1234L], api.MyRelicsReads.Select(id => id!.Value));

        Assert.True(service.NeedsRead("de", 5678));
        Assert.True(service.NeedsRead("en", 1234));
    }

    [Fact]
    public async Task A_tick_sends_the_stage_and_clearing_sends_an_empty_one()
    {
        var api = new FakeApiClient { RelicTickResult = ApiResult<MyRelicsResponse>.Ok(new MyRelicsResponse { Data = Mine() }) };
        var service = Service(api);

        Assert.Null(await service.TickAsync(1234, "phantom", "PLD", "obscurum", CancellationToken.None));
        Assert.Null(await service.TickAsync(1234, "phantom", "PLD", null, CancellationToken.None));

        Assert.Equal("obscurum", api.RelicTicks[0].Stage);
        Assert.Equal(string.Empty, api.RelicTicks[1].Stage);
        Assert.NotNull(service.Mine);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(api.RelicTicks[1], EorzeaJson.Options));
        Assert.Equal(string.Empty, json.RootElement.GetProperty("stage").GetString());
        Assert.Equal(1234, json.RootElement.GetProperty("character_id").GetInt64());
    }

    [Fact]
    public async Task A_failed_read_is_kept_as_the_error_and_keeps_what_was_held()
    {
        var api = new FakeApiClient
        {
            RelicCatalogResult = ApiResult<RelicCatalogResponse>.Ok(new RelicCatalogResponse { Data = Catalog() }),
            MyRelicsResult = ApiResult<MyRelicsResponse>.Ok(new MyRelicsResponse { Data = Mine() }),
        };
        var service = Service(api);
        await service.RefreshAsync("de", 1234, CancellationToken.None);

        api.MyRelicsResult = ApiResult<MyRelicsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, StatusCode = 403, Message = "x" });
        service.Invalidate();
        await service.RefreshAsync("de", 1234, CancellationToken.None);

        Assert.Equal(ApiErrorKind.Forbidden, service.LastError?.Kind);
        Assert.NotNull(service.Mine);
    }

    private static RelicService Service(FakeApiClient api)
    {
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_test_key");
        return new RelicService(api, tokens, new StubSettings("https://dev.example.test/api/v1"));
    }

    private sealed class StubSettings(string baseUrl) : IApiSettings
    {
        public string BaseUrl { get; } = baseUrl;
    }
}
