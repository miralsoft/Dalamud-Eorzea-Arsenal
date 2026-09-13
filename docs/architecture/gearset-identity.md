# Gearset identity: the server mints it, we recognise it

A gearset's number in game is its **position**, and nothing more. `RaptureGearsetModule.GearsetEntry`
holds `Id` as a single byte at offset `0x00` — the slot — and the module offers `ReassignGearsetId` to
move it. There is no persistent unique id anywhere in the struct (`Id`, `_name`, `ClassJob`,
`GlamourSetLink`, `ItemLevel`, `BannerIndex`, `Flags`, `_items`, `_glassesIds`; 452 bytes, verified by
reflecting `FFXIVClientStructs.dll`).

The website used to key a gearset's pinned BiS target, its team share and its hidden flag on that
number. Anything that reordered the list — another plugin, or a player tidying up — moved every one of
those to a different gearset. Silently: a tank set compared against a healer's target shows plausible,
false numbers.

## Who decides

**The server mints `set_uid` and only the server does.** The plugin sends gearsets, reads the mapping
out of the answer, and never invents an identity. This is not a division of labour we chose for
convenience — three things make it the only workable one:

- **Two computers.** A plugin-minted id lives in one installation's configuration. Two machines have
  two configurations, so the same gearset would get two identities and they would overwrite each other
  on every switch. A reinstall or a wiped config does the same. Gearsets live on the game's servers,
  not on the PC, so nothing invented locally can be the identity.
- **There is more than one writer.** The web editor creates gearsets too (`source = manual`, positions
  from 1000). Identity has to be assigned where all writing paths meet.
- **Whoever deletes must be able to check.** If the matching happened here and the server merely
  believed the uid it was handed, an old or broken client could talk it into deleting a player's data.

Full contract: `BIS-Searcher/docs/plugin/contract-gearset-identity.md` and `handover-gearset-identity.md`.
The older `prompt-stable-gearset-identity.md` in the same folder is **superseded** — it asks for
`complete: true` to be sent, which today's server ignores.

## What this side does

1. **Read the mapping out of the push response.** `PUT /gear` answers with `sets[]`, index-aligned with
   the gearsets that were sent: `{ gear_index, set_uid, matched_by }`. That is the cheap path — no extra
   request, and it covers exactly the list just sent.
2. **Key the in-game comparison on `set_uid`**, not on `(gear_index, job)`. `BisComparer` takes a
   delegate that resolves a live gearset to its identity; the position remains as a fallback **per
   target**, chosen only when that target carries no uid.
3. **Fill the gap before the first push with a read.** `GET /gear/sets` returns the mapping without
   writing. Pushing to learn it would be a write in order to ask a question.
4. **Say when the server guessed.** `matched_by` of `name_ambiguous` (identical job and name) or `index`
   (position as a last resort) means the mapping may sit on the wrong set. A line in the status window,
   not a dialog — the fix is for the player to name the sets apart.

## The local cache, and why it is only a cache

`GearsetMappingService` remembers what the server decided, persisted per `cid_hash`. It exists because
the in-game list can change between two pushes and a live gearset has to be re-attached to its identity
to show anything at all. Two properties keep it from becoming a second truth, and both are covered by
tests:

- **it never mints a uid** — only the server does;
- **on a miss it shows nothing**, never something wrong.

`Resolve` walks four rungs, **ordered by how specific each one is, not by how sure it is**:

| rung | over | survives | ends where |
|---|---|---|---|
| `job+name+gear` | job, name, item id per slot | being moved | two sets share all three |
| `place+gear` | position, job, item id per slot | being renamed | the set moved |
| `place+name` | position, job, name that no second row carries | being re-geared | the set moved, or the name is shared |
| `job+name` | job, name | being moved, being re-geared | two sets share both |

The order is load-bearing and was once the other way round. The position is the **surer** indication, the
strong key the **more precise** one. With the position on top, a set whose whole description matched one
stored row was handed the uid of whoever happened to sit on its number, confidently. Every rung reports
ambiguity rather than guessing, the strong key included: same job, same name, same gear is the best
evidence the contents can give, and it is still not a distinction.

