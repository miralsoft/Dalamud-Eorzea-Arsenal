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
(R41/R43). A red pipeline blocks merge/release (R32). `.github/workflows/codeql.yml` runs the
CodeQL SAST pass (R43) — it needs GitHub Advanced Security if the repository is private.

CI obtains the Dalamud assemblies by downloading the official distrib
(`https://goatcorp.github.io/dalamud-distrib/latest.zip`) into the dev path before building.

## Release (custom Dalamud repository)

Releases are **SemVer**, tag-driven. `.github/workflows/release.yml` triggers on a `v*` tag:

1. Build `-c Release` and package via DalamudPackager.
2. Attach `latest.zip` to a GitHub Release.
3. Regenerate **`pluginmaster.json`** (the custom-repo index) pointing at the release asset, and
   commit/publish it.

Users add the **raw URL of `pluginmaster.json`** under Dalamud → Settings → Experimental → Custom
Plugin Repositories (private; not the official list).

### Cutting a release

1. Bump `<Version>` in `EorzeaArsenalPlugin.csproj` and update `CHANGELOG.md`.
2. Commit (conventional commit, no AI author — R34) and tag: `git tag vX.Y.Z && git push --tags`.
3. The release workflow does the rest.

## In-game smoke test (operator)

1. Enable Dalamud **Dev Plugins** and point it at the built DLL (or the local repo).
2. `/xlplugins` → load **Eorzea Arsenal**.
3. In settings: accept the ToS notice, enable, set the base URL the API launcher printed
   (incl. `/api/v1`), **Test connection**, then connect (paste a key created via the API for
   end-to-end testing today).
4. Run `/bisexport`; confirm the push and check the gear in the web app.
