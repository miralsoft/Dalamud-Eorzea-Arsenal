using System.Globalization;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The kinds a spot report names.</summary>
public static class WorldKinds
{
    /// <summary>A person, an ENpcResident row.</summary>
    public const string Npc = "npc";

    /// <summary>A monster, a BNpcName row.</summary>
    public const string Mob = "mob";

    /// <summary>A FATE, a Fate row.</summary>
    public const string Fate = "fate";
}

/// <summary>The size factor and offset of the map an object stands on.</summary>
/// <param name="SizeFactor">The map's size factor (100 for a normal field map).</param>
/// <param name="OffsetX">The map's x offset.</param>
/// <param name="OffsetY">The map's y offset, applied to the world's z.</param>
public readonly record struct MapScale(ushort SizeFactor, short OffsetX, short OffsetY);

/// <summary>
/// Turns what the game shows into spot reports for the world database (server 1.5,
/// <c>contract-world-reports.md</c>): only what the wanted list names, at map coordinates with one decimal,
/// once per thing, zone and day. Never the player's own position; pure, the game is read by the plugin.
/// </summary>
public sealed class WorldSpotter
{
    private readonly HashSet<string> _reported = [];
    private string _day = string.Empty;
    private HashSet<int> _npc = [];
    private HashSet<int> _mob = [];
    private HashSet<int> _fate = [];

    /// <summary>Whether a wanted list is held; without one nothing is reported.</summary>
    public bool HasList { get; private set; }

    /// <summary>Takes a new wanted list.</summary>
    /// <param name="wanted">The lists, or <see langword="null"/> to forget them.</param>
    public void SetWanted(WorldWanted? wanted)
    {
        _npc = [.. wanted?.Npc ?? []];
        _mob = [.. wanted?.Mob ?? []];
        _fate = [.. wanted?.Fate ?? []];
        HasList = wanted is not null;
    }

    /// <summary>Whether the list names a thing; checked before anything else is read about it.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="id">The game's id of that kind.</param>
    /// <returns><see langword="true"/> when it is wanted.</returns>
    public bool Wants(string kind, int id) => kind switch
    {
        WorldKinds.Npc => _npc.Contains(id),
        WorldKinds.Mob => _mob.Contains(id),
        WorldKinds.Fate => _fate.Contains(id),
        _ => false,
    };

    /// <summary>A wanted thing seen at a world position; reported once per zone and day.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="id">The game's id of that kind.</param>
    /// <param name="zoneId">The territory's PlaceName row id.</param>
    /// <param name="worldX">The world x of the thing (for a FATE its centre).</param>
    /// <param name="worldZ">The world z of the thing.</param>
    /// <param name="map">The map it stands on.</param>
    /// <param name="dayUtc">Today in UTC.</param>
    /// <returns>The report, or <see langword="null"/> when not wanted, seen already today or off the map.</returns>
    public WorldReport? See(string kind, int id, int zoneId, float worldX, float worldZ, MapScale map, DateOnly dayUtc)
    {
        if (!Wants(kind, id) || zoneId <= 0)
        {
            return null;
        }

        var day = dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (day != _day)
        {
            _day = day;
            _reported.Clear();
        }

        var x = ToMap(worldX, map.OffsetX, map.SizeFactor);
        var y = ToMap(worldZ, map.OffsetY, map.SizeFactor);
        if (x is < 0.5 or > 45.0 || y is < 0.5 or > 45.0 || !_reported.Add($"{kind}|{id}|{zoneId}"))
        {
            return null;
        }

        return new WorldReport { Kind = kind, Id = id, ZoneId = zoneId, X = x, Y = y, Day = day };
    }

    /// <summary>
    /// A world coordinate as the game's map shows it, to one decimal: the map's offset is added, the result
    /// scaled by its size factor onto the 2048-unit texture, and read on the map's 1 to 42 grid.
    /// </summary>
    /// <param name="world">The world coordinate (x, or z for the map's y).</param>
    /// <param name="offset">The map's offset on that axis.</param>
    /// <param name="sizeFactor">The map's size factor.</param>
    /// <returns>The map coordinate.</returns>
    public static double ToMap(float world, short offset, ushort sizeFactor)
    {
        var scale = (sizeFactor <= 0 ? 100 : sizeFactor) / 100.0;
        var value = (41.0 / scale * (((world + offset) * scale) + 1024.0) / 2048.0) + 1.0;
        return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// The wanted list of the world database (<c>GET /world/wanted</c>, server 1.5): read at most once a day while
/// the setting is on, kept with its version. A 409 (intake closed) or a 429 waits a day or an hour.
/// </summary>
public sealed class WorldWantedCache
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IClock _clock;
    private readonly Action<WorldWantedResponse, DateTimeOffset> _persist;
    private readonly ILog _log;
    private DateTimeOffset _nextReadUtc;
    private int _reading;

    /// <summary>Creates the cache.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="clock">Time source (injectable for tests).</param>
    /// <param name="persist">Keeps a list read and when.</param>
    /// <param name="restored">The list kept from before, if any.</param>
    /// <param name="restoredAtUtc">When it was read.</param>
    /// <param name="log">Diagnostics sink.</param>
    public WorldWantedCache(
        IApiClient api,
        ITokenStore tokens,
        IClock clock,
        Action<WorldWantedResponse, DateTimeOffset> persist,
        WorldWantedResponse? restored = null,
        DateTimeOffset? restoredAtUtc = null,
        ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _clock = clock;
        _persist = persist;
        _log = log ?? NullLog.Instance;
        Current = restored;
        _nextReadUtc = restored is not null && restoredAtUtc is { } at ? at + TimeSpan.FromDays(1) : DateTimeOffset.MinValue;
    }

    /// <summary>The list held, or <see langword="null"/> before the first read.</summary>
    public WorldWantedResponse? Current { get; private set; }

    /// <summary>Whether the site takes no reports now (a 409); nothing is collected then.</summary>
    public bool IntakeClosed { get; private set; }

    /// <summary>Reads the list when a day has passed since the last read.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when a new list arrived.</returns>
    public async Task<bool> RefreshIfDueAsync(CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        var now = _clock.UtcNow;
        if (string.IsNullOrEmpty(key) || now < _nextReadUtc || Interlocked.Exchange(ref _reading, 1) != 0)
        {
            return false;
        }

        try
        {
            _nextReadUtc = now + TimeSpan.FromDays(1);
            var result = await _api.GetWorldWantedAsync(key, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Value is { } list)
            {
                IntakeClosed = false;
                Current = list;
                _persist(list, now);
                return true;
            }

            switch (result.Error?.Kind)
            {
                case ApiErrorKind.Conflict:
                    IntakeClosed = true;
                    break;
                case ApiErrorKind.RateLimited:
                    _nextReadUtc = now + TimeSpan.FromHours(1);
                    break;
                case ApiErrorKind.Network or ApiErrorKind.Unexpected:
                    _nextReadUtc = now + TimeSpan.FromHours(1);
                    break;
            }

            _log.Info($"Wanted list not read: {result.Error?.Kind}.");
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Wanted list failed: {ex.GetType().Name}.");
            return false;
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    /// <summary>Forgets the list (the setting was turned off), so it is read again once turned back on.</summary>
    public void Forget()
    {
        Current = null;
        IntakeClosed = false;
        _nextReadUtc = DateTimeOffset.MinValue;
    }
}
