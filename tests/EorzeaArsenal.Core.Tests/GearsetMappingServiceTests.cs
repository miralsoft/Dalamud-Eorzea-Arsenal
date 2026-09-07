using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The local gearset mapping cache. What is being guarded here is not "does it find things" but the two
/// properties that keep it a cache rather than a second source of truth: it never invents an identity,
/// and when it cannot answer it says so instead of falling back to the position — the position being the
/// one value guaranteed to be wrong in the case this whole mechanism exists for.
/// </summary>
public sealed class GearsetMappingServiceTests
{
    private const string Cid = TestData.ExampleHash;
    private const string UidA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string UidB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string UidC = "cccccccccccccccccccccccccccccccc";

    private static GearsetDto Set(int index, string job, string? name, int weapon) => new()
    {
        GearIndex = index,
        Job = job,
        Name = name,
        Items = new Dictionary<string, ItemDto> { ["Weapon"] = new() { Id = weapon } },
    };

    private static (GearsetMappingService Service, FakeApiClient Api, InMemoryGearsetIdentityStore Store, CapturingLog Log)
        Build()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        var store = new InMemoryGearsetIdentityStore();
        var log = new CapturingLog();
        return (new GearsetMappingService(api, tokens, store, new TestClock(), log), api, store, log);
    }

    [Fact]
    public void APushTeachesTheMappingAndItResolves()
    {
        var (service, _, _, _) = Build();
        var sent = new[] { Set(0, "DRK", "2.50", 100), Set(1, "WHM", "Heal", 200) };

        service.RecordPush(Cid, sent, new[]
        {
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.New },
        });

        Assert.True(service.ServerMintsUids);
        Assert.Equal(UidA, service.Resolve(Cid, sent[0]).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, sent[1]).SetUid);
    }

    /// <summary>
    /// The acceptance criterion of the whole feature, on this side of it: move the sets around and every
    /// identity stays with the gearset it belonged to. The position is not part of either cache key, so
    /// this holds whether or not the plugin was running while the list was reordered.
    /// </summary>
    [Fact]
    public void ReorderingChangesNothingButThePosition()
    {
        var (service, _, _, _) = Build();
        var sent = new[] { Set(0, "DRK", "2.50", 100), Set(1, "WHM", "Heal", 200) };
        service.RecordPush(Cid, sent, new[]
        {
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        });

        // The same two gearsets, positions swapped end to end.
        Assert.Equal(UidA, service.Resolve(Cid, Set(1, "DRK", "2.50", 100)).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, Set(0, "WHM", "Heal", 200)).SetUid);
    }

    [Fact]
    public void ResolvingTwiceInARowGivesTheSameAnswer()
    {
        var (service, _, _, _) = Build();
        var set = Set(3, "DRK", "2.50", 100);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 3, SetUid = UidA }]);

        Assert.Equal(service.Resolve(Cid, set), service.Resolve(Cid, set));
    }

    /// <summary>
    /// Re-gearing keeps the name, so the weak key still bridges. This is the rung that makes the cache
    /// useful between a gear change and the next push.
    /// </summary>
    [Fact]
    public void ReGearingIsStillFoundByJobAndName()
    {
        var (service, _, _, _) = Build();
        service.RecordPush(Cid, [Set(0, "DRK", "2.50", 100)], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);

        Assert.Equal(UidA, service.Resolve(Cid, Set(0, "DRK", "2.50", 999)).SetUid);
    }

    /// <summary>
    /// A rename changes both content keys, so once the set has also moved there is nothing left to
    /// recognise it by, and this side must not pretend otherwise. The server finds it on the items rung at
    /// the next push and the mapping is repaired then; until that happens the interface shows nothing,
    /// which is the correct half of "never something wrong". A rename on its own is a different matter,
    /// since the set is still where it was: see <see cref="ARenamedSetStillResolvesWhereItSits"/>.
    /// </summary>
    [Fact]
    public void ARenameAndAMoveTogetherAreAMissRatherThanAWrongAnswer()
    {
        var (service, _, _, _) = Build();
        service.RecordPush(Cid, [Set(0, "DRK", "2.50", 100)], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);

        var match = service.Resolve(Cid, Set(4, "DRK", "Savage", 999));

        Assert.False(match.IsResolved);
        Assert.Null(match.SetUid);
    }

    [Fact]
    public void AJobChangeIsNeverTheSameGearset()
    {
        var (service, _, _, _) = Build();
        service.RecordPush(Cid, [Set(0, "DRK", "2.50", 100)], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);

        Assert.False(service.Resolve(Cid, Set(0, "WHM", "2.50", 100)).IsResolved);
    }

    /// <summary>
    /// Two gearsets with the same job and name, re-geared so the strong key no longer matches either.
    /// The server pairs such rows in position order; this side refuses, because the position is exactly
    /// what cannot be trusted. Reported as ambiguous rather than as "nothing found", because the
    /// difference is worth showing.
    /// </summary>
    [Fact]
    public void TwinsAreReportedAsAmbiguousRatherThanGuessedAt()
    {
        var (service, _, _, _) = Build();
        service.RecordPush(
            Cid,
            [Set(0, "DRK", "Twin", 100), Set(1, "DRK", "Twin", 200)],
            [
                new GearsetAssignment { GearIndex = 0, SetUid = UidA },
                new GearsetAssignment { GearIndex = 1, SetUid = UidB },
            ]);

        var match = service.Resolve(Cid, Set(0, "DRK", "Twin", 300));

        Assert.False(match.IsResolved);
        Assert.True(match.WasAmbiguous);
    }

    /// <summary>
    /// The case the guard was missing for, found in a live report rather than by reading. Two gearsets
    /// with the same job, the same name <b>and</b> the same gear carry the same strong key, and the
    /// strong-key search used to return the first of them and call the rung exact. Two live sets resolved
    /// to one identity, the position table wrote one uid twice so the last one won, and two windows
    /// printed the same set number for two different sets, with nothing reporting a doubt because the
    /// doubt was never detected. Same job, same name, same items is the strongest evidence the contents
    /// can give, and it is still not a distinction.
    /// </summary>
    /// <remarks>
    /// Both sets have moved here, which is what leaves the strong key alone with the question. Where
    /// nothing has moved their positions tell them apart, and that is the ordinary case:
    /// <see cref="IdenticalTwinsResolveByWhereTheySit"/>.
    /// </remarks>
    [Fact]
    public void IdenticalTwinsAreAmbiguousOnTheStrongKeyToo()
    {
        var (service, _, _, _) = Build();

        service.RecordPush(Cid, [Set(0, "BRD", "Barde", 100), Set(1, "BRD", "Barde", 100)], [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB },
        ]);

        var match = service.Resolve(Cid, Set(5, "BRD", "Barde", 100));

        Assert.False(match.IsResolved);
        Assert.True(match.WasAmbiguous);

        // And the same for the other one, which is the half that made it dangerous: it did not fail, it
        // answered with somebody else's identity.
        Assert.False(service.Resolve(Cid, Set(6, "BRD", "Barde", 100)).IsResolved);
    }

    /// <summary>
    /// The case the whole position rung exists for. A player never names a gearset when they make one:
    /// the game writes the job in, so two sets of one job are called the same thing from the start, and
    /// if they also hold the same gear nothing derived from their contents can tell them apart. Their
    /// positions can, and the position is the one value here that came from an answer rather than from a
    /// guess. Before this they both came back ambiguous and lost their comparison.
    /// </summary>
    [Fact]
    public void IdenticalTwinsResolveByWhereTheySit()
    {
        var (service, _, _, _) = Build();
        var first = Set(3, "DRK", "Dunkelritter", 100);
        var second = Set(7, "DRK", "Dunkelritter", 100);

        service.RecordPush(Cid, [first, second], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        Assert.Equal(UidA, service.Resolve(Cid, first).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, second).SetUid);
    }

    /// <summary>
    /// And it survives a rename, which is the whole point of anchoring on the gear rather than on the
    /// strong key: that one takes the name in with it, so renaming a set used to drop it to a weak key
    /// two identically named sets share.
    /// </summary>
    [Fact]
    public void ARenamedSetStillResolvesWhereItSits()
    {
        var (service, _, _, _) = Build();
        var before = Set(3, "DRK", "Dunkelritter", 100);

        service.RecordPush(Cid, [before, Set(7, "DRK", "Dunkelritter", 100)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        Assert.Equal(UidA, service.Resolve(Cid, Set(3, "DRK", "FRU DRK", 100)).SetUid);
    }

    /// <summary>
    /// A re-geared set keeps its place too, but only where its name belongs to nothing else. Where two
    /// sets share a name, swapping them would otherwise be indistinguishable from re-gearing one, and the
    /// position would hand back the wrong identity with no doubt attached. That is worse than the
    /// ambiguity it replaces, so the rung declines and the content ladder takes over.
    /// </summary>
    [Fact]
    public void ARegearedSetKeepsItsPlaceOnlyWhereItsNameIsItsOwn()
    {
        var (service, _, _, _) = Build();

        service.RecordPush(Cid, [Set(3, "DRK", "FRU DRK", 100), Set(7, "WHM", "Heal", 200)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        // Unique name, gear changed: the place decides.
        Assert.Equal(UidA, service.Resolve(Cid, Set(3, "DRK", "FRU DRK", 999)).SetUid);

        var (twins, _, _, _) = Build();
        twins.RecordPush(Cid, [Set(3, "DRK", "Dunkelritter", 100), Set(7, "DRK", "Dunkelritter", 200)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        // Shared name, gear changed: this side cannot tell a re-gear from a swap, and says so.
        var match = twins.Resolve(Cid, Set(3, "DRK", "Dunkelritter", 999));
        Assert.False(match.IsResolved);
        Assert.True(match.WasAmbiguous);
    }

    /// <summary>
    /// Reordering moves the position, which is exactly when the content ladder has to take over. Two sets
    /// of one job with different gear, swapped: each is found at its new place by what is in it, which is
    /// the property this whole feature exists to keep.
    /// </summary>
    [Fact]
    public void ReorderingFallsThroughToWhatIsInTheSet()
    {
        var (service, _, _, _) = Build();
        var a = Set(3, "DRK", "FRU DRK", 100);
        var b = Set(7, "DRK", "Palazzo", 200);

        service.RecordPush(Cid, [a, b], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        Assert.Equal(UidA, service.Resolve(Cid, Set(7, "DRK", "FRU DRK", 100)).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, Set(3, "DRK", "Palazzo", 200)).SetUid);
    }

    /// <summary>
    /// Copy a set, rename the copy, then reorder: two sets hold the same gear under different names. The
    /// position rung looks at the gear and not at the name, which is what lets it survive a rename, and
    /// that same blindness had it hand back the neighbour's identity with no doubt attached. The name is
    /// evidence here and it points somewhere else, so the content ladder has to win.
    /// </summary>
    [Fact]
    public void TheNameWinsWhereItPointsSomewhereElse()
    {
        var (service, _, _, _) = Build();

        service.RecordPush(Cid, [Set(3, "DRK", "FRU", 100), Set(7, "DRK", "Ultimate", 100)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        ]);

        Assert.Equal(UidB, service.Resolve(Cid, Set(3, "DRK", "Ultimate", 100)).SetUid);
        Assert.Equal(UidA, service.Resolve(Cid, Set(7, "DRK", "FRU", 100)).SetUid);
    }

    /// <summary>
    /// The cross-check reaching the interface. A push whose answer contradicts what was remembered leaves
    /// those sets unidentified, however well a rung would otherwise have found them: the position rung
    /// here would answer at once and confidently, and confidently is the problem.
    /// </summary>
    [Fact]
    public void ASetTheTwoAccountsDisagreeAboutIsNotIdentified()
    {
        var (service, _, _, _) = Build();

        service.RecordPush(Cid, [Set(0, "GNB", "Revolverklinge", 100), Set(1, "GNB", "Revolverklinge", 200)], [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        ]);

        // Reordered, and the server broke the tie on the value the reorder just changed: it now says the
        // set at 0 is A, while the gear there is what B was carrying.
        var swapped = new[] { Set(0, "GNB", "Revolverklinge", 200), Set(1, "GNB", "Revolverklinge", 100) };
        service.RecordPush(Cid, swapped, [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.NameAmbiguous },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.NameAmbiguous },
        ]);

        Assert.Equal(2, service.ContestedMatches);
        Assert.True(service.Resolve(Cid, swapped[0]).WasAmbiguous);
        Assert.True(service.Resolve(Cid, swapped[1]).WasAmbiguous);
    }

    /// <summary>
    /// And the mark lasts exactly as long as the evidence. It is recomputed on every push rather than
    /// carried, so a set the two accounts agree about again is identified again, without anybody having to
    /// clear anything.
    /// </summary>
    [Fact]
    public void TheDoubtLiftsOnceTheTwoAccountsAgreeAgain()
    {
        var (service, _, _, _) = Build();
        var first = new[] { Set(0, "GNB", "Revolverklinge", 100), Set(1, "GNB", "Revolverklinge", 200) };

        service.RecordPush(Cid, first, [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        ]);

        var swapped = new[] { Set(0, "GNB", "Revolverklinge", 200), Set(1, "GNB", "Revolverklinge", 100) };
        service.RecordPush(Cid, swapped, [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.NameAmbiguous },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.NameAmbiguous },
        ]);

        Assert.Equal(2, service.ContestedMatches);

        service.RecordPush(Cid, swapped, [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        ]);

        Assert.Equal(0, service.ContestedMatches);
        Assert.Equal(UidA, service.Resolve(Cid, swapped[0]).SetUid);
    }

    /// <summary>
    /// A refresh from <c>GET /gear/sets</c> brings no items, so it keeps whatever a push established. Where
    /// the name has moved since, the row is rebuilt from the read and the strong key goes with it, because
    /// the name is part of what that key hashes. The gear key is not, and it must survive: a rename is the
    /// one case it exists for, and this refresh runs every ten minutes whether anybody asked or not.
    /// </summary>
    [Fact]
    public async Task ARefreshDoesNotForgetTheGearWhenTheNameMoved()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        var clock = new TestClock();
        var service = new GearsetMappingService(api, tokens, new InMemoryGearsetIdentityStore(), clock, new CapturingLog());

        service.RecordPush(Cid, [Set(3, "DRK", "Dunkelritter", 100)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA, MatchedBy = MatchedBy.Exact },
        ]);

        // A push holds the cache fresh for a while, and the refresh under test only runs once that lapses.
        clock.Advance(TimeSpan.FromHours(1));

        // Renamed on the website, so the read comes back under a name the cache never saw and the row is
        // rebuilt from it. The strong key cannot survive that; the gear key has no business dying with it.
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset
                {
                    SetUid = UidA, Job = "DRK", Name = "FRU DRK", GearIndex = 3, Source = GearsetSource.Plugin,
                },
            ],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        // The game still calls it what it always did, since the rename happened elsewhere. So the cached
        // name and the live name now disagree, which retires both the name rungs and leaves the gear as
        // the only thing that can still recognise the set.
        Assert.Equal(UidA, service.Resolve(Cid, Set(3, "DRK", "Dunkelritter", 100)).SetUid);
    }

    /// <summary>
    /// Which attributions are still open used to come from a push and from nothing else, and it is not
    /// persisted. So every reload started out believing nothing was outstanding, and stayed that way until
    /// the next push happened to mention it: a set the server is waiting on showed its provisional target
    /// as though it were settled, and a diagnostics dump read "none open" with a card sitting in the
    /// review window. The read knows, and now says so.
    /// </summary>
    [Fact]
    public async Task AReadAlsoSaysWhichAttributionsAreStillOpen()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "FRU", GearIndex = 0, State = RowState.Active },
                new StoredGearset { SetUid = UidB, Job = "GNB", Name = "Revolver", GearIndex = 1, State = RowState.Held },
            ],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        Assert.True(service.IsHeld(UidB));
        Assert.False(service.IsHeld(UidA));
    }

    /// <summary>
    /// And a server that reports no state says nothing about what is open. Reading its silence as "nothing
    /// is open" would replace a real set of questions with an empty one, which is the same mistake in the
    /// other direction.
    /// </summary>
    [Fact]
    public async Task AServerThatReportsNoStateDoesNotClearWhatIsOpen()
    {
        var (service, api, _, _) = Build();
        service.RecordPush(Cid, [Set(0, "GNB", "Revolver", 100)], [
            new GearsetAssignment { GearIndex = 0, SetUid = UidB, MatchedBy = MatchedBy.New, State = PushState.Held },
        ]);

        Assert.True(service.IsHeld(UidB));

        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data = [new StoredGearset { SetUid = UidB, Job = "GNB", Name = "Revolver", GearIndex = 0 }],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));
        Assert.True(service.IsHeld(UidB));
    }

    /// <summary>
    /// Answering one question in the review window used to cost the whole character's gear knowledge. The
    /// caller wanted the attribution re-read, dropped the cache to force it, and the re-read carries no
    /// items, so every row came back with its uid, its job and its name and nothing else. Two sets of one
    /// job are named identically by the game, so every such pair went unresolvable, and stayed that way
    /// until the next push.
    /// </summary>
    [Fact]
    public async Task ExpiringTheMappingKeepsWhatOnlyAPushCanKnow()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        var service = new GearsetMappingService(api, tokens, new InMemoryGearsetIdentityStore(), new TestClock(), new CapturingLog());

        var twins = new[] { Set(0, "GNB", "Revolverklinge", 100), Set(1, "GNB", "Revolverklinge", 200) };
        service.RecordPush(Cid, twins, [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        ]);

        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "GNB", Name = "Revolverklinge", GearIndex = 0, State = RowState.Active },
                new StoredGearset { SetUid = UidB, Job = "GNB", Name = "Revolverklinge", GearIndex = 1, State = RowState.Active },
            ],
        });

        service.ExpireMapping(Cid);
        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        Assert.Equal(UidA, service.Resolve(Cid, twins[0]).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, twins[1]).SetUid);
    }

    /// <summary>
    /// Each rung says which one it was. Not decoration: the report used to print only the rung the server
    /// had reported months ago, so a set found by its contents and a set found by where it sits looked
    /// identical, and a test of the position rungs could not be told from one where nothing had changed.
    /// </summary>
    [Fact]
    public void EachRungSaysWhichOneItWas()
    {
        var (service, _, _, _) = Build();
        service.RecordPush(Cid, [Set(3, "DRK", "Dunkelritter", 100), Set(7, "DRK", "Dunkelritter", 200)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA, MatchedBy = MatchedBy.Exact },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB, MatchedBy = MatchedBy.Exact },
        ]);

        // Nothing changed: the contents describe it completely.
        Assert.Equal("job+name+gear", service.Resolve(Cid, Set(3, "DRK", "Dunkelritter", 100)).By);

        // Renamed: the contents no longer match, the place and the gear do.
        Assert.Equal("place+gear", service.Resolve(Cid, Set(3, "DRK", "FRU", 100)).By);

        // Both sets moved and their name belongs to two rows, so nothing is left to ask.
        Assert.Equal("ambiguous", service.Resolve(Cid, Set(5, "DRK", "Dunkelritter", 999)).By);

        var (lone, _, _, _) = Build();
        lone.RecordPush(Cid, [Set(3, "DRK", "FRU", 100)], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA, MatchedBy = MatchedBy.Exact },
        ]);

        // Re-geared under a name nothing else carries.
        Assert.Equal("place+name", lone.Resolve(Cid, Set(3, "DRK", "FRU", 999)).By);

        // Re-geared and moved, so only the name is left.
        Assert.Equal("job+name", lone.Resolve(Cid, Set(8, "DRK", "FRU", 999)).By);

        Assert.Equal("none", lone.Resolve(Cid, Set(8, "DRK", "Nothing", 999)).By);
    }

    /// <summary>
    /// The ordinary refresh, ten minutes after a push, with nothing changed in between. It must keep what
    /// the push established: a read carries no items, so anything it writes over is gone until the next
    /// push, and the position rung then drops from the gear to the name and stops separating two sets of
    /// one job.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryRefreshKeepsTheStrongKey()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        var clock = new TestClock();
        var store = new InMemoryGearsetIdentityStore();
        var service = new GearsetMappingService(api, tokens, store, clock, new CapturingLog());

        var set = Set(3, "DRK", "Dunkelritter", 100);
        service.RecordPush(Cid, [set], [
            new GearsetAssignment { GearIndex = 3, SetUid = UidA, MatchedBy = MatchedBy.Exact },
        ]);

        Assert.NotNull(store.Identities[Cid][0].ItemsKey);
        Assert.NotNull(store.Identities[Cid][0].GearKey);

        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset
                {
                    SetUid = UidA, Job = "DRK", Name = "Dunkelritter", GearIndex = 3, State = RowState.Active,
                },
            ],
        });

        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        Assert.NotNull(store.Identities[Cid][0].ItemsKey);
        Assert.NotNull(store.Identities[Cid][0].GearKey);
        Assert.Equal("job+name+gear", service.Resolve(Cid, set).By);
    }

    [Fact]
    public void TwinsStillResolveWhileTheirItemsDiffer()
    {
        var (service, _, _, _) = Build();
        var first = Set(0, "DRK", "Twin", 100);
        var second = Set(1, "DRK", "Twin", 200);
        service.RecordPush(Cid, [first, second], [
            new GearsetAssignment { GearIndex = 0, SetUid = UidA },
            new GearsetAssignment { GearIndex = 1, SetUid = UidB },
        ]);

        Assert.Equal(UidA, service.Resolve(Cid, first).SetUid);
        Assert.Equal(UidB, service.Resolve(Cid, second).SetUid);
    }

    /// <summary>
    /// An older server answers a push without the mapping. That contradicts nothing, so it must leave
    /// the cache alone — clearing it would throw away a working mapping in response to silence.
    /// </summary>
    [Fact]
    public void AServerWithoutIdentitiesLeavesTheCacheAlone()
    {
        var (service, _, _, _) = Build();
        var set = Set(0, "DRK", "2.50", 100);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);

        service.RecordPush(Cid, [set], []);

        Assert.Equal(UidA, service.Resolve(Cid, set).SetUid);
    }

    [Fact]
    public void NothingIsResolvedBeforeAnythingWasLearned()
    {
        var (service, _, _, _) = Build();

        Assert.False(service.ServerMintsUids);
        Assert.False(service.Resolve(Cid, Set(0, "DRK", "2.50", 100)).IsResolved);
    }

    /// <summary>
    /// The response is documented as index-aligned with the list that was sent. If it ever is not,
    /// pairing the rows anyway would write one gearset's identity onto another — the exact bug class
    /// this feature removes. So the whole mapping for that push is dropped and the fact is logged.
    /// </summary>
    [Fact]
    public void AMisalignedResponseIsRefusedWholesale()
    {
        var (service, _, _, log) = Build();
        var sent = new[] { Set(0, "DRK", "2.50", 100), Set(1, "WHM", "Heal", 200) };

        service.RecordPush(Cid, sent, new[]
        {
            new GearsetAssignment { GearIndex = 0, SetUid = UidA },
            new GearsetAssignment { GearIndex = 7, SetUid = UidB },
        });

        Assert.False(service.Resolve(Cid, sent[0]).IsResolved);
        Assert.False(service.Resolve(Cid, sent[1]).IsResolved);
        Assert.Contains(log.Messages, m => m.Contains("out of order", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUncertainRungIsRememberedAndWarnedAboutOnce()
    {
        var (service, _, _, log) = Build();
        service.RecordPush(
            Cid,
            [Set(0, "DRK", "Twin", 100)],
            [new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.NameAmbiguous }]);

        Assert.Equal(MatchedBy.NameAmbiguous, service.UncertainMatches[UidA]);
        Assert.Equal(1, service.AmbiguousMatches);
        Assert.Equal(0, service.PositionalMatches);
        Assert.Single(log.Messages, m => m.Contains("share a job and a name", StringComparison.Ordinal));
    }

    /// <summary>
    /// The two uncertain rungs get their own sentence. Renaming ends an ambiguity and does nothing for a
    /// positional match, so a single message with a single piece of advice was wrong in whichever case it
    /// did not fit. Found in a real run where all five uncertain rows were `index` and already had
    /// distinct names, and the plugin told the owner to rename them.
    /// </summary>
    [Fact]
    public void EachUncertainRungGetsItsOwnAdvice()
    {
        var (service, _, _, log) = Build();

        service.RecordPush(
            Cid,
            [Set(0, "DRK", "Twin", 100), Set(1, "DRK", "Twin", 200), Set(2, "WAR", "Moved", 300)],
            [
                new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.NameAmbiguous },
                new GearsetAssignment { GearIndex = 1, SetUid = UidB, MatchedBy = MatchedBy.NameAmbiguous },
                new GearsetAssignment { GearIndex = 2, SetUid = UidC, MatchedBy = MatchedBy.Index },
            ]);

        Assert.Equal(2, service.AmbiguousMatches);
        Assert.Equal(1, service.PositionalMatches);

        var naming = Assert.Single(log.Messages, m => m.Contains("share a job and a name", StringComparison.Ordinal));
        Assert.Contains("2 gearset(s)", naming, StringComparison.Ordinal);
        Assert.Contains("different names", naming, StringComparison.Ordinal);

        var positional = Assert.Single(log.Messages, m => m.Contains("position alone", StringComparison.Ordinal));
        Assert.Contains("1 gearset(s)", positional, StringComparison.Ordinal);
        Assert.DoesNotContain("different names", positional, StringComparison.Ordinal);
    }

    [Fact]
    public void APositionMatchCountsAsUncertainToo()
    {
        Assert.True(MatchedBy.IsUncertain(MatchedBy.Index));
        Assert.True(MatchedBy.IsUncertain(MatchedBy.NameAmbiguous));
        Assert.False(MatchedBy.IsUncertain(MatchedBy.Exact));
        Assert.False(MatchedBy.IsUncertain(MatchedBy.Items));
        Assert.False(MatchedBy.IsUncertain(null));
    }

    [Fact]
    public void AnUncertainRungIsForgottenOnceTheServerIsSure()
    {
        var (service, _, _, _) = Build();
        var set = Set(0, "DRK", "Twin", 100);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.NameAmbiguous }]);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact }]);

        Assert.Empty(service.UncertainMatches);
    }

    /// <summary>Reading the mapping is a read. Learning it by pushing would be a write to ask a question.</summary>
    [Fact]
    public async Task TheMappingIsLearnedByReadingNotByPushing()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "2.50", GearIndex = 0, Source = GearsetSource.Plugin },
            ],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        Assert.Equal(1, api.GearSetsCalls);
        Assert.Equal(0, api.PushCalls);
        Assert.Equal(UidA, service.Resolve(Cid, Set(4, "DRK", "2.50", 100)).SetUid);
    }

    /// <summary>
    /// What a resolution cache keeps is what is in game, and once the server says so per row, that is one
    /// condition over one field. A parked row is the case the old source rule got wrong: it came from a
    /// push, so it looked cacheable, and it is not in the live list any more.
    /// </summary>
    [Fact]
    public async Task OnlyLiveRowsGoIntoTheCacheOnceTheServerReportsState()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "live", GearIndex = 0, Source = GearsetSource.Plugin, State = RowState.Active },
                new StoredGearset { SetUid = UidB, Job = "DRK", Name = "gone", GearIndex = 100, Source = GearsetSource.Plugin, State = RowState.Parked },
            ],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.Equal(UidA, service.Resolve(Cid, Set(0, "DRK", "live", 100)).SetUid);
        Assert.False(service.Resolve(Cid, Set(1, "DRK", "gone", 100)).IsResolved);
    }

    /// <summary>
    /// A held row belongs to a live gearset and only its attribution is open, so it goes in. Leaving it out
    /// would show the player nothing for the very set the window is asking them about.
    /// </summary>
    [Fact]
    public async Task AHeldRowIsCachedBecauseItsGearsetExists()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data = [new StoredGearset { SetUid = UidA, Job = "DRK", Name = "asked about", GearIndex = 7, Source = GearsetSource.Plugin, State = RowState.Held }],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.Equal(UidA, service.Resolve(Cid, Set(7, "DRK", "asked about", 100)).SetUid);
    }

    /// <summary>
    /// An ignored row is a decision, not a live gearset. It stays a candidate on the server and is offered
    /// there; it has no business in a cache whose whole job is attaching what is in the list right now.
    /// </summary>
    [Fact]
    public async Task AnIgnoredRowIsNotCached()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "live", GearIndex = 0, Source = GearsetSource.Plugin, State = RowState.Active },
                new StoredGearset { SetUid = UidB, Job = "DRK", Name = "aside", GearIndex = 1000, Source = GearsetSource.Manual, State = RowState.Ignored },
            ],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.False(service.Resolve(Cid, Set(1, "DRK", "aside", 100)).IsResolved);
    }

    /// <summary>
    /// The compatibility half, and it matters more than it looks: a server that does not send the field at
    /// all reports null on every row, so the state rule read literally would cache nothing and break the
    /// mapping outright. Asked of the whole answer, because a hand-made row legitimately has no state and
    /// one row therefore cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task AServerThatSendsNoStateFallsBackToTheSourceRule()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "2.50", GearIndex = 0, Source = GearsetSource.Plugin },
                new StoredGearset { SetUid = UidB, Job = "DRK", Name = "2.50", GearIndex = 1000, Source = GearsetSource.Manual },
            ],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.Equal(UidA, service.Resolve(Cid, Set(0, "DRK", "2.50", 100)).SetUid);
    }

    /// <summary>
    /// A hand-made set exists only on the website. Caching it could not help — it will never appear in
    /// the live list — and it could hurt, by making a weak key ambiguous that otherwise resolves.
    /// </summary>
    [Fact]
    public async Task HandMadeSetsAreNotCached()
    {
        var (service, api, store, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, Job = "DRK", Name = "2.50", GearIndex = 0, Source = GearsetSource.Plugin },
                new StoredGearset { SetUid = UidB, Job = "DRK", Name = "2.50", GearIndex = 1000, Source = GearsetSource.Manual },
            ],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.Single(store.Identities[Cid]);
        Assert.Equal(UidA, service.Resolve(Cid, Set(0, "DRK", "2.50", 100)).SetUid);
    }

    /// <summary>
    /// A mapping row knows the uid but not the items, so a refresh must not replace a row a push
    /// established with the full key. Otherwise every refresh would quietly downgrade the cache and
    /// re-gearing would stop being recognised.
    /// </summary>
    [Fact]
    public async Task ARefreshDoesNotDowngradeWhatAPushEstablished()
    {
        var (service, api, store, _) = Build();
        var set = Set(0, "DRK", "2.50", 100);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact }]);

        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data = [new StoredGearset { SetUid = UidA, Job = "DRK", Name = "2.50", GearIndex = 0, Source = GearsetSource.Plugin }],
        });

        // The refresh interval has not elapsed, so force the read by clearing the schedule the push set.
        service.Forget(Cid);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA, MatchedBy = MatchedBy.Exact }]);
        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.NotNull(store.Identities[Cid][0].ItemsKey);
        Assert.Equal(MatchedBy.Exact, store.Identities[Cid][0].MatchedBy);
    }

    /// <summary>
    /// A server that does not know the route answers 404. That is a fact about the server, not a failure
    /// to report loudly, and what is already cached stays — the comparison keeps working.
    /// </summary>
    [Fact]
    public async Task AnUnknownRouteKeepsWhatIsCached()
    {
        var (service, api, _, _) = Build();
        var set = Set(0, "DRK", "2.50", 100);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);
        service.Forget(Cid);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);

        api.GearSetsResult = ApiResult<GearSetsResponse>.Fail(
            new ApiError { Kind = ApiErrorKind.NotFound, StatusCode = 404, Endpoint = "/gear/sets", Message = "no such route" });

        service.Forget(Cid);
        service.RecordPush(Cid, [set], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);
        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        Assert.Equal(UidA, service.Resolve(Cid, set).SetUid);
    }

    [Fact]
    public async Task WithoutAKeyNothingIsRead()
    {
        var api = new FakeApiClient();
        var service = new GearsetMappingService(
            api, new InMemoryTokenStore(), new InMemoryGearsetIdentityStore(), new TestClock(), new CapturingLog());

        Assert.False(await service.EnsureMappingAsync(Cid, CancellationToken.None));
        Assert.Equal(0, api.GearSetsCalls);
    }

    [Fact]
    public void ForgettingOneCharacterLeavesTheOthers()
    {
        var (service, _, store, _) = Build();
        const string other = "0000000000000000000000000000000000000000000000000000000000000000";
        service.RecordPush(Cid, [Set(0, "DRK", "2.50", 100)], [new GearsetAssignment { GearIndex = 0, SetUid = UidA }]);
        service.RecordPush(other, [Set(0, "WHM", "Heal", 200)], [new GearsetAssignment { GearIndex = 0, SetUid = UidB }]);

        service.Forget(Cid);

        Assert.False(store.Identities.ContainsKey(Cid));
        Assert.True(store.Identities.ContainsKey(other));
    }
    /// <summary>
    /// A cached row whose name no longer agrees with the server's is <b>replaced</b>, not trusted. It
    /// loses its items key in the process — the mapping read cannot supply one — and the next push
    /// restores it. This happened for real: four names with umlauts were corrupted in the config by a
    /// stray tool, and the mapping read repaired them without ever handing out a wrong identity. The
    /// weaker outcome is the correct one here, because the server is the truth about a name it stored.
    /// </summary>
    [Fact]
    public async Task ACachedRowThatDisagreesWithTheServerIsReplaced()
    {
        var (service, api, store, _) = Build();
        var set = Set(0, "WHM", "Weißmagier", 100);

        // As if the cached name had been mangled while the plugin was not running.
        store.Identities[Cid] =
        [
            new CachedGearsetIdentity
            {
                SetUid = UidA,
                Job = "WHM",
                Name = "WeiÃŸmagier",
                ItemsKey = "stale",
                MatchedBy = MatchedBy.Exact,
            },
        ];

        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data = [new StoredGearset { SetUid = UidA, Job = "WHM", Name = "Weißmagier", GearIndex = 0, Source = GearsetSource.Plugin }],
        });

        await service.EnsureMappingAsync(Cid, CancellationToken.None);

        // Repaired: the server's name, no items key, and the live gearset resolves again.
        Assert.Equal("Weißmagier", store.Identities[Cid][0].Name);
        Assert.Null(store.Identities[Cid][0].ItemsKey);
        Assert.Equal(UidA, service.Resolve(Cid, set).SetUid);
    }

    /// <summary>
    /// A read names one character. The development server accepts <c>?cid_hash=</c> and answers with the
    /// whole account anyway (verified against it on 2026-08-23), so a row of another character can arrive
    /// sharing a job and a name with a real set. Cached, it would make the real set ambiguous and leave it
    /// without an identity — the one outcome this cache exists to prevent.
    /// </summary>
    [Fact]
    public async Task RowsOfAnotherCharacterAreNotCached()
    {
        const string other = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f";
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset { SetUid = UidA, CidHash = Cid, Job = "DRK", Name = "same name", GearIndex = 0, Source = GearsetSource.Plugin, State = RowState.Active },
                new StoredGearset { SetUid = UidB, CidHash = other, Job = "DRK", Name = "same name", GearIndex = 0, Source = GearsetSource.Plugin, State = RowState.Active },
            ],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        var match = service.Resolve(Cid, Set(0, "DRK", "same name", 100));
        Assert.Equal(UidA, match.SetUid);
        Assert.False(match.WasAmbiguous);
        Assert.Equal(1, service.ForeignRowsDropped);
    }

    /// <summary>
    /// A server that names no character on its rows is still usable: the filter drops what is known to be
    /// foreign, never what is merely unstated.
    /// </summary>
    [Fact]
    public async Task ARowThatNamesNoCharacterIsKept()
    {
        var (service, api, _, _) = Build();
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data = [new StoredGearset { SetUid = UidA, Job = "DRK", Name = "no owner named", GearIndex = 0, Source = GearsetSource.Plugin }],
        });

        Assert.True(await service.EnsureMappingAsync(Cid, CancellationToken.None));

        Assert.Equal(UidA, service.Resolve(Cid, Set(0, "DRK", "no owner named", 100)).SetUid);
        Assert.Equal(0, service.ForeignRowsDropped);
    }
}
