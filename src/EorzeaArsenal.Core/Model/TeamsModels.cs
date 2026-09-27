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

    /// <summary>
    /// How far the team is from its target line-up, per role (Phase E). <see langword="null"/> from a
    /// server that does not send it yet, which is not the same as "nothing missing".
    /// </summary>
    public LineupSummary? LineupSummary { get; init; }

    /// <summary>The caller's own rights in this team; the owner holds every one.</summary>
    /// <remarks>
    /// The only source for "may I": an action is offered when its right is in this list and at no other
    /// time. In particular no right is read from some other field happening to be present, such as the
    /// join-request count that only managers are sent. A server that sends no list grants nothing here.
    /// </remarks>
    public List<string>? Capabilities { get; init; }

    /// <summary>Whether the caller holds a right in this team.</summary>
    /// <param name="capability">One of <see cref="TeamCapability"/>.</param>
    /// <returns><see langword="true"/> only when the server listed it.</returns>
    public bool Can(string capability) =>
        Capabilities is { } held && held.Contains(capability, StringComparer.Ordinal);
}

/// <summary>The team rights this plugin asks about.</summary>
public static class TeamCapability
{
    /// <summary>May manage members, which includes creating invitation links.</summary>
    public const string ManageMembers = "manage_members";
}

/// <summary>A team's targets and what is still missing against them, as the team list shows it.</summary>
public sealed class LineupSummary
{
    /// <summary>The target per role. A target of 0 means the team sets none for that role.</summary>
    public RoleCounts? Targets { get; init; }

    /// <summary>How many are missing per role: <c>max(0, target - count)</c>, computed by the server.</summary>
    public RoleCounts? Missing { get; init; }
}

/// <summary>One number per role. The roles are tank, healer and DPS (melee, ranged and caster together).</summary>
public sealed class RoleCounts
{
    /// <summary>Tanks.</summary>
    public int Tank { get; init; }

    /// <summary>Healers.</summary>
    public int Healer { get; init; }

    /// <summary>DPS of every kind.</summary>
    public int Dps { get; init; }
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

    /// <summary>The content this plan belongs to, or <see langword="null"/> if it isn't linked to one.</summary>
    public long? ContentId { get; init; }
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

/// <summary>A team resource: a plan/video/link (<c>url</c>), a note (<c>body</c>), or an uploaded file.</summary>
public sealed class ResourceEntry
{
    /// <summary>Resource id (used to build the file download URL).</summary>
    public long Id { get; init; }

    /// <summary>The content this resource belongs to.</summary>
    public long ContentId { get; init; }

    /// <summary>Kind: <c>link | video | note | plan | file</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Title.</summary>
    public string? Title { get; init; }

    /// <summary>External url for <c>link|video|plan</c> (<see langword="null"/> for note/file).</summary>
    public string? Url { get; init; }

    /// <summary>Note body text for <c>note</c> (plain text; <see langword="null"/> otherwise).</summary>
    public string? Body { get; init; }

    /// <summary>Sort order.</summary>
    public int Sort { get; init; }

    /// <summary>Original file name for <c>file</c>.</summary>
    public string? FileName { get; init; }

    /// <summary>MIME type for <c>file</c> (e.g. <c>image/png</c>, <c>application/pdf</c>).</summary>
    public string? Mime { get; init; }

    /// <summary>File size in bytes for <c>file</c>.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Whether this resource is an inline-renderable image (a <c>file</c> with an image MIME).</summary>
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

