using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Asks the game whether the character has learned what a goal item unlocks (server 1.5, goals), through
/// Dalamud's <see cref="IUnlockState"/>: an item is looked at only when the game calls it unlockable (a mount,
/// a minion, an orchestrion roll, a card and the like), and then counts as learned when the game says so.
/// Framework thread only (P1); never throws (P2).
/// </summary>
public sealed class GameUnlockSource : IUnlockSource
{
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IDataManager _data;
    private readonly IUnlockState _unlocks;
    private readonly ILog _log;

    /// <summary>Creates the source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">The logged-in character.</param>
    /// <param name="framework">Marshals the read onto the framework thread.</param>
    /// <param name="data">The item sheet.</param>
    /// <param name="unlocks">Dalamud's unlock state.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameUnlockSource(IClientState clientState, IPlayerState playerState, IFramework framework, IDataManager data, IUnlockState unlocks, ILog log)
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
    public Task<UnlockSnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct) =>
        _framework.RunOnFrameworkThread(() => ReadOnFramework(itemIds));

    private UnlockSnapshot? ReadOnFramework(IReadOnlyList<int> itemIds)
    {
        try
        {
            var items = _data.GetExcelSheet<LuminaItem>();
            if (!IsAvailable || items is null)
            {
                return null;
            }

            var character = new CharacterDto
            {
                Name = _playerState.CharacterName,
                World = _playerState.HomeWorld.Value.Name.ExtractText(),
                CidHash = CidHash.Compute(_playerState.ContentId),
            };

            var looked = new List<int>();
            var learned = new List<int>();
            foreach (var id in itemIds)
            {
                if (id <= 0 || !items.TryGetRow((uint)id, out var row) || !_unlocks.IsItemUnlockable(row))
                {
                    continue;
                }

                looked.Add(id);
                if (_unlocks.IsItemUnlocked(row))
                {
                    learned.Add(id);
                }
            }

            return new UnlockSnapshot(character, looked, learned);
        }
        catch (Exception ex)
        {
            _log.Error($"Unlock read failed: {ex.GetType().Name}.");
            return null;
        }
    }
}
