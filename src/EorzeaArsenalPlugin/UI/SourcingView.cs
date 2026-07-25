using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Renders the server's <c>routes[]</c> sourcing — "how to get this piece" — shared by the Teams farm
/// table and the BiS window/tooltip so the two never disagree. It turns the routes into an ordered list
/// of concrete <b>steps</b> (fight it / buy it / upgrade it), each with its own "have / need" from the
/// player's inventory, so a Tome+ piece reads as "first get the base, then augment" rather than
/// assuming the base is already in hand. Purely presentational (R11); ownership + the map come through
/// <see cref="IWorldActions"/>. The primary route follows the web's <c>FarmPlanner::primaryRoute</c>.
/// </summary>
internal sealed class SourcingView
{
    private const float TooltipFontScale = 1.15f;

    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.83f, 0.35f, 1f);
    private static readonly Vector4 Blue = new(0.55f, 0.78f, 1f, 1f);
    private static readonly Vector4 Dim = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 Text = new(0.88f, 0.88f, 0.88f, 1f);

    private static readonly Dictionary<string, string> SlotDe = new(StringComparer.Ordinal)
    {
        ["Weapon"] = "Waffe",
        ["OffHand"] = "Nebenhand",
        ["Head"] = "Kopf",
        ["Body"] = "Rumpf",
        ["Hands"] = "Hände",
        ["Legs"] = "Beine",
        ["Feet"] = "Füße",
        ["Ears"] = "Ohrringe",
        ["Neck"] = "Halskette",
        ["Wrists"] = "Armreif",
        ["RingLeft"] = "Ring links",
        ["RingRight"] = "Ring rechts",
    };

    private readonly Localizer _localizer;
    private readonly IWorldActions _world;
    private readonly ObtainService _obtain;
    private readonly HoldingsService _holdings;

    /// <summary>Creates the renderer.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="world">Game actions: how many of an item is owned (live), and pin a vendor on the map.</param>
    /// <param name="obtain">Sourcing lookup — used to recognise an equipped item as a tome base.</param>
    /// <param name="holdings">Server-side owned counts (retainers included); preferred over the live count.</param>
    public SourcingView(Localizer localizer, IWorldActions world, ObtainService obtain, HoldingsService holdings)
    {
        _localizer = localizer;
        _world = world;
        _obtain = obtain;
        _holdings = holdings;
    }

    private enum StepKind
    {
        Fight,
        Buy,
        Augment,
        Craft,
        Market,
        Retired,
        BaseOwned,
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    /// <summary>
    /// How many of an item the player owns: the higher of the server's holdings and the live in-game
    /// count.
    /// </summary>
    /// <remarks>
    /// Neither source alone is right. Only the server sees a retainer's stock (as of its last visit),
    /// so it must be able to win. But a server <c>0</c> means "no record", not "you own none": a
    /// <b>currency</b> like the weekly tomestone is never part of the inventory sync at all, and a
    /// material only synced after the tracked-items list arrived has no rows yet — taking the server's
    /// number there would report 0 of 495 tomestones to a player holding 1109. The game is always right
    /// about what is local, the server adds what is remote, so the maximum is the honest answer; the
    /// only error it can make is briefly counting retainer stock that was already spent.
    /// </remarks>
    private int Owned(long itemId)
    {
        if (itemId <= 0)
        {
            return 0;
        }

        var live = _world.OwnedCount((uint)itemId);
        return _holdings.TryGet(itemId, out var stored) ? Math.Max(stored, live) : live;
    }

    /// <summary>Warms holdings for every material/token/base id a piece references, so counts are ready.</summary>
    private void EnsureHoldings(List<FarmRoute>? routes)
    {
        var ids = new List<long>();
        CollectHoldingsIds(routes, ids, 0);
        if (ids.Count > 0)
        {
            _ = _holdings.PrefetchAsync(ids, System.Threading.CancellationToken.None);
        }
    }

    private static void CollectHoldingsIds(List<FarmRoute>? routes, List<long> into, int depth)
    {
        if (routes is null || depth > 2)
        {
            return;
        }

        foreach (var route in routes)
        {
            foreach (var cost in route.Cost ?? [])
            {
                if (cost.Id is { } id && id > 0)
                {
                    into.Add(id);
                }

                foreach (var baseId in cost.Ids ?? [])
                {
                    into.Add(baseId);
                }

                CollectHoldingsIds(cost.Chain, into, depth + 1);
            }
        }
    }

    /// <summary>Localizes a gear-slot key (German where a translation exists, else the raw key).</summary>
    public string SlotName(string key) => German && SlotDe.TryGetValue(key, out var de) ? de : key;

    /// <summary>A short coloured badge for an acquisition source.</summary>
    public (string Label, Vector4 Color) SourceBadge(string? source) => source switch
    {
        "savage" => ("Savage", Red),
        "tomeplus" => ("Tome+", Blue),
        "tome" => ("Tome", Green),
        _ => (T(LocKeys.TeamsFarmUnknownSource), Dim),
    };

    /// <summary>Sort rank by how hard a piece is to get: savage first, then tome+, tome, unknown.</summary>
    public static int SourceRank(string? source) => source switch
    {
        "savage" => 0,
        "tomeplus" => 1,
        "tome" => 2,
        _ => 3,
    };

    // --- Public rendering -------------------------------------------------------------------------

    /// <summary>
    /// A compact one-line summary for a table cell: the primary way's steps joined by "→", each coloured
    /// by whether you already have what it needs, plus an "or …" hint when other ways exist (the hover
    /// lists them all). Empty renders a dim dash.
    /// </summary>
    public void DrawCompact(string? source, List<FarmRoute>? routes, long equippedItemId = 0)
    {
        EnsureHoldings(routes);
        var methods = BuildMethods(source, routes, equippedItemId);
        if (methods.Count == 0)
        {
            ImGui.TextDisabled("—");
            return;
        }

        var steps = methods[0];
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, 4f);
                ImGui.TextDisabled("→");
                ImGui.SameLine(0f, 4f);
            }
            else
            {
                // Let the first chunk begin the wrap region at the cell's start.
                ImGui.PushTextWrapPos(0f);
                ImGui.PopTextWrapPos();
            }

            var (ready, _) = StepStatus(steps[i]);
            ImGui.TextColored(StepColor(steps[i], ready), CompactStep(steps[i]));
        }

        // There is another way in (e.g. a savage piece can also be traded for books) — name it briefly
        // so the row does not read as if the drop were the only option.
        if (methods.Count > 1 && methods[1].Count > 0)
        {
            ImGui.SameLine(0f, 6f);
            ImGui.TextDisabled($"{T(LocKeys.SourceOr)} {StepVerb(methods[1][^1])}");
        }
    }

    /// <summary>A standalone tooltip with the full, numbered step-by-step path (larger, spaced).</summary>
    public void DrawTooltip(string? source, List<FarmRoute>? routes, long equippedItemId = 0)
    {
        ImGui.BeginTooltip();
        DrawBody(source, routes, equippedItemId);
        ImGui.EndTooltip();
    }

    /// <summary>
    /// The full step-by-step detail — a numbered checklist with a heading, costs, "have / need" and the
    /// vendor + coordinates for each step. No forced wrap, so each line stays on one line and the
    /// tooltip auto-sizes instead of breaking a number across lines. Drawn slightly larger, as the
    /// detail view. Used inside the BiS tile tooltip and the farm hover.
    /// </summary>
    public void DrawBody(string? source, List<FarmRoute>? routes, long equippedItemId = 0)
    {
        ImGui.SetWindowFontScale(TooltipFontScale);
        try
        {
            DrawStepList(source, routes, heading: true, inline: false, equippedItemId);
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    /// <summary>
    /// A compact one-line-per-step rendering — verb, cost with "have / need", then the vendor and
    /// coordinates — for tight places like the in-game hover overlay, where the full block is too tall
    /// but the "where" still matters.
    /// </summary>
    public void DrawInline(string? source, List<FarmRoute>? routes, long equippedItemId = 0) => DrawStepList(source, routes, heading: false, inline: true, equippedItemId);

    private void DrawStepList(string? source, List<FarmRoute>? routes, bool heading, bool inline, long equippedItemId)
    {
        EnsureHoldings(routes);
        var methods = BuildMethods(source, routes, equippedItemId);

        if (heading)
        {
            ImGui.TextColored(Yellow, T(LocKeys.TeamsFarmWaysHeading));
        }

        if (methods.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.SourceNoInfo));
            return;
        }

        // Each method is an alternative ("drop OR trade"); the steps inside one are sequential and only
        // then numbered. A separator (block) or an "or" label (inline) keeps the alternatives apart.
        for (var m = 0; m < methods.Count; m++)
        {
            var steps = methods[m];
            if (!inline)
            {
                ImGui.Spacing();
                if (m > 0)
                {
                    ImGui.Separator();
                }
            }
            else if (m > 0)
            {
                ImGui.TextColored(Dim, T(LocKeys.SourceOr));
            }

            for (var i = 0; i < steps.Count; i++)
            {
                DrawStepRow(steps.Count > 1 ? i + 1 : 0, steps[i], inline);
            }
        }
    }

    // --- Step model -------------------------------------------------------------------------------

    private sealed record SourceStep(
        StepKind Kind,
        IReadOnlyList<FarmCostPart> Costs,
        FarmNpc? Npc,
        IReadOnlyList<string>? Shops,
        string? Coffer,
        IReadOnlyList<string> Duties,
        string? HandInSlot);

    /// <summary>
    /// Every way to get a piece, best/normal first — each an independent alternative (drop <i>or</i>
    /// trade <i>or</i> craft), and each already flattened into its own sequential steps (a Tome+ trade
    /// becomes "get the base, then augment"). This is what the detail view lists in full.
    /// </summary>
    private List<List<SourceStep>> BuildMethods(string? source, List<FarmRoute>? routes, long equippedItemId)
    {
        var methods = new List<List<SourceStep>>();
        foreach (var route in OrderedRoutes(source, routes))
        {
            var steps = FlattenRoute(route, equippedItemId, 0);
            if (steps.Count > 0)
            {
                methods.Add(steps);
            }
        }

        return methods;
    }

    /// <summary>The routes, the source's primary one first, then the rest in their given order.</summary>
    private static IEnumerable<FarmRoute> OrderedRoutes(string? source, List<FarmRoute>? routes)
    {
        if (routes is not { Count: > 0 })
        {
            yield break;
        }

        var primary = PrimaryRoute(source, routes);
        if (primary is not null)
        {
            yield return primary;
        }

        foreach (var route in routes)
        {
            if (!ReferenceEquals(route, primary))
            {
                yield return route;
            }
        }
    }

    /// <summary>
    /// Flattens ONE route into its sequential steps. A drop/retired is a single step; a trade expands a
    /// handed-in base from its own (primary) chain first — unless the base is already equipped — then the
    /// augment/purchase. Depth-capped so a self-referential price cannot recurse forever.
    /// </summary>
    private List<SourceStep> FlattenRoute(FarmRoute? route, long equippedItemId, int depth)
    {
        var steps = new List<SourceStep>();
        if (route is null || depth > 2)
        {
            return steps;
        }

        switch (route.Kind)
        {
            case "drop":
                steps.Add(new SourceStep(StepKind.Fight, [], route.Npc?.FirstOrDefault(), null, route.Via?.Name, route.Duties ?? [], null));
                break;

            case "retired":
                steps.Add(new SourceStep(StepKind.Retired, [], null, null, null, [], null));
                break;

            default:
                var pieces = (route.Cost ?? []).Where(c => string.Equals(c.Role, "piece", StringComparison.Ordinal)).ToList();
                var effort = (route.Cost ?? []).Where(c => !string.Equals(c.Role, "piece", StringComparison.Ordinal)).ToList();

                foreach (var piece in pieces)
                {
                    if (IsBaseOwned(piece, equippedItemId))
                    {
                        steps.Add(new SourceStep(StepKind.BaseOwned, [], null, null, null, [], piece.Slot));
                    }
                    else if (piece.Chain is { Count: > 0 })
                    {
                        steps.AddRange(FlattenRoute(PrimaryRoute(AcqToSource(piece.Acq), piece.Chain), 0, depth + 1));
                    }
                    else
                    {
                        steps.Add(new SourceStep(StepKind.Buy, [], null, null, null, [], piece.Slot));
                    }
                }

                var kind = route.Kind switch
                {
                    "craft" => StepKind.Craft,
                    "market" => StepKind.Market,
                    _ => pieces.Count > 0 ? StepKind.Augment : StepKind.Buy,
                };
                steps.Add(new SourceStep(kind, effort, route.Npc?.FirstOrDefault(), route.Shops, null, [], pieces.FirstOrDefault()?.Slot));
                break;
        }

        return steps;
    }

    private static string? AcqToSource(string? acq) => acq;

    /// <summary>
    /// Whether the handed-in base is already owned, so the "buy the base" step collapses to "base owned":
    /// either it is owned <b>anywhere</b> (any of the server's base ids for the slot held per holdings,
    /// retainers included) or it is the currently equipped piece.
    /// </summary>
    private bool IsBaseOwned(FarmCostPart piece, long equippedItemId)
    {
        if (piece.Id is { } single && single > 0 && Owned(single) > 0)
        {
            return true;
        }

        foreach (var id in piece.Ids ?? [])
        {
            if (Owned(id) > 0)
            {
                return true;
            }
        }

        return IsEquippedBase(equippedItemId, piece.Slot);
    }

    /// <summary>
    /// Whether the currently equipped item is the tome base for a slot — resolved from its own sourcing
    /// (a tome piece of the same slot). The fast path for a worn base, and the only path until the
    /// server sends the base ids.
    /// </summary>
    private bool IsEquippedBase(long equippedItemId, string? slot)
    {
        if (equippedItemId <= 0 || string.IsNullOrEmpty(slot) || !_obtain.TryGet(equippedItemId, out var info))
        {
            return false;
        }

        return string.Equals(info?.Source, "tome", StringComparison.Ordinal) && SameSlot(info?.Slot, slot);
    }

    /// <summary>
    /// Whether two slot keys refer to the same wear position. A ring fits either finger, and the config
    /// files a ring item under one canonical ring slot, so <c>RingLeft</c> and <c>RingRight</c> are
    /// treated as one — otherwise a base ring worn in one finger would not satisfy the other's augment.
    /// </summary>
    private static bool SameSlot(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        return IsRing(a) && IsRing(b);
    }

    private static bool IsRing(string? slot) =>
        string.Equals(slot, "RingLeft", StringComparison.Ordinal) || string.Equals(slot, "RingRight", StringComparison.Ordinal);

    // --- Step rendering ---------------------------------------------------------------------------

    private void DrawStepRow(int number, SourceStep step, bool inline)
    {
        var (ready, _) = StepStatus(step);

        // Line 1: a step number when the method has several, else a plain bullet, then the verb.
        ImGui.TextColored(Dim, number > 0 ? $"{number}." : "●");
        ImGui.SameLine(0f, 6f);
        ImGui.TextColored(StepColor(step, ready), StepVerb(step));
        if (step.HandInSlot is { } handIn)
        {
            ImGui.SameLine(0f, 6f);
            ImGui.TextColored(Dim, $"· {T(LocKeys.TeamsFarmHandIn)}: {SlotName(handIn)}");
        }

        // Inline: keep the headline cost + where on this same line (overlay). Block: one line each.
        if (inline)
        {
            var headline = step.Costs.FirstOrDefault();
            if (headline is not null)
            {
                ImGui.SameLine(0f, 8f);
                DrawCost(headline);
            }

            var oneWhere = StepWhere(step);
            if (!string.IsNullOrEmpty(oneWhere))
            {
                ImGui.SameLine(0f, 8f);
                ImGui.TextColored(Dim, $"· {oneWhere}");
            }

            return;
        }

        foreach (var cost in step.Costs)
        {
            ImGui.Indent(16f);
            DrawCost(cost);
            ImGui.Unindent(16f);
        }

        var where = StepWhere(step);
        if (!string.IsNullOrEmpty(where))
        {
            ImGui.Indent(16f);
            ImGui.TextColored(Dim, where);
            ImGui.Unindent(16f);
        }

        // The coffer as its own line only when the "where" above is the fight — otherwise the where line
        // already is the coffer (the fallback when no fight was linked), so this would repeat it.
        if (step.Coffer is { Length: > 0 } coffer && step.Duties is { Count: > 0 })
        {
            ImGui.Indent(16f);
            ImGui.TextColored(Dim, $"{T(LocKeys.TeamsFarmCoffer)}: {coffer}");
            ImGui.Unindent(16f);
        }
    }

    private void DrawCost(FarmCostPart cost)
    {
        var name = cost.Name ?? (cost.Id is { } id ? $"#{id}" : T(LocKeys.TeamsFarmCoffer));
        if (cost.Id is { } itemId && itemId > 0)
        {
            var have = Owned(itemId);
            var enough = have >= cost.Count;
            ImGui.TextColored(Text, $"{cost.Count}× {name}");
            ImGui.SameLine(0f, 6f);
            ImGui.TextColored(enough ? Green : Red, $"{have}/{cost.Count}");
        }
        else
        {
            ImGui.TextColored(Text, $"{cost.Count}× {name}");
        }
    }

    /// <summary>Whether a step is satisfied by what the player owns, plus the total items still short.</summary>
    private (bool Ready, int Short) StepStatus(SourceStep step)
    {
        if (step.Kind is StepKind.Fight or StepKind.Market or StepKind.BaseOwned)
        {
            return (true, 0); // an action, or already done
        }

        if (step.Kind == StepKind.Retired)
        {
            return (false, 0);
        }

        var shortBy = 0;
        foreach (var cost in step.Costs)
        {
            if (cost.Id is { } id && id > 0)
            {
                shortBy += Math.Max(0, cost.Count - Owned(id));
            }
        }

        return (shortBy == 0, shortBy);
    }

    private Vector4 StepColor(SourceStep step, bool ready) => step.Kind switch
    {
        StepKind.Fight => Blue,
        StepKind.Retired => Red,
        StepKind.BaseOwned => Green,
        _ => ready ? Green : Text,
    };

    private string StepVerb(SourceStep step) => step.Kind switch
    {
        StepKind.Fight => T(LocKeys.SourceStepFight),
        StepKind.Augment => T(LocKeys.SourceStepAugment),
        StepKind.Craft => T(LocKeys.SourceStepCraft),
        StepKind.Market => T(LocKeys.TeamsFarmMarket),
        StepKind.Retired => T(LocKeys.TeamsFarmRetired),
        StepKind.BaseOwned => T(LocKeys.SourceBaseOwned),
        _ => step.HandInSlot is not null && step.Costs.Count == 0 ? T(LocKeys.SourceStepBase) : T(LocKeys.SourceStepBuy),
    };

    /// <summary>The compact chip for a step in the table cell: the verb plus its headline cost/where.</summary>
    private string CompactStep(SourceStep step)
    {
        switch (step.Kind)
        {
            case StepKind.Fight:
                return step.Duties is { Count: > 0 } d ? d[0] : T(LocKeys.SourceStepFight);
            case StepKind.Retired:
                return T(LocKeys.TeamsFarmRetired);
            case StepKind.BaseOwned:
                return T(LocKeys.SourceBaseOwned);
            default:
                var headline = step.Costs.FirstOrDefault();
                var verb = StepVerb(step);
                if (headline is null)
                {
                    return verb;
                }

                var name = headline.Name ?? T(LocKeys.TeamsFarmCoffer);
                return $"{verb} {headline.Count}× {name}";
        }
    }

    private string StepWhere(SourceStep step)
    {
        if (step.Kind == StepKind.Fight)
        {
            // Prefer the fight name(s); if the server did not link one, the coffer name is the fallback.
            if (step.Duties is { Count: > 0 } d)
            {
                return string.Join(", ", d);
            }

            return step.Coffer is { Length: > 0 } coffer ? $"{T(LocKeys.TeamsFarmCoffer)}: {coffer}" : string.Empty;
        }

        if (step.Npc is { } npc && !string.IsNullOrEmpty(npc.Name))
        {
            var zone = string.IsNullOrEmpty(npc.Zone) ? string.Empty : $" · {npc.Zone}";
            var coords = npc is { X: { } x, Y: { } y }
                ? $" ({x.ToString("0.#", CultureInfo.InvariantCulture)}, {y.ToString("0.#", CultureInfo.InvariantCulture)})"
                : string.Empty;
            return $"{npc.Name}{zone}{coords}";
        }

        return step.Shops is { Count: > 0 } shops && !string.IsNullOrEmpty(shops[0]) ? shops[0] : string.Empty;
    }

    // --- Primary route + map ----------------------------------------------------------------------

    /// <summary>
    /// The route matching <paramref name="source"/> (savage → drop, else the trade), falling back to
    /// any trade then the first route — the web's <c>FarmPlanner::primaryRoute</c> rule.
    /// </summary>
    public static FarmRoute? PrimaryRoute(string? source, List<FarmRoute>? routes)
    {
        if (routes is not { Count: > 0 })
        {
            return null;
        }

        if (string.Equals(source, "savage", StringComparison.Ordinal))
        {
            var drop = routes.FirstOrDefault(r => string.Equals(r.Kind, "drop", StringComparison.Ordinal));
            if (drop is not null)
            {
                return drop;
            }
        }

        return routes.FirstOrDefault(r => string.Equals(r.Kind, "trade", StringComparison.Ordinal)) ?? routes[0];
    }

    private (FarmNpc Npc, MapPin Pin)? MappableVendor(List<FarmRoute>? routes)
    {
        foreach (var route in routes ?? [])
        {
            foreach (var npc in route.Npc ?? [])
            {
                if (_world.ResolvePin(npc) is { } pin)
                {
                    return (npc, pin);
                }
            }
        }

        return null;
    }

    /// <summary>Whether any route has a vendor that can be pinned on the map.</summary>
    public bool HasMapTarget(List<FarmRoute>? routes) => MappableVendor(routes) is not null;

    /// <summary>
    /// Draws a "show NPC on map" context-menu entry when a mappable vendor exists. Call inside an open
    /// <c>BeginPopupContextItem</c>. Returns <see langword="true"/> when the entry was rendered.
    /// </summary>
    public bool DrawMapMenuItem(List<FarmRoute>? routes)
    {
        if (MappableVendor(routes) is not { } hit)
        {
            return false;
        }

        if (ImGui.MenuItem($"{T(LocKeys.TeamsFarmShowOnMap)}: {hit.Npc.Name}"))
        {
            _world.OpenMap(hit.Pin);
        }

        return true;
    }
}
