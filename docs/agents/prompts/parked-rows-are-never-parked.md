# Prompt: eight rows have no place in the game and cannot be removed

**For the API agent. One task and one question.** The reconciliation window works. It has nothing to
show because the state it reads is never set. The task is the missing step; the question is a contract
point that the same migration exposed.

## Please check the status first, and implement it if it is not already done

Measured again on **2026-08-24 at 20:00**, dev, read-only:

```
GET /gear/sets            8 rows in the position band at 100 and up, every one of them "state":"active"
GET /gear/review?character_id=29
                          {"state_token":"2bef8717...","held":[],"orphans":[]}
```

The `state_token` is byte for byte the one from 2026-08-23. Nothing has moved.

**And this is no longer a single sync's leftover.** A second push ran in between: the live rows carry
`updated_at 2026-08-23 22:23:38`, the eight stranded ones still carry `21:19:36` from the push that
stranded them. So a scoped sync that could have parked them has since run at least once and did not.

If the parking has been built since and simply has not run against this character, say so and say what
triggers it. If it has not been built, this is the task.

**What "done" looks like, so it can be checked rather than believed:** after the next push with
`scope: all`, those eight report `"state":"parked"`, `GET /gear/review` lists them in `orphans[]` with
`delete` among the offered verbs, and the `state_token` differs from `2bef8717...`.

## What the eight rows are worth, measured

Worth knowing before anything deletes them, because it turns out to be the reassuring answer.

- **All eight carry a pinned BiS target.** They are not empty clutter.
- **Every one of those targets already hangs on a live set of the same job.** "Set 27" (DNC) points at
  *Relic Weapon BiS* and so does the live DNC set; the two DRK rows point at *2.50 Relic Weapon* and so
  do all three live DRK sets; and so on for PCT, BLM, MNK, RPR and WAR.
- **No pin points at a `set_uid` the server does not have**, and **all 22 live combat sets carry a
  pin**. Nothing is left hanging on either side.

So a `delete` on these eight loses nothing that a live set does not already have. Nothing needs
relinking, and the player is not being asked to choose between two things they care about. That is the
best case, and it is also why leaving them is tempting and wrong: the player sees eight sets under
"Mein Gear" that they do not own, and has no way to remove them.

## What was measured

A read-only key on `dev.xivarsenal.app`, 2026-08-23, after a data sync, an adoption of the migrated
character, and one push from the plugin. The push is in the plugin log:

```
[18:25:50] INFO Job table from https://dev.xivarsenal.app/api/v1: 42 code(s), scope all.
[21:19:36] WARN The server could not identify 5 gearset(s) with certainty ...
[21:19:36] INFO Push OK: 34 gearset(s).
```

The adopted character carried 30 migrated rows. The push reported 34 live gearsets with
`scope: all` and the server answered on these rungs:

| rung | rows |
|---|---|
| `items` | 17 (every job with exactly one set: the migrated names are "Set 1" ... "Set 34", the plugin sends the real names, the items are identical, so "a renamed set") |
| `index` | 5 (every job with more than one set: 3x DRK, 2x WAR) |
| `new` | 12 (BLU and the 11 hand and land jobs, which the old 21-code scope never sent) |

17 plus 5 is 22 of the 30 migrated rows matched. **Eight were left over.** `GET /gear/sets` now shows
them:

| gear_index | job | name | state |
|---|---|---|---|
| 100 | DNC | Set 27 | **`active`** |
| 101 | PCT | Set 28 | **`active`** |
| 102 | DRK | Set 29 | **`active`** |
| 103 | BLM | Set 30 | **`active`** |
| 104 | DRK | Set 31 | **`active`** |
| 105 | MNK | Set 32 | **`active`** |
| 106 | RPR | Set 33 | **`active`** |
| 107 | WAR | Set 34 | **`active`** |

All 42 rows of that character report `active`. And:

```
GET /gear/review?character_id=29
{"state_token":"2bef8717...","held":[],"orphans":[]}
```

## The task

Those eight rows are the definition of `parked`, contract line 513:

> A row is `parked` when a sync happened that **could** have reported it and did not.

And line 516 settles the scope condition, which is the only thing that could have excused them:

> a push with `scope: all` can refresh and park any row.

The push declared `scope: all` and reported 34 rows. All eight leftovers are combat jobs, so they are
inside the reported scope on any reading. They must be `parked`. They are `active`, therefore they are
not orphans, therefore `orphans[]` is empty, therefore the player is never offered `delete` for a set
that no longer exists in the game.

**The window is not the problem.** Given what the server reports, an empty window is the correct
drawing. The missing step is one earlier.

**Where to look.** The server already did half of it: all eight were moved into the position band at
100 and up, which is the pre-identity mechanism for "no longer in the list". Whatever code performs
that move is exactly where the `parked` state belongs. The old step runs and the new one does not.

Worth checking at the same time, since it is the mechanism the parking rests on: `last_seen_at` is
written by every push that claims a row (contract line 510), deliberately not `updated_at`, because
MariaDB only advances that when a value actually changes and an unchanged set would look missing.

## What the plugin does meanwhile, and what it will not do

The plugin keeps a row in its resolution cache exactly when the state is `active` or `held`. These
eight report `active`, so it caches them. No harm today: no live set shares a job and a name with
"Set 27" to "Set 34". But the rule is only ever as good as the parking, and the failure is quiet. The
day somebody names a DRK set "Set 29", the weak key finds two candidates, and the real set loses its
identity until one of them is renamed.

The plugin will **not** work around this by ignoring rows in the 100 and up band. That would make the
position number carry meaning again, which is the thing this whole feature removed. The repair belongs
at the parking step.

## The question: should the server hold instead of guessing when items finds nothing?

The same push produced the five `index` rows above, and the owner expected a window for them. It did
not come, and by the contract it should not have. Line 100:

> Both are deterministic and harmless to data, both are worth a quiet marker.

And line 619, which answers the expectation directly and is worth keeping exactly as it is:

> **It does not stop** the case where a set at position 7 is replaced by a different set at position 7
> of the same job. That still resolves, with a marker. Its mistake is same-job ... Asking about it
> would produce a dialog nobody cares about, which is how people learn to dismiss dialogs.

That reasoning holds for normal use, and **it should not be weakened.** A migration is where it holds
least well, and the question is whether that case can be separated without touching the normal one:

**Today `index` covers two different situations.** In one, the items rung found more than one
candidate and could not choose: the sets really are near-identical, position is as good an answer as
any, and the marker is right. In the other, the items rung found **nothing at all**: the set was
re-geared since the last sync, so there is no evidence except the position, and the server is guessing
in the plain sense of the word.

Only the second one is the case where a human knows something the server does not. So the question is:
**when no candidate matches on items, should the server hold instead of matching on position?**

Arguments to weigh, not a proposal:

- It would not produce the dismissable dialog: the first situation, which is the common one, stays a
  marker.
- It sits close to the line the contract already draws for `held`: "exactly the case only a human can
  answer: a set renamed **and** re-geared" (line 627). A re-geared set whose position happens to be
  free is arguably the same case with one accidental hint.
- Against it: position is real evidence, not noise, and a player who reorders nothing gets a correct
  answer from it every time. Turning that into a question would ask about something the server usually
  gets right.
- And the timing matters either way. After the push, the matched rows carry the names the plugin sent,
  so the **next** push matches them on `exact` (same job, same name, same position). Whatever the
  position decided becomes permanent and unquestionable one push later. If a guess is ever worth
  asking about, that is the only moment it can be asked.

The plugin side has no preference it can defend from here. It is your call, and either answer is easy
to draw: a `held` row is already a question the window knows how to ask.
