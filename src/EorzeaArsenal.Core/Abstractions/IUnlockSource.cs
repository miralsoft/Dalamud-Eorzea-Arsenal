using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>What the game says about a set of unlock items, for one character.</summary>
/// <param name="Character">The logged-in character.</param>
/// <param name="Checked">The items looked at: those that unlock something to learn.</param>
/// <param name="Learned">Those of them whose unlock the character has learned.</param>
public sealed record UnlockSnapshot(CharacterDto Character, IReadOnlyList<int> Checked, IReadOnlyList<int> Learned);

/// <summary>
/// Asks the game whether the character has learned what an item unlocks (a mount, a minion, an orchestrion
/// roll, a card). The concrete implementation lives in the plugin (framework thread, P1); tests use a fake.
/// </summary>
public interface IUnlockSource
{
    /// <summary>Whether a character is logged in and its unlocks can be read (P4).</summary>
    bool IsAvailable { get; }

    /// <summary>Looks at each item.</summary>
    /// <param name="itemIds">The goal items the server lists under <c>unlocks</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot, or <see langword="null"/> when nothing can be read right now. Never throws (P2).</returns>
    Task<UnlockSnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct);
}
