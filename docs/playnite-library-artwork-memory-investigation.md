# Playnite Library artwork-memory investigation

Status: diagnostic findings captured; remediation and Plane decomposition are
deferred until WIDGE-124 closes

Observed: 2026-08-31

Scope: `PlayniteLibraryApplication`, `WidgetBridge`, and `OverlayHost`
encoded/decoded/Direct2D artwork pipeline

Source snapshots inspected:

- local `main` at `39c24766fc60b03cb6d8f69f4805c7ac544ffec6`;
- the clean WIDGE-124 candidate
  `40bcbb868393c421aecaebb94380ba9a03404104` (parent
  `38b091fe1824209e8b2ba03f959ecc5d3c30ccbe`), after its implementation gates
  and before physical acceptance or integration; and
- the running local Playnite Library, bridge, and overlay processes plus
  `WidgetRail\overlay.log` during normal use, idle time, and fast rail
  traversal.

This is an investigation record, not an implementation plan or a release
verdict. WIDGE-124 remains the active delivery priority. No performance code or
Plane work item was created as part of this capture. Revalidate source identities
and measurements before turning any section below into acceptance criteria.

## Executive finding

The observed memory is not explained by one `RemoteImageCache`, and the current
evidence does not prove a classic unreachable-object leak. It does establish
three actionable problems:

1. **Excessive reachable retention.** The Playnite application retains encoded
   artwork by handle without a byte, age, current-authority, or current-window
   budget. The native host separately retains decoded CPU pixels and Direct2D
   bitmaps close to their configured byte ceilings.
2. **Load amplification.** The special PosterTile artwork path can request an
   image for a clipped or offscreen tile. Fast traversal also requests a new
   focused background for each visited game. Work therefore grows faster than
   the number of images the user can see.
3. **Cache churn.** During fast traversal the decoded and Direct2D caches evict
   and recreate images rapidly under byte pressure. The caches remain warm and
   near their limits after interaction stops, while managed temporary objects
   and the OS working set may fall later.

This combination matches the user-visible pattern: memory rises quickly while
scrolling, some of it falls after the overlay is left alone, and the idle total
remains higher than expected. Idle decline is not evidence that the design is
already healthy; it is consistent with garbage collection and operating-system
working-set trimming while deliberately retained caches remain populated.

## What was observed

### Process point samples

The following are Windows process counter samples collected during the same
investigation. They are useful for shape and order of magnitude, not exact heap
ownership. Private bytes include committed memory that may not be resident;
working set is volatile; neither counter distinguishes managed heap, native
heap, shared mappings, or GPU allocation.

| Observation | Playnite application WS / private | WidgetBridge WS / private | OverlayHost WS / private |
| --- | ---: | ---: | ---: |
| Initial high-water sample | 701.5 / 1,882.7 MiB | 581.4 / 1,091.8 MiB | 308.7 / 497.8 MiB |
| Later sample | 458.8 / 731.2 MiB | 551.8 / 1,061.8 MiB | 309.2 / 497.8 MiB |
| Further idle sample | 211.1 / 731.8 MiB | 427.2 / 1,128.6 MiB | 298.3 / 544.7 MiB |
| After rapid rail traversal | 297.4 / 990.2 MiB | 411.6 / 979.4 MiB | 297.6 / 564.5 MiB |

A short 12-second idle interval after the rapid traversal produced no material
change. Longer idle periods did reduce some process working set in separate
observations. The samples must not be summed and called a live artwork heap:
they include the runtimes, executable images, stacks, JSON, catalog state, IPC,
graphics infrastructure, allocators, and other process state.

### Native cache evidence

At one stable `overlay.log` sample, the host reported:

| Cache | Tracked payload | Configured cap | Entries and churn |
| --- | ---: | ---: | --- |
| Decoded CPU artwork | 199,987,200 bytes (190.72 MiB) | 192 MiB | 19 entries: 18 ready, 1 failed; 299 byte-pressure evictions |
| Direct2D bitmap cache | 98,389,920 bytes (93.83 MiB) | 96 MiB | 35 entries; 4,162 creates and 4,127 byte-pressure evictions |

