# Native UI evolution, animation and graphics assessment

2026-09-26; production baseline `092ee64d`. Companion to the
[end-to-end architecture assessment](ui-architecture-assessment.md).
The original design/effort assessment uses source inspection and saved trace
evidence. Subsequent native implementation is tracked below; no replacement
renderer or Skia integration benchmark has been performed.

## Implementation progress

WIDGE-293–298 integrate only into `codex/native-ui-integration`; `main` stays untouched.
WIDGE-293 now has protocol-v59 list/grid declarations, logical geometry, renderer
realization/navigation, UIA reveal without focus transfer, and immutable SDK item
reuse. Playnite Browse and regular YouTube Music rows use the contract. Styled
fixtures match eager visible geometry/content pixels at three widths and 100%/125%
scale, excluding estimated scrollbar thumbs. Native/managed gates cover margins,
pixel-phase stability, anchors, resets and renderer failure rollback.

A bounded preparation API preserves committed geometry with explicit Ready/Pending/
Failed states. Incoming main/pinned snapshots now prepare in host timer slices
before session admission; old input/UIA authority remains current meanwhile.
Renderer, session retry/lifecycle and production pinned-owner tests pass.
Directional offscreen focus waits for prepared geometry; scrolling rolls back pending
distance. Incoming focus-group targets are prepared before admission, and UIA reveals
use bounded timer slices without moving focus. Renderer progress/rollback and pinned
host tests pass. Final paint/device publication, broader cursor/lifecycle validation,
and further widget adoption remain.
Draw-failure hardening gates interaction and preserves unseen accessibility
reveals. Renderer scene state now waits for an exact host frame acknowledgement;
rejected frames retain committed layout, scroll, motion and raster leases. Main
group requests and collection-focus/page settlement follow successful submission.
Renderer (29,787), interaction (411), pinned-owner (409) and host-contract (517)
checks pass, with a coherent Release host/runtime build. Main-host injected
submission/lifecycle coverage and remaining collection acceptance are still open.
WIDGE-294–298 have not
started. No live-overlay performance improvement is claimed. Details:
[collection lifecycle](native-collection-lifecycle.md).

## Decision

Native evolution is a first-class alternative to Compose adoption. The available
evidence does not establish that a toolkit migration is necessary to achieve the
desired controller experience. A coherent native lazy/retained UI project is the
best next implementation to scope, before replacing the graphics library. Compose
remains both a reference implementation and the principal alternative if owning
that project and its maintenance proves less attractive than migration.

This recommendation does not preserve existing code or contracts for their own sake.
The SDK, protocol and preparation pipeline may change. It preserves the useful
capabilities already established on Windows—especially native composition, media
integration and controller policy—while addressing the measured architectural gap.
It also avoids treating good Android TV behavior as a guarantee about a Windows JVM
host's memory or performance.

The focused native project should deliver one consistent collection/item lifecycle,
incremental preparation and predictable focus, not another series of independent
scroll patches. Keep the existing Direct2D/DirectWrite/DirectComposition foundation
initially. Improve animation declaration and resource reuse as separate workstreams.
Evaluate Skia only against a specific graphics capability or measured paint cost.

## Is laziness the only shortcoming?

No. Distinguish a measured bottleneck, a design gap, a feature limit and a deliberate
tradeoff. Not every difference from Compose is a defect or a requirement to implement.

