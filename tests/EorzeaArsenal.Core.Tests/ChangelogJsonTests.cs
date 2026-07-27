using System.Text.Json;
using System.Text.RegularExpressions;
using EorzeaArsenal.Localization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The public <c>changelog.json</c> the web side polls to announce plugin releases on the site and in
/// Discord. It is generated from the shipped release notes, so these tests guard the two things a
/// machine consumer depends on: that the committed file is current, and that ids never move.
/// </summary>
public sealed class ChangelogJsonTests
{
    /// <summary>Set to <c>1</c> to rewrite the committed file instead of failing (see the release doc).</summary>
    private const string UpdateVariable = "EORZEA_UPDATE_CHANGELOG";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// The committed file must match what the notes produce. It is fetched as a static raw file, so a
    /// stale one silently announces the wrong thing — or nothing.
    /// </summary>
    [Fact]
    public void TheCommittedFileIsUpToDate()
    {
        var path = Path.Combine(RepoRoot(), "changelog.json");
        var expected = ChangelogJson.Build();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, expected);
            return;
        }

        Assert.True(File.Exists(path), $"changelog.json is missing — regenerate with {UpdateVariable}=1.");
        Assert.Equal(expected.ReplaceLineEndings("\n"), File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The id is what the web side remembers as "already announced". A duplicate would silently
    /// swallow one of the two entries; an empty one would break the record entirely.
    /// </summary>
    [Fact]
    public void IdsAreUniqueAndWellFormed()
    {
        var ids = ReleaseNotes.All.SelectMany(n => n.Items).Select(i => i.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in ids)
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id);
        }
    }

    /// <summary>The shape the web side agreed on, checked on the real file rather than on a fixture.</summary>
    [Fact]
    public void TheFileHasTheAgreedShape()
    {
        using var doc = JsonDocument.Parse(ChangelogJson.Build());
        var entries = doc.RootElement.GetProperty("entries");

        Assert.NotEqual(0, entries.GetArrayLength());
        foreach (var entry in entries.EnumerateArray())
        {
            Assert.Equal("plugin", entry.GetProperty("source").GetString());
            Assert.Matches(@"^\d+\.\d+\.\d+$", entry.GetProperty("version").GetString()!);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", entry.GetProperty("date").GetString()!);
            Assert.Contains(entry.GetProperty("kind").GetString(), (string[])["feature", "fix", "change"]);

            foreach (var lang in (string[])["de", "en"])
            {
                var text = entry.GetProperty(lang);
                Assert.False(string.IsNullOrWhiteSpace(text.GetProperty("title").GetString()));

                // Simple HTML only: the consumer converts it to Discord markdown and drops the rest.
                var body = text.GetProperty("body").GetString()!;
                Assert.StartsWith("<p>", body, StringComparison.Ordinal);
                Assert.EndsWith("</p>", body, StringComparison.Ordinal);
                Assert.DoesNotMatch(new Regex("<(?!/?(p|strong|em|code|br|li|ul)\\b)", RegexOptions.IgnoreCase), body);
            }
        }
    }

    /// <summary>Every release the plugin ships notes for reaches the file — the site is a reference.</summary>
    [Fact]
    public void EveryShippedReleaseIsPresent()
    {
        using var doc = JsonDocument.Parse(ChangelogJson.Build());
        var versions = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("version").GetString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ReleaseNotes.All.Select(n => n.Version).ToList(), versions);
    }
}
