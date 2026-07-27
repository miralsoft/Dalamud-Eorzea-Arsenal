using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the player's <i>owned</i> gear (and gear coffers) from the game and maps them to the inventory
/// wire model. The <c>character</c> scope is scanned as one snapshot across every locally readable
/// storage (equipped, armoury, bags, saddlebag, glamour dresser) so moving an item between them is
/// harmless; the armoire is intentionally skipped (it holds only non-tradeable seasonal/unique gear
/// you cannot sell, and needs a different, heavier API). Retainer storages are scanned separately,
/// only while a retainer is open. All game-memory access happens on the framework thread (P1) behind
/// logged-in/null guards (P4); every read is wrapped so no exception ever reaches the game (P2).
/// <para>
/// Besides equippable gear, the loose storages (bags, saddlebag, retainer) also report <b>savage gear
/// coffers</b> (so the web can show "you own this coffer ×N") and the active tier's <b>tracked
/// consumables</b> — the books/tokens/materials the server names via <c>/gear/tracked-items</c> — so
/// its holdings can answer the sourcing view's "have / need". A coffer is identified the same way the
/// web derives them (a usable item whose name contains "Coffer"/"Kiste"); the tracked set is the
/// server's bounded list, so neither needs a hard-coded id list. Everything else (potions, food, other
/// materials) stays filtered out: the server's inventory store must not be flooded.
/// </para>
/// </summary>
public sealed class GameInventorySource : IInventorySource
{
    // Filter to weapons/armour/accessories (and soul crystals): Item.EquipSlotCategory > 0.
    private const int MaxRealItemId = 9_999_999;

    // Substrings that mark a gear coffer's name in the two UI languages the plugin supports. The
    // in-game name is client-locale, so on a non-DE/EN client coffers are simply not recognised (the
    // gear scan is unaffected). Paired with a usable-item gate to exclude housing "coffers".
    private static readonly string[] CofferNameMarkers = ["coffer", "kiste"];

    private static readonly InventoryType[] ArmouryTypes =
    [
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
        InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    private static readonly InventoryType[] BagTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] SaddlebagTypes =
    [
        InventoryType.SaddleBag1, InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2,
    ];

    private static readonly InventoryType[] RetainerTypes =
    [
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
        InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6,
        InventoryType.RetainerPage7, InventoryType.RetainerEquippedItems,
    ];

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IDataManager _data;
    private readonly TrackedItemsStore _tracked;
    private readonly ILog _log;

    /// <summary>Creates the game inventory source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework thread marshaller.</param>
    /// <param name="data">Excel data (for the equippable filter).</param>
    /// <param name="tracked">The tier's tracked consumables to also report (materials/tokens).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameInventorySource(IClientState clientState, IPlayerState playerState, IFramework framework, IDataManager data, TrackedItemsStore tracked, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _data = data;
        _tracked = tracked;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<InventoryData?> ReadCharacterAsync(CancellationToken ct) =>
        _framework.RunOnFrameworkThread(ReadCharacterOnFramework);

    /// <summary>
    /// Whether the chocobo saddlebag can be read right now. The game only fills those containers once
    /// the player has opened the saddlebag in this session.
    /// </summary>
    /// <remarks>
    /// This matters because the saddlebag belongs to the <c>character</c> scope, and the upload
    /// declares that scope <b>fully observed</b> — the server then replaces it entirely. Syncing while
    /// the saddlebag is unreadable therefore tells the server "there is nothing in it", and everything
    /// stored there is deleted. Callers must not sync the character scope until this is true.
    /// </remarks>
    public bool IsSaddlebagReadable => ScanSaddlebag().Count > 0;

