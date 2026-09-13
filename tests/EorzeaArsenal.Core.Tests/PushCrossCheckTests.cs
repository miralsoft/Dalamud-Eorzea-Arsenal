using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Checking the server's guess against what this side remembered. The value of this is entirely in what it
/// stays quiet about, so most of what follows is about not crying wolf.
/// </summary>
public sealed class PushCrossCheckTests
{
    private const string UidA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string UidB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string UidC = "cccccccccccccccccccccccccccccccc";

    private static GearsetDto Set(int index, int weapon, string name = "Revolverklinge") => new()
    {
        GearIndex = index,
        Job = "GNB",
        Name = name,
        Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = weapon } },
    };

    private static CachedGearsetIdentity Remembered(string uid, int index, int weapon, string name = "Revolverklinge") => new()
    {
        SetUid = uid,
        Job = "GNB",
        Name = name,
        GearIndex = index,
        GearKey = GearsetFingerprint.Gear(Set(index, weapon, name)),
        ItemsKey = GearsetFingerprint.Strong(Set(index, weapon, name)),
    };

    private static GearsetAssignment Says(int index, string uid, string rung) =>
        new() { GearIndex = index, SetUid = uid, MatchedBy = rung };

    /// <summary>
    /// The case this exists for. Two sets of one job that the player never named apart, reordered, and the
    /// server breaking the tie on the one value the reorder just changed. Here it swapped them, and the
    /// gear says so: the set the server calls A is carrying what B was carrying.
    /// </summary>
    [Fact]
    public void ASwapUnderAGuessedRungIsCaught()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 200) };
        var sent = new[] { Set(0, 200), Set(1, 100) };
        var answer = new[]
        {
            Says(0, UidA, MatchedBy.NameAmbiguous),
            Says(1, UidB, MatchedBy.NameAmbiguous),
        };

        var contested = PushCrossCheck.Contested(previous, sent, answer);

        Assert.Equal([UidA, UidB], contested.OrderBy(u => u));
    }

    /// <summary>
    /// The same shape on the <c>index</c> rung, which is the other one the server admits to guessing on and
    /// the one it falls to when the name and the gear both found nothing.
    /// </summary>
    [Fact]
    public void TheIndexRungIsCheckedToo()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 200) };
        var sent = new[] { Set(0, 200), Set(1, 100) };
        var answer = new[] { Says(0, UidA, MatchedBy.Index), Says(1, UidB, MatchedBy.Index) };

        Assert.NotEmpty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// A minted identity is not a guess, it is the server stating it has never seen this set, and gear this
    /// side remembers under another identity contradicts that just as squarely.
    /// </summary>
    [Fact]
    public void AMintedIdentityForRememberedGearIsCaught()
    {
        var previous = new[] { Remembered(UidA, 0, 100) };
        var sent = new[] { Set(0, 999), Set(1, 100) };
        var answer = new[] { Says(0, UidA, MatchedBy.Exact), Says(1, UidC, MatchedBy.New) };

        // UidA was re-geared and kept its name, so it no longer holds what it used to; the gear it used to
        // hold turned up at position 1 under a fresh identity. Both are in doubt.
        Assert.Equal([UidA, UidC], PushCrossCheck.Contested(previous, sent, answer).OrderBy(u => u));
    }

    /// <summary>
    /// Copying a gearset is the most common thing a player does here and it must stay silent. The copy is
    /// minted new and carries the original's gear, but the original kept both its identity and its
    /// contents, so nothing moved: the gear simply belongs to two sets now.
    /// </summary>
    [Fact]
    public void ACopiedGearsetIsNotAContradiction()
    {
        var previous = new[] { Remembered(UidA, 0, 100) };
        var sent = new[] { Set(0, 100), Set(1, 100) };
        var answer = new[] { Says(0, UidA, MatchedBy.Exact), Says(1, UidC, MatchedBy.New) };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// And it stays silent whichever order the two arrive in, since the copy can as easily sit above the
    /// original in the list.
    /// </summary>
    [Fact]
    public void ACopiedGearsetIsNotAContradictionInEitherOrder()
    {
        var previous = new[] { Remembered(UidA, 1, 100) };
        var sent = new[] { Set(0, 100), Set(1, 100) };
        var answer = new[] { Says(0, UidC, MatchedBy.New), Says(1, UidA, MatchedBy.Exact) };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// Two remembered rows already sharing a fingerprint cannot witness against anything: that gear was
    /// never a distinction, so it has nothing to say about which of them is which now. This is the case in
    /// the live report that prompted the whole check, and the honest answer there is silence.
    /// </summary>
    [Fact]
    public void GearTwoRememberedRowsSharedSaysNothing()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 100) };
        var sent = new[] { Set(0, 100), Set(1, 100) };
        var answer = new[]
        {
            Says(0, UidB, MatchedBy.NameAmbiguous),
            Says(1, UidA, MatchedBy.NameAmbiguous),
        };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// Where the server matched on evidence of its own and the name was that evidence, this side does not
    /// second-guess it. A disagreement there is far more likely to be a re-gear that made one set look like
    /// another used to than a mix-up.
    /// </summary>
    [Theory]
    [InlineData(MatchedBy.Exact)]
    [InlineData(MatchedBy.Name)]
    [InlineData(MatchedBy.Items)]
    public void ASettledRungOnANameOfItsOwnIsLeftAlone(string rung)
    {
        var previous = new[] { Remembered(UidA, 0, 100, "FRU"), Remembered(UidB, 1, 200, "Ultimate") };
        var sent = new[] { Set(0, 200, "FRU"), Set(1, 100, "Ultimate") };
        var answer = new[] { Says(0, UidA, rung), Says(1, UidB, rung) };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// The case that made this check nearly useless when it was first written, and the ordinary shape of
    /// the whole problem. Swap two sets that share a job and a name and the server matches both on
    /// <c>exact</c>: its exact rung is job, name and position together, so where the name belongs to two
    /// sets it is the position and nothing else. It never reaches the rung it would have admitted to
    /// guessing on, and answers confidently the wrong way round.
    /// </summary>
    /// <remarks>
    /// Gating on the rung alone left this silent, which meant the check only ever fired where the server
    /// had already owned up. Since the game names a new gearset after its job, two sets of one job share a
    /// name from the moment they exist, so this is not a corner case.
    /// </remarks>
    [Fact]
    public void ASwapOfTwoSetsSharingANameIsCaughtOnAnyRung()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 200) };
        var sent = new[] { Set(0, 200), Set(1, 100) };
        var answer = new[] { Says(0, UidA, MatchedBy.Exact), Says(1, UidB, MatchedBy.Exact) };

        Assert.Equal([UidA, UidB], PushCrossCheck.Contested(previous, sent, answer).OrderBy(u => u));
    }

    /// <summary>
    /// A guess that agrees with what this side remembered is the ordinary outcome and says nothing.
    /// </summary>
    [Fact]
    public void AGuessThatAgreesIsNotReported()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 200) };
        var sent = new[] { Set(0, 100), Set(1, 200) };
        var answer = new[]
        {
            Says(0, UidA, MatchedBy.NameAmbiguous),
            Says(1, UidB, MatchedBy.NameAmbiguous),
        };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// A row learned from <c>GET /gear/sets</c> carries no gear at all, since that route returns no items.
    /// It can neither witness nor be contradicted, and must not be read as an empty fingerprint that every
    /// other empty one matches.
    /// </summary>
    [Fact]
    public void ARowWithNoRememberedGearIsNotEvidence()
    {
        var previous = new[] { new CachedGearsetIdentity { SetUid = UidA, Job = "GNB", Name = "Revolverklinge" } };
        var sent = new[] { Set(0, 100) };
        var answer = new[] { Says(0, UidB, MatchedBy.NameAmbiguous) };

        Assert.Empty(PushCrossCheck.Contested(previous, sent, answer));
    }

    /// <summary>
    /// The first push of an installation has nothing to compare against and must not invent a doubt.
    /// </summary>
    [Fact]
    public void AnEmptyCacheContradictsNothing()
    {
        var sent = new[] { Set(0, 100) };
        var answer = new[] { Says(0, UidA, MatchedBy.New) };

        Assert.Empty(PushCrossCheck.Contested([], sent, answer));
    }

    /// <summary>
    /// One disagreement does not spread. Everything the two accounts agree on stays out of it, or a single
    /// muddled pair would empty the whole window.
    /// </summary>
    [Fact]
    public void OnlyTheSetsInDoubtAreNamed()
    {
        var previous = new[] { Remembered(UidA, 0, 100), Remembered(UidB, 1, 200), Remembered(UidC, 2, 300) };
        var sent = new[] { Set(0, 200), Set(1, 100), Set(2, 300) };
        var answer = new[]
        {
            Says(0, UidA, MatchedBy.NameAmbiguous),
            Says(1, UidB, MatchedBy.NameAmbiguous),
            Says(2, UidC, MatchedBy.NameAmbiguous),
        };

        Assert.Equal([UidA, UidB], PushCrossCheck.Contested(previous, sent, answer).OrderBy(u => u));
    }
}
