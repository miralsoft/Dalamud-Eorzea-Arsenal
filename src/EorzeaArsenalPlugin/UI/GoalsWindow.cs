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

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The goals view (server 1.5, <c>goals-progress</c>): the character's open goals with the progress of each
/// cost, "Was noch fehlt" and the done goals, as <see cref="GoalsService"/> read them. Every number is the
/// server's; nothing here computes a cost, a sum or "done". Read when the view opens and after a sync. Where
/// the server takes goals from a key (<c>goals-from-plugin</c>), a goal can be removed here after a second
/// click; ticking and changing stay on the website.
/// </summary>
public sealed class GoalsWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.36f, 0.78f, 0.36f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Link = new(0.45f, 0.65f, 1f, 1f);
    private static readonly Vector4 BarBack = new(0.22f, 0.22f, 0.25f, 1f);

    private readonly Localizer _localizer;
    private readonly GoalsService _goals;
    private readonly Func<string?> _cidHash;
    private readonly Func<bool> _writeOffered;
    private readonly Func<string> _webAppBase;
    private readonly Action<string> _openLink;
    private readonly Func<int, uint> _iconId;
    private readonly Func<int, string> _gameItemName;
    private readonly ITextureProvider _textures;
    private readonly ILog _log;
    private readonly Action _goalsChanged;
    private readonly CancellationTokenSource _cts = new();
    private volatile string? _message;
    private volatile bool _writing;
    private long _armedRemove;

    /// <summary>Creates the goals view.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="goals">The goals service.</param>
    /// <param name="cidHash">The logged-in character, or <see langword="null"/>.</param>
    /// <param name="writeOffered">Whether the server takes goals from a key (<c>goals-from-plugin</c>).</param>
    /// <param name="webAppBase">The website's base address, for the links.</param>
    /// <param name="openLink">Opens an http(s) address in the browser.</param>
    /// <param name="iconId">The game's icon of an item.</param>
    /// <param name="gameItemName">The game's name of an item, for one the server sends without a name.</param>
    /// <param name="textures">Loads the icons.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="goalsChanged">Called after a goal was removed (the tracked list follows the goals).</param>
    public GoalsWindow(
        Localizer localizer,
        GoalsService goals,
        Func<string?> cidHash,
        Func<bool> writeOffered,
        Func<string> webAppBase,
        Action<string> openLink,
        Func<int, uint> iconId,
        Func<int, string> gameItemName,
        ITextureProvider textures,
        ILog log,
        Action goalsChanged)
        : base("Eorzea Arsenal · Goals###EorzeaArsenalGoals")
    {
        _localizer = localizer;
        _goals = goals;
        _cidHash = cidHash;
        _writeOffered = writeOffered;
        _webAppBase = webAppBase;
        _openLink = openLink;
        _iconId = iconId;
        _gameItemName = gameItemName;
        _textures = textures;
        _log = log;
        _goalsChanged = goalsChanged;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
            MaximumSize = new Vector2(1400, 1500),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private string T(string key, params object[] args) => _localizer.Get(key, args);

    private bool German => _localizer.Language == Localizer.German;

    private string Language => German ? Localizer.German : Localizer.English;

    private CultureInfo Culture => German ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-US");

    /// <inheritdoc />
    public override void PreDraw()
    {
        // The title follows the language; the part after ### keeps the window's identity and its place.
        WindowName = $"Eorzea Arsenal · {T(LocKeys.GoalOpen)}###EorzeaArsenalGoals";
    }

    /// <inheritdoc />
    public override void OnOpen()
    {
        // Opening the view is one of the two moments the contract names for reading the goals.
        _goals.Invalidate();
        _message = null;
        _armedRemove = 0;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        var cid = _cidHash();
        if (string.IsNullOrEmpty(cid))
        {
            Wrapped(Dim, T(LocKeys.GoalsNotLoggedIn));
            return;
        }

        if (_goals.NeedsRead(Language, cid) && !_goals.Loading)
        {
            var language = Language;
            _ = Task.Run(() => _goals.RefreshAsync(language, cid, _cts.Token));
        }

        Wrapped(Dim, T(LocKeys.GoalsHint));
        if (ImGui.SmallButton(T(LocKeys.GoalsOpenWeb)))
        {
            _openLink(_webAppBase() + GoalLinks.Path(null, German));
        }

        if (_message is { } message)
        {
            Wrapped(Yellow, message);
        }

        if (_goals.LastError is { } error && !_goals.Loading)
        {
            Wrapped(Red, error.Kind == ApiErrorKind.NotFound ? T(LocKeys.GoalsNotSynced) : T(LocKeys.GoalsError, Code(error)));
        }

        var mine = _goals.Mine;
        if (mine is null)
        {
            if (_goals.LastError is null)
            {
                ImGui.TextDisabled(T(LocKeys.GoalsLoading));
            }

            return;
        }

        if (mine.Goals.Count == 0)
        {
            ImGui.Spacing();
            Wrapped(Dim, T(LocKeys.GoalsNone));
            return;
        }

        ImGui.Spacing();
        using var tabs = ImRaii.TabBar("##goalTabs");
        if (!tabs)
        {
            return;
        }

        using (var open = ImRaii.TabItem(T(LocKeys.GoalsOpenSection)))
        {
            if (open)
            {
                DrawOpen(mine, cid);
            }
        }

        if (mine.Plan.Count > 0)
        {
            using var plan = ImRaii.TabItem(T(LocKeys.GoalsPlanSection));
            if (plan)
            {
                DrawPlan(mine);
            }
        }

        using (var done = ImRaii.TabItem(T(LocKeys.GoalsDoneSection)))
        {
            if (done)
            {
                DrawDone(mine, cid);
            }
        }
    }

    private void DrawOpen(MyGoals mine, string cid)
    {
        var open = mine.Goals.Where(g => g.Done is null).ToList();
        ImGui.Spacing();
        if (open.Count == 0)
        {
            Wrapped(Dim, T(LocKeys.GoalsNoneOpen));
            return;
        }

        using var child = ImRaii.Child("##goalsOpen", Vector2.Zero, false);
        var size = ImGui.GetFrameHeight() * 1.6f;
        foreach (var goal in open)
        {
            using var id = ImRaii.PushId($"open{goal.Id}");
            DrawIcon(goal.Item?.Id ?? 0, size);
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                DrawGoalName(goal);
                DrawRemove(goal, cid);

                // A goal to collect an item to learn (unlock-kinds) counts what is held, not whether it is learned.
                var collect = string.Equals(goal.Purpose, GoalPurposes.Collect, StringComparison.Ordinal);
                if (collect)
                {
                    ImGui.TextColored(Dim, T(LocKeys.GoalPurposeCollect));
                }

                if (goal.Shared)
                {
                    ImGui.TextColored(Dim, T(LocKeys.GoalShared));
                }

                if (goal.Unused)
                {
                    ImGui.TextColored(Green, T(LocKeys.GoalUnused));
                }
                else if ((!goal.Learnable || collect) && goal.Qty > 1)
                {
                    ImGui.TextUnformatted(T(LocKeys.GoalHeldOf, Number(goal.Held), Number(goal.Qty)));
                }
                else if (goal.Held > 0)
                {
                    ImGui.TextUnformatted(T(LocKeys.GoalHeld, Number(goal.Held)));
                }

                if (goal.Progress is { Cost.Count: > 0 } progress)
                {
                    foreach (var cost in progress.Cost)
                    {
                        DrawCost(cost);
                    }
                }
                else if (!goal.Unused)
                {
                    ImGui.TextColored(Dim, T(LocKeys.GoalNoWay));
                }
            }

            ImGui.Separator();
        }
    }

    /// <summary>One cost: its icon, "have / need name", what is missing, and a thin bar of have against need.</summary>
    private void DrawCost(GoalCost cost)
    {
        var line = ImGui.GetTextLineHeight();
        DrawIcon((int)cost.Id, line);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{Number(cost.Have)} / {Number(cost.Need)} {Name(cost.Name, cost.Id)}");
        ImGui.SameLine();
        if (cost.Missing > 0)
        {
            ImGui.TextColored(Red, T(LocKeys.GoalMissing, Number(cost.Missing)));
        }
        else
        {
            ImGui.TextColored(Green, T(LocKeys.GoalEnough));
        }

        // A drawing of the server's two numbers, not a judgement: "enough" above is the server's missing.
        var fraction = cost.Need > 0 ? Math.Clamp(cost.Have / (float)cost.Need, 0f, 1f) : 0f;
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, cost.Missing > 0 ? Yellow : Green))
        using (ImRaii.PushColor(ImGuiCol.FrameBg, BarBack))
        {
            ImGui.ProgressBar(fraction, new Vector2(220f * ImGui.GetIO().FontGlobalScale, 3f), string.Empty);
        }
    }

    private void DrawPlan(MyGoals mine)
    {
        ImGui.Spacing();
        using var table = ImRaii.Table("##goalPlan", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("count", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("missing", ImGuiTableColumnFlags.WidthFixed);

        var line = ImGui.GetFrameHeight();
        foreach (var row in mine.Plan)
        {
            using var id = ImRaii.PushId($"plan{row.Id}");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawIcon((int)row.Id, line);
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                ImGui.TextUnformatted(Name(row.Name, row.Id));
                if (row.Goals.Count > 0)
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Dim))
                    {
                        ImGui.TextWrapped(T(LocKeys.GoalForGoals, string.Join(", ", row.Goals)));
                    }
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{Number(row.Have)} / {Number(row.Need)}");
            ImGui.TableNextColumn();
            if (row.Missing > 0)
            {
                ImGui.TextColored(Red, T(LocKeys.GoalMissing, Number(row.Missing)));
            }
            else
            {
                ImGui.TextColored(Green, T(LocKeys.GoalEnough));
            }
        }
    }

    private void DrawDone(MyGoals mine, string cid)
    {
        var done = mine.Goals.Where(g => g.Done is not null).ToList();
        ImGui.Spacing();
        using var child = ImRaii.Child("##goalsDone", Vector2.Zero, false);
        var line = ImGui.GetFrameHeight();
        foreach (var goal in done)
        {
            using var id = ImRaii.PushId($"done{goal.Id}");
            DrawIcon(goal.Item?.Id ?? 0, line);
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                DrawGoalName(goal);
                DrawRemove(goal, cid);
                var why = goal.Done?.By switch
                {
                    "hand" => T(LocKeys.GoalDoneHand),
                    "learned" => T(LocKeys.GoalDoneLearned),
                    "held" => T(LocKeys.GoalDoneHeld),
                    _ => string.Empty,
                };
                var at = goal.Done?.At is { Length: >= 10 } stamp ? $"  ·  {Day(stamp)}" : string.Empty;
                ImGui.TextColored(Green, why + at);
            }
        }
    }

    /// <summary>The goal's name, which opens the item's page on the website at what the character holds of it.</summary>
    private void DrawGoalName(Goal goal)
    {
        var itemId = goal.Item?.Id ?? 0;
        ImGui.TextColored(Link, Name(goal.Item?.Name, itemId));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemClicked() && itemId > 0)
        {
            _openLink($"{_webAppBase()}/item/{itemId.ToString(CultureInfo.InvariantCulture)}#besitz");
        }
    }

    /// <summary>"Ziel entfernen", which asks once more before it removes; only where the server takes it from a key.</summary>
    private void DrawRemove(Goal goal, string cid)
    {
        if (!_writeOffered())
        {
            return;
        }

        ImGui.SameLine();
        using var disabled = ImRaii.Disabled(_writing);
        if (_armedRemove == goal.Id)
        {
            if (ImGui.SmallButton(T(LocKeys.GoalMenuRemove) + "?"))
            {
                _armedRemove = 0;
                Remove(goal, cid);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("x"))
            {
                _armedRemove = 0;
            }
        }
        else if (ImGui.SmallButton(T(LocKeys.GoalMenuRemove)))
        {
            _armedRemove = goal.Id;
        }
    }

    private void Remove(Goal goal, string cid)
    {
        _writing = true;
        _message = null;
        var language = Language;
        var name = Name(goal.Item?.Name, goal.Item?.Id ?? 0);
        _ = Task.Run(async () =>
        {
            try
            {
                var error = await _goals.RemoveAsync(goal.Id, language, cid, _cts.Token).ConfigureAwait(false);
                _message = error is null ? T(LocKeys.GoalRemoved, name) : GoalMessages.Describe(_localizer, error, name);
                if (error is null)
                {
                    _goalsChanged();
                }
            }
            catch (OperationCanceledException)
            {
                // closing down
            }
            catch (Exception ex)
            {
                _log.Error($"Goal remove failed: {ex.GetType().Name}.");
            }
            finally
            {
                _writing = false;
            }
        });
    }

    private void DrawIcon(long itemId, float size)
    {
        var icon = itemId > 0 ? _iconId((int)itemId) : 0;
        if (icon == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
    }

    /// <summary>The server's name, or the game's where the server sends none (<c>#&lt;id&gt;</c>).</summary>
    private string Name(string? name, long itemId) =>
        !string.IsNullOrEmpty(name) && !name.StartsWith('#') ? name : itemId > 0 ? _gameItemName((int)itemId) : string.Empty;

    private string Number(long value) => value.ToString("N0", Culture);

    private string Day(string stamp) =>
        DateTime.TryParseExact(stamp[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString(German ? "dd.MM.yyyy" : "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : stamp[..10];

    private static string Code(ApiError error) =>
        error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? error.Kind.ToString();

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

/// <summary>The player's sentence for a goal that could not be added or removed, from the contract's answers.</summary>
internal static class GoalMessages
{
    /// <summary>Describes a failed add or remove.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="error">The error.</param>
    /// <param name="name">The item's name.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(Localizer localizer, ApiError error, string name) => error.Kind switch
    {
        ApiErrorKind.Conflict => localizer.Get(LocKeys.GoalConflict, name),
        ApiErrorKind.BadRequest => localizer.Get(LocKeys.GoalUnknownItem, name),
        ApiErrorKind.NotFound => localizer.Get(LocKeys.GoalNeedsSync),
        ApiErrorKind.RateLimited => localizer.Get(LocKeys.GoalBusy),
        _ => localizer.Get(LocKeys.GoalFailed, error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? error.Kind.ToString()),
    };
}
