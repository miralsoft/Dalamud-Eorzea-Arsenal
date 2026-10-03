using System.Net;
using EorzeaArsenal.Api;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// What the stored key may do, read from <c>GET /device/key</c>. Only a known list that lacks a scope locks
/// anything: a server that cannot say, or a question that failed, leaves the delete button live and lets a
/// 403 decide, because guessing "no" would hide a working feature.
/// </summary>
public sealed class KeyScopeServiceTests
{
    [Fact]
    public async Task AKnownListWithoutGearDeleteLocksTheDelete()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Scopes("gear:write", "gear:read", "gear:review");

        var state = await service.EnsureAsync(CancellationToken.None);

        Assert.Equal(KeyScopeState.Known, state);
        Assert.True(service.IsKnownToLack(ScopeUtil.GearDelete));
        Assert.Equal(false, service.Has(ScopeUtil.GearDelete));
        Assert.Equal(true, service.Has(ScopeUtil.GearWrite));
    }

    [Fact]
    public async Task AKnownListWithGearDeleteLeavesItLive()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Scopes("gear:write", "gear:review", " GEAR:DELETE ");

        await service.EnsureAsync(CancellationToken.None);

        Assert.False(service.IsKnownToLack(ScopeUtil.GearDelete));
    }

    /// <summary>
    /// A 404 is a server before 1.4, where deleting works with <c>gear:review</c> alone. Locking the button
    /// there would hide a feature that works.
    /// </summary>
    [Fact]
    public async Task AnOldServerChecksNothing()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Fail(ApiErrorKind.NotFound);

        var state = await service.EnsureAsync(CancellationToken.None);

        Assert.Equal(KeyScopeState.NotOffered, state);
        Assert.False(service.IsKnownToLack(ScopeUtil.GearDelete));
        Assert.Null(service.Has(ScopeUtil.GearWrite));
    }

    [Fact]
    public async Task AFailedQuestionChecksNothingAndIsAskedAgain()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Fail(ApiErrorKind.Network);

        Assert.Equal(KeyScopeState.Failed, await service.EnsureAsync(CancellationToken.None));
        Assert.False(service.IsKnownToLack(ScopeUtil.GearDelete));

        api.KeyScopesResult = Scopes("gear:write");
        Assert.Equal(KeyScopeState.Known, await service.EnsureAsync(CancellationToken.None));
        Assert.Equal(2, api.KeyScopesCalls);
    }

    /// <summary>Once per key: opening the window ten times does not ask ten times.</summary>
    [Fact]
    public async Task AnAnswerIsAskedForOncePerKey()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Scopes("gear:write");

        await service.EnsureAsync(CancellationToken.None);
        await service.EnsureAsync(CancellationToken.None);

        Assert.Equal(1, api.KeyScopesCalls);
    }

    /// <summary>
    /// A new key, from pairing again, is exactly how the player gets the right. Its predecessor's answer must
    /// not keep the button locked.
    /// </summary>
    [Fact]
    public async Task ANewKeyForgetsTheOldAnswer()
    {
        var (service, api, tokens) = Build();
        api.KeyScopesResult = Scopes("gear:write");
        await service.EnsureAsync(CancellationToken.None);
        service.NoteRefused(ScopeUtil.GearDelete);

        tokens.SetApiKey("ea_second");

        Assert.Equal(KeyScopeState.NotAsked, service.State);
        Assert.False(service.IsKnownToLack(ScopeUtil.GearDelete));
    }

    /// <summary>
    /// A 403 that names the scope is as good as the list, so the lock appears after the first refusal even on
    /// a server that answered nothing at <c>/device/key</c>.
    /// </summary>
    [Fact]
    public async Task ARefusalNamingTheScopeLocksItToo()
    {
        var (service, api, _) = Build();
        api.KeyScopesResult = Fail(ApiErrorKind.NotFound);
        await service.EnsureAsync(CancellationToken.None);

        service.NoteRefused(ScopeUtil.GearDelete);

        Assert.True(service.IsKnownToLack(ScopeUtil.GearDelete));
        Assert.Equal(false, service.Has(ScopeUtil.GearDelete));
    }

    [Fact]
    public async Task NoKeyAsksNothing()
    {
        var api = new FakeApiClient();
        var service = new KeyScopeService(api, new InMemoryTokenStore());

        Assert.Equal(KeyScopeState.NotAsked, await service.EnsureAsync(CancellationToken.None));
        Assert.Equal(0, api.KeyScopesCalls);
    }

    /// <summary>The route and the shape, from the wire.</summary>
    [Fact]
    public async Task TheClientReadsTheKeysScopes()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"scopes":["gear:write","gear:delete"],"later":1}""");
        var client = new ApiClient(new HttpClient(handler), new StubHttpMessageHandler.Settings());

        var result = await client.GetKeyScopesAsync("ea_secret", CancellationToken.None);

        Assert.Equal(["gear:write", "gear:delete"], result.Value!.Scopes);
        Assert.EndsWith("/device/key", handler.Requests.Single().Uri!.AbsolutePath);
        Assert.Equal("Bearer ea_secret", handler.Requests.Single().Authorization);
    }

    private static ApiResult<KeyScopesResponse> Scopes(params string[] scopes) =>
        ApiResult<KeyScopesResponse>.Ok(new KeyScopesResponse { Scopes = [.. scopes] });

    private static ApiResult<KeyScopesResponse> Fail(ApiErrorKind kind) =>
        ApiResult<KeyScopesResponse>.Fail(new ApiError { Kind = kind, Message = "x" });

    private static (KeyScopeService Service, FakeApiClient Api, InMemoryTokenStore Tokens) Build()
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_secret");
        return (new KeyScopeService(api, tokens), api, tokens);
    }
}
