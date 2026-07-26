using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A dedicated, read-only "what will be sent" preview: reads the current gearsets, sanitizes them the
/// same way a push does, and lists them. Kept out of the menu window so that stays purely actionable
/// (R11: no domain logic here — it only reads via <see cref="IGearSource"/> and formats).
/// </summary>
public sealed class PreviewWindow : Window
{
    private readonly IGearSource _gearSource;
    private readonly Localizer _localizer;
    private readonly ILog _log;

    private volatile string[] _lines = [];
    private volatile bool _ran;
    private volatile bool _running;

    /// <summary>Creates the preview window.</summary>
    /// <param name="gearSource">Gear source read for the preview.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="log">Diagnostics sink.</param>
    public PreviewWindow(IGearSource gearSource, Localizer localizer, ILog log)
        : base("Eorzea Arsenal — Preview###EorzeaArsenalPreview")
    {
        _gearSource = gearSource;
        _localizer = localizer;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 220),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Opens the window and (re-)runs the preview.</summary>
    public void Open()
    {
        IsOpen = true;
        Refresh();
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (ImGui.Button(T(LocKeys.PreviewButton)))
        {
            Refresh();
        }

        ImGui.Separator();

        if (_running)
        {
            ImGui.TextDisabled(T(LocKeys.TeamsLoading));
            return;
        }

        if (!_ran)
        {
            return;
        }

        var lines = _lines;
        if (lines.Length == 0)
        {
            ImGui.TextDisabled(T(LocKeys.PreviewEmpty));
            return;
        }

        ImGui.TextUnformatted(_localizer.Get(LocKeys.PreviewHeader, lines.Length));
        foreach (var line in lines)
        {
            ImGui.BulletText(line);
        }
    }

    private void Refresh()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await _gearSource.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                if (data is null)
                {
                    _lines = [];
                    return;
                }

                var clean = GearSanitizer.Sanitize(data);
                _lines = clean.Gearsets
                    .Select(g => $"#{g.GearIndex} {g.Job}{(string.IsNullOrEmpty(g.Name) ? string.Empty : $" — {g.Name}")} ({g.Items.Count} items)")
                    .ToArray();
            }
            catch (Exception ex)
            {
                _log.Error($"Preview failed: {ex.GetType().Name}.");
                _lines = [];
            }
            finally
            {
                _ran = true;
                _running = false;
            }
        });
    }
}
