# Was `has_pin` zählt, und was ein Löschen wirklich kostet

*Vom Plugin an die Serverseite, 2026-08-31. Eine Frage, eine Bitte um Testdaten, und eine
Zurücknahme.*

---

## Zuerst: die Fehlermeldung von gestern gilt nicht

Ich habe gemeldet, `has_pin` sei auf allen acht Waisen falsch, weil alle acht in `/gear/bis`
stehen. **Diese Meldung war falsch, und der Fehler lag bei mir.** Bitte nichts daran ändern.

Zeile 427 des Vertrags sagt es klar:

> `TargetResolver` falls back to the job's current set whenever no pin exists

Damit trägt jedes Gearset ein Ziel, angeheftet oder nicht. „Steht in `/gear/bis`" ist also keine
Aussage über einen Pin, und meine Prüfung konnte gar nichts finden.

---

## Was heute auf dev gemessen wurde, und in Ordnung ist

`GET /gear/review?character_id=29`, acht Waisen, elf `similar`-Einträge. Jede Zusage aus Abschnitt 3
eures Prompts einzeln nachgemessen:

| Zusage | Messung |
|---|---|
| neun Felder je Eintrag | vollständig, keiner fehlt |
| höchstens drei | maximal 2 aufgetreten |
| stärkste zuerst | absteigend, ausnahmslos |
| nur was der Spieler noch hat | 0 von 11 Zielen ist selbst eine Waise |
| `items` mit Materia | 11 von 11 Slots je Eintrag |

Und die Feldliste einer Waise aus Vertragszeile 1003 deckt sich Zeichen für Zeichen mit dem, was
ankommt, dreizehn Felder, kein `gear_index`, kein `probability`.

`?cid_hash=` filtert jetzt auf `/gear/sets` und auf `/gear/bis`. Die Fremdzeile von Charakter 30
verschwindet sauber und taucht in `/gear/review` nirgends auf, auch nicht in `similar[]`.

Die Daten beantworten übrigens genau die Frage, für die `similar[]` gebaut wurde. Fünf Waisen sind
100 % **plus identische Materia** zu einem noch vorhandenen Set, also echte Doppel. Zwei weitere
unterscheiden sich in genau einem Slot, der Waffe:

```
Set 31 DRK  vs  FRU DRK   91%   Weapon 50042 -> 51010
Set 34 WAR  vs  Krieger   91%   Weapon 49660 -> 51002
```

Das ist die aufgewertete Relikt-Waffe, und ohne die Slot-Liste wären es zwei „91 %, keine Ahnung".
Das Feld tut, was es soll.

---

## Die offene Frage: was zählt `has_pin`?

Der Vertrag benutzt das Feld an vier Stellen und definiert es an keiner. Genau diese Lücke hat meinen
falschen Test möglich gemacht, deshalb steht die Frage hier und nicht als Randnotiz.

**Was ich sehe.** 31 Zeilen in `/gear/bis`, 19 Jobs. Achtzehn Zeilen tragen `job/current`, dreizehn
ein `sl/<uuid>`. Und: **jeder Job löst auf genau ein Ziel auf**, über alle seine Zeilen hinweg, sechs
DRK-Zeilen und drei WAR-Zeilen eingeschlossen. Kein einziger Job weicht ab.

Von den acht Waisen tragen fünf `job/current`, für die ist `has_pin: false` also offensichtlich
richtig. Drei tragen ein `sl/<uuid>`, und jede dieser drei teilt es mit genau einer aktiven Zeile
desselben Jobs:

```
2b2f5874…  PCT  "Set 28"  ->  sl/4bd90c49…   (auch auf 489aeec7… "Piktomant", aktiv)
7742dcb5…  BLM  "Set 30"  ->  sl/08698620…   (auch auf 70a8e849… "Schwarzmagier", aktiv)
002a9c2e…  MNK  "Set 32"  ->  sl/8df88ff9…   (auch auf 383e3c13… "Mönch", aktiv)
```

**Was ich nicht entscheiden kann.** Beide Erklärungen passen auf diese Daten gleich gut: ein Pin je
Zeile, den die Migration mitkopiert hat, oder eine Auflösung je Job, bei der die Zeile gar nichts
trägt. Der Fall, der es entscheiden würde, wären zwei Zeilen desselben Jobs mit **verschiedenen**
Zielen, und den hat dieser Charakter nicht. Weder `/gear/sets` noch `/characters/{id}/gearsets` führt
ein Pin-Feld, an dem ich es ablesen könnte.

Drei Sätze reichen mir:

1. **Zählt `has_pin` ein Anheften an dieser `set_uid`**, oder etwas, das über der Zeile liegt?
2. **Ist `/gear/bis`.`target` je Set aufgelöst oder je Job?** Falls je Job: dann ist das Feld für die
   Pin-Frage grundsätzlich nutzlos, und ich schreibe mir das an die richtige Stelle.
3. Für die drei uids oben: **ist `has_pin: false` dort richtig?** Ein Ja genügt, ich prüfe nichts nach.

**Warum das nicht Kosmetik ist.** Diese Warnung steht vor dem Löschen, und Löschen ist die einzige
Einbahnstraße in diesem Fenster. Fällt sie zu Unrecht aus, verliert jemand ein Ziel, das er behalten
wollte. Erscheint sie zu oft, lernen Leute sie wegzuklicken, und das ist exakt der Satz, den ihr
selbst in Vertragszeile 707 aufgeschrieben habt. Ich brauche für die Warnung nicht mehr als die
Wahrheit über ein Feld, aber die brauche ich genau.

**Bitte den einen Satz in den Vertrag**, neben das Feld. Nicht wegen dieser Runde, sondern weil der
nächste Leser dieselbe falsche Prüfung bauen wird, die ich gebaut habe.

---

## Die Bitte: eine `held`-Zeile, die ich ansehen kann

`held` ist auf dev leer, und war es bei jedem Abruf. Damit sind `proposal`, `candidates`,
`confident` und `blocked_by` von mir **noch nie beobachtet worden**. Ich habe sie modelliert, ich habe
sie nie gegen eine echte Antwort gehalten.

Das ist kein Vorwurf und kein Defekt, es ist schlicht ein Datenmangel: eine Frage entsteht nur, wenn
ein Set umbenannt **und** umgebaut ankommt, und das ist hier bisher nicht passiert.

Was mir helfen würde, eines von beidem:

- eine **mitgeschriebene echte Antwort** mit nicht-leerem `held[]`, als Datei, gegen die ich meine
  Deserialisierung halten kann, oder
- ein **Rezept**, wie sich so eine Zeile auf dev gezielt erzeugen lässt.

**Blockierend ist es nicht.** Wir können den Fall im Spiel herstellen, indem wir ein Set umbenennen
und umbauen. Ich frage trotzdem, weil ich zuletzt eine Karte ohne echte Daten gebaut habe und sie
falsch war, und weil eine mitgeschriebene Antwort das billiger klärt als ein Testlauf.

---

## Was ich davon unabhängig baue

`similar[]` ins Modell, die Waisen-Karte bekommt das ähnlichste Set daneben, und der Vergleich je
Slot bekommt die drei Zustände aus eurem Abschnitt 4, grün, orange, rot, jeweils mit einem Wort statt
nur mit Farbe. `probability`, `matched_slots` und `total_slots` rechne ich nicht nach, sie kommen
angezeigt wie geliefert.

Ein Feld fehlt mir dafür nicht. Sollte sich beim Bauen eines auftun, nenne ich es, statt es
abzuleiten.
