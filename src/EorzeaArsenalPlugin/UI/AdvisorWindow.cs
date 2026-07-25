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
/// Two independent parts. <b>Your saved plan</b> renders what the player built in the web advisor,
/// read per (character, job, target set) — the plan is only addressable once the server sends a set's
/// <see cref="BisGearset.Target"/>, so the section explains itself until then. <b>Your stock</b> shows
/// the active tier's tracked materials/stones/books with the <i>server's</i> owned counts, which is the
/// only number that includes retainers; the live game count is a fallback until it lands. Holds no
/// domain logic (R11).
/// </remarks>
public sealed class AdvisorWindow : Window
{
    private const float IconSize = 30f;

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

    // Stock ids we have already asked the holdings service for, so Draw fires one prefetch per set of
    // ids rather than a task every frame.
    private readonly HashSet<long> _stockRequested = [];

    /// <summary>Creates the purchase-advisor window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store (gates every read).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="bis">Supplies the sets, their targets and the current-gear comparison.</param>
    /// <param name="advisor">Reads the saved purchase plans.</param>
    /// <param name="tracked">Supplies the active tier's stock groups.</param>
    /// <param name="holdings">Server-side owned counts (retainers included).</param>
    /// <param name="obtain">Impersonal "how to get it" sourcing for the planned pieces.</param>
    /// <param name="gearSource">Resolves item names, item levels and icons.</param>
    /// <param name="world">Game actions (live owned count, open the map at a vendor).</param>
    /// <param name="textures">Loads game icons.</param>
    /// <param name="resolveCharacterId">Maps a set's <c>cid_hash</c> to the server's numeric character id.</param>
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
            MinimumSize = new Vector2(480, 380),
            MaximumSize = new Vector2(1000, 1600),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void OnOpen()
    {
        // The sets (and their targets) come from the BiS read — refresh it so a freshly pinned set and
        // its plan are visible without a detour through the BiS window.
        if (_config.Enabled && _store.HasKey && !_bis.IsLoading && _bis.IsStale(TimeSpan.FromSeconds(30)))
        {
            _ = Task.Run(() => _bis.RefreshAsync(CancellationToken.None));
        }
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
        ImGui.Separator();

        DrawPlanSection(comparison);

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

    private static string SetLabel(GearsetComparison set) =>
        string.IsNullOrEmpty(set.Name) ? $"#{set.GearIndex} {set.Job}" : $"#{set.GearIndex} {set.Job} — {set.Name}";

    /// <summary>Renders the saved plan for the selected set, or the reason there is nothing to render.</summary>
    private void DrawPlanSection(GearsetComparison? comparison)
    {
        ImGui.TextColored(Accent, T(LocKeys.AdvisorPlanHeading));

        if (comparison is null)
        {
            return;
        }

        var target = _bis.TargetGearset(comparison.GearIndex);

        // A plan is stored under the set's web identity. Without it there is no key to look up — an
        // older server simply does not send one, which is a missing feature, not an error.
        if (string.IsNullOrEmpty(target?.Target))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Muted))
            {
                ImGui.TextWrapped(T(LocKeys.AdvisorNoTarget));
            }

