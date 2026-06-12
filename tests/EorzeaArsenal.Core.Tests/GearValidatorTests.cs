using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Client-side validation (R18) must mirror the server bounds and fail closed.</summary>
public sealed class GearValidatorTests
{
    private static GearPayload Payload(params GearsetDto[] sets) => new()
    {
        Character = new CharacterDto { Name = "Sanaka", World = "Twintania", CidHash = TestData.ExampleHash },
        Gearsets = sets,
    };

    private static GearsetDto ValidSet(int gearIndex = 0, string job = "DRK") => new()
    {
        GearIndex = gearIndex,
        Job = job,
        Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 49671 } },
    };

    [Fact]
    public void Valid_payload_passes()
    {
        Assert.True(GearValidator.Validate(Payload(ValidSet())).IsValid);
    }

    [Fact]
    public void Empty_gearsets_fail()
    {
        Assert.False(GearValidator.Validate(Payload()).IsValid);
    }

    [Fact]
    public void Bad_cid_hash_fails()
    {
        var payload = new GearPayload
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = "nope" },
            Gearsets = [ValidSet()],
        };
        Assert.False(GearValidator.Validate(payload).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Out_of_range_gear_index_fails(int gearIndex)
    {
        Assert.False(GearValidator.Validate(Payload(ValidSet(gearIndex))).IsValid);
    }

    [Fact]
    public void Unknown_job_fails()
    {
        Assert.False(GearValidator.Validate(Payload(ValidSet(job: "BLU"))).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void Out_of_range_item_id_fails(int itemId)
    {
        var set = new GearsetDto
        {
            GearIndex = 0,
            Job = "DRK",
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = itemId } },
        };
        Assert.False(GearValidator.Validate(Payload(set)).IsValid);
    }

    [Fact]
    public void Oversized_payload_fails()
    {
        // Build many gearsets with long names to exceed 64 KB.
        var sets = Enumerable.Range(0, 100)
            .Select(i => new GearsetDto
            {
                GearIndex = i % 100,
                Job = "DRK",
                Name = new string('x', 64),
                Items = Enumerable.Range(0, 12).ToDictionary(
                    s => "Slot" + s,
                    _ => new ItemDto { Id = 49671, Materia = [41773, 41773, 41773, 41773, 41773] }),
            })
            .ToArray();

        Assert.False(GearValidator.Validate(Payload(sets)).IsValid);
    }
}
