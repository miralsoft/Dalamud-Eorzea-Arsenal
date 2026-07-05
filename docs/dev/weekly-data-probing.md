# Weekly game-data probing & re-verification

How the plugin reads the FFXIV weekly-checklist values from game memory, **how those locations were
reverse-engineered**, and — most importantly — the **routine to re-verify them after a game/Dalamud
patch** or when the weekly sync stops working. Keep this in sync with `GameWeeklySource`.

The diagnostic tooling described here is intentionally **kept in the codebase** (behind hidden
`/bisexport …` commands, no UI). It is not wired into the sync path; it exists so we never have to
re-derive this from scratch.

---

## 1. What we read, and from where

| Field | Source (ClientStructs) | Notes |
|---|---|---|
| `tomesHave` | `InventoryManager.GetWeeklyAcquiredTomestoneCount()` (+ `GetLimitedTomestoneWeeklyLimit()`) | Solid, public API. |
| `custom` | `SatisfactionSupplyManager.GetUsedAllowances()` / `GetRemainingAllowances()` | `custom=true` only when `used+remaining==12 && remaining==0`. Only ever sends `true`. |
| `f1`–`f4` (Savage) | `AgentRaidFinder.Instance()->Tabs[0]` (the "Raids" tab) → `TabEntryData[i]` → **`UnkFlags1` (struct offset 16) bit 2 (`& 0x04`)** | `bit 2 = weekly reward obtained this week`. **Internal/undocumented flag** — the highest patch-risk item. |
| `unreal` | `PlayerState.FauxHollowsTimestamp` ≥ most-recent weekly reset (`WeeklyDecode.IsUnrealDone`) | Background-readable. **Not** the Raid-Finder LOOT bit — the Unreal trial entry (`tab1` `icId 64013`) stays `LOOT=False` even when done (its reward is the Faux Hollows board, not the trial drop). |
| `wondrous` | `PlayerState.WeeklyBingoNumPlacedStickers == 9` **and** `GetWeeklyBingoExpireUnixTimestamp()` > next weekly reset (`WeeklyDecode.IsWondrousDone`) | Background-readable, stateless. See §1.1. |
| `normal` / `alliance` | `AgentContentsFinder.InterfaceSub` `GetReceivedRewardCount() >= GetMaxReceivedRewardCount()` for the **selected** duty; classified normal (8-player) vs alliance (24-player) via `ContentFinderCondition` | Duty Finder only exposes the selected duty, so read opportunistically while it is open. Gated on `normal_lockout` / `alliance_lockout`. |

### 1.1 The `wondrous` two-week-book subtlety (validated 2026-07-05)
A Wondrous Tails journal is valid for **two weeks** (`expiry = the reset of its purchase week + 14
days`), and the sticker count **stays stale at 9 after a hand-in** until a new book is picked up. So
`stickers == 9` alone is ambiguous: read at the start of the following week it would falsely report
"done". The fix is stateless and needs no client persistence — anchor on the book's own expiry:

