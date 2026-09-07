# `release` lässt die Zeile nicht beiseite, und damit fehlt der Weg zurück

*Vom Plugin an die Serverseite, 2026-08-31. Ein Befund, gemessen auf dev, mit bekannter Handlung.*

---

## Was gemacht wurde

Der Betreiber hat auf Charakter 29 die acht Waisen abgearbeitet. Sieben gelöscht, und auf einer,
`2b2f5874c741c4b34742de0cc6e349c0` (PCT, „Set 28"), **im Plugin „Aus dem Plugin nehmen" gedrückt**,
also `release`. Danach hat er dieselbe Zeile **auf der Website ausgeblendet**. In dieser Reihenfolge,
von ihm selbst bestätigt.

Das Ausblenden funktioniert und ist nicht gemeint: `view.hidden` steht auf `true`.

## Was der Vertrag zu `release` sagt

Zeile 688:

> the row becomes `source='manual'` **and `ignored`**: it leaves plugin governance, keeps its pin and
> its team share, is never parked again and raises no question. It stands in the inventory as a row
> that was put aside, and `reopen` is the way back from a decision made in haste

## Was gemessen wurde

```
heute Morgen   source=plugin   state=parked    in orphans[]: ja
jetzt          source=manual   state=null      in orphans[]: NEIN
               updated_at 2026-08-31 19:51:01
```

Die Umstellung auf `manual` ist passiert. Das `ignored` nicht.

**Der Beweis hängt nicht daran, wie `/gear/sets` das Feld `state` rendert.** Er steht in eurer eigenen
Regel zu `orphans[]`:

> `orphans[]` carries the ignored rows too, of either kind, each with its `state` and its `source`,
> which is what makes them reachable at all: `reopen` needs something to point at

Wäre die Zeile `ignored`, stünde sie dort. `orphans[]` ist leer.

## Was daraus folgt

**Der Weg zurück fehlt, den derselbe Absatz verspricht.** `reopen` braucht etwas zum Zeigen, und im
Plugin gibt es nichts mehr: eine von Hand gebaute Zeile, die niemand beiseitegelegt hat, steht laut
Vertrag bewusst nicht in `orphans[]`. Das Fenster sieht sie also gar nicht.

**Und sie ist jetzt unentfernbar.** Als `manual` lehnt ihr `delete` aus dem Plugin ab, 422
`verb_not_applicable`, vor jeder anderen Regel. Die Zeile sitzt auf `gear_index` 101 und keine der
beiden Seiten wird sie los. Das ist der Zustand, gegen den `reopen` gebaut wurde, nur ohne `reopen`.

**Ein dritter Nebeneffekt, der fast ironisch ist.** `hidden` liefert ihr laut Vertrag deshalb an das
Plugin, damit ein Fenster den Satz „dieses Set ist auf der Website verborgen" sagen kann. Für diese
Zeile kann er nie erscheinen, weil sie aus der einzigen Liste gefallen ist, in der das Plugin ihn
sagen würde.

## Was wir brauchen

Entweder `release` setzt `ignored`, wie der Vertrag es beschreibt, oder der Vertrag beschreibt etwas
anderes als das, was gewollt ist. Beides ist in Ordnung, aber die beiden müssen sich einigen: das
Plugin zeichnet, was in `orphans[]` steht, und die Verben, die `OfferedVerbs` daraus ableitet, hängen
an `state` und `source`.

Falls `ignored` das Gewollte ist, hilft uns zusätzlich zu wissen, was mit den Zeilen geschieht, die
seit dem Fehler freigegeben wurden. Diese eine ist bekannt; ob es weitere gibt, seht nur ihr.

## Was ausdrücklich **kein** Befund ist

- **Das Ausblenden.** Es hat getan, was es soll, und die Umstellung auf `manual` ging ihm voraus.
- **`has_pin`.** Geklärt, unsere Prüfung war falsch, eure Definition steht jetzt im Vertrag.
- **Ein Löschen auf der Website.** Wir hatten das zwischendurch vermutet. Es hat nie stattgefunden.
