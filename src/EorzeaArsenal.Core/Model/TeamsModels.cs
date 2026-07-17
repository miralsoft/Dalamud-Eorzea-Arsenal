using System.Text.Json;
using System.Text.Json.Serialization;

namespace EorzeaArsenal.Model;

// Wire models for the Teams companion (Phase C + D). All reads are server-authoritative and
// display-ready — the plugin renders verbatim and does no recurrence/timezone/membership/BiS logic.
// Numeric ids are `long` and tolerate string-or-number (the shared options allow reading numbers
// from strings), because the API is inconsistent (e.g. absence id is a number on GET, a string on
// POST). Unknown fields are ignored (R14), so a future server field never breaks an old plugin.

/// <summary>Scopes for the Teams companion.</summary>
public static class TeamsProtocol
{
    /// <summary>Scope required for the team reads (calendar, sheets, hub, farm, logs, notifications).</summary>
    public const string ReadScope = "teams:read";

    /// <summary>Scope required for the two writes (own RSVP, own absence).</summary>
    public const string WriteScope = "teams:write";

    /// <summary>Notification type raised as a loot toast.</summary>
    public const string NotifLoot = "team.loot";

    /// <summary>Notification type raised as an event reminder toast (1 day / 3 h / 1 h before).</summary>
    public const string NotifReminder = "team.reminder";
}

// --- GET /me/teams ---------------------------------------------------------------------------------

/// <summary>Response of <c>GET /me/teams</c>: the player's active teams and their active mit plans.</summary>
public sealed class TeamsResponse
{
    /// <summary>The player's teams.</summary>
    public List<TeamSummary>? Data { get; init; }
}

/// <summary>A team the player belongs to, with its active mit plans (no plan detail).</summary>
public sealed class TeamSummary
{
    /// <summary>Server team id.</summary>
    public long Id { get; init; }

    /// <summary>Team display name.</summary>
    public string? Name { get; init; }

    /// <summary>Team type (e.g. <c>static</c>).</summary>
    public string? Type { get; init; }

    /// <summary>Active mit plans for the team/plan picker.</summary>
    public List<MitPlanRef>? MitPlans { get; init; }
}

/// <summary>A reference to a mit plan (for the picker; no detail).</summary>
public sealed class MitPlanRef
{
    /// <summary>Plan id.</summary>
    public long Id { get; init; }

    /// <summary>Plan display name.</summary>
    public string? Name { get; init; }

    /// <summary>Boss label.</summary>
    public string? Boss { get; init; }

    /// <summary>The content this plan belongs to.</summary>
    public long ContentId { get; init; }
}

// --- GET /me/calendar ------------------------------------------------------------------------------

/// <summary>Response of <c>GET /me/calendar</c>: fully-expanded occurrences across all teams.</summary>
public sealed class CalendarResponse
{
    /// <summary>The occurrences.</summary>
    public List<CalendarOccurrence>? Data { get; init; }

    /// <summary>The resolved window start (server-derived; informational).</summary>
    public string? From { get; init; }

    /// <summary>The resolved window end (server-derived; informational).</summary>
    public string? To { get; init; }
}

/// <summary>One fully-rendered calendar occurrence. Shown verbatim — never converted/recomputed.</summary>
public sealed class CalendarOccurrence
{
    /// <summary>The team this occurrence belongs to.</summary>
    public long TeamId { get; init; }

    /// <summary>Team display name.</summary>
    public string? TeamName { get; init; }

    /// <summary>The event id (a recurring series or a one-off).</summary>
    public long EventId { get; init; }

    /// <summary>Occurrence kind (e.g. <c>recurring</c>, <c>single</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>Event title.</summary>
    public string? Title { get; init; }

    /// <summary>Linked content id (legacy singular; superseded by <see cref="Contents"/>).</summary>
    public long? ContentId { get; init; }

    /// <summary>Linked content name (legacy singular).</summary>
    public string? ContentName { get; init; }

    /// <summary>Linked content(s) — plural (Phase D 3.6). Show these when present.</summary>
    public List<ContentRef>? Contents { get; init; }

    /// <summary>Occurrence date <c>YYYY-MM-DD</c> in the team timezone (show verbatim).</summary>
    public string? Date { get; init; }

    /// <summary>Start time <c>HH:MM</c> (show verbatim with <see cref="Timezone"/>).</summary>
    public string? Time { get; init; }

    /// <summary>End time <c>HH:MM</c> (show verbatim).</summary>
    public string? EndTime { get; init; }

    /// <summary>Timezone label (e.g. <c>Europe/Berlin</c>) — display only, never convert.</summary>
    public string? Timezone { get; init; }