The two reported payloads alone total about 284.55 MiB. That is tracked cache
payload, not total `OverlayHost` memory and not a measurement of physical GPU
residency. The create-minus-evict count equals the 35 current bitmap entries;
zero resource invalidations at that point make ordinary byte-pressure churn,
not repeated device recreation, the primary explanation for those counters.

During a sub-second fast traversal across several games:

- decoded evictions rose from 334 to 341;
- bitmap creates rose from 4,220 to 4,223;
- bitmap evictions rose from 4,185 to 4,188;
- decoded bytes oscillated between roughly 185.7 and 200.0 MB; and
- bitmap bytes returned to 98,389,920.

After navigation stopped, a later line still reported 190,464,000 decoded bytes
plus 98,389,920 bitmap bytes, or approximately 275.5 MiB of tracked native
image payload. That is direct evidence for a high retained baseline and burst
churn; it does not by itself prove a monotonically growing leak.

## Where the copies and retention come from

An artwork request can occupy several representations. Some overlap only while
one request is in flight; others are independently retained caches.

| Stage | Representation and owner | Retention behavior |
| --- | --- | --- |
| Playnite HTTP client | Encoded response in a `MemoryStream`, followed by `ToArray()` | Temporary full encoded copy during response materialization |
| Playnite application | `WidgetEncodedArtwork` copies the bytes again and `_artworkContent` retains the object by artwork handle | Long-lived, count-related retention with no byte or age limit in the inspected WIDGE-124 candidate |
| WidgetBridge | Base64 string plus JSON/UTF-8 transport buffers | Intended to be transient, but large repeated objects can enter the managed large-object heap and leave committed heap high after collection |
| Artwork decoder | Encoded input plus a shared mapping sized for bounded encoded and decoded output | In-flight overlap; the mapping's address-space reservation is not equivalent to resident memory |
| Overlay CPU cache | Full premultiplied BGRA pixel vector | Byte/count LRU, up to 192 MiB process-wide |
| Renderer bitmap cache | Direct2D bitmap created from the CPU pixels | Separate byte/count LRU, up to 96 MiB per renderer |

The pipeline therefore can contain multiple copies or representations of the
same logical image. It is not correct to assume that all of them are durable at
the same time, but it is also not correct to treat the Playnite application's
encoded cache as the only owner.

### Playnite application ownership

In the active WIDGE-124 candidate at `40bcbb868393c421aecaebb94380ba9a03404104`,
`samples/PlayniteLibraryWidget/Application/PlayniteLibraryApplicationService.cs`
registers two handles per game, Cover and Background. Its ceiling is:

```text
(192 retained cursor items + 64 saved/fixed items) * 2 artwork roles = 512 handles
```

`_artworkContent` stores the full `WidgetEncodedArtwork` returned for a resolved
handle. It is cleared on application disposal and when the FIFO registration
ceiling retires a handle, but it has no:

- byte budget;
- least-recently-used or idle policy;
- current snapshot/authority liveness budget;
- distinction between a small cover and a large background; or
- alias for a missing Background that resolves to the same game's Cover.

The last point can create a literal duplicate: the Cover bytes may be retained
under the Cover handle and again under the Background handle after fallback.
The current 8 MiB per-artwork protocol bound makes 512 times 8 MiB, or 4 GiB, a
formal worst-case encoded-content ceiling. That is **not** an observed live heap
size. One unchanged 33-game catalog revision exposes about 66 current role
handles. Historical handles from changed game revisions can nevertheless
accumulate toward the 512-entry FIFO ceiling, including duplicate encoded
artwork when metadata changes without the underlying image changing.

The current local `main` snapshot still has the earlier one-role, 256-handle
shape in
[`PlayniteLibraryApplicationService.cs`](../samples/PlayniteLibraryWidget/Application/PlayniteLibraryApplicationService.cs).
The two-role WIDGE-124 candidate is documented because it is the code being
physically reviewed and is the likely successor. This difference must be
rechecked after WIDGE-124 integration.

