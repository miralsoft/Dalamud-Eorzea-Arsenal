using System.Text.Json;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// A crafter or gatherer target in the comparison: its materia judged as the web does, and HQ where the
/// target asks for it. The rows come from the shared sample, so the numbers are the game's.
/// </summary>
public sealed class CraftComparisonTests
{
    private const int Body = 47185;

    private static readonly CraftTables Tables = JsonSerializer.Deserialize<CraftTables>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "meld-match-cases.json")),
        EorzeaJson.Options)!;

    /// <summary>The guide's materia in another order, and a weaker CP materia that still reaches the cap.</summary>
    [Fact]
    public void ADifferentOrderAndACappedWeakerMateriaAreDone()
    {
        var target = Craft(new ItemDto { Id = Body, Materia = [41778, 41778, 41779], Hq = true });

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41766, 41778, 41778], Hq = true });

        Assert.True(slot.IsComplete);
        Assert.Empty(slot.MissingMateria);
        Assert.Empty(slot.ExtraMateria);
        Assert.Equal(3, slot.Melds!.Count);
    }

    /// <summary>The same worn piece against a combat row is judged the plain way, where the weaker one is wrong.</summary>
    [Fact]
    public void ACombatRowKeepsThePlainComparison()
    {
        var piece = new ItemDto { Id = Body, Materia = [41778, 41778, 41779] };
        var target = new BisGearset { Job = "DRK", GearIndex = 0, Items = new() { ["Body"] = piece } };

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41766, 41778, 41778] }, job: "DRK");

        Assert.False(slot.IsComplete);
        Assert.Equal([41779], slot.MissingMateria);
        Assert.Equal([41766], slot.ExtraMateria);
        Assert.Null(slot.Melds);
    }

    /// <summary>A weaker materia below the cap is not enough, and the slot names what to meld and what is in it.</summary>
    [Fact]
    public void AnUnfilledSlotNamesWhatToMeldAndWhatSitsThere()
    {
        var target = Craft(new ItemDto { Id = Body, Materia = [41778, 41778, 33938, 41765, 41765], Hq = true });

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41778, 41778, 41765, 41765, 41765], Hq = true });

        Assert.False(slot.IsComplete);
        Assert.Equal([33938], slot.MissingMateria);
        Assert.Equal([41765], slot.ExtraMateria);
    }

    /// <summary>A worn NQ copy of a piece the target wants in HQ is not reached, whatever its materia.</summary>
    [Fact]
    public void AnNqCopyWhereHqIsWantedIsNotReached()
    {
        var target = Craft(new ItemDto { Id = Body, Materia = [41778], Hq = true });

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41778], Hq = false });

        Assert.True(slot.MateriaMatch);
        Assert.True(slot.HqMissing);
        Assert.False(slot.IsComplete);
    }

    /// <summary>A piece whose HQ state was not read is not held against the player.</summary>
    [Fact]
    public void AnUnknownHqIsNotHeldAgainstThePlayer()
    {
        var target = Craft(new ItemDto { Id = Body, Materia = [41778], Hq = true });

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41778], Hq = null });

        Assert.False(slot.HqMissing);
        Assert.True(slot.IsComplete);
    }

    /// <summary>A crafter row the server sent without tables falls back to the plain way rather than inventing caps.</summary>
    [Fact]
    public void ACrafterRowWithoutTablesFallsBackToThePlainWay()
    {
        var target = Craft(new ItemDto { Id = Body, Materia = [41778, 41779], Hq = true });

        var slot = Compare(target, new ItemDto { Id = Body, Materia = [41779, 41778], Hq = true }, tables: null);

        Assert.True(slot.IsComplete);
        Assert.Null(slot.Melds);
    }

    /// <summary>Rings stay interchangeable on a crafter row, judged the crafter way.</summary>
    [Fact]
    public void CrafterRingsStayInterchangeable()
    {
        var target = new BisGearset
        {
            Job = "CRP",
            GearIndex = 0,
            Craft = new CraftBlock { Level = 3 },
            Items = new()
            {
                ["RingLeft"] = new ItemDto { Id = Body, Materia = [41778, 41779], Hq = true },
                ["RingRight"] = new ItemDto { Id = Body, Materia = [41778, 41778], Hq = true },
            },
        };
        var live = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
            Gearsets =
            [
                new GearsetDto
                {
                    GearIndex = 0,
                    Job = "CRP",
                    Items = new()
                    {
                        ["RingLeft"] = new ItemDto { Id = Body, Materia = [41778, 41778], Hq = true },
                        ["RingRight"] = new ItemDto { Id = Body, Materia = [41766, 41778], Hq = true },
                    },
                },
            ],
        };

        var comparison = BisComparer.Compare(live, [target], tables: Tables).Single();

        Assert.True(comparison.IsComplete);
    }

    private static BisGearset Craft(ItemDto body) => new()
    {
        Job = "CRP",
        GearIndex = 0,
        Craft = new CraftBlock { Level = 3, Family = "crafter" },
        Items = new() { ["Body"] = body },
    };

    private static SlotComparison Compare(BisGearset target, ItemDto worn, string job = "CRP") =>
        Compare(target, worn, Tables, job);

    private static SlotComparison Compare(BisGearset target, ItemDto worn, CraftTables? tables, string job = "CRP")
    {
        var live = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
            Gearsets = [new GearsetDto { GearIndex = 0, Job = job, Items = new() { ["Body"] = worn } }],
        };

        return BisComparer.Compare(live, [target], tables: tables).Single().Slots.Single();
    }
}
