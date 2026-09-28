# WinUI collection data contracts

Status: native indexed-source implementation and integration evidence, 2026-09-27.
The public SDK and worker/bridge range protocol described below are still to be
implemented. This is not a claim that YouTube Music or Playnite already uses them.

## Two different addressing models

An indexed query has an exact count and a stable ordering for its lifetime. Any
bounded range can be read independently. Loading or evicting cached payload does
not insert/remove positions. A cursor query has opaque adjacent-page tokens and
does not acquire random access merely because a host wants an IList.

Microsoft's [data-virtualization guidance](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/listview-and-gridview-data-optimization)
applies to Windows App SDK. Its random-access pattern is IList plus asynchronous
payload loading, with `IItemsRangeInfo` providing visible/tracked ranges. Tracked
ranges include native buffering and focus retention. Each items control needs its
own range-info instance; data caches may be shared separately.

`ISupportIncrementalLoading` is useful for forward discovery: Count is discovered
membership and WinUI requests the next batch. It is not a random-access contract.
`IItemsRangeInfo` is a demand/retention interface, not a viewport anchor API.

## Implemented native indexed adapter

`OverlayFrontend.WinUI/Collections/IndexedItemsSource<T>` is an internal adapter
for one fixed-count query and one native ListView/GridView consumer. It implements
IList, INotifyCollectionChanged and IItemsRangeInfo. Query identity includes runtime
incarnation, widget instance, collection and query generation. The runtime identity
must identify an incarnation, not just a package fingerprint.

The indexer returns a stable slot for requested positions. Slot payload changes
raise property notifications; eviction removes references outside native tracked
pages without shrinking Count or emitting structural collection notifications.
Visible pages take request priority over other tracked pages. Bounded concurrent
readers run off the UI thread; completion is marshaled to the dispatcher and must
still belong to the active request. Results echo query, request ID and start index,
and must have exact range length, unique keys and stable keys at retained positions.
An invalid batch cannot partly fill the cache. A new query replaces the source.

The range reader is trusted framework code, not widget code loaded into WinUI. It
must bound execution and honor cancellation. Asynchronous disposal waits for its
owned readers, including cancelled requests that have not returned. Detach the
native ItemsSource before disposing it. The forthcoming service client must satisfy
that bounded-reader contract; the current adapter alone cannot forcibly cancel an
arbitrary delegate. Failed ranges stop automatic retry; complete error/retry UX and
service demand withdrawal remain to be connected.

The consumer must constrain its viewport and choose supported selection policy.
The implementation does not claim that an unbounded layout, Select All over every
logical item, or arbitrary explicit enumeration is constant-memory. Its normal
ListView range-callback path is measured, not assumed. Do not assume ItemsView
implements IItemsRangeInfo; its data source and anchoring behavior differ.

`--validate-indexed` exercises one million logical rows with delayed reads. The
eight-check native UI run observed at most 96 resident slots and 21 realized
containers, with zero enumeration of the source. A held buffer page was released
after down/up navigation; focus stayed at index 600000, y=20, and scroll offset
43200172 before and after admission. Returning released distant payload, and a
subsequent deep visit performed another actual load without deleting logical
positions. These are functional/retention measurements, not a frame-time benchmark
or acceptance of production widget styles, controller loading barriers or providers.
See `scripts/Test-WinUiIndexedCollection.ps1` and `artifacts/winui-indexed/authority`.

## Current provider capabilities

| Surface | Truth established by current source | Required migration work |
| --- | --- | --- |
| YouTube Music finite browse snapshot | `StandaloneMusicWidget.ReplaceCollection` already stores the returned Items array; pages supply FirstItemIndex and TotalItemCount | Expose that frozen snapshot as an indexed query. The count is the returned snapshot, not an unlimited remote YouTube library. |
| Playnite Library | Application service loads the complete catalog and freezes a traversal, but widget-side post-page filters can remove displayed entries | Apply final display filtering before exposing an indexed count/order; do not forward unqualified provider offsets. |
| Spotify playlist/item endpoints | Offset/total exist, but null/unsupported entries may be filtered and disable authoritative-window metadata | Preserve provider slots or normalize an authoritative display ordering first. Preserve duplicate track occurrence identities. |
| Spotify search | Ranking/totals are live and current code deliberately marks the window non-authoritative | Keep discovered/cursor semantics; do not fabricate a stable total/index mapping. |

