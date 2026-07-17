using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The Teams companion window: a personal calendar (with in-game RSVP), mit cheat sheets, the content
/// hub (inline images), the who-needs-what farm, FFLogs, and own-absence management. Purely renders
/// server-authoritative data via <see cref="TeamsService"/> (R8/R9) and never computes recurrence,
/// timezones, membership or BiS. All strings via the localizer (R6); links open in the browser.
/// </summary>
public sealed class TeamsWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly TeamsService _teams;
    private readonly ITextureProvider _textures;
    private readonly IDataManager _data;
    private readonly IPlayerState _playerState;
    private readonly ILog _log;
    private readonly Action _save;
    private readonly Action _openConfig;

    private readonly Slot<TeamsResponse> _teamsSlot = new();
    private readonly Slot<MitSheetResponse> _mitSlot = new();
    private readonly Slot<ContentSheetResponse> _contentSlot = new();
    private readonly Slot<FarmResponse> _farmSlot = new();
    private readonly Slot<LogsResponse> _logsSlot = new();
    private readonly Slot<AbsencesResponse> _absenceSlot = new();

    private int _teamIndex;
    private int _planIndex;
    private string? _selectedJob;
    private string _absFrom = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absTo = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absNote = string.Empty;
    private volatile string? _actionMessage;

    private readonly Lock _imageLock = new();
    private readonly Dictionary<long, IDalamudTextureWrap?> _images = new();
    private readonly HashSet<long> _imageLoading = [];

    /// <summary>Creates the Teams window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="teams">The teams service (reads + writes; holds the key).</param>
    /// <param name="textures">Texture provider (content-hub images, skill icons).</param>
    /// <param name="data">Excel data (resolves skill icons from action ids).</param>
    /// <param name="playerState">Local player (current job for the mit-sheet preselection).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="save">Persists config (remembered team/job).</param>
    /// <param name="openConfig">Opens the settings window (reconnect hint).</param>
    public TeamsWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        TeamsService teams,
        ITextureProvider textures,
        IDataManager data,
        IPlayerState playerState,
        ILog log,
        Action save,
        Action openConfig)
        : base("Eorzea Arsenal — Teams###EorzeaArsenalTeams")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _teams = teams;
        _textures = textures;
        _data = data;
        _playerState = playerState;
        _log = log;
        _save = save;
        _openConfig = openConfig;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(1400, 1200),
        };
    }

    private string T(string key) => _localizer.Get(key);

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

        Tab(LocKeys.TeamsTabCalendar, DrawCalendar);
        Tab(LocKeys.TeamsTabMit, DrawMit);
        Tab(LocKeys.TeamsTabContent, DrawContent);
        Tab(LocKeys.TeamsTabFarm, DrawFarm);
        Tab(LocKeys.TeamsTabLogs, DrawLogs);
        Tab(LocKeys.TeamsTabAbsence, DrawAbsence);
    }

    private void Tab(string key, Action body)
    {
        using var tab = ImRaii.TabItem(T(key));
        if (!tab)
        {
            return;
        }

        ImGui.Spacing();
        body();
        ImGui.Spacing();
    }

    // --- Header: team picker + refresh + help -----------------------------------------------------

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

            // Restore the last-used team once the list is available.
            if (_config.TeamsLastTeamId != 0)
            {
                var idx = teams.FindIndex(t => t.Id == _config.TeamsLastTeamId);
                if (idx >= 0)
                {
                    _teamIndex = idx;
                    _config.TeamsLastTeamId = 0; // one-shot restore
                }
            }

            var names = teams.Select(t => t.Name ?? $"#{t.Id}").ToArray();
            ImGui.SetNextItemWidth(260f);
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

    // --- Calendar (cross-team) + RSVP -------------------------------------------------------------

    private void DrawCalendar()
    {
        var occurrences = _teams.Calendar;
        if (occurrences.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoEvents));
            return;
        }

        using var child = ImRaii.Child("##cal", new Vector2(0, 0), false);
        foreach (var occ in occurrences)
        {
            using var id = ImRaii.PushId($"occ_{occ.EventId}_{occ.Date}");

            var header = $"{occ.Date}  {occ.Time}" + (string.IsNullOrEmpty(occ.EndTime) ? string.Empty : $"–{occ.EndTime}");
            ImGui.TextColored(Yellow, header);
            if (!string.IsNullOrEmpty(occ.Timezone))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({occ.Timezone})");
            }

            ImGui.TextUnformatted($"{occ.TeamName}  ·  {occ.Title}");

            var contents = occ.Contents is { Count: > 0 }
                ? string.Join(", ", occ.Contents.Select(c => c.Name))
                : occ.ContentName;
            if (!string.IsNullOrEmpty(contents))
            {
                ImGui.TextDisabled($"{T(LocKeys.TeamsContentsLabel)}: {contents}");
            }

            ImGui.TextUnformatted(_localizer.Get(LocKeys.TeamsAttendCounts, occ.Yes, occ.Maybe, occ.No, occ.Total));

            RsvpButton(occ, "yes", LocKeys.TeamsRsvpYes, Green);
            ImGui.SameLine();
            RsvpButton(occ, "maybe", LocKeys.TeamsRsvpMaybe, Yellow);
            ImGui.SameLine();
            RsvpButton(occ, "no", LocKeys.TeamsRsvpNo, Red);

            ImGui.Separator();
        }
    }

    private void RsvpButton(CalendarOccurrence occ, string status, string labelKey, Vector4 activeColor)
    {
        var isOwn = string.Equals(occ.OwnStatus, status, StringComparison.OrdinalIgnoreCase);
        using var color = ImRaii.PushColor(ImGuiCol.Text, activeColor, isOwn);
        var label = (isOwn ? "● " : string.Empty) + T(labelKey);
        if (ImGui.Button($"{label}##rsvp_{status}") && occ.Date is { } date)
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
                    _teams.RequestPoll(force: true); // re-poll for authoritative counters (no local math)
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RSVP failed: {ex.GetType().Name}.");
                _actionMessage = T(LocKeys.TeamsErrorGeneric);
            }
        });
    }

    // --- Mit cheat sheet --------------------------------------------------------------------------

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

        var planNames = plans.Select(p => p.Name ?? $"#{p.Id}").ToArray();
        ImGui.SetNextItemWidth(220f);
        var planRef = _planIndex;
        if (ImGui.Combo(T(LocKeys.TeamsPlanLabel), ref planRef, planNames, planNames.Length))
        {
            _planIndex = planRef;
            _selectedJob = null;
        }

        var plan = plans[_planIndex];
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

        var jobs = (sheet.Plan.Jobs ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (jobs.Length == 0 && sheet.Placements is { } pls)
        {
            jobs = pls.Where(p => p.Job is not null).Select(p => p.Job!).Distinct().ToArray();
        }

        // Preselect the current job only when the plan actually contains it; otherwise keep the last
        // remembered choice (or the first job). Always freely switchable.
        _selectedJob ??= ResolveJob(plan.Id, jobs);

        var showAll = _config.TeamsShowAllJobs;
        if (ImGui.Checkbox(T(LocKeys.TeamsAllJobs), ref showAll))
        {
            _config.TeamsShowAllJobs = showAll;
            _save();
        }

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

        ImGui.Separator();
        DrawMitTimeline(sheet, showAll ? null : _selectedJob);
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

    private void DrawMitTimeline(MitSheet sheet, string? jobFilter)
    {
        var cooldowns = (sheet.Cooldowns ?? []).ToDictionary(c => c.Id, c => c);
        var placements = (sheet.Placements ?? [])
            .Where(p => jobFilter is null || string.Equals(p.Job, jobFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.TimeS)
            .ToList();
        var rows = (sheet.Rows ?? []).OrderBy(r => r.TimeS).ToList();
        var phases = sheet.Plan?.Phases ?? [];

        using var child = ImRaii.Child("##mit", new Vector2(0, 0), false);

        // Timeline mechanics (rows).
        if (rows.Count > 0)
        {
            ImGui.TextColored(Dim, T(LocKeys.TeamsMechanics));
            foreach (var row in rows)
            {
                ImGui.TextUnformatted($"{FormatTime(row.TimeS)}  {row.Label}");
                if (!string.IsNullOrEmpty(row.Tag))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"[{row.Tag}]");
                }

                var phaseName = PhaseLabel(phases, row.Phase);
                if (phaseName is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(phaseName);
                }
            }

            ImGui.Spacing();
        }

        ImGui.TextColored(Dim, T(LocKeys.TeamsCooldowns));
        if (placements.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoPlacements));
            return;
        }

        foreach (var pl in placements)
        {
            ImGui.TextUnformatted(FormatTime(pl.TimeS));
            ImGui.SameLine();
            if (cooldowns.TryGetValue(pl.CatalogId, out var cd))
            {
                DrawActionIcon(cd.ActionId, 22f);
                ImGui.SameLine();
                var name = _localizer.Language == Localizer.German && !string.IsNullOrEmpty(cd.NameDe) ? cd.NameDe : cd.Name;
                ImGui.TextUnformatted($"{pl.Job}: {name}");
            }
            else
            {
                ImGui.TextUnformatted($"{pl.Job}: #{pl.CatalogId}");
            }
        }
    }

    private static string? PhaseLabel(List<PhaseName> phases, int index)
    {
        if (index < 0 || index >= phases.Count)
        {
            return index > 0 ? $"P{index + 1}" : null;
        }

        var phase = phases[index];
        return string.IsNullOrEmpty(phase.En) && string.IsNullOrEmpty(phase.De) ? null : (phase.De ?? phase.En);
    }

    private void DrawActionIcon(uint actionId, float size)
    {
        var iconId = ActionIcon(actionId);
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
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
        foreach (var res in resources)
        {
            using var id = ImRaii.PushId($"res_{res.Id}");
            if (res.IsImage)
            {
                DrawInlineImage(teamId, res);
            }
            else if (res.Kind == "note")
            {
                ImGui.TextUnformatted($"• {res.Title}");
                if (!string.IsNullOrEmpty(res.Text))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Dim))
                    {
                        ImGui.TextWrapped(res.Text);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(res.Url))
            {
                if (ImGui.Button($"{ResourceIcon(res.Kind)} {res.Title}"))
                {
                    OpenExternal(res.Url);
                }
            }
            else if (res.Kind == "file")
            {
                // A non-image file (e.g. PDF): open it in the browser.
                if (ImGui.Button($" {res.Title}"))
                {
                    OpenApp($"/api/v1/teams/{teamId}/resources/{res.Id}/file");
                }
            }
        }
    }

    private static string ResourceIcon(string? kind) => kind switch
    {
        "video" => "",
        "plan" => "",
        _ => "",
    };

    private void DrawInlineImage(long teamId, ResourceEntry res)
    {
        ImGui.TextUnformatted($" {res.Title}");
        var wrap = GetImage(teamId, res.Id);
        if (wrap is null)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        var avail = ImGui.GetContentRegionAvail().X;
        var max = Math.Min(avail, 460f);
        var scale = wrap.Width > 0 ? Math.Min(1f, max / wrap.Width) : 1f;
        ImGui.Image(wrap.Handle, new Vector2(wrap.Width * scale, wrap.Height * scale));
    }

    // --- Farm (who needs what) --------------------------------------------------------------------

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
                .Select(kv => kv.Key)
                .ToList();

            if (missing.Count == 0)
            {
                ImGui.TextColored(Green, T(LocKeys.TeamsComplete));
            }
            else
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Yellow))
                {
                    ImGui.TextWrapped($"{T(LocKeys.TeamsMissing)}: {string.Join(", ", missing)}");
                }
            }

            ImGui.Separator();
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

        if (ImGui.Button($" {connection.Label ?? "FFLogs"}") && !string.IsNullOrEmpty(connection.Url))
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
            ImGui.TextDisabled(_localizer.Get(LocKeys.TeamsKillsWipes, report.Kills, report.Wipes));
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

        // Add form.
        ImGui.SetNextItemWidth(120f);
        ImGui.InputText(T(LocKeys.TeamsAbsenceFrom), ref _absFrom, 10);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.InputText(T(LocKeys.TeamsAbsenceTo), ref _absTo, 10);
        ImGui.SetNextItemWidth(260f);
        ImGui.InputText(T(LocKeys.TeamsAbsenceNote), ref _absNote, 200);
        if (ImGui.Button(T(LocKeys.TeamsAbsenceAdd)))
        {
            AddAbsence(team.Id);
        }

        ImGui.Separator();

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

        foreach (var absence in absences)
        {
            using var id = ImRaii.PushId($"abs_{absence.Id}");
            ImGui.TextUnformatted($"{absence.FromDate}  →  {absence.ToDate}");
            if (!string.IsNullOrEmpty(absence.Note))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({absence.Note})");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(T(LocKeys.TeamsAbsenceDelete)))
            {
                DeleteAbsence(team.Id, absence.Id);
            }
        }
    }

    private void AddAbsence(long teamId)
    {
        if (!IsIsoDate(_absFrom) || !IsIsoDate(_absTo))
        {
            _actionMessage = T(LocKeys.TeamsAbsenceInvalid);
            return;
        }

        _actionMessage = T(LocKeys.TeamsWorking);
        var req = new AbsenceCreateRequest { FromDate = _absFrom, ToDate = _absTo, Note = string.IsNullOrWhiteSpace(_absNote) ? null : _absNote };
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _teams.CreateAbsenceAsync(teamId, req, CancellationToken.None).ConfigureAwait(false);
                _actionMessage = res.IsSuccess ? T(LocKeys.TeamsSaved) : Describe(res.Error);
                if (res.IsSuccess)
                {
                    _absNote = string.Empty;
                    _absenceSlot.Reset();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Absence create failed: {ex.GetType().Name}.");
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

    private static bool IsIsoDate(string value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    // --- Loading / helpers ------------------------------------------------------------------------

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
        lock (_imageLock)
        {
            foreach (var wrap in _images.Values)
            {
                wrap?.Dispose();
            }

            _images.Clear();
            _imageLoading.Clear();
        }

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

    private IDalamudTextureWrap? GetImage(long teamId, long resourceId)
    {
        lock (_imageLock)
        {
            if (_images.TryGetValue(resourceId, out var wrap))
            {
                return wrap;
            }

            if (!_imageLoading.Add(resourceId))
            {
                return null;
            }
        }

        _ = Task.Run(async () =>
        {
            IDalamudTextureWrap? result = null;
            try
            {
                var res = await _teams.GetResourceFileAsync(teamId, resourceId, CancellationToken.None).ConfigureAwait(false);
                if (res.IsSuccess && res.Value is { Bytes.Length: > 0 } file)
                {
                    result = await _textures.CreateFromImageAsync(file.Bytes).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Resource image load failed: {ex.GetType().Name}.");
            }
            finally
            {
                lock (_imageLock)
                {
                    _images[resourceId] = result;
                    _imageLoading.Remove(resourceId);
                }
            }
        });

        return null;
    }

    private string? CurrentJob()
    {
        try
        {
            if (!_playerState.IsLoaded)
            {
                return null;
            }

            return _playerState.ClassJob.ValueNullable?.Abbreviation.ExtractText()?.ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTime(int seconds)
    {
        var sign = seconds < 0 ? "-" : string.Empty;
        var s = Math.Abs(seconds);
        return $"{sign}{s / 60}:{s % 60:00}";
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
            // Surface the concrete detail (HTTP status or "could not parse") so the cause is visible.
            _ => error.StatusCode is { } sc
                ? $"{T(LocKeys.TeamsErrorGeneric)} (HTTP {sc})"
                : $"{T(LocKeys.TeamsErrorGeneric)} ({error.Message})",
        };
    }

    /// <summary>Opens an app-relative path in the browser (prefixes the web-app URL).</summary>
    private void OpenApp(string relativePath) => OpenExternal(AppUrl() + relativePath);

    /// <summary>Opens an absolute http(s) URL in the browser; ignores anything else (P8).</summary>
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

    /// <summary>Disposes cached image textures on unload (P3).</summary>
    public void Dispose()
    {
        lock (_imageLock)
        {
            foreach (var wrap in _images.Values)
            {
                wrap?.Dispose();
            }

            _images.Clear();
        }
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