The position had been left out on the grounds that reordering makes it unreliable. That skipped a fact
about the game: the player is never asked for a name when a set is made, so the game writes the job in and
two sets of one job are called the same thing from the moment they exist. Only a reorder moves a number,
and a reorder is exactly the case the content rungs were built for.

> **`GearKey` and `GearIndex` are filled by a push and by nothing else.** `GET /gear/sets` returns no
> items, so a read cannot supply them, and everything that separates two sets of one job hangs off them.
> They must not be discarded anywhere that only means "read this again": that was wrong at **four** call
> sites on 2026-09-07 and cost an evening, because the loss was invisible. Use `ExpireMapping`, which
> clears only the refresh schedule; `Forget` genuinely forgets and belongs to disconnecting. The
> diagnostics line `cached keys` exists so the difference can be seen rather than reasoned about.

Where two live gearsets still resolve to one identity, `LivePositionMap` withdraws the claim from **both**
rather than letting the last one win, and a third claimant cannot restore it. The status window says how
many, and names the remedy: give one of each pair a different name.

Everything the cache reports about a character is held **per `cid_hash`**, in one immutable
`CharacterSummary` published whole: the status line, the last read, the counts, and which attributions are
held, uncertain or contested. As flat fields these described whichever character was touched last, so
after a switch the report read "learned from a push, 10 row(s)" directly above "cached rows : 34". No
identity was ever wrong, because a `set_uid` is unique across the account; the damage was to the
measurement, which is the one thing this view exists to make trustworthy.

## Rules that are load-bearing

- **Detect `set_uid`, never assume it.** The identity work is not on the live API yet, and two machines
  can talk to a live and a test instance on the same day. `GearsetMappingService.ServerMintsUids` turns
  true only once something carrying a uid has been seen; until then the old path applies.
- **Do not validate an incoming `gear_index` against 0–99.** A row whose position was claimed by another
  gearset moves to the 100–999 band, and hand-made sets sit at 1000+. `MaxGearIndex` guards what is
  *sent*, and only that.
- **Do not send `complete: true` yet.** The server ignores it and nothing prunes; its first appearance
  must be against code that honours it, or nothing has been tested. It arrives with the pruning work.
- **Never normalise `name` or `job`.** The server's matching leans on both. An empty name is better
  information than an invented one — `GearSanitizer` passes them through untouched, and a test says so.
- **A misaligned push response is refused wholesale.** If `sets[]` ever disagrees with the positions that
  were sent, pairing the rows anyway would write one gearset's identity onto another. The mapping for
  that push is dropped and the fact is logged.

## Not ours to build

No `identity` field, no matching ladder of our own, no map we maintain — the server sees every machine,
this sees one. No richer per-set fields yet either (glamour plate, dye channels, `GlamourSetLink`,
`BannerIndex`, glasses): agreed as a later phase, and it goes into the contract before it goes into the
code (R25/R27).

## Testing it in game

The mapping is invisible by design, so verifying it needs a way to see it. `/xivarsenal gearsets` dumps
the live list with the identity each set resolves to, the rung the server matched it on, and whether the
mapping could be read at all — read-only, and kept for the same reason the weekly probes are kept.

```
gearsets: 12 live, mapping known, server mints uids: True
  #0   DRK 3f9c1a2b exact           2.50
  #1   WHM a71bc0d4 name_ambiguous  Heal
  #2   SGE —        unknown         New set
gearsets: 11/12 identified, 0 ambiguous, 1 the server was unsure about
```

