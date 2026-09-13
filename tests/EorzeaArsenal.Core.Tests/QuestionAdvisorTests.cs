using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// What a question is, before the buttons under it mean anything.
/// </summary>
/// <remarks>
/// The card drew the comparison, the percentages and four controls and left the reader to work out which
/// one was theirs. These are the sentences that replaced that, and the rule worth testing is the same one
/// the inventory follows: name an answer only where naming it cannot be wrong.
/// </remarks>
public sealed class QuestionAdvisorTests
{
    private static HeldGearset Held(string? action, bool confident) => new()
    {
        SetUid = "new",
        Job = "DRK",
        Name = "DRK Test 2",
        Proposal = action is null ? null : new ReviewProposal { Action = action, Confident = confident },
        Candidates = [new ReviewCandidate { SetUid = "old", Name = "DRK Test 1", Proposed = true }],
    };

    [Fact]
    public void AVouchedForSuggestionMayBeNamedAsTheOrdinaryAnswer()
    {
        var held = Held(ReviewAction.Link, confident: true);

        Assert.Equal(QuestionVerdict.ConfidentLink, QuestionAdvisor.VerdictOf(held));
        Assert.True(QuestionAdvisor.MayRecommend(held));
    }

    /// <summary>
    /// The case that matters most. At 75 % the server proposes and does not vouch, and a card that reads
    /// the two the same puts its own certainty on somebody else's guess.
    /// </summary>
    [Fact]
    public void ASuggestionWithoutConfidenceIsNotRecommended()
    {
        var held = Held(ReviewAction.Link, confident: false);

        Assert.Equal(QuestionVerdict.UncertainLink, QuestionAdvisor.VerdictOf(held));
        Assert.False(QuestionAdvisor.MayRecommend(held));
    }

    /// <summary>
    /// "It is new" is a proposal like any other, and it is never the confident one: the mapping had nothing
    /// left for this newcomer, which is not the same as knowing it is new.
    /// </summary>
    [Fact]
    public void ProposingANewSetIsNeverARecommendation()
    {
        var held = Held(ReviewAction.New, confident: true);

        Assert.Equal(QuestionVerdict.ProposesNew, QuestionAdvisor.VerdictOf(held));
        Assert.False(QuestionAdvisor.MayRecommend(held));
    }

    /// <summary>
    /// The one control that decides more than one thing at once follows the same line as everything else:
    /// what the server vouched for may be applied without looking, what it merely suggested may not.
    /// Without this the window argued with itself, telling the reader on the card that a proposal was not
    /// backed and offering, two lines higher, to accept twenty of them in one press.
    /// </summary>
    [Theory]
    [InlineData(ReviewAction.Link, true, true)]
    [InlineData(ReviewAction.Link, false, true)]
    [InlineData(ReviewAction.New, true, true)]
    [InlineData(null, false, true)]
    public void ASingleCandidateAlwaysRidesAlongBecauseThereIsNothingToDecide(string? action, bool confident, bool expected) =>
        Assert.Equal(expected, QuestionAdvisor.SafeForBulk(Held(action, confident)));

    /// <summary>
    /// The one combination that stays out: a choice existed and the server could not settle it. One line
    /// in a list cannot show the alternatives, so the shortcut would be deciding something unseen.
    /// </summary>
    [Fact]
    public void SeveralCandidatesWithNoVouchingStayOut()
    {
        var held = Held(ReviewAction.Link, confident: false);
        held.Candidates.Add(new ReviewCandidate { SetUid = "second", Name = "Another" });

        Assert.False(QuestionAdvisor.SafeForBulk(held));
    }

    /// <summary>
    /// Several candidates alone do not keep a pairing out: where the server vouched for its pick there is
    /// nothing left in doubt, and the line says a choice existed.
    /// </summary>
    [Fact]
    public void SeveralCandidatesDoNotByThemselvesKeepAPairingOut()
    {
        var held = Held(ReviewAction.Link, confident: true);
        held.Candidates.Add(new ReviewCandidate { SetUid = "second", Name = "Another" });

        Assert.Equal(2, held.Candidates.Count);
        Assert.True(QuestionAdvisor.SafeForBulk(held));
    }

    /// <summary>An older server sends no proposal. Then the card says so instead of inventing one.</summary>
    [Fact]
    public void NoProposalRecommendsNothing()
    {
        var held = Held(null, confident: false);

        Assert.Equal(QuestionVerdict.NoProposal, QuestionAdvisor.VerdictOf(held));
        Assert.False(QuestionAdvisor.MayRecommend(held));
    }
}
