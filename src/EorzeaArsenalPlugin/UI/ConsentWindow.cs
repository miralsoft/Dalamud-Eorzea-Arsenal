using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The one dialog of 1.3.0 that asks what may be sent: the inventory sync, the collection and the reports (drops
/// and spots behind one switch), together and once, as server 1.5 asks. It shows on the update and on a new
/// installation's first connected session. The reports start off; closing without saving is no, and either way
/// the dialog is not shown again. Everything stays changeable in the settings.
/// </summary>
public sealed class ConsentWindow : Window
{
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly PluginConfig _config;
    private readonly Localizer _localizer;
    private readonly Action _save;
    private readonly Func<bool> _reportsOffered;
    private readonly Func<bool> _collectionOffered;
    private bool _collection;
    private bool _inventory;
    private bool _reports;

    /// <summary>Creates the dialog.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="save">Persists the config.</param>
    /// <param name="reportsOffered">Whether the server takes drop or spot reports.</param>
    /// <param name="collectionOffered">Whether the server takes the collection.</param>
    public ConsentWindow(PluginConfig config, Localizer localizer, Action save, Func<bool> reportsOffered, Func<bool> collectionOffered)
        : base("Eorzea Arsenal · 1.3.0###EorzeaArsenalConsent", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _config = config;
        _localizer = localizer;
        _save = save;
        _reportsOffered = reportsOffered;
        _collectionOffered = collectionOffered;

        // Escape closes Dalamud windows, and a close counts as no. Pressed for something else in the game, it
        // would answer this question unseen, so only the window's own close button or "save" ends it.
        RespectCloseHotkey = false;
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Opens the dialog with the current choices.</summary>
    public void Ask()
    {
        _inventory = _config.SyncInventory;
        _reports = _config.ShareReports;
        _collection = _config.ReportCollection;
        IsOpen = true;
    }

    /// <inheritdoc />
    public override void OnClose()
    {
        // No answer is no: the reports stay as they were (off), and the question is not asked again.
        if (!_config.ReportConsentAsked)
        {
            _config.ReportConsentAsked = true;
            _save();
        }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
        ImGui.TextColored(Yellow, T(LocKeys.ConsentHeading));
        ImGui.Spacing();
        ImGui.TextWrapped(T(LocKeys.ConsentIntro));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Choice(LocKeys.SyncInventory, LocKeys.SyncInventoryHint, ref _inventory);
        if (_collectionOffered())
        {
            Choice(LocKeys.ReportCollection, LocKeys.ReportCollectionHint, ref _collection);
        }

        if (_reportsOffered())
        {
            Choice(LocKeys.ShareReports, LocKeys.ShareReportsHint, ref _reports);
        }

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button(T(LocKeys.ConsentApply)))
        {
            _config.SyncInventory = _inventory;
            _config.ShareReports = _reports && _reportsOffered();
            _config.ReportCollection = _collection && _collectionOffered();
            _config.ReportConsentAsked = true;
            _save();
            IsOpen = false;
        }

        ImGui.SameLine();
        ImGui.TextColored(Dim, T(LocKeys.ConsentCloseHint));
        ImGui.PopTextWrapPos();
    }

    private void Choice(string labelKey, string hintKey, ref bool value)
    {
        ImGui.Checkbox(T(labelKey), ref value);
        ImGui.Indent();
        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        ImGui.TextWrapped(T(hintKey));
        ImGui.PopStyleColor();
        ImGui.Unindent();
        ImGui.Spacing();
    }
}
