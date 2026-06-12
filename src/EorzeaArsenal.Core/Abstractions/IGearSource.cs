using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Reads the player's gearsets from the game and maps them to the wire model. The concrete
/// implementation lives in the plugin (it touches game memory on the framework thread, P1/P4);
/// tests use a fake/recorded source. This keeps the core free of any game dependency (R8/R9).
/// </summary>
public interface IGearSource
{
    /// <summary>
    /// Whether gear can currently be read (logged in, character present, pointers valid — P4).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads a full snapshot of the current character and all gearsets across all jobs.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The snapshot, or <see langword="null"/> if gear cannot be read right now
    /// (e.g. not logged in). The implementation never throws into the caller (P2).
    /// </returns>
    Task<GearData?> ReadAsync(CancellationToken ct);
}
