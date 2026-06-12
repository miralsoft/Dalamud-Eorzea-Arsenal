using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Shows the in-game "gear vs BiS" diff (Feature A). Reads cached state from
/// <see cref="BisService"/> (which owns the fetch + comparison) and renders the per-slot diff.
/// Holds no domain logic (R11); all strings via the localizer (R6).
/// </summary>
public sealed class BisWindow : Window
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly BisService _bis;

    /// <summary>Creates the BiS window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="bis">The shared BiS service (cache + fetch + comparison).</param>
    public BisWindow(PluginConfig config, ConfigStore store, Localizer localizer, BisService bis)
        : base("Eorzea Arsenal###EorzeaArsenalBis")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _bis = bis;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(900, 1400),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void Draw()
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

        var statusMessage = StatusMessage();
        if (statusMessage is not null)
        {
            ImGui.TextWrapped(statusMessage);
        }

        ImGui.Separator();
        foreach (var comparison in _bis.Comparisons)
        {
            DrawComparison(comparison);
        }
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

    private void DrawComparison(GearsetComparison comparison)
    {
        var header = $"#{comparison.GearIndex} {comparison.Job}" +
                     (string.IsNullOrEmpty(comparison.Name) ? string.Empty : $" — {comparison.Name}");
        ImGui.TextUnformatted(header);

        ImGui.SameLine();
        if (comparison.IsComplete)
        {
            ImGui.TextColored(Green, $"({T(LocKeys.BisComplete)})");
        }
        else
        {
            ImGui.TextDisabled($"({_localizer.Get(LocKeys.BisSummary, comparison.FullyMatchedSlots, comparison.Slots.Count)})");
        }

        foreach (var slot in comparison.Slots)
        {
            DrawSlot(slot);
        }

        ImGui.Spacing();
    }

    private void DrawSlot(SlotComparison slot)
    {
        var (color, detail) = slot.Status switch
        {
            SlotMatch.Match when slot.MateriaMatch => (Green, string.Empty),
            SlotMatch.Match => (Yellow, $" ({T(LocKeys.BisMateriaDiff)})"),
            SlotMatch.ItemDiffers => (Yellow, string.Empty),
            _ => (Red, string.Empty),
        };

        var body = slot.Status == SlotMatch.MissingCurrent
            ? _localizer.Get(LocKeys.BisMissing, slot.TargetItemId)
            : _localizer.Get(LocKeys.BisHave, slot.CurrentItemId ?? 0, slot.TargetItemId);

        ImGui.TextColored(color, $"  {slot.Slot}: {body}{detail}");
    }
}
