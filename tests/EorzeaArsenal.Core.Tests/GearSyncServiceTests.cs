using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Verifies the orchestrator's hard rules: single in-flight + coalescing (P11), proactive
/// throttle/back-off/unchanged-skip (R23) and validate-before-send (R18).
/// </summary>
public sealed class GearSyncServiceTests
{
    private static (GearSyncService svc, FakeGearSource gear, FakeApiClient api, InMemoryTokenStore tokens, TestClock clock)
        Make(bool connected = true, bool loggedIn = true)
    {
        var gear = new FakeGearSource { IsAvailable = loggedIn, Snapshot = TestData.Snapshot(TestData.ExampleHash) };
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        if (connected)
        {
            tokens.SetApiKey("ea_key");
        }

        var clock = new TestClock();
        var svc = new GearSyncService(gear, api, tokens, clock) { MinAutoPushInterval = TimeSpan.FromMinutes(5) };
        return (svc, gear, api, tokens, clock);
    }

    private static async Task<PushReport> PushAndWait(GearSyncService svc, PushTrigger trigger)
    {
        var tcs = new TaskCompletionSource<PushReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(PushReport r)
        {
            svc.PushCompleted -= Handler;
            tcs.TrySetResult(r);
        }

        svc.PushCompleted += Handler;
        svc.RequestPush(trigger);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Manual_push_sends_and_reports_count()
    {
        var (svc, _, api, _, _) = Make();

        var report = await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.Sent, report.Outcome);
        Assert.Equal(1, report.GearsetCount);
        Assert.Equal(1, api.PushCalls);
    }

