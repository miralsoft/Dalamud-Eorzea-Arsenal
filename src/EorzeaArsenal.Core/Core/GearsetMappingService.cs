using System.Collections.Concurrent;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Holds the server's gearset mapping — which <c>set_uid</c> belongs to which live gearset — and
/// answers the one question the rest of the plugin asks: "what is this gearset's identity?".
/// </summary>
/// <remarks>
/// <para>
/// Identity is minted by the server and only by the server. This service is a cache in front of that
/// decision, with the two properties that keep it a cache: it <b>never</b> invents a uid, and when it
/// cannot answer it says so instead of falling back to the position — the position is precisely the
/// value that is wrong in the case this whole mechanism exists for.
/// </para>
/// <para>
/// It is fed from two places. A push answers with the mapping for the list it just sent, which is the
/// cheap and complete path. <c>GET /gear/sets</c> fills the gap before the first push of a session —
/// a read, deliberately, because pushing in order to learn the mapping would be a write in order to
/// read.
/// </para>
/// </remarks>
public sealed class GearsetMappingService
{
    /// <summary>How long a refresh from <c>GET /gear/sets</c> is considered current.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait after a failed refresh before trying again.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IGearsetIdentityStore _store;
    private readonly IClock _clock;
    private readonly ILog _log;

    private readonly Dictionary<string, DateTimeOffset> _nextRefreshUtc = new(StringComparer.Ordinal);

    // Per character, because two characters on one account share a plugin and a configuration file. As one
    // flat set of fields these described whichever character was touched last: after a switch the report
    // read "learned from a push, 10 row(s)" above "cached rows : 34", and a measurement that names the
    // wrong character is worse than a missing one. Concurrent and swapped whole rather than mutated, for
    // the reason the fields were volatile before: IsHeld runs per frame for every gearset in the BiS
    // window while a push writes from another thread, and reading a collection during a write to it
    // surfaces as an exception on the framework thread rather than as a wrong number.
    private readonly ConcurrentDictionary<string, CharacterSummary> _summaries = new(StringComparer.Ordinal);

