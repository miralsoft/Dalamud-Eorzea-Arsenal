# The reconciliation window is built wrong, and how it should be built

Owner feedback on 2026-08-24, after the first real run with eight orphan rows. Written down before any
code, because the mistake is in the shape and not in the details.

## What is wrong

**The orphan half was built as an inventory and the player's task is a sequence of decisions.**

An inventory is the right shape when somebody wants to look something up. Eight rows stacked on one
page, each with three buttons, is twenty four buttons and no order to work through them in. The owner
called it a button battle and that is exactly what it is. The questions half already is a carousel, one
card at a time. Building the second half as a list treated it as a different kind of work. It is not:
both are "decide this, then the next one".

## A bug the same screenshot shows

All eight rows read **"war nie im Spiel"**. That string is what the window prints when `last_seen_at`
is empty. Those eight rows **were** in the game; the server simply sends no timestamp for them.

So a missing field is being rendered as a positive claim, and the one sentence meant to explain the
cause states the opposite of the truth. Absence of data must read as absence ("nicht bekannt, wann
zuletzt gemeldet"), never as a fact. Fix this even if nothing else changes.

## The shape to build instead

**One card at a time, for orphans as well as for questions.**

- Position and movement: "3 von 8", back and forward, so the player can look before deciding and can
  come back. Nothing is decided by moving.
- The gear on the card, as icons, the way `Gear vs BiS` shows a set. The current window hides it behind
  a "Was drin ist" toggle, which is the wrong default: the gear is the thing that lets somebody
  recognise a set they made months ago.
- A reason line, derived from what is actually known and never beyond it:
  - a timestamp is known: "zuletzt gemeldet am X, im Spiel gibt es das Set nicht mehr";
  - the row is hand made: "auf der Webseite angelegt, war nie im Spiel";
  - nothing is known: say that, and say what follows anyway.
- What hangs on it, plainly: the pinned BiS target (the data carries `HasPin` and the window does not
  draw it today), the team share, the hidden flag. That is what a delete costs, and it belongs on the
  card, not in a tooltip.
- The decision on the card, with the consequence beside each verb rather than under all of them.
- The full list stays reachable as a second view, for somebody who wants to clear thirty rows at once.
  Secondary, not the front door.

**For a question, show both sets side by side.** The proposed target next to the live set, gear against
gear, so "is this the same set" is answered by looking rather than by trusting a percentage.

## The limit that decides how far this goes today

`HeldGearset` carries `Items`. `OrphanRow` carries `Items`. **`ReviewCandidate` does not.** It carries
`Probability`, `MatchedSlots` and `TotalSlots`, which are numbers about an overlap, not the gear.

So the side by side comparison is **not buildable client side** for candidates in general. Partially:
where a candidate is also listed in `orphans[]` of the same answer, its items can be taken from there
by uid. For the rest it needs a field on the candidate, which is a request to the API side.

Decide that before building the question card, because it changes the card.

## Making a new discrepancy visible

The badge sits in a window somebody may never open, so a discrepancy that appears while the window is
closed is invisible. The owner proposed opening the window automatically the first time, not again
after it is dismissed, and again later only when something new appears. That is the right rule, with
two conditions.

**Key it on identities, not on the count.** One row deleted and one new one appearing keeps the count
at eight, and a count based rule stays silent exactly when it should speak. Remember the orphan uids
already shown, in the configuration so a restart does not undo it, and open when the open set contains
a uid that is not among them.

**Only open when interrupting is acceptable.** In the world, not in combat, not in a cutscene, not in a
duty. The plugin already defers the what's new window until a character is in the world; this needs
that guard plus the others. A window that opens over a pull is the thing people uninstall for, and it
would discredit a feature whose whole purpose is to be trusted.
