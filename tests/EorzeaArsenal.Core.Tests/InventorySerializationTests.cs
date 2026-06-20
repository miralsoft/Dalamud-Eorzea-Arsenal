using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Locks the <c>POST /inventory</c> wire shape (snake_case keys, protocol v2) to the contract.</summary>
public sealed class InventorySerializationTests
{
    [Fact]
    public void Payload_serializes_to_the_contract_shape()
    {
        var payload = new InventoryPayload
        {
            Character = new CharacterDto { Name = "Sanaka", World = "Twintania", CidHash = TestData.ExampleHash },
            Scopes = [InventoryProtocol.ScopeCharacter, InventoryProtocol.RetainerScope("abc123")],
            Items =
            [
                new InventoryItemDto { ItemId = 49671, Container = "armoury", SourceId = string.Empty, Qty = 1, Hq = false },
                new InventoryItemDto { ItemId = 33333, Container = "retainer", SourceId = "abc123", Qty = 1, Hq = true },
            ],
        };

        var json = JsonSerializer.Serialize(payload, EorzeaJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("protocol_version").GetInt32());
        Assert.Equal(TestData.ExampleHash, root.GetProperty("character").GetProperty("cid_hash").GetString());

        var scopes = root.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("character", scopes);
        Assert.Contains("retainer:abc123", scopes);

        var first = root.GetProperty("items")[0];
        Assert.Equal(49671, first.GetProperty("item_id").GetInt32());
        Assert.Equal("armoury", first.GetProperty("container").GetString());
        Assert.Equal(string.Empty, first.GetProperty("source_id").GetString());
        Assert.Equal(1, first.GetProperty("qty").GetInt32());
        Assert.False(first.GetProperty("hq").GetBoolean());

        var second = root.GetProperty("items")[1];
        Assert.Equal("abc123", second.GetProperty("source_id").GetString());
        Assert.True(second.GetProperty("hq").GetBoolean());
    }

    [Fact]
    public void Result_parses_from_server_body()
    {
        const string body = """{ "status": "ok", "character_id": "42", "items": 137 }""";
        var result = JsonSerializer.Deserialize<InventoryPushResult>(body, EorzeaJson.Options);

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Status);
        Assert.Equal("42", result.CharacterId);
        Assert.Equal(137, result.Items);
    }
}
