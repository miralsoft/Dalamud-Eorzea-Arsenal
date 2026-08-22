# Reconciliation: the agreed state, the reasoning, and the six things still open

**For the API agent. Not a task.** This is the consolidated record after three rounds of review, so the
concept can be checked in one place rather than across a chat log. Read it as: *"this is what we both
believe we agreed — confirm it, or say what you want changed."* Once the open items at the end are
answered, this becomes the contract on the plugin side and implementation begins.

It supersedes [`gearset-reconciliation-review.md`](gearset-reconciliation-review.md), which was the
question. The API-side documents that answered it are `contract-gearset-identity.md` and
`handover-gearset-identity.md`.

**How to read it.** Sections 1–10 are what is settled, with the reasoning inline where the reasoning is
the load-bearing part. Section 11 has the wire shapes. Section 12 lists every decision together with the
alternative that lost and why — that is the section to read if something below looks arbitrary. Section 13
walks thirty concrete scenarios end to end; it is the most useful section for finding a gap, and one of
the open items was found by writing it. Section 14 is what is still open.

---

## 1. The one sentence the whole design rests on

**A write can never destroy anything.**

Pushes happen automatically — on login, on a gearset change, on a timer — so no automatic action may
carry a deletion right. Everything that removes or re-attaches goes through one endpoint that a person
drives, one decision at a time. `complete: true` was dropped before it was built, and nothing replaces it.

Every other decision in this document follows from that sentence or from one of two others:

- **Identity is minted where all writing paths meet**, which is the server, because a plugin-minted id
  lives in one installation and there are two computers, a web editor, and reinstalls.
- **Where the answer is genuinely unknowable, ask the human; where it is knowable, never ask.** A dialog
  about something nobody cares about teaches people to dismiss dialogs, including the ones that matter.

## 2. Vocabulary, for quick reference

**Row states** — what a stored gearset row is:

| state | meaning |
|---|---|
| `active` | reported by the last sync that could have reported it |
| `held` | written, waiting for an attribution decision |
| `parked` | not reported by the last sync that could have reported it — an orphan |
| `ignored` | parked, not asked about again, still a candidate for future suggestions |

**Push outcomes** — what happened to a gearset the plugin just sent:

| `state` | meaning |
|---|---|
| `resolved` | recognised as an existing row |
| `new` | genuinely new; a uid was minted |
| `held` | could not be attributed; a question was raised |

**Ladder rungs** — how a `resolved` set was recognised (`matched_by`):

| rung | condition | certainty |
|---|---|---|
| `exact` | same job, same name, same position | certain |
| `name` | same job, same name, unique on both sides | certain |
| `items` | same job, identical items, unique | certain |
| `index` | same job, same position, nothing better left | **a guess, within one job** |
| `name_ambiguous` | same job and name, several candidates, paired by position | **a guess, within one job** |
| `new` | nothing matched and no unclaimed compatible row | certain |

**Decision verbs**: `link`, `new`, `ignore`, `delete`, `release`. Closed set; grows only with a version.

## 3. Settled: identity

| | |
|---|---|
| Who mints a `set_uid` | The server, always. 32 hex characters from `RANDOM_BYTES`, opaque, minted once, never re-assigned |
| What it is derived from | **Nothing.** Not the name, not the equipment. Content only ever decides which existing row a pushed set *is*; it never decides what the row is called |
| `gear_index` | Display order only. Still sent on every push, never an identity again |
| Push response | `sets[]`, **one entry per sent gearset**, in the order sent, always complete. Matched up by `gear_index`, which is unique within a push — **not** by position in the array |
| Partial answers | Cannot happen: if one set fails validation the whole push fails |
| Reading without writing | `GET /gear/sets` stays, flat, read-only, one row per gearset across every character the key can see |
| `set_uid` also appears on | `GET /gear/bis`, `GET /characters/{id}/gearsets` |
| Old clients | The ladder runs for everyone. Nothing prunes for anybody, ever |
| Fresh installation | Attaches everything by itself — no questions, no duplicates, because the identity never lived in the plugin |

