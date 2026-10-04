using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>Why a team request was refused with a 403, as the server names it.</summary>
public enum ForbiddenCause
{
    /// <summary>Not a 403, or a 403 that names neither member.</summary>
    Unknown,

    /// <summary>The key lacks a permission. Reconnecting helps.</summary>
    MissingScope,

    /// <summary>The user lacks a right in that team. Reconnecting does not help; the team lead can grant it.</summary>
    MissingCapability,
}

/// <summary>What a refused team request tells the player, decided from what the server says and nothing else.</summary>
/// <remarks>
/// <para>
/// Every decision here turns on the status code and on the members the server put in the problem response
/// for exactly this purpose, never on the wording of <c>detail</c> or <c>title</c>. That text is written for
/// people and may change; branching on it is how a reworded sentence on the server becomes a wrong
/// instruction in the game. The two 403 members were added on 2026-09-27 so that this could be done
/// without reading prose.
/// </para>
/// <para>
/// The two kinds of 403 need different remedies, which is why they are told apart at all. Telling somebody
/// to reconnect when their team has not granted a right sends them in a circle: they reconnect, get the
/// same key with the same scopes, and meet the same refusal.
/// </para>
/// </remarks>
public static class TeamErrors
{
    /// <summary>Which of the two 403s this is, from the members the server set.</summary>
    /// <param name="error">The error, if any.</param>
    /// <returns>The cause; <see cref="ForbiddenCause.Unknown"/> for anything that names neither.</returns>
    public static ForbiddenCause CauseOf(ApiError? error) =>
        error is not { Kind: ApiErrorKind.Forbidden } ? ForbiddenCause.Unknown
        : !string.IsNullOrEmpty(error.MissingCapability) ? ForbiddenCause.MissingCapability
        : !string.IsNullOrEmpty(error.MissingScope) ? ForbiddenCause.MissingScope
        : ForbiddenCause.Unknown;

    /// <summary>What a failed invitation tells the player.</summary>
    /// <param name="error">The error.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>One sentence with the remedy, where there is one.</returns>
    /// <remarks>
    /// A 400 shows the server's own <c>detail</c> as it stands. That is displaying it, not reading it: the
    /// plugin does not branch on the words. The briefing names one 400, the limit of open invitations per
    /// team, but a 400 can mean other things too, so claiming that one would be a guess.
    /// </remarks>
    public static string DescribeInvite(ApiError? error, ILocalizer loc)
    {
        if (error is null)
        {
            return loc.Get(LocKeys.TeamsErrorGeneric);
        }

        switch (CauseOf(error))
        {
            case ForbiddenCause.MissingCapability:
                return loc.Get(LocKeys.TeamsInviteNoRight);
            case ForbiddenCause.MissingScope:
                return loc.Get(LocKeys.TeamsInviteReconnect);
        }

        return error.Kind switch
        {
            ApiErrorKind.RateLimited => RateLimited(error.RetryAfter, loc),
            ApiErrorKind.BadRequest when !string.IsNullOrWhiteSpace(error.Detail) =>
                loc.Get(LocKeys.TeamsInviteRejected, error.Detail!),
            ApiErrorKind.Forbidden => loc.Get(LocKeys.TeamsErrorForbidden),
            ApiErrorKind.NotFound => loc.Get(LocKeys.TeamsNotMember),
            ApiErrorKind.Unauthorized => loc.Get(LocKeys.TeamsDisabledHint),
            ApiErrorKind.Network => loc.Get(LocKeys.TeamsErrorNetwork),
            _ => loc.Get(LocKeys.TeamsErrorGeneric),
        };
    }

    /// <summary>The wait after too many invitations, in whole minutes, rounded up so it is never too short.</summary>
    private static string RateLimited(TimeSpan? retryAfter, ILocalizer loc)
    {
        if (retryAfter is not { TotalSeconds: > 0 } wait)
        {
            return loc.Get(LocKeys.TeamsInviteRateLimited);
        }

        var minutes = (int)Math.Ceiling(wait.TotalMinutes);
        return loc.Get(minutes == 1 ? LocKeys.TeamsInviteRateLimitedOne : LocKeys.TeamsInviteRateLimitedMany, minutes);
    }
}
