using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The model held against a recorded answer of the real server, not against one written to match it.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of what a missing field does here: nothing. A property the model does not declare
/// is dropped in silence, the window draws without it, and the gap only shows up as a sentence that never
/// appears. Two fields went missing exactly that way and were found by reading a recorded answer rather
/// than by anything failing.
/// </para>
/// <para>
/// The file is <c>docs/plugin/samples/gear-review-held.json</c> from the API side, copied verbatim: two
/// pushes, the second with a set renamed, re-geared in one slot and moved. It carries the one case the
/// live dev data has never produced, a row whose attribution is an open question.
/// </para>
/// </remarks>
public sealed class ReviewShapeTests
{
    private static ReviewState Recorded()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "gear-review-held.json");
        return JsonSerializer.Deserialize<ReviewState>(File.ReadAllText(path), EorzeaJson.Options)!;
    }

    [Fact]
    public void TheRecordedAnswerCarriesAQuestionAndAnOrphan()
    {
        var state = Recorded();

        Assert.Equal("17040ee1f990e326400b57cb4b09364f649f445d023cac9686c4ea9033ab9442", state.StateToken);
        Assert.Single(state.Held);
        Assert.Single(state.Orphans);
    }

    /// <summary>
    /// All eighteen candidate fields, named one by one. A count would pass while the wrong field was
    /// missing, and the two that were actually missing here were the ones nothing else looked at.
    /// </summary>
    [Fact]
    public void EveryCandidateFieldSurvivesDeserialisation()
    {
        var candidate = Recorded().Held[0].Candidates[0];

        Assert.Equal("7473daa6e052ec3daf4c6e07845c7cdc", candidate.SetUid);
        Assert.Equal("DRK", candidate.Job);
        Assert.Equal("FRU DRK", candidate.Name);
        Assert.Equal(GearsetSource.Plugin, candidate.Source);
        Assert.Equal(RowState.Parked, candidate.State);
        Assert.Equal(75, candidate.Probability);
        Assert.Equal(3, candidate.MatchedSlots);
        Assert.Equal(4, candidate.TotalSlots);
        Assert.True(candidate.SameJob);
        Assert.True(candidate.Proposed);
        Assert.Null(candidate.BlockedBy);
        Assert.False(candidate.HasPin);
        Assert.False(candidate.HasTeamShare);
        Assert.Empty(candidate.TeamNames);
        Assert.False(candidate.Hidden);
        Assert.Equal(4, candidate.Items.Count);
        Assert.Equal("2026-08-31 08:22:46", candidate.LastSeenAt);
        Assert.EndsWith("#s-7473daa6e052ec3daf4c6e07845c7cdc", candidate.Url);
    }

    /// <summary>
    /// The proposal, which is what a dialog preselects. Note <c>confident: false</c>: at 75 % the server
    /// proposes and does not vouch, and a window that preselects without saying so is putting its own
    /// certainty on the server's suggestion.
    /// </summary>
    [Fact]
    public void TheProposalIsReadWithItsConfidence()
    {
        var held = Recorded().Held[0];

        Assert.Equal(ReviewAction.Link, held.Proposal!.Action);
        Assert.Equal("7473daa6e052ec3daf4c6e07845c7cdc", held.Proposal.TargetUid);
        Assert.False(held.Proposal.Confident);
        Assert.False(ReviewRules.IsConfident(held));
        Assert.Same(held.Candidates[0], ReviewRules.Preselected(held));
    }

    /// <summary>The held row carries a url. The contract's example does not show one; the server sends it.</summary>
    [Fact]
    public void TheHeldRowCanBeOpenedOnTheWebsite()
    {
        Assert.EndsWith("#s-fa63549a03f4b0171d2b5a8db9308e2f", Recorded().Held[0].Url);
    }

    /// <summary>
    /// All nine fields of a resemblance, and the one thing about it that is easy to get wrong: the row it
    /// points at may itself be a row that is still under question.
    /// </summary>
    [Fact]
    public void AnOrphanSaysWhichSetItResembles()
    {
        var state = Recorded();
        var similar = Assert.Single(state.Orphans[0].Similar);

        Assert.Equal("fa63549a03f4b0171d2b5a8db9308e2f", similar.SetUid);
        Assert.Equal("DRK", similar.Job);
        Assert.Equal("Rechte Hand", similar.Name);
        Assert.Equal(GearsetSource.Plugin, similar.Source);
        Assert.Equal(75, similar.Probability);
        Assert.Equal(3, similar.MatchedSlots);
        Assert.Equal(4, similar.TotalSlots);
        Assert.Equal(4, similar.Items.Count);
        Assert.EndsWith("#s-fa63549a03f4b0171d2b5a8db9308e2f", similar.Url);

        Assert.Equal(state.Held[0].SetUid, similar.SetUid);
    }

    /// <summary>
    /// The comparison the card draws, against the recorded pair. Three slots agree down to the materia and
    /// the gloves differ, which is exactly the change that produced the question.
    /// </summary>
    [Fact]
    public void TheRecordedPairDiffersInOneSlot()
    {
        var state = Recorded();
        var pairs = SetComparison.Compare(state.Orphans[0].Items, state.Orphans[0].Similar[0].Items);

        Assert.Equal(4, pairs.Count);
        Assert.Equal((3, 0, 1), SetComparison.Counts(pairs));
        Assert.False(SetComparison.IsCopy(pairs));

        var gloves = Assert.Single(pairs, p => p.Slot == "Hands");
        Assert.Equal(SlotAgreement.Different, gloves.Agreement);
        Assert.Equal(44102, gloves.Mine!.Id);
        Assert.Equal(51010, gloves.Theirs!.Id);
    }

    /// <summary>
    /// The slots come out in the order a character sheet reads, not in the order the server happened to
    /// serialise them, because the two strips only mean something if they are in step.
    /// </summary>
    [Fact]
    public void TheComparisonIsInSheetOrder()
    {
        var state = Recorded();
        var pairs = SetComparison.Compare(state.Held[0].Items, state.Held[0].Candidates[0].Items);

        Assert.Equal(["Weapon", "Head", "Body", "Hands"], pairs.Select(p => p.Slot));
    }
}