The YouTube service currently bounds returned results (500 generally, smaller
search/radio requests). This source capability does not add remote continuation
support. The empty indexed query has Count zero; do not reuse the old cursor
normalizer's positive-total restriction.

## SDK and service implementation direction

An indexed collection declaration must carry a source/query identity, exact count
and native layout intent. It must not contain a complete visual tree for every
logical item. Worker-author code should register a range reader and an item-render
callback using existing declarative elements; only demanded bounded ranges invoke
that callback. This retains flexible C# item authoring without shipping executable
XAML or widget code into the host. Trusted native templates realize the returned
item declarations through the shared presenter.

The service contract needs these distinct lifetimes:

1. **Query:** widget/runtime/instance/source identity, query generation, exact count
   and stable ordering. Query/sort/filter replacement retires old requests.
2. **Surface demand:** one main/pinned surface's visible and tracked ranges, request
   IDs and cancellation. Native unrealization releases demand; it does not delete
   an item from the query or imply loss of presentation-source membership.
3. **Item content:** occurrence key, source index, item revision and bounded visual,
   action and presentation declarations. Payload changes can update retained slots
   without changing the collection's structure.
4. **Action/artwork authority:** a bounded current range lease associated with the
   parent presentation and active input scope. The old facade validates artwork and
   actions against the complete root snapshot; it cannot be reused unchanged when
   item declarations are delivered separately. Never make actions valid merely
   because a recycled control still holds an old item.

Parent snapshots must reference the registered source; range responses need explicit
generation/request/index/key validation through worker, bridge and client. Loading,
error and retry are visible states, not focusable replacement rows. Controller
navigation at unloaded edges must retain its logical target and suppress unintended
escape into unrelated headers while existing loaded items remain usable.

Query membership changes are different from cache eviction. An actual insert,
remove, filter or reordering still needs a defined focus/anchor policy and validation.
The fixed-count adapter does not solve arbitrary live structural mutation. Preserve
logical presentation contributions when containers are unrealized, and retain any
payload still demanded by background slots, outgoing motion or pinned surfaces.

The first production application is YouTube Music's finite browse list, followed
by Playnite after its final displayed ordering is authoritative. SDK/wire integration
must cover row actions, context actions, focus groups, shortcuts, artwork and
background selection; a title-only list is not the completed port.

## Cursor anchoring evidence remains open

Granular insertion/prepend and leading eviction failed viewport retention on both
ItemsView and GridView with the tested keyed source. Explicit native AnchorRequested
selection at an interior anchor ratio improved one prepend/deep case, but did not
hold across broader layout/release/navigation cases. All experimental anchor code
was removed; evidence/source copies remain in `artifacts/winui-collection/anchor-selection`.
No custom offset correction, forced layout loop or timed anchor hold was retained.

Discovered cursors should retain logical positions and release heavy payload
where possible. True insertion before an unknown origin still requires a proven
anchor policy. This remains required migration work, rather than an excuse to label
opaque cursors as indexed queries or silently drop their behavior.


## Indexed SDK contract implemented on the migration branch

Protocol 60 adds an `IndexedCollection` node containing a source descriptor and
list/grid layout, with no inline item trees. Authors register one
`WidgetIndexedCollection<TQuery,TItem>` through `CreateIndexedCollection`, then
use the indexed overload of `UI.CollectionList` or `UI.CollectionGrid` in their
ordinary page. The source descriptor is captured when that declaration is built.

- `ReadRange(query, start, count, token)` returns exactly the requested entries.
  Count and ordering must be truthful for the immutable query snapshot, including
  count zero. Reads may run concurrently and must observe cancellation.
- `ItemKey` identifies an occurrence, including repeated songs. `RenderItem` is
  pure and uses captured query/item values. It returns one always-present Button
  or ActionSurface with ordinary presentation content and metadata.