    /// <summary>
    /// Per target slot: does this member already <b>have</b> the piece, worn or not. The one fact
    /// <see cref="Equipped"/> cannot give — someone who owns the augmented neck but still wears the
    /// base looks from outside exactly like someone who owns nothing.
    /// </summary>
    /// <remarks>
    /// Held means worn (rings count as a pair), ticked off by the team, or marked as a
    /// <c>tomeplus</c>/<c>raid</c> tier. Deliberately not held: the <c>tome</c> tier, which says the
    /// base is there and the upgrade is not — the one case where "upgrade it" is the right advice.
    /// Slots the target does not name are absent rather than <see langword="false"/>. Says nothing
    /// about bags, materials or tomestones, and only appears for sets already shared with the team.
    /// </remarks>
    public Dictionary<string, bool>? Owned { get; init; }

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

/// <summary>
/// An item reference inside an equipped/target slot map. Target slots are additionally annotated by the
/// server (via <c>SourcingConfig::describe</c>) with the ways to get the piece — the same
/// <c>{ id, source, routes[] }</c> shape the web farm plan, obtain popup and <c>GET /gear/obtain</c>
/// use. Every sourcing field is absent for gear the admin-curated tier config does not know (older
/// gear, crafted pieces, an unconfigured tier), so treat <see cref="Source"/>/<see cref="Routes"/> as
/// optional and fall back to just the item.
/// </summary>
public sealed class FarmSlot
{
    /// <summary>Item id (a game item id; may be 0/absent).</summary>
    public long Id { get; init; }

    /// <summary>Acquisition kind: <c>tome</c>, <c>tomeplus</c> or <c>savage</c>; <see langword="null"/> when unknown.</summary>
    public string? Source { get; init; }

    /// <summary>The ways to get the piece, best/normal first; <see langword="null"/> when unknown.</summary>
    public List<FarmRoute>? Routes { get; init; }
}

/// <summary>One way to obtain a piece: a raid drop, a vendor trade, a craft, the market board, or gone.</summary>
public sealed class FarmRoute
{
    /// <summary>Route kind: <c>drop</c>, <c>trade</c>, <c>craft</c>, <c>market</c> or <c>retired</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The coffer the drop arrives as (<c>drop</c>).</summary>
    public FarmVia? Via { get; init; }

    /// <summary>The fights it drops in (<c>drop</c>).</summary>
    public List<string>? Duties { get; init; }

    /// <summary>
    /// The game's own <c>InstanceContent</c> ids for those fights, so a client can name each one in the
    /// player's language instead of matching English spellings. <b>Not</b> index-aligned with
    /// <see cref="Duties"/>: a duty the server has no id for is simply absent, so this list can be
    /// shorter or missing entirely.
    /// </summary>
    public List<long>? DutyContentIds { get; init; }

    /// <summary>The price components (<c>trade</c>/<c>craft</c>).</summary>
    public List<FarmCostPart>? Cost { get; init; }

    /// <summary>The vendors who take the trade (<c>trade</c>).</summary>
    public List<FarmNpc>? Npc { get; init; }

    /// <summary>Shop names, when there is no named NPC (<c>trade</c>).</summary>
    public List<string>? Shops { get; init; }
}

/// <summary>The coffer a drop arrives as (also a real item id, deep-linkable to <c>/item/{id}</c>).</summary>
public sealed class FarmVia
{
    /// <summary>Coffer item id.</summary>
    public long Id { get; init; }

    /// <summary>Coffer name.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// One component of a price. A <c>currency</c>/<c>token</c>/<c>material</c> part names a real item
/// (<see cref="Id"/>/<see cref="Name"/>); a <c>piece</c> part is a gear slot handed in — not effort
/// still ahead — and, when the server walked the chain, carries its own <see cref="Chain"/>.
/// </summary>
public sealed class FarmCostPart
{
    /// <summary>How many are needed.</summary>
    public int Count { get; init; }

    /// <summary>Role: <c>currency</c>, <c>token</c>, <c>material</c> or <c>piece</c>.</summary>
    public string? Role { get; init; }

    /// <summary>Item id (currency/token/material).</summary>
    public long? Id { get; init; }

    /// <summary>Item name (currency/token/material); may be <see langword="null"/> — fall back to the id.</summary>
    public string? Name { get; init; }

    /// <summary>The gear slot handed in (<c>piece</c>).</summary>
    public string? Slot { get; init; }

    /// <summary>How that handed-in slot is acquired (<c>piece</c>).</summary>
    public string? Acq { get; init; }

