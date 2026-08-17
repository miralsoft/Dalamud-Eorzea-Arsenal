# Briefing: ein eigenständiges Gearset-Plugin für FFXIV

Du sollst ein neues Dalamud-Plugin entwickeln. Dieses Dokument enthält die Aufgabe, die bereits
getroffenen Entscheidungen samt ihrer Begründung, und den Entwurf einer Schnittstelle, die **später**
dazukommt, aber **von Anfang an mitgeplant** werden soll.

Die Entscheidungen unten sind das Ergebnis einer ausführlichen Abwägung. Du darfst sie hinterfragen,
wenn du einen echten Fehler darin siehst — aber bitte nicht aus Gewohnheit neu aufrollen.

---

## 1. Der Zusammenhang

**Final Fantasy XIV** ist ein Online-Rollenspiel. **Dalamud** ist ein Erweiterungs-Framework, über
das Spieler Plugins installieren. Der Auftraggeber (Sanaka, GitHub `miralsoft`) entwickelt eine
Familie unabhängiger FFXIV-Plugins, die alle über **eine** Adresse installierbar sind:

```
https://xivarsenal.app/plugin.json
```

Dahinter liegt ein Verzeichnis-Repository (`miralsoft/Dalamud-Plugins`), das die Releases der
einzelnen Plugin-Repositories einsammelt. Für dich heißt das: **Dein Plugin bekommt ein eigenes
öffentliches Repository mit eigenen Releases.** Aufgenommen wird es später durch eine einzige Zeile
im Verzeichnis — dafür musst du nichts vorbereiten außer: Repository öffentlich, und am Release hängt
das von DalamudPackager erzeugte ZIP.

