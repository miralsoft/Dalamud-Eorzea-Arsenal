using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Drop reports of server 1.5, held to <c>contract-item-sources.md</c>.</summary>
public sealed class DropReportTests
{
    private static readonly DateOnly Day = new(2026, 11, 22);

    private static Dictionary<int, int> Bag(params (int Id, int Count)[] items) => items.ToDictionary(i => i.Id, i => i.Count);

    [Fact]
    public void A_chest_piece_is_reported_once_and_winning_it_is_not_reported_again()
    {
        var ledger = new DropLedger();
        ledger.Start(101, Bag((5057, 3)));

        var first = ledger.SeeChestItem(0x4000_0001, 0, 49424, 1, Day);
        Assert.Null(ledger.SeeChestItem(0x4000_0001, 0, 49424, 1, Day));

        // The player wins it: the bag gains the piece, which nets against the chest.
        var received = ledger.Finish(Bag((5057, 3), (49424, 1)), Day);

        Assert.Equal(new DropReport { ItemId = 49424, Qty = 1, ContentId = 101, Day = "2026-11-22", Seen = DropSeen.Chest }, first);
        Assert.Empty(received);
    }

    [Fact]
    public void The_same_item_in_two_chests_is_two_drops_and_netting_counts_pieces()
    {
        var ledger = new DropLedger();
        ledger.Start(101, Bag());

        Assert.NotNull(ledger.SeeChestItem(0x4000_0001, 0, 49424, 1, Day));
        Assert.NotNull(ledger.SeeChestItem(0x4000_0002, 0, 49424, 1, Day));

        // Won one of the two, and a third arrived without any chest.
        var received = ledger.Finish(Bag((49424, 3)), Day);

        var drop = Assert.Single(received);
        Assert.Equal(1, drop.Qty);
        Assert.Equal(DropSeen.Received, drop.Seen);
    }

    [Fact]
    public void Only_gains_count_as_received_and_an_hq_piece_is_its_base_item()
    {
        var ledger = new DropLedger();
        ledger.Start(101, Bag((4551, 10), (5057, 2)));

        var chest = ledger.SeeChestItem(1, 0, 1_049_424, 1, Day);

        // Potions used (fewer), crystals unchanged, a coffer and two tokens received.
        var received = ledger.Finish(Bag((4551, 7), (5057, 2), (49200, 1), (49400, 2)), Day);

        Assert.Equal(49424, chest!.ItemId);
        Assert.Equal([(49200, 1), (49400, 2)], received.Select(r => (r.ItemId, r.Qty)));
        Assert.Null(ledger.ContentId);
    }

    [Fact]
    public void Outside_a_duty_nothing_is_collected()
    {
        var ledger = new DropLedger();

        Assert.Null(ledger.SeeChestItem(1, 0, 49424, 1, Day));
        Assert.Empty(ledger.Finish(Bag((49424, 1)), Day));

        ledger.Start(101, Bag());
        Assert.Null(ledger.SeeChestItem(1, 0, 2_003_456, 1, Day));
        ledger.Abandon();

        ledger.Start(101, Bag());
        ledger.Abandon();
        Assert.Empty(ledger.Finish(Bag((49424, 1)), Day));
    }

