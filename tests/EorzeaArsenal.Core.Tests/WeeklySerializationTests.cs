using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// Locks the weekly wire contract: the <c>items</c> object keeps its literal camelCase keys (they are
/// not snake-cased like the top-level fields), and the GET/PUT bodies round-trip.
/// </summary>
public sealed class WeeklySerializationTests
{
    [Fact]
    public void Payload_keeps_literal_item_keys()
    {
        var payload = new WeeklyPayload
        {
            Items = new Dictionary<string, object> { ["tomesHave"] = 450, ["custom"] = true },
        };

        var json = JsonSerializer.Serialize(payload, EorzeaJson.Options);

        Assert.Contains("\"items\"", json);
        Assert.Contains("\"tomesHave\":450", json);
        Assert.Contains("\"custom\":true", json);
    }

    [Fact]
    public void Response_parses_data_week_and_savage_lockout()
    {
        const string body = """{"data":{"tomesHave":300,"custom":true},"week":"2026-06-23","savage_lockout":true}""";

        var res = JsonSerializer.Deserialize<WeeklyResponse>(body, EorzeaJson.Options);

        Assert.NotNull(res);
        Assert.Equal("2026-06-23", res!.Week);
        Assert.True(res.SavageLockout);
        Assert.NotNull(res.Data);
        Assert.Equal(300, res.Data!.Value.GetProperty("tomesHave").GetInt32());
        Assert.True(res.Data!.Value.GetProperty("custom").GetBoolean());
    }

    [Fact]
    public void Response_tolerates_empty_object_serialized_as_array()
    {
        // A PHP backend serializes an empty object as [] — this must parse, not throw.
        const string body = """{"data":[],"week":"2026-06-23","savage_lockout":false}""";

        var res = JsonSerializer.Deserialize<WeeklyResponse>(body, EorzeaJson.Options);

        Assert.NotNull(res);
        Assert.Equal(JsonValueKind.Array, res!.Data!.Value.ValueKind);
    }

    [Fact]
    public void Push_result_parses()
    {
        const string body = """{"status":"ok","week":"2026-06-23","data":{"tomesHave":450}}""";

        var res = JsonSerializer.Deserialize<WeeklyPushResult>(body, EorzeaJson.Options);

        Assert.NotNull(res);
        Assert.Equal("ok", res!.Status);
        Assert.Equal(450, res.Data!.Value.GetProperty("tomesHave").GetInt32());
    }
}
