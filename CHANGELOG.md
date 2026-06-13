# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **Default API base URL is now the production server** `https://xivarsenal.app/api/v1` (was the
  localhost dev URL). New installs connect to production out of the box; existing saved configs are
  unchanged, and the value stays user-editable (set it to `localhost` for local testing).

### Added
- **Clickable items, materia icons and per-set progress in the BiS window.** Each slot's item line
  is now interactive: **left-click** posts a clickable item link to your local chat log (inspect /
  jump to the marketboard), **right-click** copies the **item name** to the clipboard so you can
  paste it anywhere — FC/party chat, Discord, the marketboard search. (A plugin can't write into the
  game's chat input or send messages — Dalamud blocks that for ban-safety — so copy-and-paste is the
  safe way to share.) The wrong/missing **materia now show their icons** next to the names, and each
  gearset has a **progress bar** (e.g. *9/12*, green when complete).
- **Gear vs BiS window overhaul.** It now shows **item icons** and **names** (not raw ids), the
  item level and source per slot, and the **wrong (red) / missing (yellow) materia** at a glance.
  New toolbar: **scope** (current set / all sets) and a **filter** (all slots / incomplete only /
  materia issues only) so you can cut the noise. Choices persist.
- **Per-session log.** The diagnostics log is cleared on each login, so it always reflects the
  current game session (it lives only in memory — nothing is written to disk).
- **Diagnostics log window.** A new window lists the recent plugin messages (status codes,
  `request_id`s, the **failing request URL**, errors — never secrets/bodies, R22) with **Copy** and
  **Clear** buttons, so you can copy them for support. Open via the **log icon** in the status
  window or `/bisexport log`. Failed pushes and connection tests now log the exact method + URL.
- **BiS overlay tells you when a gearset has no BiS target.** Instead of silently showing nothing
  (which looked like a bug for jobs/gearsets without a pinned/returned BiS, e.g. Summoner), hovering
  an equippable item now shows a clear hint with the gearset index — so you can see it's "no target
  from the server for gearset #N" and pin one in the web app (or check you're on the pinned gearset).
- **BiS overlay shows the item source.** When `GET /gear/bis` provides a `source` (per item, or a
  set-level fallback) it is shown next to each slot — e.g. *Raid*, *Tomestone*, *Crafted*, *Relic*,
  *Ultimate* — localized DE/EN, with the raw value shown for any source the plugin doesn't know yet
  (forward-compatible).
