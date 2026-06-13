# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
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
