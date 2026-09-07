# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-08-26

Gearsets carry a server-minted identity instead of being keyed on their position in the list. Everything
that was attached to a position (pinned BiS target, team share, hidden flag) now follows the set itself.

### Added
- **Gearset identity.** A push carries no uid; the server matches each set and answers with one, on a
  named rung (`exact`, `name`, `name_ambiguous`, `items`, `index`, `new`). The plugin caches the mapping
  per character and resolves a live set back to it through its own short ladder: first where the set sits,
  then what is in it. Position and job with the same gear settles it, which survives a rename and is the
  only thing that separates two sets a player never named apart; position and job with the same name
  settles it where that name belongs to no other set, which survives a re-gear and declines where a swap
  of two same-named sets could produce a confident wrong answer. Anything moved falls through to the
  strong key (job, name, items) and the weak one (job, name). Every rung reports ambiguity rather than
  guessing, the strong key included: same job, same name, same gear is the best evidence the contents can
  give and it is still not a distinction.
  The position had been left out of the cache on the grounds that reordering makes it unreliable. That
  reasoning skipped a fact about the game: the player is never asked for a name when a set is made, so the
  game writes the job in and two sets of one job are called the same thing from the start. Asking the name
  to tell them apart was asking it to do work it was never able to do, and a freshly copied set lost its
  BiS comparison until somebody renamed it by hand. Only a reorder moves the number, and a reorder is
  exactly the case the content ladder was built for. The server needed no change: a push is index-aligned
  and `GET /gear/sets` already returns `gear_index`; neither was being kept.
  The ladder is ordered by how specific each rung is and not by how sure it is, which took a second look
  to get right. The position is the surer evidence and the strong key the more discriminating one, and
  putting the surer first meant a set whose full description matched another row exactly was handed to
  whoever happened to sit at its number. Copy a set, rename the copy, reorder: two sets hold the same gear
  under different names, the position rung ignores the name by design, and it answered with the
  neighbour's identity and no doubt attached. The strong key goes first and the position answers what the
  contents cannot.
- **The server's guesses are checked against what this side remembered.** Its `name_ambiguous` rung means
  it searched by name, found more than one candidate and broke the tie by position; its `index` rung means
  the name and the gear both failed and only the position was left. Both are guesses on the position, and
  the position is what a reorder has just changed, so they are least reliable in exactly the situation
  this feature exists for. The server cannot check itself there, because from where it stands the two
  candidates are the same. This side holds one thing it does not: which gear sat under which identity at
  the last push. Where a gearset's contents identified one uid on their own and the answer has just put
  them under a different one, the two accounts contradict each other, and neither is provably right, so
  both identities stop being used until they agree again. Until now the only sign was a log line once per
  session advising that the sets be given different names, which is no advice at all: the game names a new
  gearset after its job, so the player never chose those names and two sets of one job share one from the
  start. Four conditions have to hold together, and the ones that keep it quiet matter more than the one
  that makes it speak: a rung worth checking, gear that identified exactly one remembered row, an identity
  that differs from the remembered one, and a remembered identity that did not keep that same gear in this
  same push. The last is what tells a copied gearset from a mix-up, and copying is the most common thing a
  player does here. A pair of true copies stays silent throughout, because their gear was never a
  distinction and saying so anyway would be noise on the one case that is honestly undecidable. Recomputed
  on every push, so the doubt lasts exactly as long as the evidence for it and nobody has to clear
  anything.
  "A rung worth checking" is not the same as "a rung the server called uncertain", and reading it that way
  left the check firing only where the server had already owned up. Its `exact` rung is job, name and
  position together: for a set whose name is its own that is strong evidence, and for one of two sets
  sharing a name it is the position and nothing else. Swap two such sets and the server matches both on
  `exact`, confidently and the wrong way round, never reaching a rung it would have called a guess. So any
  rung at all is checked once a second set in the same push carries the same job and name, which since the
  game names a set after its job is the ordinary case rather than a corner of it.
- **Answering one question no longer costs the character's gear knowledge.** A decision in the
  reconciliation window can move an attribution, so the mapping was dropped and read again rather than
  left to expire. Dropping it was too much: a read carries no items, so everything derived from them went
  with it, and each row came back holding its identity, its job and its name and nothing else. The gear is
  what separates two sets of one job, which the game names identically the moment they are made, so
  answering a single question turned every same-named pair on the character unresolvable and left them
  that way until the next push. Five sets lost their BiS comparison to one click. Expiring the schedule
  says the same thing without the loss: the merge already drops rows the server no longer lists, so an
  identity that was just linked away goes, and everything still listed keeps what a push established.
  Forgetting outright stays for the case that means it, such as disconnecting.
  The same confusion sat at three call sites, and only measuring found the third. Linking a row is the one
  decision that really does retire an identity, so the service that sends it dropped the cache as well,
  and that is the site a link actually reaches: every link cost the whole character's gear knowledge, and
  a test demanded exactly that. The stale row still goes, because a refresh drops what the server no
  longer lists, and that is precisely the row a link retires. The developer window's
  "re-read identities" had the same fault for the same reason: forgetting first sounded like the thorough
  version and was the destructive one, so pressing the button that diagnoses a mapping problem created
  one. It now does what its label says.
- **Answering a question no longer blanks every set number on screen.** The position table was emptied
  after each decision and refetched right afterwards, and for as long as that took, no gearset anywhere in
  the interface carried a number. A missing number is not nothing: it is how this window says a set is not
  in the game any more, so putting one row aside announced that about every set the player owns, for a
  second, untruthfully. What the emptying guarded against is a number that has moved, and only a link can
  move one; even then the row it retires is the row whose card just closed. The refresh is what makes the
  state right and it runs either way, so the last known table stays until it lands. Stale for a moment
  beats wrong for a moment, which is the same rule the rest of this plugin follows when a refresh fails.
- **Rows put aside are counted in words.** The heading read "Put aside (1)", and a bare number in brackets
  is the one counter shape this interface does not use.
- **The menu no longer offers decisions that are already made.** The badge prefers the review service's
  own reading over a push answer, and rightly: answering a question in the window moves the state without
  any push, so a badge fed only by pushes keeps announcing work that is done. The other direction was left
  open. A push moves the state too, and the reading then went stale with nothing to notice it. Delete a
  gearset and make it again: the server attributes the new one to the row it left behind, that row stops
  being an orphan, and the menu went on saying "two decisions" and led to a window with nothing in it. The
  state token settles it and exists for exactly this, fingerprinting which rows are being asked about
  rather than their contents: where it disagrees with the cached reading, that reading is over and the
  badge falls back to the push answer, which is the newer of the two. An ordinary push that only writes
  fresh numbers keeps its token and changes nothing here.
- **A refresh also says which attributions are still open.** Which gearsets the server is waiting on came
  from a push and from nothing else, and it is not kept across a restart. So every reload began believing
  nothing was outstanding and stayed that way until the next push happened to mention it: a set whose
  attribution was open showed its provisional target as though it were settled, and the diagnostics dump
  read "none open" with a card sitting in the review window. `GET /gear/sets` carries the state per row and
  was not being read for it. A server that reports no state still clears nothing, since silence about what
  is open is not the same as saying nothing is.
- **A refresh no longer forgets the gear when a name moved.** `GET /gear/sets` returns no items, so the
  ten-minute refresh keeps whatever a push established rather than writing a thinner row over it. That
  only held while the name matched: rename a set on the website and the cached row was rebuilt from the
  read, losing the strong key and the gear key together. The strong key has to go, since the name is part
  of what it hashes. The gear key does not, and a rename is the one case it exists for. Worse, the game
  keeps calling the set what it always did, so after such a refresh the cached name and the live name
  disagree and both name rungs retire too: the gear was the only thing left that could still recognise it,
  and it was the one thing being thrown away. It now carries over, and so does a contradiction found by
  the cross-check, since a read brings no gear and cannot settle what it cannot see.
- **An identity claimed by two live gearsets is withdrawn from both.** The mapping declines where two
  remembered rows fit one live set, and that is the only doubt it can see; the other direction is
  invisible to it, because two live sets fitting one remembered row are two separate questions with one
  confident answer each. Copy a gearset and the copy is indistinguishable from the original until the next
  push tells the server it exists. The position table wrote the second over the first, so it stayed full
  and pointed at whichever came last, and a wrong set number with somebody else's BiS target beside it
  read exactly like a right one. Both claims are dropped now and both positions say they are undecided,
  which is the same rule the mapping already followed one layer up. Moved out of the service into
  `LivePositionMap` on the way, so the rule is proven rather than read.
  The comparison had its own copy of the same fault and did not learn from the fix, which a live report
  showed within the hour: the position table dropped all the claims on a doubled identity while the
  resolver the comparison consults answered the same way as before, because it went straight back to the
  mapping and never asked the table. So a BiS target was still drawn against one gearset and labelled with
  another one's number, and only one of the copies was reported as having no target. The resolver now
  treats a withdrawn position as no identity, and the comparison's own index refuses a second claimant on
  the same key instead of letting it overwrite the first.
