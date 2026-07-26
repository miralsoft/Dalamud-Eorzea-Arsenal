using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaDuty = Lumina.Excel.Sheets.ContentFinderCondition;
using LuminaENpc = Lumina.Excel.Sheets.ENpcResident;
using LuminaItem = Lumina.Excel.Sheets.Item;
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

    /// <summary>
    /// The game's own name for a vendor NPC, in the client's language. The server names things in
    /// English; the game holds every language, so anything it can identify by id is shown the way the
    /// player sees it in game.
    /// </summary>
    /// <param name="npcId">The NPC's <c>ENpcResident</c> row id, as the server sends it.</param>
    /// <param name="englishName">The server's English name (kept for the caller's fallback).</param>
    /// <returns>The localized name, or <see langword="null"/> when the id is unknown.</returns>
    string? LocalizedNpcName(long npcId, string? englishName);

    /// <summary>The game's own name for an item, in the client's language.</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The localized name, or <see langword="null"/> when the id is unknown.</returns>
    string? LocalizedItemName(long itemId);

    /// <summary>
    /// The game's own name for a duty, in the client's language. The server sends only the English
    /// name here, so it is matched against the game's English list — an exact hit gives the localized
    /// name, anything else keeps the server's text.
    /// </summary>
    /// <param name="englishName">The duty name as the server sends it.</param>
    /// <returns>The localized name, or <see langword="null"/> when it cannot be matched.</returns>
    string? LocalizedDutyName(string? englishName);

    /// <summary>
    /// The game's own name for a duty, found by its <c>InstanceContent</c> id — the exact way, with no
    /// spelling to match. The id names the instance; the name the player knows lives on the Duty Finder
    /// row that points at it.
    /// </summary>
    /// <param name="instanceContentId">The instance's game id, as the server now sends it.</param>
    /// <returns>The localized name, or <see langword="null"/> when the id is unknown.</returns>
    string? LocalizedDutyNameById(long instanceContentId);

    /// <summary>The game's own name for a zone, in the client's language.</summary>
    /// <param name="territoryId">The territory id, when the server sent one.</param>
    /// <param name="englishName">The server's English zone name (also used to find the zone without an id).</param>
    /// <returns>The localized name, or <see langword="null"/> when it cannot be resolved.</returns>
    string? LocalizedZoneName(long? territoryId, string? englishName);
}

/// <inheritdoc cref="IWorldActions"/>
public sealed class WorldActions : IWorldActions
{
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;

    // English zone name (lower-case) -> (territory, default map). Built once, lazily, so a vendor the
    // server named but did not give ids for can still be placed. Null until first use.
    private Dictionary<string, (uint Territory, uint Map)>? _zoneByName;

    // English duty name (lower-case) -> ContentFinderCondition row. Built once, lazily; the server
    // sends duty names as plain text with no id, so the English name is the only handle we have.
    private Dictionary<string, uint>? _dutyByEnglishName;

    // InstanceContent id -> Duty Finder row. Built once, lazily; the exact path once the server sends
    // the game's own id for a fight.
    private Dictionary<uint, uint>? _dutyByContentId;

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

