using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The whole chain against one realistic answer, rather than each rule against a minimal one.
/// </summary>
/// <remarks>
/// The scenarios the contract describes in prose, played through: the website-first morning, a mixed
/// inventory, a candidate offered under more than one question. Written because the rules all passed their
/// own tests while one of them still counted an adoption once per appearance instead of once per pair — a
/// mistake that only shows up when the data has the shape real data has.
/// </remarks>
public sealed class ReviewScenarioTests
{
    /// <summary>
    /// Twenty hand-made rows and twenty new gearsets: one question per row, every answer offered is an
    /// adoption, and the whole mapping is one press with the count said out loud.
    /// </summary>
    [Fact]
    public void TheWebsiteFirstMorningIsOnePressWithAnHonestCount()
    {
        var state = WebsiteFirst(20);

        var pairs = ReviewRules.MappingToAccept(state);

        Assert.Equal(20, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(ReviewAction.Link, p.Action));
        Assert.Equal(20, ReviewRules.AdoptionCount(state, pairs));
    }

    /// <summary>
    /// The correction the contract insists goes first: strike one out, answer it on its own. The rest still
    /// goes in one press, and the struck one is not answered by it.
    /// </summary>
    [Fact]
    public void AStruckQuestionLeavesTheMappingAndStaysAQuestion()
    {
        var state = WebsiteFirst(20);
        var struck = new HashSet<string>(StringComparer.Ordinal) { "held-7" };

        var pairs = ReviewRules.MappingToAccept(state, struck);

        Assert.Equal(19, pairs.Count);
        Assert.DoesNotContain("held-7", pairs.Select(p => p.SetUid));
        Assert.Equal(19, ReviewRules.AdoptionCount(state, pairs));

        // Still on the list, still asking. Striking out defers; it does not decide.
        Assert.Contains("held-7", state.Held.Select(h => h.SetUid));
    }

    /// <summary>
    /// Every question preselects what the server proposed, and never the highest score. Here every second
    /// question has a better-scoring candidate that the mapping gave to somebody else.
    /// </summary>
    [Fact]
    public void EveryQuestionPreselectsTheProposalNotTheScore()
    {
        var state = WebsiteFirst(6, withHigherScoringRival: true);

        foreach (var held in state.Held)
        {
            var chosen = ReviewRules.Preselected(held);

            Assert.NotNull(chosen);
            Assert.True(chosen!.Proposed);
            Assert.Equal(held.Proposal!.TargetUid, chosen.SetUid);

            var best = held.Candidates.MaxBy(c => c.Probability)!;
            Assert.True(best.Probability > chosen.Probability, "the rival should score higher");
        }
    }

    /// <summary>
    /// A mixed inventory: parked plugin rows, one put aside, and a hand-made one somebody set aside. Each
    /// gets exactly the verbs that apply, and no orphan ever offers attribution.
    /// </summary>
    [Fact]
    public void AMixedInventoryOffersOnlyWhatAppliesToEachRow()
    {
        var parked = Orphan("p1", GearsetSource.Plugin, RowState.Parked);
        var ignoredPlugin = Orphan("p2", GearsetSource.Plugin, RowState.Ignored);
        var ignoredManual = Orphan("m1", GearsetSource.Manual, RowState.Ignored);

        Assert.Equal([ReviewAction.Ignore, ReviewAction.Release, ReviewAction.Delete], ReviewRules.OfferedVerbs(parked));
        Assert.Equal([ReviewAction.Reopen, ReviewAction.Release, ReviewAction.Delete], ReviewRules.OfferedVerbs(ignoredPlugin));
        Assert.Equal([ReviewAction.Reopen], ReviewRules.OfferedVerbs(ignoredManual));

        foreach (var row in new[] { parked, ignoredPlugin, ignoredManual })
        {
            var verbs = ReviewRules.OfferedVerbs(row);
            Assert.DoesNotContain(ReviewAction.Link, verbs);
            Assert.DoesNotContain(ReviewAction.New, verbs);
            Assert.All(verbs, v => Assert.True(row.IsFromPlugin || v != ReviewAction.Delete, "delete on a hand-made row"));
        }
    }

    /// <summary>
    /// The open and put-aside split a window shows as "10 open, 3 put aside", straight off the state.
    /// </summary>
    [Fact]
    public void TheInventorySplitsIntoOpenAndPutAside()
    {
        var state = new ReviewState
        {
            StateToken = "t",
            Orphans =
            [
                Orphan("a", GearsetSource.Plugin, RowState.Parked),
                Orphan("b", GearsetSource.Plugin, RowState.Parked),
                Orphan("c", GearsetSource.Plugin, RowState.Ignored),
                Orphan("d", GearsetSource.Manual, RowState.Ignored),
            ],
        };

        Assert.Equal(2, state.OpenOrphans.Count());
        Assert.Equal(2, state.PutAsideOrphans.Count());
    }

    /// <summary>
    /// A proposal of "new" contributes a pair that names no target, and is not an adoption.
    /// </summary>
    [Fact]
    public void AProposedNewIsAPairWithoutATargetAndIsNoAdoption()
    {
        var state = new ReviewState
        {
            StateToken = "t",
            Held =
            [
                new()
                {
                    SetUid = "held-1",
                    Proposal = new ReviewProposal { Action = ReviewAction.New, TargetUid = null },
                    Candidates = [new() { SetUid = "cand", Source = GearsetSource.Manual, Probability = 40 }],
                },
            ],
        };

        var pairs = ReviewRules.MappingToAccept(state);

        var only = Assert.Single(pairs);
        Assert.Equal(ReviewAction.New, only.Action);
        Assert.Null(only.TargetUid);
        Assert.Equal(0, ReviewRules.AdoptionCount(state, pairs));
        Assert.Null(ReviewRules.Preselected(state.Held[0]));
    }

    private static ReviewState WebsiteFirst(int count, bool withHigherScoringRival = false) => new()
    {
        StateToken = "9f13c0a2",
        Held =
        [
            .. Enumerable.Range(0, count).Select(i =>
            {
                var target = $"manual-{i}";
                var candidates = new List<ReviewCandidate>
                {
                    new()
                    {
                        SetUid = target,
                        Job = "DRK",
                        Name = $"Website set {i}",
                        Source = GearsetSource.Manual,
                        Probability = 74,
                        MatchedSlots = 9,
                        TotalSlots = 12,
                        Proposed = true,
                        HasPin = true,
                    },
                };

                if (withHigherScoringRival)
                {
                    candidates.Add(new ReviewCandidate
                    {
                        SetUid = $"rival-{i}",
                        Job = "DRK",
                        Name = $"Contested {i}",
                        Source = GearsetSource.Manual,
                        Probability = 93,
                        Proposed = false,
                        BlockedBy = $"held-{(i + 1) % count}",
                    });
                }

                return new HeldGearset
                {
                    SetUid = $"held-{i}",
                    Job = "DRK",
                    Name = $"In game {i}",
                    GearIndex = i,
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = target, Confident = true },
                    Candidates = candidates,
                };
            }),
        ],
    };

    private static OrphanRow Orphan(string uid, string source, string state) => new()
    {
        SetUid = uid,
        Job = "DRK",
        Name = $"Row {uid}",
        Source = source,
        State = state,
    };
}
