# ADR 0004 — Split into a pure core and a Dalamud host project

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

The acceptance criteria require the API client, gear mapping and `cid_hash` to be unit-testable
**without the game or network**. A single project built on `Dalamud.NET.Sdk` would force the test
project to drag in Dalamud/game assemblies.

## Decision

Two projects: **`EorzeaArsenal.Core`** (`Microsoft.NET.Sdk`, `net10.0`, BCL-only, no Dalamud) for
all domain logic, and **`EorzeaArsenalPlugin`** (`Dalamud.NET.Sdk`) for game/UI/wiring only. Tests
reference the core alone.

## Consequences

- Fast, game-free unit tests; the core is portable and the API layer / gear source can be
  replaced without touching it (R8/R9). This refines the briefing's single-`src/` suggestion,
  which was explicitly a suggestion.
- Game-touching code (the `RaptureGearsetModule` reader) is the only part that requires in-game
  verification by the operator.
