using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the player's <i>owned, equippable</i> items from the game and maps them to the inventory
/// wire model. The <c>character</c> scope is scanned as one snapshot across every locally readable
/// storage (equipped, armoury, bags, saddlebag, glamour dresser) so moving an item between them is
/// harmless; the armoire is intentionally skipped (it holds only non-tradeable seasonal/unique gear
/// you cannot sell, and needs a different, heavier API). Retainer storages are scanned separately,
/// only while a retainer is open. All game-memory access happens on the framework thread (P1) behind
/// logged-in/null guards (P4); every read is wrapped so no exception ever reaches the game (P2).
/// </summary>
public sealed class GameInventorySource : IInventorySource
{
    // Filter to weapons/armour/accessories (and soul crystals): Item.EquipSlotCategory > 0.
    private const int MaxRealItemId = 9_999_999;

    private static readonly InventoryType[] ArmouryTypes =
    [
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
        InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    private static readonly InventoryType[] BagTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] SaddlebagTypes =
    [
        InventoryType.SaddleBag1, InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    ];

    private static readonly InventoryType[] RetainerTypes =
    [
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
        InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6,
        InventoryType.RetainerPage7, InventoryType.RetainerEquippedItems,
    ];

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IDataManager _data;
    private readonly ILog _log;

    /// <summary>Creates the game inventory source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework thread marshaller.</param>
    /// <param name="data">Excel data (for the equippable filter).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameInventorySource(IClientState clientState, IPlayerState playerState, IFramework framework, IDataManager data, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _data = data;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<InventoryData?> ReadCharacterAsync(CancellationToken ct) =>
        _framework.RunOnFrameworkThread(ReadCharacterOnFramework);

    private InventoryData? ReadCharacterOnFramework()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var items = new List<InventoryItemDto>();
            AddContainer(items, InventoryType.EquippedItems, InventoryContainers.Equipped);
            foreach (var t in ArmouryTypes)
            {
                AddContainer(items, t, InventoryContainers.Armoury);
            }

            foreach (var t in BagTypes)
            {
                AddContainer(items, t, InventoryContainers.Bags);
            }

            foreach (var t in SaddlebagTypes)
            {
                AddContainer(items, t, InventoryContainers.Saddlebag);
            }

            AddGlamourDresser(items);

            return new InventoryData
            {
                Character = character,
                Scopes = [InventoryProtocol.ScopeCharacter],
                Items = items,
            };
        }
        catch (Exception ex)
        {
            _log.Error($"Inventory read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// Reads the currently-open retainer's storage as a <c>retainer:&lt;id&gt;</c> snapshot, or
    /// <see langword="null"/> if no retainer inventory is loaded. Must be called on the framework
    /// thread (it is driven from the framework tick). Reports an empty scope when the retainer owns
    /// no equippable items, so selling everything there reconciles on the next visit.
    /// </summary>
    /// <returns>The retainer snapshot, or <see langword="null"/>.</returns>
    public unsafe InventoryData? TryReadActiveRetainer()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var manager = RetainerManager.Instance();
            if (manager == null)
            {
                return null;
            }

            var retainerId = manager->LastSelectedRetainerId;
            if (retainerId == 0)
            {
                return null;
            }

            // Only treat the retainer as "scanned" once its bag is actually loaded (i.e. the player
            // is at the summoning bell), so we never report a stale/empty scope and wipe its items.
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                return null;
            }

            var firstPage = inventory->GetInventoryContainer(InventoryType.RetainerPage1);
            if (firstPage == null || !firstPage->IsLoaded)
            {
                return null;
            }

            var sourceId = retainerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var items = new List<InventoryItemDto>();
            foreach (var t in RetainerTypes)
            {
                AddContainer(items, t, InventoryContainers.Retainer, sourceId);
            }

            return new InventoryData
            {
                Character = character,
                Scopes = [InventoryProtocol.RetainerScope(sourceId)],
                Items = items,
            };
        }
        catch (Exception ex)
        {
            _log.Error($"Retainer read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    private CharacterDto? ReadCharacter()
    {
        if (!_clientState.IsLoggedIn || !_playerState.IsLoaded)
        {
            return null;
        }

        var contentId = _playerState.ContentId;
        if (contentId == 0)
        {
            return null;
        }

        return new CharacterDto
        {
            Name = _playerState.CharacterName,
            World = _playerState.HomeWorld.Value.Name.ExtractText(),
            CidHash = CidHash.Compute(contentId),
        };
    }

    private unsafe void AddContainer(List<InventoryItemDto> items, InventoryType type, string container, string sourceId = "")
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return;
        }

        var c = inventory->GetInventoryContainer(type);
        if (c == null || !c->IsLoaded)
        {
            return;
        }

        for (var i = 0; i < c->Size; i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0)
            {
                continue;
            }

            var id = (int)slot->ItemId;
            if (id is <= 0 or > MaxRealItemId || !IsEquippable(id))
            {
                continue;
            }

            items.Add(new InventoryItemDto
            {
                ItemId = id,
                Container = container,
                SourceId = sourceId,
                Hq = slot->IsHighQuality(),
                Qty = Math.Max(1, slot->Quantity),
            });
        }
    }

    private unsafe void AddGlamourDresser(List<InventoryItemDto> items)
    {
        var mirage = MirageManager.Instance();
        if (mirage == null)
        {
            return;
        }

        var ids = mirage->PrismBoxItemIds;
        for (var i = 0; i < ids.Length; i++)
        {
            var raw = ids[i];
            if (raw == 0)
            {
                continue;
            }

            // Glamour-dresser ids may carry the +1,000,000 HQ offset; normalize to the base id.
            var hq = raw >= 1_000_000;
            var id = (int)(hq ? raw - 1_000_000 : raw);
            if (id is <= 0 or > MaxRealItemId || !IsEquippable(id))
            {
                continue;
            }

            items.Add(new InventoryItemDto
            {
                ItemId = id,
                Container = InventoryContainers.Glamour,
                Hq = hq,
                Qty = 1,
            });
        }
    }

    private bool IsEquippable(int itemId)
    {
        var sheet = _data.GetExcelSheet<LuminaItem>();
        return sheet is not null && sheet.TryGetRow((uint)itemId, out var row) && row.EquipSlotCategory.RowId > 0;
    }
}
