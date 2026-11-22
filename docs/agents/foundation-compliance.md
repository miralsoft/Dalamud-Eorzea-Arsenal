# Foundation compliance: what this repository still owes

An audit against the MIRAL Soft ruleset, run on 2026-08-24 against ruleset **3.3.0**. Twenty
deviations, none dangerous, several cheap. Nothing here is done. It is written down so it survives a
session, and so the next person can see what was checked and what was not.

Deliberately parked until the gearset identity work is finished and tested.

## Update: ruleset 5.3.0 (2026-11-22)

The foundation moved from 3.3.0 to **5.3.0** (commit `97445a6`, 2026-11-18). This pass read what changed
between the two in the global rules and the C# profile; the Dalamud profile and the blueprint are
unchanged. Two new findings, numbered on from the twenty above. Everything else that changed either
holds already or does not apply here.

**New findings**

21. **C-03 names a different check for C# now.** It has two halves: `dotnet format whitespace
    --verify-no-changes` for the layout, and the build itself, strict through `EnforceCodeStyleInBuild`
    and `TreatWarningsAsErrors`, for everything else. CI still runs the full `dotnet format
    --verify-no-changes` per project, the form the profile retired because it analyses without the
    sources the build generates and then flags a needed `using`. It passes here today only because no
    project generates sources. The switch belongs with the build strictness of section A (findings 1
    to 7), since the second half does not exist until those land. Part of the same item: `.editorconfig`
    lacks `dotnet_diagnostic.IDE0005.severity = warning` under `[*.cs]`, without which unnecessary
    `using` directives are never reported.
22. **R-19: the committing identities are declared nowhere.** A project names in its `project.md` which
    owner name applies and every further identity that authors commits. Here that is `Sanaka` with the
    repository-local address, plus Dependabot. The repository has no `project.md`.

**Changed and already held**

| Rule | What it asks | Here |
|---|---|---|
| C-15, C-16 | a generated artefact is never edited, and its reproduction is checked | `changelog.json` is generated from `ReleaseNotes.cs`; `ChangelogJsonTests` fails on any difference |
| I-13 | an assistant does not write by hand what a tool writes | `changelog.json` is regenerated with `EORZEA_UPDATE_CHANGELOG=1`, never edited |
| D-17 | an announcement reports only what a user could have seen | the release-note rule of this repository, applied since 1.2.0 |
| R-09 | no AI credited with the project's work, widened in 5.2.0 | no such line in any commit, PR or release |
| R-23 | a gate that could not run has not passed | the CI steps fail on a missing tool or input instead of skipping |
| S-16 | a development-only door does not exist in a deployment | developer surfaces compile only with `EORZEA_ARSENAL_DEVTOOLS`, and CI refuses the file that sets it |

**Not relevant here:** the O rules (operations of hosted services), the AC rules, MCP, CSS and Sass, WPF,
and the xUnit v3 notes of the C# profile (this repository is on xUnit 2.9.3; they apply on a move).
**Borderline:** the PowerShell profile, for the one script `scripts/generate-pluginmaster.ps1`.

**A correction to "What already holds" below:** since 1.3.0 one switch ships on, "Tomestones and
scrips", as agreed with the server side. It sends to the plugin's own API, never to the game server, so
the Dalamud profile's rule is untouched; the sentence below is adjusted to say so.

## What was checked, and what was not

| Document | Lines | Coverage |
|---|---|---|
| `rules/languages/csharp.md` | 137 | read in full, every mechanical setting checked against the repository |
| `rules/frameworks/dalamud.md` | 433 | read in full, every concrete claim checked |
| `blueprints/dalamud-plugin.md` | 638 | structure plus sections 10 (commands) and 15 (documents); the rest **not read line by line** |
| the global rules | not re-read this pass | only the two already known to bite: I-02 and R-22 |

The blueprint carries no numbered rules and says the rules win where they seem to differ, so it was
read last and only where it names something load-bearing. **The uncovered part of the blueprint is the
most likely place for a twenty-first finding.**

The foundation is a separate repository at `BIS-Searcher/.foundation-docs`. It is read, never written.
Where something there needs changing, it goes into a prompt for its agent.

## A. Build strictness (C# profile)

The largest cluster and the one that hides the others: a warning that does not break the build runs
along for years. It already did today. Adding a fourth `ReleaseNoteKind` produced two CS8524 warnings
and a green build.

1. **`Directory.Build.props` carries none of the shared settings.** It only imports the developer-tools
   file. The profile puts them in exactly this one place, "covering every project", and names the defect
   it prevents: two projects built to different strictness, invisible until a warning appears in only
   one of them. Today they are scattered over three `.csproj` files and incomplete in all three.
2. **`TreatWarningsAsErrors` is set nowhere.** Required outright.
3. **`EnableNETAnalyzers` only in the core project**, `EnforceCodeStyleInBuild` nowhere, and
   `AnalysisLevel` is `latest` in the core, which is below the required `latest-recommended` floor, and
   unset in the other two.
4. **`GenerateDocumentationFile` is missing in the test project**, and `CS1591` is not suppressed
   anywhere, although the profile prescribes that pairing (without the documentation file, IDE0005
   never runs; in return CS1591 is turned off).
5. **`RestorePackagesWithLockFile` is set nowhere.** Only the plugin project has a `packages.lock.json`,
   presumably from the Dalamud SDK. The core and the test project are unlocked.
6. **No `NuGet.config`.** The profile requires one that clears inherited feeds and allows
   `https://api.nuget.org` only, so a machine-wide feed cannot be pulled from silently.
7. **CI restores without `--locked-mode`** (`.github/workflows/ci.yml:39`).

**Measured cost of complying**, with the required settings switched on temporarily: **916 warnings**.

