using EorzeaArsenal.Model;

namespace EorzeaArsenal.Api;

/// <summary>
/// Helpers for reasoning about the scopes a key carries. Used to warn the user after a
/// connection test if the issued key is missing <c>gear:write</c> (least-privilege check, R17).
/// </summary>
public static class ScopeUtil
{
    /// <summary>The single scope the plugin needs.</summary>
    public const string GearWrite = ProtocolConstants.RequiredScope;

    /// <summary>The scope required to upload owned items via <c>POST /inventory</c>.</summary>
    public const string InventoryWrite = InventoryProtocol.RequiredScope;

    /// <summary>Whether the given scope list grants <c>inventory:write</c>.</summary>
    /// <param name="scopes">Scopes from <c>GET /version</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>inventory:write</c> is present (case-insensitive).</returns>
    public static bool HasInventoryWrite(IEnumerable<string>? scopes) => Has(scopes, InventoryWrite);

    private static bool Has(IEnumerable<string>? scopes, string wanted)
    {
        if (scopes is null)
        {
            return false;
        }

        foreach (var scope in scopes)
        {
            if (string.Equals(scope?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the given scope list grants <c>gear:write</c>.</summary>
    /// <param name="scopes">Scopes from <c>GET /version</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>gear:write</c> is present (case-insensitive).</returns>
    public static bool HasGearWrite(IEnumerable<string>? scopes)
    {
        if (scopes is null)
        {
            return false;
        }

        foreach (var scope in scopes)
        {
            if (string.Equals(scope?.Trim(), GearWrite, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
