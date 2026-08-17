# Briefings for the other agents

The plugin family is worked on by several agents, each owning one repository or system. These are the
briefings handed to them, kept here so the reasoning behind an arrangement survives the conversation
it was agreed in — and so a briefing can be handed out again without being reconstructed from memory.

They are written **in German**, because that is the language the operator works in.

| File | Handed to | Purpose |
| --- | --- | --- |
| [`plugin-index-repo-agent.md`](plugin-index-repo-agent.md) | The agent maintaining `miralsoft/Dalamud-Plugins` | Onboarding for the plugin index: what it is, what to maintain, what must never be broken. |
| [`website-plugin-json-source.md`](website-plugin-json-source.md) | The agent maintaining the web app | Point `/plugin.json` at the index, and add `/plugins.json` as a permanent second name. |
| [`gearset-plugin-briefing.md`](gearset-plugin-briefing.md) | A new agent, for a plugin not yet started | The gearset switcher: the problem, why it stays a separate plugin, and the IPC contract Arsenal will offer it later. |

## Why they live in this repository

None of them is about Eorzea Arsenal itself. They are here because this is the repository the
operator opens — so this is where a document is found again. See
[`../project-state.md`](../project-state.md) for how these pieces fit together.

## Keeping them honest

A briefing that describes a state of affairs that has moved on is worse than none, because it is
believed. When one of these arrangements changes, change the briefing in the same commit. Two of them
carry a claim with a date on it — that `plugin.json` is the documented address, and that the gearset
plugin has not been started — and those are exactly the sentences that rot first.