| Area | What is established today | What deserves improvement |
|---|---|---|
| Collection realization | Provider windows, stable keys, viewport decoration deferral and retained Taffy nodes exist | New structural snapshots still prepare concrete loaded trees. Introduce independently realized items whose lifetime is shared by layout, focus, accessibility, painting and prefetch. |
| General invalidation/preparation | Atomic structural protocol operations and retained paint/layout paths already exist | Structure still bypasses local planning. `BuildLayout` prepares/computes an estimate and a correction; anchor/focus changes can cause further preparation. Localize dependencies outside collections too. |
| Focus integration | Current native controller policies, scopes, anchors and geometry-based navigation are substantial and tested | A truly lazy UI cannot require geometry for every loaded item. Logical navigation must request realization and consume one committed geometry generation. |
| Animation authoring | Shared section groups, selection/layout transitions, modal/focus/press presets and style transitions | More general state/property transitions, explicit interruption semantics, item insertion/removal/reorder motion, true velocity-aware springs and reusable motion declarations. |
| Animation backend coverage | DirectComposition handles supported transforms, clips, fades and retained surfaces | Spring easing uses a CPU path; any live media/window-preview surface currently disables layered widget motion. These are specific limitations, not evidence that all animations repaint on the UI thread. |
| Painting | GPU-backed Direct2D, DirectWrite plans, retained bands, decoded/GPU caches, shadow masks and translated item captures | Some paint paths still create brushes/geometries repeatedly. Scope invalidation, resource lifetime and surface-copy costs before adding caches or changing painter. |
| Resource accounting | Multiple bounded caches and node/raster limits exist | Coordinate total CPU/GPU retention and lifecycle release across decoded images, bitmaps, band rasters, compositor surfaces and atlases. Per-cache limits alone do not define total resident cost. |
| General UI expressiveness | A bounded component/style vocabulary suits safe host rendering | Richer text/effects and more composable controls can be added if the product needs them. General document editors or complete Compose API parity are not current requirements. |
| Observability | Phase timings, cache/upload counters and deterministic replay tests exist | Add realized/measured item counts, structural dirty reasons, scheduling debt and animation backend decisions when implementing the new lifecycle. Measure GPU completion separately if evidence points there. |

In the latest saved physical sample, seven new-view scrolling renders had medians
of 47.244 ms total, 24.552 ms preparation and 12.876 ms node drawing. These are CPU
stage timings, not GPU duration. Style, text and layout subtimings overlap; their
medians must not be added. This supports investigating both preparation and paint
work, but does not establish that Direct2D itself is slow or that Skia would win.

### Existing source anchors

- [Renderer](../../src/OverlayHost/DeclarativeRenderer.cpp): `BuildLayout`,
  `PlanPresentationUpdate`, `ReconcileCollectionAnchors`, `ProjectScrollOffsets`,
  brush/geometry creation and current phase timings.
- [Atomic updates](../../src/WidgetProtocol/PresentationUpdates.cs): structural
  insert/remove/move operations already exist; replacing JSON alone is not a fix.
- [Composition painting](../../src/OverlayHost/WidgetCompositionPaint.inl):
  `CompositorControlsEnabled`, `UsesCompositorControlScale`, `DrawWidgetComposition`.
- [Native text](../../src/OverlayHost/NativeTextLayout.h): one immutable DirectWrite
  plan is shared by measurement and paint.
- [Surface depth](../../src/OverlayHost/SurfaceDepth.h): bounded reusable shadow
  patches; CPU mask/blur construction happens on cache misses, not every frame.

## Concrete native architecture

### 1. Separate semantic items from realized visual subtrees

Introduce a first-class collection contract for vertical/horizontal lists and
adaptive grids, rather than an ambiguous `virtualize=true` flag on arbitrary layout.
It carries stable collection/query/item identities, ordered lightweight item
descriptors, layout policy, loading boundaries and transactional mutations.
Descriptors can contain bounded declarative item content. They are data, not worker
callbacks executed by the UI host.

New SDK helpers should describe items without rebuilding every already-known item
subtree on each playback/loading update. Existing generic Scroll remains available
for small or intrinsically coupled layouts. A documented fallback is preferable to
guessing that arbitrary nested content satisfies the collection contract.

The host owns a `CollectionState` concept with:

- logical item order and key/index lookup;
- per-item content/style/measurement revisions;
- realized items around the viewport plus an explicit buffer;
- protected items for focus, pending navigation, accessibility realization and
  modal-parent restoration;
- measured row/line extents and estimates for not-yet-measured content;
- current anchor key plus offset, range boundaries and pending data demand.

This is a proposed internal design, not a new public type already implemented.
Logical identity must outlive recycled controls. Eviction of provider data is a
different event from unrealization of an offscreen UI subtree.

### 2. Give the collection ownership of placement and extent

