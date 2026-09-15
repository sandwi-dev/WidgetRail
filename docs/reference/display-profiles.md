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

Profiles save a physical identity derived from the monitor's manufacturer and
serial number when available. Exact connections are preferred; a unique serial
match also recognizes the monitor after VRR changes its reported product ID or
a connector changes. Missing or ambiguous matches make a profile unavailable.
Monitors without usable serials rely on their connection identity, so changes to
that identity require saving the setup again. Per-display overlay sizing uses
the same identity resolver.

Windows validates the saved modes before applying them. A monitor, driver or
connection that no longer supports a saved mode can refuse it.
Monitor VRR controls can change the reported refresh rate and signal timings.
Keep VRR consistent when saving and restoring a profile. Summaries show refresh
rates to three decimal places when needed (for example, 240 Hz and 239.997 Hz);
the matching check still compares the exact reported rate.

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

The first temporary apply also wakes sleeping monitors. Before offering Keep,
the guard requires the requested setup to remain stable for three seconds. If
Windows settles on a different setup while the monitors wake, the guard resolves
the display paths again and retries once. This stabilization phase has a
12-second budget, checked between Windows calls; an in-progress native call
cannot be interrupted. Failure or loss of the bridge connection restores the
previous setup. The full 15-second confirmation countdown starts after the
preview stabilizes. Keep reads the active setup again and refuses to save it if
it has changed or if the confirmation expired during that read.
The widget runs restore actions through the SDK's background-operation facility
so the wake-up wait does not block controller requests.
A themed loading indicator and status text remain visible during that wait.
Transient enumeration failures do not end the widget's display-change
subscription. At countdown expiry it also refreshes the transaction status;
an already-ended confirmation clears the stale dialog and fetches current state.

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
During a restore transaction, a separate diagnostic worker also reads the active
setup every 500 ms and logs changes to `display-profiles/restore.log` under the
settings directory. It records UTC timestamps, native call flags/results,
baseline and target modes, confirmation decisions and rollback outcomes.
Readbacks include their own start time and duration: a read triggered by Keep
can finish after the subsequent apply, so its trigger is not proof of ordering.
Monitor connections are hashed; raw device paths, serials and profile names are
excluded. The log retains up to 1 MiB plus one previous file, `restore.log.1`.
Reads stop when the transaction ends. Diagnostic failures do not alter display
decisions or block rollback; a stalled worker gets only a bounded final flush.
These observations cannot identify which external process or driver changed
the topology, and changes shorter than the sampling interval may be missed.
Normal automated tests use a fake display backend for every apply and rollback.
The optional test-runner argument `--native-read` reads and validates the
current configuration. It is separate from the normal suite so CI does not
depend on a physical desktop.
After building the complete host and test project, maintainers can run
[`Test-DisplayRestoreGuard.ps1`](../../scripts/Test-DisplayRestoreGuard.ps1)
to verify the packaged helper's pipe and parent-exit behavior without applying
a display configuration.
