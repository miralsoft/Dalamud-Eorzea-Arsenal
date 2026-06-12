using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The sanitizer drops the noise the server would drop anyway, before validation.</summary>
public sealed class GearSanitizerTests
{
    private static GearData Wrap(params GearsetDto[] sets) => new()
    {
        Character = new CharacterDto { Name = "X", World = "Y", CidHash = TestData.ExampleHash },
        Gearsets = sets,
    };

    [Fact]
    public void Drops_items_with_fake_ids()
    {
        var set = new GearsetDto
        {
            GearIndex = 0,
            Job = "DRK",
            Items = new Dictionary<string, ItemDto>
            {
                ["Weapon"] = new() { Id = 49671 },          // real
                ["Head"] = new() { Id = 99_999_999 },        // fake/synced ultimate id
            },
        };

        var clean = GearSanitizer.Sanitize(Wrap(set));

        var items = clean.Gearsets.Single().Items;
        Assert.True(items.ContainsKey("Weapon"));
        Assert.False(items.ContainsKey("Head"));
    }

    [Fact]
    public void Drops_unmapped_jobs_and_unknown_slots()
    {
        var blu = new GearsetDto { GearIndex = 1, Job = "BLU", Items = [] };
        var drk = new GearsetDto
        {
            GearIndex = 0,
            Job = "DRK",
            Items = new Dictionary<string, ItemDto>
            {
                ["Weapon"] = new() { Id = 49671 },
                ["Belt"] = new() { Id = 12345 }, // not a valid API slot
            },
        };

        var clean = GearSanitizer.Sanitize(Wrap(blu, drk));

        var only = Assert.Single(clean.Gearsets);
        Assert.Equal("DRK", only.Job);
        Assert.False(only.Items.ContainsKey("Belt"));
    }

    [Fact]
    public void Filters_fake_materia_ids()
    {
        var set = new GearsetDto
        {
            GearIndex = 0,
            Job = "DRK",
            Items = new Dictionary<string, ItemDto>
            {
                ["Weapon"] = new() { Id = 49671, Materia = [41773, 0, 50_000_000] },
            },
        };

        var clean = GearSanitizer.Sanitize(Wrap(set));

        Assert.Equal([41773], clean.Gearsets.Single().Items["Weapon"].Materia);
    }

    [Fact]
    public void Truncates_overlong_name()
    {
        var set = new GearsetDto
        {
            GearIndex = 0,
            Job = "DRK",
            Name = new string('x', 100),
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 49671 } },
        };

        var clean = GearSanitizer.Sanitize(Wrap(set));

        Assert.Equal(ProtocolConstants.MaxGearsetNameLength, clean.Gearsets.Single().Name!.Length);
    }

    [Fact]
    public void Result_passes_validation()
    {
        var clean = GearSanitizer.Sanitize(TestData.Snapshot(TestData.ExampleHash));
        Assert.True(GearValidator.Validate(GearPayload.From(clean)).IsValid);
    }
}
