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
