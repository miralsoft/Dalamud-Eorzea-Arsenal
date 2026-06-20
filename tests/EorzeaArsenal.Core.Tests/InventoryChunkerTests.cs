using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Chunking keeps requests under the server limits without ever splitting a scope.</summary>
public sealed class InventoryChunkerTests
{
    private static CharacterDto Character() =>
        new() { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash };

    [Fact]
    public void Small_snapshot_is_one_chunk()
    {
        var data = new InventoryData
        {
            Character = Character(),
            Scopes = [InventoryProtocol.ScopeCharacter],
            Items = [new InventoryItemDto { ItemId = 49671, Container = InventoryContainers.Bags }],
        };

        var chunks = InventoryChunker.Split(data);

        Assert.Single(chunks);
        Assert.Equal([InventoryProtocol.ScopeCharacter], chunks[0].Scopes);
        Assert.Single(chunks[0].Items);
    }

    [Fact]
    public void Many_retainer_scopes_split_under_the_scope_cap()
    {
        var count = InventoryProtocol.MaxScopes + 5;
        var scopes = Enumerable.Range(0, count).Select(i => InventoryProtocol.RetainerScope("r" + i)).ToList();
        var data = new InventoryData { Character = Character(), Scopes = scopes, Items = [] };

        var chunks = InventoryChunker.Split(data);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Scopes.Count <= InventoryProtocol.MaxScopes));

        // Every scope appears exactly once across all chunks (a scope is never split).
        var all = chunks.SelectMany(c => c.Scopes).ToList();
        Assert.Equal(count, all.Count);
        Assert.Equal(count, all.Distinct().Count());
    }

    [Fact]
    public void Items_stay_with_their_scope()
    {
        var scopes = new[] { InventoryProtocol.RetainerScope("r1"), InventoryProtocol.RetainerScope("r2") };
        var items = new[]
        {
            new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Retainer, SourceId = "r1" },
            new InventoryItemDto { ItemId = 200, Container = InventoryContainers.Retainer, SourceId = "r2" },
        };
        var data = new InventoryData { Character = Character(), Scopes = scopes, Items = items };

        var chunks = InventoryChunker.Split(data);

        foreach (var chunk in chunks)
        {
            foreach (var item in chunk.Items)
            {
                Assert.Contains(InventoryProtocol.ScopeForItem(item), chunk.Scopes);
            }
        }
    }

    [Fact]
    public void No_scopes_yields_no_chunks()
    {
        var data = new InventoryData { Character = Character(), Scopes = [], Items = [] };
        Assert.Empty(InventoryChunker.Split(data));
    }
}
