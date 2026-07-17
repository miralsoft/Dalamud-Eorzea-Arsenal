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
    private bool _tagOther = true;

    private string _absFrom = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absTo = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private string _absNote = string.Empty;
    private volatile string? _actionMessage;

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
    /// <param name="openImage">Opens a content-hub image in the image window (teamId, resourceId, title).</param>
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
        Action openConfig,
        Action<long, long, string?> openImage)
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
        _openImage = openImage;

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
            OpenApp($"/teams/{team.Id}");
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

        // Controls: job, phase checkboxes, tag filter.
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
                .Select(kv => SlotName(kv.Key))
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

        // Add form with date pickers.
        ImGui.TextUnformatted(T(LocKeys.TeamsAbsenceFrom));
        ImGui.SameLine();
        DrawDatePicker("from", ref _absFrom);
        ImGui.SameLine();
        ImGui.TextUnformatted(T(LocKeys.TeamsAbsenceTo));
        ImGui.SameLine();
        DrawDatePicker("to", ref _absTo);

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
            ImGui.TextUnformatted($"{LocalDate(absence.FromDate)}  →  {LocalDate(absence.ToDate)}");
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

    private void AddAbsence(long teamId)
    {
        if (!DateOnly.TryParseExact(_absFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(_absTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) ||
            to < from)
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

    // --- Helpers ----------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> SlotDe = new(StringComparer.Ordinal)
    {
        ["Weapon"] = "Waffe", ["OffHand"] = "Nebenhand", ["Head"] = "Kopf", ["Body"] = "Rumpf",
        ["Hands"] = "Hände", ["Legs"] = "Beine", ["Feet"] = "Füße", ["Ears"] = "Ohrringe",
        ["Neck"] = "Halskette", ["Wrists"] = "Armreif", ["RingLeft"] = "Ring links", ["RingRight"] = "Ring rechts",
    };

    private string SlotName(string key) => German && SlotDe.TryGetValue(key, out var de) ? de : key;

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
            _mitPlanKey = planKey;
            _phaseChecked = Enumerable.Repeat(true, count).ToArray();
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
