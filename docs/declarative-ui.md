# Declarative UI reference

Status: protocol and managed SDK implemented; generic native rendering remains
a prototype integration

Widgets return a semantic tree from `Widget.Render()`. The host owns layout,
pixels, focus presentation, accessibility, and controller dispatch. Widgets
cannot submit HTML, JavaScript, SVG, font glyphs, or arbitrary drawing paths.

## Elements

| SDK call | Protocol kind | Purpose |
| --- | --- | --- |
| `UI.Stack(id, children)` | `stack` | Vertical semantic container. |
| `UI.Row(id, children)` | `row` | Horizontal semantic container. |
| `UI.VerticalScroll(id, children)` | `scroll` | Host-owned vertical viewport with controller focus-follow. |
| `UI.HorizontalScroll(id, children)` | `scroll` | Host-owned horizontal viewport with controller focus-follow. |
| `UI.Scroll(id, axis, children)` | `scroll` | Axis-explicit form of the same bounded viewport. |
| `UI.Text(text, id, accessibilityLabel?)` | `text` | Non-interactive text. |
| `UI.Button(label, action, id)` | `button` | Focusable action control; may include a semantic glyph. |
| `UI.ToggleButton(label, isOn, action, id)` | `button` | Controller-ready two-state button composed from existing button semantics. |
| `UI.Stepper(label, value, decrementAction, incrementAction, id, canDecrement?, canIncrement?)` | `row`, `text`, `button` | Label/value row with separate bounded decrement and increment actions. |
| `UI.Progress(value, maximum, id, accessibilityLabel?)` | `progress` | Read-only bounded progress where `0 <= value <= maximum` and `maximum > 0`. |
| `UI.Slider(value, minimum, maximum, step, valueChangedAction, id, accessibilityLabel, accessibilityValue?, activationAction?)` | `slider` | Protocol-v3 controller value control with absolute requested values. |
| `UI.Spacer(id)` | `spacer` | Layout spacing node. |
| `UI.Image(httpsSource, id, accessibilityLabel, fit?)` | `image` | HTTPS image with required accessible alternative text. |
| `UI.Icon(glyph, id, accessibilityLabel)` | `icon` | Host-rendered semantic vector icon from a closed enum. |
| `UI.IconButton(glyph, action, id, accessibilityLabel, variant?, size?)` | `button` | Accessible icon-only action with controller-safe semantic classes. |
| `UI.Card(id, variant?, children...)` | `stack` | Nonfocusable raised/subtle/transparent grouping surface. |
| `UI.SectionHeader(title, id, eyebrow?, description?, trailing?)` | `stack`, `row`, `text` | Stable title hierarchy with optional trailing content. |
| `UI.StatusBadge(label, tone, id, glyph?)` | `row`, `icon`, `text` | Nonfocusable status that never relies on color alone. |
| `UI.Divider(id)` | `spacer` | Decorative themeable separator. |
| `UI.Alert(...)`, `UI.EmptyState(...)` | `stack`, content, optional `button` | Bounded guidance with zero or one recovery focus stop. |

Stack, Row, and Scroll containers may call `.InputScope("scope-id")` to start a nested
controller input surface. The root is always the default input scope, so a
simple widget does not need to declare one. Those containers may also call
`.Shortcut(button, actionId)` for a surface-level action that must work without
focused content, such as B to dismiss a modal.

## Controller scroll containers

Use a semantic Scroll container when a surface can contain more controller
targets than its clamped viewport. Do not implement a hidden index, LB/RB
cycling, or widget-owned pixel offsets:

```csharp
var sessions = UI.VerticalScroll("audio.sessions",
    sessionRows.Select(BuildSessionRow).ToArray())
    .Classes("session-list");
```

The host measures the full content extent along the declared axis, clips it to
the container's content box, and clamps its offset to that measured extent.
Offscreen buttons remain valid D-pad/analog navigation targets. When focus
moves, the host scrolls the nearest edge just far enough to make the complete
control visible before painting it. A widget never receives or publishes the
offset.

Keep the Scroll ID and descendant button IDs stable across snapshots. Offset
memory is isolated by exact widget runtime instance, active input scope, and
Scroll container ID, so closing/reopening a widget or nested surface restores
the focused row without leaking state to another version or modal. If a
dynamic item disappears, host focus memory selects the focusable control nearest
its prior tree position and the Scroll container reveals it. Runtime
replacement clears both focus and scroll state.

