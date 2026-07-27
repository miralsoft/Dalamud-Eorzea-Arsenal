using System.Text.Json;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>
/// The report a player writes in game, sent to the same inbox the website's contact form feeds. The
/// wire shape matters because the server takes the situation apart again — a pre-joined line could
/// not be split back reliably, which is why every part travels as its own field.
/// </summary>
public sealed class ContactRequestTests
{
    [Fact]
    public void TheClientBlockTravelsAsSeparateSnakeCaseFields()
    {
        var body = JsonSerializer.Serialize(
            new ContactRequest
            {
                Kind = ContactKinds.Bug,
                Subject = "Sync hängt nach Klassenwechsel",
                Message = "Nach dem Wechsel von DRK auf WHM wird nichts mehr hochgeladen.",
                Client = new ContactClient
                {
                    Character = "Sanaka Sundream",
                    World = "Twintania",
                    GameVersion = "7.4",
                    DalamudVersion = "15.0.2.3",
                    PluginVersion = "1.0.0",
                    Where = "Advisor",
                },
            },
            EorzeaJson.Options);

        Assert.Contains("\"kind\":\"bug\"", body, StringComparison.Ordinal);
        Assert.Contains("\"game_version\":\"7.4\"", body, StringComparison.Ordinal);
        Assert.Contains("\"dalamud_version\":\"15.0.2.3\"", body, StringComparison.Ordinal);
        Assert.Contains("\"plugin_version\":\"1.0.0\"", body, StringComparison.Ordinal);
        Assert.Contains("\"world\":\"Twintania\"", body, StringComparison.Ordinal);

        // Which window the report was written from — the one piece of context a player should never
        // have to type out, and the difference between a searchable report and a vague one.
        Assert.Contains("\"where\":\"Advisor\"", body, StringComparison.Ordinal);

        // A world name with a space in it is exactly why this is not one joined string.
        Assert.Contains("\"character\":\"Sanaka Sundream\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every part of the situation is optional; what cannot be read is left out rather than sent as an
    /// empty string, which the server would have to display as a blank field.
    /// </summary>
    [Fact]
    public void UnreadablePartsAreOmittedEntirely()
    {
        var body = JsonSerializer.Serialize(
            new ContactRequest
            {
                Kind = ContactKinds.Other,
                Subject = "Test",
                Message = "Ein Test aus einer Testinstallation.",
                Client = new ContactClient { PluginVersion = "1.0.0" },
            },
            EorzeaJson.Options);

        Assert.DoesNotContain("character", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dalamud_version", body, StringComparison.Ordinal);
        Assert.Contains("\"plugin_version\":\"1.0.0\"", body, StringComparison.Ordinal);
    }

    /// <summary>No client block at all is a valid report — the key already says who is writing.</summary>
    [Fact]
    public void TheClientBlockIsOptional()
    {
        var body = JsonSerializer.Serialize(
            new ContactRequest { Kind = ContactKinds.Bug, Subject = "S", Message = "0123456789" },
            EorzeaJson.Options);

        Assert.DoesNotContain("client", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The topic keys are exactly the four the endpoint knows — anything else is a <c>400</c>, and
    /// they are keys, not labels, so they stay lower case whatever the UI shows.
    /// </summary>
    [Fact]
    public void TheFourTopicsAreKeysNotLabels()
    {
        Assert.Equal(["bug", "feature", "feedback", "other"], ContactKinds.All);
        Assert.All(ContactKinds.All, k => Assert.Equal(k.ToLowerInvariant(), k));

        Assert.True(ContactKinds.IsKnown("feature"));
        Assert.False(ContactKinds.IsKnown("Bug"));      // case matters — it is a key
        Assert.False(ContactKinds.IsKnown("question"));
        Assert.False(ContactKinds.IsKnown(null));
    }

    /// <summary>
    /// The picker is built from what the server says is open. A topic can be switched off, and
    /// offering it anyway would fail the report with a 503 after the player wrote their text.
    /// </summary>
    [Fact]
    public void OpenTopicsAndDirectContactAreRead()
    {
        const string body = """
        {"data":{"character":"Sanaka Sundream","world":"Twintania","discord":"sanaka",
          "discord_invite":"https://discord.gg/abc","kinds":["feedback","bug","feature","other"]}}
        """;

        var info = JsonSerializer.Deserialize<ContactInfoResponse>(body, EorzeaJson.Options)!.Data!;

        Assert.Equal(["feedback", "bug", "feature", "other"], info.Kinds);
        Assert.Equal("https://discord.gg/abc", info.DiscordInvite);
        Assert.Equal("Sanaka Sundream", info.Character);

        // A server that offers fewer topics narrows the picker rather than widening it.
        var reduced = JsonSerializer.Deserialize<ContactInfoResponse>(
            """{"data":{"kinds":["bug"]}}""", EorzeaJson.Options)!.Data!;
        Assert.Equal(["bug"], reduced.Kinds);
        Assert.Null(reduced.DiscordInvite);
    }

    /// <summary>The limits are mirrored so the window can stop before the request does.</summary>
    [Fact]
    public void TheServersLimitsAreMirrored()
    {
        Assert.Equal(140, ContactKinds.MaxSubject);
        Assert.Equal(10, ContactKinds.MinMessage);
        Assert.Equal(3000, ContactKinds.MaxMessage);
        Assert.Equal(32, ContactKinds.MaxVersion);
        Assert.Equal(80, ContactKinds.MaxWhere);
    }

    /// <summary>
    /// The context block must never be the reason a report is refused. Dalamud's <c>ScmVersion</c> is
    /// the realistic case: a <c>git describe</c> on a non-stable build outgrows 32 characters, and
    /// losing the player's written message over a background detail would be absurd.
    /// </summary>
    [Fact]
    public void AnOverlongContextValueIsShortenedRatherThanCostingTheReport()
    {
        const string scm = "v15.0.2.3-14-g91ad60c88cde1932bb1a1d9c931ff9d4a670c1ef (stg)";
        var clipped = ContactKinds.Clip(scm, ContactKinds.MaxVersion);

        Assert.NotNull(clipped);
        Assert.Equal(ContactKinds.MaxVersion, clipped!.Length);
        Assert.StartsWith("v15.0.2.3-14-g", clipped, StringComparison.Ordinal);

        // A value that already fits is handed through untouched apart from stray whitespace.
        Assert.Equal("7.4", ContactKinds.Clip("  7.4  ", ContactKinds.MaxVersion));
    }

    /// <summary>
    /// Nothing to say stays nothing: an empty or blank value is omitted rather than sent as an empty
    /// string the server would have to render as a blank field.
    /// </summary>
    [Fact]
    public void ThereIsNoSuchThingAsAnEmptyContextValue()
    {
        Assert.Null(ContactKinds.Clip(null, ContactKinds.MaxVersion));
        Assert.Null(ContactKinds.Clip("", ContactKinds.MaxVersion));
        Assert.Null(ContactKinds.Clip("   ", ContactKinds.MaxVersion));
        Assert.Null(ContactKinds.Clip("anything", 0));
    }

    /// <summary>
    /// Cutting must not produce malformed text. A "where" label is translated prose in the general
    /// case, so the cut stops short of splitting a surrogate pair rather than trusting it to be ASCII.
    /// </summary>
    [Fact]
    public void CuttingNeverSplitsACharacterInHalf()
    {
        // Each emoji is one surrogate pair, so the limit falls exactly between the two halves of one.
        var text = string.Concat(Enumerable.Repeat("🐛", 10));
        var clipped = ContactKinds.Clip(text, 5);

        Assert.NotNull(clipped);
        Assert.Equal(4, clipped!.Length);                  // stepped back rather than cutting mid-pair
        Assert.Equal("🐛🐛", clipped);
    }

    /// <summary>
    /// A failure has to reach the player: nothing is stored on the way, so the plugin must be able to
    /// tell "could not deliver" (503) from "you may not" (403) and from "too many" (429).
    /// </summary>
    [Fact]
    public void TheProblemBodyKeepsItsDetailAndStatus()
    {
        const string problem = """
        {"type":"about:blank","title":"Service Unavailable","status":503,
         "detail":"Die Meldung konnte nicht zugestellt werden.","request_id":"abc-123"}
        """;

        var parsed = JsonSerializer.Deserialize<ProblemDetails>(problem, EorzeaJson.Options)!;

        Assert.Equal(503, parsed.Status);
        Assert.Equal("Die Meldung konnte nicht zugestellt werden.", parsed.Detail);
        Assert.Equal("abc-123", parsed.RequestId);
    }
}
