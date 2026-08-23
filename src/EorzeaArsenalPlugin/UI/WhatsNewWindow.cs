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

    /// <summary>
    /// Text scale for this window. The notes are prose, read once per release, so the game's default
    /// size is too small here — the settings window runs at the same scale for the same reason.
    /// </summary>
    private const float TextScale = 1.25f;

    /// <summary>The three kind labels, measured together so the badge column fits the widest of them.</summary>
    private static readonly string[] KindKeys =
    [
        LocKeys.WhatsNewKindAdded,
        LocKeys.WhatsNewKindImproved,
        LocKeys.WhatsNewKindFixed,
    ];

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

        // Wide enough that the text column keeps a readable line length next to the badge column.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 360),
            MaximumSize = new Vector2(1100, 1400),
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

    /// <summary>Acknowledges the newest release, which is what clears the menu highlight.</summary>
    private void MarkSeen()
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
        ImGui.SetWindowFontScale(TextScale);

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
            using (ImRaii.PushColor(ImGuiCol.Text, Green))
            {
                ImGui.TextUnformatted($"● {T(LocKeys.WhatsNewInstalled)}");
            }
        }

        DrawItems(note);
        ImGui.Spacing();
    }

    /// <summary>
    /// Draws one release's items as a two-column table. The badge used to sit on the same line as
    /// wrapped text, which put every continuation line back under the badge and let the left edge of the
    /// prose move from block to block. In a table the text column is one straight edge, and its width is
    /// measured over all three kind labels — so it is the same in every release, not just this one.
    /// </summary>
    private void DrawItems(ReleaseNote note)
    {
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 5f) * TextScale);
        if (!ImGui.BeginTable("##items", 2, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.PadOuterX))
        {
            return;
        }

        try
        {
            ImGui.TableSetupColumn("##kind", ImGuiTableColumnFlags.WidthFixed, BadgeWidth());
            ImGui.TableSetupColumn("##text", ImGuiTableColumnFlags.WidthStretch);

            foreach (var item in note.Items)
            {
                DrawItem(item);
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawItem(ReleaseNoteItem item)
    {
        var (label, colour) = item.Kind switch
        {
            ReleaseNoteKind.Added => (T(LocKeys.WhatsNewKindAdded), Green),
            ReleaseNoteKind.Improved => (T(LocKeys.WhatsNewKindImproved), Blue),
            _ => (T(LocKeys.WhatsNewKindFixed), Yellow),
        };

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        using (ImRaii.PushColor(ImGuiCol.Text, colour))
        {
            ImGui.TextUnformatted(Badge(label));
        }

        // Note text is authored content, so it goes through TextUnformatted — a stray percent sign in a
        // note must not be read as a format specifier. Wrap position 0 means "the end of this cell".
        ImGui.TableNextColumn();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(ReleaseNotes.Text(item, German));
        ImGui.PopTextWrapPos();
    }

    /// <summary>Width of the badge column: the widest of the three kind labels at the current scale.</summary>
    private float BadgeWidth()
    {
        var widest = 0f;
        foreach (var key in KindKeys)
        {
            widest = Math.Max(widest, ImGui.CalcTextSize(Badge(T(key))).X);
        }

        return widest;
    }

    private static string Badge(string label) => $"[{label}]";

    /// <summary>Formats the ISO release date for the active language; falls back to the raw value.</summary>
    private string FormatDate(string iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("d", German ? new System.Globalization.CultureInfo("de-DE") : System.Globalization.CultureInfo.InvariantCulture)
            : iso;
}
