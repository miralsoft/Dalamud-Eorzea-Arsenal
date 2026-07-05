using System.Runtime.CompilerServices;
using System.Text;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LuminaCfc = Lumina.Excel.Sheets.ContentFinderCondition;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>
/// Reads the player's <i>weekly checklist</i> progress from the game and maps it to the weekly wire
/// model. Only fields that can be read with confidence are set:
/// <list type="bullet">
/// <item><b>tomesHave</b> — weekly-limited tomestones acquired this week (<see cref="InventoryManager"/>).</item>
/// <item><b>custom</b> — Custom Deliveries: reported <i>done</i> only when all weekly allowances are
/// used (<see cref="SatisfactionSupplyManager"/>); a "not done" is never sent, so an un-loaded state
/// or a manual web-app entry is never clobbered (merge is one-directional here on purpose).</item>
/// </list>
/// The remaining fields are read where the game exposes them reliably:
/// <list type="bullet">
/// <item><b>f1..f4</b> (Savage) — the Raid Finder's per-floor weekly-loot flag (live or via a hidden
/// refresh).</item>
/// <item><b>unreal</b> — the Faux Hollows timestamp vs the weekly reset (<see cref="PlayerState"/>,
/// background-readable).</item>
/// <item><b>wondrous</b> — a completed Wondrous Tails book whose expiry is beyond the next reset,
/// i.e. a book bought this week (<see cref="PlayerState"/>, background-readable).</item>
/// <item><b>normal</b>/<b>alliance</b> — the Duty Finder's weekly-reward count for the selected duty
/// (only while it is open), classified via the duty's party size.</item>
/// </list>
/// Only confident values are set; "not done" is never sent for the soft flags, so a manual web-app
/// entry is never clobbered. All game-memory access happens on the framework thread (P1) behind
/// logged-in/null guards (P4); every read is wrapped so no exception ever reaches the game (P2).
/// </summary>
public sealed class GameWeeklySource : IWeeklySource
{
    // Custom Deliveries grant 12 weekly allowances in total.
    private const int CustomWeeklyAllowances = 12;

    // ContentType row id for "Raids" (normal/Savage/alliance all share it); stable game data.
    private const uint RaidContentType = 5;

    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IFramework _framework;
    private readonly IGameGui _gameGui;
    private readonly IDataManager _data;
    private readonly ILog _log;

    /// <summary>Creates the game weekly source.</summary>
    /// <param name="clientState">Login state.</param>
    /// <param name="playerState">Local character identity (name, world, ContentId).</param>
    /// <param name="framework">Framework thread marshaller.</param>
    /// <param name="gameGui">Addon lookup (for the temporary diagnostic probe).</param>
    /// <param name="data">Excel data access (classifies a Duty Finder duty as normal vs alliance).</param>
    /// <param name="log">Diagnostics sink.</param>
    public GameWeeklySource(IClientState clientState, IPlayerState playerState, IFramework framework, IGameGui gameGui, IDataManager data, ILog log)
    {
        _clientState = clientState;
        _playerState = playerState;
        _framework = framework;
        _gameGui = gameGui;
        _data = data;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsAvailable => _clientState.IsLoggedIn && _playerState.IsLoaded && _playerState.ContentId != 0;

    /// <inheritdoc />
    public Task<WeeklyData?> ReadAsync(CancellationToken ct) =>
        _framework.RunOnFrameworkThread(ReadOnFramework);

    private WeeklyData? ReadOnFramework()
    {
        try
        {
            var character = ReadCharacter();
            if (character is null)
            {
                return null;
            }

            var (f1, f2, f3, f4) = ReadSavageFloors();
            var (normal, alliance) = ReadNormalAlliance();
            var values = new WeeklyValues
            {
                TomesHave = ReadWeeklyTomes(),
                Custom = ReadCustomDone(),
                F1 = f1,
                F2 = f2,
                F3 = f3,
                F4 = f4,
                Unreal = ReadUnreal(),
                Wondrous = ReadWondrous(),
                Normal = normal,
                Alliance = alliance,
            };

            return new WeeklyData { Character = character, Values = values };
        }
        catch (Exception ex)
        {
            _log.Error($"Weekly read failed: {ex.GetType().Name}.");
            return null;
        }
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

    private unsafe int? ReadWeeklyTomes()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return null;
        }

        var acquired = (int)inventory->GetWeeklyAcquiredTomestoneCount();
        var limit = (int)InventoryManager.GetLimitedTomestoneWeeklyLimit();
        if (limit <= 0)
        {
            limit = WeeklyProtocol.MaxTomes;
        }

        return Math.Clamp(acquired, 0, limit);
    }

    private unsafe bool? ReadCustomDone()
    {
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null)
        {
            return null;
        }

        var remaining = (int)manager->GetRemainingAllowances();
        var used = (int)manager->GetUsedAllowances();