- **Job scope.** `GET /gear/jobs` decides which of the 42 class and job codes may be sent; a server
  without the route leaves a frozen 21-code combat floor in place. Every push declares which range it
  covered, so the server can park what a sync could have reported and did not.
- **The reconciliation window.** Questions as a carousel, the inventory of rows with no gearset as one
  card per decision, with the gear as icons, why the row is there, what hangs on it, and the six verbs
  the contract defines (`link`, `new`, `ignore`, `reopen`, `delete`, `release`). It opens by itself once
  for a row nobody has seen, keyed on identity rather than on a count, and only at a moment where
  interrupting is acceptable.
- **Two sets side by side, slot by slot.** A row the game no longer reports is drawn against the set it
  resembles (`orphans[].similar[]`); a question is drawn against each candidate. Three states per slot,
  because the server compares the item and not the materia: same, same item with other melds, different.
  The colour never carries it alone, the counts are written out and every slot names its state on hover,
  and the numbers stay the server's own (`probability`, `matched_slots`, `total_slots` are shown, never
  recomputed).
- **Role groups in the gear window**, and sets with no BiS target are listed instead of being absent.
- A fourth release-note kind, `Removed`, which the framework profile names alongside the other three.
- `images/icon.png` ships inside the package. The packager does not descend into subfolders, so it is
  appended after packaging; without it every installed copy showed Dalamud's default icon.
- **The language follows Dalamud unless somebody says otherwise.** It never read the host at all: the
  default was English, so anybody running the game and Dalamud in German was greeted in the wrong one and
  had to find a setting to fix it. "Same as Dalamud" is the first entry and the new default, and it names
  what it currently resolves to in brackets, because otherwise it is a choice made blind. A host language
  with no catalogue here lands on English rather than on raw keys. Existing installations keep an explicit
  German; a stored English moves onto following the host, since that value is both the old default and a
  real choice and cannot be told apart, and on an English host the two resolve the same anyway.
- **A diagnostics report in the developer build.** Identity, the uid-to-live-position table the set
  numbers are drawn from, and the whole reconciliation state down to each candidate and each similar row,
  in plain lines, with a button that puts the lot on the clipboard and buttons that re-read each cache.
  Built after a bug cost a dozen guesses: two gearsets shared a job and a name, one identity swallowed
  both, and two windows printed the same set number for two different sets. Every value needed to see
  that was in memory and none of it was anywhere a person could read, so diagnosing it meant asking for
  another screenshot and reasoning from it. The report is composed as text and then drawn, so what is
  copied is exactly what was on screen. It is a window of its own rather than a section above the log,
  because the two are read differently: the log is an account of what happened and is read from the top,
  the report is the state right now and is read against what is on screen. The wrench that opens it sits
  in every window title bar, driven off the window registration so one added later carries it without
  anybody remembering to say so. Absent from a released build like every developer surface here: not in
  the assembly, rather than switched off in it. Its shape is a fixed bar of buttons over one output area:
  the first version drew every section inline and had the fault it was built to remove, since finding a
  value meant scrolling and reaching a button meant scrolling back. A probe replaces the output, the copy
  button takes it, and one of the probes is "duplicates", which is the diagnosis above in a single press
  rather than in a dozen exchanges.

### Fixed
- **Switching character asked the server once per frame.** The guard that stops the window showing one
  character's questions while another is on screen re-read inside `Draw` with nothing holding it back.
  The service has a lock, so the calls could not overlap, but they queued rather than being dropped and
  every one of them became a request: the switch would fire them as fast as the server answered until the
  rate limiter closed the door, which is exactly when the new character needs its questions read. Once
  every two seconds now, still on a timer rather than once ever, so a failed read is tried again.
- **And it explained itself with the word "Re-read".** That is the label off a button, shown as the whole
  content of an otherwise empty window. It says which character it is reading for and why.
- **"Take it out of the plugin" was printed on a row already out of the plugin.** The share line carried
  its consequence unconditionally, so a released row, whose only remaining verb is "ask about it again",
  was told to do two things that are not on it. The fact and the consequence are separate sentences now,
  and the second one appears only where a delete could otherwise have been pressed.
- **"Nothing open" was not said when anything had ever been put aside.** The line hung on a condition that
  counted archived rows, so a player with an archive got no word at all: the last answer landed, the cards
  vanished, and the window closed two seconds later with nothing having said it was finished. Rows in the
  archive are decided, and "nothing open" is true beside them.
- **A confirmation opened with the result instead of the deed.** The team line came first, on the
  argument that the part reaching somebody outside the room comes first. That argument belongs to a
  delete, where the word on the button is wrong and has to be corrected before anything else; everywhere
  else it put what follows above what was pressed and left the reader working backwards. The order is now
  what somebody agrees to, then what it costs, and a test holds it rather than a comment.
- **The confirmation for a release never said what a release does.** With a team it listed only what the
  team would keep seeing; with neither team nor pin it came out empty and fell back to repeating the
  label on the button. The one thing the press actually does, that the row stays and stops being the
  plugin's and stops following the game, was in none of them. It is said on every release now, and on a
  delete that a share turns into one.
- **The advice recommended deleting on a row where delete is not offered.** Each verdict ends with a
  sentence about what to do and every one of them names deleting, so the card said "otherwise delete it"
  two lines under "this cannot be deleted". Where a team follows the row that closing sentence is
  replaced by the thing a reader would ask next: drop the share on the website first, and then it can
  really be deleted.
- **A team name stood in a sentence with nothing saying it was a team.** "Programmiertest folgt dieser
  Zeile" reads as a word rather than as somebody else, and a team can be called "Test" or "Mo". Every
  line that names one says "the team" or "the teams" now, with each name in quotes, and singular and
  plural are separate strings because "the team A, B" is not a sentence.
- **Delete is no longer offered on a row a team follows.** Its tooltip promised "permanent, the row and
  everything on it is gone" on the one row where nothing of the sort can happen: the server carries a
  delete out as a release there, so that one person cannot remove a set other people depend on. The first
  fix was to say so on the button, and that was the wrong fix. A press would still have worked and done
  something else, and the button that does that something else was already on the card with the right
  word on it: two buttons doing one thing, one of them lying about it, is not a choice. `OfferedVerbs`
  already leaves delete out where the server refuses it, on hand-made rows, and this is the same category
  one step removed. Not the refusal the contract rejected either, which was about the server erroring and
  leaving the player to un-share on the website first: nothing is blocked, the path is one button to the
  left, and the card says why this one is missing.
- **"Otherwise delete" was printed above the reason not to.** A row a team follows, or one carrying a
  pinned target, said so under the gear grid, four lines below the advice that ends by suggesting the
  delete. The old argument for that position was that the warning must not hide behind a hover, which is
  right and not enough: in plain sight below the reason to act is still below it. Both lines come before
  the advice now.
- **A row put aside could not be reached again.** Putting one aside has an undo, `reopen`, and the status
  entry was the only route into the window: it appeared while something waited, so once the last thing
  waiting was the archive itself the entry was gone and the archive with it. A button whose undo cannot
  be reached is a one-way door however the contract describes it, which is the fault `release` had until
  `released_at` existed. The entry stays while anything is archived, quiet instead of yellow and saying
  how many rows are in there rather than how many decisions wait.
- **The reconciliation window closes itself once nothing is left to decide.** It is only ever reached
  from the status entry, and that entry appears only while something waits, so after the last answer
  there is nothing to come back for and leaving it open made the player dismiss a window whose whole
  content was the word "done". It waits a couple of seconds first: the answer that emptied it has just
  landed, and the line saying so, the count of questions that settled themselves with it included, would
  otherwise be gone in the frame it appeared. A window that vanishes on a press reads as a mis-click. It
  stays open while a call is in flight, while the archive of put-aside rows is unfolded, and for anyone
  who opened it with nothing waiting, since they came for the archive rather than to answer something.
- **The delete confirmation warned about losing a pinned target the row did not have.** On a row with no
  pin and no share that sentence was the only line in the dialog, and it was about something that was not
  there; nothing said the row itself was being removed, and nothing said the gearset in game is not
  touched, which under a button labelled "delete" is the question somebody actually has. The pin sentence
  is now conditional on there being a pin, and every delete carries the plain one.
