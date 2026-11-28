using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The feature names a server lists in <c>features</c> of <c>GET /version</c> (server 1.5).</summary>
public static class FeatureNames
{
    /// <summary>A player's times, the team's week and "not tonight".</summary>
    public const string TeamAvailability = "team-availability";

    /// <summary>Polls for finding a date.</summary>
    public const string Polls = "polls";

    /// <summary>The relic helper.</summary>
    public const string Relics = "relics";

    /// <summary>The currency sync (tomestones, scrips, gil and the other currencies the server lists).</summary>
    public const string CurrencySync = "currency-sync";

    /// <summary>Drop reports from the plugin.</summary>
    public const string DropReports = "drop-reports";

    /// <summary>Spots reported for the world database.</summary>
    public const string WorldReports = "world-reports";

    /// <summary>The armoire as an inventory scope of its own.</summary>
    public const string InventoryArmoire = "inventory-armoire";

    /// <summary>Goals: <c>unlocks</c> in the tracked items and <c>PUT /me/unlocks</c>.</summary>
    public const string Goals = "goals";

    /// <summary>The collection: <c>kind</c> and <c>full</c> on <c>PUT /me/unlocks</c>, and <c>GET /me/unlocks</c>.</summary>
    public const string UnlockCollection = "unlock-collection";

    /// <summary>The goals to read: <c>GET /me/goals</c> by <c>cid_hash</c> with progress and plan, and <c>GET /teams/{id}/goals</c>.</summary>
    public const string GoalsProgress = "goals-progress";

    /// <summary>Adding and removing a goal from the game: <c>POST /me/goals</c> and <c>DELETE /me/goals/{id}</c> for a key.</summary>
    public const string GoalsFromPlugin = "goals-from-plugin";

    /// <summary>Every unlock item counts, in ten kinds, and a goal for one is to learn or to collect (<c>purpose</c>).</summary>
    public const string UnlockKinds = "unlock-kinds";

    /// <summary>
    /// Goals of a team: <c>team_goals</c> and <c>team_goal_teams</c> in <c>GET /me/goals</c>, the team's own list
    /// <c>GET /teams/{id}/team-goals</c>, setting one and answering for a character. Own goals are shown per team.
    /// </summary>
    public const string TeamGoals = "team-goals";
}

/// <summary>
/// Holds what each server address says it offers, read from <c>features</c> of <c>GET /version</c>.
/// </summary>
/// <remarks>
/// <para>
/// Kept per address, like the job table: the live site and the dev site answer differently, and a list
/// learned from one must never decide what is offered on the other.
/// </para>
/// <para>
/// Read again after <see cref="Freshness"/>, which is short on purpose. A server gains features by being
/// deployed while the plugin keeps running; the dev rehearsal of server 1.4 showed what holding an answer
/// for a whole session costs, when a stale "not offered" kept a button live across the update. The call
/// needs no key and is cheap.
/// </para>
/// <para>
/// A server that answers without the field is a server before 1.5 and offers nothing. A failed read keeps
/// the list already held, and with none held nothing is offered: a feature shown on a server that does not
/// have it would answer every click with a 404.
/// </para>
/// </remarks>
public sealed class ServerFeatures
{
    /// <summary>How long a list is used before it is read again.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    private readonly IApiClient _api;
    private readonly IApiSettings _settings;
    private readonly IClock _clock;
    private readonly ILog? _log;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _byAddress = new(StringComparer.OrdinalIgnoreCase);
    private Task? _inFlight;

    /// <summary>Creates the holder.</summary>
    /// <param name="api">The API client. <c>GET /version</c> needs no key.</param>
    /// <param name="settings">Supplies the address the list belongs to.</param>
    /// <param name="clock">Clock, for freshness.</param>
    /// <param name="log">Optional log.</param>
    public ServerFeatures(IApiClient api, IApiSettings settings, IClock clock, ILog? log = null)
    {
        _api = api;
        _settings = settings;
        _clock = clock;
        _log = log;
    }

    /// <summary>Raised after a read changed what the configured address offers.</summary>
    public event Action? Changed;

    private string Address => _settings.BaseUrl.TrimEnd('/');

    /// <summary>
    /// Whether the configured server offers <paramref name="feature"/>, from the list held. Asks nothing;
    /// call <see cref="RefreshIfStaleAsync"/> to keep the list current.
    /// </summary>
    /// <param name="feature">A name from <see cref="FeatureNames"/>.</param>
    /// <returns><see langword="true"/> only when the held list names it.</returns>
    public bool Has(string feature)
    {
        lock (_gate)
        {
            return _byAddress.TryGetValue(Address, out var entry) && entry.Names.Contains(feature);
        }
    }

    /// <summary>The names held for the configured address, for diagnostics.</summary>
    public IReadOnlyCollection<string> Current
    {
        get
        {
            lock (_gate)
            {
                return _byAddress.TryGetValue(Address, out var entry) ? entry.Names.ToArray() : [];
            }
        }
    }

    /// <summary>Reads the list unless one younger than <see cref="Freshness"/> is held. Never throws.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the list is current.</returns>
    public Task RefreshIfStaleAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_byAddress.TryGetValue(Address, out var held) && _clock.UtcNow - held.ReadUtc < Freshness)
            {
                return Task.CompletedTask;
            }
        }

        return RefreshAsync(ct);
    }

    /// <summary>
    /// Reads the list now, joining a read already on its way. Called after the address changed or a
    /// connection test, where an old answer is the wrong one to keep. Never throws.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the read is done.</returns>
    public Task RefreshAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
            {
                return running;
            }

            _inFlight = ReadAsync(Address, ct);
            return _inFlight;
        }
    }

    private async Task ReadAsync(string address, CancellationToken ct)
    {
        ApiResult<VersionResponse> result;
        try
        {
            result = await _api.GetVersionAsync(null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _log?.Warning($"Server features: read failed ({ex.GetType().Name}).");
            return;
        }

        bool changed;
        lock (_gate)
        {
            _byAddress.TryGetValue(address, out var before);
            if (!result.IsSuccess)
            {
                // Transient: keep what is held, but do not read again at once.
                if (before is not null)
                {
                    _byAddress[address] = before with { ReadUtc = _clock.UtcNow };
                }

                _log?.Info($"Server features of {address} could not be read ({result.Error!.Kind}); keeping what was held.");
                return;
            }

            var names = new HashSet<string>(
                (result.Value!.Features ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
                StringComparer.Ordinal);
            changed = before is null || !before.Names.SetEquals(names);
            _byAddress[address] = new Entry(names, _clock.UtcNow);
        }

        if (changed)
        {
            _log?.Info($"Server features of {address}: {(Current.Count == 0 ? "none" : string.Join(", ", Current.Order(StringComparer.Ordinal)))}.");
            Changed?.Invoke();
        }
    }

    private sealed record Entry(HashSet<string> Names, DateTimeOffset ReadUtc);
}
