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
    public ReviewWindow(
        ReviewService review,
        Localizer localizer,
        Func<string?> currentCharacter,
        Action afterDecision)
        : base("Eorzea Arsenal###EorzeaArsenalReview")
    {
        _review = review;
        _localizer = localizer;
        _currentCharacter = currentCharacter;
        _afterDecision = afterDecision;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 340),
            MaximumSize = new Vector2(1100, 1200),
        };
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
        DrawHeader();

        if (_review.LastOutcome == ReviewOutcome.Unavailable)
        {
            ImGui.TextColored(Muted, T(LocKeys.ReviewUnavailable));
            return;
        }

        if (_review.Current is not { } state)
        {
            ImGui.TextColored(Muted, T(LocKeys.ReviewNothing));
            return;
        }

        if (_staleNotice)
        {
            ImGui.TextColored(Warn, T(LocKeys.ReviewStale));
        }

        ImGui.Separator();

        var anything = state.Held.Count > 0 || state.Orphans.Count > 0;
        if (!anything)
        {
            ImGui.TextColored(Good, T(LocKeys.ReviewNothing));
            return;
        }

        if (state.Held.Count > 0)
        {
            DrawQuestion(state);
            ImGui.Separator();
        }

        DrawInventory(state);
    }

    private void DrawHeader()
    {
        using (ImRaii.Disabled(_review.IsBusy))
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
        ImGui.TextColored(state.Held.Count > 0 ? Warn : Muted, T(LocKeys.ReviewQuestions, state.Held.Count));
        ImGui.SameLine();
        ImGui.TextColored(Muted, "·");
        ImGui.SameLine();
        ImGui.TextColored(open > 0 ? Warn : Muted, T(LocKeys.ReviewOrphansOpen, open));

        if (aside > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, "·");
            ImGui.SameLine();
            ImGui.TextColored(Muted, T(LocKeys.ReviewOrphansAside, aside));
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
            ImGui.TextColored(Muted, $"{_question + 1} / {state.Held.Count}");
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

        ImGui.TextColored(Accent, T(LocKeys.ReviewWhichSet, held.Job ?? "?", held.Name ?? string.Empty));
        ImGui.Spacing();

        var preselected = ReviewRules.Preselected(held);
        foreach (var candidate in held.Candidates)
        {
            DrawCandidate(held, candidate, ReferenceEquals(candidate, preselected));
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
    private void DrawCandidate(HeldGearset held, ReviewCandidate candidate, bool proposed)
    {
        using var id = ImRaii.PushId(candidate.SetUid ?? string.Empty);

        var name = string.IsNullOrWhiteSpace(candidate.Name) ? "—" : candidate.Name;
        ImGui.TextColored(proposed ? Good : Muted, proposed ? $"› {candidate.Job} {name}" : $"  {candidate.Job} {name}");

        ImGui.SameLine();
        ImGui.TextColored(Muted, T(LocKeys.ReviewMatch, candidate.MatchedSlots, candidate.TotalSlots, candidate.Probability));

        if (candidate.Hidden)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"· {T(LocKeys.ReviewHiddenOnSite)}");
        }

        if (!proposed && candidate.BlockedBy is { Length: > 0 })
        {
            ImGui.TextColored(Muted, $"    {T(LocKeys.ReviewBlocked)}");
        }

        if (ReviewRules.IsAdoption(candidate))
        {
            ImGui.TextColored(Warn, $"    {T(LocKeys.ReviewAdoption)}");
        }

        // Only where there is something to say. A dialog that appears every time teaches people to click it
        // away, including the times it matters.
        if (candidate.HasPin)
        {
            ImGui.TextColored(Muted, $"    {T(LocKeys.ReviewKeepsPin, name)}");
        }

        if (candidate.HasTeamShare && candidate.TeamNames.Count > 0)
        {
            ImGui.TextColored(Warn, $"    {T(LocKeys.ReviewKeepsShare, string.Join(", ", candidate.TeamNames), name)}");
        }

        using (ImRaii.Disabled(_review.IsBusy))
        {
            if (ImGui.SmallButton(T(LocKeys.ReviewThisIsIt)))
            {
                Decide(new ReviewDecision
                {
                    SetUid = held.SetUid!,
                    Action = ReviewAction.Link,
                    TargetUid = candidate.SetUid,
                });
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
        using (ImRaii.Disabled(_review.IsBusy))
        {
            if (ImGui.Button(T(LocKeys.ReviewItIsNew)))
            {
                Decide(new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.New });
            }

            // Beside the question rather than among the answers: this is the way out, not an answer.
            ImGui.SameLine();
            if (ImGui.Button(T(LocKeys.ReviewTakeOut)))
            {
                Decide(new ReviewDecision { SetUid = held.SetUid!, Action = ReviewAction.Release });
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
        using (ImRaii.Disabled(_review.IsBusy))
        {
            if (ImGui.Button(T(LocKeys.ReviewAcceptAll, pairs.Count)))
            {
                AcceptMapping();
            }
        }

        var adoptions = ReviewRules.AdoptionCount(state, pairs);
        if (adoptions > 0)
        {
            ImGui.TextColored(Warn, T(LocKeys.ReviewAcceptAdoptions, adoptions));
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

        ImGui.TextColored(Accent, T(LocKeys.ReviewInventory));

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
        ImGui.TextColored(Muted, $"{row.Job} {name}");

        ImGui.SameLine();
        ImGui.TextColored(
            Muted,
            row.LastSeenAt is { Length: > 0 } seen
                ? T(LocKeys.ReviewLastSeen, seen)
                : T(LocKeys.ReviewNeverInGame));

        if (row.Hidden)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"· {T(LocKeys.ReviewHiddenOnSite)}");
        }

        if (row.HasTeamShare && row.TeamNames.Count > 0)
        {
            ImGui.TextColored(Warn, $"    {T(LocKeys.ReviewKeepsShare, string.Join(", ", row.TeamNames), name)}");
        }

        using (ImRaii.Disabled(_review.IsBusy))
        {
            foreach (var verb in ReviewRules.OfferedVerbs(row))
            {
                if (ImGui.SmallButton(Label(verb)))
                {
                    Decide(new ReviewDecision { SetUid = row.SetUid!, Action = verb });
                }

                ImGui.SameLine();
            }
        }

        ImGui.NewLine();

        // Said only where it is true. A parked or ignored row was not being reported anyway, and a hand-made
        // one cannot come back at all, so promising a return there would be a lie.
        if (row.IsFromPlugin && !row.IsPutAside)
        {
            ImGui.TextColored(Muted, $"    {T(LocKeys.ReviewDeleteComesBack)}");
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

    private void Decide(ReviewDecision decision) => _ = Task.Run(async () =>
    {
        var outcome = await _review.DecideAsync(decision, CancellationToken.None).ConfigureAwait(false);
        AfterCall(outcome);
    });

    private void AcceptMapping() => _ = Task.Run(async () =>
    {
        var outcome = await _review.AcceptMappingAsync(_struckOut, CancellationToken.None).ConfigureAwait(false);
        AfterCall(outcome);
    });

    private void AfterCall(ReviewOutcome outcome)
    {
        _staleNotice = outcome == ReviewOutcome.Stale;

        if (outcome != ReviewOutcome.Ok)
        {
            return;
        }

        // The question that was on screen may be gone, and the ones after it have shifted up. Walking back
        // to the first is the honest thing: the list was re-sorted most likely first, so it is not the same
        // carousel any more.
        _question = 0;
        _struckOut.Clear();
        _afterDecision();
    }
}
