using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// What a server offers, read from <c>features</c> of <c>GET /version</c>. Never from the version number:
/// dev carries new features under an old number, and a version check would hide them where they are tested.
/// </summary>
public sealed class ServerFeaturesTests
{
    private static ApiResult<VersionResponse> Version(string appVersion, params string[]? features) =>
        ApiResult<VersionResponse>.Ok(new VersionResponse { AppVersion = appVersion, Features = features?.ToList() });

    /// <summary>The dev case of the 1.4 rehearsal: an old number, the new features there.</summary>
    [Fact]
    public async Task TheListDecidesNotTheVersionNumber()
    {
        var api = new FakeApiClient { VersionResult = Version("1.3.2", FeatureNames.Relics, FeatureNames.InventoryArmoire) };
        var features = new ServerFeatures(api, new StubSettings("https://dev.example.test/api/v1"), new TestClock());

        await features.RefreshAsync(CancellationToken.None);

        Assert.True(features.Has(FeatureNames.Relics));
        Assert.True(features.Has(FeatureNames.InventoryArmoire));
        Assert.False(features.Has(FeatureNames.Polls));
    }

    [Fact]
    public async Task AServerWithoutTheFieldOffersNothing()
    {
        var api = new FakeApiClient { VersionResult = Version("1.4.0", null) };
        var features = new ServerFeatures(api, new StubSettings("https://example.test/api/v1"), new TestClock());

        await features.RefreshAsync(CancellationToken.None);

        Assert.False(features.Has(FeatureNames.Relics));
        Assert.Empty(features.Current);
    }

    /// <summary>Nothing is offered before the first answer: a feature shown wrongly answers every click with a 404.</summary>
    [Fact]
    public void NothingIsOfferedBeforeTheFirstAnswer()
    {
        var features = new ServerFeatures(new FakeApiClient(), new StubSettings("https://example.test/api/v1"), new TestClock());

        Assert.False(features.Has(FeatureNames.Relics));
    }

    /// <summary>A failed read keeps what was held; a new feature is not lost to one bad request.</summary>
    [Fact]
    public async Task AFailedReadKeepsTheHeldList()
    {
        var clock = new TestClock();
        var api = new FakeApiClient { VersionResult = Version("1.5.0", FeatureNames.Polls) };
        var features = new ServerFeatures(api, new StubSettings("https://example.test/api/v1"), clock);
        await features.RefreshAsync(CancellationToken.None);

        api.VersionResult = ApiResult<VersionResponse>.Fail(new ApiError { Kind = ApiErrorKind.Network, Message = "x" });
        clock.Advance(ServerFeatures.Freshness + TimeSpan.FromMinutes(1));
        await features.RefreshIfStaleAsync(CancellationToken.None);

        Assert.True(features.Has(FeatureNames.Polls));
    }

    /// <summary>
    /// A server gains features while the plugin runs. Within <see cref="ServerFeatures.Freshness"/> nothing
    /// is asked again; after it, the next check reads the new list.
    /// </summary>
    [Fact]
    public async Task ANewFeatureArrivesAfterFreshnessWithoutARestart()
    {
        var clock = new TestClock();
        var api = new FakeApiClient { VersionResult = Version("1.4.0", null) };
        var features = new ServerFeatures(api, new StubSettings("https://example.test/api/v1"), clock);
        await features.RefreshAsync(CancellationToken.None);

        api.VersionResult = Version("1.4.0", FeatureNames.CurrencySync);
        await features.RefreshIfStaleAsync(CancellationToken.None);
        Assert.False(features.Has(FeatureNames.CurrencySync));

        clock.Advance(ServerFeatures.Freshness + TimeSpan.FromSeconds(1));
        await features.RefreshIfStaleAsync(CancellationToken.None);
        Assert.True(features.Has(FeatureNames.CurrencySync));
    }

    /// <summary>A list learned from one address never decides for another.</summary>
    [Fact]
    public async Task TheListIsKeptPerAddress()
    {
        var settings = new StubSettings("https://dev.example.test/api/v1");
        var api = new FakeApiClient { VersionResult = Version("1.5.0", FeatureNames.Relics) };
        var features = new ServerFeatures(api, settings, new TestClock());
        await features.RefreshAsync(CancellationToken.None);

        settings.BaseUrl = "https://live.example.test/api/v1";

        Assert.False(features.Has(FeatureNames.Relics));
    }

    private sealed class StubSettings(string baseUrl) : EorzeaArsenal.Abstractions.IApiSettings
    {
        public string BaseUrl { get; set; } = baseUrl;
    }
}
