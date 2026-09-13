namespace EorzeaArsenal.Model;

/// <summary>
/// One job as the server describes it, from <c>GET /gear/jobs</c>: its code, its role, whether it is a
/// combat job, and the base-class relation that decides compatibility.
/// </summary>
/// <remarks>
/// Compatibility is a <b>relation, not a partition</b>. A base class is compatible with the job it
/// becomes, but two upgrades of one base class are not compatible with each other: <c>ACN ↔ SMN</c> and
/// <c>ACN ↔ SCH</c> are both fine, <c>SMN ↔ SCH</c> is not. They share a weapon class and nothing else
/// that matters, and linking one to the other would be the wrong-comparison bug this all exists to
/// remove.
/// </remarks>
public sealed class JobEntry
{
    /// <summary>The uppercase three-letter code.</summary>
    public string? Code { get; init; }

    /// <summary>Role name as the server classifies it (<c>tank</c>, <c>crafter</c>, …).</summary>
    public string? Role { get; init; }

    /// <summary>Whether this is a combat job. Not the same as membership of the frozen combat floor.</summary>
    public bool Combat { get; init; }

    /// <summary>The base class or classes this job grows out of.</summary>
    public List<string> From { get; init; } = [];

    /// <summary>The job or jobs this class becomes.</summary>
    public List<string> To { get; init; } = [];
}

/// <summary>
/// Response of <c>GET /gear/jobs</c>. Read-only and cacheable; the server's copy always wins over the
/// one shipped with the plugin.
/// </summary>
public sealed class JobTableResponse
{
    /// <summary>The table's own version stamp, for diagnostics.</summary>
    public string? Version { get; init; }

    /// <summary>Every job the server accepts.</summary>
    public List<JobEntry> Jobs { get; init; } = [];
}

/// <summary>
/// What a push declares it covered. Sent on every push from the first version that can send more than
/// the combat floor, <b>including when the range is full</b> — only then does its absence unambiguously
/// mean "an older client".
/// </summary>
/// <remarks>
/// This field exists because of a mistake worth remembering. The server used to derive the range from
/// <c>plugin_version</c>, and a version is only a <i>proxy</i> for the range. A current client that fell
/// back to the combat floor — because <c>GET /gear/jobs</c> answered 404 — would have been read as
/// "reported everything", and the server would have parked every hand and land row of that character.
/// A parked row is an orphan and an orphan is offered with <c>delete</c>: a safeguard against a rejected
/// job code would have staged a deletion. The general rule is now: whoever sends less than they could
/// says so, because otherwise "not reported" cannot be told from "gone from the game".
/// </remarks>
public static class JobScope
{
    /// <summary>Only the frozen combat floor was reported.</summary>
    public const string Combat = "combat";

    /// <summary>Every job in the server's table was reported.</summary>
    public const string All = "all";

    /// <summary>
    /// The 21 combat codes every server has accepted since before any of this — a <b>compatibility floor</b>, compiled in, and it never grows.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> derived from the server's table, for two reasons. It would be circular:
    /// the rule that needs it is "a rejected job code discards the cached table, then send
    /// <see cref="Combat"/>", and the roles live in the table that was just discarded. And it would be
    /// wrong from the first day rather than at some future twenty-second combat job: <c>BLU</c> is a
    /// combat job and is <c>combat: true</c> in the table, yet it is not in this list, because it
    /// arrives with the widening to 42. This list retires together with the version rule, once no
    /// client is below the version that sends everything.
    /// </remarks>
    public static readonly IReadOnlySet<string> CombatFloor = new HashSet<string>(StringComparer.Ordinal)
    {
        "PLD", "WAR", "DRK", "GNB",
        "WHM", "SCH", "AST", "SGE",
        "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC",
        "BLM", "SMN", "RDM", "PCT",
    };
}
