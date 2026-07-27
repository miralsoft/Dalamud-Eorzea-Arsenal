namespace EorzeaArsenal.Localization;

/// <summary>How a release-note entry is classified, so the UI can badge it.</summary>
public enum ReleaseNoteKind
{
    /// <summary>A new capability.</summary>
    Added,

    /// <summary>An existing capability got better.</summary>
    Improved,

    /// <summary>A bug is gone.</summary>
    Fixed,
}

/// <summary>One user-facing line of a release note, in both supported languages.</summary>
/// <param name="Kind">Added / improved / fixed.</param>
/// <param name="Id">
/// A stable, repo-unique, kebab-case identifier. The web side remembers it as "already announced", so
/// it must <b>never</b> change once published — correcting a typo in the text must not turn an entry
/// into a new one. Written by hand for exactly that reason; the existing ones were slugged from their
/// English text once and are frozen from here on.
/// </param>
/// <param name="De">German text.</param>
/// <param name="En">English text.</param>
public sealed record ReleaseNoteItem(ReleaseNoteKind Kind, string Id, string De, string En);

/// <summary>One shipped version and what it changed for the user.</summary>
/// <param name="Version">Semantic version, e.g. <c>0.4.0</c>.</param>
/// <param name="Date">Release date, <c>yyyy-MM-dd</c>.</param>
/// <param name="Items">The highlights, in display order.</param>
public sealed record ReleaseNote(string Version, string Date, IReadOnlyList<ReleaseNoteItem> Items);

