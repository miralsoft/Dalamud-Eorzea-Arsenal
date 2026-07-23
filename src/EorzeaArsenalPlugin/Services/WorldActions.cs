using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>
/// Small seam over the two game actions the sourcing UI needs: counting how many of an item the player
/// owns, and pinning a vendor on the in-game map. Both are called from the UI draw (already the main
/// thread), so no extra marshalling is needed; every call is guarded so a null manager or a bad id
/// never throws into the render loop (P2).
/// </summary>
public interface IWorldActions
{
    /// <summary>How many of an item the player owns (bags, equipped, armoury and currency).</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The owned count, or 0 when it cannot be read.</returns>
    int OwnedCount(uint itemId);

    /// <summary>Opens the in-game map at a vendor and drops a flag on it.</summary>
    /// <param name="territoryTypeId">The zone's TerritoryType id.</param>
    /// <param name="mapId">The Map row id.</param>
    /// <param name="x">Friendly map X coordinate.</param>
    /// <param name="y">Friendly map Y coordinate.</param>
    void OpenMap(uint territoryTypeId, uint mapId, float x, float y);
}

/// <inheritdoc cref="IWorldActions"/>
public sealed class WorldActions : IWorldActions
{
    private readonly IGameGui _gameGui;

    /// <summary>Creates the world-actions seam.</summary>
    /// <param name="gameGui">Dalamud game GUI (opens the map).</param>
    public WorldActions(IGameGui gameGui) => _gameGui = gameGui;

    /// <inheritdoc />
    public unsafe int OwnedCount(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId);
        }
        catch
        {
            return 0;
        }
    }

    /// <inheritdoc />
    public void OpenMap(uint territoryTypeId, uint mapId, float x, float y)
    {
        try
        {
            _gameGui.OpenMapWithMapLink(new MapLinkPayload(territoryTypeId, mapId, x, y));
        }
        catch
        {
            // A stale/unknown map ref simply does nothing rather than disturbing the game.
        }
    }
}
