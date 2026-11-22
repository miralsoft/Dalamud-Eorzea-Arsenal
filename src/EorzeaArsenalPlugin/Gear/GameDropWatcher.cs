using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Watches duties for drop reports (server 1.5): on entering an instanced duty it notes the inventory, while
/// in it it reads the loot chest about once a second, and on leaving it hands what was seen to the uploader.
/// Framework thread only (P1), never throws (P2), and does nothing at all while <c>active</c> says no.
/// </summary>
/// <remarks>
/// The duty is the territory's ContentFinderCondition, reported by its InstanceContent row id, which is the
/// id the server's catalog holds. A duty that is not instanced content (a roulette's event, a field) is
/// not watched. The log names the duty by id when it starts, so the id can be checked against the catalog.
/// </remarks>
public sealed class GameDropWatcher : IDisposable
{
    private static readonly InventoryType[] Watched =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
        InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    private readonly IClientState _clientState;
    private readonly IDataManager _data;
    private readonly ILog _log;
    private readonly Func<bool> _active;
    private readonly Action<IReadOnlyList<DropReport>> _report;
    private readonly Action _dutyEnded;
    private readonly DropLedger _ledger = new();
    private long _nextScanTicks;

    /// <summary>Creates the watcher and listens for territory changes.</summary>
    /// <param name="clientState">Territory and login state.</param>
    /// <param name="data">Excel data (the duty behind a territory).</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <param name="active">Whether drops may be collected right now (the setting, consent and server).</param>
    /// <param name="report">Takes the drops seen.</param>
    /// <param name="dutyEnded">Called when a watched duty ends, so the uploader can send at once.</param>
    public GameDropWatcher(IClientState clientState, IDataManager data, ILog log, Func<bool> active, Action<IReadOnlyList<DropReport>> report, Action dutyEnded)
    {
        _clientState = clientState;
        _data = data;
        _log = log;
        _active = active;
        _report = report;
        _dutyEnded = dutyEnded;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _clientState.Logout += OnLogout;
    }

    /// <summary>Reads the loot chest about once a second while in a watched duty. Framework thread.</summary>
    public void Tick()
    {
        try
        {
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

            _nextScanTicks = now + 1_000;
            ScanChest();
        }
        catch (Exception ex)
        {
            _log.Error($"Drop watch failed: {ex.GetType().Name}.");
        }
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

            if (!_active() || DutyOf(territory) is not { } contentId)
            {
                return;
            }

            _ledger.Start(contentId, ReadInventory());
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

        var received = _ledger.Finish(ReadInventory(), DateOnly.FromDateTime(DateTime.UtcNow));
        if (received.Count > 0)
        {
            _report(received);
        }

        _log.Info($"Drop watch: duty ended, {received.Count} item(s) received without a chest.");
        _dutyEnded();
    }

    private void OnLogout(int type, int code) => _ledger.Abandon();

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

    /// <summary>Count per item id in the bags and the armoury, HQ and NQ together.</summary>
    private static unsafe Dictionary<int, int> ReadInventory()
    {
        var counts = new Dictionary<int, int>();
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return counts;
        }

        foreach (var type in Watched)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
            {
                continue;
            }

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
