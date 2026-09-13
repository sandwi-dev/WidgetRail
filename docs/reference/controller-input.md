# Controller input

The host owns the opening shortcut and ordinary navigation. Widgets handle
meaningful actions for the currently active view.

## Context matters

| Context | Behavior |
|---|---|
| Overlay hidden | View + Menu or the selected Guide shortcut opens it |
| Tray | Navigation selects widgets; supported dashboard actions may be available |
| Open widget | Focus, controls, scoped shortcuts, and Back operate within the widget |
| Pin controls | Placement and size actions follow the pin-control guide |

F1 is the desktop toggle fallback. Settings lets the user choose one controller
opening shortcut at a time; View + Menu is the default.

## Actions before raw buttons

For a normal button, handle its action ID in `OnActionAsync`. A shortcut can
invoke the same action. This lets controller activation, the guide, and other
supported input paths share one meaning.

The focused control gets the first opportunity to consume an input appropriate
to its interaction. Scope rules determine which shortcuts and Back behavior are
available. Avoid a raw handler that duplicates a standard button or slider action.

## Back and routes

B closes or leaves the deepest relevant widget scope before returning to the
tray. Use the shared navigation helpers for nested pages and dialogs. Do not
turn “no widget action here” into an error when input should return to the host.

See [Routes and scopes](routes.md) for the route stack and cancellation behavior.

## Focus and scrolling

D-pad and left-stick movement choose a focus target and reveal it. Navigation
uses layout geometry, collection context, and any explicit links. Offscreen
items can still be candidates within their scroll collection.

Right-stick scrolling moves the viewport and settles focus after scrolling
stops. See [Collections](collections.md) rather than writing a second paging
or focus-follow system inside the widget.

## Updates while an input is pending

An input belongs to the view and action the user saw. If that snapshot is stale,
the runtime may obtain the current snapshot and revalidate before delivery.
It must not replay an already-delivered action or redirect input to a different
control merely because the new view has a matching position.

Custom raw handlers need stronger validity checks than “focus still matches”.
Prefer declared semantic actions whenever possible.

## Windows limitations

Foreground focus and controller hiding are different mechanisms. GameInput
foreground exclusivity does not suppress all XInput, HID, or other application
input paths. Optional Exclusive control has its own lifecycle and drivers.
Keep game-compatibility claims within the [documented limits](../users/known-limitations.md).

For implementation details, see [`ControllerInputOwnership.h`](../../src/OverlayHost/ControllerInputOwnership.h),
[`ControllerShortcutResolver.cs`](../../src/WidgetProtocol/ControllerShortcutResolver.cs),
and [Controller read fallback](controller-read-fallback.md).