- A book bought **this** week expires `> next reset` (this week's reset + 14 d = next reset + 7 d).
- A carried-over book bought **last** week expires **at** the next reset — excluded by `> next reset`.

Hence `wondrous = (stickers == 9) && (expireTs > nextWeeklyReset)`. Confirmed against a live capture:
completed book, `bingoExpireTs = 1784016000` (Tue 2026-07-14 08:00), next reset 2026-07-07 → done;
the same stale value read the following week no longer satisfies `> next reset`. `bingoExpired` /
`HasWeeklyBingoJournal` are **not** used (both mislead: expiry stays in the future after a hand-in,
and the journal flag is false both before buying and after handing in).

### `unreal` — the Faux Hollows timestamp (validated 2026-07-05)
`PlayerState.FauxHollowsTimestamp` jumps to a within-current-week value when the Unreal is engaged and
stays put through the whole run, so `fauxTs >= mostRecentReset` means "done this week". The Raid-Finder
LOOT bit is a red herring here — `tab1 icId 64013` reads `LOOT=False` even when fully done.

### `normal` / `alliance` — the Duty Finder reward count (validated 2026-07-05)
`AgentContentsFinder.InterfaceSub.GetReceivedRewardCount()` for the **selected** duty went `0 → 1`
(normal R4, max 1) and `0 → 3` (alliance Windurst, max 3) on clearing — one clear fills the duty to
its max, so `received >= max` is the uniform "done" test. The finder only exposes the *selected* duty
(no per-list reward). Classification is by party size from `ContentFinderCondition` (`ContentType ==
Raids`, `ContentMemberType.AlliancePartyCount` / `PartyCount` ⇒ 24-player alliance vs 8-player normal;
`HighEndDuty` excludes Savage; the current tier's = the highest CFC RowId of each kind).

**Hidden Duty-Finder refresh** (`BeginContentsFinderRefresh` / `PumpContentsFinderRefresh`): since the
reward is per-selected-duty, the plugin drives the finder itself — `AgentContentsFinder.OpenRegularDuty(cfcId,
hideIfShown: false)` selects a duty (loading its reward **synchronously** — no queueing) — for the
current normal raid, then the current alliance raid, with the `ContentsFinder` addon's `IsVisible`
suppressed each tick, then `Hide()`s the agent. The two reads land in a 2-minute fresh cache and raise
`ContentsRefreshCompleted` → sync. Runs at login (+12 s, staggered after the Savage refresh), hourly,
on the manual sync, and it also live-reads the selected duty while you have the finder open. The
`IsHiddenBusy` guard keeps the Savage and Duty-Finder refreshes from driving a window at once. (Note:
`InterfaceSub.LoadInstanceContent`/`LoadContentFinderCondition` take a **byte list index**, not an id —
`OpenRegularDuty(uint)` is the id-addressable entry point.) Minor side effect: the finder's remembered
selection becomes the last-loaded duty.

### The Savage "background" trick (the important one)
The Savage per-floor loot state is **not** stored persistently on the client. The Raid-Finder agent
fetches it from the server when the window opens, and the tab memory is stale garbage when the agent
has never been active this session. To read it without the user opening the window
(`GameWeeklySource.BeginHiddenRefresh` / `PumpHiddenRefresh`):

1. `AgentRaidFinder.Instance()->Show()` — starts the agent's normal open/data-request path.
2. On the **next** framework ticks, set the `RaidFinder` addon's `IsVisible = false` (one UI-flag
   write — the same mechanism the game uses to hide windows). The window never visibly renders.
3. Wait for `IsSavageReadable` (agent active + tab0 has 4 entries). In testing the data arrived in
   **~15 ms**.
4. Read the floors into a **2-minute fresh cache**, `agent->Hide()`, raise `HiddenRefreshCompleted`.

Gotchas learned the hard way:
- `Show()` **immediately** followed by `Hide()` in the same frame **cancels the load** — the agent
  must stay alive a few frames. Hence the tick-driven pump.
- After a relog with the window never opened, the tab memory is garbage (huge `EntryCount`). Guard
  with `IsAgentActive()` **and** `EntryCount == 4` **and** `entries[0].InstanceContentId != 0`.
- The cached tab data goes **stale** after you loot a floor until the window is reopened/refreshed —
  so only ever trust a *fresh* read (live-while-open, or ≤ 2 min after a hidden refresh). Never send
  the snapshot-stale values.
- `UnkFlags1` also carries transient bits (locked/UI state, e.g. bit 5) that flicker; **only bit 2**
  is the weekly-loot flag.

Production triggers: login (+10 s), hourly, and the edge-trigger when the user opens the Raid Finder.
All hidden refreshes are gated by `CanHiddenRefresh()` (never in combat / duty / between areas /
cutscene) and by the server GET's `savage_lockout`.

---

## 2. The diagnostic commands (kept for re-testing)

All are subcommands of `/bisexport`, log to the diagnostics window (`/bisexport log`), and touch no
sync state:

- **`/bisexport weekdump`** — one big read-only dump: tomes, custom, unreal/wondrous fields (incl.
  `bingoExpireTs`/`bingoExpired`), all visible addon names, the Duty Finder's selected-duty reward
  counts and classified `kind=`, the Raid-Finder tabs with per-entry `LOOT=<bit2>`/`f1`/raw bytes,
  plus experimental memory regions (`icHeap`, `icDiff` self-diff, `weeklyLockout`, `uiStateRegions`).
- **`/bisexport weekopen`** — `Show()` + instant `Hide()` (proved insufficient — kept as a negative
  control).
- **`/bisexport weekshow`** — `Show()` visibly (manual control test).
- **`/bisexport weekopen2`** — the **working** hidden Savage refresh: show, suppress the window, wait,
  log `Weekly refresh: Savage data arrived after Xms (f1=… …)`.
- **`/bisexport dutyrefresh`** — the **working** hidden normal/alliance refresh: loads the current
  normal + alliance raids into the suppressed Duty Finder, logs
  `Weekly refresh: ContentsFinder read (normal=… alliance=…)`, closes it.
- **`/bisexport dutyprobe`** — raw control test: `OpenRegularDuty` the current normal/alliance raids
  and dump their reward (leaves the window open).

`icDiff` remembers the previous run's buffers and prints only changed bytes — the fastest way to find
"which byte moved when I looted a floor" without eyeballing kilobytes of hex.

---

## 3. The reverse-engineering method (reusable recipe)

This is the general approach that cracked Savage; reuse it for `unreal`, `wondrous`, or anything new.

1. **Reflect ClientStructs metadata offline.** A tiny .NET tool in the scratchpad reads
   `%AppData%\XIVLauncher\addon\Hooks\dev\FFXIVClientStructs.dll` with `System.Reflection.Metadata`
   (`PEReader`/`MetadataReader`) — no game needed. Use it to:
   - list a type's members and **field offsets** (`FieldDefinition.GetOffset()`, struct size via
     `GetLayout()`),
   - search all method/field names by regex (e.g. `Reward|Weekly|Lockout|Received`),
   - decode method/field signatures with a custom `ISignatureTypeProvider`.

   (Windows PowerShell 5.1 can't reflect the .NET-9 DLL directly — use the metadata reader from a
   net10 console app.)
