# Review request: reconciliation without a deletion licence

> **Answered, and two of its claims were reversed. Superseded on 2026-08-22.** The current agreement is
> [`../../architecture/gearset-reconciliation.md`](../../architecture/gearset-reconciliation.md); the
> API-side source is `BIS-Searcher/docs/plugin/contract-gearset-identity.md`. This page is kept because
> the answers reference its numbering, and rewriting it would falsify the record — but **two things in it
> are now forbidden**, so read it as history and not as permission:
>
> - **the manual `set_uid` paste path is dropped** (§7 below offers it) — with candidates filtered by job
>   compatibility and never empty, a typed uid is either already in the list or an error;
> - **nothing may link across a job boundary** (§5 and §7 below allow it for the manual path) — that
>   route no longer exists at all, and `link` onto an incompatible row is a 422.

**For the API agent. This is not a task and nothing is being built from it.** It is the concept as the
plugin side now understands it, written down so you can confirm it, contradict it, or find the hole. We
build only once you have waved it through — and a partial wave-through is a "no", because the parts lean
on each other.

Self-contained: no plugin repository needed. Written in English to match `contract-gearset-identity.md`
and `handover-gearset-identity.md`, which is where this belongs if it survives review.

It replaces `prompt-complete-and-decisions.md` in one respect that matters: **`complete: true` is gone.**
The reasoning is in claim 2. Everything else from that document that still holds is carried over
explicitly, so nothing has to be reconstructed from a chat log.

---

## The flow, in one page

1. **The plugin pushes the whole list, every time.** No flag, no mode, no negotiation. Same request it
   sends today.
2. **The server checks before it applies.** Per sent gearset it decides one of three things: this is a
   row I recognise, this is genuinely new, or this I cannot attribute. It writes the first two and holds
   the third.
3. **The server removes nothing.** Not on a push, not on a clear case, not ever without an explicit
   decision that names the row.
4. **The answer says what happened per set**, and for anything held, what the candidates are and how
   likely each is.
5. **Decisions go to their own endpoint.** That is the only place anything is removed or re-attached, and
   the only place that needs a guard against concurrent change.
6. **One unclear set does not stop the other twenty-one.** Granularity is the set, not the sync.

The property that makes this worth the change: **a write can no longer destroy anything.** Pushes happen
automatically — on login, on a gearset change, on a timer. Attaching a deletion right to an automatic
action was the risk; taking it away is the fix.

---

## The claims to confirm or contradict

### 1. Three outcomes per sent gearset, and only the third asks

| outcome | server does | player sees |
|---|---|---|
| **resolved** — a row is recognised | writes the gear | nothing, it just works |
| **new, no candidate** — nothing on the server could be it | mints a uid, writes the gear | nothing |
| **ambiguous** — a newcomer *alongside* an unmatched row | holds this set, offers candidates | a question |

The middle row is load-bearing and easy to lose. If "unrecognised" alone were enough to hold a set, then
creating a gearset in game would not sync until the player clicked something. That would be worse than
today. **Ambiguity, not unfamiliarity, is what raises a question.**

### 2. `complete: true` is dropped entirely

With the server checking before it applies and never removing without a decision, the flag has no job
left. It would be a dangerous permission that nothing needs.

Three questions from the earlier round die with it, and that is the point: whether its fingerprint spans
the equipment or only the identity fields, from which client version it is honoured, and how a partial
read is distinguished from a complete one. **The plugin no longer has to certify anything.**

The one thing to confirm here: the plugin's list is, and always was, "every gearset it can report". With
41 job codes (claim 5) that is every gearset that exists, but the server should never *rely* on that — a
sent list is evidence about what was seen, never a statement about what does not exist.

### 3. Orphans are a separate queue with three states

A row with no live gearset is not a failed transfer. There is nothing to send for it. It is a removal
question, counted and shown separately, because these are two different sentences for a player:

> "2 sets are waiting for your decision" ≠ "10 rows on the site no longer exist in game"

| state | meaning |
|---|---|
| **open** | the player is asked |
| **ignored** | not asked again — but **still a candidate** for future suggestions |
| **deleted** | gone |

**`ignored` lives on the server.** A mark in the plugin's configuration would be a second truth per
machine, and the second computer would ask again what the first already settled. Confirmed as your side.

