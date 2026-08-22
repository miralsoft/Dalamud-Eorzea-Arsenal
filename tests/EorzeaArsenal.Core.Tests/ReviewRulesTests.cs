using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// What a window may offer and what it must preselect. Each of these exists because getting it wrong makes
/// the server refuse something a player was offered, and an offer that gets refused is worse than a missing
/// one: they did nothing wrong and get an error for it.
/// </summary>
public sealed class ReviewRulesTests
{
    /// <summary>
    /// The case the contract spells out: on a hand-made row that was put aside there is exactly one verb.
    /// Delete is rejected on hand-made rows, release does not apply to one that is already hand-made, and
    /// ignore is what put it there. One button, not one live beside three dead ones.
    /// </summary>
    [Fact]
    public void AHandMadeRowPutAsideOffersOnlyTheWayBack()
    {
        var verbs = ReviewRules.OfferedVerbs(Orphan(GearsetSource.Manual, RowState.Ignored));

        Assert.Equal([ReviewAction.Reopen], verbs);
    }

    [Fact]
    public void AParkedPluginRowOffersTheInventoryVerbs()
    {
        var verbs = ReviewRules.OfferedVerbs(Orphan(GearsetSource.Plugin, RowState.Parked));

        Assert.Contains(ReviewAction.Ignore, verbs);
        Assert.Contains(ReviewAction.Release, verbs);
        Assert.Contains(ReviewAction.Delete, verbs);
        Assert.DoesNotContain(ReviewAction.Reopen, verbs);
    }

    [Fact]
    public void AnIgnoredPluginRowOffersTheWayBackInsteadOfIgnoreAgain()
    {
        var verbs = ReviewRules.OfferedVerbs(Orphan(GearsetSource.Plugin, RowState.Ignored));

        Assert.Contains(ReviewAction.Reopen, verbs);
        Assert.DoesNotContain(ReviewAction.Ignore, verbs);
    }

    /// <summary>Attribution belongs to a question, never to an inventory.</summary>
    [Theory]
    [InlineData(GearsetSource.Plugin, RowState.Parked)]
    [InlineData(GearsetSource.Plugin, RowState.Ignored)]
    [InlineData(GearsetSource.Manual, RowState.Ignored)]
    public void NoOrphanEverOffersAttribution(string source, string state)
    {
        var verbs = ReviewRules.OfferedVerbs(Orphan(source, state));

        Assert.DoesNotContain(ReviewAction.Link, verbs);
        Assert.DoesNotContain(ReviewAction.New, verbs);
    }

    /// <summary>Delete is never offered on a hand-made row, whatever state it is in.</summary>
    [Fact]
    public void DeleteIsNeverOfferedOnAHandMadeRow()
    {
        Assert.DoesNotContain(ReviewAction.Delete, ReviewRules.OfferedVerbs(Orphan(GearsetSource.Manual, RowState.Ignored)));
        Assert.DoesNotContain(ReviewAction.Delete, ReviewRules.OfferedVerbs(Orphan(GearsetSource.Manual, null)));
    }


    /// <summary>
    /// The case that made these two fields exist. Two newcomers fit row X best, X can go to one of them,
    /// so the other one is proposed Y even though X scores higher for it. Preselect by score and the accept
    /// call names a pair the server never proposed, and a player who did nothing wrong gets a 409.
    /// </summary>
    [Fact]
    public void ThePreselectionFollowsTheProposalEvenWhenAnotherScoresHigher()
    {
        var held = new HeldGearset
        {
            SetUid = "c05e",
            Job = "DRK",
            Candidates =
            [
                new() { SetUid = "x", Probability = 91, Proposed = false, BlockedBy = "other" },
                new() { SetUid = "y", Probability = 62, Proposed = true },
            ],
        };

        var chosen = ReviewRules.Preselected(held);

        Assert.Equal("y", chosen!.SetUid);
    }

