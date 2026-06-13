namespace EorzeaArsenal.Localization;

/// <summary>
/// Maps an API equipment slot key (PascalCase, e.g. <c>Head</c>) to its localization key, so slot
/// names can be shown in the user's language (R6).
/// </summary>
public static class SlotNames
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["Weapon"] = "slot.weapon",
        ["OffHand"] = "slot.offhand",
        ["Head"] = "slot.head",
        ["Body"] = "slot.body",
        ["Hands"] = "slot.hands",
        ["Legs"] = "slot.legs",
        ["Feet"] = "slot.feet",
        ["Ears"] = "slot.ears",
        ["Neck"] = "slot.neck",
        ["Wrists"] = "slot.wrists",
        ["RingLeft"] = "slot.ringleft",
        ["RingRight"] = "slot.ringright",
    };

    /// <summary>Returns the localization key for a slot, or the slot key itself if unknown.</summary>
    /// <param name="slot">The API slot key.</param>
    /// <returns>The localization key.</returns>
    public static string LocKey(string slot) => Keys.TryGetValue(slot, out var key) ? key : slot;
}
