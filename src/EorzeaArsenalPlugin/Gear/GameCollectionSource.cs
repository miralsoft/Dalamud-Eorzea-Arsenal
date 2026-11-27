using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the character's collection (server 1.5, <c>unlock-collection</c>): every item of the game that unlocks a
/// mount, a minion, an orchestrion roll, a Triple Triad card or (with <c>unlock-kinds</c>) an emote, a hairstyle, a
/// barding, a fashion accessory, facewear or a framer's kit, and whether Dalamud's <see cref="IUnlockState"/>
/// says it is learned. Framework thread only (P1); never throws (P2).
/// </summary>
/// <remarks>
/// The kind is the item's action type in the game data: 1322 mount, 853 minion, 25183 orchestrion roll, 3357
/// card. Counted in the game data of 7.x: 355, 584, 886 and 475 items, which matches what the server's catalog
/// knows. The lists are built once and kept; the learned state is asked fresh at every read.
/// </remarks>
public sealed class GameCollectionSource : ICollectionSource
{
    /// <summary>The action an emote and a hairstyle share, and the message a hairstyle's data names (as the server reads them).</summary>
    private const uint UnlockLink = 2633;
    private const ushort HairstyleMessage = 4659;

    private static readonly Dictionary<uint, string> KindByActionType = new()
    {
        [1322] = UnlockKinds.Mount,
        [853] = UnlockKinds.Minion,
        [25183] = UnlockKinds.Orchestrion,
        [3357] = UnlockKinds.Card,
        [1013] = UnlockKinds.Barding,
        [20086] = UnlockKinds.Accessory,
        [37312] = UnlockKinds.Facewear,
        [29459] = UnlockKinds.Portrait,
    };

    /// <summary>
    /// The kind of an item's unlock, or <see langword="null"/>. An emote and a hairstyle share one action: an emote
    /// names the emote in the third value, a hairstyle names the game's message for a new hairstyle in the second;
    /// anything else of that action is left out, as the server does.
    /// </summary>
    private static string? KindOf(Lumina.Excel.Sheets.ItemAction action)
    {
        var type = action.Action.RowId;
        if (type != UnlockLink)
        {
            return KindByActionType.GetValueOrDefault(type);
        }

        var data = action.Data;
        if (data.Count > 2 && data[2] > 0)
        {
            return UnlockKinds.Emote;
        }

        return data.Count > 1 && data[1] == HairstyleMessage ? UnlockKinds.Hairstyle : null;
    }

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IDataManager _data;
    private readonly IUnlockState _unlocks;
    private readonly ILog _log;
    private Dictionary<string, List<LuminaItem>>? _itemsByKind;

    /// <summary>Creates the source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">The logged-in character.</param>
    /// <param name="framework">Marshals the read onto the framework thread.</param>
    /// <param name="data">The item sheet.</param>
    /// <param name="unlocks">Dalamud's unlock state.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameCollectionSource(IClientState clientState, IPlayerState playerState, IFramework framework, IDataManager data, IUnlockState unlocks, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _data = data;
        _unlocks = unlocks;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<CollectionSnapshot?> ReadAsync(IReadOnlyCollection<string> kinds, CancellationToken ct) =>
        _framework.RunOnFrameworkThread(() => ReadOnFramework(kinds));

    private CollectionSnapshot? ReadOnFramework(IReadOnlyCollection<string> kinds)
    {
        try
        {
            if (!IsAvailable)
            {
                return null;
            }

            var byKind = ItemsByKind();
            var character = new CharacterDto
            {
                Name = _playerState.CharacterName,
                World = _playerState.HomeWorld.Value.Name.ExtractText(),
                CidHash = CidHash.Compute(_playerState.ContentId),
            };

            var result = new Dictionary<string, CollectionKindSnapshot>(StringComparer.Ordinal);
            foreach (var kind in kinds)
            {
                if (!byKind.TryGetValue(kind, out var items))
                {
                    continue;
                }

                var ids = new List<int>(items.Count);
                var learned = new List<int>();
                foreach (var item in items)
                {
                    ids.Add((int)item.RowId);
                    if (_unlocks.IsItemUnlocked(item))
                    {
                        learned.Add((int)item.RowId);
                    }
                }

                result[kind] = new CollectionKindSnapshot(ids, learned);
            }

            return new CollectionSnapshot(character, result);
        }
        catch (Exception ex)
        {
            _log.Error($"Collection read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>Every unlock item of the ten kinds, built once from the item sheet; the server's kinds decide which are sent.</summary>
    private Dictionary<string, List<LuminaItem>> ItemsByKind()
    {
        if (_itemsByKind is not null)
        {
            return _itemsByKind;
        }

        var byKind = UnlockKinds.Extended.ToDictionary(k => k, _ => new List<LuminaItem>(), StringComparer.Ordinal);
        var sheet = _data.GetExcelSheet<LuminaItem>();
        if (sheet is not null)
        {
            foreach (var item in sheet)
            {
                if (item.ItemAction.RowId == 0 || item.ItemAction.ValueNullable is not { } action ||
                    KindOf(action) is not { } kind)
                {
                    continue;
                }

                byKind[kind].Add(item);
            }
        }

        _log.Info($"Collection: the game has {string.Join(", ", byKind.Select(kv => $"{kv.Value.Count} {kv.Key}"))}.");
        _itemsByKind = byKind;
        return byKind;
    }
}