For a grid, derive columns from the admitted container width and lay out needed
lines. Preserve partial rows and logical start columns through non-row-aligned
eviction. For variable-height lists, maintain measured extents/prefix information
and estimate unknown items. Correct estimates while preserving a surviving visible
anchor; do not pretend unseen item geometry is exact.

Use Taffy to measure realized item subtrees and surrounding generic layout. Reuse
measurement only when content, inherited style, constraints, text/display scale
and relevant intrinsic inputs agree. A stable key alone never authorizes reuse.
Explicit query reset or position requests override old anchor memory.

The earlier fixed-item-island experiment was too narrow for content-sized music
rows. A collection-owned realization/measurement lifecycle avoids that assumption.

### 3. Make navigation and accessibility realization-aware

For regular collections, next/previous logical targets come from item/line identity,
not an always-complete map of offscreen rectangles. A navigation request to an
unrealized target first ensures its descriptor/data is available, schedules its
realization and measurement, then commits focus after the target has current geometry.
Replaced query/scope/widget authority cancels the pending request.

Keep explicit focus groups and cross-section destinations for rails, navigation
bars and modals. Generic spatial search remains useful inside realized arbitrary
layouts. Do not let presentation scale change logical directional order accidentally.
Pointer and accessibility bounds are projected through current presentation geometry;
logical navigation uses the declared collection/group policy.

Accessibility should request realization through the same lifecycle instead of
forcing every decorative subtree into existence. Pin the current target when
required, but do not equate that with painting an offscreen item. Parent modal focus
and scroll restoration retain logical identity, not a second hidden copy of the UI.

### 4. Localize preparation and publish atomically

Maintain current semantic nodes separately from reusable prepared layout/paint
state. Apply ordered mutations, invalidate dependencies, prepare changed/realized
subtrees, reconcile anchors, and publish geometry, focus targets and paint content as
one admitted state. Old actions cannot be resurrected by a reused paint/layout entry.

The broader renderer should resolve parent-relative inputs at the correct constraint
boundary instead of repeatedly preparing the whole root from estimates. This needs
an explicit layout/style contract: do not simply delete the correction pass while
existing percentage/font semantics depend on it. Generic fallback remains correct
until a layout's dependencies support local preparation.

Avoid a giant generalized reactive framework initially. A typed dirty-dependency
model for structure, measure, placement, paint, resource and semantics is sufficient
to start. Existing protocol impacts and retained Taffy state are foundations, not
the complete implementation.

### 5. Schedule work before it becomes visible

Visible dirty items and pending focus take priority. Prepare a bounded adjacent
window using recent scroll direction; cancel obsolete jobs on reversal or authority
change. Data fetching and UI preparation have different queues and budgets. Work
that can use immutable, thread-safe data may run off-thread; do not move arbitrary
Direct2D or COM objects to workers by assumption.

At a loaded-content boundary, keep the current presentation, stop consuming further
distance and resume from fresh input/current gesture state after admission. Do not
accumulate a movement backlog. Preparation may be staged across frames, but avoid
publishing half-admitted content or transferring focus into an incomplete item.

### First delivery and verification

Deliver adaptive Playnite posters and content-sized music rows through this same
contract, then port other suitable collections. Do not begin by promising arbitrary
virtualization of every nested layout or rewriting all widgets.

Validate append/prepend/eviction/reorder/reset, unknown totals and inaccurate
estimates; variable heights and wrapping; DPI/text scale and column changes; data
and artwork completion; focus/hold/reversal/accessibility; modal/pinned/hidden
lifetimes; stale requests and failed publication. Compare realized geometry and
visible pixels against a full reference, and compare newly realized targets when
requested. A lazy engine should not be required to supply exact unmeasured geometry
for all offscreen items merely to satisfy an old eager-layout test.

At a fixed viewport, increase retained data from hundreds to thousands of descriptors.
Expensive preparation and measurement must follow changed/realized items. Lightweight
bounded metadata reconciliation may remain linear. Measure frame distributions,
input-to-visible delay and total process/GPU memory, with logging modes matched.

## Animation declaration comparison

