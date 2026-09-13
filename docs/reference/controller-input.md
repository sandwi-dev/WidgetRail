# Controller input model

## Opening the overlay

Settings → Controllers offers one active opening shortcut: View + Menu (the default)
or Guide. Explicit saved choices are preserved. The selected shortcut also closes the overlay. View + Menu uses
an optional 25 ms background observer with GameInput device connection callbacks
and public XInput state reads; it does not depend on the Windows Guide callback.
Both buttons must be down on the same controller. Held input is ignored at
startup/reconnect until released, and all observed copies must release before
another toggle. Selecting Guide stops the observer. The existing View + Menu
widget-restart action is disabled while it is the overlay shortcut; the selected
combination is not also dispatched as widget input. Game input is not suppressed
or delayed by the shortcut observer. Exclusive control remains independent.

## Exclusive control lifecycle

The Controllers Settings preference defaults to off and is separate from ordinary
overlay navigation. When enabled, one native routing thread owns the selected
physical GameInput reader and the ViGEm output. The selected-device disconnect
callback retires the reader, neutralizes output and stops old feedback. Discovery
uses GameInput blocking enumeration at 250 ms intervals while waiting, rather
than a permanent global arrival callback. Other devices cannot replace a connected
selection. Device handoff retains the owned virtual target where possible, restores
only owned HidHide changes, and requires fresh neutral input from the replacement.
Turning the setting off also cancels discovery or an initial held-input wait.

The host exchanges preference and status with the Bridge once per second; this
administrative exchange is separate from gameplay routing and has a two-second
transport deadline. Driver readiness reports expire after five seconds. The
Settings worker refreshes status only on its active Controllers page, and stops
that work when deactivated. Physical-to-virtual forwarding is supported;
recognized virtual input sources are excluded to avoid recapturing virtual output.

### Source classification and owned output

