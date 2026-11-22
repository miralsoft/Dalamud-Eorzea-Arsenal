using System.Globalization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>Why an absence form cannot be sent as it stands.</summary>
public enum AbsenceInputError
{
    /// <summary>A date is not a date.</summary>
    InvalidDate,

    /// <summary>The end date lies before the start date.</summary>
    RangeInvalid,

    /// <summary>A date lies before yesterday or more than 400 days ahead.</summary>
    OutOfWindow,

    /// <summary>Only one of the two times is set, a time is not one, or both are the same.</summary>
    InvalidTimes,

    /// <summary>The level is none of <c>x</c>, <c>2</c>, <c>1</c>.</summary>
    InvalidLevel,
}

/// <summary>
/// The checks server 1.5 makes on an absence, made here first so the player hears the plugin's own
/// sentence instead of a 400 (R18), plus the wording helpers the absence view needs.
/// </summary>
/// <remarks>
/// <para>
/// Dates run from yesterday to 400 days ahead. The server judges them on its clock and the plugin on the
/// player's, so the window here is the same one and an edge day can still be refused there; the server's
/// answer then says so.
/// </para>
/// <para>
/// Times are both set or both empty (the whole day). The end may be <c>24:00</c>, and an end before the
/// start runs past midnight, so the only pair that is wrong is one with the same two times.
/// </para>
/// </remarks>
public static class AbsenceRules
{
    /// <summary>"Not there", the default: the only level that counts a player out of an event.</summary>
    public const string LevelAway = "x";

    /// <summary>"Can" for those hours.</summary>
    public const string LevelCan = "2";

    /// <summary>"If needed" for those hours.</summary>
    public const string LevelIfNeeded = "1";

    /// <summary>The longest note the server keeps; a longer one is cut there.</summary>
    public const int MaxNote = 160;

    /// <summary>How far ahead an absence may start or end.</summary>
    public const int MaxDaysAhead = 400;

    /// <summary>The levels in the order the form offers them.</summary>
    public static IReadOnlyList<string> Levels { get; } = [LevelAway, LevelCan, LevelIfNeeded];

    /// <summary>
    /// Every half hour of a day as <c>HH:MM</c>; with <paramref name="asEnd"/> also <c>24:00</c>, which only
    /// an end may be.
    /// </summary>
    /// <param name="asEnd">Whether the list is for the end of the hours.</param>
    /// <returns>The half hours, from 00:00.</returns>
    public static IReadOnlyList<string> HalfHours(bool asEnd) => asEnd ? EndHalfHours : StartHalfHours;

    private static readonly string[] StartHalfHours =
        [.. Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{(i % 2) * 30:00}")];

    private static readonly string[] EndHalfHours = [.. StartHalfHours, "24:00"];

    /// <summary>Whether an absence of this level counts the player out of an event. An older server sends no level.</summary>
    /// <param name="level">The absence's level.</param>
    /// <returns><see langword="true"/> for "not there".</returns>
    public static bool CountsAsAway(string? level) => level is null or LevelAway;

    /// <summary>Checks a form before it is sent.</summary>
    /// <param name="fromDate">Start date, <c>YYYY-MM-DD</c>.</param>
    /// <param name="toDate">End date, <c>YYYY-MM-DD</c>.</param>
    /// <param name="fromTime">Start of the hours, or <see langword="null"/> for the whole day.</param>
    /// <param name="toTime">End of the hours, or <see langword="null"/> for the whole day.</param>
    /// <param name="level">The level.</param>
    /// <param name="today">Today on the player's clock.</param>
    /// <returns>The first problem, or <see langword="null"/> when the form can go.</returns>
    public static AbsenceInputError? Validate(
        string fromDate, string toDate, string? fromTime, string? toTime, string level, DateOnly today)
    {
        if (!TryDate(fromDate, out var from) || !TryDate(toDate, out var to))
        {
            return AbsenceInputError.InvalidDate;
        }

        if (to < from)
        {
            return AbsenceInputError.RangeInvalid;
        }

        if (from < today.AddDays(-1) || to > today.AddDays(MaxDaysAhead))
        {
            return AbsenceInputError.OutOfWindow;
        }

        var hasFrom = !string.IsNullOrEmpty(fromTime);
        var hasTo = !string.IsNullOrEmpty(toTime);
        if (hasFrom != hasTo ||
            (hasFrom && (!IsTime(fromTime!, allowEndOfDay: false) || !IsTime(toTime!, allowEndOfDay: true) ||
                         string.Equals(fromTime, toTime, StringComparison.Ordinal))))
        {
            return AbsenceInputError.InvalidTimes;
        }

        return Levels.Contains(level) ? null : AbsenceInputError.InvalidLevel;
    }

    /// <summary>Builds the request body: the note trimmed and cut, the default level and a whole day left out.</summary>
    /// <param name="fromDate">Start date.</param>
    /// <param name="toDate">End date.</param>
    /// <param name="fromTime">Start of the hours, or <see langword="null"/>.</param>
    /// <param name="toTime">End of the hours, or <see langword="null"/>.</param>
    /// <param name="level">The level.</param>
    /// <param name="note">The note as typed.</param>
    /// <param name="withTimesAndLevel">Whether the server takes times and levels (server 1.5).</param>
    /// <returns>The body.</returns>
    public static AbsenceCreateRequest BuildRequest(
        string fromDate, string toDate, string? fromTime, string? toTime, string level, string? note, bool withTimesAndLevel)
    {
        var trimmed = note?.Trim();
        var cut = string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > MaxNote ? trimmed[..MaxNote] : trimmed;
        var timed = withTimesAndLevel && !string.IsNullOrEmpty(fromTime) && !string.IsNullOrEmpty(toTime);
        return new AbsenceCreateRequest
        {
            FromDate = fromDate,
            ToDate = toDate,
            Note = cut,
            FromTime = timed ? fromTime : null,
            ToTime = timed ? toTime : null,
            Level = withTimesAndLevel && level != LevelAway ? level : null,
        };
    }

    /// <summary>Whether the hours of an absence run past midnight (the end before the start).</summary>
    /// <param name="fromTime">Start of the hours.</param>
    /// <param name="toTime">End of the hours.</param>
    /// <returns><see langword="true"/> when the end lies on the next day.</returns>
    public static bool RunsPastMidnight(string? fromTime, string? toTime) =>
        !string.IsNullOrEmpty(fromTime) && !string.IsNullOrEmpty(toTime) &&
        string.CompareOrdinal(toTime, fromTime) < 0;

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool IsTime(string value, bool allowEndOfDay) =>
        (allowEndOfDay && value == "24:00") ||
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