There is also a live view of the same thing in the diagnostics window (**Log → "Set-Zuordnung
(Diagnose)"**, collapsed by default, local only): whether this server mints identities at all, what the
last mapping read did, how many rows are cached, and a table of the current sample with the uid and the
rung per gearset. Colour carries the message — a column of green means every set was recognised, yellow
marks the rows where the server guessed or this side declined to.

Sampling is behind a button rather than per frame on purpose: resolving hashes every gearset, and doing
that thirty times a frame would put real work on the framework thread (P1). The button and
`/xivarsenal gearsets` run the same code, so the window and the chat dump cannot come to disagree.

### The developer build

The panel and the wrench are **absent from a released build**, not switched off in it: they are compiled
in only when `EORZEA_ARSENAL_DEVTOOLS` is defined, and that comes from `Directory.Build.local.props` —
a file git ignores and CI refuses to see in a checkout. A runtime flag would leave the panel in the
assembly with an "off" somebody could go looking for; absent is not something anyone can switch.

Switch it on:

```bash
cp Directory.Build.local.props.example Directory.Build.local.props
```

Then rebuild. Verify it against the **built assembly** rather than the source, which is the only check
that answers the question:

```bash
grep -c GearsetIdentityPanel src/EorzeaArsenalPlugin/bin/Release/EorzeaArsenalPlugin.dll
```

One or more with the file in place, zero without it (measured both ways when this landed).

What stays in every build: the log window itself and `/xivarsenal gearsets`. A player's problem has to
remain diagnosable, so the log and the chat dump belong to everyone — only the interactive developer
surface goes.

The run to make, once a server that mints identities is reachable:

1. `/xivarsenal gearsets`, note the uids.
2. Reorder several gearsets in game, push, `/xivarsenal gearsets` again — every uid stays with its set
   and only the position changed.
3. Do the same with the plugin **disabled**, restart, push: identical result.
4. Rename a set without touching its items: the dump shows it unresolved, the push repairs it on the
   `items` rung, and the next dump has the same uid with the new name.
5. Open the BiS window before and after: every comparison is on the set it belongs to.

Against a server without `/gear/sets` the dump says `mapping unavailable` and `server mints uids: False`.
That is the fallback path, and it is worth confirming too — it is what every user is on until the server
side ships.

## When `complete: true` arrives, it must mean "unabridged"

Pruning is Phase 3 on the server, and the flag that licenses it does not exist in this code yet — on
purpose. When it is built, one property decides whether it is safe, and it is not obvious from the
outside:

**What this plugin sends is not "every gearset the character has". It is "every gearset it can
report."** Three things are left out between the game and the payload:

- **jobs outside the 21-job whitelist** — crafters and gatherers are skipped by `JobMap.ToCode`. They
  are the gaps in a sorted list (`#0, #4, #8, #10, …` were all DoH/DoL sets in the 2026-08-20 test run);
- **anything `GearSanitizer` drops** — an unknown job code, or a position outside 0–99;
- **everything past `MaxGearsets`**, where the sanitizer stops — which cannot actually happen: the game
  allows at most 100 gearsets and the payload cap is 200, so this bound is defence in depth, not a risk.

If `complete: true` means "delete what you do not see here", each of those becomes a deletion. The first
is harmless only because such sets were never sent in the first place. The other two are not: a set the
sanitizer quietly drops would disappear from the website while still existing in game, and the player
would have no way to tell why.

So the flag is **not** a constant and not a configuration option. It is computed where the list is
produced, and it is `true` only when nothing was dropped on the way — the sanitizer has to say whether
it abridged anything, and any doubt means `false` or absent, which the server treats as "merge". A wrong
`true` is a request to delete a player's data.

Cleaning up rows that are already orphaned is a **different problem** and a forced full push is the
wrong first instrument for it: the plugin can only push, while the website can show what would go and
ask. Orphans are also identifiable more precisely than "not in the list" — a row whose position was
claimed sits in the 100–999 band, carries `source = 'plugin'`, and went unmatched by the most recent
push. `source = 'manual'` must never be touched by either mechanism.

### Sending every job, and why it makes deletion safe

Proposed 2026-08-20 and, from this side, the right move — not for convenience but because it removes the
reason the flag was dangerous. With crafters and gatherers included, the job whitelist stops being a
filter, and `complete: true` can mean what it says instead of "complete, except for a category we agreed
not to mention". A destructive operation should not rest on an unstated exception.

What remains as an abridgement is then genuinely exceptional — a `ClassJob` that maps to nothing at all —
which is exactly the rare `false` a "was this read abridged?" flag is for, rather than a routine one.

Three things it needs, and the first is the one that can lose data:

- **One release, not two.** The plugin version that starts sending `complete: true` must be the same one
  that sends all 32 jobs. A build that sends the flag while still filtering to the 21 combat jobs would
  tell a server that already stores crafter sets to delete them. Old builds are safe by accident here:
  they never send the flag at all.
- **The codes agreed literally**, so neither side invents a spelling: `CRP BSM ARM GSM LTW WVR ALC CUL`
  (hand) and `MIN BTN FSH` (land), same uppercase three-letter shape as the combat codes. The server has
  to accept them *before* the plugin sends them — `GearValidator` rejects an unknown code locally too, so
  this is a coordinated change in that order.
- **Hand and land have BiS too**, so almost nothing about the comparison changes: same slots (the tool
  pair lands in `Weapon` and `OffHand`, which the map already has), same item ids, same materia.
  `BisComparer` needs no change at all — it knows a job only as a string. What differs is where a piece
  *comes from*: crafted or farmed rather than dropped, which is the sourcing side and already
  server-computed. What does need work is **keeping them apart in the interface**: combat jobs and
  hand/land are not browsed together, so the gearset lists in the BiS and advisor windows have to group
  or filter by role rather than presenting one list of thirty-two. That is a view decision only —
  grouping must never reorder what is sent, because `gear_index` is the player's own order.

It also widens what leaves the machine (R25/R27) — eleven more jobs' worth of item ids. The
justification is stated rather than assumed: it is what makes the deletion path honest. And there is an
upside beyond safety, which is not this repository's call to make: the website could then show crafter
and gatherer gear at all.

### A job the game adds later names itself

The map from `ClassJob` row to three-letter code was transcribed by hand and stopped at row 42.
Beastmaster arrived in the game afterwards, and a gearset whose job cannot be named is skipped where the
list is read: no warning, no entry, just a gap in the numbering that looks exactly like a deleted set. It
took half an hour of one session to work out that the gaps at 7, 8 and 18 were not all deletions.

The `Abbreviation` column that table was transcribed from is there at runtime, so `GameJobSheet` reads it
at startup and `JobMap.LearnFromGame` fills what the compiled floor does not know. **Only gaps.** A row
the floor names keeps the name the floor gave it, so no quirk in the sheet and no future renaming in the
game can turn `PLD` into something else under a running plugin. Four rejections, each for something a
sheet really contains: row 0, an empty abbreviation, anything that is not three uppercase letters, and a
code some other row already carries. The merge is a pure function, so the whole decision is testable
without touching the tables a running plugin is using.

Two things make this safe rather than a widening:

- **Naming is not permission to send.** `JobMap` answers "what is this?"; `JobPolicy`, built from the
  table the server published, answers "may I?". A job learned from the sheet is filtered out of every
  push until that table lists it, so the mechanism fails in the safe direction. It also had to teach
  `GearSanitizer`, which drops a set whose code it cannot name and runs *before* the policy.
- **The code comes from the English column**, because that is what the API speaks. The client shows a
  different abbreviation for **30 of 43** rows, and a client reading its own column would send a code no
  server knows for two thirds of all jobs. The diagnostics count that, so the choice rests on a number.

The report's `== jobs ==` section holds three lists against each other, what the game has, what the
plugin can name and what the server accepts, and prints only the disagreements, with both names, because
a row id is not something anybody can check. That block is the message to carry to the server side.

Beastmaster synced the day the server added `BST` to its table, with no plugin release in between, which
was the point. **One hazard belongs to whoever adds a job over there:** a client older than this
mechanism cannot name the new job but still fetches the table, works under it and therefore declares
`scope: all`. The server would read that as "the job is gone from the game" and park the row. It is the
same shape as the mistake `scope` itself replaced, one level further in.
