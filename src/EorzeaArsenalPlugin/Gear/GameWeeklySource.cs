using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the player's <i>weekly checklist</i> progress from the game and maps it to the weekly wire
/// model. Only fields that can be read with confidence are set:
/// <list type="bullet">
/// <item><b>tomesHave</b> — weekly-limited tomestones acquired this week (<see cref="InventoryManager"/>).</item>
/// <item><b>custom</b> — Custom Deliveries: reported <i>done</i> only when all weekly allowances are
/// used (<see cref="SatisfactionSupplyManager"/>); a "not done" is never sent, so an un-loaded state
/// or a manual web-app entry is never clobbered (merge is one-directional here on purpose).</item>
/// </list>
/// Everything else (Savage floors, Unreal, Wondrous Tails) is intentionally left unset — its
/// game-state semantics are not reliable enough to risk overwriting a manual entry. All game-memory
/// access happens on the framework thread (P1) behind logged-in/null guards (P4); every read is
/// wrapped so no exception ever reaches the game (P2).
/// </summary>
public sealed class GameWeeklySource : IWeeklySource
{
    // Custom Deliveries grant 12 weekly allowances in total.
    private const int CustomWeeklyAllowances = 12;

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly ILog _log;

    /// <summary>Creates the game weekly source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework thread marshaller.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameWeeklySource(IClientState clientState, IPlayerState playerState, IFramework framework, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<WeeklyData?> ReadAsync(CancellationToken ct) =>
        _framework.RunOnFrameworkThread(ReadOnFramework);

    private WeeklyData? ReadOnFramework()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var values = new WeeklyValues
            {
                TomesHave = ReadWeeklyTomes(),
                Custom = ReadCustomDone(),
            };

            return new WeeklyData { Character = character, Values = values };
        }
        catch (Exception ex)
        {
            _log.Error($"Weekly read failed: {ex.GetType().Name}.");
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

    private unsafe int? ReadWeeklyTomes()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return null;
        }

        var acquired = (int)inventory->GetWeeklyAcquiredTomestoneCount();
        var limit = (int)InventoryManager.GetLimitedTomestoneWeeklyLimit();
        if (limit <= 0)
        {
            limit = WeeklyProtocol.MaxTomes;
        }

        return Math.Clamp(acquired, 0, limit);
    }

    private unsafe bool? ReadCustomDone()
    {
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null)
        {
            return null;
        }

        var remaining = (int)manager->GetRemainingAllowances();
        var used = (int)manager->GetUsedAllowances();

        // Only report a *confident* "done": all weekly allowances used. Never send "not done" — the
        // data may not be loaded yet, and merge must never overwrite a manual web-app entry.
        return used + remaining == CustomWeeklyAllowances && remaining == 0 ? true : null;
    }

    /// <summary>
    /// Temporary diagnostic: reads the accessible Faux-Hollows (Unreal) and Wondrous-Tails state and
    /// returns it for the log, so the "done this week" encoding can be reverse-engineered from known
    /// in-game states before it is trusted. (The Savage weekly loot lockout is <b>not</b> included:
    /// its only candidate field is an internal, single-byte, undocumented value that cannot represent
    /// per-floor loot and is not safely readable.) Read-only; framework thread; never throws (P2).
    /// </summary>
    /// <returns>A human-readable dump of the raw values.</returns>
    public unsafe string ReadRawWeeklyDiagnostics()
    {
        try
        {
            var ps = PlayerState.Instance();
            if (ps == null)
            {
                return "weekdump: PlayerState unavailable.";
            }

            return $"weekdump: fauxState={ps->FauxHollowsState} fauxTs={ps->FauxHollowsTimestamp} " +
                   $"bingoJournal={ps->HasWeeklyBingoJournal} bingoStickers={ps->WeeklyBingoNumPlacedStickers} " +
                   $"bingoSecondChance={ps->WeeklyBingoNumSecondChancePoints}";
        }
        catch (Exception ex)
        {
            _log.Error($"weekdump failed: {ex.GetType().Name}.");
            return "weekdump: read failed.";
        }
    }
}
