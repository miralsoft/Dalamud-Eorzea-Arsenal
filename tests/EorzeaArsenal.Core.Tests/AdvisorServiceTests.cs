using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
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

    /// <summary>
    /// The advisor's one ranking, as the server sends it: raw source spellings, a <c>when</c> that is
    /// one of three shapes, and material with have/need. Parsing this wrong would misprice the plan.
    /// </summary>
    [Fact]
    public void DeserializesTheServerSideRecommendation()
    {
        const string body = """
        {"data":{"character_id":"11","job":"WHM","target":"sl/abc-123","target_name":"2.5 BiS",
          "gear_index":3,"tome_balance":830,"weekly_cap":450,"sort":"power",
          "steps":[
            {"slot":"Head","from":40123,"to":49700,"final":49586,"kind":"buy","cost":495,
             "material":[{"id":49757,"name":"Glaze","count":1,"owned":2}],"books_missing":0,
             "pct":42,"when":{"now":true},"vendor":"Cihanti"},
            {"slot":"Body","from":null,"to":49631,"final":null,"kind":"books","cost":0,
             "material":[],"books_missing":2,"pct":30,"when":{"books":2},"vendor":null},
            {"slot":"Feet","from":1,"to":2,"final":null,"kind":"buy","cost":495,
             "material":[],"books_missing":0,"pct":28,"when":{"week":2},"vendor":null}],
          "slots":{"Head":{"current":{"id":40123,"name":"Old","ilvl":730,"source":"Tome","score":10},
                           "bis":{"id":49586,"name":"New","ilvl":790,"source":"AugTome","score":20},
                           "recommended":49700,"even":false,
                           "options":[{"id":49700,"name":"Base","ilvl":780,"source":"Tome","score":18}]}},
          "materials":[{"id":49757,"name":"Glaze","need":3,"owned":2}]}}
        """;

        var data = JsonSerializer.Deserialize<AdvisorOptionsResponse>(body, EorzeaJson.Options)!.Data!;

        Assert.Equal("11", data.CharacterId);
        Assert.Equal(830, data.TomeBalance);
        Assert.Equal(450, data.WeeklyCap);
        Assert.Equal(3, data.GearIndex);

        var buy = data.Steps![0];
        Assert.Equal("buy", buy.Kind);
        Assert.Equal(495, buy.Cost);
        Assert.Equal(49586, buy.Final);
        Assert.True(buy.When!.Now);
        Assert.Equal("Cihanti", buy.Vendor);
        Assert.Equal(2, buy.Material![0].Owned);

        // The three when-shapes must stay distinguishable, not collapse into "now".
        Assert.False(data.Steps[1].When!.Now);
        Assert.Equal(2, data.Steps[1].When!.Books);
        Assert.Equal(2, data.Steps[2].When!.Week);
        Assert.Null(data.Steps[2].When!.Books);

        // An empty slot is a real state, not a zero.
        Assert.Null(data.Steps[1].From);

        var head = data.Slots!["Head"];
        Assert.Equal(49700, head.Recommended);
        Assert.Equal(790, head.Bis!.Ilvl);
        Assert.Single(head.Options!);
        Assert.Equal(3, data.Materials![0].Need);
    }

    /// <summary>
    /// <c>materials</c> and <c>target_needs</c> answer two different questions and must stay apart: the
    /// first is what the advised path costs (a bridge's material included), the second what the BiS set
    /// itself costs. Conflating them told a player with an Ultimate weapon that their BiS wanted the
    /// tome bridge's upgrade material.
    /// </summary>
    [Fact]
    public void TargetNeedsAreSeparateFromThePathsNeeds()
    {
        const string body = """
        {"data":{"job":"DRK","target":"sl/abc-123",
          "materials":[{"id":49759,"name":"Solvent","need":1,"owned":0},
                       {"id":49758,"name":"Twine","need":2,"owned":0}],
          "target_needs":{"tomes":2190,
            "materials":[{"id":49758,"name":"Twine","need":1,"owned":0,"for":["Body"]},
                         {"id":49763,"name":"Edition IV","need":8,"owned":0,"for":["Weapon","Legs"]}]}}}
        """;

        var data = JsonSerializer.Deserialize<AdvisorOptionsResponse>(body, EorzeaJson.Options)!.Data!;

        // The path still wants the bridge's Solvent…
        Assert.Contains(data.Materials!, m => m.Id == 49759);

        // …but the set itself does not, and it asks for less Twine.
        Assert.DoesNotContain(data.TargetNeeds!.Materials!, m => m.Id == 49759);
        Assert.Equal(1, data.TargetNeeds.Materials!.Single(m => m.Id == 49758).Need);
        Assert.Equal(2190, data.TargetNeeds.Tomes);

        // `for` names the slots, so a row can say what the material is actually for.
        Assert.Equal(["Weapon", "Legs"], data.TargetNeeds.Materials.Single(m => m.Id == 49763).For);
    }

    /// <summary>
    /// The advice used to carry the raw xivgear source spellings while <c>/gear/bis</c> sent the
    /// lower-case enum; the server has since unified on the enum. Both still resolve, so an older
    /// server keeps rendering correctly.
    /// </summary>
    [Theory]
    [InlineData("Tome", "source.tome")]
    [InlineData("tome", "source.tome")]
    [InlineData("AugTome", "source.augmented_tome")]
    [InlineData("augmented_tome", "source.augmented_tome")]
    [InlineData("SavageRaid", "source.raid")]
    [InlineData("ExtremeTrial", "source.extreme")]
    public void BothSourceVocabulariesResolve(string source, string expectedKey) =>
        Assert.Equal(expectedKey, SourceNames.LocKey(source));

    [Fact]
    public async Task OptionsAreCachedPerRankingAndDroppedOnInvalidate()
    {
        var api = new FakeApiClient();
        var service = NewService(api);

        await service.EnsureOptionsAsync(1234, "WHM", Target, 3, "power", CancellationToken.None);
        Assert.True(service.TryGetOptions(1234, "whm", Target, "power", out var options));
        Assert.NotNull(options);
        Assert.Equal((1234L, "WHM", Target, (int?)3, (string?)"power"), api.AdvisorOptionsReads[0]);

        // Same ranking → cached; a different ranking is a different answer and is fetched.
        await service.EnsureOptionsAsync(1234, "whm", Target, 3, "power", CancellationToken.None);
        Assert.Single(api.AdvisorOptionsReads);
        await service.EnsureOptionsAsync(1234, "whm", Target, 3, "cheap", CancellationToken.None);
        Assert.Equal(2, api.AdvisorOptionsReads.Count);

        // The advice depends on current gear and holdings, so it must be droppable without
        // touching the stored plans.
        await service.EnsureAsync(1234, "whm", Target, CancellationToken.None);
        service.InvalidateOptions();
        Assert.False(service.TryGetOptions(1234, "whm", Target, "power", out _));
        Assert.True(service.TryGet(1234, "whm", Target, out _));
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
