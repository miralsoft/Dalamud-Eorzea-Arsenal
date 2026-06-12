using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The settings window. Every string is resolved through <see cref="ILocalizer"/> (R6) and it
/// holds no domain logic — it drives <see cref="ConnectionService"/> and <see cref="IApiClient"/>
/// only (R11). Network work runs on background tasks; the draw loop just reflects their state.
/// </summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly ConnectionService _connection;
    private readonly IApiClient _api;
    private readonly ILog _log;
    private readonly Action _save;

    private string _baseUrl;
    private string _pasteKey = string.Empty;

    private CancellationTokenSource? _deviceFlowCts;
    private volatile DeviceCodeResponse? _deviceCode;
    private volatile string _connectStatus = string.Empty;
    private volatile bool _connecting;
    private volatile string _testStatus = string.Empty;

    /// <summary>Creates the window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="connection">Connect/disconnect service.</param>
    /// <param name="api">API client (for the test-connection button).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="save">Persists the config.</param>
    public ConfigWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        ConnectionService connection,
        IApiClient api,
        ILog log,
        Action save)
        : base("Eorzea Arsenal###EorzeaArsenalConfig")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _connection = connection;
        _api = api;
        _log = log;
        _save = save;
        _baseUrl = config.BaseUrl;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 360),
            MaximumSize = new Vector2(900, 1200),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void Draw()
    {
        DrawTosAndMaster();
        ImGui.Separator();

        if (!_config.TosAccepted)
        {
            return; // nothing else is usable until the ToS notice is acknowledged (R36)
        }

        DrawLanguage();
        ImGui.Separator();
        DrawBaseUrl();
        ImGui.Separator();
        DrawConnection();
        ImGui.Separator();
        DrawPushOptions();
    }

    private void DrawTosAndMaster()
    {
        ImGui.TextColored(Yellow, T(LocKeys.TosHeader));
        ImGui.TextWrapped(T(LocKeys.TosBody));

        var accepted = _config.TosAccepted;
        if (ImGui.Checkbox(T(LocKeys.TosAccept), ref accepted))
        {
            _config.TosAccepted = accepted;
            if (!accepted)
            {
                _config.Enabled = false;
            }

            _save();
        }

        var enabled = _config.Enabled;
        if (ImGui.Checkbox(T(LocKeys.EnablePushMaster), ref enabled))
        {
            _config.Enabled = accepted && enabled;
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.EnablePushMasterHint));
    }

    private void DrawLanguage()
    {
        var index = _localizer.Language == Localizer.German ? 1 : 0;
        ReadOnlySpan<string> labels = ["English", "Deutsch"];
        if (ImGui.Combo(T(LocKeys.Language), ref index, labels, labels.Length))
        {
            _localizer.Language = index == 1 ? Localizer.German : Localizer.English;
            _config.Language = _localizer.Language;
            _save();
        }
    }

    private void DrawBaseUrl()
    {
        ImGui.TextUnformatted(T(LocKeys.BaseUrlLabel));
        if (ImGui.InputText("##baseUrl", ref _baseUrl, 256))
        {
            _config.BaseUrl = _baseUrl;
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.BaseUrlHint));

        if (ImGui.Button(T(LocKeys.TestConnection)))
        {
            RunTestConnection();
        }

        if (!string.IsNullOrEmpty(_testStatus))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_testStatus);
        }
    }

    private void DrawConnection()
    {
        var connected = _store.HasKey;
        ImGui.TextColored(connected ? Green : Red, connected ? T(LocKeys.StatusConnected) : T(LocKeys.StatusDisconnected));

        if (connected)
        {
            if (ImGui.Button(T(LocKeys.Disconnect)))
            {
                _connection.Disconnect();
                _connectStatus = string.Empty;
                _deviceCode = null;
            }

            return;
        }

        ImGui.TextUnformatted(T(LocKeys.ConnectHeader));

        using (ImRaii.Disabled(_connecting))
        {
            if (ImGui.Button(T(LocKeys.ConnectDeviceFlow)))
            {
                StartDeviceFlow();
            }
        }

        DrawDeviceFlowState();

        ImGui.Spacing();
        ImGui.TextUnformatted(T(LocKeys.PasteKeyLabel));
        ImGui.InputText("##pasteKey", ref _pasteKey, 512, ImGuiInputTextFlags.Password);
        ImGui.TextDisabled(T(LocKeys.PasteKeyHint));
        if (ImGui.Button(T(LocKeys.PasteKeyButton)))
        {
            if (_connection.ConnectWithPastedKey(_pasteKey))
            {
                _pasteKey = string.Empty;
                _connectStatus = T(LocKeys.ConnectSuccess);
            }
        }
    }

    private void DrawDeviceFlowState()
    {
        var code = _deviceCode;
        if (code is not null)
        {
            ImGui.TextWrapped(T(LocKeys.ConnectOpenBrowser));
            if (ImGui.Button(code.VerificationUri))
            {
                Util.OpenLink(code.VerificationUri);
            }

            ImGui.TextColored(Yellow, _localizer.Get(LocKeys.ConnectUserCode, code.UserCode));
        }

        if (_connecting && ImGui.Button(T(LocKeys.ConnectCancel)))
        {
            _deviceFlowCts?.Cancel();
        }

        if (!string.IsNullOrEmpty(_connectStatus))
        {
            ImGui.TextUnformatted(_connectStatus);
        }
    }

    private void DrawPushOptions()
    {
        var pushOnLogin = _config.PushOnLogin;
        if (ImGui.Checkbox(T(LocKeys.PushOnLogin), ref pushOnLogin))
        {
            _config.PushOnLogin = pushOnLogin;
            _save();
        }

        var autoPush = _config.AutoPush;
        if (ImGui.Checkbox(T(LocKeys.AutoPush), ref autoPush))
        {
            _config.AutoPush = autoPush;
            _save();
        }

        ImGui.TextDisabled(_localizer.Get(LocKeys.AutoPushHint, _config.AutoPushIntervalMinutes));
    }

    private void RunTestConnection()
    {
        _config.BaseUrl = _baseUrl;
        _save();
        _testStatus = "…";

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _api.GetVersionAsync(_store.ApiKey, CancellationToken.None).ConfigureAwait(false);
                _testStatus = result.IsSuccess
                    ? _localizer.Get(LocKeys.TestOk, result.Value!.ProtocolVersion)
                    : _localizer.Get(LocKeys.TestFailed, result.Error!.Message);
            }
            catch (Exception ex)
            {
                _log.Error($"Test connection failed: {ex.GetType().Name}.");
                _testStatus = _localizer.Get(LocKeys.TestFailed, ex.GetType().Name);
            }
        });
    }

    private void StartDeviceFlow()
    {
        _deviceFlowCts?.Cancel();
        _deviceFlowCts?.Dispose();
        _deviceFlowCts = new CancellationTokenSource();
        _connecting = true;
        _connectStatus = string.Empty;
        _deviceCode = null;

        var ct = _deviceFlowCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var start = await _connection.StartDeviceFlowAsync(ct).ConfigureAwait(false);
                if (!start.IsSuccess)
                {
                    _connectStatus = _localizer.Get(LocKeys.ConnectFailed, start.Error!.Message);
                    return;
                }

                _deviceCode = start.Value;
                _connectStatus = T(LocKeys.ConnectWaiting);

                var result = await _connection.PollForKeyAsync(start.Value!, ct).ConfigureAwait(false);
                _connectStatus = result.IsSuccess
                    ? T(LocKeys.ConnectSuccess)
                    : _localizer.Get(LocKeys.ConnectFailed, result.Outcome.ToString());
            }
            catch (Exception ex)
            {
                _log.Error($"Device flow failed: {ex.GetType().Name}.");
                _connectStatus = _localizer.Get(LocKeys.ConnectFailed, ex.GetType().Name);
            }
            finally
            {
                _connecting = false;
                _deviceCode = null;
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _deviceFlowCts?.Cancel();
        _deviceFlowCts?.Dispose();
    }
}
