namespace EorzeaArsenal.Localization;

/// <summary>
/// Maps an API item-source value (e.g. <c>raid</c>) to its localization key, so the source can be
/// shown in the user's language (R6). Unknown values return <see langword="null"/> so the caller
/// can fall back to the raw value — keeping it forward-compatible if the API adds new sources.
/// </summary>
/// <remarks>
/// Two vocabularies reach the plugin for the same concept: <c>/gear/bis</c> and <c>/gear/obtain</c>
/// send the lower-case API enum, while <c>/me/advisor-options</c> passes through the <b>raw</b>
/// xivgear values (<c>Tome</c>, <c>AugTome</c>, <c>SavageRaid</c>, …). Both are accepted here so a
/// piece never reads differently depending on which endpoint produced it.
/// </remarks>
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

        // The raw xivgear spellings, as the advisor sends them.
        ["augcrafted"] = "source.crafted",
        ["savageraid"] = "source.raid",
        ["normalraid"] = "source.raid",
        ["augtome"] = "source.augmented_tome",
        ["allianceraid"] = "source.alliance",
        ["extremetrial"] = "source.extreme",
        ["criterion"] = "source.other",
        ["artifact"] = "source.other",
        ["unknown"] = "source.other",
    };

    /// <summary>Returns the localization key for a source value, or <see langword="null"/> if unknown.</summary>
    /// <param name="source">The API source value.</param>
    /// <returns>The localization key, or <see langword="null"/>.</returns>
    public static string? LocKey(string? source) =>
        source is not null && Keys.TryGetValue(source, out var key) ? key : null;
}
