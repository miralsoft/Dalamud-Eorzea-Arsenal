# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  log icon or `/bisexport log`. The log is per-session (cleared on login; in memory only).
- **Status window** — last push time, outcome + `request_id`, rate-limit countdown, and quick
  actions: push now, preview what will be sent, open web app, open settings.
- **Connect via OAuth 2.0 device flow and paste-key fallback**, with in-plugin **Disconnect**. The
  device flow opens the pre-filled approval page (`verification_uri_complete`, RFC 8628) and copies
  the `user_code`; approval stays the user's explicit click (the plugin never approves
  programmatically). A scope check after the connection test warns if the key lacks `gear:write`.
- **`PUT /gear` push** of all gearsets across all jobs, triggered by `/bisexport`, login, a
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