Scroll containers may start an input scope and declare scope shortcuts exactly
like Stack/Row. GBSS can target their `scroll` role or a stable ID/class. The
axis is semantic and cannot be changed by GBSS; this prevents a theme from
breaking controller navigation. Non-finite, negative, or excessive internal
offsets are clamped by the native layout engine, and an unknown/missing axis is
rejected before publication.

## Per-view surface hints

`WidgetView.Surface` describes the useful shape of the *current view* without
requesting a window size. The host remains authoritative:

```csharp
return new WidgetView(
    root,
    InitialFocusId: "master-mute",
    Surface: new WidgetSurfaceHints
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 560,
        PreferredHeight = 420,
        MinimumWidth = 360,
        MinimumHeight = 260,
    });
```

Modes are `Adaptive`, `Compact`, `Standard`, and `Wide`. Explicit preferred and
minimum dimensions are optional logical-DIP pairs: specify both width and
height or neither. Accepted widths are 240–1600 DIPs and heights are 180–1200
DIPs; values must be finite, and a minimum cannot exceed its preferred value.
These bounds protect placement math, not the monitor: the shell may render
smaller than a requested minimum when the current work area or accessibility
scale leaves no alternative.

Suggested host defaults are 560×420 for Compact, 880×520 for Standard, and
1120×620 for Wide. Adaptive asks the shell to select from content/shell policy.
Explicit values refine the selected mode but do not bypass work-area, DPI,
text-scale, tray/footer, or minimum-control-size constraints. Widgets must
still reflow and use Scroll for overflow after the host clamps the surface.

Image fit is `Contain`, `Cover` (default), or `Fill`. Image sources must be
absolute HTTPS URLs with a host and no embedded credentials. Redirect,
download-size, decode-size, MIME, and cache policy are enforced by the host
image service; widgets never receive native image handles.

The closed `WidgetGlyph` set is `Music`, `Play`, `Pause`, `Previous`, `Next`,
`Refresh`, `Shuffle`, `Like`, `Dislike`, `Repeat`, `Settings`, `Warning`,
`Check`, and `Connection`.

The same closed glyphs can decorate a button without turning the button into
an arbitrary drawing surface:

```csharp
UI.Button("Play", "toggle", "play")
    .Icon(WidgetGlyph.Play, "Play or pause")
    .Shortcut(ControllerButton.X)
```

`ButtonElement.Icon(glyph, accessibilityLabel?)` keeps the visible text and
action semantics. Supplying an accessibility label replaces the button's
optional explicit label; otherwise the visible button text remains its name.
Glyphs are valid only on `icon` and `button` nodes.

## Controller-ready setting composites

`ToggleButton` and `Stepper` are SDK composition helpers, not new protocol node
kinds. They produce the same bounded semantic nodes as hand-authored UI, so
focus, validation, GBSS, accessibility, and controller dispatch do not need a
special renderer path.

```csharp
UI.Stack("appearance-settings",
    UI.ToggleButton(
        "Reduced motion",
        isOn: _reducedMotion,
        action: "toggle-reduced-motion",
        id: "reduced-motion"),
    UI.Stepper(
        "Text scale",
        $"{_textScale:P0}",
        decrementAction: "text-scale-down",
        incrementAction: "text-scale-up",
        id: "text-scale",
        canDecrement: _textScale > 0.85,
        canIncrement: _textScale < 1.50))
```

`ToggleButton` renders visible `On`/`Off` text, a matching accessibility label,
`.setting-toggle`, and selected state while on. The widget still owns the
value: handle its action, update state, and call `Invalidate()`.

`Stepper` creates stable child IDs by appending `.label`, `.decrement`,
`.value`, and `.increment` to its base ID. Keep the resulting IDs within the
128-character protocol limit and never change the base ID when the displayed
value changes. The two buttons expose independent action IDs, explicit
left/right focus neighbors, accessible `Decrease <label>` / `Increase <label>`
names, and disabled state at a bound. Its semantic classes are:

- `.setting-stepper` on the row;
- `.setting-stepper-label` and `.setting-stepper-value` on text;
- `.setting-stepper-button` on both buttons; and
- `.setting-stepper-decrement` / `.setting-stepper-increment` on the respective
  action.

The helper does not parse, clamp, persist, or mutate the displayed value.
Validate the domain in widget/host logic, then rebuild the composite from that
authoritative value. A remains the activation button; D-pad and analog focus
navigation continue through the normal host routing.

The modern component helpers and their stable `gbar-*` class contracts are
documented in [Controller UI component patterns](controller-ui-components.md).
Use `.AddClasses(...)` to augment those semantic classes. `.Classes(...)`
deliberately replaces the complete class list and is intended for primitives
or authors who explicitly take over the component contract.

