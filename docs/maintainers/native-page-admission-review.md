# Page arrival, reversal and preparation consistency

Review baseline: `ca9e8088` (integration `bf5be092`), following physical PID 40476.
This delivery adds a reference review and an opt-in diagnostic regression only.
It does not change the renderer, scheduler, SDK, widgets or running candidate.

## Findings

The new regression demonstrates that changing scheduler priority alone is
insufficient. Under a deterministic one-preparation-job allowance, 24 cases cover
lists/grids, 100/125% scale, append/prepend, and three provider arrival times during
reversal. The real native renderer performs preparation, layout, drawing, source
replacement and anchor restoration; only host job dispatch is simulated.

| Result | Cases | Interpretation |
|---|---:|---|
| No added holds or anchor failure | 12 | All list cases in this fixture |
| Additional movement holds | 8 | Grid cases; four extra held ticks versus their no-page control |
| Lost viewport anchor | 4 | Grid prepend while the candidate needs multiple preparation slices |
| Extra holds eliminated by current-work-first experiment | 2 of 8 | Priority helps, but is not a complete correction |
| Anchor failures eliminated by rebuilding at admission | 4 of 4 | Isolates faulty prepared-frame adoption/checkpoint state |

Two current-work-first cases did not admit until movement stopped. They eventually
admitted during the bounded idle drain. This is evidence against unconditional
current-work priority without fairness or progress preservation.

In a failing prepend case, the last committed offset was 144. The synchronous
reference correctly adopted offset 544 to keep the same visible items in place.
The prepared-frame path retained 144 and lost the visible anchor, while reporting
`reusedCollectionPreparation=true`. Equal input/source/options proofs do not
establish that the speculative checkpoint is internally consistent.

The code explains a route to this mismatch:

1. `PrepareCollection` captures the current anchor, replaces collection geometry
   for the incoming items and computes an adjusted offset in a local variable.
2. `PrepareCollectionItem` can throw `CollectionSlicePending` before the final
   `StoreScrollOffset` in its caller executes.
3. `PrepareCollections` saves the partially changed collection model together
   with the staged scroll map, which can still contain the old coordinate.
4. Resumption combines the incoming model with that old offset. The resulting
   frame can pass the external reuse proof and still publish incorrect placement.

Relevant implementation: `CollectionRealization.inl` (`PrepareCollection`,
`PrepareCollectionItem`) and `DeclarativeRenderer.cpp` (`PrepareCollections`,
ready-frame adoption in `Render`). This is an atomic checkpoint problem, not a
reason to remove strict reuse checks or permanently disable frame reuse.

## What Flutter actually does

Sources inspected at Flutter stable commit
`6a19cca56475dbfba1478ee68d7bd0c2ef891da1`; these are source findings, not a Flutter
benchmark on this machine.

| Mechanism | Source behavior | Implication for WidgetRail |
|---|---|---|
| Viewport realization | Sliver lists/grids derive the needed range from the viewport and cache constraints, then create/layout children for that range. | Current viewport demand should drive required work; provider page size should not determine interactive work. |
| Layout reuse | `RenderObject.layout` returns early when the object is clean and constraints match. | Reuse depends on identity, inputs and invalidation, not just retaining pixels or stable keys. |
| Offscreen lifetime | Multi-box slivers remove ordinary children outside the cache range; explicitly kept-alive children use a separate bucket. | Eviction is not inherently our defect. Keeping every loaded item alive is not a required remedy. |
| Local painting | Clean repaint boundaries reuse their layers at a new offset; dirty boundaries are repainted. Sliver builders enable repaint boundaries by default. | Keep unchanged item content independent of scroll placement and page-level changes. Our retained-item/compositor work already addresses part of this. |
| Frame phases | The rendering binding flushes layout, compositing bits and paint before sending the scene onward. | Preparing speculative work in slices requires a consistent checkpoint and atomic admission, not partially updated state. |
| Background work | The default scheduler deprioritizes low-priority scheduled tasks while frame callbacks are active. | Useful priority separation, but not an automatic guarantee for arbitrary data-loading callbacks. |
| Windows timing | The Windows embedder reads DWM refresh rate for the interval and snaps engine time to its tick phase. It does not use our `qpcVBlank` budget calculation. | Reference the semantics, not a drop-in timer implementation; WidgetRail also has independent DirectComposition ownership. |
| Backpressure | The animator defers another frame when its layer-tree pipeline is full. | More queued work does not imply more progress; bounded queues matter. |

