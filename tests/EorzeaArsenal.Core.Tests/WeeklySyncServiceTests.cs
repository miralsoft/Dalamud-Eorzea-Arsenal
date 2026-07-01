using System.Text.Json;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Verifies the weekly orchestrator: it addresses the numeric server id, waits for that id, reads the
/// server state and sends only the confident fields that changed (never clobbering manual entries),
/// and classifies errors.
/// </summary>
public sealed class WeeklySyncServiceTests
{
    private const string CharId = "42";

    private static WeeklyData Snapshot(int? tomes = 450, bool? custom = null) => new()
    {
        Character = new CharacterDto { Name = "Sanaka Sundream", World = "Twintania", CidHash = TestData.ExampleHash },
        Values = new WeeklyValues { TomesHave = tomes, Custom = custom },
    };

    private static (WeeklySyncService svc, FakeWeeklySource src, FakeApiClient api, CharacterDirectory dir)
        Make(bool connected = true, bool available = true, bool resolved = true, WeeklyData? snapshot = null)
    {
        var src = new FakeWeeklySource { IsAvailable = available, Snapshot = snapshot ?? Snapshot() };
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        if (connected)
        {
            tokens.SetApiKey("ea_key");
        }

        var dir = new CharacterDirectory();
        if (resolved)
        {
            dir.Record(TestData.ExampleHash, CharId);
        }

        var svc = new WeeklySyncService(src, api, tokens, dir, new TestClock());
        return (svc, src, api, dir);
    }

    private static async Task<WeeklyReport> Wait(WeeklySyncService svc, Action trigger)
    {
        var tcs = new TaskCompletionSource<WeeklyReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(WeeklyReport r)
        {
            svc.SyncCompleted -= Handler;
            tcs.TrySetResult(r);
        }

        svc.SyncCompleted += Handler;
        trigger();
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ApiResult<WeeklyResponse> ServerState(params (string Key, object Value)[] fields)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            data[key] = value;
        }

        return ApiResult<WeeklyResponse>.Ok(new WeeklyResponse { Data = JsonSerializer.SerializeToElement(data) });
    }

    [Fact]
    public async Task No_key_reports_not_connected()
    {
        var (svc, _, api, _) = Make(connected: false);

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Manual));

        Assert.Equal(WeeklyOutcome.NotConnected, report.Outcome);
        Assert.Equal(0, api.WeeklyPutCalls);
    }

    [Fact]
    public async Task Unknown_character_id_reports_not_resolved()
    {
        var (svc, _, api, _) = Make(resolved: false);

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Login));

        Assert.Equal(WeeklyOutcome.NotResolved, report.Outcome);
        Assert.Equal(0, api.WeeklyGetCalls);
        Assert.Equal(0, api.WeeklyPutCalls);
    }

    [Fact]
    public async Task Empty_values_report_nothing()
    {
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: null, custom: null));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Manual));

        Assert.Equal(WeeklyOutcome.Nothing, report.Outcome);
        Assert.Equal(0, api.WeeklyGetCalls);
    }

    [Fact]
    public async Task Sends_known_fields_to_numeric_id()
    {
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: 450, custom: true));
        api.EnqueueWeeklyGet(ServerState()); // empty server state

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Login));

        Assert.Equal(WeeklyOutcome.Sent, report.Outcome);
        Assert.Equal(2, report.FieldCount);
        Assert.Equal(1, api.WeeklyPutCalls);
        Assert.Contains(CharId, api.WeeklyCharacterIds);
        var items = api.WeeklyPayloads[0].Items;
        Assert.Equal(450, Convert.ToInt32(items["tomesHave"]));
        Assert.True((bool)items["custom"]);
    }

    [Fact]
    public async Task Skips_when_unchanged_on_auto()
    {
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: 300, custom: null));
        api.EnqueueWeeklyGet(ServerState(("tomesHave", 300)));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Auto));

        Assert.Equal(WeeklyOutcome.SkippedUnchanged, report.Outcome);
        Assert.Equal(0, api.WeeklyPutCalls);
    }

    [Fact]
    public async Task Sends_only_changed_fields_on_auto()
    {
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: 450, custom: true));
        api.EnqueueWeeklyGet(ServerState(("tomesHave", 300), ("custom", true)));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Auto));

        Assert.Equal(WeeklyOutcome.Sent, report.Outcome);
        Assert.Equal(1, report.FieldCount);
        var items = api.WeeklyPayloads[0].Items;
        Assert.True(items.ContainsKey("tomesHave"));
        Assert.False(items.ContainsKey("custom")); // unchanged → never re-sent
    }

    [Fact]
    public async Task Empty_object_serialized_as_array_still_sends()
    {
        // Regression: a PHP backend returns "data": [] for an empty object; it must not fail parsing
        // (which would report Unexpected and skip the write) — treat it as "no server data".
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: 200, custom: null));
        api.EnqueueWeeklyGet(ApiResult<WeeklyResponse>.Ok(new WeeklyResponse { Data = JsonSerializer.SerializeToElement(Array.Empty<int>()) }));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Auto));

        Assert.Equal(WeeklyOutcome.Sent, report.Outcome);
        Assert.True(api.WeeklyPayloads[0].Items.ContainsKey("tomesHave"));
    }

    [Fact]
    public async Task Missing_server_row_sends_all_known_fields()
    {
        var (svc, _, api, _) = Make(snapshot: Snapshot(tomes: 100, custom: null));
        api.EnqueueWeeklyGet(ApiResult<WeeklyResponse>.Fail(new ApiError { Kind = ApiErrorKind.NotFound, Message = "no row" }));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Auto));

        Assert.Equal(WeeklyOutcome.Sent, report.Outcome);
        Assert.True(api.WeeklyPayloads[0].Items.ContainsKey("tomesHave"));
    }

    [Fact]
    public async Task Forbidden_reports_failed_without_writing()
    {
        var (svc, _, api, _) = Make();
        api.EnqueueWeeklyGet(ApiResult<WeeklyResponse>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, Message = "missing scope" }));

        var report = await Wait(svc, () => svc.RequestSync(WeeklyTrigger.Manual));

        Assert.Equal(WeeklyOutcome.Failed, report.Outcome);
        Assert.Equal(ApiErrorKind.Forbidden, report.ErrorKind);
        Assert.Equal(0, api.WeeklyPutCalls);
    }
}
