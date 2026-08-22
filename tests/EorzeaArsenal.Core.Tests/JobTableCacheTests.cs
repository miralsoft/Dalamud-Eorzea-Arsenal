using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The four situations the contract writes out for reaching the job table, plus the two rules that keep a
/// wrong answer from doing damage: the table is held per address, and a rejected code discards it.
/// </summary>
public sealed class JobTableCacheTests
{
    [Fact]
    public async Task ASuccessfulFetchGovernsAndDeclaresAll()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Ok("PLD", "CRP", "MIN");

        var policy = await cache.GetPolicyAsync(default);

        Assert.Equal(JobScope.All, policy.Scope);
        Assert.True(policy.Allows("CRP"));
        Assert.Equal(1, api.JobTableCalls);
    }

    [Fact]
    public async Task TheTableIsReadOnceAndThenHeld()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Ok("PLD");

        await cache.GetPolicyAsync(default);
        await cache.GetPolicyAsync(default);
        await cache.GetPolicyAsync(default);

        Assert.Equal(1, api.JobTableCalls);
    }

    [Fact]
    public async Task AStaleTableIsReadAgain()
    {
        var (cache, api, _, clock, log) = Build();
        api.JobTableResult = Ok("PLD");

        await cache.GetPolicyAsync(default);
        clock.Advance(JobTableCache.Freshness + TimeSpan.FromMinutes(1));
        await cache.GetPolicyAsync(default);

        Assert.Equal(2, api.JobTableCalls);
    }

    /// <summary>
    /// A 404 is an answer, not a failure: this server predates the route. The floor governs, the push says
    /// so, and the answer is remembered so every push does not ask again.
    /// </summary>
    [Fact]
    public async Task AnOldServerYieldsTheFloorAndIsRemembered()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Fail(ApiErrorKind.NotFound, 404);

        var first = await cache.GetPolicyAsync(default);
        var second = await cache.GetPolicyAsync(default);

        Assert.Equal(JobScope.Combat, first.Scope);
        Assert.Equal(JobScope.Combat, second.Scope);
        Assert.Equal(1, api.JobTableCalls);
        Assert.Null(cache.Current);
    }

    /// <summary>
    /// A transient failure with a table already held keeps that table governing, even past its freshness:
    /// a day-old truth beats holding back rows the server has.
    /// </summary>
    [Fact]
    public async Task ATransientFailureFallsBackToTheTableInHand()
    {
        var (cache, api, _, clock, log) = Build();
        api.JobTableResult = Ok("PLD", "CRP");
        await cache.GetPolicyAsync(default);

        clock.Advance(JobTableCache.Freshness + TimeSpan.FromMinutes(1));
        api.JobTableResult = Fail(ApiErrorKind.Network);
        var policy = await cache.GetPolicyAsync(default);

        Assert.Equal(JobScope.All, policy.Scope);
        Assert.True(policy.Allows("CRP"));
    }

    /// <summary>
    /// The conservative row, and deliberately so: a floor push is honestly labelled, so it damages nothing
    /// and syncs most of it, where a full push against a server that rejects a code syncs nothing at all.
    /// </summary>
    [Fact]
    public async Task ATransientFailureWithNothingEverSeenYieldsTheFloor()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Fail(ApiErrorKind.Network);

        var policy = await cache.GetPolicyAsync(default);

        Assert.Equal(JobScope.Combat, policy.Scope);
        Assert.False(policy.Allows("CRP"));
    }

    /// <summary>
    /// Held per address, never globally. Two machines, or one machine on two days, can talk to live and to
    /// a test instance, and a table remembered globally would let one decide what goes to the other.
    /// </summary>
    [Fact]
    public async Task ATableFromOneAddressDoesNotGovernAnother()
    {
        var (cache, api, settings, _, log) = Build();
        api.JobTableResult = Ok("PLD", "CRP");
        var onDev = await cache.GetPolicyAsync(default);

        settings.BaseUrl = "https://xivarsenal.app/api/v1";
        api.JobTableResult = Fail(ApiErrorKind.Network);
        var onLive = await cache.GetPolicyAsync(default);

        Assert.True(onDev.Allows("CRP"));
        Assert.Equal(JobScope.Combat, onLive.Scope);
        Assert.False(onLive.Allows("CRP"));
    }

    /// <summary>
    /// The one case freshness cannot cover: a server rolled back to a version publishing fewer codes while
    /// its table is still held here. Without this the sync stays dead until the entry expires on its own.
    /// </summary>
    [Fact]
    public async Task ARejectedCodeDiscardsTheTableSoTheNextPushRefetches()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Ok("PLD", "CRP");
        await cache.GetPolicyAsync(default);

        cache.Invalidate(["CRP"]);

        Assert.Null(cache.Current);
        api.JobTableResult = Fail(ApiErrorKind.Network);
        var afterwards = await cache.GetPolicyAsync(default);

        Assert.Equal(2, api.JobTableCalls);
        Assert.Equal(JobScope.Combat, afterwards.Scope);
    }

    [Fact]
    public async Task DiscardingNamesTheCodesInTheLog()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Ok("PLD");
        await cache.GetPolicyAsync(default);

        cache.Invalidate(["CRP", "MIN"]);

        Assert.Contains(log.Messages, m => m.Contains("CRP") && m.Contains("MIN"));
    }

    /// <summary>The route reads no key, so none is sent: it is fetched before one exists.</summary>
    [Fact]
    public async Task TheTableIsFetchedWithoutAKey()
    {
        var (cache, api, _, _, log) = Build();
        api.JobTableResult = Ok("PLD");

        await cache.GetPolicyAsync(default);

        Assert.Equal(1, api.JobTableCalls);
    }

    private static Harness Build()
    {
        var api = new FakeApiClient();
        var settings = new MutableSettings();
        var clock = new TestClock();
        var log = new CapturingLog();
        return new Harness(new JobTableCache(api, settings, clock, log), api, settings, clock, log);
    }

    private sealed record Harness(
        JobTableCache Cache,
        FakeApiClient Api,
        MutableSettings Settings,
        TestClock Clock,
        CapturingLog Log);

    private static ApiResult<JobTableResponse> Ok(params string[] codes) =>
        ApiResult<JobTableResponse>.Ok(new JobTableResponse
        {
            Version = "2026-08-22",
            Jobs = [.. codes.Select(c => new JobEntry { Code = c })],
        });

    private static ApiResult<JobTableResponse> Fail(ApiErrorKind kind, int? status = null) =>
        ApiResult<JobTableResponse>.Fail(new ApiError
        {
            Kind = kind,
            StatusCode = status,
            Message = kind.ToString(),
        });

    /// <summary>Settings whose address can move, which is what the per-address rule is about.</summary>
    private sealed class MutableSettings : IApiSettings
    {
        public string BaseUrl { get; set; } = "https://dev.xivarsenal.app/api/v1";
    }
}
