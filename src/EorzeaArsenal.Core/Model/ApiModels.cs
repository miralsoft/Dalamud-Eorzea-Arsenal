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
    public List<string>? Scopes { get; init; }

    /// <summary>Webhook event names the server can emit (informational; unused by the plugin).</summary>
    public List<string>? WebhookEvents { get; init; }
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
}
