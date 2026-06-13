namespace EorzeaArsenal.Localization;

/// <summary>
/// Stable string keys for every user-facing UI text. Using constants instead of raw strings
/// keeps call sites typo-proof and makes missing translations easy to find (R6). Each constant
/// is the lookup key; the German/English values live in <see cref="Localizer"/>.
/// </summary>
public static class LocKeys
{
    /// <summary>Plugin display name.</summary>
    public const string PluginName = "plugin.name";

    /// <summary>Config window title.</summary>
    public const string ConfigWindowTitle = "window.config.title";

    /// <summary>Help text for the /bisexport command.</summary>
    public const string CommandHelp = "command.help";

    /// <summary>"Language" label.</summary>
    public const string Language = "common.language";

    /// <summary>"Save" button.</summary>
    public const string Save = "common.save";

    /// <summary>"Close" button.</summary>
    public const string Close = "common.close";

    /// <summary>ToS notice header (R36).</summary>
    public const string TosHeader = "tos.header";

    /// <summary>ToS notice body (R36).</summary>
    public const string TosBody = "tos.body";

    /// <summary>ToS accept checkbox (R36).</summary>
    public const string TosAccept = "tos.accept";

    /// <summary>"Connected" status.</summary>
    public const string StatusConnected = "status.connected";

    /// <summary>"Not connected" status.</summary>
    public const string StatusDisconnected = "status.disconnected";

    /// <summary>API base URL label.</summary>
    public const string BaseUrlLabel = "config.baseurl.label";

    /// <summary>API base URL hint.</summary>
    public const string BaseUrlHint = "config.baseurl.hint";

    /// <summary>"Test connection" button.</summary>
    public const string TestConnection = "config.test.button";

    /// <summary>Connection-test success message (arg: protocol version).</summary>
    public const string TestOk = "config.test.ok";

    /// <summary>Connection-test failure message (arg: reason).</summary>
    public const string TestFailed = "config.test.failed";

    /// <summary>"Connect" section header.</summary>
    public const string ConnectHeader = "connect.header";

    /// <summary>Device-flow connect button.</summary>
    public const string ConnectDeviceFlow = "connect.deviceflow.button";

    /// <summary>Prompt to open the verification page.</summary>
    public const string ConnectOpenBrowser = "connect.openbrowser";

    /// <summary>User-code display (arg: code).</summary>
    public const string ConnectUserCode = "connect.usercode";

    /// <summary>"Waiting for approval" status.</summary>
    public const string ConnectWaiting = "connect.waiting";

    /// <summary>Connect success message.</summary>
    public const string ConnectSuccess = "connect.success";

    /// <summary>Connect failure message (arg: reason).</summary>
    public const string ConnectFailed = "connect.failed";

    /// <summary>"Cancel" connect button.</summary>
    public const string ConnectCancel = "connect.cancel";

    /// <summary>"Open browser again" button.</summary>
    public const string ConnectOpenBrowserAgain = "connect.openbrowseragain";

    /// <summary>Pasted-key field label.</summary>
    public const string PasteKeyLabel = "connect.pastekey.label";

    /// <summary>Pasted-key field hint.</summary>
    public const string PasteKeyHint = "connect.pastekey.hint";

    /// <summary>"Use pasted key" button.</summary>
    public const string PasteKeyButton = "connect.pastekey.button";

    /// <summary>"Disconnect" button (R42).</summary>
    public const string Disconnect = "connect.disconnect";

    /// <summary>Auto-push toggle.</summary>
    public const string AutoPush = "config.autopush";

    /// <summary>Auto-push hint (arg: interval minutes).</summary>
    public const string AutoPushHint = "config.autopush.hint";

    /// <summary>Push-on-login toggle.</summary>
    public const string PushOnLogin = "config.pushonlogin";

    /// <summary>Master opt-in toggle (R36).</summary>
    public const string EnablePushMaster = "config.enable.master";

    /// <summary>Master opt-in hint.</summary>
    public const string EnablePushMasterHint = "config.enable.master.hint";

    /// <summary>"Pushing…" status.</summary>
    public const string PushStarted = "push.started";

    /// <summary>Push success (arg: gearset count).</summary>
    public const string PushSuccess = "push.success";

    /// <summary>"Not connected" push message.</summary>
    public const string PushNotConnected = "push.notconnected";

    /// <summary>"Not logged in" push message.</summary>
    public const string PushNotLoggedIn = "push.notloggedin";

    /// <summary>"Already in progress" push message.</summary>
    public const string PushInProgress = "push.inprogress";

    /// <summary>"Failed local validation" push message.</summary>
    public const string PushInvalid = "push.invalid";

    /// <summary>"Nothing to push" message.</summary>
    public const string PushNothing = "push.nothing";

    /// <summary>401 error message.</summary>
    public const string Error401 = "error.401";

    /// <summary>403 error message.</summary>
    public const string Error403 = "error.403";

    /// <summary>409 error message.</summary>
    public const string Error409 = "error.409";

    /// <summary>422 error message.</summary>
    public const string Error422 = "error.422";

    /// <summary>400 error message.</summary>
    public const string Error400 = "error.400";

    /// <summary>429 error message.</summary>
    public const string Error429 = "error.429";

    /// <summary>Network error message.</summary>
    public const string ErrorNetwork = "error.network";

    /// <summary>Generic unexpected error message.</summary>
    public const string ErrorUnexpected = "error.unexpected";

