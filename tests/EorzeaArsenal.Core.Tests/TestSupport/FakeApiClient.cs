using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Tests.TestSupport;

/// <summary>A programmable <see cref="IApiClient"/> for service-level tests.</summary>
public sealed class FakeApiClient : IApiClient
{
    private readonly Queue<ApiResult<DeviceTokenResponse>> _tokenResults = new();
    private readonly Queue<ApiResult<GearPushResult>> _pushResults = new();
    private readonly Queue<ApiResult<InventoryPushResult>> _inventoryResults = new();

    /// <summary>Result returned by <see cref="RequestDeviceCodeAsync"/>.</summary>
    public ApiResult<DeviceCodeResponse> DeviceCodeResult { get; set; } =
        ApiResult<DeviceCodeResponse>.Ok(new DeviceCodeResponse
        {
            DeviceCode = "dev-code",
            UserCode = "ABCD-1234",
            VerificationUri = "http://localhost/approve",
            Interval = 1,
            ExpiresIn = 10,
        });

    /// <summary>Result returned by <see cref="GetVersionAsync"/>.</summary>
    public ApiResult<VersionResponse> VersionResult { get; set; } =
        ApiResult<VersionResponse>.Ok(new VersionResponse { ProtocolVersion = 1 });

    /// <summary>Number of push calls made.</summary>
    public int PushCalls { get; private set; }

    /// <summary>The payloads passed to <see cref="PushGearAsync"/>, in order.</summary>
    public List<GearPayload> PushedPayloads { get; } = [];

    /// <summary>Number of inventory upload calls made.</summary>
    public int InventoryCalls { get; private set; }

    /// <summary>The payloads passed to <see cref="PushInventoryAsync"/>, in order.</summary>
    public List<InventoryPayload> InventoryPayloads { get; } = [];

    /// <summary>Queues an inventory upload result.</summary>
    /// <param name="result">The result to return on the next upload.</param>
    public void EnqueueInventory(ApiResult<InventoryPushResult> result) => _inventoryResults.Enqueue(result);

    /// <summary>Queues a device-token poll result.</summary>
    /// <param name="result">The result to return on the next poll.</param>
    public void EnqueueToken(ApiResult<DeviceTokenResponse> result) => _tokenResults.Enqueue(result);

    /// <summary>Queues a push result.</summary>
    /// <param name="result">The result to return on the next push.</param>
    public void EnqueuePush(ApiResult<GearPushResult> result) => _pushResults.Enqueue(result);

    /// <inheritdoc />
    public Task<ApiResult<DeviceCodeResponse>> RequestDeviceCodeAsync(CancellationToken ct) =>
        Task.FromResult(DeviceCodeResult);

