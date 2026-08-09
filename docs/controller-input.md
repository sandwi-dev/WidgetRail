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
monotonic timestamp, active input-scope ID, rendered snapshot sequence, and a
closed origin: `PhysicalController` or `AccessibilityAutomation`. The default
physical origin is omitted on the wire, so legacy payloads retain their prior
meaning; automation is always explicit and requires a matching strict transport
peer. The MVP host emits only `Pressed`; snapshot validation rejects `Released`
or `Repeated` shortcut bindings until those phases are transported end to end.

### Desktop test fallbacks

Keyboard and pointer support exists for local testing and accessibility without
changing the controller-first widget contract. Arrow keys use the same host
navigation path, Enter maps only to A/select, and Escape maps only to B/back.
No other controller buttons are synthesized and these fallbacks are not added
to controller guide hints. A left click on a tray icon selects/enters it. A
left click on a visible declarative Button or Slider selects that stable focus
ID and invokes semantic A only when enabled. Hit testing is host-owned, clipped
to the renderer's visible active input scope, and never forwards raw mouse data
to widget code. Clicking the dimmed backdrop closes the overlay.

## B behavior

While widget controls own focus, B is offered to the active input scope. A
nested scope must bind its own one-level Back action; an unhandled nested B does
not bubble through widget state. If the root scope does not handle B, the host
moves focus to the icon tray without hiding the selected panel. B on the tray
closes the overlay. Guide/Home remains the global toggle, is detached from both
regions' navigation graphs, and closes immediately from any depth.

The UI Automation composite exposes the same hierarchy. Root Back is a typed
host transition to the tray. Nested Back is present only for an exact active
scope where the managed open-widget resolver would handle pressed B for the
current focus. Focusless input checks the scope root; focused input honors the
focused shortcut first, including Disabled/Busy suppression, then its nearest
ancestor shortcut. Stale focus and focus inside another scope fail closed.
Invocation revalidates the current widget generation, snapshot sequence, scope,
and focus before sending B with `AccessibilityAutomation` origin. A stale or
missing nested binding is dropped; it never becomes a root or tray fallback.

The tray and each widget scope retain independent focus memory. A on the tray
enters the selected panel at its remembered/root initial control. Down from the
last root-scope control—including an explicit self-neighbor used to express a
boundary—moves focus back to the tray. The host applies that boundary fallback
only at the widget root; it never escapes a nested dialog or subnavigation
scope.

## Dashboard quick actions

Quick actions are bounded, visible prompts, not hidden global hotkeys. Each
declares `{ button, actionId, label, capability? }`. The optional typed
`capability` contains one exact capability ID and operation ID; both fields are
required together and use the same bounded safe-identifier grammar as action
IDs. The host owns card navigation and
decides how prompts are displayed. Validation rejects:

- more than three quick actions;
- duplicate buttons;
- blank labels or invalid action IDs; and
- A, B, Y, or D-pad as dashboard bindings.

A capability-bearing quick action does not make the selected widget
Interactive. The bridge matches the pressed button and both controller/snapshot
sequences against its current cached rendered snapshot, then records a dormant
host-owned reservation for that exact declared control operation for at most 10
seconds. This reservation is not broker authority. The SDK carries the input
and snapshot sequences privately while it executes the bounded serial action;
the widget author never receives or forwards a lease token. Only when the exact
typed operation is invoked does the runtime atomically match and remove the
reservation and activate an identity/PID-bound broker lease. The broker expires
that lease within two seconds and consumes an exact match once. Wrong
capability/operation/sequence, replay, stale snapshots, lifecycle change, worker
replacement, missing declaration, or missing consent all fail closed. No read
access, subscription, background work, or lifecycle promotion is created by
this gesture.

