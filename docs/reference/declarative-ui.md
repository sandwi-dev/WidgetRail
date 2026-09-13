# UI elements

`Widget.Render()` returns a `WidgetView`. The view describes controls and their
relationships. WidgetRail turns that description into native layout, rendering,
controller navigation, and accessibility information.

## A small view

```csharp
public override WidgetView Render() => new(
    UI.Stack("root",
        UI.Text("My library", "heading"),
        UI.Button("Refresh", "refresh", "refresh-button")),
    InitialFocusId: "refresh-button");
```

`Stack` arranges children vertically. `Text` displays a label. The button invokes
the `refresh` action and has the stable element ID `refresh-button`.

## Choose an element

| Purpose | API or guide |
|---|---|
| Vertical or horizontal content | `UI.Stack`, `UI.Row` |
| Content larger than its visible area | `UI.Scroll`; [collections](collections.md) |
| Text and an action | `UI.Text`, `UI.Button` |
| A bounded numeric value | `UI.Slider`; [controller components](controller-ui-components.md) |
| An image or semantic icon | [Visual content](visual-content.md) |
| A clickable card or poster | `UI.ActionSurface`, `UI.PosterTile` |
| A transient message | `UI.Toast` |
| A responsive game or media grid | [Collections](collections.md) |
| A live application preview | `UI.WindowPreview` |

Prefer an existing component over assembling a new interaction pattern. A
poster owns one action; its image and text are presentation content, not extra
focus stops.

## Identity and updates

Give elements stable IDs. For data items, derive the ID from the item identity
rather than its changing index. A new view can then preserve focus on the same
control even when text, artwork, or another row changes.

After state changes, call `Invalidate()` or use a helper that invalidates for
you. Do not fetch data inside `Render()`. A render request may be caused by
something unrelated to data freshness.

Disabled, busy, selected, and focused are separate states. A disabled control
can remain focusable so the user can discover why its action is unavailable.
Use visible explanatory text rather than color alone.

## Layout and appearance

The host places the widget in the available viewport. Use containers and styles
that can adapt to different widths and text scales. See [Display and layout](display-and-resolution.md).

WRSS styles control the native appearance. This is not an HTML DOM: browser CSS,
arbitrary drawing code, and inline scripts are not part of an ordinary view.
Package SVG icons and embedded media each have a separate supported contract.

For action routing, see [Controller input](controller-input.md). For screen-reader
semantics, see [Accessibility](accessibility.md). Exact element signatures and
validation live in [`UI.cs`](../../src/WidgetSdk/UI.cs),
[`Elements.cs`](../../src/WidgetSdk/Elements.cs), and
[`WidgetProtocol`](../../src/WidgetProtocol/).
