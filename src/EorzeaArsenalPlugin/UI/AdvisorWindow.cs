using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The purchase-advisor ("Kaufberater") window: the gear <i>between</i> raid tiers on the way to BiS.
/// Deliberately separate from the BiS window — BiS is the goal, this is the path to it — and it never
/// surfaces a plan as a gearset.
/// </summary>
/// <remarks>
/// Two views over one set, exactly like the web. <b>Recommendation</b> renders the server's single
/// ranking (<c>GET /me/advisor-options</c>): what to do per slot, what it costs in tomestones, and
/// when it is affordable given the balance the plugin pushed. It is computed server-side on purpose,
/// so a rule change or a tier rotation reaches the plugin without a release. <b>My layout</b> is the
/// player's own override, stored via <c>/me/advisor-plan</c> and edited here from the very options the
/// server offers — the plugin never invents an item id. Below both sits the tier's stock. Holds no
/// domain logic (R11).
/// </remarks>
public sealed class AdvisorWindow : Window
{
    private const float IconSize = 32f;
    private const float TileSize = 40f;
    private static readonly string[] Sorts = ["power", "value", "cheap"];

    // The character-screen layout, row by row — the same shape as the BiS grid, so a player reads both
    // windows the same way.
    private static readonly (string Left, string Right)[] GridRows =
    [
        ("Weapon", "OffHand"),
        ("Head", "Ears"),
        ("Body", "Neck"),
        ("Hands", "Wrists"),
        ("Legs", "RingLeft"),
        ("Feet", "RingRight"),
    ];

    private static readonly Vector4 Accent = new(0.62f, 0.82f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.78f, 0.80f, 0.85f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Orange = new(0.96f, 0.62f, 0.22f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Blue = new(0.45f, 0.68f, 0.95f, 1f);
    private static readonly Vector4 Grey = new(0.58f, 0.58f, 0.62f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly BisService _bis;
    private readonly AdvisorService _advisor;
    private readonly TrackedItemsStore _tracked;
    private readonly HoldingsService _holdings;
    private readonly ObtainService _obtain;
    private readonly GameGearSource _gearSource;
    private readonly IWorldActions _world;
    private readonly ITextureProvider _textures;
    private readonly SourcingView _sourcing;
    private readonly Func<string?, long?> _resolveCharacterId;
    private readonly Action<int> _linkItem;

    // The gearset the user is looking at; -1 means "not chosen yet — take the one being worn".
    private int _selectedGearIndex = -1;

    // The gearset the character wore last frame, so an in-game job/gearset switch can be told apart
    // from the player simply having picked another set from the combo.
    private int _lastWornGearIndex = -1;
    private bool _showPlanView;
    private int _sortIndex;

    // The layout being edited, and which set it belongs to — so switching sets never carries edits
    // from one set into another.
    private readonly Dictionary<string, long> _draft = new(StringComparer.Ordinal);
    private string? _draftKey;
    private bool _draftDirty;
    private string? _saveMessage;
    private bool _saveFailed;

    // Stock ids already asked for, so Draw fires one prefetch per set of ids, not one per frame.
    private readonly HashSet<long> _stockRequested = [];

    // Per-frame counter giving each drawn icon its own popup id.
    private int _iconSeq;

    /// <summary>Creates the purchase-advisor window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store (gates every read).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="bis">Supplies the sets and their targets.</param>
    /// <param name="advisor">Reads the server's advice and the saved layouts.</param>
    /// <param name="tracked">Supplies the active tier's stock groups.</param>
    /// <param name="holdings">Server-side owned counts (retainers included).</param>
    /// <param name="obtain">Impersonal "how to get it" sourcing for the pieces.</param>
    /// <param name="gearSource">Resolves item names, item levels and icons.</param>
    /// <param name="world">Game actions (live owned count, open the map at a vendor).</param>
    /// <param name="textures">Loads game icons.</param>
    /// <param name="resolveCharacterId">Fallback <c>cid_hash</c> → server character id, learned from pushes.</param>
    /// <param name="linkItem">Posts a clickable item link to the game chat (arg: item id).</param>
    public AdvisorWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        BisService bis,
        AdvisorService advisor,
        TrackedItemsStore tracked,
        HoldingsService holdings,
        ObtainService obtain,
        GameGearSource gearSource,
        IWorldActions world,
        ITextureProvider textures,
        Func<string?, long?> resolveCharacterId,
        Action<int> linkItem)
        : base("Eorzea Arsenal###EorzeaArsenalAdvisor")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _bis = bis;
        _advisor = advisor;
        _tracked = tracked;
        _holdings = holdings;
        _obtain = obtain;
        _gearSource = gearSource;
        _world = world;
        _textures = textures;
        _sourcing = new SourcingView(localizer, world, obtain, holdings);
        _resolveCharacterId = resolveCharacterId;
        _linkItem = linkItem;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(1100, 1600),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private string Sort => Sorts[Math.Clamp(_sortIndex, 0, Sorts.Length - 1)];

    /// <inheritdoc />
    public override void OnOpen()
    {
        // The sets (and their targets) come from the BiS read; the advice is derived from live gear and
        // holdings, so both are worth refreshing when the window opens.
        if (_config.Enabled && _store.HasKey && !_bis.IsLoading && _bis.IsStale(TimeSpan.FromSeconds(30)))
        {
            _ = Task.Run(() => _bis.RefreshAsync(CancellationToken.None));
        }

        _advisor.InvalidateOptions();
    }

    /// <inheritdoc />
    public override void Draw()
    {
        WindowName = $"{T(LocKeys.AdvisorWindowTitle)}###EorzeaArsenalAdvisor";

        _iconSeq = 0;

        // Item names here are long and the pickers hold dozens of entries, so the window runs at its
        // own scale rather than the game's default — otherwise the list is unreadable in a raid.
        ImGui.SetWindowFontScale(Math.Clamp(_config.AdvisorTextScale, 1f, 1.6f));
        try
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Muted))
            {
                ImGui.TextWrapped(T(LocKeys.AdvisorIntro));
            }

            ImGui.Spacing();
            var comparison = DrawSetPicker();
            DrawToolbar();
            ImGui.Separator();

            DrawSetBody(comparison);

            ImGui.Spacing();
            ImGui.Separator();
            DrawStockSection();
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    /// <summary>
    /// The set picker. Defaults to the gearset the character currently wears, so the window opens on
    /// what the player is playing; the selection then sticks until they change it.
    /// </summary>
    private GearsetComparison? DrawSetPicker()
    {
        var sets = _bis.Comparisons;
        if (sets.Count == 0)
        {
            ImGui.TextDisabled(_bis.IsLoading ? T(LocKeys.BisLoading) : T(LocKeys.BisNone));
            return null;
        }

        FollowWornGearset(sets);

        var labels = sets.Select(SetLabel).ToArray();
        var index = 0;
        for (var i = 0; i < sets.Count; i++)
        {
            if (sets[i].GearIndex == _selectedGearIndex)
            {
                index = i;
            }
        }

        ImGui.SetNextItemWidth(320f);
        if (ImGui.Combo(T(LocKeys.AdvisorSetLabel), ref index, labels, labels.Length))
        {
            _selectedGearIndex = sets[index].GearIndex;
        }

        return sets[index];
    }

    private void DrawToolbar()
    {
        if (ImGui.RadioButton(T(LocKeys.AdvisorViewRecommendation), !_showPlanView))
        {
            _showPlanView = false;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(T(LocKeys.AdvisorViewPlan), _showPlanView))
        {
            _showPlanView = true;
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.AdvisorRefresh)))
        {
            _advisor.InvalidateOptions();
            _holdings.Invalidate();
            _stockRequested.Clear();
        }

