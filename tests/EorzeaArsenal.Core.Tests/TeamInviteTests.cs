using System.Net;
using System.Text.Json;
using EorzeaArsenal.Api;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;
using EorzeaArsenal.Tests.TestSupport;
using Xunit;

namespace EorzeaArsenal.Tests;

/// <summary>The invitation link: what is sent, what a refusal says, and that the secret goes nowhere.</summary>
public sealed class TeamInviteTests
{
    private static readonly Localizer De = new(Localizer.German);

    private static ApiClient Make(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), new StubHttpMessageHandler.Settings());

    /// <summary>
    /// A <c>max_uses</c> outside 1 to 100 means <b>unlimited</b> on the server, which turns a link meant
    /// for one person into one anybody who sees it can use. So both numbers are forced into range here, and
    /// both are always sent, so the server's own defaults never decide.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 7, 1, 7)]
    [InlineData(500, 99, 100, 30)]
    [InlineData(3, 14, 3, 14)]
    public void UsesAndDaysAreForcedIntoTheRangeTheServerAccepts(int uses, int days, int sentUses, int sentDays)
    {
        var request = InviteRequest.Link(uses, days);

        Assert.Equal((sentUses, sentDays), (request.MaxUses, request.TtlDays));
    }

    [Fact]
    public void TheRequestNamesItsFieldsTheWayTheServerReadsThem()
    {
        var json = JsonSerializer.Serialize(InviteRequest.Link(1, 7), EorzeaJson.Options);

        Assert.Equal("""{"kind":"link","max_uses":1,"ttl_days":7}""", json);
    }

    [Fact]
    public async Task TheLinkComesBackFromTheShapeTheBriefingGives()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.Created,
            """
            { "code": "abc123", "link": "https://xivarsenal.app/beitreten/abc123", "expires_in": 604800,
              "invite": { "id": 17, "kind": "link", "label": null, "uses": 0, "max_uses": 1, "expires_at": "2026-10-03 12:00:00" } }
            """);

        var result = await Make(handler).CreateInviteAsync("ea_secret", 6, InviteRequest.Link(1, 7), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://xivarsenal.app/beitreten/abc123", result.Value!.Link);
        Assert.Equal(17, result.Value.Invite!.Id);
        Assert.EndsWith("/teams/6/invite", handler.Requests.Single().Uri!.AbsolutePath);
    }

    /// <summary>
    /// A log line that interpolates the answer by accident must not carry the secret. The code and the link
    /// are the whole reason the website lists invitations without them.
    /// </summary>
    [Fact]
    public void TheAnswerPrintsNothingSecret()
    {
        var answer = new InviteResponse
        {
            Code = "abc123",
            Link = "https://xivarsenal.app/beitreten/abc123",
            Invite = new InviteInfo { Id = 17 },
        };

        var printed = $"{answer}";

        Assert.DoesNotContain("abc123", printed, StringComparison.Ordinal);
        Assert.Contains("17", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A403NamesTheRightTheUserLacks()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.Forbidden,
            """{"title":"Forbidden","status":403,"detail":"Missing capability: manage_members","missing_capability":"manage_members"}""",
            contentType: "application/problem+json");

        var result = await Make(handler).CreateInviteAsync("ea_secret", 6, InviteRequest.Link(1, 7), CancellationToken.None);

        Assert.Equal("manage_members", result.Error!.MissingCapability);
        Assert.Null(result.Error.MissingScope);
        Assert.Equal(ForbiddenCause.MissingCapability, TeamErrors.CauseOf(result.Error));
    }

    [Fact]
    public async Task A403NamesThePermissionTheKeyLacks()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.Forbidden,
            """{"title":"Forbidden","status":403,"detail":"Missing required scope: teams:write","missing_scope":"teams:write"}""",
            contentType: "application/problem+json");

        var result = await Make(handler).CreateInviteAsync("ea_secret", 6, InviteRequest.Link(1, 7), CancellationToken.None);

        Assert.Equal("teams:write", result.Error!.MissingScope);
        Assert.Equal(ForbiddenCause.MissingScope, TeamErrors.CauseOf(result.Error));
    }

    /// <summary>
    /// The rule the server asked for: never read the cause out of the text. A 403 whose <c>detail</c> says
    /// "scope" but that names neither member is not treated as a missing scope, because the words are for
    /// people and may change.
    /// </summary>
    [Fact]
    public void TheCauseIsNeverReadOutOfTheText()
    {
        var error = new ApiError
        {
            Kind = ApiErrorKind.Forbidden,
            StatusCode = 403,
            Message = "Missing required scope: teams:write",
            Detail = "Missing required scope: teams:write",
        };

        Assert.Equal(ForbiddenCause.Unknown, TeamErrors.CauseOf(error));
        Assert.Equal(De.Get(LocKeys.TeamsErrorForbidden), TeamErrors.DescribeInvite(error, De));
    }

    [Fact]
    public void TheTwo403sGetTwoDifferentRemedies()
    {
        var capability = new ApiError { Kind = ApiErrorKind.Forbidden, Message = "x", MissingCapability = "manage_members" };
        var scope = new ApiError { Kind = ApiErrorKind.Forbidden, Message = "x", MissingScope = "teams:write" };

        Assert.Equal(De.Get(LocKeys.TeamsInviteNoRight), TeamErrors.DescribeInvite(capability, De));
        Assert.Equal(De.Get(LocKeys.TeamsInviteReconnect), TeamErrors.DescribeInvite(scope, De));
    }

    /// <summary>The wait is rounded up, so a player told "in 2 minutes" never tries again a few seconds early.</summary>
    [Fact]
    public void TooManyInvitationsSaysHowLongToWait()
    {
        var error = new ApiError { Kind = ApiErrorKind.RateLimited, Message = "x", RetryAfter = TimeSpan.FromSeconds(90) };

        Assert.Equal("Zu viele Einladungen in kurzer Zeit. In 2 Minuten wieder möglich.", TeamErrors.DescribeInvite(error, De));
    }

    [Fact]
    public void OneMinuteIsSingular()
    {
        var error = new ApiError { Kind = ApiErrorKind.RateLimited, Message = "x", RetryAfter = TimeSpan.FromSeconds(40) };

        Assert.Equal("Zu viele Einladungen in kurzer Zeit. In 1 Minute wieder möglich.", TeamErrors.DescribeInvite(error, De));
    }

    [Fact]
    public void TooManyInvitationsWithoutAWaitSaysLater()
    {
        var error = new ApiError { Kind = ApiErrorKind.RateLimited, Message = "x" };

        Assert.Equal(De.Get(LocKeys.TeamsInviteRateLimited), TeamErrors.DescribeInvite(error, De));
    }

    [Fact]
    public async Task TheWaitComesFromTheRetryAfterHeader()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 300);

        var result = await Make(handler).CreateInviteAsync("ea_secret", 6, InviteRequest.Link(1, 7), CancellationToken.None);

        Assert.Equal(ApiErrorKind.RateLimited, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(300), result.Error.RetryAfter);
    }

    /// <summary>
    /// A 400 shows the server's own words as they stand. That is displaying them, not reading them: nothing
    /// here branches on the text, and claiming the one 400 the briefing names would be a guess.
    /// </summary>
    [Fact]
    public void ARefusalShowsTheServersOwnWords()
    {
        var error = new ApiError { Kind = ApiErrorKind.BadRequest, Message = "x", Detail = "Maximum number of open invites reached." };

        Assert.Equal("Die Einladung wurde abgelehnt: Maximum number of open invites reached.", TeamErrors.DescribeInvite(error, De));
    }
}