The plugin **detects** identity support rather than assuming it: a response carrying `set_uid` means the
new server, its absence means the old one, and the old `(gear_index, job)` path keeps working. That
detection is permanent, not transitional — two machines can talk to a live and a test instance on the
same day.

## 4. Settled: what a push does, per gearset

| outcome | `state` | the server | the player sees |
|---|---|---|---|
| recognised | `resolved` | writes the gear | nothing |
| genuinely new, no candidate | `new` | mints a uid, writes the gear | nothing |
| cannot attribute | `held` | mints a uid, **writes the gear**, marks the row, offers candidates | one question |

Two properties of this table are load-bearing and easy to lose:

**Ambiguity raises a question; unfamiliarity does not.** If "the server does not recognise this" were
enough to hold a set, creating a gearset in game would not sync until somebody clicked something. That
would be worse than the behaviour we are replacing.

**A held row is written like any other.** A player who leaves the question for a week has current values
with a marker, not week-old values. This was the API side's own argument in an earlier round and it
applies here unchanged.

## 5. Settled: the ladder guesses within a job, and holds across nothing else

This moved the most during review, and the reasoning matters more than the conclusion.

The bug that started all of this was **cross-job**: a tank set compared against a healer's target,
plausible numbers, completely false. What the guessing rungs can get wrong is a **different target within
the same job** — the comparison runs against FRU instead of Savage. The slots line up, the numbers mean
something, the target's name is on screen, and a player who wants a different target has to act either
way.

That is not the same severity. It is a different order of magnitude, and the cost of the alternative is
real: asking about it produces a dialog nobody cares about, which is how people learn to dismiss dialogs.

So:

| rung group | behaviour |
|---|---|
| `exact`, `name`, `items` | unambiguous → attribute silently |
| `index`, `name_ambiguous` | a guess, always **within one job** → attribute, and **mark it** |
| held | unmatched newcomer *plus* an unclaimed compatible row, matching neither name nor items nor position → **ask** |

`held` therefore keeps exactly the case only a human can answer: a set renamed **and** re-geared,
somewhere else in the list. That is the backlog that exists right now.

**Job compatibility applies to the automatic ladder, not only to suggestions.** Without it, levelling
Gladiator into Paladin produces a question about something that has nothing to decide: the set in game is
the same set, only its job code changed.

**Held rows take part in matching.** Without that, every push would fail to recognise its own held set and
create another copy of the same question.

## 6. Settled: how the server knows a row is missing

Per row, `last_seen_at`, written by every push that claims the row. **Not `updated_at`**: MariaDB only
advances that when a value actually changes, so an unchanged set would look missing.

A row is `parked` when a sync happened that **could** have reported it and did not. That is a *finding*,
not a certificate — which is what allows `complete` to be gone. The plugin never asserts what does not
exist; the server observes what was not claimed.

Two consequences worth keeping:

- The sentence for the player is **"not included since the last sync"**, not "no longer exists".
- A character that is no longer synced at all produces no orphans, because there is no newer sync for
  anything to be missing from.

**The version scope is what keeps a second PC from parking what it cannot report:**

| client | scope | effect |
|---|---|---|
| below **1.1.0** | `combat` | only rows of the 21 combat jobs can be refreshed or parked |
| from 1.1.0 on | `all` | every row can be refreshed or parked |
| version missing or unparseable | `combat` | the safe direction is "does not ask" |

Read from `plugin_version` in the request body, **never from the stored key**, because the key's recorded
version flips back and forth between two machines and would make the behaviour depend on who pushed last.
`plugin_version` has been in every write since 1.0.0 and is plain SemVer, pinned by test to the built
version, so `< 1.1.0` and `>= 1.1.0` are safe comparisons.

`1.1.0` is confirmed as the number. If it ever changes before shipping, the plugin side says so **before**
the constant is deployed.

## 7. Settled: the vocabulary in detail

