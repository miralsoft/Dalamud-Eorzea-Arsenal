# Eorzea Arsenal — Dalamud Plugin

Reads your **FFXIV gearsets across all jobs** and pushes them to your
[Eorzea Arsenal](https://github.com/miralsoft/Dalamud-Eorzea-Arsenal) account so the web app can
show *"my gear vs BiS"* for every job. The plugin's job is intentionally small: **connect once,
then push gear**.

> [!WARNING]
> **Third-party tool notice.** Dalamud and third-party tools officially **violate the FFXIV
> Terms of Service**. Using this plugin is **at your own risk**. Connecting and pushing your gear
> is strictly **opt-in** — nothing leaves your machine until you enable it and accept this notice
> in the settings window.

## What it does

- Reads every in-game gearset (all 21 combat jobs) via `RaptureGearsetModule`.
- Computes a stable, privacy-preserving character id (a salted-free SHA-256 hash of your
  ContentId — the raw ContentId is **never** sent).
- Pushes the gearsets to the API with a single `PUT /gear`. Re-pushes update in place.
- Bilingual UI (**Deutsch / English**).

Armoury/inventory is **out of scope** for now.

## Install (custom plugin repository)

This plugin is distributed through a **private/custom Dalamud repository**, not the official
plugin list.

1. In game, open Dalamud settings: `/xlsettings` → **Experimental**.
2. Under **Custom Plugin Repositories**, add the repository URL provided by the operator
   (the `pluginmaster.json` raw URL), then **Save**.
3. Open `/xlplugins`, find **Eorzea Arsenal**, and install it.

## Connect

Open the settings with `/xlplugins` → Eorzea Arsenal → settings, or the gear icon.

1. **Accept the ToS notice** and tick **Enable connecting and pushing** (master opt-in).
2. Set the **API base URL** — the full URL **including `/api/v1`**, e.g.
   `http://127.0.0.1:8080/api/v1`. Use **Test connection** to confirm.
3. Connect one of two ways:
   - **Connect via browser** (device flow): click the button, open the shown page, and approve
     the displayed code while logged in via Discord.
   - **Paste API key** (works today): create a key in the web app at `/me/keys` and paste it.

The issued key is **write-only (`gear:write`)** — it can do nothing except push gear. Use
**Disconnect** to wipe the stored key at any time.

## Usage

- Run **`/bisexport`** to push all your gearsets. `/bisexport status` opens the status window,
  `/bisexport config` the settings.
- The **status window** (Main UI button) shows the last push, its result, a rate-limit countdown,
  and quick actions: **push now**, **preview what will be sent**, **open web app**.
- Optionally enable **Push on login**, **Push automatically** and **Push on gearset change**
  (all throttled — at most one push every few minutes, only when something changed, to respect the
  API's 30 uploads/hour limit).
- **Per-character opt-in**, **toast notifications**, **log verbosity** and a **web app URL** are
  configurable in the settings window.
- **Gear vs BiS:** the status window's *Gear vs BiS* button reads your pinned BiS targets
  (`GET /gear/bis`, needs the `gear:read` scope your key now carries) and shows a per-slot diff of
  your current gear against BiS. If you connected before this existed, **reconnect** to get read
  access. No BiS shown? Pin one for your gearsets in the web app.

## Base URL note

There is **no fixed production URL yet**, and local testing uses `localhost` with a port that may
vary (the API's launcher falls back from `8080` to `8081`, …). Always set the **exact** URL the
launcher printed, including `/api/v1`. A trailing slash is trimmed automatically.

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Not connected" | Connect via browser or paste a key in settings. |
| "Your key is invalid or expired" (401) | Reconnect. |
| "lacks gear:write" (403) | Use a key with the `gear:write` scope. |
| "already linked to another account" (409) | The character belongs to a different account; this is expected and not retried. |
| "Rate limit reached" (429) | The plugin backs off automatically; try again later. |
| Test connection fails | Check the base URL (must include `/api/v1`) and that the API is running. |

Logs go to the Dalamud log (`/xllog`). The plugin **never logs your API key** or request bodies.

## Building from source

See [docs/operations/build-test-release.md](docs/operations/build-test-release.md). In short:
`dotnet build -c Release` (requires the .NET 10 SDK and a local Dalamud dev install) produces the
plugin and, via DalamudPackager, the distributable zip + manifest.

## License & attribution

Licensed under **[AGPL-3.0-or-later](LICENSE)**. This is a **non-commercial fan project**.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. All material used under the
[FFXIV Materials Usage License](https://support.na.square-enix.com/rule.php?id=5382&tag=authc).
FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd. This project is not
affiliated with or endorsed by SQUARE ENIX.
