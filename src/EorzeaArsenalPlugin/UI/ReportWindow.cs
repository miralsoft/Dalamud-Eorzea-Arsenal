using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Configuration;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// "Report a problem" — a subject, a message, a send button.
/// </summary>
/// <remarks>
/// The point is that a report is written where the problem happened. By the time a player has left
/// the instance and found the website, the detail that mattered is gone. Deliberately small: who is
/// reporting and how to answer them comes from the API key, so there is no name field, no e-mail
/// field and no topic picker to get wrong.
/// <para>
/// Two rules from the server side are kept here. <b>Nothing is sent automatically</b> — never from an
/// exception handler, never collected in the background; a report exists because a person wrote it,
/// and an inbox shared with real players' mail must not fill with machine noise. And <b>a failure is
/// shown</b>: nothing is stored on the way, so a silent "thank you" would be a lie. The text stays in
/// the window so it can simply be sent again.
/// </para>
/// </remarks>
public sealed class ReportWindow : Window
{
    private static readonly Vector4 Muted = new(0.78f, 0.80f, 0.85f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.82f, 0.45f, 1f);
    private static readonly Vector4 Orange = new(0.96f, 0.62f, 0.22f, 1f);

    private readonly ConfigStore _store;
    private readonly Localizer _localizer;
    private readonly IApiClient _api;
    private readonly ILog _log;
    private readonly Func<string?, ContactClient> _describeClient;

    private string _subject = string.Empty;
    private string _message = string.Empty;
    private string? _status;
    private bool _failed;
    private bool _sending;

    // A report opens because something broke, so that is the preselection.
    private string _kind = ContactKinds.Bug;

    // Which topics actually have a channel behind them, and how to reach a human instead. Asked once
    // per opening; until it answers (or if it never does) all four are offered — better to let a
    // switched-off topic fail loudly than to silently offer nothing.
    private IReadOnlyList<string> _kinds = ContactKinds.All;
    private ContactInfo? _info;
    private bool _infoRequested;

    /// <summary>Creates the report window.</summary>
    /// <param name="store">Token store — the key is what identifies the reporter.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="api">HTTP client.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="describeClient">Builds the situation block (character, versions, where).</param>
    public ReportWindow(ConfigStore store, Localizer localizer, IApiClient api, ILog log, Func<string?, ContactClient> describeClient)
        : base("Eorzea Arsenal###EorzeaArsenalReport")
    {
        _store = store;
        _localizer = localizer;
        _api = api;
        _log = log;
        _describeClient = describeClient;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(900, 900),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <summary>Opens the window for a fresh report, noting where the player was.</summary>
    /// <param name="where">The part of the plugin they came from, for the report's context.</param>
    public void Open(string? where = null)
    {
        _where = where;
        _status = null;
        _failed = false;
        IsOpen = true;
        LoadContactInfo();
    }

    private string? _where;

    /// <summary>
    /// Asks once what the inbox currently accepts. A topic that has been switched off would otherwise
    /// still be offered, and the player's report would fail with a 503 through no fault of theirs.
    /// </summary>
    private void LoadContactInfo()
    {
        if (_infoRequested)
        {
            return;
        }

        _infoRequested = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _api.GetContactInfoAsync(_store.ApiKey, CancellationToken.None).ConfigureAwait(false);
                if (!result.IsSuccess || result.Value?.Data is not { } info)
                {
                    return; // unreachable: keep all four and let the error case speak
                }

                _info = info;
                if (info.Kinds is { Count: > 0 } kinds)
                {
                    var open = kinds.Where(ContactKinds.IsKnown).ToList();
                    if (open.Count > 0)
                    {
                        _kinds = open;
                        if (!open.Contains(_kind, StringComparer.Ordinal))
                        {
                            _kind = open[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Info($"Contact info unavailable: {ex.GetType().Name}. Offering all topics.");
            }
        });
    }

    /// <inheritdoc />
    public override void Draw()
    {
        WindowName = $"{T(LocKeys.ReportWindowTitle)}###EorzeaArsenalReport";

        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(T(LocKeys.ReportIntro));
        }

        ImGui.Spacing();

        if (!_store.HasKey)
        {
            ImGui.TextColored(Orange, T(LocKeys.ReportNotConnected));
            return;
        }

        DrawTopics();

        ImGui.Spacing();
        ImGui.TextUnformatted(T(LocKeys.ReportSubject));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##reportSubject", ref _subject, ContactKinds.MaxSubject);

        ImGui.Spacing();
        ImGui.TextUnformatted(T(LocKeys.ReportMessage));
        ImGui.InputTextMultiline(
            "##reportMessage",
            ref _message,
            ContactKinds.MaxMessage,
            new Vector2(-1, ImGui.GetTextLineHeight() * 8f));

        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(T(LocKeys.ReportNoLogs));
        }

        ImGui.Spacing();

        // The server's own limits, checked here so an obvious miss costs no round trip.
        var ready = _subject.Trim().Length > 0 && _message.Trim().Length >= ContactKinds.MinMessage;
        using (ImRaii.Disabled(_sending || !ready))
        {
            if (ImGui.Button(T(LocKeys.ReportSend)))
            {
                Send();
            }
        }

        if (!ready && _message.Trim().Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, _localizer.Get(LocKeys.ReportTooShort, ContactKinds.MinMessage));
        }