        // The ranking is the server's, not ours: it schedules purchases against the weekly cap, so a
        // client that re-sorted would show an order its own schedule no longer matches.
        if (_showPlanView)
        {
            return;
        }

        ImGui.SameLine();
        ReadOnlySpan<string> sorts = [T(LocKeys.AdvisorSortPower), T(LocKeys.AdvisorSortValue), T(LocKeys.AdvisorSortCheap)];
        ImGui.SetNextItemWidth(200f);
        var sort = _sortIndex;
        if (ImGui.Combo(T(LocKeys.AdvisorSortLabel), ref sort, sorts, sorts.Length))
        {
            _sortIndex = sort;
        }
    }

    /// <summary>
    /// Moves the shown set to the one the character actually wears whenever they switch gearset or job
    /// in game — the BiS window behaves that way, and switching class to look at that class's advice is
    /// exactly why the window is open. Picking another set from the combo still sticks: only a real
    /// in-game change moves the selection, not every frame.
    /// </summary>
    private void FollowWornGearset(IReadOnlyList<GearsetComparison> sets)
    {
        var worn = _gearSource.GetCurrentGearsetIndex();
        var wornIsKnown = sets.Any(s => s.GearIndex == worn);
        var switched = worn != _lastWornGearIndex;
        _lastWornGearIndex = worn;

        // Never yank the view away from a layout that has unsaved edits in it.
        if (switched && wornIsKnown && !(_showPlanView && _draftDirty))
        {
            _selectedGearIndex = worn;
            return;
        }

        if (_selectedGearIndex < 0 || sets.All(s => s.GearIndex != _selectedGearIndex))
        {
            _selectedGearIndex = wornIsKnown ? worn : sets[0].GearIndex;
        }
    }

    private static string SetLabel(GearsetComparison set) =>
        string.IsNullOrEmpty(set.Name) ? $"#{set.GearIndex} {set.Job}" : $"#{set.GearIndex} {set.Job} — {set.Name}";

    /// <summary>Resolves the set's identity, then renders whichever view is selected.</summary>
    private void DrawSetBody(GearsetComparison? comparison)
    {
        if (comparison is null)
        {
            return;
        }

        var set = _bis.TargetGearset(comparison.GearIndex);

        // A plan and the advice are both addressed by the set's web identity. Without it there is no
        // key — an older server simply does not send one.
        if (string.IsNullOrEmpty(set?.Target))
        {
            Wrapped(Muted, T(LocKeys.AdvisorNoTarget));
            return;
        }

        // The server hands the numeric id back with the set; the locally learned directory is only the
        // fallback for a server that does not.
        var characterId = ParseId(set.CharacterId) ?? _resolveCharacterId(set.CidHash);
        if (characterId is not { } id)
        {
            Wrapped(Muted, T(LocKeys.AdvisorNoCharacter));
            return;
        }

        if (!_advisor.TryGetOptions(id, comparison.Job, set.Target, Sort, out var options) || options is null)
        {
            _ = _advisor.EnsureOptionsAsync(id, comparison.Job, set.Target, comparison.GearIndex, Sort, CancellationToken.None);
            ImGui.TextDisabled(_advisor.LastErrorKind == ApiErrorKind.Forbidden
                ? T(LocKeys.AdvisorScopeHint)
                : T(LocKeys.AdvisorLoadingOptions));
            return;
        }

        if (_showPlanView)
        {
            DrawPlanView(id, comparison.Job, set, options);
        }
        else
        {
            DrawRecommendation(options);
        }
    }

    // --- Recommendation -------------------------------------------------------------------------

    /// <summary>The server's ranked steps: what to do, what it costs, and when it is affordable.</summary>
    private void DrawRecommendation(AdvisorOptions options)
    {
        DrawHeadline(options);
        DrawSummary(options);

        ImGui.Spacing();
        ImGui.TextColored(Accent, T(LocKeys.AdvisorYourSet));
        DrawSlotGrid(options, planned: null);
        ImGui.TextDisabled(T(LocKeys.AdvisorLegend));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Accent, T(LocKeys.AdvisorOrderHeading));

        var steps = options.Steps ?? [];
        if (steps.Count == 0)
        {
            // No steps does not mean the set is done: a slot whose BiS cannot be bought — an Ultimate
            // weapon, a relic — is open with nothing to plan for it. Saying "nothing to do" there would
            // claim the set was finished while a slot still sits red in the grid.
            var slots = options.Slots ?? [];
            var open = slots.Count(kv => !IsOnBis(kv.Key, kv.Value, slots));
            if (open > 0)
            {
                Wrapped(Muted, _localizer.Get(LocKeys.AdvisorNoSteps, open));
            }
            else
            {
                ImGui.TextColored(Green, T(LocKeys.AdvisorNothingToDo));
            }
        }

        for (var i = 0; i < steps.Count; i++)
        {
            DrawStep(i + 1, steps[i]);
        }

        DrawNeeds(options.Materials);
    }

    /// <summary>
    /// The one-line answer the window exists for: the single best next move, with how much of the
    /// remaining way to BiS it closes.
    /// </summary>
    private void DrawHeadline(AdvisorOptions options)
    {
        if (options.Steps is not { Count: > 0 } steps)
        {
            return;
        }

        var best = steps[0];
        var itemId = (int)(best.To ?? 0);
        if (itemId <= 0)
        {
            return;
        }

        var from = (int)(best.From ?? 0);

        ImGui.TextDisabled(T(LocKeys.AdvisorNextBest));
        DrawItemIcon(itemId, TileSize, from);
        ImGui.SameLine();
        ImGui.BeginGroup();

        var arrow = from > 0
            ? $"i{_gearSource.GetItemLevel(from)} → i{_gearSource.GetItemLevel(itemId)}"
            : $"i{_gearSource.GetItemLevel(itemId)}";
        ImGui.TextColored(Accent, $"{KindLabel(best.Kind)}: {_gearSource.GetItemName(itemId)}  ·  {arrow}");

        var (whenText, whenColor) = WhenLabel(best.When);
        var cost = best.Cost > 0 ? $"{_localizer.Get(LocKeys.AdvisorCostTomes, best.Cost)} · " : string.Empty;
        ImGui.TextColored(whenColor, $"{cost}{whenText}");
        ImGui.EndGroup();

        if (best.Pct > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Green, $"+{best.Pct}% {T(LocKeys.AdvisorProgress)}");
        }

        ImGui.Spacing();
    }

    /// <summary>The set at a glance: slots already on BiS, the average item level, and the balance.</summary>
    private void DrawSummary(AdvisorOptions options)
    {
        var slots = options.Slots ?? [];
        if (slots.Count == 0)
        {
            return;
        }

        var onBis = slots.Count(kv => IsOnBis(kv.Key, kv.Value, slots));
        var current = slots.Values.Where(s => s.Current is not null).Select(s => s.Current!.Ilvl).ToList();
        var target = slots.Values.Where(s => s.Bis is not null).Select(s => s.Bis!.Ilvl).ToList();

        ImGui.TextColored(Muted, $"{T(LocKeys.AdvisorOnBis)} {onBis}/{slots.Count}");
        if (current.Count > 0 && target.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"   ·   {T(LocKeys.AdvisorAvgIlvl)} {current.Average():0} → {target.Average():0}");
        }

        ImGui.SameLine();
        ImGui.TextColored(Muted, $"   ·   {_localizer.Get(LocKeys.AdvisorBalance, options.TomeBalance, options.WeeklyCap)}");
    }

    /// <summary>
    /// Rings are interchangeable, so a slot counts as done when its piece sits in <i>either</i> ring
    /// target — the same rule the server ranks by.
    /// </summary>
    private static bool IsOnBis(string slot, AdvisorSlot entry, Dictionary<string, AdvisorSlot> all)
    {
        var worn = entry.Current?.Id ?? 0;
        if (worn <= 0)
        {
            return false;
        }

        if (slot is "RingLeft" or "RingRight")
        {
            return worn == (all.GetValueOrDefault("RingLeft")?.Bis?.Id ?? 0)
                || worn == (all.GetValueOrDefault("RingRight")?.Bis?.Id ?? 0);
        }

        return worn == (entry.Bis?.Id ?? 0);
    }

    /// <summary>
    /// The character-screen grid: per slot what is worn and what it becomes next, colour-coded so the
    /// state of the whole set reads at a glance. In the layout view the "next" piece is the player's
    /// own pick instead of the recommendation.
    /// </summary>
    private void DrawSlotGrid(AdvisorOptions options, IReadOnlyDictionary<string, long>? planned)
    {
        var slots = options.Slots ?? [];
        if (slots.Count == 0)
        {
            return;
        }

        if (!ImGui.BeginTable("##advisorgrid", 4, ImGuiTableFlags.PadOuterX))
        {
            return;
        }

        ImGui.TableSetupColumn("li", ImGuiTableColumnFlags.WidthFixed, (TileSize * 2f) + 24f);
        ImGui.TableSetupColumn("ld", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("ri", ImGuiTableColumnFlags.WidthFixed, (TileSize * 2f) + 24f);
        ImGui.TableSetupColumn("rd", ImGuiTableColumnFlags.WidthStretch);

        foreach (var (left, right) in GridRows)
        {
            ImGui.TableNextRow();
            DrawGridCells(left, slots, planned);
            DrawGridCells(right, slots, planned);
        }

        ImGui.EndTable();
    }

    private void DrawGridCells(string slot, Dictionary<string, AdvisorSlot> slots, IReadOnlyDictionary<string, long>? planned)
    {
        ImGui.TableNextColumn();
        if (!slots.TryGetValue(slot, out var entry))
        {
            // A slot this job does not fill (no off-hand): leave the row aligned and move on.
            ImGui.Dummy(new Vector2(TileSize, TileSize));
            ImGui.TableNextColumn();
            return;
        }

        var worn = (int)(entry.Current?.Id ?? 0);
        var next = (int)(planned is not null
            ? planned.GetValueOrDefault(slot)
            : entry.Recommended ?? entry.Current?.Id ?? 0);

        // Both icons answer on hover: the left one is what you wear, the right one what it becomes.
        DrawItemIcon(worn, TileSize, worn);
        if (next > 0 && next != worn)
        {
            ImGui.SameLine(0f, 4f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Muted, "→");
            ImGui.SameLine(0f, 4f);
            DrawItemIcon(next, TileSize, worn);
        }

        ImGui.TableNextColumn();
        var shown = next > 0 ? next : worn;
        var color = SlotColor(slot, entry, slots, next);
        var detail = shown > 0 ? $"i{_gearSource.GetItemLevel(shown)}" : "—";
        ClickableItem(color, $"{_sourcing.SlotName(slot)}  ·  {detail}", shown, $"##grid{slot}", worn);
    }

    /// <summary>
    /// The tile colour, matching the web advisor: green = already on BiS, blue = you own the piece and
    /// only have to put it on, orange = the next purchase, grey = nothing deterministic left (a savage
    /// BiS you can only get lucky on).
    /// </summary>
    private Vector4 SlotColor(string slot, AdvisorSlot entry, Dictionary<string, AdvisorSlot> slots, long next)
    {
        if (IsOnBis(slot, entry, slots))
        {
            return Green;
        }

        if (entry.Even)
        {
            return Grey;
        }

        var worn = entry.Current?.Id ?? 0;
        if (next > 0 && next != worn && Owned((int)next) > 0)
        {
            return Blue;
        }

        return next > 0 && next != worn ? Orange : Grey;
    }

    private void DrawStep(int rank, AdvisorStep step)
    {
        var itemId = (int)(step.To ?? 0);
        if (itemId <= 0 || string.IsNullOrEmpty(step.Slot))
        {
            return;
        }

        ImGui.Spacing();
        DrawItemIcon(itemId, IconSize, (int)(step.From ?? 0));
        ImGui.SameLine();
        ImGui.BeginGroup();

        var (whenText, whenColor) = WhenLabel(step.When);
        var head = $"{rank}. {_sourcing.SlotName(step.Slot)} — {KindLabel(step.Kind)}: " +
            $"{_gearSource.GetItemName(itemId)} · iLvl {_gearSource.GetItemLevel(itemId)}";
        ClickableItem(Muted, head, itemId, $"##step{step.Slot}", (int)(step.From ?? 0));

        // The price line: what it costs, when it is doable, and where to buy it.
        var parts = new List<string>();
        if (step.Cost > 0)
        {
            parts.Add(_localizer.Get(LocKeys.AdvisorCostTomes, step.Cost));
        }

        parts.Add(whenText);
        if (!string.IsNullOrEmpty(step.Vendor))
        {
            parts.Add(_localizer.Get(LocKeys.AdvisorVendor, step.Vendor));
        }

        ImGui.TextColored(whenColor, "    " + string.Join(" · ", parts));

        // A bridge piece: say what it is on the way to, so a "why this?" never comes up.
        if (step.Final is { } final && final > 0 && final != step.To)
        {
            ImGui.TextColored(Muted, $"    {_localizer.Get(LocKeys.AdvisorLeadsTo, _gearSource.GetItemName((int)final))}");
        }

        foreach (var material in step.Material ?? [])
        {
            // The server counts what it has stored; the game counts what is in the bags right now. The
            // advice may have been computed before the last sync landed, so take the better number.
            var owned = Math.Max(material.Owned, Owned((int)material.Id));
            ImGui.TextColored(owned >= material.Count ? Green : Orange,
                $"    {material.Count}× {_gearSource.GetItemName((int)material.Id)} ({owned}/{material.Count})");
        }

        ImGui.EndGroup();
    }

    /// <summary>What the still-open slots need in material and books, with what is already held.</summary>
    private void DrawNeeds(List<AdvisorMaterialNeed>? needs)
    {
        if (needs is not { Count: > 0 })
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(Accent, T(LocKeys.AdvisorNeeds));
        foreach (var need in needs)
        {
            var owned = Math.Max(need.Owned, Owned((int)need.Id));
            DrawItemIcon((int)need.Id, IconSize);
            ImGui.SameLine();
            ClickableItem(
                owned >= need.Need ? Green : Orange,
                $"{_gearSource.GetItemName((int)need.Id)} — {owned}/{need.Need}",
                (int)need.Id,
                $"##need{need.Id}");
        }
    }

    private (string Text, Vector4 Color) WhenLabel(AdvisorWhen? when) => when switch
    {
        { Week: { } weeks and > 0 } => (_localizer.Get(LocKeys.AdvisorWhenWeeks, weeks), Orange),
        { Books: { } books and > 0 } => (_localizer.Get(LocKeys.AdvisorWhenBooks, books), Orange),
        _ => (T(LocKeys.AdvisorWhenNow), Green),
    };

    private string KindLabel(string? kind) => kind switch
    {
        "buy" => T(LocKeys.AdvisorStepBuy),
        "augment" => T(LocKeys.AdvisorStepAugment),
        "trial" => T(LocKeys.AdvisorStepTrial),
        "books" => T(LocKeys.AdvisorStepBooks),
        "equip" => T(LocKeys.AdvisorStepEquip),
        _ => kind ?? string.Empty,
    };

    // --- My layout ------------------------------------------------------------------------------

    /// <summary>
    /// The player's own layout: one picker per slot, filled from the very options the server offers, so
    /// a saved plan can never contain an item id the plugin made up.
    /// </summary>
    private void DrawPlanView(long characterId, string job, BisGearset set, AdvisorOptions options)
    {
        var target = set.Target!;
        if (!_advisor.TryGet(characterId, job, target, out var plan))
        {
            _ = _advisor.EnsureAsync(characterId, job, target, CancellationToken.None);
            ImGui.TextDisabled(_advisor.LastErrorKind == ApiErrorKind.Forbidden
                ? T(LocKeys.AdvisorScopeHint)
                : T(LocKeys.AdvisorLoading));
            return;
        }

        SyncDraft(characterId, job, target, plan, options);

        ImGui.TextColored(Accent, T(LocKeys.AdvisorViewPlan));
        if (plan is null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {T(LocKeys.AdvisorNoPlan)}");
        }
        else if (!string.IsNullOrEmpty(plan.UpdatedAt))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {_localizer.Get(LocKeys.AdvisorPlanSaved, ShortDate(plan.UpdatedAt))}");
        }

        if (_draftDirty)
        {
            ImGui.SameLine();
            ImGui.TextColored(Orange, $"· {T(LocKeys.AdvisorUnsaved)}");
        }

        // The same grid as the recommendation, so switching views does not relearn the layout — only
        // the "next" piece changes from the advisor's pick to the player's.
        DrawSlotGrid(options, _draft);
        ImGui.TextDisabled(T(LocKeys.AdvisorLegend));

        ImGui.Spacing();
        ImGui.Separator();
        foreach (var slot in SlotOrder(options.Slots?.Keys ?? Enumerable.Empty<string>()))
        {
            DrawPlanSlot(slot, options.Slots![slot]);
        }

        ImGui.Spacing();
        DrawPlanActions(characterId, job, set, options, plan);
    }

    /// <summary>
    /// Loads the editable layout when the shown set changes: the saved plan when there is one, else the
    /// recommendation as a starting point. Unsaved edits on the same set survive a redraw.
    /// </summary>
    private void SyncDraft(long characterId, string job, string target, AdvisorPlan? plan, AdvisorOptions options)
    {
        var key = AdvisorService.CacheKey(characterId, job, target);
        if (_draftKey == key)
        {
            return;
        }

        _draftKey = key;
        _draftDirty = false;
        _saveMessage = null;
        _draft.Clear();

        foreach (var (slot, entry) in options.Slots ?? [])
        {
            var planned = plan?.Items is { } items && items.TryGetValue(slot, out var item) && item.Id > 0
                ? item.Id
                : entry.Recommended ?? entry.Current?.Id ?? entry.Bis?.Id ?? 0;
            if (planned > 0)
            {
                _draft[slot] = planned;
            }
        }
    }

    private void DrawPlanSlot(string slot, AdvisorSlot entry)
    {
        var options = entry.Options ?? [];
        var chosen = _draft.GetValueOrDefault(slot);

        // A plan may name a piece the current-tier menu does not carry (an older set, a hand-edit on the
        // web). Keep it selectable rather than silently rewriting the player's choice.
        var ids = options.Select(o => o.Id).ToList();
        if (chosen > 0 && !ids.Contains(chosen))
        {
            ids.Insert(0, chosen);
        }

        if (ids.Count == 0)
        {
            return;
        }

        DrawItemIcon((int)chosen, IconSize, (int)(entry.Current?.Id ?? 0));
        ImGui.SameLine();

        var labels = ids.Select(id => OptionLabel(id, entry)).ToArray();
        var index = Math.Max(0, ids.IndexOf(chosen));

        // The picker holds every current-tier piece for the slot and the names are long, so give it as
        // much of the window as the slot label leaves — a cramped list is unusable.
        var labelWidth = ImGui.CalcTextSize(_sourcing.SlotName(slot)).X + (ImGui.GetStyle().ItemSpacing.X * 2f);
        ImGui.SetNextItemWidth(Math.Max(360f, ImGui.GetContentRegionAvail().X - labelWidth));
        if (ImGui.Combo($"{_sourcing.SlotName(slot)}##planslot{slot}", ref index, labels, labels.Length))
        {
            _draft[slot] = ids[index];
            _draftDirty = true;
            _saveMessage = null;
        }

        if (ImGui.IsItemHovered() && chosen > 0)
        {
            ImGui.BeginTooltip();
            if (_obtain.TryGet((int)chosen, out var info) && info?.Routes is { Count: > 0 } routes)
            {
                _sourcing.DrawBody(info.Source, routes, (int)(entry.Current?.Id ?? 0));
            }
            else
            {
                ImGui.TextDisabled(T(LocKeys.SourceNoInfo));
            }

            ImGui.EndTooltip();
        }

        if (chosen > 0 && !_obtain.TryGet((int)chosen, out _))
        {
            _ = _obtain.PrefetchAsync([chosen], CancellationToken.None);
        }
    }

    /// <summary>An option's label: name, item level, source, and whether it is the recommendation or BiS.</summary>
    private string OptionLabel(long itemId, AdvisorSlot entry)
    {
        var badge = itemId == entry.Recommended
            ? $" · {T(LocKeys.AdvisorIsRecommended)}"
            : itemId == entry.Bis?.Id
                ? $" · {T(LocKeys.AdvisorIsBis)}"
                : string.Empty;

        var source = (entry.Options ?? []).FirstOrDefault(o => o.Id == itemId)?.Source;
        var sourceText = string.IsNullOrEmpty(source) ? string.Empty : $" · {SourceLabel(source)}";
        return $"{_gearSource.GetItemName((int)itemId)} · iLvl {_gearSource.GetItemLevel((int)itemId)}{sourceText}{badge}";
    }

    private void DrawPlanActions(long characterId, string job, BisGearset set, AdvisorOptions options, AdvisorPlan? plan)
    {
        using (ImRaii.Disabled(_advisor.IsBusy || !_draftDirty))
        {
            if (ImGui.Button(T(LocKeys.AdvisorSave)))
            {
                Save(characterId, job, set, new Dictionary<string, long>(_draft, StringComparer.Ordinal));
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.AdvisorAdopt)))
        {
            foreach (var (slot, entry) in options.Slots ?? [])
            {
                var recommended = entry.Recommended ?? entry.Current?.Id ?? 0;
                if (recommended > 0)
                {
                    _draft[slot] = recommended;
                }
            }

            _draftDirty = true;
            _saveMessage = null;
        }

        // Only offered when there is something to drop — deleting a layout the player built on the web
        // is not something to invite by accident.
        if (plan is not null)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(_advisor.IsBusy))
            {
                if (ImGui.Button(T(LocKeys.AdvisorDeletePlan)))
                {
                    Delete(characterId, job, set.Target!);
                }
            }
        }

        if (_saveMessage is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(_saveFailed ? Red : Green, _saveMessage);
        }
    }

    private void Save(long characterId, string job, BisGearset set, Dictionary<string, long> items) => _ = Task.Run(async () =>
    {
        var ok = await _advisor
            .SaveAsync(characterId, job, set.Target!, set.TargetName ?? set.Name, items, CancellationToken.None)
            .ConfigureAwait(false);

        _saveFailed = !ok;
        _saveMessage = ok ? T(LocKeys.AdvisorSaved) : T(LocKeys.AdvisorSaveFailed);
        if (ok)
        {
            _draftDirty = false;
        }
    });

    private void Delete(long characterId, string job, string target) => _ = Task.Run(async () =>
    {
        var ok = await _advisor.DeleteAsync(characterId, job, target, CancellationToken.None).ConfigureAwait(false);
        _saveFailed = !ok;
        _saveMessage = ok ? T(LocKeys.AdvisorSaved) : T(LocKeys.AdvisorSaveFailed);
        if (ok)
        {
            _draftKey = null; // reload the draft from the recommendation on the next frame
        }
    });

    // --- Stock ----------------------------------------------------------------------------------

    /// <summary>
    /// The active tier's tracked consumables with the server's owned counts. The list and its grouping
    /// are server-curated, so a tier rotation carries itself without a plugin release.
    /// </summary>
    private void DrawStockSection()
    {
        var groups = _tracked.Groups;
        if (groups.Count == 0)
        {
            ImGui.TextColored(Accent, T(LocKeys.AdvisorStockHeading));
            ImGui.TextDisabled(T(LocKeys.AdvisorStockEmpty));
            return;
        }

        if (!ImGui.CollapsingHeader(T(LocKeys.AdvisorStockHeading)))
        {
            return;
        }

        PrefetchStock(groups);

        foreach (var group in groups)
        {
            ImGui.Spacing();
            ImGui.TextColored(Muted, GroupLabel(group.Kind));
            foreach (var itemId in group.ItemIds)
            {
                DrawStockRow(itemId);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled(T(LocKeys.AdvisorStockHint));
    }

    private void DrawStockRow(int itemId)
    {
        var count = Owned(itemId);

        DrawItemIcon(itemId, IconSize);
        ImGui.SameLine();
        ClickableItem(count > 0 ? Muted : new Vector4(0.6f, 0.6f, 0.6f, 1f), $"{count}× {_gearSource.GetItemName(itemId)}", itemId, $"##stock{itemId}");
    }

    /// <summary>Asks the server for the stock counts once per new set of ids.</summary>
    private void PrefetchStock(IReadOnlyList<TrackedStockGroup> groups)
    {
        if (!_store.HasKey)
        {
            return;
        }

        var toRequest = new List<long>();
        foreach (var group in groups)
        {
            foreach (var itemId in group.ItemIds)
            {
                if (_stockRequested.Add(itemId))
                {
                    toRequest.Add(itemId);
                }
            }
        }

        if (toRequest.Count > 0)
        {
            _ = _holdings.PrefetchAsync(toRequest, CancellationToken.None);
        }
    }

    /// <summary>
    /// The owned count: the higher of the server's number (it alone covers retainers) and the live
    /// in-game one (it alone covers anything the sync does not carry, such as a currency). A server
    /// <c>0</c> means "no record", never "you own none" — see <see cref="SourcingView"/>.
    /// </summary>
    private int Owned(int itemId)
    {
        var live = _world.OwnedCount((uint)itemId);
        return _holdings.TryGet(itemId, out var stored) ? Math.Max(stored, live) : live;
    }

    private string GroupLabel(string kind) => kind switch
    {
        "material" => T(LocKeys.AdvisorStockMaterial),
        "stone" => T(LocKeys.AdvisorStockStone),
        "book" => T(LocKeys.AdvisorStockBook),
        _ => kind,
    };

    // --- Shared helpers -------------------------------------------------------------------------

    /// <summary>A hoverable item line: left-click links it in chat, right-click offers name/map actions.</summary>
    private void ClickableItem(Vector4 color, string text, int itemId, string id, int equippedItemId = 0)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            if (ImGui.Selectable(text + id))
            {
                _linkItem(itemId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            DrawItemTooltip(itemId, equippedItemId);
        }

        ItemContextMenu(itemId, id);
        WarmSourcing(itemId);
    }

    /// <summary>
    /// An item icon that answers for itself on hover — what the piece is, whether you have it and how
    /// to get it. The icons are what the eye lands on in a grid, so they carry the same detail the
    /// text line does rather than being decoration.
    /// </summary>
    private void DrawItemIcon(int itemId, float size, int equippedItemId = 0)
    {
        DrawIcon(itemId, size);
        if (itemId <= 0)
        {
            return;
        }

        if (ImGui.IsItemHovered())
        {
            DrawItemTooltip(itemId, equippedItemId);
        }

        // The same piece can appear several times in one frame (grid, step list, stock), so the popup
        // id counts rather than deriving from the item — two popups sharing an id fight over opening.
        ItemContextMenu(itemId, $"##icon{_iconSeq++}");
        WarmSourcing(itemId);
    }

    /// <summary>
    /// The full picture of one item: what it is, how it stands against what you wear, where it comes
    /// from, and — for a material — which bag or retainer it is sitting in. Deliberately structured
    /// (heading, status, routes, location) rather than one dense block, because this is read mid-raid.
    /// </summary>
    private void DrawItemTooltip(int itemId, int equippedItemId)
    {
        ImGui.BeginTooltip();

        // A tooltip is its own window and does not inherit the advisor's scale, so set it again.
        ImGui.SetWindowFontScale(Math.Clamp(_config.AdvisorTextScale, 1f, 1.6f));
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);

        _obtain.TryGet(itemId, out var info);

        ImGui.TextColored(Accent, _gearSource.GetItemName(itemId));

        var ilvl = _gearSource.GetItemLevel(itemId);
        var source = info?.Source;
        var subtitle = ilvl > 0 ? $"iLvl {ilvl}" : string.Empty;
        if (!string.IsNullOrEmpty(source))
        {
            subtitle = subtitle.Length > 0 ? $"{subtitle} · {SourceLabel(source)}" : SourceLabel(source);
        }

        if (subtitle.Length > 0)
        {
            ImGui.TextColored(Muted, subtitle);
        }

        DrawOwnershipLine(itemId, equippedItemId);

        if (equippedItemId > 0 && equippedItemId != itemId)
        {
            ImGui.TextColored(Muted, _localizer.Get(
                LocKeys.BisYouHave,
                $"{_gearSource.GetItemName(equippedItemId)} · iLvl {_gearSource.GetItemLevel(equippedItemId)}"));
        }

        if (info?.Routes is { Count: > 0 } routes)
        {
            ImGui.Separator();
            _sourcing.DrawBody(info.Source, routes, equippedItemId);
        }
        else
        {
            ImGui.TextDisabled(T(LocKeys.SourceNoInfo));
        }

        ImGui.Spacing();
        DrawWhereItSits(itemId);
        ImGui.TextDisabled(T(LocKeys.BisItemHint));

        ImGui.PopTextWrapPos();
        ImGui.SetWindowFontScale(1f);
        ImGui.EndTooltip();
    }

    /// <summary>Whether the piece is worn, merely owned, or still missing — the first thing to know.</summary>
    private void DrawOwnershipLine(int itemId, int equippedItemId)
    {
        if (equippedItemId == itemId)
        {
            ImGui.TextColored(Green, T(LocKeys.AdvisorPlanWorn));
            return;
        }

        var owned = Owned(itemId);
        if (owned > 0)
        {
            ImGui.TextColored(Blue, owned > 1 ? $"{T(LocKeys.AdvisorPlanOwned)} ({owned}×)" : T(LocKeys.AdvisorPlanOwned));
            return;
        }

        ImGui.TextColored(Orange, T(LocKeys.AdvisorPlanMissing));
    }

    private void ItemContextMenu(int itemId, string id)
    {
        if (!ImGui.BeginPopupContextItem(id))
        {
            return;
        }

        if (ImGui.Selectable(T(LocKeys.BisCopyName)))
        {
            ImGui.SetClipboardText(_gearSource.GetItemName(itemId));
        }

        if (_obtain.TryGet(itemId, out var info))
        {
            _sourcing.DrawMapMenuItem(info?.Routes);
        }

        ImGui.EndPopup();
    }

    /// <summary>Warms the sourcing for an item once, so the second hover is instant.</summary>
    private void WarmSourcing(int itemId)
    {
        if (itemId > 0 && !_obtain.TryGet(itemId, out _))
        {
            _ = _obtain.PrefetchAsync([itemId], CancellationToken.None);
        }
    }

    /// <summary>
    /// Where an item's count actually sits, per storage and per retainer — the question that follows
    /// "do I have it" when you are about to go and fetch it. Only the server can answer it, because a
    /// retainer's stock is invisible unless its window is open.
    /// </summary>
    private void DrawWhereItSits(int itemId)
    {
        if (!_holdings.TryGetBreakdown(itemId, out var stacks))
        {
            return;
        }

        ImGui.TextColored(Accent, T(LocKeys.HoldingWhere));
        foreach (var stack in stacks)
        {
            ImGui.TextColored(Muted, $"    {stack.Qty}× {StackLocation(stack)}");
        }

        ImGui.Spacing();
    }

    /// <summary>A stack's location: the retainer's name when known, else the storage it sits in.</summary>
    private string StackLocation(HoldingStack stack)
    {
        if (stack.Container == InventoryContainers.Retainer)
        {
            return string.IsNullOrEmpty(stack.SourceName)
                ? T(LocKeys.HoldingRetainer)
                : $"{T(LocKeys.HoldingRetainer)} {stack.SourceName}";
        }

        return stack.Container switch
        {
            InventoryContainers.Bags => T(LocKeys.HoldingBags),
            InventoryContainers.Saddlebag => T(LocKeys.HoldingSaddlebag),
            InventoryContainers.Armoury => T(LocKeys.HoldingArmoury),
            InventoryContainers.Equipped => T(LocKeys.HoldingEquipped),
            InventoryContainers.Glamour => T(LocKeys.HoldingGlamour),
            InventoryContainers.Armoire => T(LocKeys.HoldingArmoire),
            "manual" => T(LocKeys.HoldingManual),
            _ => stack.Container ?? string.Empty,
        };
    }

    private string SourceLabel(string source)
    {
        var key = SourceNames.LocKey(source);
        return key is not null ? _localizer.Get(key) : source;
    }

    /// <summary>Orders slots like the character screen rather than alphabetically.</summary>
    private static IEnumerable<string> SlotOrder(IEnumerable<string> slots)
    {
        string[] order =
        [
            "Weapon", "OffHand", "Head", "Body", "Hands", "Legs", "Feet",
            "Ears", "Neck", "Wrists", "RingLeft", "RingRight",
        ];

        var present = slots.ToHashSet(StringComparer.Ordinal);
        foreach (var slot in order)
        {
            if (present.Remove(slot))
            {
                yield return slot;
            }
        }

        // Anything the contract gains later still shows, just at the end.
        foreach (var rest in present.OrderBy(s => s, StringComparer.Ordinal))
        {
            yield return rest;
        }
    }

    private static long? ParseId(string? value) =>
        long.TryParse(value, out var id) && id > 0 ? id : null;

    /// <summary>Shortens an ISO timestamp to a plain date; falls back to the raw text.</summary>
    private static string ShortDate(string isoTimestamp) =>
        DateTimeOffset.TryParse(isoTimestamp, out var parsed) ? parsed.ToLocalTime().ToString("d") : isoTimestamp;

    private static void Wrapped(Vector4 color, string text)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
    }

    private void DrawIcon(int itemId, float size)
    {
        var iconId = itemId > 0 ? _gearSource.GetItemIconId(itemId) : 0;
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
    }
}
