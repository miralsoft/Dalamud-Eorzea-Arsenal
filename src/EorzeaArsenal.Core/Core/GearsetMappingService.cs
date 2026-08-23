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

    // Guards the identity store and the uncertainty map. Both are read from the drawing thread while a
    // push or a mapping read writes them from another, and a Dictionary read during a write is a race that
    // surfaces as an exception on the framework thread rather than as a wrong number. Held only around the
    // collection work, never around a request.
    private readonly Lock _state = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private bool _warnedAboutUncertainty;

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

    // Swapped whole rather than mutated: IsHeld runs per frame for every gearset in the BiS window while a
    // push writes this from another thread, and Contains() during Add() on a HashSet is a race that ends as
    // an exception on the framework thread. A snapshot costs one small allocation per push and nothing per
    // frame.
    private volatile IReadOnlySet<string> _held = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The gearsets the server wrote but could not attribute, by uid, as of the last push that said so.
    /// </summary>
    /// <remarks>
    /// Read from the push answer rather than from the review, because a push happens anyway and the review
    /// is only read when somebody opens it. A marker that only appeared after opening the window would be
    /// missing at exactly the moment it is useful.
    /// </remarks>
    public IReadOnlySet<string> HeldUids => _held;

    /// <summary>Whether this gearset attribution is still open.</summary>
    /// <param name="setUid">The identity to ask about.</param>
    /// <returns><see langword="true"/> while the server is waiting for an answer about it.</returns>
    public bool IsHeld(string? setUid) => setUid is not null && _held.Contains(setUid);
    /// <summary>
    /// Whether the server this plugin is talking to mints identities at all. <see langword="false"/>
    /// until something carrying a <c>set_uid</c> has been seen. Detected rather than assumed on
    /// purpose: two machines can talk to a live and a test instance on the same day, and the old
    /// <c>(gear_index, job)</c> path has to keep working against the one that does not know uids yet.
    /// </summary>
    public bool ServerMintsUids { get; private set; }

    /// <summary>
    /// What the last attempt to read the mapping did, in one short line for the diagnostics view. This
    /// is what separates the two states that look identical from outside: a server that mints no
    /// identities, and one that does but could not be reached.
    /// </summary>
    public string MappingStatus { get; private set; } = "not read yet";

    /// <summary>
    /// How many rows of the last read belonged to another character and were dropped. Not an error: the
    /// read names the character it wants, and a server that answers with the whole account is worth
    /// noticing rather than trusting.
    /// </summary>
    public int ForeignRowsDropped { get; private set; }

    /// <summary>When the mapping was last read from the server, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastMappingReadUtc { get; private set; }

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
                match.WasAmbiguous));
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
    public IReadOnlyDictionary<string, string> UncertainMatches => _uncertain;

    private volatile IReadOnlyDictionary<string, string> _uncertain =
        new Dictionary<string, string>(StringComparer.Ordinal);

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

        var rows = new List<CachedGearsetIdentity>(assignments.Count);
        var held = new HashSet<string>(StringComparer.Ordinal);
        var unsure = new Dictionary<string, string>(StringComparer.Ordinal);
        var uncertain = 0;

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
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        // Published as one whole after the loop, so a reader never sees a half-built collection. The answer
        // covers exactly the list that was just sent, so what is not in it is neither held nor unsure.
        _held = held;
        _uncertain = unsure;

        Replace(cidHash, rows);
        ScheduleNextRead(cidHash, RefreshInterval);
        LastMappingReadUtc = _clock.UtcNow;
        MappingStatus = $"learned from a push, {rows.Count} row(s), {uncertain} uncertain";
        WarnAboutUncertainty(uncertain);
    }

    /// <summary>
    /// Resolves a live gearset to the identity the server gave it. The strong key first (job, name and
    /// items — survives being moved), then the weak key (job and name — the only one a
    /// <c>GET /gear/sets</c> row can be matched on). A weak key with more than one candidate is
    /// reported as ambiguous rather than guessed at.
    /// </summary>
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

        var strong = GearsetFingerprint.Strong(set);
        foreach (var row in rows)
        {
            if (row.ItemsKey is not null && string.Equals(row.ItemsKey, strong, StringComparison.Ordinal))
            {
                return new GearsetIdentityMatch(row.SetUid, row.MatchedBy, false);
            }
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
            : new GearsetIdentityMatch(single.SetUid, single.MatchedBy, false);
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
                MappingStatus = result.Error!.Kind == ApiErrorKind.NotFound
                    ? "route unknown to this server"
                    : $"unavailable ({result.Error!.Kind})";
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
                rows.Add(new CachedGearsetIdentity
                {
                    SetUid = stored.SetUid!,
                    Job = stored.Job ?? string.Empty,
                    Name = stored.Name ?? string.Empty,
                    ItemsKey = null,
                    MatchedBy = null,
                });
            }

            ScheduleNextRead(cidHash, RefreshInterval);
            LastMappingReadUtc = _clock.UtcNow;
            ForeignRowsDropped = foreign;
            MappingStatus = reportsState
                ? $"read ok, {rows.Count} live row(s) of {result.Value!.Data.Count}"
                : $"read ok, {rows.Count} row(s) from the server";
            if (foreign > 0)
            {
                MappingStatus += $", {foreign} of another character";
            }

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
            MappingStatus = $"read failed ({ex.GetType().Name})";
            _log.Warning($"Gearset mapping refresh failed: {ex.GetType().Name}.");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Forgets everything cached for a character, e.g. after disconnecting.</summary>
    /// <param name="cidHash">The character, or <see langword="null"/> to forget all of them.</param>
    public void Forget(string? cidHash)
    {
        lock (_state)
        {
            if (string.IsNullOrEmpty(cidHash))
            {
                _store.Identities.Clear();
                _nextRefreshUtc.Clear();
                _uncertain = new Dictionary<string, string>(StringComparer.Ordinal);
                _held = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                _store.Identities.Remove(cidHash);
                _nextRefreshUtc.Remove(cidHash);
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

    /// <summary>Says once per session that the server had to guess somewhere, and where to look.</summary>
    private void WarnAboutUncertainty(int count)
    {
        if (count == 0 || _warnedAboutUncertainty)
        {
            return;
        }

        _warnedAboutUncertainty = true;
        _log.Warning(
            $"The server could not identify {count} gearset(s) with certainty (identical job and name, " +
            "or matched on position alone). The comparison may be attached to the wrong set; renaming " +
            "one of them apart fixes it for good.");
    }
}
