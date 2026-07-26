using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Verifies the Teams poller: it caches the calendar, toasts genuinely-new team notifications exactly
/// once (deduped by the persisted watermark, never toasting the first-fetch backlog), self-throttles,
/// classifies a missing scope, and keeps the last-good cache on error.
/// </summary>
public sealed class TeamsServiceTests
{
    private static (TeamsService svc, FakeApiClient api, InMemoryTeamsSeenStore seen, TestClock clock)
        Make(bool connected = true, long lastNotificationId = 0)
    {
        var api = new FakeApiClient();
        var tokens = new InMemoryTokenStore();
        if (connected)
        {
            tokens.SetApiKey("ea_key");
        }

        var seen = new InMemoryTeamsSeenStore { LastNotificationId = lastNotificationId };
        var clock = new TestClock();
        var svc = new TeamsService(api, tokens, seen, clock);
        return (svc, api, seen, clock);
    }

    private static async Task<TeamsPollOutcome> Poll(TeamsService svc, bool force = false)
    {
        var tcs = new TaskCompletionSource<TeamsPollOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(TeamsPollOutcome o)
        {
            svc.PollCompleted -= Handler;
            tcs.TrySetResult(o);
        }

        svc.PollCompleted += Handler;
        svc.RequestPoll(force);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static CalendarOccurrence Occurrence(long eventId = 7, string date = "2026-07-21") =>
        new() { TeamId = 12, TeamName = "Tuesday Static", EventId = eventId, Title = "Prog", Date = date, Time = "20:00", Timezone = "Europe/Berlin", Total = 8 };

    private static NotificationEntry Notif(long id, string type, string title = "t", string body = "b", string link = "/teams/12/loot") =>
        new() { Id = id, Type = type, Title = title, Body = body, Link = link, TeamId = 12 };

    private static ApiResult<NotificationsResponse> Notifs(int unread, params NotificationEntry[] entries) =>
        ApiResult<NotificationsResponse>.Ok(new NotificationsResponse { Data = entries.ToList(), Unread = unread });

    [Fact]
    public async Task No_key_reports_not_connected()
    {
        var (svc, api, _, _) = Make(connected: false);

        var outcome = await Poll(svc);

        Assert.Equal(TeamsPollOutcome.NotConnected, outcome);
        Assert.Equal(0, api.CalendarCalls);
        Assert.Equal(0, api.NotificationCalls);
    }

    [Fact]
    public async Task Successful_poll_caches_calendar_and_raises_updated()
    {
        var (svc, api, _, _) = Make();
        api.EnqueueCalendar(ApiResult<CalendarResponse>.Ok(new CalendarResponse { Data = [Occurrence()] }));
        var raised = false;
        svc.CalendarUpdated += () => raised = true;

        var outcome = await Poll(svc);

        Assert.Equal(TeamsPollOutcome.Ok, outcome);
        Assert.Single(svc.Calendar);
        Assert.True(raised);
    }

    [Fact]
    public async Task First_fetch_seeds_watermark_without_toasting()
    {
        var (svc, api, seen, _) = Make();
        api.EnqueueNotifications(Notifs(2, Notif(100, TeamsProtocol.NotifLoot)));
        var toasts = new List<TeamToast>();
        svc.Toast += toasts.Add;

        await Poll(svc);

        Assert.Empty(toasts);                     // never toast the backlog on first fetch
        Assert.Equal(100, seen.LastNotificationId); // watermark seeded to the newest id
        Assert.Equal(2, svc.UnreadNotifications);
    }

    [Fact]
    public async Task New_toastable_notification_after_seed_raises_one_toast()
    {
        var (svc, api, _, clock) = Make();
        api.EnqueueNotifications(Notifs(0, Notif(100, TeamsProtocol.NotifLoot))); // seed
        api.EnqueueNotifications(Notifs(1, Notif(101, TeamsProtocol.NotifLoot, title: "Loot"), Notif(100, TeamsProtocol.NotifLoot)));
        var toasts = new List<TeamToast>();
        svc.Toast += toasts.Add;

        await Poll(svc);                 // seeds at 100
        clock.Advance(TimeSpan.FromMinutes(6));
        await Poll(svc);                 // 101 is new → one toast

        Assert.Single(toasts);
        Assert.Equal("Loot", toasts[0].Title);
        Assert.Equal(TeamsProtocol.NotifLoot, toasts[0].Type);
    }

    [Fact]
    public async Task Non_toastable_type_is_not_toasted()
    {
        var (svc, api, _, clock) = Make(lastNotificationId: 50);
        api.EnqueueNotifications(Notifs(0, Notif(60, "team.transfer")));
        var toasts = new List<TeamToast>();
        svc.Toast += toasts.Add;

        await Poll(svc);

        Assert.Empty(toasts);
    }

    [Fact]
    public async Task Watermark_from_store_prevents_retoasting_across_instances()
    {
        // A fresh service (relog) with a persisted watermark of 100 must toast only ids > 100.
        var (svc, api, _, _) = Make(lastNotificationId: 100);
        api.EnqueueNotifications(Notifs(1, Notif(101, TeamsProtocol.NotifReminder, title: "Reminder"), Notif(100, TeamsProtocol.NotifLoot)));
        var toasts = new List<TeamToast>();
        svc.Toast += toasts.Add;

        await Poll(svc);

        Assert.Single(toasts);
        Assert.Equal("Reminder", toasts[0].Title);
    }

    [Fact]
    public async Task Missing_scope_reports_scope_missing()
    {
        var (svc, api, _, _) = Make();
        api.EnqueueCalendar(ApiResult<CalendarResponse>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, Message = "Missing required scope: teams:read" }));

        var outcome = await Poll(svc);

        Assert.Equal(TeamsPollOutcome.ScopeMissing, outcome);
    }

    [Fact]
    public async Task Second_poll_within_interval_is_skipped()
    {
        var (svc, api, _, _) = Make();
        api.EnqueueCalendar(ApiResult<CalendarResponse>.Ok(new CalendarResponse { Data = [Occurrence()] }));

        await Poll(svc);              // Ok
        var outcome = await Poll(svc); // within 5 min, not forced

        Assert.Equal(TeamsPollOutcome.Skipped, outcome);
        Assert.Equal(1, api.CalendarCalls); // no second network read
    }

    [Fact]
    public async Task Forced_poll_bypasses_the_interval()
    {
        var (svc, api, _, _) = Make();

        await Poll(svc);
        var outcome = await Poll(svc, force: true);

        Assert.Equal(TeamsPollOutcome.Ok, outcome);
        Assert.Equal(2, api.CalendarCalls);
    }

    [Fact]
    public async Task Kept_cache_on_network_error()
    {
        var (svc, api, _, clock) = Make();
        api.EnqueueCalendar(ApiResult<CalendarResponse>.Ok(new CalendarResponse { Data = [Occurrence()] }));
        await Poll(svc);

        clock.Advance(TimeSpan.FromMinutes(6));
        api.EnqueueCalendar(ApiResult<CalendarResponse>.Fail(new ApiError { Kind = ApiErrorKind.Network, Message = "offline" }));
        api.EnqueueNotifications(ApiResult<NotificationsResponse>.Fail(new ApiError { Kind = ApiErrorKind.Network, Message = "offline" }));
        var outcome = await Poll(svc);

        Assert.Equal(TeamsPollOutcome.Failed, outcome);
        Assert.Single(svc.Calendar); // last-good cache retained
    }
}