- `WidgetIndexedItemContext.Id(name)` produces IDs scoped to source, collection
  placement and occurrence key; position changes do not rename a logical item.
- `PublishQuery` replaces membership/order/count and retires old reads.
  `UpdateContent` advances a content revision while preserving every key's index.
  Both invalidate the widget; old descriptors cannot serve a new query revision.
- Only requested items are rendered. Mutable lists in returned declarations are
  copied before retention/publication. Ranges are bounded to 64 items and 2048
  nodes, and validated against the actual main or pinned parent projection.
  Row focus/presentation metadata cannot invent a separate page or bypass the
  parent's scope and presentation-surface ownership.
- Each source permits four actual provider tasks. Cancellation/timeout releases
  the caller, but an uncooperative provider continues occupying its slot until it
  actually finishes. The default read timeout is ten seconds (100 ms-30 seconds).
  Widget/process lifetime remains the final boundary for uncooperative author code.

This is migration infrastructure, not a production-ready widget switch. The
shared frontend still rejects unsupported collection declarations. End-to-end
service demand, bounded row-action/artwork leases, native template presentation,
error/retry UI, and production widget adoption must be connected before these
overloads can replace an existing shipped collection. A range response by itself
is not authority to invoke row actions through the existing parent-only route.

## Bridge and presentation-session demand path

`WidgetPresentationSession.ReadIndexedRangeAsync` now sends the source/range with
host-owned unique demand identity and the exact widget instance, runtime and
presentation generation. The bridge admits only an already-running worker and
revalidates its start ordinal, query and projection before and after loading.
Reads bypass the ordinary widget request FIFO and release its operation gate
across provider work. They are bounded to eight bridge-wide and four per widget;
cancellation and ordinary actions retain transport capacity.

Cancellation is ordered after read-frame completion and the bridge's read
admission boundary, so it cannot arrive before the matching request is registered.
Original response correlation stays alive through terminal cancellation. Query or
worker retirement cancels captured work; the registration's publication lease
prevents disposal during its actual read. The trusted worker-client adapter owns
bounded cancellation/drain and never starts or recovers a replacement worker for
an old range. The bridge verifies this with real process-backed pipe traffic.

A pending data range survives unrelated parent snapshot revisions and opening a
modal over its unchanged parent. Its source/query/content revision, parent scope,
projection and worker identity must still match. Session closure, query replacement
or explicit surface retirement cancels it. Range data does not grant input or
artwork authority; bounded semantic leases and native item-template integration
remain required before production widget adoption.


### Indexed item semantics and remaining integration

Production adoption needs a typed item action callback using the captured immutable
query/item, with ordinary local action names rather than positions encoded in IDs.
YouTube's current handlers resolve `item.<index>` against its current page/queue;
that must change deliberately so an already-admitted action cannot target a new
item occupying that position. Queue-position commands also need queue revision
validation or an explicit captured playback context.

Use bounded worker semantic leases for frozen row declarations and captured item
values. A realized XAML container is not that lease: cached/focused rows, open menus
and retained presentation can outlive container recycling. Separate data retention,
current interaction eligibility, artwork demand and an already-admitted operation.
Extend the existing serial action queue with a captured execution binding and
identity-aware repeat/coalescing; do not create an item-specific action queue.
Admitted actions retain their captured item until terminal completion under the
existing active lifetime, independently of ordinary visual eviction.

A shared logical input-path resolver must append the leased row path to its real
parent scope/collection path. Apply existing nearest-owner shortcut rules so row
actions use the captured item callback while page/collection actions continue to
invoke the parent handler. Validate exact context-menu option and opener ownership.
Modal scope changes disable background input without discarding valid parent
presentation/artwork. Pinned projection ownership must remain distinct. Resolve
opaque artwork through declared handles and an optional captured item resolver;
HTTPS images continue to use host network policy. None of this action/artwork
admission is implied by the currently implemented read-only range transport.


### Implemented worker-side semantic leases

