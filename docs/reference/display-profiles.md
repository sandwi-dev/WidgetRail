# Display Controls

Save a Windows display setup once, then restore it from the overlay.
Install **Display Controls** from Catalogue. Its package identity remains `widgetrail.samples.display-profiles`; existing saved profiles are retained.

## Adjust a display

Choose **Adjust displays**, then select the monitor. Resolution and refresh rate use the modes reported by the Windows graphics driver. Refresh rates keep their rational precision internally: 59.94 Hz and 60 Hz are distinct modes. Select both before choosing **Apply resolution and refresh**. The same independent timed Keep/Revert guard used by profiles verifies the actual resulting mode; Windows silently substituting another mode is treated as a failed preview.

Supported monitors also offer **Turn HDR on/off**. Status distinguishes HDR enabled by the user from HDR currently active. This uses the Windows advanced-color API that explicitly distinguishes HDR from wide color gamut. Windows versions/drivers that do not expose that API show HDR control as unavailable rather than treating generic advanced-color support as HDR.

Adjacent horizontal or vertical monitor strips keep their order and primary origin when resolution changes; neighboring positions move only enough to retain touching edges. Other arrangements keep their positions when safe. A resize that would overlap or disconnect an irregular arrangement is rejected before changing the display, with Windows Settings offered as the place to adjust it.

Direct mode selection is unavailable for cloned displays, where several monitors share one source. Use the existing saved-profile workflow to switch topology, orientation or primary display. Mode and HDR actions do not overwrite a saved profile. The mode list is read on entering controls and after Windows display-change notifications; there is no continuous discovery polling.

Hardware backlight brightness, a software dimming overlay and GPU color adjustment are different features. This widget does not substitute one for another. WMI backlight control is limited mainly to integrated panels; external-monitor DDC/CI support and native-call latency vary. Software dimming adds another window with HDR/capture/input implications. The reference's AMD ADLX colors are vendor-specific. These controls are not exposed until a provider with suitable hardware coverage and failure bounds is available. Windows scaling and color-profile management remain in Windows Settings.

The display-control workflow was informed by [Quick Settings](https://github.com/LoZazaMastro/Quick-Settings) (MIT). No source code or bundled dependencies were copied; native access uses documented Windows APIs.

## Save and use a profile

Arrange your displays in Windows Settings first. Open **Display Controls** and
choose **Save current setup**, or press **Y**. Select the name field to open
WidgetRail's keyboard. Committing the name saves the profile.
If access is missing, the selected action opens WidgetRail's permission dialog and continues only after approval. Read access can be requested with Retry. Save, rename, replace, delete and apply request display control; timed Keep/Revert controls never insert another permission prompt.

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
`KeepAsync`, `RevertAsync` and `ApplyControlAsync`. `GetControlsAsync` uses the read permission and returns opaque display/mode IDs, exact rational refresh rates and HDR capabilities. `ApplyControlAsync` accepts exactly one selected mode or HDR state and returns the ordinary pending confirmation state. These requests must begin while Interactive.
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
Mode changes are saved to the
Windows display database only after Keep is confirmed. Windows applies HDR preferences immediately; the independent guard records the baseline first and restores it on Revert, timeout or parent loss. The API does not offer a separate nonpersistent HDR preview.

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
The optional test-runner argument `--controls-read` enumerates modes and HDR capabilities without writes; `--native-read` reads and validates the
current configuration. It is separate from the normal suite so CI does not
depend on a physical desktop.
After building the complete host and test project, maintainers can run
[`Test-DisplayRestoreGuard.ps1`](../../scripts/Test-DisplayRestoreGuard.ps1)
to verify the packaged helper's pipe and parent-exit behavior without applying
a display configuration.