And **`ignored` must not mean "invisible to scoring"**. A row that can never be found again is deleted in
all but name. This is also what makes a later *archive* feature free: if `ignored` is a real state rather
than a suppression flag, the archive is a view over it and not a new mechanism.

### 4. The vocabulary, and where it may act

`delete`, `keep`/`ignore`, `link`, `new` — a closed set. If it must grow, it grows with a version, because
the plugin's interface can only offer verbs it knows; a fifth verb would otherwise be silently missing
from the dialog.

**On a `source='manual'` row, the rule splits** — it was previously absolute and should not stay that way:

| action on a hand-made row | allowed |
|---|---|
| **adopt** (`link` a live gearset onto it) | **yes** |
| **delete** | **no, still a hard rejection** |

Adoption is additive: the row keeps its pinned target and its team share, gains the live gear, and becomes
plugin-governed. It solves a real case — somebody maintains sets on the website, installs the plugin
later, and inherits them instead of getting duplicates. Deletion stays forbidden for the original reason:
a hand-made row does not exist in game, so a sync can know nothing about it.

Two conditions on adoption:

- **Never automatic, not even at 100 %.** It does not only change an attribution, it changes *who governs
  the row* from here on. That deserves a confirmation of its own.
- **The dialog says it is a one-way door.** "From now on this set follows the game" — because afterwards
  the row can become a removal question like any other, and a player who was not told will not understand
  why.

Noted for the future, deliberately not a requirement now: because adoption is a state change rather than
a merge, **releasing it back to `manual` stays possible** — for somebody who stops using the plugin. No
need to build it; worth not designing it out.

### 5. Forty-one job codes, and compatibility is a table, not a class

The plugin currently reports 21 combat jobs. Everything else is dropped: hand and land, the nine base
classes, and Blue Mage. So today's list is routinely short, which is why claim 2's "it can report
everything" needs these:

```
hand   CRP BSM ARM GSM LTW WVR ALC CUL
land   MIN BTN FSH
base   GLA MRD CNJ THM ARC LNC PGL ROG ACN
other  BLU
```

Same shape as the combat codes, three uppercase letters. **The live API has to accept all of them before
the plugin ships**, not just `dev` — otherwise the first player on the new version gets a 422 on the whole
push. The plugin validates locally too, so this is one coordinated change in that order.

**Job compatibility for suggestions is a hard filter**, and it is not an equivalence class:

| base class | compatible with |
|---|---|
| GLA | PLD |
| MRD | WAR |
| CNJ | WHM |
| THM | BLM |
| ARC | BRD |
| LNC | DRG |
| PGL | MNK |
| ROG | NIN |
| **ACN** | **SMN and SCH** |

Two consequences, and the second is the one that gets missed:

- A base class and its job are the same gearset line: a Marauder set may be suggested against a Warrior
  row. That is a real move a player makes.
- **`ACN` is one-to-many, so this is not a partition.** `ACN ↔ SMN` and `ACN ↔ SCH` are both fine;
  **`SMN ↔ SCH` is not.** They share the weapon class and nothing else that matters — a Summoner set
  linked to a Scholar row is exactly the wrong-comparison bug this whole feature exists to remove.

Jobs with no base class, for completeness: DRK, AST, MCH, SAM, RDM, GNB, DNC, RPR, SGE, VPR, PCT, BLU.

**No suggestion ever crosses a compatibility boundary.** The one exception is the manual path in claim 7,
where a human types a uid, and there the pinned BiS target does **not** follow — a target of the old job
would be the wrong comparison again.

### 6. Suggestions carry a probability, several candidates, and their reasons as data

Scoring stays on your side, for the reason the purchase advisor already established: logic in the plugin
would cost a plugin release every time it improves. You also see more than we do — the stored rows, their
history, what other machines reported.

Four conditions:

- **A suggestion is shown, never executed.** Deterministic rungs may apply on their own (same job,
  identical items is not a guess). A *score* is not consent, and "97 %, just do it" is where the
  separation would quietly collapse.
- **Rank several candidates, not one.** Two nearly identical sets differ by a ring; the best score is
  then a coin toss and the player knows better than either of us.
- **No hiding threshold.** "Best candidate: 4 of 12 slots, probably unrelated" is honest; silence removes
  an option the player might have wanted. Show the number and let them judge.