            return;
        }

        if (_resolveCharacterId(target.CidHash) is not { } characterId)
        {
            // The numeric id is only learned from a push; until then the plan is not addressable.
            using (ImRaii.PushColor(ImGuiCol.Text, Muted))
            {
                ImGui.TextWrapped(T(LocKeys.AdvisorNoTarget));
            }

            return;
        }

        if (!_advisor.TryGet(characterId, comparison.Job, target.Target, out var plan))
        {
            _ = _advisor.EnsureAsync(characterId, comparison.Job, target.Target, CancellationToken.None);
            ImGui.TextDisabled(_advisor.LastErrorKind == ApiErrorKind.Forbidden
                ? T(LocKeys.AdvisorScopeHint)
                : T(LocKeys.AdvisorLoading));
            return;
        }

        if (plan?.Items is not { Count: > 0 } items)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Muted))
            {
                ImGui.TextWrapped(T(LocKeys.AdvisorNoPlan));
            }

            return;
        }

        DrawPlanHeader(plan, comparison, items);

        var equipped = comparison.Slots.ToDictionary(s => s.Slot, s => s.CurrentItemId ?? 0, StringComparer.Ordinal);
        foreach (var slot in SlotOrder(items.Keys))
        {
            DrawPlanRow(slot, (int)items[slot].Id, equipped.GetValueOrDefault(slot));
        }

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(T(LocKeys.AdvisorEditPending));
        }
    }

    private void DrawPlanHeader(AdvisorPlan plan, GearsetComparison comparison, Dictionary<string, AdvisorPlanItem> items)
    {
        var done = items.Count(kv => IsWorn(comparison, kv.Key, (int)kv.Value.Id));
        ImGui.TextColored(Muted, _localizer.Get(LocKeys.AdvisorPlanSummary, done, items.Count));

        if (!string.IsNullOrEmpty(plan.TargetName))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {plan.TargetName}");
        }

        if (!string.IsNullOrEmpty(plan.UpdatedAt))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {_localizer.Get(LocKeys.AdvisorPlanSaved, ShortDate(plan.UpdatedAt))}");
        }
    }

    private static bool IsWorn(GearsetComparison comparison, string slot, int plannedItemId) =>
        comparison.Slots.Any(s => string.Equals(s.Slot, slot, StringComparison.Ordinal) && s.CurrentItemId == plannedItemId);

    /// <summary>One planned slot: the piece, whether you wear/own it, and how to get it on hover.</summary>
    private void DrawPlanRow(string slot, int plannedItemId, int equippedItemId)
    {
        if (plannedItemId <= 0)
        {
            return;
        }

        DrawIcon(plannedItemId, IconSize);
        ImGui.SameLine();

        var worn = equippedItemId == plannedItemId;
        var owned = worn || Owned(plannedItemId) > 0;
        var (state, color) = worn
            ? (T(LocKeys.AdvisorPlanWorn), Green)
            : owned
                ? (T(LocKeys.AdvisorPlanOwned), Orange)
                : (T(LocKeys.AdvisorPlanMissing), Red);

        var line = $"{_sourcing.SlotName(slot)}: {_gearSource.GetItemName(plannedItemId)} · iLvl {_gearSource.GetItemLevel(plannedItemId)} · {state}";

        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            if (ImGui.Selectable($"{line}##plan{slot}"))
            {
                _linkItem(plannedItemId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            if (_obtain.TryGet(plannedItemId, out var info) && info?.Routes is { Count: > 0 } routes)
            {
                _sourcing.DrawBody(info.Source, routes, equippedItemId);
                ImGui.Spacing();
            }

            ImGui.TextDisabled(T(LocKeys.BisItemHint));
            ImGui.EndTooltip();
        }

        if (ImGui.BeginPopupContextItem($"##planctx{slot}"))
        {
            if (ImGui.Selectable(T(LocKeys.BisCopyName)))
            {
                ImGui.SetClipboardText(_gearSource.GetItemName(plannedItemId));
            }

            if (_obtain.TryGet(plannedItemId, out var ctxInfo))
            {
                _sourcing.DrawMapMenuItem(ctxInfo?.Routes);
            }

            ImGui.EndPopup();
        }

        // Warm the sourcing for the planned piece, so the hover is instant on the second pass.
        if (!_obtain.TryGet(plannedItemId, out _))
        {
            _ = _obtain.PrefetchAsync([plannedItemId], CancellationToken.None);
        }
    }

    /// <summary>
    /// The active tier's tracked consumables with the server's owned counts. The list and its grouping
    /// are server-curated, so a tier rotation carries itself without a plugin release.
    /// </summary>
    private void DrawStockSection()
    {
        ImGui.TextColored(Accent, T(LocKeys.AdvisorStockHeading));

        var groups = _tracked.Groups;
        if (groups.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.AdvisorStockEmpty));
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

        using (ImRaii.PushColor(ImGuiCol.Text, count > 0 ? Muted : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            if (ImGui.Selectable($"{count}× {_gearSource.GetItemName(itemId)}##stock{itemId}"))
            {
                _linkItem(itemId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(T(LocKeys.BisItemHint));
        }

        if (ImGui.BeginPopupContextItem($"##stockctx{itemId}"))
        {
            if (ImGui.Selectable(T(LocKeys.BisCopyName)))
            {
                ImGui.SetClipboardText(_gearSource.GetItemName(itemId));
            }

            ImGui.EndPopup();
        }
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
    /// The owned count: the server's number when it has arrived (it alone covers retainers), otherwise
    /// the live in-game count so the row is never blank while the read is in flight.
    /// </summary>
    private int Owned(int itemId) =>
        _holdings.TryGet(itemId, out var count) ? count : _world.OwnedCount((uint)itemId);

    private string GroupLabel(string kind) => kind switch
    {
        "material" => T(LocKeys.AdvisorStockMaterial),
        "stone" => T(LocKeys.AdvisorStockStone),
        "book" => T(LocKeys.AdvisorStockBook),
        _ => kind,
    };

    /// <summary>Orders a plan's slots like the character screen rather than alphabetically.</summary>
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

    /// <summary>Shortens an ISO timestamp to a plain date; falls back to the raw text.</summary>
    private static string ShortDate(string isoTimestamp) =>
        DateTimeOffset.TryParse(isoTimestamp, out var parsed) ? parsed.ToLocalTime().ToString("d") : isoTimestamp;

    private void DrawIcon(int itemId, float size)
    {
        var iconId = _gearSource.GetItemIconId(itemId);
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
    }
}
