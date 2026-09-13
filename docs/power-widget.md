# Power widget

The bundled Power widget controls the Windows PC, independently of Settings'
Quit and Restart WidgetRail buttons.

- Sleep acts immediately and keeps applications open.
- Shut down and Restart show a confirmation with Cancel initially focused.
  B cancels; closing the overlay discards the confirmation.
- Unavailable options are disabled. Check again refreshes availability without polling.
- Errors use themed, timed toasts. Failed commands are never retried automatically.

Like other built-in widgets, its permissions require explicit consent in Settings.
`system.power.read.v1` exposes three availability booleans.
`system.power.control.v1` permits the three explicit operations while Interactive;
it does not allow dashboard gestures or background control. Neither permission
grants arbitrary executable or command-line access.

Authors can use `HostServices.Power.GetAvailabilityAsync`, `ShutDownAsync`,
`RestartAsync`, and `SleepAsync`. They should confirm shutdown/restart before
invoking. Consent authorizes the widget to perform power operations; the broker
does not claim to verify an arbitrary author's confirmation UI.

The Windows provider uses `ExitWindowsEx(EWX_POWEROFF/EWX_REBOOT)` and
`SetSuspendState(FALSE, FALSE, FALSE)`. No force or force-if-hung flags are used.
An application may block shutdown or ask the user to save work. An acknowledgement
means Windows accepted the request, not that the PC has finished shutting down.
Sleep availability uses `IsPwrSuspendAllowed`, and Windows policy can still reject
an operation after an availability check.

Shutdown privilege is enabled on a duplicated token used only during the native
call. Scoped impersonation restores the prior thread identity on exit; the host's
process token is not changed. Commands are serialized without queuing duplicates,
and cancellation is checked immediately before the side effect. Cancellation
cannot undo a power operation Windows has already accepted.

Tests use an injected native adapter for all effects. The only real Windows probe
checks availability and temporary-token setup; automated tests never shut down,
restart or suspend the computer.
