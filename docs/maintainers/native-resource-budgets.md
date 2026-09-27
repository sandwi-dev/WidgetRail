# Native UI resource accounting and pressure

WIDGE-296 is in progress. This document describes the implemented foundation and
the remaining production connections; it is not a completion claim.

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
The provisional default is 384 MiB; aggregate production measurements still need
to validate it after all resource owners are connected.

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

## Remaining work

- Connect the same budget to GPU image entries, compatible capture allocations,
  retained/temporary animation bitmaps, compositor uploads/atlases and host surfaces.
  Busy raster leases and allocation lifetime are different: an idle pooled target
  still occupies storage, and several scenes may share it.
- Distinguish source rasters from compositor copies while sharing the allocation
  token when owner structures refer to the same backing store.
- Coordinate owner-thread pressure reclamation and visible/adjacent artwork demand,
  including cancellation, stale completions, hidden/pinned owners and failed frames.
- Verify transient overlap and steady retention under scrolling, replacement,
  modals, motion, device loss and reader/frame retirement; document aggregate limits.

Known-byte accounting does not claim to measure driver residency, Direct2D's
internal layer pool, allocator overhead or other process memory.

## Verification so far

The standalone core checks shared storage, overlapping protection, soft pressure,
required working sets, concurrent producer release, owner destruction, arithmetic
overflow, commit/creation distinction and deep copies. CPU-cache tests exercise
reader lifetime beyond eviction, copy lifetime, shared pressure with a protected
surface reservation, visible retry and reclamation after protection is removed.
Run the native `-UiResourceBudgetTestsOnly` and `-TrustedArtworkTestsOnly` gates.
The latest checkpoint passes 32 core checks, the full remote-image suite and
47,819 renderer checks. A blocked decode test observes allocated bytes before
cache publication; transport tests verify that mapping and copied output retire
independently. GPU/surface accounting and cancellation are not yet verified.