    /// <summary>
    /// The tier's base item ids for a handed-in <c>piece</c> — every job's variant of that slot+acq —
    /// so the plugin can look ownership up. Present alongside <see cref="Slot"/>/<see cref="Acq"/>.
    /// </summary>
    public List<long>? Ids { get; init; }

    /// <summary>How to get the handed-in piece, one level deeper (only when the chain was walked).</summary>
    public List<FarmRoute>? Chain { get; init; }
}

/// <summary>A vendor NPC that takes a trade.</summary>
public sealed class FarmNpc
{
    /// <summary>NPC id.</summary>
    public long Id { get; init; }

    /// <summary>NPC name.</summary>
    public string? Name { get; init; }

    /// <summary>Zone the NPC stands in.</summary>
    public string? Zone { get; init; }

    /// <summary>TerritoryType id of the zone (for a map marker; may be absent).</summary>
    public long? ZoneId { get; init; }

    /// <summary>Map row id (for a map marker; may be absent).</summary>
    public long? MapId { get; init; }

    /// <summary>Map X coordinate (may be absent).</summary>
    public float? X { get; init; }

    /// <summary>Map Y coordinate (may be absent).</summary>
    public float? Y { get; init; }

    /// <summary>Whether this NPC can be pinned on the in-game map (has zone, map and coordinates).</summary>
    public bool CanMap => ZoneId is > 0 && MapId is > 0 && X is not null && Y is not null;
}

// --- GET /gear/obtain (impersonal "where do I get this") -------------------------------------------

/// <summary>
/// Response of <c>GET /gear/obtain?item_ids=…</c>: per requested item id (as a string key), how to get
/// it — or <see langword="null"/> when nothing is curated for that id ("no info yet", never
/// "unobtainable"). The value is the same <c>{ source, slot, routes[] }</c> shape a farm target
/// carries, with the cost chain walked, so the sourcing renderer is shared.
/// </summary>
public sealed class ObtainResponse
{
    /// <summary>Item id (string) → its sourcing, or <see langword="null"/> when uncurated.</summary>
    public Dictionary<string, ObtainInfo?>? Data { get; init; }
}

/// <summary>How a single gear piece is obtained: its acquisition kind, slot and the routes in.</summary>
public sealed class ObtainInfo
{
    /// <summary>Acquisition kind: <c>tome</c>, <c>tomeplus</c> or <c>savage</c>.</summary>
    public string? Source { get; init; }

    /// <summary>The gear slot this piece fills.</summary>
    public string? Slot { get; init; }

    /// <summary>The ways to get it, best/normal first (chain walked).</summary>
    public List<FarmRoute>? Routes { get; init; }
}

/// <summary>
/// Response of <c>GET /me/holdings?item_ids=…</c>: the caller's own character's owned quantity per
/// item id (string key), summed across every synced storage — bags, saddlebag, equipped, armoury and
/// each retainer as of its last visit. A missing/zero id is <c>0</c>.
/// </summary>
public sealed class HoldingsResponse
{
    /// <summary>Item id (string) → owned quantity.</summary>
    public Dictionary<string, int>? Data { get; init; }

    /// <summary>
    /// With <c>&amp;breakdown=1</c>: item id (string) → where the count sits, one entry per stack. The
    /// sum answers "do I have it", this answers "where do I go to get it". Absent without the flag.
    /// </summary>
    public Dictionary<string, List<HoldingStack>>? Breakdown { get; init; }
}

/// <summary>One stack of an owned item: which storage it sits in, and how many.</summary>
public sealed class HoldingStack
{
    /// <summary>The reconciliation scope: <c>character</c> or <c>retainer:&lt;source_id&gt;</c>.</summary>
    public string? Scope { get; init; }

    /// <summary>The finer storage tag as it was uploaded (<c>bags</c>, <c>saddlebag</c>, <c>retainer</c>…).</summary>
    public string? Container { get; init; }

    /// <summary>Retainer id for a retainer stack, otherwise empty.</summary>
    public string? SourceId { get; init; }

    /// <summary>The retainer's name, when one was reported with a scan.</summary>
    public string? SourceName { get; init; }

