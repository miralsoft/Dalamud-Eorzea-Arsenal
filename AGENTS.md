# AGENTS.md — read-first orientation

This file orients any AI agent or contributor before touching the repo. Read it, then read
`docs/agents/project-state.md` (living status) and `docs/agents/handoff.md` (read order).

## What this is

A **Dalamud plugin (C# / .NET 10)** for FFXIV that reads gearsets across all jobs and pushes
them to the **Eorzea Arsenal API**. Surface is intentionally tiny: **connect once, then push
gear**. The API is the fixed contract — we implement against it, we do not change it.

## Architecture (do not break)

Two projects:

- **`src/EorzeaArsenal.Core`** — pure domain core. **No Dalamud, no game, no UI.** Holds the
  abstractions (`IApiClient`, `IGearSource`, `ITokenStore`, `IApiSettings`, `ILocalizer`,
  `IClock`, `IDelayProvider`, `ILog`), the API client, device-flow + sync services, gear
  mapping, `cid_hash`, validation and localization. This is what makes everything unit-testable
  without the game or network.
- **`src/EorzeaArsenalPlugin`** — the Dalamud host (`Dalamud.NET.Sdk`). Thin entry point, the
  game gear source (`RaptureGearsetModule`), Dalamud-backed config/token store, and the ImGui UI.

`tests/EorzeaArsenal.Core.Tests` references **only** the core.

New features are added as **modules behind interfaces** — never by adding logic to the entry
point or UI, and never by calling `HttpClient` outside the API module.

## Hard rules (the important ones)

- **R5/R6** — code, comments, logs, docs in **English**; UI strings bilingual **DE/EN** via
  `ILocalizer` (never hardcode UI text).
- **R34** — **no AI authorship.** Commits are authored by the human only; never add a
  `Co-Authored-By` AI line. Author: `Michael Tosch`.
- **R33** — conventional commits.
- **R19/R20/R22** — the API key is a secret: only in local config, **never logged or committed**;
  never log `/device/token` or `/gear` bodies. Log status + `request_id` only.
- **R13/R17** — use only the documented device-flow + `PUT /gear` + `GET /version` slice with the
  `gear:write` key. No other endpoints.
- **R14** — send `protocol_version`; ignore unknown response fields.
- **R18/R23** — validate before sending; honor the 30/h limit and poll interval proactively.
- **R3/R4/R7** — keep `docs/agents/project-state.md` and docs current **in the same commit**;
  record decisions as ADRs in `docs/decisions/`; docs live under `docs/` (except the root
  `README`, `CHANGELOG`, `LICENSE`, `AGENTS.md`).
- **P1–P12** — Dalamud safety: framework-thread reads, HTTP off-thread, never crash the game,
  clean `IDisposable`, one shared `HttpClient`, TLS on, stable `cid_hash`, base URL never
  hardcoded, single in-flight push, versioned/migrated config.

The full rule text lives in the operator's briefing; the essentials above are binding here.

## Build / test / format

```
dotnet build -c Release            # needs .NET 10 SDK + local Dalamud dev install
dotnet test                        # core unit tests (no game needed)
dotnet format --verify-no-changes  # style gate (run per-project; .slnx not yet supported)
```

In-game testing is the **operator's** step (the agent cannot launch FFXIV).

## License

AGPL-3.0-or-later; non-commercial fan project; FFXIV © SQUARE ENIX (see `README.md`).
