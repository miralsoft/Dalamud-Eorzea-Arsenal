using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// What the card says about a row, and the much shorter list of cases where it names a verb outright.
/// </summary>
/// <remarks>
/// The window used to lay out a comparison and then stop, which left "73 %, three slots different" as the
/// whole answer to "what do I do now". These are the sentences that replaced it, and the point of testing
/// them is the recommendation: a wrong one here sends somebody to delete a set they wanted.
/// </remarks>
public sealed class OrphanAdvisorTests
{
    private static ItemDto Piece(int id, params int[] materia) => new() { Id = id, Materia = [.. materia] };

    private static OrphanRow Row(
        string source = GearsetSource.Plugin,
        string? state = RowState.Parked,
        bool pin = false,
        bool share = false,
        string? released = null,
        Dictionary<string, ItemDto>? items = null,
        Dictionary<string, ItemDto>? similar = null) => new()
    {
        SetUid = "aaaa",
        Job = "DRK",
        Name = "Set 29",
        Source = source,
        State = state,
        HasPin = pin,
        HasTeamShare = share,
        ReleasedAt = released,
        TeamNames = share ? ["Kreszentia"] : [],
        Items = items ?? new Dictionary<string, ItemDto>(StringComparer.Ordinal) { ["Weapon"] = Piece(1) },
        Similar = similar is null
            ? []
            : [new SimilarSet { SetUid = "bbbb", Job = "DRK", Name = "DRK Kreszentia", Items = similar }],
    };

    private static OrphanAdvice Advise(OrphanRow row) =>
        OrphanAdvisor.For(row, row.Similar.Count > 0 ? SetComparison.Compare(row.Items, row.Similar[0].Items) : []);

    private static Dictionary<string, ItemDto> Set(params (string Slot, ItemDto Piece)[] pieces)
    {
        var items = new Dictionary<string, ItemDto>(StringComparer.Ordinal);
        foreach (var (slot, piece) in pieces)
        {
            items[slot] = piece;
        }

        return items;
    }

    /// <summary>
    /// The one case where a recommendation cannot be wrong: everything in this row is in a row that is
    /// still there, down to the melds, and nothing else hangs on it.
    /// </summary>
    [Fact]
    public void ACopyWithNothingAttachedIsRecommendedForDeletion()
    {
        var gear = Set(("Weapon", Piece(1, 10, 10)), ("Head", Piece(2)));
        var advice = Advise(Row(items: gear, similar: Set(("Weapon", Piece(1, 10, 10)), ("Head", Piece(2)))));

        Assert.Equal(OrphanVerdict.Copy, advice.Verdict);
        Assert.Equal(ReviewAction.Delete, advice.Recommended);
        Assert.Equal("DRK Kreszentia", advice.OtherName);
    }

    /// <summary>
    /// A pinned target is not part of the comparison, so a copy carrying one is not a free delete. The
    /// verdict stays true and the recommendation goes away: this is the exact case the window exists for.
    /// </summary>
    [Fact]
    public void ACopyCarryingAPinRecommendsNothing()
    {
        var gear = Set(("Weapon", Piece(1)));
        var advice = Advise(Row(pin: true, items: gear, similar: Set(("Weapon", Piece(1)))));

        Assert.Equal(OrphanVerdict.Copy, advice.Verdict);
        Assert.Null(advice.Recommended);
    }

    /// <summary>Same, for a row other people are following. Deleting it is performed as a release.</summary>
    [Fact]
    public void ACopyCarryingATeamShareRecommendsNothing()
    {
        var gear = Set(("Weapon", Piece(1)));
        var advice = Advise(Row(share: true, items: gear, similar: Set(("Weapon", Piece(1)))));

        Assert.Equal(OrphanVerdict.Copy, advice.Verdict);
        Assert.Null(advice.Recommended);
    }

    /// <summary>
    /// The trap, as a verdict. Both sets read as a full match on the server's count, and calling this a
    /// copy would recommend throwing away melds somebody paid for.
    /// </summary>
    [Fact]
    public void SamePiecesWithOtherMeldsIsNotACopyAndRecommendsNothing()
    {
        var gear = Set(("Weapon", Piece(1, 10, 10)));
        var advice = Advise(Row(items: gear, similar: Set(("Weapon", Piece(1, 10, 11)))));

        Assert.Equal(OrphanVerdict.SameGearOtherMateria, advice.Verdict);
        Assert.Null(advice.Recommended);
    }

