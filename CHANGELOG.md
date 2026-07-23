# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **"How to get it" on BiS pieces.** Hovering a BiS target — in the list, the grid tile or the
  shopping list — now also shows where the piece comes from and what it costs (the same route detail
  as the team farm: the fight it drops in with its coffer, or what to trade and with which vendor,
  down the whole chain). Reads impersonal game data via `GET /gear/obtain` with your existing key,
  cached for the session and fetched in the background; toggle under *Display*. The route renderer is
  shared with the farm tab, so the two never describe a piece differently.

### Fixed
- The hidden Duty-Finder refresh (the `normal`/`alliance` weekly read) no longer leaves the finder on
  the raid it loaded. It now remembers the duty you had selected and re-selects it before closing, so
  reopening the Duty Finder puts you back where you were — including a **roulette** (Duty Roulette /
  daily), which the game stores in the same field as a regular duty but restores through a different
  call. Nothing selected beforehand means nothing is restored.

## [0.4.0] - 2026-07-17

### Added
- **Teams companion (opt-in).** A new in-game window (hub button, `/xivarsenal teams`) that mirrors your
  teams from the web app — read-only rendering; the server owns all logic. Off by default; needs a key
  with **`teams:read`** (+ **`teams:write`** for the two writes). Existing keys auto-upgrade on the next
  call. Covers:
  - **Calendar** — a dedicated cross-team month calendar (own hub button / `/xivarsenal calendar`)
    over the current + next two months, colour-coded per team, with **in-game RSVP** (yes/maybe/no;
    optimistic, then re-polled) and per-event "open in web".
  - **Mit cheat sheets** — a **time-axis timeline**: mechanics on the left, cooldowns in per-job
    columns aligned to the same times, grouped by named phase. **Phase checkboxes** and a **tag filter**
    (raidwide / tankbuster / other) pick what to show; cooldowns render as skill icons with the name,
    recast and duration on hover. The current job is preselected only when the plan has it (else a free
    picker; single-job or all-jobs), remembered per plan.
  - **Content hub** — every fight with its bosses/drops and resources, each **labelled by type**
    (link / video / plan / note / image / pdf / file); **note text** shown inline; **images open in a
    dedicated window** scaled to size; PDFs and links open in the browser.
  - **Farm** — who-needs-what across the team (equipped vs BiS target, still-missing per member), each
    missing piece labelled by **source** (savage / Tome+ / Tome) with the **way to get it** on one line
    — the fight it drops in, or what to trade and with which vendor — and **every route on hover**
    (coffer, the piece you hand in, vendor zone + coordinates). The primary route follows the same rule
    as the web, so plugin and site never disagree. Hardest-to-get first; all server-provided, the
    plugin never guesses, and an unconfigured tier just shows the item as before.
  - **Owned gear coffers** ride the existing inventory upload: the loose storages (bags, saddlebag,
    retainers) now also report savage gear coffers, so a coffer's web page can show "you own ×N, here"
    and how many open pieces you can make now. Detected by name like the web does, not a hard-coded
    list; potions, food and materials stay out of the ownership set.
  - **FFLogs** — recent kills/wipes with each report's **date** and a **direct link** to it
    (best-effort; a not-connected/empty state never crashes).
  - **Absence** — report/cancel your own vacation ranges, with a **date picker** and localized date
    display (DE/EN).
  - **Events** — the per-team list of upcoming (and optionally past) dates, grouped by event, with
    **zebra-striped rows** and a **configurable text size** so long lists stay readable at a glance.
  - A **Teams settings tab** (icon/text display for cooldowns and resource labels, note visibility,
    default job/tag/phase selection for mit plans, event text size) and **"open in web" deep links**
    that land on the exact thing you were looking at — the selected mit plan, that one event
    occurrence, that content — not just the tab.
  - **In-game notifications** — a toast **with a sound** and a **clickable chat link** for new loot,
    event reminders (each of your 1-day / 3-hour / 1-hour warnings fires once) and newly planned events.
    Deduped by notification id and persisted, so a relog never re-toasts the backlog.
- Polls the calendar + notifications at most every ~5 minutes; the content hub, farm and FFLogs load on
  demand. Never writes anything but your own RSVP and your own absence; a server `403`/`404` is shown,
  never worked around.