        // Only report a *confident* "done": all weekly allowances used. Never send "not done" — the
        // data may not be loaded yet, and merge must never overwrite a manual web-app entry.
        return used + remaining == CustomWeeklyAllowances && remaining == 0 ? true : null;
    }

    /// <summary>
    /// Whether the Unreal trial was done this week, read from <c>PlayerState.FauxHollowsTimestamp</c>
    /// (background-readable). Returns <see langword="true"/> only when confidently done; otherwise
    /// <see langword="null"/> so a manual web-app entry is never overwritten.
    /// </summary>
    private unsafe bool? ReadUnreal()
    {
        var ps = PlayerState.Instance();
        if (ps == null)
        {
            return null;
        }

        return WeeklyDecode.IsUnrealDone(ps->FauxHollowsTimestamp, DateTimeOffset.UtcNow) ? true : null;
    }

    /// <summary>
    /// Whether a Wondrous Tails book bought this week is complete, read from <see cref="PlayerState"/>
    /// (background-readable). A completed book alone is ambiguous (the count stays stale after a
    /// hand-in and a book is valid two weeks), so <see cref="WeeklyDecode.IsWondrousDone"/> anchors on
    /// the book's expiry to isolate a this-week book. <see langword="true"/> or <see langword="null"/>.
    /// </summary>
    private unsafe bool? ReadWondrous()
    {
        var ps = PlayerState.Instance();
        if (ps == null)
        {
            return null;
        }

        var complete = WeeklyDecode.IsWondrousDone(
            ps->WeeklyBingoNumPlacedStickers,
            ps->GetWeeklyBingoExpireUnixTimestamp(),
            DateTimeOffset.UtcNow);
        return complete ? true : null;
    }

    /// <summary>
    /// The normal-raid and alliance-raid weekly-reward state, read from the Duty Finder's selected
    /// duty while it is open. The finder only exposes the received/max reward count for the
    /// <i>selected</i> duty, so this reports only when the user has such a duty selected (an
    /// opportunistic, non-intrusive read — it never opens or drives the window). A weekly-locked duty
    /// (max &gt; 0) whose reward is received is classed as normal (8-player) or alliance (24-player)
    /// by party size. Only <see langword="true"/> is ever produced — never a "not done".
    /// </summary>
    private (bool? Normal, bool? Alliance) ReadNormalAlliance()
    {
        // Live read of the currently-selected duty (when the user has the finder open) plus, as a
        // fallback, the fresh values a hidden refresh gathered moments ago (≤ 2 min — still server-
        // fresh; no reward can change outside a duty). Prefer any confident "true" from either source.
        var (normal, alliance) = ReadNormalAllianceLive();
        if (_freshNormalAllianceTicks != 0 && Environment.TickCount64 - _freshNormalAllianceTicks < 120_000)
        {
            normal ??= _freshNormal;
            alliance ??= _freshAlliance;
        }

        return (normal, alliance);
    }

    /// <summary>
    /// Reads the weekly reward of the Duty Finder's currently-selected duty (only while it is open),
    /// classified normal/alliance. One of the two at most (whatever is selected); <see langword="null"/>
    /// when the finder is closed or the selection is not a done normal/alliance raid.
    /// </summary>
    private unsafe (bool? Normal, bool? Alliance) ReadNormalAllianceLive()
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null || !agent->IsAgentActive())
        {
            return (null, null);
        }

        var dutyId = agent->InterfaceSub.SelectedDutyId;
        if (dutyId <= 0)
        {
            return (null, null);
        }

        var max = agent->InterfaceSub.GetMaxReceivedRewardCount();
        var received = agent->InterfaceSub.GetReceivedRewardCount();
        if (max <= 0 || received < max)
        {
            return (null, null); // not weekly-locked, or not yet fully rewarded this week
        }

        return ClassifyRaidDuty((uint)dutyId) switch
        {
            RaidDutyKind.Normal => (true, null),
            RaidDutyKind.Alliance => (null, true),
            _ => (null, null),
        };
    }

    /// <summary>How a Duty Finder raid maps to the weekly checklist.</summary>
    private enum RaidDutyKind
    {
        /// <summary>Not a normal/alliance raid we track (Savage, a trial, a dungeon, …).</summary>
        Other,

        /// <summary>An 8-player normal raid → the <c>normal</c> field.</summary>
        Normal,

        /// <summary>A 24-player alliance raid → the <c>alliance</c> field.</summary>
        Alliance,
    }

    /// <summary>Cached <c>InstanceContentId → kind</c> map for normal/alliance raids (built once from Excel).</summary>
    private Dictionary<uint, RaidDutyKind>? _raidDutyKinds;

    /// <summary>
    /// Classifies a Duty Finder duty (by its InstanceContent id) as a normal or alliance raid using
    /// the ContentFinderCondition sheet: a "Raids" duty that is not high-end (Savage) is normal when
    /// its party is 8-player and alliance when it is 24-player. Built lazily and cached.
    /// </summary>
    private RaidDutyKind ClassifyRaidDuty(uint instanceContentId)
    {
        _raidDutyKinds ??= BuildRaidDutyKinds();
        return _raidDutyKinds.GetValueOrDefault(instanceContentId, RaidDutyKind.Other);
    }

    private Dictionary<uint, RaidDutyKind> BuildRaidDutyKinds()
    {
        var map = new Dictionary<uint, RaidDutyKind>();
        try
        {
            var sheet = _data.GetExcelSheet<LuminaCfc>();
            if (sheet is null)
            {
                return map;
            }

            foreach (var row in sheet)
            {
                var contentId = row.Content.RowId;
                if (contentId == 0 || row.ContentType.RowId != RaidContentType)
                {
                    continue;
                }

                var member = row.ContentMemberType.ValueNullable;
                // 24-player alliance content registers three parties; normal light-party raids one.
                var isAlliance = member is { } m && (m.AlliancePartyCount >= 2 || m.PartyCount >= 3);
                if (isAlliance)
                {
                    map[contentId] = RaidDutyKind.Alliance;
                }
                else if (!row.HighEndDuty)
                {
                    map[contentId] = RaidDutyKind.Normal; // 8-player, non-Savage → normal raid
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Weekly raid classification failed: {ex.GetType().Name}.");
        }

        return map;
    }

    /// <summary>
    /// Whether the Savage floor state is fully readable right now — the Raid Finder is open <i>and</i>
    /// its Raids tab is populated with the four current-tier floors. Guards the opportunistic trigger
    /// so it does not fire a frame too early (agent active but the list not yet filled).
    /// </summary>
    public unsafe bool IsSavageReadable
    {
        get
        {
            var agent = AgentRaidFinder.Instance();
            if (agent == null || !agent->IsAgentActive())
            {
                return false;
            }

            var tabs = agent->Tabs;
            return tabs.Length > 0 && tabs[0].EntryCount == 4 && tabs[0].Entries[0].InstanceContentId != 0;
        }
    }

    /// <summary>
    /// The InstanceContent id of the Duty Finder's currently-selected duty when it is a normal/alliance
    /// raid whose weekly reward is already received (i.e. a value <see cref="ReadNormalAlliance"/>
    /// would report), else <c>0</c>. Lets the plugin edge-trigger a sync when the user selects such a
    /// duty, without ever opening or driving the window.
    /// </summary>
    public unsafe uint SelectedDoneRaidDutyId
    {
        get
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null || !agent->IsAgentActive())
            {
                return 0;
            }

            var dutyId = agent->InterfaceSub.SelectedDutyId;
            if (dutyId <= 0)
            {
                return 0;
            }

            var max = agent->InterfaceSub.GetMaxReceivedRewardCount();
            var received = agent->InterfaceSub.GetReceivedRewardCount();
            if (max <= 0 || received < max)
            {
                return 0;
            }

            return ClassifyRaidDuty((uint)dutyId) is RaidDutyKind.Normal or RaidDutyKind.Alliance ? (uint)dutyId : 0u;
        }
    }

    /// <summary>
    /// The current Savage tier's weekly loot state (f1..f4): a live read while the Raid Finder is
    /// open (also refreshing the short-lived cache), else the cache from a hidden refresh moments
    /// ago (≤ 2 min — still server-fresh; no loot can change outside a duty). All
    /// <see langword="null"/> when neither is available, so nothing is sent and no manual web-app
    /// entry is ever clobbered.
    /// </summary>
    private (bool? F1, bool? F2, bool? F3, bool? F4) ReadSavageFloors()
    {
        var live = ReadSavageFloorsLive();
        if (live.F1 is not null)
        {
            _freshFloors = live;
            _freshFloorsTicks = Environment.TickCount64;
            return live;
        }

        if (_freshFloorsTicks != 0 && Environment.TickCount64 - _freshFloorsTicks < 120_000)
        {
            return _freshFloors;
        }

        return (null, null, null, null);
    }

    /// <summary>
    /// Reads the floors from the live Raid Finder tab data — only while the window/agent is active:
    /// the game populates the tabs on demand (server-fresh), and they are stale garbage when closed.
    /// The per-floor flag <c>UnkFlags1 &amp; 0x04</c> means "weekly reward obtained this week"
    /// (verified in-game across two loot states).
    /// </summary>
    private unsafe (bool? F1, bool? F2, bool? F3, bool? F4) ReadSavageFloorsLive()
    {
        var agent = AgentRaidFinder.Instance();
        if (agent == null || !agent->IsAgentActive())
        {
            return (null, null, null, null);
        }

        var tabs = agent->Tabs;
        if (tabs.Length == 0)
        {
            return (null, null, null, null);
        }

        // Tab 0 is the "Raids" tab; the current Savage tier is its (exactly four) weekly-locked floors.
        var tab = tabs[0];
        if (tab.EntryCount != 4)
        {
            return (null, null, null, null);
        }

        var entries = tab.Entries;
        var result = new bool?[4];
        for (var i = 0; i < 4; i++)
        {
            var entry = entries[i];
            if (entry.InstanceContentId == 0)
            {
                return (null, null, null, null); // not a sane entry — don't trust anything
            }

            // UnkFlags1 lives at offset 16; it is internal, so read it from a stack copy of the entry.
            var raw = (byte*)Unsafe.AsPointer(ref entry);
            result[i] = (raw[16] & 0x04) != 0;
        }

        return (result[0], result[1], result[2], result[3]);
    }

    /// <summary>
    /// Temporary developer probe: dumps the candidate weekly values via the game's own APIs (no memory
    /// hacking) so their meaning can be confirmed from known in-game states before any of them is
    /// trusted for upload. Covers the Duty Finder weekly-reward counts (open the "Difficult content"
    /// tab and select a floor first), Faux Hollows (Unreal), Custom Deliveries, Wondrous Tails and the
    /// weekly tomestones. Read-only; must run on the framework thread; never throws (P2). Not committed
    /// to the sync path — purely diagnostic.
    /// </summary>
    /// <returns>A multi-line dump for the log.</returns>
    public unsafe string ReadRawWeeklyDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("weekprobe:");

        Section(sb, "tomes", () =>
        {
            var inv = InventoryManager.Instance();
            return inv == null
                ? "InventoryManager null"
                : $"weeklyAcquired={inv->GetWeeklyAcquiredTomestoneCount()} weeklyLimit={InventoryManager.GetLimitedTomestoneWeeklyLimit()}";
        });

        Section(sb, "custom", () =>
        {
            var mgr = SatisfactionSupplyManager.Instance();
            return mgr == null
                ? "SatisfactionSupplyManager null"
                : $"used={mgr->GetUsedAllowances()} remaining={mgr->GetRemainingAllowances()}";
        });

        Section(sb, "unreal/wondrous", () =>
        {
            var ps = PlayerState.Instance();
            return ps == null
                ? "PlayerState null"
                : $"fauxState={ps->FauxHollowsState} fauxTs={ps->FauxHollowsTimestamp} " +
                  $"bingoJournal={ps->HasWeeklyBingoJournal} bingoStickers={ps->WeeklyBingoNumPlacedStickers} " +
                  $"bingoSecondChance={ps->WeeklyBingoNumSecondChancePoints} " +
                  $"bingoExpireTs={ps->GetWeeklyBingoExpireUnixTimestamp()} bingoExpired={ps->IsWeeklyBingoExpired()}";
        });

        Section(sb, "visibleAddons", () =>
        {
            var stage = AtkStage.Instance();
            if (stage == null)
            {
                return "AtkStage null";
            }

            var mgr = stage->RaptureAtkUnitManager;
            if (mgr == null)
            {
                return "RaptureAtkUnitManager null";
            }

            var inner = new StringBuilder();
            var list = mgr->AtkUnitManager.AllLoadedUnitsList;
            int count = list.Count;
            var entries = list.Entries;
            for (var i = 0; i < count && i < entries.Length; i++)
            {
                var u = entries[i].Value;
                if (u == null || !u->IsVisible)
                {
                    continue;
                }

                inner.Append(' ').Append(u->NameString);
            }

            return inner.Length == 0 ? "(none visible)" : inner.ToString();
        });

        Section(sb, "contentValue", () =>
        {
            var ps = PlayerState.Instance();
            if (ps == null)
            {
                return "PlayerState null";
            }

            // Known current-tier ids from earlier probes: R1..R4 InstanceContent + ContentFinderCondition.
            uint[] icIds = { 30155, 30157, 30159, 30161 };
            uint[] cfcIds = { 1069, 1071, 1073, 1075 };
            var inner = new StringBuilder("ic[");
            foreach (var id in icIds)
            {
                inner.Append($"{id}={ps->GetContentValue(id)} ");
            }

            inner.Append("] cfc[");
            foreach (var id in cfcIds)
            {
                inner.Append($"{id}={ps->GetContentValue(id)} ");
            }

            inner.Append(']');
            return inner.ToString();
        });

        Section(sb, "contentKV", () =>
        {
            var ps = PlayerState.Instance();
            if (ps == null)
            {
                return "PlayerState null";
            }

            var kv = ps->ContentKeyValueData;
            var inner = new StringBuilder($"count={kv.Length}:");
            for (var i = 0; i < kv.Length; i++)
            {
                var pair = kv[i];
                if (pair.Item1 == 0 && pair.Item2 == 0)
                {
                    continue;
                }

                inner.Append($" {pair.Item1}={pair.Item2}");
            }

            return inner.ToString();
        });

        Section(sb, "uiStateRegions", () =>
        {
            var ui = UIState.Instance();
            if (ui == null)
            {
                return "UIState null";
            }

            // Two ClientStructs-unmapped sub-structs of UIState that plausibly hold per-duty weekly
            // state: InstanceContent (@80208, 120 bytes) and ContentsFinder (@80424, 176 bytes).
            // Hexdump both (in-bounds of UIState, size 107520; read-only) so we can DIFF the bytes
            // across known state changes (open raid finder / clear R2 / weekly reset).
            var basePtr = (byte*)ui;
            var inner = new StringBuilder();
            AppendRegion(inner, "InstanceContent@80208", basePtr, 80208, 120);
            AppendRegion(inner, "ContentsFinder@80424", basePtr, 80424, 176);
            return inner.ToString();
        });

        Section(sb, "icHeap", () =>
        {
            var ui = UIState.Instance();
            if (ui == null)
            {
                return "UIState null";
            }

            // The UIState.InstanceContent sub-struct (@80208) holds heap pointers; the triplets at
            // +8 and +32 look like StdVectors (begin/end/capacity). The actual per-content records —
            // possibly including the weekly loot state the Raid Finder list is built from — live
            // BEHIND those pointers. Scan the buffers for the current-tier ids to locate them.
            var basePtr = (byte*)ui;
            var inner = new StringBuilder();
            ScanHeapVector(inner, "vec@+8", basePtr, 80208 + 8);
            ScanHeapVector(inner, "vec@+32", basePtr, 80208 + 32);
            return inner.Length == 0 ? "(no valid vectors)" : inner.ToString();
        });

        Section(sb, "icDiff", () =>
        {
            var ui = UIState.Instance();
            if (ui == null)
            {
                return "UIState null";
            }

            // Self-diffing snapshots: remember the buffers from the previous probe run and print only
            // the bytes that changed since. Run once as baseline, change game state (open raid finder
            // / loot a floor), run again — the diff pinpoints where the weekly state lands.
            var basePtr = (byte*)ui;
            var inner = new StringBuilder();

            // vec@+8: one byte per entry (values 0/2/3…) — a per-content state table candidate.
            var b8 = *(ulong*)(basePtr + 80208 + 8);
            var e8 = *(ulong*)(basePtr + 80208 + 16);
            if (b8 != 0 && e8 > b8 && e8 - b8 < 1024 * 1024)
            {
                DiffAndStore(inner, "vec8", (byte*)b8, (int)(e8 - b8));
            }

            // vec@+32: the instance-content registry — snapshot 48 bytes around each savage floor
            // (and Unreal 64013) so a loot-state byte inside the record shows up as a diff.
            var b32 = *(ulong*)(basePtr + 80208 + 32);
            var e32 = *(ulong*)(basePtr + 80208 + 40);
            if (b32 != 0 && e32 > b32 && e32 - b32 < 4 * 1024 * 1024)
            {
                var p = (byte*)b32;
                var size = (int)(e32 - b32);
                uint[] ids = [30155, 30157, 30159, 30161, 64013];
                foreach (var id in ids)
                {
                    for (var i = 0; i + 4 <= size; i++)
                    {
                        if (*(uint*)(p + i) != id)
                        {
                            continue;
                        }

                        var from = Math.Max(0, i - 8);
                        var len = Math.Min(48, size - from);
                        DiffAndStore(inner, $"ic{id}", p + from, len);
                        break;
                    }
                }
            }

            return inner.Length == 0 ? "(nothing readable)" : inner.ToString();
        });

        Section(sb, "weeklyLockout", () =>
        {
            var ps = PlayerState.Instance();
            if (ps == null)
            {
                return "PlayerState null";
            }

            // PlayerState.WeeklyLockoutInfo is internal; read it + its neighbourhood by raw offset
            // (offset 1536, taken from the ClientStructs field layout). Diagnostic only.
            var basePtr = (byte*)ps;
            const int lockoutOffset = 1536;
            var lockout = basePtr[lockoutOffset];
            var bits = Convert.ToString(lockout, 2).PadLeft(8, '0');

            var region = new StringBuilder();
            for (var o = lockoutOffset - 8; o < lockoutOffset + 24; o++)
            {
                if (o == lockoutOffset)
                {
                    region.Append('[');
                }

                region.Append(basePtr[o].ToString("X2"));
                region.Append(o == lockoutOffset ? "] " : " ");
            }

            return $"byte@1536={lockout} (bits={bits}) region[1528..1559]={region}";
        });

        Section(sb, "addon", () =>
        {
            var addr = _gameGui.GetAddonByName("ContentsFinder").Address;
            if (addr == nint.Zero)
            {
                return "ContentsFinder addon NOT loaded";
            }

            var addon = (AtkUnitBase*)addr;
            return $"ContentsFinder loaded IsVisible={addon->IsVisible}";
        });

        Section(sb, "dutyfinder", () =>
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                return "AgentContentsFinder null";
            }

            var inner = new StringBuilder();
            inner.Append($"agentActive={agent->IsAgentActive()} addonId={agent->AddonId} ");
            inner.Append($"addonShown={agent->IsAddonShown()} tab={agent->SelectedTab} ");
            inner.Append($"numCollectedRewards={agent->NumCollectedRewards} ");
            inner.Append($"selDutyId={agent->InterfaceSub.SelectedDutyId} ");
            inner.Append($"recvReward={agent->InterfaceSub.GetReceivedRewardCount()} ");
            inner.Append($"maxReward={agent->InterfaceSub.GetMaxReceivedRewardCount()} ");
            var selForKind = agent->InterfaceSub.SelectedDutyId;
            inner.Append($"kind={(selForKind > 0 ? ClassifyRaidDuty((uint)selForKind) : RaidDutyKind.Other)}");

            var rewardVals = agent->InterfaceSub.UnkMaxReceivedRewardValues;
            inner.Append(" unkRewardVals=[");
            for (var r = 0; r < rewardVals.Length; r++)
            {
                if (r > 0)
                {
                    inner.Append(',');
                }

                inner.Append(rewardVals[r]);
            }

            inner.Append(']');

            var list = agent->ContentList;
            var count = (int)list.Count;
            inner.Append($" listCount={count}");
            for (var i = 0; i < count && i < 12; i++)
            {
                var c = list[i].Value;
                if (c == null)
                {
                    continue;
                }

                inner.Append($"\n    #{i} type={c->Id.ContentType} id={c->Id.Id} name={c->Name}");
            }

            return inner.ToString();
        });

        Section(sb, "raidfinder", () =>
        {
            var agent = AgentRaidFinder.Instance();
            if (agent == null)
            {
                return "AgentRaidFinder null";
            }

            var inner = new StringBuilder();
            inner.Append($"agentActive={agent->IsAgentActive()} addonShown={agent->IsAddonShown()} ");
            inner.Append($"addonId={agent->AddonId} selTab={agent->SelectedTab} selEntry={agent->SelectedEntry} ");
            inner.Append($"selDutyId={agent->InterfaceSub.SelectedDutyId} ");
            inner.Append($"recvReward={agent->InterfaceSub.GetReceivedRewardCount()} ");
            inner.Append($"maxReward={agent->InterfaceSub.GetMaxReceivedRewardCount()}");

            var vals = agent->InterfaceSub.UnkMaxReceivedRewardValues;
            inner.Append(" unkRewardVals=[");
            for (var r = 0; r < vals.Length; r++)
            {
                if (r > 0)
                {
                    inner.Append(',');
                }

                inner.Append(vals[r]);
            }

            inner.Append(']');

            // Read the tab entries even when the agent is inactive (testing whether the last-loaded
            // data survives closing the window), but validate instead of trusting blindly: a sane
            // tab has 1..8 entries; never-loaded memory shows garbage counts in the millions.
            if (!agent->IsAgentActive())
            {
                inner.Append(" (agent inactive — reading possibly stale tabs)");
            }

            var tabs = agent->Tabs;
            for (var t = 0; t < tabs.Length; t++)
            {
                var tab = tabs[t];
                var entryCount = tab.EntryCount;
                if (entryCount is <= 0 or > 8)
                {
                    inner.Append($"\n    tab{t}: entryCount={entryCount} (invalid — skipped)");
                    continue;
                }

                inner.Append($"\n    tab{t} '{tab.Label}' entries={entryCount}:");
                var entries = tab.Entries;
                for (var e = 0; e < entryCount && e < entries.Length; e++)
                {
                    var en = entries[e];
                    var completed = UIState.IsInstanceContentCompleted(en.InstanceContentId);

                    // Dump the full raw struct (incl. the internal UnkC/UnkFlags1/UnkFlags2) from a
                    // stack copy — R1 (looted this week) should differ from R2-R4 in some byte.
                    var raw = (byte*)Unsafe.AsPointer(ref en);
                    var unkC = *(int*)(raw + 12);
                    var f1 = raw[16];
                    var f2 = raw[17];
                    var hex = new StringBuilder();
                    for (var b = 0; b < 20; b++)
                    {
                        hex.Append(raw[b].ToString("X2"));
                    }

                    inner.Append($"\n      icId={en.InstanceContentId} cfcId={en.ContentFinderConditionId} " +
                                 $"sort={en.SortKey} completed={completed} LOOT={(f1 & 4) != 0} " +
                                 $"unkC={unkC} f1={f1} f2={f2} raw={hex}");
                }
            }

            return inner.ToString();
        });

        return sb.ToString();
    }

    /// <summary>
    /// Temporary experiment: triggers the Raid Finder agent's own open path (<c>Show()</c>) so it
    /// requests fresh weekly data from the server — exactly what happens when the user opens the
    /// window — and optionally hides it again in the same frame so it never renders. If the data
    /// still arrives (readable via the tab cache a few seconds later), a true background refresh is
    /// possible without the user ever opening the window. Uses only the agent's public functions.
    /// </summary>
    /// <param name="keepVisible">Keep the window visible (control test) instead of hiding instantly.</param>
    /// <returns>A status line for chat/log.</returns>
    public unsafe string TriggerRaidFinderLoad(bool keepVisible)
    {
        try
        {
            if (!IsAvailable)
            {
                return "weekopen: not logged in.";
            }

            var agent = AgentRaidFinder.Instance();
            if (agent == null)
            {
                return "weekopen: AgentRaidFinder null.";
            }

            if (agent->IsAgentActive())
            {
                return "weekopen: already open — nothing to trigger.";
            }

            agent->Show();
            if (!keepVisible)
            {
                agent->Hide();
                return "weekopen: show+hide sent — wait ~3s, then run /bisexport weekdump.";
            }

            return "weekopen: shown visibly (control test) — close it manually.";
        }
        catch (Exception ex)
        {
            _log.Error($"weekopen failed: {ex.GetType().Name}.");
            return "weekopen: failed (see log).";
        }
    }

    private long _hiddenRefreshStartTicks; // 0 = idle (Environment.TickCount64 when started)
    private bool _hiddenRefreshSuppressed;

    // The most recent server-fresh Savage read (from an open window or a hidden refresh). Only
    // trusted briefly — see ReadSavageFloors.
    private (bool? F1, bool? F2, bool? F3, bool? F4) _freshFloors;
    private long _freshFloorsTicks;

    /// <summary>Raised (on the framework thread) when a hidden refresh delivered fresh Savage data.</summary>
    public event Action? HiddenRefreshCompleted;

    /// <summary>
    /// Temporary experiment, stage 2: opens the Raid Finder agent but <b>suppresses the window</b>
    /// (clears the addon's visible flag — the same mechanism the game itself uses to hide windows),
    /// waits until the weekly data arrives, logs it and closes the agent again. An immediate
    /// Show()+Hide() proved to cancel the load before the data request fires, so the agent must stay
    /// alive for a few frames. Driven per-tick via <see cref="PumpHiddenRefresh"/>.
    /// </summary>
    /// <returns>A status line for chat.</returns>
    public unsafe string BeginHiddenRefresh()
    {
        try
        {
            if (!IsAvailable)
            {
                return "weekopen2: not logged in.";
            }

            var agent = AgentRaidFinder.Instance();
            if (agent == null)
            {
                return "weekopen2: AgentRaidFinder null.";
            }

            if (agent->IsAgentActive())
            {
                return "weekopen2: Raid Finder already open — nothing to do.";
            }

            agent->Show();
            _hiddenRefreshStartTicks = Environment.TickCount64;
            _hiddenRefreshSuppressed = false;
            return "weekopen2: started — suppressing the window and waiting for data (watch the log).";
        }
        catch (Exception ex)
        {
            _log.Error($"weekopen2 failed: {ex.GetType().Name}.");
            return "weekopen2: failed (see log).";
        }
    }

    /// <summary>
    /// Framework-tick pump for <see cref="BeginHiddenRefresh"/>: suppresses the window as soon as the
    /// addon exists, then waits for the tab data (up to 4s), logs the read floors and closes the
    /// agent. No-op while idle. Never throws (P2).
    /// </summary>
    public unsafe void PumpHiddenRefresh()
    {
        if (_hiddenRefreshStartTicks == 0)
        {
            return;
        }

        try
        {
            var agent = AgentRaidFinder.Instance();
            if (agent == null)
            {
                _hiddenRefreshStartTicks = 0;
                return;
            }

            // Make the window invisible the moment it exists (it may render for a frame or two).
            if (!_hiddenRefreshSuppressed)
            {
                var addr = _gameGui.GetAddonByName("RaidFinder").Address;
                if (addr != nint.Zero)
                {
                    ((AtkUnitBase*)addr)->IsVisible = false;
                    _hiddenRefreshSuppressed = true;
                    _log.Info("weekopen2: RaidFinder window suppressed.");
                }
            }

            var elapsed = Environment.TickCount64 - _hiddenRefreshStartTicks;
            if (IsSavageReadable)
            {
                var (f1, f2, f3, f4) = ReadSavageFloors(); // live read — also fills the fresh cache
                _log.Info($"Weekly refresh: Savage data arrived after {elapsed}ms (f1={f1} f2={f2} f3={f3} f4={f4}).");
                agent->Hide();
                _hiddenRefreshStartTicks = 0;

                try
                {
                    HiddenRefreshCompleted?.Invoke();
                }
                catch (Exception ex)
                {
                    _log.Error($"HiddenRefreshCompleted handler threw: {ex.GetType().Name}.");
                }

                return;
            }

            if (elapsed > 4_000)
            {
                _log.Info("Weekly refresh: timeout (4s) — no data arrived. Closing agent.");
                agent->Hide();
                _hiddenRefreshStartTicks = 0;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"weekopen2 pump failed: {ex.GetType().Name}.");
            _hiddenRefreshStartTicks = 0;
        }
    }

    // --- Hidden ContentsFinder refresh (normal / alliance) ----------------------------------------
    private long _cfRefreshStartTicks; // 0 = idle
    private long _cfStepTicks;
    private int _cfStep; // 0 = open normal, 1 = read normal → open alliance, 2 = read alliance → close
    private uint _cfNormalCfc, _cfNormalIc, _cfAllianceCfc, _cfAllianceIc;
    private bool _raidTargetsBuilt;

    // Fresh normal/alliance from a hidden refresh (or a live open), trusted ≤ 2 min — see ReadNormalAlliance.
    private bool? _freshNormal, _freshAlliance;
    private long _freshNormalAllianceTicks;

    /// <summary>Raised (framework thread) when a hidden ContentsFinder refresh gathered fresh normal/alliance data.</summary>
    public event Action? ContentsRefreshCompleted;

    /// <summary>Whether any hidden refresh (Savage or normal/alliance) is currently in progress.</summary>
    public bool IsHiddenBusy => _hiddenRefreshStartTicks != 0 || _cfRefreshStartTicks != 0;

    /// <summary>
    /// Resolves (once, cached) the current tier's normal and alliance raid — the highest CFC row of
    /// each kind, i.e. the newest content — so the hidden refresh knows which two duties to load.
    /// </summary>
    private void EnsureRaidTargets()
    {
        if (_raidTargetsBuilt)
        {
            return;
        }

        _raidTargetsBuilt = true;
        try
        {
            var sheet = _data.GetExcelSheet<LuminaCfc>();
            if (sheet is null)
            {
                return;
            }

            foreach (var row in sheet)
            {
                if (row.Content.RowId == 0 || row.ContentType.RowId != RaidContentType)
                {
                    continue;
                }

                var member = row.ContentMemberType.ValueNullable;
                var isAlliance = member is { } m && (m.AlliancePartyCount >= 2 || m.PartyCount >= 3);
                if (isAlliance)
                {
                    if (row.RowId > _cfAllianceCfc)
                    {
                        (_cfAllianceCfc, _cfAllianceIc) = (row.RowId, row.Content.RowId);
                    }
                }
                else if (!row.HighEndDuty && row.RowId > _cfNormalCfc)
                {
                    (_cfNormalCfc, _cfNormalIc) = (row.RowId, row.Content.RowId);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Weekly raid-target resolve failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Starts a hidden ContentsFinder refresh: opens the Duty Finder to the current normal and alliance
    /// raids one after another (suppressing the window), reads each weekly reward, and closes it —
    /// exposing <c>normal</c>/<c>alliance</c> without the user ever opening the finder. No-op if the
    /// finder is already open (never hijacks the user), unavailable, or has no targets. Driven per-tick
    /// by <see cref="PumpContentsFinderRefresh"/>.
    /// </summary>
    public unsafe void BeginContentsFinderRefresh()
    {
        try
        {
            if (!IsAvailable || _cfRefreshStartTicks != 0)
            {
                return;
            }

            var agent = AgentContentsFinder.Instance();
            if (agent == null || agent->IsAgentActive())
            {
                return; // finder already open — the live read covers it; don't take it over
            }

            EnsureRaidTargets();
            if (_cfNormalCfc == 0 && _cfAllianceCfc == 0)
            {
                return;
            }

            _freshNormal = null;
            _freshAlliance = null;
            _cfStep = 0;
            _cfRefreshStartTicks = Environment.TickCount64;
            _cfStepTicks = _cfRefreshStartTicks;
        }
        catch (Exception ex)
        {
            _log.Error($"Contents refresh begin failed: {ex.GetType().Name}.");
            _cfRefreshStartTicks = 0;
        }
    }

    /// <summary>
    /// Framework-tick pump for <see cref="BeginContentsFinderRefresh"/>: keeps the window suppressed,
    /// loads normal then alliance via <c>OpenRegularDuty</c>, reads each once it has settled, then
    /// closes the finder and raises <see cref="ContentsRefreshCompleted"/>. No-op while idle; never
    /// throws (P2).
    /// </summary>
    public unsafe void PumpContentsFinderRefresh()
    {
        if (_cfRefreshStartTicks == 0)
        {
            return;
        }

        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                _cfRefreshStartTicks = 0;
                return;
            }

            // Keep the window invisible whenever it exists (it may try to render for a frame or two).
            var addr = _gameGui.GetAddonByName("ContentsFinder").Address;
            if (addr != nint.Zero)
            {
                ((AtkUnitBase*)addr)->IsVisible = false;
            }

            var sub = &agent->InterfaceSub;
            var now = Environment.TickCount64;
            var stepElapsed = now - _cfStepTicks;

            switch (_cfStep)
            {
                case 0: // kick off: load the normal raid (or skip straight to alliance)
                    if (_cfNormalCfc != 0)
                    {
                        agent->OpenRegularDuty(_cfNormalCfc, false);
                        Advance(1, now);
                    }
                    else
                    {
                        agent->OpenRegularDuty(_cfAllianceCfc, false);
                        Advance(2, now);
                    }

                    break;

                case 1: // normal loading → read it, then load the alliance raid
                    if ((sub->SelectedDutyId == (int)_cfNormalIc && stepElapsed >= 150) || stepElapsed > 1500)
                    {
                        _freshNormal = RewardDone(sub, _cfNormalIc);
                        if (_cfAllianceCfc != 0)
                        {
                            agent->OpenRegularDuty(_cfAllianceCfc, false);
                            Advance(2, now);
                        }
                        else
                        {
                            FinishContentsRefresh(agent);
                        }
                    }

                    break;

                case 2: // alliance loading → read it, then close
                    if ((sub->SelectedDutyId == (int)_cfAllianceIc && stepElapsed >= 150) || stepElapsed > 1500)
                    {
                        _freshAlliance = RewardDone(sub, _cfAllianceIc);
                        FinishContentsRefresh(agent);
                    }

                    break;
            }

            if (_cfRefreshStartTicks != 0 && now - _cfRefreshStartTicks > 6_000)
            {
                _log.Info("Weekly refresh: ContentsFinder timeout — closing.");
                FinishContentsRefresh(agent);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Contents refresh pump failed: {ex.GetType().Name}.");
            _cfRefreshStartTicks = 0;
        }
    }

    private void Advance(int step, long now)
    {
        _cfStep = step;
        _cfStepTicks = now;
    }

    /// <summary>Reads whether the just-loaded duty's weekly reward is received (only <c>true</c>/<c>null</c>).</summary>
    private static unsafe bool? RewardDone(AgentContentsFinderInterface* sub, uint expectedIc)
    {
        if (sub->SelectedDutyId != (int)expectedIc)
        {
            return null; // never settled — don't trust a mismatched selection
        }

        var max = sub->GetMaxReceivedRewardCount();
        return max > 0 && sub->GetReceivedRewardCount() >= max ? true : null;
    }

    private unsafe void FinishContentsRefresh(AgentContentsFinder* agent)
    {
        _freshNormalAllianceTicks = Environment.TickCount64;
        agent->Hide();
        _cfRefreshStartTicks = 0;
        _log.Info($"Weekly refresh: ContentsFinder read (normal={_freshNormal} alliance={_freshAlliance}).");
        try
        {
            ContentsRefreshCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error($"ContentsRefreshCompleted handler threw: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Diagnostic experiment for the normal/alliance background read: without opening the Duty Finder
    /// window, ask its interface to <b>load</b> a specific duty (<c>LoadInstanceContent</c>) and read
    /// back the weekly received/max reward count. If this returns the right counts with the window
    /// closed, a fully-automatic background read (analogous to the hidden Savage refresh, but without
    /// even a window) is possible. Probes the known current-tier ids plus every classified
    /// normal/alliance raid. Read-only w.r.t. game data; only mutates the finder's selected-duty
    /// preview state. Never throws (P2).
    /// </summary>
    /// <returns>A multi-line status dump for chat/log.</returns>
    public unsafe string ProbeContentsFinderLoad()
    {
        try
        {
            if (!IsAvailable)
            {
                return "dutyprobe: not logged in.";
            }

            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                return "dutyprobe: AgentContentsFinder null.";
            }

            var sub = &agent->InterfaceSub;
            var sb = new StringBuilder();
            sb.Append($"dutyprobe: agentActive={agent->IsAgentActive()} addonShown={agent->IsAddonShown()} " +
                      $"(load a duty into the closed finder, then read its weekly reward)");

            var sheet = _data.GetExcelSheet<LuminaCfc>();
            if (sheet is null)
            {
                return "dutyprobe: no ContentFinderCondition sheet.";
            }

            // Collect normal + alliance raids, newest first (highest CFC RowId = current tier on top).
            var raids = new List<(uint Cfc, uint Ic, RaidDutyKind Kind, string Name)>();
            foreach (var row in sheet)
            {
                if (row.Content.RowId == 0 || row.ContentType.RowId != RaidContentType)
                {
                    continue;
                }

                var member = row.ContentMemberType.ValueNullable;
                var isAlliance = member is { } m && (m.AlliancePartyCount >= 2 || m.PartyCount >= 3);
                if (isAlliance)
                {
                    raids.Add((row.RowId, row.Content.RowId, RaidDutyKind.Alliance, row.Name.ExtractText()));
                }
                else if (!row.HighEndDuty)
                {
                    raids.Add((row.RowId, row.Content.RowId, RaidDutyKind.Normal, row.Name.ExtractText()));
                }
            }

            var ordered = raids.OrderByDescending(r => r.Cfc).ToList();
            var newestNormal = ordered.FirstOrDefault(r => r.Kind == RaidDutyKind.Normal);
            var newestAlliance = ordered.FirstOrDefault(r => r.Kind == RaidDutyKind.Alliance);

            foreach (var r in new[] { newestNormal, newestAlliance })
            {
                if (r.Cfc == 0)
                {
                    continue;
                }

                // OpenRegularDuty selects the duty (and loads its detail/reward). We read the reward
                // synchronously right after; a follow-up weekdump ~1s later shows the settled value if
                // it turns out to load asynchronously.
                agent->OpenRegularDuty(r.Cfc, false);
                sb.Append($"\n  OpenRegularDuty(cfc {r.Cfc} ic {r.Ic} {r.Kind} '{r.Name}') → " +
                          $"sel={sub->SelectedDutyId} recv={sub->GetReceivedRewardCount()} max={sub->GetMaxReceivedRewardCount()}");
            }

            sb.Append("\n  (if the window popped open, note it; run /bisexport weekdump ~1s later to read the settled reward + kind)");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _log.Error($"dutyprobe failed: {ex.GetType().Name}.");
            return "dutyprobe: failed (see log).";
        }
    }

    /// <summary>Buffer snapshots from the previous probe run, keyed by region (diagnostic only).</summary>
    private static readonly Dictionary<string, byte[]> Snapshots = new(StringComparer.Ordinal);

    /// <summary>
    /// Copies <paramref name="size"/> bytes and prints only what changed since the previous snapshot
    /// under the same <paramref name="key"/> (capped at 24 diffs), then stores the new snapshot.
    /// </summary>
    private static unsafe void DiffAndStore(StringBuilder sb, string key, byte* p, int size)
    {
        var current = new byte[size];
        for (var i = 0; i < size; i++)
        {
            current[i] = p[i];
        }

        if (Snapshots.TryGetValue(key, out var prev) && prev.Length == size)
        {
            var diffs = 0;
            for (var i = 0; i < size && diffs < 24; i++)
            {
                if (prev[i] != current[i])
                {
                    if (diffs == 0)
                    {
                        sb.Append($"\n      {key} DIFFS:");
                    }

                    sb.Append($" @{i}:{prev[i]:X2}->{current[i]:X2}");
                    diffs++;
                }
            }

            if (diffs == 0)
            {
                sb.Append($"\n      {key}: unchanged ({size}B)");
            }
        }
        else
        {
            sb.Append($"\n      {key}: baseline stored ({size}B)");
        }

        Snapshots[key] = current;
    }

    /// <summary>
    /// Treats the three pointers at <paramref name="tripletOffset"/> as a StdVector (begin/end/
    /// capacity), validates them hard (non-null, ordered, &lt; 4 MB) and scans the buffer for the
    /// current Savage tier's ids (30155..30161, cfc 1069..1075). Each hit is dumped with 48
    /// surrounding bytes so record layout and a weekly-loot bit can be identified by diffing.
    /// </summary>
    private static unsafe void ScanHeapVector(StringBuilder sb, string label, byte* uiBase, int tripletOffset)
    {
        var begin = *(ulong*)(uiBase + tripletOffset);
        var end = *(ulong*)(uiBase + tripletOffset + 8);
        var cap = *(ulong*)(uiBase + tripletOffset + 16);
        if (begin == 0 || end <= begin || cap < end || cap - begin > 4 * 1024 * 1024)
        {
            sb.Append($"\n    {label}: invalid (begin=0x{begin:X} end=0x{end:X} cap=0x{cap:X})");
            return;
        }

        var size = (int)(end - begin);
        sb.Append($"\n    {label}: size={size}");
        var p = (byte*)begin;

        // First 48 bytes as a structural sample.
        sb.Append("\n      head: ");
        for (var i = 0; i < Math.Min(48, size); i++)
        {
            sb.Append(p[i].ToString("X2")).Append(' ');
        }

        uint[] targets = [30155, 30157, 30159, 30161, 1069, 1071, 1073, 1075];
        var hits = 0;
        for (var i = 0; i + 4 <= size && hits < 12; i++)
        {
            var v = *(uint*)(p + i);
            var isTarget = false;
            foreach (var t in targets)
            {
                if (v == t)
                {
                    isTarget = true;
                    break;
                }
            }

            if (!isTarget)
            {
                continue;
            }

            hits++;
            var from = Math.Max(0, i - 8);
            var len = Math.Min(48, size - from);
            sb.Append($"\n      hit {v} @+{i}: ");
            for (var b = 0; b < len; b++)
            {
                sb.Append(p[from + b].ToString("X2")).Append(' ');
            }
        }

        if (hits == 0)
        {
            sb.Append("\n      (no id hits)");
        }
    }

    /// <summary>Appends a labelled hexdump of <paramref name="length"/> bytes at <paramref name="offset"/> (16 per line).</summary>
    private static unsafe void AppendRegion(StringBuilder sb, string label, byte* basePtr, int offset, int length)
    {
        sb.Append('\n').Append("    ").Append(label).Append(':');
        for (var i = 0; i < length; i++)
        {
            if (i % 16 == 0)
            {
                sb.Append($"\n      +{i,3}: ");
            }

            sb.Append(basePtr[offset + i].ToString("X2")).Append(' ');
        }
    }

    private void Section(StringBuilder sb, string label, Func<string> read)
    {
        try
        {
            sb.Append("  ").Append(label).Append(": ").AppendLine(read());
        }
        catch (Exception ex)
        {
            sb.Append("  ").Append(label).Append(": FAILED ").AppendLine(ex.GetType().Name);
        }
    }
}