    // Guards the identity store and the uncertainty map. Both are read from the drawing thread while a
    // push or a mapping read writes them from another, and a Dictionary read during a write is a race that
    // surfaces as an exception on the framework thread rather than as a wrong number. Held only around the
    // collection work, never around a request.
    private readonly Lock _state = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Once per character rather than once per session: the second character deserves the same warning as
    // the first, and reading the first one's silence as "already said" would swallow it.
    private readonly HashSet<string> _warnedAboutUncertainty = new(StringComparer.Ordinal);

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Where the API key comes from.</param>
    /// <param name="store">Persistence for the cache.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="log">Diagnostics sink.</param>
    public GearsetMappingService(
        IApiClient api,
        ITokenStore tokens,
        IGearsetIdentityStore store,
        IClock clock,
        ILog log)
    {
        _api = api;
        _tokens = tokens;
        _store = store;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// The gearsets the server wrote but could not attribute, by uid, as of the last push that said so.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The open attributions, empty when there are none.</returns>
    /// <remarks>
    /// Read from the push answer rather than from the review, because a push happens anyway and the review
    /// is only read when somebody opens it. A marker that only appeared after opening the window would be
    /// missing at exactly the moment it is useful.
    /// </remarks>
    public IReadOnlySet<string> HeldUids(string? cidHash) => SummaryOf(cidHash).Held;

    /// <summary>Whether this gearset attribution is still open.</summary>
    /// <param name="cidHash">The character the gearset belongs to.</param>
    /// <param name="setUid">The identity to ask about.</param>
    /// <returns><see langword="true"/> while the server is waiting for an answer about it.</returns>
    public bool IsHeld(string? cidHash, string? setUid) =>
        setUid is not null && SummaryOf(cidHash).Held.Contains(setUid);

    /// <summary>
    /// Whether the server this plugin is talking to mints identities at all. <see langword="false"/>
    /// until something carrying a <c>set_uid</c> has been seen. Detected rather than assumed on
    /// purpose: two machines can talk to a live and a test instance on the same day, and the old
    /// <c>(gear_index, job)</c> path has to keep working against the one that does not know uids yet.
    /// </summary>
    public bool ServerMintsUids { get; private set; }

    /// <summary>
    /// What the last attempt to read this character's mapping did, in one short line for the diagnostics
    /// view. This is what separates the two states that look identical from outside: a server that mints
    /// no identities, and one that does but could not be reached.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The one-line status.</returns>
    public string MappingStatus(string? cidHash) => SummaryOf(cidHash).Status;

    /// <summary>
    /// How many rows of that character's last read belonged to somebody else and were dropped. Not an
    /// error: the read names the character it wants, and a server that answers with the whole account is
    /// worth noticing rather than trusting.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The count, zero when nothing was dropped.</returns>
    public int ForeignRowsDropped(string? cidHash) => SummaryOf(cidHash).ForeignDropped;

    /// <summary>When this character's mapping was last read, or <see langword="null"/>.</summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The moment of the last read.</returns>
    public DateTimeOffset? LastMappingReadUtc(string? cidHash) => SummaryOf(cidHash).LastReadUtc;

    /// <summary>How many rows are cached for a character.</summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The row count, zero when nothing is cached.</returns>
    public int CachedCount(string? cidHash)
    {
        if (cidHash is null)
        {
            return 0;
        }

        lock (_state)
        {
            return _store.Identities.TryGetValue(cidHash, out var rows) ? rows.Count : 0;
        }
    }

    /// <summary>
    /// How much of what only a push can know is still in the cache, for the diagnostics view.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>How many rows carry the strong key, how many the gear key, and how many rows there are.</returns>
    /// <remarks>
    /// Both are filled by a push and by nothing else, and everything that separates two sets of one job
    /// hangs off them. Whether they are present decided several hours of guessing, and none of it was
    /// visible: the rungs each row resolved on could be read, but not whether the material for the better
    /// rung was even there.
    /// </remarks>
    public (int WithItems, int WithGear, int Total) CachedKeys(string? cidHash)
    {
        if (cidHash is null)
        {
            return (0, 0, 0);
        }

        lock (_state)
        {
            if (!_store.Identities.TryGetValue(cidHash, out var rows))
            {
                return (0, 0, 0);
            }

            var items = 0;
            var gear = 0;
            foreach (var row in rows)
            {
                if (row.ItemsKey is not null)
                {
                    items++;
                }

                if (row.GearKey is not null)
                {
                    gear++;
                }
            }

            return (items, gear, rows.Count);
        }
    }

    /// <summary>
    /// Resolves a whole list at once for the diagnostics view. Hashes every set, so it belongs off the
    /// framework thread — the caller reads the game, hands the list over, and only displays the result.
    /// </summary>
    /// <param name="cidHash">The character the gearsets belong to.</param>
    /// <param name="sets">The live gearsets.</param>
    /// <returns>One row per gearset, in the order given.</returns>
    public IReadOnlyList<GearsetIdentityRow> Snapshot(string cidHash, IReadOnlyList<GearsetDto> sets)
    {
        var rows = new List<GearsetIdentityRow>(sets.Count);
        foreach (var set in sets)
        {
            var match = Resolve(cidHash, set);
            rows.Add(new GearsetIdentityRow(
                set.GearIndex,
                set.Job,
                set.Name,
                match.SetUid,
                match.MatchedBy,
                match.WasAmbiguous,
                match.By));
        }

        return rows;
    }

    /// <summary>
    /// The gearsets whose mapping the server reported on an uncertain rung — it guessed between
    /// identical candidates, or fell back to the position. Keyed by <c>set_uid</c>, with the rung as
    /// the value. Worth showing in the interface and worth quoting in a bug report.
    /// </summary>
    /// <remarks>
    /// Swapped whole rather than mutated, like the held set: the diagnostics view reads its count every
    /// frame while a push writes it, and a snapshot costs one small allocation per push against nothing per
    /// frame.
    /// </remarks>
    /// <param name="cidHash">The character.</param>
    /// <returns>The uncertain attributions, empty when there are none.</returns>
    public IReadOnlyDictionary<string, string> UncertainMatches(string? cidHash) =>
        SummaryOf(cidHash).Uncertain;

    /// <summary>
    /// How many of <see cref="UncertainMatches"/> sit on <c>name_ambiguous</c>: two sets share a job and a
    /// name, and the mapping is a guess between them. The player can end it by naming them apart.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The count.</returns>
    public int AmbiguousMatches(string? cidHash) => SummaryOf(cidHash).Ambiguous;

    /// <summary>
    /// How many of <see cref="UncertainMatches"/> sit on <c>index</c>: neither the name nor the gear found a
    /// stored row, so the position decided. Renaming does not help here, which is why the two are
    /// counted apart and told apart in what the interface says.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The count.</returns>
    public int PositionalMatches(string? cidHash) => SummaryOf(cidHash).Positional;

    /// <summary>
    /// How many identities that character's last push and the cache before it disagreed about. Both sides
    /// of each disagreement are counted, since neither is provably the right one.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// Distinct from the two counters above, which report what the <b>server</b> said about its own
    /// confidence. This one reports what happened when that was checked against what this side
    /// remembered, and it is the only number here the server could not have produced.
    /// </remarks>
    public int ContestedMatches(string? cidHash) => SummaryOf(cidHash).Contested.Count;

    /// <summary>
    /// Which identities those are, for the diagnostics view and for a bug report.
    /// </summary>
    /// <param name="cidHash">The character.</param>
    /// <returns>The contested identities, empty when there are none.</returns>
    public IReadOnlySet<string> ContestedUids(string? cidHash) => SummaryOf(cidHash).Contested;

    /// <summary>
    /// Records the mapping a push answered with. The response is index-aligned with the list that was
    /// sent, so the sent gearsets supply the keys and the response supplies the identities.
    /// </summary>
    /// <param name="cidHash">The character the push was for.</param>
    /// <param name="sent">The gearsets as they were sent, in order.</param>
    /// <param name="assignments">The <c>sets</c> array from the response; may be empty or shorter.</param>
    public void RecordPush(
        string cidHash,
        IReadOnlyList<GearsetDto> sent,
        IReadOnlyList<GearsetAssignment> assignments)
    {
        if (string.IsNullOrEmpty(cidHash) || assignments.Count == 0)
        {
            // An older server answers without the mapping. That is not a failure and must not clear
            // what is already cached — nothing was contradicted, it simply was not mentioned.
            return;
        }

        // Read before it is overwritten. The cross-check below is the whole reason this is taken: what the
        // previous push put under each identity is the only account of these gearsets that the server does
        // not also hold, so it is the only thing that can disagree with it.
        List<CachedGearsetIdentity> previous;
        lock (_state)
        {
            previous = _store.Identities.TryGetValue(cidHash, out var before) ? [.. before] : [];
        }

        var rows = new List<CachedGearsetIdentity>(assignments.Count);
        var held = new HashSet<string>(StringComparer.Ordinal);
        var unsure = new Dictionary<string, string>(StringComparer.Ordinal);
        var uncertain = 0;
        var ambiguous = 0;
        var positional = 0;

        for (var i = 0; i < assignments.Count && i < sent.Count; i++)
        {
            var assignment = assignments[i];
            if (string.IsNullOrEmpty(assignment.SetUid))
            {
                continue;
            }

            // The response is index-aligned with what was sent. Cross-check the position anyway: if
            // the server ever answers out of order, silently pairing the wrong rows would write one
            // gearset's identity onto another, which is the exact bug class this feature removes.
            var set = sent[i];
            if (assignment.GearIndex != set.GearIndex)
            {
                _log.Warning(
                    $"Push mapping is out of order at {i} (sent gear_index {set.GearIndex}, " +
                    $"answered {assignment.GearIndex}); ignoring the mapping for this push.");
                return;
            }

            ServerMintsUids = true;
            rows.Add(new CachedGearsetIdentity
            {
                SetUid = assignment.SetUid!,
                Job = set.Job,
                Name = set.Name ?? string.Empty,
                ItemsKey = GearsetFingerprint.Strong(set),
                GearKey = GearsetFingerprint.Gear(set),
                GearIndex = set.GearIndex,
                MatchedBy = assignment.MatchedBy,
            });


            // Which sets the server could not attribute, so the comparison can say its target is provisional.
            // A held row DOES get the job default target, so it shows numbers either way, and without the
            // marker those numbers look settled while the question is still open.
            if (string.Equals(assignment.State, PushState.Held, StringComparison.Ordinal))
            {
                held.Add(assignment.SetUid!);
            }
            if (MatchedBy.IsUncertain(assignment.MatchedBy))
            {
                unsure[assignment.SetUid!] = assignment.MatchedBy!;
                uncertain++;
                if (string.Equals(assignment.MatchedBy, MatchedBy.NameAmbiguous, StringComparison.Ordinal))
                {
                    ambiguous++;
                }
                else
                {
                    positional++;
                }
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        // Where the server guessed, ask what this side remembered. Recomputed here and not carried over,
        // so the mark lasts exactly as long as the evidence for it.
        var contested = PushCrossCheck.Contested(previous, sent, assignments);
        foreach (var row in rows)
        {
            row.Contested = contested.Contains(row.SetUid);
        }

        Replace(cidHash, rows);
        ScheduleNextRead(cidHash, RefreshInterval);

        // Published as one whole after the loop, so a reader never sees a half-built summary. The answer
        // covers exactly the list that was just sent, so what is not in it is neither held nor unsure.
        UpdateSummary(cidHash, older => new CharacterSummary
        {
            Status = $"learned from a push, {rows.Count} row(s), {uncertain} uncertain",
            LastReadUtc = _clock.UtcNow,

            // Kept, not reset: this counts what the last read found, and a push does not read. A push is
            // index-aligned with the list it sent, so it has no foreign rows to drop and nothing to say.
            ForeignDropped = older.ForeignDropped,
            Ambiguous = ambiguous,
            Positional = positional,
            Held = held,
            Contested = contested,
            Uncertain = unsure,
        });

        WarnAboutUncertainty(ambiguous, positional, cidHash);
        WarnAboutContradiction(contested, rows);
    }

    /// <summary>
    /// Resolves a live gearset to the identity the server gave it, on four rungs ordered by how much each
    /// one distinguishes rather than by how sure it is. The strong key (job, name and items) survives
    /// being moved. Then the position, twice over: with the same gear, which survives a rename, and with
    /// the same name where no other set has it, which survives a re-gear. Then the weak key (job and
    /// name), the only one a row learned from <c>GET /gear/sets</c> can be matched on at all.
    /// </summary>
    /// <remarks>
    /// Every rung reports ambiguity rather than guessing at it. The order is load-bearing: the position is
    /// the surer evidence and the strong key the more discriminating one, and putting the surer first
    /// handed a set whose full description matched another row exactly to whoever sat at its number.
    /// </remarks>
    /// <param name="cidHash">The character the gearset belongs to.</param>
    /// <param name="set">The live gearset.</param>
    /// <returns>The match, which may be a miss.</returns>
    public GearsetIdentityMatch Resolve(string cidHash, GearsetDto set)
    {
        List<CachedGearsetIdentity> rows;
        lock (_state)
        {
            if (string.IsNullOrEmpty(cidHash) ||
                !_store.Identities.TryGetValue(cidHash, out var cached) ||
                cached.Count == 0)
            {
                return GearsetIdentityMatch.None;
            }

            // Copied under the lock, then matched outside it: the comparison hashes the gearset, and holding
            // a lock across that would put a push behind the drawing thread for no reason.
            rows = [.. cached];
        }

        // The strong key first: job, name and items together are the most specific thing this side can
        // ask, and where exactly one row answers, that row is the set. It survives being moved, which is
        // why it comes before the position below.
        //
        // Its guard took a live case to notice. A player had two gearsets with the same job, the same name
        // and the same gear; both stored rows then carried the same strong key, and this loop returned the
        // first of them and called the rung exact. Two live sets resolved to one identity, the position
        // table wrote one uid twice, and two windows printed the same set number for two different sets,
        // with nothing anywhere reporting a doubt because the doubt was never detected. Same job, same
        // name, same items is the best evidence the contents can give and it is still not a distinction.
        // Where it matches twice the contents are exhausted, and the position below is asked instead.
        var strong = GearsetFingerprint.Strong(set);
        CachedGearsetIdentity? onStrong = null;
        var strongIsAmbiguous = false;
        foreach (var row in rows)
        {
            if (row.ItemsKey is null || !string.Equals(row.ItemsKey, strong, StringComparison.Ordinal))
            {
                continue;
            }

            if (onStrong is not null)
            {
                strongIsAmbiguous = true;
                break;
            }

            onStrong = row;
        }

        if (!strongIsAmbiguous && onStrong is not null)
        {
            return Answer(onStrong, "job+name+gear");
        }

        // Then the position, for everything the contents cannot settle. It is the only value in a cached
        // row that came from an answer rather than from a guess, because a push is index-aligned and the
        // server confirms the index it answered for. Everything else is re-derived from what a live
        // gearset looks like, and the name in that is not something a player ever chose: the game writes
        // it when the set is made, so two sets of one job start out called the same thing. Leaving the
        // position out meant asking a name to do work it was never able to do.
        //
        // Only a reorder moves the number. A rename, a re-gear, both at once: the number stays. So it
        // answers the three cases the contents lose, and a reorder is already answered above.
        var atSamePlace = rows.FindAll(r =>
            r.GearIndex == set.GearIndex && string.Equals(r.Job, set.Job, StringComparison.Ordinal));

        if (atSamePlace.Count == 1)
        {
            var here = atSamePlace[0];

            // Same place, same job, same gear. Survives a rename, and tells apart two sets the player
            // never named differently, which is the case this rung was added for. Reached only where the
            // strong key found nothing or found two, so a set whose full description matches another row
            // exactly has already been given to that row: this cannot overrule a name that points
            // elsewhere, which it silently did while it sat above the strong key.
            if (here.GearKey is not null &&
                string.Equals(here.GearKey, GearsetFingerprint.Gear(set), StringComparison.Ordinal))
            {
                return Answer(here, "place+gear");
            }

            // Same place, same job, same name. Survives a re-gear, but only where that name belongs to
            // nothing else: swap two sets a player never named apart and this would otherwise hand back
            // the wrong one with no doubt attached, which is worse than the ambiguity it replaces.
            var sameName = string.Equals(here.Name, set.Name ?? string.Empty, StringComparison.Ordinal);
            var nameIsUnique = rows.Count(r =>
                string.Equals(GearsetFingerprint.NameKey(r.Job, r.Name), GearsetFingerprint.NameKey(set), StringComparison.Ordinal)) == 1;

            if (sameName && nameIsUnique)
            {
                return Answer(here, "place+name");
            }
        }

        if (strongIsAmbiguous)
        {
            // Two rows describe this set equally well and its place says nothing. The weak key below is
            // job and name, which those two rows also share, so there is nothing left to ask.
            return GearsetIdentityMatch.Ambiguous;
        }

        var weak = GearsetFingerprint.NameKey(set);
        CachedGearsetIdentity? single = null;
        foreach (var row in rows)
        {
            if (!string.Equals(GearsetFingerprint.NameKey(row.Job, row.Name), weak, StringComparison.Ordinal))
            {
                continue;
            }

            if (single is not null)
            {
                // Two gearsets share a job and a name. The server pairs them in position order, but the
                // position is exactly what cannot be trusted here, so this side declines to pick.
                return GearsetIdentityMatch.Ambiguous;
            }

            single = row;
        }

        return single is null
            ? GearsetIdentityMatch.None
            : Answer(single, "job+name");
    }

    /// <summary>
    /// Makes sure the mapping for a character has been read at least once, and refreshes it when it has
    /// gone stale. Cheap to call repeatedly: it returns immediately while the cache is current, and only
    /// one refresh runs at a time.
    /// </summary>
    /// <param name="cidHash">The character to read the mapping for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the cache holds something for this character afterwards.</returns>
    public async Task<bool> EnsureMappingAsync(string cidHash, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cidHash) || !_tokens.HasKey)
        {
            return false;
        }

        if (IsFresh(cidHash))
        {
            return HasCached(cidHash);
        }

        if (!await _refreshGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return HasCached(cidHash);
        }

        try
        {
            var result = await _api.GetGearSetsAsync(_tokens.ApiKey!, cidHash, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                // A server that does not know the route answers 404. That is a fact about the server,
                // not an error to shout about, and the old comparison path still works.
                ScheduleNextRead(cidHash, FailureBackoff);
                UpdateSummary(cidHash, older => older with
                {
                    Status = result.Error!.Kind == ApiErrorKind.NotFound
                        ? "route unknown to this server"
                        : $"unavailable ({result.Error!.Kind})",
                });
                _log.Info($"Gearset mapping unavailable ({result.Error!.Kind}); keeping what is cached.");
                return HasCached(cidHash);
            }

            // Does this server report a row state at all? Asked of the answer rather than of a version,
            // and per response rather than per row: a hand-made row legitimately has no state, so only the
            // whole answer can say "this server does not send the field". Applying the state rule to a
            // server that sends none would cache nothing and break the mapping outright.
            var reportsState = false;
            foreach (var probe in result.Value!.Data)
            {
                if (!string.IsNullOrEmpty(probe.State))
                {
                    reportsState = true;
                    break;
                }
            }

            var rows = new List<CachedGearsetIdentity>();
            var stillHeld = new HashSet<string>(StringComparer.Ordinal);
            var foreign = 0;
            foreach (var stored in result.Value!.Data)
            {
                if (string.IsNullOrEmpty(stored.SetUid))
                {
                    continue;
                }

                // The read asked for one character. A server may still answer with the whole account —
                // the development one does today, ignoring the cid_hash it was given. A foreign row that
                // shares a job and a name with a real set makes that set ambiguous and its identity
                // unresolvable, so what was not asked for is dropped here rather than trusted.
                if (!string.IsNullOrEmpty(stored.CidHash) &&
                    !string.Equals(stored.CidHash, cidHash, StringComparison.Ordinal))
                {
                    foreign++;
                    continue;
                }

                // What a resolution cache keeps is what is IN GAME, which is not the same as "not made by
                // hand". A parked row is a plugin row that no live gearset occupies any more, and caching
                // it puts a second (job, name) candidate in front of the set that actually exists.
                var keep = reportsState
                    ? RowState.BelongsInResolutionCache(stored.State)
                    : stored.IsFromPlugin;

                if (!keep)
                {
                    continue;
                }

                ServerMintsUids = true;

                // A read can say which attributions are still open, and it is the only thing that can say
                // so at the start of a session. This was filled by a push and nothing else, and it is not
                // persisted, so after every reload the plugin believed no question was outstanding until
                // the next push happened to mention it. A set the server is waiting on then showed its
                // provisional target as though it were settled, and a diagnostics dump read "none open"
                // while the review window had a card waiting in it.
                if (string.Equals(stored.State, RowState.Held, StringComparison.Ordinal))
                {
                    stillHeld.Add(stored.SetUid!);
                }

                rows.Add(new CachedGearsetIdentity
                {
                    SetUid = stored.SetUid!,
                    Job = stored.Job ?? string.Empty,
                    Name = stored.Name ?? string.Empty,
                    ItemsKey = null,
                    GearKey = null,

                    // Where the server last recorded it, which is where it still is unless the player
                    // has reordered since. Good enough to anchor on, because every rung that uses it
                    // also wants the job and either the gear or a name nothing else carries.
                    GearIndex = stored.GearIndex >= 0 && stored.GearIndex <= ProtocolConstants.MaxGearIndex
                        ? stored.GearIndex
                        : null,
                    MatchedBy = null,
                });
            }

            ScheduleNextRead(cidHash, RefreshInterval);

            var status = reportsState
                ? $"read ok, {rows.Count} live row(s) of {result.Value!.Data.Count}"
                : $"read ok, {rows.Count} row(s) from the server";
            if (foreign > 0)
            {
                status += $", {foreign} of another character";
            }

            UpdateSummary(cidHash, older => older with
            {
                Status = status,
                LastReadUtc = _clock.UtcNow,
                ForeignDropped = foreign,

                // Only where the server actually reports the field. A server that sends no state says
                // nothing about what is open, and reading its silence as "nothing is open" would replace a
                // real set of questions with an empty one.
                Held = reportsState ? stillHeld : older.Held,
            });

            if (rows.Count == 0)
            {
                // The server answered and knows no gearsets for this character. That is information:
                // there is nothing to attach, and the next push will establish the mapping.
                Replace(cidHash, rows);
                return false;
            }

            Merge(cidHash, rows);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ScheduleNextRead(cidHash, FailureBackoff);
            UpdateSummary(cidHash, older => older with { Status = $"read failed ({ex.GetType().Name})" });
            _log.Warning($"Gearset mapping refresh failed: {ex.GetType().Name}.");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Makes the next <see cref="EnsureMappingAsync"/> actually go to the server, without throwing away
    /// what is cached in the meantime.
    /// </summary>
    /// <param name="cidHash">The character whose mapping is now suspect.</param>
    /// <remarks>
    /// <para>
    /// For a caller that knows the attribution may have moved and wants it re-read, which is not the same
    /// as wanting it forgotten. <see cref="Forget"/> was doing both, and the difference is severe: a read
    /// carries no items, so everything derived from them goes with it. The row keeps its uid, its job and
    /// its name and loses its gear, and the gear is the only thing that separates two sets of one job,
    /// which the game names identically the moment they are made.
    /// </para>
    /// <para>
    /// Answering one question in the review window therefore turned every same-named pair on the character
    /// unresolvable, and left them that way until the next push. The merge already does what this caller
    /// wanted: rows the server no longer lists are dropped, so an identity that was just linked away goes,
    /// and everything still listed keeps what a push had established about it.
    /// </para>
    /// </remarks>
    public void ExpireMapping(string? cidHash)
    {
        if (string.IsNullOrEmpty(cidHash))
        {
            return;
        }

        lock (_state)
        {
            _nextRefreshUtc.Remove(cidHash);
        }
    }

    /// <summary>Forgets everything cached for a character, e.g. after disconnecting.</summary>
    /// <param name="cidHash">The character, or <see langword="null"/> to forget all of them.</param>
    /// <remarks>
    /// Genuinely forgets. Where the mapping is only suspect rather than wrong, <see cref="ExpireMapping"/>
    /// is the one that re-reads without also discarding what only a push can know.
    /// </remarks>
    public void Forget(string? cidHash)
    {
        lock (_state)
        {
            if (string.IsNullOrEmpty(cidHash))
            {
                _store.Identities.Clear();
                _nextRefreshUtc.Clear();
                _summaries.Clear();
                _warnedAboutUncertainty.Clear();
            }
            else
            {
                _store.Identities.Remove(cidHash);
                _nextRefreshUtc.Remove(cidHash);
                _summaries.TryRemove(cidHash, out _);
                _warnedAboutUncertainty.Remove(cidHash);
            }
        }

        // Outside the lock: writing the configuration touches the disk, and nothing about that needs to
        // hold a reader on the drawing thread.
        _store.Save();
    }

    /// <summary>Replaces a character's rows wholesale — used when the source knew the complete list.</summary>


    /// <summary>Notes when this character mapping may be read again. Takes the state lock.</summary>
    /// <param name="cidHash">The character.</param>
    /// <param name="after">How long to wait.</param>
    private void ScheduleNextRead(string cidHash, TimeSpan after)
    {
        lock (_state)
        {
            _nextRefreshUtc[cidHash] = _clock.UtcNow + after;
        }
    }

    /// <summary>Whether this character mapping is still considered fresh. Takes the state lock.</summary>
    /// <param name="cidHash">The character.</param>
    /// <returns><see langword="true"/> while the last read is young enough to trust.</returns>
    private bool IsFresh(string cidHash)
    {
        lock (_state)
        {
            return _nextRefreshUtc.TryGetValue(cidHash, out var next) && _clock.UtcNow < next;
        }
    }
    /// <summary>Whether anything is cached for a character. Takes the state lock.</summary>
    /// <param name="cidHash">The character.</param>
    /// <returns><see langword="true"/> when at least one row is held.</returns>
    private bool HasCached(string cidHash)
    {
        lock (_state)
        {
            return _store.Identities.TryGetValue(cidHash, out var rows) && rows.Count > 0;
        }
    }
    private void Replace(string cidHash, List<CachedGearsetIdentity> rows)
    {
        lock (_state)
        {
            _store.Identities[cidHash] = rows;
        }

        _store.Save();
    }

    /// <summary>
    /// Folds rows that carry no items key into what is cached, keeping any stronger row already there.
    /// A <c>GET /gear/sets</c> row knows the uid but not the items, so it must not overwrite a row a
    /// push established with the full key — that would downgrade the cache on every refresh.
    /// </summary>
    private void Merge(string cidHash, List<CachedGearsetIdentity> rows)
    {
        Dictionary<string, CachedGearsetIdentity> existing;
        lock (_state)
        {
            existing = _store.Identities.TryGetValue(cidHash, out var current)
                ? current.ToDictionary(r => r.SetUid, StringComparer.Ordinal)
                : new Dictionary<string, CachedGearsetIdentity>(StringComparer.Ordinal);
        }

        var merged = new List<CachedGearsetIdentity>(rows.Count);
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.SetUid, out var known) && known.ItemsKey is not null &&
                string.Equals(known.Job, row.Job, StringComparison.Ordinal) &&
                string.Equals(known.Name, row.Name, StringComparison.Ordinal))
            {
                merged.Add(known);
                continue;
            }

            // The name moved, so the row is rebuilt from the read. Two things still carry over, because
            // neither of them was ever about the name.
            //
            // The gear key is name-free by construction, and a rename is the one case it exists for: it is
            // what recognises a set the player just renamed, and dropping it here would have retired it at
            // exactly that moment, on a refresh that happens every ten minutes whether anybody asked or
            // not. The strong key does go, and rightly, since the name is part of what it hashes.
            //
            // A doubt carries over too. It was found by comparing two accounts of the same uid, and a read
            // is neither of them: it brings no gear, so it cannot settle what it cannot see.
            if (existing.TryGetValue(row.SetUid, out var older))
            {
                row.GearKey ??= older.GearKey;
                row.Contested = older.Contested;
            }

            merged.Add(row);
        }

        // Rows the server no longer lists are gone from the mapping. Dropping them here loses nothing:
        // the server is the truth, and a stale row could only ever attach a uid that no longer exists.
        lock (_state)
        {
            _store.Identities[cidHash] = merged;
        }

        _store.Save();
    }

