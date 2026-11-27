using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>Every unlock item of one kind the game has, and those of them the character has learned.</summary>
/// <param name="Items">Every item of the kind.</param>
/// <param name="Learned">Those whose unlock is learned.</param>
public sealed record CollectionKindSnapshot(IReadOnlyList<int> Items, IReadOnlyList<int> Learned);

/// <summary>The collection as the game shows it, for one character.</summary>
/// <param name="Character">The logged-in character.</param>
/// <param name="Kinds">Per kind (<see cref="UnlockKinds"/>) what the game has and what of it is learned.</param>
public sealed record CollectionSnapshot(CharacterDto Character, IReadOnlyDictionary<string, CollectionKindSnapshot> Kinds);

/// <summary>
/// Reads the character's collection of mounts, minions, orchestrion rolls and cards from the game. The
/// concrete implementation lives in the plugin (framework thread, P1); tests use a fake.
/// </summary>
public interface ICollectionSource
{
    /// <summary>Whether a character is logged in and its unlocks can be read (P4).</summary>
    bool IsAvailable { get; }

    /// <summary>Reads the given kinds.</summary>
    /// <param name="kinds">The kinds to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot, or <see langword="null"/> when nothing can be read right now. Never throws (P2).</returns>
    Task<CollectionSnapshot?> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct);
}

/// <summary>What this machine remembers about one kind of a character's collection, kept in the config.</summary>
public sealed class CollectionKindRecord
{
    /// <summary>The UTC day of the last full report, <c>YYYY-MM-DD</c>.</summary>
    public string FullDay { get; set; } = string.Empty;

    /// <summary>
    /// The server's <c>learned</c> after this machine's last report of the kind. The login check compares the
    /// server's count with it: a difference means the website changed something (a tick by hand, "Sammlung
    /// löschen"), which is when a full report is due. Unlike the plugin's own count it does not differ just
    /// because the server has not read a new patch's items yet.
    /// </summary>
    public int ServerLearned { get; set; }

    /// <summary>
    /// The learned item ids the server last accepted from this machine for the kind, after a full report and
    /// every changed-only one. A change is what differs from this, also across sessions and restarts, so only
    /// what really changed goes up. <see langword="null"/> when unknown (a record from before it was kept);
    /// the kind then waits for its next full report.
    /// </summary>
    public List<int>? Learned { get; set; }
}
