using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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
    private const string CommandName = "/bisexport";
    private const string ChatPrefix = "[Eorzea Arsenal] ";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
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
    private readonly ConnectionService _connection;
    private readonly GearSyncService _sync;
    private readonly InventorySyncService _inventorySync;
    private readonly BisService _bisService;

    private readonly WindowSystem _windowSystem = new("EorzeaArsenal");
    private readonly ConfigWindow _configWindow;
    private readonly StatusWindow _statusWindow;
    private readonly BisWindow _bisWindow;
    private readonly LogWindow _logWindow;
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
        _connection = new ConnectionService(api, _store, new RealDelay(), _log);
        _sync = new GearSyncService(_gearSource, api, _store, new SystemClock(), _log)
        {
            MinAutoPushInterval = TimeSpan.FromMinutes(Math.Max(1, _config.AutoPushIntervalMinutes)),
        };
        _sync.PushCompleted += OnPushCompleted;
        _inventorySync = new InventorySyncService(_inventorySource, api, _store, new SystemClock(), _log);
        _inventorySync.SyncCompleted += OnInventoryCompleted;
        _bisService = new BisService(api, _gearSource, _store, _log);

        _bisWindow = new BisWindow(_config, _store, _localizer, _bisService, _gearSource, textureProvider, Save, LinkItemInChat);
        _logWindow = new LogWindow(_logBuffer, _localizer);
        _statusWindow = new StatusWindow(_config, _store, _localizer, _sync, _inventorySync, _gearSource, _log, RequestManualPush, RequestInventorySync, OpenConfig, OpenBis, OpenLog);
        _configWindow = new ConfigWindow(_config, _store, _localizer, _connection, api, _log, Save, OpenStatus);
        _bisTooltip = new BisTooltip(_config, _localizer, gameGui, _bisService, _gearSource);
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
        _configWindow.Dispose();
        _httpClient.Dispose();
    }

    private void Save() => _pluginInterface.SavePluginConfig(_config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OpenStatus() => _statusWindow.IsOpen = true;

    private void OpenBis() => _bisWindow.IsOpen = true;

    private void OpenLog() => _logWindow.IsOpen = true;

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
            case "status":
                OpenStatus();
                break;
            case "log":
                OpenLog();
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

        // Upload owned items once per session start so the web app reflects this character on login.
        if (_config is { Enabled: true, TosAccepted: true, SyncInventory: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _inventorySync.RequestCharacterSync(InventoryTrigger.Login);
            _lastRetainerScope = null;
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
