using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>
/// The <b>only</b> component that talks to the Eorzea Arsenal API (R10: no HTTP outside
/// this module). Covers exactly the documented slice — device flow, <c>PUT /gear</c> and
/// <c>GET /version</c> (R13) — and nothing else. Swappable for a fake in tests.
/// </summary>
public interface IApiClient
{
    /// <summary>Starts the OAuth device flow via <c>POST /device/code</c> (no auth).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The device/user codes and polling parameters, or an error.</returns>
    Task<ApiResult<DeviceCodeResponse>> RequestDeviceCodeAsync(CancellationToken ct);

    /// <summary>Polls <c>POST /device/token</c> once for the given device code (no auth).</summary>
    /// <param name="deviceCode">The device code from <see cref="RequestDeviceCodeAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A pending status, the issued key, or a terminal error.</returns>
    Task<ApiResult<DeviceTokenResponse>> PollDeviceTokenAsync(string deviceCode, CancellationToken ct);

    /// <summary>Pushes all gearsets via <c>PUT /gear</c> using the supplied bearer key.</summary>
    /// <param name="apiKey">The <c>gear:write</c> API key (kept secret; never logged — R22).</param>
    /// <param name="payload">The validated gear payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The push result, or a classified error (401/403/409/422/400/429).</returns>
    Task<ApiResult<GearPushResult>> PushGearAsync(string apiKey, GearPayload payload, CancellationToken ct);

    /// <summary>
    /// Uploads scope-scoped owned items via <c>POST /inventory</c> (requires the
    /// <c>inventory:write</c> scope). The server replaces only the reported scopes; unreported
    /// scopes keep their last state (R13: documented Phase-2 slice).
    /// </summary>
    /// <param name="apiKey">The API key (must carry <c>inventory:write</c>; never logged — R22).</param>
    /// <param name="payload">The validated inventory payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The push result, or a classified error (401/403/409/422/400/429).</returns>
    Task<ApiResult<InventoryPushResult>> PushInventoryAsync(string apiKey, InventoryPayload payload, CancellationToken ct);

    /// <summary>Calls <c>GET /version</c> for an optional capability/compatibility/connection check.</summary>
    /// <param name="apiKey">Optional key to also report the key's scopes; may be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server version info, or an error.</returns>
    Task<ApiResult<VersionResponse>> GetVersionAsync(string? apiKey, CancellationToken ct);

    /// <summary>
    /// Reads the BiS targets via <c>GET /gear/bis</c> (requires the <c>gear:read</c> scope). Used
    /// for the in-game "gear vs BiS" diff (R13: still part of the documented slice).
    /// </summary>
    /// <param name="apiKey">The API key (must carry <c>gear:read</c>).</param>
    /// <param name="cidHash">Optional character hash to narrow to one character; <see langword="null"/> for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved BiS targets, or a classified error (401/403/404).</returns>
    Task<ApiResult<BisResponse>> GetBisAsync(string apiKey, string? cidHash, CancellationToken ct);
}