[`WidgetEncodedArtwork`](../src/WidgetSdk/WidgetEncodedArtwork.cs) defensively
copies its input with `ToArray()`. The bounded HTTP reader in
[`PlayniteBridgeClient.cs`](../samples/PlayniteLibraryWidget/PlayniteBridgeClient.cs)
first accumulates a response in a `MemoryStream` and returns another
`ToArray()`. Those are reasonable isolation boundaries individually, but they
produce avoidable full-size overlap for large images.

The application also retains a complete last-good catalog so it can survive a
bridge failure. Refresh can temporarily overlap the old catalog, the growing
replacement catalog, and filtered/authority projections. With the observed
33-game library, artwork dominates this metadata; at large library sizes the
eager full-catalog owner becomes a separate scale concern.

The candidate manifest requests 48 MB of worker memory and uses `keep-alive`
residency. The request is guidance, not an enforced heap limit, but measured
private bytes in the hundreds of MiB are materially inconsistent with the
intended footprint. Forcing worker unload is not the primary fix: it would hide
the retention by discarding useful state, add loading flashes, and weaken warm
controller UX. See [widget residency](widget-residency.md).

### WidgetBridge ownership

[`WidgetBridgeServer.cs`](../src/WidgetBridge/WidgetBridgeServer.cs) converts
the encoded bytes to Base64 for the runtime response. Base64 expands raw bytes
by approximately one third. While represented as a .NET UTF-16 string it can
temporarily consume roughly 2.67 times the raw byte count before the JSON UTF-8
buffer and receiving-side decoded bytes are considered.

No intentional long-lived bridge artwork-content cache was found. The bridge's
roughly 1 GiB private-byte observations therefore need a managed-heap trace or
dump before attribution. Repeated Base64 strings, JSON buffers, arrays, and
large-object-heap segments are a credible contributor, but that remains an
inference rather than a measured per-type result.

### OverlayHost ownership

[`RemoteImageCache.h`](../src/OverlayHost/RemoteImageCache.h) bounds ready
decoded pixels at 256 entries and 192 MiB, with 64 MiB per decoded image. Its
encoded transfer bounds are separate: 8 MiB per artwork, 32 MiB per widget, and
64 MiB total. Encoded bytes are pending transport/decode data, not the main
long-lived native payload.

[`DeclarativeRenderer.cpp`](../src/OverlayHost/DeclarativeRenderer.cpp) has its
own Direct2D bitmap LRU: 256 entries, 32 MiB per entry, and 96 MiB total. The
overlay and pinned-surface renderers can each own this bitmap cache while
sharing the CPU decoded cache. Their configured tracked envelope is therefore
up to 192 + 96 + 96 = 384 MiB if both renderers are populated. The sampled log
proved the primary renderer near its cap; it did not prove that the pinned
renderer was populated.

These native caches are bounded, which argues against an unbounded cache leak,
but they are process/renderer scoped and do not currently:

- shrink after an idle interval;
- budget decoded bytes fairly per widget;
- release entries immediately when a widget snapshot or authority retires;
- distinguish current/nearby artwork from a distant historical traversal; or
- reliably expose superseded-entry retirement through the existing counters.

Graphics-resource disposal and logical widget-state retirement are also
separate. Ordinary widget switching can forget focus/scroll/motion state
without immediately removing decoded pixels. Unpinning can release target
resources while retaining the renderer bitmap cache for possible reuse. Those
choices favor warm return but need a more deliberate liveness policy.

## Confirmed load amplifier: offscreen PosterTiles

The ordinary declarative `image` path intersects the image's paint rectangle
with the prepared visible box before it calls `DrawImage`. The button leading-
image path does the same. The special PosterTile/ActionSurface poster path in
[`DeclarativeRenderer.cpp`](../src/OverlayHost/DeclarativeRenderer.cpp) calls
`DrawImage` without that visibility test, while render traversal still visits
clipped descendants in a scroll.

Consequently, a tile can trigger fetch, decode, CPU-cache admission, and bitmap
upload even when its poster is wholly offscreen. This is a platform defect and
the clearest first optimization because it removes work without changing the
public SDK, protocol, visual behavior, or collection ownership.

## Why rapid focus movement is expensive