/// <summary>
/// The in-game "what's new" content: a deliberately short, plain-language digest of each release, as
/// opposed to the full CHANGELOG (which is written for contributors). Bundled with the plugin rather
/// than fetched, so it is available offline and can never disagree with the build the user is running.
/// Newest first; <see cref="Latest"/> doubles as the version the UI compares against.
/// </summary>
public static class ReleaseNotes
{
    /// <summary>Every release worth telling the user about, newest first.</summary>
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new ReleaseNote("1.0.0", "2026-07-27",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "report-a-problem-from-inside",
                "Problem melden, ohne das Spiel zu verlassen: Betreff, Text, senden — fertig. Wer du bist und wie man dir antwortet, kommt aus deiner Verbindung, du musst also weder Namen noch Adresse eintippen.",
                "Report a problem from inside the game: subject, text, send. Who you are and how to reply comes from your connection, so there is no name or address to type."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "version-1-0-the-plugin",
                "Version 1.0: Das Plugin verlässt die Erprobungsphase. Ausrüstungs-Abgleich, Kaufberater, Teams-Begleiter und wöchentliche Checkliste stehen — und ab hier gibt es zu jeder Neuerung eine Ankündigung statt einer stillen Aktualisierung.",
                "Version 1.0: the plugin leaves its trial phase. Gear comparison, purchase advisor, teams companion and the weekly checklist are all in place — and from here on every change is announced instead of arriving silently."),
        ]),

        new ReleaseNote("0.4.0", "2026-07-27",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "teams-companion-opt-in-your",
                "Teams-Begleiter (optional): Kalender, Mit-Pläne, Inhalte, Gruppen-Farm und FFLogs deiner Teams direkt im Spiel.",
                "Teams companion (opt-in): your teams' calendar, mit plans, content, group farm and FFLogs right in the game."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "rsvp-to-events-and-report",
                "Termine zu- und absagen sowie Abwesenheiten eintragen, ohne das Spiel zu verlassen.",
                "RSVP to events and report absences without leaving the game."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "mit-plans-as-a-timeline",
                "Mit-Pläne als Zeitstrahl: Mechaniken links, Cooldowns pro Job daneben, mit Skill-Icons und dem gewohnten Spiel-Tooltip. Phasen und Mechanik-Typen sind filterbar.",
                "Mit plans as a timeline: mechanics on the left, cooldowns per job beside them, with skill icons and the familiar in-game tooltip. Phases and mechanic types can be filtered."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "a-toast-with-a-sound",
                "Hinweis mit Ton und anklickbarem Chat-Link bei neuem Loot, Termin-Erinnerungen und neu geplanten Terminen.",
                "A toast with a sound and a clickable chat link for new loot, event reminders and newly planned events."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "group-farm-shows-for-every",
                "Die Gruppen-Farm zeigt zu jedem fehlenden Teil, woher es kommt (Savage / Tome+ / Tome), den Bezugsweg in einer Zeile und alle Wege beim Drüberfahren (Koffer, Abgabe-Teil, Händler mit Koordinaten).",
                "Group farm shows for every missing piece where it comes from (savage / Tome+ / Tome), the way to get it on one line, and every route on hover (coffer, hand-in piece, vendor with coordinates)."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "owned-gear-coffers-are-uploaded",
                "Besessene Ausrüstungs-Koffer werden mit hochgeladen, damit die Koffer-Seite im Web zeigt, wie viele du hast und wie viele Teile du sofort erstellen kannst.",
                "Owned gear coffers are uploaded too, so a coffer's web page shows how many you own and how many pieces you can make now."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "in-the-bis-window-hovering",
                "Im BiS-Fenster zeigt das Überfahren eines Teils jetzt auch 'Bezug': woher es kommt und was es kostet — wie in der Gruppen-Farm.",
                "In the BiS window, hovering a piece now also shows 'how to get it' — where it comes from and what it costs, just like the group farm."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "sourcing-is-now-shown-as",
                "Der Bezug wird jetzt als nummerierte Schritte gezeigt (Erspielen / Kaufen / Aufwerten) — bei Tome+ also 'zuerst Basis, dann aufwerten' — mit 'hast du / brauchst du' pro Schritt und 'NPC auf Karte anzeigen' per Rechtsklick.",
                "Sourcing is now shown as numbered steps (Fight / Buy / Upgrade) — so a Tome+ piece reads 'base first, then augment' — with a have/need count per step and a right-click 'Show NPC on map'."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "in-the-group-farm-each",
                "In der Gruppen-Farm steht neben jedem Mitglied, welche Materialien ihm insgesamt noch fehlen.",
                "In the group farm, each member now has a one-line summary of the materials they still need in total."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "have-need-now-counts-retainer",
                "'Hast du / brauchst du' zählt jetzt auch Gehilfen-Bestände mit (vom letzten Besuch), und 'Basis vorhanden' greift, egal ob die Basis getragen wird oder in Tasche/Gehilfe liegt.",
                "'Have / need' now counts retainer stock too (as of the last visit), and 'Base owned' applies whether the base is equipped or sitting in a bag/retainer."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "open-in-web-jumps-straight",
                "'Im Web öffnen' springt direkt auf den Plan, den Termin oder den Inhalt, den du gerade ansiehst.",
                "'Open in web' jumps straight to the plan, event or content you are looking at."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "new-purchase-advisor-what-to",
                "Neuer Kaufberater: was als Nächstes zu kaufen ist — mit Preis in Steinen, ob es jetzt oder erst in N Wochen reicht, Händler und Material pro Schritt. Dein Set als Raster wie im BiS-Vergleich, dazu deine eigene Aufstellung, die du im Spiel bearbeiten und speichern kannst.",
                "New purchase advisor: what to buy next — with its tomestone price, whether you can afford it now or in N weeks, the vendor and the material per step. Your set as a grid like the BiS view, plus your own layout, editable and saveable in game."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "in-the-bis-view-each",
                "Im BiS-Vergleich klappt unter jedem Set 'Noch nötig für dieses Set' auf: wie viele Steine die restlichen Käufe zusammen kosten (und wie viele Wochen das noch sind) sowie jedes Material und jedes Buch, das noch fehlt — inklusive der Bücher als Alternative zum Kistendrop.",
                "In the BiS view, each set folds out 'Still needed for this set': what the remaining purchases add up to in tomestones (and how many weeks that is), plus every material and raid book still missing — books included as the alternative to a coffer drop."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "this-very-page-what-s",
                "Dieser Bereich: 'Was ist neu' fasst nach jedem Update die wichtigsten Änderungen zusammen.",
                "This very page: 'What's new' sums up the important changes after each update."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "sourcing-now-speaks-your-language",
                "Bezugswege sprechen jetzt deine Sprache: Koffer, Material, Bücher, Händler, Zonen und Kampfnamen kommen aus den Spieldaten statt auf Englisch vom Server.",
                "Sourcing now speaks your language: coffers, materials, books, vendors, zones and fight names come from the game's own data instead of arriving in English."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "in-the-group-farm-your",
                "In der Gruppen-Farm steht bei deinen eigenen Charakteren der Weg vorn, den du jetzt gehen kannst: Koffer schon in der Tasche, Bücher für den Tausch beisammen, Teil längst da — statt dich zum Kampf zu schicken.",
                "In the group farm, your own characters lead with the way you can actually take now: the coffer already in your bag, the books for the trade in hand, the piece already yours — instead of being sent to fight for it."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "the-group-farm-measured-every",
                "In der Gruppen-Farm wurde der Bedarf jedes Mitglieds gegen deine eigenen Bestände gerechnet. Bei anderen steht jetzt, was das Set braucht — ihr Bestand ist für das Plugin nicht sichtbar und wird nicht mehr geraten.",
                "The group farm measured every member's remaining cost against your own stock. A teammate's row now states what the set requires — their stock is not visible to the plugin and is no longer guessed at."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "the-chat-command-is-now",
                "Der Chat-Befehl heißt jetzt /xivarsenal (vorher /bisexport) — das Plugin kann längst mehr als BiS-Export.",
                "The chat command is now /xivarsenal (was /bisexport) — the plugin long outgrew a pure BiS export."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "the-main-window-is-now",
                "Das Hauptfenster ist ein reines Menü; die Vorschau 'was wird gesendet' hat ein eigenes Fenster.",
                "The main window is now a pure menu; the 'what will be sent' preview moved into its own window."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "a-failed-team-request-now",
                "Fehlgeschlagene Team-Abrufe nennen jetzt die konkrete Ursache statt eines allgemeinen Fehlers.",
                "A failed team request now names the concrete cause instead of a generic error."),
        ]),

        new ReleaseNote("0.3.0", "2026-07-05",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "the-weekly-checklist-now-also",
                "Die Wochenliste füllt jetzt auch Unreal, das Tagebuch der Abenteuer, den normalen Raid und den Allianz-Raid automatisch.",
                "The weekly checklist now also fills Unreal, Wondrous Tails, the normal raid and the alliance raid automatically."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "sync-weekly-now-picks-up",
                "'Woche synchronisieren' holt die Raid- und Duty-Finder-Werte gleich mit, statt sie erst beim nächsten Login zu sehen.",
                "'Sync weekly' now picks up the raid and Duty Finder values too, instead of waiting for the next login."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "a-completed-wondrous-tails-book",
                "Ein abgeschlossenes Tagebuch aus der Vorwoche wird nicht mehr fälschlich als diese Woche erledigt gemeldet.",
                "A completed Wondrous Tails book from last week is no longer mis-reported as done this week."),
        ]),

        new ReleaseNote("0.2.0", "2026-07-02",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "weekly-checklist-auto-fill-opt",
                "Wochenliste automatisch füllen (optional): Steine, Sonderaufträge und die Savage-Ebenen. Es wird nur gesendet, was sicher erkannt wurde — deine manuellen Einträge bleiben unangetastet.",
                "Weekly checklist auto-fill (opt-in): tomestones, custom deliveries and the savage floors. Only values read with certainty are sent — your manual entries are never overwritten."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "reworked-windows-the-main-window",
                "Fenster überarbeitet: Hauptfenster als Menü mit großen Schaltflächen, Einstellungen in Reitern.",
                "Reworked windows: the main window is a menu with large buttons, settings are organised into tabs."),
        ]),

        new ReleaseNote("0.1.1", "2026-06-21",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "plugin-icon-in-the-dalamud",
                "Plugin-Symbol im Dalamud-Installer.",
                "Plugin icon in the Dalamud installer."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "the-repo-index-is-generated",
                "Der Repo-Index wird korrekt erzeugt, sodass Updates zuverlässig angeboten werden.",
                "The repo index is generated correctly, so updates are offered reliably."),
        ]),

        new ReleaseNote("0.1.0", "2026-06-21",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "first-public-release-push-gearsets",
                "Erste öffentliche Version: Gearsets an die Web-App senden, Inventar-Upload (optional) und der BiS-Vergleich im Spiel.",
                "First public release: push gearsets to the web app, inventory upload (opt-in) and the in-game BiS comparison."),
        ]),
    ];

    /// <summary>The newest release — also the version the "seen" marker is compared against.</summary>
    public static ReleaseNote Latest => All[0];

    /// <summary>Whether the newest release has not been acknowledged yet.</summary>
    /// <param name="lastSeenVersion">The version last marked as seen (may be empty/null).</param>
    /// <returns><see langword="true"/> when the user has not seen the current notes.</returns>
    public static bool HasUnseen(string? lastSeenVersion) =>
        !string.Equals(lastSeenVersion, Latest.Version, StringComparison.Ordinal);

    /// <summary>Picks the text for the active language.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="german">Whether German is active.</param>
    /// <returns>The localized line.</returns>
    public static string Text(ReleaseNoteItem item, bool german) => german ? item.De : item.En;
}
