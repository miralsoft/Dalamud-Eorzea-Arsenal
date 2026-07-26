using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The holdings read side: the wire shapes of <c>/me/holdings</c>, <c>/gear/tracked-items</c> and the
/// new <c>ids</c> on a hand-in cost, plus the caching / invalidation of <see cref="HoldingsService"/>.
/// </summary>
public sealed class HoldingsServiceTests
{
    [Fact]
    public void DeserializesHoldingsAndTrackedItems()
    {
        var holdings = JsonSerializer.Deserialize<HoldingsResponse>("""{"data":{"49757":2,"49763":1109,"999":0}}""", EorzeaJson.Options);
        Assert.Equal(2, holdings!.Data!["49757"]);
        Assert.Equal(1109, holdings.Data["49763"]);

        var tracked = JsonSerializer.Deserialize<TrackedItemsResponse>("""{"data":[49757,49763,44549]}""", EorzeaJson.Options);
        Assert.Equal([49757L, 49763L, 44549L], tracked!.Data);
    }

    /// <summary>
    /// The server sends the game's instance ids beside the English duty names, but omits any it could
    /// not resolve — so the two lists only correspond when there is one id per name. Anything else has
    /// to fall back, or a fight gets labelled with another fight's name.
    /// </summary>
    [Fact]
    public void DutyContentIdsAreOptionalAndNotIndexAligned()
    {
        var aligned = JsonSerializer.Deserialize<FarmRoute>(
            """{"kind":"drop","duties":["AAC Heavyweight M3 (Savage)"],"duty_content_ids":[30159]}""",
            EorzeaJson.Options)!;
        Assert.Equal([30159L], aligned.DutyContentIds);
        Assert.Equal(aligned.Duties!.Count, aligned.DutyContentIds!.Count);

        // Two fights, one resolvable id: the lists no longer correspond and must not be paired.
        var partial = JsonSerializer.Deserialize<FarmRoute>(
            """{"kind":"drop","duties":["A (Savage)","B (Savage)"],"duty_content_ids":[30159]}""",
            EorzeaJson.Options)!;
        Assert.NotEqual(partial.Duties!.Count, partial.DutyContentIds!.Count);

        // An older server sends no ids at all, which must stay harmless.
        var legacy = JsonSerializer.Deserialize<FarmRoute>(
            """{"kind":"drop","duties":["A (Savage)"]}""", EorzeaJson.Options)!;
        Assert.Null(legacy.DutyContentIds);
    }

