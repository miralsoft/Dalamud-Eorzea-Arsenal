using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaMateria = Lumina.Excel.Sheets.Materia;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the player's gearsets from <see cref="RaptureGearsetModule"/> and maps them to the wire
/// model. All game-memory access happens on the framework thread (P1) and behind logged-in/null
/// guards (P4); every read is wrapped so no exception ever reaches the game (P2). The pure
/// mapping (jobs, slots, item-id normalization, cid_hash) lives in the core and is unit-tested.
/// </summary>
public sealed class GameGearSource : IGearSource
{
    private const int MaxGearsetSlots = 100;
    private const int EquipmentSlotCount = 14;
    private const int MateriaSlotCount = 5;

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IDataManager _data;
    private readonly ILog _log;

    /// <summary>Creates the game gear source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework thread marshaller.</param>
    /// <param name="data">Excel data (for materia resolution).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameGearSource(IClientState clientState, IPlayerState playerState, IFramework framework, IDataManager data, ILog log)
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
    public Task<GearData?> ReadAsync(CancellationToken ct) =>
        _framework.RunOnFrameworkThread(ReadOnFramework);

    private GearData? ReadOnFramework()
    {
        try
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

            var character = new CharacterDto
            {
                Name = _playerState.CharacterName,
                World = _playerState.HomeWorld.Value.Name.ExtractText(),
                CidHash = CidHash.Compute(contentId),
            };

            return new GearData
            {
                Character = character,
                Gearsets = ReadGearsets(),
            };
        }
        catch (Exception ex)
        {
            // P2: degrade gracefully; never surface into the game.
            _log.Error($"Gear read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    private unsafe List<GearsetDto> ReadGearsets()
    {
        var result = new List<GearsetDto>();

        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return result;
        }

        // The gearset only stores a materia snapshot updated on save. The currently-worn gear has
        // live materia in the EquippedItems container, so for the active gearset we read that
        // instead — materia changes are then sent without needing to re-save the gearset.
        var currentIndex = module->CurrentGearsetIndex;
        var equipped = ReadEquippedItems();

        for (var i = 0; i < MaxGearsetSlots; i++)
        {
            if (!module->IsValidGearset(i))
            {
                continue;
            }

            var entry = module->GetGearset(i);
            if (entry == null)
            {
                continue;
            }

            var job = JobMap.ToCode(entry->ClassJob);
            if (job is null)
            {
                continue; // base classes / non-whitelisted jobs are skipped
            }

            var items = i == currentIndex && equipped.Count > 0 ? equipped : ReadItems(entry);
            result.Add(new GearsetDto
            {
                GearIndex = i,
                Name = entry->NameString,
                Job = job,
                Items = items,
            });
        }

        return result;
    }

    /// <summary>
    /// Computes a cheap fingerprint of all gearsets (job + item ids + materia types only, no Excel
    /// lookups) so the plugin can detect changes on the framework thread without a full read. Must
    /// be called on the framework thread (P1). Returns 0 if gear cannot be read right now.
    /// </summary>
    /// <returns>An FNV-1a hash over the current gearsets, or 0.</returns>
    public unsafe ulong ComputeGearsetSignature()
    {
        try
        {
            if (!_clientState.IsLoggedIn || !_playerState.IsLoaded)
            {
                return 0;
            }

            var module = RaptureGearsetModule.Instance();
            if (module == null)
            {
                return 0;
            }

            var hash = 14695981039346656037UL; // FNV-1a offset basis
            for (var i = 0; i < MaxGearsetSlots; i++)
            {
                if (!module->IsValidGearset(i))
                {
                    continue;
                }

                var entry = module->GetGearset(i);
                if (entry == null)
                {
                    continue;
                }

                hash = Fnv(hash, (ulong)i);
                hash = Fnv(hash, entry->ClassJob);
                for (var slot = 0; slot < EquipmentSlotCount; slot++)
                {
                    ref var item = ref entry->Items[slot];
                    hash = Fnv(hash, item.ItemId);
                    for (var k = 0; k < MateriaSlotCount; k++)
                    {
                        // Hash both the materia type and its grade: melding/overmelding changes
                        // the resolved materia item id, so the signature must cover both.
                        hash = Fnv(hash, item.Materia[k]);
                        hash = Fnv(hash, item.MateriaGrades[k]);
                    }
                }
            }

            // Intentionally based ONLY on the stored gearset entries: they change only when a
            // gearset is saved/updated, so change-detection fires on *saving* a gearset — not on
            // merely switching gearsets or melding without saving. (The push payload still reads
            // live equipped materia for correctness; that is a separate concern.)
            return hash;
        }
        catch (Exception ex)
        {
            // P2: never surface into the game.
            _log.Error($"Gear signature read failed: {ex.GetType().Name}.");
            return 0;
        }
    }

    private static ulong Fnv(ulong hash, ulong value)
    {
        hash ^= value;
        return hash * 1099511628211UL; // FNV-1a prime
    }

    private unsafe Dictionary<string, ItemDto> ReadItems(RaptureGearsetModule.GearsetEntry* entry)
    {
        var items = new Dictionary<string, ItemDto>(StringComparer.Ordinal);

        for (var slot = 0; slot < EquipmentSlotCount; slot++)
        {
            var key = EquipmentSlots.KeyForIndex(slot);
            if (key is null)
            {
                continue;
            }

            ref var gearsetItem = ref entry->Items[slot];
            var id = ItemIdNormalizer.Normalize(gearsetItem.ItemId);
            if (id <= 0)
            {
                continue;
            }

            items[key] = new ItemDto
            {
                Id = id,
                Materia = ReadMateria(ref gearsetItem),
            };
        }

        return items;
    }

    private unsafe List<int> ReadMateria(ref RaptureGearsetModule.GearsetItem gearsetItem)
    {
        var materia = new List<int>();
        for (var k = 0; k < MateriaSlotCount; k++)
        {
            var id = ResolveMateriaId(gearsetItem.Materia[k], gearsetItem.MateriaGrades[k]);
            if (id > 0)
            {
                materia.Add(id);
            }
        }

        return materia;
    }

    /// <summary>
    /// Reads the live equipped items (with up-to-date materia) from the EquippedItems container.
    /// Unlike a gearset, this reflects melds on the worn gear immediately, without a re-save.
    /// </summary>
    private unsafe Dictionary<string, ItemDto> ReadEquippedItems()
    {
        var items = new Dictionary<string, ItemDto>(StringComparer.Ordinal);

        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return items;
        }

        var container = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null)
        {
            return items;
        }

