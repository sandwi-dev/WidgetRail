# Native frame scheduling and incremental composition (WIDGE-299)

Baseline: native integration `06fb530b`. The compositor spinner was physically
smooth, but ordinary scrolling still cost about 10 ms of host CPU work and the
last physical sample's page-change frames cost 21.7 ms median. Those observations
are not display FPS or GPU execution measurements.

## Preparation ownership

The open host opts into deferred scroll preparation. Input planning checks
committed visible/protected coverage and coalesces one speculative viewport
request. Covered movement is submitted immediately; missing required geometry
holds the current offset. The request contains source, scope, focus, viewport and
layout options. Timer work warms it without publishing input geometry or replaying
accumulated movement. Replacement, scale/viewport change, focus change and explicit
free-scroll retirement cancel obsolete requests.

`PreparationFrameBudget` computes available CPU time from the DWM vblank clock and
refresh period. It gives outstanding paints priority, leaves up to 1.5 ms (one
quarter frame) as a deadline margin, caps a slice at 3 ms and uses observed cost to decline work that
does not fit. Missing timing uses a conservative 1 ms fallback. Focus, UIA,
incoming snapshots and scroll preparation share that allowance. An indivisible
measurement can still overrun it; this is not a hard real-time guarantee. The
timer is armed once, so repeated controller samples cannot postpone it forever.
Preparation also gets an opportunity after paint submission because Win32 gives
paint messages priority over timers. Work deferred for 100 ms may execute one
indivisible measurement with less headroom; a high-refresh display must not cause
permanent starvation when the measured cost exceeds every available frame window.

Speculative preparation now retains tentative scroll state together with its
matching collection extent/anchor model. Restarting every slice from the visible
viewport discarded progress made while revealing offscreen focus. The checkpoint
is reusable only with the same immutable source, target, exact viewport,
measurement/presentation options and unchanged committed scroll geometry. LRU
access timestamps do not invalidate geometry. Live scrolling or a changed request
discards that checkpoint. Nothing becomes authoritative until normal frame commit.

Ready preparation retains its final layout, text measurement proofs and base
styles. The admitting frame rebinds semantic pointers, republishes logical
collection navigation, and resolves current presentation. It skips the redundant
outer layout rebuild only when the same proof still holds. Changed options,
selection sources or viewport take the existing full path. Storage remains bounded
to the two existing preparation branches and the realized content within them.

## Section entry and loading boundaries

An incoming one-shot focus-group request takes precedence over the outgoing
section's remembered focus. Preparation first discovers the incoming group's
logical children without following an old/recycled focus ID, then prepares the
requested remembered/default child. Discovery neither consumes the request nor
publishes focus. Consumed requests use ordinary focus restoration.

Live YouTube Music debugger samples found a pending snapshot and an incomplete
preparation for `item.6`, with no worker request in flight. This corresponds to the
reported threshold where section switching stopped once focus needed revealing.
The actual YouTube layout probe now exercises preparation without painting or
another input at the first, sixth, seventh and later items, at compact/preferred
widths, fractional host viewport sizes, and 100/125/200% scaling. Physical
confirmation of the complete host section-switch path remains separate.

When a widget withdraws page actions during loading, the declared loading edge
still retains directional focus inside its scroll. A missing action and a retired
host request latch no longer make that edge a geometric exit to header controls.
An in-flight request also stays in flight while that edge declares loading;
temporarily hiding its action is not recorded as a visible-page completion.
The opposite direction and settled terminal edges retain ordinary navigation;
explicit authored focus links keep their existing precedence.

## Compositor updates

The presenter retains each parent's ordered child list with COM ownership. It
detaches removed/reparented children before inserting or moving edges and preserves
all unchanged edges. Focus-effect internal hierarchy uses the same reconciliation.
The outer root stays attached when its parent and stacking anchor are unchanged.
Raster content/placement setters and group clips are updated only when necessary.

Animation retirement and API failure invalidate the retained topology proof. The
next application reconstructs it conservatively. Existing paint order, clipping,
effect graphs, animation clocks and resource leases are preserved. Counters expose
visual adds/removes/resets separately from raster uploads and reuse.

## Evidence and limits

Evidence is retained under `artifacts/native-frame-scheduling`. Paired preparation
tests use the same warmed item measurements with ready-frame reuse enabled and
disabled and require exact visible pixels. The final 18-fixture Debug run averaged
0.647 ms preparation with reuse versus 1.380 ms rebuilding. These are small native
CPU fixtures, not a claim of the same reduction in the live application.

The cadence tests cover lists/grids, both axes, 100/125% scaling and direction
reversal. Both synchronous and deferred preparation advance all 120 covered
samples without a hold, with exact reference pixels/offsets. Cancellation tests
ensure an obsolete preparation cannot jump the viewport. Existing page/anchor,
UIA, old-scene repaint, frame rejection, resource and pinned-owner gates remain
required. Pixel probes cover independent spinner rotation and reduced motion;
compositor tests check unchanged edges, insertion, reparenting and retirement.

The quiet trace records `collection-preparation` (resume, measurements and cost),
`preparation-slice` (allowance and actual cost), `prepared-frame-reused`, and
compositor edge mutations. Physical follow-up should distinguish source loading
latency from required-geometry holds and CPU/GPU submission costs.

No SDK/wire contract or widget package update is required. Widget authors continue
to provide stable keys, collection metadata, loading state and one-shot focus
requests; they do not choose frame budgets or schedule native preparation.

Final automated gates pass: 57,253 renderer checks, 49,523 chrome checks, 419
interaction checks, 517 pinned-host checks and 409 surface-coordinator checks.
YouTube Music's 27 managed checks and production-styled layout/preparation probe
pass. Desktop pixel checks pass for sections, dialogs, popups, focus fades and
loading indicators. The section/popup probe now pumps HWND startup messages before
starting its measured interval, matching the spinner probe; it still pauses the
host thread throughout that interval. The Release host/runtime bundle builds.

## Post-candidate corrections

The physical follow-up confirmed YouTube section switching works, but exposed
two preparation integration defects. DWM's `qpcVBlank` was ahead of the caller's
clock in all 300 live probe samples, despite a valid 240 Hz refresh period. The
budget now normalizes past or future references onto the same cadence, rather
than treating future timestamps as missing timing. The bounded fallback for
missing/invalid periods also honors cost admission and aged-work promotion.
Microsoft documents `qpcVBlank` as the QPC value before vertical blank, without
requiring it to precede the caller's sample:
[DWM_TIMING_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info).
This remains a scheduling estimate, not a guaranteed frame deadline.

The host now resolves compositor eligibility and global motion settings through
`ApplyWidgetRenderMotionPolicy` for both preparation and painting, using the
actual candidate snapshot. Previously only painting received those settings,
so the exact preparation-context check rejected ready frames. Embedded media
and non-compositor/inert surfaces keep their conservative path. The renderer's
reuse checks remain unchanged; settings, viewport, source, focus and scroll-policy
changes still reject stale preparation.

The correction passes 57,318 renderer checks and 50,410 chrome checks, including
past/future cadence equivalence at several refresh rates, deadline margins,
fallback starvation prevention and host-policy adoption/invalidation. The
production-styled YouTube fixture checks reuse at first/sixth/seventh/later items,
compact/preferred viewports and 100/125/200% scaling. A repeat live timing probe
sent none of its 300 valid future samples through the fixed fallback. These
results establish the corrections, not elimination of all scrolling stutter;
physical performance acceptance remains separate.