WIDGE-124 assigns a distinct Background handle to the focused game's backdrop
and a Cover handle to the poster. Moving rapidly across the rail therefore
creates legitimate new background demand in addition to poster demand. The
current pipeline can complete and cache a request after focus has already moved
to another game. Under byte pressure, that stale result can displace an image
that is current or about to be reused.

The desired policy is latest-wins without adding a noticeable focus debounce:

- poster focus and its outline remain immediate;
- the prior ready background remains visible while the next one resolves;
- a superseded focus-background request is cancelled or marked low-value;
- a stale completion cannot displace the current background or visible covers;
- optionally prefetch at most the immediately adjacent item; and
- measure any later crossfade against the WIDGE-104 frame budget.

This is separate from WIDGE-153's presentation-retention semantics. Retaining
the last valid background answers what should remain visible; request
supersession answers which expensive decode/upload work should still finish.

## What `WidgetCursorResource` solves—and what it does not

The Playnite widget configures
[`WidgetCursorResource`](../src/WidgetSdk/WidgetCursorResource.cs) with a
64-item page and a 192-item retained window. It trims complete cursor segments,
bounds cursor history, and prevents the declarative collection snapshot from
growing with the whole provider catalog. Its virtual-window fields can also
represent logical collection extent without serializing every item.

It does **not** currently bound:

- the application's complete `_lastGood` catalog;
- saved/fixed-row and authority projections outside the cursor window;
- `_artworkContent` encoded bytes;
- the host CPU decoded cache;
- either renderer bitmap cache; or
- in-flight Base64/JSON/decode buffers.

There is no application callback saying that a cursor item was evicted and its
artwork may now be released. Snapshot artwork authority correctly prevents a
stale undeclared handle from being resolved, but that security/liveness check
does not reclaim bytes already retained in the application or host.

`WidgetCursorResource` should be one liveness input to the future cache policy,
not the cache policy itself. Reducing the cursor window aggressively before
fixing offscreen requests and byte retention risks more network churn and worse
return fluidity. Fully lazy catalog ownership will also require the Playnite
adapter/bridge to support revision-bound cursors, server-side filtering/sorting,
and compact summaries; cursor presentation alone cannot make the eager
application catalog disappear. See the [declarative UI reference](declarative-ui.md)
and [Playnite Library architecture](playnite-library.md).

## Classification

| Classification | Verdict | Evidence |
| --- | --- | --- |
| Unbounded classic leak | Not proven | Memory can fall after idle; native caches have byte ceilings; no managed/native heap ownership dump was captured |
| Excessive reachable retention | Confirmed | Application encoded cache lacks a byte/age/liveness bound; native decoded and bitmap caches remain near their caps |
| Duplicate representations | Confirmed by source | HTTP/materialization copies, retained encoded bytes, Base64/JSON, decoded BGRA, and Direct2D bitmap stages coexist at least transiently; fallback can retain the same Cover bytes under two handles |
| Offscreen artwork loading | Confirmed by source | PosterTile path omits the visibility gate used by ordinary image paths |
| Cache thrash during fast traversal | Confirmed by logs | Creates and byte-pressure evictions rise while decoded bytes oscillate near the cap |
| Bridge's exact 1 GiB owner | Unknown | Requires EventPipe/GC-dump attribution; process private bytes alone are insufficient |
| GPU-resident artwork total | Unknown | Current counters account renderer bitmap payload but are not a GPU residency trace |

## Deferred ticket candidates

These are proposed decomposition boundaries, not created Plane items. Preserve
the order unless profiling disproves an assumption after WIDGE-124 closes.

### 1. Platform P0 — visibility-gate PosterTile artwork

Apply the same prepared-visible-rectangle check used by ordinary images before
the PosterTile path requests artwork. Add a renderer regression with many
clipped/offscreen posters and prove that only visible plus an explicitly bounded
prefetch window can request images.

### 2. Widget/application P0 — byte-budgeted encoded artwork LRU

Replace `_artworkContent`'s count-related lifetime with a measured byte LRU.
Start validation around a 16–24 MiB budget, then tune from real cover/background
sizes. Keep handle metadata separate from bytes, alias same-game Background-to-
Cover fallback instead of copying it, and protect current plus one short
previous-snapshot grace set so rapid focus reversal remains warm.

