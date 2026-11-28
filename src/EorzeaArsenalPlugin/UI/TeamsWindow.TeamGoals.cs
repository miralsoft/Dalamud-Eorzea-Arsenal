using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The goals of a team (<c>team-goals</c>, <c>contract-team-goals.md</c>): each goal with its item, the first way's
/// costs, where it comes from and "Noch gebraucht: 3 von 8", then every character with its state and the answers
/// the reader may give. Reached, closed and goals nobody counts for are folded below. Every state and number is the
/// server's; an answer's reply is the team's goals as they are now and replaces what was read.
/// </summary>
public sealed partial class TeamsWindow
{
    private void DrawTeamGoalBoard(TeamSummary team, string language)
    {
        Ensure(_teamGoalsSlot, $"{team.Id.ToString(CultureInfo.InvariantCulture)}|{language}", ct => _goals.ReadTeamGoalsAsync(team.Id, language, ct));
        if (_teamGoalsSlot.Loading && _teamGoalsSlot.Value is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_teamGoalsSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        if (_teamGoalsSlot.Value?.Data is not { } board)
        {
            return;
        }

        if (_teamGoalWidgets.Message is { } message)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Yellow))
            {
                ImGui.TextWrapped(message);
            }
        }

        var open = board.Goals.Where(g => !g.Closed && !g.Reached && g.Counted > 0).ToList();
        var folded = board.Goals.Where(g => g.Closed || g.Reached || g.Counted == 0).ToList();
        if (open.Count == 0 && folded.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamGoalsNoneSet));
            return;
        }

        var own = _ownCharacters();
        var ownUsers = TeamGoalRules.OwnUsers(board.Goals, own);
        foreach (var goal in open)
        {
            DrawTeamGoal(team.Id, board, goal, own, ownUsers);
        }

        if (folded.Count > 0 && ImGui.CollapsingHeader(T(LocKeys.TeamGoalReachedSection)))
        {
            foreach (var goal in folded)
            {
                DrawTeamGoal(team.Id, board, goal, own, ownUsers);
            }
        }
    }

    private void DrawTeamGoal(long teamId, TeamGoalBoard board, TeamGoal goal, IReadOnlySet<long> own, IReadOnlySet<long> ownUsers)
    {
        var culture = German ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-US");
        var itemId = goal.Item?.Id ?? 0;
        var name = GoalItemName(goal.Item?.Name, itemId);
        using var id = ImRaii.PushId(string.Create(CultureInfo.InvariantCulture, $"teamgoal{goal.Id}"));
        DrawGoalIcon(itemId, ImGui.GetFrameHeight() * 1.4f);
        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.45f, 0.65f, 1f, 1f), name);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (ImGui.IsItemClicked() && itemId > 0)
            {
                OpenApp($"/item/{itemId.ToString(CultureInfo.InvariantCulture)}");
            }

            if (goal.ToCollect)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, goal.Qty > 1 ? $"{T(LocKeys.GoalPurposeCollect)} · {goal.Qty.ToString("N0", culture)}" : T(LocKeys.GoalPurposeCollect));
            }
            else if (goal.Kind is null && goal.Qty > 1)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, _localizer.Get(LocKeys.TeamGoalQty, goal.Qty.ToString("N0", culture)));
            }

            if (goal.Closed)
            {
                ImGui.TextColored(Dim, T(LocKeys.TeamGoalClosed));
            }
            else if (goal.Reached)
            {
                ImGui.TextColored(Green, T(LocKeys.TeamGoalReached));
            }
            else if (goal.Counted == 0)
            {
                ImGui.TextColored(Dim, T(LocKeys.TeamGoalNobody));
            }
            else
            {
                ImGui.TextColored(Yellow, _localizer.Get(LocKeys.TeamGoalNeeded, goal.Needed.ToString(culture), goal.Counted.ToString(culture)));
            }

            if (goal.Way is { Cost.Count: > 0 } way)
            {
                var costs = string.Join(", ", way.Cost.Select(c => $"{c.Count.ToString("N0", culture)} {GoalItemName(c.Name, c.Id)}"));
                ImGui.TextColored(Dim, _localizer.Get(LocKeys.TeamGoalCost, costs));
            }

            _teamGoalWidgets.DrawWhere(goal);
            foreach (var participant in goal.Participants)
            {
                DrawTeamGoalParticipant(teamId, board, goal, participant, own, ownUsers, name, culture);
            }
        }

        ImGui.Separator();
    }

    private void DrawTeamGoalParticipant(
        long teamId,
        TeamGoalBoard board,
        TeamGoal goal,
        TeamGoalParticipant participant,
        IReadOnlySet<long> own,
        IReadOnlySet<long> ownUsers,
        string itemName,
        CultureInfo culture)
    {
        var who = string.IsNullOrEmpty(participant.Member) || participant.Member == participant.Name
            ? participant.Name ?? string.Empty
            : $"{participant.Name} ({participant.Member})";
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted(who);
        ImGui.SameLine();
        _teamGoalWidgets.DrawState(participant);

        // While open: what is held of the costs, or a lock where the player keeps the numbers to themselves.
        if (string.Equals(participant.State, TeamGoalStates.Open, StringComparison.Ordinal))
        {
            var parts = new List<string>();
            if (goal.Qty > 1 && participant.Held is { } held)
            {
                parts.Add(_localizer.Get(LocKeys.GoalHeldOf, held.ToString("N0", culture), goal.Qty.ToString("N0", culture)));
            }

            foreach (var cost in participant.Progress?.Cost ?? [])
            {
                parts.Add($"{cost.Have.ToString("N0", culture)} / {cost.Need.ToString("N0", culture)} {GoalItemName(cost.Name, cost.Id)}");
            }

            if (parts.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, string.Join("  ·  ", parts));
            }

            if (participant.CountsHidden)
            {
                ImGui.SameLine();
                _teamGoalWidgets.DrawHidden();
            }
        }

        var mayAnswer = TeamGoalRules.MayAnswer(participant, board.MayManage, own, ownUsers);
        _teamGoalWidgets.DrawAnswers(teamId, goal, participant, mayAnswer, itemName, answered =>
        {
            // The reply is the team's goals as they are now: shown at once, no second read.
            if (answered is not null)
            {
                _teamGoalsSlot.Value = new TeamGoalsResponse { Data = answered };
            }
            else
            {
                _teamGoalsSlot.Reset();
            }
        });
    }

    /// <inheritdoc />
    public void Dispose() => _teamGoalWidgets.Dispose();
}
