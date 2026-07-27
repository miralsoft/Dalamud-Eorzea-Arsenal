using EorzeaArsenal.Core;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Keys are held per API address so a test environment and production never share one. The rule that
/// makes that worth anything is the absence of a fallback: without it, the first switch to a test
/// server would send the production key straight to it.
/// </summary>
public sealed class ApiKeyRingTests
{
    private const string Live = "https://xivarsenal.app/api/v1";
    private const string Dev = "https://dev.xivarsenal.app/api/v1";

    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["xivarsenal.app"] = "live-key",
        ["dev.xivarsenal.app"] = "dev-key",
    };

    [Fact]
    public void EachAddressGetsItsOwnKey()
    {
        Assert.Equal("live-key", ApiKeyRing.KeyFor(Keys, Live));
        Assert.Equal("dev-key", ApiKeyRing.KeyFor(Keys, Dev));
    }

    /// <summary>The point of the whole thing: an unknown address is disconnected, not borrowed for.</summary>
    [Fact]
    public void AnUnknownAddressNeverBorrowsAKey()
    {
        Assert.Null(ApiKeyRing.KeyFor(Keys, "https://someone-elses-server.example/api/v1"));
        Assert.Null(ApiKeyRing.KeyFor(Keys, "http://127.0.0.1:8080/api/v1"));
        Assert.Null(ApiKeyRing.KeyFor(new Dictionary<string, string>(), Live));
        Assert.Null(ApiKeyRing.KeyFor(null, Live));
    }

    /// <summary>An empty stored value is not a key — it must read as disconnected, not as "".</summary>
    [Fact]
    public void AnEmptyStoredValueCountsAsNoKey() =>
        Assert.Null(ApiKeyRing.KeyFor(new Dictionary<string, string> { ["xivarsenal.app"] = "" }, Live));

    /// <summary>The server decides, not the path: two API versions on one host share a key.</summary>
    [Theory]
    [InlineData("https://xivarsenal.app/api/v1", "xivarsenal.app")]
    [InlineData("https://xivarsenal.app/api/v2/", "xivarsenal.app")]
    [InlineData("https://XIVARSENAL.app/api/v1", "xivarsenal.app")]
    [InlineData("http://127.0.0.1:8080/api/v1", "127.0.0.1")]
    public void OnlyTheHostIdentifiesAnAddress(string url, string expected) =>
        Assert.Equal(expected, ApiKeyRing.HostOf(url));

    /// <summary>Garbage must not throw into the config screen; it just becomes its own "address".</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void AnUnparsableAddressIsHandledQuietly(string url)
    {
        _ = ApiKeyRing.HostOf(url);
        Assert.Null(ApiKeyRing.KeyFor(Keys, url));
    }

    [Fact]
    public void ProductionIsRecognisedSoTheUiCanWarnAboutTheRest()
    {
        Assert.False(ApiKeyRing.IsCustomAddress(Live, Live));
        Assert.False(ApiKeyRing.IsCustomAddress("https://xivarsenal.app/api/v2", Live));
        Assert.True(ApiKeyRing.IsCustomAddress(Dev, Live));
        Assert.True(ApiKeyRing.IsCustomAddress("http://127.0.0.1:8080/api/v1", Live));
    }
}
