# Architecture overview

The plugin is a **stable, encapsulated core that depends only on interfaces**, with concrete
pieces as swappable modules. New features are added as modules without touching the core
(additive, no breaking changes). This keeps the whole API layer or the gear source replaceable.

## Two assemblies

```
EorzeaArsenal.Core            (pure: no Dalamud, no game, no UI)
└── consumed by
EorzeaArsenalPlugin           (Dalamud host: game + UI + wiring)
└── tested via
EorzeaArsenal.Core.Tests      (references the core only)
```

## Module map

| Interface (Abstractions) | Responsibility | Production impl | Test impl |
|---|---|---|---|
| `IApiClient` | the only thing that talks to the API (transport + endpoints) | `Api/ApiClient` (core) | `FakeApiClient`, `StubHttpMessageHandler` |
| `IGearSource` | reads gearsets from the game | `Gear/GameGearSource` (plugin) | `FakeGearSource` |
| `ITokenStore` | stores the secret API key | `Configuration/ConfigStore` (plugin) | `InMemoryTokenStore` |
| `IApiSettings` | provides the base URL | `Configuration/ConfigStore` (plugin) | `StubHttpMessageHandler.Settings` |
| `ILocalizer` | resolves DE/EN UI strings | `Localization/Localizer` (core) | `Localizer` |
| `IClock` / `IDelayProvider` | time + waiting (for throttle/poll) | `SystemClock` / `RealDelay` | `TestClock` / `FakeDelay` |
| `ILog` | diagnostics | `PluginLogAdapter` → `IPluginLog` | `CapturingLog` / `NullLog` |

## Core orchestration

- **`ConnectionService`** — device-flow state machine (`POST /device/code` → poll
  `POST /device/token`) and the paste-key path; stores the issued key; honors the poll interval.
- **`GearSyncService`** — reads a snapshot via `IGearSource`, **sanitizes** and **validates** it,
  then pushes via `IApiClient`. Enforces:
  - **single in-flight push** with coalescing (P11) — overlapping triggers collapse into one
    pending push, latest snapshot wins;
  - **proactive throttling** (R23) — automatic triggers are spaced, unchanged data is not
    re-sent, and a 429 sets a back-off window;
  - **validate-before-send** (R18) — invalid local data is never sent.

The core contains **no** direct HTTP, game-memory or UI-framework calls. The entry point and UI
hold no domain logic (R10/R11).

## Data flow (push)

```
/bisexport | login | change | timer
        │
        ▼
GearSyncService.RequestPush(trigger)      ── coalesces into a single background loop (P11)
        │  (background thread)
        ▼
IGearSource.ReadAsync()  ── GameGearSource marshals the read onto the framework thread (P1/P4)
        │
        ▼
GearSanitizer → GearValidator (R18)  ── drop fake ids / unmapped jobs; fail closed if invalid
        │
        ▼
IApiClient.PushGearAsync(key, payload)  ── PUT /gear; maps 401/403/409/422/400/429 (R22: no bodies logged)
        │
        ▼
PushReport → PluginUI/chat (localized via ILocalizer)
```

Auto-generated API docs / UML can be produced from the XML doc comments (e.g. DocFX); the
interfaces above make the structure diagrammable.
