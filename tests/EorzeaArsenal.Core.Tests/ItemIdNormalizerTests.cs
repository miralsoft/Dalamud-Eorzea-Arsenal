using EorzeaArsenal.Gear;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The HQ-offset stripping must be exact so item ids stay "real".</summary>
public sealed class ItemIdNormalizerTests
{
    [Theory]
    [InlineData(0u, 0)]
    [InlineData(49671u, 49671)]              // normal-quality endgame item, unchanged
    [InlineData(1_049_671u, 49671)]          // HQ-flagged → base id
    [InlineData(ItemIdNormalizer.HqOffset, 0)]
    public void Normalize_strips_hq_offset(uint raw, int expected)
    {
        Assert.Equal(expected, ItemIdNormalizer.Normalize(raw));
    }
}