        if (_sending)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(T(LocKeys.ReportSending));
        }

        if (_status is not null)
        {
            ImGui.Spacing();
            using var colour = ImRaii.PushColor(ImGuiCol.Text, _failed ? Orange : Green);
            ImGui.TextWrapped(_status);
        }

        // Say what travels with the text — the player should never have to guess what they just sent.
        ImGui.Spacing();
        ImGui.TextDisabled(_localizer.Get(LocKeys.ReportContext, Describe()));

        DrawDirectContact();
    }

    /// <summary>
    /// The topic buttons, built from what the server says is open — never from a hard-coded list, so a
    /// switched-off topic is not offered at all rather than failing after the player wrote their text.
    /// </summary>
    private void DrawTopics()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(T(LocKeys.ReportTopic));
        }

        for (var i = 0; i < _kinds.Count; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            if (ImGui.RadioButton(KindLabel(_kinds[i]), _kind == _kinds[i]))
            {
                _kind = _kinds[i];
                _status = null;
            }
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(KindHint(_kind));
        }
    }

    private string KindLabel(string kind) => kind switch
    {
        ContactKinds.Bug => T(LocKeys.ReportKindBug),
        ContactKinds.Feature => T(LocKeys.ReportKindFeature),
        ContactKinds.Feedback => T(LocKeys.ReportKindFeedback),
        ContactKinds.Other => T(LocKeys.ReportKindOther),
        _ => kind,
    };

    private string KindHint(string kind) => kind switch
    {
        ContactKinds.Bug => T(LocKeys.ReportHintBug),
        ContactKinds.Feature => T(LocKeys.ReportHintFeature),
        ContactKinds.Feedback => T(LocKeys.ReportHintFeedback),
        _ => T(LocKeys.ReportHintOther),
    };

    /// <summary>The way round the form, for someone who would rather just talk to a person.</summary>
    private void DrawDirectContact()
    {
        if (_info?.DiscordInvite is not { Length: > 0 } invite
            || !Uri.TryCreate(invite, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var who = new[] { _info.Character, _info.World }.Where(p => !string.IsNullOrWhiteSpace(p));
        var directly = string.Join(" · ", who);

        ImGui.Spacing();
        ImGui.TextDisabled(_localizer.Get(LocKeys.ReportDirect, directly));
        ImGui.SameLine();
        if (ImGui.SmallButton(T(LocKeys.ReportDiscord)))
        {
            Dalamud.Utility.Util.OpenLink(invite);
        }
    }

    /// <summary>The context line, exactly as it will be sent.</summary>
    private string Describe()
    {
        var client = _describeClient(_where);
        var parts = new[] { client.Character, client.World, client.PluginVersion, client.GameVersion, client.Where }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        return string.Join(" · ", parts);
    }

    private void Send()
    {
        var key = _store.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var request = new ContactRequest
        {
            Kind = _kind,
            Subject = _subject.Trim(),
            Message = _message.Trim(),
            Client = _describeClient(_where),
        };

        _sending = true;
        _status = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _api.PostContactAsync(key, request, CancellationToken.None).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    _status = T(LocKeys.ReportSent);
                    _failed = false;

                    // Only cleared on success — a failed send must leave the text where it is, or the
                    // player pays for our problem by writing it twice.
                    _subject = string.Empty;
                    _message = string.Empty;
                }
                else
                {
                    _status = ErrorMessage(result.Error);
                    _failed = true;
                    _log.Info($"Report failed: {result.Error?.Kind} ({result.Error?.StatusCode}).");
                }
            }
            catch (Exception ex)
            {
                _status = _localizer.Get(LocKeys.ReportFailed, ex.GetType().Name);
                _failed = true;
                _log.Error($"Report failed: {ex.GetType().Name}.");
            }
            finally
            {
                _sending = false;
            }
        });
    }

    /// <summary>Turns a failure into something the player can act on.</summary>
    private string ErrorMessage(ApiError? error) => error switch
    {
        null => T(LocKeys.ErrorUnexpected),
        { Kind: ApiErrorKind.Unauthorized } => T(LocKeys.ReportNotConnected),
        { Kind: ApiErrorKind.Forbidden } => T(LocKeys.ReportNoScope),
        { Kind: ApiErrorKind.RateLimited } => T(LocKeys.ReportRateLimited),

        // 503: the server could not hand it on, and it stores nothing — so this is "try again",
        // not "we have it". Anything else with a server explanation shows that explanation.
        { StatusCode: 503 } => T(LocKeys.ReportUndelivered),
        { Kind: ApiErrorKind.Network } => T(LocKeys.ErrorNetwork),
        { Detail.Length: > 0 } detail => _localizer.Get(LocKeys.ReportFailed, detail.Detail!),
        _ => _localizer.Get(LocKeys.ReportFailed, error.Message),
    };
}