- **A target with no gearset in game was drawn as a comparison against nothing.** The list said "0 of 11
  slots match" with all eleven pieces in red, and the shopping list counted a whole set as still to buy;
  both are verdicts about a set that does not exist. Three ways to land there and all of them ordinary: a
  row built in the web app for a set not made yet, one left behind by a gearset deleted in game, and one
  waiting on an answer. The window is called gear against BiS, and with no gear there is nothing to draw,
  so they are left out of all three views. Nothing is hidden by that: each is already a card or a
  candidate in the reconciliation window, which is the window whose job they are.
- **A gearset that could not be identified was told to pin a target it already had.** Such a set cannot
  be claimed by any target, so it falls into the "no target pinned" list for want of a match, and that
  list offered the one remedy that does nothing here. It gets the true reason instead, with the remedy
  that works: give one of the two a different name. Same principle as the two reasons that list already
  told apart, that a wrong remedy is worse than none.
- **A gearset the plugin cannot identify now says so, instead of only going quiet.** Where two of the
  player's own gearsets share a job and a name, this side declines to pick between them, which is right
  and also means their BiS comparison stays empty. Nothing said why: the two warnings that existed repeat
  what the server reported about its own matching, and the server has distinct rows and reports no doubt
  at all, so the local blindness was invisible and the empty comparison read as a fault. The status
  window counts it for itself now and names the remedy, which is to give one of each pair a different
  name.
- **Every set number in the BiS window was one too low.** The API and the game module count gearsets
  from zero and the gearset list shows them from one; the reconciliation window has added the one for a
  while, this one never did, so each row named the gearset above the one it was about. And a row with no
  gearset in game is drawn without a number now instead of with its stored one, which for a hand-made row
  is a band value like 1000 and for a parked row is where it used to sit.
- **Two gearsets with the same job, the same name and the same gear resolved to one identity.** The
  strong key, job plus name plus items, was searched by returning the first row that matched it. Where
  two stored rows carried the same one, that first row was handed back with the rung reported as exact.
  Three pairs of a real player's gearsets did exactly that: the position table wrote one identity twice
  so the last write won, the BiS window drew three sets against the wrong targets, and nothing anywhere
  reported a doubt, because the doubt was never detected. The weak key had this guard from the start;
  the strong key now has the same one. Same job, same name, same items is the strongest evidence this
  side has and it is still not a distinction, so where it matches twice the answer is that this side
  cannot tell.
- **The bulk accept sent a pairing the window had just said it would not send.** A question with several
  candidates and no vouching may not ride along, and the panel showed exactly that: the line greyed out,
  a sentence saying the decision belongs on the card, no way to pull it in, "2 of 2" on the button and two
  pairings in the confirmation. Then the request was composed from the player's own strikes alone, because
  the safety rule had been computed in the drawing code and never reached the builder, and three pairs went
  to the server. The rule now lives in `MappingToAccept`, which is what composes the request, and the panel
  reads back the pairs it produced rather than deciding a second time which those are. Two tests hold it:
  a contested question stays out of the mapping unstruck, and a lone candidate stays in whatever it scores.
- **The tests that missed it were testing the wrong layer.** They asserted what the rule composes, and the
  rule was right: the two callers fed it different inputs, which a test on the rule cannot see. Six tests
  now assert the payload the service actually sends, including one that compares it pair for pair against
  what the rule composes over a state carrying every shape at once. Removing the guard turns three of them
  red, which is the only evidence that a regression test is one.
- **The button counted against something other than what it would send.** Its denominator was the number
  of questions that look safe, which is not the number that can be pairs: a question the server sent no
  proposal for passes the safety rule and can never be applied, so with nothing struck out the button read
  "(2/3)" and no press could reach three. Both halves come from the same rule now, once with the strikes
  and once without.
- **Only a delete may say "this cannot be undone" now.** A link is one-way as a verb, and that was taken
  as licence for the strongest sentence the window has. It is not the same claim: the target survives with
  its uid, its pin and its shares, and the set it used to hold can be built again in the web editor. What
  a link earns is a second look, which is `NeedsConfirming`, the wider list the two predicates were split
  apart to make possible. A test now holds the pair from the other side as well, so nothing can claim the
  strong sentence without also asking twice.
- **The bulk confirmation said "this cannot be undone" by construction.** Its test was "there is no single
  decision here", which is not a question about consequences. A bulk carries attribution and nothing else:
  no row is removed, every target keeps its identity, its pin and its shares. It asks rather than warns,
  and the strong sentence keeps its credit for the verb that earns it.
- **The bulk accept applied what the card had just called unbacked.** The server marks a proposal it does
  not vouch for, the card says so and asks the reader to check the gear, and the shortcut two lines higher
  offered to accept twenty of them unlooked at. A question with several possible rows and no vouching is
  now listed but never applied, and cannot be pulled in either: `/gear/review/accept` takes only the pairs
  the server proposed for this token, in one transaction, so the alternative could not be offered there
  even as an option, and pressing a button to accept a pick whose alternative is off screen is the blind
  decision the rest of the window refuses to make. That pairing belongs on the card, where the picker is.
  A lone candidate rides along whatever its score, because nothing is being decided there and the person
  the shortcut exists for, who left the website alone for a year, has low scores on every row.
- **The bulk accept showed a count where it needed to show the mapping.** "Accept all four proposals" meant
  paging through four carousel cards first and holding them in your head, and the strike-out that makes the
  door safe was scattered one per card, so the one screen that should have shown the whole mapping never
  existed. It is now the list itself: one line per pairing, the set out of the game and the row it becomes,
  each strikeable, and the confirmation repeats the lines rather than a number.
- **The question card says what the question is and which button answers it.** It had the comparison, the
  percentages and four controls, and nothing that told a first-time reader which of them was theirs. Now
  two sentences come first: what arrived and could not be placed, and what the server makes of it. Whether
  a button may be named as the ordinary answer is a rule in the core (`QuestionAdvisor`), and it says yes
  only where the server vouched for its own suggestion: at 75 % it proposes without vouching, and a card
  that reads the two the same puts its own certainty on somebody else's guess.
- **The question card was brought onto the inventory card's shape.** One headline with the name, the
  server's percentage and the breakdown this side counts; the same tiled grid; an eye that turns the
  comparison round so the row being offered can be looked at whole, which is the only way to answer "is
  this that one" by seeing rather than by trusting a number. The verbs became icons that say what they do
  and what it costs, the two about the question sit in the bar at the top beside the arrows, and the two
  about a candidate stay with the candidate they name.
- **"Take out of the plugin" explained itself with the behaviour that was fixed before release.** The
  sentence beside it promised that the next sync would write a fresh row for the set in game, so the player
  would end up with two. That was the pre-resolution behaviour of `release` on a held row, raised against
  the server as open item 6 and answered: the row becomes `manual` **and** `ignored`, keeps its pin and its
  shares, and never raises a question again. The same sentence was also hung on the inventory card, where
  there is no set in game at all for a fresh row to be written for. Two sentences now, one per card, each
  claiming only what the contract states. The verb is renamed too, because "take it out of the plugin" over
  a gearset that is plainly still in the game reads as impossible: what leaves is the governance, not the
  set.
- **Three things on the question card said less than they knew.** The row's origin sat behind a bare
  "(?)", which reads like a string that failed to load and hides the one fact that explains a score in the
  teens: the row was typed on the website and was never in game. It is written out now. The line of counts
  went green on the proposed candidate, so a reassuring colour sat over the words "9 different"; it is
  quiet in every case, and that a row is the suggestion is said in words by the picker and by the sentence
  at the top. And an empty tile was outlined in the off hand of a job that has none, which invites the
  reader to look for missing data; only that one slot loses its outline when neither side fills it, so a
  Paladin's shield and a crafter's second tool are unaffected and nothing in the layout moves.
- **"On 2 of them the set from the game replaces the stored contents" named a consequence nobody could
  picture.** The warning over a bulk accept now says what is actually at stake: these pairings land on sets
  built by hand on the website, and the gear stored there is replaced by the gear from the game. Singular
  and plural are separate strings, because German has no honest single form and "Bei 1 Zuordnungen" is
  the kind of seam that makes a warning read as machine output. It no longer claims the step cannot be
  undone: nothing is destroyed, the row and its contents remain editable on the website, and a warning
  that overstates its case teaches people to read past the ones that do not. The button under it counts
  the same way:
  "Accept all 1 proposals" was ungrammatical and, worse, silent about the pairing struck out just above it,
  so it names what is selected out of what could be.
