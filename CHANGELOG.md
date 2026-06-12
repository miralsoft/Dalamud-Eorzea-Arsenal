# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **"Test connection" failed with "Could not parse the server response."**
  `GET /version` returns `scopes` as a JSON **array**, but `VersionResponse.Scopes` was
  typed as a scalar `string?`, so `System.Text.Json` threw on deserialization. Changed it
  to `List<string>?` and added a regression test parsing a real `/version` body.

### Added
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
