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

    /// <summary>Help text for the /xivarsenal command.</summary>
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

    /// <summary>Config: show "how to get it" sourcing on BiS pieces.</summary>
    public const string BisShowSourcing = "config.bissourcing";

    /// <summary>Config: sourcing toggle hint.</summary>
    public const string BisShowSourcingHint = "config.bissourcing.hint";

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

    /// <summary>BiS window: "current set" scope.</summary>
    public const string BisScopeCurrent = "bis.scope.current";

    /// <summary>BiS window: "all sets" scope.</summary>
    public const string BisScopeAll = "bis.scope.all";

    /// <summary>BiS window: filter label.</summary>
    public const string BisFilterLabel = "bis.filter";

    /// <summary>BiS window filter: all slots.</summary>
    public const string BisFilterAll = "bis.filter.all";

    /// <summary>BiS window filter: incomplete only.</summary>
    public const string BisFilterIncomplete = "bis.filter.incomplete";

    /// <summary>BiS window filter: materia issues only.</summary>
    public const string BisFilterMateria = "bis.filter.materia";

    /// <summary>BiS window: nothing matches the current filter.</summary>
    public const string BisNothingShown = "bis.nothingshown";

    /// <summary>"Copy name" context action.</summary>
    public const string BisCopyName = "bis.copyname";

    /// <summary>Hover hint for clickable items.</summary>
    public const string BisItemHint = "bis.itemhint";

    /// <summary>Server-info-bar click hint.</summary>
    public const string DtrClickHint = "dtr.clickhint";

    /// <summary>Toggle: show the server-info-bar entry.</summary>
    public const string ShowDtrBar = "config.dtrbar";

    /// <summary>Hint for the server-info-bar toggle.</summary>
    public const string ShowDtrBarHint = "config.dtrbar.hint";

    /// <summary>BiS window: "shopping list" toggle.</summary>
    public const string BisShoppingList = "bis.shopping";

    /// <summary>Shopping list: "items to get" header.</summary>
    public const string BisShoppingItems = "bis.shopping.items";

    /// <summary>Shopping list: "materia to get" header.</summary>
    public const string BisShoppingMateria = "bis.shopping.materia";

    /// <summary>Shopping list: nothing left to get.</summary>
    public const string BisShoppingEmpty = "bis.shopping.empty";

    /// <summary>BiS window: "grid" (character-screen layout) toggle.</summary>
    public const string BisGridView = "bis.grid";

    // Owned items / inventory upload (Phase 2)
    /// <summary>Opt-in: also upload owned items (inventory).</summary>
    public const string SyncInventory = "config.syncinventory";

    /// <summary>Hint for the inventory opt-in.</summary>
    public const string SyncInventoryHint = "config.syncinventory.hint";

    /// <summary>Opt-in: also scan retainers when visited.</summary>
    public const string SyncRetainers = "config.syncretainers";

    /// <summary>Hint for the retainer opt-in.</summary>
    public const string SyncRetainersHint = "config.syncretainers.hint";

    /// <summary>"Sync inventory" button (status window).</summary>
    public const string InventorySyncButton = "inventory.sync.button";

    /// <summary>Hint to visit retainers to refresh their stock.</summary>
    public const string InventorySyncHint = "inventory.sync.hint";

    /// <summary>"Syncing inventory…" status.</summary>
    public const string InventoryStarted = "inventory.started";

    /// <summary>Inventory upload success (args: item count, scope count).</summary>
    public const string InventorySuccess = "inventory.success";

    /// <summary>"Last inventory sync" label.</summary>
    public const string StatusLastInventory = "status.lastinventory";

    /// <summary>403 error message specific to the inventory:write scope.</summary>
    public const string Error403Inventory = "error.403.inventory";

    // Weekly checklist (Phase 3)
    /// <summary>Opt-in: also sync the weekly checklist.</summary>
    public const string SyncWeekly = "config.syncweekly";

    /// <summary>Hint for the weekly-checklist opt-in.</summary>
    public const string SyncWeeklyHint = "config.syncweekly.hint";

    /// <summary>"Sync weekly" button (status window).</summary>
    public const string WeeklySyncButton = "weekly.sync.button";

    /// <summary>"Syncing weekly checklist…" status.</summary>
    public const string WeeklyStarted = "weekly.started";

    /// <summary>Weekly-checklist sync success (arg: merged field count).</summary>
    public const string WeeklySuccess = "weekly.success";

    /// <summary>"Last weekly sync" label.</summary>
    public const string StatusLastWeekly = "status.lastweekly";

    /// <summary>403 error message specific to the characters:write scope.</summary>
    public const string Error403Weekly = "error.403.weekly";

    // Window layout: hub sections + config tabs
    /// <summary>Hub section header: sync actions.</summary>
    public const string SectionActions = "status.section.actions";

    /// <summary>Hub section header: view/open things.</summary>
    public const string SectionView = "status.section.view";

    /// <summary>Hub section header: management.</summary>
    public const string SectionManage = "status.section.manage";

    /// <summary>Hub hint shown when not connected.</summary>
    public const string StatusConnectHint = "status.connecthint";

    /// <summary>Config tab: sync/upload options.</summary>
    public const string TabSync = "config.tab.sync";

    /// <summary>Config tab: display options.</summary>
    public const string TabDisplay = "config.tab.display";

    /// <summary>Config tab: per-character opt-in.</summary>
    public const string TabCharacters = "config.tab.characters";

    /// <summary>Config tab: connection management.</summary>
    public const string TabConnection = "config.tab.connection";

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

    // Teams companion
    /// <summary>Teams window: enable/connect hint.</summary>
    public const string TeamsDisabledHint = "teams.disabledhint";

    /// <summary>Teams window: reconnect-for-scope hint.</summary>
    public const string TeamsScopeHint = "teams.scopehint";

    /// <summary>Teams status-window entry / window title.</summary>
    public const string TeamsOpen = "teams.open";

    /// <summary>Config: enable the Teams companion.</summary>
    public const string SyncTeams = "config.syncteams";

    /// <summary>Config: Teams companion hint.</summary>
    public const string SyncTeamsHint = "config.syncteams.hint";

    /// <summary>Tab: calendar.</summary>
    public const string TeamsTabCalendar = "teams.tab.calendar";

    /// <summary>Tab: mit cheat sheet.</summary>
    public const string TeamsTabMit = "teams.tab.mit";

    /// <summary>Tab: content hub.</summary>
    public const string TeamsTabContent = "teams.tab.content";

    /// <summary>Tab: farm.</summary>
    public const string TeamsTabFarm = "teams.tab.farm";

    /// <summary>Tab: FFLogs.</summary>
    public const string TeamsTabLogs = "teams.tab.logs";

    /// <summary>Tab: absence.</summary>
    public const string TeamsTabAbsence = "teams.tab.absence";

    /// <summary>Team picker label.</summary>
    public const string TeamsTeamLabel = "teams.teamlabel";

    /// <summary>Loading placeholder.</summary>
    public const string TeamsLoading = "teams.loading";

    /// <summary>No teams placeholder.</summary>
    public const string TeamsNoTeams = "teams.noteams";

    /// <summary>Refresh button.</summary>
    public const string TeamsRefresh = "teams.refresh";

    /// <summary>Help button.</summary>
    public const string TeamsHelp = "teams.help";

    /// <summary>Working/saving placeholder.</summary>
    public const string TeamsWorking = "teams.working";

    /// <summary>Saved acknowledgement.</summary>
    public const string TeamsSaved = "teams.saved";

    /// <summary>No events placeholder.</summary>
    public const string TeamsNoEvents = "teams.noevents";

    /// <summary>Linked-content label.</summary>
    public const string TeamsContentsLabel = "teams.contentslabel";

    /// <summary>Attendance counts format: yes {0}, maybe {1}, no {2}, total {3}.</summary>
    public const string TeamsAttendCounts = "teams.attendcounts";

    /// <summary>RSVP yes.</summary>
    public const string TeamsRsvpYes = "teams.rsvp.yes";

    /// <summary>RSVP maybe.</summary>
    public const string TeamsRsvpMaybe = "teams.rsvp.maybe";

    /// <summary>RSVP no.</summary>
    public const string TeamsRsvpNo = "teams.rsvp.no";

    /// <summary>Plan picker label.</summary>
    public const string TeamsPlanLabel = "teams.planlabel";

    /// <summary>Job picker label.</summary>
    public const string TeamsJobLabel = "teams.joblabel";

    /// <summary>Show all jobs checkbox.</summary>
    public const string TeamsAllJobs = "teams.alljobs";

    /// <summary>No plan placeholder.</summary>
    public const string TeamsNoPlan = "teams.noplan";

    /// <summary>Mechanics header.</summary>
    public const string TeamsMechanics = "teams.mechanics";

    /// <summary>Cooldowns header.</summary>
    public const string TeamsCooldowns = "teams.cooldowns";

    /// <summary>No placements placeholder.</summary>
    public const string TeamsNoPlacements = "teams.noplacements";

    /// <summary>Bosses header.</summary>
    public const string TeamsBosses = "teams.bosses";

    /// <summary>Resources header.</summary>
    public const string TeamsResources = "teams.resources";

    /// <summary>No content placeholder.</summary>
    public const string TeamsNoContent = "teams.nocontent";

    /// <summary>No farm data placeholder.</summary>
    public const string TeamsNoFarm = "teams.nofarm";

    /// <summary>Core (Stamm) tag.</summary>
    public const string TeamsCore = "teams.core";

    /// <summary>Substitute (Ersatz) tag.</summary>
    public const string TeamsSubstitute = "teams.substitute";

    /// <summary>No BiS target placeholder.</summary>
    public const string TeamsTargetNone = "teams.targetnone";

    /// <summary>All BiS complete.</summary>
    public const string TeamsComplete = "teams.complete";

    /// <summary>Still-missing label.</summary>
    public const string TeamsMissing = "teams.missing";

    /// <summary>FFLogs not connected placeholder.</summary>
    public const string TeamsLogsNotConnected = "teams.logs.notconnected";

    /// <summary>No reports placeholder.</summary>
    public const string TeamsNoReports = "teams.noreports";

    /// <summary>Kills/wipes format: {0} kills, {1} wipes.</summary>
    public const string TeamsKillsWipes = "teams.killswipes";

    /// <summary>Absence: from date.</summary>
    public const string TeamsAbsenceFrom = "teams.absence.from";

    /// <summary>Absence: to date.</summary>
    public const string TeamsAbsenceTo = "teams.absence.to";

    /// <summary>Absence: note.</summary>
    public const string TeamsAbsenceNote = "teams.absence.note";

    /// <summary>Absence: add button.</summary>
    public const string TeamsAbsenceAdd = "teams.absence.add";

    /// <summary>Absence: delete button.</summary>
    public const string TeamsAbsenceDelete = "teams.absence.delete";

    /// <summary>Absence: invalid date message.</summary>
    public const string TeamsAbsenceInvalid = "teams.absence.invalid";

    /// <summary>No absences placeholder.</summary>
    public const string TeamsNoAbsences = "teams.noabsences";

    /// <summary>Generic teams error.</summary>
    public const string TeamsErrorGeneric = "teams.error.generic";

    /// <summary>Forbidden (capability) error.</summary>
    public const string TeamsErrorForbidden = "teams.error.forbidden";

    /// <summary>Not a member (404) error.</summary>
    public const string TeamsNotMember = "teams.error.notmember";

    /// <summary>Network error.</summary>
    public const string TeamsErrorNetwork = "teams.error.network";

    /// <summary>Calendar hub entry / window.</summary>
    public const string TeamsCalendarOpen = "teams.calendar.open";

    /// <summary>Calendar: open in web.</summary>
    public const string TeamsCalendarOpenWeb = "teams.calendar.openweb";

    /// <summary>Calendar: pick a day hint.</summary>
    public const string TeamsCalendarPickDay = "teams.calendar.pickday";

    /// <summary>Open the team in the web app.</summary>
    public const string TeamsOpenWeb = "teams.openweb";

    /// <summary>Mit sheet: phases label.</summary>
    public const string TeamsPhasesLabel = "teams.phases";

    /// <summary>Mit sheet: mechanic filter label.</summary>
    public const string TeamsFilterLabel = "teams.filter";

    /// <summary>Tag: raidwide.</summary>
    public const string TeamsTagRaidwide = "teams.tag.raidwide";

    /// <summary>Tag: tankbuster.</summary>
    public const string TeamsTagTankbuster = "teams.tag.tankbuster";

    /// <summary>Tag: other.</summary>
    public const string TeamsTagOther = "teams.tag.other";

    /// <summary>Timeline column: time.</summary>
    public const string TeamsColTime = "teams.col.time";

    /// <summary>Timeline column: mechanic.</summary>
    public const string TeamsColMechanic = "teams.col.mechanic";

    /// <summary>Cooldown tooltip: recast.</summary>
    public const string TeamsRecast = "teams.recast";

    /// <summary>Cooldown tooltip: duration.</summary>
    public const string TeamsDuration = "teams.duration";

    /// <summary>Resource type: link.</summary>
    public const string TeamsResLink = "teams.res.link";

    /// <summary>Resource type: video.</summary>
    public const string TeamsResVideo = "teams.res.video";

    /// <summary>Resource type: plan.</summary>
    public const string TeamsResPlan = "teams.res.plan";

    /// <summary>Resource type: note.</summary>
    public const string TeamsResNote = "teams.res.note";

    /// <summary>Resource type: image.</summary>
    public const string TeamsResImage = "teams.res.image";

    /// <summary>Resource type: pdf.</summary>
    public const string TeamsResPdf = "teams.res.pdf";

    /// <summary>Resource type: file.</summary>
    public const string TeamsResFile = "teams.res.file";

    /// <summary>FFLogs: open this report.</summary>
    public const string TeamsFflogsReport = "teams.fflogs.report";

    /// <summary>Config tab: teams.</summary>
    public const string TabTeams = "config.tab.teams";

    /// <summary>Display mode: icon + text.</summary>
    public const string TeamsDispIconText = "teams.disp.icontext";

    /// <summary>Display mode: icon only.</summary>
    public const string TeamsDispIcon = "teams.disp.icon";

    /// <summary>Display mode: text only.</summary>
    public const string TeamsDispText = "teams.disp.text";

    /// <summary>Config: mit cooldown display.</summary>
    public const string TeamsMitDisplayLabel = "teams.mitdisplay";

    /// <summary>Config: mit cooldown display hint.</summary>
    public const string TeamsMitDisplayHint = "teams.mitdisplay.hint";

    /// <summary>Config: resource label display.</summary>
    public const string TeamsResourceDisplayLabel = "teams.resdisplay";

    /// <summary>Config: resource label display hint.</summary>
    public const string TeamsResourceDisplayHint = "teams.resdisplay.hint";

    /// <summary>Config: show note bodies.</summary>
    public const string TeamsShowNotes = "teams.shownotes";

    /// <summary>Config: show note bodies hint.</summary>
    public const string TeamsShowNotesHint = "teams.shownotes.hint";

    /// <summary>Action tooltip: range.</summary>
    public const string TeamsRange = "teams.range";

    /// <summary>Action tooltip: radius.</summary>
    public const string TeamsRadius = "teams.radius";

    /// <summary>Action tooltip: cast.</summary>
    public const string TeamsCast = "teams.cast";

    /// <summary>Action tooltip: instant.</summary>
    public const string TeamsInstant = "teams.instant";

    /// <summary>Calendar legend: everyone present.</summary>
    public const string TeamsCalAllPresent = "teams.cal.allpresent";

    /// <summary>Calendar legend: unclear.</summary>
    public const string TeamsCalUnclear = "teams.cal.unclear";

    /// <summary>Calendar legend: someone missing.</summary>
    public const string TeamsCalMissing = "teams.cal.missing";

    /// <summary>Event kind: recurring.</summary>
    public const string TeamsRecurring = "teams.recurring";

    /// <summary>Event kind: single.</summary>
    public const string TeamsSingle = "teams.single";

    /// <summary>Tab: events (per-team list).</summary>
    public const string TeamsTabEvents = "teams.tab.events";

    /// <summary>Termine: show past occurrences ({0} = count).</summary>
    public const string TeamsShowPast = "teams.showpast";

    /// <summary>Config: default job view.</summary>
    public const string TeamsDefaultJobLabel = "teams.defaultjob";

    /// <summary>Config: default = current job.</summary>
    public const string TeamsDefaultCurrentJob = "teams.defaultjob.current";

    /// <summary>Config: default = all jobs.</summary>
    public const string TeamsDefaultAllJobsOpt = "teams.defaultjob.all";

    /// <summary>Config: default show the "other" tag.</summary>
    public const string TeamsDefaultShowOther = "teams.defaultshowother";

    /// <summary>Config: default show-other hint.</summary>
    public const string TeamsDefaultShowOtherHint = "teams.defaultshowother.hint";

    /// <summary>Absence: new-entry heading.</summary>
    public const string TeamsAbsenceNew = "teams.absence.new";

    /// <summary>Absence: editing heading.</summary>
    public const string TeamsAbsenceEditing = "teams.absence.editing";

    /// <summary>Absence: update button.</summary>
    public const string TeamsAbsenceUpdate = "teams.absence.update";

    /// <summary>Absence: cancel edit.</summary>
    public const string TeamsAbsenceCancel = "teams.absence.cancel";

    /// <summary>Absence: current-list heading.</summary>
    public const string TeamsAbsenceCurrent = "teams.absence.current";

    /// <summary>Absence: edit button.</summary>
    public const string TeamsAbsenceEdit = "teams.absence.edit";

    /// <summary>Absence: end-before-start error.</summary>
    public const string TeamsAbsenceRangeInvalid = "teams.absence.rangeinvalid";

    /// <summary>Config: default phase selection.</summary>
    public const string TeamsDefaultPhasesLabel = "teams.defaultphases";

    /// <summary>Config: default = all phases.</summary>
    public const string TeamsDefaultPhasesAll = "teams.defaultphases.all";

    /// <summary>Config: default = first phase only.</summary>
    public const string TeamsDefaultPhasesFirst = "teams.defaultphases.first";

    /// <summary>Config: default phase hint.</summary>
    public const string TeamsDefaultPhasesHint = "teams.defaultphases.hint";

    /// <summary>Config: Termine text size.</summary>
    public const string TeamsEventScale = "teams.eventscale";

    /// <summary>Config: Termine text size hint.</summary>
    public const string TeamsEventScaleHint = "teams.eventscale.hint";

    /// <summary>Farm column: slot.</summary>
    public const string TeamsFarmColSlot = "teams.farm.col.slot";

    /// <summary>Farm column: source.</summary>
    public const string TeamsFarmColSource = "teams.farm.col.source";

    /// <summary>Farm column: how to get it (primary route).</summary>
    public const string TeamsFarmColHow = "teams.farm.col.how";

    /// <summary>Farm: unknown source placeholder.</summary>
    public const string TeamsFarmUnknownSource = "teams.farm.unknownsource";

    /// <summary>Farm route: no longer obtainable (retired).</summary>
    public const string TeamsFarmRetired = "teams.farm.retired";

    /// <summary>Farm route: market board.</summary>
    public const string TeamsFarmMarket = "teams.farm.market";

    /// <summary>Farm route: craft.</summary>
    public const string TeamsFarmCraft = "teams.farm.craft";

    /// <summary>Farm: coffer label (the chest a drop arrives as).</summary>
    public const string TeamsFarmCoffer = "teams.farm.coffer";

    /// <summary>Farm tooltip: heading for the full list of routes.</summary>
    public const string TeamsFarmWaysHeading = "teams.farm.ways";

    /// <summary>Farm tooltip: the piece you hand in (a slot, not a purchase).</summary>
    public const string TeamsFarmHandIn = "teams.farm.handin";

    /// <summary>Context menu: pin the vendor NPC on the in-game map.</summary>
    public const string TeamsFarmShowOnMap = "teams.farm.showonmap";

    /// <summary>Sourcing step: fight/earn it (a drop).</summary>
    public const string SourceStepFight = "source.step.fight";

    /// <summary>Sourcing step: buy it.</summary>
    public const string SourceStepBuy = "source.step.buy";

    /// <summary>Sourcing step: upgrade/augment it.</summary>
    public const string SourceStepAugment = "source.step.augment";

    /// <summary>Sourcing step: craft it.</summary>
    public const string SourceStepCraft = "source.step.craft";

    /// <summary>Sourcing step: get the base piece (fallback when the chain is unknown).</summary>
    public const string SourceStepBase = "source.step.base";

    /// <summary>Sourcing: no info available.</summary>
    public const string SourceNoInfo = "source.noinfo";

    /// <summary>Sourcing: you own enough of this cost.</summary>
    public const string SourceHave = "source.have";

    /// <summary>Sourcing: the base piece is already owned (equipped), so only the upgrade remains.</summary>
    public const string SourceBaseOwned = "source.baseowned";

    /// <summary>Sourcing: separates alternative acquisition ways ("or").</summary>
    public const string SourceOr = "source.or";

    /// <summary>Menu/command: open the what's-new window.</summary>
    public const string WhatsNewOpen = "whatsnew.open";

    /// <summary>What's new: window intro line.</summary>
    public const string WhatsNewIntro = "whatsnew.intro";

    /// <summary>What's new: badge for the installed version.</summary>
    public const string WhatsNewInstalled = "whatsnew.installed";

    /// <summary>What's new: "new" badge.</summary>
    public const string WhatsNewKindAdded = "whatsnew.kind.added";

    /// <summary>What's new: "improved" badge.</summary>
    public const string WhatsNewKindImproved = "whatsnew.kind.improved";

    /// <summary>What's new: "fixed" badge.</summary>
    public const string WhatsNewKindFixed = "whatsnew.kind.fixed";

    /// <summary>What's new: link to the full changelog.</summary>
    public const string WhatsNewFullChangelog = "whatsnew.fullchangelog";

    /// <summary>Config: auto-open the notes after an update.</summary>
    public const string WhatsNewOnUpdate = "whatsnew.onupdate";

    /// <summary>Config: auto-open hint.</summary>
    public const string WhatsNewOnUpdateHint = "whatsnew.onupdate.hint";

    // Purchase advisor ("Kaufberater"): the intermediate gear on the way to BiS.

    /// <summary>Menu/command: open the purchase-advisor window.</summary>
    public const string AdvisorOpen = "advisor.open";

    /// <summary>Advisor: window title.</summary>
    public const string AdvisorWindowTitle = "advisor.title";

    /// <summary>Advisor: what this window is for.</summary>
    public const string AdvisorIntro = "advisor.intro";

    /// <summary>Advisor: the gearset picker label.</summary>
    public const string AdvisorSetLabel = "advisor.setlabel";

    /// <summary>Advisor: heading of the saved-plan section.</summary>
    public const string AdvisorPlanHeading = "advisor.plan.heading";

    /// <summary>Advisor: this set carries no web identity, so no plan can be addressed.</summary>
    public const string AdvisorNoTarget = "advisor.plan.notarget";

    /// <summary>Advisor: no plan is saved for this set (a normal state).</summary>
    public const string AdvisorNoPlan = "advisor.plan.noplan";

    /// <summary>Advisor: the plugin has not learned this character's server id yet.</summary>
    public const string AdvisorNoCharacter = "advisor.plan.nocharacter";

    /// <summary>Advisor: when the shown plan was last saved.</summary>
    public const string AdvisorPlanSaved = "advisor.plan.saved";

    /// <summary>Advisor: the planned piece is already worn.</summary>
    public const string AdvisorPlanWorn = "advisor.plan.worn";

    /// <summary>Advisor: the planned piece is owned but not worn.</summary>
    public const string AdvisorPlanOwned = "advisor.plan.owned";

    /// <summary>Advisor: the planned piece is still missing.</summary>
    public const string AdvisorPlanMissing = "advisor.plan.missing";

    /// <summary>Advisor: progress summary of the plan.</summary>
    public const string AdvisorPlanSummary = "advisor.plan.summary";

    /// <summary>Advisor: view toggle — the server's recommendation.</summary>
    public const string AdvisorViewRecommendation = "advisor.view.recommendation";

    /// <summary>Advisor: view toggle — the player's own layout.</summary>
    public const string AdvisorViewPlan = "advisor.view.plan";

    /// <summary>Advisor: ranking label.</summary>
    public const string AdvisorSortLabel = "advisor.sort.label";

    /// <summary>Advisor ranking: biggest gain first.</summary>
    public const string AdvisorSortPower = "advisor.sort.power";

    /// <summary>Advisor ranking: best gain per tomestone.</summary>
    public const string AdvisorSortValue = "advisor.sort.value";

    /// <summary>Advisor ranking: cheapest first.</summary>
    public const string AdvisorSortCheap = "advisor.sort.cheap";

    /// <summary>Advisor: recompute the advice.</summary>
    public const string AdvisorRefresh = "advisor.refresh";

    /// <summary>Advisor: the tomestone balance the schedule was computed against.</summary>
    public const string AdvisorBalance = "advisor.balance";

    /// <summary>Advisor step: buy it with tomestones.</summary>
    public const string AdvisorStepBuy = "advisor.step.buy";

    /// <summary>Advisor step: augment the base you wear.</summary>
    public const string AdvisorStepAugment = "advisor.step.augment";

    /// <summary>Advisor step: farm the Extreme-Trial piece.</summary>
    public const string AdvisorStepTrial = "advisor.step.trial";

    /// <summary>Advisor step: trade the raid books.</summary>
    public const string AdvisorStepBooks = "advisor.step.books";

    /// <summary>Advisor step: you own it already, just put it on.</summary>
    public const string AdvisorStepEquip = "advisor.step.equip";

    /// <summary>Advisor schedule: doable right now.</summary>
    public const string AdvisorWhenNow = "advisor.when.now";

    /// <summary>Advisor schedule: capped weeks still to save.</summary>
    public const string AdvisorWhenWeeks = "advisor.when.weeks";

    /// <summary>Advisor schedule: books still to collect.</summary>
    public const string AdvisorWhenBooks = "advisor.when.books";

    /// <summary>Advisor: the tomestone price of a step.</summary>
    public const string AdvisorCostTomes = "advisor.cost.tomes";

    /// <summary>Advisor: the vendor a purchase is made at.</summary>
    public const string AdvisorVendor = "advisor.vendor";

    /// <summary>Advisor: the piece this step eventually leads to.</summary>
    public const string AdvisorLeadsTo = "advisor.leadsto";

    /// <summary>Advisor: nothing deterministic left to do.</summary>
    public const string AdvisorNothingToDo = "advisor.nothingtodo";

    /// <summary>Advisor: heading of the remaining material/book needs.</summary>
    public const string AdvisorNeeds = "advisor.needs";

    /// <summary>Advisor: save the edited layout.</summary>
    public const string AdvisorSave = "advisor.save";

    /// <summary>Advisor: fill the layout from the recommendation.</summary>
    public const string AdvisorAdopt = "advisor.adopt";

    /// <summary>Advisor: drop the saved layout and fall back to the recommendation.</summary>
    public const string AdvisorDeletePlan = "advisor.delete";

    /// <summary>Advisor: the save went through.</summary>
    public const string AdvisorSaved = "advisor.saved";

    /// <summary>Advisor: the save did not go through.</summary>
    public const string AdvisorSaveFailed = "advisor.savefailed";

    /// <summary>Advisor: this pick matches the recommendation.</summary>
    public const string AdvisorIsRecommended = "advisor.isrecommended";

    /// <summary>Advisor: this pick is the BiS piece itself.</summary>
    public const string AdvisorIsBis = "advisor.isbis";

    /// <summary>Advisor: computing the advice.</summary>
    public const string AdvisorLoadingOptions = "advisor.loadingoptions";

    /// <summary>Advisor: the layout has unsaved edits.</summary>
    public const string AdvisorUnsaved = "advisor.unsaved";

    /// <summary>BiS: accordion heading — what this set still needs in total.</summary>
    public const string BisNeedsHeading = "bis.needs.heading";

    /// <summary>BiS: the tomestones the remaining purchases add up to, against the balance.</summary>
    public const string BisNeedsTomes = "bis.needs.tomes";

    /// <summary>BiS: how many tomestones are still short.</summary>
    public const string BisNeedsShort = "bis.needs.short";

    /// <summary>BiS: roughly how many capped weeks that is.</summary>
    public const string BisNeedsWeeks = "bis.needs.weeks";

    /// <summary>BiS: the balance already covers the remaining purchases.</summary>
    public const string BisNeedsCovered = "bis.needs.covered";

    /// <summary>BiS: nothing left to buy or collect for this set.</summary>
    public const string BisNeedsNothing = "bis.needs.nothing";

    /// <summary>Config: purchase-advisor text size.</summary>
    public const string AdvisorScale = "advisor.scale";

    /// <summary>Config: purchase-advisor text size hint.</summary>
    public const string AdvisorScaleHint = "advisor.scale.hint";

    /// <summary>Advisor: headline card — the single best next move.</summary>
    public const string AdvisorNextBest = "advisor.nextbest";

    /// <summary>Advisor: how much of the remaining way to BiS a step closes.</summary>
    public const string AdvisorProgress = "advisor.progress";

    /// <summary>Advisor: how many slots already sit on BiS.</summary>
    public const string AdvisorOnBis = "advisor.onbis";

    /// <summary>Advisor: average item level, current → target.</summary>
    public const string AdvisorAvgIlvl = "advisor.avgilvl";

    /// <summary>Advisor: heading of the per-slot grid.</summary>
    public const string AdvisorYourSet = "advisor.yourset";

    /// <summary>Advisor: heading of the ranked step list.</summary>
    public const string AdvisorOrderHeading = "advisor.order";

    /// <summary>Advisor: what the tile colours mean.</summary>
    public const string AdvisorLegend = "advisor.legend";

    /// <summary>Advisor: this slot is already best possible without luck.</summary>
    public const string AdvisorSlotCapped = "advisor.slot.capped";

    /// <summary>Holdings: heading of the "where it sits" breakdown.</summary>
    public const string HoldingWhere = "holding.where";

    /// <summary>Storage: inventory bags.</summary>
    public const string HoldingBags = "holding.bags";

    /// <summary>Storage: chocobo saddlebag.</summary>
    public const string HoldingSaddlebag = "holding.saddlebag";

    /// <summary>Storage: armoury chest.</summary>
    public const string HoldingArmoury = "holding.armoury";

    /// <summary>Storage: currently worn.</summary>
    public const string HoldingEquipped = "holding.equipped";

    /// <summary>Storage: glamour dresser.</summary>
    public const string HoldingGlamour = "holding.glamour";

    /// <summary>Storage: armoire / cabinet.</summary>
    public const string HoldingArmoire = "holding.armoire";

    /// <summary>Storage: a retainer (unnamed).</summary>
    public const string HoldingRetainer = "holding.retainer";

    /// <summary>Storage: hand-marked on the website.</summary>
    public const string HoldingManual = "holding.manual";

    /// <summary>Advisor: heading of the stock section.</summary>
    public const string AdvisorStockHeading = "advisor.stock.heading";

    /// <summary>Advisor: where the stock numbers come from.</summary>
    public const string AdvisorStockHint = "advisor.stock.hint";

    /// <summary>Advisor: the server has not sent a stock list (yet).</summary>
    public const string AdvisorStockEmpty = "advisor.stock.empty";

    /// <summary>Advisor stock group: upgrade materials.</summary>
    public const string AdvisorStockMaterial = "advisor.stock.material";

    /// <summary>Advisor stock group: the universal upgrade stone.</summary>
    public const string AdvisorStockStone = "advisor.stock.stone";

    /// <summary>Advisor stock group: raid books / tokens.</summary>
    public const string AdvisorStockBook = "advisor.stock.book";

    /// <summary>Advisor: reading the plan.</summary>
    public const string AdvisorLoading = "advisor.loading";

    /// <summary>Advisor: the key lacks the plans:read scope.</summary>
    public const string AdvisorScopeHint = "advisor.scopehint";
}
