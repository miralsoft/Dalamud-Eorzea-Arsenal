using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The classified outcome of a reconciliation call.</summary>
public enum ReviewOutcome
{
    /// <summary>The call went through and the state in hand is fresh.</summary>
    Ok,

    /// <summary>No API key stored.</summary>
    NotConnected,

    /// <summary>The character numeric id is not known yet, so there is nothing to ask about.</summary>
    NotResolved,

    /// <summary>
    /// The state moved under the caller. Nothing was applied, and the state now in hand is the fresh one:
    /// redraw from it rather than diffing, and let the player answer again.
    /// </summary>
    Stale,

    /// <summary>The route is unknown to this server, which is the normal answer until it ships.</summary>
    Unavailable,

    /// <summary>
    /// Too many decisions in the hour. The service holds off until <c>Retry-After</c> has passed rather
    /// than letting a person keep clicking into the same limit.
    /// </summary>
    RateLimited,

    /// <summary>Something else went wrong; see the log.</summary>
    Failed,
}

/// <summary>
/// Holds the open questions for one character and sends the answers, one decision at a time.
/// </summary>
/// <remarks>
/// <para>
/// The state and its token are kept together and replaced together, because they only mean anything as a
/// pair: counters from one snapshot with a token from another would offer a decision the server has
/// already moved past. Every answer carries a fresh state, so ten decisions are ten calls and no extra
/// reads.
/// </para>
/// <para>
/// This service decides nothing about gear. It asks, it relays what a person chose, and it keeps what came
/// back. The rules about what may be offered live in <see cref="ReviewRules"/>.
/// </para>
/// </remarks>
public sealed class ReviewService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly CharacterDirectory _directory;
    private readonly GearsetMappingService? _mapping;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="directory">Resolves <c>cid_hash</c> to the numeric character id.</param>
    /// <param name="clock">Time source, for the back-off window.</param>
    /// <param name="mapping">
    /// The identity cache, told to forget a character after a decision that moved identities. Optional
    /// only so the service can be tested on its own.
    /// </param>
    /// <param name="log">Diagnostics sink.</param>
    public ReviewService(
        IApiClient api,
        ITokenStore tokens,
        CharacterDirectory directory,
        IClock clock,
        GearsetMappingService? mapping = null,
        ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _directory = directory;
        _clock = clock;
        _mapping = mapping;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The questions and the inventory as last read, or <see langword="null"/> before any read.</summary>
    public ReviewState? Current { get; private set; }

    /// <summary>The character <see cref="Current"/> belongs to.</summary>
    public string? CurrentCidHash { get; private set; }

    /// <summary>Whether a call is in flight, so a window can disable its buttons rather than queue clicks.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>How the last call ended.</summary>
    public ReviewOutcome LastOutcome { get; private set; } = ReviewOutcome.Ok;

    /// <summary>
    /// Whether a person currently has the review open. Read by the sync path, which holds automatic pushes
    /// back while it is true.
    /// </summary>
    /// <remarks>
    /// This carries less than it used to and not nothing: the fingerprint covers the position, reordering
    /// in game changes it, and reordering triggers a push. What the narrower fingerprint took away is the
    /// case that happens while the player does nothing at all. What is left needs a hand on the keyboard,
    /// and a window that redraws then is an answer rather than a surprise.
    /// </remarks>
    public bool IsOpen { get; set; }

    /// <summary>Raised after any call completes, on the calling thread.</summary>
    public event Action? Changed;

    /// <summary>Reads the open questions for a character.</summary>
    /// <param name="cidHash">The character to ask about.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How it went.</returns>
    public async Task<ReviewOutcome> RefreshAsync(string cidHash, CancellationToken ct)
    {
        if (BackoffRemaining is { } wait)
        {
            _log.Info($"Review: still waiting {wait.TotalSeconds:F0}s after a rate limit.");
            return Finish(ReviewOutcome.RateLimited);
        }

        if (!_tokens.HasKey)
        {
            return Finish(ReviewOutcome.NotConnected);
        }

        if (!_directory.TryGet(cidHash, out var characterId))
        {
            // The numeric id is learned from a push answer. Until one has landed there is nothing to ask
            // about, and asking with a guess would be a request about somebody else.
            return Finish(ReviewOutcome.NotResolved);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IsBusy = true;
        try
        {
            var result = await _api.GetReviewAsync(_tokens.ApiKey!, characterId, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Record(Classify(result.Error!));
            }

            Current = result.Value;
            CurrentCidHash = cidHash;
            return Record(ReviewOutcome.Ok);
        }
        finally
        {
            // One release, always, and the listeners hear about it only after the gate is free.
            IsBusy = false;
            _gate.Release();
            Notify();
        }
    }

    /// <summary>Answers one question, or acts on one row.</summary>
    /// <param name="decision">The row, the verb, and the target for a link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="ReviewOutcome.Stale"/> when the state moved: nothing was applied, and
    /// <see cref="Current"/> now holds the fresh state to redraw from.
    /// </returns>
    public async Task<ReviewOutcome> DecideAsync(ReviewDecision decision, CancellationToken ct)
    {
        if (BackoffRemaining is { } wait)
        {
            _log.Info($"Review: still waiting {wait.TotalSeconds:F0}s after a rate limit.");
            return Finish(ReviewOutcome.RateLimited);
        }

        if (!_tokens.HasKey)
        {
            return Finish(ReviewOutcome.NotConnected);
        }

        if (CurrentCidHash is not { Length: > 0 } cidHash ||
            Current?.StateToken is not { Length: > 0 } token ||
            !_directory.TryGet(cidHash, out var characterId))
        {
            return Finish(ReviewOutcome.NotResolved);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IsBusy = true;
        try
        {
            var result = await _api
                .PostReviewDecisionAsync(_tokens.ApiKey!, characterId, token, decision, ct)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return Record(Classify(result.Error!));
            }

            var answer = result.Value!;
            Current = answer;

            if (answer.IsConflict)
            {
                _log.Info($"Review: the state moved ({answer.Conflicts.Count} row(s)); nothing was applied.");
                return Record(ReviewOutcome.Stale);
            }

            Applied(answer.Result, answer.AlsoResolved, cidHash);
            return Record(ReviewOutcome.Ok);
        }
        finally
        {
            // One release, always, and the listeners hear about it only after the gate is free.
            IsBusy = false;
            _gate.Release();
            Notify();
        }
    }

    /// <summary>
    /// Accepts the whole mapping the server composed, minus what the player struck out.
    /// </summary>
    /// <param name="struckOut">Held rows to leave out, by uid, or <see langword="null"/> for none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How it went. <see cref="ReviewOutcome.Ok"/> with nothing to send is also Ok.</returns>
    /// <remarks>
    /// One press over eighteen pairings, and one transaction: all of them or none, so there is no partial
    /// result to render. A pair the server did not propose is refused, which is why the payload is composed
    /// here from the proposals rather than assembled from whatever is on screen.
    /// </remarks>
    public async Task<ReviewOutcome> AcceptMappingAsync(IReadOnlySet<string>? struckOut, CancellationToken ct)
    {
        if (BackoffRemaining is { } wait)
        {
            _log.Info($"Review: still waiting {wait.TotalSeconds:F0}s after a rate limit.");
            return Finish(ReviewOutcome.RateLimited);
        }

        if (!_tokens.HasKey)
        {
            return Finish(ReviewOutcome.NotConnected);
        }

        if (CurrentCidHash is not { Length: > 0 } cidHash ||
            Current is not { } state ||
            state.StateToken is not { Length: > 0 } token ||
            !_directory.TryGet(cidHash, out var characterId))
        {
            return Finish(ReviewOutcome.NotResolved);
        }

        var pairs = ReviewRules.MappingToAccept(state, struckOut);
        if (pairs.Count == 0)
        {
            // Nothing left to accept is not a failure. It is what striking every pair out looks like, and
            // the player then answers them one at a time.
            return Finish(ReviewOutcome.Ok);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IsBusy = true;
        try
        {
            var result = await _api
                .AcceptReviewMappingAsync(_tokens.ApiKey!, characterId, token, pairs, ct)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return Record(Classify(result.Error!));
            }

            var answer = result.Value!;
            Current = answer;

            if (answer.IsConflict)
            {
                _log.Info($"Review: the mapping was stale ({answer.Conflicts.Count} row(s)); nothing was applied.");
                return Record(ReviewOutcome.Stale);
            }

            foreach (var applied in answer.Results)
            {
                Applied(applied, [], cidHash);
            }

            if (answer.AlsoResolved.Count > 0)
            {
                _log.Info($"Review: {answer.AlsoResolved.Count} further question(s) settled themselves.");
            }

            return Record(ReviewOutcome.Ok);
        }
        finally
        {
            // One release, always, and the listeners hear about it only after the gate is free.
            IsBusy = false;
            _gate.Release();
            Notify();
        }
    }


    /// <summary>How long to wait when the server did not say.</summary>
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Until when this service refuses to call, after the server said there had been enough.
    /// </summary>
    /// <remarks>
    /// The deciding paths have their own bucket, 120 an hour, so clearing a backlog cannot eat somebody
    /// ability to sync. Reaching it anyway means a person is clicking faster than the limit allows, and the
    /// honest answer is to say for how long rather than to let them keep clicking into it.
    /// </remarks>
    public DateTimeOffset? BackoffUntilUtc { get; private set; }

    /// <summary>How long the caller should wait, when it is waiting.</summary>
    public TimeSpan? BackoffRemaining =>
        BackoffUntilUtc is { } until && until > _clock.UtcNow ? until - _clock.UtcNow : null;

    private ReviewOutcome Classify(ApiError error)
    {
        if (error.Kind == ApiErrorKind.NotFound)
        {
            // Two different things arrive as a 404 and both are answers rather than faults: the route does
            // not exist on this server yet, or the row is not this caller. Neither is worth alarming about.
            return ReviewOutcome.Unavailable;
        }

        if (error.Kind == ApiErrorKind.RateLimited)
        {
            var wait = error.RetryAfter ?? DefaultBackoff;
            BackoffUntilUtc = _clock.UtcNow + wait;
            _log.Warning($"Review: rate limited, waiting {wait.TotalSeconds:F0}s. request_id={error.RequestId}.");
            return ReviewOutcome.RateLimited;
        }

        // A named cause here says the offer was wrong, which is a fault on this side rather than something
        // to put in front of the player. It belongs in the log, and the window redraws from a fresh read.
        if (error.Code is { Length: > 0 } code)
        {
            _log.Warning($"Review: the server refused it ({code}). The offered actions were wrong; re-reading.");
        }

        return ReviewOutcome.Failed;
    }

    /// <summary>Records the outcome without telling anybody. Safe to call while the gate is held.</summary>
    private ReviewOutcome Record(ReviewOutcome outcome)
    {
        LastOutcome = outcome;
        return outcome;
    }

    /// <summary>
    /// Tells listeners. Called <b>after</b> the gate is released, never while it is held.
    /// </summary>
    /// <remarks>
    /// Nothing subscribes synchronously today, and that is exactly why it is worth getting right now: a
    /// handler that called back into this service while the semaphore was still held would sit on itself,
    /// and the fault would look like a hang rather than a mistake in the handler.
    /// </remarks>
    private void Notify()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"Review Changed handler threw: {ex.GetType().Name}.");
        }
    }

    /// <summary>Records and tells, for the paths that never took the gate.</summary>
    private ReviewOutcome Finish(ReviewOutcome outcome)
    {
        Record(outcome);
        Notify();
        return outcome;
    }

    /// <summary>
    /// Records what a decision did. The identity cache is dropped for the character whenever a link
    /// survived, because after a link the target uid is the one that lives on and the held row uid ceases
    /// to exist: keeping it would attach a live gearset to a row that is gone.
    /// </summary>
    /// <param name="result">What the server did, if it said.</param>
    /// <param name="alsoResolved">Questions that settled as a consequence.</param>
    /// <param name="cidHash">The character.</param>
    private void Applied(ReviewResult? result, IReadOnlyList<string> alsoResolved, string cidHash)
    {
        if (result is null)
        {
            return;
        }

        if (result.WasConverted)
        {
            _log.Info($"Review: asked for {result.Requested}, the server performed {result.Action}.");
        }

        if (alsoResolved.Count > 0)
        {
            _log.Info($"Review: {alsoResolved.Count} further question(s) settled themselves.");
        }

        if (string.Equals(result.Action, ReviewAction.Link, StringComparison.Ordinal))
        {
            _mapping?.Forget(cidHash);
        }
    }
}
