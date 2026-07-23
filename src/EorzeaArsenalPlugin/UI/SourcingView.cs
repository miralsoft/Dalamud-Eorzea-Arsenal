using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Renders the server's <c>routes[]</c> sourcing — "how to get this piece" — shared by the Teams farm
/// table and the BiS window/tooltip so the two never disagree. Purely presentational (R11): it turns
/// a <see cref="FarmSlot"/>/<see cref="ObtainInfo"/>'s source + routes into a one-line summary and a
/// game-like tooltip. The primary route follows the same rule as the web's
/// <c>FarmPlanner::primaryRoute</c> (savage → the drop, else the trade).
/// </summary>
internal sealed class SourcingView
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Blue = new(0.55f, 0.75f, 1f, 1f);

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

    /// <summary>Creates the renderer.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="world">Game actions: how many of an item is owned, and pin a vendor on the map.</param>
    public SourcingView(Localizer localizer, IWorldActions world)
    {
        _localizer = localizer;
        _world = world;
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    /// <summary>Localizes a gear-slot key (German where a translation exists, else the raw key).</summary>
    public string SlotName(string key) => German && SlotDe.TryGetValue(key, out var de) ? de : key;

    /// <summary>A short coloured badge for an acquisition source.</summary>
    public (string Label, Vector4 Color) SourceBadge(string? source) => source switch
    {
        "savage" => ("Savage", Red),
        "tomeplus" => ("Tome+", Blue),
        "tome" => ("Tome", Green),
        _ => (T(LocKeys.TeamsFarmUnknownSource), new Vector4(0.65f, 0.65f, 0.65f, 1f)),
    };

    /// <summary>Sort rank by how hard a piece is to get: savage first, then tome+, tome, unknown.</summary>
    public static int SourceRank(string? source) => source switch
    {
        "savage" => 0,
        "tomeplus" => 1,
        "tome" => 2,
        _ => 3,
    };

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

    /// <summary>A one-line summary of a route, sized for a table cell.</summary>
    public string RouteSummary(FarmRoute route) => route.Kind switch
    {
        "drop" => route.Duties is { Count: > 0 } d ? string.Join(", ", d) : route.Via?.Name ?? T(LocKeys.TeamsFarmCoffer),
        "retired" => T(LocKeys.TeamsFarmRetired),
        "market" => T(LocKeys.TeamsFarmMarket),
        _ => TradeSummary(route),
    };

    /// <summary>A standalone game-like tooltip listing every way to get the piece.</summary>
    public void DrawRoutesTooltip(List<FarmRoute> routes)
    {
        ImGui.BeginTooltip();
        DrawRoutesBody(routes);
        ImGui.EndTooltip();
    }

    /// <summary>
    /// The routes content — heading, then every way with costs, vendors and coords — without opening a
    /// tooltip of its own, so it can also be embedded in an existing tooltip (the BiS tile). A handed-in
    /// piece expands its own <c>chain</c> one level deeper, so an augment reads "hand in the base — and
    /// here is how you get that base", which is what you have to farm first.
    /// </summary>
    public void DrawRoutesBody(List<FarmRoute> routes)
    {
        ImGui.PushTextWrapPos(380f);
        ImGui.TextColored(Yellow, T(LocKeys.TeamsFarmWaysHeading));

        foreach (var route in routes)
        {
            ImGui.Separator();
            DrawRoute(route, depth: 0);
        }

        ImGui.PopTextWrapPos();
    }

    private void DrawRoute(FarmRoute route, int depth)
    {
        var pad = new string(' ', depth * 3);
        switch (route.Kind)
        {
            case "drop":
                var where = route.Duties is { Count: > 0 } d ? string.Join(", ", d) : "?";
                ImGui.TextUnformatted($"{pad}● {where}");
                if (route.Via?.Name is { Length: > 0 } coffer)
                {
                    ImGui.TextDisabled($"{pad}   {T(LocKeys.TeamsFarmCoffer)}: {coffer}");
                }

                break;

            case "retired":
                ImGui.TextDisabled($"{pad}● {T(LocKeys.TeamsFarmRetired)}");
                break;

            default:
                ImGui.TextUnformatted($"{pad}● {VendorHeader(route)}");
                foreach (var part in route.Cost ?? [])
                {
                    DrawCostPart(part, depth);
                }

                break;
        }
    }

    private void DrawCostPart(FarmCostPart part, int depth)
    {
        var pad = new string(' ', (depth * 3) + 3);

        // A handed-in gear piece is not effort itself — but getting it is, so expand its chain.
        if (string.Equals(part.Role, "piece", StringComparison.Ordinal))
        {
            ImGui.TextDisabled($"{pad}{T(LocKeys.TeamsFarmHandIn)}: {(part.Slot is { } s ? SlotName(s) : "?")}");
            foreach (var sub in part.Chain ?? [])
            {
                DrawRoute(sub, depth + 1);
            }

            return;
        }

        var name = part.Name ?? (part.Id is { } id ? $"#{id}" : T(LocKeys.TeamsFarmCoffer));
        var line = $"{pad}{part.Count}× {name}";

        // "have / need" for a real item, coloured by whether the player already has enough.
        if (part.Id is { } itemId && itemId > 0)
        {
            var have = _world.OwnedCount((uint)itemId);
            var enough = have >= part.Count;
            ImGui.TextColored(enough ? Green : new Vector4(0.85f, 0.85f, 0.85f, 1f), $"{line}  ({have}/{part.Count})");
        }
        else
        {
            ImGui.TextUnformatted(line);
        }
    }

    /// <summary>The "where" of a trade/craft/market route: the vendor and zone, a shop, or the kind.</summary>
    private string VendorHeader(FarmRoute route)
    {
        if (route.Npc is { Count: > 0 } npc && !string.IsNullOrEmpty(npc[0].Name))
        {
            var zone = string.IsNullOrEmpty(npc[0].Zone) ? string.Empty : $" · {npc[0].Zone}";
            var coords = npc[0] is { X: { } x, Y: { } y }
                ? $" ({x.ToString("0.#", CultureInfo.InvariantCulture)}, {y.ToString("0.#", CultureInfo.InvariantCulture)})"
                : string.Empty;
            return $"{npc[0].Name}{zone}{coords}";
        }

        if (route.Shops is { Count: > 0 } shops && !string.IsNullOrEmpty(shops[0]))
        {
            return shops[0];
        }

        return route.Kind == "craft" ? T(LocKeys.TeamsFarmCraft) : route.Kind == "market" ? T(LocKeys.TeamsFarmMarket) : "?";
    }

    /// <summary>The first vendor across the routes that can be pinned on the map (with its pin), or null.</summary>
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

    /// <summary>A trade/craft in one line: the effort parts (skipping handed-in pieces) and the vendor.</summary>
    private string TradeSummary(FarmRoute route)
    {
        var parts = (route.Cost ?? [])
            .Where(c => !string.Equals(c.Role, "piece", StringComparison.Ordinal))
            .Select(CostPartText)
            .ToList();
        var cost = parts.Count > 0 ? string.Join(" + ", parts) : (route.Kind == "craft" ? T(LocKeys.TeamsFarmCraft) : string.Empty);

        var at = route.Npc is { Count: > 0 } npc && !string.IsNullOrEmpty(npc[0].Name)
            ? $" @ {npc[0].Name}"
            : route.Shops is { Count: > 0 } shops && !string.IsNullOrEmpty(shops[0]) ? $" @ {shops[0]}" : string.Empty;

        return (cost + at).Trim();
    }

    private string CostPartText(FarmCostPart part)
    {
        var name = part.Name ?? (part.Id is { } id ? $"#{id}" : T(LocKeys.TeamsFarmCoffer));
        return $"{part.Count}× {name}";
    }
}