    /// <summary>How many sit here.</summary>
    public int Qty { get; init; }
}

/// <summary>
/// Response of <c>GET /gear/tracked-items</c>: the consumable ids (raid books/tokens, upgrade
/// materials) the plugin should also report in the inventory sync so holdings has their counts, plus
/// the active tier's classified <see cref="Groups"/> for a "your stock" display.
/// </summary>
public sealed class TrackedItemsResponse
{
    /// <summary>The item ids to track — every tier, since books and materials outlive their tier.</summary>
    public List<long>? Data { get; init; }

    /// <summary>
    /// The <b>active</b> tier's items only, classified and in display order (materials, then the
    /// upgrade stone, then the raid books). Absent on an older server, which just means no stock view.
    /// </summary>
    public List<TrackedItemGroup>? Groups { get; init; }
}

/// <summary>One display group of tracked items: what kind they are and which ids belong to it.</summary>
public sealed class TrackedItemGroup
{
    /// <summary>The kind: <c>material</c>, <c>stone</c> or <c>book</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The item ids in this group.</summary>
    public List<long>? Ids { get; init; }
}

/// <summary>Body of <c>PUT /me/tome-balance</c>: the caller's capped-tomestone balance for a character.</summary>
public sealed class TomeBalanceRequest
{
    /// <summary>Capped-tomestone count (server clamps to 0…9999).</summary>
    public int Balance { get; init; }

    /// <summary>The character the balance belongs to (must be the caller's own).</summary>
    public long CharacterId { get; init; }

    /// <summary>The plugin build that produced this write (see <see cref="ProtocolConstants.PluginVersion"/>).</summary>
    public string PluginVersion { get; init; } = ProtocolConstants.PluginVersion;
}

/// <summary>Response of <c>GET/PUT /me/tome-balance</c>: the stored balance echo.</summary>
public sealed class TomeBalanceResponse
{
    /// <summary>The balance payload.</summary>
    public TomeBalanceData? Data { get; init; }
}

/// <summary>The stored capped-tomestone balance (<see langword="null"/> when never set).</summary>
public sealed class TomeBalanceData
{
    /// <summary>The balance.</summary>
    public int? Balance { get; init; }
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

    /// <summary>Start time (unix seconds). The JSON key is camelCase (<c>startTime</c>), unlike the rest.</summary>
    [JsonPropertyName("startTime")]
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

// --- Phase E: GET /teams/{id}/lineup ---------------------------------------------------------------
//
// Everything here is computed by the server and shown as delivered. "What is missing" and "which
// positions a date leaves open" are the website's numbers too, and the two must agree, so none of it is
// derived on this side. These responses carry the names of team members: they are held in memory while
// shown and never written anywhere.

/// <summary>Response of <c>GET /teams/{id}/lineup</c>.</summary>
public sealed class LineupResponse
{
    /// <summary>The team's line-up.</summary>
    public Lineup? Data { get; init; }
}

/// <summary>A team's line-up: targets, who fills which role and position, and the next date with a gap.</summary>
public sealed class Lineup
{
    /// <summary>The target per role, 0 to 8; 0 means no target.</summary>
    public RoleCounts? Targets { get; init; }

    /// <summary>Whether the team takes Blue Mage and Beastmaster sets.</summary>
    public LineupAllow? Allow { get; init; }

    /// <summary>One entry per role.</summary>
    public LineupRoles? Roles { get; init; }

    /// <summary>How many are missing per role, the same numbers as in each role entry.</summary>
    public RoleCounts? Missing { get; init; }

    /// <summary>Head counts for the team.</summary>
    public LineupCounts? Counts { get; init; }

    /// <summary>Every character and placeholder in the team.</summary>
    public List<LineupCharacter>? Characters { get; init; }

    /// <summary>The first date in the next 60 days that leaves a position open, or <see langword="null"/>.</summary>
    public OpenDate? NextOpen { get; init; }
}

/// <summary>Which limited jobs a team takes.</summary>
public sealed class LineupAllow
{
    /// <summary>Blue Mage.</summary>
    public bool Blu { get; init; }