- **An answer in the window left the plugin resolving gearsets against the picture from before it.**
  `OnReviewDecision` cleared the push cache and nothing else, so the identity mapping, which is what turns
  a live gearset into a stored row, kept the state the answer had just replaced. With two gearsets sharing
  a job and a name that produced a wrong attribution rather than a missing one: before a bulk accept only
  one row was called "BRD / Barde", afterwards the adopted row carried that name too, and the live set
  went on resolving to the row it used to be. The window then showed a position belonging to a different
  gearset. The mapping is dropped and read again after every answer, and the BiS cache with it.
- **The only three actions on an inventory card were unlabelled glyphs.** They sat at the top, they were
  the whole decision, and one of them removes a row and the target pinned to it for good. An unlabelled
  icon is a matter of taste anywhere else; over a delete it is a red square somebody is invited to guess
  at. They are words now, like every other answer in this window, and the difference had become impossible
  to defend once one carousel took a reader straight from a row of labelled buttons to a row of glyphs.
- **Twelve gear tiles answered a question nobody had asked yet.** On an inventory card the decision is
  keep or delete, and the verdict and the counts answer it two lines above the grid; the grid is the
  working out, for the reader who wants to check it, and reaching the one slot that differs meant walking
  eleven that do not. It folds away behind a button there. On a row nothing resembles it stays in the
  open, because there the gear is the only thing anybody would recognise the row by months later.
- **The evidence on an inventory card read like an offer.** The comparison there is drawn by the same
  method as the one on a question card, which was the point, and the shared shape carried the shared
  reading with it: a list of sets the row resembles looked like a list of things to pick, when nothing on
  that card offers attribution at all and never can. A line above it says what it is. The rows in it are
  A second half of this, naming each row with where it sits in game, was written and taken out again: the
  plugin resolved the wrong one of two gearsets sharing a job and a name, which is precisely the case the
  label existed for. `similar[]` carries no state, no date and no position, so the fact has to come from
  the server rather than be reconstructed here. A player with two gearsets called "Barde" still sees the
  same name twice with no way to tell them apart; that is open, and written down as such.
- **The status entry named the questions and hid the rest.** It said "one question" beside a window
  holding one question and one row with no gearset, because it fell back to the rows only when there were
  no questions at all. It names the sum now, as decisions rather than as questions: naming both kinds was
  honest and ran off the end of the row, and calling an inventory row a "question" there would have been
  the plugin using two names for two different things. One carousel already walks them together.
- **The window was two card stacks deep.** Questions were a carousel and the rows with no gearset were a
  second carousel, and both were drawn, one under the other, so what you were answering depended on how
  far you had scrolled and every decision arrived with another one already open beneath it. They are one
  carousel now: questions first, because a push can invalidate them, then the inventory rows, one set of
  arrows over all of it and exactly one decision on screen. The card says which kind it is, since the
  arrows walk both. Rows already put aside stay out of it, folded under a line at the bottom: they are
  decided, and walking eight of them to reach the one question that matters would cost the arrows their
  worth every time somebody archives something.
- **A push changed the questions and the open window did not notice.** Everything else a push touches is
  refreshed when it finishes, the advisor options and the BiS targets among them; the reconciliation state
  was not, so deleting a gearset in game and pushing left the window showing the reading from before it,
  with nothing saying so. It re-reads now, but only while it is open: with it closed the badge is already
  fed by the summary the push answered with.
- **A choice made against one reading of the state survived into the next.** The picked candidate, the
  flipped comparisons and the struck-out pairings were kept in the window and cleared, when they were
  cleared at all, one path at a time. So after a bulk accept a candidate picked minutes earlier was still
  in the box, against a mapping the server had since recomputed. They are cleared on the state token now,
  which is the name of the reading they were made against, so every way the state can change is covered by
  one rule rather than by remembering to add a clear to each new path.
- **The advice on a question named the proposal even when the reader had picked something else.** The card
  said "it might be Web DRG C" over a comparison with Web DRG A and two buttons that would have written
  Web DRG A, and on the press the lower half won. Picking another candidate now changes the sentence to
  say which row was picked and which was suggested, and it drops the reassuring colour, which belonged to
  a recommendation the reader had just declined.
- **One card carried two identical links to the website.** One under the buttons for the question and one
  under the comparison, same icon and same tooltip, with nothing to tell them apart. The rule the
  inventory card states, that the website earns a button only where the set cannot be looked at in game,
  applies with more force to a question than anywhere else: the set came out of the game a moment ago. It
  is gone, and the one that remains follows the same rule as the inventory card, which it had not been.
- **Counts said "3 Frage(n)" in the window's first line.** Every count in this window now has a singular
  and a plural, including the one the status window shows beside the entry that opens it.
- **Two tables in the bulk panel did not line up.** The lower group has no strike buttons, so its first
  column sized itself to nothing and every line in it sat one button to the left of the group above. The
  column has a width now rather than taking one from its contents.
- **The two comparisons were two pieces of code, and they had drifted.** The question card and the
  inventory card each drew their own version of "two sets, slot by slot", so a fix on one never reached the
  other: the origin of a row was written out on one and folded behind a "(?)" on the other, and the swap
  button had been moved out of the sentence on one while still sitting inside it on the other. There is one
  method now, `DrawComparisonHead`, and both cards call it. Two pieces of code doing the same job always
  end up looking like two different features.
- **The swap button names the action, not the destination.** Its label used to change with its state, so
  pressing it changed its own width and shifted whatever stood beside it. It says "switch which set is
  shown" whatever the state, the icon carries the direction, and the specific sentence stays on the hover.
- **The link to the website rode along at the end of a wrapping sentence.** How far that sentence wraps
  depends on the row's name and the window's width, so the button moved every time either changed and on
  a narrow window it was pushed off the edge entirely. It sits among the buttons now, behind two labels of
  fixed width.
- **The floor on the window size was a number somebody typed.** 520 by 340 was picked while looking at an
  English build at one interface scale; dragged to it, the button rows ran off the edge and the header
  wrapped into the toolbar. The width now has a measured part, so a longer translation raises it instead
  of overflowing, under a floor taken from the size the window was actually being read at. Both are capped
  against the display, since a floor larger than the screen is a window nobody can move.
- **A button sat inside a sentence, and pressing it moved the words around it.** The eye that turns the
  comparison round stood between the counts and the row's origin, so a press rewrote the line it was
  standing in and everything after it shifted under the cursor. The counts and the origin are two lines of
  their own now, and the eye moved down into the row of buttons as the last control in it, labelled, with
  the link to the website in front of it: the one control whose width changes has nothing after it to move.
- **The window was legible and unreadable at the same time.** The text was scaled up for a page somebody
  reads before pressing something irreversible, and the spacing was left at ImGui's defaults for a dense
  tool panel, so buttons touched, an icon sat against the sentence it was not about, and the two halves of
  the gear grid ran together into one paragraph with pictures in it. Spacing, frame padding and cell
  padding are set for this window; the bulk accept is drawn inside its own border instead of as a stretch
  of text above the first card; and a rule separates the answers that need no candidate from everything
  below them, which all belongs to one stored row.
- **A question with two candidates put four buttons on one card and no way to tell them apart.** Each
  candidate carried its own "that is the one" and "stop asking", identical and stacked, so the button said
  nothing about which row it would answer for. The candidates are a picker now: one box naming each stored
  row with its score, the server's suggestion marked, one comparison and one pair of buttons underneath.
  This works on the card and cannot work in the bulk accept, and the difference is the endpoint rather than
  the layout, since a single decision goes to `POST /gear/review` where every candidate is a valid target.
- **The same row was drawn twice, with a delete button under the copy.** A set renamed, re-geared and moved
  arrives as a question, and the row it used to be is parked: genuinely both an unclaimed candidate and an
  orphan, so the server lists it twice and the window drew it twice with a full comparison under each.
  Deleting it in the inventory and then answering "that is the one" would have named a target that no
  longer existed, and the observed row carried a pinned target. A row an open question asks about is no
  longer offered in the inventory until the question is answered.
- **"This cannot be undone" stopped being true of a release.** It became reversible the hour `released_at`
  landed: `reopen` on a released row puts it back to a parked plugin row. The rule outlived the fact, so
  the heading is now picked per verb, and it claims irreversibility only for a delete and a link. A dialog
  that overclaims teaches people the sentence is decoration, which is what it must not be where it holds.
