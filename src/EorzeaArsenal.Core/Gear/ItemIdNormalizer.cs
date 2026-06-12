namespace EorzeaArsenal.Gear;

/// <summary>
/// Normalizes a raw in-game item id into the "real" item id the API expects. The game encodes
/// high-quality items by adding a fixed offset (1,000,000); the API wants the base id. Kept pure
/// and unit-tested so the game reader stays thin.
/// </summary>
public static class ItemIdNormalizer
{
    /// <summary>The offset the game adds to a high-quality item's id.</summary>
    public const uint HqOffset = 1_000_000;

    /// <summary>Strips the HQ flag from a raw item id, yielding the base id.</summary>
    /// <param name="rawItemId">The raw item id read from the game.</param>
    /// <returns>The base item id as an <see cref="int"/> (0 if the raw id is empty).</returns>
    public static int Normalize(uint rawItemId)
    {
        var id = rawItemId >= HqOffset ? rawItemId - HqOffset : rawItemId;
        return (int)id;
    }
}