The migration SDK now requires `OnAction(query, item, action, token)` alongside
`ReadRange`, `ItemKey` and `RenderItem`. Use local row action names such as `open`
or `favorite`; the callback receives the exact captured query/item rather than
looking up a possibly replaced item by index. Ordinary page/collection shortcuts
continue to invoke `Widget.OnActionAsync`. `ResolveArtwork(query, item, handle,
token)` is optional, but required when a leased row or its focus presentation
declares opaque artwork handles. HTTPS image loading remains a host responsibility.

Internal lease acquisition retains frozen row declarations and immutable captured
values/delegates. Parent input-owner paths are resolved during admission against
an exact current worker snapshot; they are not frozen into the data lease. Each lease has a fresh opaque ID. Pending acquisition and retained
data share limits of 32 ranges, 1024 items and 32768 nodes; pending reads reserve
their worst-case node budget before loading and transfer that reservation atomically
at publication. Duplicate demand identity is rejected. Release, query/content
retirement, parent removal/scope change and widget destruction reclaim leases.
Routine parent revisions and modal opening preserve unchanged parent data.

Worker input admission validates the current snapshot sequence and logical binding,
respects nested input scopes, checks exact menu options and owner availability, and invokes either
the captured item callback or ordinary parent handler. Collection-disabled/busy
state blocks row commands while unrelated page shortcuts remain available. The
existing serial queue owns execution, diagnostics, capability/invocation context,
capacity and active-lifetime cancellation. A captured queued action survives data
lease release without retargeting. Repeat identity follows source/query/item and
projection, surviving rerealization but never joining a replacement query's action.

Captured artwork resolution validates item/handle ownership before and after the
read. It accepts retained parent artwork during modal interaction and rejects late
results after release. Four actual artwork operations may run concurrently; a
cancelled caller does not free its slot while an uncooperative provider still runs.
The resolver has a ten-second ceiling, also bounded by caller/widget cancellation.

Six new SDK scenario groups exercise these semantics; all 134 SDK checks and 14
API compatibility tests pass. Existing six indexed-runtime and five indexed-bridge
checks still pass after updating their source declarations for the required action
callback. **Lease acquisition, item invocation and item-artwork now have a complete
worker/runtime/bridge/session path.** Plain range reads remain data-only; frontend
interactive rows must use explicit acquisition. Native row-template integration
and production widget conversion remain incomplete.


### Runtime ownership handoff

The runtime's disposable lease handle binds to an exact already-running worker.
Acquisition/release, item input and artwork requests now cross the real worker pipe.
A failed lease reply or cancellation after reply delivery reclaims SDK retention.
Disposal cancels pending artwork, and an old lease cannot start or target a new
worker. The range and artwork lanes have separate bounded capacities and share
one cancellation/drain implementation; ordinary action execution remains serial.

Input and data have separate authority. The data lease follows its immutable query
and item values. Each input must name the current worker snapshot after trusted
origin/current binding validation, so retained data neither preserves obsolete
page shortcuts nor prevents new page shortcuts from working. The bridge integration
performs that origin/current check before forwarding the worker sequence.

### Bridge and presentation-session ownership

The bridge retains exact worker leases and compares origin/current logical input
bindings before forwarding input. Current modal scope affects interaction, not
retention of unchanged parent rows. Query/projection/registration changes release
the captured owner after active operations drain. Failed delivery and late
acquisition cancellation also release the owner, while rejected duplicate
acquisitions preserve an existing successful lease.

The presentation-session facade returns `WidgetPresentationIndexedLease` with
immutable rows, typed input admission, artwork resolution and asynchronous
disposal. The caller supplies the frame actually displayed when input originated.
An admitted action remains on the existing worker action queue; cancellation does
not retract or retarget it. Retained, reserved and retiring ranges all count toward
budgets. The host transport reserves separate capacity for provider work,
cancellation and ordinary controls; it schedules at most four releases together.
Release admission and reply have separate deadlines, and session shutdown is
bounded even if ordinary control requests stop responding.

The full session suite passes 64 tests. Thirteen indexed bridge tests include a
session-to-bridge-to-real-worker round trip covering captured row actions, parent
shortcuts, artwork, query replacement and shutdown. All four existing dispatcher
and eighteen registry regressions also pass. These are correctness checks;
production-widget performance still requires native templates and real sources.