| Rule | Count | Nature |
|---|---|---|
| CS1591 | 462 | disappears with the suppression the profile itself prescribes |
| CA1707 | 334 | identifiers with underscores, needs one look to decide suppress or rename |
| CA1305 | 100 | mostly `StringBuilder.Append` without a culture, in diagnostic dump code |
| CA1822, CA1859, CA1826 | 20 | small, real |

## B. The release chain

8. **`release.yml` does not check the tag against the built version.** The Dalamud profile requires the
   workflow to refuse when they disagree. Today it takes the version from the tag
   (`.github/workflows/release.yml:49`) and hands it to the index generator, so a tag `v1.2.0` on a
   commit whose `<Version>` says `1.1.0` publishes an archive whose manifest says 1.1.0 under an index
   entry promising 1.2.0.
9. **`CHANGELOG.md` has no 1.1.0 section and no `[Unreleased]`.** Its newest section is 1.0.0, so every
   change on `feat/stable-gearset-identity` is unrecorded for contributors. The release procedure in
   `docs/operations/build-test-release.md` requires it in the same pull request as the work.
10. **No test that `CHANGELOG.md` and the embedded release notes agree on which versions exist.** The
    blueprint calls it load-bearing, and it is the test that would have caught finding 9.

## C. How the plugin arrives at a player

11. **No icon ships in the archive.** `latest.zip` holds seven files and no image. Dalamud resolves the
    icon along two paths: the list of available plugins downloads `IconUrl` from the manifest, and an
    **installed** plugin reads the image out of its own directory. We feed the first only, so an
    installed copy shows Dalamud's default icon. The profile names this exact failure. The image must
    also be exactly 512 by 512, or it is dropped rather than scaled, and nothing reports it.
12. **Nothing switchable is reachable from a text command.** Profile and blueprint both call this
    load-bearing: a switch that cannot be reached from a macro is not reachable by the people who
    automate their interface. `/xivarsenal` has nine subcommands, all of which open a window or start a
    probe, and none of which turns anything on or off.
13. **No short command form is registered.** The blueprint wants the long form plus a short one that is
    allowed to fail, because another plugin or the game may already own it, with a failed registration
    logged rather than treated as an error, and teardown removing only what was actually claimed.

## D. Crash safety and teardown

14. **The audit the profile requires before every handover has never been run.** It covers every
    `unsafe` block, every pointer dereference, every pinning block, and every game call reachable from
    an interface callback, and its outcome is recorded in the project's status document. The surface:
    **38 `unsafe` occurrences across five files, 155 pointer dereferences.** `docs/agents/project-state.md`
    says nothing about it. The profile also says how: grep is where such a pass begins and not where it
    ends, start at the draw callback, and write down what was searched for so the next reader can see
    what the pass cannot have covered.
15. **Teardown order is wrong.** Required: the reverse of registration, with the per-frame callbacks
    detached **first**. `Plugin.Dispose` removes the command handler and the login handler before
    `Framework.Update` and `UiBuilder.Draw`. The reason the rule gives is a hot reload while the window
    is open, which is exactly how this plugin is developed.
16. **Per-frame cost has never been measured** with Dalamud's own plugin statistics window, which the
    profile requires before a release rather than an estimate.

## E. Two decisions nobody wrote down

17. **Opening the Duty Finder and the Raid Finder invisibly.** The profile hangs operating a game window
    higher than sending an action, names dialog boxes explicitly, and requires that where a project
    crosses that line anyway, both sides of the weighing go into a decision record, not only the side
    that won. None of the five ADRs mentions it. The behaviour itself is compliant on the point that
    matters most: `SyncWeekly` defaults to `false`, and automation that reaches the game server must
    ship off.
18. **ADR 0005 does not answer the identifier rule.** The profile requires identifiers that are
    "pseudo-random and resettable, never derived from personal or account data". `cid_hash` is derived
    from account data by design, and the neighbouring bullet ("hash anything sensitive on the client
    before it leaves") describes what we do. The position is defensible and unwritten, which is the
    problem.

## F. Documents

19. **`README.md` has no section on what the plugin does not do**, which the blueprint calls
    load-bearing for the page somebody reads before installing.
20. **I-02 beyond the shipped strings.** The cleanup on 2026-08-23 covered the two language catalogues
    and the player-visible literals in the plugin project. The README headline and roughly 582
    occurrences in source comments were left out by a stated boundary, and diagnostic and log output
    deliberately so. The API repository enforces I-02 with `tools/ci/check-em-dash.sh` over configured
    globs; this repository has no equivalent, only the two unit tests over the catalogues.

## What already holds

Worth stating so the list above is not read as a verdict on the whole plugin. **Every automatic
behaviour that reaches the game server ships off**: every switch that does defaults to `false`, which is the
single most consequential rule in the Dalamud profile. Windows go through the windowing system. The
host's own entry points are subscribed and unsubscribed. Developer surfaces are absent from a released
build, with a CI guard against committing the switch. There is no dynamic code execution. The
`cid_hash` is hashed on the client before it leaves. The version lives in one place and a test checks
the release notes against it. `pluginmaster.json` is an array. The vulnerability scan runs with
`--include-transitive`. The format gate passes on a fresh clone.

## Suggested order

1. **The icon** (finding 11). One file, and without it every installed copy shows a stranger's picture.
2. **The tag guard and the changelog section** (8, 9, 10), because both belong in front of a release
   rather than behind it.
3. **Build strictness** (1 to 7) in one pass, with the measured cleanup behind it.
4. **The crash-safety audit** (14). The real work, and the only item where the time it may take should
   be agreed before it starts.
5. The rest (12, 13, 15, 16, 17, 18, 19, 20), ten minutes each.
