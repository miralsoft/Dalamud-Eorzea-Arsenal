namespace EorzeaArsenal.Model;

/// <summary>
/// Response of <c>GET</c>/<c>PUT /me/advisor-plan</c>: the player's saved purchase plan for one
/// (character, job, target set) — the intermediate gear layout they picked in the web advisor
/// ("Kaufberater") on the way to BiS. <see cref="Data"/> is <see langword="null"/> when no plan is
/// saved, which is a normal state (show the recommendation instead), never an error.
/// </summary>
/// <remarks>
/// A plan is deliberately <b>not</b> a gearset: My Gear holds the BiS <i>goal</i>, the plan is the
/// <i>purchase path</i> to it. The two are kept apart — a plan is never surfaced as a My-Gear set.
/// </remarks>
public sealed class AdvisorPlanResponse
{
    /// <summary>The stored plan, or <see langword="null"/> when none is saved for this set.</summary>
    public AdvisorPlan? Data { get; init; }
}

/// <summary>One stored purchase plan: which piece the player intends to wear per slot.</summary>
public sealed class AdvisorPlan
{
    /// <summary>The job the plan belongs to.</summary>
    public string? Job { get; init; }

    /// <summary>The BiS set the plan leads to (its apiPath / shortlink, e.g. <c>sl/&lt;uuid&gt;</c>).</summary>
    public string? Target { get; init; }

    /// <summary>Display name of the target set, when the server knows one.</summary>
    public string? TargetName { get; init; }

    /// <summary>Contract slot spelling (<c>Weapon</c>, <c>RingLeft</c>, …) → the intended piece.</summary>
    public Dictionary<string, AdvisorPlanItem>? Items { get; init; }

    /// <summary>When the plan was last saved (ISO-8601), for display only.</summary>
    public string? UpdatedAt { get; init; }
}

/// <summary>One slot's entry in a plan: just the item id (a plan carries no materia).</summary>
public sealed class AdvisorPlanItem
{
    /// <summary>The intended item id for that slot.</summary>
    public long Id { get; init; }
}

/// <summary>
/// Body of <c>PUT /me/advisor-plan</c> (bearer + <c>plans:write</c>, no CSRF). Own character only.
/// An empty <see cref="Items"/> map clears the plan, same as a <c>DELETE</c>.
/// </summary>
public sealed class AdvisorPlanRequest
{
    /// <summary>The character the plan belongs to (must be the caller's own).</summary>
    public long CharacterId { get; init; }

    /// <summary>The job, lower-case (e.g. <c>whm</c>).</summary>
    public required string Job { get; init; }

    /// <summary>The target set's apiPath / shortlink — read back from the set, never invented.</summary>
    public required string Target { get; init; }

    /// <summary>Optional display name of the target set.</summary>
    public string? TargetName { get; init; }

    /// <summary>Slot → intended piece; the server sanitises and drops anything unknown.</summary>
    public Dictionary<string, AdvisorPlanItem> Items { get; init; } = [];
}

/// <summary>Response of <c>GET /me/advisor-options</c>: everything one advisor view needs for one set.</summary>
public sealed class AdvisorOptionsResponse
{
    /// <summary>The computed advice, or <see langword="null"/> on an empty answer.</summary>
    public AdvisorOptions? Data { get; init; }
}

/// <summary>
/// The server's single ranking for a set: what is worn, what the target is, the ranked deterministic
/// steps to get there, the pieces a plan editor may offer per slot, and what the open slots still
/// need in material. Computed server-side <b>on purpose</b> — the web renders the same answer, so a
/// rule change or a tier rotation reaches both without either shipping a release.
/// </summary>
public sealed class AdvisorOptions
{
    /// <summary>The character the advice was computed for.</summary>
    public string? CharacterId { get; init; }

    /// <summary>The job, upper-case.</summary>
    public string? Job { get; init; }

    /// <summary>The target set's apiPath / shortlink — the key a plan is stored under.</summary>
    public string? Target { get; init; }

    /// <summary>Display name of the target set.</summary>
    public string? TargetName { get; init; }

    /// <summary>The gearset index the target belongs to.</summary>
    public int? GearIndex { get; init; }

    /// <summary>The capped-tomestone balance the schedule was computed against (what the plugin pushed).</summary>
    public int TomeBalance { get; init; }

    /// <summary>The weekly capped-tomestone cap, for the "in N weeks" wording.</summary>
    public int WeeklyCap { get; init; }

    /// <summary>Which ranking produced <see cref="Steps"/>: <c>power</c>, <c>value</c> or <c>cheap</c>.</summary>
    public string? Sort { get; init; }

    /// <summary>The deterministic steps, best first. Slots already on BiS and sidegrades are not steps.</summary>
    public List<AdvisorStep>? Steps { get; init; }

    /// <summary>Slot → what is worn, what BiS is, what is recommended, and the pieces to choose from.</summary>
    public Dictionary<string, AdvisorSlot>? Slots { get; init; }

    /// <summary>
    /// What the <b>recommended path</b> still needs in material and books — a bridge's upgrade
    /// material included, because doing what the advisor says really costs it.
    /// </summary>
    public List<AdvisorMaterialNeed>? Materials { get; init; }

    /// <summary>
    /// What the <b>BiS set itself</b> still costs: each open slot's own target piece and nothing else.
    /// A different question from <see cref="Materials"/> — a bridge is a way there, not part of the
    /// goal, so an Ultimate weapon asks for nothing rather than inheriting the tome bridge's price.
    /// </summary>
    public AdvisorTargetNeeds? TargetNeeds { get; init; }
}

