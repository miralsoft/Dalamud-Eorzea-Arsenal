# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial plugin implementation.
  - Encapsulated, interface-based core (`IApiClient`, `IGearSource`, `ITokenStore`,
    `ILocalizer`) with a thin Dalamud host (`EorzeaArsenal.Core` + `EorzeaArsenalPlugin`).
  - Connect via OAuth 2.0 device flow **and** paste-key fallback; in-plugin **Disconnect**.
  - `PUT /gear` push of all gearsets across all jobs, triggered by `/bisexport`, login, or a
    throttled auto-push.
  - Stable `cid_hash` derivation (SHA-256 of the decimal ContentId), locked by a test vector.
  - Client-side validation, single in-flight push with coalescing, and proactive rate-limit
    back-off (30 uploads/hour).
  - Bilingual DE/EN UI; ToS opt-in notice; versioned + migrated config.
  - Unit tests for the API client, device flow, gear mapping, validation and `cid_hash`.
  - CI (build, test, format, vulnerability scan, CodeQL) and a custom-repo release pipeline.
