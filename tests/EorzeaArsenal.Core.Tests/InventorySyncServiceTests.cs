using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Verifies the inventory orchestrator: single in-flight + scope coalescing, throttle/unchanged
/// skip, transient back-off (and no tight-spin on terminal failures), and validate-before-send.
/// </summary>
public sealed class InventorySyncServiceTests
{
    private static CharacterDto Character() =>
        new() { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash };

    private static InventoryData CharacterSnapshot() => new()
    {
        Character = Character(),
        Scopes = [InventoryProtocol.ScopeCharacter],
        Items =
        [
            new InventoryItemDto { ItemId = 49671, Container = InventoryContainers.Armoury },
            new InventoryItemDto { ItemId = 12345, Container = InventoryContainers.Bags, Qty = 2, Hq = true },
        ],
    };

    private static InventoryData RetainerSnapshot(string id) => new()
    {
        Character = Character(),
        Scopes = [InventoryProtocol.RetainerScope(id)],
        Items = [new InventoryItemDto { ItemId = 33333, Container = InventoryContainers.Retainer, SourceId = id }],
    };

    private static (InventorySyncService svc, FakeInventorySource src, FakeApiClient api, InMemoryTokenStore tokens, TestClock clock)
        Make(bool connected = true, bool available = true)
    {
        var src = new FakeInventorySource { IsAvailable = available, Snapshot = CharacterSnapshot() };
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        if (connected)
        {
            tokens.SetApiKey("ea_key");
        }

        var clock = new TestClock();
        var svc = new InventorySyncService(src, api, tokens, clock);
        return (svc, src, api, tokens, clock);
    }

    private static async Task<InventoryReport> Wait(InventorySyncService svc, Action trigger)
    {
        var tcs = new TaskCompletionSource<InventoryReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(InventoryReport r)
        {
            svc.SyncCompleted -= Handler;
            tcs.TrySetResult(r);
        }

        svc.SyncCompleted += Handler;
        trigger();
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Manual_character_sync_sends_and_reports_counts()
    {
        var (svc, _, api, _, _) = Make();

        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));

        Assert.Equal(InventoryOutcome.Sent, report.Outcome);
        Assert.Equal(2, report.ItemCount);
        Assert.Equal(1, report.ScopeCount);
        Assert.Equal(1, api.InventoryCalls);
        Assert.Equal(2, ProtocolVersionOf(api));
        Assert.NotNull(svc.LastSuccessfulSyncUtc);
    }

    private static int ProtocolVersionOf(FakeApiClient api) => api.InventoryPayloads[0].ProtocolVersion;

    [Fact]
    public async Task No_key_reports_not_connected()
    {
        var (svc, _, api, _, _) = Make(connected: false);

        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));

        Assert.Equal(InventoryOutcome.NotConnected, report.Outcome);
        Assert.Equal(0, api.InventoryCalls);
    }

    [Fact]
    public async Task Not_available_reports_not_logged_in()
    {
        var (svc, _, api, _, _) = Make(available: false);

        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));

        Assert.Equal(InventoryOutcome.NotLoggedIn, report.Outcome);
        Assert.Equal(0, api.InventoryCalls);
    }

    [Fact]
    public async Task Unchanged_scope_is_skipped_on_auto()
    {
        var (svc, _, api, _, _) = Make();
        svc.MinAutoSyncInterval = TimeSpan.Zero;

        var first = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));
        var second = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Auto));

        Assert.Equal(InventoryOutcome.Sent, first.Outcome);
        Assert.Equal(InventoryOutcome.SkippedUnchanged, second.Outcome);
        Assert.Equal(1, api.InventoryCalls);
    }

    [Fact]
    public async Task Auto_sync_is_throttled()
    {
        var (svc, _, api, _, _) = Make(); // default MinAutoSyncInterval = 15 min

        await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));
        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Auto));

        Assert.Equal(InventoryOutcome.SkippedThrottled, report.Outcome);
        Assert.Equal(1, api.InventoryCalls);
    }

    [Fact]
    public async Task Retainer_scope_is_uploaded_with_source_id()
    {
        var (svc, _, api, _, _) = Make();

        var report = await Wait(svc, () => svc.RequestScopeSync(RetainerSnapshot("r1")));

        Assert.Equal(InventoryOutcome.Sent, report.Outcome);
        Assert.Equal(1, api.InventoryCalls);
        var payload = api.InventoryPayloads[0];
        Assert.Equal([InventoryProtocol.RetainerScope("r1")], payload.Scopes);
        Assert.Equal("r1", Assert.Single(payload.Items).SourceId);
    }

    [Fact]
    public async Task Empty_character_scope_still_sends_to_clear()
    {
        var (svc, src, api, _, _) = Make();
        src.Snapshot = new InventoryData
        {
            Character = Character(),
            Scopes = [InventoryProtocol.ScopeCharacter],
            Items = [],
        };

        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));

        Assert.Equal(InventoryOutcome.Sent, report.Outcome);
        Assert.Equal(1, api.InventoryCalls);
        Assert.Empty(api.InventoryPayloads[0].Items);
    }

    [Fact]
    public async Task RateLimit_backs_off_then_resumes()
    {
        var (svc, _, api, _, clock) = Make();
        api.EnqueueInventory(ApiResult<InventoryPushResult>.Fail(new ApiError
        {
            Kind = ApiErrorKind.RateLimited,
            StatusCode = 429,
            Message = "slow down",
            RetryAfter = TimeSpan.FromMinutes(10),
        }));

        var first = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));
        Assert.Equal(InventoryOutcome.Failed, first.Outcome);
        Assert.Equal(ApiErrorKind.RateLimited, first.ErrorKind);
        Assert.True(svc.IsRateLimited);

        var second = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));
        Assert.Equal(InventoryOutcome.SkippedBackoff, second.Outcome);

        clock.Advance(TimeSpan.FromMinutes(11));
        var third = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));
        Assert.Equal(InventoryOutcome.Sent, third.Outcome);
    }

    [Fact]
    public async Task Terminal_failure_does_not_loop()
    {
        var (svc, _, api, _, _) = Make();
        api.EnqueueInventory(ApiResult<InventoryPushResult>.Fail(new ApiError
        {
            Kind = ApiErrorKind.Forbidden,
            StatusCode = 403,
            Message = "missing inventory:write",
        }));

        var report = await Wait(svc, () => svc.RequestCharacterSync(InventoryTrigger.Manual));

        Assert.Equal(InventoryOutcome.Failed, report.Outcome);
        Assert.Equal(ApiErrorKind.Forbidden, report.ErrorKind);
        Assert.False(svc.IsRateLimited);

        // The data is dropped (not requeued), so it does not keep retrying a 403.
        await Task.Delay(50);
        Assert.Equal(1, api.InventoryCalls);
    }
}
