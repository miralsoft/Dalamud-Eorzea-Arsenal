using EorzeaArsenal.Api;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The scope check used to warn when a key lacks gear:write (R17).</summary>
public sealed class ScopeUtilTests
{
    [Fact]
    public void Detects_gear_write()
    {
        Assert.True(ScopeUtil.HasGearWrite(["profile:read", "gear:write"]));
    }

    [Fact]
    public void Is_case_insensitive_and_trims()
    {
        Assert.True(ScopeUtil.HasGearWrite([" GEAR:WRITE "]));
    }

    [Theory]
    [InlineData("profile:read")]
    [InlineData("gear:read")]
    public void Rejects_when_missing(string scope)
    {
        Assert.False(ScopeUtil.HasGearWrite([scope]));
    }

    [Fact]
    public void Null_is_false()
    {
        Assert.False(ScopeUtil.HasGearWrite(null));
    }
}
