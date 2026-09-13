using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using EorzeaArsenal.Core;
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
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly PluginConfig _config;
    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly GearSyncService _sync;
    private readonly GearsetMappingService _gearsetMapping;
    private readonly InventorySyncService _inventory;
    private readonly WeeklySyncService _weekly;
    private readonly Action _requestManualPush;
    private readonly Action _requestInventorySync;
    private readonly Action _requestWeeklySync;
    private readonly Action _openConfig;
    private readonly Action _openBis;
    private readonly Action _openAdvisor;
    private readonly Action _openLog;
    private readonly Action _openReview;

    // Preferred over the last push answer, which is a snapshot of the moment it was sent: acting in the
    // review window moves the state without a push, and a badge that keeps announcing finished work is
    // one people learn to ignore.
    private readonly Func<ReviewSummary?> _currentReview;

    /// <summary>How many live gearsets this side cannot tell apart from another of the player's own.</summary>
    private readonly Func<int> _twinCount;

    private readonly Func<string?> _currentCharacter;
    private readonly Action _openReport;
    private readonly Action _openTeams;
    private readonly Action _openCalendar;
    private readonly Action _openPreview;
    private readonly Action _openWhatsNew;

    /// <summary>Creates the status window.</summary>
    /// <param name="config">Live config.</param>
    /// <param name="store">Token/base-URL store.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="sync">The sync service whose state is shown.</param>
    /// <param name="inventory">The inventory sync service (for status + manual sync).</param>
    /// <param name="weekly">The weekly-checklist sync service (for status + manual sync).</param>
    /// <param name="requestManualPush">Callback to trigger a manual push.</param>
    /// <param name="requestInventorySync">Callback to trigger a manual inventory sync.</param>
    /// <param name="requestWeeklySync">Callback to trigger a manual weekly-checklist sync.</param>
    /// <param name="openConfig">Callback to open the settings window.</param>
    /// <param name="openBis">Callback to open the BiS comparison window.</param>
    /// <param name="openAdvisor">Callback to open the purchase-advisor window.</param>
    /// <param name="openLog">Callback to open the diagnostics log window.</param>
    /// <param name="openReview">Opens the reconciliation window. Shown only while something waits.</param>
    /// <param name="currentReview">The freshest reconciliation counts, or <see langword="null"/> when none are known.</param>
    /// <param name="openReport">Callback to open the "report a problem" window.</param>
    /// <param name="openTeams">Callback to open the Teams companion window.</param>
    /// <param name="openCalendar">Callback to open the calendar window.</param>
    /// <param name="openPreview">Callback to open the preview window.</param>
    /// <param name="openWhatsNew">Callback to open the what's-new window.</param>
    /// <param name="gearsetMapping">The gearset identity cache, for the uncertain-match warning.</param>
    /// <param name="twinCount">
    /// How many live gearsets this side declined to identify because another one is indistinguishable
    /// from it. Measured locally: the server has distinct rows and reports no doubt at all.
    /// </param>
    /// <param name="currentCharacter">
    /// Who is logged in, because everything the identity cache reports is about one character and two of
    /// them share this plugin.
    /// </param>
    public StatusWindow(
        PluginConfig config,
        ConfigStore store,
        Localizer localizer,
        GearSyncService sync,
        GearsetMappingService gearsetMapping,
        InventorySyncService inventory,
        WeeklySyncService weekly,
        Action requestManualPush,
        Action requestInventorySync,
        Action requestWeeklySync,
        Action openConfig,
        Action openBis,
        Action openAdvisor,
        Action openLog,
        Action openReview,
        Func<ReviewSummary?> currentReview,
        Action openReport,
        Action openTeams,
        Action openCalendar,
        Action openPreview,
        Action openWhatsNew,
        Func<int> twinCount,
        Func<string?> currentCharacter)
        : base("Eorzea Arsenal###EorzeaArsenalStatus")
    {
        _config = config;
        _store = store;
        _localizer = localizer;
        _sync = sync;
        _gearsetMapping = gearsetMapping;
        _inventory = inventory;
        _weekly = weekly;
        _requestManualPush = requestManualPush;
        _requestInventorySync = requestInventorySync;
        _requestWeeklySync = requestWeeklySync;
        _openConfig = openConfig;
        _openBis = openBis;
        _openAdvisor = openAdvisor;
        _openLog = openLog;
        _openReview = openReview;
        _currentReview = currentReview;
        _openReport = openReport;
        _openTeams = openTeams;
        _openCalendar = openCalendar;
        _openPreview = openPreview;
        _openWhatsNew = openWhatsNew;
        _twinCount = twinCount;
        _currentCharacter = currentCharacter;

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
        var ready = connected && _config.Enabled;

        ImGui.TextColored(connected ? Green : Red, connected ? T(LocKeys.StatusConnected) : T(LocKeys.StatusDisconnected));
        DrawLastResult();

        if (_sync.IsRateLimited)
        {
            var seconds = Math.Max(0, (int)(_sync.BackoffUntilUtc - DateTimeOffset.UtcNow).TotalSeconds);
            ImGui.TextColored(Yellow, _localizer.Get(LocKeys.StatusRateLimited, seconds));
        }

        // The server guessed on at least one gearset. Nothing is broken and no data is at risk, but the
        // comparison may sit on the wrong set. Two rungs, told apart on purpose: a shared name is
        // something the player can end by renaming, a positional match is not, and offering the wrong
        // remedy is worse than offering none. A line, not a dialog.
        var character = _currentCharacter();
        var ambiguous = _gearsetMapping.AmbiguousMatches(character);
        var positional = _gearsetMapping.PositionalMatches(character);
        if (ambiguous > 0 || positional > 0)
        {
            using var wrap = ImRaii.PushColor(ImGuiCol.Text, Yellow);
            if (ambiguous > 0)
            {
                ImGui.TextWrapped(_localizer.Get(LocKeys.StatusGearsetIdentityAmbiguous, ambiguous));
            }

            if (positional > 0)
            {
                ImGui.TextWrapped(_localizer.Get(LocKeys.StatusGearsetIdentityPositional, positional));
            }
        }

        // And the one this side works out for itself. The two above repeat what the server said about its
        // own matching, and the server has distinct rows and reports no doubt; the case where two of the
        // player's own gearsets are indistinguishable here is invisible to it. Without this line the only
        // symptom is a BiS comparison that stays empty for those sets, which reads as a fault rather than
        // as a refusal to guess. It carries the remedy, because there is one.
        var twins = _twinCount();
        if (twins > 0)
        {
            using var wrap = ImRaii.PushColor(ImGuiCol.Text, Yellow);
            ImGui.TextWrapped(_localizer.Get(
                twins == 1 ? LocKeys.StatusGearsetUnattributedOne : LocKeys.StatusGearsetUnattributedMany,
                twins));
        }

        ImGui.Spacing();

        // Not connected yet: point the user straight at the settings to link their account.
        if (!connected)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Dim))
            {
                ImGui.TextWrapped(T(LocKeys.StatusConnectHint));
            }

            ImGui.Spacing();
            if (MenuButton(FontAwesomeIcon.Plug, T(LocKeys.OpenSettings)))
            {
                _openConfig();
            }

            return;
        }

        Section(T(LocKeys.SectionActions));
        if (MenuButton(FontAwesomeIcon.CloudUploadAlt, T(LocKeys.PushNow), ready))
        {
            _requestManualPush();
        }

        if (_config.SyncInventory &&
            MenuButton(FontAwesomeIcon.Boxes, $"{T(LocKeys.InventorySyncButton)}   ·   {LastInventoryText()}", ready))
        {
            _requestInventorySync();
        }

        if (_config.SyncWeekly &&
            MenuButton(FontAwesomeIcon.CalendarCheck, $"{T(LocKeys.WeeklySyncButton)}   ·   {LastWeeklyText()}", ready))
        {
            _requestWeeklySync();
        }

        Section(T(LocKeys.SectionView));
        if (_config.SyncTeams && MenuButton(FontAwesomeIcon.Users, T(LocKeys.TeamsOpen)))
        {
            _openTeams();
        }

        if (_config.SyncTeams && MenuButton(FontAwesomeIcon.CalendarAlt, T(LocKeys.TeamsCalendarOpen)))
        {
            _openCalendar();
        }

        if (MenuButton(FontAwesomeIcon.BalanceScale, T(LocKeys.BisOpen)))
        {
            _openBis();
        }

        // Sits next to, not inside, the BiS entry: BiS is the goal, the advisor is the path to it.
        if (MenuButton(FontAwesomeIcon.ShoppingBasket, T(LocKeys.AdvisorOpen)))
        {
            _openAdvisor();
        }

        if (MenuButton(FontAwesomeIcon.Eye, T(LocKeys.PreviewButton)))
        {
            _openPreview();
        }

        if (MenuButton(FontAwesomeIcon.Globe, T(LocKeys.OpenWebApp)))
        {
            OpenWebApp();
        }

        Section(T(LocKeys.SectionManage));
        if (MenuButton(FontAwesomeIcon.Cog, T(LocKeys.OpenSettings)))
        {
            _openConfig();
        }

        // Highlighted until the user has looked at the notes for the version they are running.
        var unseen = ReleaseNotes.HasUnseen(_config.LastSeenReleaseNotes);
        var whatsNew = unseen ? $"{T(LocKeys.WhatsNewOpen)}   ·   {ReleaseNotes.Latest.Version}" : T(LocKeys.WhatsNewOpen);
        if (MenuButton(FontAwesomeIcon.Gift, whatsNew, accent: unseen ? Yellow : null))
        {
            _openWhatsNew();
        }


        // Shown while something waits, and also while anything is merely archived. A normal player who
        // builds sets in game sees neither, which is the whole point of the mechanism behind it:
        // ambiguity raises a question, unfamiliarity does not.
        //
        // The second half arrived late and it was a real hole. Putting a row aside has an undo,
        // `reopen`, and this entry was the only route into the window: once nothing else was waiting the
        // entry vanished, the window closed itself, and the archive with it. A button whose undo cannot
        // be reached is a one-way door however the contract describes it, which is the same fault
        // `release` had until `released_at` existed.
        //
        // Told apart by colour and by wording, because they are different states. Something waiting is
        // yellow and says how many decisions; an archive is quiet, says how many rows are in it, and is
        // there for whoever goes looking rather than for whoever needs telling.
        if (_currentReview() is { } review && (review.NeedsAttention || (review.Orphans?.Ignored ?? 0) > 0))
        {
            // The sum, not one of the two halves. It used to name the questions and fall back to the rows
            // only when there were no questions at all, so one question beside one orphan read as "one
            // question" and the row behind it was invisible until the window was opened. Naming both was
            // honest and did not fit: the row is one line in a panel and the text ran off it. So the sum,
            // in a word that covers both kinds without borrowing either one's name. Calling an inventory
            // row a "question" here would be the window using two names for two different things, and
            // "decision" is what they already are: one carousel walks them together.
            var waiting = review.Held + (review.Orphans?.Open ?? 0);
            var label = waiting > 0
                ? waiting == 1
                    ? _localizer.Get(LocKeys.ReviewDecisionsOne)
                    : _localizer.Get(LocKeys.ReviewDecisionsMany, waiting)
                : _localizer.Get(LocKeys.ReviewOrphansAside, review.Orphans?.Ignored ?? 0);

            if (MenuButton(
                FontAwesomeIcon.QuestionCircle,
                $"{T(LocKeys.ReviewTitle)}   ·   {label}",
                accent: waiting > 0 ? Yellow : null))
            {
                _openReview();
            }
        }
        if (MenuButton(FontAwesomeIcon.ClipboardList, T(LocKeys.OpenLog)))
        {
            _openLog();
        }

        // Reporting from in game is the whole point — by the time someone has left the instance and
        // found the website, the detail that mattered is gone.
        if (MenuButton(FontAwesomeIcon.Bug, T(LocKeys.ReportOpen)))
        {
            _openReport();
        }
    }

    /// <summary>A dimmed, labelled section separator.</summary>
    private static void Section(string label)
    {
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Dim))
        {
            ImGui.TextUnformatted(label);
        }

        ImGui.Separator();
    }

    /// <summary>A full-width "menu" button with a leading FontAwesome icon and a text label.</summary>
    private bool MenuButton(FontAwesomeIcon icon, string label, bool enabled = true, Vector4? accent = null)
    {
        using var disabled = ImRaii.Disabled(!enabled);

        var height = ImGui.GetFrameHeight() * 1.5f;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.Button($"##menu_{label}", new Vector2(width, height));

        var draw = ImGui.GetWindowDrawList();
        var color = enabled && accent is { } tint
            ? ImGui.GetColorU32(tint)
            : ImGui.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled);
        var midY = origin.Y + (height / 2f);

        ImGui.PushFont(UiBuilder.IconFont);
        var iconStr = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconStr);
        draw.AddText(new Vector2(origin.X + 14f, midY - (iconSize.Y / 2f)), color, iconStr);
        ImGui.PopFont();

        var labelSize = ImGui.CalcTextSize(label);
        draw.AddText(new Vector2(origin.X + 48f, midY - (labelSize.Y / 2f)), color, label);

        return clicked;
    }

    private void OpenWebApp()
    {
        // Only follow http(s) links (the URL is config-derived); never hand the OS shell an
        // arbitrary scheme.
        var url = WebUrl();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Util.OpenLink(url);
        }
    }

    private string LastInventoryText()
    {
        if (_inventory.LastSuccessfulSyncUtc is not { } last)
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

    private string LastWeeklyText()
    {
        if (_weekly.LastSuccessfulSyncUtc is not { } last)
        {
            return T(LocKeys.StatusNever);
        }

        var ago = DateTimeOffset.UtcNow - last;
        if (ago < TimeSpan.FromMinutes(1))
        {
            return $"{(int)ago.TotalSeconds}s";
        }

        if (ago < TimeSpan.FromHours(1))
        {
            return $"{(int)ago.TotalMinutes}m";
        }

        return ago < TimeSpan.FromDays(1) ? $"{(int)ago.TotalHours}h" : $"{(int)ago.TotalDays}d";
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
