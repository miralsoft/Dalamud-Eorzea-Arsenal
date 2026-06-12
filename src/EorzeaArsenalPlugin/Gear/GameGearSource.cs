using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
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

            result.Add(new GearsetDto
            {
                GearIndex = i,
                Name = entry->NameString,
                Job = job,
                Items = ReadItems(entry),
            });
        }

        return result;
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

        var sheet = _data.GetExcelSheet<LuminaMateria>();
        if (sheet is null)
        {
            return materia;
        }

        for (var k = 0; k < MateriaSlotCount; k++)
        {
            var type = gearsetItem.Materia[k];
            if (type == 0)
            {
                continue;
            }

            var grade = gearsetItem.MateriaGrades[k];
            if (!sheet.TryGetRow(type, out var row))
            {
                continue;
            }

            if (grade >= row.Item.Count)
            {
                continue;
            }

            var itemId = (int)row.Item[grade].RowId;
            if (itemId > 0)
            {
                materia.Add(itemId);
            }
        }

        return materia;
    }
}
