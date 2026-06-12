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
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [LocKeys.PluginName] = "Eorzea Arsenal",
        [LocKeys.ConfigWindowTitle] = "Eorzea Arsenal — Einstellungen",
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
