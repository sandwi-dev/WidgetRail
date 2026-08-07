# Controller input model

Status: implemented prototype policy

Guide/Home is the global overlay toggle. B is hierarchical Back: nested widget
scope, widget root, then dashboard/close. Dashboard navigation and reordering
are host responsibilities; once a widget is open, its semantic focus graph and
actions receive B before the host considers a root-level fallback.

Guide/Home is intentionally absent from `ControllerButton`, so widgets cannot
intercept, remap, or suppress it.

## Contexts

| Context | Host-owned input | Widget input |
| --- | --- | --- |
| Hidden | Guide/Home opens the overlay. | No widget controller input. Ordinary polling is stopped. |
| Dashboard / hover | D-pad and horizontal left-stick movement navigate; A opens; B closes; Y enters/exits reorder. | Up to three declared quick actions on X, LB, RB, LT, RT, stick clicks, Menu, or View. Other undeclared input is unhandled. |
| Open widget | Guide/Home closes. D-pad and two-dimensional left-stick movement change widget focus. Unhandled root-scope B returns to the dashboard. | A activates the focused button. B is offered to the active scope first; X, Y, bumpers, triggers, stick clicks, Menu, and View are available as scoped shortcuts or custom semantic handling. |

The protocol names the contexts `DashboardQuickAction` and `OpenWidget`.
Events carry button, phase, optional focused element ID, input sequence,
monotonic timestamp, active input-scope ID, and rendered snapshot sequence. The
MVP host emits only `Pressed`; snapshot validation rejects `Released` or
`Repeated` shortcut bindings until those phases are transported end to end.

## B behavior

While a widget is open, B is offered to the active input scope. A nested scope
must bind its own one-level Back action; an unhandled nested B does not bubble
through widget state. If the root scope does not handle B, the host returns to
the dashboard. On the dashboard, B closes the overlay. Guide/Home remains the
global toggle and closes immediately from any depth.

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

1. D-pad or two-dimensional left-stick movement changes the focused stable ID.
   An enabled explicit directional neighbor wins. If none is usable, the host
   uses the rendered focus rectangles to choose a deterministic spatial
   neighbor in that direction. There is no wraparound.
2. Pressed A invokes the focused button's action.
3. Other buttons first match a shortcut on the focused node, then a unique
   binding within `ViewSnapshot.ActiveInputScopeId`. Focus may be absent, so a
   scope-container binding such as modal B still resolves.
4. The root is the default input scope. A Stack or Row may start a nested scope
   with `.InputScope("scope-id")` and may bind actions directly with
   `.Shortcut(ControllerButton.B, "close")`. The widget explicitly publishes
   which scope is active; routing never infers it from focus and never searches
   a parent or sibling scope.
5. Disabled or busy buttons cannot activate through A or shortcuts and are not
   geometric focus candidates.
6. Unmatched or ambiguous input returns `handled = false`.

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

Bindings must be unique per `(button, phase)` inside one input scope; snapshot
validation rejects duplicates. Dashboard quick actions are a separate bounded
surface and do not participate in open-widget scope lookup. Guide/Home remains
host-owned regardless of focus or scope.

### Focus ownership and restoration

The host owns live focus. Widgets provide stable node IDs, optional directional
neighbors, `InitialFocusId`, and `ActiveInputScopeId`; they do not persist or
push the currently focused ID.

The current host remembers focus independently for each widget and input scope.
After a new snapshot, it restores the remembered ID when that button is still
enabled in the active scope, otherwise tries `InitialFocusId`, then the first
enabled button in that scope. If none exists, the surface is intentionally
focusless. Container shortcuts still work there, which lets a focusless notice
or modal bind B to dismiss itself without exposing a fake button.

Resolved dashboard and open-widget actions enter the same FIFO bounded to 16
pending items (plus the single action currently executing) and acknowledge immediately;
the acknowledgement means accepted, not completed. One action runs at a time,
so rapid LB/RB presses retain order even when an action performs network I/O.
A full queue returns `handled = false` without waiting. Deactivation cancels the
running action and drops queued actions. A later action failure is reported by
`Widget.ControllerActionFailed` and the runtime client's corresponding event;
it does not crash the worker or retroactively change the acknowledgement.
The host therefore publishes `Visible` for the selected dashboard widget before
offering its quick actions. Background widgets reject controller-action
admission without starting work; an open widget is `Interactive`.

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
