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
    /// <summary>
    /// The point of the whole change. Two gearsets swap places in game; the targets are keyed on
    /// identity, so each still lands on the gearset it was pinned to. Under the old position key this
    /// compared the tank set against the healer's target and showed plausible, false numbers.
    /// </summary>
    [Fact]
    public void Reordering_the_live_list_does_not_move_the_targets()
    {
        var tank = new GearsetDto
        {
            GearIndex = 1,
            Job = "DRK",
            Name = "2.50",
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
        };
        var healer = new GearsetDto
        {
            GearIndex = 0,
            Job = "WHM",
            Name = "Heal",
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 200 } },
        };

        var live = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
            Gearsets = [healer, tank],
        };

        // The targets still carry the positions from before the swap — which is exactly the situation
        // the old key could not survive.
        var targets = new List<BisGearset>
        {
            new()
            {
                Job = "DRK", GearIndex = 0, SetUid = "tank", Name = "Tank BiS",
                Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
            },
            new()
            {
                Job = "WHM", GearIndex = 1, SetUid = "healer", Name = "Healer BiS",
                Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 200 } },
            },
        };

        var result = BisComparer.Compare(live, targets, set => set.Job == "DRK" ? "tank" : "healer");

        Assert.All(result, c => Assert.True(c.IsComplete));
        Assert.All(result, c => Assert.True(c.HasLiveGearset));
    }

    /// <summary>
    /// A target whose gearset cannot be identified is reported as having no live gearset. Falling back
    /// to the position here would resurrect the bug: the neighbouring set would be compared instead,
    /// and the numbers would look reasonable while being about the wrong gearset.
    /// </summary>
    [Fact]
    public void An_unresolvable_identity_shows_nothing_rather_than_the_neighbour()
    {
        var live = Live(new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } });
        var targets = new List<BisGearset>
        {
            new()
            {
                Job = "DRK", GearIndex = 0, SetUid = "somewhere-else",
                Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
            },
        };

        var result = BisComparer.Compare(live, targets, _ => null);

        Assert.False(result[0].HasLiveGearset);
    }

    /// <summary>
    /// A server that does not mint identities yet sends no <c>set_uid</c>, and the old position key has
    /// to keep working — two machines can talk to a live and a test instance on the same day.
    /// </summary>
    [Fact]
    public void Without_identities_the_position_still_matches()
    {
        var live = Live(new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } });
        var targets = new List<BisGearset>
        {
            Target(new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } }),
        };

        var result = BisComparer.Compare(live, targets);

        Assert.True(result[0].HasLiveGearset);
        Assert.True(result[0].IsComplete);
        Assert.Null(result[0].SetUid);
    }

    /// <summary>
    /// A response can carry identities on some rows and not others while the server migrates. The key
    /// is chosen per target, so each row gets the best one it actually has.
    /// </summary>
    [Fact]
    public void A_mixed_response_uses_the_best_key_per_target()
    {
        var live = Live(new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } });
        var targets = new List<BisGearset>
        {
            new()
            {
                Job = "DRK", GearIndex = 0, SetUid = "known",
                Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
            },
            new()
            {
                Job = "DRK", GearIndex = 0, SetUid = null,
                Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 100 } },
            },
        };

        var result = BisComparer.Compare(live, targets, _ => "known");

        Assert.True(result[0].HasLiveGearset);
        Assert.True(result[1].HasLiveGearset);
    }
}
