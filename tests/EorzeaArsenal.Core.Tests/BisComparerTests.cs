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
    public void Materia_diff_lists_wrong_and_missing()
    {
        // Item matches; equipped [1,2], BiS wants [2,3] → wrong: 1, missing: 3.
        var live = Live(new() { ["Weapon"] = new() { Id = 100, Materia = [1, 2] } });
        var target = Target(new() { ["Weapon"] = new() { Id = 100, Materia = [2, 3] } });

        var weapon = Slot(BisComparer.Compare(live, [target])[0], "Weapon");
        Assert.Equal(SlotMatch.Match, weapon.Status);
        Assert.False(weapon.MateriaMatch);
        Assert.Equal([3], weapon.MissingMateria);
        Assert.Equal([1], weapon.ExtraMateria);
    }

    [Fact]
    public void Item_differs_lists_target_materia_as_missing()
    {
        var live = Live(new() { ["Weapon"] = new() { Id = 100 } });
        var target = Target(new() { ["Weapon"] = new() { Id = 200, Materia = [5, 6] } });

        var weapon = Slot(BisComparer.Compare(live, [target])[0], "Weapon");
        Assert.Equal(SlotMatch.ItemDiffers, weapon.Status);
        Assert.Equal([5, 6], weapon.MissingMateria);
        Assert.Empty(weapon.ExtraMateria);
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
    public void Same_ring_id_different_materia_pairs_by_materia()
    {
        // Both rings are the same item id but need different materia; the player has both, swapped
        // across fingers. Each target must pair with the matching ring → both complete.
        var live = Live(new()
        {
            ["RingLeft"] = new() { Id = 5, Materia = [2, 2] },
            ["RingRight"] = new() { Id = 5, Materia = [1, 1] },
        });
        var target = Target(new()
        {
            ["RingLeft"] = new() { Id = 5, Materia = [1, 1] },
            ["RingRight"] = new() { Id = 5, Materia = [2, 2] },
        });

        var comp = BisComparer.Compare(live, [target])[0];
        Assert.True(Slot(comp, "RingLeft").MateriaMatch);
        Assert.True(Slot(comp, "RingRight").MateriaMatch);
        Assert.True(comp.IsComplete);
    }

    [Fact]
    public void Same_ring_id_one_wrong_materia_flags_one_ring()
    {
        // Player has two identical rings ([1,1]); BiS wants one [1,1] and one [2,2].
        var live = Live(new()
        {
            ["RingLeft"] = new() { Id = 5, Materia = [1, 1] },
            ["RingRight"] = new() { Id = 5, Materia = [1, 1] },
        });
        var target = Target(new()
        {
            ["RingLeft"] = new() { Id = 5, Materia = [1, 1] },
            ["RingRight"] = new() { Id = 5, Materia = [2, 2] },
        });

        var comp = BisComparer.Compare(live, [target])[0];
        var matched = comp.Slots.Count(s => s is { Status: SlotMatch.Match, MateriaMatch: true });
        var materiaOff = comp.Slots.Count(s => s is { Status: SlotMatch.Match, MateriaMatch: false });
        Assert.Equal(1, matched);
        Assert.Equal(1, materiaOff);
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
