# Prompt: the dev sync and the plugin cannot see the same character

**For the API agent. A finding plus one small task.** Nothing below is a defect in your sync. The sync is
deliberate and well argued, the plugin is doing what its contract says, and the two simply do not meet at
one field. This page says where, with proof, and offers three ways forward so the owner can pick one.

## What was measured

An API key for `dev.xivarsenal.app` was used read-only on 2026-08-23. `GET /gear/sets` returned 65 rows
across three characters of the one account:

| character_id | rows | names | state |
|---|---|---|---|
| 28 | 30 | "Set 1" ... "Set 34" | `active` |
| 29 | 1 | "Set 1001", `source: manual` | `null` |
| 30 | 34 | the real gearset names | `active` |

The plugin's stored mapping knows exactly one of them: character 30, `cid_hash e9cc011f...`.

The other two are provably clones. Brute-forcing the derivation in `OwnerClone::cloneCharacters`:

```
sha256("devclone:34:1") = 7b6680b3b2b399afd261cbfcf2610b9a9e38b62ec0b95d018b6e36939fd48575  -> character 28
sha256("devclone:34:2") = cbc525947f33ac5f0515e36f1343a09f1aa8e5d6764f880270cf00b72a1036c2  -> character 29
```

So dev user 34 (`devtest`) holds clones of the owner's characters 1 and 2, and character 30 is the one
the plugin created for itself on its first push.

## Why the plugin can never reach the migrated data

Two deliberate decisions, each right on its own terms, combine into a wall:

1. `db/dev-sanitize.sql` sets `cid_hash = NULL` on every character. Correct: a `cid_hash` is a
   pseudonymous identifier of a real person and has no business in a snapshot that leaves the server.
2. `OwnerClone::cloneCharacters` substitutes `sha256('devclone:' . $dev . ':' . $oldId)`. Correct too,
   and the doc comment gives the reason: a copy is a copy, so it must not share identifiers.

The plugin identifies a character by exactly one value: the `cid_hash` it computes from the game. That
value is the one thing the snapshot no longer carries. So on first push the server finds no character
with that hash, creates a new one, and mints fresh identities. The migrated gearsets sit on characters
the plugin cannot address, and the plugin's own character starts empty.

The stated goal ("die Daten sind eins zu eins die gleichen und das Plugin kennt das") is therefore not
reachable as the sync stands. Not because anything is broken, but because the field the plugin
identifies by is deliberately erased.

## Three ways forward

**Option A, change nothing.** The plugin tests against its own character on dev, with a real hash and
server-minted identities. That is a good plugin test: it exercised minting and then re-matching, and 34
of 34 sets came back `exact` on the second push. The migrated data stays what it is for, which is the
website views. Cost: the account carries clone characters whose rows arrive in every `GET /gear/sets`
answer. The plugin now drops them itself (commit `05ce0ff`), so the cost is only payload size.

**Option B, a dev-only adopt tool.** Recommended. One command that hands a migrated character over to
the plugin:

```
php tools/dev/adopt-character.php <character_id> <cid_hash>
```

It sets that character's `cid_hash` to the value the plugin computes, with the same local-or-dev guard
`finish-import.php` already uses. Run it after a sync and **before** the plugin's first push, and the
plugin then attaches to the migrated character, matches the migrated sets on the `items` or `name` rung
and adopts them. That is the one to one the owner asked for, and as a side benefit it is the only way to
exercise the interesting rungs at all: today everything answers `exact`, so `items`, `name` and
`name_ambiguous` are never seen in a real run.

The owner reads the hash from the plugin's diagnostics panel (`/xivarsenal log` in a developer build). No
push is needed first, which is what makes the ordering above possible.

Two details worth getting right in the tool:

- **Refuse when the hash is already taken** by another character of the account, and say which. Setting
  it after the plugin has already pushed would collide with the unique key, and the useful message there
  is "character 30 already holds it, delete it or pick another".
- **Leave `lodestone_id` NULL.** The clone is still not verified against the game, and adoption is about
  addressing, not about verification.

**Option C, exempt the owner's own characters from the `cid_hash` scrub.** Cheapest to implement, worst
to reason about: it puts a real game identifier into every snapshot that leaves the server, including the
ones a developer copies to a laptop. Only worth considering if the sync were ever narrowed to the owner's
own data. Mentioned for completeness, not recommended.

## Two smaller asks, unrelated to the above

**`GET /gear/sets?cid_hash=` is accepted and ignored.** The plugin sends it and the answer is byte for
byte identical with and without it (16916 bytes both ways). Either honour it or drop it from the
contract, so the two sides agree on what the parameter means. The plugin no longer depends on it either
way: it now discards rows whose `cid_hash` is not the one it asked for, because a foreign row that shares
a job and a name with a real set would make that set ambiguous and leave it with no identity at all.

**A row came back with `"state": null`** (character 29, `source: manual`, `gear_index: 1000`). The
contract names four states and `null` is not one of them. The plugin survives it: it probes the whole
answer for any non-empty state and falls back to the source rule when a server sends none, so a single
stateless row is simply not cached. Still worth giving hand made rows a state, otherwise the rule "a row
belongs in the resolution cache exactly when its state is active or held" has a hole in it.
