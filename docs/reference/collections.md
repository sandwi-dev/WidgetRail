# Scrolling and collections

Widgets describe data and item identity; WinUI owns realization, layout, scrolling,
focus and accessibility. Use [indexed and discovered collections](../developers/indexed-collections.md)
for the complete authoring contract and [WinUI authoring](winui-authoring.md)
for native control mappings.

## Choose a presentation model

| Data | Presentation |
| --- | --- |
| Small, static or heterogeneous content | `UI.VerticalScroll`, `UI.HorizontalScroll` or `UI.ResponsiveGrid` |
| Immutable query with a known count and range access | `CreateIndexedCollection` with `UI.CollectionList` or `UI.CollectionGrid` |
| Remote results with an opaque next token and no known total | `CreateDiscoveredCollection` with the same list/grid controls |
| Explicit Next/Previous pages | Provider resource plus ordinary page controls, publishing the current page as a captured query |

A native list/grid must have a finite viewport. Put it in a bounded surface or a
star Grid track; do not nest it in an unbounded scroll container. Headers, filters
and page actions belong outside the virtualized item collection.

## Data and identity

A query freezes membership, ordering and all values needed by item rendering.
`ReadRange` returns the requested range and honors cancellation. `RenderItem`
creates one action surface using `context.Id(...)` for stable child IDs. Actions
and artwork resolve against the captured query/item, never an index into a mutable
viewport buffer.

Use `PublishQuery` when membership, ordering or count changes. Use `UpdateContent`
when keys remain at the same positions and only content changes. For discovered
sources, `Count` is the admitted prefix; `Discovery.HasMore` indicates more results
may arrive. Do not fabricate a remote total. See the detailed guide for duplicate
keys, retries, bounded discovery and query retirement.

UI virtualization and data retention are separate. WinUI creates controls only
for realized items, but the provider or captured query may retain already loaded
data. Choose provider caching and source limits deliberately; virtualization does
not by itself bound the size of a provider's in-memory result.

`WidgetPagedResource` and `WidgetCursorResource` remain useful provider-side data
helpers. Their legacy `Paginate`/`Present` scroll metadata and eager
`CollectionList`/`CollectionGrid` overloads are not WinUI presentation contracts.
Publish retained data through an indexed/discovered source instead. The shared
`WinUiPresentationContract`, `WidgetTestHost.ValidateWinUiPresentation` and
`wrail render` report unsupported declarations with replacement guidance.

## Focus, input and artwork

Keep the collection ID and occurrence keys stable across content updates. Use
`IndexedCollectionFocusTarget` for keyed entry; the host retains native scroll
position and restores remembered focus when the target is still usable. Right
stick scrolling and D-pad navigation use the host's current input scope. Widgets
must not run their own scrolling animation or repeat loop. Modal scopes and
retired queries cannot continue receiving input from the previous page.

Artwork is asynchronous and must not delay query publication. Supply stable
artwork handles and resolve them from the captured item. A pinned view creates
independent visible demand; recycled or retired rows release theirs. A later
request for the same artwork is valid. Expected missing/network/format failures
remain local to the image slot rather than replacing the whole widget.

## Examples and validation

- [Playnite Home](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedHome.cs)
  and [Library](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedBrowse.cs)
  expose captured indexed queries.
- [YouTube Video search](../../samples/YouTubeWidget/YouTubeVideoWidget.Search.cs)
  uses forward discovery.
- The [indexed collection guide](../developers/indexed-collections.md) explains
  SDK range leases, content updates, action/artwork authority and test fixtures.

Validate the parent declaration and separately validate acquired row roots.
Parent preflight does not eagerly request rows. Use fake providers for stateful
workflows, then check actual WinUI geometry, controller focus and visible loading
behavior. Passing protocol checks alone does not establish smooth scrolling.
