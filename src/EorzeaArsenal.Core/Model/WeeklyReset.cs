namespace EorzeaArsenal.Model;

/// <summary>
/// The FFXIV weekly reset boundary (Tuesday 08:00 UTC) and the timestamp-based decode rules for the
/// weekly-checklist fields whose game state is a raw unix timestamp rather than a simple flag. Pure
/// and time-injectable so the decode logic is unit-tested independently of any game read.
/// </summary>
public static class WeeklyReset
{
    /// <summary>The hour (UTC) at which the weekly reset occurs.</summary>
    private const int ResetHourUtc = 8;

    /// <summary>
    /// The most recent weekly reset at or before <paramref name="now"/> (Tuesday 08:00 UTC).
    /// </summary>
    /// <param name="now">The reference instant.</param>
    /// <returns>The reset boundary, in UTC.</returns>
    public static DateTimeOffset MostRecent(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var todayReset = new DateTimeOffset(utc.Year, utc.Month, utc.Day, ResetHourUtc, 0, 0, TimeSpan.Zero);
        var daysSinceTuesday = ((int)utc.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
        var candidate = todayReset.AddDays(-daysSinceTuesday);

        // Only the "today is Tuesday, before 08:00" case can land in the future — step back a week.
        return candidate <= utc ? candidate : candidate.AddDays(-7);
    }

    /// <summary>The next weekly reset strictly after the one containing <paramref name="now"/>.</summary>
    /// <param name="now">The reference instant.</param>
    /// <returns>The upcoming reset boundary, in UTC.</returns>
    public static DateTimeOffset Next(DateTimeOffset now) => MostRecent(now).AddDays(7);
}

/// <summary>
/// Pure decode rules for the two weekly fields that are backed by unix timestamps in game memory.
/// Kept out of the plugin so they can be unit-tested against known captures.
/// </summary>
public static class WeeklyDecode
{
    /// <summary>A Wondrous Tails journal is complete when all nine stickers are placed.</summary>
    public const int WondrousStickersComplete = 9;

    /// <summary>
    /// Whether the Unreal trial counts as done this week. The Faux Hollows timestamp
    /// (<c>PlayerState.FauxHollowsTimestamp</c>) jumps to a value within the current week when the
    /// Unreal is engaged/cleared, so "≥ the most recent reset" means "done this week".
    /// </summary>
    /// <param name="fauxHollowsTimestamp">The raw Faux Hollows unix timestamp (seconds).</param>
    /// <param name="now">The reference instant.</param>
    /// <returns><see langword="true"/> if the Unreal was done this week.</returns>
    public static bool IsUnrealDone(long fauxHollowsTimestamp, DateTimeOffset now) =>
        fauxHollowsTimestamp > 0 &&
        fauxHollowsTimestamp >= WeeklyReset.MostRecent(now).ToUnixTimeSeconds();

    /// <summary>
    /// Whether Wondrous Tails counts as done this week. A completed book (nine stickers) alone is
    /// ambiguous — the sticker count stays stale after a hand-in and a book is valid for two weeks —
    /// so we anchor on the book's own expiry: a book bought <i>this</i> week expires more than a week
    /// out (its reset + 14 days), while a carried-over book from last week expires at or before the
    /// next reset. Thus "complete AND expiry beyond the next reset" uniquely means "completed a book
    /// bought this week", with no client-side state and no false positive from a stale count.
    /// </summary>
    /// <param name="placedStickers">Stickers placed on the current book (0..9).</param>
    /// <param name="bingoExpireUnixTimestamp">The book's expiry unix timestamp (seconds).</param>
    /// <param name="now">The reference instant.</param>
    /// <returns><see langword="true"/> if a book bought this week is complete.</returns>
    public static bool IsWondrousDone(int placedStickers, long bingoExpireUnixTimestamp, DateTimeOffset now) =>
        placedStickers == WondrousStickersComplete &&
        bingoExpireUnixTimestamp > WeeklyReset.Next(now).ToUnixTimeSeconds();
}
