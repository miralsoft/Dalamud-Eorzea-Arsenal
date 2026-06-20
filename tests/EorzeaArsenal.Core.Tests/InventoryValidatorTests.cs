using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Client-side bounds checks for the inventory payload (R18, mirrors the server).</summary>
public sealed class InventoryValidatorTests
{
    private static CharacterDto Character() =>
        new() { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash };

    private static InventoryPayload Payload(IReadOnlyList<string> scopes, IReadOnlyList<InventoryItemDto> items) =>
        new() { Character = Character(), Scopes = scopes, Items = items };

    [Fact]
    public void Valid_character_scope_passes()
    {
        var payload = Payload(
            [InventoryProtocol.ScopeCharacter],
            [new InventoryItemDto { ItemId = 49671, Container = InventoryContainers.Armoury, Qty = 1 }]);

        Assert.True(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Empty_scope_list_is_invalid()
    {
        var payload = Payload([], []);
        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Manual_scope_is_rejected()
    {
        var payload = Payload([InventoryProtocol.ScopeManual], []);
        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Empty_character_scope_is_valid_to_clear()
    {
        // A reported-but-empty scope is how a sold-out storage is cleared — must validate.
        var payload = Payload([InventoryProtocol.ScopeCharacter], []);
        Assert.True(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Retainer_item_without_source_id_is_invalid()
    {
        var payload = Payload(
            [InventoryProtocol.RetainerScope("abc")],
            [new InventoryItemDto { ItemId = 33333, Container = InventoryContainers.Retainer, SourceId = string.Empty }]);

        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Item_in_unscanned_scope_is_invalid()
    {
        // Item resolves to retainer:xyz but only the character scope is claimed.
        var payload = Payload(
            [InventoryProtocol.ScopeCharacter],
            [new InventoryItemDto { ItemId = 33333, Container = InventoryContainers.Retainer, SourceId = "xyz" }]);

        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Out_of_range_item_id_is_invalid()
    {
        var payload = Payload(
            [InventoryProtocol.ScopeCharacter],
            [new InventoryItemDto { ItemId = 0, Container = InventoryContainers.Bags }]);

        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Too_many_scopes_is_invalid()
    {
        var scopes = Enumerable.Range(0, InventoryProtocol.MaxScopes + 1)
            .Select(i => InventoryProtocol.RetainerScope("r" + i))
            .ToList();
        var payload = Payload(scopes, []);

        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }

    [Fact]
    public void Bad_cid_hash_is_invalid()
    {
        var payload = new InventoryPayload
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = "not-a-hash" },
            Scopes = [InventoryProtocol.ScopeCharacter],
            Items = [],
        };

        Assert.False(InventoryValidator.Validate(payload).IsValid);
    }
}
