# Production WinUI pin coordination

`OverlayShellPage.Pinning` owns one pinned selection and its peer native window.
The tray exposes authored pinned layouts and eligible full-widget layouts. The
presenter receives the genuine session selection/projection; the shell never
manufactures a main frame to represent the pin.

## Lifetime and input

Main-widget selection, pin selection and teardown share the shell transition
semaphore. Widget lifecycle is the union of visible main/pinned ownership: hiding
the main overlay suspends its native presenter, while a visible pin keeps the
worker Visible. Pin interaction requires explicit entry, the current selection,
the same widget incarnation, a visible overlay and native foreground ownership
of the pin. Passive UI Automation action attempts fail the admission callback.

The existing controller stream routes to the selected input surface. View enters
or leaves the pin; an unhandled B leaves it; LB+RB+X unpins. The pin does not start
another controller reader. Main-window deactivation is not pin deactivation:
transferring focus between the two HWNDs must not cancel entry. The pin's own
activation event relinquishes interaction when its native focus is lost.

Closing/hiding the main overlay ends pin interaction and leaves the pin passive.
Unpinning retires the presenter and native window, then revokes the selected
worker demand. Shutdown preserves the chosen layout and placement for the next
launch, but never saves an interactive state. Catalog removal, incarnation
replacement and failed pin publications retire the corresponding surface.

## Persistence and placement

`winui-pinned-state.json` belongs to the explicit settings profile. It records the
chosen widget/layout and bounded logical dimensions, monitor identity, relative
work-area position and opacity. Existing placement policy clamps restored bounds
to the available monitor. The pin uses the shell palette, authored surface
appearance, high-contrast policy and interface zoom.

## Validation and remaining work

`Test-WinUiProductionPin.ps1` drives the actual shell menu and peer window using
an isolated bundled Now Playing workspace. It covers creation, native focus
transfer, returning to main, unpin and main-hide retention, without invoking media
playback. Presenter-specific control/action coverage is separate in
`Test-WinUiPinnedWidget.ps1`; click-through/opacity checks are in
`Test-WinUiPinnedWindow.ps1`.

This coordinator checkpoint is not complete pin feature parity. Compact media
transfer, placement/resize/opacity controls, live monitor-removal recovery and
per-pin display-specific appearance resolution remain. Full-widget pinning is
not offered for an embedded-media session until its transfer ownership exists.
Physical Guide/controller/mixed-monitor acceptance remains separate from UIA.

Recorded checkpoint: analyzer build clean, 40 shell tests, five production native
checks plus a fresh-process passive-restore check. The native peer window
regression passes all 14 checks after the scale/appearance/accessibility wrapper
changes. Screenshots were inspected. Evidence:
`artifacts/winui-pinned-production/native-03` and `window-regression`.