| verb | effect | on `source='manual'` |
|---|---|---|
| `link` | the newcomer **is** that row: uid, pinned target and team share stay; contents, name and position come from the newcomer | **yes** — adoption |
| `new` | genuinely different; the held row becomes `active` and keeps its own uid | n/a |
| `ignore` | stop asking, keep the row, keep it as a candidate | yes |
| `delete` | remove the row and what hangs on it | **no, hard rejection** |
| `release` | the row becomes `source='manual'`: leaves plugin governance, keeps pin and share, never becomes an orphan again | n/a |

**Adoption is never automatic**, at any probability. It does not only change an attribution, it changes
*who governs the row* from here on. The server applies `link` to a hand-made row only when the decision
names it, and the dialog says it is a one-way door — because afterwards the row can become a removal
question like any other, and a player who was not told will not understand why.

**A released row stays adoptable.** Somebody may have released it by accident, or want it back later.
Being offered as a candidate is not the same as being applied automatically, so this does not weaken the
rule above.

**`delete` on a row with an active team share is performed as `release`.** Deleting would remove the set
for other people, quietly, on one person's decision. The server converts rather than refusing — no error
path, no two-step — and the plugin warns beforehand:

> *"This set is shared with **Kreszentia**. It will not be deleted; it will be released from the plugin
> and stay available on the website."*

The answer reports **the action that was performed**, not the one requested, on success and on failure
alike. Otherwise the window says "deleted" about a row that is still there. `team_names` rides along so
the sentence can name the team.

**After a `link`, the target row's uid survives.** It carries the pin, the share and the history, which is
the point of linking. The held row's uid ceases to exist and is dropped from the local cache. The answer
names the survivor per decision, so no second call is needed.

**Deleting a row that still exists in game** brings it back on the next sync as a new set, without the
pin. The dialog says so, but only where it is true:

| row | sentence when deleting |
|---|---|
| `plugin`, active | "If the set still exists in game it comes back on the next sync, without your pinned BiS set." |
| `plugin`, parked or ignored | nothing — it was not being reported anyway |
| `manual`, including after a `release` | nothing — nothing can bring it back |

## 8. Settled: one decision per call

No batch. The reasoning is the API side's and it is better than the alternative: a batch could contain two
decisions linking the same orphan to two different newcomers, and every answer changes the candidates for
the next question anyway. Fewer error paths are worth more here than fewer requests.

The answer carries the result, the **new** `state_token`, and the fresh state in the same shape as
`GET /gear/review`, so ten decisions are ten calls and no extra reads.

- `state_token` guards **only** the decision call. A push landing on a slightly stale view writes a
  correct row with a name one cycle old, which the next sync fixes. Only deciding is not repeatable.
- A stale token is **409**, with a body in the same shape plus `conflicts[]` naming what moved. One
  renderer, no bespoke error shape.
- A row the caller does not own is **404**, not 403.
- An unanswered row is not an error. It stays open and is offered again.
- One row is the **subject** of at most one open question at a time, but may be a **candidate** in any
  number of them. Answering a question **frees its candidates**.

That shape also gives the interface its form: **one comparison per view, one decision per view, one call
per decision, next** — a carousel, in the order the server already sorts by, most likely first.

## 9. Settled: suggestions

Scoring stays server-side, for the reason the purchase advisor already established: the same logic in the
plugin would cost a plugin release every time it improves, and the server sees strictly more — the stored
rows, their history, what hangs on them, and what other machines reported.

- **Shown, never executed.** Deterministic rungs may apply on their own; a score is not consent.
  "97 %, just do it" is where the separation would quietly collapse.
- **Several candidates, ranked.** Two nearly identical sets differ by one ring; the top score is then a
  coin toss and the player knows better than either side.
- **No hiding threshold.** A weak candidate is shown with its number. Silence removes an option somebody
  might have wanted; a low number presents it honestly.
