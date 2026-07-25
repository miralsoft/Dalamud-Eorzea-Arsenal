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
