# Focus presentation retention

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
