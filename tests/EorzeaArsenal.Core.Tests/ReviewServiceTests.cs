using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Reading the questions and relaying the answers. The state and its token are kept and replaced together,
/// because counters from one snapshot with a token from another would offer a decision the server has
/// already moved past.
/// </summary>
public sealed class ReviewServiceTests
{
    private const string Cid = "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646";

    /// <summary>
    /// A push moves the review state too, and the reading held here then describes something that is over.
    /// Delete a gearset and make it again: the server attributes it to the row it left behind, that row
    /// stops being an orphan, and the menu went on offering two decisions that led to an empty window.
    /// </summary>
    [Fact]
    public async Task APushThatChangedTheQuestionDropsTheOldReading()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "96a1e4f5",
            Orphans = [new OrphanRow { SetUid = "aaaa", Job = "GNB", State = RowState.Parked }],
        });

        Assert.Equal(ReviewOutcome.Ok, await service.RefreshAsync(Cid, CancellationToken.None));
        Assert.Equal(1, service.Summary()?.Orphans?.Open);

        service.NoteFromPush(new ReviewSummary { StateToken = "646c91a6" });

        Assert.Null(service.Current);
        Assert.Null(service.Summary());
    }

    /// <summary>
    /// And an ordinary push leaves it alone. The token fingerprints which rows are being asked about and
    /// not their contents, so a push that only writes fresh numbers into known rows keeps the same token,
    /// and throwing the reading away there would clear a badge somebody still has to act on.
    /// </summary>
    [Fact]
    public async Task APushThatChangedNothingKeepsTheReading()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "96a1e4f5",
            Orphans = [new OrphanRow { SetUid = "aaaa", Job = "GNB", State = RowState.Parked }],
        });

        Assert.Equal(ReviewOutcome.Ok, await service.RefreshAsync(Cid, CancellationToken.None));

        service.NoteFromPush(new ReviewSummary { StateToken = "96a1e4f5" });
        Assert.NotNull(service.Current);

        // And a server that sends no token at all says nothing about staleness either way.
        service.NoteFromPush(new ReviewSummary());
        service.NoteFromPush(null);
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task NothingIsAskedWithoutAKey()
    {
        var (service, api, tokens, _) = Build();
        tokens.Clear();

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.NotConnected, outcome);
        Assert.Equal(0, api.ReviewCalls);
    }

    /// <summary>
    /// A plugin reload must not leave a decision running against a world being torn down, and must not
    /// leak the gate. Dalamud reloads often, so "often" is the right word for how bad a leak here is.
    /// </summary>
    [Fact]
    public async Task AfterDisposeNothingIsSentAnyMore()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);
        var callsBefore = api.ReviewCalls;

        service.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAsync(Cid, CancellationToken.None));
        Assert.Equal(callsBefore, api.ReviewCalls);
    }

    /// <summary>Disposing twice is what a reload racing a shutdown looks like, and it must be harmless.</summary>
    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var (service, _, _, _) = Build();

        service.Dispose();
        service.Dispose();
    }

    /// <summary>
    /// A decision in flight when the plugin goes away ends as a cancellation rather than as a result
    /// written into disposed state.
    /// </summary>
    [Fact]
    public async Task ADecisionInFlightIsCancelledByDispose()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        service.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DecideAsync(new ReviewDecision { SetUid = "c05e", Action = ReviewAction.New }, CancellationToken.None));
    }

    /// <summary>
    /// The numeric id is learned from a push answer. Until one has landed there is nothing to ask about,
    /// and asking with a guess would be a request about somebody else.
    /// </summary>
    [Fact]
    public async Task NothingIsAskedBeforeTheCharacterIsKnown()
    {
        var (service, api, _, _) = Build(known: false);

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.NotResolved, outcome);
        Assert.Equal(0, api.ReviewCalls);
    }

    /// <summary>A 404 is the normal answer until the server ships this, not a fault to report.</summary>
    [Fact]
    public async Task AnOldServerReportsUnavailable()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Fail(new ApiError { Kind = ApiErrorKind.NotFound, Message = "nope" });

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.Unavailable, outcome);
    }

    /// <summary>
    /// A rate limit is not a fault to report, it is a wait. Reading it as a failure lets somebody keep
    /// clicking into the same limit, and the Retry-After the server sends says exactly how long.
    /// </summary>
    [Fact]
    public async Task ARateLimitBecomesAWaitRatherThanAFailure()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Fail(new ApiError
        {
            Kind = ApiErrorKind.RateLimited,
            Message = "too many",
            RetryAfter = TimeSpan.FromSeconds(3600),
        });

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.RateLimited, outcome);
        Assert.NotNull(service.BackoffRemaining);
    }

    /// <summary>While the wait is on, nothing is sent: the point is not to spend the limit twice.</summary>
    [Fact]
    public async Task NothingIsSentWhileTheWaitIsOn()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Fail(new ApiError
        {
            Kind = ApiErrorKind.RateLimited,
            Message = "too many",
            RetryAfter = TimeSpan.FromSeconds(600),
        });
        await service.RefreshAsync(Cid, CancellationToken.None);
        var callsAfterFirst = api.ReviewCalls;

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.RateLimited, outcome);
        Assert.Equal(callsAfterFirst, api.ReviewCalls);
    }

    /// <summary>Without a Retry-After there is still a wait, because guessing zero would be a retry storm.</summary>
    [Fact]
    public async Task AWaitIsAssumedWhenTheServerDoesNotSayHowLong()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Fail(new ApiError { Kind = ApiErrorKind.RateLimited, Message = "too many" });

        await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.NotNull(service.BackoffRemaining);
    }

    [Fact]
    public async Task AReadKeepsTheStateAndItsToken()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });

        var outcome = await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(ReviewOutcome.Ok, outcome);
        Assert.Equal("9f13", service.Current!.StateToken);
        Assert.Equal(Cid, service.CurrentCidHash);
    }

    [Fact]
    public async Task ADecisionCarriesTheTokenItWasReadWith()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.DecideAsync(new ReviewDecision { SetUid = "c05e", Action = ReviewAction.New }, CancellationToken.None);

        Assert.Equal("9f13", Assert.Single(api.SentTokens));
        Assert.Equal(ReviewAction.New, Assert.Single(api.SentDecisions).Action);
    }

    /// <summary>
    /// A stale token is not an error: nothing was applied, and the state now in hand is the fresh one to
    /// redraw from. Reporting it as a failure would leave the window showing the state that just moved.
    /// </summary>
    [Fact]
    public async Task AStaleTokenReplacesTheStateInsteadOfFailing()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "old" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        api.DecisionResults.Enqueue(ApiResult<ReviewDecisionResponse>.Ok(new ReviewDecisionResponse
        {
            StateToken = "moved",
            Conflicts = ["7bb2"],
        }));

        var outcome = await service.DecideAsync(
            new ReviewDecision { SetUid = "c05e", Action = ReviewAction.New },
            CancellationToken.None);

        Assert.Equal(ReviewOutcome.Stale, outcome);
        Assert.Equal("moved", service.Current!.StateToken);
        Assert.True(service.Current.IsConflict);
    }

    /// <summary>
    /// After a link the target uid is the one that lives on and the held row uid ceases to exist. Keeping
    /// that row would attach a live gearset to a row that is gone, so the mapping is re-read.
    /// </summary>
    /// <remarks>
    /// Re-read and not forgotten, which this test used to demand the other way round. Dropping the cache
    /// is a far larger thing than it sounds: a read carries no items, so everything only a push can know
    /// goes with it, and the rungs that separate two sets of one job stop working until the next push. One
    /// link cost a whole character's gear knowledge, and it took an evening of measuring to find, because
    /// the same confusion sat at three call sites and this was the one a link actually reached. The stale
    /// row still goes: the refresh drops what the server no longer lists, and that is the row a link
    /// retires.
    /// </remarks>
    [Fact]
    public async Task ALinkReReadsTheMappingWithoutThrowingItAway()
    {
        var (service, api, _, mapping) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        mapping.RecordPush(
            Cid,
            [new GearsetDto { GearIndex = 0, Job = "DRK", Name = "x", Items = [] }],
            [new GearsetAssignment { GearIndex = 0, SetUid = "abc", MatchedBy = MatchedBy.Exact }]);
        Assert.Equal(1, mapping.CachedCount(Cid));

        api.DecisionResults.Enqueue(ApiResult<ReviewDecisionResponse>.Ok(new ReviewDecisionResponse
        {
            StateToken = "next",
            Result = new ReviewResult
            {
                SetUid = "c05e",
                Action = ReviewAction.Link,
                Requested = ReviewAction.Link,
                SurvivingUid = "9a1f",
                State = RowState.Active,
            },
        }));

        await service.DecideAsync(
            new ReviewDecision { SetUid = "c05e", Action = ReviewAction.Link, TargetUid = "9a1f" },
            CancellationToken.None);

        // What the push established is still there, keys included.
        Assert.Equal(1, mapping.CachedCount(Cid));
        Assert.Equal((1, 1, 1), mapping.CachedKeys(Cid));

        // And the refresh really is due, which is what makes the stale row go.
        api.GearSetsResult = ApiResult<GearSetsResponse>.Ok(new GearSetsResponse
        {
            Data =
            [
                new StoredGearset
                {
                    SetUid = "abc", Job = "DRK", Name = "x", GearIndex = 0, State = RowState.Active,
                },
            ],
        });

        var before = api.GearSetsCalls;
        Assert.True(await mapping.EnsureMappingAsync(Cid, CancellationToken.None));
        Assert.Equal(before + 1, api.GearSetsCalls);
        Assert.Equal((1, 1, 1), mapping.CachedKeys(Cid));
    }

    /// <summary>A verb that changes nothing about identity leaves the cache alone.</summary>
    [Fact]
    public async Task AnIgnoreLeavesTheIdentityCacheAlone()
    {
        var (service, api, _, mapping) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        mapping.RecordPush(
            Cid,
            [new GearsetDto { GearIndex = 0, Job = "DRK", Name = "x", Items = [] }],
            [new GearsetAssignment { GearIndex = 0, SetUid = "abc", MatchedBy = MatchedBy.Exact }]);

        api.DecisionResults.Enqueue(ApiResult<ReviewDecisionResponse>.Ok(new ReviewDecisionResponse
        {
            StateToken = "next",
            Result = new ReviewResult { SetUid = "7bb2", Action = ReviewAction.Ignore, Requested = ReviewAction.Ignore },
        }));

        await service.DecideAsync(
            new ReviewDecision { SetUid = "7bb2", Action = ReviewAction.Ignore },
            CancellationToken.None);

        Assert.Equal(1, mapping.CachedCount(Cid));
    }

    /// <summary>
    /// The mapping is composed from the proposals, so a pair the server never proposed cannot be sent even
    /// if something on screen suggested it.
    /// </summary>
    [Fact]
    public async Task TheAcceptedMappingComesFromTheProposals()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held =
            [
                new()
                {
                    SetUid = "a",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "target-a" },
                },
                new()
                {
                    SetUid = "b",
                    Proposal = new ReviewProposal { Action = ReviewAction.New },
                },
            ],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(null, CancellationToken.None);

        var sent = Assert.Single(api.SentMappings);
        Assert.Equal(2, sent.Count);
        Assert.Equal("target-a", sent[0].TargetUid);
    }

    /// <summary>
    /// Striking every pair out is not a failure. It is what a player who wants none of it does, and they
    /// then answer the questions one at a time.
    /// </summary>
    [Fact]
    public async Task AnEmptyMappingIsNotSentAndIsNotAnError()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held = [new() { SetUid = "a", Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "t" } }],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        var outcome = await service.AcceptMappingAsync(
            new HashSet<string>(StringComparer.Ordinal) { "a" },
            CancellationToken.None);

        Assert.Equal(ReviewOutcome.Ok, outcome);
        Assert.Empty(api.SentMappings);
    }

    /// <summary>
    /// The bug these exist for, and the reason they sit here rather than on the rule. A question with
    /// several candidates and no vouching may not ride along in a bulk accept. The rule said so, the tests
    /// on the rule said so, and the window still sent one: it computed the excluded set once for the panel
    /// it was drawing and left the request builder to work it out again from the player's own strikes.
    /// Two computations of one thing, agreeing until they did not. A test on the rule cannot see that
    /// gap. Only a test that asks what actually went out can, so from here on the mapping is asserted on
    /// the payload.
    /// </summary>
    [Fact]
    public async Task AContestedQuestionIsNeverInWhatIsSent()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held = [Plain("plain", "target-plain"), Contested("contested", "b-side", "a-side")],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(null, CancellationToken.None);

        var sent = Assert.Single(api.SentMappings);
        var only = Assert.Single(sent);
        Assert.Equal("plain", only.SetUid);
    }

    /// <summary>
    /// The other half of the same rule, and the half that lets something in rather than keeping it out.
    /// Where the server vouches for its pick, at least 90 % and at least 20 clear of the next best, the
    /// pairing rides along even with several candidates: the choice is settled and the line says how many
    /// there were. Without this test the exception could quietly disappear and nobody would notice, since
    /// its failure mode is extra clicks rather than a wrong write.
    /// </summary>
    [Fact]
    public async Task AVouchedForProposalRidesAlongEvenWithSeveralCandidates()
    {
        var (service, api, _, _) = Build();
        var confident = Contested("sure", "b-side", "a-side");
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held =
            [
                new HeldGearset
                {
                    SetUid = confident.SetUid,
                    Job = "DRG",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "b-side", Confident = true },
                    Candidates = confident.Candidates,
                },
            ],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(null, CancellationToken.None);

        var only = Assert.Single(Assert.Single(api.SentMappings));
        Assert.Equal("b-side", only.TargetUid);
    }

    /// <summary>
    /// A lone candidate goes along whatever it scores. The case the shortcut exists for is somebody who
    /// kept sets on the website, left them for a year and installs the plugin now: their gear has drifted
    /// so far that every score is low, and reading a low score as doubt would hand exactly that person the
    /// wall of one-by-one clicks the door was built to spare them.
    /// </summary>
    [Fact]
    public async Task ALoneCandidateIsSentHoweverLowItScores()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held =
            [
                new HeldGearset
                {
                    SetUid = "lonely",
                    Job = "BRD",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "only-row" },
                    Candidates = [new ReviewCandidate { SetUid = "only-row", Probability = 9, Proposed = true }],
                },
            ],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(null, CancellationToken.None);

        var only = Assert.Single(Assert.Single(api.SentMappings));
        Assert.Equal("only-row", only.TargetUid);
    }

    /// <summary>
    /// Striking one out sends the rest. The all-struck case is covered above; this is the ordinary one,
    /// where the difference between what stays on screen and what goes out has to be exact.
    /// </summary>
    [Fact]
    public async Task AStruckPairIsMissingFromWhatIsSentAndTheRestGoes()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held = [Plain("a", "target-a"), Plain("b", "target-b")],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(
            new HashSet<string>(StringComparer.Ordinal) { "a" },
            CancellationToken.None);

        var only = Assert.Single(Assert.Single(api.SentMappings));
        Assert.Equal("b", only.SetUid);
        Assert.Equal("target-b", only.TargetUid);
    }

    /// <summary>
    /// Three shapes that must never reach the wire, in one state so their interaction is covered too: a
    /// row the server sent no proposal for, a proposal carrying something that is not an attribution, and
    /// a row with no uid to name. Each would come back as a refusal the player did nothing to earn.
    /// </summary>
    [Fact]
    public async Task NothingThatCannotBeAnsweredInBulkReachesTheWire()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held =
            [
                Plain("good", "target-good"),
                new HeldGearset { SetUid = "no-proposal", Job = "WAR" },
                new HeldGearset
                {
                    SetUid = "not-attribution",
                    Job = "WHM",
                    Proposal = new ReviewProposal { Action = ReviewAction.Ignore },
                },
                new HeldGearset
                {
                    SetUid = null,
                    Job = "BLM",
                    Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = "somewhere" },
                },
            ],
        });
        await service.RefreshAsync(Cid, CancellationToken.None);

        await service.AcceptMappingAsync(null, CancellationToken.None);

        var only = Assert.Single(Assert.Single(api.SentMappings));
        Assert.Equal("good", only.SetUid);
    }

    /// <summary>
    /// The invariant the bug broke, stated once over everything at once: what goes out is exactly what the
    /// rule produces, pair for pair and in order. Any second opinion about the mapping anywhere between the
    /// rule and the request fails here, which the per-shape tests above cannot promise on their own.
    /// </summary>
    [Fact]
    public async Task WhatIsSentIsExactlyWhatTheRuleComposes()
    {
        var state = new ReviewState
        {
            StateToken = "9f13",
            Held =
            [
                Plain("struck", "target-struck"),
                Plain("plain", "target-plain"),
                Contested("contested", "b-side", "a-side"),
                new HeldGearset { SetUid = "no-proposal", Job = "WAR" },
                new HeldGearset
                {
                    SetUid = "brand-new",
                    Job = "SGE",
                    Proposal = new ReviewProposal { Action = ReviewAction.New },
                    Candidates = [new ReviewCandidate { SetUid = "someone", Probability = 12 }],
                },
            ],
        };

        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(state);
        await service.RefreshAsync(Cid, CancellationToken.None);

        var struck = new HashSet<string>(StringComparer.Ordinal) { "struck" };
        await service.AcceptMappingAsync(struck, CancellationToken.None);

        var sent = Assert.Single(api.SentMappings);
        var expected = ReviewRules.MappingToAccept(state, struck);

        Assert.Equal(expected.Count, sent.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].SetUid, sent[i].SetUid);
            Assert.Equal(expected[i].Action, sent[i].Action);
            Assert.Equal(expected[i].TargetUid, sent[i].TargetUid);
        }

        // And named outright, so a change that makes both sides wrong in the same way still fails here.
        Assert.Equal(["plain", "brand-new"], sent.Select(p => p.SetUid).ToArray());
    }

    /// <summary>A question with one candidate and a proposal, which is the ordinary shape.</summary>
    private static HeldGearset Plain(string uid, string target) => new()
    {
        SetUid = uid,
        Job = "DRK",
        Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = target },
        Candidates = [new ReviewCandidate { SetUid = target, Probability = 55, Proposed = true }],
    };

    /// <summary>Two candidates and no vouching: the one shape a bulk accept may not answer.</summary>
    private static HeldGearset Contested(string uid, string proposed, string other) => new()
    {
        SetUid = uid,
        Job = "DRG",
        Proposal = new ReviewProposal { Action = ReviewAction.Link, TargetUid = proposed },
        Candidates =
        [
            new ReviewCandidate { SetUid = proposed, Probability = 18, Proposed = true },
            new ReviewCandidate { SetUid = other, Probability = 9 },
        ],
    };

    [Fact]
    public async Task TheChangedEventFiresOnEveryOutcome()
    {
        var (service, api, tokens, _) = Build();
        var fired = 0;
        service.Changed += () => fired++;

        tokens.Clear();
        await service.RefreshAsync(Cid, CancellationToken.None);
        tokens.SetApiKey("ea_secret");
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Equal(2, fired);
    }

    private static (ReviewService Service, FakeApiClient Api, InMemoryTokenStore Tokens, GearsetMappingService Mapping) Build(
        bool known = true)
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_secret");
        var directory = new CharacterDirectory(known ? [new KeyValuePair<string, string>(Cid, "42")] : null);
        var store = new InMemoryGearsetIdentityStore();
        var mapping = new GearsetMappingService(api, tokens, store, new TestClock(), new CapturingLog());
        return (new ReviewService(api, tokens, directory, new TestClock(), mapping), api, tokens, mapping);
    }

    /// <summary>
    /// Nothing read yet means nothing to say, so the caller falls back to whatever the last push
    /// answered rather than being handed a summary of zeroes that would clear a badge too early.
    /// </summary>
    [Fact]
    public void ThereIsNoSummaryBeforeAnythingIsRead()
    {
        var (service, _, _, _) = Build();

        Assert.Null(service.Summary());
    }

    /// <summary>
    /// The badge is fed from here rather than from the push answer, which is a snapshot of the moment it
    /// was sent. Acting in the window moves the state without a push, and a marker that keeps announcing
    /// finished work is one people learn to ignore. Rows put aside are counted apart, because they are
    /// decided and must not keep the badge alight.
    /// </summary>
    [Fact]
    public async Task TheSummaryCountsWhatWasLastRead()
    {
        var (service, api, _, _) = Build();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState
        {
            StateToken = "9f13",
            Held = [new HeldGearset { SetUid = "a", Job = "DRK", GearIndex = 0 }],
            Orphans =
            [
                new OrphanRow { SetUid = "b", Job = "DRK", Source = GearsetSource.Plugin, State = RowState.Parked },
                new OrphanRow { SetUid = "c", Job = "WAR", Source = GearsetSource.Plugin, State = RowState.Parked },
                new OrphanRow { SetUid = "d", Job = "MNK", Source = GearsetSource.Plugin, State = RowState.Ignored },
            ],
        });

        Assert.Equal(ReviewOutcome.Ok, await service.RefreshAsync(Cid, CancellationToken.None));

        var summary = service.Summary();

        Assert.NotNull(summary);
        Assert.Equal("9f13", summary!.StateToken);
        Assert.Equal(1, summary.Held);
        Assert.Equal(2, summary.Orphans!.Open);
        Assert.Equal(1, summary.Orphans!.Ignored);
        Assert.True(summary.NeedsAttention);
    }
}