        for (var slot = 0; slot < EquipmentSlotCount; slot++)
        {
            var key = EquipmentSlots.KeyForIndex(slot);
            if (key is null)
            {
                continue;
            }

            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0)
            {
                continue;
            }

            // Equipped items hold the base id (HQ is a flag, not a +1,000,000 offset).
            var id = (int)item->ItemId;
            if (id is <= 0 or > 9_999_999)
            {
                continue;
            }

            items[key] = new ItemDto
            {
                Id = id,
                Materia = ReadMateriaInventory(item),
            };
        }

        return items;
    }

    private unsafe List<int> ReadMateriaInventory(InventoryItem* item)
    {
        var materia = new List<int>();
        for (var k = 0; k < MateriaSlotCount; k++)
        {
            var id = ResolveMateriaId(item->Materia[k], item->MateriaGrades[k]);
            if (id > 0)
            {
                materia.Add(id);
            }
        }

        return materia;
    }

    /// <summary>Resolves a materia (type + grade) to its real item id via the Materia sheet.</summary>
    private int ResolveMateriaId(ushort type, byte grade)
    {
        if (type == 0)
        {
            return 0;
        }

        var sheet = _data.GetExcelSheet<LuminaMateria>();
        if (sheet is null || !sheet.TryGetRow(type, out var row) || grade >= row.Item.Count)
        {
            return 0;
        }

        return (int)row.Item[grade].RowId;
    }

    /// <summary>The currently equipped gearset index, or -1 if unavailable. Read on the main thread.</summary>
    /// <returns>The active gearset index, or -1.</returns>
    public unsafe int GetCurrentGearsetIndex()
    {
        try
        {
            if (!_clientState.IsLoggedIn)
            {
                return -1;
            }

            var module = RaptureGearsetModule.Instance();
            return module == null ? -1 : module->CurrentGearsetIndex;
        }
        catch (Exception ex)
        {
            _log.Error($"Current gearset read failed: {ex.GetType().Name}.");
            return -1;
        }
    }

    /// <summary>Resolves an item id to its display name, or <c>#id</c> if unknown.</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The localized item name.</returns>
    public string GetItemName(int itemId)
    {
        var sheet = _data.GetExcelSheet<LuminaItem>();
        if (sheet is not null && sheet.TryGetRow((uint)itemId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return $"#{itemId}";
    }

    /// <summary>Returns the API slot key(s) an item can be equipped into (empty if not equippable).</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The matching slot keys; rings yield both ring slots.</returns>
    public IReadOnlyList<string> GetSlotsForItem(int itemId)
    {
        var slots = new List<string>();
        var sheet = _data.GetExcelSheet<LuminaItem>();
        if (sheet is null || !sheet.TryGetRow((uint)itemId, out var row) || row.EquipSlotCategory.ValueNullable is not { } category)
        {
            return slots;
        }

        if (category.MainHand > 0)
        {
            slots.Add("Weapon");
        }

        if (category.OffHand > 0)
        {
            slots.Add("OffHand");
        }

        if (category.Head > 0)
        {
            slots.Add("Head");
        }

        if (category.Body > 0)
        {
            slots.Add("Body");
        }

        if (category.Gloves > 0)
        {
            slots.Add("Hands");
        }

        if (category.Legs > 0)
        {
            slots.Add("Legs");
        }

        if (category.Feet > 0)
        {
            slots.Add("Feet");
        }

        if (category.Ears > 0)
        {
            slots.Add("Ears");
        }

        if (category.Neck > 0)
        {
            slots.Add("Neck");
        }

        if (category.Wrists > 0)
        {
            slots.Add("Wrists");
        }

        if (category.FingerL > 0 || category.FingerR > 0)
        {
            slots.Add("RingLeft");
            slots.Add("RingRight");
        }

        return slots;
    }

    /// <summary>Whether the player owns at least one of the item (inventory, armoury or equipped).</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns><see langword="true"/> if owned.</returns>
    public unsafe bool OwnsItem(int itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            return inventory != null && inventory->GetInventoryItemCount((uint)itemId) > 0;
        }
        catch (Exception ex)
        {
            _log.Error($"Ownership check failed: {ex.GetType().Name}.");
            return false;
        }
    }
}
