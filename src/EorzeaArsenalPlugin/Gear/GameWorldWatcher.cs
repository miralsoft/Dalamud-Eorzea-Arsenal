using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Looks for what the world database wants placed (server 1.5): every two seconds it goes through the
/// people, monsters and FATEs the game has loaded and hands the wanted ones to the <see cref="WorldSpotter"/>.
/// Only their positions are read, never the player's own. Framework thread only (P1), never throws (P2),
/// and does nothing while <c>active</c> says no.
/// </summary>
/// <remarks>
/// The zone is the territory's PlaceName row id. A territory with more than one map is left out, as agreed
/// with the server, because the spot would have to name which map it is on.
/// </remarks>
public sealed class GameWorldWatcher : IDisposable
{
    private readonly IClientState _clientState;
    private readonly IObjectTable _objects;
    private readonly IFateTable _fates;
    private readonly IDataManager _data;
    private readonly ILog _log;
    private readonly WorldSpotter _spotter;
    private readonly Func<bool> _active;
    private readonly Action<IReadOnlyList<WorldReport>> _report;
    private readonly Action _loggedOut;
    private readonly Dictionary<uint, (int ZoneId, MapScale Map)?> _zones = [];
    private Dictionary<uint, int>? _mapsPerTerritory;
    private long _nextScanTicks;

    /// <summary>Creates the watcher.</summary>
    /// <param name="clientState">Territory and login state.</param>
    /// <param name="objects">The loaded objects.</param>
    /// <param name="fates">The active FATEs.</param>
    /// <param name="data">Excel data (zone and map of a territory).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="spotter">Holds the wanted list and turns sightings into reports.</param>
    /// <param name="active">Whether spots may be collected right now.</param>
    /// <param name="report">Takes the spots seen.</param>
    /// <param name="loggedOut">Called on logout, so the uploader can send at once.</param>
    public GameWorldWatcher(
        IClientState clientState,
        IObjectTable objects,
        IFateTable fates,
        IDataManager data,
        ILog log,
        WorldSpotter spotter,
        Func<bool> active,
        Action<IReadOnlyList<WorldReport>> report,
        Action loggedOut)
    {
        _clientState = clientState;
        _objects = objects;
        _fates = fates;
        _data = data;
        _log = log;
        _spotter = spotter;
        _active = active;
        _report = report;
        _loggedOut = loggedOut;
        _clientState.Logout += OnLogout;
    }

    /// <summary>Looks around every two seconds while active. Framework thread.</summary>
    public void Tick()
    {
        try
        {
            var now = Environment.TickCount64;
            if (now < _nextScanTicks)
            {
                return;
            }

            _nextScanTicks = now + 2_000;
            if (!_clientState.IsLoggedIn || !_spotter.HasList || !_active() || ZoneOf(_clientState.TerritoryType) is not { } zone)
            {
                return;
            }

            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var seen = new List<WorldReport>();
            foreach (var obj in _objects)
            {
                var (kind, id) = obj.ObjectKind switch
                {
                    ObjectKind.EventNpc => (WorldKinds.Npc, (int)obj.BaseId),
                    ObjectKind.BattleNpc when obj is ICharacter character => (WorldKinds.Mob, (int)character.NameId),
                    _ => (string.Empty, 0),
                };

                if (id > 0 && _spotter.Wants(kind, id) &&
                    _spotter.See(kind, id, zone.ZoneId, obj.Position.X, obj.Position.Z, zone.Map, day) is { } spot)
                {
                    seen.Add(spot);
                }
            }

            foreach (var fate in _fates)
            {
                var id = (int)fate.FateId;
                if (id > 0 && _spotter.Wants(WorldKinds.Fate, id) &&
                    _spotter.See(WorldKinds.Fate, id, zone.ZoneId, fate.Position.X, fate.Position.Z, zone.Map, day) is { } spot)
                {
                    seen.Add(spot);
                }
            }

            if (seen.Count > 0)
            {
                _log.Info($"World watch: {seen.Count} wanted thing(s) seen in zone {zone.ZoneId}.");
                _report(seen);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"World watch failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>The zone and map of a territory, or <see langword="null"/> when it has none or several maps.</summary>
    private (int ZoneId, MapScale Map)? ZoneOf(uint territory)
    {
        if (territory == 0)
        {
            return null;
        }

        if (_zones.TryGetValue(territory, out var known))
        {
            return known;
        }

        (int, MapScale)? zone = null;
        var territories = _data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        if (territories is not null && territories.TryGetRow(territory, out var row) &&
            row.PlaceName.RowId > 0 && row.Map.RowId > 0 && MapsOf(territory) == 1 &&
            row.Map.ValueNullable is { } map)
        {
            zone = ((int)row.PlaceName.RowId, new MapScale(map.SizeFactor, map.OffsetX, map.OffsetY));
        }

        _zones[territory] = zone;
        return zone;
    }

    private int MapsOf(uint territory)
    {
        if (_mapsPerTerritory is null)
        {
            _mapsPerTerritory = [];
            var maps = _data.GetExcelSheet<Lumina.Excel.Sheets.Map>();
            if (maps is not null)
            {
                foreach (var map in maps)
                {
                    var owner = map.TerritoryType.RowId;
                    if (owner > 0)
                    {
                        _mapsPerTerritory[owner] = _mapsPerTerritory.GetValueOrDefault(owner) + 1;
                    }
                }
            }
        }

        return _mapsPerTerritory.GetValueOrDefault(territory);
    }

    private void OnLogout(int type, int code) => _loggedOut();

    /// <inheritdoc />
    public void Dispose() => _clientState.Logout -= OnLogout;
}