    /// <summary>Count of <c>yes</c> RSVPs.</summary>
    public int Yes { get; init; }

    /// <summary>Count of <c>maybe</c> RSVPs.</summary>
    public int Maybe { get; init; }

    /// <summary>Count of <c>no</c> RSVPs.</summary>
    public int No { get; init; }

    /// <summary>Total roster size.</summary>
    public int Total { get; init; }

    /// <summary>The player's own status (<c>yes|maybe|no</c>), or <see langword="null"/> if unset.</summary>
    public string? OwnStatus { get; init; }

    /// <summary>A stable key for the new-event seen-set: <c>event_id|date</c>.</summary>
    [JsonIgnore]
    public string SeenKey => $"{EventId}|{Date}";
}

/// <summary>A linked content reference on an event.</summary>
public sealed class ContentRef
{
    /// <summary>Content id.</summary>
    public long Id { get; init; }

    /// <summary>Content name.</summary>
    public string? Name { get; init; }
}

// --- GET /teams/{id}/mit/{planId}/sheet ------------------------------------------------------------

/// <summary>Response of <c>GET /teams/{id}/mit/{planId}/sheet</c>.</summary>
public sealed class MitSheetResponse
{
    /// <summary>The sheet payload.</summary>
    public MitSheet? Data { get; init; }
}

/// <summary>A read-only mit cheat sheet: the plan, its timeline rows, cooldown placements and catalog.</summary>
public sealed class MitSheet
{
    /// <summary>The plan header.</summary>
    public MitPlan? Plan { get; init; }

    /// <summary>Timeline mechanics (rows).</summary>
    public List<MitRow>? Rows { get; init; }

    /// <summary>Cooldowns assigned at a time (placements reference <see cref="MitCooldown"/> via <c>catalog_id</c>).</summary>
    public List<MitPlacement>? Placements { get; init; }

    /// <summary>The cooldown catalog used by this plan (self-contained; resolves each placement).</summary>
    public List<MitCooldown>? Cooldowns { get; init; }
}

/// <summary>The mit plan header.</summary>
public sealed class MitPlan
{
    /// <summary>Plan id.</summary>
    public long Id { get; init; }

    /// <summary>Owning team.</summary>
    public long TeamId { get; init; }

    /// <summary>Boss label.</summary>
    public string? Boss { get; init; }

    /// <summary>Plan name.</summary>
    public string? Name { get; init; }

    /// <summary>Ordered phase names; a row/placement <c>phase</c> is the 0-based index into this.</summary>
    public List<PhaseName>? Phases { get; init; }

    /// <summary>Comma-separated jobs the plan covers (e.g. <c>DRK,WHM</c>).</summary>
    public string? Jobs { get; init; }

    /// <summary>Whether the plan is archived.</summary>
    public bool Archived { get; init; }
}

/// <summary>A localized phase name.</summary>
public sealed class PhaseName
{
    /// <summary>English name.</summary>
    public string? En { get; init; }

    /// <summary>German name.</summary>
    public string? De { get; init; }
}

/// <summary>A timeline mechanic row.</summary>
public sealed class MitRow
{
    /// <summary>Row id.</summary>
    public long Id { get; init; }

    /// <summary>Mechanic label.</summary>
    public string? Label { get; init; }

    /// <summary>Time in seconds from the pull.</summary>
    public int TimeS { get; init; }

    /// <summary>Duration in seconds.</summary>
    public int DurationS { get; init; }

    /// <summary>Tag (e.g. <c>raidwide</c>, <c>tankbuster</c>).</summary>
    public string? Tag { get; init; }

    /// <summary>0-based phase index into <see cref="MitPlan.Phases"/>.</summary>
    public int Phase { get; init; }

    /// <summary>Row colour (<c>#rrggbb</c>), if any.</summary>
    public string? Color { get; init; }
}

/// <summary>A cooldown assigned at a time; resolve its label via <see cref="MitCooldown"/> (<c>catalog_id</c>).</summary>
public sealed class MitPlacement
{
    /// <summary>Placement id.</summary>
    public long Id { get; init; }

    /// <summary>Owning plan.</summary>
    public long PlanId { get; init; }

    /// <summary>The job that uses the cooldown.</summary>
    public string? Job { get; init; }

    /// <summary>The cooldown catalog id (resolve via <see cref="MitSheet.Cooldowns"/>).</summary>
    public long CatalogId { get; init; }

    /// <summary>Time in seconds from the pull.</summary>
    public int TimeS { get; init; }

