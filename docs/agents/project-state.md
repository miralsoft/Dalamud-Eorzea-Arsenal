# Project state (living)

> The complete in-repo memory so an AI/contributor on another machine can continue without losing
> context (R3). Keep this current **in the same commit** as the change it describes.

_Last updated: 2026-08-17._

## Status: 1.0.0 released and in-game verified

`v1.0.0` was tagged on `10fc8cc` and published on 2026-08-03. The release workflow built, packaged,
regenerated `pluginmaster.json` and published the GitHub Release; the operator confirmed the build
works in game.

The sections below are the running history, oldest first. **Start with "Where things stand" at the
bottom** if you only need the current picture.

## History

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

### Released since (see `CHANGELOG.md` for the full text)
- **0.3.0** — weekly checklist (Unreal, Wondrous Tails, normal/alliance fields, hidden Duty-Finder
  refresh).
- **0.4.0** (2026-07-26) — teams companion (calendar, mit plans, resources, FFLogs, absences,
  notifications with toast + clickable chat link) and the purchase advisor (server-computed
  recommendation, editable own plan, sourcing, holdings).
- **1.0.0** (2026-08-03) — report a problem from in game (`POST /contact`, four topics built from
  `GET /contact`, bug button in every window's title bar reporting *which* window it came from, game
  / Dalamud / plugin versions as separate fields); generated `changelog.json` consumed by the website
  and Discord; API keys held per address with no fallback to another address's key. Fixed: the
  saddlebag and retainer scopes were declared empty when nobody had opened them, so the server
  deleted the stored stock — **finding something is now the only evidence of having looked**, which
  is the rule to keep in mind for any future storage that is only readable sometimes.

## Where things stand

### The plugin family and the one address
Plugins are installed from **one** Dalamud repository address:

```
https://xivarsenal.app/plugin.json
```

The website serves that route. What it serves is being moved to a dedicated index repository,
**`miralsoft/Dalamud-Plugins`**, which builds `pluginmaster.json` from a list of source repositories:
for each it reads the *newest release*, takes the manifest out of the released zip and pins the
download link to that exact tag. Adding a plugin is one line in its `plugins.json`; the source
repository needs no workflow and no knowledge that the index exists. Its `docs/` explains the rest.

Two rules there are load-bearing and look like sloppiness if you do not know why: links are pinned to
a **tag** (never `/releases/latest/`, which means "newest release in the whole repository" and breaks
as soon as a repository holds more than one plugin), and a repository that cannot be reached **keeps
its previous entry** rather than being dropped — the same "not observed is not gone" rule as the
saddlebag fix.

### Open, in rough order of readiness
- **Website:** point `/plugin.json` at the index repository, and add `/plugins.json` as a permanent
  second name. Briefing: [`prompts/website-plugin-json-source.md`](prompts/website-plugin-json-source.md).
  Until then everything keeps working unchanged — the old source is still correct.
- **This repository's `README.md` still advertises the old GitHub address**
  (`releases/latest/download/pluginmaster.json`). It should become the domain address, so there is
  one link rather than two. Offered to the operator, not yet decided.
- **`dev.xivarsenal.app`** — a test environment on the server side. Nothing to prepare here: keys are
  already held per address and deliberately do not fall back to another address's key. Once the
  address exists, that separation is what to test.
- **Gearset switcher** — a *separate* plugin, not started. It needs no account and no server, which
  is precisely why it is not folded in here. Later, Arsenal is to offer it a small IPC gate
  (`EorzeaArsenal.GearsetBis.V1`, `Func<uint, string?>` returning a short JSON object) so it can show
  how far a gearset is from BiS. **That contract is Arsenal-side work and is not built yet**; agree
  it with the other side before either party implements it. Briefing and full reasoning:
  [`prompts/gearset-plugin-briefing.md`](prompts/gearset-plugin-briefing.md).

### Notes that still apply
- **CI:** workflows run on `windows-latest` with the Dalamud distrib download. **CodeQL** is active
  (`codeql.yml`); actions are SHA-pinned + Dependabot-managed. CI on the PR is the quality gate —
  the release workflow does not run tests.
- **Release assets:** the release workflow ships `pluginmaster.json` + `latest.zip` as release assets
  and never pushes to `main` (which is why `main` can stay fully branch-protected). The
  `pluginmaster.json` committed in this repository therefore lags behind on purpose — the one users
  receive is the release asset, and soon the index repository's file.
- **Deferred (design only, do not build):** pairing-code connect path.
- `dalamud_version` in reports comes from `IDalamudPluginInterface.GetDalamudVersion()`; `ScmVersion`
  is preferred over the bare version because it is the `git describe` output Dalamud shows about
  itself, and it says "Local Build" for a self-compiled one.

## Key facts
- Build needs **.NET 10 SDK** + local Dalamud dev libs (`%AppData%\XIVLauncher\addon\Hooks\dev`,
  Dalamud 15 → `Dalamud.NET.Sdk/15.0.0`, .NET 10 target). Don't hand-pick versions (P6).
- `dotnet format` does not accept `.slnx` yet — run it per-project.
- Commit author: **Sanaka** (`20637644+miralsoft@users.noreply.github.com`, repo-local identity);
  **never** add an AI co-author (R34).
