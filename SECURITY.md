# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via either:

- GitHub **Security → Report a vulnerability** (Private Vulnerability Reporting) on this repository, or
- email **m.tosch@miralsoft.com** with the details and steps to reproduce.

Please include what you found, how to reproduce it, and the impact you expect. You'll get an
acknowledgement as soon as possible; fixes for confirmed issues are prioritized and a coordinated
disclosure is preferred once a fix is available.

## Supported versions

Only the **latest release** is supported. Fixes ship in a new release.

## Scope

This repository contains the **Dalamud client plugin** only. Issues in the Eorzea Arsenal **web
app / API** belong to that service and should be reported through its own channel.

In scope here, for example:

- Anything that could leak the user's API key or other secrets from the plugin.
- The plugin sending more data than documented below, or to an unintended host.
- Supply-chain issues in the build/release pipeline (workflows, actions, dependencies).

## What the plugin sends (and what it never sends)

The plugin contacts the API **only after** the user enables it and accepts the in-app notice
(strictly opt-in). When enabled it may send:

- character **name** and **home world**;
- a one-way **`cid_hash`** (salt-free SHA-256 of the ContentId — the raw ContentId is **never** sent);
- an optional **Lodestone id** (public);
- the user's **gearsets** (`PUT /gear`);
- **owned, equippable items** (`POST /inventory`) — only if that separate opt-in is enabled.

It **never** sends the API key in any request body or log, **never** logs request/response bodies,
and **never** writes into the game's chat or sends messages on the user's behalf.

## Hardening in this repo

- API key is stored only in the local Dalamud config; TLS validation is never disabled.
- CI runs build + tests + format + a vulnerable-dependency scan; CodeQL scans the code weekly.
- GitHub Actions are pinned to commit SHAs and kept current by Dependabot.
- Releases are published as signed-by-GitHub release assets; the workflow needs no write access to
  protected branches.