    [Fact]
    public void PieceCostCarriesBaseIds()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"RingRight":{"id":49586,"source":"tomeplus","routes":[
          {"kind":"trade","cost":[
            {"slot":"RingRight","acq":"tome","role":"piece","count":1,"ids":[49700,49701],"id":49700,"name":"Kingsmelt Ring"}
          ]}]}}}]}
        """;

        var piece = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options)!
            .Data![0].Target!["RingRight"].Routes![0].Cost![0];

        Assert.Equal("piece", piece.Role);
        Assert.Equal([49700L, 49701L], piece.Ids);
        Assert.Equal(49700, piece.Id);
    }

    [Fact]
    public async Task CachesUntilInvalidated()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        api.HoldingsResult = ApiResult<HoldingsResponse>.Ok(new HoldingsResponse { Data = new() { ["10"] = 5 } });
        var service = new HoldingsService(api, tokens, NullLog.Instance);

        await service.PrefetchAsync([10L], CancellationToken.None);
        Assert.True(service.TryGet(10, out var count));
        Assert.Equal(5, count);

        // Cached → a second prefetch does not call again.
        await service.PrefetchAsync([10L], CancellationToken.None);
        Assert.Single(api.HoldingsRequests);

        // After an inventory sync the counts are stale → invalidate re-reads them.
        service.Invalidate();
        Assert.False(service.TryGet(10, out _));
        await service.PrefetchAsync([10L], CancellationToken.None);
        Assert.Equal(2, api.HoldingsRequests.Count);
    }

    /// <summary>
    /// The breakdown answers "where do I go to get it", and the retainer's name is the only useful
    /// label for a stack sitting on one — a numeric retainer id tells the player nothing.
    /// </summary>
    [Fact]
    public async Task BreakdownIsCachedLargestStackFirst()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        api.HoldingsResult = ApiResult<HoldingsResponse>.Ok(new HoldingsResponse
        {
            Data = new() { ["49757"] = 6 },
            Breakdown = new()
            {
                ["49757"] = [
                    new HoldingStack { Scope = "character", Container = "saddlebag", Qty = 1 },
                    new HoldingStack { Scope = "retainer:337", Container = "retainer", SourceId = "337", SourceName = "Nanamo", Qty = 5 },
                ],
            },
        });
        var service = new HoldingsService(api, tokens, NullLog.Instance, () => 42);

        await service.PrefetchAsync([49757L], CancellationToken.None);

        Assert.True(service.TryGetBreakdown(49757, out var stacks));
        Assert.Equal(5, stacks[0].Qty);                 // biggest stack first — that is where to go
        Assert.Equal("Nanamo", stacks[0].SourceName);
        Assert.Equal("saddlebag", stacks[1].Container);

        // The count must be for the character on screen, not whichever one the account made active.
        Assert.Equal(42, api.HoldingsCharacterIds[0]);

        service.Invalidate();
        Assert.False(service.TryGetBreakdown(49757, out _));
    }

    [Fact]
    public async Task WithoutAKeyNothingIsRequested()
    {
        var api = new FakeApiClient();
        var service = new HoldingsService(api, new InMemoryTokenStore(), NullLog.Instance);

        await service.PrefetchAsync([1L, 2L], CancellationToken.None);

        Assert.Empty(api.HoldingsRequests);
        Assert.False(service.TryGet(1, out _));
    }

    [Fact]
    public void TomeBalanceRoundTrips()
    {
        var body = JsonSerializer.Serialize(new TomeBalanceRequest { Balance = 830, CharacterId = 1234 }, EorzeaJson.Options);
        Assert.Contains("\"balance\":830", body);
        Assert.Contains("\"character_id\":1234", body);

        var res = JsonSerializer.Deserialize<TomeBalanceResponse>("""{"data":{"balance":830}}""", EorzeaJson.Options);
        Assert.Equal(830, res!.Data!.Balance);

        var unset = JsonSerializer.Deserialize<TomeBalanceResponse>("""{"data":{"balance":null}}""", EorzeaJson.Options);
        Assert.Null(unset!.Data!.Balance);
    }

    /// <summary>
    /// <c>data</c> (what to sync, every tier) and <c>groups</c> (what to display, active tier only)
    /// are separate lists — a server that sends no groups just means no stock view.
    /// </summary>
    [Fact]
    public void DeserializesTrackedItemGroups()
    {
        const string body = """
        {"data":[49757,49756,49760],
         "groups":[{"kind":"material","ids":[49757,49758]},{"kind":"stone","ids":[49756]},
                   {"kind":"book","ids":[49760,49761]}]}
        """;

        var tracked = JsonSerializer.Deserialize<TrackedItemsResponse>(body, EorzeaJson.Options)!;

        Assert.Equal(3, tracked.Groups!.Count);
        Assert.Equal("material", tracked.Groups[0].Kind);
        Assert.Equal([49757L, 49758L], tracked.Groups[0].Ids);
        Assert.Equal("book", tracked.Groups[2].Kind);

        var legacy = JsonSerializer.Deserialize<TrackedItemsResponse>("""{"data":[1]}""", EorzeaJson.Options)!;
        Assert.Null(legacy.Groups);
    }

    [Fact]
    public void TrackedItemsStoreKeepsGroupOrderAndDropsEmptyOnes()
    {
        var store = new TrackedItemsStore();
        Assert.Empty(store.Groups);

        store.SetGroups(
        [
            new TrackedItemGroup { Kind = "material", Ids = [49757, -1, 49758] },
            new TrackedItemGroup { Kind = "stone", Ids = [] },      // no usable ids → no empty heading
            new TrackedItemGroup { Kind = null, Ids = [49760] },     // no kind → cannot be labelled
            new TrackedItemGroup { Kind = "book", Ids = [49760] },
        ]);

        Assert.Equal(2, store.Groups.Count);
        Assert.Equal("material", store.Groups[0].Kind);          // server order preserved
        Assert.Equal([49757, 49758], store.Groups[0].ItemIds);
        Assert.Equal("book", store.Groups[1].Kind);

        store.SetGroups(null);
        Assert.Empty(store.Groups);
    }

    [Fact]
    public void TrackedItemsStoreFiltersAndSwaps()
    {
        var store = new TrackedItemsStore();
        Assert.False(store.HasAny);

        store.Set([49757L, -1L, 0L, 44549L]);
        Assert.True(store.HasAny);
        Assert.True(store.Contains(49757));
        Assert.True(store.Contains(44549));
        Assert.False(store.Contains(-1));

        store.Set([]);
        Assert.False(store.HasAny);
        Assert.False(store.Contains(49757));
    }
}
