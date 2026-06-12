using Dalamud.Game.Command;
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
    private readonly ILog _log;

    private readonly HttpClient _httpClient;
    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly GameGearSource _gearSource;
    private readonly ConnectionService _connection;
    private readonly GearSyncService _sync;

    private readonly WindowSystem _windowSystem = new("EorzeaArsenal");
    private readonly ConfigWindow _configWindow;
    private readonly StatusWindow _statusWindow;
    private readonly BisWindow _bisWindow;

    // Framework-tick throttles (Environment.TickCount64 milliseconds).
    private long _nextAutoPushCheckTicks;
    private long _nextSignatureCheckTicks;
    private long _nextCharRecordTicks;
    private ulong _lastSignature;
    private bool _pendingChange;
    private long _changeDebounceUntilTicks;

    /// <summary>Constructs and wires the plugin. Dalamud injects the services.</summary>
    /// <param name="pluginInterface">The Dalamud plugin interface.</param>
    /// <param name="commandManager">Command registration.</param>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework-thread marshaller.</param>
    /// <param name="dataManager">Excel data access.</param>
    /// <param name="log">Plugin log.</param>
    /// <param name="chatGui">Chat output for user feedback.</param>
    /// <param name="toastGui">Toast notifications.</param>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework,
        IDataManager dataManager,
        IPluginLog log,
        IChatGui chatGui,
        IToastGui toastGui)
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

        _log = new PluginLogAdapter(log, () => _config.Verbosity);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        _store = new ConfigStore(_config, Save);
        _localizer = new Localizer(_config.Language);

        var api = new ApiClient(_httpClient, _store);
        _gearSource = new GameGearSource(clientState, playerState, framework, dataManager, _log);
        _connection = new ConnectionService(api, _store, new RealDelay(), _log);
        _sync = new GearSyncService(_gearSource, api, _store, new SystemClock(), _log)
        {
            MinAutoPushInterval = TimeSpan.FromMinutes(Math.Max(1, _config.AutoPushIntervalMinutes)),
        };
        _sync.PushCompleted += OnPushCompleted;

        _bisWindow = new BisWindow(_config, _store, _localizer, api, _gearSource, _log);
        _statusWindow = new StatusWindow(_config, _store, _localizer, _sync, _gearSource, _log, RequestManualPush, OpenConfig, OpenBis);
        _configWindow = new ConfigWindow(_config, _store, _localizer, _connection, api, _log, Save, OpenStatus);
        _windowSystem.AddWindow(_bisWindow);
        _windowSystem.AddWindow(_statusWindow);
        _windowSystem.AddWindow(_configWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
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
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenStatus;
        _windowSystem.RemoveAllWindows();

        _sync.PushCompleted -= OnPushCompleted;
        _sync.Dispose();
        _configWindow.Dispose();
        _httpClient.Dispose();
    }

    private void Save() => _pluginInterface.SavePluginConfig(_config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OpenStatus() => _statusWindow.IsOpen = true;

    private void OpenBis() => _bisWindow.IsOpen = true;

    private void Chat(string message) => _chatGui.Print(ChatPrefix + message);

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

    private void OnLogin()
    {
        RecordCurrentCharacter();
        if (_config is { Enabled: true, TosAccepted: true, PushOnLogin: true } && _store.HasKey && CurrentCharacterAllowed())
        {
            _sync.RequestPush(PushTrigger.Login);
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

        if (_config.PushOnGearsetChange)
        {
            DetectGearsetChange(now);
        }
    }

    private void DetectGearsetChange(long now)
    {
        if (now >= _nextSignatureCheckTicks)
        {
            _nextSignatureCheckTicks = now + 3_000;
            var signature = _gearSource.ComputeGearsetSignature();
            if (signature != 0)
            {
                if (_lastSignature == 0)
                {
                    _lastSignature = signature; // first observation, do not push
                }
                else if (signature != _lastSignature)
                {
                    _lastSignature = signature;
                    _pendingChange = true;
                    _changeDebounceUntilTicks = now + 8_000; // coalesce rapid edits
                }
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
        var message = PushReportFormatter.Describe(report, _localizer);
        if (message is null)
        {
            return; // quiet "skipped" outcomes
        }

        Chat(message);
        if (_config.UseToasts)
        {
            _toastGui.ShowNormal(message);
        }
    }
}
