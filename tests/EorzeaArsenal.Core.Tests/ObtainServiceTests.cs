using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The obtain read side: the wire shape of <c>GET /gear/obtain</c> and the caching/dedup/chunking of
/// <see cref="ObtainService"/>.
/// </summary>
public sealed class ObtainServiceTests
{
    [Fact]
    public void DeserializesKeyedObtainResponse()
    {
        const string body = """
        {"data":{
          "49586":{"source":"tomeplus","slot":"Weapon","routes":[
            {"kind":"trade","cost":[
              {"slot":"Weapon","acq":"tome","role":"piece","count":1,
               "chain":[{"kind":"trade","cost":[{"id":9,"name":"Tomestone","count":825,"role":"currency"}]}]},
              {"id":49757,"name":"Thundersteeped Solvent","count":1,"role":"material"}
            ],"npc":[{"id":1,"name":"Theone","zone":"Radz-at-Han","x":10.6,"y":9.9}]}
          ]},
          "999":null
        }}
        """;

        var res = JsonSerializer.Deserialize<ObtainResponse>(body, EorzeaJson.Options);

        Assert.Null(res!.Data!["999"]);
        var info = res.Data["49586"]!;
        Assert.Equal("tomeplus", info.Source);
        Assert.Equal("Weapon", info.Slot);
        var piece = info.Routes![0].Cost![0];
        Assert.Equal("piece", piece.Role);
        Assert.Equal(825, piece.Chain![0].Cost![0].Count);
    }

    [Fact]
    public async Task CachesAndDeduplicatesAndSplitsIntoChunks()
    {
        var api = new FakeApiClient();
        var tokens = Connected("key");
        var data = new Dictionary<string, ObtainInfo?>();
        for (var id = 1; id <= 70; id++)
        {
            data[id.ToString()] = new ObtainInfo { Source = "savage" };
        }

        api.ObtainResult = ApiResult<ObtainResponse>.Ok(new ObtainResponse { Data = data });
        var service = new ObtainService(api, tokens, NullLog.Instance);

        await service.PrefetchAsync(Enumerable.Range(1, 70).Select(i => (long)i), CancellationToken.None);

        // 70 ids > the 60/call cap → two calls, and every id is resolved.
        Assert.Equal(2, api.ObtainRequests.Count);
        Assert.Equal([60, 10], api.ObtainRequests.Select(r => r.Length).ToList());
        Assert.True(service.TryGet(1, out var one));
        Assert.Equal("savage", one!.Source);

        // A second prefetch of the same ids hits the cache: no further call.
        await service.PrefetchAsync(Enumerable.Range(1, 70).Select(i => (long)i), CancellationToken.None);
        Assert.Equal(2, api.ObtainRequests.Count);
    }

    [Fact]
    public async Task ARequestedButUnreturnedIdBecomesNoInfoAndIsNotRefetched()
    {
        var api = new FakeApiClient
        {
            // The server answers but says nothing about id 5.
            ObtainResult = ApiResult<ObtainResponse>.Ok(new ObtainResponse { Data = new() }),
        };
        var service = new ObtainService(api, Connected("key"), NullLog.Instance);

        await service.PrefetchAsync([5L], CancellationToken.None);

        Assert.True(service.TryGet(5, out var info));
        Assert.Null(info);
        Assert.Single(api.ObtainRequests);

        await service.PrefetchAsync([5L], CancellationToken.None);
        Assert.Single(api.ObtainRequests);
    }

    [Fact]
    public async Task WithoutAKeyNothingIsRequested()
    {
        var api = new FakeApiClient();
        var service = new ObtainService(api, new InMemoryTokenStore(), NullLog.Instance);

        await service.PrefetchAsync([1L, 2L], CancellationToken.None);

        Assert.Empty(api.ObtainRequests);
        Assert.False(service.TryGet(1, out _));
    }

    private static InMemoryTokenStore Connected(string key)
    {
        var store = new InMemoryTokenStore();
        store.SetApiKey(key);
        return store;
    }
}
