using System.Net;
using EorzeaArsenal.Api;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// A refused decision says why. Before this, the window returned in silence on anything but a missing route,
/// so a refused delete looked like a dead button. With server 1.4 that refusal is certain for every key
/// paired before the split, because deleting moves to <c>gear:delete</c> and old keys are never topped up.
/// </summary>
public sealed class ReviewRefusalTests
{
    private const string Cid = "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646";

    /// <summary>The case the split creates: the one answer that names its own way out.</summary>
    [Fact]
    public void AMissingDeleteRightIsNamed()
    {
        var refusal = ReviewRules.RefusalOf(Forbidden(scope: "gear:delete"));

        Assert.Equal(ReviewRefusal.NeedsDeleteRight, refusal);
    }

    [Fact]
    public void AnyOtherMissingScopeAsksForAReconnect()
    {
        Assert.Equal(ReviewRefusal.NeedsReconnect, ReviewRules.RefusalOf(Forbidden(scope: "gear:review")));
    }

    /// <summary>
    /// A right the team withholds is not the key's fault, and reconnecting would not change it. Saying
    /// "reconnect" here sends the player round in a circle.
    /// </summary>
    [Fact]
    public void AMissingTeamRightPointsAtTheTeamLead()
    {
        Assert.Equal(ReviewRefusal.NeedsTeamRight, ReviewRules.RefusalOf(Forbidden(capability: "manage_members")));
    }

    /// <summary>
    /// A 403 without either member stays a plain refusal. The cause is never read out of <c>detail</c>, even
    /// when the text happens to name a scope: that text is for people and may change.
    /// </summary>
    [Fact]
    public void ABare403DoesNotGuess()
    {
        var error = new ApiError { Kind = ApiErrorKind.Forbidden, Message = "missing scope gear:delete" };

        Assert.Equal(ReviewRefusal.Refused, ReviewRules.RefusalOf(error));
    }

    [Fact]
    public void AnInvalidKeyIsSaidToBeInvalid()
    {
        Assert.Equal(ReviewRefusal.KeyRejected, ReviewRules.RefusalOf(new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "x" }));
    }

    [Theory]
    [InlineData(ApiErrorKind.Network)]
    [InlineData(ApiErrorKind.Validation)]
    [InlineData(ApiErrorKind.Conflict)]
    public void EverythingElseSaysTryLater(ApiErrorKind kind)
    {
        Assert.Equal(ReviewRefusal.TryLater, ReviewRules.RefusalOf(new ApiError { Kind = kind, Message = "x" }));
    }

    [Fact]
    public void NoKeptErrorSaysTryLater()
    {
        Assert.Equal(ReviewRefusal.TryLater, ReviewRules.RefusalOf(null));
    }

    /// <summary>Both members arrive from the wire, not only from a test's hand.</summary>
    [Fact]
    public async Task TheClientCarriesBothMembersOfA403()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.Forbidden,
            """{"type":"about:blank","title":"Forbidden","status":403,"detail":"x","missing_scope":"gear:delete","missing_capability":"manage_members"}""",
            contentType: "application/problem+json");
        var client = new ApiClient(new HttpClient(handler), new StubHttpMessageHandler.Settings());

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash), JobScope.All);
        var result = await client.PushGearAsync("k", payload, CancellationToken.None);

        Assert.Equal(ApiErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal("gear:delete", result.Error.MissingScope);
        Assert.Equal("manage_members", result.Error.MissingCapability);
    }

    /// <summary>The service keeps the error of a refused decision, so the window has something to read.</summary>
    [Fact]
    public async Task ARefusedDecisionKeepsItsError()
    {
        var (service, api) = Build();
        api.DecisionResults.Enqueue(ApiResult<ReviewDecisionResponse>.Fail(Forbidden(scope: "gear:delete")));

        var outcome = await service.DecideAsync(
            new ReviewDecision { SetUid = "c05e", Action = ReviewAction.Delete }, CancellationToken.None);

        Assert.Equal(ReviewOutcome.Failed, outcome);
        Assert.Equal("gear:delete", service.LastError!.MissingScope);
    }

    /// <summary>
    /// Once a call goes through, the old refusal is gone. A kept error that outlives the next success would
    /// explain a failure that is no longer happening.
    /// </summary>
    [Fact]
    public async Task ASuccessfulCallForgetsTheRefusal()
    {
        var (service, api) = Build();
        api.DecisionResults.Enqueue(ApiResult<ReviewDecisionResponse>.Fail(Forbidden(scope: "gear:delete")));
        await service.DecideAsync(new ReviewDecision { SetUid = "c05e", Action = ReviewAction.Delete }, CancellationToken.None);

        await service.RefreshAsync(Cid, CancellationToken.None);

        Assert.Null(service.LastError);
    }

    private static ApiError Forbidden(string? scope = null, string? capability = null) => new()
    {
        Kind = ApiErrorKind.Forbidden,
        Message = "forbidden",
        MissingScope = scope,
        MissingCapability = capability,
    };

    private static (ReviewService Service, FakeApiClient Api) Build()
    {
        var api = new FakeApiClient();
        api.ReviewResult = ApiResult<ReviewState>.Ok(new ReviewState { StateToken = "9f13" });
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_secret");
        var directory = new CharacterDirectory([new KeyValuePair<string, string>(Cid, "42")]);
        var mapping = new GearsetMappingService(api, tokens, new InMemoryGearsetIdentityStore(), new TestClock(), new CapturingLog());
        var service = new ReviewService(api, tokens, directory, new TestClock(), mapping);
        service.RefreshAsync(Cid, CancellationToken.None).GetAwaiter().GetResult();
        return (service, api);
    }
}
