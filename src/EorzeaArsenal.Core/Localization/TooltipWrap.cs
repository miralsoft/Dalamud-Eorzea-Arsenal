using System.Text;

namespace EorzeaArsenal.Localization;

/// <summary>
/// Breaks a line for the game's item tooltip before the game does, so a continuation keeps its indent.
/// </summary>
/// <remarks>
/// <para>
/// The tooltip's text field wraps on its own, but it starts every continuation at the left edge, and it has
/// no way to indent one. A line set in under a heading then spills back out under it. Breaking the line
/// here, at a space, and starting the continuation with the same indent plus the width of the line's mark
/// keeps the block aligned, the way the tooltip's own sections are.
/// </para>
/// <para>
/// The font is proportional, so the width is estimated: a narrow letter or a space counts half, a wide one
/// a third more. The budget sits below what the tooltip was measured to hold (about 44 of these units a
/// line on a screenshot: a materia line of 45.6 overflowed), so the game never needs to wrap a line twice.
/// </para>
/// </remarks>
public static class TooltipWrap
{
    /// <summary>The estimated width a tooltip line may take, in average characters.</summary>
    public const double Budget = 41;

    private const string NarrowChars = " .,:;'!|iIlj·()[]tfr";
    private const string WideChars = "mwMWÄÖÜ×✓—─";

    /// <summary>The estimated width of a text, in average characters.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its width.</returns>
    public static double Width(string text)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += NarrowChars.Contains(c, StringComparison.Ordinal) ? 0.5
                : WideChars.Contains(c, StringComparison.Ordinal) || char.IsUpper(c) ? 1.3
                : 1.0;
        }

        return width;
    }

    /// <summary>
    /// Breaks a line into tooltip lines: the first starts with <paramref name="indent"/>, every continuation
    /// with <paramref name="indent"/> plus as many spaces as the line's mark takes ("✓ ", "! ", "× "), so
    /// the continuation lines up with the text after the mark.
    /// </summary>
    /// <param name="text">The line, possibly starting with spaces of its own and a mark.</param>
    /// <param name="indent">What the whole block is set in by.</param>
    /// <param name="budget">The width a line may take.</param>
    /// <returns>The lines, each already indented.</returns>
    public static IReadOnlyList<string> Wrap(string text, string indent, double budget = Budget)
    {
        var own = text.Length - text.TrimStart(' ').Length;
        var body = text.TrimStart(' ');
        var lead = indent + new string(' ', own);
        var hanging = lead + (body.Length > 1 && body[1] == ' ' && !char.IsLetterOrDigit(body[0]) ? "  " : string.Empty);

        var lines = new List<string>();
        var current = new StringBuilder(lead);
        var currentWidth = Width(lead);
        var empty = true;
        foreach (var word in body.Split(' '))
        {
            var wordWidth = Width(word);
            var spaceWidth = empty ? 0 : Width(" ");
            if (!empty && currentWidth + spaceWidth + wordWidth > budget)
            {
                lines.Add(current.ToString());
                current.Clear().Append(hanging);
                currentWidth = Width(hanging);
                empty = true;
                spaceWidth = 0;
            }

            if (!empty)
            {
                current.Append(' ');
            }

            current.Append(word);
            currentWidth += spaceWidth + wordWidth;
            empty = false;
        }

        lines.Add(current.ToString());
        return lines;
    }
}
