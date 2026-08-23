# Prompt: make the what's-new note style a hard rule

**For the Foundation agent. A task.** Add the rule below to `rules/05-ai-conduct.md` beside the existing
I-series, under the next free number.

Read the correction in the next section first. An earlier draft of this prompt proposed banning the
em-dash in release notes as if that were new. It is not: **I-02 already covers it**, and covers it more
widely than the draft did.

## What is already covered, and what is not

**I-02 (no em-dash characters in prose)** already applies here. Its own text names the reach: "every
piece of prose written for a person to read, wherever it lives: documents, source comments, commit
messages, **strings compiled into a product**, generated output, and messages shown at run time." A
release note is a string compiled into a product. Nothing needs adding for the punctuation.

What is genuinely missing is the rule about **content**, and it is a different rule: a note may be
perfectly punctuated and still spend three sentences explaining itself.

## Why the content rule is being asked for

The 1.1.0 notes were written the way an assistant answers in chat: what changed, then why it is that
way, then how it used to be, then what it means for other cases, then a reassurance. A player reading
the "What's new" window wants the first clause and nothing after it. The owner's instruction, verbatim:

> Du solltest den Text so schreiben, dass dieser nur beschreibt was sich verändert und nicht erklären
> warum das so ist oder unwichtige Dinge erwähnen. [...] Es geht darum, dass der Spieler so schnell und
> einfach mit so wenig Text wie möglich die neue Funktion versteht. Da interessiert das drum herum nicht!

This is not a preference about prose. It is a rule about whose time the note spends.

## The rule

> **I-<next> A release note says what changed. Nothing else.**
>
> Every item is one line per language, in the form `"Headline: detail"`. The detail is one sentence
> naming the new behaviour, in the shortest form in which a player understands what is now different.
>
> A note must not carry:
> - **why** it is built that way, or what the mechanism is;
> - **how it used to be** (the note is read by people who never saw the old behaviour);
> - **who decides** something, or under which conditions the code takes which branch;
> - what it means for **neighbouring cases** ("anybody who only does X never sees this");
> - **reassurance** ("and nobody notices"), or any sentence that exists to pre-empt a worry.
>
> A subordinate clause is allowed only where the sentence would be **false** without it. "The plugin
> syncs all 42 classes and jobs, as soon as the server accepts them" keeps its clause for that reason,
> not as an explanation.
>
> **Length:** the detail stays at or under **160 characters**. If it does not fit, the note is trying to
> explain something. Cut the explanation, not the fact.
>
> Applies to every shipped language and therefore to any generated changelog. Applies to **new**
> versions only: released notes stay as the players read them.

## The check that belongs with it

The rule needs a test, or the next note breaks it again. The right place already exists:
`ChangelogJsonTests.NewNotesCarryAHeadlineTheSiteCanUse` on `release/v1.1.0`, which already holds the
`FrozenVersions` list such a check needs in order to skip released versions.

Add one `[Fact]` beside it, over the same non-frozen versions, asserting for each item and each language
that the body (the part after the first `": "`) is at most 160 characters. Name it for what it protects,
not for what it forbids, and have the failure message print the offending line the way the headline test
does.

## A second finding for the same rule, in the plugin repo

I-02 says the enforcement layer checks configured files and that a string reaching a person through a
compiled resource is covered by "the project's own test (C-11)". In
`miralsoft/Dalamud-Eorzea-Arsenal` that test **does not exist**, and the strings are not clean:

| Where | Count |
|---|---|
| shipped UI strings (`Localizer.cs`, DE and EN) | **67** |
| release notes (`ReleaseNotes.cs`, mostly versions 1.0.0 and older) | 21 |
| all source files including comments | 582 |

Every one of the 67 reaches a player at run time, which is exactly the case I-02 singles out. This is a
finding about the plugin repository, not a task for the Foundation, and it is recorded here so the two
halves are not discovered twice. The plugin side owns both the cleanup and the missing test.

## What the plugin repo already did

Version 1.1.0's five notes were rewritten to the content rule (commit `72dcee6` on
`feat/stable-gearset-identity`) and the public changelog regenerated from them. Before and after, for the
note that broke the rule worst:

- **before:** "Handwerker und Sammler kommen mit: das Plugin kann jetzt alle 42 Klassen und Jobs
  benennen statt nur der 21 Kampfjobs, und jede Übertragung sagt dem Server ausdrücklich, welchen
  Umfang sie abgedeckt hat. Was tatsächlich rausgeht, entscheidet der Server der jeweiligen Adresse,
  solange er die neuen Codes nicht annimmt, bleibt es beim bisherigen Umfang, und niemand merkt etwas
  davon."
- **after:** "Handwerker und Sammler kommen mit: das Plugin überträgt alle 42 Klassen und Jobs, sobald
  der Server sie annimmt."
