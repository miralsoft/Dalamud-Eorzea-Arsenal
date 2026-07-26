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

    /// <summary>
    /// The saddlebag reconciles on its own, because it reads as <i>empty</i> rather than
    /// <i>unavailable</i> until the player has opened it. Folding it into the character scope is what
    /// let an unread bag delete its own contents server-side.
    /// </summary>
    [Fact]
    public void Saddlebag_items_belong_to_their_own_scope()
    {
        var saddlebag = new InventoryItemDto { ItemId = 49757, Container = InventoryContainers.Saddlebag };
        var bags = new InventoryItemDto { ItemId = 49757, Container = InventoryContainers.Bags };

        Assert.Equal(InventoryProtocol.ScopeSaddlebag, InventoryProtocol.ScopeForItem(saddlebag));
        Assert.Equal(InventoryProtocol.ScopeCharacter, InventoryProtocol.ScopeForItem(bags));
        Assert.DoesNotContain(InventoryContainers.Saddlebag, InventoryContainers.CharacterContainers);

        // Declaring only `character` therefore drops saddlebag items rather than clearing the bag.
        var clean = InventorySanitizer.Sanitize(Data([InventoryProtocol.ScopeCharacter], [saddlebag, bags]));

        Assert.Single(clean.Items);
        Assert.Equal(InventoryContainers.Bags, clean.Items[0].Container);
        Assert.DoesNotContain(InventoryProtocol.ScopeSaddlebag, clean.Scopes);
    }

    /// <summary>
    /// The glamour dresser has the saddlebag's problem and no "loaded" flag to check, so it too
    /// reconciles on its own. The armoire stays in the character scope — it reads from a structure
    /// that is always there.
    /// </summary>
    [Fact]
    public void The_dresser_reconciles_on_its_own_but_the_armoire_does_not()
    {
        var dresser = new InventoryItemDto { ItemId = 49671, Container = InventoryContainers.Glamour };
        var armoire = new InventoryItemDto { ItemId = 49672, Container = InventoryContainers.Armoire };

        Assert.Equal(InventoryProtocol.ScopeGlamour, InventoryProtocol.ScopeForItem(dresser));
        Assert.Equal(InventoryProtocol.ScopeCharacter, InventoryProtocol.ScopeForItem(armoire));
        Assert.DoesNotContain(InventoryContainers.Glamour, InventoryContainers.CharacterContainers);
        Assert.Contains(InventoryContainers.Armoire, InventoryContainers.CharacterContainers);

        // An undeclared dresser drops its items rather than clearing the stored ones.
        var undeclared = InventorySanitizer.Sanitize(Data([InventoryProtocol.ScopeCharacter], [dresser, armoire]));
        Assert.Single(undeclared.Items);
        Assert.Equal(InventoryContainers.Armoire, undeclared.Items[0].Container);

        // A declared one is sent and passes validation.
        var declared = InventorySanitizer.Sanitize(Data(
            [InventoryProtocol.ScopeCharacter, InventoryProtocol.ScopeGlamour], [dresser, armoire]));
        Assert.Equal(2, declared.Items.Count);
        Assert.Empty(InventoryValidator.Validate(InventoryPayload.From(declared)).Errors);
    }

    /// <summary>A scanned saddlebag is declared and kept — that is the whole point of the split.</summary>
    [Fact]
    public void A_declared_saddlebag_scope_keeps_its_items()
    {
        var clean = InventorySanitizer.Sanitize(Data(
            [InventoryProtocol.ScopeCharacter, InventoryProtocol.ScopeSaddlebag],
            [new InventoryItemDto { ItemId = 49757, Container = InventoryContainers.Saddlebag, Qty = 3 }]));

        Assert.Single(clean.Items);
        Assert.Equal(3, clean.Items[0].Qty);
        Assert.Contains(InventoryProtocol.ScopeSaddlebag, clean.Scopes);
        Assert.Empty(InventoryValidator.Validate(InventoryPayload.From(clean)).Errors);
    }

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
