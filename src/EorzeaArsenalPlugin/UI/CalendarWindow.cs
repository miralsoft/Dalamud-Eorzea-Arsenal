using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A cross-team month calendar: shows every team's events (from the shared <see cref="TeamsService"/>
/// poll) in a month grid over the current + next two months, colour-coded per team. Clicking a day
/// lists its events with RSVP and an "open in web" link. Purely renders the server-expanded occurrences
/// (R8/R9 — no recurrence/timezone maths; dates are grouped by the verbatim date string).
/// </summary>
public sealed class CalendarWindow : Window
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);

    // Per-team text colour so events are attributable at a glance (the dot still shows attendance).
    private static readonly Vector4[] Palette =
    [
        new(0.45f, 0.72f, 0.98f, 1f), new(0.55f, 0.85f, 0.55f, 1f), new(0.96f, 0.70f, 0.42f, 1f),
        new(0.82f, 0.60f, 0.96f, 1f), new(0.96f, 0.58f, 0.66f, 1f), new(0.50f, 0.88f, 0.85f, 1f),
        new(0.90f, 0.85f, 0.48f, 1f), new(0.70f, 0.78f, 0.95f, 1f),
    ];

    private static Vector4 TeamColor(long teamId) => Palette[(int)((ulong)teamId % (ulong)Palette.Length)];

    private readonly TeamsService _teams;
    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly ILog _log;
    private readonly Action _openConfig;

    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateOnly? _selected;
    private volatile string? _actionMessage;

    /// <summary>Creates the calendar window.</summary>
    /// <param name="teams">Teams service (calendar cache + RSVP write).</param>
    /// <param name="config">Live config.</param>
    /// <param name="store">Base-URL store (for web deep links).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="openConfig">Opens the settings (disabled hint).</param>
    public CalendarWindow(TeamsService teams, PluginConfig config, ConfigStore store, Localizer localizer, ILog log, Action openConfig)
        : base("Eorzea Arsenal — Calendar###EorzeaArsenalCalendar")
    {
        _teams = teams;
        _config = config;
        _store = store;
        _localizer = localizer;
        _log = log;
        _openConfig = openConfig;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 520),
            MaximumSize = new Vector2(1600, 1400),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private CultureInfo Culture => _localizer.Language == Localizer.German ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture;

    /// <summary>Opens the window and requests a fresh poll.</summary>
    public void Open()
    {
        IsOpen = true;
        _viewMonth = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
        if (_config is { Enabled: true, TosAccepted: true, SyncTeams: true } && _store.HasKey)
        {
            _teams.RequestPoll();
        }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (!_store.HasKey || !_config.Enabled || !_config.SyncTeams)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Dim))
            {
                ImGui.TextWrapped(T(LocKeys.TeamsDisabledHint));
            }

            if (ImGui.Button(T(LocKeys.OpenSettings)))
            {
                _openConfig();
            }

            return;
        }

        DrawMonthHeader();
        DrawGrid();
        DrawLegends();
        ImGui.Separator();
        DrawSelectedDay();
    }

    private void DrawMonthHeader()
    {
        var min = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
        var max = min.AddMonths(2);

        using (ImRaii.Disabled(_viewMonth <= min))
        {
            if (ImGui.Button("<##prevMonth"))
            {
                _viewMonth = _viewMonth.AddMonths(-1);
            }
        }

        ImGui.SameLine();
        var label = _viewMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Culture);
        ImGui.SetNextItemWidth(200f);
        ImGui.TextUnformatted(label);
        ImGui.SameLine();

        using (ImRaii.Disabled(_viewMonth >= max))
        {
            if (ImGui.Button(">##nextMonth"))
            {
                _viewMonth = _viewMonth.AddMonths(1);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.TeamsRefresh)))
        {
            _teams.RequestPoll(force: true);
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.TeamsCalendarOpenWeb)))
        {
            OpenWeb("/termine");
        }

        if (_actionMessage is { } msg)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, msg);
        }
    }

    private void DrawGrid()
    {
        var byDate = _teams.Calendar
            .Where(o => o.Date is not null)
            .GroupBy(o => o.Date!)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Time).ToList(), StringComparer.Ordinal);

        var daysInMonth = DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month);
        // Monday-based leading offset for day 1.
        var firstWeekday = ((int)_viewMonth.DayOfWeek + 6) % 7;
        var today = DateOnly.FromDateTime(DateTime.Now);

        using var noSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2f, 2f));
        var colW = MathF.Max(60f, ImGui.GetContentRegionAvail().X / 7f);
        var cellH = 74f;

        var weekdays = Culture.DateTimeFormat.AbbreviatedDayNames;
        // Reorder to Monday-first.
        for (var i = 0; i < 7; i++)
        {
            var name = weekdays[(i + 1) % 7];
            ImGui.TextDisabled(name.Length > 3 ? name[..3] : name);
            if (i < 6)
            {
                ImGui.SameLine(0, colW - ImGui.CalcTextSize(name.Length > 3 ? name[..3] : name).X);
            }
        }

        var day = 1;
        var draw = ImGui.GetWindowDrawList();
        for (var week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (var slot = 0; slot < 7; slot++)
            {
                var origin = ImGui.GetCursorScreenPos();
                if ((week == 0 && slot < firstWeekday) || day > daysInMonth)
                {
                    ImGui.Dummy(new Vector2(colW, cellH));
                }
                else
                {
                    var date = new DateOnly(_viewMonth.Year, _viewMonth.Month, day);
                    var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    byDate.TryGetValue(key, out var events);
                    DrawDayCell(draw, origin, new Vector2(colW, cellH), date, today, events);
                    day++;
                }

                if (slot < 6)
                {
                    ImGui.SameLine();
                }
            }
        }
    }

    private void DrawDayCell(ImDrawListPtr draw, Vector2 origin, Vector2 size, DateOnly date, DateOnly today, List<CalendarOccurrence>? events)
    {
        if (ImGui.InvisibleButton($"##cell{date}", size))
        {
            _selected = date;
        }

        var hovered = ImGui.IsItemHovered();
        var isSelected = _selected == date;
        var past = date < today;

        var bg = ImGui.GetColorU32(hovered ? ImGuiCol.HeaderHovered : ImGuiCol.FrameBg);
        draw.AddRectFilled(origin, origin + size, bg, 3f);
        if (isSelected)
        {
            // Strong selection: a filled tint + a thick accent border.
            draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.30f, 0.55f, 0.95f, 0.25f)), 3f);
            draw.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(0.40f, 0.65f, 1f, 1f)), 3f, ImDrawFlags.None, 3f);
        }
        else if (date == today)
        {
            draw.AddRect(origin, origin + size, ImGui.GetColorU32(Yellow), 3f, ImDrawFlags.None, 2f);
        }

        var numColor = date == today ? ImGui.GetColorU32(Yellow) : past ? ImGui.GetColorU32(Dim) : ImGui.GetColorU32(ImGuiCol.Text);
        draw.AddText(origin + new Vector2(6f, 3f), numColor, date.Day.ToString(CultureInfo.InvariantCulture));

        if (events is null)
        {
            return;
        }

        var y = 22f;
        var shown = 0;
        foreach (var occ in events)
        {
            if (shown >= 3)
            {
                draw.AddText(origin + new Vector2(6f, y), ImGui.GetColorU32(Dim), $"+{events.Count - shown}");
                break;
            }

            var (dotColor, _) = EventStatus(occ);
            var alpha = past ? 0.45f : 1f;
            var dot = dotColor with { W = alpha };
            var textCol = TeamColor(occ.TeamId) with { W = alpha };

            draw.AddCircleFilled(origin + new Vector2(11f, y + 6f), 3.5f, ImGui.GetColorU32(dot));
            var text = Truncate($"{occ.Time} {occ.Title}", size.X - 24f);
            var textPos = origin + new Vector2(20f, y);
            draw.AddText(textPos, ImGui.GetColorU32(textCol), text);
            if (string.Equals(occ.OwnStatus, "no", StringComparison.OrdinalIgnoreCase))
            {
                var w = ImGui.CalcTextSize(text).X;
                draw.AddLine(textPos + new Vector2(0f, 7f), textPos + new Vector2(w, 7f), ImGui.GetColorU32(textCol));
            }

            y += 15f;
            shown++;
        }
    }

    /// <summary>Aggregate attendance status: green (all present), yellow (unclear), red (someone missing).</summary>
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

    private void DrawSelectedDay()
    {
        if (_selected is not { } date)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsCalendarPickDay));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ImGui.TextColored(Yellow, date.ToDateTime(TimeOnly.MinValue).ToString("D", Culture));

        var events = _teams.Calendar.Where(o => o.Date == key).OrderBy(o => o.Time).ToList();
        if (events.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsNoEvents));
            return;
        }

        var past = date < today;
        using var child = ImRaii.Child("##dayEvents", new Vector2(0, 0), false);
        foreach (var teamGroup in events.GroupBy(o => o.TeamId).OrderBy(g => g.First().TeamName, StringComparer.OrdinalIgnoreCase))
        {
            var teamName = teamGroup.First().TeamName ?? $"#{teamGroup.Key}";
            ImGui.TextColored(TeamColor(teamGroup.Key), teamName);

            foreach (var occ in teamGroup.OrderBy(o => o.Time))
            {
                using var id = ImRaii.PushId($"ev_{occ.EventId}_{occ.Date}");
                var (status, _) = EventStatus(occ);
                ImGui.ColorButton($"##col{occ.EventId}", status, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoInputs, new Vector2(12f, 12f));
                ImGui.SameLine();

                var time = $"{occ.Time}" + (string.IsNullOrEmpty(occ.EndTime) ? string.Empty : $"–{occ.EndTime}");
                var signedOff = string.Equals(occ.OwnStatus, "no", StringComparison.OrdinalIgnoreCase);
                using (ImRaii.PushColor(ImGuiCol.Text, Dim, past || signedOff))
                {
                    ImGui.TextUnformatted($"{time}  {occ.Title}");
                    if (signedOff)
                    {
                        var min = ImGui.GetItemRectMin();
                        var max = ImGui.GetItemRectMax();
                        var midY = (min.Y + max.Y) / 2f;
                        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, midY), new Vector2(max.X, midY), ImGui.GetColorU32(ImGuiCol.Text));
                    }
                }

                ImGui.SameLine();
                var kind = string.Equals(occ.Kind, "recurring", StringComparison.OrdinalIgnoreCase) ? LocKeys.TeamsRecurring : LocKeys.TeamsSingle;
                ImGui.TextDisabled($"({T(kind)})");

                ImGui.TextUnformatted(_localizer.Get(LocKeys.TeamsAttendCounts, occ.Yes, occ.Maybe, occ.No, occ.Total));

                using (ImRaii.Disabled(past))
                {
                    Rsvp(occ, "yes", LocKeys.TeamsRsvpYes, Green);
                    ImGui.SameLine();
                    Rsvp(occ, "maybe", LocKeys.TeamsRsvpMaybe, Yellow);
                    ImGui.SameLine();
                    Rsvp(occ, "no", LocKeys.TeamsRsvpNo, Red);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton(T(LocKeys.TeamsCalendarOpenWeb)))
                {
                    OpenWeb($"/teams/{occ.TeamId}/termine");
                }
            }

            ImGui.Separator();
        }
    }

    private void DrawLegends()
    {
        ImGui.Spacing();
        LegendDot(Green, T(LocKeys.TeamsCalAllPresent));
        ImGui.SameLine();
        LegendDot(Yellow, T(LocKeys.TeamsCalUnclear));
        ImGui.SameLine();
        LegendDot(Red, T(LocKeys.TeamsCalMissing));

        var teams = _teams.Calendar
            .GroupBy(o => o.TeamId)
            .Select(g => (Id: g.Key, Name: g.First().TeamName ?? $"#{g.Key}"))
            .ToList();
        var first = true;
        foreach (var (teamId, name) in teams)
        {
            if (first)
            {
                first = false; // start the team colours on a new line
            }
            else
            {
                ImGui.SameLine();
            }

            LegendDot(TeamColor(teamId), name);
        }
    }

    private static void LegendDot(Vector4 color, string label)
    {
        ImGui.ColorButton($"##leg{label}", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoInputs, new Vector2(12f, 12f));
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
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
                _actionMessage = res.IsSuccess ? T(LocKeys.TeamsSaved) : T(LocKeys.TeamsErrorGeneric);
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

    private static string Truncate(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
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

    private void OpenWeb(string relativePath)
    {
        var url = AppUrl() + relativePath;
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
}
