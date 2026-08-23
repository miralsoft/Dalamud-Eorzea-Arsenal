# Prompt: make the what's-new note style a hard rule

**For the Foundation agent. A task.** Add the rule below to the Foundation rule list under the next free
number (R43 is the highest one this repo currently cites), and keep the wording as terse as the rule
itself demands.

## Why this is being asked for

The 1.1.0 notes were written the way I write to the user in chat: what changed, then why it is that way,
then how it used to be, then what it means for other cases, then a reassurance. A player reading the
"What's new" window wants the first clause and nothing after it. The user's instruction, verbatim:

> Du solltest den Text so schreiben, dass dieser nur beschreibt was sich verändert und nicht erklären
> warum das so ist oder unwichtige Dinge erwähnen. […] Es geht darum, dass der Spieler so schnell und
> einfach mit so wenig Text wie möglich die neue Funktion versteht. Da interessiert das drum herum nicht!

This is not a style preference about prose. It is a rule about **whose time the note spends**.

## The rule

> **R<next> — A release note says what changed. Nothing else.**
>
> Every item in `ReleaseNotes.cs` is one line per language, in the form `"Headline: detail"` (the
> existing headline rule, unchanged). The detail is **one sentence naming the new behaviour** — the
> shortest form in which a player understands what is now different.
>
> A note must **not** carry:
> - **why** it is built that way, or what the mechanism is;
> - **how it used to be** (the note is read by people who never saw the old behaviour);
> - **who decides** something, or under which conditions the code takes which branch;
> - what it means for **neighbouring cases** ("anybody who only does X never sees this");
> - **reassurance** ("and nobody notices"), or any sentence that exists to pre-empt a worry.
>
> **No dashes** (`—`, ` - `) in a note. A dash is where the forbidden second half gets attached; banning
> the punctuation is what makes the rule self-enforcing while writing.
>
> **Length:** the detail stays at or under **160 characters**. If it does not fit, the note is trying to
> explain something — cut the explanation, not the fact.
>
> Applies to **both languages** and therefore to the generated `changelog.json`. Applies to **new**
> versions only: released notes stay as the players read them (see the frozen-versions rule).

## The check to add with it

The rule needs a test, or it will be broken again by whoever writes the next note — me included. The
right place already exists: `ChangelogJsonTests.NewNotesCarryAHeadlineTheSiteCanUse` on
`release/v1.1.0`, which already holds the `FrozenVersions` list this check needs to skip.

Add one `[Fact]` beside it, over the same non-frozen versions, asserting for each item and each language:

- the body (the part after the first `": "`) is at most 160 characters;
- neither the title nor the body contains `—` or ` - `.

Name it for what it protects, not for what it forbids — the failure message should say *"a note says what
changed, nothing else"* and print the offending line, the way the headline test does.

## What this repo already did

Version 1.1.0's five notes were rewritten to the rule (commit `72dcee6` on
`feat/stable-gearset-identity`) and `changelog.json` regenerated from them. Before and after, for the
one that broke the rule worst:

- **before:** "Handwerker und Sammler kommen mit: das Plugin kann jetzt alle 42 Klassen und Jobs
  benennen statt nur der 21 Kampfjobs, und jede Übertragung sagt dem Server ausdrücklich, welchen
  Umfang sie abgedeckt hat. Was tatsächlich rausgeht, entscheidet der Server der jeweiligen Adresse —
  solange er die neuen Codes nicht annimmt, bleibt es beim bisherigen Umfang, und niemand merkt etwas
  davon."
- **after:** "Handwerker und Sammler kommen mit: das Plugin überträgt alle 42 Klassen und Jobs, sobald
  der Server sie annimmt."

The condition survives in the "after" line because without it the sentence would be **false**, not
because it explains anything. That is the only kind of subordinate clause the rule permits.
