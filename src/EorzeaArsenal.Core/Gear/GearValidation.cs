using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;

namespace EorzeaArsenal.Gear;

/// <summary>The result of validating a gear payload before sending.</summary>
public sealed class ValidationResult
{
    /// <summary>Whether the payload is safe to send.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Human-readable validation errors (empty when valid).</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Adds an error message.</summary>
    /// <param name="message">The error to record.</param>
    public void Add(string message) => Errors.Add(message);
}

/// <summary>
/// Client-side validation of a <see cref="GearPayload"/> before it is sent (rule R18,
/// "validate before sending; fail closed"). Mirrors the server's bounds so invalid local data
/// never leaves the machine: job in the whitelist, item ids 1..9,999,999, gear_index 0..99,
/// ≤ 200 gearsets, serialized body ≤ 64 KB.
/// </summary>
public static class GearValidator
{
    /// <summary>Validates a payload against all client-side rules.</summary>
    /// <param name="payload">The payload to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> describing any problems.</returns>
    public static ValidationResult Validate(GearPayload payload)
    {
        var result = new ValidationResult();

        ValidateCharacter(payload.Character, result);
        ValidateGearsets(payload.Gearsets, result);
        ValidateSize(payload, result);

        return result;
    }

    private static void ValidateCharacter(CharacterDto character, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(character.Name))
        {
            result.Add("Character name is empty.");
        }

        if (string.IsNullOrWhiteSpace(character.World))
        {
            result.Add("Character world is empty.");
        }

        if (!CidHash.IsValid(character.CidHash))
        {
            result.Add("cid_hash is not a 64-character lowercase hex string.");
        }
    }

    private static void ValidateGearsets(IReadOnlyList<GearsetDto> gearsets, ValidationResult result)
    {
        if (gearsets.Count == 0)
        {
            result.Add("No gearsets to send.");
        }

        if (gearsets.Count > ProtocolConstants.MaxGearsets)
        {
            result.Add($"Too many gearsets ({gearsets.Count} > {ProtocolConstants.MaxGearsets}).");
        }

        foreach (var set in gearsets)
        {
            ValidateGearset(set, result);
        }
    }

    private static void ValidateGearset(GearsetDto set, ValidationResult result)
    {
        if (set.GearIndex is < 0 or > ProtocolConstants.MaxGearIndex)
        {
            result.Add($"gear_index {set.GearIndex} out of range 0..{ProtocolConstants.MaxGearIndex}.");
        }

        if (!JobMap.IsValidCode(set.Job))
        {
            result.Add($"Unknown job code '{set.Job}' (gear_index {set.GearIndex}).");
        }

        if (set.Name is { Length: > ProtocolConstants.MaxGearsetNameLength })
        {
            result.Add($"Gearset name longer than {ProtocolConstants.MaxGearsetNameLength} chars (gear_index {set.GearIndex}).");
        }

        foreach (var (slot, item) in set.Items)
        {
            ValidateItem(slot, item, set.GearIndex, result);
        }

        if (set.Food is { } food && !IsValidItemId(food))
        {
            result.Add($"Food id {food} out of range (gear_index {set.GearIndex}).");
        }
    }

    private static void ValidateItem(string slot, ItemDto item, int gearIndex, ValidationResult result)
    {
        if (!IsValidItemId(item.Id))
        {
            result.Add($"Item id {item.Id} out of range in slot '{slot}' (gear_index {gearIndex}).");
        }

        foreach (var materia in item.Materia)
        {
            if (!IsValidItemId(materia))
            {
                result.Add($"Materia id {materia} out of range in slot '{slot}' (gear_index {gearIndex}).");
            }
        }
    }

    private static void ValidateSize(GearPayload payload, ValidationResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EorzeaJson.Options);
        if (bytes.Length > ProtocolConstants.MaxPayloadBytes)
        {
            result.Add($"Payload is {bytes.Length} bytes (> {ProtocolConstants.MaxPayloadBytes}).");
        }
    }

    private static bool IsValidItemId(int id) =>
        id is >= ProtocolConstants.MinItemId and <= ProtocolConstants.MaxItemId;
}
