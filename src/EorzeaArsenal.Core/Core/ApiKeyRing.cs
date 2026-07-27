namespace EorzeaArsenal.Core;

/// <summary>
/// Which API key belongs to which address.
/// </summary>
/// <remarks>
/// A key is issued by one server and is meaningless on another — but "meaningless" is the harmless
/// half. Sending a production key to a test server, or a test key to production, is the kind of
/// mistake nothing surfaces until it matters. So keys are held per address, and the rule that makes
/// that worth anything is the <b>absence of a fallback</b>: an address with no key of its own is
/// simply not connected. Pure and game-free, so the rule itself is unit-tested rather than trusted.
/// </remarks>
public static class ApiKeyRing
{
    /// <summary>
    /// The host a base URL points at — what a key is filed under. The path is deliberately ignored, so
    /// two API versions on the same server share a key while a different server never does.
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <returns>The lower-case host, or the trimmed input when it cannot be parsed.</returns>
    public static string HostOf(string? baseUrl)
    {
        var text = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : text.ToLowerInvariant();
    }

    /// <summary>
    /// The key stored for an address, or <see langword="null"/>. Never falls back to another address's
    /// key — that is the whole point.
    /// </summary>
    /// <param name="keys">Host → key.</param>
    /// <param name="baseUrl">The address being used.</param>
    /// <returns>The key, or <see langword="null"/> when this address has none.</returns>
    public static string? KeyFor(IReadOnlyDictionary<string, string>? keys, string? baseUrl)
    {
        if (keys is null)
        {
            return null;
        }

        return keys.TryGetValue(HostOf(baseUrl), out var key) && !string.IsNullOrEmpty(key) ? key : null;
    }

    /// <summary>Whether an address is something other than the shipped production one.</summary>
    /// <param name="baseUrl">The address being used.</param>
    /// <param name="productionUrl">The default the plugin ships with.</param>
    /// <returns><see langword="true"/> when the plugin is not talking to production.</returns>
    public static bool IsCustomAddress(string? baseUrl, string productionUrl) =>
        !string.Equals(HostOf(baseUrl), HostOf(productionUrl), StringComparison.Ordinal);
}
