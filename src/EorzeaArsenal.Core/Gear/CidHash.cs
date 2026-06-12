using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Derives the stable, privacy-preserving character identifier sent to the server.
/// </summary>
/// <remarks>
/// <para>
/// <c>cid_hash</c> = lowercase-hex <b>SHA-256 of the ContentId rendered as a decimal string</b>,
/// with no salt — a fixed 64-character hex string. The raw ContentId is never transmitted
/// (rules R25/R27, P7).
/// </para>
/// <para>
/// This derivation is <b>locked by a unit-test vector</b> and must stay stable across plugin
/// versions and sessions so re-pushes map to the same character. Changing it requires an ADR
/// <i>and</i> a coordinated API change (P7).
/// </para>
/// </remarks>
public static class CidHash
{
    /// <summary>Computes the <c>cid_hash</c> for a character ContentId.</summary>
    /// <param name="contentId">The character's ContentId.</param>
    /// <returns>The 64-character lowercase hex SHA-256 hash.</returns>
    public static string Compute(ulong contentId)
    {
        var decimalString = contentId.ToString(CultureInfo.InvariantCulture);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(decimalString));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Whether a string is a syntactically valid <c>cid_hash</c> (64 lowercase hex chars).</summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> if it is 64 lowercase hex characters.</returns>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isLowerHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!isLowerHex)
            {
                return false;
            }
        }

        return true;
    }
}