- **Reasons as structured data**, not prose: `probability` (integer 0–100 — decimals would claim a
  precision the scoring does not have), `matched_slots`, `total_slots`, `same_job`, `compatible_job`,
  `has_pin`, `has_team_share`, `team_names`, `hidden`. The plugin composes the sentence in the player's
  language; a generated sentence could only ever be in one.
- **Item ids, never names.** The plugin has the item sheet locally and resolves names in the player's game
  language.
- **Every `url` is read back, never composed.** Same rule as `target` on `/gear/bis`.
- Candidates carry their items; the **push response stays small** — no candidates, no items — because it
  is sent on every sync while the window is opened later.

## 10. Settled: the job table, the doors, and the plugin's commitments

### The job table comes from the server

`GET /gear/jobs` is the source: `code`, `role`, `combat`, `from`/`to`. Read-only and cacheable, with a
shipped fallback for a first start without a network. **On any disagreement the server wins.** An unknown
code is displayed as the code rather than swallowed.

42 codes — the 21 combat jobs already accepted, plus:

```
hand   CRP BSM ARM GSM LTW WVR ALC CUL
land   MIN BTN FSH
base   GLA MRD CNJ THM ARC LNC PGL ROG ACN
other  BLU
```

**Live must accept all 42 before the plugin ships**, not only `dev`, or the first player on the new
version gets a 422 on the whole push. The plugin validates locally too, so this is one coordinated change
in that order: server first, plugin second.

`ACN` is compatible with `SMN` **and** `SCH`; `SMN` and `SCH` are **not** compatible with each other. It
is a relation, not a partition — they share the weapon class and nothing else that matters, and a Summoner
set linked to a Scholar row is exactly the wrong-comparison bug this feature exists to remove.

**One distinction the table does not cover and cannot:** the server's table maps *code → role,
compatibility, combat*. The mapping from the game's numeric `ClassJob` row id → code is **game data** and
can only live in the plugin; without it a job cannot be *sent*, whatever the table says. So "a new job
costs the plugin no release" is true for display, not for sending. The plugin side intends to close that
gap by reading the English `Abbreviation` column from the game's own `ClassJob` sheet instead of
maintaining a numeric list by hand — which would make a new job genuinely free of a release — and will
verify that against the sheet before relying on it, keeping the hand-written map as a fallback.

### Which door answers what

| decision | where | why |
|---|---|---|
| `link`, `new` — attribution | **plugin only** | needs the live in-game list at the moment of answering |
| `ignore`, `delete`, `release` — inventory | plugin **and** website | concerns one row, which the website knows |

The website is a mirror: if the plugin has been off for two weeks its picture is two weeks old, and an
attribution made on an old picture is a guess with a click. A question is answered where it was asked. On
the website a held row shows its marker, one sentence and exactly one verb, `release` — the way out for
somebody who uninstalls and would otherwise carry the marker forever.

### What the plugin side commits to

Recorded here so none of it is a surprise later.

- **The carousel runs on the server's fresh state after every decision**, never on the list the window
  opened with. A linked orphan must not still be offered in view three.
- **No optimistic display.** A row moves only after the server has agreed. On a 409 or a refusal the
  question stays and the reason is shown.
- **Attribution is logged, and not silenceable.** Per decision: character, `set_uid`, requested verb,
  `target_uid`, **performed** verb, surviving uid, outcome, `state_token` before and after. Never a key,
  never a body (R22). The in-game buffer holds 300 lines and rolls over; the same lines go to Dalamud's own
  log on disk, which is what a support case three days later needs. These lines are deliberately **not**
  gated by the user's verbosity setting — that gate is precisely why they would be missing when they
  matter.
- **Rows in the parked band stay out of the resolution cache.** A parked row is by definition not in game
  and would otherwise make a live set's name key ambiguous, breaking the comparison for its living twin.
- **Decisions enter the push fingerprint**, so the plugin's unchanged-skip cannot swallow the request that
  carries them, and a user-driven action is never delayed by the auto-push interval.
