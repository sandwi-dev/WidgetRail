# Scrolling and paged collections

A scroll container describes the visible area of a list. A collection resource
owns loading, errors and the items available to render. Choose the resource
according to both the provider's API and how people will browse the list.

## Choose a loading model

| Collection | Suitable starting point |
|---|---|
| Small or already-loaded list | A normal scroll container; slice the list locally if you want pages |
| One page at a time, backed by offset/limit requests and a known total | `WidgetPagedResource<TItem>` |
| Continuous browsing through a large library | `WidgetCursorResource<TItem>` |

Both resource types are supported. CursorResource does not replace all of
PagedResource's behavior, and an offset-based provider does not require a paged
UI: an adapter can encode offsets as cursors for continuous browsing.

## One page at a time

`WidgetPagedResource<TItem>` loads a `WidgetPage<TItem>` containing items,
offset, limit and total count. Moving Next or Previous replaces the displayed
page. There is no built-in jump-to-page-number operation.

It keeps a bounded cache of pages, with configurable expiry and eviction of the
least recently used pages. It also handles retries, cancellation, stale results
and focus placement when entering a page. `RetainLastGoodPage` controls whether
the previous page remains available after a loading error.

Use `CreatePagedResource`, render `Snapshot.Page`, and pass your scroll through
`Paginate(scroll)`. Route pagination actions through `TryHandlePagination`, or
call `Move` for an explicit Next/Previous control. An opaque-cursor provider with
no known total is not a direct match for this offset-based contract.

## Continuous browsing

`WidgetCursorResource<TItem>` combines adjacent pages into one retained window.
Items have stable keys; before/after cursors identify what to fetch next. A total
count is optional. Capture the resource once while building a view, then use
that capture's `PresentItem` and `Present` methods for its rows and scroll.

The resource retains nearby pages rather than maintaining a separate expiring
page cache. Evicted pages need another load if the user returns to them. A
provider adapter can supply additional caching when needed.

Use stable item IDs and preserve the provider's ordering through one traversal.
Sorting just the currently loaded page can duplicate or reorder entries when
another page arrives.

## Rendering and controller scrolling contract

The collection resource owns data and loading. The host owns layout, clipping,
pixel reuse, navigation and continuous scrolling. There is no widget animation
timer or per-frame scroll callback to implement.

- Capture the cursor resource once per view and use that capture for both its
  item presentation and scroll metadata. Give each item its provider-stable key;
  do not use its current row/page index as identity.
- Keep the scroll container, content and item IDs stable when adjacent pages
  arrive. Append/evict through the cursor resource so the host receives the
  matching window change and anchor. Use refresh/reset for a changed query or
  ordering, not as a notification that another page loaded.
- Use regular rows or `ResponsiveGrid` with theme-defined sizing/aspect ratios
  for large libraries. Clipped tiles permit the host to skip invisible visual
  subtrees. Visible-overflow and unusual nested layouts retain the conservative
  rendering path; clipping is not permission to discard focus or accessibility
  semantics.
- Keep existing items interactive during adjacent loading. The host can hold
  movement at the loaded boundary and continue after the new window is admitted.
  It does not replay a queue of navigation commands against the arriving page.
  Movement is coalesced once per paint; repeated input while blocked does not
  accumulate a distance that will jump through the new page.
- The Controllers setting **Hold D-pad to scroll** is optional and off by default.
  A tap retains normal focus navigation. A vertical hold uses the current active
  scroll/input scope, then lands in the originating column where possible on release. Widgets
  use the same standard Scroll and collection contracts as right-stick scrolling;
  do not add a second D-pad repeat loop.
  Focus also settles when the loaded boundary stops movement; holding can resume
  scrolling when a new page extends the available range.
- Modal scopes, collection resets, widget replacement, and loss of input authority
  retire the old gesture. A parent page cannot continue receiving held navigation
  through its modal.

`ShowScrollbar` controls the indicator and its reserved gutter together. It does
not turn off scrolling, cursor loading, or host rendering optimizations.

Pixel retention is an implementation detail rather than an SDK promise about a
specific number of render calls. Authors should produce immutable view snapshots
and preserve the identities above; changing theme, text size, content or clipping
must remain free to repaint or relayout.

## What triggers more cursor data?

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

The two resources have different refresh behavior:

| Operation | PagedResource | CursorResource |
|---|---|---|
| `Refresh()` | Reloads the current page's offset, bypassing its cache | Reloads from the beginning |
| `EnsureLoaded()` | Reuses a ready page if its cache entry is fresh; otherwise loads from offset zero | Reuses ready data until explicitly refreshed or reset |
| `Reset()` | Cancels work and clears the page and page cache | Cancels work and clears the retained window |

After `Reset()`, neither resource fetches until another load is requested.
Loading an adjacent page continues the existing traversal.

For cursor collections, a successful refresh publishes a new reset generation.
The host uses that signal to reset scroll position and remembered item focus.
Adjacent loads retain that reset generation. Keep the scroll container ID stable;
changing its name to force a reset is unnecessary.

When old cursor pages leave the retained window, the host compensates the scroll
offset so the remaining content does not jump. The retention target may be exceeded to
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
