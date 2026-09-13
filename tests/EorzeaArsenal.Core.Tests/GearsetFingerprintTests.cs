using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The two cache keys, and the one thing the API contract asks of the payload they are built from: the
/// job and the name travel exactly as the game holds them. The server's matching leans on both, so
/// "helpfully" trimming or defaulting a name would corrupt the input to a decision made elsewhere — an
/// empty name is better information than an invented one.
/// </summary>
public sealed class GearsetFingerprintTests
{
    private static GearsetDto Set(int index, string job, string? name, params (string Slot, int Id)[] items) => new()
    {
        GearIndex = index,
        Job = job,
        Name = name,
        Items = items.ToDictionary(i => i.Slot, i => new ItemDto { Id = i.Id }),
    };

    [Fact]
    public void The_position_is_not_part_of_either_key()
    {
        var here = Set(0, "DRK", "2.50", ("Weapon", 100));
        var there = Set(42, "DRK", "2.50", ("Weapon", 100));

        Assert.Equal(GearsetFingerprint.Strong(here), GearsetFingerprint.Strong(there));
        Assert.Equal(GearsetFingerprint.NameKey(here), GearsetFingerprint.NameKey(there));
    }

    [Fact]
    public void Slot_order_is_not_part_of_the_strong_key()
    {
        var one = Set(0, "DRK", "2.50", ("Weapon", 100), ("Head", 101));
        var other = Set(0, "DRK", "2.50", ("Head", 101), ("Weapon", 100));

        Assert.Equal(GearsetFingerprint.Strong(one), GearsetFingerprint.Strong(other));
    }

    [Fact]
    public void Changing_an_item_changes_the_strong_key_but_not_the_weak_one()
    {
        var before = Set(0, "DRK", "2.50", ("Weapon", 100));
        var after = Set(0, "DRK", "2.50", ("Weapon", 999));

        Assert.NotEqual(GearsetFingerprint.Strong(before), GearsetFingerprint.Strong(after));
        Assert.Equal(GearsetFingerprint.NameKey(before), GearsetFingerprint.NameKey(after));
    }

    [Fact]
    public void The_job_is_part_of_both_keys()
    {
        var tank = Set(0, "DRK", "2.50", ("Weapon", 100));
        var healer = Set(0, "WHM", "2.50", ("Weapon", 100));

        Assert.NotEqual(GearsetFingerprint.Strong(tank), GearsetFingerprint.Strong(healer));
        Assert.NotEqual(GearsetFingerprint.NameKey(tank), GearsetFingerprint.NameKey(healer));
    }

    /// <summary>
    /// A separator between the parts, so a job code running into a name cannot collide with a different
    /// split of the same characters.
    /// </summary>
    [Fact]
    public void Parts_cannot_run_into_each_other()
    {
        Assert.NotEqual(GearsetFingerprint.NameKey("AB", "C"), GearsetFingerprint.NameKey("A", "BC"));
    }

    [Fact]
    public void A_missing_name_and_an_empty_name_are_the_same_key()
    {
        Assert.Equal(GearsetFingerprint.NameKey("DRK", null), GearsetFingerprint.NameKey("DRK", string.Empty));
    }

    /// <summary>
    /// The contract says not to normalise the name. Whitespace and case are part of it, because they are
    /// part of it in game — two sets that differ only in case are two different sets to the server.
    /// </summary>
    [Fact]
    public void The_name_is_taken_literally()
    {
        Assert.NotEqual(GearsetFingerprint.NameKey("DRK", "2.50"), GearsetFingerprint.NameKey("DRK", " 2.50"));
        Assert.NotEqual(GearsetFingerprint.NameKey("DRK", "savage"), GearsetFingerprint.NameKey("DRK", "Savage"));
    }

    /// <summary>
    /// The sanitizer runs between the game read and the push, so it is the last place a name could be
    /// quietly changed. It must pass one through untouched — including an empty one, and including the
    /// leading space a player deliberately typed to sort a set to the top.
    /// </summary>
    [Fact]
    public void The_sanitizer_does_not_touch_the_name_or_the_job()
    {
        var data = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestSupport.TestData.ExampleHash },
            Gearsets =
            [
                Set(0, "DRK", " leading space", ("Weapon", 100)),
                Set(1, "WHM", string.Empty, ("Weapon", 200)),
                Set(2, "SGE", null, ("Weapon", 300)),
            ],
        };

        var clean = GearSanitizer.Sanitize(data);

        Assert.Equal(" leading space", clean.Gearsets[0].Name);
        Assert.Equal(string.Empty, clean.Gearsets[1].Name);
        Assert.Null(clean.Gearsets[2].Name);
        Assert.Equal(["DRK", "WHM", "SGE"], clean.Gearsets.Select(g => g.Job));
    }
}
