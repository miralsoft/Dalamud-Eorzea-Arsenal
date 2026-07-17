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

    /// <summary>
    /// Reads the server-stored weekly checklist for a character via
    /// <c>GET /characters/{characterId}/weekly</c> (requires <c>gear:read</c>). Used to send only the
    /// fields that actually changed, so manual web-app entries are never overwritten.
    /// </summary>
    /// <param name="apiKey">The API key (must carry <c>gear:read</c>).</param>
    /// <param name="characterId">The server's numeric character id (from a push response).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current weekly values, or a classified error (401/403/404).</returns>
    Task<ApiResult<WeeklyResponse>> GetWeeklyAsync(string apiKey, string characterId, CancellationToken ct);

    /// <summary>
    /// Merges weekly-checklist fields via <c>PUT /characters/{characterId}/weekly</c> (requires
    /// <c>characters:write</c>). Only the sent fields change; unsent fields keep their state.
    /// </summary>
    /// <param name="apiKey">The API key (must carry <c>characters:write</c>; never logged — R22).</param>
    /// <param name="characterId">The server's numeric character id (from a push response).</param>
    /// <param name="payload">The known fields to merge.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merge result, or a classified error (401/403/404/422/429).</returns>
    Task<ApiResult<WeeklyPushResult>> PutWeeklyAsync(string apiKey, string characterId, WeeklyPayload payload, CancellationToken ct);

    // --- Teams companion (Phase C + D): teams:read reads + teams:write own-record writes -----------

    /// <summary>Reads the player's teams + active mit plans via <c>GET /me/teams</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The teams, or a classified error.</returns>
    Task<ApiResult<TeamsResponse>> GetTeamsAsync(string apiKey, CancellationToken ct);

    /// <summary>Reads the expanded calendar via <c>GET /me/calendar</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="from">Optional window start <c>YYYY-MM-DD</c>.</param>
    /// <param name="to">Optional window end <c>YYYY-MM-DD</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The occurrences, or a classified error.</returns>
    Task<ApiResult<CalendarResponse>> GetCalendarAsync(string apiKey, string? from, string? to, CancellationToken ct);

    /// <summary>Reads a mit cheat sheet via <c>GET /teams/{id}/mit/{planId}/sheet</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="planId">The plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sheet, or a classified error (404 if not a member).</returns>
    Task<ApiResult<MitSheetResponse>> GetMitSheetAsync(string apiKey, long teamId, long planId, CancellationToken ct);

    /// <summary>Reads the content hub via <c>GET /teams/{id}/content/sheet</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hub, or a classified error (404 if not a member).</returns>
    Task<ApiResult<ContentSheetResponse>> GetContentSheetAsync(string apiKey, long teamId, CancellationToken ct);

    /// <summary>Streams a resource file via <c>GET /teams/{id}/resources/{resourceId}/file</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="resourceId">The resource id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bytes + MIME, or a classified error.</returns>
    Task<ApiResult<ResourceFile>> GetResourceFileAsync(string apiKey, long teamId, long resourceId, CancellationToken ct);

    /// <summary>Reads the who-needs-what farm overview via <c>GET /teams/{id}/farm</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The farm entries, or a classified error.</returns>
    Task<ApiResult<FarmResponse>> GetFarmAsync(string apiKey, long teamId, CancellationToken ct);

    /// <summary>Reads the FFLogs mirror via <c>GET /teams/{id}/logs</c> (<c>teams:read</c>; best-effort).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The logs (possibly a disconnected/empty state), or a classified error.</returns>
    Task<ApiResult<LogsResponse>> GetLogsAsync(string apiKey, long teamId, CancellationToken ct);

    /// <summary>Sets your own RSVP via <c>POST /teams/{id}/events/{eventId}/attendance</c> (<c>teams:write</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:write</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="request">The occurrence date + new status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ack, or a classified error (403 capability / 404 not a member).</returns>
    Task<ApiResult<StatusAck>> PostAttendanceAsync(string apiKey, long teamId, long eventId, AttendanceRequest request, CancellationToken ct);

    /// <summary>Reads absence ranges via <c>GET /teams/{id}/absences</c> (<c>teams:read</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:read</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Your own ranges (plus others' if you hold the right), or a classified error.</returns>
    Task<ApiResult<AbsencesResponse>> GetAbsencesAsync(string apiKey, long teamId, CancellationToken ct);

    /// <summary>Reports your own absence via <c>POST /teams/{id}/absences</c> (<c>teams:write</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:write</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="request">The date range + optional note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created id, or a classified error.</returns>
    Task<ApiResult<AbsenceCreateResponse>> PostAbsenceAsync(string apiKey, long teamId, AbsenceCreateRequest request, CancellationToken ct);

    /// <summary>Deletes your own absence via <c>DELETE /teams/{id}/absences/{absenceId}</c> (<c>teams:write</c>).</summary>
    /// <param name="apiKey">The API key (must carry <c>teams:write</c>).</param>
    /// <param name="teamId">The team id.</param>
    /// <param name="absenceId">The absence id (must be yours).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success (2xx), or a classified error (403/404).</returns>
    Task<ApiResult<bool>> DeleteAbsenceAsync(string apiKey, long teamId, long absenceId, CancellationToken ct);

    /// <summary>Reads the notification feed via <c>GET /notifications</c> (bearer).</summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The feed (newest first), or a classified error.</returns>
    Task<ApiResult<NotificationsResponse>> GetNotificationsAsync(string apiKey, CancellationToken ct);
}