    /// <summary>
    /// None proposed means the mapping has nothing left for this newcomer, and the proposal says so
    /// outright rather than leaving it to be inferred from a missing flag.
    /// </summary>
    [Fact]
    public void NothingIsPreselectedWhenTheMappingProposesNew()
    {
        var held = new HeldGearset
        {
            SetUid = "c05e",
            Proposal = new ReviewProposal { Action = ReviewAction.New, TargetUid = null },
            Candidates = [new() { SetUid = "x", Probability = 80, Proposed = false }],
        };

        Assert.Null(ReviewRules.Preselected(held));
    }

    [Fact]
    public void AdoptionIsWhatALinkOntoAHandMadeRowIs()
    {
        Assert.True(ReviewRules.IsAdoption(new ReviewCandidate { Source = GearsetSource.Manual }));
        Assert.False(ReviewRules.IsAdoption(new ReviewCandidate { Source = GearsetSource.Plugin }));
    }

    [Fact]
    public void OnlyACandidateWithSomethingOnItEarnsASentence()
    {
        Assert.True(ReviewRules.LeavesSomethingBehind(new ReviewCandidate { HasPin = true }));
        Assert.True(ReviewRules.LeavesSomethingBehind(new ReviewCandidate { HasTeamShare = true }));
        Assert.False(ReviewRules.LeavesSomethingBehind(new ReviewCandidate()));
    }

    /// <summary>The mapping is the server proposals, and only those.</summary>
    [Fact]
    public void TheMappingIsComposedFromTheProposals()
    {
        var state = StateWith(
            Held("a", ReviewAction.Link, "target-a"),
            Held("b", ReviewAction.New, null));

        var pairs = ReviewRules.MappingToAccept(state);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("target-a", pairs[0].TargetUid);
        Assert.Null(pairs[1].TargetUid);
        Assert.Equal(ReviewAction.New, pairs[1].Action);
    }

    /// <summary>
    /// Striking a pair out leaves it out of the mapping. It is not answered: it stays a question, and the
    /// player answers it on its own — which is also the only sequence that works, since a single decision
    /// mints a new token and makes the mapping in hand stale.
    /// </summary>
    [Fact]
    public void AStruckPairIsLeftOutRatherThanAnswered()
    {
        var state = StateWith(
            Held("a", ReviewAction.Link, "target-a"),
            Held("b", ReviewAction.Link, "target-b"));

        var pairs = ReviewRules.MappingToAccept(state, new HashSet<string>(StringComparer.Ordinal) { "a" });

        var only = Assert.Single(pairs);
        Assert.Equal("b", only.SetUid);
    }

    [Fact]
    public void AHeldRowWithNoProposalIsNotInTheMapping()
    {
        var state = StateWith(Held("a", null, null));

        Assert.Empty(ReviewRules.MappingToAccept(state));
    }

    /// <summary>
    /// The number the dialog names: each adoption is a row that starts following the game from then on, and
    /// putting twenty of them behind one press is exactly why that sentence has to carry a count.
    /// </summary>
    [Fact]
    public void AdoptionsAreCountedForTheDialog()
    {
        var state = new ReviewState
        {
            Held =
            [
                new()
                {
                    SetUid = "a",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "hand-made" },
                    Candidates = [new() { SetUid = "hand-made", Source = GearsetSource.Manual, Proposed = true }],
                },
                new()
                {
                    SetUid = "b",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "from-push" },
                    Candidates = [new() { SetUid = "from-push", Source = GearsetSource.Plugin, Proposed = true }],
                },
            ],
        };

        var pairs = ReviewRules.MappingToAccept(state);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(1, ReviewRules.AdoptionCount(state, pairs));
    }

    private static ReviewState StateWith(params HeldGearset[] held) => new()
    {
        StateToken = "9f13",
        Held = [.. held],
    };

    private static HeldGearset Held(string uid, string? action, string? target) => new()
    {
        SetUid = uid,
        Job = "DRK",
        Proposal = action is null ? null : new ReviewProposal { Action = action, TargetUid = target },
    };
    private static OrphanRow Orphan(string source, string? state) => new()
    {
        SetUid = "7bb2",
        Job = "DRK",
        Name = "Dunkelritter",
        Source = source,
        State = state,
    };
}
