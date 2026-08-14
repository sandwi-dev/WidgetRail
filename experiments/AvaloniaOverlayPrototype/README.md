# Avalonia Overlay Prototype — AVP-004-REDESIGN

This .NET 10/Avalonia 12.1.1 candidate replaces only the production
presentation boundary. It is not a production cutover. The default executable
contains no representative domain pages, fake remote endpoint, experiment-owned
XInput reader, widget-specific renderer, transport, schema, GameInput owner, or
second HWND.

The candidate launches the existing packaged `WidgetBridge`, connects through
`WidgetPresentationSession`, and admits the ordinary bundled and installed
Community catalog. One `SemanticTreeRenderer` plus a closed
`ControllerComponentCompiler` maps every current `WidgetProtocol.ViewNodeKind`
and its typed axis/orientation/responsive/advanced-slot properties to standard
Avalonia controls and ControlThemes. Widget identity, IDs, text, providers,
tree shape, authored style classes, and bridge-computed geometry never select a
component or layout. Ordinary installed
catalog evidence reports only the kinds those snapshots actually emit; final
coverage combines it with the exact-commit focused all-kind mapping test rather
than claiming absent ordinary Slider/Spacer/ActionSurface nodes. It preserves raw
widget/runtime/presentation/session/snapshot/input-scope/node/action/collection/
focus identity and revalidates the latest node/action tuple before the managed
session performs its exact authority check. Large Scroll and Grid collections
use a recycling `ListBox`; no custom accessibility tree or container cache is
present.

`WidgetPresentationSession` is the sole invalidation-refresh owner. The AVP
coordinator consumes its coalesced `PresentationChanged`/last-good publication
and does not launch a second `RefreshAsync` whose stale loser could become a
visible failure.

`OverlayPlatformClient` consumes accepted `OverlayPlatformInterop` ABI v1. The
native component remains the sole GameInput/Guide/device/repeat/neutral/
foreground/placement owner, while Avalonia supplies its one HWND and one focus
tree. Controller directions, A/B, contextual actions, tray Y short/hold,
keyboard input, tray navigation, sliders, and Back converge on the shared shell
and managed presentation session. The Avalonia host now admits the native
visible GameInput lease even when Windows declines foreground activation; it
still attempts activation once on Guide show, restores managed focus, and never
adds a second reader or foreground-steal loop. A bounded JSON trace records
native lease/connection changes and semantic routing decisions. The old
AVP-002 Vortice dependency is retired.

Each admitted current view keeps its authored semantic structure and validated
preferred/minimum logical envelope. The host resolves that atomic pair against
the active work area, render scale, accessibility scale, and platform safe
insets, then sizes one HWND to only the union of content and stationary chrome.
When a physical work area cannot contain an authored minimum plus fixed chrome,
the resolver clamps only the unavailable axis; it does not unnecessarily shrink
the other usable axis, and the generic scroll owner preserves content reachability.
There is no monitor-sized backdrop or replacement compact/standard/wide window
preset table. The content grows upward and outward around one bottom-center
screen anchor; the scrolling tray and controller guide keep identical absolute
screen bounds while the content envelope and HWND change. The admitted shell
layout is resolved in one UI turn and the sole platform client applies x/y/
width/height with one native `SetWindowPos`, rather than exposing four separate
top-level property mutations. Start/mid/completion transition diagnostics retain
the absolute tray and guide pixel rectangles. Status and modal
remain host-owned layers outside the content-only native
`TransitioningContentControl`/`CrossFade`. A first-class tray/content/modal state
machine uses Avalonia FocusManager/XYFocus and typed component zones for tray
cycling, explicit content entry/exit, sliders, collection navigation, and
per-widget focus memory. A latest-wins admission pump serializes transitions, coalesces
same-widget snapshots, and admits authority/focus only while the exact
destination remains current. Superseded trees are removed, artwork is canceled,
and decoded bitmaps are disposed. Responsive visibility uses the current logical surface;
focus remains only on effectively visible controls, remembers valid identities
per compact/expanded mode, and falls back within the current page or selected
tray when a branch becomes hidden. Hidden responsive branches remain outside
UIA and XYFocus. An explicit semantic Scroll or large Grid owns its requested
axis; otherwise the page supplies the single vertical scrolling owner. When a
vertical root stack contains a direct semantic vertical Scroll, its siblings and
the Scroll children are compiled into one recycling page-level list. This keeps
headers, tabs, and collection items reachable without nesting another catch-all
scroller around the semantic owner.
Advanced presentation uses only the closed protocol kind/preset/slot enums and
never package, widget, element, provider, or tree-shape identity.

