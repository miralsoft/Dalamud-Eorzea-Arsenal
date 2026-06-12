using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Device-flow state machine and the paste/disconnect paths.</summary>
public sealed class ConnectionServiceTests
{
    private static DeviceCodeResponse Code(int interval = 1, int expires = 3) => new()
    {
        DeviceCode = "d1",
        UserCode = "AB-12",
        VerificationUri = "http://x/approve",
        Interval = interval,
        ExpiresIn = expires,
    };

    private static (ConnectionService svc, FakeApiClient api, InMemoryTokenStore tokens) Make()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        var svc = new ConnectionService(api, tokens, new FakeDelay());
        return (svc, api, tokens);
    }

    [Fact]
    public async Task Polls_until_key_then_stores_it()
    {
        var (svc, api, tokens) = Make();
        api.EnqueueToken(ApiResult<DeviceTokenResponse>.Ok(new DeviceTokenResponse { Status = "pending" }));
        api.EnqueueToken(ApiResult<DeviceTokenResponse>.Ok(new DeviceTokenResponse { ApiKey = "ea_key", Scopes = "gear:write" }));

        var result = await svc.PollForKeyAsync(Code(), CancellationToken.None);

        Assert.Equal(ConnectOutcome.Success, result.Outcome);
        Assert.Equal("ea_key", tokens.ApiKey);
    }

    [Fact]
    public async Task Expires_when_never_approved()
    {
        var (svc, _, tokens) = Make();
        // No tokens enqueued → FakeApiClient returns "pending" forever; loop ends at expiry.
        var result = await svc.PollForKeyAsync(Code(interval: 1, expires: 3), CancellationToken.None);

        Assert.Equal(ConnectOutcome.Expired, result.Outcome);
        Assert.False(tokens.HasKey);
    }

    [Fact]
    public async Task Denied_status_is_terminal()
    {
        var (svc, api, _) = Make();
        api.EnqueueToken(ApiResult<DeviceTokenResponse>.Ok(new DeviceTokenResponse { Status = "denied" }));

        var result = await svc.PollForKeyAsync(Code(), CancellationToken.None);

        Assert.Equal(ConnectOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task Cancellation_stops_the_flow()
    {
        var (svc, _, _) = Make();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await svc.PollForKeyAsync(Code(), cts.Token);

        Assert.Equal(ConnectOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public void Paste_key_stores_trimmed_value()
    {
        var (svc, _, tokens) = Make();

        Assert.True(svc.ConnectWithPastedKey("  ea_pasted  "));
        Assert.Equal("ea_pasted", tokens.ApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Paste_key_rejects_empty(string? key)
    {
        var (svc, _, tokens) = Make();

        Assert.False(svc.ConnectWithPastedKey(key));
        Assert.False(tokens.HasKey);
    }

    [Fact]
    public void Disconnect_clears_key()
    {
        var (svc, _, tokens) = Make();
        tokens.SetApiKey("ea_key");

        svc.Disconnect();

        Assert.False(tokens.HasKey);
    }
}
