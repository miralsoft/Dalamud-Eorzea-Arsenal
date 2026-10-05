namespace EorzeaArsenal.Model;

/// <summary>Response of <c>POST /device/code</c> — the start of the OAuth device flow.</summary>
public sealed class DeviceCodeResponse
{
    /// <summary>Opaque device code used when polling <c>POST /device/token</c>.</summary>
    public required string DeviceCode { get; init; }

    /// <summary>Short human code the user types/confirms on the web approval page.</summary>
    public required string UserCode { get; init; }

    /// <summary>URL the user opens in a browser to approve the pairing.</summary>
    public required string VerificationUri { get; init; }

    /// <summary>
    /// Approval URL with the <c>user_code</c> already embedded (RFC 8628). Preferred when present
    /// so the user only has to click "Approve". Falls back to <see cref="VerificationUri"/>.
    /// </summary>
    public string? VerificationUriComplete { get; init; }

    /// <summary>Minimum seconds the client must wait between token polls (R23).</summary>
    public int Interval { get; init; } = 5;

    /// <summary>Seconds until the device code expires; stop polling afterwards.</summary>
    public int ExpiresIn { get; init; } = 300;
}

/// <summary>
/// Response of <c>POST /device/token</c>. Either still pending, or it carries the issued key.
/// A 4xx with a terminal <see cref="Status"/> (<c>expired|invalid|redeemed|denied</c>) ends the flow.
/// </summary>
public sealed class DeviceTokenResponse
{
    /// <summary>Set to <c>"pending"</c> while waiting, or a terminal reason on failure.</summary>
    public string? Status { get; init; }

    /// <summary>The issued API key (<c>ea_…</c>) once the user approved. Treated as a secret.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Granted scopes, expected to be exactly <c>gear:write</c>.</summary>
    public string? Scopes { get; init; }
}

/// <summary>Response of <c>GET /version</c> — used for an optional capability/compatibility check.</summary>
public sealed class VersionResponse
{
    /// <summary>API version string.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Server application version string.</summary>
    public string? AppVersion { get; init; }

    /// <summary>The gear protocol version the server understands.</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>
    /// Scopes advertised by the server. <c>GET /version</c> returns these as a JSON
    /// <b>array</b> (e.g. <c>["profile:read","gear:write"]</c>), so this must be a list —
    /// a scalar string here makes <see cref="System.Text.Json"/> throw on parse.
    /// </summary>
    /// <remarks>
    /// This is the server's <b>catalogue</b>, the same for everybody, and says nothing about a key. What a
    /// key may do is read from <c>GET /device/key</c> (<see cref="KeyScopesResponse"/>).
    /// </remarks>
    public List<string>? Scopes { get; init; }

    /// <summary>Webhook event names the server can emit (informational; unused by the plugin).</summary>
    public List<string>? WebhookEvents { get; init; }
}

/// <summary>
/// Response of <c>GET /device/key</c> (server 1.4): the scopes of the key in the <c>Authorization</c>
/// header, after the automatic top-up, and of nothing else.
/// </summary>
public sealed class KeyScopesResponse
{
    /// <summary>The key's scopes, such as <c>gear:write</c> or <c>gear:delete</c>.</summary>
    public List<string> Scopes { get; init; } = [];
}

/// <summary>
/// RFC 7807 <c>application/problem+json</c> error body returned by the API.
/// <see cref="RequestId"/> is logged for support; bodies themselves are never logged (R22).
/// </summary>
public sealed class ProblemDetails
{
    /// <summary>A URI reference identifying the problem type.</summary>
    public string? Type { get; init; }

    /// <summary>Short, human-readable summary of the problem.</summary>
    public string? Title { get; init; }

    /// <summary>HTTP status code, duplicated in the body.</summary>
    public int Status { get; init; }

    /// <summary>Human-readable explanation specific to this occurrence.</summary>
    public string? Detail { get; init; }

    /// <summary>Server-assigned correlation id — safe to log for support.</summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// The machine-readable cause, where the server names one: <c>job_unknown</c>,
    /// <c>job_incompatible</c>, <c>verb_not_applicable</c>, <c>verb_not_allowed_here</c>.
    /// </summary>
    /// <remarks>
    /// Not part of RFC 7807, and read from the same body on purpose: a 422 can just as well be an item id
    /// out of range or a name too long, so a client acting on the status code alone would refetch the job
    /// table after every failed push and write about jobs into a log that has nothing to do with jobs.
    /// The rule turns on the cause, never on the code.
    /// </remarks>
    public string? Error { get; init; }

    /// <summary>
    /// For <c>job_unknown</c>: the codes this server does not accept. The most useful line a rollback can
    /// leave behind, and the only cause today that carries a list.
    /// </summary>
    public List<string>? Jobs { get; init; }

    /// <summary>
    /// On a 403: the permission the <b>key</b> lacks, such as <c>teams:write</c>. Reconnecting helps.
    /// </summary>
    /// <remarks>
    /// One of two members that tell the two kinds of 403 apart, added on 2026-09-27. They exist so that
    /// nobody reads the cause out of <see cref="Detail"/>: that text is for people and may change. Telling a
    /// player to reconnect when their team has not granted a right sends them round in a circle.
    /// </remarks>
    public string? MissingScope { get; init; }

    /// <summary>
    /// On a 403: the right the <b>user</b> lacks in that team, such as <c>manage_members</c>. Reconnecting
    /// does not help; the team lead can grant it.
    /// </summary>
    public string? MissingCapability { get; init; }

    /// <summary>
    /// Why a request was refused, where the server names it apart from <see cref="Error"/>, such as
    /// <c>craft_target</c> on the advisor's 422 for a crafter or gatherer set. Read instead of <see cref="Detail"/>.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The machine-readable causes the API names in an error body, as agreed in the identity contract.
/// </summary>
/// <remarks>
/// Every one of these is a <b>detector</b>, not a sentence for a window: it says the caller offered
/// something it should not have, so it belongs in a log line and the player gets the plugin's own wording.
/// The list is closed for the deciding paths — anything outside it is a fault on the server side and
/// belongs in a bug report rather than in error handling here.
/// </remarks>
public static class ApiErrorCodes
{
    /// <summary>
    /// A job code this server does not accept. The one cause that discards the cached job table: it means
    /// the table in hand is wrong, which is the only way a client can believe something about a server
    /// that was true and is not any more.
    /// </summary>
    public const string JobUnknown = "job_unknown";

    /// <summary>A <c>link</c> aimed at a row whose job is not compatible. Never allowed, so a 422.</summary>
    public const string JobIncompatible = "job_incompatible";

    /// <summary>A verb that does not apply to the row it names.</summary>
    public const string VerbNotApplicable = "verb_not_applicable";

    /// <summary>A verb this endpoint does not take, which is a different statement from the one above.</summary>
    public const string VerbNotAllowedHere = "verb_not_allowed_here";
}
