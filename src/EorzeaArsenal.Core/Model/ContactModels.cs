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

/// <summary>The report kinds the endpoint accepts.</summary>
public static class ContactKinds
{
    /// <summary>Something is broken — what a report from the plugin practically always is.</summary>
    public const string Bug = "bug";

    /// <summary>General feedback.</summary>
    public const string Feedback = "feedback";

    /// <summary>A wish.</summary>
    public const string Feature = "feature";

    /// <summary>Anything else — also what a deliberate test should use.</summary>
    public const string Other = "other";

    /// <summary>Server-side length limits, mirrored so the UI can stop before the request does.</summary>
    public const int MaxSubject = 140;

    /// <summary>Shortest message the server accepts.</summary>
    public const int MinMessage = 10;

    /// <summary>Longest message the server accepts.</summary>
    public const int MaxMessage = 3000;
}
