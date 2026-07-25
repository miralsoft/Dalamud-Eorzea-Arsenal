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
/// <param name="De">German text.</param>
/// <param name="En">English text.</param>
public sealed record ReleaseNoteItem(ReleaseNoteKind Kind, string De, string En);

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
        new ReleaseNote("0.4.0", "2026-07-17",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Teams-Begleiter (optional): Kalender, Mit-Pläne, Inhalte, Gruppen-Farm und FFLogs deiner Teams direkt im Spiel.",
                "Teams companion (opt-in): your teams' calendar, mit plans, content, group farm and FFLogs right in the game."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Termine zu- und absagen sowie Abwesenheiten eintragen, ohne das Spiel zu verlassen.",
                "RSVP to events and report absences without leaving the game."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Mit-Pläne als Zeitstrahl: Mechaniken links, Cooldowns pro Job daneben, mit Skill-Icons und dem gewohnten Spiel-Tooltip. Phasen und Mechanik-Typen sind filterbar.",
                "Mit plans as a timeline: mechanics on the left, cooldowns per job beside them, with skill icons and the familiar in-game tooltip. Phases and mechanic types can be filtered."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Hinweis mit Ton und anklickbarem Chat-Link bei neuem Loot, Termin-Erinnerungen und neu geplanten Terminen.",
                "A toast with a sound and a clickable chat link for new loot, event reminders and newly planned events."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Die Gruppen-Farm zeigt zu jedem fehlenden Teil, woher es kommt (Savage / Tome+ / Tome), den Bezugsweg in einer Zeile und alle Wege beim Drüberfahren (Koffer, Abgabe-Teil, Händler mit Koordinaten).",
                "Group farm shows for every missing piece where it comes from (savage / Tome+ / Tome), the way to get it on one line, and every route on hover (coffer, hand-in piece, vendor with coordinates)."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Besessene Ausrüstungs-Koffer werden mit hochgeladen, damit die Koffer-Seite im Web zeigt, wie viele du hast und wie viele Teile du sofort erstellen kannst.",
                "Owned gear coffers are uploaded too, so a coffer's web page shows how many you own and how many pieces you can make now."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Im BiS-Fenster zeigt das Überfahren eines Teils jetzt auch 'Bezug': woher es kommt und was es kostet — wie in der Gruppen-Farm.",
                "In the BiS window, hovering a piece now also shows 'how to get it' — where it comes from and what it costs, just like the group farm."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Der Bezug wird jetzt als nummerierte Schritte gezeigt (Erspielen / Kaufen / Aufwerten) — bei Tome+ also 'zuerst Basis, dann aufwerten' — mit 'hast du / brauchst du' pro Schritt und 'NPC auf Karte anzeigen' per Rechtsklick.",
                "Sourcing is now shown as numbered steps (Fight / Buy / Upgrade) — so a Tome+ piece reads 'base first, then augment' — with a have/need count per step and a right-click 'Show NPC on map'."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "In der Gruppen-Farm steht neben jedem Mitglied, welche Materialien ihm insgesamt noch fehlen.",
                "In the group farm, each member now has a one-line summary of the materials they still need in total."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "'Hast du / brauchst du' zählt jetzt auch Gehilfen-Bestände mit (vom letzten Besuch), und 'Basis vorhanden' greift, egal ob die Basis getragen wird oder in Tasche/Gehilfe liegt.",
                "'Have / need' now counts retainer stock too (as of the last visit), and 'Base owned' applies whether the base is equipped or sitting in a bag/retainer."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "'Im Web öffnen' springt direkt auf den Plan, den Termin oder den Inhalt, den du gerade ansiehst.",
                "'Open in web' jumps straight to the plan, event or content you are looking at."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Neuer Kaufberater: zeigt den im Web angelegten Plan für die Zwischenausrüstung bis zum BiS und daneben deine Vorräte (Material, Stein, Bücher) mit den Server-Zahlen, also inklusive Gehilfen.",
                "New purchase advisor: shows the plan you built on the web for the gear on the way to BiS, next to your stock (materials, stone, books) counted server-side — retainers included."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Dieser Bereich: 'Was ist neu' fasst nach jedem Update die wichtigsten Änderungen zusammen.",
                "This very page: 'What's new' sums up the important changes after each update."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "Der Chat-Befehl heißt jetzt /xivarsenal (vorher /bisexport) — das Plugin kann längst mehr als BiS-Export.",
                "The chat command is now /xivarsenal (was /bisexport) — the plugin long outgrew a pure BiS export."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "Das Hauptfenster ist ein reines Menü; die Vorschau 'was wird gesendet' hat ein eigenes Fenster.",
                "The main window is now a pure menu; the 'what will be sent' preview moved into its own window."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "Fehlgeschlagene Team-Abrufe nennen jetzt die konkrete Ursache statt eines allgemeinen Fehlers.",
                "A failed team request now names the concrete cause instead of a generic error."),
        ]),

        new ReleaseNote("0.3.0", "2026-07-05",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Die Wochenliste füllt jetzt auch Unreal, das Tagebuch der Abenteuer, den normalen Raid und den Allianz-Raid automatisch.",
                "The weekly checklist now also fills Unreal, Wondrous Tails, the normal raid and the alliance raid automatically."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "'Woche synchronisieren' holt die Raid- und Duty-Finder-Werte gleich mit, statt sie erst beim nächsten Login zu sehen.",
                "'Sync weekly' now picks up the raid and Duty Finder values too, instead of waiting for the next login."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "Ein abgeschlossenes Tagebuch aus der Vorwoche wird nicht mehr fälschlich als diese Woche erledigt gemeldet.",
                "A completed Wondrous Tails book from last week is no longer mis-reported as done this week."),
        ]),

        new ReleaseNote("0.2.0", "2026-07-02",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Wochenliste automatisch füllen (optional): Steine, Sonderaufträge und die Savage-Ebenen. Es wird nur gesendet, was sicher erkannt wurde — deine manuellen Einträge bleiben unangetastet.",
                "Weekly checklist auto-fill (opt-in): tomestones, custom deliveries and the savage floors. Only values read with certainty are sent — your manual entries are never overwritten."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Improved,
                "Fenster überarbeitet: Hauptfenster als Menü mit großen Schaltflächen, Einstellungen in Reitern.",
                "Reworked windows: the main window is a menu with large buttons, settings are organised into tabs."),
        ]),

        new ReleaseNote("0.1.1", "2026-06-21",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
                "Plugin-Symbol im Dalamud-Installer.",
                "Plugin icon in the Dalamud installer."),
            new ReleaseNoteItem(
                ReleaseNoteKind.Fixed,
                "Der Repo-Index wird korrekt erzeugt, sodass Updates zuverlässig angeboten werden.",
                "The repo index is generated correctly, so updates are offered reliably."),
        ]),

        new ReleaseNote("0.1.0", "2026-06-21",
        [
            new ReleaseNoteItem(
                ReleaseNoteKind.Added,
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