- **"What's new" window** (`/xivarsenal whatsnew`, or the menu entry, which stays highlighted until you
  have read the notes for the version you are running). A short, plain-language digest of what each
  release changed — new / improved / fixed — as opposed to this changelog, which is written for
  contributors. It ships **inside the plugin**, so it works offline and can never disagree with the
  build you are running. After an install or update it **opens once, on the first frame you are
  actually in the world** — not at the title screen, and it works just as well when the update is
  installed mid-session. Switchable off under *Display*; links out to the full changelog. A test pins
  the notes to the shipped version so a release cannot forget them.

### Changed
- **The chat command is now `/xivarsenal`** (was `/bisexport`) — the plugin long outgrew a pure BiS
  export. The old `/bisexport` command has been removed.
- The hub is now the **menu** window (`/xivarsenal menu`, was `/xivarsenal status`) and is purely
  actionable; the **"what will be sent" preview** moved into its own window instead of expanding inline.
- A failed team/parse response now surfaces the concrete cause (HTTP status or the JSON path of a
  shape mismatch) instead of a generic "could not load", to make diagnosis quick.

## [0.3.0] - 2026-07-05

### Added
- **Weekly checklist — four more fields.** Building on 0.2.0, the weekly auto-fill now also covers:
  - **Unreal trial** (`unreal`) — done-this-week, decoded from the Faux Hollows timestamp vs the
    weekly reset. Background-readable, so it syncs on login/hourly without opening anything.
  - **Wondrous Tails** (`wondrous`) — a completed book (9/9) that was **bought this week**. Because a
    book is valid for two weeks and its sticker count stays put after a hand-in, the plugin anchors on
    the book's own expiry (a this-week book expires beyond the next reset) — so a stale completed book
    can never be mis-reported the following week. Background-readable, no client-side state.
  - **Normal raid** (`normal`) and **Alliance raid** (`alliance`) — read from the Duty Finder's
    weekly-reward count (the game only exposes it for the selected duty), classified as 8-player normal
    vs 24-player alliance from the duty's party size. Fetched via a **hidden Duty-Finder refresh** — the
    plugin loads the current tier's normal and alliance raids into the finder with the window suppressed,
    reads each reward, then closes it (analogous to the Savage refresh) — at login, hourly, on the
    manual sync, and opportunistically while you have the finder open. Gated on the account's
    `alliance_lockout` / `normal_lockout`. As with every weekly field, only a confident **done** is
    ever sent, so a manual web-app entry is never overwritten.

  All four decodes were validated in-game against before/after captures.

### Changed
- The manual **Sync weekly** action now also kicks off the hidden Savage + Duty-Finder refreshes, so a
  button press picks up the `f1`–`f4` / `normal` / `alliance` fields too.
- The `/xivarsenal weekdump` diagnostic now also reports the Wondrous Tails expiry/expired flags and
  the classified kind (normal/alliance) of the selected Duty Finder duty. New `/xivarsenal dutyrefresh`
  triggers the hidden Duty-Finder refresh on demand; `dutyprobe` is a raw control test.

## [0.2.0] - 2026-07-02

### Added
- **Weekly-checklist auto-fill (opt-in).** When enabled, the plugin fills your web-app **weekly
  checklist** from the game so completed weeklies show up without manual ticking — for the right
  character, cross-device (stored server-side). It reads only values it can determine **with
  certainty**, reads the server's current state first and merges just the fields that **changed** via
  `PUT /characters/{id}/weekly`, so it **never overwrites a manual entry** and never wastes a write.
  Covered:
  - **Tomestones** (`tomesHave`) — weekly-limited tomestones acquired this week.
  - **Custom Deliveries** (`custom`) — reported *done* once all weekly allowances are used.
  - **Savage floor loot** (`f1`–`f4`) — obtained-this-week per floor, gated on the account's
    `savage_lockout`. This state lives only in the Raid Finder, so the plugin fetches it via a
    **hidden, instant Raid-Finder refresh** — the window never actually opens — at login, hourly and
    whenever you open the Raid Finder yourself. It is guarded to never run in combat, a duty, a
    cutscene or between areas, and is purely read-only.

  Syncs on login, after a gear push, hourly and via a **"Sync weekly"** button; needs a key with
  **`characters:write`** + **`gear:read`** (a 403 shows a reconnect hint). The per-character server
  id is learned from the gear/inventory push response and cached.

