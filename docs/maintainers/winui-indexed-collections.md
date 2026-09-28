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


### Remaining indexed item semantics

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
