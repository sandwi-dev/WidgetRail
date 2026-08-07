# Controller input model

Status: implemented prototype policy

Guide/Home is the global overlay toggle. The visible widget panel and icon tray
are persistent sibling regions while the overlay is open; changing focus does
not hide the selected panel. B is hierarchical Back: nested widget scope,
widget root to tray, then tray to close. Dashboard navigation and reordering are
host responsibilities; while widget controls own focus, their semantic focus
graph and actions receive B before the host considers a root-level fallback.

Guide/Home is intentionally absent from `ControllerButton`, so widgets cannot
intercept, remap, or suppress it.

## Contexts

| Context | Host-owned input | Widget input |
| --- | --- | --- |
| Hidden | Guide/Home opens the overlay. | No widget controller input. Ordinary polling is stopped. |
| Tray / dashboard focus | D-pad or left-stick Left/Right selects the adjacent widget and swaps the visible panel automatically; A moves focus into that panel; B closes; Y enters/exits reorder. | The selected panel is Visible and may expose up to three declared quick actions on X, LB, RB, LT, RT, stick clicks, Menu, or View. Other undeclared input is unhandled. |
| Widget-panel focus | Guide/Home closes. D-pad and two-dimensional left-stick movement change widget focus; focused Sliders own horizontal adjustment. Unhandled root-scope B and a root Down boundary return focus to the still-visible tray. | A activates the focused Button or an optional Slider activation. B is offered to the active scope first; X, Y, bumpers, triggers, stick clicks, Menu, and View are available as scoped shortcuts or custom semantic handling. |

The protocol names the contexts `DashboardQuickAction` and `OpenWidget`.
Events carry button, phase, optional focused element ID, input sequence,
monotonic timestamp, active input-scope ID, and rendered snapshot sequence. The
MVP host emits only `Pressed`; snapshot validation rejects `Released` or
`Repeated` shortcut bindings until those phases are transported end to end.

## B behavior

While widget controls own focus, B is offered to the active input scope. A
nested scope must bind its own one-level Back action; an unhandled nested B does
not bubble through widget state. If the root scope does not handle B, the host
moves focus to the icon tray without hiding the selected panel. B on the tray
closes the overlay. Guide/Home remains the global toggle, is detached from both
regions' navigation graphs, and closes immediately from any depth.

The tray and each widget scope retain independent focus memory. A on the tray
enters the selected panel at its remembered/root initial control. Down from the
last root-scope control—including an explicit self-neighbor used to express a
boundary—moves focus back to the tray. The host applies that boundary fallback
only at the widget root; it never escapes a nested dialog or subnavigation
scope.

## Dashboard quick actions

Quick actions are bounded, visible prompts, not hidden global hotkeys. Each
declares `{ button, actionId, label }`. The host owns card navigation and
decides how prompts are displayed. Validation rejects:

- more than three quick actions;
- duplicate buttons;
- blank labels or invalid action IDs; and
- A, B, Y, or D-pad as dashboard bindings.

The bridge rejects attempts to forward dashboard A, B, Y, or D-pad as raw widget
input even if a malformed native client requests it.

## Open-widget routing

The default SDK routes against the most recently rendered snapshot:

1. D-pad or two-dimensional left-stick movement normally changes the focused
   stable ID. An explicit directional neighbor wins; otherwise the host uses
   the rendered focus rectangles to choose a deterministic spatial neighbor.
   There is no wraparound. A focused Slider is the exception: Left/Right is
   consumed for one-step value adjustment, while Up/Down remains navigation.
2. Pressed A invokes the focused Button action or a Slider's optional
   activation action.
3. Other buttons first match a shortcut on the focused node, then a binding on
   the `ViewSnapshot.ActiveInputScopeId` root container. The router never
   searches an arbitrary unfocused descendant. Focus may be absent, so a
   scope-container binding such as modal B still resolves.
4. The root is the default input scope. A Stack or Row may start a nested scope
   with `.InputScope("scope-id")` and may bind actions directly with
   `.Shortcut(ControllerButton.B, "close")`. The widget explicitly publishes
   which scope is active; routing never infers it from focus and never searches
   a parent or sibling scope.
5. Disabled or busy Buttons and Sliders remain explicit and geometric focus
   candidates, preserving the exact focus ID across state changes. The SDK
   suppresses their A activation, shortcut, and Slider adjustment actions.
6. Unmatched or ambiguous input returns `handled = false`.

Shortcut placement follows the same lookup order. Put an action that belongs
only to one control on that Button; it works only while that exact Button is
focused. Put an action that must work anywhere in the open window on the active
scope-root Stack/Row/Scroll. Do not copy the same window shortcut onto sibling
buttons and do not expect the router to search those siblings.

The runtime also requires the input's active-scope ID and snapshot sequence to
match the latest rendered snapshot. Stale input, a mismatched scope, or a focus
ID outside the active scope returns unhandled. Explicit focus-neighbor edges
cannot cross scope boundaries, and initial focus must belong to the active
scope.

