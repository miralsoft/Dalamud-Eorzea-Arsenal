# Project state (living)

> The complete in-repo memory so an AI/contributor on another machine can continue without losing
> context (R3). Keep this current **in the same commit** as the change it describes.

_Last updated: 2026-06-12._

## Status: first implementation complete (pre-release, not yet in-game verified)

### Done
- Repo scaffolding: `.slnx`, two projects + tests, `.editorconfig` (CRLF, naming), `global.json`
  (.NET 10), `.gitignore`.
- **Core (`EorzeaArsenal.Core`)** — abstractions, models/DTOs, `EorzeaJson` (snake_case,
  literal dict keys), `ApiClient` (device flow + `PUT /gear` + `GET /version`, full 4xx/429
  mapping, no body logging), `ConnectionService`, `GearSyncService` (single in-flight + coalesce,
  throttle/back-off, validate-before-send), gear mapping (`JobMap`, `EquipmentSlots`,
  `ItemIdNormalizer`), `CidHash`, `GearValidator`, `GearSanitizer`, `Localizer` (DE/EN).
- **Plugin (`EorzeaArsenalPlugin`)** — thin entry point, `GameGearSource`
  (`RaptureGearsetModule` via `IPlayerState`/`IClientState`/`IFramework`/`IDataManager`),
  `PluginConfig` (versioned + migrated), `ConfigStore` (`ITokenStore`/`IApiSettings`),
  `PluginLogAdapter`, `ConfigWindow` (ToS opt-in, language, base URL + test, connect/paste/
  disconnect, push options). Triggers wired: `/bisexport` (manual), login (`PushOnLogin`), and a
  throttled auto-push driven by `IFramework.Update` (requests at most once/min; the service then
  enforces the min interval + unchanged-skip). A dedicated gearset-change event is **not** hooked —
  the periodic auto-push + unchanged-skip covers "push when something changed".
- **Tests** — 74 passing: cid_hash vectors, job/slot maps, item-id normalize, validation,
  sanitizer, ApiClient (handler-scripted incl. 401/403/409/422/400/429 + Retry-After + network),
  ConnectionService (device flow/expiry/deny/cancel/paste/disconnect), GearSyncService (sent,
  not-connected, not-logged-in, unchanged, throttle, back-off, invalid-local, **coalescing/P11**).
- Build green (0 warnings), `dotnet format` clean, DalamudPackager produces `latest.zip` + manifest.
- Docs: README, CHANGELOG, AGENTS, this state, handoff, ADRs 0001–0005, operations guide.

### Added after the initial implementation (in-scope, no API change)
- **Status window** (`StatusWindow`): last push/outcome/`request_id`, rate-limit countdown; quick
  actions push-now, preview, open-web-app, open-settings. Opens via the Main UI button.
- **Per-character opt-in** (`PluginConfig.Characters` keyed by `cid_hash`; gated in the plugin).
- **Gearset-change detection**: `GameGearSource.ComputeGearsetSignature()` + debounced
  framework-tick trigger (`PushTrigger.GearsetChange`), still bounded by the sync throttle.
- **Scope check** after Test connection (`ScopeUtil.HasGearWrite` over `/version` scopes).
- **Toasts** (`IToastGui`), **log verbosity** (`PluginLogAdapter` + `LogVerbosity`), **web app URL**.
- `GearSyncService` now exposes `LastReport` / `LastSuccessfulPushUtc` / `IsRateLimited` for the UI.
- In-game verified so far: connect (device-flow/paste) + `/version` test + `/bisexport` push.

### Gear vs BiS (Feature A) — implemented (API contract published 2026-06-13)
- Keys now carry `gear:write gear:read`. New read path `GET /gear/bis` (optional `?cid_hash=`).
- Core: `BisResponse`/`BisGearset` models, `IApiClient.GetBisAsync`, `ApiErrorKind.NotFound`,
  pure `BisComparer` (match by gear_index+job; rings L/R interchangeable; materia multiset).
- Plugin: `BisWindow` reads live gear → `GET /gear/bis` → renders per-slot diff; opened from the
  status window. 403→reconnect, 404/empty→"no BiS pinned" messaging.
- **Feature B (inventory) stays deferred** to `protocol_version: 2` (see agent memory).

### Next / open
- **In-game verification (operator):** load the dev build, run `/bisexport`, confirm gearsets,
  materia ids, world name and `cid_hash` are correct. The `GameGearSource` mapping
  (materia resolution via the `Materia` Excel sheet, HQ-offset stripping) is **best-effort and
  not yet validated in-game** — most likely place for adjustments.
- **CI:** confirm the workflows run on `windows-latest` with the Dalamud distrib download; enable
  CodeQL (needs GitHub Advanced Security if the repo stays private).
- **Custom repo:** the release workflow regenerates `pluginmaster.json`; the operator adds its raw
  URL in Dalamud settings.
- **Deferred (design only, do not build yet):** pairing-code connect path; inventory.

## Key facts
- Build needs **.NET 10 SDK** + local Dalamud dev libs (`%AppData%\XIVLauncher\addon\Hooks\dev`,
  Dalamud 15 → `Dalamud.NET.Sdk/15.0.0`, .NET 10 target). Don't hand-pick versions (P6).
- `dotnet format` does not accept `.slnx` yet — run it per-project.
- Commit author: **Michael Tosch** (`m.tosch@miralsoft.com`); **never** add an AI co-author (R34).