    /// <summary>Beastmaster.</summary>
    public bool Bst { get; init; }
}

/// <summary>The three role entries of a line-up.</summary>
public sealed class LineupRoles
{
    /// <summary>Tanks.</summary>
    public LineupRole? Tank { get; init; }

    /// <summary>Healers.</summary>
    public LineupRole? Healer { get; init; }

    /// <summary>DPS of every kind.</summary>
    public LineupRole? Dps { get; init; }
}

/// <summary>One role of a line-up.</summary>
public sealed class LineupRole
{
    /// <summary>
    /// Core characters (neither paused nor blocked) and core placeholders filling this role. A character
    /// sharing sets for two roles counts in both. Substitutes are not counted.
    /// </summary>
    public int Count { get; init; }

    /// <summary>The target for this role, 0 meaning none.</summary>
    public int Target { get; init; }

    /// <summary><c>max(0, target - count)</c>, from the server.</summary>
    public int Missing { get; init; }

    /// <summary>Job codes present in this role.</summary>
    public List<string>? Jobs { get; init; }

    /// <summary>
    /// The positions those jobs hold: spots, not people. A job held by two characters with different
    /// positions appears twice. Always a list, empty when none.
    /// </summary>
    public List<LineupPosition>? Positions { get; init; }

    /// <summary>
    /// Job codes of this role that no core character plays, where that character is neither paused nor
    /// blocked. Listed only while the role still needs somebody. Substitutes do not count, so a job a
    /// substitute plays can appear here: it is missing <b>from the core</b>, and must be labelled so.
    /// </summary>
    public List<string>? NotPresent { get; init; }
}

/// <summary>A job and the position it holds, such as <c>SCH</c> as <c>H1</c>.</summary>
public sealed class LineupPosition
{
    /// <summary>The job code.</summary>
    public string? Job { get; init; }

    /// <summary>The position, free text.</summary>
    public string? Position { get; init; }
}

/// <summary>Head counts of a team.</summary>
public sealed class LineupCounts
{
    /// <summary>Active members, counted as players.</summary>
    public int Members { get; init; }

    /// <summary>Core characters.</summary>
    [JsonPropertyName("stamm")]
    public int Core { get; init; }

    /// <summary>Substitute characters.</summary>
    [JsonPropertyName("ersatz")]
    public int Substitutes { get; init; }

    /// <summary>Characters that share no gearset with the team.</summary>
    public int SharingNothing { get; init; }

    /// <summary>
    /// Open join requests, sent only to callers who may manage members. Shown when present, and never read
    /// as a statement about anybody's rights: that is what the team's own capability list is for.
    /// </summary>
    public int? Pending { get; init; }
}

/// <summary>A character or placeholder in a team's line-up.</summary>
public sealed class LineupCharacter
{
    /// <summary><c>character</c> or <c>placeholder</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The owning player's id, when it is a character.</summary>
    public long? UserId { get; init; }

    /// <summary>The character id, when it is a character.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The character's name.</summary>
    public string? Name { get; init; }

    /// <summary>The player's display name.</summary>
    public string? Member { get; init; }

    /// <summary>Whether this is a core character.</summary>
    public bool IsCore { get; init; }

    /// <summary>Whether it is paused.</summary>
    public bool Paused { get; init; }

    /// <summary>Whether it is blocked.</summary>
    public bool Blocked { get; init; }

    /// <summary>The position, free text, or <see langword="null"/>.</summary>
    public string? Position { get; init; }

    /// <summary>The jobs it shares sets for.</summary>
    public List<string>? Jobs { get; init; }

    /// <summary>The roles those jobs belong to.</summary>
    public List<string>? Roles { get; init; }

    /// <summary>Whether it shares no gearset at all.</summary>
    public bool SharesNothing { get; init; }

