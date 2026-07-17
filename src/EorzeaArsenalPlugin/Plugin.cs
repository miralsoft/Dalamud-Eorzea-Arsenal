using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Api;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;
using EorzeaArsenal.Plugin.UI;

namespace EorzeaArsenal.Plugin;

/// <summary>
/// Plugin entry point. Deliberately thin (R11): it injects Dalamud services, wires the swappable
/// modules together and owns their lifecycle. All domain logic lives in the core.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivarsenal";
    private const string ChatPrefix = "[Eorzea Arsenal] ";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly LogBuffer _logBuffer;
    private readonly ILog _log;

    private readonly HttpClient _httpClient;
    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly GameGearSource _gearSource;
    private readonly GameInventorySource _inventorySource;
    private readonly GameWeeklySource _weeklySource;
    private readonly ConnectionService _connection;
    private readonly CharacterDirectory _characterDirectory;
    private readonly GearSyncService _sync;
    private readonly InventorySyncService _inventorySync;
    private readonly WeeklySyncService _weeklySync;
    private readonly TeamsService _teamsService;
    private readonly TeamsSeenStore _teamsSeenStore;
    private readonly BisService _bisService;

    private readonly WindowSystem _windowSystem = new("EorzeaArsenal");
    private readonly ConfigWindow _configWindow;
    private readonly StatusWindow _statusWindow;
    private readonly TeamsWindow _teamsWindow;
    private readonly BisWindow _bisWindow;
    private readonly LogWindow _logWindow;
    private readonly PreviewWindow _previewWindow;
    private readonly BisTooltip _bisTooltip;
    private readonly IDtrBarEntry _dtrEntry;

    // Framework-tick throttles (Environment.TickCount64 milliseconds).
    private long _nextAutoPushCheckTicks;
    private long _nextSignatureCheckTicks;
    private long _nextCharRecordTicks;
    private long _nextBisRefreshTicks;
    private long _nextDtrUpdateTicks;
    private long _nextInventoryAutoTicks;
    private long _nextRetainerCheckTicks;
    private long _nextWeeklyAutoTicks;
    private long _nextRaidFinderCheckTicks;
    private bool _raidFinderWasOpen;
    private uint _lastContentsFinderDutyId;
    private long _nextTeamsPollTicks;
    private uint _nextTeamsLinkId = 1;
    private long _hiddenRefreshDueTicks; // 0 = none scheduled
    private long _nextHiddenRefreshTryTicks;
    private long _contentsRefreshDueTicks; // 0 = none scheduled
    private long _nextContentsRefreshTryTicks;
    private string? _lastRetainerScope;
    private bool _bisLoadPending;
    private ulong _lastStoredSig;
    private ulong _lastEquippedItemsSig;
    private ulong _lastEquippedMateriaSig;
    private int _lastGearsetIndex;
    private bool _signaturesInitialized;
    private bool _pendingChange;
    private long _changeDebounceUntilTicks;

    /// <summary>Constructs and wires the plugin. Dalamud injects the services.</summary>
    /// <param name="pluginInterface">The Dalamud plugin interface.</param>
    /// <param name="commandManager">Command registration.</param>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework-thread marshaller.</param>
    /// <param name="condition">Game condition flags (combat/duty guards for the weekly refresh).</param>
    /// <param name="dataManager">Excel data access.</param>
    /// <param name="gameGui">Provides the hovered item id for the BiS overlay.</param>
    /// <param name="textureProvider">Loads game item icons for the BiS window.</param>
    /// <param name="log">Plugin log.</param>
    /// <param name="chatGui">Chat output for user feedback.</param>
    /// <param name="toastGui">Toast notifications.</param>
    /// <param name="dtrBar">The in-game server-info bar (compact status entry).</param>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework,
        ICondition condition,
        IDataManager dataManager,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IPluginLog log,
        IChatGui chatGui,
        IToastGui toastGui,
        IDtrBar dtrBar)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _condition = condition;
        _chatGui = chatGui;
        _toastGui = toastGui;

        _config = pluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        if (_config.Migrate())
        {
            pluginInterface.SavePluginConfig(_config);
        }

        _logBuffer = new LogBuffer();
        _log = new CompositeLog(new PluginLogAdapter(log, () => _config.Verbosity), _logBuffer);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        _store = new ConfigStore(_config, Save);
        _localizer = new Localizer(_config.Language);

        var api = new ApiClient(_httpClient, _store);
        _gearSource = new GameGearSource(clientState, playerState, framework, dataManager, _log);
        _inventorySource = new GameInventorySource(clientState, playerState, framework, dataManager, _log);
        _weeklySource = new GameWeeklySource(clientState, playerState, framework, gameGui, dataManager, _log);
        _connection = new ConnectionService(api, _store, new RealDelay(), _log);

        // Learns cid_hash → server character_id from push responses (persisted); the weekly sync needs
        // that numeric id to address per-character REST paths.
        _characterDirectory = new CharacterDirectory(_config.CharacterIds);
        _characterDirectory.Changed += OnCharacterDirectoryChanged;

        _sync = new GearSyncService(_gearSource, api, _store, new SystemClock(), _log, _characterDirectory)
        {
            MinAutoPushInterval = TimeSpan.FromMinutes(Math.Max(1, _config.AutoPushIntervalMinutes)),
        };
        _sync.PushCompleted += OnPushCompleted;
        _inventorySync = new InventorySyncService(_inventorySource, api, _store, new SystemClock(), _log, _characterDirectory);
        _inventorySync.SyncCompleted += OnInventoryCompleted;
        _weeklySync = new WeeklySyncService(_weeklySource, api, _store, _characterDirectory, new SystemClock(), _log);
        _weeklySync.SyncCompleted += OnWeeklyCompleted;
        _weeklySource.HiddenRefreshCompleted += OnHiddenRefreshCompleted;
        _weeklySource.ContentsRefreshCompleted += OnHiddenRefreshCompleted;
        _teamsSeenStore = new TeamsSeenStore(_config, Save);
        _teamsService = new TeamsService(api, _store, _teamsSeenStore, new SystemClock(), _log);
        _teamsService.Toast += OnTeamToast;
        _bisService = new BisService(api, _gearSource, _store, _log);

        _bisWindow = new BisWindow(_config, _store, _localizer, _bisService, _gearSource, textureProvider, Save, LinkItemInChat);
        _logWindow = new LogWindow(_logBuffer, _localizer);
        _previewWindow = new PreviewWindow(_gearSource, _localizer, _log);
        _statusWindow = new StatusWindow(_config, _store, _localizer, _sync, _inventorySync, _weeklySync, RequestManualPush, RequestInventorySync, RequestWeeklySync, OpenConfig, OpenBis, OpenLog, OpenTeams, OpenPreview);
        _teamsWindow = new TeamsWindow(_config, _store, _localizer, _teamsService, textureProvider, dataManager, playerState, _log, Save, OpenConfig);
        _configWindow = new ConfigWindow(_config, _store, _localizer, _connection, api, _log, Save);
        _bisTooltip = new BisTooltip(_config, _localizer, gameGui, _bisService, _gearSource, _log);
        _windowSystem.AddWindow(_previewWindow);
        _windowSystem.AddWindow(_teamsWindow);
        _windowSystem.AddWindow(_bisWindow);
        _windowSystem.AddWindow(_logWindow);
        _windowSystem.AddWindow(_statusWindow);
        _windowSystem.AddWindow(_configWindow);

        _dtrEntry = dtrBar.Get("Eorzea Arsenal");
        _dtrEntry.OnClick = _ => OpenStatus();
        UpdateDtr();

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw += _bisTooltip.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        _pluginInterface.UiBuilder.OpenMainUi += OpenStatus;
        _clientState.Login += OnLogin;
        _framework.Update += OnFrameworkUpdate;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = _localizer.Get(LocKeys.CommandHelp),
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _clientState.Login -= OnLogin;
        _framework.Update -= OnFrameworkUpdate;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw -= _bisTooltip.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenStatus;
        _windowSystem.RemoveAllWindows();

        _dtrEntry.Remove();
        _sync.PushCompleted -= OnPushCompleted;
        _sync.Dispose();
        _inventorySync.SyncCompleted -= OnInventoryCompleted;
        _inventorySync.Dispose();
        _weeklySource.HiddenRefreshCompleted -= OnHiddenRefreshCompleted;
        _weeklySource.ContentsRefreshCompleted -= OnHiddenRefreshCompleted;
        _weeklySync.SyncCompleted -= OnWeeklyCompleted;
        _weeklySync.Dispose();
        _teamsService.Toast -= OnTeamToast;
        _teamsService.Dispose();
        _teamsWindow.Dispose();
        _chatGui.RemoveChatLinkHandler();
        _characterDirectory.Changed -= OnCharacterDirectoryChanged;
        _configWindow.Dispose();
        _httpClient.Dispose();
    }

    private void Save() => _pluginInterface.SavePluginConfig(_config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OpenStatus() => _statusWindow.IsOpen = true;

    private void OpenBis() => _bisWindow.IsOpen = true;

    private void OpenLog() => _logWindow.IsOpen = true;

    private void OpenPreview() => _previewWindow.Open();

    private void OpenTeams()
    {
        _teamsWindow.IsOpen = true;
        if (_config is { Enabled: true, TosAccepted: true, SyncTeams: true } && _store.HasKey)
        {
            _teamsService.RequestPoll();
        }
    }

    private void Chat(string message) => _chatGui.Print(ChatPrefix + message);

    /// <summary>
    /// Prints a clickable item link to the game chat so the user can inspect it or jump to the
    /// marketboard. The link text is the game's localized item name (P2: wrapped, never throws).
    /// </summary>
    private void LinkItemInChat(int itemId)
    {
        if (itemId <= 0)
        {
            return;
        }

        try
        {
            var message = new SeStringBuilder()
                .AddText(ChatPrefix)
                .AddItemLink((uint)itemId, false)
                .Build();
            _chatGui.Print(message);
        }
        catch (Exception ex)
        {
            _log.Error($"Item link failed: {ex.GetType().Name}.");
        }
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "config":
                OpenConfig();
                break;
            case "menu":
                OpenStatus();
                break;
            case "teams":
                OpenTeams();
                break;
            case "log":
                OpenLog();
                break;
            case "weekdump":
                RunWeeklyProbe();
                break;
            case "weekopen":
                // Temporary experiment: trigger the Raid Finder's own data load, hidden.
                _ = _framework.RunOnFrameworkThread(() => Chat(_weeklySource.TriggerRaidFinderLoad(keepVisible: false)));
                break;
            case "weekopen2":
                // Stage 2: keep the agent alive but suppress the window until the data arrives.
                _ = _framework.RunOnFrameworkThread(() => Chat(_weeklySource.BeginHiddenRefresh()));
                break;
            case "weekshow":
                // Control variant: same trigger but visibly (close manually).
                _ = _framework.RunOnFrameworkThread(() => Chat(_weeklySource.TriggerRaidFinderLoad(keepVisible: true)));
                break;
            case "dutyprobe":
                // Raw probe: load the current normal/alliance raids and read their reward (leaves the
                // window open — kept as a control test).
                _ = _framework.RunOnFrameworkThread(() =>
                {
                    var dump = _weeklySource.ProbeContentsFinderLoad();
                    _log.Info(dump);
                    Chat("Duty-load probe written to the log (/xivarsenal log).");
                });
                break;
            case "dutyrefresh":
                // The real thing: the hidden Duty-Finder refresh — suppress the window, read normal +
                // alliance, close it. Watch the log for "ContentsFinder read (normal=… alliance=…)".
                _ = _framework.RunOnFrameworkThread(() =>
                {
                    _weeklySource.BeginContentsFinderRefresh();
                    Chat("Hidden Duty-Finder refresh started — watch the log (/xivarsenal log).");
                });
                break;
            default:
                RequestManualPush();
                break;
        }
    }

    /// <summary>Triggers a manual push, gated by opt-in, connection and per-character settings.</summary>
    private void RequestManualPush()
    {
        if (!_config.Enabled || !_config.TosAccepted)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.EnablePushMasterHint));
            return;
        }

        if (!_store.HasKey)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.PushNotConnected));
            return;
        }

        RecordCurrentCharacter();
        if (!CurrentCharacterAllowed())
        {
            Chat(_localizer.Get(LocKeys.CharacterDisabled));
            return;
        }

        Chat(_localizer.Get(LocKeys.PushStarted));
        _sync.RequestPush(PushTrigger.Manual);
    }

    /// <summary>Triggers a manual inventory (character-scope) sync, gated like the gear push.</summary>
    private void RequestInventorySync()
    {
        if (!_config.Enabled || !_config.TosAccepted || !_config.SyncInventory)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.EnablePushMasterHint));
            return;
        }

        if (!_store.HasKey)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.PushNotConnected));
            return;
        }

        RecordCurrentCharacter();
        if (!CurrentCharacterAllowed())
        {
            Chat(_localizer.Get(LocKeys.CharacterDisabled));
            return;
        }

        Chat(_localizer.Get(LocKeys.InventoryStarted));
        _inventorySync.RequestCharacterSync(InventoryTrigger.Manual);
    }

    /// <summary>
    /// Developer probe: reads the candidate weekly values via the game's own APIs on the framework
    /// thread and writes them to the diagnostics log so their meaning can be confirmed / re-verified
    /// after a game patch. Triggered by <c>/xivarsenal weekdump</c> (see docs/dev/weekly-data-probing.md).
    /// </summary>
    private void RunWeeklyProbe() => _ = _framework.RunOnFrameworkThread(() =>
    {
        var dump = _weeklySource.ReadRawWeeklyDiagnostics();
        _log.Info(dump);
        Chat("Weekly probe written to the log (open it via the log button / /xivarsenal log).");
    });

    /// <summary>Triggers a manual weekly-checklist sync, gated like the gear push.</summary>
    private void RequestWeeklySync()
    {
        if (!_config.Enabled || !_config.TosAccepted || !_config.SyncWeekly)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.EnablePushMasterHint));
            return;
        }

        if (!_store.HasKey)
        {
            OpenConfig();
            Chat(_localizer.Get(LocKeys.PushNotConnected));
            return;
        }

        RecordCurrentCharacter();
        if (!CurrentCharacterAllowed())
        {
            Chat(_localizer.Get(LocKeys.CharacterDisabled));
            return;
        }

        Chat(_localizer.Get(LocKeys.WeeklyStarted));
        _weeklySync.RequestSync(WeeklyTrigger.Manual);

        // The manual sync reads the current state immediately (tomes/custom/unreal/wondrous), but the
        // Savage and normal/alliance fields live only in the finders — kick off both hidden refreshes
        // too so a button press picks them up as well (their completion fires a follow-up diff-sync).
        _hiddenRefreshDueTicks = Environment.TickCount64;
        _contentsRefreshDueTicks = Environment.TickCount64;
    }

    private void OnLogin()
    {
        // Each login starts a fresh diagnostics log for the new game session.
        _logBuffer.Clear();
        _log.Info("New game session (logged in).");

        RecordCurrentCharacter();
        if (_config is { Enabled: true, TosAccepted: true, PushOnLogin: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _sync.RequestPush(PushTrigger.Login);
        }

        // A new session means no retainer is open and the character may differ — reset the
        // retainer-scan dedup so the next visited retainer is re-scanned.
        _lastRetainerScope = null;

        // Upload owned items once per session start so the web app reflects this character on login.
        if (_config is { Enabled: true, TosAccepted: true, SyncInventory: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _inventorySync.RequestCharacterSync(InventoryTrigger.Login);
        }

        // Sync the weekly checklist on login. If this character's server id isn't known yet, the sync
        // reports NotResolved and retries after the gear push records it (see OnPushCompleted).
        if (_config is { Enabled: true, TosAccepted: true, SyncWeekly: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _weeklySync.RequestSync(WeeklyTrigger.Login);

            // Also fetch the Savage floor state via the invisible Raid-Finder refresh, once the
            // session has settled (the tick performs it when the character is out of combat/duty), then
            // the normal/alliance state via the invisible Duty-Finder refresh (staggered after it).
            _hiddenRefreshDueTicks = Environment.TickCount64 + 10_000;
            _contentsRefreshDueTicks = Environment.TickCount64 + 12_000;
        }

        // Poll the Teams companion once the session settles (soon after login).
        if (_config is { Enabled: true, TosAccepted: true, SyncTeams: true } && _store.HasKey)
        {
            _nextTeamsPollTicks = Environment.TickCount64 + 8_000;
        }

        // Auto-load BiS for the new session so the window/overlay have current data without a manual
        // refresh. The framework tick performs it once the character is fully loaded and gear-readable.
        if (_store.HasKey)
        {
            _bisLoadPending = true;
            _nextBisRefreshTicks = 0;
        }
    }

    /// <summary>
    /// Framework-tick driver for: recording the current character, the throttled auto-push, and
    /// debounced gearset-change detection. All cadence/limits are enforced by the sync service
    /// (R23, P11); this only *requests* pushes. Runs on the framework thread, so game reads here
    /// (the cheap signature) are safe (P1).
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;

        // Hidden-refresh drivers (no-op while idle): the Raid-Finder (Savage) and Duty-Finder
        // (normal/alliance) background reads.
        _weeklySource.PumpHiddenRefresh();
        _weeklySource.PumpContentsFinderRefresh();

        if (now >= _nextDtrUpdateTicks)
        {
            _nextDtrUpdateTicks = now + 5_000;
            UpdateDtr();
        }

        if (now >= _nextCharRecordTicks)
        {
            _nextCharRecordTicks = now + 30_000;
            RecordCurrentCharacter();
        }

        // Teams companion polls the calendar + notifications on an account level (independent of the
        // per-character push opt-in). The service itself throttles to ≥ 5 min and backs off on errors.
        if (_config is { Enabled: true, TosAccepted: true, SyncTeams: true } && _store.HasKey && now >= _nextTeamsPollTicks)
        {
            _nextTeamsPollTicks = now + 300_000;
            _teamsService.RequestPoll();
        }

        if (_config is not { Enabled: true, TosAccepted: true } || !_store.HasKey || !CurrentCharacterAllowed())
        {
            return;
        }

        if (_config.AutoPush && now >= _nextAutoPushCheckTicks)
        {
            _nextAutoPushCheckTicks = now + 60_000;
            _sync.RequestPush(PushTrigger.Auto);
        }

        // Keep the BiS cache warm (reads are cheap: 120/min) for the hover overlay, the open window,
        // or a pending login auto-load.
        var wantBis = _config.ShowBisTooltip || _bisWindow.IsOpen || _bisLoadPending;
        if (wantBis && now >= _nextBisRefreshTicks && _bisService.IsStale(TimeSpan.FromMinutes(5)))
        {
            _nextBisRefreshTicks = now + 60_000;
            _bisLoadPending = false;
            _ = Task.Run(() => _bisService.RefreshAsync(CancellationToken.None));
        }

        if (_config.SyncInventory)
        {
            // Periodic character-scope refresh so sold/looted items reconcile. The service throttles
            // (≥ 15 min) and skips unchanged scans, so this is cheap and never spams the upload budget.
            if (now >= _nextInventoryAutoTicks)
            {
                _nextInventoryAutoTicks = now + 300_000;
                _inventorySync.RequestCharacterSync(InventoryTrigger.Auto);
            }

            if (_config.SyncRetainers)
            {
                DetectOpenRetainer(now);
            }
        }

        if (_config.SyncWeekly && now >= _nextWeeklyAutoTicks)
        {
            // Hourly is ample: the service GETs the server state and sends only changed fields, so an
            // unchanged week costs one read and no write. Refresh the Savage state first (invisible
            // Raid-Finder request); its completion triggers a follow-up diff-sync with fresh floors.
            _nextWeeklyAutoTicks = now + 3_600_000;
            _hiddenRefreshDueTicks = now;
            _contentsRefreshDueTicks = now;
            _weeklySync.RequestSync(WeeklyTrigger.Auto);
        }

        // Run a due hidden Savage refresh once the character is in a safe state (never in combat,
        // in a duty, between areas or in a cutscene); retries every 5s until it can run.
        if (_config.SyncWeekly && _hiddenRefreshDueTicks != 0 && now >= _hiddenRefreshDueTicks && now >= _nextHiddenRefreshTryTicks)
        {
            _nextHiddenRefreshTryTicks = now + 5_000;
            if (CanHiddenRefresh())
            {
                _hiddenRefreshDueTicks = 0;
                _weeklySource.BeginHiddenRefresh();
            }
        }

        // Same for the normal/alliance Duty-Finder refresh; the IsHiddenBusy guard sequences it after
        // the Savage refresh so the two never drive a window at once.
        if (_config.SyncWeekly && _contentsRefreshDueTicks != 0 && now >= _contentsRefreshDueTicks && now >= _nextContentsRefreshTryTicks)
        {
            _nextContentsRefreshTryTicks = now + 5_000;
            if (CanHiddenRefresh() && !_weeklySource.IsHiddenBusy)
            {
                _contentsRefreshDueTicks = 0;
                _weeklySource.BeginContentsFinderRefresh();
            }
        }

        // Opportunistic Savage read: the per-floor weekly-loot state is only in memory while the Raid
        // Finder is open, so sync once each time it opens (edge-triggered; diff-based, so it only
        // writes when a floor actually changed).
        if (_config.SyncWeekly && now >= _nextRaidFinderCheckTicks)
        {
            _nextRaidFinderCheckTicks = now + 2_000;
            var raidFinderOpen = _weeklySource.IsSavageReadable;
            if (raidFinderOpen && !_raidFinderWasOpen)
            {
                _weeklySync.RequestSync(WeeklyTrigger.RaidFinder);
            }

            _raidFinderWasOpen = raidFinderOpen;

            // Same idea for normal/alliance raids: the Duty Finder only exposes the weekly-reward count
            // for the selected duty, so sync each time the user selects a different done normal/alliance
            // raid (diff-based; a stale/unchanged selection or a closed finder writes nothing).
            var dutyId = _weeklySource.SelectedDoneRaidDutyId;
            if (dutyId != 0 && dutyId != _lastContentsFinderDutyId)
            {
                _weeklySync.RequestSync(WeeklyTrigger.RaidFinder);
            }

            _lastContentsFinderDutyId = dutyId;
        }

        if (_config.PushOnGearsetChange)
        {
            DetectGearsetChange(now);
        }
    }

    /// <summary>
    /// Polls (cheaply, on the framework thread) for an open retainer whose bag is loaded and uploads
    /// its <c>retainer:&lt;id&gt;</c> scope once per visit; resets when the retainer is closed so the
    /// next visit re-scans (and reconciles anything sold there).
    /// </summary>
    private void DetectOpenRetainer(long now)
    {
        if (now < _nextRetainerCheckTicks)
        {
            return;
        }

        _nextRetainerCheckTicks = now + 2_000;

        var data = _inventorySource.TryReadActiveRetainer();
        if (data is null || data.Scopes.Count == 0)
        {
            _lastRetainerScope = null; // no retainer open — allow the next visit to re-scan
            return;
        }

        var scope = data.Scopes[0];
        if (scope == _lastRetainerScope)
        {
            return; // already uploaded this retainer for the current visit
        }

        _lastRetainerScope = scope;
        _inventorySync.RequestScopeSync(data);
    }

    private void DetectGearsetChange(long now)
    {
        if (now >= _nextSignatureCheckTicks)
        {
            _nextSignatureCheckTicks = now + 2_000;
            var sig = _gearSource.ComputeSignatures();
            if (sig.StoredGearsets != 0) // 0 = not readable (e.g. not logged in)
            {
                if (!_signaturesInitialized)
                {
                    _signaturesInitialized = true; // first observation, set the baseline only
                }
                else
                {
                    var storedChanged = sig.StoredGearsets != _lastStoredSig;
                    var indexChanged = sig.CurrentGearset != _lastGearsetIndex;
                    var itemsChanged = sig.EquippedItems != _lastEquippedItemsSig;
                    var materiaChanged = sig.EquippedMateria != _lastEquippedMateriaSig;

                    // Push when a gearset is saved, or when materia is socketed on the worn gear
                    // (materia changed, items unchanged, same gearset). Swapping a piece (items
                    // changed — possibly temporary) or merely switching gearsets is ignored.
                    if (storedChanged || (materiaChanged && !itemsChanged && !indexChanged))
                    {
                        _pendingChange = true;
                        _changeDebounceUntilTicks = now + 5_000; // coalesce rapid edits
                    }
                }

                _lastStoredSig = sig.StoredGearsets;
                _lastEquippedItemsSig = sig.EquippedItems;
                _lastEquippedMateriaSig = sig.EquippedMateria;
                _lastGearsetIndex = sig.CurrentGearset;
            }
        }

        if (_pendingChange && now >= _changeDebounceUntilTicks)
        {
            _pendingChange = false;
            _sync.RequestPush(PushTrigger.GearsetChange);
        }
    }

    private void RecordCurrentCharacter()
    {
        if (!_playerState.IsLoaded || _playerState.ContentId == 0)
        {
            return;
        }

        var hash = CidHash.Compute(_playerState.ContentId);
        var world = _playerState.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        if (_config.RecordCharacter(hash, _playerState.CharacterName, world))
        {
            Save();
        }
    }

    private bool CurrentCharacterAllowed()
    {
        if (!_playerState.IsLoaded || _playerState.ContentId == 0)
        {
            return true; // can't determine; the push path reports "not logged in"
        }

        return _config.IsCharacterEnabled(CidHash.Compute(_playerState.ContentId));
    }

    private void OnPushCompleted(PushReport report)
    {
        UpdateDtr();

        // A successful push just recorded this character's server id — sync the weekly checklist now
        // (covers a first-time character whose id was not yet known at login).
        if (report.Outcome == PushOutcome.Sent &&
            _config is { Enabled: true, TosAccepted: true, SyncWeekly: true } && _store.HasKey)
        {
            _weeklySync.RequestSync(WeeklyTrigger.GearPush);
        }

        var message = PushReportFormatter.Describe(report, _localizer);
        if (message is null)
        {
            return; // quiet "skipped" outcomes
        }

        // On failure, append the target server host so a base-URL mismatch (e.g. an old key sent
        // to the wrong server) is immediately obvious. The host is not a secret (R22).
        var chatMessage = report.Outcome == PushOutcome.Failed ? $"{message} ({ServerHost()})" : message;
        Chat(chatMessage);
        if (_config.UseToasts)
        {
            _toastGui.ShowNormal(message);
        }
    }

    /// <summary>Reports inventory upload outcomes to chat/toast; stays quiet for skipped/no-op runs.</summary>
    private void OnInventoryCompleted(InventoryReport report)
    {
        var message = InventoryMessage(report);
        if (message is null)
        {
            return;
        }

        var chatMessage = report.Outcome == InventoryOutcome.Failed ? $"{message} ({ServerHost()})" : message;
        Chat(chatMessage);
        if (_config.UseToasts)
        {
            _toastGui.ShowNormal(message);
        }
    }

    /// <summary>Maps an inventory report to a user message, or <see langword="null"/> to stay quiet.</summary>
    private string? InventoryMessage(InventoryReport report) => report.Outcome switch
    {
        InventoryOutcome.Sent => _localizer.Get(LocKeys.InventorySuccess, report.ItemCount ?? 0, report.ScopeCount ?? 0),
        InventoryOutcome.Failed => InventoryErrorMessage(report.ErrorKind),
        _ => null, // skipped/unchanged/throttled/backoff/not-connected/not-logged-in: no noise
    };

    private string InventoryErrorMessage(ApiErrorKind? kind) => kind switch
    {
        ApiErrorKind.Unauthorized => _localizer.Get(LocKeys.Error401),
        ApiErrorKind.Forbidden => _localizer.Get(LocKeys.Error403Inventory),
        ApiErrorKind.Conflict => _localizer.Get(LocKeys.Error409),
        ApiErrorKind.Validation => _localizer.Get(LocKeys.Error422),
        ApiErrorKind.BadRequest => _localizer.Get(LocKeys.Error400),
        ApiErrorKind.RateLimited => _localizer.Get(LocKeys.Error429),
        ApiErrorKind.Network => _localizer.Get(LocKeys.ErrorNetwork),
        _ => _localizer.Get(LocKeys.ErrorUnexpected),
    };

    /// <summary>Whether the invisible Savage refresh may run right now (safe, idle game state).</summary>
    private bool CanHiddenRefresh() =>
        _store.HasKey
        && CurrentCharacterAllowed()
        && !_condition[ConditionFlag.InCombat]
        && !_condition[ConditionFlag.BoundByDuty]
        && !_condition[ConditionFlag.BetweenAreas]
        && !_condition[ConditionFlag.OccupiedInCutSceneEvent]
        && !_condition[ConditionFlag.WatchingCutscene];

    /// <summary>A hidden refresh (Savage or normal/alliance) delivered fresh data — sync it (diff-based, quiet).</summary>
    private void OnHiddenRefreshCompleted()
    {
        if (_config is { Enabled: true, TosAccepted: true, SyncWeekly: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _weeklySync.RequestSync(WeeklyTrigger.RaidFinder);
        }
    }

    /// <summary>Reports weekly-checklist sync outcomes to chat/toast; stays quiet for skipped/no-op runs.</summary>
    private void OnWeeklyCompleted(WeeklyReport report)
    {
        var message = WeeklyMessage(report);
        if (message is null)
        {
            return;
        }

        var chatMessage = report.Outcome == WeeklyOutcome.Failed ? $"{message} ({ServerHost()})" : message;
        Chat(chatMessage);
        if (_config.UseToasts)
        {
            _toastGui.ShowNormal(message);
        }
    }

    /// <summary>Maps a weekly report to a user message, or <see langword="null"/> to stay quiet.</summary>
    private string? WeeklyMessage(WeeklyReport report) => report.Outcome switch
    {
        WeeklyOutcome.Sent => _localizer.Get(LocKeys.WeeklySuccess, report.FieldCount ?? 0),
        WeeklyOutcome.Failed => WeeklyErrorMessage(report.ErrorKind),
        _ => null, // skipped/unchanged/backoff/not-connected/not-logged-in/not-resolved/nothing: no noise
    };

    private string WeeklyErrorMessage(ApiErrorKind? kind) => kind switch
    {
        ApiErrorKind.Unauthorized => _localizer.Get(LocKeys.Error401),
        ApiErrorKind.Forbidden => _localizer.Get(LocKeys.Error403Weekly),
        ApiErrorKind.Conflict => _localizer.Get(LocKeys.Error409),
        ApiErrorKind.Validation => _localizer.Get(LocKeys.Error422),
        ApiErrorKind.BadRequest => _localizer.Get(LocKeys.Error400),
        ApiErrorKind.RateLimited => _localizer.Get(LocKeys.Error429),
        ApiErrorKind.Network => _localizer.Get(LocKeys.ErrorNetwork),
        _ => _localizer.Get(LocKeys.ErrorUnexpected),
    };

    /// <summary>
    /// A new team notification arrived (loot / reminder / planned event). Shows a game toast with a
    /// sound and prints a clickable chat line that opens the deep link in the browser. Marshals to the
    /// framework thread (game calls); fires only for genuinely-new items (deduped in the service).
    /// </summary>
    private void OnTeamToast(TeamToast toast) => _ = _framework.RunOnFrameworkThread(() => ShowTeamToast(toast));

    private unsafe void ShowTeamToast(TeamToast toast)
    {
        try
        {
            var text = string.IsNullOrEmpty(toast.Body) ? toast.Title : $"{toast.Title}: {toast.Body}";
            if (_config.UseToasts)
            {
                _toastGui.ShowNormal(text);
            }

            try
            {
                UIGlobals.PlaySoundEffect(6); // a soft in-game chime so the toast is noticed
            }
            catch (Exception ex)
            {
                _log.Warning($"Toast sound failed: {ex.GetType().Name}.");
            }

            var url = TeamLinkUrl(toast.Link);
            if (string.IsNullOrEmpty(url))
            {
                Chat(text);
                return;
            }

            // A clickable chat line that opens the deep link (Dalamud toasts themselves aren't clickable).
            var id = _nextTeamsLinkId++;
            var payload = _chatGui.AddChatLinkHandler(id, (_, _) => OpenExternalLink(url));
            var message = new SeStringBuilder()
                .AddText($"{ChatPrefix}{text}  ")
                .Add(payload)
                .AddText($"[{_localizer.Get(LocKeys.OpenWebApp)}]")
                .Add(RawPayload.LinkTerminator)
                .Build();
            _chatGui.Print(message);
        }
        catch (Exception ex)
        {
            _log.Error($"Team toast failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>Turns a relative notification link (<c>/teams/…</c>) into an absolute web-app URL.</summary>
    private string? TeamLinkUrl(string? relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return null;
        }

        var baseUrl = WebAppBase();
        return relative.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relative : baseUrl + relative;
    }

    private string WebAppBase()
    {
        if (!string.IsNullOrWhiteSpace(_config.WebAppUrl))
        {
            return _config.WebAppUrl.Trim().TrimEnd('/');
        }

        var baseUrl = _store.BaseUrl;
        var idx = baseUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? baseUrl[..idx] : baseUrl.TrimEnd('/');
    }

    /// <summary>Opens an absolute http(s) URL in the browser; ignores any other scheme (P8).</summary>
    private void OpenExternalLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Util.OpenLink(url);
        }
    }

    /// <summary>Persists the learned <c>cid_hash → character_id</c> map (best-effort; runs off-thread).</summary>
    private void OnCharacterDirectoryChanged()
    {
        _config.CharacterIds = new Dictionary<string, string>(_characterDirectory.Snapshot(), StringComparer.Ordinal);
        Save();
    }

    private string ServerHost()
    {
        var baseUrl = _store.BaseUrl;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : baseUrl;
    }

    /// <summary>
    /// Refreshes the server-info-bar entry: a compact "Arsenal: &lt;last push&gt;" label (or "off"
    /// when not set up), a tooltip with the full last-push time, and click-to-open the status window.
    /// </summary>
    private void UpdateDtr()
    {
        _dtrEntry.Shown = _config.ShowDtrBar;
        if (!_dtrEntry.Shown)
        {
            return;
        }

        if (!_config.Enabled || !_config.TosAccepted || !_store.HasKey)
        {
            _dtrEntry.Text = "Arsenal: off";
            _dtrEntry.Tooltip = $"{_localizer.Get(LocKeys.StatusDisconnected)}\n{_localizer.Get(LocKeys.DtrClickHint)}";
            return;
        }

        var last = _sync.LastSuccessfulPushUtc;
        var failed = _sync.LastReport?.Outcome == PushOutcome.Failed;
        var when = last is { } t ? RelativeTime(t) : _localizer.Get(LocKeys.StatusNever);

        _dtrEntry.Text = failed ? "Arsenal: !" : $"Arsenal: {when}";
        _dtrEntry.Tooltip = $"{_localizer.Get(LocKeys.StatusLastPush)}: {when}\n{_localizer.Get(LocKeys.DtrClickHint)}";
    }

    /// <summary>Compact relative-age label (e.g. "now", "12s", "5m", "2h", "1d") for the DTR entry.</summary>
    private static string RelativeTime(DateTimeOffset utc)
    {
        var age = DateTimeOffset.UtcNow - utc;
        if (age < TimeSpan.FromSeconds(10))
        {
            return "now";
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return $"{(int)age.TotalSeconds}s";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m";
        }

        return age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
    }
}
