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

## Native list and grid realization

For regular collections, `UI.CollectionList` and `UI.CollectionGrid` let the host
measure and prepare visible items plus a buffer. They require protocol 59. The
provider still owns loading and the widget still submits its bounded retained
item window; realization does not remove the protocol's item or tree limits.

```csharp
WidgetElement[] items = Enumerable.Range(0, 4)
    .Select(i => UI.Button($"Song {i}", $"play-{i}", $"song-{i}")
        .CollectionItem(new WidgetCollectionItemKey($"key-{i}"))).ToArray();
var list = UI.CollectionList("songs", estimatedItemExtent: 72, items: items);
var grid = UI.CollectionGrid("games", minimumColumnWidth: 180,
    estimatedItemExtent: 270, maximumColumns: 6, items: items);
```

Use one of these containers in a view. Each direct child must be a keyed Button
or ActionSurface representing one item, with a unique stable key and no nested
focusable controls or per-item responsive visibility. Poster tiles and ordinary
tiles qualify. Keep headers, filters, page buttons and empty-state copy outside
the realized collection, or use an ordinary Scroll for that heterogeneous layout.
`CollectionList` also accepts `ScrollAxis.Horizontal`; adaptive grids are vertical.

The extent is an initial main-axis estimate in DIPs, not a fixed item size. Text
wrapping, item styles and grid column constraints determine measured size. The
host maintains estimated total extent, corrects measurements and preserves keyed
anchors. Style item contents normally; collection-level flex justification does
not redistribute rows. Grid estimates exclude the row gap.

For a cursor resource, capture once, apply `capture.PresentItem(item, element)`
to each row **before** constructing the collection, then pass that collection to
`capture.Present`. This preserves window, reset, anchor and pagination authority.
Do not replace those fields using a second resource capture or a guessed index.
Offscreen logical items remain navigable and accessible; widgets need no manual
realization or scrolling callbacks.

For expensive item declarations, retain one `WidgetCollectionItems<TItem>` on the
widget (or one per retained section). Its factory receives an immutable input
containing every render dependency, including artwork, selection, availability
and action/menu state. Input equality controls reuse; a key alone is insufficient.
The factory must not read changing widget fields. Mutable inputs require `Clear`
before capture. A capture retains only its current window, keeps earlier returned
declarations immutable, and leaves the old cache intact if a factory fails. It
does not fetch data or grant action authority.

See the production [Playnite Browse presentation](../../samples/PlayniteLibraryWidget/PlayniteLibraryPresentation.cs)
for immutable item reuse, and [Spotify cursor presentation](../../samples/SpotifyWidget/SpotifyPresentationState.cs)
for applying collection layout without replacing captured cursor metadata.

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
- Use `CollectionList` or `CollectionGrid` for regular keyed rows/tiles, with
  theme-defined sizing/aspect ratios. Ordinary Scroll/`ResponsiveGrid` remains
  available for heterogeneous or nested layouts. Clipping is not permission to
  discard logical focus or accessibility semantics.
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

## Artwork demand and retention

Keep image URLs and artwork handles stable while their content is unchanged.
Publish a new artwork revision when the underlying image changes; do not generate
new image identities for focus movement, scrolling or realization alone.

The host protects visible and submitted images, prepares a bounded amount of
adjacent artwork, and cancels unfinished requests when no realized view needs
them. A visible pinned surface has its own demand even while the main overlay is
hidden. Providers must honor their existing cancellation token where possible
and tolerate another request for the same asset when an item becomes visible
again. Already-dispatched broker work may finish; the host rejects replies for
retired demand. Cancellation does not mean that the image is unavailable.

CPU pixels, GPU copies and retained drawing layers have separate lifetimes under
a shared host retention budget. Authors do not need to duplicate artwork caches,
manage GPU storage or infer collection position from image completion. Memory
pressure may evict idle pixels without resetting cursor data, focus or scroll.

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

Keep declaring the loading edge while its pagination action is temporarily
unavailable. The host uses that state to retain directional focus in the list
until the page arrives, rather than treating it as a terminal exit to a header.

## Refresh, reset, and position

The two resources have different refresh behavior:

| Operation | PagedResource | CursorResource |
|---|---|---|
| `Refresh()` | Reloads the current page's offset, bypassing its cache | Reloads from the beginning |
| `EnsureLoaded()` | Reuses a ready page if its cache entry is fresh; otherwise loads from offset zero | Reuses ready data until explicitly refreshed or reset |
| `Reset()` | Cancels work and clears the page and page cache | Cancels work and clears the retained window |

After `Reset()`, neither resource fetches until another load is requested.
Loading an adjacent page continues the existing traversal.

The host separates provider loading from visible layout and optional lookahead.
Moving through items already in the current window does not wait for an adjacent
page to finish preparing. Keep item keys and the collection reset generation
stable during adjacent loads so valid measurements can be reused. This is a
lifecycle rule, not a guarantee that expensive item rendering fits every frame.

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
