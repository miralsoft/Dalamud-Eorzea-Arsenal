using System.Security.Cryptography;
using System.Text;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Turns a gearset into the two keys the local mapping cache is indexed by. Pure and deterministic —
/// the same gearset always produces the same keys, on any machine, in any order.
/// </summary>
/// <remarks>
/// This is <b>not</b> an identity and must never be mistaken for one: identity is minted by the server
/// (see the gearset identity contract). These keys only re-attach a live gearset to the
/// <c>set_uid</c> the server already gave it, so something can be shown between two pushes. The
/// position is deliberately not part of either key — it is the one value guaranteed to be wrong in the
/// case this exists for.
/// </remarks>
public static class GearsetFingerprint
{
    /// <summary>
    /// Separates the parts of a key so "AB" + "C" cannot collide with "A" + "BC". A unit separator
    /// because it cannot occur in a job code or a gearset name.
    /// </summary>
    private const char Separator = (char)0x1F;

    /// <summary>
    /// The strong key: job, name and the item id per slot. Survives being moved, and changes when the
    /// set is renamed or re-geared — which is why <see cref="NameKey(GearsetDto)"/> exists beside it.
    /// </summary>
    /// <param name="set">The gearset, as read from the game and sanitized.</param>
    /// <returns>Lowercase hex SHA-256 over the normalised parts.</returns>
    public static string Strong(GearsetDto set)
    {
        var builder = new StringBuilder();
        builder.Append(set.Job).Append(Separator).Append(set.Name ?? string.Empty).Append(Separator);

        // Ordinal by slot so the payload's dictionary order cannot leak into the key.
        foreach (var slot in set.Items.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            builder.Append(slot).Append(':').Append(set.Items[slot].Id).Append(Separator);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// The weak key: job and name only. This is all <c>GET /gear/sets</c> gives back, so it is the
    /// only key that can bridge from a stored row to a live gearset — and it is ambiguous by nature,
    /// which the cache handles by refusing to answer rather than by guessing.
    /// </summary>
    /// <param name="job">Uppercase 3-letter job code.</param>
    /// <param name="name">The gearset name; <see langword="null"/> and empty are the same key.</param>
    /// <returns>A comparable key, case-sensitive in the name because the game is.</returns>
    public static string NameKey(string? job, string? name) =>
        (job ?? string.Empty) + Separator + (name ?? string.Empty);

    /// <summary>The weak key of a live gearset.</summary>
    /// <param name="set">The gearset.</param>
    /// <returns>The same key <see cref="NameKey(string?, string?)"/> builds.</returns>
    public static string NameKey(GearsetDto set) => NameKey(set.Job, set.Name);
}
