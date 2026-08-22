using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Where a person answers the questions a sync could not answer, and clears the rows that no gearset in
/// game occupies any more.
/// </summary>
/// <remarks>
/// <para>
/// One question at a time, because each answer changes the candidates for the next one. The bulk door is
/// there for the one morning it is the right shape: somebody who kept sets on the website and installs the
/// plugin gets asked once per row before they have done anything, and twenty carousel views is where people
/// close the window.
/// </para>
/// <para>
/// It offers exactly what the server said it would accept and nothing else. No free picker over the
/// unattached rows, because that would put a decision in front of somebody that the server then has to
/// refuse — and they did nothing wrong.
/// </para>
/// </remarks>
public sealed class ReviewWindow : Window
{
    private static readonly Vector4 Accent = new(0.55f, 0.78f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.72f, 0.74f, 0.78f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.55f, 1f);

    private readonly ReviewService _review;
    private readonly Localizer _localizer;
    private readonly Func<string?> _currentCharacter;
    private readonly Action _afterDecision;
    private readonly Func<int, string> _itemName;
    private readonly Action<string> _openLink;

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private int _alsoSettled;

    // Set on a background thread, acted on in Draw. The sets below are touched by the drawing thread only,
    // and that is the whole safety argument: a Clear() from a task while Draw is asking Contains() is a
    // race on a HashSet, and an exception on the framework thread breaks the window rather than logging.
    private volatile bool _resetAfterDecision;

    private readonly HashSet<string> _struckOut = new(StringComparer.Ordinal);
    private int _question;
    private bool _showAside;
    private bool _staleNotice;

