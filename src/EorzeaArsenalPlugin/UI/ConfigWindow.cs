using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Api;
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
    private readonly Action _openStatus;

    private string _baseUrl;
    private string _webAppUrl;
    private string _pasteKey = string.Empty;

    private CancellationTokenSource? _deviceFlowCts;
    private volatile DeviceCodeResponse? _deviceCode;
    private volatile string _connectStatus = string.Empty;
    private volatile bool _connecting;
    private volatile string _testStatus = string.Empty;
    private string? _lastCopiedDeviceCode;

    /// <summary>Creates the window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="connection">Connect/disconnect service.</param>
    /// <param name="api">API client (for the test-connection button).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="save">Persists the config.</param>
    /// <param name="openStatus">Callback to open the status window.</param>
    public ConfigWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        ConnectionService connection,
        IApiClient api,
        ILog log,
        Action save,
        Action openStatus)
        : base("Eorzea Arsenal###EorzeaArsenalConfig")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _connection = connection;
        _api = api;
        _log = log;
        _save = save;
        _openStatus = openStatus;
        _baseUrl = config.BaseUrl;
        _webAppUrl = config.WebAppUrl ?? string.Empty;

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

        if (ImGui.Button(T(LocKeys.OpenStatus)))
        {
            _openStatus();
        }

        ImGui.Separator();
        DrawLanguage();
        ImGui.Separator();
        DrawBaseUrl();
        ImGui.Separator();
        DrawConnection();
        ImGui.Separator();
        DrawPushOptions();
        ImGui.Separator();
        DrawExtras();
        ImGui.Separator();
        DrawCharacters();
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
            // Auto-copy the code once when the dialog first shows it (render thread).
            if (_lastCopiedDeviceCode != code.DeviceCode)
            {
                _lastCopiedDeviceCode = code.DeviceCode;
                ImGui.SetClipboardText(code.UserCode);
            }

            ImGui.TextWrapped(T(LocKeys.ConnectOpenBrowser));

            ImGui.TextColored(Yellow, _localizer.Get(LocKeys.ConnectUserCode, code.UserCode));
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            if (ImGui.Button($"{FontAwesomeIcon.Copy.ToIconString()}##copyUserCode"))
            {
                ImGui.SetClipboardText(code.UserCode);
            }

            ImGui.PopFont();

            if (ImGui.Button(T(LocKeys.ConnectOpenBrowserAgain)))
            {
                OpenWeb(CompleteVerificationUri(code));
            }

            ImGui.TextDisabled(code.VerificationUri);
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

        var pushOnChange = _config.PushOnGearsetChange;
        if (ImGui.Checkbox(T(LocKeys.PushOnChange), ref pushOnChange))
        {
            _config.PushOnGearsetChange = pushOnChange;
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.PushOnChangeHint));
    }

    private void DrawExtras()
    {
        var toasts = _config.UseToasts;
        if (ImGui.Checkbox(T(LocKeys.UseToasts), ref toasts))
        {
            _config.UseToasts = toasts;
            _save();
        }

        var bisTooltip = _config.ShowBisTooltip;
        if (ImGui.Checkbox(T(LocKeys.BisTooltipToggle), ref bisTooltip))
        {
            _config.ShowBisTooltip = bisTooltip;
            _save();
        }

        var dtrBar = _config.ShowDtrBar;
        if (ImGui.Checkbox(T(LocKeys.ShowDtrBar), ref dtrBar))
        {
            _config.ShowDtrBar = dtrBar;
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.ShowDtrBarHint));

        var syncInventory = _config.SyncInventory;
        if (ImGui.Checkbox(T(LocKeys.SyncInventory), ref syncInventory))
        {
            _config.SyncInventory = syncInventory;
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.SyncInventoryHint));

        if (_config.SyncInventory)
        {
            ImGui.Indent();
            var syncRetainers = _config.SyncRetainers;
            if (ImGui.Checkbox(T(LocKeys.SyncRetainers), ref syncRetainers))
            {
                _config.SyncRetainers = syncRetainers;
                _save();
            }

            ImGui.TextDisabled(T(LocKeys.SyncRetainersHint));
            ImGui.Unindent();
        }

        var verbosity = (int)_config.Verbosity;
        ReadOnlySpan<string> levels = ["Quiet", "Normal", "Verbose"];
        if (ImGui.Combo(T(LocKeys.Verbosity), ref verbosity, levels, levels.Length))
        {
            _config.Verbosity = (LogVerbosity)verbosity;
            _save();
        }

        ImGui.TextUnformatted(T(LocKeys.WebAppUrlLabel));
        if (ImGui.InputText("##webAppUrl", ref _webAppUrl, 256))
        {
            _config.WebAppUrl = string.IsNullOrWhiteSpace(_webAppUrl) ? null : _webAppUrl.Trim();
            _save();
        }

        ImGui.TextDisabled(T(LocKeys.WebAppUrlHint));
    }

    private void DrawCharacters()
    {
        ImGui.TextUnformatted(T(LocKeys.CharactersHeader));
        ImGui.TextDisabled(T(LocKeys.CharactersHint));

        if (_config.Characters.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.CharactersNone));
            return;
        }

        foreach (var (hash, entry) in _config.Characters)
        {
            var enabled = entry.Enabled;
            if (ImGui.Checkbox($"{entry.Name} — {entry.World}##{hash}", ref enabled))
            {
                entry.Enabled = enabled;
                _save();
            }
        }
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
                if (!result.IsSuccess)
                {
                    _log.Warning($"Test connection failed: {result.Error!.Kind} (HTTP {result.Error.StatusCode}) {result.Error.Endpoint}.");
                    _testStatus = _localizer.Get(LocKeys.TestFailed, result.Error.Message);
                    return;
                }

                _log.Info($"Test connection OK (protocol v{result.Value!.ProtocolVersion}) at {_store.BaseUrl}/version.");
                var ok = _localizer.Get(LocKeys.TestOk, result.Value.ProtocolVersion);
                // R17: if a key is present, warn when it lacks gear:write.
                var scopeOk = !_store.HasKey || ScopeUtil.HasGearWrite(result.Value.Scopes);
                _testStatus = scopeOk ? ok : $"{ok} ⚠ {T(LocKeys.ScopeMissing)}";
            }
            catch (Exception ex)
            {
                _log.Error($"Test connection failed: {ex.GetType().Name}.");
                _testStatus = _localizer.Get(LocKeys.TestFailed, ex.GetType().Name);
            }
        });
    }

    private static string CompleteVerificationUri(DeviceCodeResponse code) =>
        string.IsNullOrEmpty(code.VerificationUriComplete) ? code.VerificationUri : code.VerificationUriComplete;

    /// <summary>
    /// Opens a link in the browser only if it is an <c>http(s)</c> URL. The value comes from the
    /// configured API server's device-code response; never hand an arbitrary scheme to the OS shell.
    /// </summary>
    private static void OpenWeb(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Util.OpenLink(url);
        }
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

                // Open the pre-filled approval page so the user only clicks "Approve".
                // We never approve programmatically — that is the user's explicit action.
                OpenWeb(CompleteVerificationUri(start.Value!));

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
