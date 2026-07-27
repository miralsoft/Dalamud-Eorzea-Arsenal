# Build, test & release

## Prerequisites

- **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10`).
- A local **Dalamud dev install** for the assembly references. With XIVLauncher installed these
  live at `%AppData%\XIVLauncher\addon\Hooks\dev`. The `Dalamud.NET.Sdk` finds them there, or via
  the `DALAMUD_HOME` environment variable. **Do not hand-pick** the .NET target or Dalamud API
  level — they are inherited from `Dalamud.NET.Sdk/15.0.0` (P6).

## Build & test

```powershell
dotnet restore
dotnet build -c Release
dotnet test                       # core unit tests; no game required
```

The plugin build runs **DalamudPackager**, producing
`src/EorzeaArsenalPlugin/bin/Release/EorzeaArsenalPlugin/latest.zip` plus the manifest.

## Format (style gate)

```powershell
# .slnx is not yet supported by `dotnet format`; run it per project.
dotnet format src/EorzeaArsenal.Core/EorzeaArsenal.Core.csproj --verify-no-changes
dotnet format src/EorzeaArsenalPlugin/EorzeaArsenalPlugin.csproj --verify-no-changes
dotnet format tests/EorzeaArsenal.Core.Tests/EorzeaArsenal.Core.Tests.csproj --verify-no-changes
```

Drop `--verify-no-changes` to apply fixes. CRLF line endings are enforced by `.editorconfig`.

## Continuous integration

`.github/workflows/ci.yml` runs on every push/PR (Windows runner): restore, `build -c Release`,
`test`, `format --verify-no-changes` (per project), and `dotnet list package --vulnerable`
(R41/R43). A red pipeline blocks merge/release (R32). **CodeQL SAST** runs in
`.github/workflows/codeql.yml` (C# + workflow analysis) — free now that the repo is public.
All actions are **pinned to commit SHAs** and kept current by Dependabot
(`.github/dependabot.yml`).

CI obtains the Dalamud assemblies by downloading the official distrib
(`https://goatcorp.github.io/dalamud-distrib/latest.zip`) into the dev path before building.

## Release (custom Dalamud repository)

Releases are **SemVer**, tag-driven. `.github/workflows/release.yml` triggers on a `v*` tag:

1. Build `-c Release` and package via DalamudPackager.
2. Regenerate **`pluginmaster.json`** (the custom-repo index) pointing at the stable
   `releases/latest/download/latest.zip` redirect.
3. Attach **both** `latest.zip` and `pluginmaster.json` to a GitHub Release. The workflow does **not**
   push to `main` (so `main` can be fully branch-protected).

Users add this stable URL under Dalamud → Settings → Experimental → Custom Plugin Repositories
(not the official list):
`https://github.com/<owner>/<repo>/releases/latest/download/pluginmaster.json`.

### Cutting a release

> **The release preparation belongs in the same PR as the work it ships.** `main` is branch-protected,
> so every change needs a PR anyway — and a separate "prepare the release" PR afterwards is a second
> review round for the same action. Worse, two open branches that each pass alone can fail once merged
> (a date change in `ReleaseNotes.cs` invalidates the generated `changelog.json`). One PR per release.

1. In the feature branch, alongside the work: bump `<Version>` in `EorzeaArsenalPlugin.csproj`, turn
   `[Unreleased]` in `CHANGELOG.md` into the new dated version section.
2. Add the user-facing lines to `ReleaseNotes.cs` (each with a **new, hand-written, kebab-case `Id`**
   — see below) and regenerate the public changelog:
   ```bash
   EORZEA_UPDATE_CHANGELOG=1 dotnet test --filter FullyQualifiedName~ChangelogJsonTests
   ```
3. Verify from clean — an incremental build hides warnings:
   ```bash
   dotnet clean -c Release && dotnet build -c Release && dotnet test && dotnet format --verify-no-changes
   ```
4. Open the PR. **CI is the quality gate** — the release workflow only builds and packages, it does not
   test. A tag must therefore only ever sit on a commit CI has already passed, i.e. on merged `main`.
5. The maintainer tests in game and merges.
6. Tag the merge commit and push it:
   ```bash
   git checkout main && git pull
   git tag -a vX.Y.Z -m "vX.Y.Z - what it is" && git push origin vX.Y.Z
   ```
   Never tag unasked, and never before the merge: the tag has to point at the commit that is actually
   on `main`, and that commit does not exist until the PR is merged.
7. The release workflow does the rest.

### `changelog.json` — the public feed

The web side polls `changelog.json` from the repo root and announces new entries on its `/neu` page
and in Discord. It is **generated** from `ReleaseNotes.cs`, so the same sentence reaches the in-game
"what's new", the site and Discord without three copies drifting apart. A test fails when the
committed file is stale.

Two rules it depends on:

- **An `Id` is permanent.** It is what the web side remembers as "already announced". Changing one
  re-announces the entry; reusing one silently swallows it. Write ids by hand — never derive them from
  the text, or fixing a typo would mint a new entry. (The pre-0.5.0 ids were slugged once from their
  English text and are frozen.)
- **It is stamped in the release commit, not by a workflow.** `main` is branch-protected and nothing
  pushes to it, so the file is regenerated locally in step 2 and travels with the release PR.

## In-game smoke test (operator)

1. Enable Dalamud **Dev Plugins** and point it at the built DLL (or the local repo).
2. `/xlplugins` → load **Eorzea Arsenal**.
3. In settings: accept the ToS notice, enable, set the base URL the API launcher printed
   (incl. `/api/v1`), **Test connection**, then connect (paste a key created via the API for
   end-to-end testing today).
4. Run `/xivarsenal`; confirm the push and check the gear in the web app.
