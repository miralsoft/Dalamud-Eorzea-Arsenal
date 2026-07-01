using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Reads the player's <i>weekly checklist</i> progress from the game (tomestones acquired, weekly
/// content done) for the current character. Only fields the plugin can determine with confidence are
/// returned; the concrete implementation lives in the plugin (it touches game memory on the framework
/// thread, P1/P4) and never throws (P2). Tests use a fake.
/// </summary>
public interface IWeeklySource
{
    /// <summary>Whether weekly values can currently be read (logged in, character present — P4).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads the current character's weekly values.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The read (character + confident values), or <see langword="null"/> if nothing can be read
    /// right now. Never throws (P2).
    /// </returns>
    Task<WeeklyData?> ReadAsync(CancellationToken ct);
}
