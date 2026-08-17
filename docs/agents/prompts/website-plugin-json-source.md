# Bitte: `/plugin.json` auf eine neue Quelle umstellen

## Worum es geht

`https://xivarsenal.app/plugin.json` ist die Adresse, die Nutzer in Dalamud als *Custom Plugin
Repository* eintragen. Sie liefert heute den Index für **ein** Plugin (Eorzea Arsenal) und holt ihn
aus dessen Release-Asset.

Künftig sollen **mehrere, voneinander unabhängige Plugins** über dieselbe Adresse installierbar sein.
Die Liste wird ab sofort in einem eigenen Repository zusammengestellt, das sie automatisch aus den
Releases aller beteiligten Plugin-Repos baut (stündlich).

**Neue Quelle:**

```
https://raw.githubusercontent.com/miralsoft/Dalamud-Plugins/main/pluginmaster.json
```

## Die Bitte

`/plugin.json` soll diese Datei ausliefern statt der bisherigen Quelle. **Das ist die einzige
Änderung.**

Inhaltlich ändert sich dabei nichts: Es ist dasselbe Format — ein JSON-Array von Plugin-Manifesten —
und es steht dort aktuell genau ein Eintrag, derselbe wie bisher, in derselben Version. Nachgeprüft:

```
$ curl -s https://raw.githubusercontent.com/miralsoft/Dalamud-Plugins/main/pluginmaster.json
[
{
  "Author": "Sanaka",
  "Name": "Eorzea Arsenal",
  "InternalName": "EorzeaArsenalPlugin",
  "AssemblyVersion": "1.0.0.0",
  ...
```

Es kommen mit der Zeit nur weitere Einträge dazu, ohne dass auf eurer Seite je wieder etwas zu tun
wäre.

## Zweite Bitte: `/plugins.json` als zusätzliche Route

Weil künftig mehrere Plugins darüber laufen, soll die Adresse zusätzlich in der **Mehrzahl**
erreichbar sein:

```
https://xivarsenal.app/plugins.json
```

Beide Routen liefern **denselben Inhalt** — am besten derselbe Handler, zweimal registriert.

**`/plugin.json` (Einzahl) muss dauerhaft bestehen bleiben.** Sie ist bei Nutzern in Dalamud
eingetragen; wird sie abgeschaltet oder auf eine Weiterleitung umgestellt, die Dalamud nicht folgt,
verlieren diese Nutzer alle Plugins aus ihrer Liste, ohne je zu erfahren warum. Sie ist kein
Übergang, sondern ein dauerhafter Zweitname.

Sobald `/plugins.json` live ist, bitte kurz Bescheid geben — dann wird sie auf der Website und in der
Dokumentation zur genannten Adresse. Vorher darf sie **nirgends** dokumentiert werden, sonst bekommen
Besucher einen 404.

## Worauf es dabei ankommt

1. **Bei einem Fehler die letzte gute Fassung weiterliefern.** Ist GitHub gerade nicht erreichbar
   oder liefert eine kaputte Antwort, darf `/plugin.json` weder einen Fehler noch eine leere Liste
   ausgeben. Dalamud würde daraus schließen, dass es die Plugins nicht mehr gibt, und sie den Nutzern
   aus der Liste nehmen. Ein veralteter Index ist harmlos, ein leerer nicht.
2. **Vor dem Ausliefern prüfen: nicht-leeres JSON-Array.** Alles andere ist ein Fehlerfall im Sinne
   von Punkt 1 und darf den Cache nicht überschreiben.
3. **`Content-Type: application/json`** beibehalten, ebenso das Caching (aktuell
   `Cache-Control: public, max-age=600`) — das passt gut zum stündlichen Rhythmus der Quelle.
4. **Kein BOM** am Dateianfang; Dalamuds Parser lehnt ihn ab. Die Quelldatei ist BOM-frei, sie darf
   beim Durchreichen nur keinen bekommen.

## Ausdrücklich nicht nötig

- **Keine Änderung an der `/neu`-Seite und am Discord-Announcer.** Die weiteren Plugins sind eigene
  Anwendungen und sollen dort **nicht** auftauchen. `changelog.json` bleibt wie es ist und gehört
  weiterhin allein zu Eorzea Arsenal.
- **Keine Kenntnis der einzelnen Plugins.** Die Liste wird ausschließlich im Index-Repository
  gepflegt; für euch ist es eine Datei, die durchgereicht wird.

## Zum Nachprüfen

Vorher wie nachher muss gelten:

```bash
# beide Adressen, gleicher Inhalt, beginnt mit [ und enthält "EorzeaArsenalPlugin"
curl -s https://xivarsenal.app/plugin.json  | head -c 200
curl -s https://xivarsenal.app/plugins.json | head -c 200

# beide application/json
curl -sI https://xivarsenal.app/plugin.json  | grep -i content-type
curl -sI https://xivarsenal.app/plugins.json | grep -i content-type
```