| Capability | Current WidgetRail | Compose reference |
|---|---|---|
| Section changes | `TransitionContent(group, key, order)` with host-selected presets | `AnimatedContent`, transition APIs and composable enter/exit definitions |
| Synchronized header/selection | `TransitionLayout` and `TransitionSelection` share a group timeline | `updateTransition` coordinates multiple typed animated values |
| Focus/press styling | Focused/pressed WRSS values; bounded opacity, scale and translation transitions | State-driven property APIs and graphics-layer modifiers; interaction policy still belongs to the app |
| Curves | Fixed-duration linear/ease curves; a normalized critically damped-looking spring easing | Tweens, configurable physical springs, keyframes, repeat/decay and typed animation specs |
| Interruption | Retargets from the current displayed value; specialized section/focus handlers | `Animatable` supports cancellation and current value/velocity continuity, including spring retargeting |
| List mutation motion | No general SDK insertion/removal/reorder animation contract | Lazy `animateItem` exposes appearance, placement and disappearance specs |
| Layout-affecting motion | Section-specific placement/selection behavior; no general animated reflow API | Size/layout animation APIs, with the associated remeasure cost |
| Arbitrary code | Widgets send bounded declarations; no author frame callbacks | Trusted application code can use composables/coroutines directly; that unrestricted API cannot be handed to sandboxed plugins in the trusted host |

Current declarations are small and product-consistent, but considerably less
expressive. For example, `content.TransitionContent("library", sectionKey, order)`
requests an intent; the host chooses timing/style. A focused WRSS `scale` target
does not define a general multi-property state machine. These are useful defaults,
not substitutes for a reusable animation API when more complex behavior is needed.

Compose's richer API does not mean all its animation is cheaper. Animating layout
dimensions can force measurement; updating a graphics-layer transform can avoid
composition/layout work. This distinction must remain explicit in either framework.

## Animation backend comparison and native design

WidgetRail already has a worthwhile native backend:

- [WidgetAnimationPolicy](../../src/OverlayHost/WidgetAnimationPolicy.h) defines
  shared recipes, poses, curves, presets and global speed policy.
- [WidgetCompositionPresenter](../../src/OverlayHost/WidgetCompositionPresenter.cpp)
  compiles supported transforms/opacity/clips to DirectComposition animations.
  Its `Advance` retires finished visuals without requesting a raster frame.
- Immutable raster leases and retained surfaces let supported motion run without
  repeatedly painting pixels. CPU sampling of common curves supports matching
  presentation/input calculations.
- [DeclarativeMotion](../../src/OverlayHost/DeclarativeMotion.cpp) is a separate
  host-sampled bounded style timeline. Its spring is a fixed-duration normalized
  response, not a velocity-preserving physical spring. Retargeting stores the
  current value but has no general velocity state.
- [Composition painting](../../src/OverlayHost/WidgetCompositionPaint.inl) explicitly
  excludes spring-eased control scaling from compositor promotion. Live
  `MediaViewport`/`WindowPreview` nodes send the widget down a stationary/direct
  composition path because external surfaces do not yet share the scene placement.

Compose Desktop also paints through GPU-backed Skia, but a graphics-layer modifier
is not equivalent to an independent Windows compositor animation. Its runtime
advances animation/state and its desktop rendering path produces frames. The exact
work depends on which phase reads the value and on layer retention. Neither
framework's animation backend is universally faster.

For native evolution, retain the good backend and broaden the declaration/execution
model deliberately:

1. Add bounded declarative motion specifications: typed targets, tween/spring or
   bounded keyframes, group/timeline identity and explicit interruption policy.
   High-level section/focus/modal presets compile into the same model. No per-frame
   widget IPC and no unrestricted executable animation/shader code in the UI host.
2. Separate presentation-only targets from layout-affecting targets. Default common
   focus, press and navigation motion to transforms/opacity/clip. Introduce actual
   animated reflow only as an explicit, budgeted feature.
3. Centralize motion state, cancellation and clock ownership while retaining separate
   semantic policies for focus, sections and popups. Store velocity where a physical
   spring promises velocity continuity. Do not silently substitute a cubic for an
   authored spring without an explicit equivalence/error policy.
4. Compile eligible motion into DirectComposition. Keep unsupported operations on a
   clearly diagnosed path; do not imply they are compositor-driven. Add traces and
   tests for both paths and for transitions between them.
