using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaPlaceName = Lumina.Excel.Sheets.PlaceName;
using LuminaTerritory = Lumina.Excel.Sheets.TerritoryType;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>A resolved place to pin on the map: the zone, its map and the friendly coordinates.</summary>
/// <param name="Territory">TerritoryType id.</param>
/// <param name="Map">Map row id.</param>
/// <param name="X">Friendly map X.</param>
/// <param name="Y">Friendly map Y.</param>
public readonly record struct MapPin(uint Territory, uint Map, float X, float Y);

/// <summary>
/// Small seam over the game actions the sourcing UI needs: counting how many of an item the player
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

    /// <summary>
    /// Resolves a vendor NPC to a map pin: from the server's ids when present, otherwise derived from
    /// whichever it did send (map from territory or vice versa), and finally from the English zone name.
    /// </summary>
    /// <param name="npc">The vendor NPC (needs at least coordinates and a zone name or id).</param>
    /// <returns>The pin, or <see langword="null"/> when it cannot be placed.</returns>
    MapPin? ResolvePin(EorzeaArsenal.Model.FarmNpc npc);

    /// <summary>Opens the in-game map at a pin and drops a flag on it.</summary>
    /// <param name="pin">The resolved pin.</param>
    void OpenMap(MapPin pin);
}

/// <inheritdoc cref="IWorldActions"/>
public sealed class WorldActions : IWorldActions
{
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;

    // English zone name (lower-case) -> (territory, default map). Built once, lazily, so a vendor the
    // server named but did not give ids for can still be placed. Null until first use.
    private Dictionary<string, (uint Territory, uint Map)>? _zoneByName;

    /// <summary>Creates the world-actions seam.</summary>
    /// <param name="gameGui">Dalamud game GUI (opens the map).</param>
    /// <param name="data">Excel data (resolves a zone name to its territory + map).</param>
    public WorldActions(IGameGui gameGui, IDataManager data)
    {
        _gameGui = gameGui;
        _data = data;
    }

    private static readonly InventoryType[] SaddlebagTypes =
    [
        InventoryType.SaddleBag1, InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    ];

    /// <inheritdoc />
    public unsafe int OwnedCount(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                return 0;
            }

            // GetInventoryItemCount covers bags, equipped/armoury and the currency crystal, but not the
            // saddlebag (loaded whenever the game is), so materials stashed there are counted too. NQ
            // and HQ are summed. Retainer stock is not readable unless the retainer is open — see the
            // note in the README on why totals can lag until the next inventory sync.
            var count = inventory->GetInventoryItemCount(itemId) + inventory->GetInventoryItemCount(itemId, isHq: true);
            foreach (var type in SaddlebagTypes)
            {
                var container = inventory->GetInventoryContainer(type);
                if (container == null || !container->IsLoaded)
                {
                    continue;
                }

                for (var i = 0; i < container->Size; i++)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot != null && slot->ItemId == itemId)
                    {
                        count += slot->Quantity;
                    }
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    /// <inheritdoc />
    public MapPin? ResolvePin(EorzeaArsenal.Model.FarmNpc npc)
    {
        if (npc.X is not { } x || npc.Y is not { } y)
        {
            return null;
        }

        try
        {
            var territory = npc.ZoneId is > 0 ? (uint)npc.ZoneId.Value : 0u;
            var map = npc.MapId is > 0 ? (uint)npc.MapId.Value : 0u;

            // Fill in whichever id the server left out — the sheets link the two, no name needed.
            if (map == 0 && territory != 0 && _data.GetExcelSheet<LuminaTerritory>()?.GetRowOrDefault(territory) is { } t)
            {
                map = t.Map.RowId;
            }

            if (territory == 0 && map != 0 && _data.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRowOrDefault(map) is { } m)
            {
                territory = m.TerritoryType.RowId;
            }

            // Last resort: resolve both from the (English) zone name.
            if ((territory == 0 || map == 0) && !string.IsNullOrEmpty(npc.Zone)
                && ZoneIndex().TryGetValue(npc.Zone.ToLowerInvariant(), out var byName))
            {
                (territory, map) = byName;
            }

            return territory != 0 && map != 0 ? new MapPin(territory, map, x, y) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void OpenMap(MapPin pin)
    {
        try
        {
            _gameGui.OpenMapWithMapLink(new MapLinkPayload(pin.Territory, pin.Map, pin.X, pin.Y));
        }
        catch
        {
            // A stale/unknown map ref simply does nothing rather than disturbing the game.
        }
    }

    /// <summary>Builds (once) the English-zone-name → (territory, map) index used as the last-resort resolver.</summary>
    private Dictionary<string, (uint Territory, uint Map)> ZoneIndex()
    {
        if (_zoneByName is not null)
        {
            return _zoneByName;
        }

        var index = new Dictionary<string, (uint, uint)>(StringComparer.Ordinal);
        try
        {
            var territories = _data.GetExcelSheet<LuminaTerritory>();
            var placesEn = _data.GetExcelSheet<LuminaPlaceName>(ClientLanguage.English);
            if (territories is not null && placesEn is not null)
            {
                foreach (var territory in territories)
                {
                    var placeId = territory.PlaceName.RowId;
                    var mapId = territory.Map.RowId;
                    if (placeId == 0 || mapId == 0)
                    {
                        continue;
                    }

                    var name = placesEn.GetRowOrDefault(placeId)?.Name.ExtractText();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    // First territory that carries a given zone name wins (they resolve to the same map).
                    index.TryAdd(name.ToLowerInvariant(), (territory.RowId, mapId));
                }
            }
        }
        catch
        {
            // A resolver that could not be built simply yields no name matches.
        }

        return _zoneByName = index;
    }
}
