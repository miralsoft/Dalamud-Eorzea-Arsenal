using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Gear;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The relic view (server 1.5), laid out like the website's relic page: per series a header with "x of y
/// jobs done" and a progress bar, per job its icon and name, its stages as numbered circles (filled: held,
/// ring: ticked by hand, grey ring: missing) and either "done" or the next stage by name, which opens the
/// job's row on the website. Combat and crafting/gathering are two tabs; "only open" hides finished jobs.
/// Everything shown comes from the server through <see cref="RelicService"/>; a stage is ticked only when
/// the player clicks it.
/// </summary>
public sealed class RelicWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.36f, 0.78f, 0.36f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Link = new(0.45f, 0.65f, 1f, 1f);
    private static readonly Vector4 Ring = new(0.45f, 0.45f, 0.48f, 1f);
    private static readonly Vector4 BarBack = new(0.22f, 0.22f, 0.25f, 1f);
    private static readonly Vector4 Dark = new(0.08f, 0.09f, 0.1f, 1f);

    private readonly Localizer _localizer;
    private readonly RelicService _relics;
    private readonly Func<long?> _characterId;
    private readonly Func<bool> _inventorySyncOn;
    private readonly Func<string> _webAppBase;
    private readonly Action<string> _openLink;
    private readonly Action _openConfig;
    private readonly ILog _log;
    private readonly Func<int, string> _gameItemName;
    private readonly Func<string, GameJob?> _job;
    private readonly ITextureProvider _textures;
    private readonly CancellationTokenSource _cts = new();
    private volatile string? _message;
    private volatile bool _ticking;
    private bool _onlyOpen;

    /// <summary>Creates the relic view.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="relics">The relic service.</param>
    /// <param name="characterId">The logged-in character's server id, or <see langword="null"/> when not known yet.</param>
    /// <param name="inventorySyncOn">Whether the inventory sync is switched on.</param>
    /// <param name="webAppBase">The website's base address, for the links.</param>
    /// <param name="openLink">Opens an http(s) address in the browser.</param>
    /// <param name="openConfig">Opens the settings window.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="gameItemName">The game's name of an item, for a piece the server sends without one.</param>
    /// <param name="job">The game's row for a job code (name and icon), or <see langword="null"/>.</param>
    /// <param name="textures">Loads the job icons.</param>
    public RelicWindow(
        Localizer localizer,
        RelicService relics,
        Func<long?> characterId,
        Func<bool> inventorySyncOn,
        Func<string> webAppBase,
        Action<string> openLink,
        Action openConfig,
        ILog log,
        Func<int, string> gameItemName,
        Func<string, GameJob?> job,
        ITextureProvider textures)
        : base("Eorzea Arsenal · Relics###EorzeaArsenalRelics")
    {
        _localizer = localizer;
        _relics = relics;
        _characterId = characterId;
        _inventorySyncOn = inventorySyncOn;
        _webAppBase = webAppBase;
        _openLink = openLink;
        _openConfig = openConfig;
        _log = log;
        _gameItemName = gameItemName;
        _job = job;
        _textures = textures;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 400),
            MaximumSize = new Vector2(1500, 1500),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    private string Language => German ? Localizer.German : Localizer.English;

    /// <inheritdoc />
    public override void PreDraw()
    {
        // The title follows the language; the part after ### keeps the window's identity and its place.
        WindowName = $"Eorzea Arsenal · {T(LocKeys.RelicOpen)}###EorzeaArsenalRelics";
    }

    /// <inheritdoc />
    public override void OnOpen()
    {
        // Opening the view is one of the moments the contract names for reading the progress.
        _relics.Invalidate();
        _message = null;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        var characterId = _characterId();
        if (_relics.NeedsRead(Language, characterId) && !_relics.Loading)
        {
            var language = Language;
            _ = Task.Run(() => _relics.RefreshAsync(language, characterId, _cts.Token));
        }

        Wrapped(Dim, T(LocKeys.RelicReminder));
        var mine = _relics.Mine;
        if (!_inventorySyncOn() || mine is { InventorySynced: false })
        {
            Wrapped(Yellow, T(LocKeys.RelicInventoryOff));
            if (ImGui.SmallButton(T(LocKeys.OpenSettings)))
            {
                _openConfig();
            }
        }

        if (_message is { } message)
        {
            Wrapped(Yellow, message);
        }

        var catalog = _relics.CatalogFor(Language);
        if (catalog is null)
        {
            if (_relics.LastError is { } error && !_relics.Loading)
            {
                Wrapped(Red, $"{T(LocKeys.RelicError)} ({error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? error.Kind.ToString()})");
            }
            else
            {
                ImGui.TextDisabled(T(LocKeys.RelicLoading));
            }

            return;
        }

        if (mine is null && !_relics.Loading && _relics.LastError is null)
        {
            Wrapped(Dim, T(LocKeys.RelicNoCharacter));
        }

        ImGui.Spacing();
        using var tabs = ImRaii.TabBar("##relicTabs");
        if (!tabs)
        {
            return;
        }

        DrawKind(catalog, mine, "combat", LocKeys.RelicCombat, characterId);
        DrawKind(catalog, mine, "craft", LocKeys.RelicCraft, characterId);
    }

    private void DrawKind(RelicCatalog catalog, MyRelics? mine, string kind, string labelKey, long? characterId)
    {
        using var tab = ImRaii.TabItem(T(labelKey));
        if (!tab)
        {
            return;
        }

        ImGui.Checkbox(T(LocKeys.RelicOnlyOpen), ref _onlyOpen);
        DrawLegend();
        ImGui.Spacing();

        using var child = ImRaii.Child($"##relic{kind}", Vector2.Zero, false);
        foreach (var series in catalog.Series.Where(s => string.Equals(s.Kind, kind, StringComparison.Ordinal)))
        {
            var progress = mine?.Series.FirstOrDefault(s => s.Key == series.Key);
            if (!DrawSeriesHeader(series, progress))
            {
                continue;
            }

            DrawSeries(series, progress, characterId);
            ImGui.Spacing();
        }
    }

    /// <summary>"● held  ○ ticked  ○ missing · a click ticks…", drawn with the same circles as the stages.</summary>
    private void DrawLegend()
    {
        var size = ImGui.GetTextLineHeight();
        LegendItem(RelicMark.Held, T(LocKeys.RelicLegendHeld), size);
        ImGui.SameLine();
        LegendItem(RelicMark.Ticked, T(LocKeys.RelicLegendTicked), size);
        ImGui.SameLine();
        LegendItem(RelicMark.Missing, T(LocKeys.RelicLegendMissing), size);
        Wrapped(Dim, T(LocKeys.RelicLegendHint));
    }

    private void LegendItem(RelicMark mark, string label, float size)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size, size));
        Circle(ImGui.GetWindowDrawList(), start + new Vector2(size / 2f, size / 2f), (size / 2f) - 1f, mark, null);
        ImGui.SameLine();
        ImGui.TextColored(Dim, label);
    }

    /// <summary>The series header: name, "x of y jobs done" on the right, and a progress bar below.</summary>
    /// <returns>Whether the series is unfolded.</returns>
    private bool DrawSeriesHeader(RelicSeries series, MyRelicSeries? progress)
    {
        var name = series.Name ?? series.Key;
        var open = ImGui.CollapsingHeader($"{name}###series_{series.Key}");

        if (progress is not null)
        {
            var count = _localizer.Get(progress.Of == 1 ? LocKeys.RelicSeriesCountOne : LocKeys.RelicSeriesCountMany, progress.Done, progress.Of);
            var width = ImGui.CalcTextSize(count).X;
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var y = min.Y + ((max.Y - min.Y - ImGui.GetTextLineHeight()) / 2f);
            ImGui.GetWindowDrawList().AddText(new Vector2(max.X - width - 8f, y), ImGui.GetColorU32(Dim), count);

            var fraction = progress.Of > 0 ? Math.Clamp(progress.Done / (float)progress.Of, 0f, 1f) : 0f;
            using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Green))
            using (ImRaii.PushColor(ImGuiCol.FrameBg, BarBack))
            {
                ImGui.ProgressBar(fraction, new Vector2(-1f, 4f), string.Empty);
            }
        }

        return open;
    }

    private void DrawSeries(RelicSeries series, MyRelicSeries? progress, long? characterId)
    {
        var rows = RelicService.Rows(series, progress).Where(r => !_onlyOpen || !r.Done).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        using var table = ImRaii.Table($"##tbl_{series.Key}", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
        {
            return;
        }

        var lineHeight = ImGui.GetFrameHeight();
        ImGui.TableSetupColumn("job", ImGuiTableColumnFlags.WidthFixed, 190f * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn("stages", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("next", ImGuiTableColumnFlags.WidthStretch);

        foreach (var row in rows)
        {
            using var id = ImRaii.PushId($"{series.Key}_{row.Job}");
            ImGui.TableNextRow(ImGuiTableRowFlags.None, lineHeight + 6f);

            ImGui.TableNextColumn();
            DrawJob(row.Job, lineHeight);

            ImGui.TableNextColumn();
            for (var i = 0; i < row.Stages.Count; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine(0, 4f);
                }

                DrawStage(series, row, row.Stages[i], i + 1, characterId, lineHeight);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (row.Done)
            {
                ImGui.TextColored(Green, T(LocKeys.RelicDone));
            }
            else if (row.Next is { } next)
            {
                ImGui.TextUnformatted(T(LocKeys.RelicNext));
                ImGui.SameLine();
                var names = string.Join(" + ", next.Items.Select(ItemName));
                ImGui.TextColored(Link, names);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip($"{T(LocKeys.RelicWebsite)}  ·  i{next.Ilvl}");
                }

                if (ImGui.IsItemClicked())
                {
                    var page = German ? "relikte" : "relics";
                    _openLink($"{_webAppBase()}/{page}?series={Uri.EscapeDataString(series.Key)}&job={Uri.EscapeDataString(row.Job)}");
                }
            }
        }
    }

    /// <summary>The job's icon and name from the game's own sheet; the code where the game has no row for it.</summary>
    private void DrawJob(string code, float lineHeight)
    {
        var job = _job(code);
        if (job is not null)
        {
            // 62100 + row id: the framed job icons, the ones the website shows.
            var wrap = _textures.GetFromGameIcon(new GameIconLookup(62100 + job.Id)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, new Vector2(lineHeight, lineHeight));
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(job is null ? code : JobName(job));
    }

    private string JobName(GameJob job)
    {
        var name = German ? job.German : job.English;
        return string.IsNullOrEmpty(name) ? job.Code : (German ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture).TextInfo.ToTitleCase(name);
    }

    /// <summary>One stage as a numbered circle, clickable unless held.</summary>
    private void DrawStage(RelicSeries series, RelicJobRow row, RelicStageMark stage, int number, long? characterId, float size)
    {
        var start = ImGui.GetCursorScreenPos();
        var enabled = !_ticking && stage.Mark != RelicMark.Held;
        var clicked = ImGui.InvisibleButton($"##st_{stage.Key}", new Vector2(size, size)) && enabled;
        var hovered = ImGui.IsItemHovered();
        var centre = start + new Vector2(size / 2f, size / 2f);
        Circle(ImGui.GetWindowDrawList(), centre, (size / 2f) - 1f, stage.Mark, number.ToString(CultureInfo.InvariantCulture), hovered && enabled);

        if (hovered)
        {
            using var tooltip = ImRaii.Tooltip();
            ImGui.TextUnformatted($"{stage.Key}  ·  i{stage.Ilvl}");
            foreach (var item in stage.Items)
            {
                ImGui.TextUnformatted(ItemName(item));
            }

            ImGui.TextColored(Dim, stage.Mark switch
            {
                RelicMark.Held => T(LocKeys.RelicStageHeld),
                RelicMark.Ticked => T(LocKeys.RelicStageTicked),
                _ => T(LocKeys.RelicStageMissing),
            });

            if (stage.Mark != RelicMark.Held)
            {
                ImGui.TextColored(Dim, stage.Key == row.Ticked ? T(LocKeys.RelicUntickHint) : T(LocKeys.RelicTickHint));
            }
        }

        if (!clicked)
        {
            return;
        }

        // The player's own click is the only thing that sends a tick. Clicking the ticked stage takes the
        // tick back; any other stage not held is ticked up to there.
        var target = stage.Key == row.Ticked ? null : stage.Key;
        Tick(characterId, series.Key, row.Job, target);
    }

    /// <summary>Filled green: held. Green ring: ticked. Grey ring: missing. The number sits in the middle.</summary>
    private static void Circle(ImDrawListPtr draw, Vector2 centre, float radius, RelicMark mark, string? number, bool hovered = false)
    {
        switch (mark)
        {
            case RelicMark.Held:
                draw.AddCircleFilled(centre, radius, ImGui.GetColorU32(Green));
                break;
            case RelicMark.Ticked:
                draw.AddCircle(centre, radius - 1f, ImGui.GetColorU32(Green), 0, 2f);
                break;
            default:
                draw.AddCircle(centre, radius - 1f, ImGui.GetColorU32(hovered ? Dim : Ring), 0, 1.5f);
                break;
        }

        if (number is null)
        {
            return;
        }

        var text = ImGui.CalcTextSize(number);
        var colour = mark == RelicMark.Held ? Dark : mark == RelicMark.Ticked ? Green : Dim;
        draw.AddText(centre - (text / 2f), ImGui.GetColorU32(colour), number);
    }

    private void Tick(long? characterId, string series, string job, string? stage)
    {
        _ticking = true;
        _message = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var error = await _relics.TickAsync(characterId, series, job, stage, _cts.Token).ConfigureAwait(false);
                if (error is not null)
                {
                    _message = error.Kind == ApiErrorKind.Forbidden
                        ? T(LocKeys.RelicScopeHint)
                        : $"{T(LocKeys.RelicError)} ({error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? error.Kind.ToString()})";
                }
            }
            catch (OperationCanceledException)
            {
                // closing down
            }
            catch (Exception ex)
            {
                _log.Error($"Relic tick failed: {ex.GetType().Name}.");
            }
            finally
            {
                _ticking = false;
            }
        });
    }

    /// <summary>
    /// The server's name, or the game's where the server has none in this language (it then sends
    /// <c>#&lt;id&gt;</c>). The game's name is in the client's language, which beats a bare number.
    /// </summary>
    private string ItemName(RelicItem item) =>
        !string.IsNullOrEmpty(item.Name) && !item.Name.StartsWith('#') ? item.Name : _gameItemName((int)item.Id);

    private static void Wrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
