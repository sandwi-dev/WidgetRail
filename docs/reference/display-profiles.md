# Display Profiles

Save a Windows display setup once, then restore it from the overlay.
The widget is included in both editions.

## Save and use a profile

Arrange your displays in Windows Settings first. Open **Display Profiles** and
choose **Save current setup**, or press **Y**. Select the name field to open
WidgetRail's keyboard. Committing the name saves the profile.

Each profile shows its monitors, resolution, refresh rate and whether it matches
your current setup. Press **A** on a profile to apply it. Choose **Keep changes**
within 15 seconds, or the previous setup is restored. **B** reverts immediately.
The countdown continues if you hide the overlay or leave the widget.

Press **Menu** on a profile to rename it, replace it with the current setup, or
delete it. Replace and Delete ask for confirmation. Disconnected profiles remain
focusable so you can still manage them.

## What is remembered

Profiles preserve active monitors, extended or duplicated topology, resolution,
refresh rate, orientation, desktop positions and the primary display.
Windows UI scaling, HDR, brightness and color profiles are left unchanged.

Monitors are matched by their Windows device identity rather than their display
number. A missing or ambiguous monitor makes a profile unavailable. Moving a
monitor to a different connector can change that identity; save the profile again
if Windows no longer recognizes the connection.

Windows validates the saved modes before applying them. A monitor, driver or
connection that no longer supports a saved mode can refuse it.

## SDK access

Declare `system.displays.read.v1` to use `HostServices.DisplayProfiles.GetAsync`.
The state includes current monitors, saved profile summaries and any pending
confirmation. Open a subscription to `WidgetDisplayProfilesCapabilities.Changed`
before the initial fetch to receive display and profile changes.

The optional `system.displays.control.v1` permission authorizes
`SaveAsync`, `RenameAsync`, `ReplaceAsync`, `DeleteAsync`, `ApplyAsync`,
`KeepAsync` and `RevertAsync`. These requests must begin while Interactive.
An admitted restore or confirmation may finish after a lifecycle transition.
Raw Windows display structures stay inside the trusted provider.

Only the widget identity that started a restore can confirm or revert its
transaction. The helper process applies the temporary setup and owns rollback;
it reverts on timeout or loss of the bridge connection. The launcher requests
documented process-job breakaway and verifies the exact child on a private pipe.
The helper is outside WidgetRail's worker containment. No display change is sent
until it acknowledges readiness. An external launcher's outer process job can
still supervise the whole application.
A per-user guard lock prevents overlapping restores across bridge restarts.
Changes are saved to the
Windows display database only after Keep is confirmed.

If a monitor disappears during confirmation, rollback can fall back to Windows'
last saved available topology.

## Storage and failures

Up to 24 named profiles are stored in
`%LOCALAPPDATA%\WidgetRail\display-profiles\profiles.json` for a normal installation.
Profiles are shared by authorized widgets. Writes replace this file atomically;
a failed write or malformed file does not discard the existing profiles.
The installer's optional data removal also removes this directory.

Display changes use Windows notifications, not periodic display polling.
The widget's one-second timer updates only the visible confirmation countdown.
Normal automated tests use a fake display backend for every apply and rollback.
The optional test-runner argument `--native-read` reads and validates the
current configuration. It is separate from the normal suite so CI does not
depend on a physical desktop.
After building the complete host and test project, maintainers can run
[`Test-DisplayRestoreGuard.ps1`](../../scripts/Test-DisplayRestoreGuard.ps1)
to verify the packaged helper's pipe and parent-exit behavior without applying
a display configuration.
