using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Words for a team's line-up, from the server's numbers and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Formatting only. What is missing is computed by the server and is the same number the website shows,
/// so this never derives a count, never adds the roles up and never works one out from a target and a
/// missing figure. It picks the words and the singular or plural, and that is all.
/// </para>
/// <para>
/// Three rules from the contract are enforced here rather than at the call site, so both places that show
/// the need agree: a target of 0 means the team sets none for that role and the role is not mentioned; a
/// team where nothing is missing says nothing at all; and a server that sends no summary is not a team
/// that lacks nothing, so it says nothing too rather than "complete".
/// </para>
/// </remarks>
public static class LineupText
{
    private const string Separator = " · ";

    /// <summary>What is missing, as a short list: <c>1 Tank, 2 DPS</c>.</summary>
    /// <param name="missing">The server's missing counts per role.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>The list, or <see langword="null"/> when nothing is missing or nothing was sent.</returns>
    public static string? MissingList(RoleCounts? missing, ILocalizer loc)
    {
        if (missing is null)
        {
            return null;
        }

        var parts = new List<string>(3);
        Add(parts, missing.Tank, LocKeys.TeamsRoleTankCountOne, LocKeys.TeamsRoleTankCountMany, loc);
        Add(parts, missing.Healer, LocKeys.TeamsRoleHealerCountOne, LocKeys.TeamsRoleHealerCountMany, loc);
        Add(parts, missing.Dps, LocKeys.TeamsRoleDpsCountOne, LocKeys.TeamsRoleDpsCountMany, loc);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The short form for the team picker: <c>Test Static · fehlt 1 Tank, 2 DPS</c>.</summary>
    /// <param name="name">The team name.</param>
    /// <param name="summary">The team's line-up summary, if the server sent one.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>The label; just the name when nothing is missing.</returns>
    public static string TeamLabel(string name, LineupSummary? summary, ILocalizer loc) =>
        MissingList(summary?.Missing, loc) is { } list
            ? loc.Get(LocKeys.TeamsLineupTeamMissing, name, list)
            : name;

    /// <summary>
    /// The written-out form under the picker, one part per role that has a target:
    /// <c>Tank: 1 von 1 fehlt · Heiler: besetzt · DPS: 2 von 3 fehlen</c>.
    /// </summary>
    /// <param name="summary">The team's line-up summary, if the server sent one.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>The line, or <see langword="null"/> when nothing is missing or nothing was sent.</returns>
    /// <remarks>
    /// A role that is full is named as full here, and only here: once something is missing the reader
    /// wants to see the whole line-up at a glance, not just the gaps. When nothing is missing the whole
    /// line stays away, as the contract asks.
    /// </remarks>
    public static string? MissingDetail(LineupSummary? summary, ILocalizer loc)
    {
        if (summary?.Targets is not { } targets || summary.Missing is not { } missing ||
            MissingList(missing, loc) is null)
        {
            return null;
        }

        var parts = new List<string>(3);
        AddRole(parts, LocKeys.TeamsRoleTank, targets.Tank, missing.Tank, loc);
        AddRole(parts, LocKeys.TeamsRoleHealer, targets.Healer, missing.Healer, loc);
        AddRole(parts, LocKeys.TeamsRoleDps, targets.Dps, missing.Dps, loc);
        return parts.Count == 0 ? null : string.Join(Separator, parts);
    }

    /// <summary>Adds one role to the short list when something is missing in it.</summary>
    private static void Add(List<string> parts, int missing, string one, string many, ILocalizer loc)
    {
        if (missing > 0)
        {
            parts.Add(loc.Get(missing == 1 ? one : many, missing));
        }
    }

    /// <summary>Adds one role to the written-out line when the team sets a target for it.</summary>
    private static void AddRole(List<string> parts, string roleKey, int target, int missing, ILocalizer loc)
    {
        if (target <= 0)
        {
            return;
        }

        var role = loc.Get(roleKey);
        parts.Add(missing <= 0
            ? loc.Get(LocKeys.TeamsLineupRoleFull, role)
            : loc.Get(missing == 1 ? LocKeys.TeamsLineupRoleMissingOne : LocKeys.TeamsLineupRoleMissingMany, role, missing, target));
    }
}
