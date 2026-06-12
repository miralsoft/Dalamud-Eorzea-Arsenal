using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The pure live-gear vs BiS diff: matching, materia multiset, ring interchange.</summary>
public sealed class BisComparerTests
{
    private static GearData Live(Dictionary<string, ItemDto> items) => new()
    {
        Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
        Gearsets = [new GearsetDto { GearIndex = 0, Job = "DRK", Items = items }],
    };

    private static BisGearset Target(Dictionary<string, ItemDto> items) =>
        new() { Job = "DRK", GearIndex = 0, Name = "BiS", Items = items };

    private static SlotComparison Slot(GearsetComparison c, string slot) =>
        c.Slots.Single(s => s.Slot == slot);

    [Fact]
    public void Exact_match_is_complete()
    {
        var items = new Dictionary<string, ItemDto>
        {
            ["Weapon"] = new() { Id = 100, Materia = [1, 2] },
            ["Head"] = new() { Id = 101 },
        };
        var result = BisComparer.Compare(Live(items), [Target(items)]);

        var comp = Assert.Single(result);
        Assert.True(comp.HasLiveGearset);
        Assert.True(comp.IsComplete);
        Assert.Equal(SlotMatch.Match, Slot(comp, "Weapon").Status);
    }

    [Fact]
    public void Materia_order_is_irrelevant()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100, Materia = [2, 1] } });
        var target = Target(new() { ["Weapon"] = new() { Id = 100, Materia = [1, 2] } });

        var weapon = Slot(BisComparer.Compare(live, [target])[0], "Weapon");
        Assert.Equal(SlotMatch.Match, weapon.Status);
        Assert.True(weapon.MateriaMatch);
    }

    [Fact]
    public void Same_item_different_materia_flags_materia()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100, Materia = [1] } });
        var target = Target(new() { ["Weapon"] = new() { Id = 100, Materia = [9] } });

        var weapon = Slot(BisComparer.Compare(live, [target])[0], "Weapon");
        Assert.Equal(SlotMatch.Match, weapon.Status);
        Assert.False(weapon.MateriaMatch);
    }

    [Fact]
    public void Different_item_is_item_differs()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100 } });
        var target = Target(new() { ["Weapon"] = new() { Id = 200 } });

        Assert.Equal(SlotMatch.ItemDiffers, Slot(BisComparer.Compare(live, [target])[0], "Weapon").Status);
    }

    [Fact]
    public void Empty_slot_is_missing_current()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100 } });
        var target = Target(new() { ["Weapon"] = new() { Id = 100 }, ["Head"] = new() { Id = 101 } });

        Assert.Equal(SlotMatch.MissingCurrent, Slot(BisComparer.Compare(live, [target])[0], "Head").Status);
    }

    [Fact]
    public void Rings_are_interchangeable()
    {
        var live = Live(new()
        {
            ["RingLeft"] = new() { Id = 2 },
            ["RingRight"] = new() { Id = 1 },
        });
        var target = Target(new()
        {
            ["RingLeft"] = new() { Id = 1 },
            ["RingRight"] = new() { Id = 2 },
        });

        var comp = BisComparer.Compare(live, [target])[0];
        Assert.Equal(SlotMatch.Match, Slot(comp, "RingLeft").Status);
        Assert.Equal(SlotMatch.Match, Slot(comp, "RingRight").Status);
    }

    [Fact]
    public void Target_without_live_gearset_is_all_missing()
    {
        var live = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
            Gearsets = [], // player has no gearset for this target
        };
        var target = Target(new() { ["Weapon"] = new() { Id = 100 } });

        var comp = Assert.Single(BisComparer.Compare(live, [target]));
        Assert.False(comp.HasLiveGearset);
        Assert.Equal(SlotMatch.MissingCurrent, Slot(comp, "Weapon").Status);
    }

    [Fact]
    public void One_comparison_per_target()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100 } });
        var targets = new List<BisGearset>
        {
            Target(new() { ["Weapon"] = new() { Id = 100 } }),
            new() { Job = "WAR", GearIndex = 1, Items = new() { ["Weapon"] = new() { Id = 1 } } },
        };

        Assert.Equal(2, BisComparer.Compare(live, targets).Count);
    }
}
