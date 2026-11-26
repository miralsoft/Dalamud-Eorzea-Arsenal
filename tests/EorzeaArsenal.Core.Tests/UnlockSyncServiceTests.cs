using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The learned unlocks of goal items, held to <c>contract-goals.md</c>.</summary>
public sealed class UnlockSyncServiceTests
{
    private readonly FakeApiClient _api = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly TestClock _clock = new();
    private readonly FakeUnlockSource _source = new();
    private List<int> _ids = [6188, 52270, 99999];

    public UnlockSyncServiceTests() => _tokens.SetApiKey("ea_test_key");

    private UnlockSyncService Service() => new(_source, _api, _tokens, _clock, () => _ids);

    private static ApiResult<UnlocksResult> Fail(ApiErrorKind kind, int status, TimeSpan? retryAfter = null) =>
        ApiResult<UnlocksResult>.Fail(new ApiError { Kind = kind, StatusCode = status, Message = "x", RetryAfter = retryAfter });

    [Fact]
    public async Task Checked_is_what_was_looked_at_and_learned_only_what_of_it_is_learned()
    {
        _source.Unlockable = [52270, 6188];
        _source.Learned = [52270, 777];
        using var service = Service();

        Assert.Equal(UnlockOutcome.Sent, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        var sent = Assert.Single(_api.UnlockPushes);
        Assert.Equal([6188, 52270], sent.Checked);
        Assert.Equal([52270], sent.Learned);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(sent, EorzeaJson.Options));
        Assert.Equal(TestData.ExampleHash, json.RootElement.GetProperty("character").GetProperty("cid_hash").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("checked").GetArrayLength());
        Assert.Equal(52270, json.RootElement.GetProperty("learned")[0].GetInt32());
    }

    [Fact]
    public async Task The_same_report_is_not_sent_twice_and_a_change_waits_five_minutes()
    {
        _source.Unlockable = [6188];
        using var service = Service();
        await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None);

        Assert.Equal(UnlockOutcome.SkippedUnchanged, (await service.SyncAsync(UnlockTrigger.AfterInventorySync, CancellationToken.None)).Outcome);

        _source.Learned = [6188];
        Assert.Equal(UnlockOutcome.SkippedThrottled, (await service.SyncAsync(UnlockTrigger.Change, CancellationToken.None)).Outcome);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(UnlockOutcome.Sent, (await service.SyncAsync(UnlockTrigger.Change, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Without_unlock_goals_nothing_is_sent()
    {
        _ids = [];
        using var service = Service();
        Assert.Equal(UnlockOutcome.Nothing, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _ids = [1];
        _source.Unlockable = [];
        Assert.Equal(UnlockOutcome.Nothing, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Empty(_api.UnlockPushes);
    }

    [Fact]
    public async Task A_404_sends_once_more_after_a_gear_sync_and_then_stops_for_the_session()
    {
        _source.Unlockable = [6188];
        _api.UnlockResults.Enqueue(Fail(ApiErrorKind.NotFound, 404));
        using var service = Service();

        Assert.Equal(UnlockOutcome.WaitingForGear, (await service.SyncAsync(UnlockTrigger.AfterInventorySync, CancellationToken.None)).Outcome);
        Assert.Equal(UnlockOutcome.WaitingForGear, (await service.SyncAsync(UnlockTrigger.AfterInventorySync, CancellationToken.None)).Outcome);
        Assert.Single(_api.UnlockPushes);

        await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None);
        await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None);
        Assert.Equal(2, _api.UnlockPushes.Count);
    }

    [Fact]
    public async Task A_429_waits_for_its_retry_after()
    {
        _source.Unlockable = [6188];
        _api.UnlockResults.Enqueue(Fail(ApiErrorKind.RateLimited, 429, TimeSpan.FromSeconds(90)));
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Ok(new UnlocksResult { Status = "ok", Checked = 1 }));
        using var service = Service();

        Assert.Equal(UnlockOutcome.SkippedBackoff, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        _clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(UnlockOutcome.SkippedBackoff, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(UnlockOutcome.Sent, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_refused_key_stops_until_another_is_connected_and_a_refused_payload_is_not_repeated()
    {
        _source.Unlockable = [6188];
        _api.UnlockResults.Enqueue(Fail(ApiErrorKind.Forbidden, 403));
        _api.UnlockResults.Enqueue(Fail(ApiErrorKind.Validation, 422));
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Ok(new UnlocksResult { Status = "ok" }));
        using var service = Service();

        Assert.Equal(UnlockOutcome.Stopped, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(UnlockOutcome.Stopped, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _tokens.SetApiKey("ea_other_key");
        Assert.Equal(UnlockOutcome.Rejected, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(UnlockOutcome.SkippedUnchanged, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _source.Learned = [6188];
        Assert.Equal(UnlockOutcome.Sent, (await service.SyncAsync(UnlockTrigger.AfterGearSync, CancellationToken.None)).Outcome);
    }

    [Fact]
    public void The_list_of_unlock_items_is_kept_sorted()
    {
        var store = new TrackedItemsStore();
        store.SetUnlocks([52270, 6188, 0, 6188]);
        Assert.Equal([6188, 52270], store.Unlocks);
        store.SetUnlocks(null);
        Assert.Empty(store.Unlocks);
    }

    private sealed class FakeUnlockSource : IUnlockSource
    {
        public List<int> Unlockable { get; set; } = [];

        public List<int> Learned { get; set; } = [];

        public bool IsAvailable => true;

        public Task<UnlockSnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct)
        {
            var looked = itemIds.Where(Unlockable.Contains).ToList();
            return Task.FromResult<UnlockSnapshot?>(new UnlockSnapshot(
                new CharacterDto { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash },
                looked,
                [.. Learned]));
        }
    }
}