- **The delete confirmation said the opposite of what it does.** ""PLD Test 1" keeps its pinned BiS set"
  belongs to a candidate answered away with `new`; in front of a delete it promised exactly what the delete
  was about to take, and it was the last sentence read before the row was gone. The rule moved into the
  core as `ReviewRules.ConsequencesOf`, which also draws the distinction the wording had flattened: a
  delete on a row with a team share is performed as a release, the row survives, and there the pin really
  is kept. Four tests, one per case.
- **The BiS tooltip answered from the previous order.** The map from live position to identity was rebuilt
  only when a window opened, so after a reorder the tooltip handed out a neighbouring set's target with
  nothing to suggest a doubt, and opening the gear window silently made it right again. A push now clears
  that map and refetches it, and the clearing comes first: with no map, a position resolves to no
  identity, so the tooltip says nothing rather than something wrong.
- **The BiS tooltip compared against nothing** from the day the server began minting identities: a target
  carrying a uid is looked up by uid, and the tooltip passed no resolver, so every slot read as missing.
- The mapping cache was written by a push and read while drawing, without a lock.
- `GET /gear/sets` answers with the whole account; rows of another character are dropped rather than
  cached, where they would have made a real set ambiguous.
- The uncertainty warning gave the advice for the wrong rung: renaming ends an ambiguity and does nothing
  for a positional match.
- A missing `last_seen_at` printed "never in game", a claim built out of an absence.
- The what's-new window appeared on a first installation, which the framework profile forbids.
- The tome balance was pushed every five minutes whether or not it had changed.
- The reconciliation badge counted what the last push said rather than the current state.

### Changed
- **The diagnostics report says which rung found each gearset.** It printed one rung, and that was the
  server's, recorded when the mapping was minted and carried along unchanged ever since. What this side
  did to arrive at the answer was nowhere, so a set recognised by its contents and one recognised by where
  it sits read identically, and a test of the position rungs could not be told from one where nothing had
  changed. The two are named apart now, "found by" beside "server rung": `job+name+gear`, `place+gear`,
  `place+name`, `job+name`, and the three ways of not answering.
- **A gearset in a sentence now carries the number it has in game.** The reconciliation window named sets
  by name alone, and the name is the one thing that does not identify them: the game writes a new set's
  name from its job and never asks, so two sets of one job are called the same thing from the moment they
  exist. A card then read "this is a copy of X" and two lines below "also resembles X", naming two
  different sets identically with nothing to tell them apart. The number was tried once during this cycle
  and taken straight back out, because the lookup behind it resolved the wrong one of two same-named sets:
  a label whose whole purpose is to separate them, wrong in exactly that case. It works now because the
  table it reads withdraws every claim it cannot be sure of, so a number is either right or absent, and
  absent is a real answer rather than a failure: the set may be one the game no longer holds, which is
  usually why it is on that card. The quotes moved out of the sentences and into the piece that builds the
  name, so the number sits outside them and does not read as part of what the set is called.
- **"When" and "what" are two questions, and the settings page now answers them that way.** The three
  timing switches governed the gear push and nothing else. What you own went out at login and again every
  five minutes, the weekly checklist at login and again every hour, both of them behind their own switch
  and neither behind the timing ones. Somebody who had turned every timing switch off in order to send
  only by hand still had two of the three payloads leaving on a schedule, with nothing on the page saying
  so, under a master opt-in that promises the plugin never contacts the API while it is off. The wording
  had already said otherwise: "also send what you own" is an addition to the transfers already happening,
  not a schedule of its own. So the login switch and the automatic switch now cover everything switched on
  beneath them, and both say so. The gearset-change switch stays with the gear, since an inventory scan
  set off by a gearset change would answer nobody's question. Two triggers are left alone and are now
  described rather than left to be discovered: a retainer's bags and the Raid Finder's weekly state can
  only be read while those windows are open, so they are read then, and opening them is the player's own
  doing rather than a schedule.
- **A released row is told apart from a hand-made one**, by `released_at` and by nothing else. Both are
  `manual`, and while they read as one case the card called a player's own set a stranger and the single
  button on it promised a way back that led out of the window instead. Found in the field on a real row,
  which the server side then split with a mark rather than a second `source`. The sentence in front of a
  `link` changed with it: the door that cannot be reopened is the overwrite of the target's contents, not
  the change of governance, so all three targets name the overwrite and only the price differs.
- **The card says what the row is and what to do about it.** A comparison and then silence left "73 %,
  three slots different" as the whole answer to a question nobody could answer from it. Now the first
  thing on the card is the verdict (a copy of a set still there, the same pieces with other melds, a
  resemblance that is not the same gear, or nothing resembling it at all) and the sentence that follows
  from it. A verb is named outright only where naming one cannot be wrong, and never while a pinned
  target or a team share hangs on the row; the button it names is ringed in the bar above.
- **The reconciliation card was rebuilt around what is being decided.** The gear is a grid in the shape
  the gear window uses rather than a second arrangement to learn; the verbs are one row of icons that say
  what they do and what that costs on hover; the explaining sentences moved behind a mark, and the two
  that name what a delete takes away stayed in plain sight. One way to the website per set, beside the
  name it belongs to.
- No shipped string carries an em-dash (I-02), and two tests keep it that way.
- The what's-new window reads at a larger scale and lays its notes out as a table.

### Internal
- A release refuses to publish when the tag disagrees with the manifest inside the archive.
- The localisation test gained its other half: a catalogue may not carry a key nobody declares.
- The review model is held against a recorded answer of the real server rather than a hand-written one.
  Two fields were missing and neither had failed anything: an undeclared property is dropped in silence.
- Test count: 1231 to 2559.

## [1.0.0] - 2026-07-27

The plugin leaves its trial phase. `0.x` in SemVer means "anything may change"; that is no longer
true — the server contract is settled and covered by tests, and from here on every change is
announced rather than arriving silently. Nothing about the existing behaviour changes with this
number.

### Added
- **Write to the developers from inside the game.** A topic, a subject, a message, a send button —
  into the same inbox the website's contact form feeds (`POST /contact`, scope `contact:write`, which
  an existing key gains on its next sync). The four topics match the website's (report a bug,
  suggestion, feedback, something else) and are built from what the server says is currently open, so
  a switched-off one is never offered — otherwise the report would fail after the text was already
  written. Who is reporting and how to answer them comes from the API key, so there is no name or
  address to fill in; character, world and the game, Dalamud and plugin versions travel as separate
  fields, and the window shows exactly what it is about to send — a field that is not on that line is
  not in the request. Every main window carries a bug button in its title bar, and it says which
  window the report came from: "it does not work" from the purchase advisor is a different search
  than the same sentence from Teams, and that is the one piece of context a player should never have
  to type out. There is also a way round the form: the
  maintainer's character and a Discord invite, for anyone who would rather just talk to a person.
  Nothing is ever sent automatically — no exception handler, no background collection — because an
  inbox shared with real player mail must not fill with machine noise. A failed delivery is shown
  rather than swallowed and leaves the text in the window: the server stores nothing on the way, so a
  silent "thank you" would be a lie.
- **A machine-readable changelog** at `changelog.json` in the repo root, generated from the in-game
  release notes so the same sentence reaches the "what's new" window, the website's news page and
  Discord without three copies drifting apart. Each note line carries a permanent `Id`; the web side
  remembers it as "already announced", so changing one re-announces the entry and reusing one swallows
  it. A test fails when the committed file is stale — see *Cutting a release*.
- **The plugin version travels with every write** (`plugin_version` beside `protocol_version` on gear,
  inventory, weekly, tome balance and advisor plans), so a "it stopped working" report says which build
  produced it.

### Fixed
- **The saddlebag no longer empties itself on login.** 0.4.0 gave the saddlebag its own scope so an
  unread one would be left alone, but decided "unread" from the containers reporting themselves as
  loaded — which they do for a saddlebag nobody has opened this session; they are simply empty. The
  scope was therefore declared with nothing in it and the server dutifully cleared it, so a stock of
  books and materials read as `0` until the bag was opened once. Finding something in it is now the
  only evidence that counts as having looked. The trade is deliberate: a genuinely emptied saddlebag
  keeps its last known contents until something is in it again — a stale count can be corrected on
  the website, a deleted one cannot be recovered.
- **Retainer stock was exposed to the same fault.** The retainer scan runs on a timer and leaned on
  the same flag to decide whether the player was standing at the summoning bell, so a retainer
  visited in an earlier session could be uploaded as empty without anyone opening it. It now reports
  only what it actually found, with the same trade-off.

### Changed
- **API keys are held per address.** The base URL has always been configurable; what was missing is
  that one key served every address, so pointing the plugin at a test server would have sent it the
  production key. Each address now keeps its own, and an address with no key of its own reads as
  disconnected rather than borrowing one — without that rule the split would be decoration. An existing
  key is filed under the address it was actually issued for. The settings screen says plainly when the
  plugin is not talking to the live server.

