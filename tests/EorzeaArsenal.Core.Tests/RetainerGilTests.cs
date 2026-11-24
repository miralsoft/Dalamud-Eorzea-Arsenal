using System.Text.Json;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>A retainer's gil travels in its scope of the inventory sync (server 1.5, "The retainers' gil").</summary>
public sealed class RetainerGilTests
{
    private static CharacterDto Character() =>
        new() { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash };

    [Fact]
    public void The_gil_row_is_item_1_in_the_retainer_container_and_survives_cleanup_and_validation()
    {
        var scope = InventoryProtocol.RetainerScope("33000001");
        var gear = new InventoryItemDto { ItemId = 49757, Container = InventoryContainers.Retainer, SourceId = "33000001" };
        var gil = InventoryProtocol.RetainerGilRow("33000001", 2_500_000)!;

        var clean = InventorySanitizer.Sanitize(new InventoryData { Character = Character(), Scopes = [scope], Items = [gear, gil] });

        var row = Assert.Single(clean.Items, i => i.ItemId == InventoryProtocol.GilItemId);
        Assert.Equal(2_500_000, row.Qty);
        Assert.Empty(InventoryValidator.Validate(InventoryPayload.From(clean)).Errors);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, EorzeaJson.Options));
        Assert.Equal(1, json.RootElement.GetProperty("item_id").GetInt32());
        Assert.Equal("retainer", json.RootElement.GetProperty("container").GetString());
        Assert.Equal("33000001", json.RootElement.GetProperty("source_id").GetString());
        Assert.Equal(2_500_000, json.RootElement.GetProperty("qty").GetInt32());
        Assert.False(json.RootElement.GetProperty("hq").GetBoolean());
    }

    [Fact]
    public void No_gil_sends_no_row_and_the_ceiling_is_the_games()
    {
        Assert.Null(InventoryProtocol.RetainerGilRow("1", 0));
        Assert.Equal(InventoryProtocol.MaxGil, InventoryProtocol.RetainerGilRow("1", uint.MaxValue)!.Qty);
    }
}
