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
- The sourcing detail is now shown as **numbered steps**, and is actionable and complete in both the
  BiS window and the team farm:
  - **Every way in, and every step of it.** A piece lists all of its acquisition routes as
    alternatives — a savage piece drops in the fight **or** can be traded for books — and each route is
    broken into the concrete things to do (*Fight …*, *Buy …*, *Upgrade …*), so an augmented (Tome+)
    piece reads **get the base first, then augment it** rather than assuming the base is in hand. The
    farm pulls the full chain from `GET /gear/obtain`, which its own response omits.
  - **"Do I have it?" per step, retainers included.** Each purchasable cost (tokens, materials) carries
    a **have / need** count, green once you own enough. The count comes from the server's holdings
    (`GET /me/holdings`), which sum every synced storage **including each retainer as of its last
    visit** — the one thing a live game read cannot see — with the live in-game count as an immediate
    fallback until the server number lands. The plugin now also reports the tier's tracked consumables
    (from `GET /gear/tracked-items`) in the inventory sync so those counts exist, and refreshes the
    holdings after each sync.
  - **"Base owned" from anywhere.** The Tome+ base step collapses to *Base owned* not only when the
    base is equipped but whenever you hold it (bags, saddlebag or a retainer), using the base item ids
    the server now sends on the hand-in cost.
  - **What's still short, per character.** Next to each teammate in the farm, a one-line summary sums
    the materials/tokens they still need across all their missing pieces, minus what they own — so you
    see at a glance what to gather for them.
  - **Show NPC on the map.** Right-click a piece (farm row or BiS item) → *Show NPC on map* opens the
    map and drops a flag on the vendor. The location is resolved from the server's ids, or — when it
    only sends a zone name — from the game's own place names, so it works without a server change.
  - A larger, better-spaced tooltip for the whole checklist.
  - **Skips a step you already did.** If you are already wearing the tome base a Tome+ piece upgrades
    from (recognised as a tome piece of the same slot), the "buy the base" step collapses to
    **"Base owned"** — only the upgrade remains.
  - The **in-game hover overlay** now also shows how to get the target when you are hovering a piece
    that is *not* your BiS item and you do not own the right one yet — so you see where to get it
    without opening a window.
- **Capped-tomestone balance sync.** The plugin now sends your current capped-tomestone count to the
  web purchase advisor (`PUT /me/tome-balance`), so it can say what to buy now vs. in N weeks without
  you retyping a number the game already knows. It piggy-backs the inventory sync (no extra polling),
  needs a key with **`characters:write`**, and is per character; if it cannot push, the advisor still
  works from a hand-typed number.

- **Purchase advisor ("Kaufberater") window.** A separate menu entry, deliberately not folded into the
  BiS window: BiS is the *goal*, the advisor is the *path* to it (the intermediate gear between raid
  tiers).
  - **Your saved plan.** Renders the layout you built in the web advisor, read per (character, job,
    target set) via `GET /me/advisor-plan` (scope `plans:read`), per slot with *worn / owned / still
    missing* and the usual sourcing on hover. A plan is stored under the set's **web identity**, so it
    only becomes addressable once `GET /gear/bis` sends a set's `target`; until then the section says
    so instead of failing. "No plan saved" is a normal state, not an error — the advisor's
    *recommendation* is computed client-side in the web and has no endpoint, so it is not mirrored here
    yet, and editing a plan in game waits on the per-slot choices from the server.
  - **Your stock.** The active tier's tracked materials, upgrade stone and books — from the new
    `groups` on `GET /gear/tracked-items`, so a tier rotation carries itself without a plugin release —
    each with the **server's** owned count, which is the only one that includes your retainers. The
    live in-game count fills in until the server number lands.
  - **The recommendation, computed server-side.** `GET /me/advisor-options` returns the one ranking the
    web renders too, so the plugin never owns a second copy of the rules and a tier rotation needs no
    release: the ranked steps with their tomestone price, when each becomes affordable against the
    pushed balance, the vendor, and the material each consumes.
  - **The set as a grid**, laid out like the BiS window and coloured like the web advisor (green on
    BiS, blue you own it, orange next purchase, grey nothing deterministic left), leading with the
    single best next move and the set's numbers.
  - **My layout is editable in game.** The picker per slot offers exactly the pieces the server lists,
    so a saved plan can never contain an invented item id; saving writes to the same key the web does.
    A plan is only ever written on a deliberate action, never as a background sync.
  - Every icon answers on hover: what the piece is, whether you wear/own/still need it, the route in,
    and — for a material — which bag or retainer the stacks sit in.
