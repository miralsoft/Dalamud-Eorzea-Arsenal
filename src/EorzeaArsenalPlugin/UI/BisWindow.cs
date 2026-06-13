using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
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

    private static readonly Vector4 Accent = new(0.62f, 0.82f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.78f, 0.80f, 0.85f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly BisService _bis;
    private readonly GameGearSource _gearSource;
    private readonly ITextureProvider _textures;
    private readonly Action _save;

    /// <summary>Creates the BiS window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="bis">The shared BiS service (cache + comparison).</param>
    /// <param name="gearSource">Resolves item names, item levels and icons.</param>
    /// <param name="textures">Loads game icons.</param>
    /// <param name="save">Persists the config (filter/scope choices).</param>
    public BisWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        BisService bis,
        GameGearSource gearSource,
        ITextureProvider textures,
        Action save)
        : base("Eorzea Arsenal###EorzeaArsenalBis")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _bis = bis;
        _gearSource = gearSource;
        _textures = textures;
        _save = save;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(1000, 1600),
        };
    }

    private string T(string key) => _localizer.Get(key);

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
        var shownAny = false;
        foreach (var comparison in _bis.Comparisons)
        {
            if (!_config.BisShowAllSets && comparison.GearIndex != currentIndex)
            {
                continue;
            }

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

    private void DrawGearset(GearsetComparison comparison, List<SlotComparison> slots)
    {
        var name = string.IsNullOrEmpty(comparison.Name) ? string.Empty : $" — {comparison.Name}";
        ImGui.TextColored(Accent, $"#{comparison.GearIndex} {comparison.Job}{name}");
        ImGui.SameLine();
        ImGui.TextDisabled(comparison.IsComplete
            ? $"({T(LocKeys.BisComplete)})"
            : $"({_localizer.Get(LocKeys.BisSummary, comparison.FullyMatchedSlots, comparison.Slots.Count)})");

        var target = _bis.TargetGearset(comparison.GearIndex);
        foreach (var slot in slots)
        {
            DrawSlot(slot, target?.Items.GetValueOrDefault(slot.Slot)?.Source ?? target?.Source);
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawSlot(SlotComparison slot, string? source)
    {
        DrawIcon(slot.TargetItemId);
        ImGui.SameLine();
        ImGui.BeginGroup();

        var complete = slot is { Status: SlotMatch.Match, MissingMateria.Count: 0, ExtraMateria.Count: 0 };
        var color = complete ? Green : slot.Status == SlotMatch.MissingCurrent ? Red : Yellow;
        var slotName = _localizer.Get(SlotNames.LocKey(slot.Slot));
        var sourceSuffix = string.IsNullOrEmpty(source) ? string.Empty : $" · {SourceLabel(source)}";
        Wrapped(color, $"{slotName}: {_gearSource.GetItemName(slot.TargetItemId)} · iLvl {_gearSource.GetItemLevel(slot.TargetItemId)}{sourceSuffix}");

        if (slot.Status == SlotMatch.ItemDiffers && slot.CurrentItemId is { } currentId && currentId > 0)
        {
            Wrapped(Muted, $"    {_localizer.Get(LocKeys.BisYouHave, $"{_gearSource.GetItemName(currentId)} · iLvl {_gearSource.GetItemLevel(currentId)}")}");
        }

        if (slot.ExtraMateria.Count > 0)
        {
            Wrapped(Red, $"    {_localizer.Get(LocKeys.BisMateriaWrong, Names(slot.ExtraMateria))}");
        }

        if (slot.MissingMateria.Count > 0)
        {
            var key = slot.Status == SlotMatch.Match ? LocKeys.BisMateriaMissing : LocKeys.BisMateriaList;
            Wrapped(slot.Status == SlotMatch.Match ? Yellow : Muted, $"    {_localizer.Get(key, Names(slot.MissingMateria))}");
        }

        ImGui.EndGroup();
    }

    private void DrawIcon(int itemId)
    {
        var iconId = _gearSource.GetItemIconId(itemId);
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(IconSize, IconSize));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(IconSize, IconSize));
    }

    private string Names(IReadOnlyList<int> itemIds) => string.Join(", ", itemIds.Select(_gearSource.GetItemName));

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
