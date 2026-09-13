# Gearset reconciliation: the closed contract, and why it looks like this

Closed on **2026-08-22** after six revisions with the API side. This page is the plugin's half: what this
side does, which rules are load-bearing here, and — at the end — every decision together with the
alternative that lost. It exists so a fresh session starts from a document rather than from a chat log.

**The wire shapes are deliberately not copied here.** They live in
`BIS-Searcher/docs/plugin/contract-gearset-identity.md`, which is the source, and a second copy is the
thing that drifts. Read that for field names; read this for what this side does with them and why.

Identity itself — `set_uid`, the local cache, the re-keyed comparison — is a separate, already-built
concern: see [gearset-identity.md](gearset-identity.md). Nothing below required it to be revisited.

---

## The three sentences everything follows from

**A write can never destroy anything.** Pushes happen automatically — on login, on a gearset change, on a
timer — so no automatic action may carry a deletion right. Everything that removes or re-attaches goes
through one endpoint a person drives, one decision at a time.

**Identity is minted where all writing paths meet**, which is the server. A plugin-minted id lives in one
installation and there are two computers, a web editor, and reinstalls.

**Where the answer is knowable, never ask; where it is genuinely unknowable, ask the human.** A dialog
about something nobody cares about teaches people to dismiss dialogs, including the ones that matter.

The measure of success follows from the third: **a player who simply builds gearsets in game never sees
this feature at all.** If they do, something is wrong.

## What the push does, and what this side reads back

The plugin sends the whole list, every time, with no flag and no negotiation — plus `scope` (below). The
server checks before it applies, writes what it can attribute, mints for what is genuinely new, holds what
it cannot, and **removes nothing**. Per sent gearset the answer carries a `state`:

| `state` | meaning | the player sees |
|---|---|---|
| `resolved` | recognised as an existing row | nothing |
| `new` | genuinely new; a uid was minted | nothing |
| `held` | could not be attributed; a question was raised | one question |

`held` rows are **written like any other** and get the job's default BiS target. A player who leaves the
question for a week has current values with a marker, not week-old values. What a held row never does is
inherit a candidate's pin — that would be the guess itself.

**Ambiguity raises a question; unfamiliarity does not.** And its sharper form, which is the rule that keeps
the whole mechanism quiet: **no compatible row means no question.** Where nothing can be attributed there
is nothing to decide, so the set is new, is written, and nobody is asked.

`matched_by` still says which rung answered. Two of them guess, and both guess **within one job**
(`index`, `name_ambiguous`); those get a marker, not a question. `MatchedBy.IsUncertain` covers exactly
those two.

## `scope`: saying how much was reported

Sent on every push from 1.1.0 on, **including when the range is full**, because only then does its absence
mean "old client".

| value | meaning |
|---|---|
| `all` | every job in the server's table was reported |
| `combat` | only the frozen 21 were reported |

This exists because of a mistake worth remembering: the server derived the range from `plugin_version`,
and a version is only a *proxy* for the range. A 1.1.0 client that fell back to combat-only would have
been read as `all`, which would have parked every hand and land row of that character — and a parked row
is an orphan, and an orphan is offered with `delete`. **A safeguard against a rejected job code would have
staged a deletion.** The general rule that replaced it: whoever sends less than they could says so, because
otherwise "not reported" cannot be told from "gone from the game".

Two `scope` values are enough, and the reason is worth keeping: the server can only park rows it has, and
it can only have rows for jobs it accepts. A job outside its table has no row, so there is nothing to park
and no finer gradation to express. That is the same property that made `complete` unnecessary, one level
down — a statement about range is only dangerous where it judges things the other side cannot know.

## What governs which jobs may be sent

The server's published table, never the shipped copy:

| situation | table used | `scope` sent |
|---|---|---|
| `GET /gear/jobs` → 404 (route unknown) | none — old server | `combat` |
| success | the fetched one | `all` |
| transient failure, a table cached **for this address** | the cached one | as it says |
| transient failure, no table for this address | none | `combat` |
| push answers 422 `job_unknown` | **discard, refetch** | `combat` until a fresh one arrives |

- **Cached per address, never globally.** Keys are already per address; a global table would let `dev`'s
  answer govern a `live` push.
- **The shipped fallback governs display, never sending.** It exists so the role split is not empty on a
  first start without a network. Letting it decide what to send would be an assumption about what the
  server accepts, and "on any disagreement the server wins" applies to the send direction too.
- **The last row is the one that survives a rollback.** A cached table can hold something that was true
  and no longer is; only the 422 proves it. Without this rule the sync stays dead until a cache expiry.