    /// <summary>0-based phase index.</summary>
    public int Phase { get; init; }
}

/// <summary>A catalog cooldown resolving a placement to a label + game action.</summary>
public sealed class MitCooldown
{
    /// <summary>Catalog id (matches <see cref="MitPlacement.CatalogId"/>).</summary>
    public long Id { get; init; }

    /// <summary>Job.</summary>
    public string? Job { get; init; }

    /// <summary>English cooldown name.</summary>
    public string? Name { get; init; }

    /// <summary>German cooldown name.</summary>
    public string? NameDe { get; init; }

    /// <summary>The game action id — use it to fetch the skill icon.</summary>
    public uint ActionId { get; init; }

    /// <summary>Server-relative fallback icon path (prefix the app URL).</summary>
    public string? Icon { get; init; }

    /// <summary>Effect duration in seconds.</summary>
    public int DurationS { get; init; }

    /// <summary>Recast (cooldown) in seconds.</summary>
    public int RecastS { get; init; }
}

// --- GET /teams/{id}/content/sheet -----------------------------------------------------------------

/// <summary>Response of <c>GET /teams/{id}/content/sheet</c>: the full content hub.</summary>
public sealed class ContentSheetResponse
{
    /// <summary>The content entries (fights) for the team.</summary>
    public List<ContentEntry>? Data { get; init; }
}

/// <summary>One content/fight with its bosses+drops and its resources.</summary>
public sealed class ContentEntry
{
    /// <summary>Content id.</summary>
    public long Id { get; init; }

    /// <summary>Content name.</summary>
    public string? Name { get; init; }

    /// <summary>Content type (e.g. <c>savage</c>).</summary>
    public string? Type { get; init; }

    /// <summary>Status (e.g. <c>active</c>).</summary>
    public string? Status { get; init; }

    /// <summary>Number of resources.</summary>
    public int ResourceCount { get; init; }

    /// <summary>Number of mit plans.</summary>
    public int MitCount { get; init; }

    /// <summary>Number of events.</summary>
    public int EventCount { get; init; }

    /// <summary>The bosses/floors, each with its curated drops.</summary>
    public List<BossEntry>? Bosses { get; init; }

    /// <summary>The resources (plans, videos, links, notes, files).</summary>
    public List<ResourceEntry>? Resources { get; init; }
}

/// <summary>A boss/floor within a content, with its curated drop labels.</summary>
public sealed class BossEntry
{
    /// <summary>Boss key: <c>g:&lt;id&gt;</c> (global catalog) or <c>t:&lt;id&gt;</c> (team-custom).</summary>
    public string? Key { get; init; }

    /// <summary>Boss name.</summary>
    public string? Name { get; init; }

    /// <summary>Floor number, or <see langword="null"/>.</summary>
    public int? Floor { get; init; }

    /// <summary>Curated drop labels (not game item ids).</summary>
    public List<DropEntry>? Drops { get; init; }

    /// <summary>Source (<c>global</c> or team-custom).</summary>
    public string? Source { get; init; }
}

/// <summary>A curated drop label.</summary>
public sealed class DropEntry
{
    /// <summary>Drop kind: <c>slot | mat | cosmetic</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Short human label (e.g. <c>Earrings</c>, <c>Weapon Coffer</c>).</summary>
    public string? Value { get; init; }
}

/// <summary>A team resource: a plan/video/link (url), a note (text), or an uploaded file (mime + path).</summary>
public sealed class ResourceEntry
{
    /// <summary>Resource id.</summary>
    public long Id { get; init; }

    /// <summary>Kind: <c>link | video | note | plan | file</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Title.</summary>
    public string? Title { get; init; }

    /// <summary>External url for <c>link|video|plan</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Note body text for <c>note</c>.</summary>
    public string? Text { get; init; }

    /// <summary>MIME type for <c>file</c> (e.g. <c>image/png</c>, <c>application/pdf</c>).</summary>
    public string? Mime { get; init; }

    /// <summary>Server-relative streaming path for <c>file</c> (see the file endpoint).</summary>
    public string? File { get; init; }

    /// <summary>Whether this resource is an inline-renderable image.</summary>
    [JsonIgnore]
    public bool IsImage => Kind == "file" && Mime is { } m && m.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

// --- GET /teams/{id}/farm --------------------------------------------------------------------------

/// <summary>Response of <c>GET /teams/{id}/farm</c>: who-needs-what across the team.</summary>
public sealed class FarmResponse
{
    /// <summary>One entry per shared gearset / proxy with a BiS target.</summary>
    public List<FarmEntry>? Data { get; init; }
}

/// <summary>A member/character/job's equipped items vs the resolved BiS target.</summary>
public sealed class FarmEntry
{
    /// <summary>Sharing user id (<c>"0"</c> for a team proxy).</summary>
    public string? SharedBy { get; init; }

