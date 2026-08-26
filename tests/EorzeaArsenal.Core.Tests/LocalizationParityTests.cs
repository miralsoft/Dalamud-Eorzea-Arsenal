using System.Reflection;
using System.Text.RegularExpressions;
using EorzeaArsenal.Localization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Every key exists in every language, and says the same thing about its arguments.
/// </summary>
/// <remarks>
/// A missing translation is not a crash, it renders the raw key: a German player reads
/// <c>bis.nocatalogue</c> where a sentence should be. Nothing caught that before, and adding a key means
/// editing two dictionaries by hand in a 950-line file, which is exactly the edit that goes half-done.
/// The placeholder check is the sharper half: a <c>{0}</c> present in one language and absent in the other
/// throws at the call site rather than looking wrong.
/// </remarks>
public sealed class LocalizationParityTests
{
    private static readonly string[] Languages = [Localizer.English, Localizer.German];

    /// <summary>Every <c>public const string</c> on <see cref="LocKeys"/>, which is the full set.</summary>
    public static TheoryData<string, string> EveryKeyInEveryLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in AllKeys())
        {
            foreach (var language in Languages)
            {
                data.Add(language, key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryKeyInEveryLanguage))]
    public void EveryKeyResolvesInEveryLanguage(string language, string key)
    {
        var localizer = new Localizer(language);

        var text = localizer.Get(key);

        Assert.NotEqual(key, text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    /// <summary>
    /// The placeholders have to match, not the wording. A string formatted with one argument where the
    /// other language expects two is a <see cref="FormatException"/> in front of a player.
    /// </summary>
    [Fact]
    public void ThePlaceholdersMatchAcrossLanguages()
    {
        var en = new Localizer(Localizer.English);
        var de = new Localizer(Localizer.German);
        var mismatched = new List<string>();

        foreach (var key in AllKeys())
        {
            var inEnglish = Placeholders(en.Get(key));
            var inGerman = Placeholders(de.Get(key));
            if (!inEnglish.SetEquals(inGerman))
            {
                mismatched.Add($"{key}: en={{{string.Join(",", inEnglish.Order())}}} de={{{string.Join(",", inGerman.Order())}}}");
            }
        }

        Assert.Empty(mismatched);
    }

    [Fact]
    public void ThereAreKeysToCheckAtAll()
    {
        Assert.True(AllKeys().Count > 100, "LocKeys should be discoverable by reflection.");
    }

    private static IReadOnlyList<string> AllKeys() =>
        [.. typeof(LocKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    private static HashSet<int> Placeholders(string text)
    {
        var found = new HashSet<int>();
        foreach (var match in Regex.Matches(text, @"\{(\d+)").Cast<Match>())
        {
            found.Add(int.Parse(match.Groups[1].Value));
        }

        return found;
    }

    /// <summary>
    /// Foundation rule I-02: no em-dash characters in prose. Its own text names the reach, and this is
    /// the case it singles out: "strings compiled into a product". The rule points at the project's own
    /// test (C-11) for exactly this, and until now the project had none, while 65 shipped strings in the
    /// two catalogues carried the character.
    /// </summary>
    /// <remarks>
    /// Release notes are checked for the shipping version only. Older notes are what players already
    /// read, and the owner settled on 2026-08-20 that they stay as written.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryKeyInEveryLanguage))]
    public void NoShippedStringCarriesAnEmDash(string language, string key)
    {
        var text = new Localizer(language).Get(key);

        Assert.DoesNotContain('—', text);
    }

    /// <summary>The same rule for the notes of the version being shipped (see the remark above).</summary>
    [Fact]
    public void TheShippedReleaseNotesCarryNoEmDash()
    {
        var latest = ReleaseNotes.Latest;

        foreach (var item in latest.Items)
        {
            Assert.DoesNotContain('—', ReleaseNotes.Text(item, german: true));
            Assert.DoesNotContain('—', ReleaseNotes.Text(item, german: false));
        }
    }

    /// <summary>
    /// The other half of C-11: a catalogue must not carry a key nobody declares any more. Without this
    /// a replaced string stays in the file, translated into every language and shown nowhere, and the
    /// next reader has to work out whether it is dead or whether its call site is missing.
    /// </summary>
    [Theory]
    [InlineData(Localizer.English)]
    [InlineData(Localizer.German)]
    public void NoCatalogueCarriesAKeyNobodyDeclares(string language)
    {
        // Declared means "the code can ask for it", which is wider than the constants: two small tables
        // build their keys at run time from a slot or a source name.
        var declared = new HashSet<string>(AllKeys(), StringComparer.Ordinal);
        declared.UnionWith(SlotNames.AllKeys);
        declared.UnionWith(SourceNames.AllKeys);

        var undeclared = Localizer.KeysOf(language).Where(key => !declared.Contains(key)).Order().ToList();

        Assert.True(undeclared.Count == 0, $"{language} carries undeclared key(s): {string.Join(", ", undeclared)}");
    }
}
