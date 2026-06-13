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
/// A safe, additive hover overlay: when the user hovers an item that is a BiS target for the
/// <b>currently selected</b> gearset, it shows a small styled window docked to the native item
/// tooltip (above it, or below when there is no room) so it stays attached and never overlaps. It
/// never touches the native game tooltip (no UI-node manipulation), so it cannot crash the client
/// and is patch-stable (P2/P6). Registered on the UI draw loop.
/// </summary>
public sealed class BisTooltip
{
    // ItemDetail has a transparent left border; nudge right so we sit flush with the visible frame.
    private const float FrameInset = 26f;
    private const float Gap = 1f;
    private const float ScreenPadding = 4f;

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

    private Vector2 _lastSize = new(240f, 80f);

    /// <summary>Creates the overlay.</summary>
    /// <param name="config">Live config (holds the on/off toggle).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="gameGui">Provides the hovered item id and addon bounds.</param>
    /// <param name="bis">The shared BiS cache/lookup.</param>
    /// <param name="gearSource">Provides the current gearset index.</param>
    public BisTooltip(PluginConfig config, Localizer localizer, IGameGui gameGui, BisService bis, GameGearSource gearSource)
    {
        _config = config;
        _localizer = localizer;
        _gameGui = gameGui;
        _bis = bis;
        _gearSource = gearSource;
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Draws the overlay when an item is hovered. Registered on <c>UiBuilder.Draw</c>.</summary>
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
        var currentIndex = _gearSource.GetCurrentGearsetIndex();

        // Only the currently selected gearset/class is relevant while hovering.
        var hits = new List<BisHit>();
        foreach (var hit in _bis.FindForItem(itemId))
        {
            if (hit.GearIndex == currentIndex)
            {
                hits.Add(hit);
            }
        }

        if (hits.Count == 0 || !TryGetDockPosition(out var position))
        {
            return; // not next to a native tooltip → don't show a stray box
        }

        DrawWindow(position, hits);
    }

    private void DrawWindow(Vector2 position, List<BisHit> hits)
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
            foreach (var hit in hits)
            {
                ImGui.TextColored(Muted, $"{hit.Job} · {hit.Slot} · Gearset #{hit.GearIndex}");
                DrawSlotState(hit);
            }

            _lastSize = ImGui.GetWindowSize(); // used next frame to dock above the native tooltip
        }

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private void DrawSlotState(BisHit hit)
    {
        if (_bis.SlotStatus(hit.GearIndex, hit.Job, hit.Slot) is not { } slot)
        {
            return;
        }

        var (icon, color, text) = slot.Status switch
        {
            SlotMatch.Match when slot.MateriaMatch => (FontAwesomeIcon.Check, Green, T(LocKeys.BisComplete)),
            SlotMatch.Match => (FontAwesomeIcon.ExclamationTriangle, Yellow, T(LocKeys.BisMateriaDiff)),
            SlotMatch.ItemDiffers => (FontAwesomeIcon.ExclamationTriangle, Yellow, _localizer.Get(LocKeys.BisHave, slot.CurrentItemId ?? 0, slot.TargetItemId)),
            _ => (FontAwesomeIcon.Times, Red, _localizer.Get(LocKeys.BisMissing, slot.TargetItemId)),
        };

        IconText(icon, color, text);
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
    /// Computes the docked position for the overlay: flush to the native <c>ItemDetail</c> addon's
    /// left edge, directly above it when there is room (else below), clamped on-screen. Returns
    /// <see langword="false"/> when no native tooltip is visible.
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

        var x = anchor.X + FrameInset;
        var aboveY = anchor.Y - _lastSize.Y - Gap;
        var y = aboveY >= ScreenPadding ? aboveY : anchor.Y + addon.ScaledHeight + Gap;

        x = Math.Clamp(x, ScreenPadding, Math.Max(ScreenPadding, display.X - _lastSize.X - ScreenPadding));
        y = Math.Clamp(y, ScreenPadding, Math.Max(ScreenPadding, display.Y - _lastSize.Y - ScreenPadding));

        position = new Vector2(x, y);
        return true;
    }
}