5. Integrate item appearance/placement/removal with the realization lifecycle.
   Outgoing decoration may retain a bounded surface, never a live removed action.
6. Unify live-surface placement with scene transforms/clips as a separate integration
   project. Hardware video is not an ordinary bitmap and cannot be fixed by exposing
   another easing preset.

This buys most product-relevant motion flexibility without implementing all Compose
animation APIs, an arbitrary coroutine DSL or a full 3D scene engine.

## Does Skia/Skiko paint better?

**Skia is a serious standalone C++ graphics dependency. Skiko is not required for a
native integration:** it supplies Kotlin bindings and platform/rendering glue for
the Compose path. Adding Skiko would bring an unnecessary JVM/Kotlin boundary to
the current C++ painter.

Skia offers a broad, cohesive API for paths, clipping, gradients, image filters,
blend operations, text/glyph drawing, color handling, recorded drawing commands and
custom runtime shader effects. Its GPU implementations include resource/glyph
caches, batching and backend-specific rendering strategies. That can reduce the
amount of specialized graphics code WidgetRail owns if those capabilities are needed.

Direct2D/DirectWrite are also mature GPU/text systems with geometry caching,
effects, gradients and batching. Several limitations of today's UI are in our
component/style vocabulary or preparation pipeline, not in those libraries. There
is no measured evidence here that Skia draws our posters, text, scrims and shadows
faster, uses less memory or always looks better.

Important distinctions:

- A `SkPicture` records drawing commands; it is not automatically a cached texture
  of a widget. WidgetRail would still own invalidation, scene layering, clipping,
  lifetime, scheduling, focus and resource policy.
- Using Skia does not supply lazy collections or Compose's animation/state system.
- New GPU caches can duplicate decoded images, existing bitmaps and compositor
  surfaces. Skia resource-cache limits do not alone account for externally owned
  textures, swap chains or all process memory.
- Skia can use DirectWrite for Windows font management. That does not make its
  paragraph layout/paint API interchangeable with `IDWriteTextLayout`. Preserve
  one authoritative shaping/measurement/paint plan or migrate that contract together.
  Measuring with one engine and independently laying out for paint with another
  risks exactly the wrapping/baseline inconsistencies we have already encountered.

### Why it is not a drop-in swap like Taffy

Taffy consumes a bounded layout graph and returns geometry through a narrow ABI.
Skia would participate in long-lived GPU resources, device loss, command submission,
fonts, pixel formats, alpha, color space and compositor synchronization.

Our current `OverlayCompositionSurface` creates D3D11/Direct2D devices and
DirectComposition surfaces. `WidgetCompositionNode` currently carries an
`ID2D1Bitmap`, and the presenter draws it into an `IDCompositionSurface`.
The inspected Skia Ganesh Direct3D backend accepts an `ID3D12Device`, command queue
and `ID3D12Resource` textures. It cannot simply wrap an `ID2D1Bitmap` or assume the
current D3D11 surface is its own render target.

Potential routes, none proved here:

| Route | Implication |
|---|---|
| Coherent D3D12-backed Skia painting and composition surfaces | Could avoid CPU readback, but requires a new device/surface/presentation ownership design and media interoperability proof. Per-layer retention/animation must survive; one flattened swap chain is not equivalent. |
| Shared-resource/D3D11–D3D12 or ANGLE interoperability | Can be technically viable, but requires format/adapter compatibility, fences/resource states and lifecycle handling; may include GPU copies. No assumption of free zero-copy interop. |
| CPU Skia raster output uploaded into our existing surfaces | Useful for isolated static assets or a correctness experiment; an inappropriate default for continuously redrawing the complete widget. |

The inspected upstream build defaults have Ganesh enabled, Graphite disabled and
Direct3D disabled unless selected; direct D3D is a Ganesh backend. Choose and pin an
actual backend/build rather than treating the Skia name as one interchangeable GPU
implementation. Selected APIs such as `SkRuntimeEffect` explicitly warn about
experimental stability. Library maturity does not remove release/API maintenance. Cold shader/pipeline
creation and cache warm-up must also be profiled; a mature GPU backend does not
promise that the first use of every effect is free of stalls.

