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
}
