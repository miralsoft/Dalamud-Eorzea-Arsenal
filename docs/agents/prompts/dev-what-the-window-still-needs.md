# Prompt: three things the reconciliation window still needs from dev

**For the API agent. One decision, two confirmations.** The parking landed and works: measured on
2026-08-24, a push with `scope: all` parked the eight stranded rows, `GET /gear/review` now reports
`0 held, 8 open orphans`, and the `state_token` moved. Thank you, that closed the blocking item in
`parked-rows-are-never-parked.md`.

Only dev matters here. Live is deliberately out of scope for this development round.

The plugin side is now rebuilding the window around one card per decision, with the gear shown as icons
and the reason spelled out, instead of the list of rows and buttons it was. Three things decide how far
that can go.

## 1. The decision that is still open

From `parked-rows-are-never-parked.md`, unanswered so far, and it is the one that changes the plugin's
work: **should the server hold instead of matching on position when the items rung finds nothing?**

The distinction it rests on: `index` covers two different situations today. In one, the items rung
found more than one candidate and could not choose, the sets really are near identical, and the marker
is right. In the other, the items rung found **nothing at all**, so there is no evidence except the
position and the server is guessing in the plain sense.

Only the second is a case where a human knows something the server does not. The reasoning that keeps
`index` a quiet marker for the common case stays untouched either way, and it should: a dialog nobody
cares about is how people learn to dismiss dialogs.

Either answer is easy to draw. A `held` row is already a question the window knows how to ask. What the
plugin needs is the answer, not a particular one.

## 2. Confirm what a candidate actually carries on the wire

The contract specifies seventeen fields for an entry in `candidates[]`, including `items`, `has_pin`,
`has_team_share`, `team_names`, `hidden`, `state`, `proposed`, `blocked_by` and `url`. The plugin models
all of them.

**None of them has ever been seen in a real response**, because `held` has been empty in every reading
so far, so `candidates[]` has never been populated on dev. The new question card puts the proposed
target's gear next to the live set's gear, so the player can answer "is this the same set" by looking
instead of by trusting a percentage. That card stands or falls on `items` really arriving.

Please confirm that a populated `candidates[]` carries all of them, `items` above all. If any field is
specified but not yet implemented, name it rather than leaving it to be discovered when the first real
question appears in front of a player.

## 3. Confirm `last_seen_at` is written on every push that claims a row

The contract says so, and it is the field the whole explanation now rests on. The window is being
changed to say **why** a row has no gearset any more, and the honest sentence depends on it:

| Data | What the card will say |
|---|---|
| `source: manual` | made on the website, never in game |
| plugin row with `last_seen_at` | last reported on X, the set no longer exists in game |
| plugin row without `last_seen_at` | when it was last reported is not known |

The eight rows on dev carry no `last_seen_at`, which is expected: their history was rebuilt by the data
sync. Going forward it matters, because "last reported on 14 August" is what turns a mystery into a
memory: the player recognises the day they deleted that set.

This is also where the plugin was actively wrong until today. It printed **"war nie im Spiel"** whenever
the field was empty, which was a positive claim built out of missing data, and for those eight it stated
the opposite of the truth. That is fixed on the plugin side regardless. The confirmation asked for here
is only that the field will be there when there is something to report.
