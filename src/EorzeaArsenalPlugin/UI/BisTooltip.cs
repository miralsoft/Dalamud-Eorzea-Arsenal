using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A safe, additive hover overlay: when the user hovers an item that is a BiS target for the
/// <b>currently selected</b> gearset, it shows a small tooltip docked next to the native item
/// tooltip (left if there is room, otherwise right) so it does not overlap it. It never touches
/// the native game tooltip (no UI-node manipulation), so it cannot crash the client and is
/// patch-stable (P2/P6). Registered on the UI draw loop.
/// </summary>
public sealed class BisTooltip
{
    private const float Margin = 4f;

    private static readonly Vector4 Accent = new(0.55f, 0.8f, 1f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly Localizer _localizer;
    private readonly IGameGui _gameGui;
    private readonly BisService _bis;
    private readonly GameGearSource _gearSource;

    private Vector2 _lastSize = new(220f, 80f);

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

        if (hits.Count == 0)
        {
            return;
        }

        PositionNextToNativeTooltip();
        ImGui.BeginTooltip();
        ImGui.TextColored(Accent, T(LocKeys.BisTooltipHeader));
        foreach (var hit in hits)
        {
            var name = string.IsNullOrEmpty(hit.Name) ? string.Empty : $" — {hit.Name}";
            ImGui.TextUnformatted($"{hit.Job} · {hit.Slot} (Gearset #{hit.GearIndex}){name}");
            DrawSlotState(hit);
        }

        _lastSize = ImGui.GetWindowSize(); // used next frame to dock above the native tooltip
        ImGui.EndTooltip();
    }

    private void DrawSlotState(BisHit hit)
    {
        if (_bis.SlotStatus(hit.GearIndex, hit.Job, hit.Slot) is not { } slot)
        {
            return;
        }

        var (color, text) = slot.Status switch
        {
            SlotMatch.Match when slot.MateriaMatch => (Green, T(LocKeys.BisComplete)),
            SlotMatch.Match => (Yellow, T(LocKeys.BisMateriaDiff)),
            SlotMatch.ItemDiffers => (Yellow, _localizer.Get(LocKeys.BisHave, slot.CurrentItemId ?? 0, slot.TargetItemId)),
            _ => (Red, _localizer.Get(LocKeys.BisMissing, slot.TargetItemId)),
        };

        ImGui.TextColored(color, $"   {text}");
    }

    /// <summary>
    /// Docks the next tooltip to the native <c>ItemDetail</c> addon, aligned to its left edge:
    /// directly <b>above</b> it when there is room, otherwise <b>below</b> it (small margin). This
    /// keeps it attached to and clearly readable next to the item tooltip. Falls back to the
    /// default cursor position when the addon is not visible.
    /// </summary>
    private void PositionNextToNativeTooltip()
    {
        var addon = _gameGui.GetAddonByName("ItemDetail", 1);
        if (addon.IsNull || !addon.IsVisible)
        {
            return; // no native tooltip visible → fall back to the default cursor position
        }

        var position = addon.Position;
        var aboveY = position.Y - _lastSize.Y - Margin;
        var y = aboveY >= 0
            ? aboveY                                    // dock directly above the tooltip
            : position.Y + addon.ScaledHeight + Margin; // no room above → dock below it

        ImGui.SetNextWindowPos(new Vector2(position.X, y));
    }
}
