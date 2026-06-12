# ADR 0001 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

Per rule R4, significant decisions must be recorded as ADRs rather than buried in prose, so an
agent on another machine can reconstruct *why* the code looks the way it does.

## Decision

We keep numbered ADRs in `docs/decisions/`, one per significant decision, using `template.md`.
ADR numbers are stable identifiers referenced from code/docs.

## Consequences

Every significant change ships with (or updates) an ADR in the same commit (R3). The overhead is
small and the in-repo memory stays complete.
