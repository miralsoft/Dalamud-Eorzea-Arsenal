# ADR 0003 — API client lives inside the plugin (no separate C# SDK)

- **Status:** Accepted
- **Date:** 2026-06-12

## Context

The plugin needs to call a small, stable slice of the Eorzea Arsenal API. There is no published
C# SDK, and the slice is tiny (device flow + `PUT /gear` + `GET /version`).

## Decision

Implement the API client **inside the repo**, behind the `IApiClient` interface, using
`HttpClient` and `System.Text.Json`. No external API/SDK dependency (R41).

## Consequences

- All HTTP lives in one module (`Api/ApiClient`); UI and entry point never touch `HttpClient`
  (R10). The transport is swappable and unit-tested with a scripted `HttpMessageHandler`.
- If the API later ships a generated `openapi.json`, it becomes the machine-readable source for
  this **same** slice; it does not expand what the plugin may call (R13).
