using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The "what's new" window: a short, plain-language digest of what each release changed, so a user can
/// see at a glance what moved without reading the contributor changelog. Content comes from the bundled
/// <see cref="ReleaseNotes"/> (R6/R11 — no domain logic here). Opening it marks the newest version as
/// seen, which is what clears the highlight on the menu entry.
/// </summary>
public sealed class WhatsNewWindow : Window
{
    private const string ChangelogUrl = "https://github.com/miralsoft/Dalamud-Eorzea-Arsenal/blob/main/CHANGELOG.md";

    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Blue = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly PluginConfig _config;
    private readonly Localizer _localizer;
    private readonly Action _save;

    /// <summary>Creates the what's-new window.</summary>
    /// <param name="config">Live config (holds the last-seen version).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="save">Persists the config after marking the notes as seen.</param>
    public WhatsNewWindow(PluginConfig config, Localizer localizer, Action save)
        : base("Eorzea Arsenal — What's new###EorzeaArsenalWhatsNew")
    {
        _config = config;
        _localizer = localizer;
        _save = save;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(900, 1200),
        };
    }

    private string T(string key) => _localizer.Get(key);

    private bool German => _localizer.Language == Localizer.German;

    /// <summary>Opens the window and acknowledges the newest release.</summary>
    public void Open()
    {
        MarkSeen();
        IsOpen = true;
    }

    /// <summary>
    /// Acknowledges the newest release without showing anything — used on a fresh install, where a
    /// "what changed since last time" list would be meaningless.
    /// </summary>
    public void MarkSeen()
    {
        if (!ReleaseNotes.HasUnseen(_config.LastSeenReleaseNotes))
        {
            return;
        }

        _config.LastSeenReleaseNotes = ReleaseNotes.Latest.Version;
        _save();
    }

    /// <inheritdoc />
    public override void Draw()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Dim))
        {
            ImGui.TextWrapped(T(LocKeys.WhatsNewIntro));
        }

        ImGui.Spacing();
        if (ImGui.Button(T(LocKeys.WhatsNewFullChangelog)))
        {
            Util.OpenLink(ChangelogUrl);
        }

        ImGui.Spacing();
        ImGui.Separator();

        using var child = ImRaii.Child("##notes", new Vector2(0, 0), false);
        if (!child)
        {
            return;
        }

        for (var i = 0; i < ReleaseNotes.All.Count; i++)
        {
            DrawRelease(ReleaseNotes.All[i], newest: i == 0);
        }
    }

    private void DrawRelease(ReleaseNote note, bool newest)
    {
        using var id = ImRaii.PushId($"rel_{note.Version}");

        // Only the newest release is expanded — older ones are there to look up, not to read again.
        var flags = newest ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        var header = $"{note.Version}   ·   {FormatDate(note.Date)}";
        if (!ImGui.CollapsingHeader($"{header}###hdr_{note.Version}", flags))
        {
            return;
        }

        if (newest)
        {
            ImGui.TextColored(Green, $"● {T(LocKeys.WhatsNewInstalled)}");
        }

        foreach (var item in note.Items)
        {
            DrawItem(item);
        }

        ImGui.Spacing();
    }

    private void DrawItem(ReleaseNoteItem item)
    {
        var (label, color) = item.Kind switch
        {
            ReleaseNoteKind.Added => (T(LocKeys.WhatsNewKindAdded), Green),
            ReleaseNoteKind.Improved => (T(LocKeys.WhatsNewKindImproved), Blue),
            _ => (T(LocKeys.WhatsNewKindFixed), Yellow),
        };

        // The badge sits on the same line as the first wrapped line of the text.
        ImGui.TextColored(color, $"[{label}]");
        ImGui.SameLine();
        ImGui.TextWrapped(ReleaseNotes.Text(item, German));
    }

    /// <summary>Formats the ISO release date for the active language; falls back to the raw value.</summary>
    private string FormatDate(string iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("d", German ? new System.Globalization.CultureInfo("de-DE") : System.Globalization.CultureInfo.InvariantCulture)
            : iso;
}
