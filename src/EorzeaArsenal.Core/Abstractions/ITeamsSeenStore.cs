namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Persists the "already toasted" watermark for the notification feed so toasts fire exactly once —
/// even across relogs/restarts. Notification ids are monotonic (descending in the feed), so a single
/// high-water id is enough: anything with a larger id is new. Backed by the plugin config in
/// production, an in-memory fake in tests (R19/R20 — no secrets here, just an id).
/// </summary>
public interface ITeamsSeenStore
{
    /// <summary>The highest notification id already handled; <c>0</c> before the first fetch.</summary>
    long LastNotificationId { get; set; }

    /// <summary>Persists the current value.</summary>
    void Save();
}
