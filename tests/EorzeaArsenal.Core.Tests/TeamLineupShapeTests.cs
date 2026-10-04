using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Phase E, held against the recorded answers of the real server rather than against ones written to
/// match the model.
/// </summary>
/// <remarks>
/// <para>
/// A property the model does not declare is dropped in silence, and the gap shows up only as a sentence
/// that never appears. The three files are copied verbatim from <c>docs/plugin/samples/</c> on the API
/// side (as of 2026-09-26), the same way the reconciliation model is held against a recorded answer.
/// </para>
/// <para>
/// The briefing and these samples disagreed in three places, and the samples were right each time:
/// <c>not_present</c> means "missing from the core", <c>roles.&lt;role&gt;.target</c> and
/// <c>characters[].user_id</c> exist though the table did not list them. Where the two disagree, the
/// test follows the recording.
/// </para>
/// </remarks>
public sealed class TeamLineupShapeTests
{
    private static T Recorded<T>(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", file);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), EorzeaJson.Options)!;
    }

    [Fact]
    public void TheTeamListCarriesTargetsAndWhatIsMissing()
    {
        var team = Assert.Single(Recorded<TeamsResponse>("me-teams-lineup-summary.json").Data!);

        Assert.Equal("Test Static Lineup", team.Name);
        var summary = team.LineupSummary!;
        Assert.Equal((1, 2, 3), (summary.Targets!.Tank, summary.Targets.Healer, summary.Targets.Dps));
        Assert.Equal((1, 0, 2), (summary.Missing!.Tank, summary.Missing.Healer, summary.Missing.Dps));
    }

    [Fact]
    public void TheLineupCarriesEveryRoleWithItsJobsPositionsAndGaps()
    {
        var lineup = Recorded<LineupResponse>("team-lineup.json").Data!;

        Assert.True(lineup.Allow!.Blu);
        Assert.True(lineup.Allow.Bst);

        var healer = lineup.Roles!.Healer!;
        Assert.Equal((2, 2, 0), (healer.Count, healer.Target, healer.Missing));
        Assert.Equal(["WHM", "SCH"], healer.Jobs);
        Assert.Equal(["SCH H1", "WHM H1"], healer.Positions!.Select(p => $"{p.Job} {p.Position}"));
        Assert.Empty(healer.NotPresent!);

        var dps = lineup.Roles.Dps!;
        Assert.Equal((1, 3, 2), (dps.Count, dps.Target, dps.Missing));
        Assert.Contains("BST", dps.NotPresent!);
    }

    /// <summary>
    /// The case the briefing's first wording got wrong. A substitute plays DRK, and DRK is still listed as
    /// not present for the tank role, because substitutes count for neither <c>count</c> nor
    /// <c>not_present</c>. The label on screen therefore has to say "missing from the core", or a player
    /// reads "missing: DRK" directly above a DRK and takes it for a fault.
    /// </summary>
    [Fact]
    public void AJobASubstitutePlaysIsStillMissingFromTheCore()
    {
        var lineup = Recorded<LineupResponse>("team-lineup.json").Data!;

        var tank = lineup.Roles!.Tank!;
        Assert.Equal(0, tank.Count);
        Assert.Contains("DRK", tank.NotPresent!);

        var substitute = Assert.Single(lineup.Characters!, c => c.Jobs!.Contains("DRK"));
        Assert.False(substitute.IsCore);
    }

    [Fact]
    public void TheCountsMapTheGermanWireNamesOntoEnglishProperties()
    {
        var counts = Recorded<LineupResponse>("team-lineup.json").Data!.Counts!;

        Assert.Equal(2, counts.Members);
        Assert.Equal(3, counts.Core);
        Assert.Equal(1, counts.Substitutes);
        Assert.Equal(0, counts.SharingNothing);
        Assert.Equal(0, counts.Pending);
    }

    [Fact]
    public void EveryCharacterCarriesWhatTheTableShows()
    {
        var characters = Recorded<LineupResponse>("team-lineup.json").Data!.Characters!;

        Assert.Equal(4, characters.Count);
        var first = characters[0];
        Assert.Equal("Char 29", first.Name);
        Assert.Equal("Dev Test Admin", first.Member);
        Assert.Equal(38, first.UserId);
        Assert.Equal(29, first.CharacterId);
        Assert.Equal("H1", first.Position);
        Assert.True(first.IsCore);
        Assert.False(first.IsPlaceholder);
        Assert.Null(characters[2].Position);
    }

    [Fact]
    public void TheNextOpenDateNamesItsEventAndWhatItLeavesOpen()
    {
        var next = Recorded<LineupResponse>("team-lineup.json").Data!.NextOpen!;

        Assert.Equal(9, next.EventId);
        Assert.Equal("2026-10-05", next.Date);
        var open = Assert.Single(next.Open!);
        Assert.Equal("M1", open.Position);
        Assert.Equal("absent", open.Reason);
        Assert.Null(open.Note);
    }

    /// <summary>
    /// The first recordings sent the event id as a string, while the calendar sends a number. The API made
    /// it a number, and the recordings above now carry one; this side still reads the string, so an answer
    /// cached or replayed from before the change cannot break it.
    /// </summary>
    [Fact]
    public void AnEventIdSentAsAStringStillReads()
    {
        const string json = """{ "event_id": "9", "title": "Raid Montag", "date": "2026-10-05", "time": "20:00", "open": [] }""";

        Assert.Equal(9, JsonSerializer.Deserialize<OpenDate>(json, EorzeaJson.Options)!.EventId);
    }