## Controller-native Slider (protocol v3)

Use `UI.Slider` for a value the controller can change directly. `Progress` is
read-only and `Stepper` creates two focus stops; neither should be repurposed as
a draggable or controller-adjustable track.

```csharp
var volume = UI.Slider(
        value: _volume,
        minimum: 0,
        maximum: 1,
        step: 0.05,
        valueChangedAction: "player.volume.set",
        id: "player.volume",
        accessibilityLabel: "Player volume. Press A to mute",
        accessibilityValue: $"{Math.Round(_volume * 100)}%",
        activationAction: "player.mute.toggle")
    .FocusUp("player.output")
    .FocusDown("player.balance")
    .Classes("volume-slider");
```

While the Slider is focused:

- D-pad or left-stick Left/Right belongs to the Slider and adjusts by one
  minimum-anchored step. It never escapes into horizontal focus navigation,
  including at a range bound or while the control is unavailable.
- Up/Down remains normal focus navigation. A Slider may declare only
  `.FocusUp(...)` and `.FocusDown(...)`; horizontal focus neighbors are
  rejected.
- A invokes the optional `activationAction`. Without one, A is unhandled by
  the Slider. This lets one focus stop expose a related discrete action such
  as mute without adding an adjacent controller target.

The host clamps and quantizes a transient presentation target, repaints it
immediately, and sends that **absolute** target as
`WidgetActionEvent.RequestedValue`. Authors must assign the requested value;
do not increment the last widget value again:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId == "player.volume.set" &&
        action.RequestedValue is { } requested)
    {
        _volume = requested; // already an absolute, quantized target
        Invalidate();        // publish optimistic feedback before slow I/O
        await _player.SetVolumeAsync(requested, cancellationToken);
    }
}
```

Ranges require finite `minimum < maximum`, an in-range value, and
`0 < step <= maximum - minimum`. The accessibility label and localized,
human-readable accessibility value are required. The SDK rejects a stale
snapshot sequence, wrong input scope, wrong focus ID, non-finite/out-of-range
target, a target off the declared step grid, or a value moving opposite the
reported direction.

Repeated input can arrive faster than a provider round trip. The SDK serializes
controller actions and replaces only a contiguous pending tail for the exact
same `(input scope, Slider ID, value-changed action ID)` with its newest
absolute target. It never coalesces across a button action or another Slider,
so action ordering is preserved. Leaving the active Visible/Interactive
lifetime cancels the running action and discards pending values. Provider work
must honor the supplied cancellation token and reconcile later authoritative
events without letting stale data overwrite the pending intent.

## Stable IDs and limits

Every node requires a unique stable ID containing only ASCII letters, digits,
`.`, `-`, and `_`, up to 128 characters. IDs connect focus, styling,
accessibility, action sources, and host persistence; changing one is a state
migration.

The current snapshot limits include:

- protocol versions 1–3. Plain Stack/Row views remain v1; Scroll or explicit
  surface hints opt that snapshot into v2; a view containing Slider opts into
  v3. These additive snapshot features do not change package host API 1;
- at most 2,048 nodes;
- at most 32 levels of tree depth;
- strings up to 4,096 characters; and
- at most three dashboard quick actions.

Collections cannot be null. Unknown JSON members, unsupported enum values,
duplicate IDs, unsafe focus targets, and malformed node-specific properties
are rejected before publication.

## Focus and actions

Buttons and Sliders are focusable elements. Buttons use `.FocusUp(id)`,
`.FocusDown(id)`, `.FocusLeft(id)`, and `.FocusRight(id)` when automatic spatial
navigation would be ambiguous. Sliders accept only Up/Down neighbors because
they own Left/Right adjustment. `WidgetView.InitialFocusId` must name a
focusable node.

The host tries an explicit focusable neighbor first. Disabled and busy controls
remain focusable, so an asynchronous state change cannot teleport focus. If no
neighbor was declared, the native renderer falls back to geometry from the
last render. It favors a target
whose perpendicular span overlaps the current button, then forward distance,
lateral distance, and stable ID. It does not wrap focus at an edge.

```csharp
return new WidgetView(
    UI.Row("transport",
        UI.Button("Previous", "previous", "previous")
            .Icon(WidgetGlyph.Previous)
            .FocusRight("play")
            .Shortcut(ControllerButton.LeftBumper),
        UI.Button("Play", "toggle", "play")
            .Icon(WidgetGlyph.Play, "Play or pause")
            .FocusLeft("previous")
            .FocusRight("next")
            .Shortcut(ControllerButton.X),
        UI.Button("Next", "next", "next")
            .Icon(WidgetGlyph.Next)
            .FocusLeft("play")
            .Shortcut(ControllerButton.RightBumper)),
    InitialFocusId: "play");
