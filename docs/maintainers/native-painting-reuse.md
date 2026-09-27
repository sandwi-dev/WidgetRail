# Native painting reuse

WIDGE-295 keeps Direct2D/DirectWrite/DirectComposition. It applies bounded resource
reuse and immutable preparation; no graphics-library or SDK contract changes.

## Resource and preparation ownership

`paint::Resources` belongs to the renderer's existing resource domain. Solid
brushes, gradient brushes/stop collections and local rounded-rectangle geometries
use exact inputs and independent 64-entry LRU limits. Brushes remain immutable;
opacity and positions are part of their keys, not mutable shared drawing state.
Compatible capture targets share the domain. A genuinely changed device/domain or
explicit retirement clears the cache; replacing a context on the same device does
not invalidate compatible resources. Outstanding COM leases survive cache eviction.
Gradient brushes can retain evicted stop collections, so cached entry counts are
not a claim about the number or size of all driver allocations.

Rounded tile clips use Direct2D-managed layers on `ID2D1DeviceContext`. The legacy
render-target path still owns one layer per active nesting depth. Immutable mask
geometry is shared across compatible capture targets; clips, transforms and painter
order are unchanged. Existing bounded nine-slice shadow masks remain in use, and
their solid brushes now share the resource cache.

For a matching immutable source and admitted layout, base paint preparation can
reuse committed styles while rebinding current semantic pointers. Viewport,
accessibility, inherited contexts and layout-bearing presentation identities must
match. Current focus/press styles are resolved normally. This removes adaptation
work from unchanged controls without retaining old actions or selected content.

Composition cache hits move the already-validated immutable identity into the next
frame instead of duplicating it. Capture-pool selection uses stored dimensions,
refreshing the selected target's logical size when applying DPI, rather than
querying every pooled COM target. Existing independent captures, exact pixel
identities, surface leases and admission limits are retained. No additional flushes
or capture copies were introduced.

The design follows [Microsoft's Direct2D performance guidance](https://learn.microsoft.com/en-us/windows/win32/direct2d/improving-direct2d-performance):
reuse device resources, retain buffered-command resource lifetimes, and let device
contexts manage layers. The assessment's existing Skia/Compose references informed
the immutable preparation and bounded retention approach; no third-party code was copied.

## Measurements

Three fresh-process paired runs per exported production fixture, alternating order.
Each run performs 96 focus and 96 scrolling samples at 980×700 DIPs and 125% scale.
Depth, themed styles and compositor captures are enabled. The same-build
`no-paint-reuse` mode disables resource reuse, managed clip layers and unchanged
style preparation. Capture housekeeping improvements are present in both modes.
Values below are medians of the three run-level statistics, in milliseconds.

| Fixture | Focus CPU p50, bypass → enabled | Focus CPU p95 | Scroll CPU p50 | Scroll CPU p95 |
|---|---:|---:|---:|---:|
| Playnite Library, 150 items | 23.01 → 21.25 | 26.22 → 24.28 | 10.38 → 10.06 | 12.37 → 11.43 |
| Spotify Queue, 50 items | 7.67 → 7.05 | 8.66 → 7.94 | 5.14 → 4.82 | 5.61 → 5.52 |
| Games and Apps, 64 items | 10.33 → 9.31 | 11.24 → 10.17 | 6.29 → 5.66 | 7.09 → 6.49 |

In Playnite's 96 focus frames, enabled reuse replaces 1,968 solid-brush creations,
1,120 gradient/stop pairs and 1,904 clip-geometry creations with cache hits. Explicit
application layer creations fall from 1,904 to zero; Direct2D's internal allocation
is not measured. Most other warm phases create no brushes/geometries, with one new
gradient position in the Spotify and Games and Apps scroll runs.

These are offscreen WIC CPU measurements, including preparation and recording;
they do not measure hardware GPU execution, compositor completion or live FPS.
Raw evidence is under `artifacts/native-painting-reuse/final-*-{off,on}.log`.
Aggregate image/surface budgets remain WIDGE-296's responsibility.

## Verification

Renderer comparisons cover fractional scale, wrapping, alpha, gradients,
focus/press/depth, rounded tile clipping, cached versus uncached pixels, target
replacement, explicit retirement, palette churn and outstanding resource leases.
Seven exported Playnite/Spotify/Games and Apps fixtures match full-layout pixels
and geometry over 20 stateful focus/scroll/UIA frames at three widths and two scales.
The renderer suite passes 47,819 checks. Composition/chrome gates (including 49,446
chrome checks), 409 pinned-owner checks and the normal Release host/runtime build
pass. No normal overlay launch, installation or hardware frame-rate claim is part
of this verification.

Reproduce paired profiles with `ScrollWorkloadProbe <fixture> --profile <count>
realized`, adding `no-paint-reuse` for the bypass. Build the probe using the native
`build.ps1 -ScrollWorkloadFixture <fixture>` entry point.

## Loading feedback follow-up

Collection loading chrome previously captured the entire collection viewport and
advanced its spinner only when another event repainted the widget. Page admission
could therefore make the spinner visibly hesitate even while the application was
otherwise idle between frames.

Loading badges now have separate bounded captures for their stationary background
and label, their rotating arc, and the scrollbar. The arc uses the native
`IndeterminateRotation` composition group with a canonical cached raster. Its
900 ms loop runs on DirectComposition without host paint ticks or repeated uploads.
Ordinary scene refreshes and placement changes preserve the loop; input-scope
replacement starts a fresh owner. Completion removes it. Reduced motion retains a
static arc. Loading feedback keeps the existing fixed cadence independently of the
transition-speed setting. The decoration never remaps input or changes layout.

Standalone `loadingIndicator` nodes use the same mechanism. Rounded ancestor clips
and authored translations retain the conservative painter path; direct rendering
now requests the existing animation cadence for collection badges too. These are
host changes: widget authors continue to declare collection loading state or use
the existing loading-indicator component. No SDK/package changes are needed.

At 125% scale, the fixture's badge and arc occupy 32,304 captured bytes (39,328 with
its scrollbar), rather than a viewport-sized capture. Time-only redraws reuse the
captures with zero painted bytes. This is capture work eliminated, not an FPS claim.
The physical trace that motivated this fix still had page-admission CPU frames of
20.338 ms median and 23.228 ms p95; this change does not establish that all admission
work now fits a display frame.

Validation: 52,507 renderer checks, 49,503 chrome checks, 517 pinned-host checks,
and 409 surface-coordinator checks pass. The
Release host/runtime build also passes; physical acceptance remains separate. The
`OverlayChromeTests --loading-indicator-pixels` probe verifies visible rotation at
100%, 125%, and 200% scale while the host thread sleeps through multiple rotation
cycles without further application painting, uploads, or commits. It also checks
reduced motion and pixel removal. The probe pumps window/composition startup
messages before intentionally blocking; immediately sleeping after creating the
window previously sampled the initial arc before animation startup. Non-pixel
checks cover clock reuse, scope retirement, input neutrality, fallback cadence,
capture bounds, loading direction and idle cache reuse. Evidence is under
`artifacts/native-loading-indicator/final-*.log`.
