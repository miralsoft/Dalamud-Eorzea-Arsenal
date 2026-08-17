# Einstieg: `miralsoft/Dalamud-Plugins`

Du übernimmst die Pflege dieses Repositories. Es ist klein, fertig eingerichtet und hat genau eine
Aufgabe. **Die technische Beschreibung steht bereits im Repository — du musst nichts davon
rekonstruieren.** Dieser Text sagt dir nur, worum es geht und was dein Teil daran ist.

## Der Zusammenhang in fünf Sätzen

Der Betreiber (Sanaka, GitHub `miralsoft`) entwickelt mehrere, voneinander unabhängige Plugins für
das Online-Rollenspiel **Final Fantasy XIV**. Installiert werden sie über **Dalamud**, ein
Erweiterungs-Framework, in dem Spieler eigene Plugin-Quellen eintragen können — eine solche Quelle
ist technisch nur eine URL, die eine JSON-Liste ausliefert. Dieses Repository **ist** diese Liste:
ein Verzeichnis, das auf die Releases der einzelnen Plugin-Repositories verweist. Es enthält keinen
Plugin-Code und baut nichts. Alle Plugins sollen über **eine einzige Adresse** installierbar sein,
damit Nutzer nicht für jedes Plugin eine neue Quelle eintragen müssen.

## Lies das zuerst

| Datei | Inhalt |
| --- | --- |
| `docs/wie-der-index-funktioniert.md` | Aufbau, Adresskette, die Regeln, die nicht gebrochen werden dürfen, Testen |
| `docs/plugin-hinzufuegen.md` | Die Schritt-für-Schritt-Anleitung samt Fehlerdiagnose |
| `README.md` | Die Nutzerseite — Vorstellung der Plugins |
| `plugins.json` | Die Liste der Quell-Repositories (einzige von Hand gepflegte Datei) |

Danach unter *Actions* einmal den letzten Lauf von *Index* ansehen.

## Deine Aufgabe

**1. Die README pflegen — das ist der Hauptteil.**

Die README ist die **Nutzerseite**, nicht die Entwicklerdoku. Wer dort ankommt, will wissen: *Was
gibt es hier, was kann es, wie bekomme ich es?* In dieser Reihenfolge, ohne vorher zu erfahren, wie
das Verzeichnis intern arbeitet. Alles Interne gehört nach `docs/`.

Pro Plugin ein Abschnitt: Name, ein fetter Satz was es tut, ein paar Stichpunkte, Links zum
Plugin-Repository und ggf. zur Webseite. Halte dich an das Muster des vorhandenen Eintrags.

**2. Plugins aufnehmen** — eine Zeile in `plugins.json` plus der README-Abschnitt. Die vollständige
Anleitung samt Voraussetzungen und Fehlerbildern steht in `docs/plugin-hinzufuegen.md`; arbeite sie
ab, statt zu improvisieren.

**3. Die Doku aktuell halten**, wenn sich etwas am Ablauf ändert.

## Die drei Dinge, die du auf keinen Fall tun darfst

Die vollständige Liste steht in `docs/wie-der-index-funktioniert.md`. Diese drei werden erfahrungs-
gemäß mit den besten Absichten kaputtgemacht:

1. **`pluginmaster.json` von Hand bearbeiten.** Sie wird erzeugt. Jede Handänderung ist beim nächsten
   Lauf weg — oder bleibt und ist falsch.
2. **Die Fehlerbehandlung im Skript „aufräumen".** Ein Repository, das gerade nicht erreichbar ist,
   behält absichtlich seinen bisherigen Eintrag. Das sieht nach nachlässigem Code aus und ist das
   Gegenteil: Etwas nicht lesen zu können ist kein Beleg dafür, dass es weg ist. Würde der Eintrag
   verschwinden, nähme Dalamud das Plugin bei **jedem** Nutzer aus der Liste — wegen einer schlechten
   Minute bei GitHub.
3. **Die öffentliche Adresse ändern oder eine alte abschalten.** Nutzer haben sie eingetragen; sie
   erfahren von einer Änderung nichts und stünden ohne Updates da.

## Zur Adresse

Aktuell dokumentiert und bei Nutzern eingetragen:

```
https://xivarsenal.app/plugin.json
```

Geplant ist, zusätzlich `plugins.json` (Mehrzahl) anzubieten und künftig diese zu dokumentieren.
**`plugin.json` bleibt dabei dauerhaft bestehen.** Ändere die Adresse in README und Doku erst, wenn
die neue Route nachweislich live ist — vorher würdest du Besuchern einen 404 in die Hand geben. Ein
`curl -sI https://xivarsenal.app/plugins.json` beantwortet das in einer Sekunde.

Die Route selbst liegt auf der Website und wird dort von einem anderen Agenten betreut; sie gehört
nicht zu deinem Repository.

## Umgangsformen

- **Sprache mit dem Betreiber: Deutsch.**
- **Conventional Commits** (`feat:`, `fix:`, `docs:`, `chore:`).
- Git-Identität: `Sanaka <20637644+miralsoft@users.noreply.github.com>`.
- **Niemals `Co-Authored-By`-Zeilen oder sonstige KI-Hinweise** in Commits, PR-Texten oder Dateien.
  Ausdrückliche Regel des Betreibers.
- `main` ist die einzige Branch; kleine Änderungen dürfen direkt dorthin.
- Nach einer Änderung an `plugins.json` läuft der Index-Workflow sofort an — schau nach, dass er grün
  ist, bevor du „fertig" meldest.

## Zum Einstieg

Lies die beiden Dokumente unter `docs/`, dann `README.md` und `plugins.json`. Melde dich mit einer
kurzen Zusammenfassung, was du vorgefunden hast, und frag, welches Plugin als nächstes dazukommen
soll und wie es sich vorstellen möchte.
