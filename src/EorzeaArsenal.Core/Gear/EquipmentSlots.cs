namespace EorzeaArsenal.Gear;

/// <summary>
/// Maps a gearset's equipment item index (the order used by the game's gearset entry) to the
/// API slot key. Belt (deprecated) and the Soul Crystal are intentionally not sent — the API
/// has 12 slots. Any non-mapped index yields <see langword="null"/> and is skipped.
/// </summary>
public static class EquipmentSlots
{
    /// <summary>
    /// Slot key per gearset item index (0..13). <see langword="null"/> entries (Belt = 5,
    /// Soul Crystal = 13) are not part of the API's 12-slot model.
    /// </summary>
    public static readonly IReadOnlyList<string?> ByGearsetIndex =
    [
        "Weapon",    // 0  MainHand
        "OffHand",   // 1  OffHand
        "Head",      // 2  Head
        "Body",      // 3  Body
        "Hands",     // 4  Hands
        null,        // 5  Belt (deprecated, not sent)
        "Legs",      // 6  Legs
        "Feet",      // 7  Feet
        "Ears",      // 8  Ears
        "Neck",      // 9  Neck
        "Wrists",    // 10 Wrists
        "RingRight", // 11 Right ring
        "RingLeft",  // 12 Left ring
        null,        // 13 Soul Crystal (not sent)
    ];

    /// <summary>The 12 canonical slot keys accepted by the API.</summary>
    public static readonly IReadOnlySet<string> ValidKeys = new HashSet<string>(
        ["Weapon", "OffHand", "Head", "Body", "Hands", "Legs", "Feet", "Ears", "Neck", "Wrists", "RingLeft", "RingRight"],
        StringComparer.Ordinal);

    /// <summary>Maps a gearset item index to its slot key, or <see langword="null"/> if not sent.</summary>
    /// <param name="gearsetItemIndex">The item index within the gearset entry (0..13).</param>
    /// <returns>The slot key, or <see langword="null"/>.</returns>
    public static string? KeyForIndex(int gearsetItemIndex) =>
        gearsetItemIndex >= 0 && gearsetItemIndex < ByGearsetIndex.Count
            ? ByGearsetIndex[gearsetItemIndex]
            : null;
}