### Native interactive templates

The shared WinUI presenter now hosts indexed declarations with `ListView` or
`GridView` and a compiled `x:Bind` item template. `WidgetIndexedRows` maps native
range callbacks to owned presentation-session leases. Ranges and opaque artwork
share the session's advertised provider capacity, so realizing a page of image
rows cannot overwhelm the service's artwork slots. Item contents use the same
declaration renderer as ordinary widget content, with native item containers
owning focus and activation. Range replies carry validated immutable computed
styles, resolved with the same theme cascade as parent snapshots.

Stacks/rows use native Grid auto/star tracks for their basic layout, including
declared growth, so a fill collection receives a finite viewport. This is an
initial layout/style mapping, not the complete WRSS implementation. Ordinary
scroll regions still own their native ScrollViewer. Lists/grids are never wrapped
in another scroll control by the collection adapter.

Spatial `FocusManager.TryMoveFocus` alone missed moves at realization boundaries
in a real-worker probe. The indexed adapter therefore chooses a logical index,
coalesces pending directional input, and uses native `ScrollIntoView` followed by
focus on the realized native container. Native WinUI still performs realization,
layout and scrolling; the adapter has no scrolling timer or layout engine.
Pointer/keyboard navigation and disabled scopes cancel pending intent. Grid
column count follows the same bounded native wrap-panel configuration.

`Test-WinUiIndexedWidget.ps1` exercises both service pipes, a real worker, range
delay, declared artwork sizes, content revision, forward/reverse navigation,
batched input, and native grid columns. The current 100-row fixture is a correctness
check at 125% Windows scaling. It does not establish YouTube/Playnite frame times,
physical controller routing, live theme restyling, or complete widget feature parity.

### First-party adoption requirements

YouTube Music already downloads a complete bounded browse page (usually capped
at 500 entries); its current 24-item cursor layer is artificial paging over that
array. Freeze membership/order and slice it in `ReadRange`. Capture the full
ordered song-only playback context and selected occurrence in each query: playing
a row must not substitute the current section or just the realized window.
Queue selection needs explicit queue identity/revision and occurrence IDs,
validated atomically in `MusicService`; duplicate tracks are distinct occurrences.

Typed logical collection entry/return focus and public indexed author helpers are
now available (below). Grouped header/grid declarations for Home's single scrolling
surface still need native validation. Static focus IDs cannot refer to undeclared
lazy children. Keep author tests exercising real captured handlers
and the existing action queue. The relevant first-party suite is
`tests/YtMusicStandalone.Tests`, and it needs to retain playback, mixed result,
queue mutation, section return, account reset and stale search assertions.

### Native page ownership and content refresh

`IndexedItemsSource` accepts an asynchronous lifetime with each range result.
The source releases it on page eviction, successful replacement, rejected or
cancelled delivery, and disposal. Release work runs outside the UI dispatcher;
pending releases also count against preparation concurrency so slow cleanup
cannot accumulate an unbounded queue of remote owners. Disposal drains actual
fetches and releases, including a late result from a provider ignoring cancellation.

`RefreshContent` advances only the content revision. Logical count and position,
slot objects, native containers, focus and scroll offsets remain unchanged.
Existing payload stays visible until a validated replacement arrives. The new
result must match both its request and current revision, and keys cannot change at
an already retained position. Changed membership/order still requires a new query.

The native million-row check now also refreshes deep content and verifies exact
focus/viewport preservation. Seven real-dispatcher ownership checks cover pending
refresh, late cancellation, invalid replies and disposal; asynchronous owners are
released exactly once. This validates the native data-source lifecycle, not yet a
production widget's row templates, actions, artwork or performance.

### Logical focus and author tests

An indexed collection is itself a focus group. Use `source.Enter(collectionId,
requestId)` to enter its remembered/default item. For an exact occurrence, pass
`source.FocusTarget(collectionId, key, index)` as the optional third argument.
Assign the result to the existing `WidgetView.FocusGroupEntryRequest`; increment
its ID for each new intent. This uses the existing one-shot request mechanism.

