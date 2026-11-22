using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The currency sync of server 1.5, held to <c>contract-currency-sync.md</c>.</summary>
public sealed class CurrencySyncServiceTests
{
    private const string Key = "ea_test_key";

    private readonly FakeApiClient _api = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly TestClock _clock = new();
    private readonly FakeCurrencySource _source = new();
    private List<int> _ids = [28, 48, 49, 41784, 41785];

    public CurrencySyncServiceTests() => _tokens.SetApiKey(Key);

    private CurrencySyncService Service() => new(_source, _api, _tokens, _clock, () => _ids);

    private static ApiResult<CurrencyStockResult> Fail(ApiErrorKind kind, int status, TimeSpan? retryAfter = null) =>
        ApiResult<CurrencyStockResult>.Fail(new ApiError { Kind = kind, StatusCode = status, Message = "x", RetryAfter = retryAfter });

    [Fact]
    public async Task A_snapshot_carries_every_listed_id_in_order_zeros_included_and_nothing_else()
    {
        _source.Counts = new Dictionary<int, int> { [49] = 830, [41784] = 3120, [1] = 999_999, [28] = -5 };
        using var service = Service();

        var report = await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None);

        Assert.Equal(CurrencyOutcome.Sent, report.Outcome);
        var sent = Assert.Single(_api.CurrencyPushes);
        Assert.Equal([28, 48, 49, 41784, 41785], sent.Stock.Select(r => r.ItemId));
        Assert.Equal([0, 0, 830, 3120, 0], sent.Stock.Select(r => r.Count));
        Assert.Equal(TestData.ExampleHash, sent.Character.CidHash);
    }

    [Fact]
    public async Task The_body_speaks_the_contract()
    {
        _source.Counts = new Dictionary<int, int> { [49] = 830 };
        using var service = Service();
        await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(_api.CurrencyPushes[0], EorzeaJson.Options));
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("plugin_version", out _));
        Assert.Equal(TestData.ExampleHash, root.GetProperty("character").GetProperty("cid_hash").GetString());
        var row = root.GetProperty("stock")[2];
        Assert.Equal(49, row.GetProperty("item_id").GetInt32());
        Assert.Equal(830, row.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Counts_the_server_already_took_are_not_sent_again()
    {
        _source.Counts = new Dictionary<int, int> { [49] = 830 };
        using var service = Service();

        await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None);
        var again = await service.SyncAsync(CurrencyTrigger.AfterInventorySync, CancellationToken.None);

        Assert.Equal(CurrencyOutcome.SkippedUnchanged, again.Outcome);
        Assert.Single(_api.CurrencyPushes);
    }

    [Fact]
    public async Task A_change_is_held_to_one_send_every_five_minutes_and_a_sync_is_not()
    {
        _source.Counts = new Dictionary<int, int> { [49] = 830 };
        using var service = Service();
        await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None);

        _source.Counts = new Dictionary<int, int> { [49] = 840 };
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(CurrencyOutcome.SkippedThrottled, (await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None)).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None)).Outcome);

        _source.Counts = new Dictionary<int, int> { [49] = 850 };
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.AfterInventorySync, CancellationToken.None)).Outcome);
        Assert.Equal(3, _api.CurrencyPushes.Count);
    }

    /// <summary>
    /// A 404 means the server has not met the character. A gear sync creates it, so the snapshot goes once
    /// more after the next one, and a second 404 ends it for the session: never a loop.
    /// </summary>
    [Fact]
    public async Task A_character_the_server_does_not_know_is_sent_once_more_after_a_gear_sync_and_then_left()
    {
        _api.CurrencyResults.Enqueue(Fail(ApiErrorKind.NotFound, 404));
        using var service = Service();

        Assert.Equal(CurrencyOutcome.WaitingForGear, (await service.SyncAsync(CurrencyTrigger.AfterInventorySync, CancellationToken.None)).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(CurrencyOutcome.WaitingForGear, (await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None)).Outcome);
        Assert.Equal(CurrencyOutcome.WaitingForGear, (await service.SyncAsync(CurrencyTrigger.AfterInventorySync, CancellationToken.None)).Outcome);
        Assert.Single(_api.CurrencyPushes);

        Assert.Equal(CurrencyOutcome.WaitingForGear, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(2, _api.CurrencyPushes.Count);

        Assert.Equal(CurrencyOutcome.WaitingForGear, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(2, _api.CurrencyPushes.Count);
    }

    [Fact]
    public async Task A_gear_sync_after_a_404_lets_the_snapshot_through()
    {
        _api.CurrencyResults.Enqueue(Fail(ApiErrorKind.NotFound, 404));
        _api.CurrencyResults.Enqueue(ApiResult<CurrencyStockResult>.Ok(new CurrencyStockResult { Status = "ok", Rows = 0 }));
        using var service = Service();

        await service.SyncAsync(CurrencyTrigger.AfterInventorySync, CancellationToken.None);
        var retried = await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None);

        Assert.Equal(CurrencyOutcome.Sent, retried.Outcome);
    }

    [Fact]
    public async Task A_429_holds_every_send_for_an_hour_then_the_newest_counts_go()
    {
        _api.CurrencyResults.Enqueue(Fail(ApiErrorKind.RateLimited, 429, TimeSpan.FromSeconds(60)));
        _api.CurrencyResults.Enqueue(ApiResult<CurrencyStockResult>.Ok(new CurrencyStockResult { Status = "ok", Rows = 1 }));
        using var service = Service();

        Assert.Equal(CurrencyOutcome.SkippedBackoff, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(CurrencyOutcome.SkippedBackoff, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _source.Counts = new Dictionary<int, int> { [49] = 900 };
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None)).Outcome);
        Assert.Equal(900, _api.CurrencyPushes[^1].Stock.Single(r => r.ItemId == 49).Count);
    }

    [Theory]
    [InlineData(ApiErrorKind.Unauthorized, 401)]
    [InlineData(ApiErrorKind.Forbidden, 403)]
    public async Task A_refused_key_stops_the_sync_until_another_key_is_connected(ApiErrorKind kind, int status)
    {
        _api.CurrencyResults.Enqueue(Fail(kind, status));
        _api.CurrencyResults.Enqueue(ApiResult<CurrencyStockResult>.Ok(new CurrencyStockResult { Status = "ok" }));
        using var service = Service();

        Assert.Equal(CurrencyOutcome.Stopped, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _source.Counts = new Dictionary<int, int> { [49] = 1 };
        Assert.Equal(CurrencyOutcome.Stopped, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Single(_api.CurrencyPushes);

        _tokens.SetApiKey("ea_renewed_key");
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(ApiErrorKind.BadRequest, 400)]
    [InlineData(ApiErrorKind.Validation, 422)]
    public async Task A_refused_payload_is_not_sent_again_but_new_counts_are(ApiErrorKind kind, int status)
    {
        _api.CurrencyResults.Enqueue(Fail(kind, status));
        _api.CurrencyResults.Enqueue(ApiResult<CurrencyStockResult>.Ok(new CurrencyStockResult { Status = "ok" }));
        using var service = Service();

        Assert.Equal(CurrencyOutcome.Rejected, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(CurrencyOutcome.SkippedUnchanged, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _source.Counts = new Dictionary<int, int> { [48] = 10 };
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task The_hourly_limit_is_kept_here_before_the_server_has_to_say_it()
    {
        using var service = Service();
        for (var i = 1; i <= CurrencyProtocol.MaxCallsPerHour; i++)
        {
            _source.Counts = new Dictionary<int, int> { [49] = i };
            Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
            _clock.Advance(TimeSpan.FromSeconds(30));
        }

        _source.Counts = new Dictionary<int, int> { [49] = 999 };
        Assert.Equal(CurrencyOutcome.SkippedBackoff, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
        Assert.Equal(CurrencyProtocol.MaxCallsPerHour, _api.CurrencyPushes.Count);

        _clock.Advance(TimeSpan.FromMinutes(46));
        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_new_session_sends_once_even_when_nothing_changed()
    {
        using var service = Service();
        await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None);

        service.StartSession();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(CurrencyOutcome.Sent, (await service.SyncAsync(CurrencyTrigger.Change, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Without_a_list_a_key_or_a_character_nothing_is_sent()
    {
        using var service = Service();

        _ids = [];
        Assert.Equal(CurrencyOutcome.Nothing, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _ids = [49];
        _source.Available = false;
        Assert.Equal(CurrencyOutcome.NotLoggedIn, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        _tokens.Clear();
        Assert.Equal(CurrencyOutcome.NotConnected, (await service.SyncAsync(CurrencyTrigger.AfterGearSync, CancellationToken.None)).Outcome);

        Assert.Empty(_api.CurrencyPushes);
    }

    [Fact]
    public void The_list_is_kept_sorted_and_its_absence_marks_an_older_server()
    {
        var store = new TrackedItemsStore();
        Assert.False(store.ServerListsCurrencies);

        store.SetCurrencies([49, 28, 0, 49, 41784]);
        Assert.True(store.ServerListsCurrencies);
        Assert.Equal([28, 49, 41784], store.Currencies);

        store.SetCurrencies(null);
        Assert.False(store.ServerListsCurrencies);
        Assert.Empty(store.Currencies);

        store.SetCurrencies([]);
        Assert.True(store.ServerListsCurrencies);
    }

    private sealed class FakeCurrencySource : ICurrencySource
    {
        public bool Available { get; set; } = true;

        public Dictionary<int, int> Counts { get; set; } = [];

        public bool IsAvailable => Available;

        public Task<CurrencySnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct) =>
            Task.FromResult<CurrencySnapshot?>(new CurrencySnapshot(
                new CharacterDto { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash },
                new Dictionary<int, int>(Counts)));
    }
}
