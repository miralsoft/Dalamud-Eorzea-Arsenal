namespace EorzeaArsenal.Localization;

/// <summary>
/// Maps an API item-source value (e.g. <c>raid</c>) to its localization key, so the source can be
/// shown in the user's language (R6). Unknown values return <see langword="null"/> so the caller
/// can fall back to the raw value — keeping it forward-compatible if the API adds new sources.
/// </summary>
public static class SourceNames
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["crafted"] = "source.crafted",
        ["raid"] = "source.raid",
        ["tome"] = "source.tome",
        ["augmented_tome"] = "source.augmented_tome",
        ["alliance"] = "source.alliance",
        ["dungeon"] = "source.dungeon",
        ["extreme"] = "source.extreme",
        ["trial"] = "source.trial",
        ["ultimate"] = "source.ultimate",
        ["relic"] = "source.relic",
        ["pvp"] = "source.pvp",
        ["other"] = "source.other",
    };

    /// <summary>Returns the localization key for a source value, or <see langword="null"/> if unknown.</summary>
    /// <param name="source">The API source value.</param>
    /// <returns>The localization key, or <see langword="null"/>.</returns>
    public static string? LocKey(string? source) =>
        source is not null && Keys.TryGetValue(source, out var key) ? key : null;
}