Es existiert bereits ein großes Plugin dieser Familie: **Eorzea Arsenal**
(`miralsoft/Dalamud-Eorzea-Arsenal`). Es überträgt Gearsets an ein Web-Konto und vergleicht sie mit
dem BiS („Best in Slot"). Es ist **nicht** dein Projekt, aber es wird später zum Partner — siehe
Abschnitt 5.

---

## 2. Das Problem, das gelöst werden soll

In eigenen Worten des Auftraggebers, sinngemäß:

> Wenn ich alle meine Gearsets auf Hotbars lege, um zwischen ihnen zu wechseln, verbrauche ich
> unheimlich viele Leisten — Platz, den ich für anderes brauche. Besonders schlimm mit den ganzen
> Hand- und Sammelberufen. Gehe ich stattdessen auf die Ebene, die das Spiel dafür vorsieht, habe ich
> eine extrem lange Liste, in der ich heraussuchen muss, welcher Job welches Set ist. Benennen kann
> ich sie, aber **filtern kann ich nicht**, richtig ansehen kann ich sie nicht, und Kommentare oder
> Notizen kann ich auch nicht setzen. Alles läuft über den Namen. Das ist sehr unübersichtlich.

Das Plugin soll also das **Auswählen und Wechseln von Gearsets** übernehmen: filtern, suchen,
gruppieren, ansehen, eigene Notizen — statt Hotbar-Plätze zu verbrauchen oder eine ungefilterte
Liste durchzuscrollen.

Der genaue Funktionsumfang der ersten Fassung ist **noch nicht festgelegt**. Kläre ihn mit dem
Auftraggeber. Naheliegende Bausteine: Suche über Name und Job, Gruppierung nach Kampf- /
Handwerks- / Sammelberufen, Favoriten, eigene Notizen pro Set, ein kompaktes Schnellwahl-Fenster,
Bedienung per Tastenkürzel.

---

## 3. Die Grundsatzentscheidung: eigenständig, ohne Server

Das Plugin wird **nicht** Teil von Eorzea Arsenal, obwohl das technisch möglich wäre. Die Gründe, in
der Reihenfolge ihres Gewichts:

1. **Es braucht kein Konto.** Eorzea Arsenal ist im Kern der Client eines Dienstes: Konto,
   API-Schlüssel, Server. Ein Gearset-Umschalter braucht davon nichts — er liest das Spiel und
   schaltet um. Eingebaut in Arsenal müsste jeder, der bloß eine bessere Auswahl will, ein Plugin
   installieren, das ihn nach einem Web-Konto fragt. Die meisten tun das nicht. Ein Werkzeug mit
   breitem Publikum an ein Nadelöhr zu binden, das es nicht braucht, wäre der teuerste Fehler bei
   dieser Entscheidung — und später kaum zu korrigieren.
2. **Getrennte Veröffentlichungsgründe.** Arsenals Rhythmus hängt an Änderungen des Server-Vertrags.
   Eine reine Oberflächen-Funktion würde entweder darauf warten oder Server-Releases erzwingen, weil
   ein Filter klemmt.
3. **Arsenal ist bereits groß** (BiS-Vergleich, Kaufberater, Teams, Kalender, Wochenaufgaben,
   Inventar, Fehlermeldungen). Noch eine Hauptoberfläche macht es nicht besser.
4. **Ein zweites Plugin kostet die Nutzer inzwischen einen Klick**, weil das gemeinsame Verzeichnis
   existiert. Das frühere Gegenargument („die Leute installieren nicht fünf Sachen") ist weg.

**Daraus folgt die wichtigste Regel für dich:**

> **Das Plugin funktioniert vollständig ohne Eorzea Arsenal, ohne Konto und ohne Internet.**
> Es gibt keine Funktion, die eine Verbindung voraussetzt. Keine.

---

## 4. Vorab zu klären: gibt es das schon?

Gearset-Verwaltung ist ein naheliegendes Problem und die Dalamud-Landschaft ist groß. **Sieh dich
zuerst um, ob es bereits ein gutes Plugin dafür gibt**, und berichte, was du findest. Wenn ja, ist
die interessantere Frage, was dort fehlt — möglicherweise ist genau die BiS-Anbindung aus Abschnitt 5
das, was sonst niemand liefern kann. Das wäre dann sogar das stärkere Produkt. Bau nichts nach, ohne
das geprüft zu haben.

---

## 5. Die spätere Ehe mit Eorzea Arsenal

**Kommt später. Wird jetzt nicht gebaut. Soll jetzt mitgedacht werden.**

### Die Idee

Sind **beide** Plugins installiert, soll dein Umschalter neben jedem Gearset anzeigen können, wie
weit es vom BiS entfernt ist — etwa `14/16 · Ultimate-BiS`. Diese Information hat sonst niemand, und
sie macht die Auswahl inhaltlich besser statt nur bequemer.

Dalamud bietet dafür einen Mechanismus, mit dem ein Plugin eine kleine Schnittstelle bereitstellt und
ein anderes sie benutzt, wenn sie vorhanden ist (Plugin-IPC über Call Gates).

### Die vier Regeln, die das tragfähig machen

1. **Die Abhängigkeit zeigt in genau eine Richtung.** Arsenal *bietet an*, dein Plugin *fragt*.
   Niemals umgekehrt, niemals beidseitig. Sobald sich zwei Plugins gegenseitig brauchen, kann keines
   mehr allein veröffentlicht werden — dann wäre die Kopplung, die die Trennung vermeiden sollte,
   durch die Hintertür zurück.
2. **Die BiS-Logik bleibt vollständig bei Arsenal.** Dein Plugin rechnet nichts aus, es zeigt an, was
   es bekommt. Würde es die Regeln nachbauen — welcher Slot zählt, wie Ringe paarweise behandelt
   werden, wann ein Teil „passt" — gäbe es zwei Wahrheiten, die auseinanderlaufen. Genau diese Sorte
   Fehler hat das Arsenal-Projekt bereits mehrfach beschäftigt.
3. **Der Vertrag bleibt winzig und trägt eine Version im Namen.** Nutzer fahren beliebige
   Versionskombinationen. Eine Schnittstelle wächst leicht und schrumpft nie wieder.
4. **Ohne Arsenal darf nichts kaputt aussehen.** Das Abzeichen belegt **nur dann** Platz, wenn Daten
   da sind — kein leerer Kasten, kein Platzhalter, keine Fehlermeldung. Der häufigste Fehler bei
   solchen Anbindungen ist, die Oberfläche um die Zusatzinformation herum zu bauen; dann sieht das
   Plugin für die Mehrheit ohne Arsenal defekt aus.

### Entwurf des Vertrags

Vorschlag, den die Arsenal-Seite umsetzen wird. Er ist noch nicht endgültig — Rückmeldungen dazu sind
ausdrücklich erwünscht, **bevor** eine Seite ihn implementiert.

**Name der Call Gate:** `EorzeaArsenal.GearsetBis.V1`

**Signatur:** `Func<uint, string?>` — hinein geht ein **Gearset-Index**, heraus kommt ein kurzer
JSON-String oder `null`.

```jsonc
// Antwort, wenn Arsenal etwas über dieses Gearset weiß:
{ "matched": 14, "total": 16, "target": "Ultimate BiS" }

// null, wenn: kein Konto verbunden, kein BiS für diesen Job hinterlegt,
//             Gearset unbekannt, Daten noch nicht geladen.
```

Die Begründung der drei ungewöhnlichen Entscheidungen:

- **Nur ein Index als Eingabe.** Arsenal liest das Gearset selbst — es kann das ohnehin. So
  überquert **keine** Ausrüstungsinformation die Plugin-Grenze, und der Vertrag bleibt eine Zahl.
- **JSON-String als Rückgabe statt eines eigenen Typs.** Plugins werden in getrennten Kontexten
  geladen; ein selbst definierter Typ ist auf beiden Seiten *nicht* derselbe Typ, auch wenn er gleich
  heißt. Primitive Typen und Zeichenketten überqueren die Grenze problemlos. Der JSON-String hat
  zusätzlich den Vorteil, dass Arsenal später Felder ergänzen kann, ohne ältere Fassungen deines
  Plugins zu brechen — unbekannte Felder werden einfach ignoriert.
- **Die Version steckt im Namen der Gate**, nicht in einem Feld. So kann Arsenal irgendwann `V2`
  daneben anbieten und `V1` noch eine Weile mitlaufen lassen, statt beides gleichzeitig umzustellen.

**Pflichten auf deiner Seite (dem Fragenden):**

- Die Gate kann **fehlen** (Arsenal nicht installiert), eine **andere Version** haben, oder beim
  Aufruf eine **Ausnahme** werfen (Arsenal wird gerade neu geladen). Alle drei Fälle sind normal und
  bedeuten schlicht: kein Abzeichen, weitermachen.
- **Nicht pro Bild aufrufen.** Das Ergebnis zwischenspeichern und nur dann neu holen, wenn sich etwas
  ändern konnte (Gearset gewechselt, Ausrüstung geändert, Fenster geöffnet). Ein IPC-Aufruf in der
  Zeichenschleife ist eine Bremse für beide Plugins.
- Niemals blockierend warten.

### Reihenfolge

**Zuerst das Plugin allein, fertig und gut.** Es muss Leute überzeugen, die von Eorzea Arsenal nie
gehört haben. Erst danach die Anbindung. Zwei Gründe: Du weißt dann aus der Benutzung heraus, welche
Information wirklich fehlt, statt sie zu erraten — und auf der Arsenal-Seite entsteht keine
Schnittstelle auf Verdacht, die danach ewig mitgeschleppt werden muss.

---

## 6. Technische Hinweise zum Einstieg

Als Ausgangspunkte, **bitte selbst verifizieren** statt ungeprüft zu übernehmen:

- Gearsets liegen in FFXIVClientStructs unter `RaptureGearsetModule` — Name, Job, Index, belegte
  Plätze. Eorzea Arsenal liest sie bereits; sein Quellcode ist öffentlich und ein brauchbares
  Vorbild.
- Für das Umschalten gibt es sowohl einen Weg über das Modul als auch den Gearset-Befehl des Spiels.
  Prüfe beide und wähle den, der sich robust und regelkonform verhält.
- Aktuelles Dalamud-API-Level: **15**. Oberflächen entstehen mit ImGui über `Dalamud.Bindings.ImGui`
  und `ImRaii`.

**Grundregeln, die im Arsenal-Projekt gelten und hier genauso gelten sollen:**

- Jeder Zugriff auf Spielspeicher läuft auf dem Framework-Thread und hinter Prüfungen auf
  „eingeloggt" und „nicht null".
- **Keine Ausnahme darf je das Spiel erreichen.** Jeder Lesevorgang ist abgesichert.
- **Keine Automatik.** Ein Wechsel passiert, weil der Spieler ihn auslöst — keine Schleifen, keine
  Aktionen ohne Anlass.
- Zweisprachig Deutsch/Englisch, wobei die Sprache der **Einstellung des Plugins** folgt, nicht der
  des Spielclients.

---

## 7. Umgangsformen

- **Sprache mit dem Auftraggeber: Deutsch.**
- **Conventional Commits** (`feat:`, `fix:`, `docs:`, `chore:`).
- Git-Identität: `Sanaka <20637644+miralsoft@users.noreply.github.com>`.
- **Niemals `Co-Authored-By`-Zeilen oder sonstige KI-Hinweise** in Commits, PR-Texten oder Dateien.
  Ausdrückliche Regel des Auftraggebers.
- Vor jedem Commit sauber bauen und testen; Warnungen gelten als Fehler.
- Veröffentlicht wird über Tags; das Release hängt das gepackte ZIP an. Erst **nach** dem Merge
  taggen, und nur auf ausdrückliche Anweisung — **niemals von selbst.**

---

## 8. Womit du anfängst

1. Abschnitt 4 abarbeiten: Gibt es das schon, und was fehlt dort?
2. Mit dem Auftraggeber den Umfang der ersten Fassung klären — welche Ansicht, welche Filter, ob
   Notizen pro Charakter oder global, wie das Fenster geöffnet wird.
3. Einen Namen und das Repository festlegen.
4. Dann bauen: erst das Auswählen und Wechseln, danach Komfort.

Melde dich mit deiner Einschätzung zu Punkt 1 und 2, bevor du Code schreibst.
