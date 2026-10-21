# Troubleshooting

## The overlay does not open

Use **View + Menu** together on one controller, or try **F1** with a keyboard if
you enabled **Settings → Controllers → F1 keyboard shortcut** (off by default).
The opening shortcut can be changed to Guide in Settings. Release both buttons
after reconnecting a controller before trying the shortcut again.

If WidgetRail is not running, open WidgetRail from the Start menu. For a source build, follow
[Building from source](../maintainers/building.md).

## The overlay is visible but not focused

Windows sometimes keeps another app in the foreground even when WidgetRail is
visible. This can happen with Task Manager, Windows Settings, some games, and
elevated windows. It is not proof that every affected app is elevated.

The guide shows **Focus blocked** if Windows refuses the overlay's activation
attempt and fallback. Input can still reach the other app, and app switching may
fail. Click inside WidgetRail to try to give it focus. If that does not work,
use Alt+Tab to switch to the desktop or a regular app, then reopen the overlay.
The warning clears when WidgetRail gains foreground focus or the overlay closes.

This is separate from Exclusive control and from a widget's Windows permissions.
Some protected or elevated windows may still refuse switching or previews after
the overlay gains focus.

## The overlay opens but a widget cannot control Windows

Open **Settings → Widgets**, select the widget and review its
permissions. Optional widgets, including those preinstalled by Production,
receive no capability grants from installation. Audio, networking, window
management and power operations require their respective permissions. The sealed
built-in widgets have host-managed defaults; explicit user denials are preserved.

A provider can still reject an operation after permission is granted: for
example, the device may have disconnected or Windows may prevent a window switch.
Use the message shown by the widget to decide whether to retry.

## Controller input behaves strangely in a game

If the game also responds while WidgetRail is open, first check for **Focus
blocked** and follow the foreground steps above. Receiving controller input in
WidgetRail does not prove that the game has stopped receiving it. Check Windows
Game Bar and shortcut conflicts in [Getting started](getting-started.md#recommended-windows-settings).

Turn **Exclusive control off** in Settings first. It is optional and is not
needed for ordinary overlay navigation. Close and reopen affected applications
after changing it; a running application may keep its previous controller connection.

Exclusive control can produce duplicate input in Windows Settings and the app
switcher. Elevated games can also prevent foreground activation. See
[Known limitations](known-limitations.md).

## A widget stops responding

Return to the tray and hold **Y** over that widget to reload it.
Check Settings diagnostics for a failure message. Reloading can discard that
widget's current transient page state.

For service widgets, check the widget's setup instructions, authentication
and the provider's availability before repeatedly retrying. Repeated requests
can worsen rate limiting.

## A downloaded widget or theme will not install

Use a package built for the supported host/SDK contracts. A file extension or
successful download does not establish compatibility.

Do not rename files, bypass validation or change a package's manifest to force
it to load. Obtain a compatible package from its author. Review full-trust
applications as you would other Windows software.

Full-access packages such as Playnite Library require an explicit confirmation
after file selection. **Cancel** is selected by default. Approving a fresh install through Settings or Catalogue installs and enables the
widget; features request permissions when needed. Updates keep the previous enabled
state. CLI installation leaves a new widget disabled until you enable it in Settings.
A cancelled or expired review does not install the package. If an older build only reports
`full_trust_approval_required`, update WidgetRail to a build with this review step.

## No artwork, media or window preview

Network artwork can be unavailable while offline or while a service is
unreachable. A protected, elevated or minimized window may not provide a
current preview. Web media may require the WebView2 runtime and provider setup.

## A website field or control is difficult to select

Use the left stick to point directly and A to click. D-pad navigation depends on
the page exposing usable controls; canvas/custom widgets may need free pointing.
For an editable field, A opens the keyboard even if the field was already focused.
LS opens website controls or Browser's toolbar. Use Open externally for a native
dialog or authentication flow that does not work inside WidgetRail.
See [browsing](browsing.md); avoid resetting all website data just to retry a click.

## I did not get a notification

Check the source widget's **Notification preferences**, including its master and
category choices. Website notifications also need website permission and a running
page session. List only intentionally hides banners; banner-only song changes
never appear in the bell. Quiet periods, visibility rules or occupied screen
space can suppress a banner even when delivery is otherwise enabled.
See [Notifications](notifications.md). A stopped widget cannot publish new events.

## An installed integration rejects its token or has no data

Successful installation only verifies/adopts the package. Use the widget's setup
or connection page to verify its service. For Playnite, check the detected data
profile and **Advanced → Test connection**; a working game list does not prove
optional achievements are installed or synced. For Spotify/YouTube/Game Help,
check the account, API restrictions and quota before reconnecting.

Follow the [integration's recovery instructions](integrations.md), preserving
existing settings rather than clearing everything. Never include tokens or keys
in a support report or screenshot.

## An app update was interrupted

Reopen the installed WidgetRail from Start and check **Settings → About &
troubleshooting → App updates** for the recorded result. Failed/cancelled setup
tries to reopen a verified usable installation; if none can be verified, use the
official installer manually. Follow a restart-required message before retrying.
There is no app-profile rollback UI. Do not confuse a widget's **Revert to previous
version** with app recovery. See [App updates](../reference/app-updates.md).

## Report a reproducible problem

Open **Settings → About & troubleshooting → Diagnostics**. The summary separates
current problems from recovered widget failures. Expand **Show service details**
for service status and technical codes. Missing overlay/Guide telemetry is not
itself evidence of an input failure. Failure times describe the latest recorded
failure for each widget in the running session; they are not a permanent history.

Select **Export support report** to save a ZIP under **Documents → WidgetRail →
Support**. Its filename remains visible on the diagnostics page. The report
contains app/Windows versions, service states, widget identifiers, controller
readiness, and failure categories/timestamps. It excludes raw logs, paths,
credentials, chat, captures, browsing history, and recovery tokens. Nothing is
uploaded by WidgetRail. Review the ZIP before sharing it.

Include the exact build or commit, Windows version, controller/device and
connection type, relevant settings, and the shortest reproduction sequence.
Say whether restarting the widget or overlay changes the behavior.

Runtime diagnostics are under `%LOCALAPPDATA%\WidgetRail`. Do not upload the
entire directory: it contains settings, private state and service data.
Review individual logs before sharing them. For deeper diagnosis, see
[Diagnostics and recovery](../maintainers/diagnostics-and-recovery.md).

## GameInput setup does not finish

Setup checks for a compatible runtime before offering installation. If Microsoft's
update becomes stuck, save your work and restart Windows normally, then run setup
again. If Windows blocks the restart, keep the error message for diagnosis.

WidgetRail records the vendor exit code in the setup log. When logging can be
prepared, the MSI log is `%LOCALAPPDATA%\WidgetRail\logs\GameInput-setup.log`.
It is replaced on the next attempt. Do not run another MSI over an active update.

## WidgetRail no longer starts when I sign in

Check Windows Settings → Apps → Startup. Windows can disable a startup entry
independently; WidgetRail respects that choice. If no entry exists, rerun the
WidgetRail installer and select startup at sign-in. The current WinUI Settings
widget does not expose a startup switch.