    /// <summary>
    /// Hands back a row's identity, unless the last push and the cache before it disagreed about that row.
    /// </summary>
    /// <param name="row">The row a rung settled on.</param>
    /// <param name="by">Which rung that was, in one word, for the diagnostics view.</param>
    /// <returns>The identity, or an ambiguous answer where the two accounts do not agree.</returns>
    /// <remarks>
    /// Every rung goes through here, and none of them may skip it. A rung earning its answer says nothing
    /// about whether the row it earned is trustworthy: the doubt was found at the push, against evidence
    /// no rung sees. Reported as ambiguous rather than as nothing found, because the interface already
    /// knows how to say "two of these cannot be told apart" and that is what has happened. What the server
    /// claimed is carried through even so: on a contested row that claim is the interesting half, since the
    /// disagreement is with it.
    /// </remarks>
    private static GearsetIdentityMatch Answer(CachedGearsetIdentity row, string by) =>
        row.Contested
            ? GearsetIdentityMatch.Ambiguous with { By = "contested", MatchedBy = row.MatchedBy }
            : new GearsetIdentityMatch(row.SetUid, row.MatchedBy, false, by);

    /// <summary>
    /// Says which gearsets the two accounts disagree about, by name and position rather than by uid.
    /// </summary>
    /// <param name="contested">The identities in doubt.</param>
    /// <param name="rows">The rows just written, which is where the names come from.</param>
    /// <remarks>
    /// Written on every push that finds one, unlike the once-a-session warning below. That one repeats a
    /// standing condition and would be noise; this one reports something that just happened, and it stops
    /// as soon as it stops being true.
    /// </remarks>
    private void WarnAboutContradiction(IReadOnlySet<string> contested, List<CachedGearsetIdentity> rows)
    {
        if (contested.Count == 0)
        {
            return;
        }

        var named = rows
            .Where(r => contested.Contains(r.SetUid))
            .Select(r => $"#{r.GearIndex + 1} {r.Job} \"{r.Name}\"")
            .ToList();

        _log.Warning(
            $"The server's answer disagrees with what was remembered about {contested.Count} gearset(s): " +
            $"{string.Join(", ", named)}. Gear that identified one set now sits under another, on a rung " +
            "the server itself matched by position. Neither account is provably right, so these sets are " +
            "left unidentified rather than attributed: their comparison stays empty until the two agree.");
    }