    [Fact]
    public async Task Tracks_last_report_and_success_time()
    {
        var (svc, _, _, _, _) = Make();

        Assert.Null(svc.LastReport);
        Assert.Null(svc.LastSuccessfulPushUtc);

        await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.Sent, svc.LastReport!.Value.Outcome);
        Assert.NotNull(svc.LastSuccessfulPushUtc);
        Assert.False(svc.IsRateLimited);
    }

    [Fact]
    public async Task No_key_reports_not_connected()
    {
        var (svc, _, api, _, _) = Make(connected: false);

        var report = await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.NotConnected, report.Outcome);
        Assert.Equal(0, api.PushCalls);
    }

    [Fact]
    public async Task Not_logged_in_reports_not_logged_in()
    {
        var (svc, _, api, _, _) = Make(loggedIn: false);

        var report = await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.NotLoggedIn, report.Outcome);
        Assert.Equal(0, api.PushCalls);
    }

    [Fact]
    public async Task Unchanged_data_is_not_resent_on_auto()
    {
        var (svc, _, api, _, _) = Make();
        svc.MinAutoPushInterval = TimeSpan.Zero;

        var first = await PushAndWait(svc, PushTrigger.Manual);
        var second = await PushAndWait(svc, PushTrigger.Auto);

        Assert.Equal(PushOutcome.Sent, first.Outcome);
        Assert.Equal(PushOutcome.SkippedUnchanged, second.Outcome);
        Assert.Equal(1, api.PushCalls);
    }

    [Fact]
    public async Task Automatic_push_is_throttled()
    {
        var (svc, _, api, _, _) = Make();

        await PushAndWait(svc, PushTrigger.Manual);          // sends at t0
        var report = await PushAndWait(svc, PushTrigger.Auto); // immediately after

        Assert.Equal(PushOutcome.SkippedThrottled, report.Outcome);
        Assert.Equal(1, api.PushCalls);
    }

    [Fact]
    public async Task Gearset_change_bypasses_throttle()
    {
        var (svc, _, api, _, _) = Make(); // MinAutoPushInterval = 5 min

        await PushAndWait(svc, PushTrigger.Manual);                      // sends at t0
        var report = await PushAndWait(svc, PushTrigger.GearsetChange);  // immediately after

        Assert.Equal(PushOutcome.Sent, report.Outcome); // event-driven → not throttled
        Assert.Equal(2, api.PushCalls);
    }

    [Fact]
    public async Task RateLimit_sets_backoff_for_subsequent_pushes()
    {
        var (svc, _, api, _, clock) = Make();
        api.EnqueuePush(ApiResult<GearPushResult>.Fail(new ApiError
        {
            Kind = ApiErrorKind.RateLimited,
            StatusCode = 429,
            Message = "slow down",
            RetryAfter = TimeSpan.FromMinutes(10),
        }));

        var first = await PushAndWait(svc, PushTrigger.Manual);
        var second = await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.Failed, first.Outcome);
        Assert.Equal(ApiErrorKind.RateLimited, first.ErrorKind);
        Assert.Equal(PushOutcome.SkippedBackoff, second.Outcome);

        // After the back-off window, manual pushes resume.
        clock.Advance(TimeSpan.FromMinutes(11));
        var third = await PushAndWait(svc, PushTrigger.Manual);
        Assert.Equal(PushOutcome.Sent, third.Outcome);
    }

    [Fact]
    public async Task Invalid_local_data_is_not_sent()
    {
        var (svc, gear, api, _, _) = Make();
        // Valid gearset but a broken cid_hash → passes sanitize, fails validation.
        gear.Snapshot = new GearData
        {
            Character = new CharacterDto { Name = "X", World = "Y", CidHash = "not-a-hash" },
            Gearsets = TestData.Snapshot(TestData.ExampleHash).Gearsets,
        };

        var report = await PushAndWait(svc, PushTrigger.Manual);

        Assert.Equal(PushOutcome.InvalidLocal, report.Outcome);
        Assert.Equal(0, api.PushCalls);
    }

    [Fact]
    public async Task Overlapping_triggers_coalesce_into_one_inflight_push()
    {
        var gear = new FakeGearSource { Snapshot = TestData.Snapshot(TestData.ExampleHash) };
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("ea_key");
        var blocking = new BlockingApiClient();
        var svc = new GearSyncService(gear, blocking, tokens, new TestClock()) { MinAutoPushInterval = TimeSpan.Zero };

        var completed = 0;
        svc.PushCompleted += _ => Interlocked.Increment(ref completed);

        // First manual push starts and blocks inside the API call.
        svc.RequestPush(PushTrigger.Manual);
        Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(5)));

        // While it is in flight, several more triggers arrive — they must coalesce, not start
        // a second concurrent request.
        svc.RequestPush(PushTrigger.GearsetChange);
        svc.RequestPush(PushTrigger.GearsetChange);
        svc.RequestPush(PushTrigger.Manual);

        blocking.Release.Set(); // let the first call (and the single coalesced follow-up) finish

        var spin = SpinWait.SpinUntil(() => Volatile.Read(ref completed) >= 2, TimeSpan.FromSeconds(5));

        Assert.True(spin);
        Assert.Equal(1, blocking.MaxConcurrent);   // never two at once (P11)
        Assert.Equal(2, blocking.TotalCalls);      // initial + one coalesced (not three)
    }

    /// <summary>An API client whose push blocks until released, tracking concurrency.</summary>
    private sealed class BlockingApiClient : IApiClient
    {
        private int _concurrent;

        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public int MaxConcurrent { get; private set; }

        public int TotalCalls { get; private set; }

        public Task<ApiResult<DeviceCodeResponse>> RequestDeviceCodeAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ApiResult<DeviceTokenResponse>> PollDeviceTokenAsync(string deviceCode, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ApiResult<VersionResponse>> GetVersionAsync(string? apiKey, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ApiResult<BisResponse>> GetBisAsync(string apiKey, string? cidHash, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ApiResult<GearPushResult>> PushGearAsync(string apiKey, GearPayload payload, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            TotalCalls++;
            Entered.Set();
            Release.Wait(ct);
            Interlocked.Decrement(ref _concurrent);
            return Task.FromResult(ApiResult<GearPushResult>.Ok(new GearPushResult { Status = "ok", Gearsets = payload.Gearsets.Count }));
        }
    }
}