- **The window opens only when the player opens it**, and is blocked in combat, in a duty and during a
  cutscene. A dialog mid-fight is worse than an orphan.
- **Everything is scoped to the logged-in character.** Questions for other characters wait until that
  character is on screen, because an attribution needs its live list. The whole plugin works this way
  already.
- **The local cache stays a cache**: it never mints a uid, and on a miss it shows nothing rather than
  something wrong.

## 11. The wire shapes

**Push response**, additive to what is live today:

```json
{
  "status": "ok",
  "character_id": "42",
  "gearsets": 22,
  "sets": [
    { "gear_index": 0, "set_uid": "3f9c…", "state": "resolved", "matched_by": "name" },
    { "gear_index": 5, "set_uid": "a71b…", "state": "new",      "matched_by": "new" },
    { "gear_index": 7, "set_uid": "c05e…", "state": "held",     "matched_by": null }
  ],
  "review": {
    "state_token": "9f13c0a2…",
    "held": 1,
    "orphans": { "open": 10, "ignored": 3 },
    "url": "https://xivarsenal.app/mein-gear"
  }
}
```

**The questions**, fetched when the window opens:

```json
GET /gear/review?character_id=42

{
  "state_token": "9f13c0a2…",
  "held": [{
    "set_uid": "c05e…", "job": "PLD", "name": "FRU PLD", "gear_index": 7,
    "items": { "Weapon": { "id": 44123 }, "Head": { "id": 44100 } },
    "candidates": [{
      "set_uid": "9a1f…", "job": "GLA", "name": "Gladiator", "source": "plugin",
      "probability": 83, "matched_slots": 10, "total_slots": 12,
      "same_job": false, "compatible_job": true,
      "has_pin": true, "has_team_share": false, "team_names": [], "hidden": false,
      "items": { "Weapon": { "id": 44123 } },
      "url": "https://xivarsenal.app/mein-gear#s-9a1f…"
    }]
  }],
  "orphans": [{
    "set_uid": "7bb2…", "job": "DRK", "name": "Dunkelritter", "state": "parked",
    "last_seen_at": "2026-08-14T20:11:03+00:00",
    "has_pin": true, "has_team_share": true, "team_names": ["Kreszentia"],
    "items": { }, "url": "…"
  }]
}
```

`candidates[].source` is open item 2; `orphans[].items` is open item 4.

**One decision per call:**

```json
POST /gear/review
{
  "character_id": "42",
  "state_token": "9f13c0a2…",
  "decision": { "set_uid": "c05e…", "action": "link", "target_uid": "9a1f…" }
}
```

```json
{
  "result": { "set_uid": "c05e…", "action": "link", "surviving_uid": "9a1f…", "state": "active" },
  "state_token": "…", "held": [ … ], "orphans": [ … ]
}
```

`result.action` is the **performed** action, which may differ from the requested one — see `delete` on a
shared row in section 7.

**On a stale token: 409**, same shape as the GET plus `conflicts: ["7bb2…"]`.

## 12. Every decision, and the alternative that lost

The section to read if something above looks arbitrary.

