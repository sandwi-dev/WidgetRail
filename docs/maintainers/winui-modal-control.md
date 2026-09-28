# WinUI widget-local modal checkpoint

`WidgetView.WithModal` still supplies the same protocol: a root `ModalLayer`
with two independently scoped children, the unchanged page and its dialog.
The WinUI adapter uses a native Grid at the widget's own bounds. A themed
hit-testable scrim is layered between the retained parent and dialog. It does
not create a window, centered application popup, or second layout engine.

The dialog uses native Grid rows and ScrollViewer content. Scroll-containing
children receive the bounded star row needed for native scrolling. Its authored
size (760 by 640 DIP fallback) is clamped to the actual widget with 16 DIP edge
clearance. Resizing updates native Width/Height/MaxWidth/MaxHeight and a local
clip; WinUI owns measurement and arrangement. The main host must provide a
bounded widget viewport. Intrinsic-height pinned projections omit the modal.

The default dialog brushes and corner radius use native ThemeResource bindings;
these are defaults for the shared style adapter to override with resolved WRSS.
This checkpoint does not claim full WRSS visual parity or opening/closing motion.

## Input and lifecycle

- Parent controls and ScrollViewer instances retain their identities. Reparenting
  under the layer does not rebuild their content or data sources.
- Inactive indexed collections remain visually enabled unless their declaration
  is actually disabled/busy. Input activity separately gates navigation, entry,
  item click, invocation and native tab stops. There is no disabled-state dimming
  simply because a modal owns focus.
- Native Tab focus cycles in the dialog. Presenter GettingFocus rejects focus
  into an inactive scope, including explicit programmatic attempts; controller
  navigation searches the active dialog scope. The scrim blocks parent pointer
  input. Command admission still validates the active scope.
- Existing remembered-scope focus returns to the parent on close. A new modal
  scope receives its declared initial focus; data updates in the same scope
  preserve the current native control. Removing a dialog retires its authority.
- B remains an authored shortcut dispatched by the host input coordinator. It
  must dismiss an open Select first. The layer never dispatches an implicit
  action on focus loss, outside click, resize, or overlay/window activation.
- Showing/hiding the main overlay is not opening/closing a widget modal.

`ContentDialog` was considered. Its ShowAsync/result lifetime and built-in button
semantics do not map directly to a worker-owned declarative page with its own
header/actions and independently changing active scope. A local native Grid
preserves that contract without translating it into an imperative dialog task.

## Validation

`ModalValidationPage` runs automatically on Loaded and writes
`%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/modal-controls-result.json`.
It constructs declarations through the real SDK `WithModal`, checking retained
parent objects/geometry/scroll, independent modal scroll, scope focus isolation,
obsolete commands, content updates, window focus restoration, nested Select
precedence, small and large widget resize, new-game scope, close/reopen, action
count and runtime replacement. It uses no physical controller backend.

The analyzer build passes with zero warnings/errors. Runtime execution awaits
the coordinated package slot. Real worker modal transport, indexed parent
collections, hardware controller behavior, high contrast, physical DPI scaling,
animations and production Playnite details remain integration checks, not claims
made by this fixture.

Integrated validation: --validate-modals passes 21 native checks. Screenshot review
also exposed an oversized test widget and an unsuitable translucent default panel
brush; the fixture now fits the validation window and the default panel uses the
native solid surface resource. Authored opacity/themes still require full style mapping.
Parent geometry/scroll, focus isolation, popup precedence, resize, retained commands
and owner replacement are covered. Evidence: artifacts/winui-surfaces/modal-controls-fixed-result.json
and modal-screen-fixed.png. This is not yet a real Playnite/indexed-parent acceptance.

## Indexed-parent real-worker regression

`IndexedModalValidation` extends the native real-worker fixture with a modal
opened by row 75, after logical entry and retained artwork are ready. Invoke
`IndexedWidget.ModalProbe` on the `--indexed-validation-pipe=<pipe>` page. It runs
without desktop key injection or controller ownership, and writes
`%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/indexed-modal-result.json`.

The probe captures native collection/source/container identity, parent offset,
row coordinates, semantic lease and retained presentation. It exercises row A,
modal A/B, inactive parent navigation and direct lease input rejection, live
content refresh, three additional open/update/close cycles, a smaller viewport
with 1.15 presentation scale, and parent-query replacement while a modal remains
open. Replacement must retire the former row lease/presentation and return to a
valid new-query focus target. The transform case is not monitor-DPI coverage.

A headless session-to-bridge-to-real-worker test also covers the distinction
between retained parent data/artwork and inactive input, modal B scoping, and
query retirement. The indexed bridge suite passes 16/16 with this new case.
An early version of that test issued an extra explicit Refresh concurrently with
the session's automatic invalidation consumer; it was corrected to wait for that
consumer's settled publication instead of manufacturing stale test authority.

Both the native analyzer build and bridge fixture build pass with zero warnings
or errors. Native indexed-modal probe execution remains pending coordinated
deployment. These checks are lifecycle/input correctness evidence, not Playnite
performance or physical controller acceptance.