    /// <summary>
    /// The saddlebag's contents, or an empty list when it cannot be read.
    /// </summary>
    /// <remarks>
    /// <b>Finding something is the only trustworthy evidence that we looked inside.</b> The obvious
    /// test — <c>IsLoaded</c> on the containers — reports <see langword="true"/> for a saddlebag the
    /// player has not opened this session: the containers exist, they are simply empty. Declaring the
    /// scope on that basis told the server "the saddlebag is empty" and it dutifully deleted what was
    /// in it, which is exactly the loss the separate scope was introduced to prevent.
    /// <para>
    /// The cost of this rule is that a genuinely emptied saddlebag keeps its last known contents until
    /// something is in it again. That is the harmless direction — a stale count can be corrected on
    /// the website, a deleted one cannot be recovered — and it is the same rule the glamour dresser
    /// uses, for the same reason.
    /// </para>
    /// </remarks>
    /// <returns>The equippable gear, coffers and tracked consumables found in the saddlebag.</returns>
    private unsafe List<InventoryItemDto> ScanSaddlebag()
    {
        var items = new List<InventoryItemDto>();
        try
        {
            foreach (var type in SaddlebagTypes)
            {
                AddContainer(items, type, InventoryContainers.Saddlebag, includeCoffers: true);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Saddlebag read failed: {ex.GetType().Name}.");
            return [];
        }

        return items;
    }

    private InventoryData? ReadCharacterOnFramework()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var items = new List<InventoryItemDto>();
            AddContainer(items, InventoryType.EquippedItems, InventoryContainers.Equipped);
            foreach (var t in ArmouryTypes)
            {
                AddContainer(items, t, InventoryContainers.Armoury);
            }

            foreach (var t in BagTypes)
            {
                AddContainer(items, t, InventoryContainers.Bags, includeCoffers: true);
            }

            // The saddlebag is its own scope and is only declared when something was actually found in
            // it — see ScanSaddlebag for why "the containers look loaded" is not evidence enough.
            var scopes = new List<string> { InventoryProtocol.ScopeCharacter };
            var saddlebag = ScanSaddlebag();
            if (saddlebag.Count > 0)
            {
                items.AddRange(saddlebag);
                scopes.Add(InventoryProtocol.ScopeSaddlebag);
            }
            else
            {
                _log.Info("Saddlebag not declared: nothing readable in it (open it once to sync its contents).");
            }

            // The dresser has the same trap and no "loaded" flag to check, so finding at least one
            // piece is the only evidence it was read. That makes an emptied dresser keep its last
            // known contents — the harmless direction, and the one the server's contract expects.
            if (AddGlamourDresser(items) > 0)
            {
                scopes.Add(InventoryProtocol.ScopeGlamour);
            }

            return new InventoryData
            {
                Character = character,
                Scopes = scopes,
                Items = items,
            };
        }
        catch (Exception ex)
        {
            _log.Error($"Inventory read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// Reads the currently-open retainer's storage as a <c>retainer:&lt;id&gt;</c> snapshot, or
    /// <see langword="null"/> when nothing was readable. Must be called on the framework thread (it
    /// is driven from the framework tick).
    /// <para>
    /// As with the saddlebag, finding something is the only trustworthy evidence that we looked
    /// inside: this runs on a timer, <c>LastSelectedRetainerId</c> outlives the visit that set it,
    /// and <c>IsLoaded</c> is true for containers that merely exist. An empty snapshot would
    /// therefore be uploaded for a retainer nobody has been to, and the server would delete its
    /// stock. The cost is the same and is accepted for the same reason: a retainer emptied down to
    /// the last item keeps its last known contents until something is in it again.
    /// </para>
    /// </summary>
    /// <returns>The retainer snapshot, or <see langword="null"/>.</returns>
    public unsafe InventoryData? TryReadActiveRetainer()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var manager = RetainerManager.Instance();
            if (manager == null)
            {
                return null;
            }

            var retainerId = manager->LastSelectedRetainerId;
            if (retainerId == 0)
            {
                return null;
            }

            var sourceId = retainerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var items = new List<InventoryItemDto>();
            foreach (var t in RetainerTypes)
            {
                AddContainer(items, t, InventoryContainers.Retainer, sourceId, includeCoffers: true);
            }

            // Nothing found means we are not at the bell, not that the retainer is empty — see the
            // remarks above. Uploading here would hand the server an empty snapshot to act on.
            if (items.Count == 0)
            {
                return null;
            }

            // The name is only readable here, at the bell. Sending it lets the server's holdings
            // breakdown say "2× at Nanamo" instead of quoting a numeric retainer id; a later sync may
            // omit it and the stored name stays.
            var scope = InventoryProtocol.RetainerScope(sourceId);
            var name = RetainerName(manager);

            return new InventoryData
            {
                Character = character,
                Scopes = [scope],
                Items = items,
                ScopeNames = string.IsNullOrEmpty(name)
                    ? null
                    : new Dictionary<string, string>(StringComparer.Ordinal) { [scope] = name },
            };
        }
        catch (Exception ex)
        {
            _log.Error($"Retainer read failed: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// The open retainer's display name, or <c>""</c> when the game does not hand one over. Only the
    /// active retainer is asked for — this runs while its window is open, which is the same condition
    /// that makes its bags readable at all.
    /// </summary>
    private static unsafe string RetainerName(RetainerManager* manager)
    {
        var retainer = manager->GetActiveRetainer();
        return retainer == null ? string.Empty : retainer->NameString;
    }

    private CharacterDto? ReadCharacter()
    {
        if (!_clientState.IsLoggedIn || !_playerState.IsLoaded)
        {
            return null;
        }

        var contentId = _playerState.ContentId;
        if (contentId == 0)
        {
            return null;
        }

        return new CharacterDto
        {
            Name = _playerState.CharacterName,
            World = _playerState.HomeWorld.Value.Name.ExtractText(),
            CidHash = CidHash.Compute(contentId),
        };
    }

    private unsafe void AddContainer(List<InventoryItemDto> items, InventoryType type, string container, string sourceId = "", bool includeCoffers = false)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return;
        }

        var c = inventory->GetInventoryContainer(type);
        if (c == null || !c->IsLoaded)
        {
            return;
        }

        for (var i = 0; i < c->Size; i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0)
            {
                continue;
            }

            var id = (int)slot->ItemId;
            if (id is <= 0 or > MaxRealItemId || !(IsEquippable(id) || (includeCoffers && (IsGearCoffer(id) || _tracked.Contains(id)))))
            {
                continue;
            }

            items.Add(new InventoryItemDto
            {
                ItemId = id,
                Container = container,
                SourceId = sourceId,
                Hq = slot->IsHighQuality(),
                Qty = Math.Max(1, slot->Quantity),
            });
        }
    }

