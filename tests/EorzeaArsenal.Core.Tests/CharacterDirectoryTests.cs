using EorzeaArsenal.Core;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>Verifies the <c>cid_hash → character_id</c> registry: record/resolve, change events, seeding.</summary>
public sealed class CharacterDirectoryTests
{
    [Fact]
    public void Records_and_resolves()
    {
        var dir = new CharacterDirectory();
        Assert.False(dir.TryGet("hash", out _));

        dir.Record("hash", "42");

        Assert.True(dir.TryGet("hash", out var id));
        Assert.Equal("42", id);
    }

    [Fact]
    public void Ignores_empty_inputs()
    {
        var dir = new CharacterDirectory();

        dir.Record(string.Empty, "42");
        dir.Record("hash", null);

        Assert.False(dir.TryGet("hash", out _));
    }

    [Fact]
    public void Raises_changed_only_on_actual_change()
    {
        var dir = new CharacterDirectory();
        var count = 0;
        dir.Changed += () => count++;

        dir.Record("hash", "42"); // new
        dir.Record("hash", "42"); // same → no event
        dir.Record("hash", "43"); // changed → event

        Assert.Equal(2, count);
    }

    [Fact]
    public void Seeds_from_existing_map()
    {
        var dir = new CharacterDirectory(new Dictionary<string, string> { ["hash"] = "7" });

        Assert.True(dir.TryGet("hash", out var id));
        Assert.Equal("7", id);
        Assert.Equal("7", dir.Snapshot()["hash"]);
    }
}
