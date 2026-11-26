using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Watches duties for drop reports (server 1.5): on entering an instanced duty it notes the inventory once
/// the character has arrived, while in it it reads the loot chest five times a second, and on leaving it
/// hands what was seen to the uploader. Framework thread only (P1), never throws (P2), and does nothing at
/// all while <c>active</c> says no.
/// </summary>
/// <remarks>
/// <para>
/// The duty is the territory's ContentFinderCondition, reported by its InstanceContent row id, which is the
/// id the server's catalog holds. A duty that is not instanced content (a roulette's event, a field) is
/// not watched. The log names the duty by id when it starts, so the id can be checked against the catalog.
/// </para>
/// <para>
/// What arrived without a chest is the inventory's gain between the two notes, and three things keep that
/// gain honest. The first note waits until the zone change is over and every watched container reads as
/// loaded, so a container still loading is never mistaken for an empty one whose contents then appear as
/// received. Only containers loaded at both notes are compared. And the equipped items are part of it, so
/// moving a piece between what is worn and the armoury nets out. Every report is written to the log by item,
/// with its name, so a duty can be checked afterwards.
/// </para>
/// </remarks>
public sealed class GameDropWatcher : IDisposable
{
    private static readonly InventoryType[] Watched =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
        InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    /// <summary>How often the loot chest is read: often enough that a piece the group passes on is still seen.</summary>
    private const long ScanEveryMs = 200;

    /// <summary>How long the first note waits for every container to load before it takes what is there.</summary>
    private const long ArrivalTimeoutMs = 30_000;

    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IDataManager _data;
    private readonly ILog _log;
    private readonly Func<bool> _active;
    private readonly Action<IReadOnlyList<DropReport>> _report;
    private readonly Action _dutyEnded;
    private readonly DropLedger _ledger = new();
    private HashSet<InventoryType> _loadedAtStart = [];
    private int? _arriving;
    private long _arrivingSince;
    private long _nextScanTicks;

    /// <summary>Creates the watcher and listens for territory changes.</summary>
    /// <param name="clientState">Territory and login state.</param>
    /// <param name="condition">Whether the zone change is still running.</param>
    /// <param name="data">Excel data (the duty behind a territory, item names for the log).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="active">Whether drops may be collected right now (the setting, consent and server).</param>
    /// <param name="report">Takes the drops seen.</param>
    /// <param name="dutyEnded">Called when a watched duty ends, so the uploader can send at once.</param>
    public GameDropWatcher(IClientState clientState, ICondition condition, IDataManager data, ILog log, Func<bool> active, Action<IReadOnlyList<DropReport>> report, Action dutyEnded)
    {
        _clientState = clientState;
        _condition = condition;
        _data = data;
        _log = log;
        _active = active;
        _report = report;
        _dutyEnded = dutyEnded;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _clientState.Logout += OnLogout;
    }

