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

It is indexed by two keys, neither of which includes the position:

| key | over | survives | used for |
|---|---|---|---|
| strong | job, name, item id per slot | being moved | the normal case |
| weak | job, name | being moved, re-geared | rows from `GET /gear/sets`, which carry no items |

A rename changes both, so the cache misses and the interface shows nothing until the next push, where
the server recognises the set on its items rung and the mapping is repaired. That is the intended
behaviour, not a gap.

Two live gearsets sharing a job **and** a name are reported as ambiguous rather than guessed at. The
server pairs such rows in position order; this side declines, because the position is precisely what
cannot be trusted here.

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
