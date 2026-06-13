using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A diagnostics window listing the recent plugin messages (status codes, request ids, errors —
/// never secrets, R22) with copy-to-clipboard and clear actions, so the user can share them for
/// support. Reads from the shared <see cref="LogBuffer"/>.
/// </summary>
public sealed class LogWindow : Window
{
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Muted = new(0.80f, 0.82f, 0.86f, 1f);

    private readonly LogBuffer _buffer;
    private readonly Localizer _localizer;

    /// <summary>Creates the log window.</summary>
    /// <param name="buffer">The shared log buffer.</param>
    /// <param name="localizer">UI string resolver.</param>
    public LogWindow(LogBuffer buffer, Localizer localizer)
        : base("Eorzea Arsenal — Log###EorzeaArsenalLog")
    {
        _buffer = buffer;
        _localizer = localizer;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 280),
            MaximumSize = new Vector2(1200, 1400),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void Draw()
    {
        if (ImGui.Button(T(LocKeys.LogCopy)))
        {
            ImGui.SetClipboardText(_buffer.ToText());
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.LogClear)))
        {
            _buffer.Clear();
        }

        ImGui.Separator();

        var entries = _buffer.Snapshot();
        if (ImGui.BeginChild("##logList", new Vector2(0, 0), true))
        {
            if (entries.Count == 0)
            {
                ImGui.TextDisabled(T(LocKeys.LogEmpty));
            }
            else
            {
                foreach (var entry in entries)
                {
                    var color = entry.Level switch
                    {
                        LogLevel.Error => Red,
                        LogLevel.Warning => Yellow,
                        _ => Muted,
                    };

                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextWrapped($"[{entry.Time:HH:mm:ss}] {entry.Message}");
                    ImGui.PopStyleColor();
                }

                // Keep pinned to the newest entry while the user is at the bottom.
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1f);
                }
            }
        }

        ImGui.EndChild();
    }
}
