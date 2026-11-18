# Scrolling and collections

Widgets describe data and item identity; WinUI owns realization, layout, scrolling,
focus and accessibility. Use [indexed and discovered collections](../developers/indexed-collections.md)
for the complete authoring contract and [WinUI authoring](winui-authoring.md)
for native control mappings.

## Choose a presentation model

| Data | Presentation |
| --- | --- |
| Small, static or heterogeneous content | `UI.VerticalScroll`, `UI.HorizontalScroll` or `UI.ResponsiveGrid` |
| Complete loaded immutable list with explicit query identity | `CreateLoadedCollection` with the same lazy list/grid controls |
| Immutable query with a known count and range access | `CreateIndexedCollection` with `UI.CollectionList` or `UI.CollectionGrid` |
| Remote results with an opaque next token and no known total | `CreateDiscoveredCollection` with the same list/grid controls |
| Explicit Next/Previous pages | Provider resource plus ordinary page controls, publishing the current page as a captured query |

A native list/grid must have a finite viewport. Put it in a bounded surface or a
star Grid track; do not nest it in an unbounded scroll container. Headers, filters
and page actions belong outside the virtualized item collection.

When the viewport shrinks while a realized row owns native focus, the shared
presenter keeps that row visible without changing focus. This is event-driven,
including when status content reduces the available space. It does not reclaim
focus after the user leaves the collection.

For a timeline, `.DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge)` on
`UI.CollectionList` makes directional movement from another control enter the
nearest currently visible row in that direction (protocol125). The host uses
native viewport geometry and its existing row readiness/entry lifetime. It does
not reset remembered focus, refresh the query or request an adjacent page. The default remains
remembered entry; explicit indexed focus targets, return requests, notifications
and navigation-cache restoration retain their existing semantics. This opt-in
does not apply to grids or ordinary reading scrolls.

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

For changed membership within the **same logical filter and sort**, an indexed
source can use `PublishRefresh(query, count)` (protocol127). It captures the prior
query lineage in the SDK. The host preserves its current reader or focused key
through the existing lazy lookup; if that key was removed, it may choose the
nearest remaining index. Later user input still cancels that recovery. Supply
`FindIndex` or complete bounded occurrence keys. This does not enumerate the
collection, transfer old action leases, or create an explicit widget focus request.
Use ordinary `PublishQuery` for a different search, filter, account or destination;
that resets refresh lineage. Use `UpdateContent` when membership and order remain
proven unchanged.

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

### Live cursor windows

An ordered, opaque-index provider can opt into `WidgetCursorResourceOptions.LiveUpdates`
with a comparison and an exact item-boundary cursor resolver. Capture `MutationScope`
for the current query/lifetime, then call `ApplyMutations` with keyed upserts/removals.
The SDK reconciles delayed pages, preserves reset/focus identity and bounds retained
items. Reset, deactivation (for an Active resource), and destruction invalidate old
mutation scopes. Known-index/total pages continue using their provider refresh path.

New tail items enter only the latest loaded window. `HasUnseenItems` reports changes
outside a retained older window or beyond its capacity. Tail eviction requires explicit
`allowTailEviction` admission; do not infer it from keyboard focus or a timer. Excess
deferred tail becomes an opaque paging boundary, not an unbounded local queue. Use
the host's viewport policy to decide whether to follow the end, and preserve the
reading position otherwise.

For authoritative gap recovery, a provider can supply `WidgetCursorBoundaryUpdate`
along with covered replacements/removals. This retires stale pending page work while
retaining current paint and a surviving anchor. An incomplete or failed remote read
does not authorize deletion of omitted items. `RequiresRefresh` fails closed when a
pending mutation journal exceeds its bound; keep the current view until recovery.
Provider event ordering, account checks, reconnect detection and network recovery
remain provider responsibilities. See `WidgetSdk/WidgetCursorResource.Live.cs`.

For a live timeline backed by `WidgetLoadedCollection`, opt into
`PreserveOccurrences` and `FollowEnd`. Supply immutable context/items and a
provider chronology through `LiveOrder` when a bounded tail may replace every
old row. The host owns native at-end detection, reading focus/preview protection
and variable-height layout reconciliation. `ViewportState` and
`OnViewportChanged` report conservative aggregate state over active views; they
are advisory, not atomic eviction authority or proof that messages were read.

If automatic tail eviction is enabled, implement `OnRecoverViewport` for the
race where the user starts reading just before a publication arrives. Perform
the bounded provider read outside the commit, then use
`request.TryCommit(history, recoveryScope, page)` to combine current-source
admission with cursor recovery and surviving-neighbor selection. A reset, account
change, newer navigation or retired source rejects stale commits. Cancellation
does not make a late provider result safe by itself. The SDK bounds concurrent
recovery callbacks even when a provider ignores cancellation.

Verified append-only publications may retain unchanged prefix row leases.
This requires the same semantic source/context, item values, indices and keys;
equal keys alone are insufficient. Edits, removal and account/filter changes
still retire obsolete row/media authority. A genuinely evicted preview can close
safely during raced recovery; preserving stale controls is not a continuity fix.

## Focus, input and artwork

For rich indexed content with no primary action, use `UI.ReadingSurface(id, label,
orientation, children)`. It stays focusable for controller reading and scrolling,
but has no activation action or Select hint. Ancestor shortcuts still work. It uses
the same bounded ActionSurface content contract and requires protocol 98. Supply an
explicit action ID for any authored shortcut; activation intents require an ordinary
action surface instead.

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

Resolvers can return a `WidgetArtworkSource` through `ResolveArtworkSource`.
Use `WidgetEncodedArtwork` for generated/downloaded bytes or
`new WidgetFileArtwork(path, revision)` for full-access local artwork. A sandboxed
widget can return an admitted package asset with
`new WidgetFileArtwork("assets/cover.png", revision, packageRelative: true)`.
Package paths must name verified package content. Arbitrary local paths do not
grant sandboxed widgets access through the host.

Keep `ResolveArtwork` when a byte fallback is available. File-capable hosts ask
for sources; older workers retain the byte route, and native file failures can
fall back through the byte resolver under the same demand and lease authority.
Ordinary widgets use `OnResolveArtworkSourceAsync`; the existing
`OnResolveArtworkAsync` byte callback remains supported. Source-aware ordinary
widgets advertise protocol 97; indexed and deferred templates advertise the
current SDK protocol.

File paths travel only in resource responses, never in view declarations.
Revision is an author-owned source version, not a host-computed hash. When image
content changes, publish a new/versioned artwork handle so retained pixels are
invalidated through the normal presentation lifecycle. The host does not poll
files or hash them on each frame.

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
