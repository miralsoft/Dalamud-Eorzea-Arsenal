using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The collection of server 1.5 (<c>unlock-collection</c>), held to <c>contract-goals.md</c>.</summary>
public sealed class CollectionSyncServiceTests
{
    private readonly FakeApiClient _api = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly TestClock _clock = new() { UtcNow = new DateTimeOffset(2026, 11, 27, 18, 0, 0, TimeSpan.Zero) };
    private readonly FakeCollectionSource _source = new();
    private readonly Dictionary<string, Dictionary<string, CollectionKindRecord>> _records = [];
    private int _saves;

    public CollectionSyncServiceTests()
    {
        _tokens.SetApiKey("ea_test_key");
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Ok(Answer()));
    }

    private CollectionSyncService Service() => new(_source, _api, _tokens, _clock, () => _records, () => _saves++);

    /// <summary>The server's answer: it holds what was sent as learned for every kind.</summary>
    private UnlocksResult Answer() => new()
    {
        Status = "ok",
        Kinds = UnlockKinds.All.ToDictionary(k => k, k => new UnlockKindState { Learned = _source.LearnedOf(k).Count }),
    };

    private void ServerHolds(Dictionary<string, int> learned) =>
        _api.MyUnlocksResult = ApiResult<MyUnlocksResponse>.Ok(new MyUnlocksResponse
        {
            Data = new MyUnlocks { Kinds = learned.ToDictionary(kv => kv.Key, kv => new UnlockKindState { Learned = kv.Value }) },
        });

    [Fact]
    public async Task A_first_sync_sends_every_kind_in_full_one_call_each()
    {
        using var service = Service();

        Assert.Equal(CollectionOutcome.Sent, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));

        Assert.Equal(UnlockKinds.All, _api.UnlockPushes.Select(p => p.Kind));
        Assert.All(_api.UnlockPushes, p => Assert.True(p.Full));
        Assert.Equal(0, _api.MyUnlocksReads);

        var mounts = _api.UnlockPushes[0];
        Assert.Equal([6001, 6002, 6003], mounts.Checked);
        Assert.Equal([6001], mounts.Learned);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(mounts, EorzeaJson.Options));
        Assert.Equal("mount", json.RootElement.GetProperty("kind").GetString());
        Assert.True(json.RootElement.GetProperty("full").GetBoolean());

        Assert.Equal("2026-11-27", _records[TestData.ExampleHash]["mount"].FullDay);
        Assert.True(_saves > 0);
    }

    [Fact]
    public void The_goals_mode_body_carries_neither_kind_nor_full()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            new UnlocksRequest { Character = new CharacterDto { Name = "a", World = "b", CidHash = "c" }, Checked = [1], Learned = [] },
            EorzeaJson.Options));

        Assert.False(json.RootElement.TryGetProperty("kind", out _));
        Assert.False(json.RootElement.TryGetProperty("full", out _));
    }

    [Fact]
    public async Task The_same_day_the_check_sends_only_the_kinds_whose_count_the_website_changed()
    {
        using (var first = Service())
        {
            await first.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        }

        _api.UnlockPushes.Clear();
        ServerHolds(new() { ["mount"] = 1, ["minion"] = 1, ["orchestrion"] = 5, ["card"] = 0 });

        using var service = Service();
        Assert.Equal(CollectionOutcome.Sent, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));

        Assert.Equal(1, _api.MyUnlocksReads);
        Assert.Equal(["orchestrion"], _api.UnlockPushes.Select(p => p.Kind));
    }

    [Fact]
    public async Task A_new_day_sends_every_kind_in_full_again_and_a_session_looks_only_once()
    {
        using (var first = Service())
        {
            await first.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        }

        _api.UnlockPushes.Clear();
        ServerHolds(new() { ["mount"] = 1, ["minion"] = 1, ["orchestrion"] = 0, ["card"] = 0 });
        _clock.Advance(TimeSpan.FromDays(1));

        using var service = Service();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        Assert.Equal(4, _api.UnlockPushes.Count);

        Assert.Equal(CollectionOutcome.NothingDue, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));
        Assert.Equal(4, _api.UnlockPushes.Count);
    }

    [Fact]
    public async Task Something_learned_sends_only_the_changed_items_of_its_kind_and_keeps_the_servers_count()
    {
        using var service = Service();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        _api.UnlockPushes.Clear();
        _clock.Advance(TimeSpan.FromMinutes(5));

        _source.Learn("mount", 6003);
        _api.UnlockResults.Clear();
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Ok(Answer()));

        Assert.Equal(CollectionOutcome.Sent, await service.SyncAsync(CollectionTrigger.Change, CancellationToken.None));

        var push = Assert.Single(_api.UnlockPushes);
        Assert.Equal("mount", push.Kind);
        Assert.Null(push.Full);
        Assert.Equal([6003], push.Checked);
        Assert.Equal([6003], push.Learned);

        // The count the server answered after this changed-only report is the one the next login compares.
        Assert.Equal(2, _records[TestData.ExampleHash]["mount"].ServerLearned);
    }

    [Fact]
    public async Task A_change_waits_five_minutes_after_the_previous_one()
    {
        using var service = Service();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _source.Learn("mount", 6002);
        await service.SyncAsync(CollectionTrigger.Change, CancellationToken.None);
        _api.UnlockPushes.Clear();

        _source.Learn("mount", 6003);
        Assert.Equal(CollectionOutcome.SkippedThrottled, await service.SyncAsync(CollectionTrigger.Change, CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(CollectionOutcome.Sent, await service.SyncAsync(CollectionTrigger.Change, CancellationToken.None));
    }

    [Fact]
    public async Task Turning_it_off_forgets_only_the_local_records_and_sends_nothing()
    {
        using var service = Service();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        _records["other-character"] = new() { ["mount"] = new CollectionKindRecord { FullDay = "2026-11-27" } };
        var pushes = _api.UnlockPushes.Count;
        var reads = _api.MyUnlocksReads;

        service.ForgetLocalState();

        Assert.Empty(_records);
        Assert.Equal(pushes, _api.UnlockPushes.Count);
        Assert.Equal(reads, _api.MyUnlocksReads);
    }

    [Fact]
    public async Task A_429_waits_for_its_retry_after_and_a_refused_key_stops()
    {
        _api.UnlockResults.Clear();
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Fail(new ApiError { Kind = ApiErrorKind.RateLimited, StatusCode = 429, Message = "x", RetryAfter = TimeSpan.FromSeconds(60) }));
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, StatusCode = 403, Message = "x" }));
        using var service = Service();

        Assert.Equal(CollectionOutcome.SkippedBackoff, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));
        Assert.Equal(CollectionOutcome.SkippedBackoff, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));

        _clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(CollectionOutcome.Stopped, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));
        Assert.Equal(CollectionOutcome.Stopped, await service.SyncAsync(CollectionTrigger.Full, CancellationToken.None));
        Assert.Equal(2, _api.UnlockPushes.Count);
    }

    [Fact]
    public async Task A_404_waits_for_a_gear_sync_and_tries_once_more()
    {
        _api.UnlockResults.Clear();
        _api.UnlockResults.Enqueue(ApiResult<UnlocksResult>.Fail(new ApiError { Kind = ApiErrorKind.NotFound, StatusCode = 404, Message = "x" }));
        using var service = Service();

        Assert.Equal(CollectionOutcome.WaitingForGear, await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None));
        Assert.Equal(CollectionOutcome.WaitingForGear, await service.SyncAsync(CollectionTrigger.Full, CancellationToken.None));
        Assert.Single(_api.UnlockPushes);

        Assert.Equal(CollectionOutcome.WaitingForGear, await service.SyncAsync(CollectionTrigger.AfterGearSync, CancellationToken.None));
        Assert.Equal(2, _api.UnlockPushes.Count);

        Assert.Equal(CollectionOutcome.WaitingForGear, await service.SyncAsync(CollectionTrigger.AfterGearSync, CancellationToken.None));
        Assert.Equal(2, _api.UnlockPushes.Count);
    }

    [Fact]
    public async Task Another_key_forgets_the_local_records_so_every_kind_goes_in_full_again()
    {
        using var service = Service();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);
        _api.UnlockPushes.Clear();

        service.ForgetLocalState();
        await service.SyncAsync(CollectionTrigger.Check, CancellationToken.None);

        Assert.Equal(4, _api.UnlockPushes.Count);
    }

    private sealed class FakeCollectionSource : ICollectionSource
    {
        private readonly Dictionary<string, List<int>> _items = new()
        {
            ["mount"] = [6001, 6002, 6003],
            ["minion"] = [7001, 7002],
            ["orchestrion"] = [8001],
            ["card"] = [9001],
        };

        private readonly Dictionary<string, HashSet<int>> _learned = new()
        {
            ["mount"] = [6001],
            ["minion"] = [7001],
            ["orchestrion"] = [],
            ["card"] = [],
        };

        public bool IsAvailable => true;

        public IReadOnlyCollection<int> LearnedOf(string kind) => _learned[kind];

        public void Learn(string kind, int id) => _learned[kind].Add(id);

        public Task<CollectionSnapshot?> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct) =>
            Task.FromResult<CollectionSnapshot?>(new CollectionSnapshot(
                new CharacterDto { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash },
                kinds.ToDictionary(k => k, k => new CollectionKindSnapshot([.. _items[k]], [.. _learned[k]]))));
    }
}
