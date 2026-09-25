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