## [0.4.0] - 2026-07-26

### Added
- **"How to get it" on BiS pieces.** Hovering a BiS target — in the list, the grid tile or the
  shopping list — now also shows where the piece comes from and what it costs (the same route detail
  as the team farm: the fight it drops in with its coffer, or what to trade and with which vendor,
  down the whole chain). Reads impersonal game data via `GET /gear/obtain` with your existing key,
  cached for the session and fetched in the background; toggle under *Display*. The route renderer is
  shared with the farm tab, so the two never describe a piece differently.
- The sourcing detail is now shown as **numbered steps**, and is actionable and complete in both the
  BiS window and the team farm:
  - **Every way in, and every step of it.** A piece lists all of its acquisition routes as
    alternatives — a savage piece drops in the fight **or** can be traded for books — and each route is
    broken into the concrete things to do (*Fight …*, *Buy …*, *Upgrade …*), so an augmented (Tome+)
    piece reads **get the base first, then augment it** rather than assuming the base is in hand. The
    farm pulls the full chain from `GET /gear/obtain`, which its own response omits.
  - **"Do I have it?" per step, retainers included.** Each purchasable cost (tokens, materials) carries
    a **have / need** count, green once you own enough. The count comes from the server's holdings
    (`GET /me/holdings`), which sum every synced storage **including each retainer as of its last
    visit** — the one thing a live game read cannot see — with the live in-game count as an immediate
    fallback until the server number lands. The plugin now also reports the tier's tracked consumables
    (from `GET /gear/tracked-items`) in the inventory sync so those counts exist, and refreshes the
    holdings after each sync.
  - **"Base owned" from anywhere.** The Tome+ base step collapses to *Base owned* not only when the
    base is equipped but whenever you hold it (bags, saddlebag or a retainer), using the base item ids
    the server now sends on the hand-in cost.
  - **What's still short, per character.** Next to each teammate in the farm, a one-line summary sums
    the materials/tokens they still need across all their missing pieces, minus what they own — so you
    see at a glance what to gather for them.
  - **Show NPC on the map.** Right-click a piece (farm row or BiS item) → *Show NPC on map* opens the
    map and drops a flag on the vendor. The location is resolved from the server's ids, or — when it
    only sends a zone name — from the game's own place names, so it works without a server change.
  - A larger, better-spaced tooltip for the whole checklist.
  - **Skips a step you already did.** If you are already wearing the tome base a Tome+ piece upgrades
    from (recognised as a tome piece of the same slot), the "buy the base" step collapses to
    **"Base owned"** — only the upgrade remains.
  - The **in-game hover overlay** now also shows how to get the target when you are hovering a piece
    that is *not* your BiS item and you do not own the right one yet — so you see where to get it
    without opening a window.
- **Capped-tomestone balance sync.** The plugin now sends your current capped-tomestone count to the
  web purchase advisor (`PUT /me/tome-balance`), so it can say what to buy now vs. in N weeks without
  you retyping a number the game already knows. It piggy-backs the inventory sync (no extra polling),
  needs a key with **`characters:write`**, and is per character; if it cannot push, the advisor still
  works from a hand-typed number.

- **Purchase advisor ("Kaufberater") window.** A separate menu entry, deliberately not folded into the
  BiS window: BiS is the *goal*, the advisor is the *path* to it (the intermediate gear between raid
  tiers).
  - **Your saved plan.** Renders the layout you built in the web advisor, read per (character, job,
    target set) via `GET /me/advisor-plan` (scope `plans:read`), per slot with *worn / owned / still
    missing* and the usual sourcing on hover. A plan is stored under the set's **web identity**, so it
    only becomes addressable once `GET /gear/bis` sends a set's `target`; until then the section says
    so instead of failing. "No plan saved" is a normal state, not an error — the advisor's
    *recommendation* is computed client-side in the web and has no endpoint, so it is not mirrored here
    yet, and editing a plan in game waits on the per-slot choices from the server.
  - **Your stock.** The active tier's tracked materials, upgrade stone and books — from the new
    `groups` on `GET /gear/tracked-items`, so a tier rotation carries itself without a plugin release —
    each with the **server's** owned count, which is the only one that includes your retainers. The
    live in-game count fills in until the server number lands.
  - **The recommendation, computed server-side.** `GET /me/advisor-options` returns the one ranking the
    web renders too, so the plugin never owns a second copy of the rules and a tier rotation needs no
    release: the ranked steps with their tomestone price, when each becomes affordable against the
    pushed balance, the vendor, and the material each consumes.
  - **The set as a grid**, laid out like the BiS window and coloured like the web advisor (green on
    BiS, blue you own it, orange next purchase, grey nothing deterministic left), leading with the
    single best next move and the set's numbers.
  - **My layout is editable in game.** The picker per slot offers exactly the pieces the server lists,
    so a saved plan can never contain an invented item id; saving writes to the same key the web does.
    A plan is only ever written on a deliberate action, never as a background sync.
  - Every icon answers on hover: what the piece is, whether you wear/own/still need it, the route in,
    and — for a material — which bag or retainer the stacks sit in.
- **"Still needed for this set" in the BiS window.** Each set folds out what completing it actually
  costs: the tomestones the remaining purchases add up to, measured against the balance the plugin
  pushes (with how many capped weeks that is), and every upgrade material and raid book still short,
  each with what you already hold. The totals come from the advisor, so the book trade counts as the
  alternative to a savage drop exactly as the advisor ranks it, and the read only fires when the
  section is opened.

- **The farm leads with what you can actually do.** For your own characters the ways in are ordered by
  what you hold rather than by what the piece's source suggests: a coffer already in your bag reads
  *"Coffer in hand — just open it"* instead of sending you to the fight, and a book trade you can
  afford comes before the drop, marked *"you can do this now"*. Coffers are counted server-side too,
  so one sitting on a retainer counts. A teammate's row is never re-ordered — their stock is not
  visible, so there is nothing to rank by.

- **The farm knows what a member owns, not just what they wear.** The server now answers that per
  slot (worn, ticked off by the team, or marked as an upgraded tier), so a teammate holding the
  augmented neck while still wearing the base is no longer told to buy the upgrade — and the counts
  match the web tracker instead of reading high. Your own row adds what the plugin can see itself, so
  a piece bought since the last sync counts immediately.
- **Teams companion (opt-in).** A new in-game window (hub button, `/xivarsenal teams`) that mirrors your
  teams from the web app — read-only rendering; the server owns all logic. Off by default; needs a key
  with **`teams:read`** (+ **`teams:write`** for the two writes). Existing keys auto-upgrade on the next
  call. Covers:
  - **Calendar** — a dedicated cross-team month calendar (own hub button / `/xivarsenal calendar`)
    over the current + next two months, colour-coded per team, with **in-game RSVP** (yes/maybe/no;
    optimistic, then re-polled) and per-event "open in web".
  - **Mit cheat sheets** — a **time-axis timeline**: mechanics on the left, cooldowns in per-job
    columns aligned to the same times, grouped by named phase. **Phase checkboxes** and a **tag filter**
    (raidwide / tankbuster / other) pick what to show; cooldowns render as skill icons with the name,
    recast and duration on hover. The current job is preselected only when the plan has it (else a free
    picker; single-job or all-jobs), remembered per plan.
  - **Content hub** — every fight with its bosses/drops and resources, each **labelled by type**
    (link / video / plan / note / image / pdf / file); **note text** shown inline; **images open in a
    dedicated window** scaled to size; PDFs and links open in the browser.
  - **Farm** — who-needs-what across the team (equipped vs BiS target, still-missing per member), each
    missing piece labelled by **source** (savage / Tome+ / Tome) with the **way to get it** on one line
    — the fight it drops in, or what to trade and with which vendor — and **every route on hover**
    (coffer, the piece you hand in, vendor zone + coordinates). The primary route follows the same rule
    as the web, so plugin and site never disagree. Hardest-to-get first; all server-provided, the
    plugin never guesses, and an unconfigured tier just shows the item as before.
  - **Owned gear coffers** ride the existing inventory upload: the loose storages (bags, saddlebag,
    retainers) now also report savage gear coffers, so a coffer's web page can show "you own ×N, here"
    and how many open pieces you can make now. Detected by name like the web does, not a hard-coded
    list; potions, food and materials stay out of the ownership set.
  - **FFLogs** — recent kills/wipes with each report's **date** and a **direct link** to it
    (best-effort; a not-connected/empty state never crashes).
  - **Absence** — report/cancel your own vacation ranges, with a **date picker** and localized date
    display (DE/EN).
  - **Events** — the per-team list of upcoming (and optionally past) dates, grouped by event, with
    **zebra-striped rows** and a **configurable text size** so long lists stay readable at a glance.
  - A **Teams settings tab** (icon/text display for cooldowns and resource labels, note visibility,
    default job/tag/phase selection for mit plans, event text size) and **"open in web" deep links**
    that land on the exact thing you were looking at — the selected mit plan, that one event
    occurrence, that content — not just the tab.
  - **In-game notifications** — a toast **with a sound** and a **clickable chat link** for new loot,
    event reminders (each of your 1-day / 3-hour / 1-hour warnings fires once) and newly planned events.
    Deduped by notification id and persisted, so a relog never re-toasts the backlog.
