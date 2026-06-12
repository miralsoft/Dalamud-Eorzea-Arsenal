using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Tests.TestSupport;

/// <summary>A programmable <see cref="IApiClient"/> for service-level tests.</summary>
public sealed class FakeApiClient : IApiClient
{
    private readonly Queue<ApiResult<DeviceTokenResponse>> _tokenResults = new();
    private readonly Queue<ApiResult<GearPushResult>> _pushResults = new();

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
    public Task<ApiResult<VersionResponse>> GetVersionAsync(string? apiKey, CancellationToken ct) =>
        Task.FromResult(VersionResult);
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
