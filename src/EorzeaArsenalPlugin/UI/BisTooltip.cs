using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A safe, additive hover overlay: when the user hovers an item whose id is a BiS target, it draws
/// a small ImGui tooltip near the cursor with the BiS slot(s) and the player's current state for
/// that slot. It does <b>not</b> touch the native game tooltip (no UI-node manipulation), so it
/// cannot crash the client and is patch-stable (P2/P6). Registered on the UI draw loop.
/// </summary>
public sealed class BisTooltip
{
    private static readonly Vector4 Accent = new(0.55f, 0.8f, 1f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly Localizer _localizer;
    private readonly IGameGui _gameGui;
    private readonly BisService _bis;

    /// <summary>Creates the overlay.</summary>
    /// <param name="config">Live config (holds the on/off toggle).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="gameGui">Provides the hovered item id.</param>
    /// <param name="bis">The shared BiS cache/lookup.</param>
    public BisTooltip(PluginConfig config, Localizer localizer, IGameGui gameGui, BisService bis)
    {
        _config = config;
        _localizer = localizer;
        _gameGui = gameGui;
        _bis = bis;
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
        var hits = _bis.FindForItem(itemId);
        if (hits.Count == 0)
        {
            return;
        }

        ImGui.BeginTooltip();
        ImGui.TextColored(Accent, T(LocKeys.BisTooltipHeader));
        foreach (var hit in hits)
        {
            var name = string.IsNullOrEmpty(hit.Name) ? string.Empty : $" — {hit.Name}";
            ImGui.TextUnformatted($"{hit.Job} · {hit.Slot} (Gearset #{hit.GearIndex}){name}");
            DrawSlotState(hit);
        }

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
}