    [Fact]
    public void The_body_speaks_the_contract()
    {
        var request = new DropReportRequest
        {
            InstallationId = "5f0c2a7e9b8d4c1fa36e0b2d7c9e4a18",
            GameVersion = "2026.11.13.0000.0000",
            Reports = [new DropReport { ItemId = 49424, Qty = 1, ContentId = 101, Day = "2026-11-02", Seen = DropSeen.Chest }],
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, EorzeaJson.Options));
        var root = json.RootElement;
        Assert.Equal("5f0c2a7e9b8d4c1fa36e0b2d7c9e4a18", root.GetProperty("installation_id").GetString());
        Assert.Equal("2026.11.13.0000.0000", root.GetProperty("game_version").GetString());
        var report = root.GetProperty("reports")[0];
        Assert.Equal(49424, report.GetProperty("item_id").GetInt32());
        Assert.Equal(1, report.GetProperty("qty").GetInt32());
        Assert.Equal(101, report.GetProperty("content_id").GetInt32());
        Assert.Equal("2026-11-02", report.GetProperty("day").GetString());
        Assert.Equal("chest", report.GetProperty("seen").GetString());
        Assert.False(report.TryGetProperty("boss_id", out _));
        Assert.False(report.TryGetProperty("coffer_id", out _));
    }

    // --- The uploader --------------------------------------------------------------------------------

    private readonly FakeApiClient _api = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly TestClock _clock = new() { UtcNow = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero) };
    private readonly List<IReadOnlyList<DropReport>> _persisted = [];

    public DropReportTests() => _tokens.SetApiKey("ea_test_key");

    private ReportUploader<DropReport> Uploader(IEnumerable<DropReport>? restored = null) => new(
        "drop",
        (key, batch, ct) => _api.PostDropReportsAsync(key, new DropReportRequest { InstallationId = "abcdefabcdefabcdef", GameVersion = "7.5", Reports = batch }, ct),
        _tokens,
        _clock,
        50,
        r => r.Day,
        ReportKeys.Drop,
        ReportKeys.CombineDrops,
        list => _persisted.Add(list),
        restored);

    private static DropReport Drop(int item, int qty = 1, string day = "2026-11-22", string seen = DropSeen.Received) =>
        new() { ItemId = item, Qty = qty, ContentId = 101, Day = day, Seen = seen };

    private static ApiResult<ReportAck> Fail(ApiErrorKind kind, int status) =>
        ApiResult<ReportAck>.Fail(new ApiError { Kind = kind, StatusCode = status, Message = "x" });

    [Fact]
    public async Task Collected_drops_go_every_few_minutes_or_at_once_when_a_duty_ends()
    {
        var uploader = Uploader();
        uploader.Add([Drop(1)]);
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: false, CancellationToken.None));

        uploader.Add([Drop(2)]);
        Assert.Equal(ReportOutcome.NotDue, await uploader.SendIfDueAsync(force: false, CancellationToken.None));
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: true, CancellationToken.None));

        uploader.Add([Drop(3)]);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: false, CancellationToken.None));
        Assert.Equal(3, _api.DropReportCalls.Count);
        Assert.Equal(0, uploader.PendingCount);
        Assert.Empty(_persisted[^1]);
    }

    [Fact]
    public async Task The_same_drop_goes_as_one_with_its_quantities_added_and_a_call_carries_at_most_50()
    {
        var uploader = Uploader();
        uploader.Add([Drop(1), Drop(1, 2), Drop(1, seen: DropSeen.Chest)]);
        uploader.Add(Enumerable.Range(100, 60).Select(i => Drop(i)));

        await uploader.SendIfDueAsync(force: true, CancellationToken.None);

        var first = _api.DropReportCalls[0].Reports;
        Assert.Equal(50, first.Count);
        Assert.Equal(3, first.Single(r => r.ItemId == 1 && r.Seen == DropSeen.Received).Qty);
        Assert.Equal(1, first.Single(r => r.ItemId == 1 && r.Seen == DropSeen.Chest).Qty);
        Assert.Equal(12, uploader.PendingCount);
    }

    [Fact]
    public async Task Refused_entries_of_a_202_are_not_sent_again()
    {
        _api.DropReportResults.Enqueue(ApiResult<ReportAck>.Ok(new ReportAck { Accepted = 1, Rejected = [new RejectedReport { Index = 1, Reason = "unknown_item" }] }));
        var uploader = Uploader();
        uploader.Add([Drop(1), Drop(2)]);

        await uploader.SendIfDueAsync(force: true, CancellationToken.None);

        Assert.Equal(0, uploader.PendingCount);
    }

    [Fact]
    public async Task A_closed_intake_drops_everything_and_waits_a_day()
    {
        _api.DropReportResults.Enqueue(Fail(ApiErrorKind.Conflict, 409));
        _api.DropReportResults.Enqueue(ApiResult<ReportAck>.Ok(new ReportAck { Accepted = 1 }));
        var uploader = Uploader();
        uploader.Add([Drop(1), Drop(2)]);

        Assert.Equal(ReportOutcome.IntakeClosed, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Equal(0, uploader.PendingCount);
        Assert.True(uploader.IsHolding);

        uploader.Add([Drop(3)]);
        _clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(ReportOutcome.Holding, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
    }

    [Fact]
    public async Task A_429_holds_an_hour_and_keeps_what_was_collected()
    {
        _api.DropReportResults.Enqueue(Fail(ApiErrorKind.RateLimited, 429));
        _api.DropReportResults.Enqueue(ApiResult<ReportAck>.Ok(new ReportAck { Accepted = 1 }));
        var uploader = Uploader();
        uploader.Add([Drop(1)]);

        Assert.Equal(ReportOutcome.Holding, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Equal(1, uploader.PendingCount);
        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(ReportOutcome.Holding, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_key_stops_until_another_is_connected()
    {
        _api.DropReportResults.Enqueue(Fail(ApiErrorKind.Forbidden, 403));
        _api.DropReportResults.Enqueue(ApiResult<ReportAck>.Ok(new ReportAck { Accepted = 1 }));
        var uploader = Uploader();
        uploader.Add([Drop(1)]);

        Assert.Equal(ReportOutcome.Stopped, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Equal(ReportOutcome.Stopped, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Single(_api.DropReportCalls);

        _tokens.SetApiKey("ea_other_key");
        Assert.Equal(ReportOutcome.Sent, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
    }

    [Theory]
    [InlineData(ApiErrorKind.BadRequest, 400)]
    [InlineData(ApiErrorKind.Validation, 422)]
    [InlineData(ApiErrorKind.Unexpected, 413)]
    public async Task A_refused_batch_is_dropped(ApiErrorKind kind, int status)
    {
        _api.DropReportResults.Enqueue(Fail(kind, status));
        var uploader = Uploader();
        uploader.Add([Drop(1)]);

        Assert.Equal(ReportOutcome.Refused, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Equal(0, uploader.PendingCount);
    }

    [Fact]
    public async Task A_network_failure_keeps_the_batch_and_old_reports_are_dropped_before_sending()
    {
        _api.DropReportResults.Enqueue(Fail(ApiErrorKind.Network, 0));
        _api.DropReportResults.Enqueue(ApiResult<ReportAck>.Ok(new ReportAck { Accepted = 1 }));
        var uploader = Uploader(restored: [Drop(1, day: "2026-11-14"), Drop(2, day: "2026-11-15")]);

        Assert.Equal(ReportOutcome.Failed, await uploader.SendIfDueAsync(force: true, CancellationToken.None));
        Assert.Equal(1, uploader.PendingCount);
        Assert.Equal(2, Assert.Single(_api.DropReportCalls[0].Reports).ItemId);
    }

    [Fact]
    public void Turning_the_setting_off_forgets_what_was_collected()
    {
        var uploader = Uploader();
        uploader.Add([Drop(1)]);
        uploader.Clear();

        Assert.Equal(0, uploader.PendingCount);
        Assert.Empty(_persisted[^1]);
    }
}
