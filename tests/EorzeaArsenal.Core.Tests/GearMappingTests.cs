using EorzeaArsenal.Gear;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Locks down the pure gear-mapping logic: cid_hash, job codes and slot keys.</summary>
public sealed class GearMappingTests
{
    // Fixed cid_hash test vectors (P7): a known ContentId must always hash to a known value.
    // Computed independently as lowercase-hex SHA-256 of the decimal ContentId string.
    [Theory]
    [InlineData(0UL, "5feceb66ffc86f38d952786c6d696c79c2dbc239dd4e91b46729d73a27fb57e9")]
    [InlineData(1234567890UL, "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646")]
    [InlineData(4611686018427387904UL, "0b5d78ce4bfad5b7a4fcdeee0e0d7c3680ab5e7e84e1cbf784a487b5c4a2dcaf")]
    public void CidHash_matches_fixed_vector(ulong contentId, string expected)
    {
        Assert.Equal(expected, CidHash.Compute(contentId));
    }

    [Fact]
    public void CidHash_is_64_lowercase_hex_and_stable()
    {
        var a = CidHash.Compute(123456789UL);
        var b = CidHash.Compute(123456789UL);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.True(CidHash.IsValid(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("C775E7B757EDE630CD0AA1113BD102661AB38829CA52A6422AB782862F268646")] // uppercase rejected
    public void CidHash_IsValid_rejects_bad_values(string? value)
    {
        Assert.False(CidHash.IsValid(value));
    }

    [Theory]
    [InlineData(19u, "PLD")]
    [InlineData(21u, "WAR")]
    [InlineData(32u, "DRK")]
    [InlineData(28u, "SCH")]
    [InlineData(41u, "VPR")]
    [InlineData(42u, "PCT")]
    public void JobMap_maps_known_jobs(uint classJobId, string expected)
    {
        Assert.Equal(expected, JobMap.ToCode(classJobId));
    }

    [Theory]
    [InlineData(1u)]   // Gladiator (base class)
    [InlineData(26u)]  // Arcanist (base class)
    [InlineData(36u)]  // Blue Mage (not whitelisted)
    [InlineData(999u)] // unknown
    public void JobMap_returns_null_for_non_whitelisted(uint classJobId)
    {
        Assert.Null(JobMap.ToCode(classJobId));
    }

    [Fact]
    public void JobMap_whitelist_has_21_unique_codes()
    {
        Assert.Equal(21, JobMap.ValidCodes.Count);
    }

    [Theory]
    [InlineData(0, "Weapon")]
    [InlineData(1, "OffHand")]
    [InlineData(11, "RingRight")]
    [InlineData(12, "RingLeft")]
    public void EquipmentSlots_map_index_to_key(int index, string expected)
    {
        Assert.Equal(expected, EquipmentSlots.KeyForIndex(index));
    }

    [Theory]
    [InlineData(5)]  // Belt (deprecated)
    [InlineData(13)] // Soul Crystal
    [InlineData(99)] // out of range
    public void EquipmentSlots_skip_unsent_slots(int index)
    {
        Assert.Null(EquipmentSlots.KeyForIndex(index));
    }

    [Fact]
    public void EquipmentSlots_have_12_valid_keys()
    {
        Assert.Equal(12, EquipmentSlots.ValidKeys.Count);
    }
}
