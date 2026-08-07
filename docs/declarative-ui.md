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
| `UI.Text(text, id, accessibilityLabel?)` | `text` | Non-interactive text. |
| `UI.Button(label, action, id)` | `button` | Focusable action control; may include a semantic glyph. |
| `UI.Progress(value, maximum, id, accessibilityLabel?)` | `progress` | Bounded progress where `0 <= value <= maximum` and `maximum > 0`. |
| `UI.Spacer(id)` | `spacer` | Layout spacing node. |
| `UI.Image(httpsSource, id, accessibilityLabel, fit?)` | `image` | HTTPS image with required accessible alternative text. |
| `UI.Icon(glyph, id, accessibilityLabel)` | `icon` | Host-rendered semantic vector icon from a closed enum. |

Stack and Row containers may call `.InputScope("scope-id")` to start a nested
controller input surface. The root is always the default input scope, so a
simple widget does not need to declare one. Those containers may also call
`.Shortcut(button, actionId)` for a surface-level action that must work without
focused content, such as B to dismiss a modal.

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

## Stable IDs and limits

Every node requires a unique stable ID containing only ASCII letters, digits,
`.`, `-`, and `_`, up to 128 characters. IDs connect focus, styling,
accessibility, action sources, and host persistence; changing one is a state
migration.

The current snapshot limits include:

- protocol version 1;
- at most 2,048 nodes;
- at most 32 levels of tree depth;
- strings up to 4,096 characters; and
- at most three dashboard quick actions.

Collections cannot be null. Unknown JSON members, unsupported enum values,
duplicate IDs, unsafe focus targets, and malformed node-specific properties
are rejected before publication.

## Focus and actions

Buttons are the current focusable element. Use `.FocusUp(id)`,
`.FocusDown(id)`, `.FocusLeft(id)`, and `.FocusRight(id)` when automatic spatial
navigation would be ambiguous. `WidgetView.InitialFocusId` must name a
focusable node.

The host tries an enabled explicit neighbor first. If the declared target is
unavailable because it is disabled or busy, or no neighbor was declared, the
native renderer falls back to geometry from the last render. It favors a target
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

A pressed A activates the focused button through its `ActionId`. A shortcut
can use the button's action or an explicit alternate action. Input resolves
against the latest host-rendered snapshot, not an unrendered state.

Non-A shortcuts are scoped to the explicitly active input surface. Set
`WidgetView.ActiveInputScopeId` when presenting a nested window; it defaults to
the root's public scope ID. The SDK first checks the focused node, then searches
only that published scope. It does not leak to a parent or sibling scope.
Bindings are unique by button and event phase inside one scope, while separate
nested scopes may reuse them:

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
the remembered enabled button, then `InitialFocusId`, then the first enabled
button in the active scope. A scope with no enabled buttons may remain focusless;
its Stack/Row shortcut still resolves.

## Interaction state

Buttons support `.Disabled(condition)`, `.Selected(condition)`, and
`.Busy(condition)`. The states are renderer-neutral and allow the host to
provide native visual and accessibility semantics. Disabled and busy buttons
remain present but the default SDK routing will not activate them with A or a
declared shortcut. Selected buttons remain activatable.

State properties are invalid on non-buttons. A true stateful button must have
visible text or an accessibility label.

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

Allowed dashboard buttons are B, X, LB, RB, LT, RT, both stick clicks, Menu,
and View. A, Y, D-pad, and Guide/Home are reserved by the dashboard. See the
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

Use `.Classes("primary", "danger")` to attach semantic GBSS classes. The host
parses no CSS; the managed bridge compiles safe GBSS and returns typed computed
values. Read the [GBSS reference](gbss.md) for selectors, allowed properties,
imports, safety limits, and the current renderer-state caveats.