    /// <summary>Adds the glamour dresser's gear and reports how many pieces were found.</summary>
    /// <param name="items">The scan being built.</param>
    /// <returns>The number of dresser pieces added — zero also means "could not read it".</returns>
    private unsafe int AddGlamourDresser(List<InventoryItemDto> items)
    {
        var added = 0;
        var mirage = MirageManager.Instance();
        if (mirage == null)
        {
            return 0;
        }

        var ids = mirage->PrismBoxItemIds;
        for (var i = 0; i < ids.Length; i++)
        {
            var raw = ids[i];
            if (raw == 0)
            {
                continue;
            }

            // Glamour-dresser ids may carry the +1,000,000 HQ offset; normalize to the base id.
            var hq = raw >= 1_000_000;
            var id = (int)(hq ? raw - 1_000_000 : raw);
            if (id is <= 0 or > MaxRealItemId || !IsEquippable(id))
            {
                continue;
            }

            items.Add(new InventoryItemDto
            {
                ItemId = id,
                Container = InventoryContainers.Glamour,
                Hq = hq,
                Qty = 1,
            });
            added++;
        }

        return added;
    }

    private bool IsEquippable(int itemId)
    {
        var sheet = _data.GetExcelSheet<LuminaItem>();
        return sheet is not null && sheet.TryGetRow((uint)itemId, out var row) && row.EquipSlotCategory.RowId > 0;
    }

    /// <summary>
    /// Whether an item is a gear coffer: a usable item (<c>ItemAction != 0</c>, which rules out housing
    /// "coffers" and other name collisions) whose name carries a coffer marker. The server only keeps
    /// the coffer ids it actually knows (via its tier config), so a rare false positive is harmless;
    /// the real risk — missing a real coffer — cannot happen, as every gear coffer's name contains the
    /// marker. Locale-bound: only recognised on a DE/EN client (see <see cref="CofferNameMarkers"/>).
    /// </summary>
    private bool IsGearCoffer(int itemId)
    {
        var sheet = _data.GetExcelSheet<LuminaItem>();
        if (sheet is null || !sheet.TryGetRow((uint)itemId, out var row) || row.ItemAction.RowId == 0)
        {
            return false;
        }

        var name = row.Name.ExtractText();
        foreach (var marker in CofferNameMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
