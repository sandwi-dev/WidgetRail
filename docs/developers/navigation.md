# Navigation and scrolling

A controller interface needs a clear answer to two questions: which control is
focused, and what happens when the user moves or activates it?

For a multi-section layout, start with
[NavigationShell](../reference/presentation-composition.md). It composes compact
tabs and an expanded rail around shared content while your widget owns route state.

## Let controls do the ordinary work

Start with SDK buttons, sliders, menus, and collection components. They already
participate in controller navigation. Handle a button's action ID instead of
writing a raw A-button handler for every control.

The host normally uses layout geometry to choose the next focus target. For a
horizontal rail, movement first considers items in that rail, including items
that need scrolling into view. Explicit navigation links can express a deliberate
relationship when the layout alone is ambiguous.

## Keep control identities stable

For a list of games, use IDs derived from the game identity, such as
`game.<stable-id>`. When a loading indicator appears or another page arrives,
the host can still recognize the focused game.

Avoid `game.0`, `game.1`, and so on if sorting or filtering can move items.
Also avoid giving the same ID to unrelated controls on different pages.

`InitialFocusId` suggests where to begin. It should not become a request to move
focus back to your header every time data changes. A loading update should leave
a still-valid focused control alone.

## Actions and shortcuts

```csharp
UI.Button("Refresh", "refresh", "refresh-button")
    .Shortcut(ControllerButton.Y)
```

A shortcut is another way to invoke an action. Keep it consistent with the
control's meaning. The controller guide can expose actions for the focused
control and its scope, so users can discover them as they navigate.

Use a menu or nested page for secondary actions. A Back action should return
through the widget's own pages before returning to the overlay tray.

## Use the shared scrolling behavior

D-pad and left-stick navigation move focus and reveal the target control.
Right-stick scrolling moves the viewport; focus settles after scrolling stops
instead of jumping with every arriving page.

Overflowing vertical containers also show a thin scroll indicator by default.
Use `with { ShowScrollbar = false }` on a scroll element to hide it for that
container and release its reserved gutter. Scrolling and focus-follow remain enabled.
See [Scroll indicators](../reference/declarative-ui.md#scroll-indicators).

For long lists and responsive grids, use the cursor resource and matching
collection metadata. The host can ask for adjacent pages near an edge or when
loaded items do not yet fill the viewport. A responsive grid may need more
items after a resize, so a fixed item threshold is not a substitute for viewport demand.

The shared collection can show a loading indicator while fetching. Page eviction
must not remove the visible range just to meet a preferred retention count.
See [Data and lifecycle](data-and-lifecycle.md) before building a custom paging loop.

For API signatures and detailed targeting rules, see
[Controller components](../reference/controller-ui-components.md),
[Controller input](../reference/controller-input.md), and
[UI elements](../reference/declarative-ui.md).