    /// <inheritdoc />
    public string? LocalizedNpcName(long npcId, string? englishName)
    {
        if (npcId <= 0 || npcId > uint.MaxValue)
        {
            return null;
        }

        try
        {
            // Confirmed with the server: this is an ENpcResident row, so the lookup is direct — no
            // cross-check against the English name needed, and no second sheet read per vendor.
            _ = englishName;
            var localized = _data.GetExcelSheet<LuminaENpc>()?.GetRowOrDefault((uint)npcId)?.Singular.ExtractText();
            return string.IsNullOrEmpty(localized) ? null : localized;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? LocalizedItemName(long itemId)
    {
        if (itemId is <= 0 or > uint.MaxValue)
        {
            return null;
        }

        try
        {
            var name = _data.GetExcelSheet<LuminaItem>()?.GetRowOrDefault((uint)itemId)?.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? LocalizedDutyName(string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
        {
            return null;
        }

        try
        {
            return DutyIndex().TryGetValue(englishName.Trim().ToLowerInvariant(), out var rowId)
                && _data.GetExcelSheet<LuminaDuty>()?.GetRowOrDefault(rowId)?.Name.ExtractText() is { Length: > 0 } name
                ? name
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? LocalizedDutyNameById(long instanceContentId)
    {
        if (instanceContentId is <= 0 or > uint.MaxValue)
        {
            return null;
        }

        try
        {
            return DutyByContent().TryGetValue((uint)instanceContentId, out var rowId)
                && _data.GetExcelSheet<LuminaDuty>()?.GetRowOrDefault(rowId)?.Name.ExtractText() is { Length: > 0 } name
                ? name
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds (once) the <c>InstanceContent</c> id → Duty Finder row index. The server sends the
    /// instance id, but the name a player recognises sits on the Duty Finder row pointing at it.
    /// </summary>
    private Dictionary<uint, uint> DutyByContent()
    {
        if (_dutyByContentId is not null)
        {
            return _dutyByContentId;
        }

        var index = new Dictionary<uint, uint>();
        try
        {
            if (_data.GetExcelSheet<LuminaDuty>() is { } duties)
            {
                foreach (var duty in duties)
                {
                    var contentId = duty.Content.RowId;
                    if (contentId != 0)
                    {
                        index.TryAdd(contentId, duty.RowId);
                    }
                }
            }
        }
        catch
        {
            // Without the index the English name simply stays.
        }

        _dutyByContentId = index;
        return index;
    }

    /// <summary>Builds (once) the English-duty-name → row index used to localize the server's duty text.</summary>
    private Dictionary<string, uint> DutyIndex()
    {
        if (_dutyByEnglishName is not null)
        {
            return _dutyByEnglishName;
        }

        var index = new Dictionary<string, uint>(StringComparer.Ordinal);
        try
        {
            if (_data.GetExcelSheet<LuminaDuty>(ClientLanguage.English) is { } duties)
            {
                foreach (var duty in duties)
                {
                    var name = duty.Name.ExtractText();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        index[name.Trim().ToLowerInvariant()] = duty.RowId;
                    }
                }
            }
        }
        catch
        {
            // A missing sheet just means the server's English names stay as they are.
        }

        _dutyByEnglishName = index;
        return index;
    }

    /// <inheritdoc />
    public string? LocalizedZoneName(long? territoryId, string? englishName)
    {
        try
        {
            var id = territoryId is > 0 and <= uint.MaxValue ? (uint)territoryId.Value : 0u;

            // Three ways to the same name, tried in order of certainty. The id's meaning is the
            // server's to define and has been a territory so far, but a zone id that turns out to be a
            // place name — or a row without a name on it — must not cost the player their language.
            return NameOfTerritory(id)
                ?? NameOfPlace(id)
                ?? NameOfTerritory(TerritoryForEnglishName(englishName));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The localized zone name behind a <c>TerritoryType</c> row.</summary>
    private string? NameOfTerritory(uint territoryId)
    {
        if (territoryId == 0 || _data.GetExcelSheet<LuminaTerritory>()?.GetRowOrDefault(territoryId) is not { } row)
        {
            return null;
        }

        return NameOfPlace(row.PlaceName.RowId);
    }

    /// <summary>The localized name on a <c>PlaceName</c> row.</summary>
    private string? NameOfPlace(uint placeId)
    {
        if (placeId == 0)
        {
            return null;
        }

        var name = _data.GetExcelSheet<LuminaPlaceName>()?.GetRowOrDefault(placeId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>The territory an English zone name belongs to — the same index the map pin uses.</summary>
    private uint TerritoryForEnglishName(string? englishName) =>
        !string.IsNullOrEmpty(englishName) && ZoneIndex().TryGetValue(englishName.Trim().ToLowerInvariant(), out var hit)
            ? hit.Territory
            : 0u;

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
