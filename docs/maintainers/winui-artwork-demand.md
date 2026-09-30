# Native artwork sizing and lifetime

Each image or background consumer owns a `NativeArtworkDemand`. It observes its
native arranged size, `XamlRoot` rasterization changes, and the overlay's interface
scale. The request includes the declared focus/press scale envelope of its native
ancestors, without sampling their transient facade/compositor scales. Small
physical-pixel buckets avoid redecoding for rounding differences. A consumer's
sufficient retained decode satisfies smaller requests; resize reversal does not
decode a smaller image and then immediately restore the larger one. This local
high-water size remains capped by the protocol source bounds and retires with the
consumer/source identity.
There is no frame callback, pixel cache, or application-owned codec thread.

`NativeArtworkDecoder` uses Windows `BitmapDecoder` metadata to enforce the
protocol's encoded-size, source-dimension and source-pixel limits before passing
the stream to WinUI. It configures physical `BitmapImage` decode dimensions before
`SetSourceAsync`. Cover and contain preserve aspect ratio; fill has independent
dimensions. Requests never upscale beyond the source. An intrinsically sized
image without an arranged dimension retains its natural sizing behavior.

The current image remains attached until the replacement has decoded. Posters,
modal images and background surfaces have independent demand sizes. Resizing or
changing DPI can replace a smaller decode, including without another widget frame.
An unchanged size and source do not reacquire or decode. Cancellation, suspension,
source replacement and native disposal prevent late publication. A retained
ordinary image's next size upgrade uses the latest displayed snapshot authority.
Indexed and pinned resource resolution keeps its existing lease/selection checks.
Background and focus-fragment demands also capture the exact indexed source's
current-authority predicate through final native publication. A retired resolver's
null or failure is not a current declaration of missing artwork: it must preserve
the prior pixels until a newly admitted source supplies a result. An authoritative
missing-image result still clears the source. The native presentation-surface
fixture exercises a pending source that retires before returning null, followed
by a genuinely current missing-artwork selection.

Array-backed, value-owned encoded bytes are exposed through a read-only stream;
they are not copied again by the presenter. The payload remains alive for the
native asynchronous operation. Inline base64 conversion runs outside the UI thread.
HTTPS images use the Windows HTTP client with headers-first, bounded asynchronous
streaming. Unknown-length responses stop after the encoded limit plus one byte;
oversized declared lengths are rejected before reading the body. Metadata bounds
and actual asynchronous completion/cancellation then follow the same path. This path does
not claim WinUI's URI-based decoded-resource cache; no new shared cache is added.

`NativeArtworkCounters.Snapshot()` reports cumulative requests, pixel-decode
starts, completed/cancelled/failed demands, admitted encoded bytes, fallback copy
bytes, and natural-versus-configured pixel bytes. Layout diagnostics include that
snapshot. Pixel totals are request estimates, not measured private commit, GPU
allocation, or live retained memory. They must not be presented as a benchmark.

The native style fixture includes large artwork at 100%, 112.5% and 125% interface
scale on the current display DPI, repeated demand, a delayed size replacement,
late cancellation, different poster/background sizes and oversized source rejection.
Existing surface and indexed fixtures retain authority, recycling, and modal tests.
Actual cross-monitor DPI changes require a separate monitor-capable check.

Resource admission requires current ownership and an attached XamlRoot, not the
IsLoaded flag. The native fixture reproduced a ContentControl with valid arranged
dimensions and a root while that flag was still false; no later size notification
retried its skipped demand. Decode can start safely before Loaded, with the same
source/lifecycle checks before publishing.

The focused native fixture (`--validate-styles --artwork-only`) passes 28 checks
at 125% Windows rasterization scale. The fixture uses supported interface scales
through 125%; an earlier requested 200% value was clamped and was not valid
coverage. Uniform-fill Image.ActualWidth/Height describe the native image extent,
which can exceed its requested box; reversal checks compare the settled native
extent rather than asserting it equals the requested Width property. Temporary
admission tracing was removed. Evidence: `artifacts/winui-shell/artwork-root-native/`.

The full native style suite also passes all 199 checks in the artwork lane. Combined-frontend and actual-widget qualification remain separate.

The combined accessibility run exposed a zoom ordering race: InterfaceScale
invalidated native measurement but left the child's ScaleTransform unchanged
until that measurement ran. Artwork's queued size observer could run first,
retain the lower-resolution decode, and receive no later size event for a
fixed-size image. `OverlayScaleRoot` now publishes the matching transform in
the scale-property callback and still invalidates layout normally.

The isolated `artwork-zoom-red-01` run reproduced the stale target before another
layout pass. Immediate-target checks at 112.5%/125%, decode upgrading, repeated
focus/press reuse, replacement and reversal all pass in the combined 206-check
`retained-accessibility-green-02` run. Both are under `artifacts/winui-shell/`.
The correction neither forces synchronous layout nor redecodes on focus motion.

## Declared background continuity, 2026-09-29

Background source selection and decoded paint retention have distinct lifetimes.
Removing a source retires its semantic selection and demand, but a retaining
background keeps decoded pixels until a replacement is ready. Missing/empty
artwork cannot erase them. Opting out clears them; surface removal, scope changes,
enclosing presentation-owner changes and runtime retirement do not inherit them.
There is no widget-wide last-image cache and no pairing of unrelated removed/added
surface IDs. Transition recreation can copy decoded paint only for an identical
surface declaration and ownership chain. Current fit updates still reach decoded
artwork, without restoring retired request authority.

Playnite Home and Library now declare the same cinematic surface ID under the
same root. Scoped source memory remains in `PresentationSourceCoordinator`;
decoded retention belongs to `WidgetPresentationSurface`.

Evidence: `artifacts/winui-shell/scoped-background-20260929/`. The native surface
fixture passes 43 checks, covering removed sources, missing/delayed images, nested
boundaries, changed scopes/enclosing owners, runtime replacement, teardown,
late completions and crossfade lifetime. The state coordinator passes 28 tests;
Playnite passes 157. Actual Home, Library and details screenshots were captured
using the isolated candidate and the updated Playnite 0.2.104 package.
