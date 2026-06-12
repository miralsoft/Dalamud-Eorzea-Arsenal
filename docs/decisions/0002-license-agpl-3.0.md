# ADR 0002 — License: AGPL-3.0-or-later

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

Rule R35 calls for a non-commercial license plus FFXIV attribution. The operator also wants to
keep the door open to later **advertising** and possible partial commercialization.

## Decision

License the code under **AGPL-3.0-or-later**, with FFXIV attribution (© SQUARE ENIX, Materials
Usage License) in the README.

## Consequences

- The **binding** non-commercial constraint for any FFXIV fan tool is SQUARE ENIX's Materials
  Usage License, independent of the code license — so the plugin itself cannot be sold regardless.
- Advertising a free plugin is not "commercial use" and is unaffected.
- As the copyright holder, the operator retains all rights and may relicense/dual-license later;
  AGPL meanwhile prevents others from closing or commercializing the code.
- AGPL is the norm in the Dalamud/FFXIV plugin ecosystem (the official SamplePlugin uses it too).