```

A pressed A activates a focused Button through its `ActionId`, or a Slider's
optional activation action. A shortcut can use the button's action or an explicit alternate action. Input resolves
against the latest host-rendered snapshot, not an unrendered state.

Non-A shortcuts are scoped to the explicitly active input surface. Set
`WidgetView.ActiveInputScopeId` when presenting a nested window; it defaults to
the root's public scope ID. The SDK first checks the focused node, then the
active scope-root container. It never searches an arbitrary unfocused child and
does not leak to a parent or sibling scope. A node may bind a button/phase only
once; separate focused controls and separate nested scopes may reuse the same
button because exact focus and active scope disambiguate them:

```csharp
var root = UI.Stack("root",
    UI.Button("Refresh", "refresh-root", "refresh-root")
        .Shortcut(ControllerButton.Y),
    UI.Stack("dialog",
        UI.Text("Apply changes?", "dialog-title"),
        UI.Button("Confirm", "confirm", "confirm")
            .Shortcut(ControllerButton.Y))
        .InputScope("confirm-dialog")
        .Shortcut(ControllerButton.B, "close-dialog"));

return new WidgetView(
    root,
    InitialFocusId: _showDialog ? "confirm" : "refresh-root",
    ActiveInputScopeId: _showDialog ? "confirm-dialog" : "root");
```

When `confirm-dialog` is active, Y and the container's focusless B resolve only
inside that scope. The root binding cannot fire. Dashboard quick actions use
their separate bounded contract, and Guide/Home is never part of a widget input
scope. The MVP accepts only Pressed shortcuts and rejects A or D-pad shortcut
bindings because those buttons are reserved for activation and navigation.

Every snapshot contains a concrete `ActiveInputScopeId`; `WidgetView` defaults
it to the root container's `.InputScope(...)` value or root node ID. Every
open-widget controller event carries that ID and the rendered snapshot's
sequence. The SDK rejects stale sequences, a different active scope, or a focus
ID outside the published scope before it resolves an action.

The host owns current focus and remembers it per widget and scope. A widget
should keep IDs stable and publish an `InitialFocusId` for fallback, not attempt
to serialize current focus into its own state. On a new snapshot the host tries
the remembered focusable Button or Slider, then `InitialFocusId`, then the
first focusable control in the active scope. A scope with no focusable controls
may remain focusless; its Stack/Row/Scroll shortcut still resolves.

## Interaction state

Buttons support `.Disabled(condition)`, `.Selected(condition)`, and
`.Busy(condition)`. Sliders support Disabled and Busy. The states are
renderer-neutral and allow the host to provide native visual and accessibility
semantics. Disabled and busy controls remain in controller focus order, but the
default SDK router suppresses their A activation, shortcut, and value-change
actions. Selected buttons remain activatable.

Use Disabled for an action that is unavailable because of product state or
permissions. Use Busy for bounded work already in flight. Neither state means
hidden or non-navigable, and neither may trigger focus fallback. If a control
must leave navigation, remove it from the semantic tree and provide a stable
replacement focus target. A focused ID that survives a snapshot must retain
focus through ordinary disabled/busy transitions.

State properties are invalid on other node kinds. A true stateful button must
have visible text or an accessibility label; every Slider requires both an
accessible name and value.

For a network-backed toggle, update the intended value immediately, render it
with `.Selected(newValue).Busy(true)`, and call `Invalidate()` before awaiting
the remote command. Busy prevents duplicate activation while the selected state
gives immediate controller feedback. Keep that optimistic state through stale
polls until authoritative confirmation or a bounded deadline; then clear Busy.
On command failure, restore the prior selected value, clear Busy, publish a
short status, and invalidate again. Mutually exclusive toggles such as
Like/Dislike should share a busy feature so they cannot race each other.

## Dashboard quick actions

`WidgetView.QuickActions` exposes up to three bounded actions while a widget's
dashboard card is selected:

```csharp
QuickActions:
[
    new WidgetQuickAction(ControllerButton.LeftBumper, "previous", "Previous"),
    new WidgetQuickAction(ControllerButton.X, "toggle", "Play or pause"),
    new WidgetQuickAction(ControllerButton.RightBumper, "next", "Next"),
]
```

Allowed dashboard buttons are X, LB, RB, LT, RT, both stick clicks, Menu, and
View. A, B, Y, D-pad, and Guide/Home are reserved by the dashboard. See the
[controller input model](controller-input.md).

## State changes and invalidation

Handle actions in `OnActionAsync`. After changing anything visible, call the
protected `Invalidate()` method. The host receives a monotonically increasing
revision and requests a new snapshot.

Lifecycle is explicit and is not the same as worker process lifetime. The
host-authoritative states are `Created`, `Background`, `Visible`,
`Interactive`, and `Destroying`. The current native host publishes `Visible`
while a bridge widget's dashboard card is selected, `Interactive` while its
full surface is open, and `Background` when selection moves away or the overlay
hides. A launched worker remains resident in `Background` by default.

Use the protected lifecycle API for work with the matching lifetime:

```csharp
private Task _service = Task.CompletedTask;
private Task _stateWork = Task.CompletedTask;
private Task _visibleUpdates = Task.CompletedTask;

protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
{
    // Requires a declared/granted background capability in a production host.
    _service = RunBrokeredNotificationServiceAsync(widgetLifetime);
    return ValueTask.CompletedTask;
}

protected override ValueTask OnLifecycleStateChangedAsync(
    WidgetLifecycleState previous,
    WidgetLifecycleState current,
    CancellationToken stateLifetime)
{
    if (current == WidgetLifecycleState.Interactive)
        _stateWork = ObserveInteractiveSessionAsync(stateLifetime);
    return ValueTask.CompletedTask;
}

protected override ValueTask OnActivatedAsync(CancellationToken visibleLifetime)
{
    // This helper uses ActiveCancellationToken and spans Visible + Interactive.
    _visibleUpdates = RunPeriodicUpdatesWhileActiveAsync(
        TimeSpan.FromSeconds(1), RefreshPreviewAsync, tickImmediately: true);
    return ValueTask.CompletedTask;
}

protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
{
    // Widget, state, and visible-lifetime tokens are already canceled.
    await Task.WhenAll(_service, _stateWork, _visibleUpdates)
        .WaitAsync(shutdownToken);
}
```

The example helpers treat cancellation of their supplied lifetime as normal;
real widgets must retain and observe every returned task so other failures stay
visible. `OnDestroyingAsync` is final bounded cleanup, not a place to begin new
long-running work.

`LifecycleState`, `WidgetLifetimeToken`, and `StateLifetimeToken` expose the
current state and its lifetimes. The widget token lasts until `Destroying`; the
state token is replaced on every stable-state change. The previous state token
is canceled before `OnLifecycleStateChangedAsync` receives the new state's
token. `OnCreatedAsync` runs exactly once before the automatic
`Created -> Background` callback. `OnDestroyingAsync` runs during bounded
shutdown after widget, state, and visible-lifetime tokens are canceled.

`IsActive`, `ActiveCancellationToken`, `OnActivatedAsync`, and
`OnDeactivatedAsync` remain compatibility APIs for the shared visible lifetime.
That lifetime spans both `Visible` and `Interactive`, so it does not restart as
a selected card opens or an open widget returns to its card. It starts on entry
from `Background` and cancels before the callback returning to `Background`.
Use `StateLifetimeToken` when work must be exclusive to `Visible` or
`Interactive`.

A widget granted a legitimate background capability may intentionally run
widget-lifetime work in `Background`; do not bind that work to the visible token
or assume `Background` unloads the worker. Conversely, ordinary refresh,
animation, and controller/UI polling must not escape the appropriate visible or
state lifetime. Hooks must start work and return promptly. Permission and
lifecycle-policy enforcement remain future host work.

`RunPeriodicUpdatesWhileActiveAsync` serializes callbacks, prevents overlap,
and optionally invalidates after each tick. Accepted intervals are 250 ms
through one hour; values outside that range are rejected.
`InvalidatePeriodicallyWhileActiveAsync` is the
render-only convenience form. Visible-lifetime cancellation completes ticker
tasks normally; callback failures fault them, so retain and observe returned
tasks. Keep network and device work out of `Render()` and make lifecycle
cleanup bounded.

## Styling

Use `.Classes("primary", "danger")` to replace the semantic GBSS classes on a
primitive. Use `.AddClasses("widget-accent")` for SDK composites so required
`gbar-*` hooks are preserved. Both APIs enforce the GBSS identifier grammar,
64-character class limit, 32-class node limit, and deterministic duplicate
policy before snapshot publication; the protocol independently revalidates
raw workers. The host parses no CSS; the managed bridge compiles safe GBSS and
returns typed computed values. Read the [GBSS reference](gbss.md) for selectors,
allowed properties, imports, safety limits, and renderer-state behavior.