- **Only `error: job_unknown` discards the table.** Keyed on any 422, a bad item id would discard it,
  refetch, and log "table discarded" for a failure that had nothing to do with jobs — a false trail costs
  more than the wasted request.
- **A backstop against ping-pong:** if the refetched table is unchanged and the push fails with
  `job_unknown` again, table and validation contradict each other. Stay on `combat` for that address and
  say so loudly rather than alternating.

### The frozen 21

`scope: combat` means this list and nothing else. It is compiled in, it is **not** a projection of the
server's table, and it never grows:

```
PLD WAR DRK GNB   WHM SCH AST SGE   MNK DRG NIN SAM RPR VPR   BRD MCH DNC   BLM SMN RDM PCT
```

Deriving it from the table would be circular — the rule above says "discard the table, then send
`combat`". And the drift is not hypothetical: **`BLU` is a combat job and is `combat: true` in the table,
but is not in the frozen 21**, because it arrives with the expansion to 42. Anyone implementing
`scope: combat` as "whatever the table calls combat" is wrong on day one, not at the twenty-second combat
job. The list retires together with the version rule, once nobody is below 1.1.0.

## The job table, and the one thing it cannot cover

`GET /gear/jobs` is the source for `code`, `role`, `combat` and the compatibility relation. Cacheable,
read-only, with a shipped fallback. On any disagreement the server wins; an unknown code is displayed as
the code rather than swallowed.

Compatibility is a **relation, not a partition**: a base class is compatible with the job it becomes, but
two upgrades of one base class are not compatible with each other. `ACN ↔ SMN` and `ACN ↔ SCH` are both
fine; **`SMN ↔ SCH` is not** — they share a weapon class and nothing else that matters.

**What the table cannot cover:** the mapping from the game's numeric `ClassJob` row id to the code is game
data and can only live here. Without it a job cannot be *sent*, whatever the table says. This side reads
the **English `Abbreviation`** column from the game's own `ClassJob` sheet rather than maintaining a
numeric list by hand — the 42 codes in the contract are exactly those strings — with the hand-written map
kept as a fallback and the reading verified in game before it is relied on.

## Questions, and where they are answered

| decision | where | why |
|---|---|---|
| `link`, `new` — attribution | **plugin only** | needs the live in-game list at the moment of answering |
| `ignore`, `delete`, `release` — inventory | plugin **and** website | concerns one row, which the website knows |

The website is a mirror: if the plugin has been off for two weeks its picture is two weeks old, and an
attribution made on an old picture is a guess with a click.

Five verbs, a closed set that grows only with a version:

| verb | effect |
|---|---|
| `link` | the newcomer **is** that row: uid, pin and share stay; contents, name and position come from the newcomer. On a `manual` row this is **adoption** |
| `new` | genuinely different; the held row becomes `active` and keeps its own uid |
| `ignore` | stop asking, keep the row, keep it as a candidate |
| `delete` | remove the row and what hangs on it. **Refused on `manual`.** On a row with an active team share it is **performed as `release`** |
| `release` | the row becomes `manual` **and `ignored`**: leaves plugin governance, keeps pin and share, never becomes an orphan, never raises a question |

Row states: `active`, `held`, `parked` (an orphan), `ignored`. **`manual` rows lie outside that cycle** —
`last_seen_at` does not apply, they are never parked, they never appear in `orphans[]`, they are only ever
candidates. And an **`ignored` row never raises a question**; it can only be an extra candidate in one.
Without that, setting an orphan aside and building a new set of the same job weeks later would bring the
dismissed question straight back.

### `orphans[].similar[]` only ever names a row the player still has

Guaranteed by the API since 2026-09-12, and held by a test over there: this list is built from
`state = 'active'`, `state = 'held'`, and hand-made rows nobody put aside. So `state` here is `active`,
`held` or `null` and **never `parked` or `ignored`**, and `gear_index` is null only for the hand-made
case, which is the only reason that field is nullable at all.

That matters because it decides what the card can argue. A resemblance to a set still standing in the
list answers "do I still need this"; a resemblance to a row that is just as gone answers nothing. The
guarantee makes the second case impossible rather than something the client has to detect, and a
sentence built for the other reading came back out of the plugin again.

It was measured before it was asked: two parked twins that matched each other completely, and neither
named in the other's list. The note that had suggested otherwise counted the position rule over the whole
state space and sat in this section, where a later paragraph already said the truth. **Two halves of one
document disagreed, and the client read the wrong half.**

Of the three fields, only `gear_index` reaches the screen, and only where the live list has withdrawn its
claim: printed as "last at #9", never as "#9", because the live list is the current one. `state` and
`last_seen_at` are carried and reported in the diagnostics, which is how the guarantee stays checkable
from this side. Telling two parked twins apart is the job of the orphan's own fields.

