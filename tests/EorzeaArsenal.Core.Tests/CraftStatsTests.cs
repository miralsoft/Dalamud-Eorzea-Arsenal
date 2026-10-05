using System.Text.Json;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The port of the web's <c>meldMatch</c> against the cases both sides share. The file is
/// <c>docs/plugin/samples/meld-match-cases.json</c> from the API side, copied verbatim; its rows are real
/// ones in the shape of <c>craft_tables</c>, and the web's own tests run the same cases. If one of these
/// fails, the plugin would call a piece missing that the web calls done, or the other way round.
/// </summary>
public sealed class CraftStatsTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "meld-match-cases.json");

    /// <summary>The case names, so each one is its own test result.</summary>
    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(SamplePath));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(c.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TheSharedCaseComesOutAsOnTheWeb(string name)
    {
        var json = File.ReadAllText(SamplePath);
        var tables = JsonSerializer.Deserialize<CraftTables>(json, EorzeaJson.Options)!;
        using var doc = JsonDocument.Parse(json);
        var c = doc.RootElement.GetProperty("cases").EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == name);

        var item = tables.Items[c.GetProperty("item").GetString()!];
        var result = CraftStats.MeldMatch(
            item,
            c.GetProperty("hq").GetBoolean(),
            Ints(c.GetProperty("target")),
            Ints(c.GetProperty("worn")),
            tables.Materia);

        var expected = c.GetProperty("expected").EnumerateArray()
            .Select(e => new MeldSlot(e.GetProperty("filled").GetBoolean(), e.GetProperty("worn").GetInt32()))
            .ToList();
        Assert.Equal(expected, result);
    }

    /// <summary>The sample carries a row's camelCase <c>canHq</c>; the snake_case options must still read it.</summary>
    [Fact]
    public void TheSampleRowsReadWithEveryField()
    {
        var tables = JsonSerializer.Deserialize<CraftTables>(File.ReadAllText(SamplePath), EorzeaJson.Options)!;

        var body = tables.Items["47185"];
        Assert.True(body.CanHq);
        Assert.True(body.Adv);
        Assert.Equal(2, body.Slots);
        Assert.Equal(1734, body.Cap["70"]);
        Assert.Equal(33, tables.Materia["41778"].Value);
    }

    /// <summary>
    /// The cap: a piece's stat stops at it, and a piece already above it keeps its base. Body 47185 HQ is
    /// 1301 + 173 Craftsmanship with a cap of 1734, so three XII (33 each) and one XI (22) give 1474 + 121
    /// = 1595, under the cap; CP 6 + 1 with a cap of 8 stops at 8 however much is melded.
    /// </summary>
    [Fact]
    public void PieceStatsAppliesHqAndTheCap()
    {
        var tables = JsonSerializer.Deserialize<CraftTables>(File.ReadAllText(SamplePath), EorzeaJson.Options)!;
        var body = tables.Items["47185"];

        var stats = CraftStats.PieceStats(body, hq: true, [41778, 41778, 41778, 41765, 41779, 41779], tables.Materia);

        Assert.Equal(1301 + 173 + (3 * 33) + 22, stats[70]);
        Assert.Equal(8, stats[11]);
        Assert.Equal(472 + 62, stats[71]);
    }

    [Fact]
    public void AnNqPieceGetsNoHqBonus()
    {
        var tables = JsonSerializer.Deserialize<CraftTables>(File.ReadAllText(SamplePath), EorzeaJson.Options)!;

        var stats = CraftStats.PieceStats(tables.Items["47185"], hq: false, [], tables.Materia);

        Assert.Equal(1301, stats[70]);
    }

    /// <summary>
    /// PHP writes an empty map as <c>[]</c>. A strict dictionary throws on it, and one empty table would
    /// fail the whole BiS read, combat rows included.
    /// </summary>
    [Fact]
    public void AnEmptyMapWrittenAsAnArrayReadsAsEmpty()
    {
        const string Json = """{"items":[],"materia":[]}""";
        var tables = JsonSerializer.Deserialize<CraftTables>(Json, EorzeaJson.Options)!;

        Assert.Empty(tables.Items);
        Assert.Empty(tables.Materia);

        var block = JsonSerializer.Deserialize<CraftBlock>("""{"level":2,"totals":[]}""", EorzeaJson.Options)!;
        Assert.Empty(block.Totals);
    }

    private static List<int> Ints(JsonElement array) => array.EnumerateArray().Select(e => e.GetInt32()).ToList();
}