The cache must report entries, bytes, high-water mark, hits, misses, role,
fallback aliases, evictions, and current protected-liveness count. Do not use
forced GC or worker unload as acceptance.

### 3. Platform P0 — latest-wins focused-background resolution

Cancel or supersede stale focused-background work, retain the last ready
background while the current request resolves, and prevent stale completions
from becoming high-priority cache entries. Prove rapid A→B→C focus settles on C,
never blanks, and does not require a worker rerender for host-local focus.

### 4. Cross-process diagnostics and performance gate

Add enough evidence to attribute the next run without a debugger guessing:

- application encoded entries/bytes/high water, per role, hit/miss, alias, and
  eviction counters;
- bridge raw artwork bytes, Base64 characters, requests/in-flight count,
  managed heap, LOH size, allocation rate, and Gen2 collections;
- host visible versus offscreen requests, per-widget/authority decoded bytes,
  overlay versus pinned bitmap caches, source dimensions, requested paint size,
  role, and real superseded counters; and
- correlated request/focus/authority IDs that contain no private paths or image
  content.

Use EventPipe/PerfView or a GC dump for managed ownership, native heap tools for
native allocations, and graphics tooling for GPU residency. Integrate the
scenario with [the performance contract](performance.md) and WIDGE-104 rather
than creating an unrelated cadence benchmark.

### 5. Platform P1 — target-sized decode and bounded size variants

Decode to the actual DIP × DPI demand instead of retaining every source at full
resolution. Scale with WIC before materializing the final BGRA vector, key only
a small number of size buckets, and permit a larger consumer to request an
upgrade. This should substantially reduce poster cost while keeping a
background-quality path for large surfaces.

### 6. Platform P1 — authority/lifecycle-aware residency and idle trim

Protect the current background, visible covers, and a small near-focus window.
Release historical backdrops, distant posters, superseded revisions, retired
authorities, and inactive pinned bitmaps. Add per-widget decoded-byte fairness
and an idle trim policy that preserves a bounded warm reopen set. Do not clear a
background merely because focus moved to a control without new background
metadata, and do not keep a surface's retained background after that surface
leaves the effective render tree.

### 7. Bridge P1 — binary artwork transport

Replace Base64/JSON artwork payloads with a bounded binary frame or shared-
buffer transfer. Preserve the existing authority, content-type, size, timeout,
and cancellation validation. Measure allocation rate and LOH behavior before
and after; do not accept the change from wire-size arithmetic alone.

### 8. Adapter/bridge P2 — revision-bound lazy catalog ownership

Add opaque, revision-bound provider cursors plus server-side filters/sorts and
compact library summaries. Then let the application retain only the active
cursor window, saved/fixed identities, and bounded summaries instead of a full
catalog. This is the larger scale fix and should follow the direct artwork
amplification/retention corrections.

## Proposed measurement scenario after WIDGE-124

Capture a correlated trace for this sequence without user interaction during
automated timing intervals:

1. cold-start Playnite Library and wait for a stable Home frame;
2. traverse the entire Home rail rapidly in both directions three times;
3. open Browse and traverse multiple rows/pages;
4. return Home, move between ordinary controls and posters, and cycle through
   tray/overlay reopen paths;
5. leave the overlay visible and inactive for 5, 15, and 30 minutes; and
6. repeat the same traversal without restarting any process.

Record per-process working set, private bytes, managed heap, LOH, allocation
rate, CPU, I/O, decoded/cache bytes and entries, creates/evictions, request
visibility, cancellations/supersessions, decode dimensions, and frame/controller
latency. The run should demonstrate:

- memory reaches a repeatable plateau instead of increasing with every full
  traversal;
- a second traversal of the same working set does not recreate and evict nearly
  one bitmap per request;
- offscreen posters do not fetch or decode outside the declared prefetch bound;
- stale focus-background requests cannot displace current artwork;
- the previous ready background remains visible while a replacement loads;
- warm visible covers remain fluid after tray/reopen without a loading flash;
- hidden/idle work stops, while bounded warm caches may remain; and
- WIDGE-104's 60 Hz frame-pacing and controller-response targets remain green.

