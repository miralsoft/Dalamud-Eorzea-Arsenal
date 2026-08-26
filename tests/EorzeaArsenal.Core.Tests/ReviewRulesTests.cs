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

    /// <summary>
    /// A delete removes the row and what hangs on it; a release freezes it for everybody who was following
    /// it. Neither has a way back through this endpoint, so the warning has to come before the click.
    /// </summary>
    [Theory]
    [InlineData(ReviewAction.Delete, true)]
    [InlineData(ReviewAction.Release, true)]
    [InlineData(ReviewAction.Ignore, false)]
    [InlineData(ReviewAction.Reopen, false)]
    [InlineData(ReviewAction.New, false)]
    [InlineData(ReviewAction.Link, false)]
    public void OnlyTheVerbsWithNoWayBackAreAskedTwice(string action, bool expected) =>
        Assert.Equal(expected, ReviewRules.IsIrreversible(action));

    /// <summary>
    /// A link is the exception that proves the rule: reversible onto a plugin row, a one-way door onto a
    /// hand-made one, so the verb alone cannot answer it and the candidate has to.
    /// </summary>
    [Fact]
    public void ALinkNeedsTheCandidateToKnowWhetherItIsAOneWayDoor()
    {
        Assert.False(ReviewRules.IsIrreversible(ReviewAction.Link));
        Assert.True(ReviewRules.IsAdoption(new ReviewCandidate { Source = GearsetSource.Manual }));
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

    /// <summary>
    /// The contract is explicit that one row may be a candidate in any number of questions: two newcomers
    /// may both point at the same orphan. So the same hand-made row can appear under several held rows, and
    /// counting it once per appearance overstates the number in front of an irreversible press.
    /// </summary>
    [Fact]
    public void AnAdoptionIsCountedOncePerPairNotOncePerAppearance()
    {
        var handMade = () => new ReviewCandidate { SetUid = "shared", Source = GearsetSource.Manual, Proposed = true };
        var state = new ReviewState
        {
            Held =
            [
                new()
                {
                    SetUid = "a",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "shared" },
                    Candidates = [handMade()],
                },
                // The same row offered again under a different question, which the contract allows.
                new()
                {
                    SetUid = "b",
                    Proposal = new ReviewProposal { Action = ReviewAction.New },
                    Candidates = [handMade()],
                },
                new()
                {
                    SetUid = "c",
                    Proposal = new ReviewProposal { Action = ReviewAction.New },
                    Candidates = [handMade()],
                },
            ],
        };

        var pairs = ReviewRules.MappingToAccept(state);

        Assert.Equal(3, pairs.Count);
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

    /// <summary>
    /// The window printed "never in game" whenever the server sent no timestamp, which turned a missing
    /// field into a claim. For every row a data migration brought in it stated the opposite of the truth,
    /// and it was the one sentence meant to explain the cause. Three cases, and the third stays a third.
    /// </summary>
    [Fact]
    public void AMissingDateIsNotAClaimThatTheSetNeverExisted()
    {
        var madeOnSite = new OrphanRow { SetUid = "a", Source = GearsetSource.Manual };
        var reported = new OrphanRow { SetUid = "b", Source = GearsetSource.Plugin, LastSeenAt = "2026-08-14T20:11:03+00:00" };
        var noDate = new OrphanRow { SetUid = "c", Source = GearsetSource.Plugin };

        Assert.Equal(OrphanOrigin.MadeOnSite, ReviewRules.OriginOf(madeOnSite));
        Assert.Equal(OrphanOrigin.LastReported, ReviewRules.OriginOf(reported));
        Assert.Equal(OrphanOrigin.Unknown, ReviewRules.OriginOf(noDate));
    }

    /// <summary>An empty string is as absent as a null one; the server has sent both.</summary>
    [Fact]
    public void AnEmptyDateCountsAsNoDate() =>
        Assert.Equal(
            OrphanOrigin.Unknown,
            ReviewRules.OriginOf(new OrphanRow { SetUid = "d", Source = GearsetSource.Plugin, LastSeenAt = string.Empty }));

    /// <summary>
    /// The window opens itself for a row nobody has seen and never again for the same one. Keyed on
    /// identity, because one row decided and another appearing leaves the count where it was, and a
    /// count based rule would stay silent exactly when it should speak.
    /// </summary>
    [Fact]
    public void OnlyRowsNobodyHasSeenAreAnnounced()
    {
        var state = new ReviewState
        {
            Held = [new HeldGearset { SetUid = "q1", Job = "DRK", GearIndex = 0 }],
            Orphans =
            [
                new OrphanRow { SetUid = "o1", Source = GearsetSource.Plugin, State = RowState.Parked },
                new OrphanRow { SetUid = "o2", Source = GearsetSource.Plugin, State = RowState.Parked },
            ],
        };

        Assert.Equal(["q1", "o1", "o2"], ReviewRules.Unannounced(state, new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal(["o2"], ReviewRules.Unannounced(state, new HashSet<string>(["q1", "o1"], StringComparer.Ordinal)));
        Assert.Empty(ReviewRules.Unannounced(state, new HashSet<string>(["q1", "o1", "o2"], StringComparer.Ordinal)));
    }

    /// <summary>
    /// A row put aside was shown once and decided. Announcing it again is how a marker becomes something
    /// people learn to ignore, so it never counts as new.
    /// </summary>
    [Fact]
    public void ARowPutAsideIsNeverAnnouncedAgain()
    {
        var state = new ReviewState
        {
            Orphans = [new OrphanRow { SetUid = "aside", Source = GearsetSource.Plugin, State = RowState.Ignored }],
        };

        Assert.Empty(ReviewRules.Unannounced(state, new HashSet<string>(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The count is the same before and after, and the answer is not: exactly the case a count based
    /// rule gets wrong.
    /// </summary>
    [Fact]
    public void ARowSwappedForAnotherIsStillNew()
    {
        var before = new HashSet<string>(["o1"], StringComparer.Ordinal);
        var after = new ReviewState
        {
            Orphans = [new OrphanRow { SetUid = "o2", Source = GearsetSource.Plugin, State = RowState.Parked }],
        };

        Assert.Single(after.OpenOrphans);
        Assert.Equal(["o2"], ReviewRules.Unannounced(after, before));
    }
}