    // Status window
    /// <summary>Status window title.</summary>
    public const string StatusWindowTitle = "window.status.title";

    /// <summary>"Last push" label.</summary>
    public const string StatusLastPush = "status.lastpush";

    /// <summary>"Never" value.</summary>
    public const string StatusNever = "status.never";

    /// <summary>Last result label.</summary>
    public const string StatusLastResult = "status.lastresult";

    /// <summary>Rate-limited until label (arg: seconds).</summary>
    public const string StatusRateLimited = "status.ratelimited";

    /// <summary>"Push now" button.</summary>
    public const string PushNow = "status.pushnow";

    /// <summary>"Open settings" button.</summary>
    public const string OpenSettings = "status.opensettings";

    /// <summary>"Open status" button.</summary>
    public const string OpenStatus = "config.openstatus";

    /// <summary>"Open web app" button.</summary>
    public const string OpenWebApp = "common.openwebapp";

    // Preview
    /// <summary>"Preview what will be sent" button.</summary>
    public const string PreviewButton = "preview.button";

    /// <summary>Preview header (arg: count).</summary>
    public const string PreviewHeader = "preview.header";

    /// <summary>Preview empty/none message.</summary>
    public const string PreviewEmpty = "preview.empty";

    // Scope check (R17)
    /// <summary>Warning that the key lacks gear:write.</summary>
    public const string ScopeMissing = "connect.scope.missing";

    // New push options
    /// <summary>Push-on-gearset-change toggle.</summary>
    public const string PushOnChange = "config.pushonchange";

    /// <summary>Push-on-gearset-change hint.</summary>
    public const string PushOnChangeHint = "config.pushonchange.hint";

    /// <summary>Toast toggle.</summary>
    public const string UseToasts = "config.toasts";

    /// <summary>Log verbosity label.</summary>
    public const string Verbosity = "config.verbosity";

    /// <summary>Web app URL label.</summary>
    public const string WebAppUrlLabel = "config.webappurl.label";

    /// <summary>Web app URL hint.</summary>
    public const string WebAppUrlHint = "config.webappurl.hint";

    // Per-character opt-in
    /// <summary>Characters section header.</summary>
    public const string CharactersHeader = "config.characters.header";

    /// <summary>Characters section hint.</summary>
    public const string CharactersHint = "config.characters.hint";

    /// <summary>No characters recorded yet.</summary>
    public const string CharactersNone = "config.characters.none";

    /// <summary>Message when the current character is opted out.</summary>
    public const string CharacterDisabled = "push.characterdisabled";

    // BiS comparison (Feature A)
    /// <summary>BiS window title.</summary>
    public const string BisWindowTitle = "window.bis.title";

    /// <summary>"Compare with BiS" / refresh button.</summary>
    public const string BisRefresh = "bis.refresh";

    /// <summary>"Open BiS comparison" button (status window).</summary>
    public const string BisOpen = "status.openbis";

    /// <summary>Loading status.</summary>
    public const string BisLoading = "bis.loading";

    /// <summary>Message when the key lacks gear:read (reconnect needed).</summary>
    public const string BisReconnect = "bis.reconnect";

    /// <summary>Message when no BiS target exists yet.</summary>
    public const string BisNone = "bis.none";

    /// <summary>Per-gearset "complete" marker.</summary>
    public const string BisComplete = "bis.complete";

    /// <summary>Per-gearset match summary (args: matched, total).</summary>
    public const string BisSummary = "bis.summary";

    /// <summary>Slot line: have X, BiS Y (args: current id, target id).</summary>
    public const string BisHave = "bis.have";

    /// <summary>Slot line: empty, BiS Y (arg: target id).</summary>
    public const string BisMissing = "bis.missing";

    /// <summary>Materia-differs note.</summary>
    public const string BisMateriaDiff = "bis.materiadiff";

    /// <summary>BiS hover-overlay header.</summary>
    public const string BisTooltipHeader = "bis.tooltip.header";

    /// <summary>BiS hover-overlay toggle.</summary>
    public const string BisTooltipToggle = "config.bistooltip";

    /// <summary>"you have {0}" line (current item name).</summary>
    public const string BisYouHave = "bis.youhave";

    /// <summary>"in your inventory/armoury" line.</summary>
    public const string BisOwned = "bis.owned";

    /// <summary>"not owned" line.</summary>
    public const string BisNotOwned = "bis.notowned";

    /// <summary>"Materia: {0}" line (target materia names).</summary>
    public const string BisMateriaList = "bis.materialist";

    /// <summary>"Wrong: {0}" line — equipped materia that should be removed.</summary>
    public const string BisMateriaWrong = "bis.materiawrong";

    /// <summary>"Missing: {0}" line — target materia that should be socketed.</summary>
    public const string BisMateriaMissing = "bis.materiamissing";

    /// <summary>Hint when the current gearset has no BiS target (arg: gearset index).</summary>
    public const string BisNoTarget = "bis.notarget";

    // Log / diagnostics window
    /// <summary>Log window title.</summary>
    public const string LogWindowTitle = "window.log.title";

    /// <summary>"Open log" button.</summary>
    public const string OpenLog = "status.openlog";

    /// <summary>"Copy" button.</summary>
    public const string LogCopy = "log.copy";

    /// <summary>"Clear" button.</summary>
    public const string LogClear = "log.clear";

    /// <summary>Empty-log placeholder.</summary>
    public const string LogEmpty = "log.empty";
}
