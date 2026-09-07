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

    /// <summary>
    /// Nor on a row a team follows, and for a reason one step removed from the hand-made case. There the
    /// server refuses outright; here it converts, carrying the delete out as a release so that one person
    /// cannot remove a set other people depend on. A press would therefore work and do something else,
    /// which is worse than a refusal, and the button that does that something else is already on the card
    /// with the right word on it. Two buttons doing one thing, one of them lying about it, is not a choice.
    /// This is not the refusal the contract rejected: nothing is blocked, the path is one button to the
    /// left, and the card says why this one is missing.
    /// </summary>
    [Fact]
    public void DeleteIsNotOfferedOnARowATeamFollows()
    {
        var shared = Orphan(GearsetSource.Plugin, RowState.Parked, share: true);

        var verbs = ReviewRules.OfferedVerbs(shared);

        Assert.DoesNotContain(ReviewAction.Delete, verbs);
        Assert.Contains(ReviewAction.Release, verbs);
        Assert.Contains(ReviewAction.Ignore, verbs);
    }

    /// <summary>And it comes back the moment the share does not: the rule is about the row, not the job.</summary>
    [Fact]
    public void DeleteReturnsOnceNoTeamFollowsTheRow()
    {
        var verbs = ReviewRules.OfferedVerbs(Orphan(GearsetSource.Plugin, RowState.Parked));

        Assert.Contains(ReviewAction.Delete, verbs);
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
    /// A delete removes the row and what hangs on it; a release hands it out of the plugin and freezes it
    /// for everybody following it; a link overwrites the target's contents. All three change enough that
    /// the sentence belongs before the click rather than beside it.
    /// </summary>
    [Theory]
    [InlineData(ReviewAction.Delete, true)]
    [InlineData(ReviewAction.Release, true)]
    [InlineData(ReviewAction.Link, true)]
    [InlineData(ReviewAction.Ignore, false)]
    [InlineData(ReviewAction.Reopen, false)]
    [InlineData(ReviewAction.New, false)]
    public void EveryVerbThatChangesSomethingIsAskedTwice(string action, bool expected) =>
        Assert.Equal(expected, ReviewRules.NeedsConfirming(action));

    /// <summary>
    /// Narrower than <see cref="ReviewRules.NeedsConfirming"/>, and the difference is the whole point:
    /// exactly one verb may be called irreversible.
    /// <b>A release left this list</b> the day <c>released_at</c> arrived, because <c>reopen</c> puts a
    /// released row back to a parked plugin row.
    /// <b>A link left it later</b>, and for a different kind of reason: the verb really is one-way, but the
    /// target survives with its uid, its pin and its shares, and what it used to hold can be built again in
    /// the web editor. A dialog that says "this cannot be undone" over that spends credit it did not earn,
    /// and every sentence like it teaches the reader to skim the next one.
    /// </summary>
    [Theory]
    [InlineData(ReviewAction.Delete, true)]
    [InlineData(ReviewAction.Link, false)]
    [InlineData(ReviewAction.Release, false)]
    [InlineData(ReviewAction.Ignore, false)]
    [InlineData(ReviewAction.Reopen, false)]
    [InlineData(ReviewAction.New, false)]
    public void OnlyWhatCannotBeTakenBackAnywhereMaySaySo(string action, bool expected) =>
        Assert.Equal(expected, ReviewRules.IsIrreversible(action));

    /// <summary>
    /// Losing the strong heading must not lose the second click. A link is still a verb somebody should
    /// look at before pressing, and that is what <see cref="ReviewRules.NeedsConfirming"/> is for: the two
    /// predicates were split precisely so "ask again" and "say it is for ever" could stop being the same
    /// question. The price of a link is still named by the candidate, and onto a hand-made row it is the
    /// set somebody built there.
    /// </summary>
    [Fact]
    public void ALinkStillAsksTwiceWithoutClaimingItIsForEver()
    {
        Assert.True(ReviewRules.NeedsConfirming(ReviewAction.Link));
        Assert.False(ReviewRules.IsIrreversible(ReviewAction.Link));
        Assert.True(ReviewRules.IsAdoption(new ReviewCandidate { Source = GearsetSource.Manual }));
    }

    /// <summary>
    /// And the one that keeps the pair honest in the other direction: every verb that may say "for ever"
    /// must also be one that asks twice. A verb that claimed the strong sentence without a second click
    /// would put the worst outcome behind the fewest presses.
    /// </summary>
    [Theory]
    [InlineData(ReviewAction.Delete)]
    [InlineData(ReviewAction.Release)]
    [InlineData(ReviewAction.Link)]
    [InlineData(ReviewAction.Ignore)]
    [InlineData(ReviewAction.Reopen)]
    [InlineData(ReviewAction.New)]
    public void NothingIsCalledForEverWithoutBeingAskedTwice(string action)
    {
        if (ReviewRules.IsIrreversible(action))
        {
            Assert.True(ReviewRules.NeedsConfirming(action));
        }
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

    /// <summary>
    /// The bug this guard was moved into the rule for. A question with several candidates and no vouching
    /// is one the shortcut may not answer, and the window knew that: it greyed the pairing out, wrote in
    /// words that the decision belongs on the card, offered no way to pull it in, and put "2 of 2" on the
    /// button. Then it composed the request from the player's strikes alone, and the server applied three
    /// pairs. The screen and the wire were two computations that agreed only by accident, so the rule now
    /// lives where the request is built and the drawing side reads it back rather than repeating it.
    /// </summary>
    [Fact]
    public void AQuestionTheShortcutMayNotAnswerIsNotInTheMappingEvenUnstruck()
    {
        var contested = new HeldGearset
        {
            SetUid = "contested",
            Job = "DRG",
            Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "b-side", Confident = false },
            Candidates =
            [
                new ReviewCandidate { SetUid = "b-side", Probability = 18, Proposed = true },
                new ReviewCandidate { SetUid = "a-side", Probability = 9 },
            ],
        };

        var state = StateWith(Held("plain", ReviewAction.Link, "target-a"), contested);

        Assert.False(QuestionAdvisor.SafeForBulk(contested));

        var pairs = ReviewRules.MappingToAccept(state);

        var only = Assert.Single(pairs);
        Assert.Equal("plain", only.SetUid);
    }

    /// <summary>
    /// And the other half of the same rule: a lone candidate rides along whatever its score, because
    /// nothing is being decided there. Without this the website-first case, where every score is low, would
    /// meet the wall of one-by-one clicks the shortcut exists to spare it.
    /// </summary>
    [Fact]
    public void ALoneCandidateStaysInTheMappingHoweverLowItScores()
    {
        var lonely = new HeldGearset
        {
            SetUid = "lonely",
            Job = "BRD",
            Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "only-row", Confident = false },
            Candidates = [new ReviewCandidate { SetUid = "only-row", Probability = 9, Proposed = true }],
        };

        var pairs = ReviewRules.MappingToAccept(StateWith(lonely));

        var only = Assert.Single(pairs);
        Assert.Equal("only-row", only.TargetUid);
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

    /// <summary>
    /// The bug this whole rule was extracted for. The delete confirmation said "keeps its pinned BiS set",
    /// which belongs to a candidate answered away with <c>new</c> and is the exact opposite of what a
    /// delete does. It was the last sentence anybody read before the row was gone.
    /// </summary>
    [Fact]
    public void DeletingAPinnedRowSaysThePinIsLost()
    {
        var row = Orphan(GearsetSource.Plugin, RowState.Parked, pin: true);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Delete, row);

        Assert.Contains(ReviewConsequence.LosesItsPin, costs);
        Assert.DoesNotContain(ReviewConsequence.KeepsItsPin, costs);
    }

    /// <summary>
    /// The plainest of these and the one that was missing. On a row with no pin and no share the
    /// confirmation had exactly one line in it, about a pinned target the row did not have, and nothing at
    /// all about the row being removed. Under a button labelled "delete", whether the gearset in game goes
    /// with it is the question somebody actually has, and the dialog answered a different one.
    /// </summary>
    [Fact]
    public void DeletingAlwaysSaysTheRowGoesAndTheGearsetDoesNot()
    {
        var bare = Orphan(GearsetSource.Plugin, RowState.Parked);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Delete, bare);

        Assert.Contains(ReviewConsequence.RowIsRemoved, costs);

        // And nothing is said about losing a pin that is not there.
        Assert.DoesNotContain(ReviewConsequence.ComesBackWithoutItsPin, costs);
        Assert.DoesNotContain(ReviewConsequence.LosesItsPin, costs);
    }

    /// <summary>
    /// The other half: a delete on a shared row is performed as a release, so nothing is removed and this
    /// sentence must not appear. Saying "the stored row is removed" over an action that keeps it would be
    /// the same class of mistake in the opposite direction.
    /// </summary>
    [Fact]
    public void ASharedRowIsNotDescribedAsRemoved()
    {
        var shared = Orphan(GearsetSource.Plugin, RowState.Parked, share: true);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Delete, shared);

        Assert.DoesNotContain(ReviewConsequence.RowIsRemoved, costs);
        Assert.Contains(ReviewConsequence.DeleteBecomesRelease, costs);
        Assert.Contains(ReviewConsequence.LeavesPluginGovernance, costs);
    }

    /// <summary>
    /// A confirmation reads in the order somebody agrees to something: what am I doing, then what falls
    /// out of it. The list used to open with the team line, so the result stood above the deed and the
    /// reader had to work backwards to what they had pressed. The one exception is a delete a share turns
    /// into a release, where the word on the button is wrong and nothing else means anything until that
    /// is said.
    /// </summary>
    [Fact]
    public void TheDeedComesBeforeWhatItCosts()
    {
        var shared = Orphan(GearsetSource.Plugin, RowState.Parked, pin: true, share: true);

        var onRelease = ReviewRules.ConsequencesOf(ReviewAction.Release, shared);
        Assert.Equal(ReviewConsequence.LeavesPluginGovernance, onRelease[0]);
        Assert.Equal(ReviewConsequence.TeamKeepsSeeingIt, onRelease[1]);

        var onDelete = ReviewRules.ConsequencesOf(ReviewAction.Delete, shared);
        Assert.Equal(ReviewConsequence.DeleteBecomesRelease, onDelete[0]);
        Assert.Equal(ReviewConsequence.LeavesPluginGovernance, onDelete[1]);

        var plain = ReviewRules.ConsequencesOf(ReviewAction.Delete, Orphan(GearsetSource.Plugin, RowState.Parked, pin: true));
        Assert.Equal(ReviewConsequence.RowIsRemoved, plain[0]);
    }

    /// <summary>
    /// A release says what it does, on every row. With a team it used to say only what the team would
    /// keep seeing, and on a row with neither team nor pin the confirmation came out empty and fell back
    /// to repeating the label on the button. Both left out the one thing the press actually does.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AReleaseAlwaysSaysTheRowLeavesThePlugin(bool pin, bool share)
    {
        var row = Orphan(GearsetSource.Plugin, RowState.Parked, pin: pin, share: share);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Release, row);

        Assert.Contains(ReviewConsequence.LeavesPluginGovernance, costs);

        // And never says the row goes, because it does not.
        Assert.DoesNotContain(ReviewConsequence.RowIsRemoved, costs);
    }

    /// <summary>
    /// And the distinction the first fix would have missed: a delete on a shared row is performed as a
    /// release, so the row survives and its pin survives with it. There "keeps its pin" is right after all.
    /// </summary>
    [Fact]
    public void DeletingASharedPinnedRowKeepsThePinAndSaysWhy()
    {
        var row = Orphan(GearsetSource.Plugin, RowState.Parked, pin: true, share: true);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Delete, row);

        Assert.Contains(ReviewConsequence.DeleteBecomesRelease, costs);
        Assert.Contains(ReviewConsequence.KeepsItsPin, costs);
        Assert.DoesNotContain(ReviewConsequence.LosesItsPin, costs);
        Assert.DoesNotContain(ReviewConsequence.ComesBackWithoutItsPin, costs);
    }

    /// <summary>Releasing never removes the row, so the pin is never at risk there.</summary>
    [Fact]
    public void ReleasingAPinnedRowKeepsThePin()
    {
        var row = Orphan(GearsetSource.Plugin, RowState.Parked, pin: true);

        var costs = ReviewRules.ConsequencesOf(ReviewAction.Release, row);

        Assert.Contains(ReviewConsequence.KeepsItsPin, costs);
        Assert.DoesNotContain(ReviewConsequence.LosesItsPin, costs);
    }

    /// <summary>A verb that takes nothing away asks nothing, so there is no second click to earn.</summary>
    [Theory]
    [InlineData(ReviewAction.Ignore)]
    [InlineData(ReviewAction.Reopen)]
    public void AReversibleVerbHasNothingToWarnAbout(string verb)
    {
        var row = Orphan(GearsetSource.Plugin, RowState.Parked, pin: true, share: true);

        Assert.Empty(ReviewRules.ConsequencesOf(verb, row));
    }

    /// <summary>
    /// The case that was on screen: a set renamed, re-geared and moved arrives as a question, and the row
    /// it used to be is parked. That row is both an unclaimed candidate and an orphan, so the server lists
    /// it twice and the window drew it twice, with a full comparison under each.
    /// </summary>
    [Fact]
    public void ARowAnOpenQuestionAsksAboutIsNotAlsoOfferedInTheInventory()
    {
        var state = new ReviewState
        {
            Held = [new HeldGearset { SetUid = "new", Candidates = [new() { SetUid = "old" }] }],
            Orphans = [new OrphanRow { SetUid = "old", Job = "DRK", Source = GearsetSource.Plugin, State = RowState.Parked }],
        };

        Assert.Empty(ReviewRules.InventoryToOffer(state));
    }

    /// <summary>
    /// Why it is a rule and not tidiness: the inventory card offers delete. Deleting the row there and then
    /// answering "that is the one" would name a target that no longer exists, and on the row this was found
    /// on there was a pinned target hanging from it.
    /// </summary>
    [Fact]
    public void TheHiddenRowIsExactlyTheOneTheProposalPointsAt()
    {
        var state = new ReviewState
        {
            Held =
            [
                new HeldGearset
                {
                    SetUid = "new",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "old" },
                    Candidates = [new() { SetUid = "old", Proposed = true }],
                },
            ],
            Orphans =
            [
                new OrphanRow { SetUid = "old", Job = "DRK", Source = GearsetSource.Plugin, State = RowState.Parked, HasPin = true },
                new OrphanRow { SetUid = "other", Job = "WAR", Source = GearsetSource.Plugin, State = RowState.Parked },
            ],
        };

        var offered = ReviewRules.InventoryToOffer(state);

        var only = Assert.Single(offered);
        Assert.Equal("other", only.SetUid);
    }

    /// <summary>Without an open question the inventory is untouched: this hides nothing on its own.</summary>
    [Fact]
    public void WithNoQuestionEveryOpenOrphanIsOffered()
    {
        var state = new ReviewState
        {
            Orphans =
            [
                new OrphanRow { SetUid = "a", Source = GearsetSource.Plugin, State = RowState.Parked },
                new OrphanRow { SetUid = "b", Source = GearsetSource.Plugin, State = RowState.Ignored },
            ],
        };

        var offered = ReviewRules.InventoryToOffer(state);

        var only = Assert.Single(offered);
        Assert.Equal("a", only.SetUid);
    }
    private static OrphanRow Orphan(string source, string? state, bool pin = false, bool share = false) => new()
    {
        SetUid = "7bb2",
        Job = "DRK",
        Name = "Dunkelritter",
        Source = source,
        State = state,
        HasPin = pin,
        HasTeamShare = share,
        TeamNames = share ? ["Kreszentia"] : [],
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