    /// <inheritdoc />
    public Task<ApiResult<DeviceTokenResponse>> PollDeviceTokenAsync(string deviceCode, CancellationToken ct)
    {
        var result = _tokenResults.Count > 0
            ? _tokenResults.Dequeue()
            : ApiResult<DeviceTokenResponse>.Ok(new DeviceTokenResponse { Status = "pending" });
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult<GearPushResult>> PushGearAsync(string apiKey, GearPayload payload, CancellationToken ct)
    {
        PushCalls++;
        PushedPayloads.Add(payload);
        var result = _pushResults.Count > 0
            ? _pushResults.Dequeue()
            : ApiResult<GearPushResult>.Ok(new GearPushResult { Status = "ok", Gearsets = payload.Gearsets.Count });
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult<InventoryPushResult>> PushInventoryAsync(string apiKey, InventoryPayload payload, CancellationToken ct)
    {
        InventoryCalls++;
        InventoryPayloads.Add(payload);
        var result = _inventoryResults.Count > 0
            ? _inventoryResults.Dequeue()
            : ApiResult<InventoryPushResult>.Ok(new InventoryPushResult { Status = "ok", Items = payload.Items.Count });
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult<VersionResponse>> GetVersionAsync(string? apiKey, CancellationToken ct) =>
        Task.FromResult(VersionResult);

    /// <summary>Result returned by <see cref="GetBisAsync"/>.</summary>
    public ApiResult<BisResponse> BisResult { get; set; } = ApiResult<BisResponse>.Ok(new BisResponse());

    /// <inheritdoc />
    public Task<ApiResult<BisResponse>> GetBisAsync(string apiKey, string? cidHash, CancellationToken ct) =>
        Task.FromResult(BisResult);

    private readonly Queue<ApiResult<WeeklyResponse>> _weeklyGetResults = new();
    private readonly Queue<ApiResult<WeeklyPushResult>> _weeklyPutResults = new();

    /// <summary>Number of <see cref="GetWeeklyAsync"/> calls made.</summary>
    public int WeeklyGetCalls { get; private set; }

    /// <summary>Number of <see cref="PutWeeklyAsync"/> calls made.</summary>
    public int WeeklyPutCalls { get; private set; }

    /// <summary>The character ids passed to weekly calls, in order.</summary>
    public List<string> WeeklyCharacterIds { get; } = [];

    /// <summary>The payloads passed to <see cref="PutWeeklyAsync"/>, in order.</summary>
    public List<WeeklyPayload> WeeklyPayloads { get; } = [];

    /// <summary>Queues a weekly-GET result.</summary>
    /// <param name="result">The result to return on the next GET.</param>
    public void EnqueueWeeklyGet(ApiResult<WeeklyResponse> result) => _weeklyGetResults.Enqueue(result);

    /// <summary>Queues a weekly-PUT result.</summary>
    /// <param name="result">The result to return on the next PUT.</param>
    public void EnqueueWeeklyPut(ApiResult<WeeklyPushResult> result) => _weeklyPutResults.Enqueue(result);

    /// <inheritdoc />
    public Task<ApiResult<WeeklyResponse>> GetWeeklyAsync(string apiKey, string characterId, CancellationToken ct)
    {
        WeeklyGetCalls++;
        WeeklyCharacterIds.Add(characterId);
        var result = _weeklyGetResults.Count > 0
            ? _weeklyGetResults.Dequeue()
            : ApiResult<WeeklyResponse>.Ok(new WeeklyResponse());
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult<WeeklyPushResult>> PutWeeklyAsync(string apiKey, string characterId, WeeklyPayload payload, CancellationToken ct)
    {
        WeeklyPutCalls++;
        WeeklyCharacterIds.Add(characterId);
        WeeklyPayloads.Add(payload);
        var result = _weeklyPutResults.Count > 0
            ? _weeklyPutResults.Dequeue()
            : ApiResult<WeeklyPushResult>.Ok(new WeeklyPushResult { Status = "ok" });
        return Task.FromResult(result);
    }

    // --- Teams companion ---------------------------------------------------------------------------

    private readonly Queue<ApiResult<CalendarResponse>> _calendarResults = new();
    private readonly Queue<ApiResult<NotificationsResponse>> _notificationResults = new();

    /// <summary>Number of <see cref="GetCalendarAsync"/> calls made.</summary>
    public int CalendarCalls { get; private set; }

    /// <summary>Number of <see cref="GetNotificationsAsync"/> calls made.</summary>
    public int NotificationCalls { get; private set; }

    /// <summary>The attendance requests posted, in order.</summary>
    public List<AttendanceRequest> AttendanceRequests { get; } = [];

    /// <summary>Result returned by <see cref="GetTeamsAsync"/>.</summary>
    public ApiResult<TeamsResponse> TeamsResult { get; set; } = ApiResult<TeamsResponse>.Ok(new TeamsResponse());

    /// <summary>Result returned by <see cref="GetMitSheetAsync"/>.</summary>
    public ApiResult<MitSheetResponse> MitSheetResult { get; set; } = ApiResult<MitSheetResponse>.Ok(new MitSheetResponse());

    /// <summary>Result returned by <see cref="GetContentSheetAsync"/>.</summary>
    public ApiResult<ContentSheetResponse> ContentSheetResult { get; set; } = ApiResult<ContentSheetResponse>.Ok(new ContentSheetResponse());

    /// <summary>Result returned by <see cref="GetResourceFileAsync"/>.</summary>
    public ApiResult<ResourceFile> ResourceFileResult { get; set; } = ApiResult<ResourceFile>.Ok(new ResourceFile { Bytes = [], Mime = null });

    /// <summary>Result returned by <see cref="GetFarmAsync"/>.</summary>
    public ApiResult<FarmResponse> FarmResult { get; set; } = ApiResult<FarmResponse>.Ok(new FarmResponse());

    /// <summary>Result returned by <see cref="GetLogsAsync"/>.</summary>
    public ApiResult<LogsResponse> LogsResult { get; set; } = ApiResult<LogsResponse>.Ok(new LogsResponse());

    /// <summary>Result returned by <see cref="GetGearObtainAsync"/>.</summary>
    public ApiResult<ObtainResponse> ObtainResult { get; set; } = ApiResult<ObtainResponse>.Ok(new ObtainResponse { Data = new() });

    /// <summary>Each id set passed to <see cref="GetGearObtainAsync"/>, in call order.</summary>
    public List<long[]> ObtainRequests { get; } = [];

    /// <summary>Result returned by <see cref="PostAttendanceAsync"/>.</summary>
    public ApiResult<StatusAck> AttendanceResult { get; set; } = ApiResult<StatusAck>.Ok(new StatusAck { Status = "ok" });

    /// <summary>Result returned by <see cref="GetAbsencesAsync"/>.</summary>
    public ApiResult<AbsencesResponse> AbsencesResult { get; set; } = ApiResult<AbsencesResponse>.Ok(new AbsencesResponse());

    /// <summary>Result returned by <see cref="PostAbsenceAsync"/>.</summary>
    public ApiResult<AbsenceCreateResponse> AbsenceCreateResult { get; set; } = ApiResult<AbsenceCreateResponse>.Ok(new AbsenceCreateResponse { Data = new AbsenceCreated { Id = 1 } });

    /// <summary>Result returned by <see cref="DeleteAbsenceAsync"/>.</summary>
    public ApiResult<bool> DeleteAbsenceResult { get; set; } = ApiResult<bool>.Ok(true);

    /// <summary>Queues a calendar result (for the polling service).</summary>
    /// <param name="result">The result to return on the next call.</param>
    public void EnqueueCalendar(ApiResult<CalendarResponse> result) => _calendarResults.Enqueue(result);

    /// <summary>Queues a notifications result (for the polling service).</summary>
    /// <param name="result">The result to return on the next call.</param>
    public void EnqueueNotifications(ApiResult<NotificationsResponse> result) => _notificationResults.Enqueue(result);

    /// <inheritdoc />
    public Task<ApiResult<TeamsResponse>> GetTeamsAsync(string apiKey, CancellationToken ct) =>
        Task.FromResult(TeamsResult);

    /// <inheritdoc />
    public Task<ApiResult<CalendarResponse>> GetCalendarAsync(string apiKey, string? from, string? to, CancellationToken ct)
    {
        CalendarCalls++;
        var result = _calendarResults.Count > 0 ? _calendarResults.Dequeue() : ApiResult<CalendarResponse>.Ok(new CalendarResponse { Data = [] });
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ApiResult<MitSheetResponse>> GetMitSheetAsync(string apiKey, long teamId, long planId, CancellationToken ct) =>
        Task.FromResult(MitSheetResult);

    /// <inheritdoc />
    public Task<ApiResult<ContentSheetResponse>> GetContentSheetAsync(string apiKey, long teamId, CancellationToken ct) =>
        Task.FromResult(ContentSheetResult);

    /// <inheritdoc />
    public Task<ApiResult<ResourceFile>> GetResourceFileAsync(string apiKey, long teamId, long resourceId, CancellationToken ct) =>
        Task.FromResult(ResourceFileResult);

    /// <inheritdoc />
    public Task<ApiResult<FarmResponse>> GetFarmAsync(string apiKey, long teamId, CancellationToken ct) =>
        Task.FromResult(FarmResult);

    /// <inheritdoc />
    public Task<ApiResult<LogsResponse>> GetLogsAsync(string apiKey, long teamId, CancellationToken ct) =>
        Task.FromResult(LogsResult);

    /// <inheritdoc />
    public Task<ApiResult<ObtainResponse>> GetGearObtainAsync(string apiKey, IReadOnlyCollection<long> itemIds, CancellationToken ct)
    {
        ObtainRequests.Add([.. itemIds]);
        return Task.FromResult(ObtainResult);
    }

    /// <inheritdoc />
    public Task<ApiResult<StatusAck>> PostAttendanceAsync(string apiKey, long teamId, long eventId, AttendanceRequest request, CancellationToken ct)
    {
        AttendanceRequests.Add(request);
        return Task.FromResult(AttendanceResult);
    }

    /// <inheritdoc />
    public Task<ApiResult<AbsencesResponse>> GetAbsencesAsync(string apiKey, long teamId, CancellationToken ct) =>
        Task.FromResult(AbsencesResult);

    /// <inheritdoc />
    public Task<ApiResult<AbsenceCreateResponse>> PostAbsenceAsync(string apiKey, long teamId, AbsenceCreateRequest request, CancellationToken ct) =>
        Task.FromResult(AbsenceCreateResult);

    /// <inheritdoc />
    public Task<ApiResult<bool>> DeleteAbsenceAsync(string apiKey, long teamId, long absenceId, CancellationToken ct) =>
        Task.FromResult(DeleteAbsenceResult);

    /// <inheritdoc />
    public Task<ApiResult<NotificationsResponse>> GetNotificationsAsync(string apiKey, CancellationToken ct)
    {
        NotificationCalls++;
        var result = _notificationResults.Count > 0 ? _notificationResults.Dequeue() : ApiResult<NotificationsResponse>.Ok(new NotificationsResponse { Data = [] });
        return Task.FromResult(result);
    }
}

/// <summary>Builders for common test data.</summary>
public static class TestData
{
    /// <summary>A valid single-gearset snapshot for the given character hash.</summary>
    /// <param name="cidHash">The cid_hash to use (must be 64 lowercase hex).</param>
    /// <param name="itemId">The weapon item id.</param>
    /// <returns>A valid <see cref="GearData"/>.</returns>
    public static GearData Snapshot(string cidHash, int itemId = 49671) => new()
    {
        Character = new CharacterDto { Name = "Sanaka Sundream", World = "Twintania", CidHash = cidHash },
        Gearsets =
        [
            new GearsetDto
            {
                GearIndex = 0,
                Name = "DRK 2.50",
                Job = "DRK",
                Items = new Dictionary<string, ItemDto>
                {
                    ["Weapon"] = new() { Id = itemId, Materia = [41773, 41773] },
                    ["Head"] = new() { Id = 49690 },
                },
                Food = 44096,
                Ilvl = 760,
            },
        ],
    };

    /// <summary>A syntactically valid example cid_hash (64 lowercase hex chars).</summary>
    public const string ExampleHash = "c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646";
}