## What this side commits to

- **`candidates[]` is the whole offer.** It is filtered by job compatibility and never empty while a set is
  held. What may be offered is exactly that list plus "this is new" and "stop asking" — no free choice over
  loose rows, and **no manual uid entry**: with the list complete and filtered, a typed uid is either
  already in it or an error.
- **Positions come from the live read, never from the payload.** A held set is labelled with where it sits
  in game right now. Candidates and orphans get **no position at all** — they are not in game, and their
  stored index is either stale or a band number that means nothing to a player. (The payload happens to
  enforce this: only `held[]` carries a `gear_index`.)
- **Preselect by `proposed`, never by `probability`.** The server's assignment is globally unique, so the
  proposed candidate is not always the top-scoring one; `blocked_by` names the row that took it, so the
  honest sentence can name it too.
- **A corrected pairing is sent individually, before any bulk accept.** An individual decision mints a new
  token and invalidates the held assignment — which the server enforces with a 409 carrying the recomputed
  proposal, so this is a property and not a discipline.
- **The carousel runs on the server's fresh state after every decision**, never on the list the window
  opened with. A linked orphan must not still be offered two views later.
- **No optimistic display.** A row moves only after the server has agreed.
- **Attribution is logged and not silenceable.** Per decision: character, `set_uid`, requested verb,
  `target_uid`, **performed** verb, surviving uid, outcome, token before and after. Never a key, never a
  body (R22). The in-game buffer holds 300 lines and rolls; the same lines reach Dalamud's log on disk,
  which is what a support case three days later needs. Deliberately not gated by the verbosity setting —
  that gate is precisely why they would be missing when they matter.
- **Only rows that are in game enter the resolution cache**, which `state` from `GET /gear/sets` now says
  outright instead of leaving it to be read out of the index band. Cache `active` **and `held`** — a held
  row belongs to a live gearset and only lacks an attribution, so it must resolve. Exclude `parked`,
  `ignored`, and `null` (a `manual` row): none of them is in game, and any of them sharing a job and name
  with a live set would make its weak key ambiguous and break the comparison for the living one.
- **Decisions enter the push fingerprint**, so the unchanged-skip cannot swallow the request carrying them.
- **Automatic pushes are suppressed while the review window is open.** The token covers identity, not
  values, so a routine push no longer invalidates a review — but `gear_index` is in it, and a reorder is one
  of this plugin's push triggers. Manual pushes stay allowed; closing the window lifts it.
- **The window opens only when the player opens it**, and is blocked in combat, in a duty and during a
  cutscene. A dialog mid-fight is worse than an orphan.
- **Everything is scoped to the logged-in character.** Questions for other characters wait until that
  character is on screen.
- **A 422 is a bug detector, not a dialog.** Since only `candidates[]` is offered, `job_incompatible` means
  this side offered something it should not have. The body's two uids and two job codes go to the log; the
  player gets a short sentence and a freshly fetched list.
- **The invariant to test is "a hand-made row is never `active`, `held` or `parked`"** — not the shorter
  `state == null ⟺ source == "manual"`, which was mine and was wrong. A hand-made row can be `ignored`:
  putting a row aside is a decision a player makes, not a position in a cycle. The short form holds until
  the first `ignore` on a hand-made row and then quietly does not. The API side found it while answering
  something else, before it could become a test that passes for the wrong reason.
- **A row enters the resolution cache exactly when `state` is `active` or `held`.** One condition over one
  field: `source` is not needed, because the corrected invariant means a hand-made row can never carry
  either. The right invariant made the rule simpler rather than more complicated.

## Every decision, and the alternative that lost

The section to read when something above looks arbitrary.

