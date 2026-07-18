using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Core.Tests;

/// <summary>
/// The farm endpoint annotates each BiS target with where the piece comes from. The annotation is
/// additive and every field is optional, so these pin down both a fully-described piece and the
/// unconfigured-tier case that must keep deserializing exactly as before.
/// </summary>
public sealed class FarmSourcingTests
{
    [Fact]
    public void ReadsSavageSourcing()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"Weapon":{"id":49668,"source":"savage",
          "cost":{"books":8,"token":"AAC Illustrated IV"},"floor":4,"zone":"AAC Cruiserweight"}}}]}
        """;

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var slot = res!.Data![0].Target!["Weapon"];
        Assert.Equal(49668, slot.Id);
        Assert.Equal("savage", slot.Source);
        Assert.Equal(8, slot.Cost!.Books);
        Assert.Equal("AAC Illustrated IV", slot.Cost.Token);
        Assert.Equal(4, slot.Floor);
        Assert.Equal("AAC Cruiserweight", slot.Zone);
    }

    [Fact]
    public void ReadsAugmentedTomeSourcingWithVendor()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"Body":{"id":123,"source":"tomeplus",
          "cost":{"books":825,"currency":"Aesthetics"},
          "upgrade":{"item":"Thundersteeped Twine","count":1},
          "vendor":{"name":"Nesvaaz","coords":{"x":12.3,"y":11.1}}}}}]}
        """;

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var slot = res!.Data![0].Target!["Body"];
        Assert.Equal("tomeplus", slot.Source);
        Assert.Equal(825, slot.Cost!.Books);
        Assert.Equal("Thundersteeped Twine", slot.Upgrade!.Item);
        Assert.Equal(1, slot.Upgrade.Count);
        Assert.Equal("Nesvaaz", slot.Vendor!.Name);
        Assert.Equal(12.3f, slot.Vendor.Coords!.X, 3);
        Assert.Equal(11.1f, slot.Vendor.Coords.Y, 3);
    }

    [Fact]
    public void UnannotatedTargetStillReads()
    {
        // An unconfigured tier leaves targets exactly as they were — bare item ids.
        const string body = """{"data":[{"job":"WHM","target":{"Head":{"id":42}}}]}""";

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var slot = res!.Data![0].Target!["Head"];
        Assert.Equal(42, slot.Id);
        Assert.Null(slot.Source);
        Assert.Null(slot.Cost);
        Assert.Null(slot.Vendor);
        Assert.Null(slot.Floor);
    }
}
