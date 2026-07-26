using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Core.Tests;

/// <summary>
/// The farm endpoint annotates each BiS target with the ways to get the piece — the server's unified
/// <c>{ id, source, routes[] }</c> shape (verified against the web's <c>TeamsTest</c> Weapon
/// assertions). These pin down a drop-plus-trade savage piece, an augment trade, and the
/// unconfigured-tier case that must keep deserializing exactly as before.
/// </summary>
public sealed class FarmSourcingTests
{
    [Fact]
    public void ReadsSavageDropAndTradeRoutes()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"Weapon":{
          "id":49668,"source":"savage","routes":[
            {"kind":"drop","via":{"id":49738,"name":"Grand Champion's Weapon Coffer"},
             "duties":["AAC Heavyweight M4 (Savage)"]},
            {"kind":"trade","cost":[{"id":49763,"name":"AAC Illustrated IV","count":8,"role":"token"}],
             "npc":[{"id":1049081,"name":"Hhihwi","zone":"Solution Nine","zone_id":1186,"map_id":890,"x":8.73,"y":13.43}]}
          ]}}}]}
        """;

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var slot = res!.Data![0].Target!["Weapon"];
        Assert.Equal(49668, slot.Id);
        Assert.Equal("savage", slot.Source);
        Assert.Equal(2, slot.Routes!.Count);

        var drop = slot.Routes[0];
        Assert.Equal("drop", drop.Kind);
        Assert.Equal("Grand Champion's Weapon Coffer", drop.Via!.Name);
        Assert.Equal(49738, drop.Via.Id);
        Assert.Equal("AAC Heavyweight M4 (Savage)", drop.Duties![0]);

        var trade = slot.Routes[1];
        Assert.Equal("trade", trade.Kind);
        Assert.Equal(8, trade.Cost![0].Count);
        Assert.Equal("token", trade.Cost[0].Role);
        Assert.Equal("AAC Illustrated IV", trade.Cost[0].Name);
        var npc = trade.Npc![0];
        Assert.Equal("Hhihwi", npc.Name);
        Assert.Equal("Solution Nine", npc.Zone);
        Assert.Equal(8.73f, npc.X!.Value, 2);
        Assert.Equal(1186, npc.ZoneId);
        Assert.Equal(890, npc.MapId);
        Assert.True(npc.CanMap);
    }

    [Fact]
    public void VendorWithoutMapRefCannotBeMapped()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"Head":{"id":1,"source":"tome","routes":[
          {"kind":"trade","cost":[{"id":9,"name":"Tomestone","count":495,"role":"currency"}],
           "npc":[{"id":2,"name":"Aymark","zone":"Solution Nine"}]}
        ]}}}]}
        """;

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var npc = res!.Data![0].Target!["Head"].Routes![0].Npc![0];
        Assert.Null(npc.MapId);
        Assert.False(npc.CanMap);
    }

    [Fact]
    public void ReadsAugmentTradeWithHandedInPiece()
    {
        const string body = """
        {"data":[{"job":"WHM","target":{"Weapon":{
          "id":49586,"source":"tomeplus","routes":[
            {"kind":"trade","cost":[
              {"slot":"Weapon","acq":"tome","role":"piece","count":1},
              {"id":49757,"name":"Thundersteeped Solvent","count":1,"role":"material"}
            ],"npc":[{"id":1,"name":"Theone","zone":"Radz-at-Han"}]}
          ]}}}]}
        """;

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var trade = res!.Data![0].Target!["Weapon"].Routes![0];
        Assert.Equal("tomeplus", res.Data[0].Target!["Weapon"].Source);
        Assert.Equal(2, trade.Cost!.Count);

        var piece = trade.Cost[0];
        Assert.Equal("piece", piece.Role);
        Assert.Equal("Weapon", piece.Slot);
        Assert.Equal("tome", piece.Acq);
        Assert.Null(piece.Id);

        var material = trade.Cost[1];
        Assert.Equal("material", material.Role);
        Assert.Equal("Thundersteeped Solvent", material.Name);
    }

    [Fact]
    public void UnannotatedTargetStillReads()
    {
        // An unconfigured tier leaves targets exactly as they were — bare item ids, no routes.
        const string body = """{"data":[{"job":"WHM","target":{"Head":{"id":42}}}]}""";

        var res = JsonSerializer.Deserialize<FarmResponse>(body, EorzeaJson.Options);

        var slot = res!.Data![0].Target!["Head"];
        Assert.Equal(42, slot.Id);
        Assert.Null(slot.Source);
        Assert.Null(slot.Routes);
    }
}