    /// <summary>Says once per session that the server had to guess somewhere, and where to look.</summary>
    /// <param name="ambiguous">How many sets were paired between identical names.</param>
    /// <param name="positional">How many were recognised by their position alone.</param>
    /// <param name="cidHash">The character it is said about, so a second one is not left out.</param>
    private void WarnAboutUncertainty(int ambiguous, int positional, string cidHash)
    {
        if (ambiguous == 0 && positional == 0)
        {
            return;
        }

        lock (_state)
        {
            if (!_warnedAboutUncertainty.Add(cidHash))
            {
                return;
            }
        }

        // Two rungs, two different pieces of advice, which is the whole reason they are counted apart.
        // Renaming ends an ambiguity and does nothing at all for a positional match: there the names are
        // already distinct and the gear no longer matches what the server stored.
        if (ambiguous > 0)
        {
            _log.Warning(
                $"{ambiguous} gearset(s) share a job and a name, so the server had to guess between " +
                "them. The comparison may sit on the wrong one; giving them different names ends it.");
        }

        if (positional > 0)
        {
            _log.Warning(
                $"{positional} gearset(s) were recognised by their position alone, because neither the " +
                "name nor the gear matched a stored set. The comparison may sit on the wrong one. Check " +
                "the pinned target on those sets: the next sync turns the current mapping into the " +
                "settled one.");
        }
    }

