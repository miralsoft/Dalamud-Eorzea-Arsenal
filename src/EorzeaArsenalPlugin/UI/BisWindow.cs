using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The "gear vs BiS" window: per-slot comparison with item icons + names, item level, source and
/// the wrong/missing materia, with scope (current gearset / all) and filter (all / incomplete /
/// materia issues). Holds no domain logic (R11); reads cached comparisons from
/// <see cref="BisService"/> and resolves names/icons via <see cref="GameGearSource"/>.
/// </summary>
public sealed class BisWindow : Window
{
    private const float IconSize = 34f;
    private const float TileSize = 48f;

    // The character-screen layout, row by row: each row has a left and a right slot at the same
    // height. Top row is weapon + off-hand; then five armour rows (left) and accessory rows (right).
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
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Orange = new(0.96f, 0.62f, 0.22f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly BisService _bis;
    private readonly GameGearSource _gearSource;
    private readonly ITextureProvider _textures;
    private readonly Action _save;
    private readonly Action<int> _linkItem;

    /// <summary>Creates the BiS window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="bis">The shared BiS service (cache + comparison).</param>
    /// <param name="gearSource">Resolves item names, item levels and icons.</param>
    /// <param name="textures">Loads game icons.</param>
    /// <param name="save">Persists the config (filter/scope choices).</param>
    /// <param name="linkItem">Posts a clickable item link to the game chat (arg: item id).</param>
    public BisWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        BisService bis,
        GameGearSource gearSource,
        ITextureProvider textures,
        Action save,
        Action<int> linkItem)
        : base("Eorzea Arsenal###EorzeaArsenalBis")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _bis = bis;
        _gearSource = gearSource;
        _textures = textures;
        _save = save;
        _linkItem = linkItem;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(1000, 1600),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void OnOpen()
    {
        // Load fresh BiS data when the window is opened, so the user always sees current values.
        if (_config.Enabled && _store.HasKey && !_bis.IsLoading && _bis.IsStale(TimeSpan.FromSeconds(30)))
        {
            _ = Task.Run(() => _bis.RefreshAsync(CancellationToken.None));
        }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        DrawToolbar();

        var statusMessage = StatusMessage();
        if (statusMessage is not null)
        {
            ImGui.TextWrapped(statusMessage);
        }

        ImGui.Separator();

        var currentIndex = _gearSource.GetCurrentGearsetIndex();
        var scoped = _bis.Comparisons.Where(c => _config.BisShowAllSets || c.GearIndex == currentIndex);

        if (_config.BisShoppingList)
        {
            DrawShoppingList(scoped);
            return;
        }

        if (_config.BisGridView)
        {
            DrawGrids(scoped);
            return;
        }

        var shownAny = false;
        foreach (var comparison in scoped)
        {
            var slots = comparison.Slots.Where(Included).ToList();
            if (slots.Count == 0)
            {
                continue;
            }

            DrawGearset(comparison, slots);
            shownAny = true;
        }

        if (!shownAny && _bis.Comparisons.Count > 0)
        {
            ImGui.TextDisabled(T(LocKeys.BisNothingShown));
        }
    }