    /// <summary>Whether this is a placeholder rather than a real character.</summary>
    [JsonIgnore]
    public bool IsPlaceholder => string.Equals(Kind, "placeholder", StringComparison.Ordinal);
}

/// <summary>A date that leaves positions open, as the line-up points to it.</summary>
public sealed class OpenDate
{
    /// <summary>The event id. Read from a number or a string, since the two responses disagreed at first.</summary>
    public long EventId { get; init; }

    /// <summary>The event title.</summary>
    public string? Title { get; init; }

    /// <summary>The date, <c>YYYY-MM-DD</c>.</summary>
    public string? Date { get; init; }

    /// <summary>The time, <c>HH:MM</c>.</summary>
    public string? Time { get; init; }

    /// <summary>The positions it leaves open.</summary>
    public List<CoverageEntry>? Open { get; init; }
}

// --- Phase E: GET /teams/{id}/events/{eventId}/coverage?date= --------------------------------------

/// <summary>Response of the coverage read for one date of one event.</summary>
public sealed class CoverageResponse
{
    /// <summary>The coverage.</summary>
    public Coverage? Data { get; init; }
}

/// <summary>Which positions a date leaves open, which are unsure, which are covered, and who could step in.</summary>
public sealed class Coverage
{
    /// <summary>The date, <c>YYYY-MM-DD</c>.</summary>
    public string? Date { get; init; }

    /// <summary>The event id.</summary>
    public long EventId { get; init; }

    /// <summary>The event title.</summary>
    public string? Title { get; init; }

    /// <summary>The time, <c>HH:MM</c>.</summary>
    public string? Time { get; init; }

    /// <summary>Positions a core character leaves open that date.</summary>
    public List<CoverageEntry>? Open { get; init; }

    /// <summary>Players who answered "maybe".</summary>
    public List<CoverageEntry>? Unsure { get; init; }

    /// <summary>Positions a substitute covers.</summary>
    public List<CoverageEntry>? Covered { get; init; }

    /// <summary>Who could step in, sent only while something is open; matching roles first.</summary>
    public List<CoverageSuggestion>? Suggestions { get; init; }
}

/// <summary>One position in a coverage list.</summary>
public sealed class CoverageEntry
{
    /// <summary><c>character</c> or <c>placeholder</c>.</summary>
    public string? Kind { get; init; }

    /// <summary><c>u&lt;userId&gt;</c> or <c>r&lt;rosterId&gt;</c>.</summary>
    public string? ParticipantKey { get; init; }

    /// <summary>The character id, when it is a character.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The roster entry id, when it is a placeholder.</summary>
    public long? RosterEntryId { get; init; }

    /// <summary>The character's name.</summary>
    public string? Name { get; init; }

    /// <summary>The player's display name.</summary>
    public string? Member { get; init; }

    /// <summary>The position, or <see langword="null"/>; then the name stands in for it.</summary>
    public string? Position { get; init; }

    /// <summary>The roles it fills.</summary>
    public List<string>? Roles { get; init; }

    /// <summary>
    /// <c>declined</c>, <c>absent</c>, or <c>away</c> when the caller may not see other people's absences.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>The reason text, sent only when the caller may see it. Shown only when present.</summary>
    public string? Note { get; init; }

    /// <summary>The substitute's name, on a covered position.</summary>
    public string? Substitute { get; init; }

    /// <summary>The substitute's character id, on a covered position.</summary>
    public long? SubstituteCharacterId { get; init; }
}

/// <summary>Somebody who could step in on a date.</summary>
public sealed class CoverageSuggestion
{
    /// <summary><c>character</c> or <c>placeholder</c>.</summary>
    public string? Kind { get; init; }

    /// <summary><c>u&lt;userId&gt;</c> or <c>r&lt;rosterId&gt;</c>.</summary>
    public string? ParticipantKey { get; init; }

    /// <summary>The character id, when it is a character.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The roster entry id, when it is a placeholder.</summary>
    public long? RosterEntryId { get; init; }

    /// <summary>The character's name.</summary>
    public string? Name { get; init; }

    /// <summary>The player's display name.</summary>
    public string? Member { get; init; }

    /// <summary>Its position, if any.</summary>
    public string? Position { get; init; }

