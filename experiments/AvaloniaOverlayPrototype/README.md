# Avalonia Overlay Prototype — AVP-004-INTEGRATION

This .NET 10/Avalonia 12.1.1 candidate replaces only the production
presentation boundary. It is not a production cutover. The default executable
contains no representative domain pages, fake remote endpoint, experiment-owned
XInput reader, widget-specific renderer, transport, schema, GameInput owner, or
second HWND.

The candidate launches the existing packaged `WidgetBridge`, connects through
`WidgetPresentationSession`, and admits the ordinary bundled and installed
Community catalog. One `SemanticTreeRenderer` maps every current
`WidgetProtocol.ViewNodeKind` to standard Avalonia controls. Ordinary installed
catalog evidence reports only the kinds those snapshots actually emit; final
coverage combines it with the exact-commit focused all-kind mapping test rather
than claiming absent ordinary Slider/Spacer/ActionSurface nodes. It preserves raw
widget/runtime/presentation/session/snapshot/input-scope/node/action/collection/
focus identity and revalidates the latest node/action tuple before the managed
session performs its exact authority check. Large Scroll and Grid collections
use a recycling `ListBox`; no custom accessibility tree or container cache is
present.

`OverlayPlatformClient` consumes accepted `OverlayPlatformInterop` ABI v1. The
native component remains the sole GameInput/Guide/device/repeat/neutral/
foreground/placement owner, while Avalonia supplies its one HWND and one focus
tree. Controller directions, A/B, contextual actions, tray Y short/hold,
keyboard input, tray navigation, sliders, and Back converge on the shared shell
and managed presentation session. The old AVP-002 Vortice dependency is retired.

The shell retains a stationary scrolling tray, page focus memory, Avalonia
FocusManager/XYFocus spatial movement, same-HWND text-entry modal, status and
controller-guide layers, native `TransitioningContentControl`/`CrossFade`, and
reduced motion. A latest-wins admission pump serializes transitions, coalesces
same-widget snapshots, and admits authority/focus only while the exact
destination remains current. Superseded trees are removed, artwork is canceled,
and decoded bitmaps are disposed. Responsive visibility uses the current logical surface;
non-Scroll roots receive a host ScrollViewer so compact content stays reachable.
Advanced presentation uses only the closed protocol kind/preset/slot enums and
never package, widget, element, provider, or tree-shape identity.

See [architecture-decision.md](architecture-decision.md) for the responsibility
map and retained manual-composition decision.

## Focused verification

From the repository root, run the one bounded final Release/compiled-binding/
MSTest.Sdk 4.3.2 suite (15 tests):

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Verify-Avp004.ps1 -TimeoutSeconds 240
```

The focused suite covers every node kind, latest-frame exact action authority,
explicit worker-to-UI publication, 10,000-item bounded realization, stable
collection/UIA identity, compact/standard/wide layouts at actual Avalonia
100/125/150-percent render scales, lifecycle switch/hide/show, exact controller
authority, stationary dynamic tray, content entry/Back focus restoration,
start/mid/end transition surface diagnostics, same/new-widget supersession,
artwork cancellation/disposal, slider quantization, ABI layout, and the absence
of Vortice. The suite retains an ignored exact-commit focused proof consumed by
the final measurement.

After the coherent commit, run exactly one ordinary exact-commit measurement:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Measure-Avp004.ps1 -TimeoutSeconds 240
```

It rebuilds the retained product package without tests, publishes a copied
candidate, cycles every currently installed catalog widget through the ordinary
bridge/runtime/domain path and same adapter, exercises 420x340, 978x466,
standard, and wide logical Avalonia viewports over one backing surface, records
UIA/containment/scroll clipping and transition phases, then separately samples
the candidate and complete bridge/worker process tree visible and hidden.
Ownership checkpoints separate managed live/heap bytes, decoded
bitmap count/bytes, tracked render trees, pending artwork, and remaining
native/Skia/render-target/other unattributed candidate memory. The ignored exact runtime and JSON are under
`artifacts/avp004`; see [evidence/README.md](evidence/README.md).

## Visible planner/user launch

```powershell
& .\experiments\AvaloniaOverlayPrototype\artifacts\avp004\runtime-win-x64\AvaloniaOverlayPrototype.exe `
  --installation .\experiments\AvaloniaOverlayPrototype\artifacts\avp004\runtime-win-x64
```

Close the production OverlayHost before physical evaluation so only one process
owns the extracted platform service. Physical Guide/controller feel, compositor
transparency, mixed-monitor DPI/clipping, and credential-gated Community behavior
remain planner/user verdicts. The candidate records whether the visible process
is below 350 MiB and enforces the assigned 500-MiB candidate ceiling. The
complete ordinary runtime tree remains reported separately. GPU and native
allocation traces were unavailable because Windows system-performance profiling
policy rejected the bounded WPR probe; the in-process top-level Skia lease was
also unavailable, and both limits are retained rather than misattributed.

The performance audit follows Avalonia's official guidance: large lists remain
height-constrained and recycling with uniform 88-DIP rows and Avalonia 12.1.1's
supported zero-extra-viewport buffer default (that version does not expose the
newer `BufferFactor` property);
decorative text/icons/images skip hit testing; superseded pages are removed
rather than retained at opacity zero; the unnecessary page-host clip is gone;
and compiled bindings remain enabled. No `BitmapCache` or Skia GPU-cache increase
was added because both can increase memory and require supporting measurement.
Phase isolation associated the private-byte jump with CrossFade overlapping
top-level resize and with whole-tree compact/expanded reprojection—not the
managed heap, decoded artwork, pending artwork, tracked trees, or an observable
Skia cache. Production placement now applies after exact frame admission, while
responsive visibility switches only protocol-declared nodes in place with
`IsVisible`. Production launches still apply native DPI/work-area surface hints;
the exact responsive matrix records each real logical viewport and its fixed
backing-surface dimensions.
