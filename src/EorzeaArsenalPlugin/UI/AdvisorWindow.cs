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
    private const float IconSize = 30f;
    private static readonly string[] Sorts = ["power", "value", "cheap"];

    private static readonly Vector4 Accent = new(0.62f, 0.82f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.78f, 0.80f, 0.85f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Orange = new(0.96f, 0.62f, 0.22f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);

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

    // The gearset the user is looking at; -1 means "the set the character currently has on".
    private int _selectedGearIndex = -1;
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

        if (_selectedGearIndex < 0 || sets.All(s => s.GearIndex != _selectedGearIndex))
        {
            var current = _gearSource.GetCurrentGearsetIndex();
            _selectedGearIndex = sets.Any(s => s.GearIndex == current) ? current : sets[0].GearIndex;
        }

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
        ImGui.TextColored(Accent, T(LocKeys.AdvisorViewRecommendation));
        ImGui.SameLine();
        ImGui.TextDisabled($"· {_localizer.Get(LocKeys.AdvisorBalance, options.TomeBalance, options.WeeklyCap)}");

        var steps = options.Steps ?? [];
        if (steps.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Green, T(LocKeys.AdvisorNothingToDo));
        }

        for (var i = 0; i < steps.Count; i++)
        {
            DrawStep(i + 1, steps[i]);
        }

        DrawNeeds(options.Materials);
    }

    private void DrawStep(int rank, AdvisorStep step)
    {
        var itemId = (int)(step.To ?? 0);
        if (itemId <= 0 || string.IsNullOrEmpty(step.Slot))
        {
            return;
        }

        ImGui.Spacing();
        DrawIcon(itemId, IconSize);
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
            DrawIcon((int)need.Id, IconSize);
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

        DrawIcon((int)chosen, IconSize);
        ImGui.SameLine();

        var labels = ids.Select(id => OptionLabel(id, entry)).ToArray();
        var index = Math.Max(0, ids.IndexOf(chosen));
        ImGui.SetNextItemWidth(430f);
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

        DrawIcon(itemId, IconSize);
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
            ImGui.BeginTooltip();
            if (_obtain.TryGet(itemId, out var info) && info?.Routes is { Count: > 0 } routes)
            {
                _sourcing.DrawBody(info.Source, routes, equippedItemId);
                ImGui.Spacing();
            }

            DrawWhereItSits(itemId);
            ImGui.TextDisabled(T(LocKeys.BisItemHint));
            ImGui.EndTooltip();
        }

        if (ImGui.BeginPopupContextItem(id))
        {
            if (ImGui.Selectable(T(LocKeys.BisCopyName)))
            {
                ImGui.SetClipboardText(_gearSource.GetItemName(itemId));
            }

            if (_obtain.TryGet(itemId, out var ctxInfo))
            {
                _sourcing.DrawMapMenuItem(ctxInfo?.Routes);
            }

            ImGui.EndPopup();
        }

        if (!_obtain.TryGet(itemId, out _))
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
