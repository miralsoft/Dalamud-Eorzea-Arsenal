using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The purchase-advisor plan: the wire shapes of <c>/me/advisor-plan</c> (including the <c>target</c>
/// a plan is keyed by, which <c>/gear/bis</c> must hand us) and the read/write/delete behaviour of
/// <see cref="AdvisorService"/> — above all that "no plan saved" is a cached, non-error state.
/// </summary>
public sealed class AdvisorServiceTests
{
    private const string Target = "sl/abc-123";

    private static AdvisorService NewService(FakeApiClient api)
    {
        var tokens = new InMemoryTokenStore();
        tokens.SetApiKey("key");
        return new AdvisorService(api, tokens, NullLog.Instance);
    }

    [Fact]
    public void DeserializesPlanWithLiteralSlotKeys()
    {
        const string body = """
        {"data":{"job":"whm","target":"sl/abc-123","target_name":"2.5 BiS",
          "items":{"Weapon":{"id":49631},"RingLeft":{"id":49712}},"updated_at":"2026-07-01T10:00:00Z"}}
        """;

        var plan = JsonSerializer.Deserialize<AdvisorPlanResponse>(body, EorzeaJson.Options)!.Data!;

        Assert.Equal("whm", plan.Job);
        Assert.Equal(Target, plan.Target);
        Assert.Equal("2.5 BiS", plan.TargetName);
        Assert.Equal(49631, plan.Items!["Weapon"].Id);
        Assert.Equal(49712, plan.Items["RingLeft"].Id);
    }

    [Fact]
    public void NullDataIsAValidNoPlanAnswer()
    {
        var response = JsonSerializer.Deserialize<AdvisorPlanResponse>("""{"data":null}""", EorzeaJson.Options);
        Assert.Null(response!.Data);
    }

    [Fact]
    public void SaveRequestKeepsSlotKeysAndSnakeCasesTheRest()
    {
        var body = JsonSerializer.Serialize(
            new AdvisorPlanRequest
            {
                CharacterId = 1234,
                Job = "whm",
                Target = Target,
                TargetName = "2.5 BiS",
                Items = new Dictionary<string, AdvisorPlanItem>(StringComparer.Ordinal) { ["RingLeft"] = new() { Id = 49712 } },
            },
            EorzeaJson.Options);

        Assert.Contains("\"character_id\":1234", body);
        Assert.Contains("\"target_name\":\"2.5 BiS\"", body);
        Assert.Contains("\"RingLeft\":{\"id\":49712}", body); // slot keys stay PascalCase
    }

    /// <summary>
    /// A plan is addressed by the set's <c>target</c>; the BiS read has to carry it, or nothing can be
    /// looked up. Absent on an older server, which must stay harmless.
    /// </summary>
    [Fact]
    public void BisGearsetCarriesTheTargetItIsKeyedBy()
    {
        const string body = """
        {"data":[{"cid_hash":"abc","job":"WHM","gear_index":0,"name":"2.5 BiS",
          "target":"sl/abc-123","target_name":"2.5 BiS","items":{}}]}
        """;

        var set = JsonSerializer.Deserialize<BisResponse>(body, EorzeaJson.Options)!.Data[0];
        Assert.Equal(Target, set.Target);
        Assert.Equal("2.5 BiS", set.TargetName);

        var legacy = JsonSerializer.Deserialize<BisResponse>(
            """{"data":[{"job":"WHM","gear_index":0,"items":{}}]}""", EorzeaJson.Options)!.Data[0];
        Assert.Null(legacy.Target);
    }

    [Fact]
    public async Task ReadsOnceAndCachesTheAnswer()
    {
        var api = new FakeApiClient
        {
            AdvisorPlanResult = ApiResult<AdvisorPlanResponse>.Ok(new AdvisorPlanResponse
            {
                Data = new AdvisorPlan
                {
                    Job = "whm",
                    Target = Target,
                    Items = new Dictionary<string, AdvisorPlanItem>(StringComparer.Ordinal) { ["Weapon"] = new() { Id = 49631 } },
                },
            }),
        };
        var service = NewService(api);

        await service.EnsureAsync(1234, "WHM", Target, CancellationToken.None);

        Assert.True(service.TryGet(1234, "whm", Target, out var plan));
        Assert.Equal(49631, plan!.Items!["Weapon"].Id);
        Assert.Equal((1234L, "WHM", Target), api.AdvisorPlanReads[0]);

        // Cached → no second call, even for a differently-cased job.
        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        Assert.Single(api.AdvisorPlanReads);
    }

    /// <summary>"No plan" is remembered too, or the view would re-ask the server every frame.</summary>
    [Fact]
    public async Task AMissingPlanIsCachedAsAKnownAbsence()
    {
        var api = new FakeApiClient { AdvisorPlanResult = ApiResult<AdvisorPlanResponse>.Ok(new AdvisorPlanResponse { Data = null }) };
        var service = NewService(api);

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);

        Assert.True(service.TryGet(1234, "whm", Target, out var plan)); // read…
        Assert.Null(plan);                                             // …and there is none
        Assert.Null(service.LastErrorKind);                             // which is not an error

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        Assert.Single(api.AdvisorPlanReads);
    }

    [Fact]
    public async Task AFailedReadStaysUncachedForARetry()
    {
        var api = new FakeApiClient
        {
            AdvisorPlanResult = ApiResult<AdvisorPlanResponse>.Fail(new ApiError { Kind = ApiErrorKind.Forbidden, Message = "no plans:read" }),
        };
        var service = NewService(api);

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);

        Assert.False(service.TryGet(1234, "whm", Target, out _));
        Assert.Equal(ApiErrorKind.Forbidden, service.LastErrorKind);

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        Assert.Equal(2, api.AdvisorPlanReads.Count);
    }

    [Fact]
    public async Task SaveSendsLowercaseJobDropsEmptySlotsAndCachesTheEcho()
    {
        var api = new FakeApiClient();
        var service = NewService(api);

        var saved = await service.SaveAsync(
            1234,
            "WHM",
            Target,
            "2.5 BiS",
            new Dictionary<string, long>(StringComparer.Ordinal) { ["Weapon"] = 49631, ["Head"] = 0 },
            CancellationToken.None);

        Assert.True(saved);
        var request = api.AdvisorPlanSaves[0];
        Assert.Equal("whm", request.Job);
        Assert.Equal(1234, request.CharacterId);
        Assert.Equal(49631, request.Items["Weapon"].Id);
        Assert.DoesNotContain("Head", request.Items.Keys); // an unset slot is not part of the plan

        // The saved plan is readable straight away — no re-fetch needed.
        Assert.True(service.TryGet(1234, "whm", Target, out var plan));
        Assert.Equal(49631, plan!.Items!["Weapon"].Id);
        Assert.Empty(api.AdvisorPlanReads);
    }

    [Fact]
    public async Task DeleteLeavesTheSetKnownToHaveNoPlan()
    {
        var api = new FakeApiClient();
        var service = NewService(api);
        await service.SaveAsync(1234, "whm", Target, null, new Dictionary<string, long>(StringComparer.Ordinal) { ["Weapon"] = 49631 }, CancellationToken.None);

        var deleted = await service.DeleteAsync(1234, "whm", Target, CancellationToken.None);

        Assert.True(deleted);
        Assert.Equal(Target, api.AdvisorPlanDeletes[0].Target);
        Assert.True(service.TryGet(1234, "whm", Target, out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public async Task WithoutAKeyOrATargetNothingIsRequested()
    {
        var api = new FakeApiClient();
        var keyless = new AdvisorService(api, new InMemoryTokenStore(), NullLog.Instance);
        await keyless.EnsureAsync(1234, "whm", Target, CancellationToken.None);

        var service = NewService(api);
        await service.EnsureAsync(1234, "whm", string.Empty, CancellationToken.None);
        await service.EnsureAsync(0, "whm", Target, CancellationToken.None);
        Assert.False(await service.SaveAsync(1234, "whm", string.Empty, null, new Dictionary<string, long>(StringComparer.Ordinal), CancellationToken.None));

        Assert.Empty(api.AdvisorPlanReads);
        Assert.Empty(api.AdvisorPlanSaves);
    }

    [Fact]
    public async Task InvalidateForcesAReRead()
    {
        var api = new FakeApiClient();
        var service = NewService(api);

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        service.Invalidate();
        Assert.False(service.TryGet(1234, "whm", Target, out _));

        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        Assert.Equal(2, api.AdvisorPlanReads.Count);
    }
}