Do not set final MiB pass/fail thresholds from the current process samples.
First obtain attributed heap/cache data after the P0 visibility, application
byte-budget, and request-supersession changes. Then set separate budgets for the
application, bridge, decoded CPU cache, renderer bitmap cache, pinned renderer,
and total warm/idle footprint.

## WIDGE-161 attributed counter contract

WIDGE-161 adds measurement only; it does not change cache limits, transport,
decode size, rendering, cursor behavior, or lifecycle policy. The counters use
bounded aggregate categories and never retain a package path, token, title,
game ID, artwork handle, encoded content, or request identifier.

| Owner | Gauge or monotonic unit |
| --- | --- |
| Playnite application | encoded entry count, current encoded bytes, high-water bytes, cache hits/misses, fallback-alias count, evictions, and bounded cover/background/neutral role-event counts |
| WidgetBridge | artwork requests/completions/failures, current and peak in-flight requests, raw bytes, Base64 characters, managed heap bytes, LOH bytes when the runtime reports them, allocation bytes/second between observations, Gen2 collection count, process private bytes, and working-set bytes |
| native decoded cache | current encoded and decoded bytes, ready/pending/failed entries, request/hit/supply/stale-completion counts, evictions, total ready source pixels, and maximum source dimensions |
| ordinary and pinned renderers | independent bitmap bytes/entries/hits/creates/evictions, visible versus fully clipped artwork observations, cumulative requested paint pixels, and maximum requested paint pixels |

Application and Bridge diagnostics saturate rather than wrap. Current entries,
bytes, and in-flight work are gauges; high-water and event totals are monotonic
for the process lifetime. Native cache and renderer counts are process-lifetime
monotonic counters beside the existing bounded current gauges. A cache entry or
bitmap estimate is tracked ownership, not an OS/GPU residency measurement.

`scripts/Measure-ArtworkMemory.ps1` records timestamped process private and
working-set bytes for explicit PID/opaque-role pairs. It performs no process
discovery, launch, interaction, dump, GC, or content capture. Use a fresh output
file at each of these named checkpoints: `cold-start`, `stable-home`,
`slow-navigation`, `rapid-home`, `browse`, `focus-thrash`, `hide-reopen`, the
three idle checkpoints, and `repeat-traversal`. The reviewer/user performs the
real overlay actions and supplies only the process identities they consent to
measure. Correlate each CSV checkpoint with the bounded application, Bridge,
and host aggregate lines, then report steady, peak, and post-idle values
separately.

This implementation task cannot produce the observed live-package run because
its authority explicitly excludes installation, launch, live private tracing,
and product-process mutation. The measurement run therefore remains the manual
gate before any remediation ticket selects retention, transport, decode-size,
or cache-budget policy. Process totals alone must not be described as a leak.

## Open questions that require profiling

- Which managed types retain most of the bridge's private commitment after a
  fast traversal?
- How much of the artwork-decoder shared mapping is resident at peak and idle?
- What is the actual GPU residency of the Direct2D bitmaps versus the renderer's
  tracked byte estimate?
- Does a long repeated run contain any monotonically growing owner after all
  bounded caches reach steady state?
- What cover/background byte-size and source-dimension distributions should
  determine the application LRU and decode-size buckets?
- How much catalog metadata is retained at 1,000 and 10,000 games, independently
  of artwork?
- Can the pinned renderer share or more aggressively release device bitmaps
  without harming pinned-surface return latency?

Until those are answered, describe the problem as **confirmed excessive
retention, offscreen load amplification, and byte-pressure churn, with a
possible but unproven additional leak**.

## Related documentation

- [Performance contract and evidence](performance.md)
- [Playnite Library architecture](playnite-library.md)
- [Declarative UI reference](declarative-ui.md)
- [Widget lifecycle and process residency](widget-residency.md)
- [Widget snapshot-cache design](widget-snapshot-cache-design.md)
- [Diagnostics and recovery](diagnostics-and-recovery.md)
- [Troubleshooting](troubleshooting.md)
