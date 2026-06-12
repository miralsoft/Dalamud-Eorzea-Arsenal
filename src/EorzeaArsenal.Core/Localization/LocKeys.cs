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
}