| decision | the alternative | why it lost |
|---|---|---|
| The server mints `set_uid` | the plugin mints and remembers it | A plugin-minted id lives in one installation. Two machines produce two identities for one gearset and overwrite each other on every switch; a reinstall does the same. Gearsets live on the game's servers, so nothing invented locally can be the identity |
| The matching ladder runs on the server | a ladder here, with the server storing an opaque blob | The web editor is a second writer and identity must be assigned where all writing paths meet; whoever deletes must be able to check the grounds; and two live sessions can race, which only the holder of the current state can resolve |
| No `complete` flag | `complete: true` on the push, licensing deletion | A push is automatic, and attaching a deletion right to an automatic action was the risk. With the server checking before it applies, the flag had no job — and three further questions died with it |
| `last_seen_at` as a finding | the plugin certifying its list is complete | A certificate is a claim this side cannot honestly make: what it sends is "every gearset it can report", and that depends on its version |
| `scope` in the body | the version as a proxy for the range | The proxy lies in exactly the case a safeguard creates. Read as `all`, a deliberately narrow push would have parked — and offered for deletion — every row it chose not to mention |
| Held rows get a uid immediately | reference a newcomer by its index in the sent list | A decision arrives minutes later, possibly after another sync. The index then points at something else, and the guard would reject an answer the player got right |
| `index` and `name_ambiguous` attribute with a marker | make both a question | Their mistake is same-job — an order of magnitude milder than the cross-job bug — and asking produces a dialog nobody cares about, which is how people learn to dismiss dialogs |
| The ladder uses the compatibility table | compatibility only for suggestions | Levelling Gladiator into Paladin would otherwise raise a question about something with nothing to decide, plus an orphan |
| One decision per call | a batch | A client-assembled batch can contradict itself — the same orphan linked to two newcomers — and every answer changes the next question's candidates |
| A server-assembled bulk accept | no bulk at all | The objection was self-contradiction, which a server-built assignment cannot have. Only `link` and `new`, only striking, never adding, one transaction — twenty attributions are a relief, twenty deletions are what the design exists to prevent |
| `delete` on a shared row becomes `release` | refuse it | The refusal left no path but un-sharing on the website first. Converting removes the error path; this side only warns, and the answer reports what happened |
| Adoption of a `manual` row is allowed | `manual` untouchable | It solves the website-first case: somebody inherits their rows instead of getting duplicates. The safety property survives by splitting the rule — adopting is additive, deleting stays refused |
| `candidates[]` filtered by job | `compatible_job` as a field | A field that can be `false` says unsuitable candidates are included, which was never meant. Filtering makes it impossible rather than merely visible — and it made the manual paste path pointless |
| No manual uid entry | keep it as the deliberate override | With candidates filtered and never empty, every target is either already listed, taken (409), incompatible (422), or someone else's (404). An input that is redundant or an error in every outcome |
| No link across a job boundary | allow it for the manual path only | Proposed by this side and withdrawn: what cannot happen needs no discipline. The pin could never have followed anyway, so only the share and the history were at stake, and `release` plus a new row still reaches that |
| `ignored` is a state that stays a candidate | suppress and forget | A row that can never be found again is deleted in all but name. As a state, a later archive view is a view rather than a new mechanism |
| Scoring server-side | scoring here | Improving it would cost a plugin release every time, and the server sees the stored rows, their history, and what other machines reported |
| Reasons as structured data | a ready-made sentence | This plugin is bilingual; a sentence with numbers in it can only ever arrive in one language |
| `results[]` keyed by `set_uid` | keyed by position in the request | `sets[]` is already matched by `gear_index` rather than array position. Two positional agreements in one contract is one too many |
| Only `job_unknown` discards the job table | any 422 discards it | A bad item id would discard the table and log "table discarded" for a failure unrelated to jobs. The false trail costs more than the wasted request |
| The frozen 21 is compiled in | derive it from the table's `combat` flag | Circular — the rule is "discard the table, then send `combat`" — and wrong from day one, because `BLU` is `combat: true` and not in the 21 |

## The build order

Interlocked with the API side, which goes first: migration, `GET /gear/jobs` and the 42 codes accepted
**live** — not only on `dev` — and then says so. Until that word arrives, the development build is not
pointed at live.

Buildable before it, against fakes and the shipped copy, because the contract is additive:

1. **`scope`, and the 42 codes** read from the `Abbreviation` column, with the frozen 21 as the floor and
   the table-governs-sending rules above.
2. **The role split** in every gearset list — combat, hand, land — with the player's own order preserved
   *inside* each group, because grouping is a view and must never reorder what is sent.
3. **`state`** in the model, the third diagnostics condition (recognised, guessed, **unanswered** — because
   `matched_by: null` on a held row otherwise reads as certain), and the counters in the status window and
   the DTR bar.
4. **The two cleanups** this design implies here: parked rows out of the resolution cache, decisions into
   the push fingerprint.

Then, once the endpoints exist: the reconciliation window as a carousel with the bulk accept.

## History

The route to this is in `docs/agents/prompts/`, both superseded and both kept:
[`gearset-reconciliation-review.md`](../agents/prompts/gearset-reconciliation-review.md) is the question,
[`gearset-reconciliation-agreed.md`](../agents/prompts/gearset-reconciliation-agreed.md) the consolidated
answer with thirty scenarios. Each carries a banner naming what was later reversed, because a document that
permits what the contract forbids is worse than an open question — it does not look like one.