2. **Probe in-game.** Add a read-only dump behind a `/bisexport` command (framework thread, null +
   bounds guards, everything in `try/catch`). Print the candidate values.
3. **Diff across a known state change.** Snapshot → change one thing in-game (loot a floor, do the
   content, cross the weekly reset) → snapshot again → compare. A field that flips exactly with the
   change is your value. The self-diffing `icDiff` section automates this.
4. **Confirm with ≥ 2 states before trusting.** For Savage, bit 2 was confirmed across R1 → R1+R2 →
   R1+R2+R3 (the bit flipped on exactly the looted floor each time, and stayed clear on the rest).
5. **Rule out impostors.** Check that permanent state (`IsInstanceContentCompleted` = *ever* cleared)
   and unrelated bytes (`WeeklyLockoutInfo`@1536 = squadron/GC, not raids) don't masquerade as the
   answer.

Sources that turned out to be **dead ends** for Savage (don't re-chase them): `GetContentValue`,
`ContentKeyValueData`, `PlayerState.WeeklyLockoutInfo`, the `UIState.InstanceContent` /
`ContentsFinder` regions and the heap vectors behind them (static registry only), the agent's
`GetReceivedRewardCount` / `UnkMaxReceivedRewardValues` (roulette bonuses, not raid lockout).

---

## 4. Re-verification routine after a patch / on dysfunction

Run this when a game or Dalamud/ClientStructs update lands, or if Savage stops syncing.

**A. Cheap sanity check (2 min).**
1. `/bisexport weekopen2`, then check the log for
   `Weekly refresh: Savage data arrived after Xms (…)`.
   - No line / timeout → the hidden-refresh path broke (see B/C).
2. `/bisexport weekdump`, look at `raidfinder: tab0 'Raids'` — confirm 4 entries with the current
   tier's `icId`s and that `LOOT=True` matches the floors you actually looted this week.

**B. If offsets/struct shifted (ClientStructs update).**
1. Re-run the metadata reflection tool for `TabEntryData` — confirm `size` and the offset of
   `UnkFlags1` (was **16** in a 20-byte struct). Update `raw[16]` in `ReadSavageFloorsLive` if it
   moved.
2. Confirm `AgentRaidFinder` still has `Tabs`, `Instance()`, `IsAgentActive()`, `Show()`/`Hide()`,
   and that `TabData.EntryCount`/`Entries` are unchanged.

**C. If the flag meaning changed (rare, but `UnkFlags1` is undocumented).**
1. With a floor looted this week and one not, run `/bisexport weekdump` and read the `f1=` values.
2. Re-derive which bit differs between looted and un-looted floors (it was `& 0x04`). If it changed,
   update the mask in `ReadSavageFloorsLive` and this doc.
3. Ideally confirm across two loot states as in §3.4.

**D. If the window flashes visibly** during a hidden refresh: the suppress timing changed — verify
the addon name is still `RaidFinder` (via `weekdump`'s `visibleAddons`) and that setting
`IsVisible = false` on the next tick still hides it.

---

## 5. Safety invariants (never regress these)

- **Read-only.** The only write is the addon `IsVisible` UI flag. No game data is modified.
- **Framework thread + guards.** All game-memory access is on the framework thread, behind
  null/bounds checks, wrapped in `try/catch` so nothing reaches the game (P1/P2/P4).
- **Never in combat/duty/between-areas/cutscene** for the hidden refresh (`CanHiddenRefresh`).
- **Merge-safe.** Only *fresh*, confident values are sent; Savage only when the server reports
  `savage_lockout`. A wrong/stale read must never overwrite a manual web-app entry.
