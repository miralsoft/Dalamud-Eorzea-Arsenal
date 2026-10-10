using EorzeaArsenal.Model;

namespace EorzeaArsenal.Api;

/// <summary>
/// Helpers for reasoning about the scopes a key carries. Used to warn the user after a
/// connection test if the issued key is missing <c>gear:write</c> (least-privilege check, R17). The list
/// comes from <c>GET /device/key</c>; the <c>scopes</c> of <c>GET /version</c> are the server's catalogue,
/// the same for everybody, and checking a key against them can never fail.
/// </summary>
public static class ScopeUtil
{
    /// <summary>The single scope the plugin needs.</summary>
    public const string GearWrite = ProtocolConstants.RequiredScope;

    /// <summary>The scope required to upload owned items via <c>POST /inventory</c>.</summary>
    public const string InventoryWrite = InventoryProtocol.RequiredScope;

    /// <summary>The scope required to write the weekly checklist via <c>PUT …/weekly</c>.</summary>
    public const string CharactersWrite = WeeklyProtocol.WriteScope;

    /// <summary>The scope required to read (<c>GET …/weekly</c>, <c>GET /gear/bis</c>).</summary>
    public const string GearRead = WeeklyProtocol.ReadScope;

    /// <summary>
    /// The scope the deciding paths need, and it is deliberately not <c>gear:write</c>.
    /// </summary>
    /// <remarks>
    /// A key with <c>gear:write</c> may add and update and nothing else; folding "may remove a row" into it
    /// would silently widen every key already handed out. An existing key collects this one on its next
    /// request, so this check exists to explain a 403 rather than to ask anybody to reconnect.
    /// </remarks>
    public const string GearReview = "gear:review";

    /// <summary>The scope the review verb "delete" needs from server 1.4 on.</summary>
    /// <remarks>
    /// Split off <c>gear:review</c> so that a key which may sort rows cannot also remove them. Unlike
    /// <c>gear:review</c>, an existing key is <b>never</b> topped up with it: the player reconnects once, or
    /// allows it for that key on the website's API keys page. Until then a delete is refused with a 403
    /// that names this scope in <c>missing_scope</c>.
    /// </remarks>
    public const string GearDelete = "gear:delete";

    /// <summary>Whether the given scope list grants <c>inventory:write</c>.</summary>
    /// <param name="scopes">A key's own scopes, from <c>GET /device/key</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>inventory:write</c> is present (case-insensitive).</returns>
    public static bool HasInventoryWrite(IEnumerable<string>? scopes) => Has(scopes, InventoryWrite);

    /// <summary>Whether the given scope list grants <c>characters:write</c> (weekly checklist upload).</summary>
    /// <param name="scopes">A key's own scopes, from <c>GET /device/key</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>characters:write</c> is present (case-insensitive).</returns>
    public static bool HasCharactersWrite(IEnumerable<string>? scopes) => Has(scopes, CharactersWrite);

    /// <summary>Whether the given scope list grants <c>gear:review</c> (reconciliation).</summary>
    /// <param name="scopes">A key's own scopes, from <c>GET /device/key</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>gear:review</c> is present (case-insensitive).</returns>
    public static bool HasGearReview(IEnumerable<string>? scopes) => Has(scopes, GearReview);

    /// <summary>Whether the given scope list grants <c>gear:read</c>.</summary>
    /// <param name="scopes">A key's own scopes, from <c>GET /device/key</c> (may be <see langword="null"/>).</param>
    /// <returns><see langword="true"/> if <c>gear:read</c> is present (case-insensitive).</returns>
    public static bool HasGearRead(IEnumerable<string>? scopes) => Has(scopes, GearRead);

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
    /// <param name="scopes">A key's own scopes, from <c>GET /device/key</c> (may be <see langword="null"/>).</param>
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