- **Reasons as structured data, not prose.** `{ candidate_uid, probability, matched_slots, total_slots,
  same_job, old_name, has_pin, has_team_share, hidden }` and the plugin writes the sentence, in DE or EN
  as the player has it set. A generated sentence with numbers in it cannot be localised on your side
  without you owning the plugin's language setting.

### 7. What the comparison view needs, including one gap

The player has to be able to verify a suggestion rather than trust it. Per candidate:

- name, job, probability;
- **the items per slot** — this is the gap: `GET /gear/sets` returns no equipment today. It belongs in the
  suggestion payload rather than a second request, because that is the one moment it is needed;
- what hangs off the row (pinned target, team share, hidden) — this is the sentence that makes a player
  choose correctly: *"linking this keeps your pinned BiS set"*;
- **a web path, read back and never invented.** Same rule as `target` on `/gear/bis`: the plugin does not
  compose addresses for the site.

**Item ids, not item names.** The plugin has the item sheet locally and resolves names in the player's
game language; a name from the server could only ever be in one language, and it would be the wrong one
often enough to notice.

And the manual path: **paste a `set_uid`.** A copy button on the site is the other half of it. Two
conditions — **you** validate that the uid belongs to this character and is not a hand-made row the player
should not be re-pointing, not us; and it is the only route allowed to cross a job boundary, loudly, with
the pin left behind.

### 8. How a newcomer is named in an answer

`delete` and `keep` reference a stored row, which has a uid. `link` and `new` reference a gearset that has
none yet. Our proposal: **the index in the list that was just sent.** Unambiguous for that one request,
and free of the position, which is the value we spent this whole feature removing from the protocol.

The operator's view, and we agree: this is a detail that can be changed later without touching the rest,
so it is not worth a long argument — but it does need to be written down before either side builds.

---

## What we need back

Not a task list. For each claim: **confirmed**, **confirmed with a change**, or **no, because**. If any
claim is a "no", say which of the others it takes with it — several of them only make sense together.

Specifically the shapes, because they are what both sides code against:

1. The per-set outcome in the push response: field names, and the state values for the three outcomes.
2. The suggestion block: field names, how probability is expressed (0–100, or 0–1), and whether it rides
   on the push response or is fetched separately.
3. The decision endpoint: path, payload, how a newcomer is referenced, and what the guard against
   concurrent change is called.
4. Whether the guard is needed **only** on the decision call. We believe yes, now that no write can
   destroy anything — a push that lands on a slightly stale view writes a correct row with a name one
   cycle old, which the next sync corrects.

## Where we defer to you entirely

- **ACN.** Compatible with both, `SMN ↔ SCH` forbidden — or do you see a reason to be stricter?
- **What a `delete` does to a team share.** The proposal names the share, which is right. But deleting a
  shared row affects other people: does the team learn, or does it disappear quietly for six others? That
  is not a decision one person should make unnoticed on their behalf.
- **A decision naming a row the player no longer owns** — rejected, presumably, but we would rather read
  it than assume it.
- **Whether the two-door story still stands** as it was written: the site can offer "these did not appear
  in the last sync, remove or keep" for the existing backlog and for players on an old plugin version,
  while the full version with `link` and newcomers needs the plugin because only there are both sides
  present. We think it does, unchanged.

## What we build once it is confirmed, so you can see the shape of the bill

Plugin side, one release:

- 41 job codes, plus the compatibility table for display purposes (grouping, "converted from Marauder");
- display split by role — combat, hand, land — with the player's own order preserved inside each group,
  because grouping is a view and must never reorder what is sent;
- a reconciliation window: orphans and held sets, candidates with their probability, a slot-by-slot
  comparison with the differences marked, a switcher between candidates, the deep link, manual uid entry,
  the four verbs plus *ignore*, and the adoption warning;
- visibility without a dialog: a count in the server-info bar, a line in the status window, at most one
  chat line per session with a clickable link. The window opens only when the player opens it, and is
  blocked in combat, in a duty and during a cutscene — a dialog mid-fight is worse than an orphan;
- two things on our own side that your design implies: rows in the parked band are kept out of the local
  resolution cache (a parked row is by definition not in game, and it would otherwise make a live set's
  name key ambiguous), and decisions enter the push fingerprint so the plugin's unchanged-skip cannot
  swallow the request that carries them.

Nothing here is started. The identity work that *is* built and measured — mapping keyed on `set_uid`,
the local cache, the diagnostics — is unaffected by all of the above and stays as it is.
