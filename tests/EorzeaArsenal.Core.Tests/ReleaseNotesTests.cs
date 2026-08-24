using System.Text.RegularExpressions;
using EorzeaArsenal.Localization;
using Xunit;

namespace EorzeaArsenal.Core.Tests;

/// <summary>
/// The bundled what's-new content. These guard the two ways it can rot: drifting away from the version
/// actually being shipped, and a note that only exists in one language.
/// </summary>
public sealed class ReleaseNotesTests
{
    [Fact]
    public void LatestMatchesTheShippedPluginVersion()
    {
        // If this fails, a release was cut without adding its notes (or vice versa).
        var csproj = FindRepoFile("src/EorzeaArsenalPlugin/EorzeaArsenalPlugin.csproj");
        var version = Regex.Match(File.ReadAllText(csproj), @"<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.Equal(version, ReleaseNotes.Latest.Version);
    }

    [Fact]
    public void EveryReleaseIsUsableInBothLanguages()
    {
        Assert.NotEmpty(ReleaseNotes.All);
        foreach (var note in ReleaseNotes.All)
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", note.Version);
            Assert.True(DateOnly.TryParse(note.Date, out _), $"{note.Version} has an unparsable date");
            Assert.NotEmpty(note.Items);
            foreach (var item in note.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.De), $"{note.Version}: missing German text");
                Assert.False(string.IsNullOrWhiteSpace(item.En), $"{note.Version}: missing English text");
                Assert.Equal(item.De, ReleaseNotes.Text(item, german: true));
                Assert.Equal(item.En, ReleaseNotes.Text(item, german: false));
            }
        }
    }

    [Fact]
    public void ReleasesAreUniqueAndNewestFirst()
    {
        var versions = ReleaseNotes.All.Select(n => Version.Parse(n.Version)).ToList();

        Assert.Equal(versions.Distinct().Count(), versions.Count);
        Assert.Equal(versions.OrderByDescending(v => v), versions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.1.0")]
    public void UnknownOrOlderSeenVersionCountsAsUnseen(string? seen) =>
        Assert.True(ReleaseNotes.HasUnseen(seen));

    [Fact]
    public void SeeingTheLatestClearsIt() =>
        Assert.False(ReleaseNotes.HasUnseen(ReleaseNotes.Latest.Version));

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relative} above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// The framework profile names four markings: new, changed, fixed, removed. Every one of them has to
    /// reach the web side as a kind it accepts, and a fifth added later must fail here rather than ship
    /// as whatever a fallback happened to be.
    /// </summary>
    [Fact]
    public void EveryNoteKindHasAChangelogName()
    {
        foreach (var kind in Enum.GetValues<ReleaseNoteKind>())
        {
            Assert.Contains(ChangelogJson.KindName(kind), (string[])["feature", "change", "fix"]);
        }
    }

    /// <summary>
    /// And every one of them has a badge in every shipped language. The window cannot be tested through
    /// ImGui, so the guard sits on the thing it reads: the key it builds from the kind's name.
    /// </summary>
    [Fact]
    public void EveryNoteKindHasABadgeInEveryLanguage()
    {
        foreach (var kind in Enum.GetValues<ReleaseNoteKind>())
        {
            var key = $"whatsnew.kind.{kind.ToString().ToLowerInvariant()}";
            foreach (var language in (string[])[Localizer.English, Localizer.German])
            {
                var text = new Localizer(language).Get(key);

                Assert.NotEqual(key, text);
                Assert.False(string.IsNullOrWhiteSpace(text));
            }
        }
    }

    /// <summary>
    /// The notes announce an update, so a first installation stays silent. Somebody installing the plugin
    /// today lived through none of it, and the framework profile names this case outright.
    /// </summary>
    [Theory]
    [InlineData(null, false, false)]      // brand new: nothing to announce
    [InlineData(null, true, true)]        // used before, never stamped: an upgrade from before the marker
    [InlineData("0.1.0", true, true)]     // used before, older marker: a normal update
    [InlineData("0.1.0", false, false)]   // not used yet, whatever the marker says
    public void TheNotesAnnounceThemselvesOnlyToSomebodyWhoUsedThePluginBefore(string? lastSeen, bool usedBefore, bool expected) =>
        Assert.Equal(expected, ReleaseNotes.ShouldAnnounce(lastSeen, usedBefore));

    /// <summary>Seeing the current version silences the announcement even for a returning user.</summary>
    [Fact]
    public void TheCurrentVersionIsNotAnnouncedTwice() =>
        Assert.False(ReleaseNotes.ShouldAnnounce(ReleaseNotes.Latest.Version, usedBefore: true));
}
