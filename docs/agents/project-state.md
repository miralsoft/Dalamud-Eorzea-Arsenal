# Project state (living)

> The complete in-repo memory so an AI/contributor on another machine can continue without losing
> context (R3). Keep this current **in the same commit** as the change it describes.

_Last updated: 2026-09-13._

## Status: 1.1.0 ready, verified in game, not yet pushed or tagged

1.0.0 shipped. The branch `feat/stable-gearset-identity` carries 1.1.0: gearsets have a server-minted
identity, the reconciliation window is in, the plugin names a job the game added after the release, and
everything below was walked through in game on 2026-09-12 and 2026-09-13.

**What was verified in game**, so that nobody repeats it and nobody assumes more than was done: all four
resolution rungs, the cross-check firing and not falsely firing, a doubled identity withdrawn from both
claimants, all six reconciliation verbs, the question card, the candidate picker, bulk booking with a row
struck out, the window opening and closing by itself, set numbers, role groups, the grid view, the
settings page, persistence across reloads, a second character including the migration path from a build
without gear keys, switching characters both ways, and Beastmaster synced end to end the day the server
accepted `BST`.

**What was not:** the login switches (the operator does those separately), a hand-made row offered as a
resemblance, and crafter/gatherer BiS, which does not exist yet.

The four CI gates were run against a **fresh clone** rather than the working tree, where the format check
reports sixteen thousand line-ending failures that do not exist. Two real violations were hiding behind
that noise. Judge the format gate from a clone, never from the working tree.

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
  disconnect, push options). Triggers wired: `/xivarsenal` (manual), login (`PushOnLogin`), and a
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
- In-game verified so far: connect (device-flow/paste) + `/version` test + `/xivarsenal` push.

### Gear vs BiS (Feature A) — implemented (API contract published 2026-06-13)
- Keys now carry `gear:write gear:read`. New read path `GET /gear/bis` (optional `?cid_hash=`).
- Core: `BisResponse`/`BisGearset` models, `IApiClient.GetBisAsync`, `ApiErrorKind.NotFound`,
  pure `BisComparer` (match by gear_index+job; rings L/R interchangeable; materia multiset).
- Plugin: `BisWindow` reads live gear → `GET /gear/bis` → renders per-slot diff; opened from the
  status window. 403→reconnect, 404/empty→"no BiS pinned" messaging.
- **BiS hover overlay**: shared `BisService` (Services/) caches targets + comparison + reverse
  item lookup; `BisTooltip` (UI/) draws a safe ImGui overlay on `UiBuilder.Draw` using
  `IGameGui.HoveredItem` (no native-tooltip manipulation, P2/P6). `BisWindow` refactored onto
  `BisService`. Cache kept warm via a throttled framework-tick refresh. Toggle: `ShowBisTooltip`.
  Native-tooltip integration intentionally deferred (operator chose overlay-first).
- **Live equipped materia**: `GameGearSource` reads the active gearset's items + materia from the
  `EquippedItems` container (live), so melding without re-saving is sent/detected; the change
  signature folds the equipped container in too. Non-equipped sets keep their saved snapshot.
- **Device-flow UX**: opens `verification_uri_complete` (RFC 8628, fallback `verification_uri`),
  auto-copies `user_code`, copy button (FontAwesome) + "Open browser again". No programmatic approve.
- **BiS overlay** now docks beside the native `ItemDetail` addon (via `IGameGui.GetAddonByName` →
  `AtkUnitBasePtr.Position/ScaledWidth`) and shows only the current gearset index
  (`GameGearSource.GetCurrentGearsetIndex`).
- **Feature B (inventory) stays deferred** to `protocol_version: 2` (see agent memory).

### Next / open
- **Waiting on the server, not on us:** nothing. Both open questions of 2026-09-12 came back. `BST` is in
  the job table since version `2026-08-22`, and `orphans[].similar[]` is now guaranteed never to name a
  row that is not in game. One hazard was handed over with the first and is theirs to decide: a client
  older than the self-detection cannot name a newly added job, still declares `scope: all`, and would
  have its row parked. Either the push grows a `named_jobs[]` list, or the plugin ships before the table
  does.
- **`pluginmaster.json` is no longer in the repo** (2026-09-13). It is build output: the release workflow
  generates it from the built manifest at tag time and attaches it to the release, and that attachment is
  the URL players give Dalamud. The committed copy was read by nobody and had sat at `0.4.0.0` through
  two releases. It is in `.gitignore` now, so a local release rehearsal does not put it back.
- **CI:** workflows run on `windows-latest` with the Dalamud distrib download. **CodeQL** is active
  (`codeql.yml`, free on the now-public repo); actions are SHA-pinned + Dependabot-managed.
- **Custom repo:** the release workflow ships `pluginmaster.json` + `latest.zip` as **release
  assets** (no push to `main`); users add `releases/latest/download/pluginmaster.json`.
- **Deferred (design only, do not build yet):** pairing-code connect path. (Inventory upload is
  implemented — opt-in, Phase 2b.)
- **Plural seams outside the reconciliation window.** Around a dozen strings still count with a bracketed
  suffix: `PushSuccess` ("{0} Gearset(s) übertragen"), `PreviewHeader`, `StatusGearsetIdentityAmbiguous`,
  `StatusGearsetIdentityPositional`, `InventorySuccess` ("Gegenstand/Gegenstände", "Bereich(en)"),
  `WeeklySuccess`, `AdvisorWhenWeeks`, `AdvisorWhenBooks`, `AdvisorNoSteps`, `BisNeedsWeeks`,
  `BisNeedsUnknown`. The reconciliation window was cleared of them in 1.1.0 by splitting each key into a
  singular and a plural and choosing in the caller; the rest of the plugin was deliberately left alone,
  because touching it there would have mixed a cosmetic sweep into a feature branch. Same fix: one key
  pair per string, both catalogues, and the caller picks. Note German has cases the suffix hides, so
  `InventorySuccess` needs "einen Gegenstand" and not just a swapped ending.

## Key facts
- Build needs **.NET 10 SDK** + local Dalamud dev libs (`%AppData%\XIVLauncher\addon\Hooks\dev`,
  Dalamud 15 → `Dalamud.NET.Sdk/15.0.0`, .NET 10 target). Don't hand-pick versions (P6).
- `dotnet format` does not accept `.slnx` yet — run it per-project.
- Commit author: **Sanaka** (`20637644+miralsoft@users.noreply.github.com`, repo-local identity);
  **never** add an AI co-author (R34).