### Clean integration decision gate

If a graphics requirement or profile justifies Skia, first extract a small internal
boundary between prepared paint operations, owned raster/texture leases and native
presentation. Do not put Skia objects in the public widget protocol or build a
general-purpose multi-renderer abstraction before two concrete consumers need it.

Render equivalent real widget content through both painters while keeping layout,
data, image readiness and effects controlled. Verify fractional DPI, alpha edges,
text fallback/wrapping, gradients/shadows, image fitting and color handling. Measure
CPU recording, GPU completion/presentation, transfer bytes, cold caches, memory,
device loss and surface retirement. Require no routine full-frame GPU-to-CPU
readback and no unexplained extra frame of input latency. A CPU canvas benchmark
cannot establish that this Windows composition integration is clean.

Adopt only if it brings needed graphics capability or a demonstrated net benefit
after integration costs. There is no honest guarantee of zero performance regressions
before that work. This gate is separate from selecting Compose as an entire UI framework.

## Techniques to apply without adopting Skia

Some are already implemented; do not count them twice as new work.

| Technique | Native status and next step |
|---|---|
| Retained pixels/layers | Already present. Improve mutation-local invalidation and capture boundaries; unchanged pixels should remain reusable when collection metadata changes. Measure actual misses first. |
| Reusable geometry/brush resources | Some resources are cached, but several surface/border paths allocate them during paint. Add bounded device-scoped reuse or scratch resources for demonstrated hot paths. Direct2D geometry realizations are available for repeatedly drawn complex static paths. |
| Cached immutable draw preparation | Extend typed prepared nodes/paint operations so stable shape/style work is not reconstructed merely because another item arrived. Recorded commands alone are not raster reuse. |
| GPU-friendly batching | Keep compatible operations together where painter order and clipping permit; avoid unnecessary target switches, explicit flushes and temporary layers. Preserve blending semantics. Direct2D already batches, so changes require measurements. |
| Shadow/filter reuse | Current nine-patch shadow masks already avoid viewport-sized blurs. Measure cold construction versus warm reuse before replacing them with per-frame GPU filters. |
| Size-aware asynchronous artwork | Already present. Improve demand priority and cancellation through the item lifecycle rather than starting a second image pipeline. |
| Resource budgets and ownership | Coordinate CPU decode, GPU image, raster-band, atlas and composition retention; account for transient overlap during animation and replacement. |
| Frame scheduling and prefetch | Add bounded preparation scheduling alongside the existing frame-paced scrolling. Rasterize/measure only where needed and avoid hiding UI-thread stalls behind a faster shader. |

These are general rendering techniques, also described in Direct2D guidance; they
do not need to be copied from Skia internals. Port algorithms only where ownership,
correctness tests and licensing are clear, rather than importing GPU subsystems piecemeal.

## Effort and delivery comparison

Rough engineering estimates for one experienced engineer familiar with this code,
including focused automated verification. They are not elapsed-time promises for
an agent run. Physical/controller/media validation and unknown integration issues
can extend them; work packages overlap and should not be mechanically summed.

| Work package | Indicative effort | Confidence and boundary |
|---|---:|---|
| Native collection/dirty-state contracts, lifecycle and test seams | 1–2 engineer-weeks | Medium; define the supported layout and authority cases first. |
| Realization/extent/measurement plus focus and anchor integration for grid + variable rows | 3–5 engineer-weeks | Medium-low; the coupled behavior is the difficult part, not list indexing. |
| Scheduling, SDK/widget adoption and adversarial hardening | 2–3 engineer-weeks | Medium-low; includes UIA, modal, scale and eviction coverage. |
| First coherent native collection delivery | About 6–10 engineer-weeks total | Planning envelope, not a promise of arbitrary-layout virtualization. Use intermediate gates. |
| Broader bounded animation declarations and interruption model | 2–4 engineer-weeks | Excludes generic animated reflow and live-media composition; those are separate extensions. |
| Targeted native paint-resource improvements | 1–2 engineer-weeks initially | Only measured hot paths; not a general renderer rewrite. |
| Skia GPU/presentation/text feasibility slice | 2–4 engineer-weeks | Low confidence until the surface/device route is chosen. |
| Full Skia painter/text/presentation migration | Additional 4–8+ engineer-weeks | Low confidence; broad visual/device regression matrix. Not needed for lazy collections. |
| Integrated Compose product slice | About 3–6 engineer-weeks | Low confidence; real native surfaces, controller behavior and sandbox boundary, not a standalone grid. |
| Broader Compose SDK/component/widget migration and hardening | Additional 6–12+ engineer-weeks | Low confidence; may remove long-term UI-engine maintenance but cannot be assumed cheaper up front. |

