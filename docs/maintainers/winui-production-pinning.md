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
WinUI retains logical focus independently in each window's XamlRoot. A weak,
root-scoped presentation policy suppresses focused/pressed computed states and
native focus visuals whenever the pin lacks interaction or native foreground.
The shared style adapter applies it to ordinary controls, nested presenters and
realized collection items. Logical focus stays remembered; no native/XAML focus
operation is performed on exit, and content remains enabled and mounted.
Explicit interactive entry restores focus presentation on the retained target.
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

Restoration uses the new runtime's current catalog and a freshly established
frame/selection. Missing or disabled widgets, revoked pin capabilities and
temporarily absent authored layouts are skipped; saved data never supplies
action authority. Full-widget embedded-media restoration follows the same
exclusion as the tray until its ownership transfer is supported. Starting the
host does not rewrite the stored bounds when current displays or zoom require
temporary clamping. Explicit pin/placement edits remain the persistence boundary.
No input or playback command is serialized in the pin preference file.

## Validation and remaining work

`Test-WinUiProductionPin.ps1` drives the actual shell menu and peer window using
an isolated bundled Now Playing workspace. It covers creation, native focus
transfer, returning to main, unpin and main-hide retention, without invoking media
playback. Presenter-specific control/action coverage is separate in
`Test-WinUiPinnedWidget.ps1`; click-through/opacity checks are in
`Test-WinUiPinnedWindow.ps1`.

This coordinator checkpoint is not complete pin feature parity. Compact media
transfer, opacity controls and
per-pin display-specific appearance resolution remain. Full-widget pinning is
not offered for an embedded-media session until its transfer ownership exists.
Physical Guide/controller/mixed-monitor acceptance remains separate from UIA.

Recorded checkpoint: analyzer build clean, 40 shell tests, five production native
checks plus a fresh-process passive-restore check. The native peer window
regression passes all 14 checks after the scale/appearance/accessibility wrapper
changes. Screenshots were inspected. Evidence:
`artifacts/winui-pinned-production/native-03` and `window-regression`.

## Controller placement adjustment

The tray's **Move / resize pinned widget** command starts a reversible host-owned
preview. Left stick/D-pad move, right stick resizes, A saves and B cancels.
Keyboard arrows move, Shift+arrows resize, Enter saves and Escape cancels. The
existing main window retains input ownership; the pinned peer remains passive
and its themed border identifies the edited surface. The semantic guide shows
the current host commands. Widget controls do not receive adjustment input.

Geometry follows the original 32-DIP step and 250/80ms repeat policy with native
axis hysteresis. The existing placement policy applies declared min/max sizes,
monitor DPI and work-area bounds. The preview never writes preferences until A.
Commit serializes with pin selection/removal and uses the existing atomic store;
failed persistence restores the starting placement. Cancel, hide, deactivation
or disconnect restore it without a write. A is the commit boundary, so hiding
after A does not convert an in-progress atomic save into cancellation.

The coordinator retains logical placement before native DPI notifications.
Queued reconciliation therefore does not divide already-scaled HWND pixels by
the old monitor scale. Monitor/work-area changes cancel an active preview and
resolve its anchor on the remaining displays. Interface zoom refreshes the
authored size limits without introducing a second scaling transform.

The opt-in `--validate-pinned-placement=<path>` requires `--shell-no-controller`
and an isolated profile with a saved pin. It exercises the real coordinator,
native bounds, Save/Cancel, profile persistence, same selection, passive input,
subsequent rail navigation and hide cancellation. It restores the original
placement afterward. Source is ready; consult the specialized-surfaces progress
document for the coordinated build/native result and remaining physical checks.

## Independent display appearance (2026-09-30)

Ordinary and compact-media pins resolve saved interface/text scale from their own
physical monitor identity. Hidden placement uses shared read-only Windows display
helpers, then chooses final dimensions before showing the peer. Monitor/DPI changes
refresh that identity; request revisions and current pin ownership reject late
results. Unavailable identity uses global defaults, never the main window's override.
This lookup does not report a new main display to the broker or redirect Settings.

Each pin owns its popup theme context. Computed text scaling is presenter-scoped,
including nested/indexed row content; shell/media chrome owns its own text scale.
Changing one window's text size cannot overwrite the other's. Native controls keep
their identities and scope subscriptions are released on unload/disposal. The
existing placement policy still separates interface zoom from monitor pixel DPI;
temporary reconciliation does not persist geometry without an explicit user edit.

Production pin41, native styles460, embedded media130 and actual local sample33
checks pass. Sample includes fullscreen/compact browser transfer, playback continuity,
input return, adjustment, opacity and unpin; captured pin/media images were inspected.
Evidence: `artifacts/winui-shell/pin-display-20260930/`. Independent monitor IDs/scales
are synthetic within the native tests; physical mixed-monitor acceptance is pending.
This supersedes the early checkpoint's missing compact-media/opacity/display-scale
items; it does not claim physical Guide/mixed-DPI or real-provider acceptance.
