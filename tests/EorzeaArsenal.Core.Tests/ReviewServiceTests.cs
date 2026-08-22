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
    /// the identity cache would attach a live gearset to a row that is gone, so it is dropped and re-read.
    /// </summary>
    [Fact]
    public async Task ALinkDropsTheIdentityCacheForThatCharacter()
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

        Assert.Equal(0, mapping.CachedCount(Cid));
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
}