    /// <summary>The jobs it shares sets for.</summary>
    public List<string>? Jobs { get; init; }

    /// <summary>The roles those jobs belong to.</summary>
    public List<string>? Roles { get; init; }

    /// <summary>Whether it shares a set in the role of an open position.</summary>
    public bool Matches { get; init; }
}

// --- Phase E: POST /teams/{id}/invite --------------------------------------------------------------
//
// The one write of Phase E. The answer carries the invitation code, which is a secret: it is shown once,
// put on the clipboard when asked, and never stored, logged or written into a diagnostics report. The
// website lists open invitations without it, so there is nothing this side would ever need it for later.

/// <summary>Body of <c>POST /teams/{id}/invite</c> for a link.</summary>
public sealed class InviteRequest
{
    /// <summary>The fewest uses a link may be given.</summary>
    public const int MinUses = 1;

    /// <summary>The most uses a link may be given.</summary>
    public const int MaxUsesLimit = 100;

    /// <summary>The shortest life a link may be given, in days.</summary>
    public const int MinDays = 1;

    /// <summary>The longest life a link may be given, in days.</summary>
    public const int MaxDays = 30;

    /// <summary>What the server would pick if both were left out: one use, seven days.</summary>
    public const int DefaultUses = 1;

    /// <summary>The default life in days.</summary>
    public const int DefaultDays = 7;

    /// <summary>Always <c>link</c> from the plugin.</summary>
    public string Kind { get; init; } = "link";

    /// <summary>How many times the link may be used.</summary>
    public int MaxUses { get; init; } = DefaultUses;

    /// <summary>How many days it stays valid.</summary>
    [JsonPropertyName("ttl_days")]
    public int TtlDays { get; init; } = DefaultDays;

    /// <summary>A link request with both numbers forced into the range the server accepts.</summary>
    /// <param name="uses">The uses asked for.</param>
    /// <param name="days">The days asked for.</param>
    /// <returns>The request.</returns>
    /// <remarks>
    /// Clamped rather than passed through, and the reason is asymmetric. A <c>ttl_days</c> out of range
    /// falls back to seven days on the server, which is harmless; a <c>max_uses</c> out of range means
    /// <b>unlimited</b>, which turns a link meant for one person into one that anybody who sees it can use.
    /// Both are always sent, so the server's own defaults never decide.
    /// </remarks>
    public static InviteRequest Link(int uses, int days) => new()
    {
        MaxUses = Math.Clamp(uses, MinUses, MaxUsesLimit),
        TtlDays = Math.Clamp(days, MinDays, MaxDays),
    };
}

/// <summary>Answer of <c>POST /teams/{id}/invite</c>. The code and the link in it are a secret.</summary>
public sealed class InviteResponse
{
    /// <summary>The invitation code. Never store, log or report it.</summary>
    public string? Code { get; init; }

    /// <summary>The link that carries the code. Never store, log or report it.</summary>
    public string? Link { get; init; }

    /// <summary>Seconds until it expires.</summary>
    public long ExpiresIn { get; init; }

    /// <summary>The invitation as the website lists it, without the code.</summary>
    public InviteInfo? Invite { get; init; }

    /// <inheritdoc />
    /// <remarks>Overridden so that a stray interpolation into a log line prints nothing secret.</remarks>
    public override string ToString() => $"InviteResponse(id={Invite?.Id})";
}

/// <summary>An invitation without its code, as the website lists it.</summary>
public sealed class InviteInfo
{
    /// <summary>The invitation id.</summary>
    public long Id { get; init; }

    /// <summary>Always <c>link</c> here.</summary>
    public string? Kind { get; init; }

    /// <summary>A label, if one was given.</summary>
    public string? Label { get; init; }

    /// <summary>How often it has been used.</summary>
    public int Uses { get; init; }

    /// <summary>How often it may be used.</summary>
    public int? MaxUses { get; init; }

    /// <summary>When it expires, server-local <c>YYYY-MM-DD HH:MM:SS</c>.</summary>
    public string? ExpiresAt { get; init; }
}
