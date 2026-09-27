# Native UI resource accounting and pressure

WIDGE-296 is complete for integration on the native evolution branch. This
contract covers known application-owned image/surface storage and demand lifetime.

## Accounting contract

`UiResourceBudget` tracks application-owned allocation reservations by resource
kind. One shared allocation lease represents one backing store; copying an owner
reference does not add bytes. Deep copies acquire another lease. `Commit()` marks
successful storage creation/adoption, distinguishing allocated bytes and their
peak from pending reservations and failed creation attempts.

Frame protection is independently shared. Two frames can protect the same store
without double-counting its bytes. The allocation state outlives the budget facade
and initiating cache when a frame or reader still owns it. Protection is a policy
lease, not a replacement for the corresponding pixel vector or COM reference.

The retention target is soft for required working storage and binding for optional
admission. Denied optional demand requests enough reclamation headroom. Required
frame overlap remains accounted and may exceed the target; owners reclaim idle
resources down to the larger of the requested threshold and protected working set.
An optional object larger than the target is rejected without flushing useful data.
The default retention target is a provisional 384 MiB policy. The measurements
below describe exercised working sets rather than a universal memory requirement.

Accounting and protection are thread-safe. The budget never calls an owner or
performs eviction while holding its mutex. GPU owners must reclaim on their render
thread; CPU-cache eviction stays under the cache's own lock. This avoids decoder
callbacks releasing Direct2D resources on a worker thread.

## Current production integration

`RemoteImageCache` owns or accepts a shared budget. Valid completed decoded images
carry a lease for their vector capacity. Evicting an entry updates the cache's
existing decoded-byte counter, but shared accounting retains the pixels until the
last reader releases them. Copying decoded storage creates a separate allocation;
moving it transfers ownership without double-counting.

Committed image protection also pins the corresponding allocation. Shared pressure
reclaims only idle ready entries, throttles prefetch and rejects optional decoded
retention when no reclaimable headroom remains. A newly visible rejected request
can retry, and required visible work can overlap another live owner. Existing
per-image, per-cache, queue and encoded-input bounds still apply.

Host WIC outputs and isolated-decoder response buffers now reserve before vector
allocation, then commit the actual capacity before filling pixels. Adoption into
the ready cache keeps that same lease. The decoder's shared transport mapping has
one separate allocation lease and is protected while a request uses it; shutdown
releases the mapping while any copied output remains accounted independently.
Validation, reservation and copy use one owned response-header snapshot.

The mapping's logical capacity is counted once, not once per mapped process. It
does not measure physical residency or the decoder process's private codec memory.
Reclaiming an idle transport on its worker thread remains part of owner integration.

`UiResource<T>` now couples a COM resource to its allocation lease. Compatible
capture targets and their bitmap aliases count once; raster captures and DComp
uploads count separately. The host injects one budget into its image cache,
main/pinned renderers, popup masks and composition owner. Image bitmaps, retained
rasters, interruption/background captures, popup captures, DComp surfaces/atlases,
shadow masks and their temporary working buffers are accounted. Scene/frame pins
protect live stores; protection objects are reused while any frame still owns one.
A failed staged host frame retains its surface allocation until replacement/reset.

Renderer pressure reclamation runs on its owner thread: it removes unprotected
paint entries, idle capture targets, spare transition targets, idle image bitmaps
and masks. Encoded artwork payload copies follow their buffers into the worker.
After an idle timeout under pressure, the decoder worker retires its transport
and helper; the next request can reopen them without a poison/circuit penalty.

## Realization and content identity

The renderer publishes committed, pending and current desired image sets separately.
A rejected frame retains desired retry demand until superseded; old committed
pixels stay protected until replacement. Realized adjacent candidates are bounded
to a viewport on either side and at most 256 keys; up to eight new adjacent requests
are admitted per successful frame, behind visible work and existing cache limits.