- Polls the calendar + notifications at most every ~5 minutes; the content hub, farm and FFLogs load on
  demand. Never writes anything but your own RSVP and your own absence; a server `403`/`404` is shown,
  never worked around.
- **"What's new" window** (`/xivarsenal whatsnew`, or the menu entry, which stays highlighted until you
  have read the notes for the version you are running). A short, plain-language digest of what each
  release changed — new / improved / fixed — as opposed to this changelog, which is written for
  contributors. It ships **inside the plugin**, so it works offline and can never disagree with the
  build you are running. After an install or update it **opens once, on the first frame you are
  actually in the world** — not at the title screen, and it works just as well when the update is
  installed mid-session. Switchable off under *Display*; links out to the full changelog. A test pins
  the notes to the shipped version so a release cannot forget them.

### Changed
- **The chat command is now `/xivarsenal`** (was `/bisexport`) — the plugin long outgrew a pure BiS
  export. The old `/bisexport` command has been removed.
- The hub is now the **menu** window (`/xivarsenal menu`, was `/xivarsenal status`) and is purely
  actionable; the **"what will be sent" preview** moved into its own window instead of expanding inline.
- A failed team/parse response now surfaces the concrete cause (HTTP status or the JSON path of a
  shape mismatch) instead of a generic "could not load", to make diagnosis quick.

### Fixed
- **The glamour dresser no longer clears itself.** Like the saddlebag it only reads after the player
  has opened it — and unlike the saddlebag the client exposes no "loaded" flag at all, so an unopened
  dresser is indistinguishable from an emptied one. It is now its own reconciliation scope, declared
  only when pieces were actually found; an emptied dresser therefore keeps its last known contents,
  which is the harmless direction.
- **A swapped ring pair is no longer two missing rings.** The farm compared finger by finger, so
  wearing both BiS rings the other way round listed them as still to get. The rule (rings are
  interchangeable) now lives once in the core, tested, instead of being re-derived per view.
- **The language setting now governs game names too.** Item, vendor, zone and duty names were read in
  the game client's language regardless of what the plugin was set to, so switching the plugin to
  English left them German. They follow the plugin's setting now — which is also the only way to use
  the plugin in English on a German client.
- **The inventory sync no longer empties the saddlebag.** Its containers only read once the player has
  opened the saddlebag in a session — and until then they read as *empty*, not *unavailable*. It used
  to ride along in the `character` scope, which the upload declares fully observed, so syncing
  beforehand told the server the saddlebag was empty and it deleted what was stored there. The
  saddlebag is now its own reconciliation scope (like a retainer): it is declared only when it was
  actually read, and left alone otherwise. A manual sync says once when it could not be read, so the
  player knows those counts are not current — nothing is lost either way.
- **The sourcing speaks the client's language.** Coffers, materials, books, vendors, zones and fight
  names were English throughout, because the server names things in English while the game carries
  every language itself. Anything the server identifies by id is now named by the game — items,
  coffers, vendor NPCs (`ENpcResident`), zones (`TerritoryType` → `PlaceName`) and fights (the
  server's new `duty_content_ids`, paired only when there is one id per name, since it omits the ones
  it cannot resolve). Every lookup falls back to the server's English text, so nothing can read worse
  than before. Shop labels stay English on purpose: they are the data source's own wording and have no
  game row to look up.
- **A teammate's row no longer answers from your bags.** The team farm measured every member's
  remaining cost against the player's own holdings, so someone else's line claimed they were short
  materials — or already owned a base piece — purely because the player was. The plugin can see
  nobody else's bags, retainers or tomestones (`/me/holdings` is caller-only and now pinned to the
  character on screen; the farm endpoint carries only shared gear). A teammate's row now states what
  the set requires and says plainly that their stock is not visible; only the player's own row is
  measured. The equipped check stays for everyone — what a member wears comes from the team data.
- **Owned counts no longer read 0 for anything the server does not track.** The server's holdings won
  unconditionally, but a server `0` means "no record", not "you own none": the weekly tomestone is a
  currency and is never part of the inventory sync at all, so a player holding 1109 was shown `0/495`.
  The count is now the higher of the server's number and the game's — the server still wins for
  retainer stock, the game still wins for anything not synced yet. Holdings are also pinned to the
  character on screen (`&character_id=`), instead of whichever one the account last made active.
- **Automatic syncs no longer talk in chat.** Only a sync you asked for reports there; logins, the
  periodic timer, retainer visits and the hidden Duty-Finder refresh (which produced the duplicate
  "2 weekly fields" lines) go to the log instead. Failures still always speak.
- The hidden Duty-Finder refresh (the `normal`/`alliance` weekly read) no longer leaves the finder on
  the raid it loaded. It now remembers the duty you had selected and re-selects it before closing, so
  reopening the Duty Finder puts you back where you were — including a **roulette** (Duty Roulette /
  daily), which the game stores in the same field as a regular duty but restores through a different
  call. Nothing selected beforehand means nothing is restored.

## [0.3.0] - 2026-07-05

### Added
- **Weekly checklist — four more fields.** Building on 0.2.0, the weekly auto-fill now also covers:
  - **Unreal trial** (`unreal`) — done-this-week, decoded from the Faux Hollows timestamp vs the
    weekly reset. Background-readable, so it syncs on login/hourly without opening anything.
  - **Wondrous Tails** (`wondrous`) — a completed book (9/9) that was **bought this week**. Because a
    book is valid for two weeks and its sticker count stays put after a hand-in, the plugin anchors on
    the book's own expiry (a this-week book expires beyond the next reset) — so a stale completed book
    can never be mis-reported the following week. Background-readable, no client-side state.
  - **Normal raid** (`normal`) and **Alliance raid** (`alliance`) — read from the Duty Finder's
    weekly-reward count (the game only exposes it for the selected duty), classified as 8-player normal
    vs 24-player alliance from the duty's party size. Fetched via a **hidden Duty-Finder refresh** — the
    plugin loads the current tier's normal and alliance raids into the finder with the window suppressed,
    reads each reward, then closes it (analogous to the Savage refresh) — at login, hourly, on the
    manual sync, and opportunistically while you have the finder open. Gated on the account's
    `alliance_lockout` / `normal_lockout`. As with every weekly field, only a confident **done** is
    ever sent, so a manual web-app entry is never overwritten.

  All four decodes were validated in-game against before/after captures.


### Changed
- The manual **Sync weekly** action now also kicks off the hidden Savage + Duty-Finder refreshes, so a
  button press picks up the `f1`–`f4` / `normal` / `alliance` fields too.
- The `/xivarsenal weekdump` diagnostic now also reports the Wondrous Tails expiry/expired flags and
  the classified kind (normal/alliance) of the selected Duty Finder duty. New `/xivarsenal dutyrefresh`
  triggers the hidden Duty-Finder refresh on demand; `dutyprobe` is a raw control test.

## [0.2.0] - 2026-07-02

### Added
- **Weekly-checklist auto-fill (opt-in).** When enabled, the plugin fills your web-app **weekly
  checklist** from the game so completed weeklies show up without manual ticking — for the right
  character, cross-device (stored server-side). It reads only values it can determine **with
  certainty**, reads the server's current state first and merges just the fields that **changed** via
  `PUT /characters/{id}/weekly`, so it **never overwrites a manual entry** and never wastes a write.
  Covered:
  - **Tomestones** (`tomesHave`) — weekly-limited tomestones acquired this week.
  - **Custom Deliveries** (`custom`) — reported *done* once all weekly allowances are used.
  - **Savage floor loot** (`f1`–`f4`) — obtained-this-week per floor, gated on the account's
    `savage_lockout`. This state lives only in the Raid Finder, so the plugin fetches it via a
    **hidden, instant Raid-Finder refresh** — the window never actually opens — at login, hourly and
    whenever you open the Raid Finder yourself. It is guarded to never run in combat, a duty, a
    cutscene or between areas, and is purely read-only.

  Syncs on login, after a gear push, hourly and via a **"Sync weekly"** button; needs a key with
  **`characters:write`** + **`gear:read`** (a 403 shows a reconnect hint). The per-character server
  id is learned from the gear/inventory push response and cached.


