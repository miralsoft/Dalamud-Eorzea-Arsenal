namespace EorzeaArsenal.Model;

/// <summary>
/// Body of <c>POST /contact</c> — a report the player wrote, sent from inside the game.
/// </summary>
/// <remarks>
/// Who is reporting and how to answer them comes from the API key, never from this body: a client
/// cannot claim to be someone else, and nothing arrives that cannot be answered. The
/// <see cref="Client"/> block is the client's own account of its situation — the server displays it
/// and decides nothing by it, which is why it being forgeable does not matter.
/// </remarks>
public sealed class ContactRequest
{
    /// <summary>What kind of report: <c>bug</c>, <c>feedback</c>, <c>feature</c> or <c>other</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>One line, as the player typed it (server limit: 140 characters).</summary>
    public required string Subject { get; init; }

    /// <summary>The report itself, as the player typed it (server limits: 10–3000 characters).</summary>
    public required string Message { get; init; }

    /// <summary>Where this came from. Every part is optional; unreadable ones are simply left out.</summary>
    public ContactClient? Client { get; init; }
}

/// <summary>
/// The situation a report was written in, sent as separate fields.
/// </summary>
/// <remarks>
/// Deliberately not pre-joined into one line: parts can always be composed, never reliably taken
/// apart — one space in a world name and every later reading of it breaks. How it is displayed is the
/// server's decision and not a protocol change.
/// </remarks>
public sealed class ContactClient
{
    /// <summary>Character name (≤ 60).</summary>
    public string? Character { get; init; }

    /// <summary>Home world (≤ 32).</summary>
    public string? World { get; init; }

    /// <summary>Game version, e.g. <c>7.4</c> (≤ 32).</summary>
    public string? GameVersion { get; init; }

    /// <summary>Dalamud version (≤ 32).</summary>
    public string? DalamudVersion { get; init; }

    /// <summary>Plugin version (≤ 32); omitted, the server falls back to the last sync's.</summary>
    public string? PluginVersion { get; init; }

    /// <summary>Which part of the plugin the player was in, e.g. <c>Farm</c> (≤ 80).</summary>
    public string? Where { get; init; }
}

/// <summary>Response of <c>GET /contact</c>: what the inbox currently accepts, and how to reach a human.</summary>
public sealed class ContactInfoResponse
{
    /// <summary>The info block.</summary>
    public ContactInfo? Data { get; init; }
}

/// <summary>
/// Which topics are actually open right now, plus the direct ways to get in touch.
/// </summary>
/// <remarks>
/// A topic can be switched off — there is then no channel behind it, and a report chosen from it
/// fails with <c>503</c> although the player did nothing wrong. So the picker is built from
/// <see cref="Kinds"/> rather than from a hard-coded list.
/// </remarks>
public sealed class ContactInfo
{
    /// <summary>The maintainer's character name, for someone who would rather write in game.</summary>
    public string? Character { get; init; }

    /// <summary>That character's world.</summary>
    public string? World { get; init; }

    /// <summary>The Discord handle.</summary>
    public string? Discord { get; init; }

    /// <summary>Invite link to the Discord server.</summary>
    public string? DiscordInvite { get; init; }

    /// <summary>The topic keys currently accepted, in display order.</summary>
    public List<string>? Kinds { get; init; }
}

/// <summary>The report kinds the endpoint accepts.</summary>
public static class ContactKinds
{
    /// <summary>Something is broken — what a report from the plugin practically always is.</summary>
    public const string Bug = "bug";

    /// <summary>General feedback.</summary>
    public const string Feedback = "feedback";

    /// <summary>A wish.</summary>
    public const string Feature = "feature";

    /// <summary>Anything else, questions included.</summary>
    public const string Other = "other";

    /// <summary>
    /// All four, in display order, as the fallback when <c>GET /contact</c> cannot be reached — better
    /// to offer them and let a switched-off one fail loudly than to offer nothing.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Bug, Feature, Feedback, Other];

    /// <summary>Whether a key is one the endpoint knows (an unknown one is a <c>400</c>).</summary>
    /// <param name="kind">The topic key.</param>
    /// <returns><see langword="true"/> when it is one of the four.</returns>
    public static bool IsKnown(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);

    /// <summary>Server-side length limits, mirrored so the UI can stop before the request does.</summary>
    public const int MaxSubject = 140;

    /// <summary>Shortest message the server accepts.</summary>
    public const int MinMessage = 10;

    /// <summary>Longest message the server accepts.</summary>
    public const int MaxMessage = 3000;

    /// <summary>Longest version string the server stores (game, Dalamud, plugin).</summary>
    public const int MaxVersion = 32;

    /// <summary>Longest <c>where</c> label the server stores.</summary>
    public const int MaxWhere = 80;

    /// <summary>
    /// Trims a context value to what the server will store, or <see langword="null"/> when there is
    /// nothing to say.
    /// </summary>
    /// <remarks>
    /// The context block is background information, never the report itself, so a value too long for
    /// its column must not cost the player the message they just wrote. Dalamud's <c>ScmVersion</c> is
    /// the realistic case: a <c>git describe</c> on a non-stable build, plus a beta track, outgrows 32
    /// characters easily. A shortened build string still identifies the build; a rejected request
    /// helps nobody. Cutting stops short of splitting a surrogate pair, so the result is never
    /// malformed text.
    /// </remarks>
    /// <param name="value">The raw value.</param>
    /// <param name="max">The column's limit.</param>
    /// <returns>The trimmed value, or <see langword="null"/>.</returns>
    public static string? Clip(string? value, int max)
    {
        if (max <= 0 || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= max)
        {
            return trimmed;
        }

        var cut = char.IsHighSurrogate(trimmed[max - 1]) ? max - 1 : max;
        var clipped = trimmed[..cut].TrimEnd();
        return clipped.Length == 0 ? null : clipped;
    }
}
