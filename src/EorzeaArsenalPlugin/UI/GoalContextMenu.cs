using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// "Als Ziel hinzufügen" and "Ziel entfernen" in the game's context menu of an item (server 1.5,
/// <c>goals-from-plugin</c>). The choice comes from the goals last read (<see cref="GoalsService.ChoiceFor"/>),
/// never from a read per right-click: "remove" for a goal; "add" only for what is still to get (never learned,
/// a single thing only outside the player's own storage, something that stacks anywhere with a quantity asked
/// first); nothing before the goals were read. The server picks the area and answers with the goals as they are
/// after the change; the player hears the outcome in their own chat. Never throws into the game (P3).
/// </summary>
public sealed class GoalContextMenu : IDisposable
{
    private const int HighQualityOffset = 1_000_000;
    private const int CollectableOffset = 500_000;

    private readonly IContextMenu _menu;
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;
    private readonly IUnlockState _unlocks;
    private readonly GoalsService _goals;
    private readonly Localizer _localizer;
    private readonly Func<bool> _offered;
    private readonly Func<string?> _cidHash;
    private readonly Func<int, string> _itemName;
    private readonly Action<string> _chat;
    private readonly GoalQuantityWindow _quantity;
    private readonly Action _goalsChanged;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _writing;

    /// <summary>Creates the menu entries and listens for the game's context menus.</summary>
    /// <param name="menu">Dalamud's context menu service.</param>
    /// <param name="gameGui">For the item under the cursor in menus that are not an inventory's.</param>
    /// <param name="data">The game data, to know a real item.</param>
    /// <param name="unlocks">Whether the item's unlock is learned.</param>
    /// <param name="goals">The goals service.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="offered">Whether the entries may show now (the server's feature, a key, the terms).</param>
    /// <param name="cidHash">The logged-in character, or <see langword="null"/>.</param>
    /// <param name="itemName">The game's name of an item, for the chat line.</param>
    /// <param name="chat">Prints a line in the player's own chat.</param>
    /// <param name="quantity">Asks how many, for something collected in numbers.</param>
    /// <param name="goalsChanged">Called after a goal was added or removed (the tracked list follows the goals).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GoalContextMenu(
        IContextMenu menu,
        IGameGui gameGui,
        IDataManager data,
        IUnlockState unlocks,
        GoalsService goals,
        Localizer localizer,
        Func<bool> offered,
        Func<string?> cidHash,
        Func<int, string> itemName,
        Action<string> chat,
        GoalQuantityWindow quantity,
        Action goalsChanged,
        ILog log)
    {
        _menu = menu;
        _gameGui = gameGui;
        _data = data;
        _unlocks = unlocks;
        _goals = goals;
        _localizer = localizer;
        _offered = offered;
        _cidHash = cidHash;
        _itemName = itemName;
        _chat = chat;
        _quantity = quantity;
        _goalsChanged = goalsChanged;
        _log = log;
        _menu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (_writing || !_offered() || _cidHash() is not { Length: > 0 } cid)
            {
                return;
            }

            var itemId = ItemOf(args);
            if (itemId == 0 || !_data.GetExcelSheet<LuminaItem>().TryGetRow(itemId, out var row) || row.Name.IsEmpty)
            {
                return;
            }

            var learned = _unlocks.IsItemUnlockable(row) && _unlocks.IsItemUnlocked(row);
            var stacks = row.StackSize > 1;
            var ownStorage = args.Target is MenuTargetInventory;
            var choice = _goals.ChoiceFor(itemId, learned, stacks, ownStorage);
            switch (choice.Action)
            {
                case GoalMenuAction.Add:
                    args.AddMenuItem(Entry(LocKeys.GoalMenuAdd, () => Add(itemId, null, cid)));
                    break;
                case GoalMenuAction.AddWithQuantity:
                    args.AddMenuItem(Entry(LocKeys.GoalMenuAdd, () =>
                        _quantity.Ask(_itemName((int)itemId), BagCount(itemId), quantity => Add(itemId, quantity, cid))));
                    break;
                case GoalMenuAction.Remove when choice.Goal is { } goal:
                    args.AddMenuItem(Entry(LocKeys.GoalMenuRemove, () => Remove(goal.Id, itemId, cid)));
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Goal menu failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// The item the menu is about: an inventory slot's item, or, in another window's menu that is not about a
    /// character, the item under the cursor. The HQ and collectable offsets are taken off.
    /// </summary>
    private uint ItemOf(IMenuOpenedArgs args)
    {
        switch (args.Target)
        {
            case MenuTargetInventory inventory:
                return inventory.TargetItem is { } item ? item.BaseItemId : 0;

            case MenuTargetDefault target when args.AddonName is not null
                && target.TargetContentId == 0
                && string.IsNullOrEmpty(target.TargetName):
                var hovered = _gameGui.HoveredItem;
                if (hovered > HighQualityOffset)
                {
                    hovered -= HighQualityOffset;
                }
                else if (hovered > CollectableOffset)
                {
                    hovered -= CollectableOffset;
                }

                return hovered is > 0 and < CollectableOffset ? (uint)hovered : 0;

            default:
                return 0;
        }
    }

    private MenuItem Entry(string key, Action onClick) => new()
    {
        Name = _localizer.Get(key),
        PrefixChar = 'E',
        PrefixColor = 37,
        OnClicked = _ => onClick(),
    };

    /// <summary>What the bags hold of the item, NQ and HQ, as a hint for the quantity. Framework thread only.</summary>
    private unsafe int BagCount(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId) + inventory->GetInventoryItemCount(itemId, true);
        }
        catch (Exception ex)
        {
            _log.Error($"Goal bag count failed: {ex.GetType().Name}.");
            return 0;
        }
    }

    private void Add(uint itemId, long? quantity, string cid)
    {
        var name = _itemName((int)itemId);
        Write(
            () => _goals.AddAsync(itemId, quantity, Language(), cid, _cts.Token),
            _localizer.Get(LocKeys.GoalAdded, name),
            name);
    }

    private void Remove(long goalId, uint itemId, string cid)
    {
        var name = _itemName((int)itemId);
        Write(
            () => _goals.RemoveAsync(goalId, Language(), cid, _cts.Token),
            _localizer.Get(LocKeys.GoalRemoved, name),
            name);
    }

    private void Write(Func<Task<ApiError?>> call, string success, string name)
    {
        _writing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var error = await call().ConfigureAwait(false);
                _chat(error is null ? success : GoalMessages.Describe(_localizer, error, name));
                if (error is null)
                {
                    _goalsChanged();
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _log.Error($"Goal change failed: {ex.GetType().Name}.");
            }
            finally
            {
                _writing = false;
            }
        });
    }

    private string Language() => _localizer.Language == Localizer.German ? Localizer.German : Localizer.English;

    /// <inheritdoc />
    public void Dispose()
    {
        _menu.OnMenuOpened -= OnMenuOpened;
        _cts.Cancel();
        _cts.Dispose();
    }
}