/// <summary>What completing the BiS set still costs, independent of the route the advisor recommends.</summary>
public sealed class AdvisorTargetNeeds
{
    /// <summary>Capped tomestones, counting only slots whose BiS piece is actually bought.</summary>
    public int Tomes { get; init; }

    /// <summary>The materials and books the target pieces themselves ask for.</summary>
    public List<AdvisorMaterialNeed>? Materials { get; init; }
}

/// <summary>One ranked step: swap this slot's piece for that one, at this price, at this time.</summary>
public sealed class AdvisorStep
{
    /// <summary>The slot the step is about.</summary>
    public string? Slot { get; init; }

    /// <summary>The piece worn today (<see langword="null"/> for an empty slot).</summary>
    public long? From { get; init; }

    /// <summary>The piece this step lands on.</summary>
    public long? To { get; init; }

    /// <summary>The piece this step eventually leads to, when <see cref="To"/> is only a stepping stone.</summary>
    public long? Final { get; init; }

    /// <summary>
    /// What to do: <c>equip</c> (you already own it), <c>buy</c> (tomestones), <c>augment</c>,
    /// <c>trial</c> (farm the Extreme piece), <c>books</c> (trade the raid books) or <c>none</c>.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>Capped tomestones the purchase costs (0 when it costs none).</summary>
    public int Cost { get; init; }

    /// <summary>Material/books the step consumes, each with what is held.</summary>
    public List<AdvisorMaterial>? Material { get; init; }

    /// <summary>Books still missing on the book-trade branch.</summary>
    public int BooksMissing { get; init; }

    /// <summary>The step's share of the total remaining gain, in percent.</summary>
    public int Pct { get; init; }

    /// <summary>When the step is doable, given the pushed tomestone balance.</summary>
    public AdvisorWhen? When { get; init; }

    /// <summary>The vendor to buy from, when the step is a purchase.</summary>
    public string? Vendor { get; init; }
}

/// <summary>When a step can happen: right now, in N capped weeks, or once N books are in.</summary>
public sealed class AdvisorWhen
{
    /// <summary>Doable immediately.</summary>
    public bool Now { get; init; }

    /// <summary>Capped weeks of tomestones still to save.</summary>
    public int? Week { get; init; }

    /// <summary>Books still to collect.</summary>
    public int? Books { get; init; }
}

/// <summary>A material or book a step consumes, with how many are held.</summary>
public sealed class AdvisorMaterial
{
    /// <summary>The item id.</summary>
    public long Id { get; init; }

    /// <summary>The item name as the server knows it (English).</summary>
    public string? Name { get; init; }

    /// <summary>How many the step needs.</summary>
    public int Count { get; init; }

    /// <summary>How many the character holds (server count, retainers included).</summary>
    public int Owned { get; init; }
}

/// <summary>A material or book the whole remaining set needs, with how many are held.</summary>
public sealed class AdvisorMaterialNeed
{
    /// <summary>The item id.</summary>
    public long Id { get; init; }

    /// <summary>The item name as the server knows it (English).</summary>
    public string? Name { get; init; }

    /// <summary>How many the open slots still need in total.</summary>
    public int Need { get; init; }

    /// <summary>How many the character holds (server count, retainers included).</summary>
    public int Owned { get; init; }

    /// <summary>The slots this need came from, so a tooltip can say which pieces want it.</summary>
    public List<string>? For { get; init; }
}

/// <summary>One slot's picture: worn, target, recommendation and the pieces that may replace it.</summary>
public sealed class AdvisorSlot
{
    /// <summary>The piece worn today, or <see langword="null"/> when the slot is empty.</summary>
    public AdvisorPiece? Current { get; init; }

    /// <summary>The BiS target for this slot.</summary>
    public AdvisorPiece? Bis { get; init; }

    /// <summary>The item id the advisor recommends next, or <see langword="null"/> when nothing is.</summary>
    public long? Recommended { get; init; }

    /// <summary>Whether the slot has no worthwhile deterministic step (a sidegrade or already done).</summary>
    public bool Even { get; init; }

    /// <summary>Every current-tier piece for the slot, best first — the plan editor's menu.</summary>
    public List<AdvisorPiece>? Options { get; init; }
}

/// <summary>A gear piece as the advisor describes it.</summary>
public sealed class AdvisorPiece
{
    /// <summary>The item id.</summary>
    public long Id { get; init; }

    /// <summary>The item name as the server knows it (English; the plugin prefers the game's own name).</summary>
    public string? Name { get; init; }

    /// <summary>Item level.</summary>
    public int Ilvl { get; init; }

    /// <summary>
    /// Where it comes from. Note this is the <b>raw</b> vocabulary (<c>Tome</c>, <c>AugTome</c>,
    /// <c>SavageRaid</c>, <c>ExtremeTrial</c>…), not the lower-case enum <c>/gear/bis</c> uses —
    /// <see cref="EorzeaArsenal.Localization.SourceNames"/> resolves both.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>The server's power score, so "current vs plan vs BiS" needs no second formula.</summary>
    public int Score { get; init; }
}

/// <summary>Body of <c>DELETE /me/advisor-plan</c>: which plan to drop (idempotent).</summary>
public sealed class AdvisorPlanDeleteRequest
{
    /// <summary>The character the plan belongs to (must be the caller's own).</summary>
    public long CharacterId { get; init; }

    /// <summary>The job, lower-case.</summary>
    public required string Job { get; init; }

    /// <summary>The target set's apiPath / shortlink.</summary>
    public required string Target { get; init; }
}
