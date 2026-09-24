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

When options are available, the guide shows **Options** beside the button that
opens them (Menu, X or Y, with the current controller's glyph). Focused tiles also
show a small ellipsis indicator; unfocused tiles do not. The guide covers menus
declared on non-focusable containers as well. A menu hint takes priority over
general shortcuts when guide space is limited.

Widget hints use the available width rather than a fixed hint count. Back and
Close retain their space; the remaining hints are considered in button priority
order: Options, X, LT/RT, LB/RB, then other shortcuts and generic A Select.
When both triggers or both bumpers have hints, each pair is shown together or
omitted together. A lone trigger or bumper hint can appear individually. Labels
remain complete, and smaller lower-priority hints can use any remaining space.

## DualSense controllers

DualSense input is read directly over USB or Bluetooth. Cross activates the
same action as A, Circle goes Back, and Square/Triangle map to X/Y. Create +
Options is the View + Menu opening shortcut; the PS button works when the Guide
shortcut is selected. The guide and shared controller hints automatically use
PlayStation glyphs while a DualSense supplies input.

The native reader supports ordinary navigation and the existing Exclusive
control routing. Ordinary navigation checks activity in GameInput, XInput, then
native DualSense order. An idle connected controller does not block another
backend. The active source keeps its held input through release, so merely
connecting a DualSense does not take control away. Exclusive control retains
its existing selected-controller routing. See [input selection](controller-read-fallback.md).
Disconnects clear the current reading; reconnecting requires fresh input before
shortcuts can fire. Widgets keep using the same semantic actions and bindings.

This initial support covers buttons, D-pad, sticks, and triggers. Rumble,
adaptive triggers, touchpad gestures, and motion controls are not implemented;
unsupported vibration feedback does not interrupt input.

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