`WidgetActionEvent.FocusedCollectionItem` carries the worker-validated occurrence
for both captured row actions and parent shortcuts. Authors can save it for a
return request without parsing generated element IDs. It is never accepted from
ordinary serialized action data. Its identity comprises collection, source, query
generation, item key and index. A content refresh preserves identity; membership
or ordering changes require a new query and invalidate the old target.

The SDK rejects newly authored targets from another query. A consumed request may
remain in subsequent snapshots while the query changes; protocol validation allows
that structurally valid stale request and the host ignores it. Native collection
entry verifies the loaded key before focusing, uses WinUI realization/scrolling,
and cancels pending intent on withdrawal, query retirement or superseding input.
Ordinary updates neither replay entry nor cancel a still-current request.
Lightweight remembered occurrence identities are bounded to 64 collections/queries
per presenter; no native controls or semantic leases are retained for that history.

For author tests, attach `WidgetTestHost.CreateIndexedCollectionHost` to an already
initialized widget. The helper publishes explicit monotonically increasing
snapshots and owns disposable `AcquireAsync` range leases. `RouteAction` exercises
the actual serial action queue and returns admission, not action completion;
`ResolveArtworkAsync` exercises actual opaque-artwork validation. Tests can supply
an explicit input context for stale-snapshot or scope rejection. Disposing the
helper cancels pending acquisitions/releases its leases, without destroying the
widget or cancelling actions already admitted to the normal widget lifetime.

### Grouped native collections

Protocol 62 supports display-only group headings on vertical indexed lists and
grids. The query still contains one flat ordered sequence:

```csharp
UI.CollectionGrid("home.items", source, 130, 170, "Home", maximumColumns: 4)
    .Grouped(new("recommended", "Recommended", 12),
             new("albums", "Albums", 8));
```

The group counts must total `source.Descriptor.Count`. Group keys are unique
occurrence IDs; repeated heading text does not imply the same group. At most 256
groups are accepted. Empty groups are permitted and hidden. Group declarations
are copied when authored and when retained. They add no input scope, action owner,
row key, provider request or focus index. All row actions and exact focus targets
continue to use flat query indices.

WinUI receives observable slices over that single source through its native
CollectionViewSource and a range-forwarding ICollectionView adapter. The standard
grouping wrapper alone does not forward IItemsRangeInfo. Native ListView/GridView
still own headers, layout, realization and scrolling. The shared controller adapter
accounts for each group's row origin and partial final row when choosing a logical
target; empty groups never become focus stops. Header text updates retain the view,
focused item and scroll position. Membership/order changes still publish a new
query; changing only display grouping preserves the underlying semantic leases.

This first header contract supplies text using the shared WinUI heading template.
Full theme/pseudo-state integration remains outstanding; arbitrary interactive
header trees are not implicitly supported by this contract. Real widget Home
adoption must verify its existing heading appearance and actions separately.

### Retained presentation demand

Native container realization and displayed background/summary demand are separate
lifetimes. `IndexedItemsSource.Retain(index)` holds an existing slot/page for a
presentation consumer without retaining its XAML item container or adding another
provider/cache/semantic lease. Tokens share reference counts, are bounded to eight
distinct positions per source, follow ordinary content refresh, and become harmless
after source disposal. Native visible demand has scheduling priority, then explicit
presentation demand, then native tracked buffers. All use the same bounded provider
and release budget.

The native presenter consumes BackgroundSurface and FocusPresentationSurface through
the shared PresentationSourceCoordinator. Static declarations contribute directly;
indexed rows retain only the selected logical data for each nearest owning surface.
An offscreen selected row can update its summary/artwork without resurrecting its
tile. Source/query/surface removal releases that demand. Input scopes do not erase
a still-valid retained parent presentation. Fragments use a presentation-only native
presenter and never acquire their own focus/action authority.

This checkpoint supplies selection, native layout and image-brush presentation.
Full WRSS surface appearance, depth and transition integration remain separate
migration work; do not infer final visual parity from the validation probes.