### Changed
- **Reworked the plugin windows for clarity.**
  - The **main window is now a hub**: large single-per-row action buttons with icons, grouped into
    *Actions* / *View* / *Manage*, above a clear connection banner.
  - **Settings are organised into tabs** — *Sync*, *Display*, *Characters*, *Connection* — that
    appear once connected; before that you only see the connect flow. Connection management and the
    third-party-tool notice live in the last tab. Everything is larger and roomier, and hint lines
    now wrap to the window width instead of overflowing.

## [0.1.1] - 2026-06-21

### Added
- **Plugin icon in the Dalamud installer** via the manifest `IconUrl` (a 512×512 PNG served from
  `xivarsenal.app`), shown both in the available list and, after install, in the installed list.


### Changed
- **Plugin author is now "Sanaka"** (the name shown in the installer); the company field was removed.
  This is a personal hobby project, intentionally not tied to a business identity.

### Fixed
- **`pluginmaster.json` generator** now runs on Windows PowerShell 5.1 (it no longer relies on the
  pwsh-only `-AsArray`) and writes UTF-8 **without a BOM**, which Dalamud's parser rejects — so the
  repo index is produced correctly both in CI and locally.

## [0.1.0] - 2026-06-21

First public release.

### Added
- **Owned-items / inventory upload (opt-in, Phase 2).** When enabled, the plugin uploads which
  equippable gear you **own** via `POST /inventory` so the web app can tick off pieces in the
  overview, item search and collection. It is **scope-accurate**: each upload reports exactly which
  storages it fully scanned, and the server replaces only those — unreported areas keep their last
  state, and a reported-empty area is cleared (so selling a piece in your bags removes it on the next
  scan). The **`character`** scope bundles every locally readable storage in one scan (equipped,
  armoury, bags, saddlebag, glamour dresser) so moving items between them is harmless; it uploads on
  login and on a throttled timer (unchanged scans are skipped, so it never wastes the 30/hour
  budget), plus a **"Sync inventory"** button in the status window. With the extra **"Include
  retainers"** opt-in, each retainer is scanned as its own `retainer:<id>` scope when you open it at
  a summoning bell. Only equippable items are sent (weapons/armour/accessories — never
  materia/consumables/materials), the **Armoire is not scanned**, and your manual web-app markings
  are never touched. Uses the same `cid_hash` as the gear push; needs an `inventory:write` key
  (reconnect if a 403 says it's missing).
- **Gear vs BiS comparison.** Reads BiS targets via `GET /gear/bis` (the `gear:read` scope, issued
  alongside `gear:write`) and shows an in-game per-slot diff of live gear vs BiS in a dedicated
  **BiS window** (opened from the status window). Pure `BisComparer` matches by `gear_index`+`job`,
  treats rings as interchangeable and materia order as irrelevant. Auto-loads on login and refreshes
  when the window opens if the data is stale.
- **BiS window views and tools.** Item **icons + names** (not raw ids), item level and source per
  slot; a **scope** selector (current set / all sets) and a **filter** (all / incomplete / materia
  issues); a **character-screen grid** view (weapon + off-hand on top, five armour rows left / five
  accessory rows right, status-bordered tiles with name + materia-to-socket beside each); a
  **shopping list** that aggregates every still-needed item + materia you don't own; per-gearset
  **progress bar**; and per-item actions — **left-click** links the item to your local chat log,
  **right-click** copies its name to the clipboard.
- **BiS hover overlay.** Hovering any equippable item resolves its slot and shows the current
  gearset's BiS target for that slot — target item name + materia, whether you own it, the localized
  slot name, item levels, the item **source** (Raid/Tome/Crafted/Relic/…), and a clear hint when a
  gearset has no BiS target. A safe, styled overlay docked to the native tooltip (toward the cursor);
  it never touches the native tooltip, so it cannot crash the client (P2/P6). Toggleable.
- **Server-info-bar (DTR) status entry.** A compact **Arsenal: &lt;last push&gt;** entry in the
  in-game server-info bar: time since the last successful push (e.g. *3m*), **!** on failure, or
  *off* when not set up. Hover for the full time; click to open the status window. Toggleable.
- **Diagnostics log window.** Lists recent plugin messages (status codes, `request_id`s, the failing
  request method+URL, errors — never secrets/bodies, R22) with **Copy**/**Clear**, opened via the
  log icon or `/xivarsenal log`. The log is per-session (cleared on login; in memory only).
- **Status window** — last push time, outcome + `request_id`, rate-limit countdown, and quick
  actions: push now, preview what will be sent, open web app, open settings.
- **Connect via OAuth 2.0 device flow and paste-key fallback**, with in-plugin **Disconnect**. The
  device flow opens the pre-filled approval page (`verification_uri_complete`, RFC 8628) and copies
  the `user_code`; approval stays the user's explicit click (the plugin never approves
  programmatically). A scope check after the connection test warns if the key lacks `gear:write`.
- **`PUT /gear` push** of all gearsets across all jobs, triggered by `/xivarsenal`, login, a
  debounced gearset-change detector, or a throttled auto-push. Stable `cid_hash` (SHA-256 of the
  decimal ContentId), locked by a test vector. Per-character push opt-in, single in-flight push with
  coalescing, client-side validation, and proactive 429 back-off (30 uploads/hour).
- **Toast notifications**, a **log-verbosity** setting, and a **configurable web app URL**.
- **Bilingual DE/EN UI**, a third-party-tool **ToS opt-in** notice, and a versioned + migrated
  config. Interface-based core (`EorzeaArsenal.Core`) with a thin Dalamud host (R8/R9/R11) and unit
  tests for the API client, device flow, gear/inventory mapping, validation, chunking and `cid_hash`.


### Changed
- **Default API base URL is the production server** `https://xivarsenal.app/api/v1`. New installs
  connect to production out of the box; existing saved configs are unchanged, and the value stays
  user-editable (set it to `localhost` for local testing).
- **BiS status colours are a clear traffic light** everywhere (window list, grid tiles, hover
  tooltips, in-game overlay): **green** = fully BiS, **orange** = item correct but materia is off,
  **red** = the item itself is wrong or the slot is empty.
- **Shopping list is grouped by class.** With *All sets* active, items are split into sections by job
  — shared pieces collapse under a combined header (e.g. *PLD · WAR · DRK · GNB*) — so you can see at
  a glance what each item is for. (Materia stays in its own aggregated section.)

### Fixed
- **Overlay shows which materia is wrong vs missing**, not the full BiS list: equipped materia that
  don't belong in red, and the BiS materia you still need in orange (a multiset diff in `BisComparer`).
- **Ring materia is shown correctly per finger.** Exact (id + materia) ring matches are claimed
  first, so two same-id rings each pair with the right target regardless of finger.
- **BiS overlay compares against the live equipped gear**, recomputing whenever equipment changes —
  correct immediately after a swap, with no upload needed.
- **Materia is read from the live equipped gear, not the gearset snapshot**, so socketing materia
  into worn gear is detected/pushed without re-saving the gearset (covers both type and grade, so
  overmelds are no longer missed).
- **Change-detection push rules refined.** A push fires only when a gearset is saved or materia is
  socketed on the worn gear; swapping a piece or merely switching gearsets does not push.
- **Event-driven pushes (manual/login/gearset-change) bypass the auto-push throttle** so they send
  promptly; only the periodic auto-push stays throttled, and the 429 back-off still applies.
- **"Test connection" parse failure** — `GET /version` returns `scopes` as a JSON array;
  `VersionResponse.Scopes` is now `List<string>?` (with a regression test).

### Security
- **Security policy (`SECURITY.md`)** with private vulnerability reporting and an exact statement of
  the data the plugin sends (never the API key, never request/response bodies in logs).
- **Hardened CI/CD:** GitHub Actions are **pinned to commit SHAs** and kept current by **Dependabot**;
  **CodeQL** static analysis (C# + workflows) runs on the public repo; CI keeps build/test/format and
  a vulnerable-dependency scan with least-privilege `GITHUB_TOKEN`.
- **Release pipeline no longer pushes to `main`** — `pluginmaster.json` and `latest.zip` are
  published as release assets and served via the stable `releases/latest/download/` redirect, so the
  default branch can be fully protected.