    /// <summary>What one character's last read or push learned, or the empty summary.</summary>
    /// <param name="cidHash">The character, or <see langword="null"/> when nobody is logged in.</param>
    /// <returns>The summary, never <see langword="null"/>.</returns>
    private CharacterSummary SummaryOf(string? cidHash) =>
        cidHash is { Length: > 0 } key && _summaries.TryGetValue(key, out var summary)
            ? summary
            : CharacterSummary.Empty;

    /// <summary>Publishes a new summary for one character, built from the one it replaces.</summary>
    /// <param name="cidHash">The character.</param>
    /// <param name="change">What the read or push learned, applied to the previous summary.</param>
    private void UpdateSummary(string cidHash, Func<CharacterSummary, CharacterSummary> change) =>
        _summaries.AddOrUpdate(cidHash, _ => change(CharacterSummary.Empty), (_, older) => change(older));

    /// <summary>
    /// What one read or push learned about one character. Immutable and published whole, so a reader on
    /// the drawing thread sees either the whole of the last answer or the whole of the one before it.
    /// </summary>
    private sealed record CharacterSummary
    {
        /// <summary>The summary of a character nothing has been read or pushed for.</summary>
        public static readonly CharacterSummary Empty = new();

        /// <summary>The one-line status for the diagnostics view.</summary>
        public string Status { get; init; } = "not read yet";

        /// <summary>When the mapping was last read, or <see langword="null"/>.</summary>
        public DateTimeOffset? LastReadUtc { get; init; }

        /// <summary>How many rows of the last read belonged to somebody else.</summary>
        public int ForeignDropped { get; init; }

        /// <summary>How many attributions sit on <c>name_ambiguous</c>.</summary>
        public int Ambiguous { get; init; }

        /// <summary>How many attributions sit on <c>index</c>.</summary>
        public int Positional { get; init; }

        /// <summary>Which attributions the server is still waiting to have confirmed.</summary>
        public IReadOnlySet<string> Held { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Which identities the last push and the cache before it disagreed about.</summary>
        public IReadOnlySet<string> Contested { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Which attributions the server itself called uncertain, with the rung it used.</summary>
        public IReadOnlyDictionary<string, string> Uncertain { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
