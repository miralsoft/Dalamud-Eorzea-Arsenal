# ADR 0005 — cid_hash derivation is fixed and test-locked

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

The server identifies and de-duplicates a character by `cid_hash` and requires it to be stable
across plugin versions/sessions (P7). The raw ContentId must never be sent (R25/R27).

## Decision

`cid_hash` = **lowercase-hex SHA-256 of the ContentId rendered as a decimal string**, no salt
(64 hex chars). Implemented in `Gear/CidHash` and locked by fixed test vectors in
`GearMappingTests` (e.g. ContentId `1234567890` →
`c775e7b757ede630cd0aa1113bd102661ab38829ca52a6422ab782862f268646`).

## Consequences

- Re-pushes map to the same character. The derivation may only change via a new ADR **and** a
  coordinated API change. The raw ContentId never leaves the machine.
