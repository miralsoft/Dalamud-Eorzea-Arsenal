#if EORZEA_ARSENAL_DEVTOOLS
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The developer window: a fixed bar of probes at the top, and everything they produce in one output
/// area at the bottom. Compiled in only when <c>EORZEA_ARSENAL_DEVTOOLS</c> is defined, which comes from
/// a git-ignored local props file, so a released build does not contain this type at all.
/// </summary>
/// <remarks>
/// <para>
/// The shape is deliberate and was borrowed from a diagnostics window that gets this right. The first
/// version drew every section inline, one under the other, and it had the fault it was built to remove:
/// finding a value meant scrolling, and reaching a button meant scrolling back. Controls that move are
/// controls somebody hunts for.
/// </para>
/// <para>
/// So the buttons never move and never scroll, and there is exactly one output area. A probe replaces
/// what is in it, the copy button takes what is in it, and the character count says there is something
/// to take. What is copied is what is on screen, because they are the same string.
/// </para>
/// <para>
/// English throughout and not localised. It exists in one build, it is read by whoever works on the
/// plugin, and nearly every line in it is a field name from the payload or the code.
/// </para>
/// </remarks>
public sealed class DevWindow : Window
{
    private static readonly Vector4 Muted = new(0.80f, 0.82f, 0.86f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.55f, 1f);

    private readonly IReadOnlyList<(string Label, Func<IReadOnlyList<string>> Run)> _probes;
    private readonly IReadOnlyList<(string Label, Action Run)> _actions;
    private readonly Func<string> _everything;

    private string _output = string.Empty;
    private string _ran = string.Empty;
    private DateTime _copiedAt = DateTime.MinValue;

    /// <summary>Creates the window.</summary>
    /// <param name="probes">Named reports. Running one replaces the output.</param>
    /// <param name="actions">Named side effects: refreshes and samples. They produce no output.</param>
    /// <param name="everything">Every report at once, with a header naming the build.</param>
    public DevWindow(
        IReadOnlyList<(string Label, Func<IReadOnlyList<string>> Run)> probes,
        IReadOnlyList<(string Label, Action Run)> actions,
        Func<string> everything)
        : base("Eorzea Arsenal · Developer###EorzeaArsenalDev")
    {
        _probes = probes;
        _actions = actions;
        _everything = everything;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <inheritdoc />
    public override void Draw()
    {
        ImGui.TextColored(Muted, "Developer tools. Run a probe, then copy the output.");
        ImGui.Spacing();

        DrawActions();
        DrawProbes();

        ImGui.Spacing();
        DrawCopyRow();
        ImGui.Spacing();

        DrawOutput();
    }

    /// <summary>The things that change state, kept on their own line and first.</summary>
    /// <remarks>
    /// Apart from the probes because they are a different kind of press. A probe answers a question; one
    /// of these drops a cache and refetches, which is what the answer usually turns out to depend on.
    /// </remarks>
    private void DrawActions()
    {
        var first = true;
        foreach (var (label, run) in _actions)
        {
            if (!first)
            {
                ImGui.SameLine();
            }

            first = false;
            if (ImGui.Button(label))
            {
                // The output is left alone. Clearing it was the first attempt and it read as a failure:
                // press "Sample gearsets" and the area you were reading goes blank, with nothing to say
                // that the sample is a background read which has not finished. What the probes print
                // carries the sample time, so a stale answer is visible where it matters.
                run();
                _ran = $"{label}: triggered";
            }
        }
    }

    /// <summary>The reports. Each replaces the output, and the last one collects all of them.</summary>
    private void DrawProbes()
    {
        var first = true;
        foreach (var (label, run) in _probes)
        {
            if (!first)
            {
                ImGui.SameLine();
            }

            first = false;
            if (ImGui.Button(label))
            {
                _output = string.Join(Environment.NewLine, run());
                _ran = label;
            }
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.70f, 0.75f)))
        {
            if (ImGui.Button("Everything"))
            {
                _output = _everything();
                _ran = "Everything";
            }
        }
    }

    /// <summary>The copy button, what was run, and how much there is to take.</summary>
    private void DrawCopyRow()
    {
        using (ImRaii.Disabled(_output.Length == 0))
        {
            if (ImGui.Button("Copy to clipboard"))
            {
                ImGui.SetClipboardText(_output);
                _copiedAt = DateTime.UtcNow;
            }
        }

        ImGui.SameLine();
        ImGui.TextColored(Muted, $"{_output.Length} characters");

        if (_ran.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"·  {_ran}");
        }

        // A clipboard write leaves no other trace, so without this the button looks like it did nothing.
        if (DateTime.UtcNow - _copiedAt < TimeSpan.FromSeconds(3))
        {
            ImGui.SameLine();
            ImGui.TextColored(Good, "·  copied");
        }
    }

    /// <summary>
    /// The output, filling whatever is left of the window.
    /// </summary>
    /// <remarks>
    /// A read-only multi-line input rather than drawn text, so the content can be selected with the
    /// mouse and scrolled without the buttons above it moving. Drawn text would also have to be split
    /// per line, which turns a five thousand character report into five thousand ImGui items.
    /// </remarks>
    private void DrawOutput()
    {
        var height = Math.Max(120f, ImGui.GetContentRegionAvail().Y);
        var text = _output;

        using var font = ImRaii.PushFont(UiBuilder.MonoFont);
        ImGui.InputTextMultiline(
            "##devOutput",
            ref text,
            64 * 1024,
            new Vector2(-1f, height),
            ImGuiInputTextFlags.ReadOnly);
    }
}
#endif
