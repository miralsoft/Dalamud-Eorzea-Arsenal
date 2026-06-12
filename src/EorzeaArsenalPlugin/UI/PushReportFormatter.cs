using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Maps a <see cref="PushReport"/> to a localized, user-facing message. Shared by the chat
/// feedback and the status window so both speak with one voice (R6).
/// </summary>
public static class PushReportFormatter
{
    /// <summary>Describes a push outcome, or <see langword="null"/> for quiet "skipped" outcomes.</summary>
    /// <param name="report">The push report.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>A localized message, or <see langword="null"/> if the outcome should stay silent.</returns>
    public static string? Describe(PushReport report, ILocalizer loc) => report.Outcome switch
    {
        PushOutcome.Sent => loc.Get(LocKeys.PushSuccess, report.GearsetCount ?? 0),
        PushOutcome.NotConnected => loc.Get(LocKeys.PushNotConnected),
        PushOutcome.NotLoggedIn => loc.Get(LocKeys.PushNotLoggedIn),
        PushOutcome.Nothing => loc.Get(LocKeys.PushNothing),
        PushOutcome.InvalidLocal => loc.Get(LocKeys.PushInvalid),
        PushOutcome.Failed => ErrorMessage(report.ErrorKind, loc),
        _ => null, // Skipped* outcomes stay quiet to avoid spam.
    };

    /// <summary>Maps an API error kind to a localized message.</summary>
    /// <param name="kind">The error kind.</param>
    /// <param name="loc">The localizer.</param>
    /// <returns>A localized error message.</returns>
    public static string ErrorMessage(ApiErrorKind? kind, ILocalizer loc) => kind switch
    {
        ApiErrorKind.Unauthorized => loc.Get(LocKeys.Error401),
        ApiErrorKind.Forbidden => loc.Get(LocKeys.Error403),
        ApiErrorKind.Conflict => loc.Get(LocKeys.Error409),
        ApiErrorKind.Validation => loc.Get(LocKeys.Error422),
        ApiErrorKind.BadRequest => loc.Get(LocKeys.Error400),
        ApiErrorKind.RateLimited => loc.Get(LocKeys.Error429),
        ApiErrorKind.Network => loc.Get(LocKeys.ErrorNetwork),
        _ => loc.Get(LocKeys.ErrorUnexpected),
    };
}
