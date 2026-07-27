using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EorzeaArsenal.Localization;

/// <summary>One entry of the public <c>changelog.json</c>, in the shape the web side agreed on.</summary>
public sealed class ChangelogEntry
{
    /// <summary>Stable, repo-unique id. Never changes once published.</summary>
    public required string Id { get; init; }

    /// <summary>The release tag without the leading <c>v</c>. Absent means "not released yet".</summary>
    public string? Version { get; init; }

    /// <summary>Release date, <c>yyyy-MM-dd</c>.</summary>
    public required string Date { get; init; }

    /// <summary><c>feature</c>, <c>fix</c> or <c>change</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Always <c>plugin</c> — the badge the site puts on it.</summary>
    public string Source { get; init; } = "plugin";

    /// <summary>German title and body.</summary>
    public required ChangelogText De { get; init; }

    /// <summary>English title and body.</summary>
    public required ChangelogText En { get; init; }
}

/// <summary>One language's text for an entry.</summary>
public sealed class ChangelogText
{
    /// <summary>Short headline.</summary>
    public required string Title { get; init; }

    /// <summary>Simple HTML body (<c>&lt;p&gt;</c> only, here).</summary>
    public required string Body { get; init; }
}

/// <summary>The file's root object.</summary>
public sealed class ChangelogFile
{
    /// <summary>Every entry, newest release first.</summary>
    public required IReadOnlyList<ChangelogEntry> Entries { get; init; }
}

/// <summary>
/// Renders <see cref="ReleaseNotes"/> as the public <c>changelog.json</c> the web side polls, so the
/// same sentence reaches the in-game "what's new", the site's <c>/neu</c> page and Discord.
/// </summary>
/// <remarks>
/// Generated rather than hand-kept on purpose: three copies of the same text drift apart, and this one
/// is read by a machine that announces whatever it finds. The file is committed (the web side fetches
/// a static raw file), and stamped in the release PR rather than by a workflow — <c>main</c> is branch
/// protected and nothing pushes to it.
/// </remarks>
public static class ChangelogJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,

        // The text is German and English prose: escaping every umlaut and dash would make the file
        // unreadable for the humans who review it in a PR.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The kind names the web side expects, from the ones the in-game notes use.</summary>
    /// <param name="kind">The in-game classification.</param>
    /// <returns><c>feature</c>, <c>change</c> or <c>fix</c>.</returns>
    public static string KindName(ReleaseNoteKind kind) => kind switch
    {
        ReleaseNoteKind.Added => "feature",
        ReleaseNoteKind.Improved => "change",
        _ => "fix",
    };

    /// <summary>Builds the file's contents from the shipped release notes.</summary>
    /// <returns>Pretty-printed JSON, newest release first, ending in a newline.</returns>
    public static string Build()
    {
        var entries = new List<ChangelogEntry>();
        foreach (var note in ReleaseNotes.All)
        {
            foreach (var item in note.Items)
            {
                entries.Add(new ChangelogEntry
                {
                    Id = item.Id,
                    Version = note.Version,
                    Date = note.Date,
                    Kind = KindName(item.Kind),
                    De = Text(item.De),
                    En = Text(item.En),
                });
            }
        }

        return JsonSerializer.Serialize(new ChangelogFile { Entries = entries }, Options) + "\n";
    }

    /// <summary>
    /// Splits one note line into a headline and a body. The notes are written as "Headline: detail",
    /// which is exactly the shape the site wants; a line without that split becomes its own title.
    /// </summary>
    private static ChangelogText Text(string line)
    {
        var trimmed = line.Trim();
        var cut = trimmed.IndexOf(": ", StringComparison.Ordinal);

        // Only treat a colon as the headline separator when what precedes it reads like one — a short
        // phrase, not half the sentence.
        var split = cut is > 0 and <= 80;
        var title = split ? trimmed[..cut] : trimmed;
        var body = split ? Capitalise(trimmed[(cut + 2)..]) : trimmed;

        return new ChangelogText { Title = title, Body = $"<p>{Escape(body)}</p>" };
    }

    /// <summary>
    /// Starts the body with a capital. Splitting "Headline: detail" leaves the detail mid-sentence,
    /// and on the site it stands on its own as a paragraph.
    /// </summary>
    private static string Capitalise(string text) =>
        text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    /// <summary>Escapes the three characters that would otherwise be read as markup.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
