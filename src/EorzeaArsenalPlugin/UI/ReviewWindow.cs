using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
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
/// refuse, and they did nothing wrong.
/// </para>
/// </remarks>
public sealed class ReviewWindow : Window
{
    private static readonly Vector4 Accent = new(0.55f, 0.78f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.72f, 0.74f, 0.78f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.55f, 1f);

    // The three states of a slot comparison. Amber is the one the numbers cannot express: the server
    // counts a slot as matched when the item id agrees, so a pair that differs only in its melds reads
    // 100 % and is still not the same set.
    private static readonly Vector4 Amber = new(0.95f, 0.62f, 0.25f, 1f);
    private static readonly Vector4 Bad = new(0.90f, 0.42f, 0.42f, 1f);

    private readonly ReviewService _review;
    private readonly Localizer _localizer;
    private readonly Func<string?> _currentCharacter;
    private readonly Action _afterDecision;
    /// <summary>Icon edge length on a card. The strip has to read as gear at a glance, not as a toolbar.</summary>
    private const float IconSize = 32f;

    /// <summary>
    /// Edge length of a comparison tile. The same size the gear window uses, so the two read as one thing.
    /// </summary>
    private const float TileSize = 48f;

    /// <summary>
    /// The width below which the window stops reading, whatever the buttons happen to measure.
    /// </summary>
    /// <remarks>
    /// Taken from the size the window was actually in use at while these cards were being read, not
    /// guessed: the gear grid is two halves of a tile plus a wrapping name, and much under this the names
    /// run to three lines each and the card becomes a column of hyphenated words.
    /// </remarks>
    private const float ComfortableWidth = 720f;

    /// <summary>The height below which a card and its gear no longer fit on screen together.</summary>
    private const float ComfortableHeight = 840f;

    /// <summary>
    /// Text scale for this window, the same one the what's-new window runs at. Everything here is read
    /// once and read carefully, and the default size belongs to numbers you glance at.
    /// </summary>
    private const float TextScale = 1.15f;

    private readonly Func<int, string> _itemName;
    private readonly Func<int, uint> _itemIcon;

    private readonly ITextureProvider _textures;
    private readonly Action<string> _openLink;

    /// <summary>Which number a gearset carries in game, by identity, or null where that is not known.</summary>
    private readonly Func<string, int?> _liveNumber;
    private readonly ILog _log;

    private int _alsoSettled;

    // Set on a background thread, acted on in Draw. The sets below are touched by the drawing thread only,
    // and that is the whole safety argument: a Clear() from a task while Draw is asking Contains() is a
    // race on a HashSet, and an exception on the framework thread breaks the window rather than logging.
    private volatile bool _resetAfterDecision;

    // Which cards currently show the other set instead of their own. Drawing state only, so it lives on
    // the drawing thread like the rest and is never touched from a task.
    private readonly HashSet<string> _showOther = new(StringComparer.Ordinal);

    /// <summary>Inventory rows whose gear grid the reader has opened.</summary>
    private readonly HashSet<string> _showGear = new(StringComparer.Ordinal);

    // Which candidate of the current question has its comparison open. Two full grids under one another
    // is the wall this window keeps growing back into, so only one is drawn at a time.
    private string? _openCandidate;

    /// <summary>The token of the reading these per-card choices were made against.</summary>
    private string? _shownToken;

    /// <summary>Since when there has been nothing left to decide, or nothing while something waits.</summary>
    private DateTime? _settledSince;

    /// <summary>Whether this opening was to answer something, and may therefore end by closing itself.</summary>
    private bool _closeWhenDone;

    /// <summary>When the character-changed guard last asked for a fresh read.</summary>
    private DateTime _guardRefreshedAt = DateTime.MinValue;

    private readonly HashSet<string> _struckOut = new(StringComparer.Ordinal);
    /// <summary>Which decision is on screen: questions first, then the rows no gearset occupies.</summary>
    private int _card;
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
    /// <param name="itemIcon">Resolves an item id to its game icon id, or zero when it has none.</param>
    /// <param name="textures">Loads those icons.</param>
    /// <param name="openLink">Opens an http(s) url, already guarded against other schemes.</param>
    /// <param name="liveNumber">Which number a gearset carries in game, by identity, or null when unknown.</param>
    /// <param name="log">Diagnostics sink, so an escaped exception is not simply lost.</param>
    public ReviewWindow(
        ReviewService review,
        Localizer localizer,
        Func<string?> currentCharacter,
        Action afterDecision,
        Func<int, string> itemName,
        Func<int, uint> itemIcon,
        ITextureProvider textures,
        Action<string> openLink,
        Func<string, int?> liveNumber,
        ILog log)
        : base("Eorzea Arsenal###EorzeaArsenalReview")
    {
        _review = review;
        _localizer = localizer;
        _currentCharacter = currentCharacter;
        _afterDecision = afterDecision;
        _itemName = itemName;
        _itemIcon = itemIcon;
        _textures = textures;
        _openLink = openLink;
        _liveNumber = liveNumber;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            // A starting value only. The real floor is measured from the widest row of buttons on the
            // first frame and set from Draw, because it depends on the language, the font and the user's
            // interface scale, none of which are knowable here.
            MinimumSize = new Vector2(ComfortableWidth, ComfortableHeight),

            // No cap worth having: this window holds a comparison grid and, on a website-first start, a
            // list of every pairing. Somebody with the room for it should be able to use the room.
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }


    /// <summary>
    /// Whether anything is clickable right now: a call in flight, or a rate limit being waited out.
    /// </summary>
    /// <remarks>
    /// The second half matters as much as the first. While the service is holding off, a click would be
    /// swallowed silently, and a button that looks alive and does nothing is worse feedback than one that
    /// is plainly greyed out.
    /// </remarks>
    private bool Blocked => _review.IsBusy || _review.BackoffRemaining is not null;

    /// <summary>Coloured text that is never read as a format string.</summary>
    /// <param name="colour">The colour to draw in.</param>
    /// <param name="text">The text, which may have come from a server or from another player.</param>
    /// <remarks>
    /// Almost everything this window draws contains a name somebody else chose: a gearset name, a team
    /// name. Whether the binding in use passes those through printf could not be established from here, so
    /// the window does not depend on the answer: an unformatted call costs nothing and a percent sign in a
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

    /// <summary>A tooltip that is never read as a format string.</summary>
    /// <param name="text">What it says.</param>
    /// <remarks>
    /// <c>SetTooltip</c> is the printf-shaped call, and everything this window puts in a tooltip can carry
    /// a percent sign: the server's own count reads "64%", and a gearset name is whatever its owner typed.
    /// The percentage disappearing from the count line was that, not a missing value. Every other place in
    /// this file already went through <c>TextUnformatted</c> for the same reason; the tooltips did not.
    /// </remarks>
    private static void Tooltip(string text)
    {
        using var tip = ImRaii.Tooltip();
        ImGui.TextUnformatted(text);
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
        _settledSince = null;

        // Whether closing itself is the right thing at all, decided once, here. The window shuts when the
        // last decision is answered, which is what somebody who came to answer them wants. Somebody who
        // opened it with nothing waiting came for the archive of put-aside rows, and shutting that under
        // them two seconds later would be the window undoing their click.
        _closeWhenDone = _review.Current is { } state && (state.Held.Count > 0 || state.OpenOrphans.Any());

        Refresh();
    }

    /// <inheritdoc />
    public override void OnClose() => _review.IsOpen = false;


