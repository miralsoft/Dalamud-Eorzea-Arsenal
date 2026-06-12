namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Resolves user-facing UI strings (R6: bilingual DE/EN). Every UI string goes through this —
/// no hardcoded text. Code, logs and docs stay English (R5). Swappable to add more languages.
/// </summary>
public interface ILocalizer
{
    /// <summary>The active two-letter language code (<c>"de"</c> or <c>"en"</c>).</summary>
    string Language { get; set; }

    /// <summary>Resolves a string by key in the active language.</summary>
    /// <param name="key">The string key (see the localization resources).</param>
    /// <returns>The localized string, or the key itself if unknown (so gaps are visible).</returns>
    string Get(string key);

    /// <summary>Resolves and <see cref="string.Format(string, object?[])"/>s a string by key.</summary>
    /// <param name="key">The string key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    string Get(string key, params object[] args);
}
