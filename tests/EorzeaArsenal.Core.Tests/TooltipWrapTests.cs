using EorzeaArsenal.Localization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Lines for the game's tooltip are broken here, so a continuation keeps its indent.</summary>
public sealed class TooltipWrapTests
{
    [Fact]
    public void A_short_line_stays_one_line_with_the_block_indent()
    {
        Assert.Equal(["  ✓ Kopf: Großmeister-Kopfstück · iLvl 790"], TooltipWrap.Wrap("✓ Kopf: Großmeister-Kopfstück · iLvl 790", "  "));
    }

    [Fact]
    public void A_long_line_continues_under_the_text_after_its_mark()
    {
        var lines = TooltipWrap.Wrap("× Hände: Großmeister-Handschuhe der Heilung · iLvl 790 · Raid", "  ");

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("  × Hände:", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("    ", lines[1], StringComparison.Ordinal);
        Assert.False(char.IsWhiteSpace(lines[1][4]));
        Assert.All(lines, l => Assert.True(TooltipWrap.Width(l) <= TooltipWrap.Budget));
    }

    [Fact]
    public void A_sub_line_keeps_its_own_indent_on_every_continuation()
    {
        var lines = TooltipWrap.Wrap("    Materia: Hellseher-Materia XII, Hellseher-Materia XII, Hellseher-Materia XII", "  ");

        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => Assert.StartsWith("      ", l, StringComparison.Ordinal));
        Assert.All(lines, l => Assert.True(TooltipWrap.Width(l) <= TooltipWrap.Budget));
        Assert.Equal(
            "Materia: Hellseher-Materia XII, Hellseher-Materia XII, Hellseher-Materia XII",
            string.Join(' ', lines.Select(l => l.Trim())));
    }

    [Fact]
    public void A_word_longer_than_a_line_stands_alone_rather_than_being_cut()
    {
        var word = new string('M', 60);
        var lines = TooltipWrap.Wrap($"× {word} end", "  ");

        Assert.Contains(lines, l => l.Contains(word, StringComparison.Ordinal));
        Assert.Equal("end", lines[^1].Trim());
    }
}
