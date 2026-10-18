# Focus presentation retention

## Interactive anchored inspection

Use the existing `WidgetModal` for explicit inspection with controls. Its optional
`AnchoredPresentation` places the panel beside an element or an exact indexed item:

```csharp
view.WithModal(new WidgetModal("inspect", "Details", details,
    "inspect.confirm", "inspect.close")
{
    AnchoredPresentation = new(new PresentationAnchor(ElementId: "candidate.inspect")),
});
```

The default requested size is 360 × 280 logical DIPs. Width accepts 160–960 and
height 96–800; the host clamps both to the widget viewport. `Placement` can prefer
Right, Left, Below or Above; Auto starts on the right. The host tries other sides
when needed. Missing, offscreen or stale anchors use a bounded centered panel.
An indexed anchor uses `IndexedCollectionFocusTarget`, including its source,
query generation, occurrence key and index. Resolving it never fetches pages,
scrolls the list or selects an item.

This remains a modal: background controls cannot receive input, B/Close follows
the existing dismiss action, and focus returns to the remembered opener or a valid
fallback. It does not assign a new global Inspect shortcut. Authors expose an
ordinary action with a meaningful label, preserve its identity, and publish the
background view again on dismissal. Ignore asynchronous results whose inspection
request or widget lifetime has ended; a late result must not publish a new modal.

Anchored inspection requires presentation protocol 92. Omitting the option keeps
the existing centered modal and its existing protocol requirements.

## Inline focus presentations

`BackgroundSurfaceElement` and `FocusPresentationSurfaceElement` share a native,
per-surface source resolver. Set `RetainLastPresentation` in C# to choose whether a
surface keeps its last valid focused source when focus moves elsewhere:

```csharp
UI.FocusPresentationSurface(content, defaultPresentation, "game-summary")
    with { RetainLastPresentation = true };
```

The default policies are: focused background artwork retains its
source; focus-presentation content uses the default when focus leaves it. Explicit
policy declarations require presentation protocol 56. Playnite enables retention
for its Home summary, matching its focused background artwork.

- A focused item with a declaration replaces that surface's selection.
- Tray, modal, and unrelated controls without a declaration retain the last source
  when enabled. Nested surfaces remain independent ownership boundaries.
- The host resolves the source from the current view, rather than copying old text
  or artwork declarations. Refreshed content is reflected immediately.
- A missing source, removed declaration, recycled item key, hidden responsive
  branch, or removed surface falls back to the current authored default. There is
  no resurrection when an evicted source later returns without receiving focus.
- Leaving the viewport does not invalidate an item still present in the view tree.
  Cursor overlap preserves identity; cursor eviction or filtering out the item
  clears it. Retention never fetches pages, changes scroll offsets, or restores a
  cursor position.
- Runtime/presentation authority changes and widget retirement cannot inherit old
  selections. Theme, scale, and render-target changes do not themselves change a
  valid semantic selection.

Retained presentation is view-only. It grants no input, action, focus, or navigation
authority. Measurement, painting, accessibility projection, image protection, and
incremental invalidation use the same selection. Compositor and fallback image
paths consume that selection; their bounded decode caches and crossfade textures
remain separate rendering concerns.

## WinUI background continuity

A retaining background keeps its last decoded image until another image is ready
for that same declared surface. Losing a source still retires its semantic selection,
lease and pending requests, as described above. An empty default, missing artwork
or a pending replacement does not clear the already painted background. A nonempty
authored default can replace it after decoding. Focus fragments continue to resolve
the current default; this pixel-retention rule applies only to backgrounds.

Surface identity is scoped to the widget/runtime/presentation owner, declared input
scope, element ID, collection-item ancestry and enclosing presentation surfaces.
Keep a shared background surface's ID and scope stable across sections; change its
foreground content underneath it. Playnite Home and Library declare the same
`playnite-library.cinematic` surface for this purpose. Different surface IDs are
independent, even when they occupy the same position. Nested surfaces never replace
their parent's retained image. Opening a modal with a separate input scope preserves
the declared parent surface; retiring the parent does not transfer its image to the
modal. Removing a surface or replacing its authority releases its retained image.

Set `RetainLastPresentation = false` when an empty selection should clear background
pixels. This is a declaration policy, not a global last-background cache; no input,
focus, data-fetch or navigation authority survives solely because pixels remain.

When a navigator supplies a different input scope per page, apply that scope to
the foreground subtree **inside** a shared background, not to its enclosing root.
Keeping the element ID alone does not preserve ownership when its inherited scope
changes. Playnite uses an explicit stable `playnite-library.presentation` scope
around the cinematic surface and keeps the navigator's page scopes underneath it.
This preserves separate page focus/scroll/action authority while artwork continues.

## Anchored passive previews

Existing `PresentOnFocus` content can appear beside its source instead of taking
space in an inline inspector. Set `Preview` on its existing enclosing surface:

```csharp
UI.FocusPresentationSurface(
    UI.Button("Audio setting", "inspect-audio", "audio")
        .PresentOnFocus(UI.Text("Current output and volume", "audio.preview")),
    UI.Text("", "preview.empty"),
    "settings.preview") with
{
    Preview = new FocusPreviewOptions(Width: 360, Height: 240)
}
```

The default is settled focus and mouse hover after 350 ms. `OnFocus` and `OnHover`
can independently disable either source; at least one must remain enabled.
`DelayMilliseconds` accepts 0–1500, width 160–960 DIP and height 96–800 DIP. The
host can shrink and flip the panel to keep it within the widget. Long content is
clipped; put essential information and controls behind an explicit inspect action
using a normal or anchored modal. Passive fragments retain their existing 64-node,
8-level limits and cannot contain focusable controls, actions or scrolling.

Preview activation does not invoke the widget or calculate data. Reuse content
already loaded for the source. Updating that content does not restart the settling
deadline. Only one preview is visible; the nearest enclosing surface owns it.
Pointer hover does not select a row or move focus. Controller input resumes the
focused source, including at a navigation boundary.

Anchored previews deliberately do not support `RetainLastPresentation`: they hide
when their source is removed, leaves the viewport or loses current occurrence
authority. Suspension, input handoff, modal entry and unloading revoke them and
cancel their pending deadline. Indexed previews inspect resident rows only, without
fetching, retaining or scrolling. They share placement geometry and scaled host
presentation layers with interactive inspection, but never take focus or pointer
input. Leaving `Preview` unset preserves existing inline presentation behaviour.