Flutter can still perform expensive layout or painting on the UI thread. Slivers
are not asynchronous provider cursors, and the examined implementation does not
promise interruption-free pagination for every application. It also does not
preserve arbitrary prepend anchors merely because an application appends keys:
child identity, scroll geometry and application update semantics still matter.

Sources:

- [Sliver list layout](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/sliver_list.dart#L46)
  and [grid layout](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/sliver_grid.dart#L594).
- [Child retention](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/sliver_multi_box_adaptor.dart#L356)
  and [stable-key remapping](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/widgets/sliver.dart#L1008).
- [Layout reuse](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/object.dart#L2864)
  and [repaint-boundary compositing](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/object.dart#L249).
- [Rendering phases](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/rendering/binding.dart#L691),
  [task priority](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/packages/flutter/lib/src/scheduler/binding.dart#L1460),
  [Windows timing](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/engine/src/flutter/shell/platform/windows/flutter_windows_engine.cc#L717),
  [frame backpressure](https://github.com/flutter/flutter/blob/6a19cca56475dbfba1478ee68d7bd0c2ef891da1/engine/src/flutter/shell/common/animator.cc#L95).

## Compose cross-check

At the previously assessed Compose core revision
`a1a7f3533363aa93849541cf451af2363fc2070a`, the common lazy-list prefetch strategy
cancels obsolete work after a direction change and marks soon-needed items
urgent. Its prefetch executor estimates individual steps and relaxes the estimate
gate for urgent work while still requiring positive available time. These are
closer references for our deferred preparation than Flutter's synchronous sliver
layout; neither is a template for blindly copying a Windows host scheduler.

- [Reversal cancellation and urgency](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/foundation/foundation/src/commonMain/kotlin/androidx/compose/foundation/lazy/LazyListPrefetchStrategy.kt#L157).
- [Per-step admission](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/foundation/foundation/src/commonMain/kotlin/androidx/compose/foundation/lazy/layout/LazyLayoutPrefetchState.kt#L572).
- [Platform scheduling boundary](https://github.com/JetBrains/compose-multiplatform-core/blob/a1a7f3533363aa93849541cf451af2363fc2070a/compose/foundation/foundation/src/commonMain/kotlin/androidx/compose/foundation/lazy/layout/PrefetchScheduler.kt#L31).

## Bounded next correction

First repair the speculative checkpoint invariant: collection geometry, anchor,
scroll position and measurement revisions must describe the same intermediate
state at every yield. Prove it for partial rows, prepend/eviction, cancellation,
reversal and intervening paints. Do not hide the failure by removing prepend
coverage or accepting a jumped reference.

Then distinguish required current-view realization from incoming-page admission
and optional lookahead. Reversal should invalidate obsolete demand without
discarding valid measurements. Fair admission must allow the incoming page to
finish while prioritizing required visible work; the priority-only experiment
already shows why an unconditional reorder is inadequate.

Finally, remeasure the synchronous admission path and isolate layout/style,
raster and upload costs. The physical log still contains expensive source-change
frames; the synthetic regression does not establish their complete cause.
Consider bounded nearby retention only if measured re-realization churn remains
after these correctness fixes. Do not increase caches or change frame budgets as
a substitute for this investigation. No SDK expansion or graphics-library
migration is justified by the findings so far.

## Reproduction and limits

Build the native renderer suite with `src/OverlayHost/build.ps1
-DeclarativeRendererTestsOnly -NoRestore`, then run
`src/OverlayHost/out/Debug/DeclarativeRendererTests.exe --page-arrival-reversal`.
The opt-in probe is intentionally red on this baseline (exit 1); it is not part
of the default suite's green result. The existing suite still passes 57,512 checks.
The companion source is `src/OverlayHost/PageArrivalReversalProbe.inl`.

The probe uses fixed-height keyed native rows/cards, a real WIC-backed Direct2D
renderer and exact anchor/offset comparisons against synchronous layout. It
compares no-page, current host ordering, current-work-first and, for failing
anchors, rebuilt-admission controls. It covers delayed arrival during movement
and a bounded idle drain. One job per tick deliberately exposes contention;
tick counts are not milliseconds or display FPS. It does not run Flutter,
Compose, the Windows message loop, artwork uploads or the exact Playnite tree.
The fixture cannot attribute every physical hitch or guarantee an eventual fix's
smoothness. Production-styled and physical follow-up remain required.

Local evidence and reference snapshots are under
`artifacts/flutter-scrolling-review/`; physical evidence is under
`artifacts/native-frame-scheduling/physical-40476-*`. No third-party source was
copied into the product.