Only `PhysicalController` input can create that reservation or enter the SDK's
private gesture context. UI Automation Invoke and RangeValue requests are
tagged `AccessibilityAutomation`: after the host revalidates the exact visible
widget, generation, snapshot, scope, node, action, and enabled state, they may
route an ordinary `OpenWidget` action, but they never qualify for the Visible-
state dashboard exception. The bridge returns no gesture authority for an
automation-origin dashboard event, and the runtime independently rejects any
authority paired with that origin. The worker also refuses to create ambient
gesture context for automation, and the broker adapter emits gesture sequences
only after exact host activation succeeds. Interactive lifecycle capability
rules still apply after the user-facing widget surface is open; origin is not a
substitute for declaration, consent, lifecycle, payload, or provider validation.

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

Direct host actions, catalog quick actions, and resolved dashboard/open-widget
actions enter the same FIFO bounded to 16 pending items plus the single action
currently executing. `WidgetProcessClient.AdmitActionAsync` returns `Enqueued`,
`Replaced`, `RejectedInactive`, or `RejectedCapacity`; acknowledgement means
accepted, not completed. `SendActionAsync` remains a compatibility wrapper and
throws for either rejection. One action runs at a time, so direct input and
rapid LB/RB presses retain their shared order even during network I/O. A full
controller queue returns `handled = false` without waiting. Deactivation
cancels the running action, drops queued actions, and drains cooperative work
before `OnDeactivatedAsync`. A later failure is reported by
`Widget.ActionFailed` and `WidgetProcessClient.ActionFailed`; the former
`ControllerActionFailed` events remain compatibility aliases. Failure does not
crash the worker or retroactively change admission.
The native host presents a late failure as fixed generic copy for four seconds;
worker exception text never crosses this boundary. Feedback is keyed by widget
ID and runtime generation, so a failure appears only on that widget's dashboard
or open-widget surface. A failure for another widget cannot overwrite it, and
catalog replacement, removal, overlay hide, or host stop retires it. Each
bounded bridge drain schedules the earliest expiry and requests at most one
repaint; the controller timer performs the same idempotent expiry as a fallback
if the dedicated Win32 timer cannot be registered.
The host therefore publishes `Visible` for the panel selected while the tray
owns focus and `Interactive` only while that panel's controls own focus.
Selecting another tray item moves the prior panel to `Background`, publishes
the new panel as `Visible`, and swaps it in without an A press. Background
widgets reject every action ingress without starting work.

Capability authority is intentionally narrower than admission. Only trusted,
snapshot-bound physical-controller input can attach one exact dashboard
gesture, and that authority starts when its queued action executes. The bridge's legacy
catalog `QuickAction` command has no snapshot/gesture metadata and therefore
never grants capability authority; capability-backed dashboard controls must
use `ControllerInput`.

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

## Foreground ownership and containment limitation

While visible, the host requests `GameInputEnableBackgroundInput` and
`GameInputExclusiveForegroundInput` together with the existing background-Guide
and foreground-exclusive-Guide policy. A visibility-scoped host lease starts
ordinary GameInput reads on show and ends them on hide, so navigation remains
reliable even when Windows declines activation. The Win32 host makes one direct
and at most one bounded `AttachThreadInput` activation attempt only when first
shown, detaching immediately; polling never runs a foreground-steal loop.
Foreground confirmation upgrades the same read path to GameInput exclusivity.
Without it, diagnostics explicitly report the visible lease as background-
shared. Alt+Tab or another valid external foreground transition still closes
the overlay.

If GameInput cannot initialize, the visible host retains a documented XInput
compatibility path, clearly diagnosed as non-exclusive. Background GameInput
delivery improves overlay reliability but does not consume the event.
GameInput exclusivity
only prevents *other GameInput clients* from seeing ordinary input received by
the focused overlay. It cannot consume delivery through XInput, Raw Input,
direct HID, Steam Input, or another remapping/virtual-controller layer. A
normal desktop overlay has no universal "consume this controller report"
operation across those APIs. Universal suppression would require a separately
installed, explicitly opt-in HID interception or physical-to-virtual controller
layer. This project does not inject into games and is not authorized to install
such a driver.

Use `tools/InputProbe` and its documented test matrix when evaluating a game,
controller, Steam, or Xbox Game Bar conflict. Do not claim universal input
suppression from a successful test in one title.

For the broader interaction rationale, see [PS5 control-center research](ps5-control-center-research.md).
