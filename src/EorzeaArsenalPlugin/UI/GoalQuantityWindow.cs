using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Localization;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// "How many do you want?" for a goal on something collected in numbers (totems, dyes, materials), opened from
/// the game's context menu. One unit would be reached at once by what is held, so the player says how many.
/// </summary>
public sealed class GoalQuantityWindow : Window
{
    private const int MaxQuantity = 999_999;
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Localizer _localizer;
    private string _name = string.Empty;
    private int _inBags;
    private int _quantity = 1;
    private Action<long>? _confirm;
    private bool _focus;

    /// <summary>Creates the dialog.</summary>
    /// <param name="localizer">UI string resolver.</param>
    public GoalQuantityWindow(Localizer localizer)
        : base("Eorzea Arsenal###EorzeaArsenalGoalQuantity", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _localizer = localizer;
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Asks for the quantity of one item.</summary>
    /// <param name="name">The item's name.</param>
    /// <param name="inBags">How many the bags hold now, as a hint.</param>
    /// <param name="confirm">Called with the quantity when the player adds the goal.</param>
    public void Ask(string name, int inBags, Action<long> confirm)
    {
        _name = name;
        _inBags = Math.Max(0, inBags);
        _quantity = Math.Clamp(_inBags + 1, 1, MaxQuantity);
        _confirm = confirm;
        _focus = true;
        IsOpen = true;
    }

    /// <inheritdoc />
    public override void PreDraw()
    {
        WindowName = $"Eorzea Arsenal · {T(LocKeys.GoalMenuAdd)}###EorzeaArsenalGoalQuantity";
    }

    /// <inheritdoc />
    public override void Draw()
    {
        ImGui.TextUnformatted(_name);
        ImGui.Spacing();
        ImGui.TextUnformatted(T(LocKeys.GoalQtyQuestion));
        ImGui.SetNextItemWidth(160f * ImGui.GetIO().FontGlobalScale);
        if (_focus)
        {
            ImGui.SetKeyboardFocusHere();
            _focus = false;
        }

        if (ImGui.InputInt("##goalQty", ref _quantity))
        {
            _quantity = Math.Clamp(_quantity, 1, MaxQuantity);
        }

        ImGui.TextColored(Dim, _localizer.Get(LocKeys.GoalQtyInBags, _inBags.ToString("N0", Culture())));
        if (_quantity <= _inBags)
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22f);
            ImGui.TextColored(Yellow, T(LocKeys.GoalQtyHeldAlready));
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        if (ImGui.Button(T(LocKeys.GoalQtyAdd)))
        {
            var confirm = _confirm;
            _confirm = null;
            IsOpen = false;
            confirm?.Invoke(_quantity);
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.GoalQtyCancel)))
        {
            _confirm = null;
            IsOpen = false;
        }
    }

    private CultureInfo Culture() =>
        _localizer.Language == Localizer.German ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-US");
}
