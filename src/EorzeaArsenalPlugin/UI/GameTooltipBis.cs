using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Plugin.Configuration;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text;
using Lumina.Text.ReadOnly;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Puts the short form of the BiS information into the game's own item tooltip, below the item's
/// description, the way other plugins add their lines there. Chosen in the settings next to the plugin's
/// own window (<see cref="BisTooltipPlacement"/>).
/// </summary>
/// <remarks>
/// <para>
/// The game fills the tooltip's text into the <c>ItemDetail</c> string array and then asks the addon to
/// update. Just before that update the block is appended to the description field, so the game lays the
/// tooltip out with it and sizes it to fit. The block starts with the heading, and an update that finds the
/// heading there already leaves the text alone, so a second update for the same item never doubles it.
/// </para>
/// <para>
/// This touches the game's own tooltip, which the overlay never did. The field is the one every plugin
/// that writes into the tooltip uses, and the whole handler is wrapped: a failure is logged once and the
/// tooltip stays as the game made it (P2). A patch that moves the field shows the block in the wrong place
/// or not at all, never a crash.
/// </para>
/// </remarks>
public sealed class GameTooltipBis : IDisposable
{
    /// <summary>The description field of the <c>ItemDetail</c> string array.</summary>
    private const int DescriptionField = 13;

    /// <summary>The rule above the block; short enough not to wrap in the narrowest item tooltip.</summary>
    private static readonly string Rule = new('─', 18);

    /// <summary>The rule's colour: the muted brown and grey of the game's own dividers (<c>UIColor</c> 3).</summary>
    private const uint RuleColor = 3;

    private readonly IAddonLifecycle _lifecycle;
    private readonly PluginConfig _config;
    private readonly BisTooltip _bis;
    private readonly ILog _log;
    private bool _failureLogged;

    /// <summary>Creates the hook and listens for the item tooltip's updates.</summary>
    /// <param name="lifecycle">Dalamud's addon events.</param>
    /// <param name="config">Live config (on/off and the placement).</param>
    /// <param name="bis">Builds the lines, the same ones the window shows.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameTooltipBis(IAddonLifecycle lifecycle, PluginConfig config, BisTooltip bis, ILog log)
    {
        _lifecycle = lifecycle;
        _config = config;
        _bis = bis;
        _log = log;
        _lifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "ItemDetail", OnItemDetail);
    }

    private unsafe void OnItemDetail(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (!_config.Enabled || !_config.ShowBisTooltip || _config.BisPlacement == BisTooltipPlacement.Overlay)
            {
                return;
            }

            var lines = _bis.TooltipLines();
            if (lines.Count == 0)
            {
                return;
            }

            var stage = AtkStage.Instance();
            if (stage == null)
            {
                return;
            }

            var strings = stage->GetStringArrayData(StringArrayType.ItemDetail);
            if (strings == null || strings->Size <= DescriptionField)
            {
                return;
            }

            var current = strings->StringArray[DescriptionField];
            var existing = current.HasValue ? current.AsSpan() : ReadOnlySpan<byte>.Empty;
            var header = _bis.Header;
            if (existing.IndexOf(Encoding.UTF8.GetBytes(header)) >= 0)
            {
                return; // already there from an earlier update of the same tooltip
            }

            var builder = new SeStringBuilder();
            if (!existing.IsEmpty)
            {
                builder.Append(new ReadOnlySeStringSpan(existing));
                builder.AppendNewLine();
            }

            // A rule above the block sets it apart from the description and the other plugins' lines. Box
            // drawing characters are in the game's fonts, which carry the Japanese set for every client.
            Colored(builder, Rule, RuleColor);
            builder.AppendNewLine();

            Colored(builder, header, BisTooltip.GameTooltipColor(BisTooltip.HeaderColor));
            foreach (var (text, color) in lines)
            {
                builder.AppendNewLine();
                Colored(builder, text, BisTooltip.GameTooltipColor(color));
            }

            strings->SetValue(DescriptionField, builder.GetViewAsSpan(), false, true, true);
        }
        catch (Exception ex)
        {
            // P2: the tooltip stays as the game made it. Logged once, since this runs on every hover.
            if (!_failureLogged)
            {
                _failureLogged = true;
                _log.Error($"BiS in the game's tooltip failed: {ex.GetType().Name}.");
            }
        }
    }

    /// <summary>One line in a colour of the game's <c>UIColor</c> sheet, which follows the UI theme.</summary>
    private static void Colored(SeStringBuilder builder, string text, uint? colorType)
    {
        if (colorType is { } row)
        {
            builder.PushColorType(row);
            builder.Append(text);
            builder.PopColorType();
        }
        else
        {
            builder.Append(text);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, "ItemDetail", OnItemDetail);
}
