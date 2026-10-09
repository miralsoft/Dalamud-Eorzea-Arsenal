using System.Text.Json;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// <c>named_jobs</c> on every push (contract "What the client can name"): the codes this build can put a
/// name to, so the server parks only rows of those jobs. Without it a job the game adds after this release
/// would have its row parked: the plugin cannot name it, leaves the set out, and still says <c>all</c>.
/// </summary>
public sealed class NamedJobsTests
{
    private static GearData Snapshot() => TestData.Snapshot(TestData.ExampleHash);

    /// <summary>A running plugin names what its map holds: the compiled floor at the least.</summary>
    [Fact]
    public void ThePushNamesWhatTheJobMapHolds()
    {
        var payload = GearPayload.From(Snapshot(), JobScope.All);

        Assert.NotNull(payload.NamedJobs);
        Assert.Equal(JobMap.ValidCodes.Order(StringComparer.Ordinal), payload.NamedJobs);
        Assert.True(payload.NamedJobs.Count >= 42);
        Assert.Contains("PLD", payload.NamedJobs);
        Assert.Contains("CRP", payload.NamedJobs);
        Assert.Contains("FSH", payload.NamedJobs);
    }

    /// <summary>A job learned from the game's sheet is named too; that is the whole point of the field.</summary>
    [Fact]
    public void ALearnedJobIsNamed()
    {
        var payload = GearPayload.From(Snapshot(), JobScope.All, ["PLD", "war", "XYZ", "PLD"]);

        Assert.Equal(["PLD", "WAR", "XYZ"], payload.NamedJobs);
    }

    [Fact]
    public void ItGoesOnTheWireAsNamedJobs()
    {
        var payload = GearPayload.From(Snapshot(), JobScope.All, ["DRK", "BST"]);

        var json = JsonSerializer.Serialize(payload, EorzeaJson.Options);

        Assert.Contains("\"named_jobs\":[\"BST\",\"DRK\"]", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Never <c>[]</c>: the server reads an empty list as "did not say", and a serialiser writing one would
    /// look like a healthy sync with half of it switched off. With nothing to name the field is left out.
    /// </summary>
    [Fact]
    public void NothingToNameLeavesTheFieldOut()
    {
        var payload = GearPayload.From(Snapshot(), JobScope.All, ["", "  "]);

        var json = JsonSerializer.Serialize(payload, EorzeaJson.Options);

        Assert.Null(payload.NamedJobs);
        Assert.DoesNotContain("named_jobs", json, StringComparison.Ordinal);
    }
}
