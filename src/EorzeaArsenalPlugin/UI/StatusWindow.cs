using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The main/status window: connection state, the last push outcome, a rate-limit countdown, and
/// quick actions (push now, preview, open web app, open settings). Holds no domain logic (R11) —
/// it reflects <see cref="GearSyncService"/> state and triggers callbacks. All strings via the
/// localizer (R6).
/// </summary>
public sealed class StatusWindow : Window
{
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(0.9f, 0.8f, 0.3f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly GearSyncService _sync;
    private readonly IGearSource _gearSource;
    private readonly ILog _log;
    private readonly Action _requestManualPush;
    private readonly Action _openConfig;
    private readonly Action _openBis;
    private readonly Action _openLog;

    private volatile string[] _previewLines = [];
    private volatile bool _previewRan;

    /// <summary>Creates the status window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="sync">The sync service whose state is shown.</param>
    /// <param name="gearSource">Gear source (for the preview).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="requestManualPush">Callback to trigger a manual push.</param>
    /// <param name="openConfig">Callback to open the settings window.</param>
    /// <param name="openBis">Callback to open the BiS comparison window.</param>
    /// <param name="openLog">Callback to open the diagnostics log window.</param>
    public StatusWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        GearSyncService sync,
        IGearSource gearSource,
        ILog log,
        Action requestManualPush,
        Action openConfig,
        Action openBis,
        Action openLog)
        : base("Eorzea Arsenal###EorzeaArsenalStatus")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _sync = sync;
        _gearSource = gearSource;
        _log = log;
        _requestManualPush = requestManualPush;
        _openConfig = openConfig;
        _openBis = openBis;
        _openLog = openLog;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 260),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void Draw()
    {
        var connected = _store.HasKey;
        ImGui.TextColored(connected ? Green : Red, connected ? T(LocKeys.StatusConnected) : T(LocKeys.StatusDisconnected));

        DrawLastResult();

        if (_sync.IsRateLimited)
        {
            var seconds = Math.Max(0, (int)(_sync.BackoffUntilUtc - DateTimeOffset.UtcNow).TotalSeconds);
            ImGui.TextColored(Yellow, _localizer.Get(LocKeys.StatusRateLimited, seconds));
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!connected || !_config.Enabled))
        {
            if (ImGui.Button(T(LocKeys.PushNow)))
            {
                _requestManualPush();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.PreviewButton)))
        {
            RunPreview();
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.OpenWebApp)))
        {
            Util.OpenLink(WebUrl());
        }

        if (ImGui.Button(T(LocKeys.BisOpen)))
        {
            _openBis();
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.OpenSettings)))
        {
            _openConfig();
        }

        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        var openLog = ImGui.Button(FontAwesomeIcon.ClipboardList.ToIconString() + "##openLog");
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(T(LocKeys.OpenLog));
        }

        if (openLog)
        {
            _openLog();
        }

        DrawPreview();
    }

    private void DrawLastResult()
    {
        ImGui.TextUnformatted($"{T(LocKeys.StatusLastPush)}: {LastPushText()}");

        if (_sync.LastReport is { } report)
        {
            var text = PushReportFormatter.Describe(report, _localizer) ?? report.Outcome.ToString();
            ImGui.TextWrapped($"{T(LocKeys.StatusLastResult)}: {text}");
            if (!string.IsNullOrEmpty(report.RequestId))
            {
                ImGui.TextDisabled($"request_id: {report.RequestId}");
            }
        }
    }

    private string LastPushText()
    {
        if (_sync.LastSuccessfulPushUtc is not { } last)
        {
            return T(LocKeys.StatusNever);
        }

        var ago = DateTimeOffset.UtcNow - last;
        if (ago < TimeSpan.FromMinutes(1))
        {
            return $"{(int)ago.TotalSeconds}s";
        }

        return ago < TimeSpan.FromHours(1) ? $"{(int)ago.TotalMinutes}m" : $"{(int)ago.TotalHours}h";
    }

    private void DrawPreview()
    {
        if (!_previewRan)
        {
            return;
        }

        ImGui.Separator();
        var lines = _previewLines;
        if (lines.Length == 0)
        {
            ImGui.TextDisabled(T(LocKeys.PreviewEmpty));
            return;
        }

        ImGui.TextUnformatted(_localizer.Get(LocKeys.PreviewHeader, lines.Length));
        foreach (var line in lines)
        {
            ImGui.BulletText(line);
        }
    }

    private void RunPreview()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await _gearSource.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                if (data is null)
                {
                    _previewLines = [];
                    _previewRan = true;
                    return;
                }

                var clean = GearSanitizer.Sanitize(data);
                _previewLines = clean.Gearsets
                    .Select(g => $"#{g.GearIndex} {g.Job}{(string.IsNullOrEmpty(g.Name) ? string.Empty : $" — {g.Name}")} ({g.Items.Count} items)")
                    .ToArray();
                _previewRan = true;
            }
            catch (Exception ex)
            {
                _log.Error($"Preview failed: {ex.GetType().Name}.");
                _previewLines = [];
                _previewRan = true;
            }
        });
    }

    private string WebUrl()
    {
        if (!string.IsNullOrWhiteSpace(_config.WebAppUrl))
        {
            return _config.WebAppUrl.Trim();
        }

        var baseUrl = _store.BaseUrl;
        var idx = baseUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? baseUrl[..idx] : baseUrl;
    }
}
