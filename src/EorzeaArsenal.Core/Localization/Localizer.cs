using System.Globalization;
using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Localization;

/// <summary>
/// Default <see cref="ILocalizer"/> with built-in German and English resources. Unknown keys
/// return the key itself so missing translations are obvious in the UI. Resources are embedded
/// dictionaries to avoid any external resource/IO dependency (R41).
/// </summary>
public sealed class Localizer : ILocalizer
{
    /// <summary>The English language code.</summary>
    public const string English = "en";

    /// <summary>The German language code.</summary>
    public const string German = "de";

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LocKeys.PluginName] = "Eorzea Arsenal",
        [LocKeys.ConfigWindowTitle] = "Eorzea Arsenal — Settings",
        [LocKeys.CommandHelp] = "Push your gearsets to Eorzea Arsenal (opens settings if not yet enabled).",
        [LocKeys.Language] = "Language",
        [LocKeys.Save] = "Save",
        [LocKeys.Close] = "Close",

        [LocKeys.TosHeader] = "Third-party tool notice",
        [LocKeys.TosBody] =
            "Dalamud and third-party tools violate the FFXIV Terms of Service. Using this plugin " +
            "is at your own risk. Connecting and pushing your gear is strictly opt-in.",
        [LocKeys.TosAccept] = "I understand and accept the risk",

        [LocKeys.StatusConnected] = "Connected",
        [LocKeys.StatusDisconnected] = "Not connected",

        [LocKeys.BaseUrlLabel] = "API base URL",
        [LocKeys.BaseUrlHint] = "Full URL including /api/v1, e.g. http://127.0.0.1:8080/api/v1",
        [LocKeys.TestConnection] = "Test connection",
        [LocKeys.TestOk] = "Connection OK (protocol v{0}).",
        [LocKeys.TestFailed] = "Connection failed: {0}",

        [LocKeys.ConnectHeader] = "Connect",
        [LocKeys.ConnectDeviceFlow] = "Connect via browser",
        [LocKeys.ConnectOpenBrowser] = "Open this page in your browser and approve the code:",
        [LocKeys.ConnectUserCode] = "Your code: {0}",
        [LocKeys.ConnectWaiting] = "Waiting for approval…",
        [LocKeys.ConnectSuccess] = "Connected successfully.",
        [LocKeys.ConnectFailed] = "Connection failed: {0}",
        [LocKeys.ConnectCancel] = "Cancel",
        [LocKeys.ConnectOpenBrowserAgain] = "Open browser again",
        [LocKeys.PasteKeyLabel] = "API key",
        [LocKeys.PasteKeyHint] = "Or paste a key created at /me/keys in the web app.",
        [LocKeys.PasteKeyButton] = "Use pasted key",
        [LocKeys.Disconnect] = "Disconnect",

        [LocKeys.AutoPush] = "Push automatically",
        [LocKeys.AutoPushHint] = "Push at most every {0} minutes when something changed.",
        [LocKeys.PushOnLogin] = "Push on login",
        [LocKeys.EnablePushMaster] = "Enable connecting and pushing",
        [LocKeys.EnablePushMasterHint] = "Master opt-in. While off, the plugin never contacts the API.",

        [LocKeys.PushStarted] = "Pushing gear…",
        [LocKeys.PushSuccess] = "Pushed {0} gearset(s).",
        [LocKeys.PushNotConnected] = "Not connected. Connect first.",
        [LocKeys.PushNotLoggedIn] = "No character is logged in.",
        [LocKeys.PushInProgress] = "A push is already in progress.",
        [LocKeys.PushInvalid] = "Gear data failed validation; nothing was sent.",
        [LocKeys.PushNothing] = "No gearsets to push.",

        [LocKeys.Error401] = "Your key is invalid or expired. Please reconnect.",
        [LocKeys.Error403] = "Your key lacks the gear:write permission. Reconnect with a correct key.",
        [LocKeys.Error409] = "This character is already linked to another account.",
        [LocKeys.Error422] = "The server rejected the gear data (validation).",
        [LocKeys.Error400] = "The payload was too large or malformed.",
        [LocKeys.Error429] = "Rate limit reached (30 uploads/hour). Backing off.",
        [LocKeys.ErrorNetwork] = "Could not reach the server. Check the base URL and your connection.",
        [LocKeys.ErrorUnexpected] = "Unexpected error.",

        [LocKeys.StatusWindowTitle] = "Eorzea Arsenal — Status",
        [LocKeys.StatusLastPush] = "Last push",
        [LocKeys.StatusNever] = "never",
        [LocKeys.StatusLastResult] = "Last result",
        [LocKeys.StatusRateLimited] = "Rate limited — backing off for ~{0}s.",
        [LocKeys.PushNow] = "Push now",
        [LocKeys.OpenSettings] = "Open settings",
        [LocKeys.OpenStatus] = "Open status",
        [LocKeys.OpenWebApp] = "Open web app",
        [LocKeys.PreviewButton] = "Preview what will be sent",
        [LocKeys.PreviewHeader] = "{0} gearset(s) would be sent:",
        [LocKeys.PreviewEmpty] = "Nothing to send (not logged in or no valid gearsets).",
        [LocKeys.ScopeMissing] = "Connected, but the key is missing the gear:write permission.",
        [LocKeys.PushOnChange] = "Push when a gearset changes",
        [LocKeys.PushOnChangeHint] = "Detects in-game changes and pushes (still rate-limited).",
        [LocKeys.UseToasts] = "Show toast notifications",
        [LocKeys.Verbosity] = "Log detail",
        [LocKeys.WebAppUrlLabel] = "Web app URL (optional)",
        [LocKeys.WebAppUrlHint] = "Used by the \"Open web app\" button. If empty, derived from the base URL.",
        [LocKeys.CharactersHeader] = "Characters",
        [LocKeys.CharactersHint] = "Choose which of your characters may be pushed.",
        [LocKeys.CharactersNone] = "No characters seen yet — log in to add one.",
        [LocKeys.CharacterDisabled] = "This character is disabled for pushing.",

        [LocKeys.BisWindowTitle] = "Eorzea Arsenal — Gear vs BiS",
        [LocKeys.BisRefresh] = "Compare with BiS",
        [LocKeys.BisOpen] = "Gear vs BiS",
        [LocKeys.BisLoading] = "Loading BiS targets…",
        [LocKeys.BisReconnect] = "Your key has no read access. Please reconnect to enable BiS comparison.",
        [LocKeys.BisNone] = "No BiS target found. Pin one for your gearsets in the web app.",
        [LocKeys.BisComplete] = "complete",
        [LocKeys.BisSummary] = "{0}/{1} slots match",
        [LocKeys.BisHave] = "have {0} → BiS {1}",
        [LocKeys.BisMissing] = "empty → BiS {0}",
        [LocKeys.BisMateriaDiff] = "materia differs",
        [LocKeys.BisTooltipHeader] = "Eorzea Arsenal — BiS",
        [LocKeys.BisTooltipToggle] = "Show BiS info on item hover",
        [LocKeys.BisYouHave] = "equipped: {0}",
        [LocKeys.BisOwned] = "you own this item",
        [LocKeys.BisNotOwned] = "not in your inventory/armoury",
        [LocKeys.BisMateriaList] = "Materia: {0}",

        ["slot.weapon"] = "Weapon",
        ["slot.offhand"] = "Off Hand",
        ["slot.head"] = "Head",
        ["slot.body"] = "Body",
        ["slot.hands"] = "Hands",
        ["slot.legs"] = "Legs",
        ["slot.feet"] = "Feet",
        ["slot.ears"] = "Ears",
        ["slot.neck"] = "Neck",
        ["slot.wrists"] = "Wrists",
        ["slot.ringleft"] = "Ring (left)",
        ["slot.ringright"] = "Ring (right)",

        ["source.crafted"] = "Crafted",
        ["source.raid"] = "Raid",
        ["source.tome"] = "Tomestone",
        ["source.alliance"] = "Alliance Raid",
        ["source.dungeon"] = "Dungeon",
        ["source.extreme"] = "Extreme",
        ["source.trial"] = "Trial",
        ["source.ultimate"] = "Ultimate",
        ["source.relic"] = "Relic",
        ["source.pvp"] = "PvP",
        ["source.other"] = "Other",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LocKeys.PluginName] = "Eorzea Arsenal",
        [LocKeys.ConfigWindowTitle] = "Eorzea Arsenal — Einstellungen",
        [LocKeys.CommandHelp] = "Überträgt deine Gearsets an Eorzea Arsenal (öffnet die Einstellungen, falls noch nicht aktiviert).",
        [LocKeys.Language] = "Sprache",
        [LocKeys.Save] = "Speichern",
        [LocKeys.Close] = "Schließen",

        [LocKeys.TosHeader] = "Hinweis zu Drittanbieter-Tools",
        [LocKeys.TosBody] =
            "Dalamud und Drittanbieter-Tools verstoßen gegen die FFXIV-Nutzungsbedingungen. Die Nutzung " +
            "dieses Plugins erfolgt auf eigenes Risiko. Das Verbinden und Übertragen deiner Ausrüstung ist " +
            "ausdrücklich freiwillig (Opt-in).",
        [LocKeys.TosAccept] = "Ich verstehe das Risiko und akzeptiere es",

        [LocKeys.StatusConnected] = "Verbunden",
        [LocKeys.StatusDisconnected] = "Nicht verbunden",

        [LocKeys.BaseUrlLabel] = "API-Basis-URL",
        [LocKeys.BaseUrlHint] = "Vollständige URL inkl. /api/v1, z. B. http://127.0.0.1:8080/api/v1",
        [LocKeys.TestConnection] = "Verbindung testen",
        [LocKeys.TestOk] = "Verbindung OK (Protokoll v{0}).",
        [LocKeys.TestFailed] = "Verbindung fehlgeschlagen: {0}",

        [LocKeys.ConnectHeader] = "Verbinden",
        [LocKeys.ConnectDeviceFlow] = "Über Browser verbinden",
        [LocKeys.ConnectOpenBrowser] = "Öffne diese Seite im Browser und bestätige den Code:",
        [LocKeys.ConnectUserCode] = "Dein Code: {0}",
        [LocKeys.ConnectWaiting] = "Warte auf Bestätigung…",
        [LocKeys.ConnectSuccess] = "Erfolgreich verbunden.",
        [LocKeys.ConnectFailed] = "Verbindung fehlgeschlagen: {0}",
        [LocKeys.ConnectCancel] = "Abbrechen",
        [LocKeys.ConnectOpenBrowserAgain] = "Browser erneut öffnen",
        [LocKeys.PasteKeyLabel] = "API-Schlüssel",
        [LocKeys.PasteKeyHint] = "Oder füge einen unter /me/keys erstellten Schlüssel ein.",
        [LocKeys.PasteKeyButton] = "Eingefügten Schlüssel verwenden",
        [LocKeys.Disconnect] = "Trennen",

        [LocKeys.AutoPush] = "Automatisch übertragen",
        [LocKeys.AutoPushHint] = "Höchstens alle {0} Minuten übertragen, wenn sich etwas geändert hat.",
        [LocKeys.PushOnLogin] = "Beim Login übertragen",
        [LocKeys.EnablePushMaster] = "Verbinden und Übertragen aktivieren",
        [LocKeys.EnablePushMasterHint] = "Haupt-Opt-in. Solange deaktiviert, kontaktiert das Plugin die API nie.",

        [LocKeys.PushStarted] = "Übertrage Ausrüstung…",
        [LocKeys.PushSuccess] = "{0} Gearset(s) übertragen.",
        [LocKeys.PushNotConnected] = "Nicht verbunden. Bitte zuerst verbinden.",
        [LocKeys.PushNotLoggedIn] = "Kein Charakter eingeloggt.",
        [LocKeys.PushInProgress] = "Eine Übertragung läuft bereits.",
        [LocKeys.PushInvalid] = "Ausrüstungsdaten ungültig; es wurde nichts gesendet.",
        [LocKeys.PushNothing] = "Keine Gearsets zum Übertragen.",

        [LocKeys.Error401] = "Dein Schlüssel ist ungültig oder abgelaufen. Bitte neu verbinden.",
        [LocKeys.Error403] = "Deinem Schlüssel fehlt die Berechtigung gear:write. Bitte mit korrektem Schlüssel verbinden.",
        [LocKeys.Error409] = "Dieser Charakter ist bereits mit einem anderen Konto verknüpft.",
        [LocKeys.Error422] = "Der Server hat die Ausrüstungsdaten abgelehnt (Validierung).",
        [LocKeys.Error400] = "Die Nutzlast war zu groß oder fehlerhaft.",
        [LocKeys.Error429] = "Ratenlimit erreicht (30 Uploads/Stunde). Warte ab.",
        [LocKeys.ErrorNetwork] = "Server nicht erreichbar. Prüfe die Basis-URL und deine Verbindung.",
        [LocKeys.ErrorUnexpected] = "Unerwarteter Fehler.",

        [LocKeys.StatusWindowTitle] = "Eorzea Arsenal — Status",
        [LocKeys.StatusLastPush] = "Letzte Übertragung",
        [LocKeys.StatusNever] = "nie",
        [LocKeys.StatusLastResult] = "Letztes Ergebnis",
        [LocKeys.StatusRateLimited] = "Ratenlimit — warte noch ~{0}s.",
        [LocKeys.PushNow] = "Jetzt übertragen",
        [LocKeys.OpenSettings] = "Einstellungen öffnen",
        [LocKeys.OpenStatus] = "Status öffnen",
        [LocKeys.OpenWebApp] = "Webapp öffnen",
        [LocKeys.PreviewButton] = "Vorschau: was wird gesendet",
        [LocKeys.PreviewHeader] = "{0} Gearset(s) würden gesendet:",
        [LocKeys.PreviewEmpty] = "Nichts zu senden (nicht eingeloggt oder keine gültigen Gearsets).",
        [LocKeys.ScopeMissing] = "Verbunden, aber dem Schlüssel fehlt die Berechtigung gear:write.",
        [LocKeys.PushOnChange] = "Bei Gearset-Änderung übertragen",
        [LocKeys.PushOnChangeHint] = "Erkennt Änderungen im Spiel und überträgt (weiterhin ratenbegrenzt).",
        [LocKeys.UseToasts] = "Toast-Benachrichtigungen anzeigen",
        [LocKeys.Verbosity] = "Log-Detailgrad",
        [LocKeys.WebAppUrlLabel] = "Webapp-URL (optional)",
        [LocKeys.WebAppUrlHint] = "Für den \"Webapp öffnen\"-Button. Leer = aus der Basis-URL abgeleitet.",
        [LocKeys.CharactersHeader] = "Charaktere",
        [LocKeys.CharactersHint] = "Wähle, welche deiner Charaktere übertragen werden dürfen.",
        [LocKeys.CharactersNone] = "Noch keine Charaktere erfasst — logge dich ein.",
        [LocKeys.CharacterDisabled] = "Dieser Charakter ist für die Übertragung deaktiviert.",

        [LocKeys.BisWindowTitle] = "Eorzea Arsenal — Gear vs BiS",
        [LocKeys.BisRefresh] = "Mit BiS vergleichen",
        [LocKeys.BisOpen] = "Gear vs BiS",
        [LocKeys.BisLoading] = "Lade BiS-Ziele…",
        [LocKeys.BisReconnect] = "Dein Schlüssel hat keinen Lesezugriff. Bitte neu verbinden, um den BiS-Vergleich zu nutzen.",
        [LocKeys.BisNone] = "Kein BiS-Ziel gefunden. Lege im Webapp eines für deine Gearsets fest.",
        [LocKeys.BisComplete] = "vollständig",
        [LocKeys.BisSummary] = "{0}/{1} Slots passen",
        [LocKeys.BisHave] = "habe {0} → BiS {1}",
        [LocKeys.BisMissing] = "leer → BiS {0}",
        [LocKeys.BisMateriaDiff] = "Materia abweichend",
        [LocKeys.BisTooltipHeader] = "Eorzea Arsenal — BiS",
        [LocKeys.BisTooltipToggle] = "BiS-Info beim Item-Hover anzeigen",
        [LocKeys.BisYouHave] = "ausgerüstet: {0}",
        [LocKeys.BisOwned] = "im Besitz (Inventar/Arsenal)",
        [LocKeys.BisNotOwned] = "nicht im Inventar/Arsenal",
        [LocKeys.BisMateriaList] = "Materia: {0}",

        ["slot.weapon"] = "Waffe",
        ["slot.offhand"] = "Nebenhand",
        ["slot.head"] = "Kopf",
        ["slot.body"] = "Rumpf",
        ["slot.hands"] = "Hände",
        ["slot.legs"] = "Beine",
        ["slot.feet"] = "Füße",
        ["slot.ears"] = "Ohren",
        ["slot.neck"] = "Hals",
        ["slot.wrists"] = "Handgelenke",
        ["slot.ringleft"] = "Ring (links)",
        ["slot.ringright"] = "Ring (rechts)",

        ["source.crafted"] = "Handwerk",
        ["source.raid"] = "Raid",
        ["source.tome"] = "Steine",
        ["source.alliance"] = "Allianz-Raid",
        ["source.dungeon"] = "Dungeon",
        ["source.extreme"] = "Extrem",
        ["source.trial"] = "Prüfung",
        ["source.ultimate"] = "Ultimativ",
        ["source.relic"] = "Relikt",
        ["source.pvp"] = "PvP",
        ["source.other"] = "Sonstige",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Languages =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [English] = En,
            [German] = De,
        };

    private string _language = English;

    /// <summary>Creates a localizer in the given language (defaults to English if unknown).</summary>
    /// <param name="language">Initial two-letter language code.</param>
    public Localizer(string language = English) => Language = language;

    /// <inheritdoc />
    public string Language
    {
        get => _language;
        set => _language = Languages.ContainsKey(value) ? value : English;
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        var table = Languages[_language];
        if (table.TryGetValue(key, out var value))
        {
            return value;
        }

        // Fall back to English, then to the key itself so gaps are visible.
        return Languages[English].TryGetValue(key, out var en) ? en : key;
    }

    /// <inheritdoc />
    public string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
