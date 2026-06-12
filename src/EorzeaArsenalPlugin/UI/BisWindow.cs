using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Shows the in-game "gear vs BiS" diff (Feature A): it reads the live gear, fetches the BiS
/// targets via <c>GET /gear/bis</c> (needs <c>gear:read</c>) and renders the per-slot comparison
/// computed by <see cref="BisComparer"/>. Holds no domain logic (R11); all strings via the
/// localizer (R6).
/// </summary>
public sealed class BisWindow : Window
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly IApiClient _api;
    private readonly IGearSource _gearSource;
    private readonly ILog _log;

    private volatile bool _loading;
    private volatile string _status = string.Empty;
    private volatile GearsetComparison[] _comparisons = [];

    /// <summary>Creates the BiS window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="api">API client (for <c>GET /gear/bis</c>).</param>
    /// <param name="gearSource">Live gear source.</param>
    /// <param name="log">Diagnostics sink.</param>
    public BisWindow(PluginConfig config, ConfigStore store, Localizer localizer, IApiClient api, IGearSource gearSource, ILog log)
        : base("Eorzea Arsenal###EorzeaArsenalBis")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _api = api;
        _gearSource = gearSource;
        _log = log;

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
        using (ImRaii.Disabled(_loading || !_store.HasKey || !_config.Enabled))
        {
            if (ImGui.Button(T(LocKeys.BisRefresh)))
            {
                RunCompare();
            }
        }

        if (_loading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(T(LocKeys.BisLoading));
        }

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.TextWrapped(_status);
        }

        ImGui.Separator();
        foreach (var comparison in _comparisons)
        {
            DrawComparison(comparison);
        }
    }

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

    private void RunCompare()
    {
        _loading = true;
        _status = string.Empty;
        _comparisons = [];

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_store.HasKey)
                {
                    _status = T(LocKeys.PushNotConnected);
                    return;
                }

                var live = await _gearSource.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                if (live is null)
                {
                    _status = T(LocKeys.PushNotLoggedIn);
                    return;
                }

                var clean = GearSanitizer.Sanitize(live);
                var result = await _api.GetBisAsync(_store.ApiKey!, clean.Character.CidHash, CancellationToken.None).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    _status = result.Error!.Kind switch
                    {
                        ApiErrorKind.Forbidden => T(LocKeys.BisReconnect),
                        ApiErrorKind.NotFound => T(LocKeys.BisNone),
                        _ => PushReportFormatter.ErrorMessage(result.Error.Kind, _localizer),
                    };
                    return;
                }

                if (result.Value!.Data.Count == 0)
                {
                    _status = T(LocKeys.BisNone);
                    return;
                }

                _comparisons = BisComparer.Compare(clean, result.Value.Data).ToArray();
            }
            catch (Exception ex)
            {
                _log.Error($"BiS comparison failed: {ex.GetType().Name}.");
                _status = _localizer.Get(LocKeys.TestFailed, ex.GetType().Name);
            }
            finally
            {
                _loading = false;
            }
        });
    }
}
