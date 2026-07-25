using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Services;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The Teams companion window: per-team mit cheat sheets (a time-axis timeline), the content hub, the
/// who-needs-what farm, FFLogs and own-absence management. The calendar lives in its own cross-team
/// window. Purely renders server-authoritative data via <see cref="TeamsService"/> (R8/R9); links open
/// in the browser and large images open in a dedicated window.
/// </summary>
public sealed class TeamsWindow : Window
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly SourcingView _sourcing;
    private readonly ObtainService _obtain;
    private readonly IWorldActions _world;
    private readonly HoldingsService _holdings;

    // The player's own server character id, so a farm row of theirs can be told from a teammate's.
    private readonly Func<long?> _myCharacterId;
    private readonly HashSet<long> _farmObtainRequested = [];
    private readonly TeamsService _teams;
    private readonly ITextureProvider _textures;
    private readonly IDataManager _data;
    private readonly IPlayerState _playerState;
    private readonly ILog _log;
    private readonly Action _save;
    private readonly Action _openConfig;
    private readonly Action<long, long, string?> _openImage;

    private readonly Slot<TeamsResponse> _teamsSlot = new();
    private readonly Slot<MitSheetResponse> _mitSlot = new();
    private readonly Slot<ContentSheetResponse> _contentSlot = new();
    private readonly Slot<FarmResponse> _farmSlot = new();
    private readonly Slot<LogsResponse> _logsSlot = new();
    private readonly Slot<AbsencesResponse> _absenceSlot = new();

    private int _teamIndex;
    private int _planIndex;
    private string? _selectedJob;
    private string _mitPlanKey = string.Empty;
    private bool[] _phaseChecked = [];
    private bool _tagRaidwide = true;
    private bool _tagTankbuster = true;
    private bool _tagOther;
    private bool _showAllJobs;
    private string _activeTab = "mit";
    private string _activeQuery = string.Empty;

    private string _absFrom = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absTo = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absNote = string.Empty;
    private long _editingAbsenceId;
    private volatile string? _actionMessage;
    private string? _absenceError;

    /// <summary>Creates the Teams window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="teams">The teams service (reads + writes; holds the key).</param>
    /// <param name="textures">Texture provider (skill icons).</param>
    /// <param name="data">Excel data (resolves skill icons from action ids).</param>
    /// <param name="playerState">Local player (current job for the mit-sheet preselection).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="save">Persists config (remembered team/job/filters).</param>
    /// <param name="openConfig">Opens the settings window.</param>
    /// <param name="world">Game actions (owned counts, open the map at a vendor) for the farm sourcing.</param>
    /// <param name="obtain">Chain-complete "how to get it" sourcing, to enrich the farm's own routes.</param>
    /// <param name="holdings">Server-side owned counts (retainers included) for the farm's have/need.</param>
    /// <param name="myCharacterId">
    /// The player's own server character id, so a farm row belonging to them can be told apart from a
    /// teammate's — only their own may be measured against what the plugin can see.
    /// </param>
    /// <param name="openImage">Opens a content-hub image in the image window (teamId, resourceId, title).</param>
    public TeamsWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        TeamsService teams,
        ITextureProvider textures,
        IDataManager data,
        IPlayerState playerState,
        IWorldActions world,
        ObtainService obtain,
        HoldingsService holdings,
        Func<long?> myCharacterId,
        ILog log,
        Action save,
        Action openConfig,
        Action<long, long, string?> openImage)
        : base("Eorzea Arsenal — Teams###EorzeaArsenalTeams")
    {
        _myCharacterId = myCharacterId;
        _config = config;
        _store = store;
        _localizer = localizer;
        _sourcing = new SourcingView(localizer, world, obtain, holdings);
        _obtain = obtain;
        _world = world;
        _holdings = holdings;
        _teams = teams;
        _textures = textures;
        _data = data;
        _playerState = playerState;
        _log = log;
        _save = save;
        _openConfig = openConfig;
        _openImage = openImage;

        // The tabs manage their own scroll (mit = table, others = child), so the window itself never
        // shows a second scrollbar.
        Flags = ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 460),
            MaximumSize = new Vector2(1600, 1300),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    /// <inheritdoc />
    public override void Draw()
    {
        if (!_store.HasKey || !_config.Enabled || !_config.SyncTeams)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Dim))
            {
                ImGui.TextWrapped(T(LocKeys.TeamsDisabledHint));
            }

            ImGui.Spacing();
            if (ImGui.Button(T(LocKeys.OpenSettings)))
            {
                _openConfig();
            }

            return;
        }

        if (_teams.LastOutcome == TeamsPollOutcome.ScopeMissing)
        {
            ImGui.TextColored(Yellow, T(LocKeys.TeamsScopeHint));
            ImGui.Spacing();
        }

        DrawHeader();

        using var tabBar = ImRaii.TabBar("##eaTeamsTabs");
        if (!tabBar)
        {
            return;
        }

        Tab(LocKeys.TeamsTabEvents, "termine", DrawEvents);
        Tab(LocKeys.TeamsTabMit, "mit", DrawMit);
        Tab(LocKeys.TeamsTabContent, "inhalte", DrawContent);
        Tab(LocKeys.TeamsTabFarm, "farm", DrawFarm);
        Tab(LocKeys.TeamsTabLogs, "logs", DrawLogs);
        Tab(LocKeys.TeamsTabAbsence, "termine", DrawAbsence);
    }

    private void Tab(string key, string slug, Action body)
    {
        using var tab = ImRaii.TabItem(T(key));
        if (!tab)
        {
            return;
        }

        _activeTab = slug;
        // Tabs that can preselect something on the web page set this while drawing.
        _activeQuery = string.Empty;
        ImGui.Spacing();
        body();
        ImGui.Spacing();
    }

    // --- Header: team picker + refresh + help + open-in-web ---------------------------------------

    private void DrawHeader()
    {
        EnsureTeams();
        var teams = _teamsSlot.Value?.Data ?? [];
        if (teams.Count > 0)
        {
            if (_teamIndex >= teams.Count)
            {
                _teamIndex = 0;
            }

            if (_config.TeamsLastTeamId != 0)
            {
                var idx = teams.FindIndex(t => t.Id == _config.TeamsLastTeamId);
                if (idx >= 0)
                {
                    _teamIndex = idx;
                    _config.TeamsLastTeamId = 0;
                }
            }

            var names = teams.Select(t => t.Name ?? $"#{t.Id}").ToArray();
            ImGui.SetNextItemWidth(240f);
            var idxRef = _teamIndex;
            if (ImGui.Combo(T(LocKeys.TeamsTeamLabel), ref idxRef, names, names.Length))
            {
                _teamIndex = idxRef;
                _planIndex = 0;
                _selectedJob = null;
                _config.TeamsLastTeamId = teams[_teamIndex].Id;
                _save();
            }
        }
        else if (_teamsSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
        }
        else if (_teamsSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
        }
        else
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.TeamsRefresh)))
        {
            RefreshAll();
        }

        ImGui.SameLine();
        if (CurrentTeam() is { } team && ImGui.Button(T(LocKeys.TeamsOpenWeb)))
        {
            // Open the page matching the active tab, preselecting whatever the tab currently shows.
            OpenApp($"/teams/{team.Id}/{_activeTab}{_activeQuery}");
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.TeamsHelp)))
        {
            OpenApp("/hilfe/plugin-sync");
        }

        if (_actionMessage is { } msg)
        {
            ImGui.TextColored(Dim, msg);
        }

        ImGui.Separator();
    }

    private TeamSummary? CurrentTeam()
    {
        var teams = _teamsSlot.Value?.Data;
        return teams is { Count: > 0 } && _teamIndex < teams.Count ? teams[_teamIndex] : null;
    }

    // --- Termine (per-team event list) ------------------------------------------------------------

    private void DrawEvents()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var all = _teams.Calendar.Where(o => o.TeamId == team.Id).ToList();
        if (all.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoEvents));
            return;
        }

        var pastCount = all.Count(o => IsPast(o.Date, today));
        if (pastCount > 0)
        {
            var showPast = _config.TeamsShowPastEvents;
            if (ImGui.Checkbox(_localizer.Get(LocKeys.TeamsShowPast, pastCount), ref showPast))
            {
                _config.TeamsShowPastEvents = showPast;
                _save();
            }
        }

        ImGui.Separator();

        using var child = ImRaii.Child("##events", new Vector2(0, 0), false);
        if (!child)
        {
            return;
        }

        // The list gets long, so it is drawn at a configurable scale with zebra striping (R31).
        var scale = Math.Clamp(_config.TeamsEventTextScale, 1f, 1.6f);
        ImGui.SetWindowFontScale(scale);
        try
        {
            foreach (var group in all.GroupBy(o => o.EventId).OrderBy(g => g.Min(o => o.Date)))
            {
                DrawEventGroup(group.ToList(), today, scale);
            }
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    private void DrawEventGroup(List<CalendarOccurrence> group, DateOnly today, float scale)
    {
        var occs = group
            .Where(o => _config.TeamsShowPastEvents || !IsPast(o.Date, today))
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Time)
            .ToList();
        if (occs.Count == 0)
        {
            return;
        }

        var first = group[0];
        using var eid = ImRaii.PushId($"evgrp_{first.EventId}");
        var kind = string.Equals(first.Kind, "recurring", StringComparison.OrdinalIgnoreCase) ? T(LocKeys.TeamsRecurring) : T(LocKeys.TeamsSingle);
        var content = first.Contents is { Count: > 0 } ? string.Join(", ", first.Contents.Select(c => c.Name)) : first.ContentName;
        ImGui.TextColored(Yellow, first.Title ?? string.Empty);
        ImGui.SameLine();
        ImGui.TextDisabled($"· {kind}" + (string.IsNullOrEmpty(content) ? string.Empty : $" · {content}"));

        using var pad = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f) * scale);
        if (!ImGui.BeginTable("##occ", 4, ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        try
        {
            ImGui.TableSetupColumn("##st", ImGuiTableColumnFlags.WidthFixed, 14f * scale);
            ImGui.TableSetupColumn("##when", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Mo., 00.00.0000    00:00-00:00").X);
            ImGui.TableSetupColumn("##cnt", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(_localizer.Get(LocKeys.TeamsAttendCounts, 88, 88, 88, 88)).X);
            ImGui.TableSetupColumn("##rsvp", ImGuiTableColumnFlags.WidthStretch);

            for (var i = 0; i < occs.Count; i++)
            {
                var occ = occs[i];
                using var id = ImRaii.PushId($"occ_{occ.Date}");
                DrawOccurrenceRow(occ, IsPast(occ.Date, today), scale, i % 2 == 1);
            }
        }
        finally
        {
            ImGui.EndTable();
        }

        ImGui.Spacing();
    }

    private void DrawOccurrenceRow(CalendarOccurrence occ, bool past, float scale, bool oddRow)
    {
        ImGui.TableNextRow();
        if (oddRow)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.055f)));
        }

        ImGui.TableNextColumn();
        var (status, _) = EventStatus(occ);
        var dot = 11f * scale;
        ImGui.ColorButton("##st", status, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoInputs, new Vector2(dot, dot));

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        var time = $"{occ.Time}" + (string.IsNullOrEmpty(occ.EndTime) ? string.Empty : $"–{occ.EndTime}");
        var signedOff = string.Equals(occ.OwnStatus, "no", StringComparison.OrdinalIgnoreCase);
        using (ImRaii.PushColor(ImGuiCol.Text, Dim, past || signedOff))
        {
            ImGui.TextUnformatted($"{LocalDate(occ.Date)}    {time}");
            if (signedOff)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var midY = (min.Y + max.Y) / 2f;
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, midY), new Vector2(max.X, midY), ImGui.GetColorU32(ImGuiCol.Text));
            }
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(_localizer.Get(LocKeys.TeamsAttendCounts, occ.Yes, occ.Maybe, occ.No, occ.Total));

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(past))
        {
            Rsvp(occ, "yes", LocKeys.TeamsRsvpYes, Green);
            ImGui.SameLine();
            Rsvp(occ, "maybe", LocKeys.TeamsRsvpMaybe, Yellow);
            ImGui.SameLine();
            Rsvp(occ, "no", LocKeys.TeamsRsvpNo, Red);
        }

        // Past occurrences stay linkable — the web page reveals them for a direct link.
        ImGui.SameLine();
        if (ImGui.SmallButton(T(LocKeys.TeamsOpenWeb)) && occ.Date is { } date)
        {
            OpenApp($"/teams/{occ.TeamId}/termine?event={occ.EventId}&date={date}");
        }
    }

    private void Rsvp(CalendarOccurrence occ, string status, string labelKey, Vector4 activeColor)
    {
        var isOwn = string.Equals(occ.OwnStatus, status, StringComparison.OrdinalIgnoreCase);
        using var color = ImRaii.PushColor(ImGuiCol.Text, activeColor, isOwn);
        var label = (isOwn ? "● " : string.Empty) + T(labelKey);
        if (ImGui.SmallButton($"{label}##rsvp_{status}") && occ.Date is { } date)
        {
            SetRsvp(occ.TeamId, occ.EventId, date, status);
        }
    }

    private void SetRsvp(long teamId, long eventId, string date, string status)
    {
        _actionMessage = T(LocKeys.TeamsWorking);
        _ = Task.Run(async () =>
        {
            try
            {
                var req = new AttendanceRequest { OccurrenceDate = date, Status = status };
                var res = await _teams.SetAttendanceAsync(teamId, eventId, req, CancellationToken.None).ConfigureAwait(false);
                _actionMessage = res.IsSuccess ? T(LocKeys.TeamsSaved) : Describe(res.Error);
                if (res.IsSuccess)
                {
                    _teams.RequestPoll(force: true);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RSVP failed: {ex.GetType().Name}.");
                _actionMessage = T(LocKeys.TeamsErrorGeneric);
            }
        });
    }

    private static (Vector4 Color, int Kind) EventStatus(CalendarOccurrence o)
    {
        if (o.No > 0)
        {
            return (new Vector4(0.9f, 0.4f, 0.4f, 1f), 2);
        }

        if (o.Total > 0 && o.Yes >= o.Total)
        {
            return (new Vector4(0.4f, 0.8f, 0.4f, 1f), 0);
        }

        return (new Vector4(0.9f, 0.8f, 0.3f, 1f), 1);
    }

    private static bool IsPast(string? isoDate, DateOnly today) =>
        DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < today;

    // --- Mit cheat sheet (time-axis timeline) -----------------------------------------------------

    private void DrawMit()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        var plans = team.MitPlans ?? [];
        if (plans.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoPlan));
            return;
        }

        if (_planIndex >= plans.Count)
        {
            _planIndex = 0;
        }

        // Plan picker — show a real name (name -> boss -> #id).
        var planNames = plans.Select(PlanLabel).ToArray();
        ImGui.SetNextItemWidth(240f);
        var planRef = _planIndex;
        if (ImGui.Combo(T(LocKeys.TeamsPlanLabel), ref planRef, planNames, planNames.Length))
        {
            _planIndex = planRef;
            _selectedJob = null;
        }

        var plan = plans[_planIndex];
        _activeQuery = $"?plan={plan.Id}";
        ImGui.SameLine();
        if (ImGui.SmallButton(T(LocKeys.TeamsOpenWeb)))
        {
            OpenApp($"/teams/{team.Id}/mit?plan={plan.Id}");
        }

        Ensure(_mitSlot, $"{team.Id}:{plan.Id}", ct => _teams.GetMitSheetAsync(team.Id, plan.Id, ct));

        if (_mitSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_mitSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        var sheet = _mitSlot.Value?.Data;
        if (sheet?.Plan is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoPlan));
            return;
        }

        var jobs = PlanJobs(sheet);
        _selectedJob ??= ResolveJob(plan.Id, jobs);
        SyncPhaseState($"{team.Id}:{plan.Id}", sheet);

        // Controls: job, phase checkboxes, tag filter. The runtime state seeds from the config defaults
        // on each plan change (see SyncPhaseState).
        ImGui.Checkbox(T(LocKeys.TeamsAllJobs), ref _showAllJobs);
        var showAll = _showAllJobs;

        if (!showAll && jobs.Length > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140f);
            var jobIdx = Math.Max(0, Array.IndexOf(jobs, _selectedJob));
            var jobRef = jobIdx;
            if (ImGui.Combo(T(LocKeys.TeamsJobLabel), ref jobRef, jobs, jobs.Length))
            {
                _selectedJob = jobs[jobRef];
                _config.TeamsPlanJob[plan.Id.ToString()] = _selectedJob;
                _save();
            }
        }

        DrawPhaseChecks(sheet);
        DrawTagFilter();
        ImGui.Separator();

        var activeJobs = showAll ? jobs : (string.IsNullOrEmpty(_selectedJob) ? [] : [_selectedJob]);
        DrawMitTimeline(sheet, activeJobs);
    }

    private void DrawPhaseChecks(MitSheet sheet)
    {
        var phases = sheet.Plan?.Phases ?? [];
        if (_phaseChecked.Length <= 1)
        {
            return; // single/none — nothing to filter
        }

        ImGui.TextUnformatted(T(LocKeys.TeamsPhasesLabel));
        for (var p = 0; p < _phaseChecked.Length; p++)
        {
            ImGui.SameLine();
            var on = _phaseChecked[p];
            if (ImGui.Checkbox(PhaseName(phases, p) + $"##phase{p}", ref on))
            {
                _phaseChecked[p] = on;
            }
        }
    }

    private void DrawTagFilter()
    {
        ImGui.TextUnformatted(T(LocKeys.TeamsFilterLabel));
        ImGui.SameLine();
        ImGui.Checkbox(T(LocKeys.TeamsTagRaidwide) + "##tRw", ref _tagRaidwide);
        ImGui.SameLine();
        ImGui.Checkbox(T(LocKeys.TeamsTagTankbuster) + "##tTb", ref _tagTankbuster);
        ImGui.SameLine();
        ImGui.Checkbox(T(LocKeys.TeamsTagOther) + "##tOt", ref _tagOther);
    }

    private void DrawMitTimeline(MitSheet sheet, string[] jobs)
    {
        var cooldowns = (sheet.Cooldowns ?? []).ToDictionary(c => c.Id, c => c);
        var rows = sheet.Rows ?? [];
        var placements = sheet.Placements ?? [];
        var phases = sheet.Plan?.Phases ?? [];
        var phaseCount = Math.Max(1, _phaseChecked.Length);

        // Collect the visible phases up front so we can render one table (one scrollbar) and show an
        // empty state when nothing matches the phase/tag/job filters.
        var visible = new List<(int Phase, List<MitRow> Mechs, List<MitPlacement> Places)>();
        for (var p = 0; p < phaseCount; p++)
        {
            if (p < _phaseChecked.Length && !_phaseChecked[p])
            {
                continue;
            }

            var mechs = rows.Where(r => r.Phase == p && TagVisible(r.Tag)).ToList();
            var places = placements.Where(pl => pl.Phase == p && jobs.Contains(pl.Job, StringComparer.OrdinalIgnoreCase)).ToList();
            if (mechs.Count > 0 || places.Count > 0)
            {
                visible.Add((p, mechs, places));
            }
        }

        if (visible.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoPlacements));
            return;
        }

        var iconOnly = _config.TeamsMitDisplay == 1;
        var jobColW = iconOnly ? 46f : 120f;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("##mitAll", 2 + jobs.Length, flags, new Vector2(0, -1)))
        {
            return;
        }

        ImGui.TableSetupColumn(T(LocKeys.TeamsColTime), ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn(T(LocKeys.TeamsColMechanic), ImGuiTableColumnFlags.WidthFixed, 210f);
        foreach (var job in jobs)
        {
            ImGui.TableSetupColumn(job, ImGuiTableColumnFlags.WidthFixed, jobColW);
        }

        ImGui.TableSetupScrollFreeze(2, 1); // keep time + mechanic (and the header) pinned while scrolling jobs
        ImGui.TableHeadersRow();

        var showPhaseHeaders = phaseCount > 1;
        foreach (var (phase, mechs, places) in visible)
        {
            if (showPhaseHeaders)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TextColored(Yellow, "— " + PhaseName(phases, phase) + " —");
            }

            var times = mechs.Select(m => m.TimeS).Concat(places.Select(pl => pl.TimeS)).Distinct().OrderBy(t => t).ToList();
            foreach (var t in times)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatTime(t));

                ImGui.TableNextColumn();
                foreach (var m in mechs.Where(m => m.TimeS == t))
                {
                    var color = ParseColor(m.Color) ?? Yellow;
                    ImGui.TextColored(color, m.Label ?? string.Empty);
                    if (!string.IsNullOrEmpty(m.Tag))
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"[{m.Tag}]");
                    }
                }

                foreach (var job in jobs)
                {
                    ImGui.TableNextColumn();
                    var cell = places.Where(pl => pl.TimeS == t && string.Equals(pl.Job, job, StringComparison.OrdinalIgnoreCase)).ToList();
                    for (var i = 0; i < cell.Count; i++)
                    {
                        if (i > 0 && iconOnly)
                        {
                            ImGui.SameLine();
                        }

                        cooldowns.TryGetValue(cell[i].CatalogId, out var cd);
                        DrawCooldown(cd, cell[i].CatalogId, jobColW);
                    }
                }
            }
        }

        ImGui.EndTable();
    }

    private void DrawCooldown(MitCooldown? cd, long catalogId, float colWidth)
    {
        var name = cd is null ? $"#{catalogId}" : (German && !string.IsNullOrEmpty(cd.NameDe) ? cd.NameDe : cd.Name) ?? $"#{catalogId}";
        var mode = _config.TeamsMitDisplay; // 0 = icon + name, 1 = icon only, 2 = name only

        if (mode != 2 && cd is not null)
        {
            var iconId = ActionIcon(cd.ActionId);
            if (iconId != 0)
            {
                var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                ImGui.Image(wrap.Handle, new Vector2(22f, 22f));
            }
            else
            {
                ImGui.Dummy(new Vector2(22f, 22f));
            }

            if (ImGui.IsItemHovered())
            {
                DrawActionTooltip(cd, name);
            }

            if (mode == 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(Truncate(name, colWidth - 30f));
            }
        }
        else
        {
            ImGui.TextUnformatted(Truncate(name, colWidth - 6f));
        }
    }

    /// <summary>Renders a game-like action tooltip (name, category, range/radius, cast/recast, description).</summary>
    private void DrawActionTooltip(MitCooldown cd, string name)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(340f);
        ImGui.TextUnformatted($"{cd.Job}: {name}");

        try
        {
            var action = _data.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(cd.ActionId);
            if (action is { } a)
            {
                var category = a.ActionCategory.ValueNullable?.Name.ExtractText();
                if (!string.IsNullOrEmpty(category))
                {
                    ImGui.TextDisabled(category);
                }

                ImGui.TextDisabled($"{T(LocKeys.TeamsRange)}: {(a.Range < 0 ? "—" : a.Range + "y")}   {T(LocKeys.TeamsRadius)}: {a.EffectRange}y");
                var cast = a.Cast100ms / 10.0;
                var recast = a.Recast100ms / 10.0;
                ImGui.TextDisabled($"{T(LocKeys.TeamsCast)}: {(cast <= 0 ? T(LocKeys.TeamsInstant) : cast.ToString("0.#", CultureInfo.InvariantCulture) + "s")}   {T(LocKeys.TeamsRecast)}: {recast.ToString("0.#", CultureInfo.InvariantCulture)}s");

                var desc = _data.GetExcelSheet<Lumina.Excel.Sheets.ActionTransient>()?.GetRowOrDefault(cd.ActionId)?.Description.ExtractText();
                if (!string.IsNullOrEmpty(desc))
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted(desc);
                }
            }
        }
        catch
        {
            // Fall back to just the name if the game data can't be read.
        }

        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static string Truncate(string text, float maxWidth)
    {
        if (maxWidth <= 0 || ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        for (var len = text.Length - 1; len > 1; len--)
        {
            var candidate = text[..len] + "…";
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return candidate;
            }
        }

        return text;
    }

    // --- Content hub ------------------------------------------------------------------------------

    private void DrawContent()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        Ensure(_contentSlot, team.Id.ToString(), ct => _teams.GetContentSheetAsync(team.Id, ct));
        if (_contentSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_contentSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        var contents = _contentSlot.Value?.Data ?? [];
        if (contents.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoContent));
            return;
        }

        using var child = ImRaii.Child("##hub", new Vector2(0, 0), false);
        foreach (var content in contents)
        {
            using var id = ImRaii.PushId($"content_{content.Id}");
            if (!ImGui.CollapsingHeader($"{content.Name}##c{content.Id}"))
            {
                continue;
            }

            _activeQuery = $"?content={content.Id}";
            if (ImGui.SmallButton($"{T(LocKeys.TeamsOpenWeb)}##web{content.Id}"))
            {
                OpenApp($"/teams/{team.Id}/inhalte?content={content.Id}");
            }

            DrawBosses(content.Bosses);
            DrawResources(team.Id, content.Resources);
        }
    }

    private void DrawBosses(List<BossEntry>? bosses)
    {
        if (bosses is not { Count: > 0 })
        {
            return;
        }

        ImGui.TextColored(Dim, T(LocKeys.TeamsBosses));
        foreach (var boss in bosses)
        {
            var floor = boss.Floor is { } f ? $"[{f}] " : string.Empty;
            ImGui.TextUnformatted($"{floor}{boss.Name}");
            if (boss.Drops is { Count: > 0 })
            {
                var drops = string.Join(", ", boss.Drops.Select(d => d.Value));
                ImGui.SameLine();
                ImGui.TextDisabled($"— {drops}");
            }
        }

        ImGui.Spacing();
    }

    private void DrawResources(long teamId, List<ResourceEntry>? resources)
    {
        if (resources is not { Count: > 0 })
        {
            return;
        }

        ImGui.TextColored(Dim, T(LocKeys.TeamsResources));
        foreach (var res in resources.OrderBy(r => r.Sort))
        {
            using var id = ImRaii.PushId($"res_{res.Id}");
            DrawResourceTag(res);
            ImGui.SameLine();

            if (res.Kind == "note")
            {
                ImGui.TextUnformatted(res.Title ?? string.Empty);
                if (_config.TeamsShowNotes && !string.IsNullOrEmpty(res.Body))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Dim))
                    {
                        ImGui.TextWrapped(res.Body);
                    }
                }
            }
            else if (res.IsImage)
            {
                if (ImGui.Button($"{res.Title}##img{res.Id}"))
                {
                    _openImage(teamId, res.Id, res.Title);
                }
            }
            else if (res.Kind == "file")
            {
                if (ImGui.Button($"{res.Title ?? res.FileName}##file{res.Id}"))
                {
                    OpenApp($"/api/v1/teams/{teamId}/resources/{res.Id}/file");
                }
            }
            else if (!string.IsNullOrEmpty(res.Url))
            {
                if (ImGui.Button($"{res.Title}##link{res.Id}"))
                {
                    OpenExternal(res.Url);
                }
            }
            else
            {
                ImGui.TextUnformatted(res.Title ?? string.Empty);
            }
        }
    }

    /// <summary>Renders a resource's type as an icon and/or text label, per the display setting.</summary>
    private void DrawResourceTag(ResourceEntry res)
    {
        var (icon, labelKey) = ResourceKind(res);
        var mode = _config.TeamsResourceDisplay; // 0 = icon + text, 1 = icon only, 2 = text only
        if (mode != 2)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(icon.ToIconString());
            ImGui.PopFont();
        }

        if (mode != 1)
        {
            if (mode != 2)
            {
                ImGui.SameLine();
            }

            ImGui.TextDisabled($"[{T(labelKey)}]");
        }
    }

    private static (FontAwesomeIcon Icon, string LabelKey) ResourceKind(ResourceEntry res) => res.Kind switch
    {
        "link" => (FontAwesomeIcon.Link, LocKeys.TeamsResLink),
        "video" => (FontAwesomeIcon.Video, LocKeys.TeamsResVideo),
        "plan" => (FontAwesomeIcon.ProjectDiagram, LocKeys.TeamsResPlan),
        "note" => (FontAwesomeIcon.StickyNote, LocKeys.TeamsResNote),
        "file" when res.IsImage => (FontAwesomeIcon.Image, LocKeys.TeamsResImage),
        "file" when res.Mime == "application/pdf" => (FontAwesomeIcon.FilePdf, LocKeys.TeamsResPdf),
        _ => (FontAwesomeIcon.File, LocKeys.TeamsResFile),
    };

    // --- Farm -------------------------------------------------------------------------------------

    private void DrawFarm()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        Ensure(_farmSlot, team.Id.ToString(), ct => _teams.GetFarmAsync(team.Id, ct));
        if (_farmSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_farmSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        var entries = _farmSlot.Value?.Data ?? [];
        if (entries.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoFarm));
            return;
        }

        // Enrich the farm (routes are chain-less here) with /gear/obtain so a Tome+ piece shows the
        // base step too. One prefetch per newly-seen target id; the service caches for the session.
        PrefetchFarmObtain(entries);

        using var child = ImRaii.Child("##farm", new Vector2(0, 0), false);
        foreach (var entry in entries.OrderByDescending(e => e.IsCore))
        {
            using var id = ImRaii.PushId($"farm_{entry.SharedBy}_{entry.CharacterId}_{entry.Job}");
            var who = entry.CharacterName ?? entry.Member ?? "?";
            ImGui.TextColored(entry.IsCore ? Green : Dim, entry.IsCore ? T(LocKeys.TeamsCore) : T(LocKeys.TeamsSubstitute));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{who}  ·  {entry.Job}  ·  {entry.Name}");

            if (entry.Target is not { Count: > 0 } target)
            {
                ImGui.TextDisabled(T(LocKeys.TeamsTargetNone));
                ImGui.Separator();
                continue;
            }

            var missing = target
                .Where(kv => kv.Value.Id != 0 && (entry.Equipped is null || !entry.Equipped.TryGetValue(kv.Key, out var eq) || eq.Id != kv.Value.Id))
                .Select(kv => (Slot: kv.Key, Item: kv.Value, Sourcing: EnrichedSourcing(kv.Value), Equipped: EquippedId(entry, kv.Key)))
                .OrderBy(m => SourcingView.SourceRank(m.Sourcing.Source))
                .ThenBy(m => _sourcing.SlotName(m.Slot), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (missing.Count == 0)
            {
                ImGui.TextColored(Green, T(LocKeys.TeamsComplete));
                ImGui.Separator();
                continue;
            }

            // Only the player's own row may be measured against what the plugin can see. For everyone
            // else the requirement is shown, but never a have/need — see DrawMemberNeeds.
            var isSelf = _myCharacterId() is { } me && entry.CharacterId == me;

            DrawMemberNeeds(missing, isSelf);
            DrawFarmMissing(missing, isSelf);
            ImGui.Separator();
        }
    }

    private static long EquippedId(FarmEntry entry, string slot) =>
        entry.Equipped is not null && entry.Equipped.TryGetValue(slot, out var eq) ? eq.Id : 0;

    /// <summary>The (source, routes) a farm piece renders from — the chain-complete obtain data when it
    /// has loaded, else the farm's own chain-less routes as a fallback.</summary>
    private (string? Source, List<FarmRoute>? Routes) EnrichedSourcing(FarmSlot item) =>
        _obtain.TryGet(item.Id, out var info) && info?.Routes is { Count: > 0 }
            ? (info.Source ?? item.Source, info.Routes)
            : (item.Source, item.Routes);

    private void PrefetchFarmObtain(List<FarmEntry> entries)
    {
        var ids = new List<long>();
        foreach (var entry in entries)
        {
            foreach (var kv in entry.Target ?? [])
            {
                if (kv.Value.Id > 0 && _farmObtainRequested.Add(kv.Value.Id))
                {
                    ids.Add(kv.Value.Id);
                }
            }

            // Also the equipped pieces, so an equipped tome base can be recognised per member.
            foreach (var kv in entry.Equipped ?? [])
            {
                if (kv.Value.Id > 0 && _farmObtainRequested.Add(kv.Value.Id))
                {
                    ids.Add(kv.Value.Id);
                }
            }
        }

        if (ids.Count > 0)
        {
            _ = _obtain.PrefetchAsync(ids, CancellationToken.None);
        }
    }

    /// <summary>
    /// A one-line "still short" summary next to the member: the farmable materials/tokens summed across
    /// all their missing pieces, minus what they own — so you see at a glance what to gather for them.
    /// </summary>
    /// <summary>
    /// The one-line summary under a member: how many pieces are still missing and what they cost in
    /// materials and tokens.
    /// </summary>
    /// <param name="missing">The member's still-missing pieces.</param>
    /// <param name="isSelf">
    /// Whether this row is the player's own character. Only then is the cost measured against what is
    /// held — the plugin can see nobody else's bags, retainers or tomestones, so for a teammate it
    /// states the full requirement instead of subtracting the player's own stock from it.
    /// </param>
    private void DrawMemberNeeds(
        List<(string Slot, FarmSlot Item, (string? Source, List<FarmRoute>? Routes) Sourcing, long Equipped)> missing,
        bool isSelf)
    {
        // Sum the farmable cost items (materials + tokens, not the tier's tomestone) across every piece.
        var needed = new Dictionary<long, (string Name, int Count)>();
        foreach (var m in missing)
        {
            var primary = SourcingView.PrimaryRoute(m.Sourcing.Source, m.Sourcing.Routes);
            CollectNeeds(primary, needed, depth: 0);
        }

        if (isSelf)
        {
            _ = _holdings.PrefetchAsync(needed.Keys, CancellationToken.None);
        }

        var parts = new List<string>();
        foreach (var (itemId, entry) in needed)
        {
            var name = _world.LocalizedItemName(itemId) ?? entry.Name;
            if (!isSelf)
            {
                parts.Add($"{entry.Count}× {name}");
                continue;
            }

            var have = Math.Max(
                _holdings.TryGet(itemId, out var h) ? h : 0,
                _world.OwnedCount((uint)itemId));
            var shortBy = entry.Count - have;
            if (shortBy > 0)
            {
                parts.Add($"{shortBy}× {name}");
            }
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Yellow))
        {
            ImGui.TextUnformatted($"{T(LocKeys.TeamsMissing)}: {missing.Count}");
        }

        if (parts.Count == 0)
        {
            return;
        }

        ImGui.SameLine();

        // Word it as the set's requirement, not as their shortfall. The numbers are identical for
        // everyone; what differs is the stock, and that is exactly what cannot be seen here — saying
        // "still missing" about a teammate would be a claim the plugin has no basis for.
        var label = isSelf ? string.Empty : $"{T(LocKeys.TeamsNeedsRequires)} ";
        ImGui.TextDisabled($"·  {label}{string.Join(", ", parts)}");

        if (!isSelf)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({T(LocKeys.TeamsStockUnknown)})");
        }
    }

    private static void CollectNeeds(FarmRoute? route, Dictionary<long, (string Name, int Count)> into, int depth)
    {
        if (route is null || depth > 2)
        {
            return;
        }

        foreach (var cost in route.Cost ?? [])
        {
            if (string.Equals(cost.Role, "material", StringComparison.Ordinal) || string.Equals(cost.Role, "token", StringComparison.Ordinal))
            {
                if (cost.Id is { } id && id > 0)
                {
                    var name = cost.Name ?? $"#{id}";
                    var prev = into.TryGetValue(id, out var e) ? e.Count : 0;
                    into[id] = (name, prev + cost.Count);
                }
            }
            else if (string.Equals(cost.Role, "piece", StringComparison.Ordinal) && cost.Chain is { Count: > 0 } chain)
            {
                CollectNeeds(SourcingView.PrimaryRoute(cost.Acq, chain), into, depth + 1);
            }
        }
    }

    /// <summary>Renders the still-missing pieces as a slot/source/steps table (R8: display only).</summary>
    private void DrawFarmMissing(
        List<(string Slot, FarmSlot Item, (string? Source, List<FarmRoute>? Routes) Sourcing, long Equipped)> missing,
        bool isSelf)
    {
        if (!ImGui.BeginTable("##farmMissing", 3, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg))
        {
            return;
        }

        try
        {
            ImGui.TableSetupColumn(T(LocKeys.TeamsFarmColSlot), ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn(T(LocKeys.TeamsFarmColSource), ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn(T(LocKeys.TeamsFarmColHow), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (slot, _, sourcing, equipped) in missing)
            {
                using var rowId = ImRaii.PushId(slot);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_sourcing.SlotName(slot));

                ImGui.TableNextColumn();
                var (label, color) = _sourcing.SourceBadge(sourcing.Source);
                ImGui.TextColored(color, label);

                ImGui.TableNextColumn();
                DrawFarmHow(sourcing.Source, sourcing.Routes, equipped, isSelf);

                // Right-click the piece → pin its vendor on the map (when the route has one).
                if (_sourcing.HasMapTarget(sourcing.Routes) && ImGui.BeginPopupContextItem("##farmctx"))
                {
                    _sourcing.DrawMapMenuItem(sourcing.Routes);
                    ImGui.EndPopup();
                }
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    /// <summary>The "how to get it" cell: the steps in one line, the full checklist on hover.</summary>
    private void DrawFarmHow(string? source, List<FarmRoute>? routes, long equippedItemId, bool isSelf)
    {
        ImGui.BeginGroup();
        _sourcing.DrawCompact(source, routes, equippedItemId, isSelf);
        ImGui.EndGroup();

        if (routes is { Count: > 0 } && ImGui.IsItemHovered())
        {
            _sourcing.DrawTooltip(source, routes, equippedItemId, isSelf);
        }
    }

    // --- FFLogs -----------------------------------------------------------------------------------

    private void DrawLogs()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        Ensure(_logsSlot, team.Id.ToString(), ct => _teams.GetLogsAsync(team.Id, ct));
        if (_logsSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_logsSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        var logs = _logsSlot.Value?.Data;
        var connection = logs?.Connection;
        if (connection is not { Connected: true })
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLogsNotConnected));
            return;
        }

        if (ImGui.Button($"{connection.Label ?? "FFLogs"}##conn") && !string.IsNullOrEmpty(connection.Url))
        {
            OpenExternal(connection.Url);
        }

        var reports = logs?.Reports ?? [];
        if (reports.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoReports));
            return;
        }

        ImGui.Separator();
        using var child = ImRaii.Child("##logs", new Vector2(0, 0), false);
        foreach (var report in reports)
        {
            using var id = ImRaii.PushId($"rep_{report.Code}");
            ImGui.TextColored(Yellow, report.Title ?? report.Zone ?? report.Code ?? "?");
            ImGui.SameLine();
            ImGui.TextDisabled(UnixDate(report.StartTime));

            ImGui.TextUnformatted(_localizer.Get(LocKeys.TeamsKillsWipes, report.Kills, report.Wipes));
            ImGui.SameLine();
            if (ImGui.SmallButton($"{T(LocKeys.TeamsFflogsReport)}##r{report.Code}") && !string.IsNullOrEmpty(report.Code))
            {
                OpenExternal($"https://www.fflogs.com/reports/{report.Code}");
            }

            if (report.Bosses is { Count: > 0 })
            {
                foreach (var boss in report.Bosses)
                {
                    ImGui.TextUnformatted($"    {boss.Name}  ·  {_localizer.Get(LocKeys.TeamsKillsWipes, boss.Kills, boss.Wipes)}");
                }
            }

            ImGui.Separator();
        }
    }

    // --- Absence ----------------------------------------------------------------------------------

    private void DrawAbsence()
    {
        var team = CurrentTeam();
        if (team is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoTeams));
            return;
        }

        Ensure(_absenceSlot, team.Id.ToString(), ct => _teams.GetAbsencesAsync(team.Id, ct));

        // Add / edit form.
        ImGui.TextColored(Yellow, _editingAbsenceId != 0 ? T(LocKeys.TeamsAbsenceEditing) : T(LocKeys.TeamsAbsenceNew));
        ImGui.Spacing();
        ImGui.TextUnformatted(T(LocKeys.TeamsAbsenceFrom));
        ImGui.SameLine();
        DrawDatePicker("from", ref _absFrom);
        ImGui.SameLine();
        ImGui.TextUnformatted(T(LocKeys.TeamsAbsenceTo));
        ImGui.SameLine();
        DrawDatePicker("to", ref _absTo);

        ImGui.SetNextItemWidth(280f);
        ImGui.InputText(T(LocKeys.TeamsAbsenceNote), ref _absNote, 200);

        if (ImGui.Button(_editingAbsenceId != 0 ? T(LocKeys.TeamsAbsenceUpdate) : T(LocKeys.TeamsAbsenceAdd)))
        {
            SubmitAbsence(team.Id);
        }

        if (_editingAbsenceId != 0)
        {
            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.TeamsAbsenceCancel)))
            {
                ResetAbsenceForm();
            }
        }

        if (_absenceError is { } inputError)
        {
            ImGui.TextColored(Red, inputError);
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (_absenceSlot.Loading)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (_absenceSlot.Error is { } err)
        {
            ImGui.TextColored(Red, err);
            return;
        }

        var absences = _absenceSlot.Value?.Items ?? [];
        if (absences.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoAbsences));
            return;
        }

        ImGui.TextColored(Dim, T(LocKeys.TeamsAbsenceCurrent));
        ImGui.Spacing();

        using var child = ImRaii.Child("##absList", new Vector2(0, 0), false);
        foreach (var absence in absences.OrderBy(a => a.FromDate, StringComparer.Ordinal))
        {
            using var id = ImRaii.PushId($"abs_{absence.Id}");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{LocalDate(absence.FromDate)}  →  {LocalDate(absence.ToDate)}");
            if (!string.IsNullOrEmpty(absence.Note))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({absence.Note})");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(T(LocKeys.TeamsAbsenceEdit)))
            {
                _editingAbsenceId = absence.Id;
                _absFrom = absence.FromDate ?? _absFrom;
                _absTo = absence.ToDate ?? _absTo;
                _absNote = absence.Note ?? string.Empty;
                _absenceError = null;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(T(LocKeys.TeamsAbsenceDelete)))
            {
                DeleteAbsence(team.Id, absence.Id);
            }

            ImGui.Spacing();
        }
    }

    private void ResetAbsenceForm()
    {
        _editingAbsenceId = 0;
        _absNote = string.Empty;
        _absenceError = null;
    }

    /// <summary>A button showing the localized date; clicking opens a mini month-grid picker.</summary>
    private void DrawDatePicker(string id, ref string isoValue)
    {
        var display = LocalDate(isoValue);
        if (ImGui.Button($"{display}##dp{id}"))
        {
            ImGui.OpenPopup($"##datepop{id}");
        }

        if (!ImGui.BeginPopup($"##datepop{id}"))
        {
            return;
        }

        var current = DateOnly.TryParseExact(isoValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Now);
        var picked = DrawMiniCalendar(id, current);
        if (picked is { } p)
        {
            isoValue = p.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private DateOnly? DrawMiniCalendar(string id, DateOnly current)
    {
        // Month state is kept in the popup via a static-ish field keyed by id.
        _pickerMonth.TryGetValue(id, out var month);
        if (month == default)
        {
            month = new DateOnly(current.Year, current.Month, 1);
        }

        if (ImGui.SmallButton($"<##pm{id}"))
        {
            month = month.AddMonths(-1);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Culture()));
        ImGui.SameLine();
        if (ImGui.SmallButton($">##pm{id}"))
        {
            month = month.AddMonths(1);
        }

        _pickerMonth[id] = month;

        DateOnly? result = null;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var firstWeekday = ((int)month.DayOfWeek + 6) % 7;
        var day = 1;
        for (var week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (var slot = 0; slot < 7; slot++)
            {
                if ((week == 0 && slot < firstWeekday) || day > daysInMonth)
                {
                    ImGui.Dummy(new Vector2(26f, 24f));
                }
                else
                {
                    var d = new DateOnly(month.Year, month.Month, day);
                    if (ImGui.Button($"{day}##d{id}{day}", new Vector2(26f, 24f)))
                    {
                        result = d;
                    }

                    day++;
                }

                if (slot < 6)
                {
                    ImGui.SameLine();
                }
            }
        }

        return result;
    }

    private readonly Dictionary<string, DateOnly> _pickerMonth = new(StringComparer.Ordinal);

    private void SubmitAbsence(long teamId)
    {
        if (!DateOnly.TryParseExact(_absFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(_absTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            _absenceError = T(LocKeys.TeamsAbsenceInvalid);
            return;
        }

        if (to < from)
        {
            _absenceError = T(LocKeys.TeamsAbsenceRangeInvalid);
            return;
        }

        _absenceError = null;
        _actionMessage = T(LocKeys.TeamsWorking);
        var oldId = _editingAbsenceId;
        var req = new AbsenceCreateRequest { FromDate = _absFrom, ToDate = _absTo, Note = string.IsNullOrWhiteSpace(_absNote) ? null : _absNote };
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _teams.CreateAbsenceAsync(teamId, req, CancellationToken.None).ConfigureAwait(false);
                if (!res.IsSuccess)
                {
                    _actionMessage = Describe(res.Error);
                    return;
                }

                // "Edit" = create the new range, then remove the old one (there is no update endpoint).
                if (oldId != 0)
                {
                    await _teams.DeleteAbsenceAsync(teamId, oldId, CancellationToken.None).ConfigureAwait(false);
                }

                _actionMessage = T(LocKeys.TeamsSaved);
                ResetAbsenceForm();
                _absenceSlot.Reset();
            }
            catch (Exception ex)
            {
                _log.Error($"Absence submit failed: {ex.GetType().Name}.");
                _actionMessage = T(LocKeys.TeamsErrorGeneric);
            }
        });
    }

    private void DeleteAbsence(long teamId, long absenceId)
    {
        _actionMessage = T(LocKeys.TeamsWorking);
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _teams.DeleteAbsenceAsync(teamId, absenceId, CancellationToken.None).ConfigureAwait(false);
                _actionMessage = res.IsSuccess ? T(LocKeys.TeamsSaved) : Describe(res.Error);
                if (res.IsSuccess)
                {
                    _absenceSlot.Reset();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Absence delete failed: {ex.GetType().Name}.");
                _actionMessage = T(LocKeys.TeamsErrorGeneric);
            }
        });
    }

    // --- Helpers ----------------------------------------------------------------------------------

    private static string PlanLabel(MitPlanRef plan) =>
        !string.IsNullOrWhiteSpace(plan.Name) ? plan.Name! : !string.IsNullOrWhiteSpace(plan.Boss) ? plan.Boss! : $"#{plan.Id}";

    private static string[] PlanJobs(MitSheet sheet)
    {
        var jobs = (sheet.Plan?.Jobs ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (jobs.Length == 0 && sheet.Placements is { } pls)
        {
            jobs = pls.Where(p => p.Job is not null).Select(p => p.Job!).Distinct().ToArray();
        }

        return jobs;
    }

    private void SyncPhaseState(string planKey, MitSheet sheet)
    {
        var maxPhase = 0;
        foreach (var r in sheet.Rows ?? [])
        {
            maxPhase = Math.Max(maxPhase, r.Phase);
        }

        foreach (var p in sheet.Placements ?? [])
        {
            maxPhase = Math.Max(maxPhase, p.Phase);
        }

        var count = maxPhase + 1;
        if (planKey != _mitPlanKey || _phaseChecked.Length != count)
        {
            if (planKey != _mitPlanKey)
            {
                // A new plan: seed the filters from the configured defaults.
                _showAllJobs = _config.TeamsDefaultAllJobs;
                _tagRaidwide = true;
                _tagTankbuster = true;
                _tagOther = _config.TeamsDefaultShowOther;
            }

            _mitPlanKey = planKey;
            var allPhases = _config.TeamsDefaultAllPhases || count <= 1;
            _phaseChecked = Enumerable.Range(0, count).Select(i => allPhases || i == 0).ToArray();
        }
    }

    private static string PhaseName(List<PhaseName> phases, int index)
    {
        if (index >= 0 && index < phases.Count)
        {
            var phase = phases[index];
            if (!string.IsNullOrEmpty(phase.De) || !string.IsNullOrEmpty(phase.En))
            {
                return phase.De ?? phase.En!;
            }
        }

        return $"P{index + 1}";
    }

    private bool TagVisible(string? tag)
    {
        var t = tag?.ToLowerInvariant() ?? string.Empty;
        if (t.Contains("raid"))
        {
            return _tagRaidwide;
        }

        return t.Contains("tank") ? _tagTankbuster : _tagOther;
    }

    private string ResolveJob(long planId, string[] jobs)
    {
        if (jobs.Length == 0)
        {
            return string.Empty;
        }

        var current = CurrentJob();
        if (current is not null && jobs.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            return jobs.First(j => j.Equals(current, StringComparison.OrdinalIgnoreCase));
        }

        if (_config.TeamsPlanJob.TryGetValue(planId.ToString(), out var remembered) && jobs.Contains(remembered))
        {
            return remembered;
        }

        return jobs[0];
    }

    private uint ActionIcon(uint actionId)
    {
        if (actionId == 0)
        {
            return 0;
        }

        try
        {
            var sheet = _data.GetExcelSheet<LuminaAction>();
            return sheet?.GetRowOrDefault(actionId)?.Icon ?? 0u;
        }
        catch
        {
            return 0;
        }
    }

    private string? CurrentJob()
    {
        try
        {
            return _playerState.IsLoaded ? _playerState.ClassJob.ValueNullable?.Abbreviation.ExtractText()?.ToUpperInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private CultureInfo Culture() => German ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture;

    private string LocalDate(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToDateTime(TimeOnly.MinValue).ToString(German ? "dd.MM.yyyy" : "MM/dd/yyyy", CultureInfo.InvariantCulture)
            : iso ?? string.Empty;

    private string UnixDate(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
            return string.Empty;
        }

        var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return dt.ToString(German ? "dd.MM.yyyy" : "MM/dd/yyyy", CultureInfo.InvariantCulture);
    }

    private static string FormatTime(int seconds)
    {
        var sign = seconds < 0 ? "-" : string.Empty;
        var s = Math.Abs(seconds);
        return $"{sign}{s / 60}:{s % 60:00}";
    }

    private static Vector4? ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return null;
        }

        var h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        return new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    private void EnsureTeams()
    {
        if (!_teamsSlot.Loading && !_teamsSlot.Has && _teamsSlot.Error is null && _teamsSlot.Key is null)
        {
            Ensure(_teamsSlot, "teams", ct => _teams.GetTeamsAsync(ct));
        }
    }

    private void RefreshAll()
    {
        _teamsSlot.Reset();
        _mitSlot.Reset();
        _contentSlot.Reset();
        _farmSlot.Reset();
        _logsSlot.Reset();
        _absenceSlot.Reset();
        _teams.RequestPoll(force: true);
    }

    private void Ensure<T>(Slot<T> slot, string key, Func<CancellationToken, Task<ApiResult<T>>> call)
        where T : class
    {
        if (slot.Loading || slot.Key == key)
        {
            return;
        }

        slot.Key = key;
        slot.Loading = true;
        slot.Error = null;
        slot.Has = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await call(CancellationToken.None).ConfigureAwait(false);
                if (res.IsSuccess)
                {
                    slot.Value = res.Value;
                    slot.Has = true;
                }
                else
                {
                    _log.Warning($"Teams load '{key}' failed: {res.Error?.Kind} (HTTP {res.Error?.StatusCode}) {res.Error?.Endpoint} req={res.Error?.RequestId} — {res.Error?.Message}");
                    slot.Error = Describe(res.Error);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Teams load failed: {ex.GetType().Name}.");
                slot.Error = _localizer.Get(LocKeys.TeamsErrorGeneric);
            }
            finally
            {
                slot.Loading = false;
            }
        });
    }

    private string Describe(ApiError? error)
    {
        if (error is null)
        {
            return T(LocKeys.TeamsErrorGeneric);
        }

        return error.Kind switch
        {
            ApiErrorKind.Forbidden when error.Message?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true => T(LocKeys.TeamsScopeHint),
            ApiErrorKind.Forbidden => T(LocKeys.TeamsErrorForbidden),
            ApiErrorKind.NotFound => T(LocKeys.TeamsNotMember),
            ApiErrorKind.Unauthorized => T(LocKeys.TeamsDisabledHint),
            ApiErrorKind.Network => T(LocKeys.TeamsErrorNetwork),
            _ => error.StatusCode is { } sc ? $"{T(LocKeys.TeamsErrorGeneric)} (HTTP {sc})" : $"{T(LocKeys.TeamsErrorGeneric)} ({error.Message})",
        };
    }

    private void OpenApp(string relativePath) => OpenExternal(AppUrl() + relativePath);

    private void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Util.OpenLink(url);
        }
    }

    private string AppUrl()
    {
        if (!string.IsNullOrWhiteSpace(_config.WebAppUrl))
        {
            return _config.WebAppUrl.Trim().TrimEnd('/');
        }

        var baseUrl = _store.BaseUrl;
        var idx = baseUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? baseUrl[..idx] : baseUrl.TrimEnd('/');
    }

    /// <summary>A lazily-loaded, keyed data slot read on the UI thread and filled off-thread.</summary>
    private sealed class Slot<T>
        where T : class
    {
        public volatile bool Loading;
        public volatile string? Error;
        public volatile bool Has;
        public string? Key;
        public T? Value;

        public void Reset()
        {
            Key = null;
            Has = false;
            Error = null;
        }
    }
}