    /// <summary>
    /// Runs a call off the drawing thread and swallows nothing silently.
    /// </summary>
    /// <param name="work">The call.</param>
    /// <remarks>
    /// A bare fire-and-forget task turns any escaped exception into an unobserved one, which in a plugin
    /// means it disappears. Cancellation is the ordinary way this ends, since the service cancels everything in
    /// flight when the plugin unloads, so that one is not worth a line in the log.
    /// </remarks>
    private void Run(Func<Task> work) => _ = Task.Run(async () =>
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The plugin is going away, or the window closed. Nothing to say.
        }
        catch (Exception ex)
        {
            _log.Error($"Review call threw: {ex.GetType().Name}.");
        }
    });
    private void Refresh()
    {
        if (_currentCharacter() is { Length: > 0 } cid)
        {
            Run(() => _review.RefreshAsync(cid, CancellationToken.None));
        }
    }

    /// <inheritdoc />
    public override void Draw()
    {
        // Read once, and read while deciding something that cannot be taken back. The game's default size
        // is a size for glanceable numbers, not for a sentence somebody has to weigh.
        ImGui.SetWindowFontScale(TextScale);

        // The text was scaled up and the spacing was not, so every line sat tighter against the next one
        // than in a window at normal size, and two buttons side by side touched. ImGui's defaults are
        // (8, 4) and (4, 3), which are defaults for a dense tool panel; this window is a page somebody
        // reads before pressing something irreversible, and it needs the air to separate one group of
        // controls from the next.
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 8f));
        using var inner = ImRaii.PushStyle(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8f, 6f));
        using var framePad = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(9f, 5f));

        // Set here rather than in the constructor, and measured rather than picked. The old floor of 520
        // was a number somebody typed while looking at an English build at one interface scale; dragged
        // to it, the button rows ran off the edge and the header wrapped into the toolbar. What the
        // window cannot do without is one row of buttons on one line, and how wide that is depends on the
        // language, the font and the scale, so it is worked out from the strings that are actually in it.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = SmallestUsableSize(),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        if (_resetAfterDecision)
        {
            // The question that was on screen may be gone and the rest have shifted up, so walking back to
            // the first is the honest thing: the list is re-sorted most likely first, and it is not the same
            // carousel any more.
            _resetAfterDecision = false;
            _card = 0;
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
            // Once every couple of seconds, not once per frame. This ran inside Draw with nothing holding
            // it back: the service has a lock, so the calls could not overlap, but they queued rather
            // than being dropped, and every one of them eventually became a request. Switching character
            // would fire them as fast as the server answered until the rate limiter closed the door,
            // which is precisely when the new character needs its questions read. Still on a timer rather
            // than once ever, so a refresh that fails is tried again.
            if (DateTime.UtcNow - _guardRefreshedAt > TimeSpan.FromSeconds(2))
            {
                _guardRefreshedAt = DateTime.UtcNow;
                Refresh();
            }

            Wrapped(Muted, T(LocKeys.ReviewCharacterChanged));
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

        // Every choice made on these cards belongs to one reading of the state and dies with it. The picked
        // candidate, the flipped comparisons and the struck-out pairings were all made against the mapping
        // in front of the player, and the token is exactly the name of that mapping: a new one means the
        // server recomputed, and a selection made against the old one is a leftover pretending to be an
        // answer. This was visible: after a bulk accept, a candidate picked minutes earlier was still in
        // the box while every sentence around it described the server's fresh proposal. Clearing on the
        // token covers every way the state can change, a decision, the shortcut, "read again", a push
        // finishing in the background, rather than one clear per path with the next path forgotten.
        if (state.StateToken is { Length: > 0 } token &&
            !string.Equals(token, _shownToken, StringComparison.Ordinal))
        {
            _shownToken = token;
            _openCandidate = null;
            _showOther.Clear();
            _showGear.Clear();
            _struckOut.Clear();
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
            Text(Good, _alsoSettled == 1
                ? T(LocKeys.ReviewAlsoSettledOne)
                : T(LocKeys.ReviewAlsoSettledMany, _alsoSettled));
        }

        // The first sync after a website-first start asks once per hand-made row, before the player has
        // done anything. Saying what is being asked is what keeps a wall of questions from reading as a
        // fault, and it is why the bulk door exists at all.
        if (state.Held.Count >= 3 && state.Held.TrueForAll(EveryCandidateIsHandMade))
        {
            TextWrapped(T(LocKeys.ReviewWebsiteFirst, state.Held.Count));
        }

        ImGui.Separator();

        // Nothing left to decide closes the window by itself. It is only ever reached from the status
        // entry, and that entry is shown only while something waits, so once the last answer is given
        // there is nothing here to come back for: leaving it open makes the player dismiss a window whose
        // whole content is the word "done".
        //
        // After a moment rather than at once. The answer that emptied it has just been applied and the
        // line saying so, including the count of questions that settled themselves with it, would
        // otherwise be gone in the same frame it appeared. A window that vanishes on a press reads as a
        // mis-click; one that says it is finished and then goes reads as finished.
        //
        // Not while a call is in flight, and not while the archive of put-aside rows is open: those are
        // decided rather than waiting, so they do not hold the window open on their own, but somebody who
        // unfolded them is looking at something.
        var decided = state.Held.Count == 0 && !state.OpenOrphans.Any();
        if (decided && _closeWhenDone && !Blocked && !_showAside)
        {
            _settledSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _settledSince > TimeSpan.FromSeconds(2.5))
            {
                IsOpen = false;
            }
        }
        else
        {
            _settledSince = null;
        }

        // Said whenever nothing is open, archive or no archive. It used to hang on a condition that counted
        // put-aside rows as well, so a player who had ever set anything aside got no line at all: the last
        // answer landed, the cards vanished and the window closed two seconds later with nothing having
        // said it was finished. Rows in the archive are decided, and "nothing open" is true beside them.
        if (decided)
        {
            Text(Good, T(LocKeys.ReviewNothing));
        }

        var anything = state.Held.Count > 0 || state.Orphans.Count > 0;
        if (!anything)
        {
            return;
        }

        // Above the carousel, not under it. The bulk door is about every question at once and has nothing
        // to do with the card in front of you, so it belongs where a shortcut belongs: offered before the
        // work, not discovered after scrolling past all of it.
        DrawBulk(state);

        // One decision on screen, whatever kind it is. Questions and inventory rows each had their own
        // carousel and both were drawn, one under the other, so the window was two card stacks deep and
        // the thing you were answering depended on how far you had scrolled. They are one list now:
        // questions first because a push can invalidate them, then the rows no gearset occupies. Both
        // kinds already draw the same comparison from the same method; walking them from one set of arrows
        // is the other half of that.
        var inventory = ReviewRules.InventoryToOffer(state);
        var decisions = state.Held.Count + inventory.Count;

        if (decisions > 0)
        {
            _card = Math.Clamp(_card, 0, decisions - 1);
            DrawStepper(ref _card, decisions, "card");

            if (_card < state.Held.Count)
            {
                DrawQuestion(state, state.Held[_card]);
            }
            else
            {
                DrawInventoryCard(inventory[_card - state.Held.Count]);
            }
        }

        DrawAside(state);
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
        Text(
            state.Held.Count > 0 ? Warn : Muted,
            state.Held.Count == 1
                ? T(LocKeys.ReviewQuestionsOne)
                : T(LocKeys.ReviewQuestionsMany, state.Held.Count));
        ImGui.SameLine();
        Text(Muted, "·");
        ImGui.SameLine();
        Text(
            open > 0 ? Warn : Muted,
            open == 1 ? T(LocKeys.ReviewOrphansOpenOne) : T(LocKeys.ReviewOrphansOpenMany, open));

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
    private void DrawQuestion(ReviewState state, HeldGearset held)
    {
        var preselected = ReviewRules.Preselected(held);
        var name = Named(string.IsNullOrWhiteSpace(held.Name) ? "-" : held.Name!, held.SetUid);

        // What this is and what to do, in two sentences, before any button. The inventory card has had
        // them since the day it stopped being a wall of evidence; the question card kept the comparison,
        // the percentages and four icons and never got the part that says which of them is yours.
        Wrapped(Accent, T(LocKeys.ReviewQuestionWhat, name));
        // Worked out before the sentence that describes it, because the sentence has to describe the row
        // that is actually selected. It named the server's proposal unconditionally, so picking the other
        // candidate left the card saying "it might be Web DRG C" over a comparison with Web DRG A and two
        // buttons that would have written Web DRG A. Two lines on one card naming two different rows, with
        // the lower one winning on the press.
        var open = held.Candidates.FirstOrDefault(c => c.SetUid == _openCandidate)
            ?? preselected
            ?? held.Candidates.FirstOrDefault();

        var ownPick = open is not null && preselected is not null && !ReferenceEquals(open, preselected);

        Wrapped(
            !ownPick && QuestionAdvisor.MayRecommend(held) ? Good : Muted,
            ownPick
                ? T(LocKeys.ReviewQuestionOwnPick, Named(NameOfCandidate(open), open?.SetUid), Named(NameOfCandidate(preselected), preselected?.SetUid))
                : QuestionAdvice(held, preselected));

        ImGui.Dummy(new Vector2(0f, 5f));

        // The two real answers, as words rather than as glyphs, because this is the decision somebody came
        // here to make. "That is the one" belongs to whichever row is picked below, so it stays down there
        // and only the answers that need no candidate at all are up here.
        using (ImRaii.PushId($"q{held.SetUid}"))
        // Every answer that is about the question rather than about one candidate, in one row. "Take it
        // out of the plugin" is one of them: the contract calls it a valid answer to a question, and it
        // used to sit at the very bottom under the last candidate's grid, where it read as belonging to
        // that candidate. It never did.
        using (ImRaii.Disabled(Blocked))
        {
            if (LabelledButton(FontAwesomeIcon.Plus, T(LocKeys.ReviewItIsNew), T(LocKeys.ReviewItIsNewMeans)))
            {
                Decide(new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.New });
            }

            ImGui.SameLine(0f, 14f);
            if (LabelledButton(FontAwesomeIcon.Unlink, T(LocKeys.ReviewTakeOut), T(LocKeys.ReviewReleaseHeld)))
            {
                Gate(
                    new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.Release },
                    [T(LocKeys.ReviewReleaseHeld)]);
            }

            // No link to the website for the row behind the question. The rule this window states on the
            // inventory card applies here with more force than anywhere else: the site being asked about
            // came out of the game a moment ago and the player can open it in the gearset list. A second
            // globe, identical to the one under the comparison and with the same tooltip, only asked the
            // reader to work out which of two identical buttons went where.
        }

        // A rule under the answers that need no candidate, because everything below belongs to one stored
        // row and everything above does not. Without it the card was one flat column of controls.
        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 4f));

        // One candidate on screen, chosen from a box. The earlier shape drew every candidate under one
        // another, each with its own "that is the one" and "stop asking": four identical buttons on one
        // card, and no way to tell from the button which row it would answer for. A picker collapses that
        // to one comparison and one pair of buttons, and it can do so because a single decision goes to
        // POST /gear/review, where every candidate is a valid target rather than only the proposed one.
        if (open is null)
        {
            return;
        }

        if (held.Candidates.Count > 1)
        {
            DrawCandidatePicker(held, ref open, preselected);
        }

        DrawCandidate(state, held, open, ReferenceEquals(open, preselected));
    }

    /// <summary>
    /// The box that says which stored row is being weighed, where more than one is in the running.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <param name="open">The one on screen, replaced when the reader picks another.</param>
    /// <param name="preselected">What the server proposes, marked in the list so the pick has a default.</param>
    /// <remarks>
    /// This box exists on the card and cannot exist in the bulk accept, and the difference is the endpoint
    /// rather than the layout: <c>/gear/review/accept</c> takes only pairs the server itself proposed for
    /// this token, in one transaction, so an entry naming any other row would fail the whole batch.
    /// </remarks>
    private void DrawCandidatePicker(HeldGearset held, ref ReviewCandidate open, ReviewCandidate? preselected)
    {
        var labels = new string[held.Candidates.Count];
        var index = 0;
        for (var i = 0; i < held.Candidates.Count; i++)
        {
            var c = held.Candidates[i];
            var name = string.IsNullOrWhiteSpace(c.Name) ? "-" : c.Name;
            labels[i] = T(
                ReferenceEquals(c, preselected) ? LocKeys.ReviewPickOptionProposed : LocKeys.ReviewPickOption,
                name,
                c.Probability);

            if (ReferenceEquals(c, open))
            {
                index = i;
            }
        }

        // The label is drawn rather than passed, because ImGui puts a combo's own label to the right of the
        // box and this one has to be read before the value, not after it.
        ImGui.AlignTextToFramePadding();
        Text(Muted, T(LocKeys.ReviewPickLabel));
        ImGui.SameLine();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
        if (ImGui.Combo("##pickcand", ref index, labels, labels.Length))
        {
            open = held.Candidates[index];
            _openCandidate = open.SetUid;
        }
    }
    /// <summary>
    /// One candidate: what it is, how well it matches, and what answering it away would leave behind.
    /// </summary>
    /// <remarks>
    /// The proposed one is marked, and it is marked by <c>proposed</c> rather than by the score. A higher
    /// score that is not proposed says so, with the reason, so the player is not left wondering why the
    /// better-looking one is not the suggestion.
    /// </remarks>
    /// <summary>
    /// What to call a row the server listed as resembling this one: its name, and where it sits in game
    /// when it sits there.
    /// </summary>
    /// <param name="similar">The row.</param>
    /// <returns>The name, with the live position appended where one is known.</returns>
    /// <remarks>
    /// <para>
    /// Two things at once, and both came from one screenshot. The card listed "Barde" twice with the same
    /// score, because the player genuinely has two gearsets by that name; nothing on screen told them
    /// apart and it read like the same row drawn twice.
    /// </para>
    /// <para>
    /// And the position carries the argument the card is making. A parked row that is nearly identical to
    /// a set still sitting in the list is a row nobody needs, and saying "in game, #15" lets the reader see
    /// that rather than being told it. The plugin's live mapping is the first source for that number and
    /// the only one that is current; since 2026-09-12 the payload carries a state, a date and a position
    /// too, so where the live list cannot place a row the server's last record fills in, marked as what it
    /// is.
    /// </para>
    /// </remarks>
    private static string NameOfSimilar(SimilarSet similar) =>
        string.IsNullOrWhiteSpace(similar.Name) ? "-" : similar.Name!;

    /// <summary>The same, with whatever position can be had for it.</summary>
    /// <param name="similar">The row it resembles.</param>
    /// <returns>Text to drop into a sentence, quotes included.</returns>
    /// <remarks>
    /// The live number where the live list has one, because that is where the set is now. Otherwise the
    /// server's last record, which by the contract of this list belongs to a row that is in game: the only
    /// entry here without a position is a hand-made one, and there a number would be a claim about a list
    /// it was never in. The fallback is worth having for exactly the case this whole label exists for, two
    /// sets of one job with one name: that is where the live table withdraws both claims and answers
    /// nothing at all.
    /// </remarks>
    private string NamedSimilar(SimilarSet similar)
    {
        var name = NameOfSimilar(similar);
        var live = similar.SetUid is { Length: > 0 } uid ? _liveNumber(uid) : null;
        if (live is { } number)
        {
            return $"\"{name}\" (#{number})";
        }

        return similar.GearIndex is { } stored
            ? $"\"{name}\" ({T(LocKeys.ReviewSimilarLastAt, stored + 1)})"
            : $"\"{name}\"";
    }

    /// <summary>Where a row an orphan resembles came from, where that explains something.</summary>
    /// <param name="similar">The row it resembles.</param>
    /// <returns>The phrase, or nothing.</returns>
    /// <remarks>
    /// Only the hand-made case, and only because it explains a set with a link and no position. No date:
    /// every row in this list is one the player still has, so "last reported" about it is the date of the
    /// last push and says nothing about the pair. Drawn anyway it was worse than useless, since the
    /// orphan's own "last reported" is the second line of the card and the same sentence with the same
    /// date then appeared twice, three lines apart, about two different sets.
    /// </remarks>
    private string SimilarOrigin(SimilarSet similar) =>
        ReviewRules.OriginOf(similar) == OrphanOrigin.MadeOnSite ? T(LocKeys.ReviewMadeOnSite) : string.Empty;

    /// <summary>
    /// A gearset for a sentence: its name in quotes, and the number it carries in game when that is known.
    /// </summary>
    /// <param name="name">The name, already reduced to a dash where there is none.</param>
    /// <param name="setUid">Its identity, or <see langword="null"/>.</param>
    /// <returns>Text to drop into a sentence, quotes included.</returns>
    /// <remarks>
    /// <para>
    /// The name alone is not an identification, which is the whole subject of this feature: the game
    /// writes a new gearset's name from its job and never asks, so two sets of one job are called the same
    /// thing from the moment they exist. A card would then read "this is a copy of X" and, two lines
    /// below, "also resembles X", naming two different sets identically with nothing to tell them apart.
    /// </para>
    /// <para>
    /// The number was tried once before and taken back out, because the lookup behind it resolved the
    /// wrong one of two same-named sets: a label whose purpose is to separate them, wrong in exactly that
    /// case. It works now because the table it reads withdraws every claim it cannot be sure of, so a
    /// number is either right or absent. Absent is a real outcome here and not a failure: the set may be
    /// one the game no longer holds, which is why the row is on this card in the first place.
    /// </para>
    /// </remarks>
    private string Named(string name, string? setUid)
    {
        var number = setUid is { Length: > 0 } uid ? _liveNumber(uid) : null;
        return number is { } n ? $"\"{name}\" (#{n})" : $"\"{name}\"";
    }

    /// <summary>Team names for a sentence: each in quotes, so a name like "Test" reads as one.</summary>
    /// <param name="teams">The teams following the row.</param>
    /// <returns>The list, ready to drop into a sentence that already says whether it is one or several.</returns>
    private static string TeamList(IReadOnlyList<string> teams) =>
        string.Join(", ", teams.Select(t => $"\"{t}\""));

    /// <summary>A candidate name, or a dash, so a sentence never carries an empty pair of quotes.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>Its name.</returns>
    private static string NameOfCandidate(ReviewCandidate? candidate) =>
        candidate?.Name is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : "-";

    /// <summary>
    /// What the question is, in the sentence that names the way out of it. Only for the case where the
    /// selected candidate is the proposed one; a reader who picked another gets a sentence about that.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <param name="preselected">What the server proposes, when it proposes a row.</param>
    /// <returns>The sentence.</returns>
    private string QuestionAdvice(HeldGearset held, ReviewCandidate? preselected)
    {
        var target = Named(preselected?.Name is { Length: > 0 } n ? n : "-", preselected?.SetUid);
        return QuestionAdvisor.VerdictOf(held) switch
        {
            QuestionVerdict.ConfidentLink => T(LocKeys.ReviewQuestionSure, target),
            QuestionVerdict.UncertainLink => T(LocKeys.ReviewQuestionUnsure, target),
            QuestionVerdict.ProposesNew => T(LocKeys.ReviewQuestionNew),
            _ => T(LocKeys.ReviewQuestionNone),
        };
    }

    private void DrawCandidate(ReviewState state, HeldGearset held, ReviewCandidate candidate, bool proposed)
    {
        using var id = ImRaii.PushId(candidate.SetUid ?? string.Empty);

        var name = Named(string.IsNullOrWhiteSpace(candidate.Name) ? "-" : candidate.Name!, candidate.SetUid);
        var key = candidate.SetUid ?? string.Empty;

        // The shared head. With a picker directly above it, the name and the score are already on screen
        // and the headline drops them; without one there is nothing else on the card carrying them.
        var pairs = DrawComparisonHead(
            key,
            held.Items,
            candidate.Items,
            name,
            candidate.Probability,
            candidate.MatchedSlots,
            candidate.TotalSlots,
            candidate.Hidden
                ? $"{CandidateOrigin(candidate)}, {T(LocKeys.ReviewHiddenOnSite)}"
                : CandidateOrigin(candidate),

            // Same rule as the inventory card, which had it and this one did not: the website only where
            // the set cannot be looked at in game. A candidate the plugin governs is a gearset somewhere
            // in the player's list; a hand-made one exists nowhere else.
            string.Equals(candidate.Source, GearsetSource.Plugin, StringComparison.Ordinal)
                ? null
                : candidate.Url,
            T(LocKeys.ReviewShowInGame),
            nameInHeadline: held.Candidates.Count <= 1);

        ImGui.Dummy(new Vector2(0f, 3f));

        using (ImRaii.Disabled(Blocked))
        {
            // Only where the answer cannot be given once at the top: with a single candidate it is the
            // question's own answer and lives with the question, and repeating it here would be two
            // buttons doing one thing on one screen.
            // Both of these belong to the candidate, and with only one candidate the question carries them
            // for it, together and labelled in the row at the bottom. Drawing "stop asking" here as well
            // put one button doing one thing in two places on the same card.
            var onLine = true;
            {
                if (LabelledButton(
                    FontAwesomeIcon.Check,
                    T(LocKeys.ReviewThisIsIt),
                    CostOfLinking(candidate),
                    proposed ? Good with { W = 0.35f } : null))
                {
                    Gate(
                        new ReviewDecision
                        {
                            SetUid = held.SetUid!,
                            Action = ReviewAction.Link,
                            TargetUid = candidate.SetUid,
                        },
                        [CostOfLinking(candidate)]);
                }

                // "Stop asking" names the candidate, not the newcomer, so it belongs under the comparison
                // rather than up with the question: it archives the row that is on screen right now.
                ImGui.SameLine(0f, 14f);
                if (LabelledButton(FontAwesomeIcon.Archive, T(LocKeys.ReviewStopAsking), T(LocKeys.ReviewStopAskingMeans)))
                {
                    Decide(new ReviewDecision { SetUid = candidate.SetUid!, Action = ReviewAction.Ignore });
                }

            }

            // Said only when the jobs differ. The rungs require the job to agree anyway, so "same job" is
            // true on almost every candidate there will ever be, and a label that is nearly always there
            // carries no information and reads as a warning about nothing. The interesting case is the
            // other one: a base class offered for the job it becomes, where the row really is stored under
            // a different code than the set that just arrived.
            if (!candidate.SameJob && candidate.Job is { Length: > 0 } job)
            {
                if (onLine)
                {
                    ImGui.SameLine();
                }

                ImGui.AlignTextToFramePadding();
                Text(Muted, T(LocKeys.ReviewStoredAs, job));
                onLine = true;
            }

        }

        // Named, not vague. The uid comes out of this same answer, so the name is already here.
        if (!proposed && candidate.BlockedBy is { Length: > 0 } blocker)
        {
            Wrapped(Muted, T(LocKeys.ReviewBlockedBy, Named(NameOf(state, blocker), blocker)));
        }

        // Only where there is something to say. A line that appears every time teaches people to read past
        // it, including the times it matters.
        if (ReviewRules.IsAdoption(candidate))
        {
            Wrapped(Warn, CostOfLinking(candidate));
        }

        // Named on the row it is about, which the contract asks for outright: what a player is about to
        // answer away with "it is new" is worth a sentence, and here the sentence is reassurance rather
        // than warning, because answering that way leaves this row and everything on it exactly as it is.
        // The rewrite dropped both of these lines and the pin one only came back when a card carrying one
        // was in front of us.
        if (candidate.HasPin)
        {
            Wrapped(Muted, T(LocKeys.ReviewKeepsPin, name));
        }

        if (candidate.HasTeamShare && candidate.TeamNames.Count > 0)
        {
            Wrapped(Warn, T(
                candidate.TeamNames.Count == 1 ? LocKeys.ReviewKeepsShareOne : LocKeys.ReviewKeepsShareMany,
                TeamList(candidate.TeamNames),
                name));
        }

        ImGui.Dummy(new Vector2(0f, 4f));
        DrawComparison(pairs, "##cmpcand", caption: null);
        ImGui.Spacing();
    }

    /// <summary>
    /// What answering "this is that one" costs on this particular target, in one sentence.
    /// </summary>
    /// <param name="candidate">The row the newcomer would be linked onto.</param>
    /// <returns>The sentence.</returns>
    /// <remarks>
    /// <para>
    /// The one-way door is not the change of governance, it is the overwrite: the target keeps its uid,
    /// its pinned target, its shares and its hidden flag, and gets name, job, items, food, ilvl and
    /// position from the set that just came out of the game. That is true of every target, so all three
    /// sentences name it and only the price changes.
    /// </para>
    /// <para>
    /// A released row is the case that reads most wrongly without this. Being handed to the website is
    /// reversible, so "that cannot be undone" is the wrong warning; what cannot be undone is that the
    /// frozen contents, the whole reason somebody released it, are replaced by the live ones.
    /// </para>
    /// </remarks>
    private string CostOfLinking(ReviewCandidate candidate)
    {
        var name = Named(string.IsNullOrWhiteSpace(candidate.Name) ? "-" : candidate.Name!, candidate.SetUid);
        return candidate.ReleasedAt is { Length: > 0 } ? T(LocKeys.ReviewLinkReleased, name)
            : ReviewRules.IsAdoption(candidate) ? T(LocKeys.ReviewLinkHandMade, name)
            : T(LocKeys.ReviewLinkParked, name);
    }

    /// <summary>
    /// The bulk door, written out: every pair it would apply, each one strikeable, and the same list again
    /// in the confirmation.
    /// </summary>
    /// <param name="state">The reconciliation state as last read.</param>
    /// <remarks>
    /// <para>
    /// It used to be a button and a count. Agreeing to "all four proposals" meant paging through four
    /// carousel cards first and holding them in your head, and the strike-out that makes the door safe was
    /// scattered one per card, so the one screen that should have shown the whole mapping never existed.
    /// A player looking at it could not see what they were about to agree to, which is a bad property for
    /// the only control here that decides more than one thing at once.
    /// </para>
    /// <para>
    /// So the list is the control. One line per pair, the set out of the game on the left and the row it
    /// would become on the right, each with the strike that takes it out. Striking is the only lever the
    /// contract allows: a pair may be left out or weakened to "this is new", never re-pointed, because
    /// anything else would be a second mapping beside the one the server checked.
    /// </para>
    /// </remarks>
    private void DrawBulk(ReviewState state)
    {
        if (state.Held.Count < 3)
        {
            return;
        }

        // Only the player's own strikes go in. What may never ride along is decided by MappingToAccept,
        // which is also what composes the request, so the list on screen and the list on the wire cannot
        // disagree. Everything the panel needs is read back out of the result rather than worked out a
        // second time: which lines are in, how many could ever be, which are only listed.
        var pairs = ReviewRules.MappingToAccept(state, _struckOut);
        var included = new HashSet<string>(pairs.Select(p => p.SetUid), StringComparer.Ordinal);

        // The denominator is the same rule with nothing struck, not a count of questions that look safe.
        // Those differ: a question with no proposal at all passes SafeForBulk and can never be a pair, so
        // the button read "(2/3)" with nothing struck out and no way to reach three.
        var applicable = new HashSet<string>(
            ReviewRules.MappingToAccept(state).Select(p => p.SetUid),
            StringComparer.Ordinal);

        var backed = state.Held.Where(h => h.SetUid is { Length: > 0 } uid && applicable.Contains(uid)).ToList();
        var unbacked = state.Held.Where(h => h.SetUid is not { Length: > 0 } uid || !applicable.Contains(uid)).ToList();

        // Drawn inside a border, because it is a panel and not a paragraph. Without one it was a stretch of
        // text between the toolbar and the first card, and nothing said where the shortcut ended and the
        // question in front of you began. The frame is measured after the fact from the group's own extent
        // rather than guessed in advance, so wrapped lines cannot overrun it.
        ImGui.Dummy(new Vector2(0f, 2f));
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var inset = new Vector2(11f, 6f);

        // Tighter inside the frame than outside it. The window spacing is set for a page of prose with
        // decisions in it; this panel is a list of short pairings, where the same gaps read as a hole
        // between every line and push the button that applies them off the first screenful.
        using var boxSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 5f));

        ImGui.BeginGroup();
        ImGui.Indent(inset.X);

        Text(Accent, T(LocKeys.ReviewBulkHeader));

        if (backed.Count > 0)
        {
            DrawBulkGroup(backed, backed: true);
        }

        // Listed rather than hidden, and read-only. Where several stored rows fit, the pairing is a choice,
        // and this list is the one place in the window that cannot show a choice: /gear/review/accept takes
        // only the pairs the server proposed for this token, in a single transaction, so the alternative
        // could not be offered here even as an option. An opt-in button was the earlier answer and it was
        // wrong, because pressing it meant accepting a pick whose alternative was off screen. These belong
        // on the card, where the picker exists. Naming them keeps the counts honest: without the list, a
        // shortcut that applies two of three questions never says where the third went.
        if (unbacked.Count > 0)
        {
            // The sentence names the reason where one reason covers the whole group, which is the case
            // that happens: several stored rows fit and the shortcut cannot show a choice. A row can also
            // land here for a duller reason, an older server sending no proposal at all, and then the
            // specific sentence would be a claim about a row it does not describe.
            var allContested = unbacked.TrueForAll(h => h.Candidates.Count > 1);

            ImGui.Dummy(new Vector2(0f, 1f));
            Wrapped(Muted, T(allContested ? LocKeys.ReviewBulkUnbacked : LocKeys.ReviewBulkNotApplicable));
            DrawBulkGroup(unbacked, backed: false);
        }

        ImGui.Dummy(new Vector2(0f, 1f));

        if (pairs.Count == 0)
        {
            Wrapped(Muted, T(LocKeys.ReviewBulkNothingLeft));
        }
        else
        {
            var adoptions = ReviewRules.AdoptionCount(state, pairs);
            if (adoptions > 0)
            {
                Wrapped(
                    Warn,
                    adoptions == 1
                        ? T(LocKeys.ReviewAcceptAdoptionsOne)
                        : T(LocKeys.ReviewAcceptAdoptionsMany, adoptions));
            }

            using (ImRaii.Disabled(Blocked))
            {
                // How many of how many, not a bare count. "Accept all 1 proposals" was both ungrammatical
                // and a lie by omission: the one that had been struck out was still on screen above, and
                // nothing in the button said it was no longer part of what the press would do.
                if (LabelledButton(
                    FontAwesomeIcon.CheckDouble,
                    T(LocKeys.ReviewAcceptAll, pairs.Count, backed.Count),
                    T(LocKeys.ReviewBulkMeans)))
                {
                    // The confirmation repeats the list rather than a count. One press over eighteen
                    // pairings is exactly where "are you sure" has to say what it is sure about, and it
                    // walks the pairs that will be sent rather than re-deciding which those are.
                    var lines = new List<string>();
                    foreach (var held in state.Held)
                    {
                        if (held.SetUid is { Length: > 0 } uid && included.Contains(uid))
                        {
                            lines.Add($"{GameSide(held)}   {T(LocKeys.ReviewBulkBecomes)}   {ServerSide(held)}");
                        }
                    }

                    if (adoptions > 0)
                    {
                        lines.Add(T(LocKeys.ReviewAdoption));
                    }

                    _pending = new Pending(null, lines);
                }
            }
        }

        ImGui.Unindent(inset.X);
        ImGui.EndGroup();

        ImGui.GetWindowDrawList().AddRect(
            origin - new Vector2(0f, inset.Y),
            new Vector2(origin.X + width, ImGui.GetItemRectMax().Y + inset.Y),
            ImGui.GetColorU32(Accent with { W = 0.30f }),
            5f);

        ImGui.Dummy(new Vector2(0f, inset.Y + 2f));
    }

    /// <summary>One group of the mapping, as a table so the two halves line up.</summary>
    /// <param name="group">The questions in this group.</param>
    /// <param name="backed">
    /// Whether these are the ones the shortcut can apply. A backed pairing is in until it is struck out;
    /// the rest are listed for the count and cannot be toggled in at all.
    /// </param>
    private void DrawBulkGroup(IReadOnlyList<HeldGearset> group, bool backed)
    {
        using var table = ImRaii.Table($"##bulk{backed}", 4, ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit);
        if (!table)
        {
            return;
        }

        // An explicit width, not one sized to the content. The two groups are two tables, and the second
        // has no strike buttons in it, so this column collapsed to nothing there and every line in the
        // lower half sat one button's width to the left of the upper half. Reserving the room keeps the
        // two readable as one list, which is what they are.
        ImGui.TableSetupColumn("x", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("game", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("server", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("note", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        Text(Muted with { W = 0.7f }, T(LocKeys.ReviewBulkFromGame));
        ImGui.TableNextColumn();
        Text(Muted with { W = 0.7f }, T(LocKeys.ReviewBulkOnServer));
        ImGui.TableNextColumn();

        foreach (var held in group)
        {
            if (held.SetUid is not { Length: > 0 } uid)
            {
                continue;
            }

            using var id = ImRaii.PushId($"bulk{uid}");
            var inMapping = backed && !_struckOut.Contains(uid);
            var colour = inMapping ? Muted : Muted with { W = 0.4f };

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (backed && IconButton(
                inMapping ? FontAwesomeIcon.Times : FontAwesomeIcon.Plus,
                "toggle",
                T(inMapping ? LocKeys.ReviewStrikeOut : LocKeys.ReviewPutBack),
                inMapping ? Bad : Good,
                flat: true))
            {
                if (!_struckOut.Remove(uid))
                {
                    _struckOut.Add(uid);
                }
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Text(colour, GameSide(held));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Text(colour, ServerSide(held));

            // Never let the line hide that there was a choice. One pairing on screen where three rows
            // were plausible reads as settled, and it is not: it is the server's assignment, which is a
            // decision and worth saying so.
            ImGui.TableNextColumn();
            if (held.Candidates.Count > 1)
            {
                ImGui.AlignTextToFramePadding();
                Text(colour, T(LocKeys.ReviewBulkChoices, held.Candidates.Count));
            }
        }
    }

    /// <summary>The set as it stands in the game, with the number the player sees in the list.</summary>
    /// <param name="held">The question.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// <b>The number is the position plus one.</b> The game numbers its gearset list from 1 and every
    /// interface the player compares this against does the same; the API counts from 0. Printing the raw
    /// index would name a neighbouring set, which on a list of forty is worse than printing no number.
    /// </remarks>
    private string GameSide(HeldGearset held) => T(
        LocKeys.ReviewBulkGameSet,
        held.GearIndex + 1,
        held.Job ?? "?",
        string.IsNullOrWhiteSpace(held.Name) ? "-" : held.Name!);

    /// <summary>What the pairing would write it onto, or that it becomes a row of its own.</summary>
    /// <param name="held">The question.</param>
    /// <returns>The line.</returns>
    private string ServerSide(HeldGearset held) =>
        ReviewRules.Preselected(held) is { } target
            ? T(LocKeys.ReviewBulkServerRow, Named(string.IsNullOrWhiteSpace(target.Name) ? "-" : target.Name!, target.SetUid))
            : T(LocKeys.ReviewBulkServerNew);

    /// <summary>
    /// The inventory half: rows no gearset in game occupies. One card at a time, because the work is a
    /// sequence of decisions and not a list to browse. Eight rows with three buttons each on one page is
    /// twenty four buttons and no order to work through them in, which is what the first version was.
    /// </summary>
    /// <remarks>
    /// Rows already put aside stay a folded list. There the task really is browsing: somebody looking for
    /// one row they set aside by mistake, not somebody working through a queue.
    /// </remarks>
    private void DrawInventoryCard(OrphanRow row)
    {
        // Says which kind of card this is, since the arrows above it walk both kinds. Without the label a
        // reader stepping from a question into the inventory has no way to tell that the question changed
        // shape rather than the set changing.
        Text(Accent, T(LocKeys.ReviewInventory));

        // Judged before anything is drawn, because the bar of buttons has to mark the recommended one and
        // the bar comes first. Reading it inside the card would mark it one frame late.
        var advice = OrphanAdvisor.For(row, ComparisonFor(row));

        // Deciding is what somebody came here for, so the verbs sit above the evidence. Underneath the
        // card they were the last thing reached and the first thing needed.
        DrawVerbs(row, sameLine: false, advice.Recommended);
        DrawOrphanCard(row, advice);
    }

    /// <summary>
    /// The rows already put aside, folded away under a line that says how many.
    /// </summary>
    /// <param name="state">The reconciliation state as last read.</param>
    /// <remarks>
    /// Not part of the carousel, and deliberately. These are decided: a carousel is for what is waiting to
    /// be answered, and walking eight archived rows to reach the one question that matters would make the
    /// arrows worth less every time somebody puts something aside.
    /// </remarks>
    private void DrawAside(ReviewState state)
    {
        var aside = state.PutAsideOrphans.ToList();
        if (aside.Count == 0)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 4f));

        var asideLabel = aside.Count == 1
            ? T(LocKeys.ReviewAsideGroupOne)
            : T(LocKeys.ReviewAsideGroupMany, aside.Count);

        if (ImGui.Selectable(asideLabel, _showAside))
        {
            _showAside = !_showAside;
        }

        if (!_showAside)
        {
            return;
        }

        foreach (var row in aside)
        {
            DrawOrphanCard(row, advice: null, compact: true);
        }
    }

    /// <summary>
    /// Position and movement for a carousel. Moving decides nothing, which is what makes it safe to look
    /// around before answering.
    /// </summary>
    /// <param name="cursor">The current position, clamped by the caller.</param>
    /// <param name="count">How many there are.</param>
    /// <param name="id">Which carousel this is, so two of them on one screen keep their own buttons.</param>
    private void DrawStepper(ref int cursor, int count, string id)
    {
        if (count <= 1)
        {
            return;
        }

        using var scope = ImRaii.PushId(id);

        // Full-size arrows rather than the small variant. This is how somebody walks eight decisions, so
        // it is a control they aim at rather than one they hit.
        using (ImRaii.Disabled(cursor == 0))
        {
            if (ImGui.ArrowButton("##prev", ImGuiDir.Left))
            {
                cursor--;
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(cursor >= count - 1))
        {
            if (ImGui.ArrowButton("##next", ImGuiDir.Right))
            {
                cursor++;
            }
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        Text(Muted, T(LocKeys.ReviewPosition, cursor + 1, count));
    }

    /// <summary>
    /// One row as a card: what it is, where it came from, what it holds, what hangs on it, and the
    /// decision with the consequence beside each verb rather than one sentence under all of them.
    /// </summary>
    /// <param name="row">The row to decide about.</param>
    /// <param name="advice">What the row is and what to do, or nothing for the folded list.</param>
    /// <param name="compact">Leaves out the gear, for the folded list of rows already put aside.</param>
    private void DrawOrphanCard(OrphanRow row, OrphanAdvice? advice, bool compact = false)
    {
        using var id = ImRaii.PushId(row.SetUid ?? string.Empty);

        // A rule under the verbs, matching the one the question card draws under its own two answers: what
        // follows is the evidence, and what came before it is the decision.
        ImGui.Dummy(new Vector2(0f, 6f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 4f));

        // The header carries the identity, and the line under it says where the row came from. That used
        // to hide behind a "(?)" beside the name, on the argument that it is wanted once rather than on
        // every card. It is not an aside: a row somebody typed on the website and one a push reported in
        // spring want different answers, and the question card says so in words, so this one does too.
        var name = string.IsNullOrWhiteSpace(row.Name) ? "-" : row.Name;
        Text(Accent, $"{row.Job}   {name}");

        // This row is the one case where the website earns a button: no gearset in game occupies it, so
        // there is nowhere else to look at it.
        DrawSiteButton(row.Url, "rowsite");

        Text(
            Muted with { W = 0.75f },
            row.Hidden ? $"{OriginText(row)}, {T(LocKeys.ReviewHiddenOnSite)}" : OriginText(row));

        // Before the advice, not after the evidence. These two are what a delete would take with it, and
        // the advice ends with "otherwise delete": reading that first and learning four lines further down
        // that other people are following the row is the wrong order to be told things in. They used to
        // sit under the gear grid, where the argument for their position was that they must not hide
        // behind a hover. True, and not enough: in plain sight below the reason to act is still below it.
        if (row.HasPin)
        {
            Wrapped(Warn, T(LocKeys.ReviewCardHasPin));
        }

        if (row.HasTeamShare && row.TeamNames.Count > 0)
        {
            // The fact always, the consequence only where a delete could otherwise have been pressed. On a
            // row already out of the plugin there is nothing to take out and nothing to delete, and the
            // line was telling somebody to do both.
            var shared = T(
                row.TeamNames.Count == 1 ? LocKeys.ReviewCardHasShareOne : LocKeys.ReviewCardHasShareMany,
                TeamList(row.TeamNames));

            Wrapped(Warn, row.IsFromPlugin
                ? $"{shared} {T(LocKeys.ReviewCardShareBlocksDelete)}"
                : shared);
        }

        // What this row is and what to do about it, which is the only thing somebody opening this window
        // actually wants. Everything under it is the evidence.
        if (advice is not null)
        {
            DrawAdvice(row, advice);
        }

        // The gear is what somebody recognises a set by months later, so it is on the card and not behind
        // a toggle. Where the server named a set this one resembles, the two are drawn against each other,
        // because the gear alone says what is in the row and the comparison says whether it is needed.
        if (!compact)
        {
            DrawSimilar(row);
        }

        // The folded list of rows already put aside carries its own verbs, since there is no bar above it.
        if (compact)
        {
            DrawVerbs(row, sameLine: false, recommended: null);
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// What may be done to a row, as one bar of icons that each say what they do and what that costs.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="sameLine">Whether the bar continues the line above, next to the carousel arrows.</param>
    /// <param name="recommended">The verb the card names outright, ringed here, or nothing.</param>
    /// <remarks>
    /// <para>
    /// The consequences used to stand under the buttons as four grey sentences: four sentences to read
    /// before the first click, on a card that was already asking a lot. On hover they are one gesture away
    /// and the bar is legible at a glance.
    /// </para>
    /// <para>
    /// A row the server sent without an identity gets none of them. Every verb needs a uid to name, so
    /// buttons here could only ever come back as a 422, which is the same mistake as offering a candidate
    /// the server would refuse: the person did nothing wrong and gets an error for it.
    /// </para>
    /// </remarks>
    private void DrawVerbs(OrphanRow row, bool sameLine, string? recommended)
    {
        if (row.SetUid is not { Length: > 0 })
        {
            return;
        }

        using var id = ImRaii.PushId($"verbs{row.SetUid}");

        foreach (var verb in ReviewRules.OfferedVerbs(row))
        {
            if (sameLine)
            {
                ImGui.SameLine(0f, 14f);
            }

            sameLine = true;
            using var disabled = ImRaii.Disabled(Blocked);
            var tint = verb == ReviewAction.Delete ? Bad with { W = 0.45f } : (Vector4?)null;
            var origin = ImGui.GetCursorScreenPos();

            // In words, like every other answer in this window. These were three bare glyphs at the top of
            // the card, the only actions on it, and one of them removes a row and the target pinned to it
            // for good. An unlabelled icon is a matter of taste anywhere else; over a delete it is a red
            // square somebody is invited to guess at. It read worse still once the two kinds of card
            // shared one carousel, because pressing the arrow took a reader straight from a row of
            // labelled buttons to a row of glyphs doing entirely different things.
            if (LabelledButton(IconFor(verb), Label(verb), MeansOf(verb, row), tint))
            {
                Gate(new ReviewDecision { SetUid = row.SetUid!, Action = verb }, WarningsFor(verb, row));
            }

            // The one the card is about to name, ringed so the sentence and the button are visibly the
            // same thing. Nothing is preselected and nothing is armed: it is a pointer, not a default.
            if (string.Equals(verb, recommended, StringComparison.Ordinal))
            {
                ImGui.GetWindowDrawList().AddRect(
                    origin - new Vector2(2f, 2f),
                    origin + ImGui.GetItemRectSize() + new Vector2(2f, 2f),
                    ImGui.GetColorU32(Good),
                    4f,
                    ImDrawFlags.RoundCornersAll,
                    2f);
            }
        }
    }

    /// <summary>Text that wraps at the window edge instead of running past it.</summary>
    private static void Wrapped(Vector4 colour, string text)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, colour);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }


    /// <summary>
    /// Where the row came from, in one phrase, and never further than the data goes. A missing date used
    /// to print "never in game", which is a claim built out of an absence and was the opposite of the
    /// truth for every row a data migration brought in.
    /// </summary>
    /// <summary>The same phrase for a candidate, which carries the same two dates.</summary>
    /// <param name="candidate">The row being offered.</param>
    /// <returns>The phrase.</returns>
    private string CandidateOrigin(ReviewCandidate candidate) => ReviewRules.OriginOf(candidate) switch
    {
        OrphanOrigin.Released => T(LocKeys.ReviewReleasedOn, FormatWhen(candidate.ReleasedAt!)),
        OrphanOrigin.MadeOnSite => T(LocKeys.ReviewMadeOnSite),
        OrphanOrigin.LastReported => T(LocKeys.ReviewLastSeen, FormatWhen(candidate.LastSeenAt!)),
        _ => T(LocKeys.ReviewLastSeenUnknown),
    };

    private string OriginText(OrphanRow row) => ReviewRules.OriginOf(row) switch
    {
        OrphanOrigin.Released => T(LocKeys.ReviewReleasedOn, FormatWhen(row.ReleasedAt!)),
        OrphanOrigin.MadeOnSite => T(LocKeys.ReviewMadeOnSite),
        OrphanOrigin.LastReported => T(LocKeys.ReviewLastSeen, FormatWhen(row.LastSeenAt!)),
        _ => T(LocKeys.ReviewLastSeenUnknown),
    };

    /// <summary>The server's timestamp as a date in the reader's language; the raw value if it will not parse.</summary>
    private string FormatWhen(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
            ? when.ToLocalTime().ToString("d", _localizer.Language == Localizer.German ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture)
            : iso;
    /// <summary>What a verb does to this row, in one phrase, beside the button that does it.</summary>
    private string MeansOf(string verb, OrphanRow row) => verb switch
    {
        ReviewAction.Delete => row.IsFromPlugin && !row.IsPutAside
            ? T(LocKeys.ReviewDeleteComesBack)
            : T(LocKeys.ReviewDeleteMeans),
        ReviewAction.Release => T(LocKeys.ReviewReleaseOrphan),

        // On a released row this button is not the undo of an ignore, it is the undo of the release, and
        // saying "counts as open again" there was the sentence that made a one-way door look harmless.
        ReviewAction.Reopen when row.WasReleased => T(LocKeys.ReviewReopenUndoesRelease),
        ReviewAction.Reopen => T(LocKeys.ReviewReopenMeans),
        _ => T(LocKeys.ReviewIgnoreMeans),
    };

    /// <summary>
    /// The gear of a set as a strip of icons, in the order the game shows a character sheet, with the item
    /// name on hover. A slot the set does not fill is left out rather than drawn empty: this is a
    /// recognition aid, not an inventory of holes.
    /// </summary>
    private void DrawGearStrip(Dictionary<string, ItemDto> items)
    {
        if (items.Count == 0)
        {
            Text(Muted, T(LocKeys.ReviewNoItems));
            return;
        }

        var drawn = 0;
        foreach (var slot in EquipmentSlots.DisplayOrder)
        {
            if (!items.TryGetValue(slot, out var item) || item.Id <= 0)
            {
                continue;
            }

            if (drawn > 0)
            {
                ImGui.SameLine();
            }

            DrawIcon(item.Id, IconSize);
            if (ImGui.IsItemHovered())
            {
                Tooltip($"{T(SlotNames.LocKey(slot))}: {_itemName(item.Id)}");
            }

            drawn++;
        }

        if (drawn == 0)
        {
            Text(Muted, T(LocKeys.ReviewNoItems));
        }
    }

    /// <summary>One item icon, or an empty square of the same size when the game has none for it.</summary>
    private void DrawIcon(int itemId, float size)
    {
        var iconId = _itemIcon(itemId);
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        var wrap = _textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
    }

    // The character-screen layout, the same one the gear window uses: each row holds a left and a right
    // slot at the same height. Two windows showing the same gear in two arrangements would be two things
    // to learn instead of one.
    /// <summary>
    /// Slots a job may legitimately not have at all, as opposed to have and leave empty.
    /// </summary>
    /// <remarks>
    /// Only the off hand. A Paladin carries a shield there and a crafter or gatherer a second tool, so on
    /// those sets the slot is filled and never reaches the placeholder; on a Dragoon nothing in the game
    /// can ever go there, and an outline around it invites the reader to look for what is missing.
    /// </remarks>
    private static readonly IReadOnlySet<string> OptionalSlots =
        new HashSet<string>(["OffHand"], StringComparer.Ordinal);

    private static readonly (string Left, string Right)[] GridRows =
    [
        ("Weapon", "OffHand"),
        ("Head", "Ears"),
        ("Body", "Neck"),
        ("Hands", "Wrists"),
        ("Legs", "RingLeft"),
        ("Feet", "RingRight"),
    ];

    /// <summary>
    /// Two sets as one grid: this row's gear, each tile bordered in the colour of how that slot relates to
    /// the other set, and the other side named wherever the two disagree.
    /// </summary>
    /// <param name="pairs">The comparison, already computed.</param>
    /// <param name="tableId">Something unique, so two comparisons on one card keep their own columns.</param>
    /// <param name="caption">
    /// What the tiles are and what they are held against, or <see langword="null"/> where the line above
    /// the grid already says it. On the inventory card it does, and repeating it there put the same fact
    /// on screen three times.
    /// </param>
    /// <remarks>
    /// <para>
    /// One grid and not two strips. A set that agrees everywhere drawn twice is the same picture twice,
    /// and the reader has to find the difference by scanning back and forth between two rows of small
    /// squares. Here the tiles that agree are quiet and the ones that do not carry the other item's name
    /// beside them, so the difference is read rather than searched for.
    /// </para>
    /// <para>
    /// The colour never carries a statement on its own: every deviation is also written out beside its
    /// tile, the counts are spelled out underneath, and hovering any tile names the state. That is not
    /// only for people who cannot separate the hues.
    /// </para>
    /// </remarks>
    private void DrawComparison(IReadOnlyList<SlotPair> pairs, string tableId, string? caption)
    {
        if (pairs.Count == 0)
        {
            Text(Muted, T(LocKeys.ReviewNoItems));
            return;
        }

        // Which half of the pair the tiles are, where the surrounding text does not already say it.
        if (caption is { Length: > 0 })
        {
            Text(Muted with { W = 0.7f }, caption);
        }

        var bySlot = new Dictionary<string, SlotPair>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            bySlot[pair.Slot] = pair;
        }

        // Two slots per row means the right-hand pair's icon lands directly against the left-hand pair's
        // wrapped name, and at two lines of text per slot that reads as one paragraph with pictures in it.
        // The padding is what separates a tile from the text that is not about it.
        using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(9f, 6f)))
        using (var table = ImRaii.Table(tableId, 4, ImGuiTableFlags.PadOuterX))
        {
            if (table)
            {
                ImGui.TableSetupColumn("li", ImGuiTableColumnFlags.WidthFixed, TileSize);
                ImGui.TableSetupColumn("ld", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("ri", ImGuiTableColumnFlags.WidthFixed, TileSize);
                ImGui.TableSetupColumn("rd", ImGuiTableColumnFlags.WidthStretch);

                foreach (var (left, right) in GridRows)
                {
                    ImGui.TableNextRow();
                    DrawSlotCells(tableId, left, bySlot);
                    DrawSlotCells(tableId, right, bySlot);
                }
            }
        }

        // Anything the server sends that this build has no place for. Never normally drawn, and better
        // than a slot that quietly is not in the comparison at all.
        foreach (var pair in pairs)
        {
            if (!EquipmentSlots.ValidKeys.Contains(pair.Slot))
            {
                Text(ColourOf(pair.Agreement), $"{pair.Slot}: {WordFor(pair)}");
            }
        }

        // The counts belong to whoever placed the grid: the inventory card carries them in its headline,
        // the question card under the grid, because there the line above is already the server's numbers.
        if (caption is { Length: > 0 })
        {
            var (same, materia, different) = SetComparison.Counts(pairs);
            Wrapped(Muted, T(LocKeys.ReviewSimilarCounts, same, materia, different));
        }
    }

    /// <summary>One slot as two cells: the bordered tile, and the name with the other side beside it.</summary>
    /// <param name="tableId">The grid this belongs to, so ids stay unique.</param>
    /// <param name="slotKey">The slot.</param>
    /// <param name="bySlot">The comparison, keyed by slot.</param>
    private void DrawSlotCells(string tableId, string slotKey, Dictionary<string, SlotPair> bySlot)
    {
        var found = bySlot.TryGetValue(slotKey, out var pair);

        ImGui.TableNextColumn();
        DrawCompareTile(tableId, slotKey, found ? pair : null);

        ImGui.TableNextColumn();
        if (found)
        {
            DrawCompareDetail(pair!);
        }
    }

    /// <summary>
    /// The tile: this side's piece, in a border the colour of how the slot relates. A slot only the other
    /// side fills gets a faint placeholder, so the two halves of the grid stay in step.
    /// </summary>
    /// <param name="tableId">The grid this belongs to.</param>
    /// <param name="slotKey">The slot.</param>
    /// <param name="pair">The comparison for it, or nothing when neither side fills it.</param>
    private void DrawCompareTile(string tableId, string slotKey, SlotPair? pair)
    {
        var size = new Vector2(TileSize, TileSize);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (pair is null)
        {
            // An empty slot and a slot the job does not have are two different things, and only one of
            // them is worth an outline. On everything but the off hand, neither side filling it is a
            // finding: the placeholder keeps the two halves of the grid in step and says "nothing here".
            // The off hand is the one slot a job may simply not own, so an outline there reads as missing
            // data on a Dragoon. It stays for a Paladin's shield and for a crafter's or gatherer's second
            // tool, because those sets do fill it and the pair is then not null at all. The cell is
            // occupied either way, so the layout does not move.
            if (!OptionalSlots.Contains(slotKey))
            {
                drawList.AddRect(origin, origin + size, ImGui.GetColorU32(Muted with { W = 0.16f }), 4f, ImDrawFlags.RoundCornersAll, 1f);
            }

            ImGui.Dummy(size);
            return;
        }

        if (pair.Mine is { } mine)
        {
            DrawIcon(mine.Id, TileSize);
            ImGui.SetCursorScreenPos(origin);
        }

        ImGui.InvisibleButton($"##tile{tableId}_{slotKey}", size);
        drawList.AddRect(
            origin,
            origin + size,
            ImGui.GetColorU32(ColourOf(pair.Agreement)),
            4f,
            ImDrawFlags.RoundCornersAll,
            pair.Agreement == SlotAgreement.Same ? 1.5f : 2.5f);

        if (ImGui.IsItemHovered())
        {
            Tooltip(SlotTooltip(pair));
        }
    }

    /// <summary>
    /// The name beside a tile, and where the two sides disagree, what the other one has there instead.
    /// </summary>
    /// <param name="pair">The slot.</param>
    /// <remarks>
    /// A slot that agrees is written in the quiet colour and one that does not is written in its own, so
    /// the eye goes to what differs rather than to a wall of green. The other side is named only where it
    /// is a difference: repeating an identical name under every tile is noise dressed as thoroughness.
    /// </remarks>
    private void DrawCompareDetail(SlotPair pair)
    {
        var mine = pair.Mine is { } item ? _itemName(item.Id) : T(LocKeys.ReviewCompareNothing);

        // The name stays in the quiet colour whatever the slot does. Four item names in red under a red
        // delete button read as "something is broken here", and nothing is: red means "another piece".
        // The state is carried by the tile's border and, where it matters, by the line underneath.
        using (ImRaii.PushColor(ImGuiCol.Text, Muted))
        {
            ImGui.TextWrapped(mine);
        }

        if (pair.Agreement == SlotAgreement.Same)
        {
            return;
        }

        var theirs = pair.Agreement == SlotAgreement.MateriaDiffers
            ? WordFor(pair)
            : pair.Theirs is { } other ? _itemName(other.Id) : T(LocKeys.ReviewCompareNothing);

        Wrapped(Muted, T(LocKeys.ReviewCompareOther, theirs));
    }

    /// <summary>The colour of a slot state.</summary>
    /// <param name="agreement">The state.</param>
    /// <returns>Its colour.</returns>
    private static Vector4 ColourOf(SlotAgreement agreement) => agreement switch
    {
        SlotAgreement.Same => Good,
        SlotAgreement.MateriaDiffers => Amber,
        _ => Bad,
    };

    /// <summary>What one slot says on hover: which slot, both sides of it, and how the two relate.</summary>
    /// <param name="pair">The slot.</param>
    /// <returns>The tooltip text.</returns>
    private string SlotTooltip(SlotPair pair)
    {
        var slot = T(SlotNames.LocKey(pair.Slot));
        var mine = pair.Mine is { } a ? _itemName(a.Id) : T(LocKeys.ReviewCompareNothing);
        var theirs = pair.Theirs is { } b ? _itemName(b.Id) : T(LocKeys.ReviewCompareNothing);
        return $"{slot}\n{mine}\n{T(LocKeys.ReviewCompareOther, theirs)}\n{WordFor(pair)}";
    }

    /// <summary>The state of a slot in words, because a colour alone is a statement nobody can quote.</summary>
    /// <param name="pair">The slot.</param>
    /// <returns>The word for its state.</returns>
    private string WordFor(SlotPair pair) => pair.Agreement switch
    {
        SlotAgreement.Same => T(LocKeys.ReviewSlotSame),
        SlotAgreement.MateriaDiffers => T(LocKeys.ReviewSlotMateria),
        _ when pair.Theirs is null => T(LocKeys.ReviewSlotOnlyHere),
        _ when pair.Mine is null => T(LocKeys.ReviewSlotOnlyThere),
        _ => T(LocKeys.ReviewSlotOther),
    };

    /// <summary>
    /// What this row is and what to do about it, in two sentences, above the evidence for both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window that lays out a comparison and then falls silent has done the easy half. "73 %, eight of
    /// eleven slots, three different" is a measurement, and somebody who opens this for the first time
    /// cannot get from it to a decision without knowing everything this window knows. So the card says it:
    /// what the row is, and which of the buttons above answers it.
    /// </para>
    /// <para>
    /// The judgement is the plugin's own. The server proposes an attribution for a question and says
    /// nothing about a row in the inventory, deliberately; what it supplies instead is the resemblance,
    /// whose stated purpose is exactly this decision. <see cref="OrphanAdvisor"/> names a verb only where
    /// naming one cannot be wrong, and otherwise gives the question that decides it, which is the honest
    /// shape of "it depends".
    /// </para>
    /// </remarks>
    /// <param name="row">The row, for the one case where the closing sentence cannot be followed.</param>
    /// <param name="advice">The judgement, made before the buttons were drawn so they can mark it.</param>
    private void DrawAdvice(OrphanRow row, OrphanAdvice advice)
    {
        var other = Named(
            string.IsNullOrWhiteSpace(advice.OtherName) ? "-" : advice.OtherName!,
            advice.OtherUid);

        var (what, todo) = advice.Verdict switch
        {
            OrphanVerdict.Released => (T(LocKeys.ReviewAdviceReleased), T(LocKeys.ReviewAdviceReleasedDo)),
            OrphanVerdict.HandMade => (T(LocKeys.ReviewAdviceHandMade), T(LocKeys.ReviewAdviceHandMadeDo)),
            OrphanVerdict.Unique => (T(LocKeys.ReviewAdviceUnique), T(LocKeys.ReviewAdviceUniqueDo)),
            OrphanVerdict.Copy => (T(LocKeys.ReviewAdviceCopy, other), T(LocKeys.ReviewAdviceCopyDo, other)),
            OrphanVerdict.SameGearOtherMateria =>
                (T(LocKeys.ReviewAdviceMateria, other), T(LocKeys.ReviewAdviceMateriaDo, other)),
            _ => (
                advice.DifferentSlots == 1
                    ? T(LocKeys.ReviewAdviceSimilarOne, other)
                    : T(LocKeys.ReviewAdviceSimilar, other, advice.DifferentSlots),
                T(LocKeys.ReviewAdviceSimilarDo)),
        };

        Wrapped(Accent, what);

        // Every one of those closing sentences names deleting, and on a row a team follows there is no
        // delete to name: the button is gone and the line above says why. Advice that recommends a verb
        // the card does not offer is the window contradicting itself two lines apart. What replaces it is
        // the thing the reader would ask next, which is how to delete it for real.
        Wrapped(
            advice.Recommended is null ? Muted : Good,
            row.HasTeamShare ? T(LocKeys.ReviewAdviceUnshareFirst) : todo);
    }

    /// <summary>
    /// The row against the set it most resembles, or nothing when it resembles none. Computed in one place
    /// because the advice and the grid must be looking at the same pair.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The comparison, empty when there is nothing to compare it to.</returns>
    private static IReadOnlyList<SlotPair> ComparisonFor(OrphanRow row) =>
        row.Similar.Count > 0 ? SetComparison.Compare(row.Items, row.Similar[0].Items) : [];

    /// <summary>
    /// What a row that the game no longer reports looks like, held against the sets that are still there.
    /// </summary>
    /// <param name="row">The row being decided about.</param>
    /// <remarks>
    /// This is the part that answers the question somebody actually has in front of a row from months
    /// ago. It is not "what was this set", which nobody remembers, but "do I still need it", and a row
    /// that shares every piece and every meld with one that is still there has answered it.
    /// </remarks>
    private void DrawSimilar(OrphanRow row)
    {
        if (row.Similar.Count == 0)
        {
            // Not silence: nothing resembling it is a reason to keep a row, and saying so is cheap.
            DrawGearStrip(row.Items);
            Wrapped(Muted, T(LocKeys.ReviewSimilarNone));
            return;
        }

        var best = row.Similar[0];
        var name = NamedSimilar(best);
        var key = row.SetUid ?? string.Empty;

        // What this block is, before it is drawn. The question card puts the same shape over a candidate,
        // where it is an offer to be answered; here nothing acts on the rows listed and the comparison is
        // evidence for one decision, keep or delete. Sharing the drawing was right and left the two
        // reading alike, so the difference has to be said in words.
        Wrapped(Muted with { W = 0.75f }, T(LocKeys.ReviewSimilarPurpose));

        // The same head the question card draws, from the same method. One line where there used to be
        // four: the verdict above, this headline, a caption naming the sides and a row of counts under the
        // grid were four accountings of one pair, and two of them looked as if they disagreed, because the
        // server counts the piece and this side counts the melds. The slot count stays on the hover, with
        // the sentence that explains the difference.
        //
        // The website only where the set cannot be looked at in game. A row the plugin governs is a
        // gearset the player has in front of them; sending them to a browser for it is a detour dressed
        // as a feature. A hand-made row was never in game, and then the site is the only place it exists.
        // Folded away by default, and only here. The question about this card is keep or delete, and it is
        // answered by the verdict two lines up and by the counts one line up; twelve tiles are the working
        // out, for the reader who wants to check it. On a row nothing resembles the gear stays out in the
        // open, because there it is the only thing anybody would recognise the row by months later.
        var showGear = _showGear.Contains(key);

        var pairs = DrawComparisonHead(
            key,
            row.Items,
            best.Items,
            name,
            best.Probability,
            best.MatchedSlots,
            best.TotalSlots,
            origin: SimilarOrigin(best),
            url: string.Equals(best.Source, GearsetSource.Plugin, StringComparison.Ordinal) ? null : best.Url,
            backTooltip: T(LocKeys.ReviewShowThisRow),
            allowSwap: showGear);

        ImGui.SameLine(0f, 6f);
        if (LabelledButton(
            showGear ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
            T(showGear ? LocKeys.ReviewHideGear : LocKeys.ReviewShowGear),
            T(LocKeys.ReviewShowGearMeans)))
        {
            if (!_showGear.Remove(key))
            {
                _showGear.Add(key);
            }
        }

        if (showGear)
        {
            ImGui.Dummy(new Vector2(0f, 3f));
            DrawComparison(pairs, "##cmpsimilar", caption: null);
        }

        // The verdict about this pair is not repeated here. It is the first thing on the card, above the
        // grid that justifies it, and saying it twice would make the card longer without making it clearer.

        // The weaker ones get a line and not a second comparison. Three drawings of the same shape is
        // where a card stops being read.
        for (var i = 1; i < row.Similar.Count; i++)
        {
            var other = row.Similar[i];
            Text(Muted, T(LocKeys.ReviewSimilarMore, NamedSimilar(other), other.Probability));
        }
    }

    /// <summary>
    /// The head of a comparison between two sets, wherever two sets are held against each other.
    /// </summary>
    /// <param name="key">What the swap state is remembered under.</param>
    /// <param name="mine">The set the tiles show while the comparison is the right way round.</param>
    /// <param name="theirs">The set it is held against.</param>
    /// <param name="otherName">What to call the other set.</param>
    /// <param name="probability">The server's score for the pair.</param>
    /// <param name="matchedSlots">The server's own numerator, for the hover.</param>
    /// <param name="totalSlots">The server's own denominator, for the hover.</param>
    /// <param name="origin">Where the other row came from, or empty where the card says it elsewhere.</param>
    /// <param name="url">The other row on the website, or nothing where the button does not belong.</param>
    /// <param name="backTooltip">What turning the comparison back means, which differs by card.</param>
    /// <param name="allowSwap">Whether the control that turns the comparison round belongs on screen.</param>
    /// <param name="nameInHeadline">
    /// Whether the headline names the other set. False where a picker directly above already does, which
    /// would otherwise put the same name twice on one card.
    /// </param>
    /// <returns>The comparison, so the caller can draw the grid where it wants it.</returns>
    /// <remarks>
    /// One method because it is one thing. The question card and the inventory card each grew their own
    /// version of this, and they drifted: an origin written out on one and folded behind a "(?)" on the
    /// other, a swap button sitting inside the sentence on one after it had been moved out on the other.
    /// Two pieces of code doing the same job will always end up looking like two different features.
    /// </remarks>
    private IReadOnlyList<SlotPair> DrawComparisonHead(
        string key,
        IReadOnlyDictionary<string, ItemDto>? mine,
        IReadOnlyDictionary<string, ItemDto>? theirs,
        string otherName,
        int probability,
        int matchedSlots,
        int totalSlots,
        string origin,
        string? url,
        string backTooltip,
        bool nameInHeadline = true,
        bool allowSwap = true)
    {
        var showOther = _showOther.Contains(key);
        var pairs = showOther
            ? SetComparison.Compare(theirs, mine)
            : SetComparison.Compare(mine, theirs);

        var (same, materia, different) = SetComparison.Counts(pairs);

        // Written in the quiet colour whatever the server proposed. It used to go green on the proposed
        // candidate, which put a reassuring colour over a line reading "9 different" and made the colour
        // argue with its own text. That a row is the suggestion is said in words.
        Wrapped(
            Muted,
            nameInHeadline
                ? T(
                    showOther ? LocKeys.ReviewSimilarToFlipped : LocKeys.ReviewSimilarTo,
                    otherName,
                    probability,
                    same,
                    materia,
                    different)
                : T(showOther ? LocKeys.ReviewCountsFlipped : LocKeys.ReviewCounts, same, materia, different));

        if (ImGui.IsItemHovered())
        {
            Tooltip(T(LocKeys.ReviewSimilarServerCount, matchedSlots, totalSlots, probability));
        }

        // On a line of its own, and in words. This is evidence and not an aside: a row a push reported an
        // hour ago and one somebody typed on the website by hand are not equally likely to be the set that
        // just arrived, and on a hand-made row it is the whole explanation for a score in the teens.
        // Behind a bare "(?)" it was information nobody would find, and the marker read like a string that
        // had failed to load.
        if (origin.Length > 0)
        {
            Text(Muted with { W = 0.75f }, origin);
        }

        // And the two controls that look rather than decide, on a line of their own. They used to sit
        // inside the headline, where pressing the swap rewrote the sentence they were standing in and
        // shifted everything after them out from under the cursor. The label says what the button does
        // rather than what it will show, so it keeps its width and nothing moves at all.
        if (url is { Length: > 0 })
        {
            if (IconButton(FontAwesomeIcon.Globe, $"headsite{key}", T(LocKeys.ReviewOpenOnSite)))
            {
                _openLink(url);
            }

            ImGui.SameLine(0f, 6f);
        }

        // Not offered while the gear is folded away: it turns the tiles round, and with no tiles on screen
        // it is a button that does nothing a reader can see.
        if (allowSwap && LabelledButton(
            showOther ? FontAwesomeIcon.Undo : FontAwesomeIcon.Eye,
            T(LocKeys.ReviewSwapSides),
            showOther ? backTooltip : T(LocKeys.ReviewShowOther, otherName)))
        {
            if (!_showOther.Remove(key))
            {
                _showOther.Add(key);
            }
        }

        return pairs;
    }

    /// <summary>How wide a labelled button will be, without drawing one.</summary>
    /// <param name="icon">The icon it carries.</param>
    /// <param name="label">Its word.</param>
    /// <returns>The width in pixels, by the same arithmetic <see cref="LabelledButton"/> uses.</returns>
    private static float LabelledButtonWidth(FontAwesomeIcon icon, string label)
    {
        Vector2 glyphSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            glyphSize = ImGui.CalcTextSize(icon.ToIconString());
        }

        return glyphSize.X
            + ImGui.GetStyle().ItemInnerSpacing.X
            + ImGui.CalcTextSize(label).X
            + (ImGui.GetStyle().FramePadding.X * 2f);
    }

    /// <summary>
    /// The width below which this window stops working, taken from the row of buttons that cannot wrap.
    /// </summary>
    /// <returns>The narrowest width at which every button row still fits on one line.</returns>
    /// <remarks>
    /// Two rows are candidates for the widest: the answers to the question, and the answers about the
    /// candidate with the two view controls after them. Everything else on the card is prose, which wraps,
    /// or the gear grid, which is a table and shrinks its columns. Buttons do neither: below their own
    /// width they leave the window rather than getting smaller, which is what the screenshot of a
    /// dragged-in window shows.
    /// </remarks>
    private float NarrowestUsableWidth()
    {
        var question =
            LabelledButtonWidth(FontAwesomeIcon.Plus, T(LocKeys.ReviewItIsNew))
            + 14f
            + LabelledButtonWidth(FontAwesomeIcon.Unlink, T(LocKeys.ReviewTakeOut));

        var candidate =
            LabelledButtonWidth(FontAwesomeIcon.Check, T(LocKeys.ReviewThisIsIt))
            + 14f
            + LabelledButtonWidth(FontAwesomeIcon.Archive, T(LocKeys.ReviewStopAsking));

        // The shared comparison head puts its two controls on a line of their own, so they are measured
        // against the button rows rather than added to one.
        var looking =
            ImGui.GetFrameHeight()
            + 6f
            + LabelledButtonWidth(FontAwesomeIcon.Eye, T(LocKeys.ReviewSwapSides));

        candidate = Math.Max(candidate, looking);

        // The inventory card's verbs, which became words at the same time and are the widest row here: a
        // parked plugin row offers all three at once.
        var verbs =
            Math.Max(
                LabelledButtonWidth(FontAwesomeIcon.Archive, T(LocKeys.ReviewIgnore)),
                LabelledButtonWidth(FontAwesomeIcon.Undo, T(LocKeys.ReviewReopen)))
            + 14f
            + LabelledButtonWidth(FontAwesomeIcon.Unlink, T(LocKeys.ReviewTakeOut))
            + 14f
            + LabelledButtonWidth(FontAwesomeIcon.Trash, T(LocKeys.ReviewDelete));

        candidate = Math.Max(candidate, verbs);

        // Plus the window's own margins and the scrollbar, which is present whenever the grid is.
        var buttons = Math.Max(question, candidate)
            + (ImGui.GetStyle().WindowPadding.X * 2f)
            + ImGui.GetStyle().ScrollbarSize
            + 8f;

        // And a floor under that, because fitting the buttons is necessary and not sufficient: at exactly
        // that width the gear grid's two halves are squeezed to nothing and every item name wraps to three
        // lines. ComfortableWidth is where the window was actually being used when it read well.
        return Math.Max(buttons, ComfortableWidth);
    }

    /// <summary>
    /// The smallest this window may be dragged to, in both directions.
    /// </summary>
    /// <returns>The floor, capped so it can never exceed the screen it is being drawn on.</returns>
    /// <remarks>
    /// The width has a measured part, so a longer translation raises it rather than overflowing. The
    /// height has none worth having: what makes the window usable is seeing a card and its gear at once,
    /// which is a judgement about reading and not an arithmetic about controls. Both are capped against
    /// the display, since a floor larger than the screen is a window nobody can move.
    /// </remarks>
    private Vector2 SmallestUsableSize()
    {
        var display = ImGui.GetIO().DisplaySize;
        return new Vector2(
            Math.Min(NarrowestUsableWidth(), display.X * 0.9f),
            Math.Min(ComfortableHeight, display.Y * 0.9f));
    }

    /// <summary>A button carrying an icon and a word, so it reads without being hovered.</summary>
    /// <param name="icon">The icon, drawn in front of the label.</param>
    /// <param name="label">What it does, in words. Also the id, so keep it distinct within a scope.</param>
    /// <param name="tooltip">What it costs, for the reader who wants more than the word.</param>
    /// <param name="tint">A background colour, for the answer the card names as the ordinary one.</param>
    /// <returns><see langword="true"/> when it was clicked.</returns>
    /// <remarks>
    /// ImGui draws one font per string, so an icon and a word cannot share a label. The button is drawn
    /// empty at the measured size and the two are painted into it, which is the same thing the status
    /// window does for its menu rows.
    /// </remarks>
    private static bool LabelledButton(FontAwesomeIcon icon, string label, string tooltip, Vector4? tint = null)
    {
        string glyph;
        Vector2 glyphSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            glyph = icon.ToIconString();
            glyphSize = ImGui.CalcTextSize(glyph);
        }

        var labelSize = ImGui.CalcTextSize(label);
        var padding = ImGui.GetStyle().FramePadding;
        var gap = ImGui.GetStyle().ItemInnerSpacing.X;
        var size = new Vector2(
            glyphSize.X + gap + labelSize.X + (padding.X * 2f),
            Math.Max(glyphSize.Y, labelSize.Y) + (padding.Y * 2f));

        var origin = ImGui.GetCursorScreenPos();
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Button, tint ?? default, tint is not null))
        {
            clicked = ImGui.Button($"##{label}", size);
        }

        var draw = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGui.IsItemActive() || !ImGui.IsItemHovered() ? ImGuiCol.Text : ImGuiCol.Text);
        var midY = origin.Y + (size.Y / 2f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            draw.AddText(new Vector2(origin.X + padding.X, midY - (glyphSize.Y / 2f)), colour, glyph);
        }

        draw.AddText(new Vector2(origin.X + padding.X + glyphSize.X + gap, midY - (labelSize.Y / 2f)), colour, label);

        if (ImGui.IsItemHovered())
        {
            Tooltip(tooltip);
        }

        return clicked;
    }

    /// <summary>A small button carrying an icon, with its meaning on hover.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="id">Something unique within the current id scope.</param>
    /// <param name="tooltip">What it does, and what that costs where it costs something.</param>
    /// <param name="tint">A colour for the icon, for the one button that removes something.</param>
    /// <param name="flat">Drops the frame, for an icon that belongs to a sentence rather than to a toolbar.</param>
    /// <returns><see langword="true"/> when it was clicked.</returns>
    /// <remarks>
    /// The tooltip is not decoration here: an icon-only button is a guess until somebody hovers it, so
    /// every one of these has to say what it does, and the ones that cannot be undone say that too.
    /// </remarks>
    private static bool IconButton(FontAwesomeIcon icon, string id, string tooltip, Vector4? tint = null, bool flat = false)
    {
        bool clicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, tint ?? default, tint is not null))

        // A flat one belongs to the sentence beside it rather than to the bar of actions above. With a
        // frame it read as a fourth button that had wandered into the text, which is what it looked like.
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f), flat))
        {
            clicked = ImGui.Button($"{icon.ToIconString()}##{id}");
        }

        if (ImGui.IsItemHovered())
        {
            Tooltip(tooltip);
        }

        return clicked;
    }

    /// <summary>The icon for a verb, so a row of buttons reads without being read.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>Its icon.</returns>
    private static FontAwesomeIcon IconFor(string verb) => verb switch
    {
        ReviewAction.Reopen => FontAwesomeIcon.Undo,
        ReviewAction.Delete => FontAwesomeIcon.Trash,
        ReviewAction.Release => FontAwesomeIcon.Unlink,
        _ => FontAwesomeIcon.Archive,
    };

    private string Label(string verb) => verb switch
    {
        ReviewAction.Reopen => T(LocKeys.ReviewReopen),
        ReviewAction.Delete => T(LocKeys.ReviewDelete),
        ReviewAction.Release => T(LocKeys.ReviewTakeOut),
        _ => T(LocKeys.ReviewIgnore),
    };


    /// <summary>The way to the row on the website, as a mark beside whatever names the row.</summary>
    /// <param name="url">The address, or nothing when the server sent none.</param>
    /// <param name="id">Something unique, since a card can name more than one set.</param>
    /// <param name="gap">The space before it, or a negative value for the ordinary item spacing.</param>
    /// <remarks>
    /// Beside the name it belongs to, and never twice on one card. Two buttons reading "open on the
    /// website" under one another is a card asking which set the reader meant, which is a question the
    /// card is supposed to be answering.
    /// </remarks>
    private void DrawSiteButton(string? url, string id, float gap = -1f)
    {
        if (url is not { Length: > 0 })
        {
            return;
        }

        ImGui.SameLine(0f, gap);
        if (IconButton(FontAwesomeIcon.Globe, id, T(LocKeys.ReviewOpenOnSite)))
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

        // Everything behind this is already unreachable, but nothing said so: the question sat in the text
        // flow under the toolbar and read like another line of the page. A framed, tinted block with a
        // warning sign in front of it says "a decision follows" before anybody has read a word of it.
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Border, Warn))
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Warn with { W = 0.10f }))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 2f))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f)))
        using (var box = ImRaii.Child("##confirm", new Vector2(0f, PendingHeight(pending)), true))
        {
            if (box)
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    Text(Warn, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                }

                ImGui.SameLine();
                // Two headings, because only two of the three verbs earn the strong one. Saying "cannot be
                // undone" over a release, which now has an undo button, would teach people that the
                // sentence is decoration, and it must not be that on the two where it is true.
                // A bulk accept gets the checking heading, not the strong one. It used to take the strong
                // one by construction, because the test was "no single decision here", and that was the
                // wrong question. A bulk carries attribution and nothing else, guarded in the rule that
                // composes it: no row is removed, every target keeps its identity, its pin and its shares,
                // and what it overwrites can be rebuilt on the website. Saying "this cannot be undone" over
                // that is a warning spending credit it has not earned, and the sentence has to keep its
                // credit for the one verb where it is true.
                Text(Warn, T(pending.Decision is not null && ReviewRules.IsIrreversible(pending.Decision.Action)
                    ? LocKeys.ReviewConfirmTitle
                    : LocKeys.ReviewConfirmCheck));
                ImGui.Spacing();

                foreach (var line in pending.Lines)
                {
                    Wrapped(Muted, line);
                }

                ImGui.Spacing();
                using (ImRaii.Disabled(Blocked))
                {
                    // The safe answer first, and the one that cannot be taken back in the colour of what it
                    // does. Two identical buttons for "for ever" and "never mind" is not a question.
                    if (ImGui.Button(T(LocKeys.ReviewConfirmNo)))
                    {
                        _pending = null;
                    }

                    ImGui.SameLine();
                    using var danger = ImRaii.PushColor(ImGuiCol.Button, Bad with { W = 0.55f });
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
                }
            }
        }

        ImGui.Spacing();
        return true;
    }

    /// <summary>How tall the confirmation box has to be to hold what it says.</summary>
    /// <param name="pending">The waiting decision.</param>
    /// <returns>The height in pixels.</returns>
    /// <remarks>
    /// Measured rather than guessed: a box that clips its own last sentence would hide the consequence
    /// somebody is about to accept, which is the one thing this box exists to show.
    /// </remarks>
    private static float PendingHeight(Pending pending)
    {
        var line = ImGui.GetTextLineHeightWithSpacing();
        var wrapped = 0f;
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X - 40f);
        foreach (var text in pending.Lines)
        {
            wrapped += Math.Max(1f, MathF.Ceiling(ImGui.CalcTextSize(text).X / width)) * line;
        }

        return line + wrapped + ImGui.GetFrameHeightWithSpacing() + (ImGui.GetStyle().ItemSpacing.Y * 4f) + 20f;
    }

    /// <summary>
    /// What has to be said before a verb is applied to an inventory row, in the order it matters.
    /// </summary>
    /// <param name="verb">The verb about to be applied.</param>
    /// <param name="row">The row.</param>
    /// <returns>The sentences, empty when there is nothing to say.</returns>
    private IReadOnlyList<string> WarningsFor(string verb, OrphanRow row)
    {
        var name = Named(string.IsNullOrWhiteSpace(row.Name) ? "-" : row.Name!, row.SetUid);
        var teams = TeamList(row.TeamNames);

        // The rule lives in the core and this only dresses it. Which sentence belongs to which case was
        // exactly what went wrong here once, so the decision is somewhere it can be tested.
        var lines = ReviewRules.ConsequencesOf(verb, row).Select(c => c switch
        {
            ReviewConsequence.DeleteBecomesRelease => T(LocKeys.ReviewDeleteBecomesRelease, name),
            ReviewConsequence.TeamKeepsSeeingIt => T(
                row.TeamNames.Count == 1 ? LocKeys.ReviewKeepsShareOne : LocKeys.ReviewKeepsShareMany,
                teams,
                name),
            ReviewConsequence.KeepsItsPin => T(LocKeys.ReviewKeepsPin, name),
            ReviewConsequence.LosesItsPin => T(LocKeys.ReviewLosesPin, name),
            ReviewConsequence.RowIsRemoved => T(LocKeys.ReviewRowIsRemoved),
            ReviewConsequence.LeavesPluginGovernance => T(LocKeys.ReviewLeavesGovernance),
            _ => T(LocKeys.ReviewDeleteComesBack),
        }).ToList();

        if (lines.Count == 0)
        {
            lines.Add(Label(verb));
        }

        return lines;
    }
    private void Decide(ReviewDecision decision) => Run(async () =>
    {
        var outcome = await _review.DecideAsync(decision, CancellationToken.None).ConfigureAwait(false);
        AfterCall(outcome);
    });

    private void AcceptMapping()
    {
        // Copied on the drawing thread before the task starts: handing the live set to a background read
        // while clicks can still change it is the same race from the other side.
        var struckOut = new HashSet<string>(_struckOut, StringComparer.Ordinal);
        Run(async () =>
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