The presentation boundary deliberately does not apply bridge-computed layout or
authored style-class roles. Avalonia owns intrinsic measurement and each
admitted semantic root's outer allocation, so legacy `100vw`/`100vh` and root
max-width declarations cannot collapse the page into a literal 100-DIP or capped
desktop column. Rows wrap, ordinary grids recompute standard
Avalonia `UniformGrid` columns from their real arranged width, action surfaces
and virtualized lists stretch, and no fixed 760-DIP column assumption remains.
Content, controller guide, and stationary tray occupy separate fixed-contract grid
rows. The reusable semantic theme supplies consistent surface, typography,
tile, button, slider, list, focus, selection, artwork, and status treatment
without a GBSS renderer or widget-identity branch.
The tray sizes labels to their ordinary text, scrolls horizontally when eight
items do not fit, and re-reveals the exact selected item after selection and
post-layout envelope changes instead of clipping it. Chrome metrics do not vary
with widget responsive mode.
Buttons, action surfaces, sections, collections, status, loading, text entry,
sliders, page headers, list selection, and focus receive reusable
controller-first themes derived only from typed semantic kinds. Raw widget
styling cannot erase interactive minimums or create hidden presentation roles.

The executable holds a named prototype-only single-instance mutex. Normal close
has an eight-second outer bound, records the exact WidgetBridge/worker descendant
set, and fails retained acceptance if graceful shell/session/process-tree
shutdown needs forced termination or leaves an owned PID alive.

See [architecture-decision.md](architecture-decision.md) for the responsibility
map and retained manual-composition decision.

## Focused verification

From the repository root, run the one bounded final Release/compiled-binding/
MSTest.Sdk 4.3.2 suite (32 tests):

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Verify-Avp004.ps1 -TimeoutSeconds 240
```

The focused suite covers every node kind and compiled component kind/zone,
atomic surface-hint admission, work-area/DPI/accessibility clamping, four
materially distinct envelopes, invariant absolute tray/guide geometry,
content-only transition ownership, latest-frame exact action authority,
explicit worker-to-UI publication, 10,000-item bounded realization, stable
collection/UIA identity, all eight installed widgets through compact 420, 978,
1180, and 1440 logical work-area admission (32 responsive rows), plus focused
100/125/150-percent Avalonia render-scale coverage, lifecycle switch/hide/show, exact controller
authority, stationary dynamic tray, content entry/Back focus restoration,
start/mid/end transition surface diagnostics, same/new-widget supersession,
artwork cancellation/disposal, slider quantization, ABI layout, and the absence
of Vortice. It also proves session-owned out-of-order invalidation coalescing and
compact/expanded focus migration, hidden-branch UIA/XYFocus exclusion, and exact
mode-identity restoration with one render tree. Geometry coverage asserts useful
page/root width, readable controls, effective visibility, bounded unintended
horizontal empty area, and non-overlapping content/guide/tray regions at the
supported sizes/scales; a direct guard test rejects a second prototype owner.
The trace regressions use a deterministic publication clock to prove a rapid
32-entry repeat-like burst produces one latest-state publication after the
bounded 225 ms live window, with explicit flush retaining every sequence once
in order. They also hold the live target without `FileShare.Delete`,
require denied replacement to preserve valid last-good JSON without throwing on
the UI path, then require a bounded retry to publish every sequence exactly once
in order with no unique-temp residue.
Each of the 32 responsive rows derives its content constraint through the same
work-area/DPI/accessibility envelope admission used by the live host; authored
preferred/minimum hints remain authoritative and no global shell preset is
introduced. Each row captures its admitted authority, semantic root, expected
IDs, controls, and geometry atomically on the Avalonia UI thread after the
viewport receives a real Avalonia render turn; a direct race test publishes a
replacement during resize and requires the replacement frame, root, and controls
to match before evidence is accepted. Any missing expected IDs are retained per
row for diagnosis rather than hidden by a summary boolean.
Every protocol `Scroll` is classified as virtualized because the generic
renderer realizes it through a recycling list; only a `Grid` also uses the
greater-than-64 threshold. Expected focusable controls below the host-owned
outer viewport are probed only when absent or non-contained in the seed capture.
Enabled targets must accept focus and remain the FocusManager's exact semantic
identity after reveal; disabled targets prove visible contained bounds and UIA
without claiming focus. Original offsets and focus are restored once per fixture.
Pre/post-probe ownership checkpoints retain candidate, managed, available Skia,
render-target, and native/unattributed residency. The evidence viewport then
reapplies the existing host-owned semantic root allocation invariant so probing
cannot reintroduce an authored root max-width into the retained geometry row.
The direct tray regression switches all eight representative long/short labels
through materially different authored envelopes and requires identical anchored
tray/guide bounds. The selected first and final items remain fully inside the
scroll viewport after layout settles at 420, 978, 1180, and 1440 admitted
content widths; label content is not truncated and chrome metrics stay fixed. Direct renderer
coverage also requires an empty typed Text node with an accessibility label to
produce readable nonzero-area display text. Mixed fixed/direct-Scroll roots keep
the typed Scroll identity and inherited compact/expanded visibility while their
children share the one outer virtualized owner.
The suite retains an ignored exact-commit focused proof consumed by
the final measurement.

After the coherent commit, run exactly one ordinary exact-commit measurement:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Measure-Avp004.ps1 -TimeoutSeconds 240
```

