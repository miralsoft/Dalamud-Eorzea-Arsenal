using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

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

    /// <summary>Creates the renderer.</summary>
    /// <param name="localizer">UI string resolver.</param>
    public SourcingView(Localizer localizer) => _localizer = localizer;

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
    /// tooltip of its own, so it can also be embedded in an existing tooltip (the BiS tile).
    /// </summary>
    public void DrawRoutesBody(List<FarmRoute> routes)
    {
        ImGui.PushTextWrapPos(360f);
        ImGui.TextColored(Yellow, T(LocKeys.TeamsFarmWaysHeading));

        foreach (var route in routes)
        {
            ImGui.Separator();
            switch (route.Kind)
            {
                case "drop":
                    var where = route.Duties is { Count: > 0 } d ? string.Join(", ", d) : "?";
                    ImGui.TextUnformatted($"● {where}");
                    if (route.Via?.Name is { Length: > 0 } coffer)
                    {
                        ImGui.TextDisabled($"   {T(LocKeys.TeamsFarmCoffer)}: {coffer}");
                    }

                    break;

                case "retired":
                    ImGui.TextDisabled($"● {T(LocKeys.TeamsFarmRetired)}");
                    break;

                default:
                    ImGui.TextUnformatted($"● {TradeSummary(route)}");
                    foreach (var part in route.Cost ?? [])
                    {
                        if (string.Equals(part.Role, "piece", StringComparison.Ordinal))
                        {
                            ImGui.TextDisabled($"   {T(LocKeys.TeamsFarmHandIn)}: {(part.Slot is { } s ? SlotName(s) : "?")}");
                        }
                    }

                    if (route.Npc is { Count: > 0 } npc && npc[0] is { X: { } x, Y: { } y })
                    {
                        ImGui.TextDisabled($"   {npc[0].Zone} (X: {x.ToString("0.#", CultureInfo.InvariantCulture)}, Y: {y.ToString("0.#", CultureInfo.InvariantCulture)})");
                    }

                    break;
            }
        }

        ImGui.PopTextWrapPos();
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
