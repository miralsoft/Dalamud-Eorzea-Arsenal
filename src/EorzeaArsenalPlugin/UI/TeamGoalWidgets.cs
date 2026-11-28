using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// What the goals view and the team's goals tab share for goals of a team (<c>team-goals</c>): the answer buttons
/// ("Habe ich schon", "Kein Interesse", "Zurücknehmen"), the line where a goal comes from, the state of a character
/// and the lock for hidden numbers. "Habe ich schon" on a goal to learn asks for a second click (agreed with the
/// operator, 2026-11-29). One answer at a time; the outcome of a failed one stays as a message.
/// </summary>
internal sealed class TeamGoalWidgets : IDisposable
{
    private static readonly Vector4 Green = new(0.36f, 0.78f, 0.36f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Link = new(0.45f, 0.65f, 1f, 1f);

    private readonly Localizer _localizer;
    private readonly GoalsService _goals;
    private readonly Func<string> _webAppBase;
    private readonly Action<string> _openLink;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _writing;
    private volatile string? _message;
    private string? _armed;

    /// <summary>Creates the shared parts.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="goals">The goals service, which sends the answers.</param>
    /// <param name="webAppBase">The website's base address, for the links.</param>
    /// <param name="openLink">Opens an http(s) address in the browser.</param>
    /// <param name="log">Diagnostics sink.</param>
    public TeamGoalWidgets(Localizer localizer, GoalsService goals, Func<string> webAppBase, Action<string> openLink, ILog log)
    {
        _localizer = localizer;
        _goals = goals;
        _webAppBase = webAppBase;
        _openLink = openLink;
        _log = log;
    }

    /// <summary>The outcome of the last answer that failed, or <see langword="null"/>.</summary>
    public string? Message => _message;

    private string Language => _localizer.Language == Localizer.German ? Localizer.German : Localizer.English;

    /// <summary>Forgets the message and an armed second click, when the view opens again.</summary>
    public void Reset()
    {
        _message = null;
        _armed = null;
    }

    /// <summary>The state of a character in its colour, and who gave the answer.</summary>
    /// <param name="participant">The character or placeholder.</param>
    public void DrawState(TeamGoalParticipant participant)
    {
        var (text, tone) = TeamGoalText.State(participant, _localizer);
        ImGui.TextColored(tone switch { GoalHintTone.Good => Green, GoalHintTone.Missing => Yellow, _ => Dim }, text);
        if (TeamGoalText.MarkedBy(participant, _localizer) is { } by)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"({by})");
        }
    }

    /// <summary>A lock for numbers the player keeps to themselves, with the reason on hover: never a 0.</summary>
    public void DrawHidden()
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(Dim, FontAwesomeIcon.Lock.ToIconString());
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_localizer.Get(LocKeys.TeamGoalHidden));
        }
    }

    /// <summary>"Aus: duty", which opens the duty's page, and the team's content of it.</summary>
    /// <param name="goal">The goal of the team.</param>
    public void DrawWhere(TeamGoal goal)
    {
        if (goal.Where?.Duty is not { Id: > 0 } duty || string.IsNullOrEmpty(duty.Name))
        {
            return;
        }

        ImGui.TextColored(Link, _localizer.Get(LocKeys.TeamGoalFrom, duty.Name));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemClicked())
        {
            _openLink(_webAppBase() + GoalLinks.Duty(duty.Id));
        }

        if (goal.Where.Content is { Name.Length: > 0 } content)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, _localizer.Get(LocKeys.TeamGoalContent, content.Name));
        }
    }

    /// <summary>The answer buttons on the same line, as <see cref="TeamGoalRules.Answers"/> allows them.</summary>
    /// <param name="teamId">The team.</param>
    /// <param name="goal">The goal of the team.</param>
    /// <param name="participant">Whom the answer is for.</param>
    /// <param name="mayAnswer">Whether the reader may answer for them.</param>
    /// <param name="itemName">The item's name, for a message.</param>
    /// <param name="answered">Called with the team's goals after an answer went through, or with <see langword="null"/> when they are to be read again (409, 403).</param>
    public void DrawAnswers(long teamId, TeamGoal goal, TeamGoalParticipant participant, bool mayAnswer, string itemName, Action<TeamGoalBoard?> answered)
    {
        var answers = TeamGoalRules.Answers(goal, participant, mayAnswer);
        if (answers.Count == 0)
        {
            return;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{teamId}|{goal.Id}|{participant.CharacterId}|{participant.RosterEntryId}");
        using var id = ImRaii.PushId(key);
        using var disabled = ImRaii.Disabled(_writing);
        foreach (var answer in answers)
        {
            ImGui.SameLine();
            var label = _localizer.Get(answer switch
            {
                TeamGoalAnswer.Have => LocKeys.TeamGoalAnswerHave,
                TeamGoalAnswer.NoInterest => LocKeys.TeamGoalAnswerNoInterest,
                _ => LocKeys.TeamGoalAnswerTakeBack,
            });

            if (TeamGoalRules.AsksTwice(goal, answer))
            {
                if (_armed == key)
                {
                    if (ImGui.SmallButton(label + "?"))
                    {
                        _armed = null;
                        Send(teamId, goal, participant, answer, itemName, answered);
                    }

                    Hint();
                    ImGui.SameLine();
                    if (ImGui.SmallButton("x"))
                    {
                        _armed = null;
                    }
                }
                else
                {
                    if (ImGui.SmallButton(label))
                    {
                        _armed = key;
                    }

                    Hint();
                }

                continue;
            }

            if (ImGui.SmallButton(label))
            {
                Send(teamId, goal, participant, answer, itemName, answered);
            }
        }
    }

    private void Hint()
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(_localizer.Get(LocKeys.TeamGoalHaveHint));
        }
    }

    private void Send(long teamId, TeamGoal goal, TeamGoalParticipant participant, TeamGoalAnswer answer, string itemName, Action<TeamGoalBoard?> answered)
    {
        _writing = true;
        _message = null;
        var language = Language;
        var mark = TeamGoalRules.MarkOf(answer);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _goals.MarkTeamGoalAsync(teamId, goal.Id, participant, mark, language, _cts.Token).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    answered(result.Value?.Data);
                }
                else
                {
                    _message = TeamGoalMessages.Describe(_localizer, result.Error, itemName);

                    // A goal closed meanwhile, or a right taken away: what is shown is out of date, read it again.
                    if (result.Error?.Kind is ApiErrorKind.Conflict or ApiErrorKind.Forbidden)
                    {
                        answered(null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // closing down
            }
            catch (Exception ex)
            {
                _log.Error($"Team goal answer failed: {ex.GetType().Name}.");
            }
            finally
            {
                _writing = false;
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>The player's sentence for a goal of the team that could not be set or answered, from the contract's answers.</summary>
internal static class TeamGoalMessages
{
    /// <summary>Describes a failed write on a goal of the team.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="error">The error.</param>
    /// <param name="name">The item's name.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(Localizer localizer, ApiError? error, string name) => error?.Kind switch
    {
        ApiErrorKind.Forbidden => localizer.Get(LocKeys.TeamGoalForbidden),
        ApiErrorKind.Conflict => localizer.Get(LocKeys.TeamGoalConflict),
        ApiErrorKind.BadRequest or ApiErrorKind.Validation => localizer.Get(LocKeys.TeamGoalUnknown, name),
        ApiErrorKind.NotFound => localizer.Get(LocKeys.TeamGoalNotFound),
        ApiErrorKind.RateLimited => localizer.Get(LocKeys.GoalBusy),
        _ => localizer.Get(LocKeys.TeamGoalFailed, error?.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? error?.Kind.ToString() ?? "?"),
    };
}