Both native evolution and Compose adoption need the new sandbox/data collection
contract; it is not a cost unique to the native option. Compose supplies more
general UI/animation machinery upstream. Native evolution has lower uncertainty
around our Windows services and existing controller/media behavior, but leaves us
owning more framework code long-term.

The decision should follow an integrated vertical slice with acceptance gates, not
the estimates alone. Start with the native collection architecture while keeping
the paint backend unchanged if that project is authorized. Reassess at the first
two representative surfaces: if the design requires broad toolkit reimplementation
or fails the memory/navigation/frame goals, compare that concrete cost with Compose.
Animation expansion can follow on its own merit; Skia is optional and should not
block the structural performance work.

## Sources and verification limits

The native observations above were checked against the cited production baseline.
The assessment changed documentation only. There is no new native/Compose/Skia
runtime benchmark, API integration proof, performance guarantee or implementation
authorization in this report. Existing physical samples remain small and workload-specific.

- [Widget transition SDK](../../src/WidgetSdk/WidgetTransitions.cs) and
  [wire declaration](../../src/WidgetProtocol/WidgetTransition.cs).
- [Native style timeline](../../src/OverlayHost/DeclarativeMotion.cpp),
  [interaction policies](../../src/OverlayHost/WidgetInteractionMotion.h),
  [composition scene/resource representation](../../src/OverlayHost/WidgetCompositionScene.h),
  [D3D11 composition owner](../../src/OverlayHost/OverlayCompositionSurface.cpp).
- Compose core release `a1a7f3533363aa93849541cf451af2363fc2070a`:
  [Animatable continuity and cancellation](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/animation/animation-core/src/commonMain/kotlin/androidx/compose/animation/core/Animatable.kt),
  [typed animation specifications](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/animation/animation-core/src/commonMain/kotlin/androidx/compose/animation/core/AnimationSpec.kt),
  [lazy item animation](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/foundation/foundation/src/commonMain/kotlin/androidx/compose/foundation/lazy/LazyItemScope.kt).
- [Compose animation API guide](https://developer.android.com/develop/ui/compose/animation/choose-api)
  and [phase/performance guidance](https://developer.android.com/develop/ui/compose/animation/quick-guide).
  Android documentation is explanatory; common-source checks establish the portable APIs.
- Upstream Skia inspected at `3a1baf677e658f2a29de94930cb986a5696d6032`, separately
  from Compose's pinned Skiko build:
  [D3D12 context](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/gpu/ganesh/d3d/GrD3DBackendContext.h),
  [D3D12 texture/fence types](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/gpu/ganesh/d3d/GrD3DTypes.h),
  [GPU surface wrapping](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/gpu/ganesh/SkSurfaceGanesh.h),
  [resource cache/submission](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/gpu/ganesh/GrDirectContext.h),
  [recorded pictures](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/core/SkPictureRecorder.h),
  [runtime effects](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/effects/SkRuntimeEffect.h),
  [DirectWrite font manager](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/include/ports/SkTypeface_win.h),
  [paragraph layout/paint](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/modules/skparagraph/include/Paragraph.h),
  [backend build configuration](https://github.com/google/skia/blob/3a1baf677e658f2a29de94930cb986a5696d6032/gn/skia.gni).
- Microsoft: [Direct2D resource reuse, batching and geometry caching](https://learn.microsoft.com/en-us/windows/win32/direct2d/improving-direct2d-performance)
  and [DirectComposition surface update ownership](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositionsurface-begindraw).
