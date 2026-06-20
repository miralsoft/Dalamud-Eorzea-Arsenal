using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Client-side validation of an <see cref="InventoryPayload"/> before it is sent (rule R18,
/// "validate before sending; fail closed"). Mirrors the server's bounds so invalid local data
/// never leaves the machine: a valid character block, 1..50 scopes (never <c>manual</c>),
/// ≤ 4000 items with real ids and qty ≥ 1, every item's derived scope present in the request,
/// retainer items carrying a <c>source_id</c>, and a serialized body ≤ 256 KB.
/// </summary>
public static class InventoryValidator
{
    /// <summary>Validates a payload against all client-side rules.</summary>
    /// <param name="payload">The payload to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> describing any problems.</returns>
    public static ValidationResult Validate(InventoryPayload payload)
    {
        var result = new ValidationResult();

        ValidateCharacter(payload.Character, result);
        var scopes = ValidateScopes(payload.Scopes, result);
        ValidateItems(payload.Items, scopes, result);
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

    private static HashSet<string> ValidateScopes(IReadOnlyList<string> scopes, ValidationResult result)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        if (scopes.Count == 0)
        {
            result.Add("No scopes to send.");
        }

        if (scopes.Count > InventoryProtocol.MaxScopes)
        {
            result.Add($"Too many scopes ({scopes.Count} > {InventoryProtocol.MaxScopes}).");
        }

        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                result.Add("Empty scope.");
                continue;
            }

            if (scope == InventoryProtocol.ScopeManual)
            {
                result.Add("The 'manual' scope must never be sent.");
                continue;
            }

            var isKnown = scope == InventoryProtocol.ScopeCharacter || InventoryProtocol.IsRetainerScope(scope);
            if (!isKnown)
            {
                result.Add($"Unknown scope '{scope}'.");
            }

            if (InventoryProtocol.IsRetainerScope(scope) &&
                scope.Length - InventoryProtocol.RetainerScopePrefix.Length > InventoryProtocol.MaxSourceIdLength)
            {
                result.Add($"Retainer id too long in scope '{scope}'.");
            }

            set.Add(scope);
        }

        return set;
    }

    private static void ValidateItems(IReadOnlyList<InventoryItemDto> items, HashSet<string> scopes, ValidationResult result)
    {
        if (items.Count > InventoryProtocol.MaxItems)
        {
            result.Add($"Too many items ({items.Count} > {InventoryProtocol.MaxItems}).");
        }

        foreach (var item in items)
        {
            if (!IsValidItemId(item.ItemId))
            {
                result.Add($"Item id {item.ItemId} out of range.");
            }

            if (item.Qty < 1)
            {
                result.Add($"Item {item.ItemId} has qty {item.Qty} (< 1).");
            }

            if (string.IsNullOrEmpty(item.Container))
            {
                result.Add($"Item {item.ItemId} has no container.");
            }

            if (item.Container == InventoryContainers.Retainer && string.IsNullOrEmpty(item.SourceId))
            {
                result.Add($"Retainer item {item.ItemId} has no source_id.");
            }

            if (item.SourceId.Length > InventoryProtocol.MaxSourceIdLength)
            {
                result.Add($"Item {item.ItemId} source_id longer than {InventoryProtocol.MaxSourceIdLength} chars.");
            }

            // Every item must fall inside a scope the request claims to have scanned, else the
            // server silently ignores it (R18: catch it locally instead).
            var scope = InventoryProtocol.ScopeForItem(item);
            if (scopes.Count > 0 && !scopes.Contains(scope))
            {
                result.Add($"Item {item.ItemId} is in scope '{scope}' which is not in the request.");
            }
        }
    }

    private static void ValidateSize(InventoryPayload payload, ValidationResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EorzeaJson.Options);
        if (bytes.Length > InventoryProtocol.MaxBodyBytes)
        {
            result.Add($"Payload is {bytes.Length} bytes (> {InventoryProtocol.MaxBodyBytes}).");
        }
    }

    private static bool IsValidItemId(int id) =>
        id is >= ProtocolConstants.MinItemId and <= ProtocolConstants.MaxItemId;
}
