using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Reads the player's <i>owned, equippable</i> items from the game for the <c>character</c> scope
/// — i.e. all locally readable storages together (equipped, armoury, bags, saddlebag, glamour,
/// armoire) in one scan, so it is robust against moving items between them. The concrete
/// implementation lives in the plugin (it touches game memory on the framework thread, P1/P4);
/// retainer scopes are scanned separately, only when a retainer is open. Tests use a fake.
/// </summary>
public interface IInventorySource
{
    /// <summary>Whether owned items can currently be read (logged in, character present — P4).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Scans every locally readable storage and returns them as the single <c>character</c> scope.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The snapshot (scope <c>character</c> + its items, possibly empty to clear a sold-out
    /// storage), or <see langword="null"/> if items cannot be read right now. Never throws (P2).
    /// </returns>
    Task<InventoryData?> ReadCharacterAsync(CancellationToken ct);
}
