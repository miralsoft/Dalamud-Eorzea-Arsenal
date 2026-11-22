using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the tomestones and scrips the server lists from the game's currency purse, for the currency sync
/// of server 1.5. Framework thread only (P1); never throws (P2).
/// </summary>
/// <remarks>
/// The count is the one <see cref="InventoryManager.GetInventoryItemCount"/> gives, the call the tome
/// balance has used since 0.6. That it matches the game's currency window for scrips as well is one of
/// the checks of the 1.3.0 test guide.
/// </remarks>
public sealed class GameCurrencySource : ICurrencySource
{
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly ILog _log;

    /// <summary>Creates the source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">The logged-in character.</param>
    /// <param name="framework">Marshals the read onto the framework thread.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameCurrencySource(IClientState clientState, IPlayerState playerState, IFramework framework, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<CurrencySnapshot?> ReadAsync(IReadOnlyList<int> itemIds, CancellationToken ct) =>
        _framework.RunOnFrameworkThread(() => ReadOnFramework(itemIds));

    private unsafe CurrencySnapshot? ReadOnFramework(IReadOnlyList<int> itemIds)
    {
        try
        {
            if (!IsAvailable)
            {
                return null;
            }

            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                return null;
            }

            var character = new CharacterDto
            {
                Name = _playerState.CharacterName,
                World = _playerState.HomeWorld.Value.Name.ExtractText(),
                CidHash = CidHash.Compute(_playerState.ContentId),
            };

            var counts = new Dictionary<int, int>(itemIds.Count);
            foreach (var id in itemIds)
            {
                if (id > 0)
                {
                    counts[id] = Math.Max(0, inventory->GetInventoryItemCount((uint)id));
                }
            }

            return new CurrencySnapshot(character, counts);
        }
        catch (Exception ex)
        {
            _log.Error($"Currency read failed: {ex.GetType().Name}.");
            return null;
        }
    }
}
