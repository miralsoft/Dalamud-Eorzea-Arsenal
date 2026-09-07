# `orphans[].similar[]` lässt zwei Zeilen nicht auseinanderhalten

Kleine Ergänzung an einem Feld, das sonst gut trägt. Kein Fehler, eine Lücke.

## Der Fall

Ein Spieler hat **zwei Gearsets mit demselben Job und demselben Namen**, hier zweimal `BRD / "Barde"`,
auf den Plätzen #15 und #40. Eine geparkte Zeile wird gegen beide verglichen, und `similar[]` liefert:

```json
[
  { "set_uid": "ae9f7de9…", "job": "BRD", "name": "Barde", "source": "plugin",
    "probability": 91, "matched_slots": 10, "total_slots": 11, "url": "…" },
  { "set_uid": "d65d9d3f…", "job": "BRD", "name": "Barde", "source": "plugin",
    "probability": 91, "matched_slots": 10, "total_slots": 11, "url": "…" }
]
```

Jedes Feld, das ein Spieler lesen kann, ist bei beiden gleich. Auf der Karte steht deshalb zweimal
wörtlich dasselbe, und das liest sich wie ein Zeichenfehler:

```
Sieht aus wie "Barde" (91%): 10 gleich, 0 mit anderer Materia, 1 anders
ähnelt außerdem "Barde", 91%
```

## Warum die Plugin-Seite es nicht selbst lösen kann

Naheliegend war, die Position aus der eigenen Identitätszuordnung nachzuschlagen und
`"Barde (im Spiel, #15)"` zu schreiben. Gebaut, getestet, wieder ausgebaut: `Resolve` geht über einen
starken Schlüssel (Job + Name + Ausrüstung) und fällt sonst auf Job + Name zurück. Trägt genau eine
zwischengespeicherte Zeile diesen schwachen Schlüssel, wird sie zurückgegeben, ohne die Ausrüstung
nachzuprüfen. Genau bei zwei gleichnamigen Sets führte das zu einer **falschen** Nummer statt zu keiner,
und eine falsche Position ist schlechter als gar keine. Die Auflösung ist selbst namensbasiert und
deshalb blind in genau dem Fall, für den die Beschriftung gedacht war.

## Bitte

Ein Feld in `orphans[].similar[]`, das die Einträge unterscheidbar macht. In der Reihenfolge, wie
brauchbar sie hier wären:

1. **`gear_index`**, wo die Zeile `active` ist. Eine Setnummer ist das, was der Spieler in seiner Liste
   sieht, und sie ist eindeutig. Abschnitt 6 des Vertrags sagt, Kandidaten und Waisen bekommen keine
   Position, und das ist dort richtig begründet: deren gespeicherter Index ist veraltet oder eine
   Bandnummer. Für eine `active` Zeile gilt das nicht, die steht wirklich dort.
2. **`state`**, damit die Karte überhaupt weiß, ob eine dieser Zeilen im Spiel hängt oder selbst geparkt
   ist. Heute ist das aus der Antwort nicht ablesbar.
3. **`last_seen_at`**, wie `candidates[]` es schon führt. Hilft bei zwei aktiven Zeilen weniger als eine
   Position, aber es wäre besser als nichts.

Eines davon genügt, `gear_index` wäre das beste. Wenn ihr Punkt 1 wegen der Positionsregel nicht wollt,
nehmen wir Punkt 2 und schreiben "noch im Spiel" statt einer Nummer.

## Was hier bis dahin passiert

Nichts Falsches. Die Karte nennt beide Zeilen beim Namen, ohne eine Nummer zu erfinden, und die
Verwechslungsgefahr ist gering, weil auf einer Waisen-Karte ohnehin keine Zuordnung angeboten wird:
`similar[]` ist dort Beleg für "behalten oder löschen", nicht eine Auswahl. Es sieht nur aus wie ein
doppelt gezeichneter Eintrag.
