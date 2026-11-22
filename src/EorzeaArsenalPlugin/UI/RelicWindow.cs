using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The relic view (server 1.5): per series how many jobs are done, per job its stages as marks (held,
/// ticked, missing) and the next stage by name. Combat and crafting/gathering are two tabs, as on the
/// website. Everything shown comes from the server through <see cref="RelicService"/>; a stage is ticked
/// only when the player clicks it.
/// </summary>
public sealed class RelicWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 HeldFill = new(0.25f, 0.55f, 0.25f, 1f);
    private static readonly Vector4 TickedFill = new(0.25f, 0.45f, 0.7f, 1f);
    private static readonly Vector4 MissingFill = new(0.22f, 0.22f, 0.22f, 1f);

    private readonly Localizer _localizer;
    private readonly RelicService _relics;
    private readonly Func<long?> _characterId;
    private readonly Func<bool> _inventorySyncOn;
    private readonly Func<string> _webAppBase;
    private readonly Action<string> _openLink;
    private readonly Action _openConfig;
    private readonly ILog _log;
    private readonly Func<int, string> _gameItemName;
    private readonly CancellationTokenSource _cts = new();
    private volatile string? _message;
    private volatile bool _ticking;

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
    public RelicWindow(
        Localizer localizer,
        RelicService relics,
        Func<long?> characterId,
        Func<bool> inventorySyncOn,
        Func<string> webAppBase,
        Action<string> openLink,
        Action openConfig,
        ILog log,
        Func<int, string> gameItemName)
        : base("Eorzea Arsenal · Relics###EorzeaArsenalRelics")
    {
        _gameItemName = gameItemName;
        _localizer = localizer;
        _relics = relics;
        _characterId = characterId;
        _inventorySyncOn = inventorySyncOn;
        _webAppBase = webAppBase;
        _openLink = openLink;
        _openConfig = openConfig;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(1400, 1400),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    private string Language => German ? Localizer.German : Localizer.English;

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

        ImGui.Separator();

        var catalog = _relics.CatalogFor(Language);
        if (catalog is null)
        {
            if (_relics.LastError is { } error && !_relics.Loading)
            {
                Wrapped(Red, $"{T(LocKeys.RelicError)} ({error.StatusCode?.ToString() ?? error.Kind.ToString()})");
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

        using var child = ImRaii.Child($"##relic{kind}", Vector2.Zero, false);
        foreach (var series in catalog.Series.Where(s => string.Equals(s.Kind, kind, StringComparison.Ordinal)))
        {
            var progress = mine?.Series.FirstOrDefault(s => s.Key == series.Key);
            var count = progress is null
                ? string.Empty
                : _localizer.Get(progress.Of == 1 ? LocKeys.RelicSeriesCountOne : LocKeys.RelicSeriesCountMany, progress.Done, progress.Of);
            var header = string.IsNullOrEmpty(count) ? series.Name ?? series.Key : $"{series.Name ?? series.Key}   ·   {count}";
            if (!ImGui.CollapsingHeader($"{header}###series_{series.Key}"))
            {
                continue;
            }

            DrawSeries(series, progress, characterId);
            ImGui.Spacing();
        }
    }

    private void DrawSeries(RelicSeries series, MyRelicSeries? progress, long? characterId)
    {
        using var table = ImRaii.Table($"##tbl_{series.Key}", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("job", ImGuiTableColumnFlags.WidthFixed, 44f);
        ImGui.TableSetupColumn("stages", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("next", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("web", ImGuiTableColumnFlags.WidthFixed);

        foreach (var row in RelicService.Rows(series, progress))
        {
            using var id = ImRaii.PushId($"{series.Key}_{row.Job}");
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(row.Job);

            ImGui.TableNextColumn();
            for (var i = 0; i < row.Stages.Count; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine(0, 3f);
                }

                DrawStage(series, row, row.Stages[i], i + 1, characterId);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (row.Done)
            {
                ImGui.TextColored(Green, T(LocKeys.RelicDone));
            }
            else if (row.Next is { } next)
            {
                var names = string.Join(" + ", next.Items.Select(ItemName));
                ImGui.TextUnformatted(_localizer.Get(LocKeys.RelicNext, names, next.Ilvl));
            }

            ImGui.TableNextColumn();
            if (ImGui.SmallButton(T(LocKeys.RelicWebsite)))
            {
                var page = German ? "relikte" : "relics";
                _openLink($"{_webAppBase()}/{page}?series={Uri.EscapeDataString(series.Key)}&job={Uri.EscapeDataString(row.Job)}");
            }
        }
    }

    /// <summary>One stage as a small numbered button: green held, blue ticked, grey missing.</summary>
    private void DrawStage(RelicSeries series, RelicJobRow row, RelicStageMark stage, int number, long? characterId)
    {
        var fill = stage.Mark switch
        {
            RelicMark.Held => HeldFill,
            RelicMark.Ticked => TickedFill,
            _ => MissingFill,
        };

        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Button, fill))
        using (ImRaii.Disabled(_ticking || stage.Mark == RelicMark.Held))
        {
            clicked = ImGui.Button($"{number}##st_{stage.Key}", new Vector2(24f, 0f));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
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
                        : $"{T(LocKeys.RelicError)} ({error.StatusCode?.ToString() ?? error.Kind.ToString()})";
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
