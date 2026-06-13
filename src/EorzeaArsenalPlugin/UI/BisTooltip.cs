using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A safe, additive hover overlay. When the user hovers an equippable item, it looks up the BiS
/// target for that item's <b>slot</b> in the currently selected gearset and shows: the target
/// item name, the target materia (what to socket), whether you own the target, and how your
/// equipped piece compares. It is a styled window docked to the native item tooltip (above it, or
/// below when there is no room; on the cursor side), and never touches the native tooltip — so it
/// cannot crash the client and is patch-stable (P2/P6). Registered on the UI draw loop.
/// </summary>
public sealed class BisTooltip
{
    private const float FrameInset = 26f;
    private const float Gap = 1f;
    private const float ScreenPadding = 4f;
    private const string Indent = "    ";

    private static readonly Vector4 Accent = new(0.62f, 0.82f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.78f, 0.80f, 0.85f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Background = new(0.08f, 0.09f, 0.12f, 0.96f);

    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.AlwaysAutoResize;

    private readonly PluginConfig _config;
    private readonly Localizer _localizer;
    private readonly IGameGui _gameGui;
    private readonly BisService _bis;
    private readonly GameGearSource _gearSource;

    private Vector2 _lastSize = new(260f, 90f);
    private int _cachedItemId = -1;
    private int _cachedGearIndex = -2;
    private List<OverlayLine> _cachedLines = [];

    /// <summary>Creates the overlay.</summary>
    /// <param name="config">Live config (holds the on/off toggle).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="gameGui">Provides the hovered item id and addon bounds.</param>
    /// <param name="bis">The shared BiS cache/lookup.</param>
    /// <param name="gearSource">Provides the current gearset index, item names and ownership.</param>
    public BisTooltip(PluginConfig config, Localizer localizer, IGameGui gameGui, BisService bis, GameGearSource gearSource)
    {
        _config = config;
        _localizer = localizer;
        _gameGui = gameGui;
        _bis = bis;
        _gearSource = gearSource;
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Draws the overlay when an equippable item with a BiS target is hovered.</summary>
    public void Draw()
    {
        if (!_config.ShowBisTooltip || !_config.Enabled)
        {
            return;
        }

        var raw = _gameGui.HoveredItem;
        if (raw is 0 or > uint.MaxValue)
        {
            return;
        }

        var itemId = ItemIdNormalizer.Normalize((uint)raw);
        var gearIndex = _gearSource.GetCurrentGearsetIndex();

        // Resolving names/slots/ownership is only done when the hovered item or gearset changes.
        if (itemId != _cachedItemId || gearIndex != _cachedGearIndex)
        {
            _cachedItemId = itemId;
            _cachedGearIndex = gearIndex;
            _cachedLines = BuildLines(itemId, gearIndex);
        }

        if (_cachedLines.Count == 0 || !TryGetDockPosition(out var position))
        {
            return;
        }

        DrawWindow(position);
    }

    private List<OverlayLine> BuildLines(int itemId, int gearIndex)
    {
        var lines = new List<OverlayLine>();
        if (gearIndex < 0)
        {
            return lines;
        }

        foreach (var slot in _gearSource.GetSlotsForItem(itemId))
        {
            if (_bis.TargetForSlot(gearIndex, slot) is not { } target)
            {
                continue; // no BiS target for this slot in the current gearset
            }

            var comparison = _bis.SlotComparisonByIndex(gearIndex, slot);
            var equipped = comparison is { CurrentItemId: { } currentId } && currentId > 0
                ? _gearSource.GetItemName(currentId)
                : null;
            var materia = target.Materia.Count > 0
                ? target.Materia.Select(_gearSource.GetItemName).ToList()
                : [];

            lines.Add(new OverlayLine(
                slot,
                _gearSource.GetItemName(target.Id),
                comparison?.Status ?? SlotMatch.MissingCurrent,
                comparison?.MateriaMatch ?? false,
                equipped,
                _gearSource.OwnsItem(target.Id),
                materia));
        }

        return lines;
    }

    private void DrawWindow(Vector2 position)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(11f, 9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Accent);

        if (ImGui.Begin("##EorzeaArsenalBisOverlay", Flags))
        {
            IconText(FontAwesomeIcon.Gem, Accent, T(LocKeys.BisTooltipHeader));
            ImGui.Separator();
            foreach (var line in _cachedLines)
            {
                DrawLine(line);
            }

            _lastSize = ImGui.GetWindowSize();
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private void DrawLine(OverlayLine line)
    {
        var complete = line.Status == SlotMatch.Match && line.MateriaMatch;
        var (icon, color) = line.Status switch
        {
            SlotMatch.Match when line.MateriaMatch => (FontAwesomeIcon.Check, Green),
            SlotMatch.Match => (FontAwesomeIcon.ExclamationTriangle, Yellow),
            SlotMatch.ItemDiffers => (FontAwesomeIcon.ExclamationTriangle, Yellow),
            _ => (FontAwesomeIcon.Times, Red),
        };

        IconText(icon, color, $"{line.Slot}: {line.TargetName}");
        if (complete)
        {
            return;
        }

        if (line.Status == SlotMatch.ItemDiffers && line.EquippedName is not null)
        {
            ImGui.TextColored(Muted, Indent + _localizer.Get(LocKeys.BisYouHave, line.EquippedName));
        }

        if (line.Status is SlotMatch.ItemDiffers or SlotMatch.MissingCurrent)
        {
            ImGui.TextColored(line.Owned ? Green : Red, Indent + (line.Owned ? T(LocKeys.BisOwned) : T(LocKeys.BisNotOwned)));
        }

        if (line.Materia.Count > 0)
        {
            ImGui.TextColored(Muted, Indent + _localizer.Get(LocKeys.BisMateriaList, string.Join(", ", line.Materia)));
        }
    }

    private static void IconText(FontAwesomeIcon icon, Vector4 color, string text)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    /// <summary>
    /// Docks the overlay to the native <c>ItemDetail</c> addon: above it when there is room (else
    /// below), aligned to the edge nearest the cursor (right edge when the tooltip is left of the
    /// cursor, otherwise left edge), clamped on-screen. Returns <see langword="false"/> when no
    /// native tooltip is visible.
    /// </summary>
    private bool TryGetDockPosition(out Vector2 position)
    {
        position = default;

        var addon = _gameGui.GetAddonByName("ItemDetail", 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            return false;
        }

        var anchor = addon.Position;
        var display = ImGui.GetIO().DisplaySize;

        var mouse = ImGui.GetMousePos();
        var tooltipLeftOfCursor = anchor.X + (addon.ScaledWidth * 0.5f) < mouse.X;
        var x = tooltipLeftOfCursor
            ? anchor.X + addon.ScaledWidth - _lastSize.X - FrameInset
            : anchor.X + FrameInset;

        var aboveY = anchor.Y - _lastSize.Y - Gap;
        var y = aboveY >= ScreenPadding ? aboveY : anchor.Y + addon.ScaledHeight + Gap;

        x = Math.Clamp(x, ScreenPadding, Math.Max(ScreenPadding, display.X - _lastSize.X - ScreenPadding));
        y = Math.Clamp(y, ScreenPadding, Math.Max(ScreenPadding, display.Y - _lastSize.Y - ScreenPadding));

        position = new Vector2(x, y);
        return true;
    }

    private readonly record struct OverlayLine(
        string Slot,
        string TargetName,
        SlotMatch Status,
        bool MateriaMatch,
        string? EquippedName,
        bool Owned,
        IReadOnlyList<string> Materia);
}
