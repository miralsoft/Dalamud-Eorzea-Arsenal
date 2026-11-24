using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>What the game shows for a set of currencies, for one character.</summary>
/// <param name="Character">The logged-in character.</param>
/// <param name="Counts">The count per requested id; an id the character does not hold reads 0.</param>
public sealed record CurrencySnapshot(CharacterDto Character, IReadOnlyDictionary<int, int> Counts);

/// <summary>
/// Reads the counts of the currencies the server lists. The concrete implementation lives in
/// the plugin and reads the game's currency purse on the framework thread (P1); tests use a fake.
/// </summary>
public interface ICurrencySource
{
    /// <summary>Whether a character is logged in and its purse can be read (P4).</summary>
    bool IsAvailable { get; }

    /// <summary>Reads the count of each id.</summary>
    /// <param name="itemIds">The ids the server lists.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot, or <see langword="null"/> when nothing can be read right now. Never throws (P2).</returns>
    Task<CurrencySnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct);
}
