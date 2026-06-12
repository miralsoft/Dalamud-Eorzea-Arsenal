# Handoff — onboarding & read order

New agent or contributor? Read in this order:

1. **`AGENTS.md`** (repo root) — what this is, the architecture you must not break, the hard rules.
2. **`docs/agents/project-state.md`** — what's built, what's next, key facts.
3. **`docs/architecture/overview.md`** — the module map and data flow.
4. **`docs/decisions/`** — ADRs for *why* (license, core/host split, API client, cid_hash).
5. **`docs/operations/build-test-release.md`** — how to build, test, format, and release.

## Orientation

- The fixed API contract is the device-flow + `PUT /gear` + `GET /version` slice. Do not call or
  add other endpoints (R13); the `gear:write` key cannot reach them anyway.
- Put new logic in the **core behind an interface**, not in the entry point or UI (R10/R11).
- Anything user-facing goes through `ILocalizer` with DE + EN (R6). Code/docs stay English (R5).
- Never log the API key or request/response bodies (R22). Keep TLS on (P8).
- Update `project-state.md` and any affected docs/ADRs **in the same commit** (R3/R4).

## What you cannot do here

Launch FFXIV. In-game testing (loading the dev plugin, `/bisexport`, verifying the gear read) is
the **operator's** step. Provide clear steps; do not claim in-game behavior you have not verified.
