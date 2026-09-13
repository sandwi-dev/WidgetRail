# Scrolling and paged collections

A scroll container describes the visible area of a list. A cursor resource
describes which items are loaded and how to fetch more. Use both when a remote
library is too large to load at once.

## Choose a loading model

| Collection | Suitable starting point |
|---|---|
| Small local list | A normal scroll container |
| Explicit numbered or offset pages | `WidgetPagedResource<TItem>` |
| Continuous browsing through a large library | `WidgetCursorResource<TItem>` |

Use stable item IDs and preserve the provider's ordering through one traversal.
Sorting just the currently loaded page can duplicate or reorder entries when
another page arrives.

## What triggers another page?

The host uses viewport and navigation demand. It can request adjacent data near
an edge, when navigation reaches the loaded boundary, or when available items
do not yet fill the visible area. A responsive grid may need more data after
its width or column count changes.

Declare the shared collection metadata so the host knows which resource and
scroll container belong together. A custom fixed threshold such as “fetch at
item 20” cannot describe all viewport sizes.

The shared loading indicator appears at the edge being fetched. Keep existing
items usable while waiting. Do not add a focusable loading button just to reveal
that an automatic request is in flight.

## Refresh, reset, and position

`Refresh()` requests a fresh collection from the beginning. `Reset()` clears the
resource state; the next load also begins fresh. Loading an adjacent page is
neither of these: it continues the existing collection.

A collection generation distinguishes fresh data from ordinary page updates.
The host uses that signal to reset the appropriate position. Keep the scroll
container ID stable; changing its name to force a reset is unnecessary.

When old pages leave the retained window, the host compensates the scroll offset
so the remaining content does not jump. The retention target may be exceeded to
protect pages that are still visible. A hard maximum is a separate resource bound.

## Focus while scrolling

D-pad navigation reveals the focused item. Right-stick scrolling moves the
viewport and settles focus when scrolling stops. Loading pages should not
repeatedly pull focus to a header or to the first item.

An item disappearing after a filter change is different from a loading update.
Choose a useful fallback when the original item no longer belongs to the collection.

For option names and provider request shapes, see
[`WidgetCursorResource.cs`](../../src/WidgetSdk/WidgetCursorResource.cs) and
[`WidgetPagedResource.cs`](../../src/WidgetSdk/WidgetPagedResource.cs).
The [Playnite example](../../samples/PlayniteLibraryWidget/README.md) demonstrates
both a horizontal rail and a responsive Browse grid.
