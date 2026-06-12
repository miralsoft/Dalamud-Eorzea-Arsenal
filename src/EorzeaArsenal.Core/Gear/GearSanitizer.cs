using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Cleans a freshly-read <see cref="GearData"/> snapshot so it passes validation and matches
/// what the server stores: it drops items/materia with non-real ids (e.g. synced "custom"
/// ultimate items with huge fake ids, which the server drops anyway), drops gearsets whose job
/// is not whitelisted, truncates over-long names, and caps the set count. This keeps the noisy,
/// game-specific cleanup pure and unit-testable instead of buried in the game reader.
/// </summary>
public static class GearSanitizer
{
    /// <summary>Returns a sanitized copy of the snapshot, ready for validation and sending.</summary>
    /// <param name="data">The raw snapshot read from the game.</param>
    /// <returns>A cleaned snapshot; never <see langword="null"/>.</returns>
    public static GearData Sanitize(GearData data)
    {
        var cleanSets = new List<GearsetDto>(data.Gearsets.Count);

        foreach (var set in data.Gearsets)
        {
            if (!JobMap.IsValidCode(set.Job) || set.GearIndex is < 0 or > ProtocolConstants.MaxGearIndex)
            {
                continue;
            }

            cleanSets.Add(SanitizeSet(set));

            if (cleanSets.Count >= ProtocolConstants.MaxGearsets)
            {
                break;
            }
        }

        return new GearData
        {
            Character = data.Character,
            Gearsets = cleanSets,
        };
    }

    private static GearsetDto SanitizeSet(GearsetDto set)
    {
        var cleanItems = new Dictionary<string, ItemDto>(set.Items.Count, StringComparer.Ordinal);

        foreach (var (slot, item) in set.Items)
        {
            if (!EquipmentSlots.ValidKeys.Contains(slot) || !IsRealItemId(item.Id))
            {
                continue;
            }

            cleanItems[slot] = new ItemDto
            {
                Id = item.Id,
                Materia = item.Materia.Where(IsRealItemId).ToList(),
            };
        }

        return new GearsetDto
        {
            GearIndex = set.GearIndex,
            Name = Truncate(set.Name, ProtocolConstants.MaxGearsetNameLength),
            Job = set.Job,
            Items = cleanItems,
            Food = set.Food is { } food && IsRealItemId(food) ? food : null,
            Ilvl = set.Ilvl,
        };
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    private static bool IsRealItemId(int id) =>
        id is >= ProtocolConstants.MinItemId and <= ProtocolConstants.MaxItemId;
}