### Changed
- **Reworked the plugin windows for clarity.**
  - The **main window is now a hub**: large single-per-row action buttons with icons, grouped into
    *Actions* / *View* / *Manage*, above a clear connection banner.
  - **Settings are organised into tabs** — *Sync*, *Display*, *Characters*, *Connection* — that
    appear once connected; before that you only see the connect flow. Connection management and the
    third-party-tool notice live in the last tab. Everything is larger and roomier, and hint lines
    now wrap to the window width instead of overflowing.

## [0.1.1] - 2026-06-21

### Added
- **Plugin icon in the Dalamud installer** via the manifest `IconUrl` (a 512×512 PNG served from
  `xivarsenal.app`), shown both in the available list and, after install, in the installed list.

### Changed
- **Plugin author is now "Sanaka"** (the name shown in the installer); the company field was removed.
  This is a personal hobby project, intentionally not tied to a business identity.

### Fixed
- **`pluginmaster.json` generator** now runs on Windows PowerShell 5.1 (it no longer relies on the
  pwsh-only `-AsArray`) and writes UTF-8 **without a BOM**, which Dalamud's parser rejects — so the
  repo index is produced correctly both in CI and locally.

## [0.1.0] - 2026-06-21

First public release.

### Added
- **Owned-items / inventory upload (opt-in, Phase 2).** When enabled, the plugin uploads which
  equippable gear you **own** via `POST /inventory` so the web app can tick off pieces in the
  overview, item search and collection. It is **scope-accurate**: each upload reports exactly which
  storages it fully scanned, and the server replaces only those — unreported areas keep their last
  state, and a reported-empty area is cleared (so selling a piece in your bags removes it on the next
  scan). The **`character`** scope bundles every locally readable storage in one scan (equipped,
  armoury, bags, saddlebag, glamour dresser) so moving items between them is harmless; it uploads on
  login and on a throttled timer (unchanged scans are skipped, so it never wastes the 30/hour
  budget), plus a **"Sync inventory"** button in the status window. With the extra **"Include
  retainers"** opt-in, each retainer is scanned as its own `retainer:<id>` scope when you open it at
  a summoning bell. Only equippable items are sent (weapons/armour/accessories — never
  materia/consumables/materials), the **Armoire is not scanned**, and your manual web-app markings
  are never touched. Uses the same `cid_hash` as the gear push; needs an `inventory:write` key
  (reconnect if a 403 says it's missing).
- **Gear vs BiS comparison.** Reads BiS targets via `GET /gear/bis` (the `gear:read` scope, issued
  alongside `gear:write`) and shows an in-game per-slot diff of live gear vs BiS in a dedicated
  **BiS window** (opened from the status window). Pure `BisComparer` matches by `gear_index`+`job`,
  treats rings as interchangeable and materia order as irrelevant. Auto-loads on login and refreshes
  when the window opens if the data is stale.
- **BiS window views and tools.** Item **icons + names** (not raw ids), item level and source per
  slot; a **scope** selector (current set / all sets) and a **filter** (all / incomplete / materia
  issues); a **character-screen grid** view (weapon + off-hand on top, five armour rows left / five
  accessory rows right, status-bordered tiles with name + materia-to-socket beside each); a
  **shopping list** that aggregates every still-needed item + materia you don't own; per-gearset
  **progress bar**; and per-item actions — **left-click** links the item to your local chat log,
  **right-click** copies its name to the clipboard.
- **BiS hover overlay.** Hovering any equippable item resolves its slot and shows the current
  gearset's BiS target for that slot — target item name + materia, whether you own it, the localized
  slot name, item levels, the item **source** (Raid/Tome/Crafted/Relic/…), and a clear hint when a
  gearset has no BiS target. A safe, styled overlay docked to the native tooltip (toward the cursor);
  it never touches the native tooltip, so it cannot crash the client (P2/P6). Toggleable.
- **Server-info-bar (DTR) status entry.** A compact **Arsenal: &lt;last push&gt;** entry in the
  in-game server-info bar: time since the last successful push (e.g. *3m*), **!** on failure, or
  *off* when not set up. Hover for the full time; click to open the status window. Toggleable.
- **Diagnostics log window.** Lists recent plugin messages (status codes, `request_id`s, the failing
  request method+URL, errors — never secrets/bodies, R22) with **Copy**/**Clear**, opened via the
  log icon or `/xivarsenal log`. The log is per-session (cleared on login; in memory only).
- **Status window** — last push time, outcome + `request_id`, rate-limit countdown, and quick
  actions: push now, preview what will be sent, open web app, open settings.
- **Connect via OAuth 2.0 device flow and paste-key fallback**, with in-plugin **Disconnect**. The
  device flow opens the pre-filled approval page (`verification_uri_complete`, RFC 8628) and copies
  the `user_code`; approval stays the user's explicit click (the plugin never approves
  programmatically). A scope check after the connection test warns if the key lacks `gear:write`.
- **`PUT /gear` push** of all gearsets across all jobs, triggered by `/xivarsenal`, login, a
  debounced gearset-change detector, or a throttled auto-push. Stable `cid_hash` (SHA-256 of the
  decimal ContentId), locked by a test vector. Per-character push opt-in, single in-flight push with
  coalescing, client-side validation, and proactive 429 back-off (30 uploads/hour).
- **Toast notifications**, a **log-verbosity** setting, and a **configurable web app URL**.
- **Bilingual DE/EN UI**, a third-party-tool **ToS opt-in** notice, and a versioned + migrated
  config. Interface-based core (`EorzeaArsenal.Core`) with a thin Dalamud host (R8/R9/R11) and unit
  tests for the API client, device flow, gear/inventory mapping, validation, chunking and `cid_hash`.

### Changed
- **Default API base URL is the production server** `https://xivarsenal.app/api/v1`. New installs
  connect to production out of the box; existing saved configs are unchanged, and the value stays
  user-editable (set it to `localhost` for local testing).
- **BiS status colours are a clear traffic light** everywhere (window list, grid tiles, hover
  tooltips, in-game overlay): **green** = fully BiS, **orange** = item correct but materia is off,
  **red** = the item itself is wrong or the slot is empty.
- **Shopping list is grouped by class.** With *All sets* active, items are split into sections by job
  — shared pieces collapse under a combined header (e.g. *PLD · WAR · DRK · GNB*) — so you can see at
  a glance what each item is for. (Materia stays in its own aggregated section.)

### Fixed
- **Overlay shows which materia is wrong vs missing**, not the full BiS list: equipped materia that
  don't belong in red, and the BiS materia you still need in orange (a multiset diff in `BisComparer`).
- **Ring materia is shown correctly per finger.** Exact (id + materia) ring matches are claimed
  first, so two same-id rings each pair with the right target regardless of finger.
- **BiS overlay compares against the live equipped gear**, recomputing whenever equipment changes —
  correct immediately after a swap, with no upload needed.
- **Materia is read from the live equipped gear, not the gearset snapshot**, so socketing materia
  into worn gear is detected/pushed without re-saving the gearset (covers both type and grade, so
  overmelds are no longer missed).
- **Change-detection push rules refined.** A push fires only when a gearset is saved or materia is
  socketed on the worn gear; swapping a piece or merely switching gearsets does not push.
- **Event-driven pushes (manual/login/gearset-change) bypass the auto-push throttle** so they send
  promptly; only the periodic auto-push stays throttled, and the 429 back-off still applies.
- **"Test connection" parse failure** — `GET /version` returns `scopes` as a JSON array;
  `VersionResponse.Scopes` is now `List<string>?` (with a regression test).

### Security
- **Security policy (`SECURITY.md`)** with private vulnerability reporting and an exact statement of
  the data the plugin sends (never the API key, never request/response bodies in logs).
- **Hardened CI/CD:** GitHub Actions are **pinned to commit SHAs** and kept current by **Dependabot**;
  **CodeQL** static analysis (C# + workflows) runs on the public repo; CI keeps build/test/format and
  a vulnerable-dependency scan with least-privilege `GITHUB_TOKEN`.
- **Release pipeline no longer pushes to `main`** — `pluginmaster.json` and `latest.zip` are
  published as release assets and served via the stable `releases/latest/download/` redirect, so the
  default branch can be fully protected.