It rebuilds the retained product package without tests, publishes a copied
candidate, cycles every currently installed catalog widget through the ordinary
bridge/runtime/domain path and same adapter, admits that current view's authored
preferred/minimum envelope against the real work area/DPI, records UIA/
containment/scroll clipping and transition phases, then separately samples
the candidate and complete bridge/worker process tree visible and hidden.
Every responsive record also retains its named logical work-area fixture,
page/root width utilization, minimum
readable dimensions, shell-region overlap, effective visibility, and maximum
unintended horizontal empty-area ratio. Geometry is sampled only after the
content-envelope transition reaches its admitted authority/size. Transition
start/mid/completion rows retain chrome pixel bounds and require one invariant
absolute tray rectangle and one invariant absolute guide rectangle across the
ordinary eight-widget traversal, including retained compact-to-wide and
wide-to-compact switches; chrome pixel bounds use the native
center-rounding convention. Final evidence includes the exact owned
process-tree normal-shutdown verdict and native visible-lease trace. The same
ordinary traversal renders one honestly labeled offscreen PNG at the admitted
authored envelope per installed widget with
logical/pixel size, render scale, and SHA-256 provenance; no taskbar or
targetability mode is used.
The input artifact records only actual native lease/controller observations.
It does not stamp focused-suite success into synthetic handled categories, and
it does not convert a visible lease into a physical-controller or routed-input
claim. Named focused regressions remain test evidence, not native trace events.
When `--input-trace` is supplied, each bounded state/routing record updates
in-memory sequence truth and signals one coalescing background publisher; the
UI/input path never serializes or replaces the JSON file. Live publication is
debounced for 225 ms so repeat bursts produce periodic latest-state snapshots,
while explicit flush bypasses that delay and retains its existing two-second
bound. The publisher uses a
unique temporary file and atomic replacement, retaining the last-good complete
prefix when replacement is denied. A later record or explicit bounded flush
retries the pending latest state. Expected file-access failures remain visible
in the final flush verdict without escaping through interactive input or normal
window shutdown, and exact measurement requires the final requested sequence
to be persisted. The native
ABI `primed` value is recorded as the one neutral-baseline frame it represents;
later connected frames with `primed=false` remain actionable through the same
semantic router. Y press/release/hold handling retains the exact shared-route
result; a rejected quick action or widget route is recorded as unhandled rather
than being promoted to a successful native input.
Any red effective-visibility or geometry record keeps AVP-004 blocked; the
renderer-capture polish does not weaken or substitute for those assertions.
Ownership checkpoints separate managed live/heap bytes, decoded
bitmap count/bytes, tracked render trees, pending artwork, and remaining
native/Skia/render-target/other unattributed candidate memory. The ignored exact runtime and JSON are under
`artifacts/avp004`; see [evidence/README.md](evidence/README.md).

## Visible planner/user launch

```powershell
$tip = (git rev-parse HEAD).Trim()
& .\experiments\AvaloniaOverlayPrototype\artifacts\avp004\runtime-win-x64\AvaloniaOverlayPrototype.exe `
  --installation .\experiments\AvaloniaOverlayPrototype\artifacts\avp004\runtime-win-x64 `
  --source-commit $tip `
  --input-trace .\experiments\AvaloniaOverlayPrototype\artifacts\avp004\manual-session-input-trace.json
```

The prototype refuses a second instance instead of silently competing for the
extracted platform service. Physical Guide/controller feel, compositor
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
rather than retained at opacity zero; avoidable semantic and page-body clipping
is not used;
and compiled bindings remain enabled. No `BitmapCache` or Skia GPU-cache increase
was added because both can increase memory and require supporting measurement.
Phase isolation associated the earlier private-byte jump with CrossFade overlapping
top-level resize and with whole-tree compact/expanded reprojection—not the
managed heap, decoded artwork, pending artwork, tracked trees, or an observable
Skia cache. Responsive visibility switches only protocol-declared nodes in place
with `IsVisible`. A frame's validated surface hints are atomically admitted with
its semantic authority and resolved by the host into the content/HWND union;
they never reposition the invariant absolute chrome anchor. Exact evidence
retains every authored pair, admitted content bounds, actual HWND bounds, and
absolute tray/guide bounds.

The generic Avalonia boundary keeps ordinary roots and small semantic
collections at intrinsic height inside the host scroll viewport, while the
10,000-item path remains recycling with uniform rows. Advanced presentation
slots reflow from bounded two-column ratios to one stacked column at compact
width without inspecting widget identity or tree shape. Authored glyph buttons
retain a visible glyph and exact UIA/action identity even when their declared
width is intentionally compact.