- **BiS overlay shows item level and localized slot names.** Each line now reads the localized slot
  name (DE/EN, e.g. *Kopf*/*Head*) and the **iLvl** of both the BiS target and your equipped piece,
  so you can gauge the gap at a glance.

### Changed
- **BiS hover overlay is now slot-based and much more informative.** Hovering any equippable item
  now resolves its equipment slot and shows the current gearset's BiS target for that slot — even
  when your equipped piece differs (previously the overlay only appeared when the hovered item was
  itself the BiS item, which is why it often seemed missing). For each slot it shows the **target
  item name**, the **target materia** (what to socket), whether you **own** the target
  (inventory/armoury/equipped), and your equipped piece for comparison.

### Fixed
- **Overlay now shows which materia is wrong vs missing, not just the full BiS list.** When the item
  matches but materia differs, it shows the equipped materia that don't belong in **red** ("Wrong:")
  and the BiS materia you still need in **yellow** ("Missing:") — so you see exactly what to replace,
  instead of the full target list. (Computed as a multiset diff in `BisComparer`.)
- **Ring materia is shown correctly per finger.** Rings are interchangeable, but left/right often
  need different materia. The comparison now claims exact (id + materia) ring matches first, so two
  same-id rings each pair with the right target regardless of finger; and the overlay always shows
  each ring's target materia (even when complete), so you can see what belongs in each.
- **BiS overlay compares against the live equipped gear.** The comparison was taken from the
  (≤5-min) cached fetch, so after swapping a piece the overlay still showed the old verdict. It now
  recomputes against the currently worn gear whenever the equipment changes — correct immediately,
  with no upload needed.
- **Change-detection push rules refined.** A push now fires only when a gearset is **saved**, or
  when **materia is socketed** on the worn gear (materia changed, item ids unchanged, same
  gearset). **Swapping** a piece (item id changes — possibly temporary) and merely **switching**
  gearsets no longer push.
- **BiS overlay docks toward the cursor.** When the native item tooltip is to the left of the
  cursor, the overlay now docks to the tooltip's right edge (near the cursor) so it's quicker to
  spot; otherwise it stays on the left edge.
- **Gearset changes now push promptly (with a confirmation).** Change-detected pushes were caught
  by the 5-minute auto-push throttle and silently skipped, so editing/switching a gearset seemed
  to do nothing. Event-driven triggers (manual, login, gearset change) now bypass that interval and
  send right away — only the periodic auto-push stays throttled; the 429 back-off still protects the
  rate limit. Change detection is also a bit snappier (poll 2s, debounce 5s). Requires "Push when a
  gearset changes" to be enabled.
- **Materia is now read from the live equipped gear, not the gearset snapshot.** A gearset only
  refreshes its materia on save, so melding into worn gear without re-saving sent stale materia.
  For the currently equipped gearset the plugin now reads item ids + materia from the live
  EquippedItems container, and change-detection folds the equipped container into its signature —
  so socketing materia is pushed/detected without re-saving the gearset. (Non-equipped gearsets
  still reflect their last save, which is inherent — those items aren't worn.)
- **Gearset-change detection now reacts to materia melds.** The change signature hashed only the
  materia type, not the grade, so overmelds (grade-only changes) were missed. It now covers both
  type and grade, matching the resolved materia item ids that get pushed.
- **"Test connection" failed with "Could not parse the server response."**
  `GET /version` returns `scopes` as a JSON **array**, but `VersionResponse.Scopes` was
  typed as a scalar `string?`, so `System.Text.Json` threw on deserialization. Changed it
  to `List<string>?` and added a regression test parsing a real `/version` body.

### Added
- **Smoother device-flow connect.** Starting "connect via browser" now opens the pre-filled
  approval page (`verification_uri_complete`, RFC 8628 — falls back to `verification_uri`) and
  copies the `user_code` to the clipboard, so the user only clicks Approve. The dialog shows the
  code with a copy button (clipboard icon) and an "Open browser again" button. Approval stays the
  user's explicit click — the plugin never approves programmatically.
- **BiS hover overlay** — hovering an item whose id is the BiS target for your **currently
  selected** gearset shows a small overlay listing the slot and your current state for it
  (complete / materia differs / different item / empty). It is a **styled window** (rounded, accent
  border, status icons) **docked flush to the native item tooltip** — directly above it (aligned to
  its left edge, on-screen-clamped), or below when there's no room — so it stays attached and never
  overlaps, and only the active gearset/class is shown. Safe ImGui overlay — it does not touch the
  native tooltip, so it cannot crash the client and is patch-stable (P2/P6). Toggleable; cached via
  a shared `BisService`.
- **Gear vs BiS (Feature A)** — reads BiS targets via `GET /gear/bis` (new `gear:read` scope,
  issued alongside `gear:write`) and shows an in-game per-slot diff of live gear vs BiS in a new
  **BiS window** (opened from the status window). Pure `BisComparer` matches by `gear_index`+`job`,
  treats rings as interchangeable and materia order as irrelevant. (Inventory/Feature B remains
  deferred to `protocol_version: 2`.)
- Plugin UX & robustness (all within the existing API contract):
  - **Status window** — last push time, outcome + `request_id`, rate-limit countdown, with quick
    actions: **push now**, **preview what will be sent**, **open web app**, open settings.
  - **Per-character push opt-in** — choose which of your characters may be pushed.
  - **Gearset-change detection** — debounced push when a set changes in-game (still rate-limited).
  - **Scope check** after the connection test — warns if the key lacks `gear:write` (R17).
  - **Toast notifications**, **log-verbosity** setting, and a **configurable web app URL**.
- Initial plugin implementation.
  - Encapsulated, interface-based core (`IApiClient`, `IGearSource`, `ITokenStore`,
    `ILocalizer`) with a thin Dalamud host (`EorzeaArsenal.Core` + `EorzeaArsenalPlugin`).
  - Connect via OAuth 2.0 device flow **and** paste-key fallback; in-plugin **Disconnect**.
  - `PUT /gear` push of all gearsets across all jobs, triggered by `/bisexport`, login, or a
    throttled auto-push.
  - Stable `cid_hash` derivation (SHA-256 of the decimal ContentId), locked by a test vector.
  - Client-side validation, single in-flight push with coalescing, and proactive rate-limit
    back-off (30 uploads/hour).
  - Bilingual DE/EN UI; ToS opt-in notice; versioned + migrated config.
  - Unit tests for the API client, device flow, gear mapping, validation and `cid_hash`.
  - CI (build, test, format, vulnerability scan, CodeQL) and a custom-repo release pipeline.
