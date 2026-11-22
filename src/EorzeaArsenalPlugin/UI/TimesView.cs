using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The "Zeiten" tab of the Teams window (server 1.5): open polls answered with three buttons and from the
/// player's own times, "not tonight", the player's times read-only (the plan of this week and the next,
/// the absences) and the team's next evening in numbers. Painting plans, the rhythm and settling polls stay
/// on the website, which a link opens.
/// </summary>
/// <remarks>
/// Reads follow the contract's limits: the polls when the tab opens and after an answer, never on a short
/// timer, and never twice within ten seconds; the team's pulse every ten seconds while the tab is drawn,
/// and the rest only read again when its mark changed.
/// </remarks>
public sealed class TimesView
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Selected = new(0.25f, 0.45f, 0.7f, 1f);

    private const long PollsMinGapMs = 10_000;
    private const long PulseGapMs = 10_000;

    private readonly Localizer _localizer;
    private readonly TeamsService _teams;
    private readonly WaitingCounter _waiting;
    private readonly Func<string, bool> _hasFeature;
    private readonly Action<string> _openApp;
    private readonly ILog _log;

    private volatile IReadOnlyList<Poll>? _polls;
    private volatile string? _pollsError;
    private volatile bool _pollsLoading;
    private long _pollsReadTicks;
    private bool _pollsDue = true;

    private volatile MyAvailability? _times;
    private volatile string? _timesError;
    private volatile bool _timesLoading;
    private bool _timesDue = true;

    private volatile TeamWeek? _week;
    private volatile TeamWeek? _nextWeek;
    private volatile bool _weekLoading;
    private long _weekTeamId;
    private bool _weekDue = true;

    private long _pulseTeamId;
    private string? _pulseMark;
    private long _pulseTicks;
    private volatile bool _pulseRunning;

    private volatile bool _busy;
    private volatile string? _message;
    private long? _confirmNotTonight; // 0 = every team, else the team
    private bool _confirmOpen;

    /// <summary>Creates the view.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="teams">The teams service (holds the key).</param>
    /// <param name="waiting">The waiting counts, expired after an answer.</param>
    /// <param name="hasFeature">Whether the server lists a feature.</param>
    /// <param name="openApp">Opens a path of the website (starting with <c>/</c>).</param>
    /// <param name="log">Diagnostics sink.</param>
    public TimesView(Localizer localizer, TeamsService teams, WaitingCounter waiting, Func<string, bool> hasFeature, Action<string> openApp, ILog log)
    {
        _localizer = localizer;
        _teams = teams;
        _waiting = waiting;
        _hasFeature = hasFeature;
        _openApp = openApp;
        _log = log;
    }

    /// <summary>Whether the server offers anything this tab shows.</summary>
    public bool Offered => _hasFeature(FeatureNames.TeamAvailability) || _hasFeature(FeatureNames.Polls);

    private bool PollsOffered => _hasFeature(FeatureNames.Polls);

    private bool TimesOffered => _hasFeature(FeatureNames.TeamAvailability);

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    /// <summary>Reads everything again on the next draw (the refresh button, or the tab being opened).</summary>
    public void Invalidate()
    {
        _pollsDue = true;
        _timesDue = true;
        _weekDue = true;
    }

    /// <summary>Draws the tab for one team.</summary>
    /// <param name="teamId">The team chosen in the window.</param>
    public void Draw(long teamId)
    {
        Pulse(teamId);
        LoadDue(teamId);

        if (_message is { } message)
        {
            Wrapped(Yellow, message);
            ImGui.Spacing();
        }

        using var child = ImRaii.Child("##timesView", Vector2.Zero, false);

        if (PollsOffered)
        {
            DrawPolls(teamId);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (TimesOffered)
        {
            DrawNotTonight(teamId);
            ImGui.Spacing();
            DrawMyTimes(teamId);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawEvening(teamId);
        }
    }

    // --- Loading ------------------------------------------------------------------------------------

    private void Pulse(long teamId)
    {
        var now = Environment.TickCount64;
        if (_pulseRunning || (teamId == _pulseTeamId && now - _pulseTicks < PulseGapMs))
        {
            return;
        }

        if (teamId != _pulseTeamId)
        {
            _pulseTeamId = teamId;
            _pulseMark = null;
            _weekDue = true;
        }

        _pulseTicks = now;
        _pulseRunning = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _teams.GetTeamPulseAsync(teamId, CancellationToken.None).ConfigureAwait(false);
                if (result.IsSuccess && result.Value?.Data?.Mark is { } mark && teamId == _pulseTeamId)
                {
                    // Only a different mark is a reason to read again; the first one only sets the baseline.
                    if (_pulseMark is not null && _pulseMark != mark)
                    {
                        Invalidate();
                    }

                    _pulseMark = mark;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Team pulse failed: {ex.GetType().Name}.");
            }
            finally
            {
                _pulseRunning = false;
            }
        });
    }

    private void LoadDue(long teamId)
    {
        var now = Environment.TickCount64;
        if (PollsOffered && _pollsDue && !_pollsLoading && now - _pollsReadTicks >= PollsMinGapMs)
        {
            _pollsDue = false;
            _pollsReadTicks = now;
            _pollsLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _teams.GetMyPollsAsync(CancellationToken.None).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        _polls = result.Value?.All ?? [];
                        _pollsError = null;
                    }
                    else
                    {
                        _pollsError = ErrorText(result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Polls read failed: {ex.GetType().Name}.");
                }
                finally
                {
                    _pollsLoading = false;
                }
            });
        }

        if (TimesOffered && _timesDue && !_timesLoading)
        {
            _timesDue = false;
            _timesLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _teams.GetMyAvailabilityAsync(CancellationToken.None).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        _times = result.Value?.Data;
                        _timesError = null;
                    }
                    else
                    {
                        _timesError = ErrorText(result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Times read failed: {ex.GetType().Name}.");
                }
                finally
                {
                    _timesLoading = false;
                }
            });
        }

        if (TimesOffered && (_weekDue || _weekTeamId != teamId) && !_weekLoading)
        {
            _weekDue = false;
            _weekTeamId = teamId;
            _weekLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _teams.GetTeamAvailabilityAsync(teamId, null, CancellationToken.None).ConfigureAwait(false);
                    var week = result.IsSuccess ? result.Value?.Data : null;
                    TeamWeek? next = null;

                    // With nothing left this week, the evening to show is next week's first.
                    if (week is not null && AvailabilityRules.NextEvent(week, DateTime.Now) is null &&
                        DateOnly.TryParseExact(week.Week, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monday))
                    {
                        var following = await _teams.GetTeamAvailabilityAsync(
                            teamId, monday.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
                        next = following.IsSuccess ? following.Value?.Data : null;
                    }

                    if (teamId == _weekTeamId)
                    {
                        _week = week;
                        _nextWeek = next;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Team week read failed: {ex.GetType().Name}.");
                }
                finally
                {
                    _weekLoading = false;
                }
            });
        }
    }

    // --- Polls --------------------------------------------------------------------------------------

    private void DrawPolls(long teamId)
    {
        ImGui.TextColored(Yellow, T(LocKeys.TimesPolls));
        ImGui.Spacing();

        if (_pollsError is { } error)
        {
            Wrapped(Red, error);
            return;
        }

        var polls = _polls;
        if (polls is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        var shown = polls.Where(AvailabilityRules.Shows).OrderBy(p => p.TeamId == teamId ? 0 : 1).ToList();
        if (shown.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TimesNoPolls));
            return;
        }

        foreach (var poll in shown)
        {
            using var id = ImRaii.PushId($"poll_{poll.Id}");
            DrawPoll(poll, teamId);
            ImGui.Spacing();
        }
    }

    private void DrawPoll(Poll poll, long teamId)
    {
        var title = poll.Title ?? $"#{poll.Id}";
        if (poll.TeamId != teamId && !string.IsNullOrEmpty(poll.TeamName))
        {
            title = $"{title}   ·   {poll.TeamName}";
        }

        ImGui.TextUnformatted(title);
        ImGui.TextColored(Dim, $"{_localizer.Get(LocKeys.TimesClosesAt, DateTimeText(poll.ClosesAt))}   ·   {_localizer.Get(LocKeys.TimesAnswered, poll.Answered, poll.Of)}");
        if (!string.IsNullOrWhiteSpace(poll.Note))
        {
            Wrapped(Dim, poll.Note!);
        }

        // An open week is asked first: the times cannot speak for a week whose plan is not chosen yet.
        var openWeeks = poll.MyOpenWeeks ?? [];
        foreach (var week in openWeeks)
        {
            DrawOpenWeek(week);
        }

        foreach (var option in AvailabilityRules.VisibleOptions(poll))
        {
            using var oid = ImRaii.PushId($"opt_{option.Id}");
            DrawOption(poll, option);
        }

        var fromTimes = AvailabilityRules.AnswersFromTimes(poll);
        using (ImRaii.Disabled(_busy || fromTimes.Count == 0 || openWeeks.Count > 0))
        {
            if (ImGui.SmallButton(T(LocKeys.TimesAnswerFromTimes)))
            {
                AnswerMany(poll.TeamId, poll.Id, [.. fromTimes.Select(a => (a.OptionId, (string?)a.Answer))]);
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(T(LocKeys.TimesWebsite)))
        {
            _openApp($"/teams/{poll.TeamId}/zeiten?poll={poll.Id}");
        }
    }

    private void DrawOpenWeek(OpenWeek week)
    {
        Wrapped(Yellow, _localizer.Get(LocKeys.TimesOpenWeek, DateText(week.Week)));
        foreach (var plan in week.Plans)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(_busy))
            {
                if (ImGui.SmallButton($"{plan.Name ?? $"#{plan.Id}"}##ow_{week.Week}_{plan.Id}"))
                {
                    SetWeek(week.Week!, plan.Id, week.TeamId);
                }
            }
        }
    }

    private void DrawOption(Poll poll, PollOption option)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted(OptionText(option));

        var counts = option.Counts ?? new PollCounts();
        ImGui.SameLine();
        ImGui.TextColored(Dim, _localizer.Get(LocKeys.TimesCounts, counts.Yes, counts.Maybe, counts.No));
        if (option.Best)
        {
            ImGui.SameLine();
            ImGui.TextColored(Green, T(LocKeys.TimesBest));
        }

        if (option.BelowMin)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, T(LocKeys.TimesBelowMin));
        }

        ImGui.Indent();
        if (option.Chosen)
        {
            ImGui.TextColored(Green, T(LocKeys.TimesChosen));
            ImGui.Unindent();
            return;
        }

        var suggested = AvailabilityRules.SuggestedAnswer(option.MyFit);
        AnswerButton(poll, option, AvailabilityRules.Yes, LocKeys.TimesYes, suggested);
        ImGui.SameLine();
        AnswerButton(poll, option, AvailabilityRules.Maybe, LocKeys.TimesMaybe, suggested);
        ImGui.SameLine();
        AnswerButton(poll, option, AvailabilityRules.No, LocKeys.TimesNo, suggested);

        if (FitText(option.MyFit) is { } fit)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, fit);
        }

        ImGui.Unindent();
    }

    /// <summary>
    /// One of the three answers. The player's own answer is filled; the answer the times suggest has
    /// yellow text while it is not chosen. A click on the chosen answer takes it back.
    /// </summary>
    private void AnswerButton(Poll poll, PollOption option, string answer, string labelKey, string? suggested)
    {
        var mine = string.Equals(option.Mine, answer, StringComparison.Ordinal);
        using (ImRaii.PushColor(ImGuiCol.Button, Selected, mine))
        using (ImRaii.PushColor(ImGuiCol.Text, Yellow, !mine && suggested == answer))
        using (ImRaii.Disabled(_busy))
        {
            if (ImGui.SmallButton($"{T(labelKey)}##{answer}"))
            {
                AnswerMany(poll.TeamId, poll.Id, [(option.Id, mine ? null : answer)]);
            }
        }
    }

    private string OptionText(PollOption option)
    {
        var hours = Hours(option.Time, option.EndTime);
        if (!string.Equals(option.Kind, "weekly", StringComparison.Ordinal))
        {
            return $"{DayAndDate(option.FirstDate)} {hours}";
        }

        // As the server writes it: the days at the candidate's own time, then each day at another time.
        var dayTimes = option.DayTimes ?? [];
        var ownDays = (option.Weekdays ?? []).Where(d => !dayTimes.ContainsKey(d.ToString(CultureInfo.InvariantCulture))).Select(DayName);
        var parts = new List<string> { $"{string.Join(", ", ownDays)} {hours}".Trim() };
        foreach (var (day, time) in dayTimes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (int.TryParse(day, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weekday))
            {
                parts.Add($"{DayName(weekday)} {Hours(time.Time, time.EndTime)}");
            }
        }

        return $"{T(LocKeys.TimesWeekly)} {string.Join(", ", parts)} {_localizer.Get(LocKeys.TimesFromDate, DayAndDate(option.FirstDate))}";
    }

    private string? FitText(PollFit? fit) => fit?.State switch
    {
        "yes" => T(LocKeys.TimesFitYes),
        "maybe" => T(LocKeys.TimesFitMaybe),
        "no" => T(LocKeys.TimesFitNo),
        "unclear" => T(LocKeys.TimesFitUnclear),
        "mixed" => _localizer.Get(LocKeys.TimesFitMixed, fit.Fits, fit.Of),
        _ => null,
    };

    private void AnswerMany(long teamId, long pollId, IReadOnlyList<(long OptionId, string? Answer)> answers)
    {
        _busy = true;
        _message = null;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var (optionId, answer) in answers)
                {
                    var result = await _teams.AnswerPollAsync(teamId, pollId, optionId, answer, CancellationToken.None).ConfigureAwait(false);
                    if (!result.IsSuccess)
                    {
                        _message = ErrorText(result.Error);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Poll answer failed: {ex.GetType().Name}.");
                _message = T(LocKeys.TeamsErrorGeneric);
            }
            finally
            {
                // Read again after an answer, as the contract asks; the counts change with it.
                _pollsDue = true;
                _pollsReadTicks = 0;
                _waiting.Expire();
                _busy = false;
            }
        });
    }

    private void SetWeek(string week, long? planId, long? teamId)
    {
        _busy = true;
        _message = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _teams.SetWeekPlanAsync(new AvailabilityWeekRequest { Week = week, PlanId = planId, TeamId = teamId }, CancellationToken.None).ConfigureAwait(false);
                _message = result.IsSuccess ? T(LocKeys.TeamsSaved) : ErrorText(result.Error);
            }
            catch (Exception ex)
            {
                _log.Error($"Week plan failed: {ex.GetType().Name}.");
                _message = T(LocKeys.TeamsErrorGeneric);
            }
            finally
            {
                _pollsDue = true;
                _pollsReadTicks = 0;
                _timesDue = true;
                _busy = false;
            }
        });
    }

    // --- Not tonight ---------------------------------------------------------------------------------

    private void DrawNotTonight(long teamId)
    {
        // A press tells the schedule managers, and the team's channel too, so it asks once before it goes.
        if (_confirmOpen && _confirmNotTonight is { } scope)
        {
            Wrapped(Yellow, T(LocKeys.TimesNotTonightConfirm));
            using (ImRaii.Disabled(_busy))
            {
                if (ImGui.Button(T(LocKeys.TimesConfirm)))
                {
                    _confirmOpen = false;
                    NotTonight(scope == 0 ? null : scope);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.TeamsAbsenceCancel)))
            {
                _confirmOpen = false;
            }

            return;
        }

        using (ImRaii.Disabled(_busy))
        {
            if (ImGui.Button(T(LocKeys.TimesNotTonight)))
            {
                _confirmNotTonight = 0;
                _confirmOpen = true;
            }

            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.TimesNotTonightTeam)))
            {
                _confirmNotTonight = teamId;
                _confirmOpen = true;
            }
        }
    }

    private void NotTonight(long? teamId)
    {
        _busy = true;
        _message = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _teams.NotTonightAsync(teamId, CancellationToken.None).ConfigureAwait(false);
                _message = result.IsSuccess ? T(LocKeys.TeamsSaved) : ErrorText(result.Error);
            }
            catch (Exception ex)
            {
                _log.Error($"Not tonight failed: {ex.GetType().Name}.");
                _message = T(LocKeys.TeamsErrorGeneric);
            }
            finally
            {
                _timesDue = true;
                _weekDue = true;
                _busy = false;
            }
        });
    }

    // --- My times -----------------------------------------------------------------------------------

    private void DrawMyTimes(long teamId)
    {
        ImGui.TextColored(Yellow, T(LocKeys.TimesMine));
        ImGui.Spacing();

        if (_timesError is { } error)
        {
            Wrapped(Red, error);
            return;
        }

        var times = _times;
        if (times is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        var scope = AvailabilityRules.ScopeFor(times, teamId);
        if (scope is null || scope.Plans.Count == 0 ||
            !DateOnly.TryParseExact(times.ThisWeek, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var thisWeek))
        {
            ImGui.TextDisabled(T(LocKeys.TimesNoTimes));
        }
        else
        {
            ImGui.TextUnformatted(_localizer.Get(LocKeys.TimesThisWeek, WeekText(AvailabilityRules.PlanForWeek(scope, thisWeek))));
            var next = thisWeek.AddDays(7);
            ImGui.TextUnformatted(_localizer.Get(LocKeys.TimesNextWeek, WeekText(AvailabilityRules.PlanForWeek(scope, next))));

            // Setting the plan of the coming week is the one edit the plugin offers; painting stays on the site.
            if (scope.Plans.Count > 1)
            {
                ImGui.TextColored(Dim, T(LocKeys.TimesSetWeek));
                foreach (var plan in scope.Plans)
                {
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_busy))
                    {
                        if (ImGui.SmallButton($"{plan.Name ?? $"#{plan.Id}"}##nw_{plan.Id}"))
                        {
                            SetWeek(next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), plan.Id, scope.TeamId);
                        }
                    }
                }
            }
        }

        if (ImGui.SmallButton(T(LocKeys.TimesEditOnSite)))
        {
            _openApp("/konto");
        }

        if (times.Absences.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(Dim, T(LocKeys.TimesMyAbsences));
        foreach (var absence in times.Absences.OrderBy(a => a.FromDate, StringComparer.Ordinal))
        {
            using var id = ImRaii.PushId($"myabs_{absence.Id}");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(AbsenceText(absence, times));
            ImGui.SameLine();
            using (ImRaii.Disabled(_busy))
            {
                if (ImGui.SmallButton(T(LocKeys.TeamsAbsenceDelete)))
                {
                    DeleteAbsence(absence.Id);
                }
            }
        }
    }

    private string WeekText(WeekPlan week) => week.Source switch
    {
        WeekPlanSource.Unclear => T(LocKeys.TimesWeekOpen),
        WeekPlanSource.Grundplan => _localizer.Get(LocKeys.TimesGrundplan, week.Plan?.Name ?? string.Empty),
        WeekPlanSource.None => T(LocKeys.TimesWeekOpen),
        _ => week.Plan?.Name ?? string.Empty,
    };

    private string AbsenceText(Absence absence, MyAvailability times)
    {
        var dates = absence.FromDate == absence.ToDate ? DateText(absence.FromDate) : $"{DateText(absence.FromDate)} - {DateText(absence.ToDate)}";
        var parts = new List<string> { dates };
        if (!string.IsNullOrEmpty(absence.FromTime) && !string.IsNullOrEmpty(absence.ToTime))
        {
            parts.Add(AbsenceRules.RunsPastMidnight(absence.FromTime, absence.ToTime)
                ? $"{absence.FromTime}-{absence.ToTime} ({T(LocKeys.TeamsAbsenceNextDay)})"
                : $"{absence.FromTime}-{absence.ToTime}");
        }

        if (!AbsenceRules.CountsAsAway(absence.Level))
        {
            parts.Add(absence.Level == AbsenceRules.LevelCan ? T(LocKeys.TeamsAbsenceLevelCan) : T(LocKeys.TeamsAbsenceLevelIfNeeded));
        }

        parts.Add(absence.TeamId is null or 0
            ? $"[{T(LocKeys.TeamsAbsenceAllTeams)}]"
            : $"[{times.Teams.FirstOrDefault(t => t.Id == absence.TeamId)?.Name ?? $"#{absence.TeamId}"}]");

        if (!string.IsNullOrWhiteSpace(absence.Note))
        {
            parts.Add($"({absence.Note})");
        }

        return string.Join("  ", parts);
    }

    private void DeleteAbsence(long absenceId)
    {
        _busy = true;
        _message = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _teams.DeleteMyAbsenceAsync(absenceId, CancellationToken.None).ConfigureAwait(false);
                _message = result.IsSuccess ? T(LocKeys.TeamsSaved) : ErrorText(result.Error);
            }
            catch (Exception ex)
            {
                _log.Error($"Own absence delete failed: {ex.GetType().Name}.");
                _message = T(LocKeys.TeamsErrorGeneric);
            }
            finally
            {
                _timesDue = true;
                _weekDue = true;
                _busy = false;
            }
        });
    }

    // --- The team's evening --------------------------------------------------------------------------

    private void DrawEvening(long teamId)
    {
        ImGui.TextColored(Yellow, T(LocKeys.TimesEvening));
        ImGui.Spacing();

        var week = _week;
        if (week is null || _weekTeamId != teamId)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        var (shownWeek, ev) = AvailabilityRules.NextEvent(week, DateTime.Now) is { } thisOne
            ? (week, thisOne)
            : _nextWeek is { } next && next.Events.Count > 0
                ? (next, next.Events.OrderBy(e => e.Date, StringComparer.Ordinal).ThenBy(e => e.Time, StringComparer.Ordinal).First())
                : (week, null);

        if (ev is null)
        {
            ImGui.TextDisabled(T(LocKeys.TimesEveningNone));
        }
        else
        {
            ImGui.TextUnformatted($"{ev.Title ?? $"#{ev.EventId}"}   ·   {DayAndDate(ev.Date)} {Hours(ev.Time, ev.EndTime)}");
            if (AvailabilityRules.AtStart(shownWeek, ev) is { } counts)
            {
                Wrapped(Dim, _localizer.Get(LocKeys.TimesEveningCounts, counts.Can, counts.Maybe, counts.Unclear, counts.Cannot, counts.Size));
            }
        }

        if (shownWeek.Missing is { Count: > 0 } missing)
        {
            var line = _localizer.Get(LocKeys.TimesMissing, missing.Count, missing.Of);
            Wrapped(Dim, missing.Names is { Count: > 0 } names ? $"{line}: {string.Join(", ", names)}" : line);
        }

        if (ImGui.SmallButton($"{T(LocKeys.TimesWebsite)}##evening"))
        {
            _openApp($"/teams/{teamId}/zeiten");
        }
    }

    // --- Text helpers --------------------------------------------------------------------------------

    private CultureInfo Culture() => German ? new CultureInfo("de-DE") : new CultureInfo("en-US");

    private string DayName(int weekday) => Culture().DateTimeFormat.AbbreviatedDayNames[((weekday % 7) + 7) % 7].TrimEnd('.');

    private string DateText(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString(German ? "dd.MM." : "MM/dd", CultureInfo.InvariantCulture)
            : iso ?? string.Empty;

    private string DayAndDate(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"{DayName((int)d.DayOfWeek)} {DateText(iso)}"
            : iso ?? string.Empty;

    private string DateTimeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var space = value.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? $"{DayAndDate(value[..space])} {value[(space + 1)..]}" : DayAndDate(value);
    }

    private static string Hours(string? from, string? to) =>
        string.IsNullOrEmpty(to) ? from ?? string.Empty : $"{from}-{to}";

    private string ErrorText(ApiError? error) => error?.Kind switch
    {
        ApiErrorKind.RateLimited => T(LocKeys.TeamsErrorRateLimited),
        ApiErrorKind.Forbidden when error.MissingScope is not null => T(LocKeys.TeamsScopeHint),
        ApiErrorKind.Forbidden => T(LocKeys.TeamsErrorForbidden),
        ApiErrorKind.Network => T(LocKeys.TeamsErrorNetwork),
        ApiErrorKind.BadRequest when !string.IsNullOrWhiteSpace(error.Detail) => $"{T(LocKeys.TeamsErrorGeneric)} ({error.Detail})",
        _ => error?.StatusCode is { } status ? $"{T(LocKeys.TeamsErrorGeneric)} (HTTP {status})" : T(LocKeys.TeamsErrorGeneric),
    };

    private static void Wrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