- **"Still needed for this set" in the BiS window.** Each set folds out what completing it actually
  costs: the tomestones the remaining purchases add up to, measured against the balance the plugin
  pushes (with how many capped weeks that is), and every upgrade material and raid book still short,
  each with what you already hold. The totals come from the advisor, so the book trade counts as the
  alternative to a savage drop exactly as the advisor ranks it, and the read only fires when the
  section is opened.

- **The farm leads with what you can actually do.** For your own characters the ways in are ordered by
  what you hold rather than by what the piece's source suggests: a coffer already in your bag reads
  *"Coffer in hand — just open it"* instead of sending you to the fight, and a book trade you can
  afford comes before the drop, marked *"you can do this now"*. Coffers are counted server-side too,
  so one sitting on a retainer counts. A teammate's row is never re-ordered — their stock is not
  visible, so there is nothing to rank by.

### Fixed
- **A swapped ring pair is no longer two missing rings.** The farm compared finger by finger, so
  wearing both BiS rings the other way round listed them as still to get. The rule (rings are
  interchangeable) now lives once in the core, tested, instead of being re-derived per view.
- **The language setting now governs game names too.** Item, vendor, zone and duty names were read in
  the game client's language regardless of what the plugin was set to, so switching the plugin to
  English left them German. They follow the plugin's setting now — which is also the only way to use
  the plugin in English on a German client.
- **The inventory sync no longer empties the saddlebag.** Its containers only read once the player has
  opened the saddlebag in a session — and until then they read as *empty*, not *unavailable*. It used
  to ride along in the `character` scope, which the upload declares fully observed, so syncing
  beforehand told the server the saddlebag was empty and it deleted what was stored there. The
  saddlebag is now its own reconciliation scope (like a retainer): it is declared only when it was
  actually read, and left alone otherwise. A manual sync says once when it could not be read, so the
  player knows those counts are not current — nothing is lost either way.
- **The sourcing speaks the client's language.** Coffers, materials, books, vendors, zones and fight
  names were English throughout, because the server names things in English while the game carries
  every language itself. Anything the server identifies by id is now named by the game — items,
  coffers, vendor NPCs (`ENpcResident`), zones (`TerritoryType` → `PlaceName`) and fights (the
  server's new `duty_content_ids`, paired only when there is one id per name, since it omits the ones
  it cannot resolve). Every lookup falls back to the server's English text, so nothing can read worse
  than before. Shop labels stay English on purpose: they are the data source's own wording and have no
  game row to look up.
- **A teammate's row no longer answers from your bags.** The team farm measured every member's
  remaining cost against the player's own holdings, so someone else's line claimed they were short
  materials — or already owned a base piece — purely because the player was. The plugin can see
  nobody else's bags, retainers or tomestones (`/me/holdings` is caller-only and now pinned to the
  character on screen; the farm endpoint carries only shared gear). A teammate's row now states what
  the set requires and says plainly that their stock is not visible; only the player's own row is
  measured. The equipped check stays for everyone — what a member wears comes from the team data.
- **Owned counts no longer read 0 for anything the server does not track.** The server's holdings won
  unconditionally, but a server `0` means "no record", not "you own none": the weekly tomestone is a
  currency and is never part of the inventory sync at all, so a player holding 1109 was shown `0/495`.
  The count is now the higher of the server's number and the game's — the server still wins for
  retainer stock, the game still wins for anything not synced yet. Holdings are also pinned to the
  character on screen (`&character_id=`), instead of whichever one the account last made active.
- **Automatic syncs no longer talk in chat.** Only a sync you asked for reports there; logins, the
  periodic timer, retainer visits and the hidden Duty-Finder refresh (which produced the duplicate
  "2 weekly fields" lines) go to the log instead. Failures still always speak.
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