    /// <summary>
    /// The case in front of the player when this was reported: it resembles something without being it.
    /// The count of differing slots rides along, because "three slots carry something else" is the fact
    /// that makes the sentence usable and a percentage is not.
    /// </summary>
    [Fact]
    public void AResemblanceNamesHowManySlotsDifferAndRecommendsNothing()
    {
        var gear = Set(("Weapon", Piece(1)), ("Head", Piece(2)), ("Body", Piece(3)));
        var other = Set(("Weapon", Piece(9)), ("Head", Piece(2)), ("Body", Piece(8)));
        var advice = Advise(Row(items: gear, similar: other));

        Assert.Equal(OrphanVerdict.Similar, advice.Verdict);
        Assert.Equal(2, advice.DifferentSlots);
        Assert.Null(advice.Recommended);
    }

    [Fact]
    public void ARowNothingResemblesSaysSoAndRecommendsNothing()
    {
        var advice = Advise(Row());

        Assert.Equal(OrphanVerdict.Unique, advice.Verdict);
        Assert.Null(advice.Recommended);
        Assert.Null(advice.OtherName);
    }

    /// <summary>
    /// A hand-made row is judged before anything else is looked at, because the server refuses to delete
    /// one however much it resembles something. Recommending a verb that comes back as an error is worse
    /// than recommending none.
    /// </summary>
    [Fact]
    public void AHandMadeRowIsNeverRecommendedForDeletionEvenAsACopy()
    {
        var gear = Set(("Weapon", Piece(1)));
        var advice = Advise(Row(source: GearsetSource.Manual, state: null, items: gear, similar: Set(("Weapon", Piece(1)))));

        Assert.Equal(OrphanVerdict.HandMade, advice.Verdict);
        Assert.Equal(ReviewAction.Ignore, advice.Recommended);
    }

    /// <summary>
    /// A released row is `manual` like a hand-made one, and until the mark existed the two were one case.
    /// Reading it as hand-made called somebody's own set a stranger and hid the only way back.
    /// </summary>
    [Fact]
    public void AReleasedRowIsNotReadAsHandMade()
    {
        var row = Row(source: GearsetSource.Manual, state: RowState.Ignored, released: "2026-08-31 19:51:01");

        Assert.Equal(OrphanOrigin.Released, ReviewRules.OriginOf(row));
        Assert.Equal(OrphanVerdict.Released, Advise(row).Verdict);
    }

    /// <summary>Releasing was a deliberate decision. The way back is offered, never urged.</summary>
    [Fact]
    public void AReleasedRowRecommendsNothing()
    {
        var row = Row(source: GearsetSource.Manual, state: RowState.Ignored, released: "2026-08-31 19:51:01");

        Assert.Null(Advise(row).Recommended);
    }

    /// <summary>
    /// Even where it looks exactly like a set that is still there. The verdict is about who governs the
    /// row, and a released row is not a copy to be swept up: somebody put it aside on purpose.
    /// </summary>
    [Fact]
    public void AReleasedCopyIsStillJudgedAsReleased()
    {
        var gear = Set(("Weapon", Piece(1)));
        var row = Row(source: GearsetSource.Manual, state: RowState.Ignored, released: "2026-08-31 19:51:01", items: gear, similar: Set(("Weapon", Piece(1))));

        Assert.Equal(OrphanVerdict.Released, Advise(row).Verdict);
        Assert.Null(Advise(row).Recommended);
    }

    /// <summary>Once it has been put aside there is nothing left to suggest: they already answered.</summary>
    [Fact]
    public void AHandMadeRowAlreadyPutAsideRecommendsNothing()
    {
        var advice = Advise(Row(source: GearsetSource.Manual, state: RowState.Ignored));

        Assert.Equal(OrphanVerdict.HandMade, advice.Verdict);
        Assert.Null(advice.Recommended);
    }
}
