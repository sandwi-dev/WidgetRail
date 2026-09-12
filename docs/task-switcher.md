# Task Switcher

The bundled Task Switcher shows up to 64 switchable application windows, with
an application name, window title and minimized state. Windows of the same app
remain separate. It reuses the running-window eligibility and hosted packaged
app resolution used by Games & Apps.

A selects an existing window; X or the Close button posts a normal close request.
Y refreshes, and unhandled root B returns to the tray. Close does not terminate a
process or suppress save prompts. The row remains until observation reports
that the window has gone. Failures use an expiring, themed toast.

The initial order follows Windows' front-to-back window enumeration, an
approximation of recently active order. Two-second refreshes run only during
the active widget lifetime and retain the existing order and stable identities,
adding new windows at the end. Reopening or explicit Refresh recalculates order.
There is no browser-tab enumeration, virtual-desktop integration or live preview.

## Host and SDK contract

WidgetHostServices.TaskSwitcher exposes GetWindowsAsync, SwitchAsync and
CloseAsync. Dedicated capabilities are system.apps.windows.read.v1,
system.apps.windows.switch.v1 and system.apps.windows.close.v1. Settings labels
describe window titles, switching, and normal close. First-party status never
auto-grants consent. Mutation operations require Interactive lifecycle; an
already admitted switch may finish across foreground-driven deactivation.

The SDK receives bounded text, minimized state and broker-session-scoped opaque
WindowIds. Raw HWNDs, process IDs, process-lifetime evidence and executable
identity remain in WindowsAppLibraryProvider. The broker translates only IDs
from its latest list. The provider serializes observation/control, re-enumerates
eligibility and checks window handle, owning process, window class, process
lifetime evidence and application identity before dispatch. Native control
rechecks immediately before the OS call. Removed or replaced targets fail safely.

The foreground overlay delegates foreground activation to its live trusted
bridge before a user action is dispatched. Provider switching restores minimized
windows, issues one SetForegroundWindow request and observes confirmation for
at most 250 ms. It uses no simulated input or foreground-stealing retry.
The existing native foreground-change path remembers the selected application
and closes the overlay. No extra host close effect or HideOverlay change is used.

Close posts WM_CLOSE and acknowledges only that the request was queued. Windows
may deny cross-integrity requests, and applications may prompt or refuse to
close. No force-close or process-kill path exists.

## Verification

- TaskSwitcherWidget.Tests: 6 tests, covering root B, per-window routing, normal
  close acknowledgement, retained rows, themed failures, ordering, lifecycle
  refresh and malformed SDK responses.
- WindowsAppLibraryProvider.Tests: 95 tests, including distinct windows sharing
  one process, stable IDs, stale/reused-process rejection and cancellation.
- PlatformBroker.Tests: 58 tests, including permission/lifecycle gating,
  cross-session token rejection and retirement of removed windows.
- SettingsWidget.Tests: 67 tests.
- SDK compatibility: 14 tests; public API changes are additive (34 symbols).
- WidgetBridge.Tests: all 129 covered by the suite plus the two corrected package
  admission checks. New runtime inventory includes TaskSwitcher.
- Complete Release native/managed build and sealed-package CLI validation passed.
- Read-only live provider observation returned six distinct windows, including
  DTS Sound Unbound with its own title. Automated checks did not close or switch
  the user's applications.

Physical acceptance is pending: enable this widget's permissions, then test
switching, minimized-window restoration, normal close/save prompts and root B.