    /// <summary>Roster entry id (set for a proxy; <see langword="null"/> otherwise).</summary>
    public long? RosterEntryId { get; init; }

    /// <summary>Character id (<see langword="null"/> for a proxy).</summary>
    public long? CharacterId { get; init; }

    /// <summary>Character name.</summary>
    public string? CharacterName { get; init; }

    /// <summary>Member (account) display name.</summary>
    public string? Member { get; init; }

    /// <summary>Job.</summary>
    public string? Job { get; init; }

    /// <summary>Gearset name.</summary>
    public string? Name { get; init; }

    /// <summary>Equipped items, slot → { id }.</summary>
    public Dictionary<string, FarmSlot>? Equipped { get; init; }

    /// <summary>Target (BiS) items, slot → { id }; <see langword="null"/> if no BiS resolved.</summary>
    public Dictionary<string, FarmSlot>? Target { get; init; }

    /// <summary>BiS target name.</summary>
    public string? TargetName { get; init; }

    /// <summary>Whether this is a core (Stamm) member.</summary>
    public bool IsCore { get; init; }

    /// <summary>Status key (<c>core</c> or a substitute key) for ranking.</summary>
    public string? StatusKey { get; init; }

    /// <summary>Localized status (German), if any.</summary>
    public string? StatusDe { get; init; }

    /// <summary>Localized status (English), if any.</summary>
    public string? StatusEn { get; init; }
}

/// <summary>An item reference inside an equipped/target slot map.</summary>
public sealed class FarmSlot
{
    /// <summary>Item id (a game item id; may be 0/absent).</summary>
    public long Id { get; init; }
}

// --- GET /teams/{id}/logs (FFLogs) -----------------------------------------------------------------

/// <summary>Response of <c>GET /teams/{id}/logs</c>: the FFLogs mirror (best-effort, never fatal).</summary>
public sealed class LogsResponse
{
    /// <summary>The logs payload.</summary>
    public LogsData? Data { get; init; }
}

/// <summary>FFLogs connection state + recent reports.</summary>
public sealed class LogsData
{
    /// <summary>Connection state.</summary>
    public LogsConnection? Connection { get; init; }

    /// <summary>Recent reports (empty when not connected).</summary>
    public List<LogReport>? Reports { get; init; }

    /// <summary>Non-fatal error (<c>null | not_configured | fetch_failed</c>).</summary>
    public string? Error { get; init; }
}

/// <summary>FFLogs connection descriptor.</summary>
public sealed class LogsConnection
{
    /// <summary>Whether anything is linked.</summary>
    public bool Connected { get; init; }

    /// <summary>Connection kind (<c>guild | guild_name | report</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>Human label.</summary>
    public string? Label { get; init; }

    /// <summary>FFLogs URL to open in the browser.</summary>
    public string? Url { get; init; }
}

/// <summary>One FFLogs report.</summary>
public sealed class LogReport
{
    /// <summary>Report code (FFLogs).</summary>
    public string? Code { get; init; }

    /// <summary>Report title.</summary>
    public string? Title { get; init; }

    /// <summary>Start time (unix seconds).</summary>
    public long StartTime { get; init; }

    /// <summary>Zone name.</summary>
    public string? Zone { get; init; }

    /// <summary>Total kills.</summary>
    public int Kills { get; init; }

    /// <summary>Total wipes.</summary>
    public int Wipes { get; init; }

    /// <summary>Per-boss kill/wipe breakdown.</summary>
    public List<LogBoss>? Bosses { get; init; }
}

/// <summary>Per-boss kill/wipe counts in a report.</summary>
public sealed class LogBoss
{
    /// <summary>Boss name.</summary>
    public string? Name { get; init; }

    /// <summary>Kills.</summary>
    public int Kills { get; init; }

    /// <summary>Wipes.</summary>
    public int Wipes { get; init; }
}

// --- Writes: RSVP + absence ------------------------------------------------------------------------

/// <summary>Body of <c>POST /teams/{id}/events/{eventId}/attendance</c> (set your own status).</summary>
public sealed class AttendanceRequest
{
    /// <summary>The occurrence date <c>YYYY-MM-DD</c>.</summary>
    public required string OccurrenceDate { get; init; }

