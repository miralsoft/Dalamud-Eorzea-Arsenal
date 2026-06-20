using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Cleanup/aggregation rules for a scanned inventory snapshot.</summary>
public sealed class InventorySanitizerTests
{
    private static CharacterDto Character() =>
        new() { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash };

    private static InventoryData Data(IReadOnlyList<string> scopes, IReadOnlyList<InventoryItemDto> items) =>
        new() { Character = Character(), Scopes = scopes, Items = items };

    [Fact]
    public void Identical_items_are_aggregated_by_quantity()
    {
        var data = Data(
            [InventoryProtocol.ScopeCharacter],
            [
                new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Bags, Qty = 1 },
                new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Bags, Qty = 2 },
            ]);

        var clean = InventorySanitizer.Sanitize(data);

        var item = Assert.Single(clean.Items);
        Assert.Equal(3, item.Qty);
    }

    [Fact]
    public void Hq_and_nq_stay_separate()
    {
        var data = Data(
            [InventoryProtocol.ScopeCharacter],
            [
                new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Bags, Hq = false, Qty = 1 },
                new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Bags, Hq = true, Qty = 1 },
            ]);

        var clean = InventorySanitizer.Sanitize(data);

        Assert.Equal(2, clean.Items.Count);
    }

    [Fact]
    public void Manual_scope_and_its_items_are_dropped()
    {
        var data = Data(
            [InventoryProtocol.ScopeManual, InventoryProtocol.ScopeCharacter],
            [new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Bags }]);

        var clean = InventorySanitizer.Sanitize(data);

        Assert.DoesNotContain(InventoryProtocol.ScopeManual, clean.Scopes);
        Assert.Contains(InventoryProtocol.ScopeCharacter, clean.Scopes);
    }

    [Fact]
    public void Items_outside_scanned_scopes_are_dropped()
    {
        var data = Data(
            [InventoryProtocol.ScopeCharacter],
            [new InventoryItemDto { ItemId = 100, Container = InventoryContainers.Retainer, SourceId = "r1" }]);

        var clean = InventorySanitizer.Sanitize(data);

        Assert.Empty(clean.Items);
    }

    [Fact]
    public void Empty_scope_is_preserved_to_clear()
    {
        var data = Data([InventoryProtocol.RetainerScope("r1")], []);

        var clean = InventorySanitizer.Sanitize(data);

        Assert.Equal([InventoryProtocol.RetainerScope("r1")], clean.Scopes);
        Assert.Empty(clean.Items);
    }

    [Fact]
    public void Invalid_item_ids_are_dropped()
    {
        var data = Data(
            [InventoryProtocol.ScopeCharacter],
            [
                new InventoryItemDto { ItemId = 0, Container = InventoryContainers.Bags },
                new InventoryItemDto { ItemId = 49671, Container = InventoryContainers.Bags },
            ]);

        var clean = InventorySanitizer.Sanitize(data);

        var item = Assert.Single(clean.Items);
        Assert.Equal(49671, item.ItemId);
    }

    [Fact]
    public void Duplicate_scopes_are_collapsed()
    {
        var data = Data([InventoryProtocol.ScopeCharacter, InventoryProtocol.ScopeCharacter], []);

        var clean = InventorySanitizer.Sanitize(data);

        Assert.Single(clean.Scopes);
    }
}