    private void DrawToolbar()
    {
        using (ImRaii.Disabled(_bis.IsLoading || !_store.HasKey || !_config.Enabled))
        {
            if (ImGui.Button(T(LocKeys.BisRefresh)))
            {
                _ = Task.Run(() => _bis.RefreshAsync(CancellationToken.None));
            }
        }

        if (_bis.IsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(T(LocKeys.BisLoading));
        }

        if (ImGui.RadioButton(T(LocKeys.BisScopeCurrent), !_config.BisShowAllSets))
        {
            _config.BisShowAllSets = false;
            _save();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(T(LocKeys.BisScopeAll), _config.BisShowAllSets))
        {
            _config.BisShowAllSets = true;
            _save();
        }

        ImGui.SameLine();
        var shopping = _config.BisShoppingList;
        if (ImGui.Checkbox(T(LocKeys.BisShoppingList), ref shopping))
        {
            _config.BisShoppingList = shopping;
            if (shopping)
            {
                _config.BisGridView = false;
            }

            _save();
        }

        ImGui.SameLine();
        var grid = _config.BisGridView;
        if (ImGui.Checkbox(T(LocKeys.BisGridView), ref grid))
        {
            _config.BisGridView = grid;
            if (grid)
            {
                _config.BisShoppingList = false;
            }

            _save();
        }

        // The per-slot filter only applies to the per-set list, not the shopping list or the grid.
        if (_config.BisShoppingList || _config.BisGridView)
        {
            return;
        }

        var filter = Math.Clamp(_config.BisFilter, 0, 2);
        ReadOnlySpan<string> filters = [T(LocKeys.BisFilterAll), T(LocKeys.BisFilterIncomplete), T(LocKeys.BisFilterMateria)];
        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo(T(LocKeys.BisFilterLabel), ref filter, filters, filters.Length))
        {
            _config.BisFilter = filter;
            _save();
        }
    }

    private bool Included(SlotComparison slot)
    {
        var complete = slot is { Status: SlotMatch.Match, MissingMateria.Count: 0, ExtraMateria.Count: 0 };
        var materiaIssue = slot.Status == SlotMatch.Match && !complete;
        return _config.BisFilter switch
        {
            1 => !complete,
            2 => materiaIssue,
            _ => true,
        };
    }

    /// <summary>
    /// The traffic-light status colour for a slot: green = fully BiS, <b>orange = item is correct but
    /// the materia is off</b>, <b>red = the item itself is wrong or the slot is empty</b>. This lets
    /// the user tell "just needs materia" apart from "wrong gear piece" at a glance.
    /// </summary>
    private Vector4 StatusColor(SlotComparison slot)
    {
        if (slot is { Status: SlotMatch.Match, MissingMateria.Count: 0, ExtraMateria.Count: 0 })
        {
            return Green;
        }

        return slot.Status == SlotMatch.Match ? Orange : Red;
    }

    private void DrawSetHeader(GearsetComparison comparison)
    {
        var name = string.IsNullOrEmpty(comparison.Name) ? string.Empty : $" — {comparison.Name}";
        ImGui.TextColored(Accent, $"#{comparison.GearIndex} {comparison.Job}{name}");
        ImGui.SameLine();

        var total = comparison.Slots.Count;
        var matched = comparison.FullyMatchedSlots;
        var fraction = total == 0 ? 1f : (float)matched / total;
        var overlay = comparison.IsComplete
            ? T(LocKeys.BisComplete)
            : _localizer.Get(LocKeys.BisSummary, matched, total);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, comparison.IsComplete ? Green : Accent);
        ImGui.ProgressBar(fraction, new Vector2(180f, 0f), overlay);
        ImGui.PopStyleColor();
    }

    private void DrawGearset(GearsetComparison comparison, List<SlotComparison> slots)
    {
        DrawSetHeader(comparison);

        var target = _bis.TargetGearset(comparison.GearIndex);
        foreach (var slot in slots)
        {
            DrawSlot(comparison.GearIndex, slot, target?.Items.GetValueOrDefault(slot.Slot)?.Source ?? target?.Source);
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    /// <summary>
    /// Aggregates, across the shown sets, the BiS items you don't yet own (not equipped and not in
    /// your inventory/armoury) and the missing materia you don't own — a single de-duplicated
    /// "shopping list". Each row keeps the link/copy actions.
    /// </summary>
    private void DrawShoppingList(IEnumerable<GearsetComparison> scoped)
    {
        var items = new Dictionary<int, (string? Source, SortedSet<string> Jobs)>();
        var materia = new Dictionary<int, int>();

        foreach (var comparison in scoped)
        {
            var target = _bis.TargetGearset(comparison.GearIndex);
            foreach (var slot in comparison.Slots)
            {
                if (slot.TargetItemId > 0 && slot.Status != SlotMatch.Match && !_gearSource.OwnsItem(slot.TargetItemId))
                {
                    if (!items.TryGetValue(slot.TargetItemId, out var entry))
                    {
                        var source = target?.Items.GetValueOrDefault(slot.Slot)?.Source ?? target?.Source;
                        entry = (source, new SortedSet<string>(StringComparer.Ordinal));
                        items[slot.TargetItemId] = entry;
                    }

                    entry.Jobs.Add(comparison.Job);
                }

                foreach (var materiaId in slot.MissingMateria)
                {
                    if (!_gearSource.OwnsItem(materiaId))
                    {
                        materia[materiaId] = materia.GetValueOrDefault(materiaId) + 1;
                    }
                }
            }
        }

        if (items.Count == 0 && materia.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.BisShoppingEmpty));
            return;
        }

        // Group the items by the set of jobs that need them, so a long all-sets list is structured by
        // class — shared pieces collapse under a combined header (e.g. "PLD · WAR · DRK · GNB"), the
        // rest under their single class. Multi-class groups first, then alphabetical.
        var groups = items
            .GroupBy(kv => string.Join(" · ", kv.Value.Jobs))
            .OrderByDescending(g => g.First().Value.Jobs.Count)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            ImGui.TextColored(Accent, group.Key);
            foreach (var kv in group.OrderByDescending(kv => _gearSource.GetItemLevel(kv.Key)))
            {
                DrawShoppingItem(kv.Key, kv.Value.Source);
            }

            ImGui.Spacing();
        }

        if (materia.Count > 0)
        {
            ImGui.TextColored(Accent, T(LocKeys.BisShoppingMateria));
            foreach (var (materiaId, count) in materia.OrderByDescending(kv => kv.Value))
            {
                DrawShoppingItem(materiaId, null, count);
            }
        }
    }

    private void DrawShoppingItem(int itemId, string? source, int count = 0)
    {
        DrawIcon(itemId, IconSize);
        ImGui.SameLine();

        // Materia rows (count > 0) show a quantity; gear rows show the item level + source.
        var detail = count > 0
            ? $" ×{count}"
            : $" · iLvl {_gearSource.GetItemLevel(itemId)}{(string.IsNullOrEmpty(source) ? string.Empty : $" · {SourceLabel(source)}")}";
        var line = $"{_gearSource.GetItemName(itemId)}{detail}";
        ClickableItem(Muted, line, itemId, $"##shop{itemId}");
    }

    /// <summary>Renders each shown gearset as a character-screen-style two-column icon grid.</summary>
    private void DrawGrids(IEnumerable<GearsetComparison> scoped)
    {
        var shownAny = false;
        foreach (var comparison in scoped)
        {
            DrawGrid(comparison);
            shownAny = true;
        }

        if (!shownAny && _bis.Comparisons.Count > 0)
        {
            ImGui.TextDisabled(T(LocKeys.BisNothingShown));
        }
    }

    private void DrawGrid(GearsetComparison comparison)
    {
        DrawSetHeader(comparison);

        var bySlot = new Dictionary<string, SlotComparison>(StringComparer.Ordinal);
        foreach (var slot in comparison.Slots)
        {
            bySlot[slot.Slot] = slot;
        }

        var target = _bis.TargetGearset(comparison.GearIndex);

        // Four columns per row: [left icon][left name + materia]   [right icon][right name + materia].
        // The two detail columns stretch, which gives the room in the middle/right for the text.
        if (ImGui.BeginTable($"##grid{comparison.GearIndex}", 4, ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableSetupColumn("li", ImGuiTableColumnFlags.WidthFixed, TileSize);
            ImGui.TableSetupColumn("ld", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("ri", ImGuiTableColumnFlags.WidthFixed, TileSize);
            ImGui.TableSetupColumn("rd", ImGuiTableColumnFlags.WidthStretch);

            foreach (var (left, right) in GridRows)
            {
                ImGui.TableNextRow();
                DrawSlotCells(comparison.GearIndex, left, bySlot, target);
                DrawSlotCells(comparison.GearIndex, right, bySlot, target);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    /// <summary>Renders one slot as two table cells: the bordered icon tile and the name + materia detail.</summary>
    private void DrawSlotCells(int gearIndex, string slotKey, Dictionary<string, SlotComparison> bySlot, BisGearset? target)
    {
        var found = bySlot.TryGetValue(slotKey, out var item);
        var source = found ? target?.Items.GetValueOrDefault(item.Slot)?.Source ?? target?.Source : null;

        ImGui.TableNextColumn();
        DrawTile(gearIndex, slotKey, found ? item : null, source);

        ImGui.TableNextColumn();
        if (found)
        {
            DrawTileDetail(item);
        }
    }

    private void DrawTile(int gearIndex, string slotKey, SlotComparison? slot, string? source)
    {
        var size = new Vector2(TileSize, TileSize);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (slot is not { } item)
        {
            // Absent slot (e.g. no off-hand for this job): faint placeholder so the rows line up.
            drawList.AddRect(origin, origin + size, ImGui.GetColorU32(Muted with { W = 0.16f }), 4f, ImDrawFlags.RoundCornersAll, 1f);
            ImGui.Dummy(size);
            return;
        }

        DrawIcon(item.TargetItemId, TileSize);
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton($"##tile{gearIndex}_{slotKey}", size);

        var color = StatusColor(item);
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(color), 4f, ImDrawFlags.RoundCornersAll, 2.5f);

        if (ImGui.IsItemHovered())
        {
            DrawTileTooltip(item, source);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _linkItem(item.TargetItemId);
        }

        if (ImGui.BeginPopupContextItem($"##tile{gearIndex}_{slotKey}"))
        {
            if (ImGui.Selectable(T(LocKeys.BisCopyName)))
            {
                ImGui.SetClipboardText(_gearSource.GetItemName(item.TargetItemId));
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>The detail next to a grid tile: the item name plus the materia still to socket (icons).</summary>
    private void DrawTileDetail(SlotComparison item)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, StatusColor(item));
        ImGui.TextWrapped(_gearSource.GetItemName(item.TargetItemId));
        ImGui.PopStyleColor();

        // The materia still to socket, as small icons (name on hover). The full wrong/missing
        // breakdown stays in the tile's hover tooltip to keep the grid uncluttered.
        var materia = item.MissingMateria;
        if (materia.Count == 0)
        {
            return;
        }

        var iconSize = ImGui.GetTextLineHeight() * 1.3f;
        for (var i = 0; i < materia.Count; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, 3f);
            }

            DrawIcon(materia[i], iconSize);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_gearSource.GetItemName(materia[i]));
            }
        }
    }

    private void DrawTileTooltip(SlotComparison slot, string? source)
    {
        ImGui.BeginTooltip();

        var color = StatusColor(slot);
        var slotName = _localizer.Get(SlotNames.LocKey(slot.Slot));
        var sourceSuffix = string.IsNullOrEmpty(source) ? string.Empty : $" · {SourceLabel(source)}";
        ImGui.TextColored(color, $"{slotName}: {_gearSource.GetItemName(slot.TargetItemId)} · iLvl {_gearSource.GetItemLevel(slot.TargetItemId)}{sourceSuffix}");

        if (slot.Status == SlotMatch.ItemDiffers && slot.CurrentItemId is { } currentId && currentId > 0)
        {
            ImGui.TextColored(Muted, _localizer.Get(LocKeys.BisYouHave, $"{_gearSource.GetItemName(currentId)} · iLvl {_gearSource.GetItemLevel(currentId)}"));
        }

        if (slot.ExtraMateria.Count > 0)
        {
            ImGui.TextColored(Red, _localizer.Get(LocKeys.BisMateriaWrong, string.Join(", ", slot.ExtraMateria.Select(_gearSource.GetItemName))));
        }

        if (slot.MissingMateria.Count > 0)
        {
            var key = slot.Status == SlotMatch.Match ? LocKeys.BisMateriaMissing : LocKeys.BisMateriaList;
            ImGui.TextColored(slot.Status == SlotMatch.Match ? Orange : Muted, _localizer.Get(key, string.Join(", ", slot.MissingMateria.Select(_gearSource.GetItemName))));
        }

        ImGui.Spacing();
        ImGui.TextDisabled(T(LocKeys.BisItemHint));
        ImGui.EndTooltip();
    }

    private void DrawSlot(int gearIndex, SlotComparison slot, string? source)
    {
        DrawIcon(slot.TargetItemId, IconSize);
        ImGui.SameLine();
        ImGui.BeginGroup();

        var color = StatusColor(slot);
        var slotName = _localizer.Get(SlotNames.LocKey(slot.Slot));
        var sourceSuffix = string.IsNullOrEmpty(source) ? string.Empty : $" · {SourceLabel(source)}";
        var line = $"{slotName}: {_gearSource.GetItemName(slot.TargetItemId)} · iLvl {_gearSource.GetItemLevel(slot.TargetItemId)}{sourceSuffix}";
        ClickableItem(color, line, slot.TargetItemId, $"##slot{gearIndex}_{slot.Slot}");

        if (slot.Status == SlotMatch.ItemDiffers && slot.CurrentItemId is { } currentId && currentId > 0)
        {
            Wrapped(Muted, $"    {_localizer.Get(LocKeys.BisYouHave, $"{_gearSource.GetItemName(currentId)} · iLvl {_gearSource.GetItemLevel(currentId)}")}");
        }

        if (slot.ExtraMateria.Count > 0)
        {
            DrawMateria(_localizer.Get(LocKeys.BisMateriaWrong, string.Empty), slot.ExtraMateria, Red);
        }

        if (slot.MissingMateria.Count > 0)
        {
            var key = slot.Status == SlotMatch.Match ? LocKeys.BisMateriaMissing : LocKeys.BisMateriaList;
            DrawMateria(_localizer.Get(key, string.Empty), slot.MissingMateria, slot.Status == SlotMatch.Match ? Orange : Muted);
        }

        ImGui.EndGroup();
    }

    /// <summary>
    /// Renders the slot's main line as a hoverable item: left-click prints a clickable item link to
    /// the local chat log (preview), right-click copies the item name to the clipboard so the user
    /// can paste it anywhere (FC/party chat, Discord, the marketboard search).
    /// </summary>
    private void ClickableItem(Vector4 color, string text, int itemId, string id)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var clicked = ImGui.Selectable(text + id);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(T(LocKeys.BisItemHint));
        }

        if (clicked)
        {
            _linkItem(itemId);
        }

        if (ImGui.BeginPopupContextItem(id))
        {
            if (ImGui.Selectable(T(LocKeys.BisCopyName)))
            {
                ImGui.SetClipboardText(_gearSource.GetItemName(itemId));
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>Renders a materia label followed by each materia's icon + name inline.</summary>
    private void DrawMateria(string label, IReadOnlyList<int> materiaItemIds, Vector4 color)
    {
        var iconSize = ImGui.GetTextLineHeight();
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted($"    {label.TrimEnd()}");
        ImGui.PopStyleColor();

        foreach (var id in materiaItemIds)
        {
            ImGui.SameLine();
            DrawIcon(id, iconSize);
            ImGui.SameLine(0f, 3f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(_gearSource.GetItemName(id));
            ImGui.PopStyleColor();
        }
    }

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

    private string SourceLabel(string source)
    {
        var key = SourceNames.LocKey(source);
        return key is not null ? _localizer.Get(key) : char.ToUpperInvariant(source[0]) + source[1..];
    }

    private static void Wrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private string? StatusMessage() => _bis.Status switch
    {
        BisFetchStatus.NotConnected => T(LocKeys.PushNotConnected),
        BisFetchStatus.NotLoggedIn => T(LocKeys.PushNotLoggedIn),
        BisFetchStatus.Forbidden => T(LocKeys.BisReconnect),
        BisFetchStatus.Empty or BisFetchStatus.NotFound => T(LocKeys.BisNone),
        BisFetchStatus.Error => PushReportFormatter.ErrorMessage(_bis.LastErrorKind, _localizer),
        _ => null,
    };
}
