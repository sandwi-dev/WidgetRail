# Troubleshooting

## The overlay does not open

Use **View + Menu** together on one controller, or try **F1** with a keyboard.
The opening shortcut can be changed to Guide in Settings. Release both buttons
after reconnecting a controller before trying the shortcut again.

If WidgetRail is not running, open WidgetRail from the Start menu. For a source build, follow
[Building from source](../maintainers/building.md).

## The overlay cannot take foreground focus

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
permissions. Built-in widgets are not automatically granted permissions.
Audio, networking, window management and power operations require their
respective permissions.

A provider can still reject an operation after permission is granted: for
example, the device may have disconnected or Windows may prevent a window switch.
Use the message shown by the widget to decide whether to retry.

## Controller input behaves strangely in a game

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
after file selection. **Cancel** is selected by default. Approving installs the
package disabled; enabling it remains a separate action in Settings. A cancelled
or expired review does not install the package. If an older build only reports
`full_trust_approval_required`, update WidgetRail to a build with this review step.

## No artwork, media or window preview

Network artwork can be unavailable while offline or while a service is
unreachable. A protected, elevated or minimized window may not provide a
current preview. Web media may require the WebView2 runtime and provider setup.

## Report a reproducible problem

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

Check both the WidgetRail setting and Windows Settings → Apps → Startup. Windows
can disable a startup entry independently; WidgetRail respects that choice.