| decision | the alternative | why it lost |
|---|---|---|
| The server mints `set_uid` | the plugin mints and remembers it | A plugin-minted id lives in one installation's configuration. Two machines produce two identities for one gearset and overwrite each other on every switch; a reinstall does the same. Gearsets live on the game's servers, not the PC, so nothing invented locally can be the identity |
| The matching ladder runs on the server | a ladder in the plugin, with the server storing an opaque `identity` blob | Three reasons: the web editor is a **second writer** and identity must be assigned where all writing paths meet; whoever deletes must be able to check the grounds, and an opaque blob makes the server the executor of logic it cannot verify; and two live sessions can race, which only the holder of the current state can resolve |
| No `complete` flag | `complete: true` on the push, licensing deletion | A push is automatic. Attaching a deletion right to an automatic action was the risk. With the server checking before it applies and removing nothing without a decision, the flag had no job left — and three further questions (its fingerprint's scope, its version gate, how a partial read is distinguished) died with it |
| `last_seen_at` as a finding | the plugin certifying its list is complete | A certificate is a claim the plugin cannot honestly make: what it sends is "every gearset it can report", and the report scope depends on its version. An observation of what was not claimed needs no certificate |
| Version scope read from the request body | read from `api_keys.client_version` | The stored value flips back and forth between two machines, so the behaviour would depend on who pushed last. `plugin_version` in the body describes the version that produced *this* list |
| Held rows get a uid immediately | reference a newcomer by its index in the sent list | A decision arrives minutes later, possibly after another sync. The index then points at something else or at nothing, and the guard would reject an answer the player got right |
| `index` and `name_ambiguous` attribute with a marker | make both a question | Their mistake is same-job, an order of magnitude less severe than the cross-job bug, and asking produces a dialog nobody cares about — which teaches people to dismiss dialogs |
| The ladder uses the compatibility table | compatibility only for suggestions | Levelling Gladiator into Paladin would otherwise produce a question about something with nothing to decide, plus an orphan |
| One decision per call | a batch of decisions | A batch could link the same orphan to two newcomers, and every answer changes the candidates for the next question anyway. Fewer error paths beat fewer requests |
| `delete` on a shared row becomes `release` | refuse it with an error | The refusal left the player with no path except un-sharing on the website first. Converting removes the error path; the plugin only has to warn, and the answer reports what happened |
| Adoption of a `manual` row is allowed | `manual` untouchable by any decision | It solves a real onboarding case — somebody maintains sets on the website, installs the plugin later, and inherits them instead of getting duplicates. The safety property is preserved by splitting the rule: adopting is additive, deleting stays a hard rejection |
| `ignored` is a state, not a flag | suppress the question and forget the row | A row that can never be found again is deleted in all but name. As a state it stays a candidate, and a later archive view is a view rather than a new mechanism |
| Scoring server-side | scoring in the plugin | Improving it would cost a plugin release every time, and the server sees the stored rows, their history and what other machines reported |
| Reasons as structured data | a ready-made sentence from the server | The plugin is bilingual; a sentence with numbers in it can only ever arrive in one language |
| `GET /gear/jobs` as the source | a job table in both places | A new job has to be accepted by the server anyway or the push 422s. If the table travels with it, the plugin needs no release for the display side |
| Everything in 1.1.0 | ship identity as 1.1.0 and reconciliation as 1.2.0 | The operator's call: it is one category of change, and a feature release may carry several features |

## 13. Thirty scenarios, end to end

The section to check against. **Expected** is what both sides should agree happens today with this design.

### Routine

| # | scenario | expected |
|---|---|---|
| 1 | Nothing changed, push | every set `resolved` on `exact`, no questions, no writes beyond the gear |
| 2 | Reorder the whole list, push | every set `resolved` on `name`, every uid unchanged, every pin still on its own set. **Measured: 22 of 22** |
| 3 | Reorder with the plugin **disabled**, restart, push | identical to 2. **Measured: 22 of 22** |
| 4 | Re-gear a set (same name) | `resolved` on `name` |
| 5 | Rename a set, then push | `resolved` on `items` |
| 6 | Rename a set, **before** the next push | the local cache misses both keys, the in-game comparison shows nothing for that set until the push repairs it. Intended: nothing rather than something wrong |
| 7 | Meld materia without re-saving the set | live equipped container is read, the set is sent with current materia, `resolved` |

### Edits that produce a guess

| # | scenario | expected |
|---|---|---|
| 8 | Rename **and** re-gear, same position | `index` — attributed with a marker; the pin stays. Accepted |
| 9 | Rename **and** re-gear, different position | **`held`** — a question, with candidates |
| 10 | Two sets, same job **and** name, both re-geared | `name_ambiguous` — paired by position, marker on both |
| 11 | Delete set 7 in game, create a different set at 7, same job | `index` — the new set inherits the old row's pin. Accepted: same job, and the player must act anyway to change a target |
| 12 | Delete set 7, create a different set elsewhere, same job | newcomer plus unclaimed row → **`held`** |
| 13 | Level Gladiator into Paladin | compatible job, same name → `resolved`, silent, no orphan |
| 14 | An `ACN` set, with both an `SMN` and a `SCH` row present | ambiguous → **`held`**, both offered as candidates |
| 15 | An `SMN` set and a `SCH` row | never linked automatically; not compatible |

### Removals

| # | scenario | expected |
|---|---|---|
| 16 | Delete a set in game, push | its row is not claimed → `parked`, appears in the orphan queue with `last_seen_at` |
| 17 | Answer `ignore` on an orphan | `ignored`, not asked again, **still a candidate** for later newcomers |
| 18 | Answer `delete` on an orphan with no share | the row and what hangs on it are removed |
| 19 | Answer `delete` on an orphan **with** a team share | the server performs `release`; the answer says `release`; the plugin warned beforehand and names the team |
| 20 | Answer `delete` on a row that still exists in game | it returns on the next sync as a new set, without the pin. The dialog said so |
| 21 | Stop syncing a character entirely | no orphans are produced — there is no newer sync for anything to be missing from |

### Decisions and their aftermath

| # | scenario | expected |
|---|---|---|
| 22 | Answer `link` | the **target** uid survives with its pin and share; the held row's uid ceases; the answer names the survivor and the plugin drops the dead uid from its cache |
| 23 | Answer `new` | the held row becomes `active` and keeps its own uid |
| 24 | Answer `link`, then look at the next question in the carousel | the linked orphan is **gone from the candidate lists**, because the view is redrawn from the answer's fresh state |
| 25 | Two decisions with an in-game change between them | the second returns **409** with the fresh state and `conflicts[]`; the window redraws, keeps the selections that are still valid, and says what moved |
| 26 | Ten `ignore` clicks | ten calls, ten answers. Must not consume the gear upload budget — **open item 3** |

### Onboarding and compatibility

| # | scenario | expected |
|---|---|---|
| 27 | Fresh install, or a second PC | no local cache, all sets sent, matched on `exact` and below → **no questions, no duplicates**. This is the case a client-minted id would have failed |
| 28 | Somebody used the website first, then installs the plugin | live sets have no plugin rows; the `manual` rows of the same job are offered as candidates → **`held`**, adoption offered with the one-way-door warning |
| 29 | A second PC on 1.0.x pushes | scope `combat`: hand and land rows are neither refreshed nor parked, so they produce no spurious questions |
| 30 | The plugin talks to a server without identity support | `set_uid` absent → detection stays false → the old `(gear_index, job)` path is used and everything keeps working |

### Two scenarios with no settled answer

| # | scenario | problem |
|---|---|---|
| A | A crafter, gatherer or base-class set is sent | The row exists, but no BiS catalogue does, so `/gear/bis` omits it and the set shows no comparison. Correct — but it is the case somebody will report as a fault, so the interface needs a sentence. Noted under open item 5 |
| B | Answer `release` on a **held** row while still syncing | The row becomes `manual`; the live gearset is unattributed; the next push finds a newcomer plus an unclaimed compatible (manual) row → **the same question comes back**. `release` was designed as the exit for somebody uninstalling, but used here it looks like a dismissal that does not dismiss. **This is open item 6** |

## 14. The six things still open

**1. The prose under the `index` rule contradicts the rule.** The paragraph beginning *"The case this
stops: somebody deletes set 7 in game and creates a different set that lands on position 7 with the same
job"* describes a case the rule permits — and, per section 5, should permit. The rule is right; the
justification under it needs rewriting, or the next reader implements the paragraph instead of the rule.

**2. `candidates[].source` is missing.** Without it the plugin cannot tell an adoption from an ordinary
link, and the one-way-door warning that was agreed cannot be shown. One field.

**3. `POST /gear/review` must not count against the existing gear upload limit.** This is not a request
for a new limit — the API side does not want one, and that settles it. What must be explicit is that these
calls do not fall into the 30-uploads-per-hour bucket. Otherwise clearing ten orphans costs a third of it
and the player cannot sync for the rest of the hour — the player with the biggest backlog being exactly
the one who hits it. "We add no limit" and "it falls under the existing one" are different outcomes, and
the second is the accident.

**4. Do `orphans[]` carry their items?** The example still shows `"items": { }`. Seeing *what* is in a row
is what makes "delete or ignore" answerable — *"ah, that is my old Ultimate set"* — and the manual-uid
path needs it. If the example is merely abbreviated, a yes is enough.

**5. A held row and its BiS target.** The contract says a held row *"does not get an automatic BiS target,
because which one that would be is the open question"*. The preference on this side is that every row,
held included, gets the **job's default** target the way any new row does: no empty state, and no forced
choice for something the player has to set by hand anyway. The distinction that keeps it safe:

- a **default for the job** is not a guess about identity — allowed;
- **inheriting a candidate's pin** before the decision is exactly the guess — still forbidden.

With this, the marker on a held set changes purpose: not "there is nothing here", but "this attribution is
provisional and the target may change when you answer". Note in passing that jobs with no catalogue (base
classes, and hand/land until that feature lands) have no default and will show no comparison — correct,
but the sentence for it belongs in the interface so it is not reported as a fault. (Scenario A.)

