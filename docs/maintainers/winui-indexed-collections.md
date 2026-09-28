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
