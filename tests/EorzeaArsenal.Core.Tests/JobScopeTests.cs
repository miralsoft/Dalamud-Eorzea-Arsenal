using EorzeaArsenal.Api;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The job map, what may be sent, and the scope a push declares. These pin the two things the contract is
/// most explicit about and that are easiest to "simplify" back into data loss: the floor is compiled in
/// rather than derived from the server's table, and a push always says what it covered.
/// </summary>
public sealed class JobScopeTests
{
    /// <summary>
    /// The 21 codes exactly as the contract writes them out. Duplicated here on purpose: if either side
    /// edits its list, this fails rather than the two drifting quietly.
    /// </summary>
    private static readonly string[] ContractFloor =
    [
        "PLD", "WAR", "DRK", "GNB",
        "WHM", "SCH", "AST", "SGE",
        "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC",
        "BLM", "SMN", "RDM", "PCT",
    ];

    /// <summary>The 21 the contract names beyond the floor, by group.</summary>
    private static readonly string[] ContractWidening =
    [
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
        "MIN", "BTN", "FSH",
        "GLA", "MRD", "CNJ", "THM", "ARC", "LNC", "PGL", "ROG", "ACN",
        "BLU",
    ];

    [Fact]
    public void TheCombatFloorIsExactlyTheTwentyOneCodesInTheContract()
    {
        Assert.Equal(21, JobScope.CombatFloor.Count);
        Assert.Equal(
            ContractFloor.OrderBy(c => c, StringComparer.Ordinal),
            JobScope.CombatFloor.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void TheMapNamesEveryCodeTheContractLists()
    {
        var expected = ContractFloor.Concat(ContractWidening).ToArray();
        Assert.Equal(42, expected.Length);
        Assert.Equal(
            expected.OrderBy(c => c, StringComparer.Ordinal),
            JobMap.ValidCodes.OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>
    /// The 42 codes are exactly ClassJob rows 1 to 42; row 0 is "adventurer" and has no gearsets. A gap
    /// here would mean a job the plugin cannot name and therefore cannot send at all, whatever the
    /// server table says.
    /// </summary>
    [Fact]
    public void EveryClassJobRowFromOneToFortyTwoMapsToACode()
    {
        for (uint id = 1; id <= 42; id++)
        {
            Assert.NotNull(JobMap.ToCode(id));
        }

        Assert.Null(JobMap.ToCode(0));
        Assert.Null(JobMap.ToCode(43));
        Assert.Null(JobMap.ToCode(9999));
    }

    [Theory]
    [InlineData("CRP", JobMap.RoleHand)]
    [InlineData("CUL", JobMap.RoleHand)]
    [InlineData("MIN", JobMap.RoleLand)]
    [InlineData("FSH", JobMap.RoleLand)]
    [InlineData("PLD", JobMap.RoleCombat)]
    [InlineData("GLA", JobMap.RoleCombat)]
    [InlineData("BLU", JobMap.RoleCombat)]
    public void RolesGroupTheWayTheSplitNeeds(string code, string role) =>
        Assert.Equal(role, JobMap.RoleOf(code));

    [Fact]
    public void AnUnknownCodeHasNoRoleRatherThanAGuessedOne() => Assert.Null(JobMap.RoleOf("XYZ"));


    /// <summary>
    /// The distinction the window needs: a combat job has lists and may simply have none pinned, which
    /// the player can fix; hand, land and the base classes have nothing to pin, and telling somebody to
    /// pin one there sends them looking for a page that does not exist.
    /// </summary>
    [Theory]
    [InlineData("DRK", true)]
    [InlineData("WHM", true)]
    [InlineData("PCT", true)]
    [InlineData("GLA", false)]
    [InlineData("ACN", false)]
    [InlineData("ROG", false)]
    [InlineData("CRP", false)]
    [InlineData("MIN", false)]
    public void OnlyJobsWithListsAreOfferedAPin(string code, bool expected) =>
        Assert.Equal(expected, JobMap.HasBisCatalogue(code));

    [Fact]
    public void TheNineBaseClassesAreNamedAsSuch()
    {
        string[] expected = ["GLA", "MRD", "CNJ", "THM", "ARC", "LNC", "PGL", "ROG", "ACN"];

        Assert.All(expected, c => Assert.True(JobMap.IsBaseClass(c), c));
        Assert.Equal(9, JobMap.ValidCodes.Count(JobMap.IsBaseClass));
        Assert.False(JobMap.IsBaseClass("DRK"));
        Assert.False(JobMap.IsBaseClass(null));
    }

    /// <summary>
    /// Every base class is a battle class, so the role split and the catalogue question are two different
    /// questions about the same code. Answering one with the other is what made BLU a standing example
    /// on the other side of this contract.
    /// </summary>
    [Fact]
    public void ABaseClassIsCombatAndStillHasNoCatalogue()
    {
        foreach (var code in JobMap.ValidCodes.Where(JobMap.IsBaseClass))
        {
            Assert.Equal(JobMap.RoleCombat, JobMap.RoleOf(code));
            Assert.False(JobMap.HasBisCatalogue(code));
        }
    }
    [Fact]
    public void TheRoleGroupsCoverAllFortyTwoAndNothingTwice()
    {
        var byRole = JobMap.ValidCodes.GroupBy(JobMap.RoleOf).ToDictionary(g => g.Key!, g => g.Count());

        Assert.Equal(8, byRole[JobMap.RoleHand]);
        Assert.Equal(3, byRole[JobMap.RoleLand]);
        Assert.Equal(31, byRole[JobMap.RoleCombat]);
        Assert.Equal(42, byRole.Values.Sum());
    }

    /// <summary>
    /// The standing example from the contract, and the reason the floor is not a projection of the table:
    /// BLU is a combat job and is marked as one, yet it arrives with the widening to 42. Implementing
    /// scope combat as "whatever the table calls combat" is wrong on the first day, not at some future
    /// twenty-second combat job.
    /// </summary>
    [Fact]
    public void BlueMageIsACombatJobAndStillNotOnTheFloor()
    {
        Assert.Equal(JobMap.RoleCombat, JobMap.RoleOf("BLU"));
        Assert.Contains("BLU", JobMap.ValidCodes);
        Assert.DoesNotContain("BLU", JobScope.CombatFloor);
        Assert.Contains("PLD", JobScope.CombatFloor);
    }

    [Theory]
    [InlineData("CRP")]
    [InlineData("BSM")]
    [InlineData("MIN")]
    [InlineData("FSH")]
    [InlineData("GLA")]
    [InlineData("MRD")]
    [InlineData("ACN")]
    [InlineData("BLU")]
    public void TheWidenedCodesAreNotOnTheFloor(string code) =>
        Assert.DoesNotContain(code, JobScope.CombatFloor);

    [Fact]
    public void TheScopeValuesAreTheTwoTheContractNames()
    {
        Assert.Equal("combat", JobScope.Combat);
        Assert.Equal("all", JobScope.All);
    }

    /// <summary>
    /// The deciding paths need a scope of their own, and it is deliberately not gear:write: folding "may
    /// remove a row" into that one would silently widen every key already handed out.
    /// </summary>
    [Fact]
    public void GearReviewIsItsOwnScope()
    {
        Assert.Equal("gear:review", ScopeUtil.GearReview);
        Assert.NotEqual(ScopeUtil.GearWrite, ScopeUtil.GearReview);
        Assert.True(ScopeUtil.HasGearReview(["gear:write", "gear:review"]));
        Assert.False(ScopeUtil.HasGearReview(["gear:write", "gear:read"]));
        Assert.False(ScopeUtil.HasGearReview(null));
    }

    [Fact]
    public void TheFloorPolicyAllowsTheTwentyOneAndSaysCombat()
    {
        Assert.Equal(JobScope.Combat, JobPolicy.Floor.Scope);
        Assert.True(JobPolicy.Floor.Allows("PLD"));
        Assert.False(JobPolicy.Floor.Allows("CRP"));
        Assert.False(JobPolicy.Floor.Allows("BLU"));
        Assert.False(JobPolicy.Floor.Allows(null));
    }

    [Fact]
    public void ATablePolicyAllowsWhatTheTableListsAndSaysAll()
    {
        var policy = JobPolicy.FromTable(Table("PLD", "CRP", "MIN"));

        Assert.Equal(JobScope.All, policy.Scope);
        Assert.True(policy.Allows("CRP"));
        Assert.False(policy.Allows("WAR"));
    }

    /// <summary>
    /// An empty table is an answer that cannot be trusted, not a licence to send nothing under a full
    /// label: a push declaring "all" while reporting nothing would park every row the character has.
    /// </summary>
    [Fact]
    public void AnEmptyTableFallsBackToTheFloorRatherThanClaimingEverything()
    {
        var policy = JobPolicy.FromTable(new JobTableResponse());

        Assert.Equal(JobScope.Combat, policy.Scope);
        Assert.Same(JobPolicy.Floor, policy);
    }

    /// <summary>
    /// The explicit rule from the contract, and the one that looks wrong until you read why: the scope
    /// follows from which table governed, never from what happened to be dropped. A job outside the
    /// server table is a job it has no rows for, so there is nothing there for it to wrongly park.
    /// </summary>
    [Fact]
    public void DroppingSetsUnderARealTableStillDeclaresAll()
    {
        var policy = JobPolicy.FromTable(Table("DRK"));

        var filtered = policy.Apply(WithJobs("DRK", "CRP", "MIN"));

        Assert.Single(filtered.Gearsets);
        Assert.Equal(JobScope.All, policy.Scope);
    }

    [Fact]
    public void ApplyKeepsTheOrderAmongWhatStays()
    {
        var filtered = JobPolicy.Floor.Apply(WithJobs("DRK", "CRP", "WHM", "MIN", "PLD"));

        Assert.Equal(["DRK", "WHM", "PLD"], filtered.Gearsets.Select(s => s.Job));
    }

    [Fact]
    public void ApplyReturnsTheSameSnapshotWhenNothingIsDropped()
    {
        var data = WithJobs("DRK", "WHM");

        Assert.Same(data, JobPolicy.Floor.Apply(data));
    }

    /// <summary>
    /// A push declares its range even when the range is full. Only then does a missing scope mean "an
    /// older client" instead of "this one held back", which is the distinction the server parks on.
    /// </summary>
    [Fact]
    public void APayloadCarriesTheScopeItWasBuiltWith()
    {
        var snapshot = TestData.Snapshot(TestData.ExampleHash);

        Assert.Equal(JobScope.All, GearPayload.From(snapshot, JobScope.All).Scope);
        Assert.Equal(JobScope.Combat, GearPayload.From(snapshot, JobScope.Combat).Scope);
    }

    /// <summary>
    /// The default is the narrow value, so a path that forgets to state its scope under-claims.
    /// Over-claiming is the mistake that costs a player their rows.
    /// </summary>
    [Fact]
    public void TheUnsetScopeIsTheSafeDirection()
    {
        var payload = new GearPayload
        {
            Character = TestData.Snapshot(TestData.ExampleHash).Character,
            Gearsets = [],
        };

        Assert.Equal(JobScope.Combat, payload.Scope);
    }

    private static JobTableResponse Table(params string[] codes) => new()
    {
        Version = "2026-08-22",
        Jobs = [.. codes.Select(c => new JobEntry { Code = c, Combat = JobMap.RoleOf(c) == JobMap.RoleCombat })],
    };

    private static GearData WithJobs(params string[] jobs) => new()
    {
        Character = TestData.Snapshot(TestData.ExampleHash).Character,
        Gearsets = [.. jobs.Select((job, i) => new GearsetDto
        {
            GearIndex = i,
            Name = job + " set",
            Job = job,
            Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = 49671 } },
        })],
    };

}