`ActiveInputScopeId` is widget-published state, not a value the host derives
from focus. `SnapshotSequence` correlates an input with the exact tree the user
saw. The host must copy both values from that rendered snapshot into every
`OpenWidget` input; an older sequence cannot activate an action after a rerender
changes scope, bindings, disabled state, or node identity.

A node may bind each `(button, phase)` only once; snapshot validation rejects a
duplicate on that node. Different controls in the same scope may reuse a button
because exact focus owns the first lookup. Dashboard quick actions are a
separate bounded surface and do not participate in open-widget scope lookup.
Guide/Home remains host-owned regardless of focus or scope.

### Focus ownership and restoration

The host owns live focus. Widgets provide stable node IDs, optional directional
neighbors, `InitialFocusId`, and `ActiveInputScopeId`; they do not persist or
push the currently focused ID.

The current host remembers focus independently for each widget and input scope.
After a new snapshot, it restores the remembered ID when that Button or Slider
is still present in the active scope, including while Disabled or Busy.
Otherwise it tries `InitialFocusId`, then the first focusable control in that
scope. If none exists, the surface is intentionally focusless. Container
shortcuts still work there, which lets a focusless notice or modal bind B to
dismiss itself without exposing a fake button.

Resolved dashboard and open-widget actions enter the same FIFO bounded to 16
pending items (plus the single action currently executing) and acknowledge immediately;
the acknowledgement means accepted, not completed. One action runs at a time,
so rapid LB/RB presses retain order even when an action performs network I/O.
A full queue returns `handled = false` without waiting. Deactivation cancels the
running action and drops queued actions. A later action failure is reported by
`Widget.ControllerActionFailed` and the runtime client's corresponding event;
it does not crash the worker or retroactively change the acknowledgement.
The host therefore publishes `Visible` for the panel selected while the tray
owns focus and `Interactive` only while that panel's controls own focus.
Selecting another tray item moves the prior panel to `Background`, publishes
the new panel as `Visible`, and swaps it in without an A press. Background
widgets reject controller-action admission without starting work.

Slider value changes use the same queue but carry a validated, quantized
**absolute** `RequestedValue`. A contiguous pending tail coalesces latest-wins
only when active lifetime, input scope, Slider source ID, and action ID all
match. A discrete action or a different Slider is an ordering boundary. This
keeps analog/D-pad repeat responsive without converting a stale rendered value
into a series of incorrect relative writes. Deactivation cancels the consumer
and clears all pending values.

### Analog hysteresis and repeat

The native navigator engages a left-stick direction at magnitude 15,000 and
does not release it until that axis falls below 9,000. It waits 360 ms before
the first repeat and repeats every 125 ms. Once engaged, small diagonal noise
does not flip axes; a perpendicular direction must cross the engage threshold
and become clearly dominant. Exact diagonal ties prefer horizontal movement.

When the overlay opens, the navigator is primed from the current stick state so
an already-held stick does not immediately move focus. D-pad movement remains
edge based. These constants are current prototype behavior, not a widget API.

Widgets can override `OnControllerInputAsync` for richer semantic controls,
but the current protocol does not provide Guide/Home or arbitrary raw HID
reports.

## Guide acquisition

The native host's primary Guide path is the documented GameInput system-button
callback, registered for background Guide delivery and foreground-exclusive
Guide behavior. Rising edges are posted onto the host window thread.

Some Xbox-360-class controller drivers observed in testing, including an
8BitDo controller in one mode, did not report Guide through that callback. The
prototype therefore has a quarantined compatibility adapter which loads the
system `xinput1_4.dll`, resolves its undocumented ordinal-100 extended-state
entry point, and polls four XInput slots every 25 ms for the hidden Guide bit.
The adapter is isolated and removable; it is not a Microsoft-supported API
contract. Both sources are rising-edge tracked and share a 150 ms deduplication
guard.

Do not describe this as universal 8BitDo support. Results can vary by model,
firmware, controller mode, transport, Steam configuration, and other software
that owns Guide. Use the input probe and the real target configuration when
making compatibility claims.

## Containment limitation

The visible prototype uses documented `XInputGetState` for ordinary controls
and reads the first connected XInput slot. GameInput remains the primary Guide
path; the compatibility adapter above is limited to Guide discovery.
Foreground focus and GameInput exclusivity do not guarantee that a game using
background Raw Input or direct HID access will stop seeing controller input.
The project does not inject into games and currently ships no filter or
virtual-controller driver.

Use `tools/InputProbe` and its documented test matrix when evaluating a game,
controller, Steam, or Xbox Game Bar conflict. Do not claim universal input
suppression from a successful test in one title.

For the broader interaction rationale, see [PS5 control-center research](ps5-control-center-research.md).