    /// <summary>The new status: <c>yes | maybe | no</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Optional reason (typically on <c>no</c>).</summary>
    public string? Reason { get; init; }
}

/// <summary>A simple <c>{ "status": "ok" }</c> acknowledgement.</summary>
public sealed class StatusAck
{
    /// <summary><c>"ok"</c> on success.</summary>
    public string? Status { get; init; }
}

/// <summary>One absence (vacation) range.</summary>
public sealed class Absence
{
    /// <summary>Absence id.</summary>
    public long Id { get; init; }

    /// <summary>Owning user id.</summary>
    public long UserId { get; init; }

    /// <summary>Roster entry id (0/absent when tied to the account).</summary>
    public long? RosterEntryId { get; init; }

    /// <summary>Start date <c>YYYY-MM-DD</c>.</summary>
    public string? FromDate { get; init; }

    /// <summary>End date <c>YYYY-MM-DD</c>.</summary>
    public string? ToDate { get; init; }

    /// <summary>Optional note.</summary>
    public string? Note { get; init; }
}

/// <summary>Body of <c>POST /teams/{id}/absences</c> (report your own vacation range).</summary>
public sealed class AbsenceCreateRequest
{
    /// <summary>Start date <c>YYYY-MM-DD</c>.</summary>
    public required string FromDate { get; init; }

    /// <summary>End date <c>YYYY-MM-DD</c>.</summary>
    public required string ToDate { get; init; }

    /// <summary>Optional note.</summary>
    public string? Note { get; init; }
}

/// <summary>Response of <c>POST /teams/{id}/absences</c>: <c>{ "data": { "id": "9" } }</c>.</summary>
public sealed class AbsenceCreateResponse
{
    /// <summary>The created entry.</summary>
    public AbsenceCreated? Data { get; init; }
}

/// <summary>The created absence's id.</summary>
public sealed class AbsenceCreated
{
    /// <summary>New absence id.</summary>
    public long Id { get; init; }
}

/// <summary>
/// Response of <c>GET /teams/{id}/absences</c>. The endpoint returns a bare JSON array; this wrapper
/// tolerates both a bare array and a <c>{ "data": [...] }</c> envelope (see <see cref="AbsencesConverter"/>).
/// </summary>
[JsonConverter(typeof(AbsencesConverter))]
public sealed class AbsencesResponse
{
    /// <summary>The absence ranges.</summary>
    public List<Absence> Items { get; init; } = [];
}

/// <summary>Reads <see cref="AbsencesResponse"/> from either a bare array or a <c>{data:[…]}</c> object.</summary>
public sealed class AbsencesConverter : JsonConverter<AbsencesResponse>
{
    /// <inheritdoc />
    public override AbsencesResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data
                : default;

        var items = new List<Absence>();
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                var absence = element.Deserialize<Absence>(options);
                if (absence is not null)
                {
                    items.Add(absence);
                }
            }
        }

        return new AbsencesResponse { Items = items };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AbsencesResponse value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Items, options);
}

/// <summary>A streamed resource file: its bytes and MIME type (from <c>Content-Type</c>).</summary>
public sealed class ResourceFile
{
    /// <summary>The raw file bytes.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>The MIME type reported by the server (e.g. <c>image/png</c>), or <see langword="null"/>.</summary>
    public string? Mime { get; init; }
}

// --- GET /notifications ----------------------------------------------------------------------------

/// <summary>Response of <c>GET /notifications</c>: the user's notification feed.</summary>
public sealed class NotificationsResponse
{
    /// <summary>The notifications, newest first (descending id).</summary>
    public List<NotificationEntry>? Data { get; init; }

    /// <summary>Number of unread notifications.</summary>
    public int Unread { get; init; }
}

/// <summary>One notification feed entry.</summary>
public sealed class NotificationEntry
{
    /// <summary>Stable id (descending — newest first); use for the seen-set.</summary>
    public long Id { get; init; }

    /// <summary>Dotted type (e.g. <c>team.loot</c>, <c>team.reminder</c>, <c>team.event</c>).</summary>
    public string? Type { get; init; }

    /// <summary>Title.</summary>
    public string? Title { get; init; }

    /// <summary>Body text.</summary>
    public string? Body { get; init; }

    /// <summary>Relative app path to open (prefix the app URL).</summary>
    public string? Link { get; init; }

    /// <summary>Team id (may be <see langword="null"/> for non-team notifications).</summary>
    public long? TeamId { get; init; }

    /// <summary>When the user read it, or <see langword="null"/> if unread.</summary>
    public string? ReadAt { get; init; }

    /// <summary>Server-local created timestamp <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string? CreatedAt { get; init; }
}
