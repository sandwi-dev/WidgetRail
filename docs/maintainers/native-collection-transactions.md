# Collection preparation and frame transactions

This correction follows the [page-admission review](native-page-admission-review.md).
It keeps Direct2D/DirectWrite/DirectComposition and the existing SDK contract.
No scheduler allowance, cache-window size or animation setting is increased.

## State ownership

| State | Owns | May become visible? |
|---|---|---|
| Committed frame | Admitted source, layout, collection geometry, scroll state and interactive geometry | Already displayed |
| Pending preparation | Validated item measurements (`CollectionMeasurements`) | No |
| Ready preparation | Complete layout, measurements, geometry and output scroll map, with exact input proof | Only through a frame |
| Pending publication | Drawn frame and the identity of any prepared frame it adopted | After successful host acknowledgement |

A pending slice cannot store a partially updated geometry/scroll pair. It starts
each placement pass from committed geometry, imports individually valid item
measurements and either yields measurements or completes the whole frame. This
prevents a prepend slice from saving incoming geometry with a pre-prepend offset.
It also allows input to move the committed viewport while an incoming page is
being prepared, without repeatedly throwing away useful item work.

`CollectionMeasurements` is separate from `CollectionRenderState`. Sharing a
measurement requires the same collection identity, reset generation, actual
measurement context and item content. Local measurement revision numbers are
not transferred across branches. Source scope, changed constraints or changed
item content reject reuse. Partial geometry is never a cache donor.

A ready frame is still subject to the exact source/focus/viewport/options/input
scroll checks. A publication records the specific prepared frame it adopted.
Acknowledging another paint cannot erase an unconsumed lookahead frame. Ready
lookahead for another position also cannot force an unchanged scene into layout.
Failed publication preserves the prior authoritative frame and never authorizes
input or accessibility against unsubmitted geometry.

## Movement and work priority

Required visible layout runs as part of the frame for accepted movement through
loaded items. It does not compete with incoming-page preparation for a deferred
job slot. Optional lookahead is deferred, as before. Focus/UIA requests continue
using their explicit realization intents and can wait for a target without
publishing speculative focus.

Normal visible frames realize visible/protected items only when the host's
deferred-lookahead policy is active. They retain already measured adjacent items
within the existing one-line window, but do not synchronously create cold adjacent
items. Thus cached measurement lifetime and realized visual lifetime are distinct.
Provider boundaries still stop movement at the admitted range; there is no
accumulated distance to replay on page arrival.

For free scrolling, a frame captures the last displayed visible key and applies
the accepted input delta to its viewport position. This intent is stable through
layout correction passes. Choosing an anchor after applying the numeric offset
could instead select a newly exposed, estimated row and move existing content
when its real height became known. Explicit focus/UIA reveal retains its own
target-follow semantics.

The host's existing preparation order can now give incoming pages time without
blocking loaded movement. There is no new unconditional current-work-first loop
that starves incoming admission. Time budgeting remains an estimate; an
indivisible visible layout or paint can still exceed a frame's available time.

## Regression gates

The default renderer suite includes the page-arrival regression; the isolated
`DeclarativeRendererTests --page-arrival-reversal` entry point covers the same
matrix. It covers append/prepend, delayed
arrival, partial grid rows, cursor-window eviction, variable item heights, and
100/125/200% scaling. It uses the real renderer with deterministic one-job dispatch
to expose contention. Required gates are no held loaded movement, preserved
visible anchors, completed admission during motion and geometry matching the
synchronous reference. The original fixed-height cases also require equal numeric
offsets. With variable heights, different offscreen measurement knowledge may
change the numeric prefix sum, so visible identities/positions and per-frame
movement are the authoritative checks. Sub-pixel clipping noise below 0.01 DIP
does not count as a visible item. Rounding tolerance for displacement is one
physical pixel, not an allowance for a row-sized jump.

The existing suite additionally checks cancellation, reset generation, same-key
content changes, incompatible widths, old-scene repaint, focus/UIA and publication
rejection. Production-styled Playnite admission probes compare geometry, focus,
accessibility, modal return and pixels against full layout; YouTube probes exercise
remembered first/sixth/seventh/later targets and host motion options.

Evidence is under `artifacts/flutter-scrolling-review/`. Native CPU timings and
offscreen WIC tests do not establish display FPS or physical scrolling acceptance.
The next candidate must still be tested at page edges, through reversal and with
the user's actual artwork/loading workload.
