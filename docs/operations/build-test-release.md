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

> **Nothing is merged into `main` that has not first been brought together in a release branch.** A
> version is assembled in `release/vX.Y.Z`; `main` receives it once, whole, through a single PR. This
> rule dates from 2026-09-27: 1.1.0 was merged straight into `main`, and although that matched every
> release before it, the maintainer wants `main` to carry only what was finished and combined first.
>
> **The release preparation still travels with the work**, now inside that release branch. A separate
> "prepare the release" PR afterwards is a second review round for the same action, and two branches that
> each pass alone can fail once combined: a date change in `ReleaseNotes.cs` invalidates the generated
> `changelog.json`, which is exactly what happened on 2026-07-27.

1. At the start of a version, branch `release/vX.Y.Z` off `main` and push it. Minor for new features,
   patch for fixes only; a branch is cheap to rename if the content turns out otherwise.
2. Feature and fix branches open their PRs **against the release branch**, never against `main`.
3. In the release branch, alongside the work: bump `<Version>` in `EorzeaArsenalPlugin.csproj`, keep the
   version's section in `CHANGELOG.md` current, and add the user-facing lines to `ReleaseNotes.cs`, each
   written as **`Headline: detail`** and with a **new, hand-written, kebab-case `Id`** (see below).
   Regenerate the public changelog:
   ```bash
   EORZEA_UPDATE_CHANGELOG=1 dotnet test --filter FullyQualifiedName~ChangelogJsonTests
   ```
4. Verify from clean, because an incremental build hides warnings, and judge the format gate from a
   **fresh clone** rather than the working tree, where it reports line-ending failures that do not exist:
   ```bash
   dotnet clean -c Release && dotnet build -c Release && dotnet test && dotnet format --verify-no-changes
   ```
5. The maintainer tests the release branch in game.
6. Open **one** PR `release/vX.Y.Z` → `main`. **CI is the quality gate**: the release workflow only builds
   and packages, it does not test, so a tag must only ever sit on a commit CI has passed, i.e. on merged
   `main`. The maintainer merges.
7. Tag the merge commit and push it, **only when asked**:
   ```bash
   git checkout main && git pull
   git tag -a vX.Y.Z -m "vX.Y.Z - what it is" && git push origin vX.Y.Z
   ```
   Never tag unasked, and never before the merge: the tag has to point at the commit that is actually
   on `main`, and that commit does not exist until the PR is merged.
8. The release workflow does the rest.

A fix for a version already out branches `release/vX.Y.Z+1` from **that version's tag**, not from a
release branch that has moved on, so the fix does not ship half of the next version with it.

**A gap in CI worth knowing about.** `ci.yml` runs on pushes to `main` and on every pull request;
`codeql.yml` on pushes to `main` and on pull requests **into `main`**. So a feature PR into a release
branch is built and tested, but the release branch itself is not checked after that PR merges, and
CodeQL does not look at it until the final PR into `main`. Two feature PRs that are each green can
therefore combine into a red release branch unnoticed, which is the 2026-07-27 failure in a new place.
Adding `release/**` to the `push` trigger of `ci.yml` would close it; that is a decision about CI, not
taken here.

### `changelog.json` — the public feed

The web side polls `changelog.json` from the repo root and announces new entries on its `/neu` page
and in Discord. It is **generated** from `ReleaseNotes.cs`, so the same sentence reaches the in-game
"what's new", the site and Discord without three copies drifting apart. A test fails when the
committed file is stale.

Three rules it depends on:

- **Write every line as `Headline: detail`.** The generator splits on the first `": "` within
  `ChangelogJson.HeadlineLimit` (80) characters: what precedes it becomes the title on the site, the
  rest becomes the paragraph. A line without that split becomes *its own title* — the whole text lands
  where a headline belongs. That is how 24 of the titles shipped up to 1.0.0 ended up over 100
  characters long. `ChangelogJsonTests.NewNotesCarryAHeadlineTheSiteCanUse` enforces it and names the
  offending line. The releases up to 1.0.0 are listed as frozen there by the operator's decision of
  2026-08-20: they are what players already read, and rewriting them would reach nobody (see the next
  rule). **Never add a version to that list** — that would switch the rule off rather than satisfy it.
- **An `Id` is permanent — and it is spent.** Confirmed with the web side on 2026-08-20: neither
  channel looks at the text. Discord keeps a list of posted `source:id` pairs and skips anything in it;
  the site's bell only records the last notified *version* and ignores plugin entries entirely. So
  **editing the text of a published entry announces nothing** — it is safe, and it is silent. Correct a
  typo, a dead link or a renamed menu path that way; anything a player needs to *know or do* needs a
  new entry with a new id instead. **Renaming an id is the one thing that must never happen**: the
  bookkeeping reads it as a new entry, so the same message goes out a second time, and every link to
  `/neu#<old-id>` breaks. Write ids by hand — never derive them from the text, or fixing a typo would
  mint a new entry. Ids are namespaced by source (`plugin:<our-id>`), so they cannot collide with the
  website's own. (The pre-0.5.0 ids were slugged once from their English text and are frozen.)
- **It is stamped in the release commit, not by a workflow.** `main` is branch-protected and nothing
  pushes to it, so the file is regenerated locally in step 2 and travels with the release PR.

## In-game smoke test (operator)

1. Enable Dalamud **Dev Plugins** and point it at the built DLL (or the local repo).
2. `/xlplugins` → load **Eorzea Arsenal**.
3. In settings: accept the ToS notice, enable, set the base URL the API launcher printed
   (incl. `/api/v1`), **Test connection**, then connect (paste a key created via the API for
   end-to-end testing today).
4. Run `/xivarsenal`; confirm the push and check the gear in the web app.
