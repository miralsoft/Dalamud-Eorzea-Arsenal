using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Api;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;
using EorzeaArsenal.Plugin.Gear;
using EorzeaArsenal.Plugin.Services;
using EorzeaArsenal.Plugin.UI;

namespace EorzeaArsenal.Plugin;

/// <summary>
/// Plugin entry point. Deliberately thin (R11): it only injects Dalamud services, wires the
/// swappable modules together and owns their lifecycle. All domain logic lives in the core; this
/// type holds none of it.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/bisexport";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly IChatGui _chatGui;
    private readonly ILog _log;

    private readonly HttpClient _httpClient;
    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly ConnectionService _connection;
    private readonly GearSyncService _sync;

    private readonly WindowSystem _windowSystem = new("EorzeaArsenal");
    private readonly ConfigWindow _configWindow;

    /// <summary>Constructs and wires the plugin. Dalamud injects the services.</summary>
    /// <param name="pluginInterface">The Dalamud plugin interface.</param>
    /// <param name="commandManager">Command registration.</param>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework-thread marshaller.</param>
    /// <param name="dataManager">Excel data access.</param>
    /// <param name="log">Plugin log.</param>
    /// <param name="chatGui">Chat output for user feedback.</param>
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework,
        IDataManager dataManager,
        IPluginLog log,
        IChatGui chatGui)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _clientState = clientState;
        _chatGui = chatGui;
        _log = new PluginLogAdapter(log);

        _config = pluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        if (_config.Migrate())
        {
            pluginInterface.SavePluginConfig(_config);
        }

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        _store = new ConfigStore(_config, () => pluginInterface.SavePluginConfig(_config));
        _localizer = new Localizer(_config.Language);

        var api = new ApiClient(_httpClient, _store);
        var gearSource = new GameGearSource(clientState, playerState, framework, dataManager, _log);
        _connection = new ConnectionService(api, _store, new RealDelay(), _log);
        _sync = new GearSyncService(gearSource, api, _store, new SystemClock(), _log)
        {
            MinAutoPushInterval = TimeSpan.FromMinutes(Math.Max(1, _config.AutoPushIntervalMinutes)),
        };
        _sync.PushCompleted += OnPushCompleted;

        _configWindow = new ConfigWindow(_config, _store, _localizer, _connection, api, _log, Save);
        _windowSystem.AddWindow(_configWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        _pluginInterface.UiBuilder.OpenMainUi += OpenConfig;
        _clientState.Login += OnLogin;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Push your gearsets to Eorzea Arsenal. Open settings with no argument issues.",
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _clientState.Login -= OnLogin;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
        _windowSystem.RemoveAllWindows();

        _sync.PushCompleted -= OnPushCompleted;
        _sync.Dispose();
        _configWindow.Dispose();
        _httpClient.Dispose();
    }

    private void Save() => _pluginInterface.SavePluginConfig(_config);

    private void OpenConfig() => _configWindow.IsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        if (!_config.Enabled || !_config.TosAccepted)
        {
            OpenConfig();
            _chatGui.Print($"[Eorzea Arsenal] {_localizer.Get(LocKeys.EnablePushMasterHint)}");
            return;
        }

        if (!_store.HasKey)
        {
            OpenConfig();
            _chatGui.Print($"[Eorzea Arsenal] {_localizer.Get(LocKeys.PushNotConnected)}");
            return;
        }

        _chatGui.Print($"[Eorzea Arsenal] {_localizer.Get(LocKeys.PushStarted)}");
        _sync.RequestPush(PushTrigger.Manual);
    }

    private void OnLogin()
    {
        if (_config is { Enabled: true, TosAccepted: true, PushOnLogin: true } && _store.HasKey)
        {
            _sync.RequestPush(PushTrigger.Login);
        }
    }

    private void OnPushCompleted(PushReport report)
    {
        var message = report.Outcome switch
        {
            PushOutcome.Sent => _localizer.Get(LocKeys.PushSuccess, report.GearsetCount ?? 0),
            PushOutcome.NotConnected => _localizer.Get(LocKeys.PushNotConnected),
            PushOutcome.NotLoggedIn => _localizer.Get(LocKeys.PushNotLoggedIn),
            PushOutcome.Nothing => _localizer.Get(LocKeys.PushNothing),
            PushOutcome.InvalidLocal => _localizer.Get(LocKeys.PushInvalid),
            PushOutcome.Failed => ErrorMessage(report.ErrorKind),
            _ => null, // Skipped* outcomes are quiet to avoid spam.
        };

        if (message is not null)
        {
            _chatGui.Print($"[Eorzea Arsenal] {message}");
        }
    }

    private string ErrorMessage(ApiErrorKind? kind) => kind switch
    {
        ApiErrorKind.Unauthorized => _localizer.Get(LocKeys.Error401),
        ApiErrorKind.Forbidden => _localizer.Get(LocKeys.Error403),
        ApiErrorKind.Conflict => _localizer.Get(LocKeys.Error409),
        ApiErrorKind.Validation => _localizer.Get(LocKeys.Error422),
        ApiErrorKind.BadRequest => _localizer.Get(LocKeys.Error400),
        ApiErrorKind.RateLimited => _localizer.Get(LocKeys.Error429),
        ApiErrorKind.Network => _localizer.Get(LocKeys.ErrorNetwork),
        _ => _localizer.Get(LocKeys.ErrorUnexpected),
    };
}
