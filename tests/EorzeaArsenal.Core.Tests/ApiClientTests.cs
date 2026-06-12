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

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash));
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

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash));
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

        var payload = GearPayload.From(TestData.Snapshot(TestData.ExampleHash));
        var result = await Make(handler).PushGearAsync("k", payload, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.RateLimited, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(120), result.Error.RetryAfter);
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
