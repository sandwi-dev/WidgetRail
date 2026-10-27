# Pinned layout handles

A pinned layout is a widget-authored view that can remain visible outside the
main overlay. The host owns the window, placement controls, and rendering.

While the main overlay is visible, passive pins stay behind it. Interacting with
a pin or adjusting its position, size or opacity raises it above the main layer.
Adjustment input belongs to the host; it does not activate the pinned widget's
controls. Saving or cancelling restores the normal passive ordering.

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

## Open a pin from a widget action

With protocol 103 or later, a widget can request one of its own currently published
layouts through the intrinsic pinning service. No widget permission is required:

```csharp
// Inside the admitted OnActionAsync handler, after publishing this layout.
WidgetPinReceipt receipt = await HostServices.Pinning.RequestOpenAsync(
    streamLayout, cancellationToken);
```

`RequestOpenAsync` accepts either a `PinnedLayoutHandle` or its authored string ID
and returns `ValueTask<WidgetPinReceipt>`. IDs may contain ASCII letters, digits,
dots, underscores and hyphens, with a maximum length of 128. The `host.` prefix is
reserved: this API cannot request full-widget pinning, another widget's pin or a
window/focus target. The widget must declare `pinningSupported` and publish the
layout in its current view.

Await the request within the original action handler. Moving it to a background
task or firing it from polling loses user-action authority. The host checks the
originating widget, scope and current declaration before opening the passive pin;
it owns replacement dialogs, geometry, focus and saved dimensions per layout.

`receipt.Accepted` reports admission, not proof that a pin is visible. A stale
layout can be rejected later and the user can cancel replacement. Use
`IsSelected` and `SelectionCancellationToken` to acquire resources or start a
receiver; do not start playback simply because the receipt was accepted. Normal
capability errors and cancellation still apply.

## Controls and artwork in a pinned root

Use the same declarative controls and action handlers as the main widget:
buttons, sliders, Select options, context actions and text entries. Give the
pinned root its own stable input scope. `OnActionAsync` receives that scope and
the original source/action IDs; a context action keeps its Menu/X/Y opener,
while text commits carry `CommittedText`. Keep action IDs meaningful across
updates and respect the existing disabled/busy and maximum-text-length rules.

Use `UI.Artwork` and the normal `OnResolveArtworkAsync` provider for images
that appear only in this root, including focus artwork. Publish a new handle
when the pixels change. The host associates each request with the selected
layout and discards replies after that selection or declaration retires.

No custom pinned transport API is needed in widget code. Rebuild packaged
application workers with the current runtime to enable pinned context/text
commits; older workers cannot handle that new input route. A legacy layout
with sizing hints but no root does not provide independent ordinary input.
Host window/media support remains a separate frontend responsibility.

## Learn from a template

The `media` template contains compact and detailed layouts. The `embedded-media`
template demonstrates a player session with host-owned presentation.
See [Media and pinning](../developers/media-and-pinning.md) for the broader design.

Source: [`PinnedLayoutHandle.cs`](../../src/WidgetSdk/PinnedLayoutHandle.cs) and
[`Widget.cs`](../../src/WidgetSdk/Widget.cs). Direct requests use
[`Pinning.cs`](../../src/WidgetSdk/Pinning.cs) and
[`WidgetPinRequest.cs`](../../src/WidgetProtocol/WidgetPinRequest.cs).