`SetImageDemand` unions owners. Removing the last interested owner retires queued
work and requests cancellation outside the cache lock. Workers compare the exact
request lifetime before publishing, so even a fetcher that ignores cancellation
cannot complete a same-key replacement. Normal decoder cancellation terminates its
request without charging the decoder fault circuit. Dispatched bridge exchanges
use shutdown cancellation only: they drain their acknowledgment instead of
aborting shared pipe framing when one item leaves the viewport. Replies are
matched by a bounded per-demand ID as well as widget authority. Old successes,
failures and retirement replies cannot complete a same-key replacement or another
decode-size demand. Stale correlated payloads are rejected before base64 allocation.
Legacy private requests omit the optional ID and retain their old wire shape;
the current host issues and accepts correlated requests only. Main-overlay hiding suspends
demand without clearing logical widget state; pinned owners remain independent.

Each ready image publication has a bounded shared content token. GPU copies and
paint signatures can retain that token without retaining CPU pixels. CPU eviction
therefore preserves valid GPU/raster reuse. Explicit cache clearing advances the
cache epoch; renderers retire stale GPU and captured content on their next bind.
Tokens live only with corresponding cache/frame owners; there is no permanent
per-URL revision map.

## Measurements and verification

Saved production-layout fixtures at 980x700 DIPs / 125% scale produced the following
known-storage readings. Artwork is a deterministic shared 192x320 substitute, so
these are layout/cache probes, not a measurement of a user's unique artwork set.

| Fixture | Loaded items | Focus retained | Scroll retained / peak |
|---|---:|---:|---:|
| Playnite Library | 64 / 150 | 23.6 MiB | 28.2 MiB |
| Spotify Tracks | 12 | 29.2 MiB | 31.7 MiB |
| Games and Apps Library | 64 | 20.0 MiB | 22.3 MiB |

Separate two-renderer scroll/replace/reorder/clip fixtures reached 47.99-102.06 MiB
of tracked storage at 100-150% scale. After submitted scene retirement and hiding
both owners, lowering the target to zero and reclaiming returned every tracked
allocation to zero. This validates ownership/reclamation, not driver residency.
The provisional 384 MiB target leaves working-set/transition overlap headroom;
it remains a policy choice, not a measured universal minimum or a process cap.

Final native bridge protocol, background host (12/12, including 240 retargets),
pinned coordinator (409 checks) and normal Release host/runtime gates pass.
Managed bridge tests pass: typed wire framing (1/1) and production artwork
admission/authority (2/2), including demand-ID echo and invalid-ID rejection.

Known-byte accounting does not claim to measure driver residency, Direct2D's
internal layer pool, allocator overhead, IPC framing/string storage, live-media
owners, font/layout metadata or other process memory. Those keep their existing
independent admission bounds and lifecycle owners.

## Verification so far

The standalone core checks shared storage, overlapping protection, soft pressure,
required working sets, concurrent producer release, owner destruction, arithmetic
overflow, commit/creation distinction and deep copies. CPU-cache tests exercise
reader lifetime beyond eviction, copy lifetime, shared pressure with a protected
surface reservation, visible retry and reclamation after protection is removed.
Run the native `-UiResourceBudgetTestsOnly` and `-TrustedArtworkTestsOnly` gates.
The latest checkpoint passes 32 core checks, the full remote-image suite and
47,872 renderer checks and 49,458 chrome/composition checks. The normal Release
host/runtime build passes. WIC tests verify alias lifetime, failure-preserved
destinations and scene/reader retirement; compositor fixtures verify accounting
returns to zero on reset. A blocked decode test observes allocated bytes before
cache publication; transport tests verify independent output lifetime and idle
retirement/readmission without poisoning. Cancellation tests cover late same-key completions, shared owners and a
re-entrant stop callback. Renderer checks cover CPU eviction versus explicit
invalidation, view replacement, hide/resume, and reclamation after scrolling.