**6. `release` on a held row, while still syncing.** Found by writing scenario B. Releasing a held row
makes it `manual`, which leaves the live gearset unattributed — and because manual rows are adoptable
candidates, the next push raises the same question again. As the exit for somebody uninstalling that is
fine; used as a way to make a question go away it does not work, and the player has no way to know that.

Proposal: **`release` implies `ignore`.** The row leaves plugin governance *and* stops being asked about,
while remaining adoptable through the manual path — so the deliberate way back still exists and the
accidental loop does not. If you prefer a different resolution, it needs one, because otherwise the verb
does something other than what a player will read into it.

## 15. What each side builds, so the bill is visible

**API side**, as it stated: two columns with a migration (`last_seen_at`, `state`), the ladder extended by
the compatibility table, scoring with candidates and reasons, two endpoints, jobs from 21 to 42 with a
combat/non-combat split so BiS and the advisor keep seeing only combat jobs, items in the suggestion
payload, and the website view with its filter and sections.

**Plugin side**, one release, 1.1.0:

- 42 job codes, the compatibility table mirrored for display, and the role split in every gearset list —
  combat, hand, land — with the player's own order preserved **inside** each group, because grouping is a
  view and must never reorder what is sent;
- the reconciliation window as a carousel: one held set per view with its ranked candidates, a
  slot-by-slot comparison with differences marked, the deep link to the row on the website, manual uid
  entry, the five verbs, the adoption warning and the team-share sentence;
- visibility without a dialog: a count in the server-info bar, a line in the status window, at most one
  chat line per session with a clickable link, and a marker on a held set in the BiS window;
- `state` folded into the diagnostics as a third condition — recognised, guessed, **unanswered** — because
  `matched_by: null` on a held row currently reads as certain;
- the commitments in section 10.

## 16. What is already built, measured, and unaffected

Identity is finished on the plugin side and none of the above touches it:

- the in-game comparison re-keyed from `(gear_index, job)` to `set_uid`, with the position kept as a
  fallback **per target** for a server that does not mint identities;
- the local cache with its two properties, persisted per `cid_hash`, indexed by two keys that both leave
  the position out;
- the `GET /gear/sets` cold start, so the comparison works before the first push of a session;
- a diagnostics panel and `/xivarsenal gearsets`, both absent from a released build.

**Measured in game** against a 22-gearset character: the list reordered twice — once with the plugin
disabled across a restart — and all 22 identities stayed with their gearsets both times, with the uids
read back from disk before any push. 289 tests, release build clean, no warnings.

Nothing in the reconciliation work above requires any of that to be revisited.