    /// <summary>Creates the window.</summary>
    /// <param name="review">The service that holds the questions.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="currentCharacter">The character on screen.</param>
    /// <param name="afterDecision">
    /// Called after a decision landed, so the sync path can drop its unchanged guard and re-learn the
    /// mapping on the next push.
    /// </param>
    /// <param name="itemName">Resolves an item id to its name in the player language.</param>
    /// <param name="openLink">Opens an http(s) url, already guarded against other schemes.</param>
    public ReviewWindow(
        ReviewService review,
        Localizer localizer,
        Func<string?> currentCharacter,
        Action afterDecision,
        Func<int, string> itemName,
        Action<string> openLink)
        : base("Eorzea Arsenal###EorzeaArsenalReview")
    {
        _review = review;
        _localizer = localizer;
        _currentCharacter = currentCharacter;
        _afterDecision = afterDecision;
        _itemName = itemName;
        _openLink = openLink;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 340),
            MaximumSize = new Vector2(1100, 1200),
        };
    }


    /// <summary>
    /// Coloured text that is never read as a format string.
    /// </summary>

    /// <summary>
    /// Whether anything is clickable right now: a call in flight, or a rate limit being waited out.
    /// </summary>
    /// <remarks>
    /// The second half matters as much as the first. While the service is holding off, a click would be
    /// swallowed silently — and a button that looks alive and does nothing is worse feedback than one that
    /// is plainly greyed out.
    /// </remarks>
    private bool Blocked => _review.IsBusy || _review.BackoffRemaining is not null;
    /// <param name="colour">The colour to draw in.</param>
    /// <param name="text">The text, which may have come from a server or from another player.</param>
    /// <remarks>
    /// Almost everything this window draws contains a name somebody else chose: a gearset name, a team
    /// name. Whether the binding in use passes those through printf could not be established from here, so
    /// the window does not depend on the answer — an unformatted call costs nothing and a percent sign in a
    /// team name stays a percent sign.
    /// </remarks>
    private static void Text(Vector4 colour, string text)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(text);
    }

    /// <summary>Wrapped text that is never read as a format string, for the longer sentences.</summary>
    /// <param name="text">The text.</param>
    private static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
    private string T(string key) => _localizer.Get(key);

    private string T(string key, params object[] args) => _localizer.Get(key, args);

    /// <summary>Opens the window, which also holds automatic pushes back and re-reads the questions.</summary>
    public void Open() => IsOpen = true;

    /// <inheritdoc />
    public override void OnOpen()
    {
        WindowName = T(LocKeys.ReviewTitle) + "###EorzeaArsenalReview";
        _review.IsOpen = true;
        _staleNotice = false;
        Refresh();
    }

    /// <inheritdoc />
    public override void OnClose() => _review.IsOpen = false;

    private void Refresh()
    {
        if (_currentCharacter() is { Length: > 0 } cid)
        {
            _ = Task.Run(() => _review.RefreshAsync(cid, CancellationToken.None));
        }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (_resetAfterDecision)
        {
            // The question that was on screen may be gone and the rest have shifted up, so walking back to
            // the first is the honest thing: the list is re-sorted most likely first, and it is not the same
            // carousel any more.
            _resetAfterDecision = false;
            _question = 0;
            _struckOut.Clear();
        }

        DrawHeader();

        // The questions belong to one character. Switching in game while this is open would leave somebody
        // answering about a character they are no longer playing: the answers would still be right for that
        // character, which is precisely what makes it confusing rather than harmless.
        var onScreen = _currentCharacter();
        if (onScreen is { Length: > 0 } &&
            _review.CurrentCidHash is { Length: > 0 } shown &&
            !string.Equals(onScreen, shown, StringComparison.Ordinal) &&
            !Blocked)
        {
            Refresh();
            Text(Muted, T(LocKeys.ReviewRefresh));
            return;
        }

        // Nothing else is reachable while a second click is waiting: the point of asking is lost if the
        // rest of the window is still live behind it.
        if (DrawPending())
        {
            return;
        }

        if (_review.LastOutcome == ReviewOutcome.Unavailable)
        {
            Text(Muted, T(LocKeys.ReviewUnavailable));
            return;
        }

        if (_review.Current is not { } state)
        {
            Text(Muted, T(LocKeys.ReviewNothing));
            return;
        }

        if (_review.BackoffRemaining is { } waiting)
        {
            Text(Warn, T(LocKeys.ReviewWaiting, (int)Math.Ceiling(waiting.TotalSeconds)));
        }

        if (_staleNotice)
        {
            Text(Warn, T(LocKeys.ReviewStale));
        }

        if (_alsoSettled > 0)
        {
            Text(Good, T(LocKeys.ReviewAlsoSettled, _alsoSettled));
        }

        // The first sync after a website-first start asks once per hand-made row, before the player has
        // done anything. Saying what is being asked is what keeps a wall of questions from reading as a
        // fault, and it is why the bulk door exists at all.
        if (state.Held.Count >= 3 && state.Held.TrueForAll(EveryCandidateIsHandMade))
        {
            TextWrapped(T(LocKeys.ReviewWebsiteFirst, state.Held.Count));
        }

        ImGui.Separator();

        var anything = state.Held.Count > 0 || state.Orphans.Count > 0;
        if (!anything)
        {
            Text(Good, T(LocKeys.ReviewNothing));
            return;
        }

        if (state.Held.Count > 0)
        {
            DrawQuestion(state);
            ImGui.Separator();
        }

        DrawInventory(state);
    }


    /// <summary>
    /// Whether every answer offered for a question is a row somebody made on the website. True for the
    /// website-first morning and false as soon as one ordinary plugin row is in the mix.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <returns><see langword="true"/> when all its candidates are hand-made.</returns>
    private static bool EveryCandidateIsHandMade(HeldGearset held) =>
        held.Candidates.Count > 0 && held.Candidates.TrueForAll(ReviewRules.IsAdoption);
    private void DrawHeader()
    {
        using (ImRaii.Disabled(Blocked))
        {
            if (ImGui.Button(T(LocKeys.ReviewRefresh)))
            {
                _staleNotice = false;
                Refresh();
            }
        }

        if (_review.Current is not { } state)
        {
            return;
        }

        var open = state.OpenOrphans.Count();
        var aside = state.Orphans.Count - open;

        ImGui.SameLine();
        Text(state.Held.Count > 0 ? Warn : Muted, T(LocKeys.ReviewQuestions, state.Held.Count));
        ImGui.SameLine();
        Text(Muted, "·");
        ImGui.SameLine();
        Text(open > 0 ? Warn : Muted, T(LocKeys.ReviewOrphansOpen, open));

        if (aside > 0)
        {
            ImGui.SameLine();
            Text(Muted, "·");
            ImGui.SameLine();
            Text(Muted, T(LocKeys.ReviewOrphansAside, aside));
        }
    }

    /// <summary>
    /// One question, with the answer the server proposes already selected. The carousel walks them in the
    /// order they arrived, which the server sorted most likely first.
    /// </summary>
    private void DrawQuestion(ReviewState state)
    {
        _question = Math.Clamp(_question, 0, state.Held.Count - 1);
        var held = state.Held[_question];

        if (state.Held.Count > 1)
        {
            Text(Muted, $"{_question + 1} / {state.Held.Count}");
            ImGui.SameLine();
            using (ImRaii.Disabled(_question == 0))
            {
                if (ImGui.SmallButton("<"))
                {
                    _question--;
                }
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(_question >= state.Held.Count - 1))
            {
                if (ImGui.SmallButton(">"))
                {
                    _question++;
                }
            }
        }

        Text(Accent, T(LocKeys.ReviewWhichSet, held.Job ?? "?", held.Name ?? string.Empty));

        // The lived position where it is known: the stored one can sit in a band the player cannot find.
        DrawItems(held.SetUid ?? string.Empty, held.Items);
        ImGui.Spacing();

        var preselected = ReviewRules.Preselected(held);
        foreach (var candidate in held.Candidates)
        {
            DrawCandidate(state, held, candidate, ReferenceEquals(candidate, preselected));
        }

        ImGui.Spacing();
        DrawQuestionFooter(state, held);
    }

    /// <summary>
    /// One candidate: what it is, how well it matches, and what answering it away would leave behind.
    /// </summary>
    /// <remarks>
    /// The proposed one is marked, and it is marked by <c>proposed</c> rather than by the score. A higher
    /// score that is not proposed says so, with the reason, so the player is not left wondering why the
    /// better-looking one is not the suggestion.
    /// </remarks>
    private void DrawCandidate(ReviewState state, HeldGearset held, ReviewCandidate candidate, bool proposed)
    {
        using var id = ImRaii.PushId(candidate.SetUid ?? string.Empty);

        var name = string.IsNullOrWhiteSpace(candidate.Name) ? "—" : candidate.Name;
        Text(proposed ? Good : Muted, proposed ? $"› {candidate.Job} {name}" : $"  {candidate.Job} {name}");

        ImGui.SameLine();
        Text(Muted, T(LocKeys.ReviewMatch, candidate.MatchedSlots, candidate.TotalSlots, candidate.Probability));

        // Same job rather than merely compatible: GLA onto PLD and PLD onto PLD are both allowed and read
        // differently to somebody deciding.
        if (candidate.SameJob)
        {
            ImGui.SameLine();
            Text(Muted, "· " + T(LocKeys.ReviewSameJob));
        }

        if (candidate.Hidden)
        {
            ImGui.SameLine();
            Text(Muted, "· " + T(LocKeys.ReviewHiddenOnSite));
        }

        DrawLink(candidate.Url);

        // Named, not vague. The uid comes out of this same answer, so the name is already here.
        if (!proposed && candidate.BlockedBy is { Length: > 0 } blocker)
        {
            Text(Muted, "    " + T(LocKeys.ReviewBlockedBy, NameOf(state, blocker)));
        }

        if (ReviewRules.IsAdoption(candidate))
        {
            Text(Warn, $"    {T(LocKeys.ReviewAdoption)}");
        }

        // Only where there is something to say. A dialog that appears every time teaches people to click it
        // away, including the times it matters.
        if (candidate.HasPin)
        {
            Text(Muted, $"    {T(LocKeys.ReviewKeepsPin, name)}");
        }

        if (candidate.HasTeamShare && candidate.TeamNames.Count > 0)
        {
            Text(Warn, $"    {T(LocKeys.ReviewKeepsShare, string.Join(", ", candidate.TeamNames), name)}");
        }

        DrawItems(candidate.SetUid ?? string.Empty, candidate.Items);

        using (ImRaii.Disabled(Blocked))
        {
            if (ImGui.SmallButton(T(LocKeys.ReviewThisIsIt)))
            {
                var link = new ReviewDecision
                {
                    SetUid = held.SetUid!,
                    Action = ReviewAction.Link,
                    TargetUid = candidate.SetUid,
                };

                // A link onto a plugin row is an ordinary answer. Onto a hand-made one it changes who
                // governs the row from here on, which is the one-way door the dialog has to name first.
                Gate(link, ReviewRules.IsAdoption(candidate) ? [T(LocKeys.ReviewAdoption)] : null);
            }

            // "Stop asking" is not a third answer to the question: it names the candidate, not the newcomer,
            // so it sits here rather than beside the two below. With three candidates on screen, one shared
            // button would have been ambiguous.
            ImGui.SameLine();
            if (ImGui.SmallButton(T(LocKeys.ReviewStopAsking)))
            {
                Decide(new ReviewDecision { SetUid = candidate.SetUid!, Action = ReviewAction.Ignore });
            }
        }

        ImGui.Spacing();
    }

    private void DrawQuestionFooter(ReviewState state, HeldGearset held)
    {
        using (ImRaii.Disabled(Blocked))
        {
            if (ImGui.Button(T(LocKeys.ReviewItIsNew)))
            {
                Decide(new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.New });
            }

            // Beside the question rather than among the answers: this is the way out, not an answer.
            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.ReviewTakeOut)))
            {
                // Named by the contract: the outcome surprises anybody who was not told, because nothing is
                // moved — the released row stays and the next push writes a second one beside it.
                Gate(
                    new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.Release },
                    [T(LocKeys.ReviewReleaseTwoRows)]);
            }
        }

        // The only lever a player has over the bulk call, and the reason it is safe at all: they may take a
        // pair out, never add or re-point one. Without this control the button was all-or-nothing, and the
        // guarantee it rests on had nothing to rest on.
        if (state.Held.Count >= 3 && held.SetUid is { Length: > 0 } uid)
        {
            var struck = _struckOut.Contains(uid);
            if (ImGui.SmallButton(struck ? T(LocKeys.ReviewPutBack) : T(LocKeys.ReviewStrikeOut)))
            {
                if (!_struckOut.Remove(uid))
                {
                    _struckOut.Add(uid);
                }
            }

            if (_struckOut.Count > 0)
            {
                ImGui.SameLine();
                Text(Muted, T(LocKeys.ReviewStruckCount, _struckOut.Count));
            }
        }

        if (state.Held.Count < 3)
        {
            return;
        }

        var pairs = ReviewRules.MappingToAccept(state, _struckOut);
        if (pairs.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        using (ImRaii.Disabled(Blocked))
        {
            if (ImGui.Button(T(LocKeys.ReviewAcceptAll, pairs.Count)))
            {
                // One press over eighteen pairings, and every adoption among them is a one-way door of its
                // own. So the count goes in front of the press rather than beside it.
                var adopting = ReviewRules.AdoptionCount(state, pairs);
                _pending = new Pending(
                    null,
                    adopting > 0
                        ? [T(LocKeys.ReviewAcceptAdoptions, adopting), T(LocKeys.ReviewAdoption)]
                        : [T(LocKeys.ReviewAcceptAll, pairs.Count)]);
            }
        }

        var adoptions = ReviewRules.AdoptionCount(state, pairs);
        if (adoptions > 0)
        {
            Text(Warn, T(LocKeys.ReviewAcceptAdoptions, adoptions));
        }
    }

    /// <summary>
    /// The inventory half: rows no gearset in game occupies. Open ones first, the ones already put aside
    /// folded away — reachable, because a misclick has to have a way back, and quiet, because somebody who
    /// said "not this one" ten times does not want to keep reading it.
    /// </summary>
    private void DrawInventory(ReviewState state)
    {
        var open = state.OpenOrphans.ToList();
        var aside = state.PutAsideOrphans.ToList();

        if (open.Count == 0 && aside.Count == 0)
        {
            return;
        }

        Text(Accent, T(LocKeys.ReviewInventory));

        foreach (var row in open)
        {
            DrawOrphan(row);
        }

        if (aside.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        if (ImGui.Selectable(T(LocKeys.ReviewAsideGroup, aside.Count), _showAside))
        {
            _showAside = !_showAside;
        }

        if (!_showAside)
        {
            return;
        }

        foreach (var row in aside)
        {
            DrawOrphan(row);
        }
    }

    /// <summary>
    /// One inventory row, with exactly the verbs that apply to it. A hand-made row that was put aside gets
    /// one button, not one live beside three dead ones.
    /// </summary>
    private void DrawOrphan(OrphanRow row)
    {
        using var id = ImRaii.PushId(row.SetUid ?? string.Empty);

        var name = string.IsNullOrWhiteSpace(row.Name) ? "—" : row.Name;
        Text(Muted, $"{row.Job} {name}");

        ImGui.SameLine();
        Text(
            Muted,
            row.LastSeenAt is { Length: > 0 } seen
                ? T(LocKeys.ReviewLastSeen, seen)
                : T(LocKeys.ReviewNeverInGame));

        if (row.Hidden)
        {
            ImGui.SameLine();
            Text(Muted, $"· {T(LocKeys.ReviewHiddenOnSite)}");
        }

        if (row.HasTeamShare && row.TeamNames.Count > 0)
        {
            Text(Warn, $"    {T(LocKeys.ReviewKeepsShare, string.Join(", ", row.TeamNames), name)}");
        }

        // A row the server sent without an identity cannot be decided about: every verb needs one to name.
        // Offering buttons that could only ever come back as a 422 is the same mistake as offering an
        // incompatible candidate, so the row is shown and the actions are not.
        if (row.SetUid is not { Length: > 0 })
        {
            ImGui.Spacing();
            return;
        }

        DrawLink(row.Url);
        DrawItems(row.SetUid ?? string.Empty, row.Items);

        using (ImRaii.Disabled(Blocked))
        {
            foreach (var verb in ReviewRules.OfferedVerbs(row))
            {
                if (ImGui.SmallButton(Label(verb)))
                {
                    Gate(new ReviewDecision { SetUid = row.SetUid!, Action = verb }, WarningsFor(verb, row));
                }

                ImGui.SameLine();
            }
        }

        ImGui.NewLine();

        // Said only where it is true. A parked or ignored row was not being reported anyway, and a hand-made
        // one cannot come back at all, so promising a return there would be a lie.
        if (row.IsFromPlugin && !row.IsPutAside)
        {
            Text(Muted, $"    {T(LocKeys.ReviewDeleteComesBack)}");
        }

        ImGui.Spacing();
    }

    private string Label(string verb) => verb switch
    {
        ReviewAction.Reopen => T(LocKeys.ReviewReopen),
        ReviewAction.Delete => T(LocKeys.ReviewDelete),
        ReviewAction.Release => T(LocKeys.ReviewTakeOut),
        _ => T(LocKeys.ReviewIgnore),
    };

    /// <summary>
    /// What a set contains, behind a toggle. Item ids come over the wire and are resolved here, because a
    /// name from the server could only ever be in one language.
    /// </summary>
    /// <param name="key">Something stable to remember the toggle by, normally the row uid.</param>
    /// <param name="items">The gear, by slot.</param>
    /// <remarks>
    /// Folded away by default and not omitted: seeing what is in a row is what makes "delete or keep"
    /// answerable, and a window that shows everything at once is a window nobody reads.
    /// </remarks>
    private void DrawItems(string key, Dictionary<string, ItemDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var open = _expanded.Contains(key);
        if (ImGui.SmallButton(open ? $"{T(LocKeys.ReviewShowItems)} ▾" : $"{T(LocKeys.ReviewShowItems)} ▸"))
        {
            if (!_expanded.Remove(key))
            {
                _expanded.Add(key);
            }
        }

        if (!open)
        {
            return;
        }

        foreach (var (slot, item) in items)
        {
            var materia = item.Materia.Count > 0 ? $"  ·  {item.Materia.Count}× materia" : string.Empty;
            Text(Muted, $"      {slot}: {_itemName(item.Id)}{materia}");
        }
    }

    /// <summary>Offers the row on the website, where there is a url to offer.</summary>
    /// <param name="url">The url the server sent. Read back, never composed.</param>
    private void DrawLink(string? url)
    {
        if (url is not { Length: > 0 })
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(T(LocKeys.ReviewOpenOnSite)))
        {
            _openLink(url);
        }
    }

    /// <summary>
    /// The name of the set a candidate went to instead, so the honest sentence can be concrete.
    /// </summary>
    /// <param name="state">The answer both rows came out of.</param>
    /// <param name="uid">The row named by <c>blocked_by</c>.</param>
    /// <returns>Its name, or the uid shortened when the name is not in this answer.</returns>
    /// <remarks>
    /// The contract points out that this uid comes out of the same answer, so the name is already here and
    /// no second call is needed. Looking in both lists because the winner can be either a held newcomer or
    /// another candidate.
    /// </remarks>
    private static string NameOf(ReviewState state, string uid)
    {
        foreach (var held in state.Held)
        {
            if (string.Equals(held.SetUid, uid, StringComparison.Ordinal) && held.Name is { Length: > 0 })
            {
                return held.Name;
            }

            foreach (var candidate in held.Candidates)
            {
                if (string.Equals(candidate.SetUid, uid, StringComparison.Ordinal) && candidate.Name is { Length: > 0 })
                {
                    return candidate.Name;
                }
            }
        }

        foreach (var orphan in state.Orphans)
        {
            if (string.Equals(orphan.SetUid, uid, StringComparison.Ordinal) && orphan.Name is { Length: > 0 })
            {
                return orphan.Name;
            }
        }

        return uid.Length > 8 ? uid[..8] : uid;
    }

    /// <summary>
    /// A decision waiting for a second click, with the sentences that say what it will do.
    /// </summary>
    /// <param name="Decision">What will be sent if it is confirmed, or null for the whole mapping.</param>
    /// <param name="Lines">What the player is told first, most consequential last.</param>
    private sealed record Pending(ReviewDecision? Decision, IReadOnlyList<string> Lines);

    private Pending? _pending;

    /// <summary>
    /// Sends a decision, or holds it for a second click when there is no way back through this endpoint.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="warnings">
    /// What to say before it happens. Empty means nothing has to be said, and the decision goes straight
    /// out: asking twice about a reversible thing is how people learn to click through the dialog that
    /// matters.
    /// </param>
    private void Gate(ReviewDecision decision, IReadOnlyList<string>? warnings = null)
    {
        if (warnings is { Count: > 0 })
        {
            _pending = new Pending(decision, warnings);
            return;
        }

        Decide(decision);
    }

    /// <summary>Draws the second click, if one is waiting.</summary>
    /// <returns><see langword="true"/> while it is waiting, so the rest of the window stays out of reach.</returns>
    private bool DrawPending()
    {
        if (_pending is not { } pending)
        {
            return false;
        }

        Text(Warn, T(LocKeys.ReviewConfirmTitle));
        foreach (var line in pending.Lines)
        {
            TextWrapped($"  {line}");
        }

        ImGui.Spacing();
        using (ImRaii.Disabled(Blocked))
        {
            if (ImGui.Button(T(LocKeys.ReviewConfirmYes)))
            {
                var decision = pending.Decision;
                _pending = null;
                if (decision is null)
                {
                    AcceptMapping();
                }
                else
                {
                    Decide(decision);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.ReviewConfirmNo)))
            {
                _pending = null;
            }
        }

        return true;
    }

    /// <summary>
    /// What has to be said before a verb is applied to an inventory row, in the order it matters.
    /// </summary>
    /// <param name="verb">The verb about to be applied.</param>
    /// <param name="row">The row.</param>
    /// <returns>The sentences, empty when there is nothing to say.</returns>
    private IReadOnlyList<string> WarningsFor(string verb, OrphanRow row)
    {
        var lines = new List<string>();
        if (!ReviewRules.IsIrreversible(verb))
        {
            return lines;
        }

        var name = string.IsNullOrWhiteSpace(row.Name) ? "—" : row.Name;

        // The team comes first: it is the part that affects somebody who is not in the room.
        if (row.HasTeamShare && row.TeamNames.Count > 0)
        {
            lines.Add(T(LocKeys.ReviewKeepsShare, string.Join(", ", row.TeamNames), name));
        }

        if (row.HasPin)
        {
            lines.Add(T(LocKeys.ReviewKeepsPin, name));
        }

        // Only where it is true: a parked or ignored row was not being reported anyway, and a hand-made one
        // cannot come back at all, so promising a return there would be a lie.
        if (string.Equals(verb, ReviewAction.Delete, StringComparison.Ordinal) &&
            row.IsFromPlugin && !row.IsPutAside)
        {
            lines.Add(T(LocKeys.ReviewDeleteComesBack));
        }

        if (lines.Count == 0)
        {
            lines.Add(Label(verb));
        }

        return lines;
    }
    private void Decide(ReviewDecision decision) => _ = Task.Run(async () =>
    {
        var outcome = await _review.DecideAsync(decision, CancellationToken.None).ConfigureAwait(false);
        AfterCall(outcome);
    });

    private void AcceptMapping()
    {
        // Copied on the drawing thread before the task starts: handing the live set to a background read
        // while clicks can still change it is the same race from the other side.
        var struckOut = new HashSet<string>(_struckOut, StringComparer.Ordinal);
        _ = Task.Run(async () =>
        {
            var outcome = await _review.AcceptMappingAsync(struckOut, CancellationToken.None).ConfigureAwait(false);
            AfterCall(outcome);
        });
    }

    private void AfterCall(ReviewOutcome outcome)
    {
        _staleNotice = outcome == ReviewOutcome.Stale;

        // What the click settled beyond the row it named. Read from the answer rather than diffed, because
        // a held row can also leave the list because its set vanished from the game, and a diff cannot tell
        // the two apart: it would report a question settling itself over a set just deleted.
        _alsoSettled = _review.Current switch
        {
            ReviewDecisionResponse decided => decided.AlsoResolved.Count,
            ReviewAcceptResponse accepted => accepted.AlsoResolved.Count,
            _ => 0,
        };

        if (outcome != ReviewOutcome.Ok)
        {
            return;
        }

        // The question that was on screen may be gone, and the ones after it have shifted up. Walking back
        // to the first is the honest thing: the list was re-sorted most likely first, so it is not the same
        // carousel any more.
        // Not touched here: this runs on a task, and both sets belong to the drawing thread. Draw picks the
        // flag up on its next pass.
        _resetAfterDecision = true;
        _afterDecision();
    }
}
