# Pinned layout handles

A pinned layout is a widget-authored view that can remain visible outside the
main overlay. The host owns the window, placement controls, and rendering.

## Declare support

The manifest's `pinningSupported` enables supported authored/compact layouts.
The separate `fullWidgetPinningSupported` option offers the ordinary full widget;
it defaults to false. Do not enable it if the full interface is unusable at a pinned size.

## Register a handle

Call `CreatePinnedLayoutHandle` once for each stable layout identity. It records
the ID, visible name, `WidgetSurfaceHints`, and optional initial focus and scope.
Keep the handle in widget state instead of registering it on every render.

In `Render()`, call the handle's `Present(root)` and include the result in
`WidgetView.PinnedLayouts`. A second overload lets that presentation choose its
initial focus without changing the layout's stable identity and metadata.

The lower-level `WidgetView.PinnedLayout` factory is also available. A handle
adds selection-lifetime management; it is not a separate window API.

## Work only while selected

`PinnedLayoutHandle.IsSelected` reports effective selection.
`SelectionCancellationToken` is cancelled when selection is revoked, replaced,
or the widget is destroyed. A later selection gets a new token.

Use that token for data needed only by the selected layout. Registering a compact
queue view should not eagerly fetch the entire queue while that layout is unused.

Selection demand is not a general permission to issue Windows operations or
override the widget's other capability/lifecycle rules.

## Learn from a template

The `media` template contains compact and detailed layouts. The `embedded-media`
template demonstrates a player session with host-owned presentation.
See [Media and pinning](../developers/media-and-pinning.md) for the broader design.

Source: [`PinnedLayoutHandle.cs`](../../src/WidgetSdk/PinnedLayoutHandle.cs) and
[`Widget.cs`](../../src/WidgetSdk/Widget.cs).
