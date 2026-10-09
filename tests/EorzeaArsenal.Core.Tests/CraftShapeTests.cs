using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The wire shapes of revision 21: a crafter row of <c>GET /gear/bis</c> with its <c>craft</c> block and
/// the answer's <c>craft_tables</c>, and <c>hq</c> on every pushed piece. The row is the contract's own
/// example; no recorded answer exists yet, so this is the shape the server code writes
/// (<c>CraftTargets::resolve</c>, <c>meta</c> and <c>tables</c>).
/// </summary>
public sealed class CraftShapeTests
{
    private const string Answer = """
        {
          "protocol_version": 1,
          "data": [
            { "cid_hash": "abc", "character_id": "11", "job": "ALC", "set_uid": "0123456789abcdef0123456789abcdef",
              "gear_index": 7, "name": "High · Teamcraft 7.51 with newer pieces from the game",
              "target": "craft:game/ALC/high", "target_name": "High · Teamcraft 7.51 with newer pieces from the game",
              "items": {
                "Body": { "id": 47185, "materia": [41778, 41778, 41778, 41765, 41767], "hq": true, "source": "crafted" },
                "Ears": { "id": 47194, "materia": [41780], "hq": false, "source": "scrip" }
              },
              "craft": { "level": 3, "level_name": "High",
                         "source": { "id": "game", "name": "Game data", "url": null },
                         "set_url": "https://ffxivteamcraft.com/gearset/2Y6valakmCuKP9Frxt0c", "patch": "7.51",
                         "family": "crafter", "totals": { "70": 6010, "71": 5520, "11": 649 },
                         "based_on": { "source": { "id": "teamcraft", "name": "Teamcraft", "url": "https://guides.ffxivteamcraft.com/" },
                                       "key": "high", "patch": "7.51" },
                         "swapped": ["Body", "Hands"] } },
            { "job": "DRK", "gear_index": 0, "target": "drk/current", "items": { "Weapon": { "id": 49668, "materia": [] } } }
          ],
          "craft_tables": {
            "items": { "47185": { "slot": "Body", "ilvl": 710, "canHq": true, "slots": 2, "adv": true,
                                  "nq": { "70": 1301 }, "hqv": { "70": 173 }, "cap": { "70": 1734 } } },
            "materia": { "41778": { "param": 70, "grade": 12, "value": 33 } }
          }
        }
        """;

    [Fact]
    public void ACrafterRowReadsWithItsBlockAndTheTables()
    {
        var answer = JsonSerializer.Deserialize<BisResponse>(Answer, EorzeaJson.Options)!;

        var row = answer.Data[0];
        Assert.True(row.IsCraft);
        Assert.Equal(3, row.Craft!.Level);
        Assert.Equal("crafter", row.Craft.Family);
        Assert.Equal(6010, row.Craft.Totals["70"]);
        Assert.Equal(["Body", "Hands"], row.Craft.Swapped);
        Assert.True(row.Items["Body"].Hq);
        Assert.Equal("scrip", row.Items["Ears"].Source);

        Assert.False(answer.Data[1].IsCraft);
        Assert.Null(answer.Data[1].Items["Weapon"].Hq);

        Assert.Equal(710, answer.CraftTables!.Items["47185"].Ilvl);
        Assert.True(answer.CraftTables.Items["47185"].CanHq);
        Assert.Equal(33, answer.CraftTables.Materia["41778"].Value);
    }

    /// <summary>
    /// A set with newer pieces from the game says "game" as its source; the attribution is the source of
    /// the set it was built from, with its name and link.
    /// </summary>
    [Fact]
    public void AGameSetIsAttributedToTheSetItWasBuiltFrom()
    {
        var row = JsonSerializer.Deserialize<BisResponse>(Answer, EorzeaJson.Options)!.Data[0];

        Assert.Equal("Teamcraft", row.Craft!.Attribution!.Name);
        Assert.Equal("https://guides.ffxivteamcraft.com/", row.Craft.Attribution.Url);
    }

    [Fact]
    public void ASetFromItsOwnSourceIsAttributedToIt()
    {
        var block = new CraftBlock { Source = new CraftSource { Id = "teamcraft", Name = "Teamcraft" } };

        Assert.Equal("Teamcraft", block.Attribution!.Name);
    }

    /// <summary>A combat-only answer carries no tables, and nothing about it changes.</summary>
    [Fact]
    public void ACombatAnswerHasNoTables()
    {
        var answer = JsonSerializer.Deserialize<BisResponse>(
            """{"protocol_version":1,"data":[{"job":"DRK","gear_index":0,"items":{}}]}""", EorzeaJson.Options)!;

        Assert.Null(answer.CraftTables);
        Assert.False(answer.Data[0].IsCraft);
    }

    /// <summary>
    /// <c>hq</c> goes out on every piece where it is known, <c>false</c> included: a missing field means
    /// "unknown" to the server, and leaving out the NQ pieces would turn every one of them into "HQ?".
    /// </summary>
    [Fact]
    public void ThePushCarriesHqTrueAndFalse()
    {
        var snapshot = TestData.Snapshot(TestData.ExampleHash);
        var set = snapshot.Gearsets[0];
        var withHq = new GearsetDto
        {
            GearIndex = set.GearIndex,
            Job = set.Job,
            Items = new()
            {
                ["Body"] = new ItemDto { Id = 47185, Hq = true },
                ["Ears"] = new ItemDto { Id = 47194, Hq = false },
            },
        };
        var payload = GearPayload.From(new GearData { Character = snapshot.Character, Gearsets = [withHq] }, JobScope.All);

        var json = JsonSerializer.Serialize(payload, EorzeaJson.Options);

        Assert.Contains("\"Body\":{\"id\":47185,\"materia\":[],\"hq\":true}", json, StringComparison.Ordinal);
        Assert.Contains("\"Ears\":{\"id\":47194,\"materia\":[],\"hq\":false}", json, StringComparison.Ordinal);
    }
}