    [Fact]
    public void CoverageCarriesItsLists()
    {
        var coverage = Recorded<CoverageResponse>("team-coverage.json").Data!;

        Assert.Equal(9, coverage.EventId);
        Assert.Equal("Raid Montag", coverage.Title);
        var open = Assert.Single(coverage.Open!);
        Assert.Equal("u5", open.ParticipantKey);
        Assert.Empty(coverage.Unsure!);
        Assert.Empty(coverage.Covered!);
    }

    /// <summary>
    /// Recorded with one suggestion in the role of the open position and one in another, and both are
    /// placeholders: no player behind them, so <c>member</c> is null. The order is the server's, fitting
    /// role first, and the window keeps it.
    /// </summary>
    [Fact]
    public void SuggestionsComeInTheServersOrderWithTheFittingRoleFirst()
    {
        var suggestions = Recorded<CoverageResponse>("team-coverage.json").Data!.Suggestions!;

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(("Aushilfe Mira", true), (suggestions[0].Name, suggestions[0].Matches));
        Assert.Equal(("Aushilfe Theo", false), (suggestions[1].Name, suggestions[1].Matches));
        Assert.All(suggestions, s => Assert.Equal("placeholder", s.Kind));
        Assert.All(suggestions, s => Assert.Null(s.Member));
        Assert.Equal(9, suggestions[0].RosterEntryId);
        Assert.Null(suggestions[0].CharacterId);
    }
}

/// <summary>The caller's rights in a team: from the list the server sends, and from nothing else.</summary>
public sealed class TeamCapabilityTests
{
    private static TeamSummary Recorded()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "me-teams-lineup-summary.json");
        return JsonSerializer.Deserialize<TeamsResponse>(File.ReadAllText(path), EorzeaJson.Options)!.Data![0];
    }

    [Fact]
    public void TheOwnerMayManageMembers()
    {
        Assert.True(Recorded().Can(TeamCapability.ManageMembers));
    }

    [Fact]
    public void ARightTheListDoesNotNameIsNotHeld()
    {
        var team = new TeamSummary { Id = 1, Capabilities = ["view_history"] };

        Assert.False(team.Can(TeamCapability.ManageMembers));
    }

    /// <summary>
    /// A server that sends no list grants nothing. The invitation button then does not appear, which is the
    /// safe side: the alternative is every member meeting a 403, which is the design this replaced.
    /// </summary>
    [Fact]
    public void NoListGrantsNothing()
    {
        Assert.False(new TeamSummary { Id = 1 }.Can(TeamCapability.ManageMembers));
    }
}

/// <summary>The words for what a team is missing: the server's numbers, singular and plural, no arithmetic.</summary>
public sealed class LineupTextTests
{
    private static readonly Localizer De = new(Localizer.German);

    private static LineupSummary Summary((int T, int H, int D) targets, (int T, int H, int D) missing) => new()
    {
        Targets = new RoleCounts { Tank = targets.T, Healer = targets.H, Dps = targets.D },
        Missing = new RoleCounts { Tank = missing.T, Healer = missing.H, Dps = missing.D },
    };

    [Fact]
    public void ThePickerNamesWhatIsMissing()
    {
        var label = LineupText.TeamLabel("Test Static", Summary((1, 2, 3), (1, 0, 2)), De);

        Assert.Equal("Test Static · fehlt 1 Tank, 2 DPS", label);
    }

    [Fact]
    public void SingularAndPluralArePairsNotABracketedSuffix()
    {
        Assert.Equal("1 Heiler", LineupText.MissingList(new RoleCounts { Healer = 1 }, De));
        Assert.Equal("2 Tanks", LineupText.MissingList(new RoleCounts { Tank = 2 }, De));
    }

    [Fact]
    public void TheWrittenOutFormNamesEveryRoleWithATarget()
    {
        var detail = LineupText.MissingDetail(Summary((1, 2, 3), (1, 0, 2)), De);

        Assert.Equal("Tank: 1 von 1 fehlt · Heiler: besetzt · DPS: 2 von 3 fehlen", detail);
    }

    [Fact]
    public void ARoleWithoutATargetIsNotMentioned()
    {
        var detail = LineupText.MissingDetail(Summary((0, 2, 4), (0, 1, 0)), De);

        Assert.Equal("Heiler: 1 von 2 fehlt · DPS: besetzt", detail);
    }

    /// <summary>The contract: show nothing when every missing count is 0.</summary>
    [Fact]
    public void ATeamThatLacksNothingSaysNothing()
    {
        var full = Summary((2, 2, 4), (0, 0, 0));

        Assert.Equal("Full Team", LineupText.TeamLabel("Full Team", full, De));
        Assert.Null(LineupText.MissingDetail(full, De));
    }

    /// <summary>
    /// A server that does not send the summary yet is not a team that lacks nothing. Saying "complete"
    /// there would be a claim built out of a missing field, so it says nothing.
    /// </summary>
    [Fact]
    public void NoSummaryIsNotAFullTeam()
    {
        Assert.Equal("Old Server", LineupText.TeamLabel("Old Server", null, De));
        Assert.Null(LineupText.MissingDetail(null, De));
        Assert.Null(LineupText.MissingList(null, De));
    }
}
