using System.Net;
using EorzeaArsenal.Api;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Exercises the HTTP client against a scripted handler: payload serialization, the device-flow
/// status parsing and the full 401/403/409/422/400/429 error mapping (briefing §13, R31).
/// </summary>
public sealed class ApiClientTests
{
    private static ApiClient Make(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), new StubHttpMessageHandler.Settings());

    [Fact]
    public async Task RequestDeviceCode_parses_snake_case()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"device_code":"d1","user_code":"AB-12","verification_uri":"http://x/approve","interval":5,"expires_in":300}""");

        var result = await Make(handler).RequestDeviceCodeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("d1", result.Value!.DeviceCode);
        Assert.Equal("AB-12", result.Value.UserCode);
        Assert.Equal(5, result.Value.Interval);
        Assert.EndsWith("/device/code", handler.Requests.Single().Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task RequestDeviceCode_parses_verification_uri_complete()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"device_code":"d1","user_code":"AB-12","verification_uri":"http://x/approve","verification_uri_complete":"http://x/approve?code=AB-12","interval":5,"expires_in":300}""");

        var result = await Make(handler).RequestDeviceCodeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("http://x/approve?code=AB-12", result.Value!.VerificationUriComplete);
    }

    [Fact]
    public async Task PollDeviceToken_returns_pending()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"status":"pending"}""");
        var result = await Make(handler).PollDeviceTokenAsync("d1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pending", result.Value!.Status);
        Assert.Null(result.Value.ApiKey);
    }

    [Fact]
    public async Task PollDeviceToken_returns_key_on_success()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK, """{"api_key":"ea_secret","scopes":"gear:write"}""");

        var result = await Make(handler).PollDeviceTokenAsync("d1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ea_secret", result.Value!.ApiKey);
    }

    [Fact]
    public async Task PollDeviceToken_treats_400_as_terminal_value()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, """{"status":"expired"}""");
        var result = await Make(handler).PollDeviceTokenAsync("d1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("expired", result.Value!.Status);
    }

    [Fact]
    public async Task PushGear_sends_bearer_and_snake_case_body()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK, """{"status":"ok","character_id":"42","gearsets":1}""");

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash), JobScope.All);
        var result = await Make(handler).PushGearAsync("ea_secret", payload, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Gearsets);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("Bearer ea_secret", request.Authorization);
        Assert.Contains("\"protocol_version\":1", request.Body);
        Assert.Contains("\"cid_hash\":", request.Body);
        Assert.Contains("\"gear_index\":0", request.Body);
        Assert.Contains("\"Weapon\":", request.Body); // slot keys stay PascalCase
        Assert.Contains("\"scope\":\"all\"", request.Body);
    }

    /// <summary>
    /// The job table route reads no key, so none is sent. It is fetched before one exists on purpose: the
    /// role split is then right from the first start, and the shipped copy covers only "no network"
    /// instead of also "not connected yet".
    /// </summary>
    [Fact]
    public async Task GetJobTable_sends_no_authorization_header()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"version":"2026-08-22","jobs":[{"code":"PLD","role":"tank","combat":true,"from":["GLA"]}]}""");

        var result = await Make(handler).GetJobTableAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var job = Assert.Single(result.Value!.Jobs);
        Assert.Equal("PLD", job.Code);
        Assert.Equal("tank", job.Role);
        Assert.True(job.Combat);
        Assert.Equal(["GLA"], job.From);

        var request = handler.Requests.Single();
        Assert.Null(request.Authorization);
        Assert.Contains("/gear/jobs", request.Uri!.ToString());
    }

    [Fact]
    public async Task GetReview_sends_bearer_and_the_character()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"state_token":"9f13","held":[],"orphans":[]}""");

        var result = await Make(handler).GetReviewAsync("ea_secret", "42", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("9f13", result.Value!.StateToken);
        Assert.False(result.Value.IsConflict);

        var request = handler.Requests.Single();
        Assert.Equal("Bearer ea_secret", request.Authorization);
        Assert.Contains("character_id=42", request.Uri!.ToString());
    }

    /// <summary>
    /// The empty case still carries a token, so a client arriving after somebody else answered everything
    /// can tell "nothing is open" from "I have no token".
    /// </summary>
    [Fact]
    public async Task GetReview_keeps_the_token_when_nothing_waits()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"state_token":"empty-but-real","held":[],"orphans":[]}""");

        var result = await Make(handler).GetReviewAsync("ea_secret", "42", CancellationToken.None);

        Assert.Equal("empty-but-real", result.Value!.StateToken);
        Assert.Empty(result.Value.Held);
        Assert.Empty(result.Value.Orphans);
    }

    [Fact]
    public async Task PostReviewDecision_sends_the_verb_and_the_token()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"result":{"set_uid":"c05e","action":"link","requested":"link","surviving_uid":"9a1f","state":"active"},"also_resolved":["d18a"],"state_token":"next","held":[],"orphans":[]}""");

        var decision = new ReviewDecision { SetUid = "c05e", Action = ReviewAction.Link, TargetUid = "9a1f" };
        var result = await Make(handler).PostReviewDecisionAsync("ea_secret", "42", "9f13", decision, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("9a1f", result.Value!.Result!.SurvivingUid);
        Assert.Equal(["d18a"], result.Value.AlsoResolved);
        Assert.Equal("next", result.Value.StateToken);
        Assert.False(result.Value.Result.WasConverted);

        var body = handler.Requests.Single().Body!;
        Assert.Contains("\"state_token\":\"9f13\"", body);
        Assert.Contains("\"set_uid\":\"c05e\"", body);
        Assert.Contains("\"action\":\"link\"", body);
        Assert.Contains("\"target_uid\":\"9a1f\"", body);
    }

    /// <summary>
    /// A stale token is not an error to report, it is the state to render. The 409 body carries the same
    /// shape plus what moved, so there is one parser and one renderer, and nothing was applied.
    /// </summary>
    [Fact]
    public async Task PostReviewDecision_returns_a_stale_token_as_a_value()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.Conflict,
            """{"state_token":"moved","conflicts":["7bb2"],"held":[],"orphans":[]}""");

        var decision = new ReviewDecision { SetUid = "c05e", Action = ReviewAction.New };
        var result = await Make(handler).PostReviewDecisionAsync("ea_secret", "42", "old", decision, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsConflict);
        Assert.Equal(["7bb2"], result.Value.Conflicts);
        Assert.Equal("moved", result.Value.StateToken);
        Assert.Null(result.Value.Result);
    }

    /// <summary>
    /// A delete on a shared plugin row is performed as a release. The window shows what happened, not what
    /// was asked, or it would say "deleted" about a row that is still there.
    /// </summary>
    [Fact]
    public async Task PostReviewDecision_reports_a_converted_action()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"result":{"set_uid":"7bb2","action":"release","requested":"delete","surviving_uid":"7bb2","state":"ignored"},"also_resolved":[],"state_token":"next","held":[],"orphans":[]}""");

        var decision = new ReviewDecision { SetUid = "7bb2", Action = ReviewAction.Delete };
        var result = await Make(handler).PostReviewDecisionAsync("ea_secret", "42", "9f13", decision, CancellationToken.None);

        var performed = result.Value!.Result!;
        Assert.True(performed.WasConverted);
        Assert.Equal(ReviewAction.Release, performed.Action);
        Assert.Equal(ReviewAction.Delete, performed.Requested);
    }

    /// <summary>
    /// A reopen on a hand-made row leaves it in no state at all, so the field is nullable. Modelled as
    /// non-nullable, that one call breaks.
    /// </summary>
    [Fact]
    public async Task PostReviewDecision_accepts_a_null_state()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"result":{"set_uid":"7bb2","action":"reopen","requested":"reopen","surviving_uid":"7bb2","state":null},"also_resolved":[],"state_token":"next","held":[],"orphans":[]}""");

        var decision = new ReviewDecision { SetUid = "7bb2", Action = ReviewAction.Reopen };
        var result = await Make(handler).PostReviewDecisionAsync("ea_secret", "42", "9f13", decision, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Result!.State);
    }

    [Fact]
    public async Task AcceptReviewMapping_sends_every_pair()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"results":[{"set_uid":"c05e","action":"link","requested":"link","surviving_uid":"9a1f","state":"active"}],"also_resolved":[],"state_token":"next","held":[],"orphans":[]}""");

        ReviewDecision[] pairs =
        [
            new() { SetUid = "c05e", Action = ReviewAction.Link, TargetUid = "9a1f" },
            new() { SetUid = "d18a", Action = ReviewAction.New },
        ];
        var result = await Make(handler).AcceptReviewMappingAsync("ea_secret", "42", "9f13", pairs, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Results);

        var body = handler.Requests.Single().Body!;
        Assert.Contains("\"pairs\":[", body);
        Assert.Contains("\"d18a\"", body);
    }

    /// <summary>
    /// A rejected job code arrives as a named cause, not as a bare 422: the rule that discards the cached
    /// table turns on the cause, or a client would refetch it after every failed push.
    /// </summary>
    [Fact]
    public async Task PushGear_surfaces_the_job_unknown_cause_and_its_codes()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.UnprocessableEntity,
            """{"error":"job_unknown","jobs":["CRP","MIN"]}""");

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash), JobScope.All);
        var result = await Make(handler).PushGearAsync("ea_secret", payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(ApiErrorCodes.JobUnknown, result.Error.Code);
        Assert.Equal(["CRP", "MIN"], result.Error.Jobs);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.BadRequest, ApiErrorKind.BadRequest)]
    public async Task PushGear_maps_errors(HttpStatusCode status, ApiErrorKind expected)
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            status,
            """{"type":"about:blank","title":"nope","status":0,"detail":"x","request_id":"req-99"}""",
            contentType: "application/problem+json");

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash), JobScope.All);
        var result = await Make(handler).PushGearAsync("k", payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal("req-99", result.Error.RequestId);
    }

    [Fact]
    public async Task PushGear_maps_429_with_retry_after()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.TooManyRequests,
            """{"title":"slow down","request_id":"req-1"}""",
            retryAfterSeconds: 120);

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash), JobScope.All);
        var result = await Make(handler).PushGearAsync("k", payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.RateLimited, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(120), result.Error.RetryAfter);
    }

    [Fact]
    public async Task GetVersion_parses_scopes_array()
    {
        // Regression: GET /version returns "scopes" as a JSON array, not a scalar string.
        // A scalar-typed model made deserialization throw ("Could not parse the server response").
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"api_version":"v1","app_version":"0.1.0-dev","protocol_version":1,"scopes":["profile:read","gear:write"],"webhook_events":["gear.updated"]}""");

        var result = await Make(handler).GetVersionAsync(null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("v1", result.Value!.ApiVersion);
        Assert.Equal(1, result.Value.ProtocolVersion);
        Assert.Contains("gear:write", result.Value.Scopes!);
        Assert.EndsWith("/version", handler.Requests.Single().Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetBis_sends_auth_and_cid_hash_and_parses()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"protocol_version":1,"data":[{"cid_hash":"abc","job":"DRK","gear_index":3,"name":"2.50","items":{"Weapon":{"id":49668,"materia":[41773]}}}]}""");

        var result = await Make(handler).GetBisAsync("ea_key", "abc", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value!.Data);
        Assert.Equal("DRK", entry.Job);
        Assert.Equal(3, entry.GearIndex);
        Assert.Equal(49668, entry.Items["Weapon"].Id);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer ea_key", request.Authorization);
        Assert.Contains("/gear/bis?cid_hash=abc", request.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetBis_parses_item_and_set_source()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{"protocol_version":1,"data":[{"job":"DRK","gear_index":0,"name":"BiS","source":"raid","items":{"Weapon":{"id":49668,"materia":[],"source":"ultimate"}}}]}""");

        var result = await Make(handler).GetBisAsync("k", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var set = Assert.Single(result.Value!.Data);
        Assert.Equal("raid", set.Source);
        Assert.Equal("ultimate", set.Items["Weapon"].Source);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorKind.NotFound)]
    public async Task GetBis_maps_errors(HttpStatusCode status, ApiErrorKind expected)
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            status, """{"title":"x","request_id":"req-7"}""", contentType: "application/problem+json");

        var result = await Make(handler).GetBisAsync("k", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
    }

    [Fact]
    public async Task Network_failure_maps_to_network_error()
    {
        var client = new ApiClient(new HttpClient(new ThrowingHandler()), new StubHttpMessageHandler.Settings());
        var result = await client.GetVersionAsync(null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Network, result.Error!.Kind);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