WIDGE-218 checks the bounded parent chain and device services. ViGEmBus devices
are excluded even when Windows gives the bus a generic `ROOT\SYSTEM` instance
ID. An exact node beneath the device-tree root beginning with `ROOT\SYSTEM\`
or `ROOT\USB\` is classified as software-enumerated and excluded from automatic
selection; this is not proof of its underlying hardware or application owner.
Supported USB, HID, Bluetooth and Bluetooth LE paths also need PCI/ACPI hardware
ancestry terminating at a recognized hardware root (including Windows ACPI HAL).
Missing, malformed, disappearing, cyclic, overly deep and unsupported paths are
unknown and never admitted simply because they lack a virtual marker. This is
conservative topology evidence, not a universal physical/virtual attestation.

For the owned ViGEm target, the adapter correlates the live target index with
the direct child's Windows device address beneath one present ViGEmBus instance.
Ambiguous buses, duplicate addresses or unavailable identity fail setup safely.
The resulting exact instance ID also excludes descendants during reconnect
discovery. Identity is cleared on target removal and resolved again on creation;
neither an Xbox name, VID/PID nor XInput player slot establishes ownership.
Children explicitly marked by Windows as pending removal do not participate in
this correlation: they may retain a devnode after their address property has
already disappeared. Unreadable live nodes and duplicate live addresses still
reject ownership. A matched, retiring target cannot be admitted either.
The bus index/address relationship is part of the dependency's implementation:
[ViGEmBus PDO metadata](https://github.com/nefarius/ViGEmBus/blob/d986e1d93708ec9b11049542fa6027272cce716c/sys/EmulationTargetPDO.cpp#L318).
No driver code is changed. Windows retains the compatible Xbox controller name.

The reconnect regression was caused by the retained output being selected while
the physical controller was absent. A device status callback did detect removal;
the subsequent discovery misclassified the output. Hardware observation on the
affected USB 8BitDo and ViGEm stack now separates both devices correctly. Broader
Bluetooth/built-in and other virtual-stack hardware coverage remains a manual
verification limit; their supported and unknown topologies have deterministic
coverage. Virtual-source forwarding and manual selection remain separate work.

If startup fails, the native host drains the routing owner and verifies exact-
owned HidHide recovery before restoring ordinary input. Failed cleanup remains
Recovery required. Successful cleanup publishes a separate Failed state; the
Settings switch displays Off with retry guidance and a Keep Exclusive control
off action to cancel the saved enable request. A saved request is never treated
as proof that exclusive routing started.

Selected-reader retirement sends an explicit zeroed GameInput rumble report.
Although the SDK annotates a null report as optional, GameInputRedist 3.3.221
can dereference it during teardown. A stop failure must not be mistaken for
successful physical-device restoration; retained owned-policy recovery remains
the recovery path after a host crash.

HidHide denies individual device instance IDs. The selected Xbox device may
also expose a separate HID joystick/gamepad collection; hiding only its Xbox
instance leaves that DirectInput/HID path accessible. Before enabling, the
native owner identifies gamepad/joystick HID interfaces in the selected device's
exact subtree and journals them together with the selected instance. Mouse,
keyboard, consumer-control and unrelated-device interfaces are excluded. All
owned entries are restored on disable, handoff or normal shutdown. Unknown or
changing identity aborts setup instead of widening the hiding scope. See the
[HidHide API documentation](https://docs.nefarius.at/projects/HidHide/API-Documentation/).

## Ordinary overlay input

Status: implemented prototype policy

Guide/Home is the global overlay toggle. View is the global one-pin navigation
control: from the tray or an open overlay widget it enters the current pin, and
from the focused pin it returns to the tray. The visible widget panel and icon tray
are persistent sibling regions while the overlay is open; changing focus does
not hide the selected panel. B is hierarchical Back: nested widget scope,
widget root to tray, then tray to close. Dashboard navigation and reordering are
host responsibilities; while widget controls own focus, their semantic focus
graph and actions receive B before the host considers a root-level fallback.

Guide/Home is intentionally absent from `ControllerButton`, so widgets cannot
intercept, remap, or suppress it. View remains in the protocol only for the
host-owned pinned-layout selection notification; authored View shortcuts and
quick actions are invalid.

## Contexts

| Context | Host-owned input | Widget input |
| --- | --- | --- |
| Hidden | Guide/Home opens the overlay. | No widget controller input. Ordinary polling is stopped. |
| Tray / dashboard focus | D-pad or left-stick Left/Right selects the adjacent widget and swaps the visible panel automatically; A moves focus into that panel; B closes; View enters the single pin regardless of tray selection; tapping Y enters/exits reorder. Holding Y for 700 ms restarts the exact selected bundled or installed bridge widget once through the F5 authority. | The selected panel is Visible and may expose up to three declared quick actions on X, LB, RB, LT, RT, stick clicks, or Menu. Y and View remain host-owned and are never forwarded from tray focus. Other undeclared input is unhandled. |
| Widget-panel focus | Guide/Home closes. View enters the single pin. D-pad and two-dimensional left-stick movement change widget focus; focused Sliders own horizontal adjustment. Unhandled root-scope B and a root Down boundary return focus to the still-visible tray. | A activates the focused Button or an optional Slider activation. B is offered to the active scope first; X, Y, bumpers, triggers, stick clicks, and Menu are available as scoped shortcuts or custom semantic handling. |
| Pinned-surface focus | Guide/Home closes the overlay; View returns to tray focus; placement/setup/opacity retain their exclusive A/B controls. | D-pad/stick moves focus, A activates, and B plus other nonreserved authored buttons route only against the exact selected projection. An unhandled root B is inert. |

The protocol names the contexts `DashboardQuickAction` and `OpenWidget`.
Events carry button, phase, optional focused element ID, input sequence,
monotonic timestamp, active input-scope ID, rendered snapshot sequence, and a
closed origin: `PhysicalController` or `AccessibilityAutomation`. The default
physical origin is omitted on the wire, so legacy payloads retain their prior
meaning; automation is always explicit and requires a matching strict transport
peer. Authored actions remain edge-only by default. A shortcut or dashboard
quick action may opt into `ControllerActionRepeatPolicy.WhileHeld`; the host
then emits one `Pressed`, waits 360 ms, and emits bounded `Repeated` actions at
125 ms while the same exact button and semantic authority remain current.
Authors still declare the shortcut phase as `Pressed`; `Released` and authored
`Repeated` bindings remain invalid.

### Tray Y tap/hold arbitration

The visible host requests a 15 ms controller cadence; bounded WIDGE-104 host
measurements delivered 58.51-59.24 controller ticks per second without
post-warmup paint. That cadence drives a fixed 700 ms Y hold; no gesture timer
or controller polling remains active while hidden. Before
the threshold, release performs the ordinary reorder tap. At or after the
threshold, the host revalidates tray focus, non-reorder state, and the exact
selected bridge widget ID, then uses the same selected-worker restart authority
as F5 exactly once. No descriptor or widget-authored Refresh action is required,
and tray Y is never forwarded into widget code. The winning hold consumes release
and cannot become reorder or widget Y even if the restart reports failure.

Any accepted shell transition—including selection, focus, open-widget, reorder,
overlay, or lifecycle change—cancels pending progress. Window focus loss and
controller loss also cancel it. A canceled capture suppresses its stale physical
release; overlay hide resets it after visible polling has stopped. The tray hint
shows live percentage progress and its UI Automation help states both meanings
for every selected bridge widget. F5 and Hold Y share one worker-restart path,
resolving the selected widget at tray focus or the active widget at widget-panel
focus.

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

UI Automation Invoke on an enabled tray item uses the same host-owned selection
transition. A successful admission selects the stable widget exactly once and
returns success even though the selected widget runtime changes during the call;
a rejected admission remains a failure. Host-shell and tray providers retain
their stable identities across that runtime change, while widget-authored
providers remain bound to their exact runtime generation.

### Host-owned text entry

A focused `TextEntry` opens one themed native modal owned by the overlay. The
host establishes its exclusive interaction scope before showing it: the widget
and tray become semantically inert, their windows cannot receive pointer or
keyboard input, and the controller's opening and closing buttons must return to
neutral before a new overlay gesture is admitted. The modal never forwards raw
controller, keyboard, pointer, clipboard, or UI Automation input to a widget.

D-pad or left stick moves among the 40 character/layer keys without typing. A
inserts the focused character, X backspaces at the caret, B cancels only the
modal, and right trigger performs **Enter**. LB/RB moves the visible caret left
or right with bounded repeat; it does not move key focus. Shift and symbol keys
change the reachable character layer. There is no focusable Clear/Cancel/Enter
action row: a nonfocusable themed legend explains these mappings. Pointer or
UI Automation Invoke activates an exact key once, while physical Left/Right,
Enter, Escape, Backspace, and typing remain available through the native edit
control. Paste is admitted only when explicitly requested while that edit has
focus and only when the complete value fits its authored bound.

The authored prompt is a label, never the editable value. The committed value
and live edit buffer are distinct; every insertion, deletion, paste, and caret
move is reflected immediately. Sensitive input uses native password semantics
and blocks copy, cut, context-menu/export, drag/drop, and UI Automation value
retrieval while retaining bounded user-invoked paste. The active WidgetRail appearance supplies the
complete canvas, panel, controls, focus treatment, typeface, text scale, and
interface scale. The borderless popup has no caption, resize, or system-menu
affordance and remains centered inside compact, wide, high-DPI, and scaled work
areas. Closing the modal restores the exact prior focus when it is still valid,
otherwise the host resolves one current target.

On the ordinary widget surface, `TextEntry` is one actionable UI Automation
button with its existing stable node/action identity. Focus and Invoke pass
through the same current widget, runtime, snapshot, scope, enabled-state, and
action checks as controller A before opening the one host modal. Failed open,
cancel, window close, and commit remain distinct sanitized outcomes. Cancel and
close dispatch no action, preserve the committed snapshot value, and restore
the exact current semantic/UIA focus target.

`UI.SensitiveTextEntry` publishes an empty authored value and non-secret prompt
or status copy only. The live secret exists solely in the protected modal edit
buffer and the one exact semantic widget action. Only one final bounded
committed value is sent with that action.
Raw key events, HWNDs, insertion history, and canceled text never enter the
snapshot or worker. The host rejects values beyond the authored maximum (at
most 96 UTF-16 code units) or containing control characters. No snapshot or
node reference survives the modal loop: before sending, the host freshly
resolves the active widget, runtime and presentation generation, input scope,
source ID, action ID, authored bound/value, and enabled/busy state. A harmless
higher-sequence refresh may retain authority only when all of those values are
unchanged. Replacement, removal, hide, scope/action/value/bound change, or an
unavailable control rejects the result. Replacement and Bridge-session
retirement cannot replay it. Enter reports bounded host feedback without
displaying or logging the committed text; framework diagnostics and failure
events retain correlation metadata with the committed value removed.

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

The selected Audio Mixer card publishes LB/RB as a clamped five-percentage-
point master-output step and X as master mute/unmute. Its three labels include
the current authoritative/optimistic percentage and mute state before
activation. These dashboard declarations are separate from the open-widget
surface: focused Sliders still own D-pad Left/Right absolute adjustment and A
mute activation, and open-widget LB/RB do not become audio shortcuts.

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
   layout geometry to choose a deterministic spatial neighbor. It searches the
   nearest responsive grid or matching-axis Scroll first, including offscreen
   items that the host can reveal, before searching the rest of the surface.
   Automatic Left/Right movement requires vertical overlap beyond subpixel
   edge contact, so a narrower button above a rail cannot become its Left
   destination. Up/Down prefers column alignment but allows diagonal fallback
   for uneven grid rows and transitions between sections. Explicit authored
   links retain priority over these geometric rules.
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

An optional bounded shortcut `Label` is presentation metadata for that exact
binding. The open-widget controller guide uses it first, then falls back to the
owning control's visible text and accessibility label. Scope-root bindings that
should be advertised need explicit labels because one element accessibility
name cannot describe several button/action pairs. An absent label preserves an
input-only shortcut. Dashboard QuickAction labels do not supply shortcut text.

The SDK's direct controller callback still requires the input's active-scope ID
and snapshot sequence to match its latest rendered snapshot. The shared bridge
admission path handles a snapshot published between native input capture and
worker delivery (WIDGE-239): it compares the retained origin and latest snapshot
under the same per-widget gate used for presentation publication. A compatible
input adopts that latest sequence without requesting another render. Runtime,
scope, focused-node availability, action owner/binding and slider bounds must
remain valid. Missing origin history or changed authority retires the event.

A revalidated worker request checks that sequence again before invoking the
callback once. Declared actions retain their existing override/bookkeeping
behavior. Unbound input can cross a snapshot update only when the worker uses
the SDK's standard controller handler; private raw override semantics cannot be
proved from matching focus alone. Standard unbound B still returns unhandled,
allowing the host's normal root-scope Back behavior. A rejected event is distinct
from unhandled: it produces no toast and cannot trigger Back in a changed scope.
The host requests an asynchronous presentation refresh after a rejection but
never replays the event. Delivered input and transport failures are never retried.

The existing strict runtime-v2 handshake and controller payload are unchanged.
Older packaged workers reject the new request before dispatch; only a verified
declared action may then use their existing input request, once. Unbound raw
revalidation requires rebuilding the application package with the shared runtime.
No widget routing changes or public SDK API changes are required.

Explicit focus-neighbor edges cannot cross scope boundaries, and initial focus
must belong to the active scope. Dashboard capability gestures and pinned-layout
selection retain their separate exact-authority policies.

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
The focused production-host fixture exercises this route without live
credentials. It opens the real YT Music widget, invokes the focused play/pause
control through the production host action ingress using the deterministic HWND
keyboard mapping, and lets a command failure travel through the worker runtime
and managed bridge. The host paints and projects the fixed status `YT Music
action failed; try again` as a polite UI Automation live region for four seconds
while retaining focus. A later failure replaces the deadline; Hide and Stop
retire feedback; the original worker is not restarted; and neither worker
exception text nor the fixture's private sentinel enters UI or the host log.
Physical-controller evidence remains separate; the fixture still traverses the
real host resolver, bridge/runtime/SDK failure path, paint pass, and UI
Automation provider.
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

The native full-widget and pinned-surface interaction owner presents every
actual controller step immediately, then emits only the latest absolute value
after a 150 ms trailing quiet period. A final A/B adjustment-mode exit or
geometric focus departure attempts that exact-current value first; an explicit
admission failure rolls the optimistic value back, reports bounded feedback,
and does not trap navigation. UI Automation `RangeValue.SetValue` remains one
immediate absolute request. Equal or clamped controller samples do not extend
the quiet period.

Snapshots remain authoritative, but they do not identify the action request
that caused them. The host therefore keeps at most 16 recent sent values only
as a lossy reconciliation hint. A newer snapshot that exactly repeats one of
those values may be held behind the latest presented target until that sent
value's fixed two-second guard expires; every newer snapshot still updates the
latest observed authoritative value, and expiry reconciles it without waiting
for another snapshot. Snapshot traffic never renews a guard. Capacity never
throttles input or transport: the oldest hint is discarded, so an unusually
late result beyond the retained history can still be presented. Eliminating
that bounded ambiguity would require a correlated response contract rather
than native value inference.

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

Without controller isolation, the native host's primary Guide path is the documented GameInput system-button
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

With `--controller-isolation`, the sole native routing thread polls that same
XInput adapter across all four slots every 25 ms and sends rising Guide edges
through the bounded local queue and existing debounce. It does not register a
GameInput Guide callback: that registration succeeded but delivered no events
under the tested HidHide configuration. Ordinary physical gamepad readings,
disconnect notifications and rumble still use GameInput. Guide is accepted from
any controller; gameplay routing remains tied to the selected physical device.
This routing thread continues while the overlay is hidden and is independent
of rendering. Normal process shutdown stops routing and restores only owned
HidHide changes; gameplay forwarding is not guaranteed after exit or crash.

Do not describe this as universal 8BitDo support. Results can vary by model,
firmware, controller mode, transport, Steam configuration, and other software
that owns Guide. Use the input probe and the real target configuration when
making compatibility claims.

## Pinned-surface placement mode

Pinned setup and adjustment are entered through the host-owned tray menu rather
than by repurposing View. D-pad or left-stick repeat changes the bounded move
preview, right stick resizes, LT/RT cycles layouts during setup, A commits
atomically, and B cancels to the exact pre-gesture state. Hiding the overlay,
capture loss, package/runtime replacement, or display reconciliation also
cancels unfinished placement. Keyboard (`M`/`R`, arrows, Enter/Escape), host
pointer chrome, and UI Automation actions use the same generation-bound state
machine.

## Pinned-surface focus and emergency exit

View explicitly transfers the one controller focus owner from either the tray
or open overlay widget to the Interactive pin and returns it to the tray.
D-pad/stick uses the shared authored/geometric focus resolver; A and B queue
exact generation/layout/scope/snapshot-bound widget actions. A root-unhandled B
is inert. Guide retains its global overlay-close meaning, cancels pinned
placement/focus, and never forwards hidden input. LB+RB+X and visible-overlay
Ctrl+Shift+H invoke one host-owned emergency unpin path. Placement mode continues
to own A/B before these ordinary routes.

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

For the broader interaction rationale, see [PS5 control-center research](../archive/ps5-control-center-research.md).