    /// <summary>Notes the arrival, then reads the loot chest five times a second while in a watched duty. Framework thread.</summary>
    public void Tick()
    {
        try
        {
            if (_arriving is not null)
            {
                TryNoteArrival();
                return;
            }

            if (_ledger.ContentId is null)
            {
                return;
            }

            if (!_active())
            {
                // Turned off mid-duty: nothing is collected and nothing kept.
                _ledger.Abandon();
                return;
            }

            var now = Environment.TickCount64;
            if (now < _nextScanTicks)
            {
                return;
            }

            _nextScanTicks = now + ScanEveryMs;
            ScanChest();
        }
        catch (Exception ex)
        {
            _log.Error($"Drop watch failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>Takes the first note once the zone change is over and every container is loaded, or after a timeout.</summary>
    private void TryNoteArrival()
    {
        if (!_active())
        {
            _arriving = null;
            return;
        }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }

        var counts = ReadInventory(out var loaded);
        var waited = Environment.TickCount64 - _arrivingSince;
        if (loaded.Count < Watched.Length && waited < ArrivalTimeoutMs)
        {
            return;
        }

        var contentId = _arriving!.Value;
        _arriving = null;
        _loadedAtStart = loaded;
        _ledger.Start(contentId, counts);
        _log.Info($"Drop watch: inventory noted ({loaded.Count} of {Watched.Length} containers loaded, after {waited} ms).");
    }

    private unsafe void ScanChest()
    {
        var loot = Loot.Instance();
        if (loot == null)
        {
            return;
        }

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var seen = new List<DropReport>();
        foreach (ref readonly var item in loot->Items)
        {
            if (item.ItemId == 0 || item.ChestObjectId == 0)
            {
                continue;
            }

            if (_ledger.SeeChestItem(item.ChestObjectId, item.ChestItemIndex, item.ItemId, item.ItemCount, day) is { } drop)
            {
                seen.Add(drop);
            }
        }

        if (seen.Count > 0)
        {
            LogReports(seen);
            _report(seen);
        }
    }

    private void OnTerritoryChanged(uint territory)
    {
        try
        {
            if (_ledger.ContentId is not null)
            {
                FinishDuty();
            }

            _arriving = null;
            if (!_active() || DutyOf(territory) is not { } contentId)
            {
                return;
            }

            // The inventory is noted on the first tick after the zone change, once everything has loaded.
            _arriving = contentId;
            _arrivingSince = Environment.TickCount64;
            _log.Info($"Drop watch: duty started (content_id {contentId}, territory {territory}).");
        }
        catch (Exception ex)
        {
            _log.Error($"Drop watch territory change failed: {ex.GetType().Name}.");
        }
    }

    private void FinishDuty()
    {
        if (!_active())
        {
            _ledger.Abandon();
            return;
        }

        // Only containers loaded at both notes are compared; one missing at the end only lowers the gain.
        var counts = ReadInventory(out _, only: _loadedAtStart);
        var received = _ledger.Finish(counts, DateOnly.FromDateTime(DateTime.UtcNow));
        if (received.Count > 0)
        {
            LogReports(received);
            _report(received);
        }

        _log.Info($"Drop watch: duty ended, {received.Count} item(s) received without a chest.");
        _dutyEnded();
    }

    private void OnLogout(int type, int code)
    {
        _arriving = null;
        _ledger.Abandon();
    }

    /// <summary>Writes each report with the item's name, so a duty can be checked against what dropped.</summary>
    private void LogReports(IReadOnlyList<DropReport> reports)
    {
        var items = _data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        foreach (var report in reports)
        {
            var name = items is not null && items.TryGetRow((uint)report.ItemId, out var row) ? row.Name.ExtractText() : "?";
            _log.Info($"Drop watch: {report.Seen} {report.ItemId} \"{name}\" x{report.Qty} (content_id {report.ContentId}).");
        }
    }

    /// <summary>The InstanceContent row id behind a territory, or <see langword="null"/> when it is no instanced duty.</summary>
    private int? DutyOf(uint territory)
    {
        var territories = _data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        var conditions = _data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>();
        if (territories is null || conditions is null || !territories.TryGetRow(territory, out var row))
        {
            return null;
        }

        var cfc = row.ContentFinderCondition.RowId;
        if (cfc == 0 || !conditions.TryGetRow(cfc, out var condition))
        {
            return null;
        }

        // Link type 1 is InstanceContent; anything else is not in the server's duty catalog.
        return condition.ContentLinkType == 1 && condition.Content.RowId is > 0 and < 10_000_000 ? (int)condition.Content.RowId : null;
    }

    /// <summary>Count per item id in the bags, the equipped items and the armoury, HQ and NQ together.</summary>
    /// <param name="loaded">The containers that read as loaded.</param>
    /// <param name="only">When set, only these containers are read.</param>
    private static unsafe Dictionary<int, int> ReadInventory(out HashSet<InventoryType> loaded, HashSet<InventoryType>? only = null)
    {
        var counts = new Dictionary<int, int>();
        loaded = [];
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return counts;
        }

        foreach (var type in Watched)
        {
            if (only is not null && !only.Contains(type))
            {
                continue;
            }

            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
            {
                continue;
            }

            loaded.Add(type);
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0)
                {
                    continue;
                }

                var id = (int)slot->ItemId;
                counts[id] = counts.GetValueOrDefault(id) + (int)slot->Quantity;
            }
        }

        return counts;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _clientState.Logout -= OnLogout;
    }
}
