# Widget snapshot cache and incremental presentation design

Status: approved architecture and staged implementation design

This document defines how widget presentation state is retained, refreshed,
updated, laid out, rendered, and admitted by the WidgetRail host. It
is the durable design companion to the DLV assignments in
[`delivery-plan.md`](delivery-plan.md). The delivery plan remains the sole
implementation authority.

## Decision summary

The current `WidgetSnapshot` is a complete immutable presentation checkpoint.
It combines a widget's view structure, current values, interaction contract,
accessibility projection, and surface request. Keeping one complete admitted
checkpoint is useful: the native host can render, navigate, expose UI
Automation, and retain a last-known presentation without synchronously calling
the widget worker.

The defect is not that a complete checkpoint exists. The defect is treating an
ordinary provider invalidation as proof that the checkpoint is unusable and
deleting it. A checkpoint remains a valid description of sequence N even when
sequence N+1 may be available.

The product will therefore use all of the following:

1. One complete last-admitted checkpoint per widget for recovery and resync.
2. Refresh demand tracked separately from checkpoint validity.
3. A small permanent set of atomic update operations for changes after a
   checkpoint.
4. A host-materialized current presentation assembled from the checkpoint and
   accepted updates.
5. Host-owned interaction state and derived rendering state kept outside that
   materialized widget presentation.
6. Full-checkpoint replacement as the compatibility and recovery fallback.

This is state replication, not an expiry cache. `LastAdmittedCheckpoint` and
`RefreshState` are the preferred terms; `dirty snapshot` is not.

## Goals

- A hidden or suspended widget retains its own last-known presentation and
  authored envelope.
- Selecting a widget never substitutes another widget's pixels or dimensions
  merely because fresh data is pending.
- Ordinary value changes update only the affected semantic properties.
- Identical publications advance authority without layout or paint work.
- Structural changes remain possible without adding a mutation type for every
  SDK element.
- Focus, scroll, pressed-state, slider, accessibility, and action authority
  remain owned by their existing host components.
- Sandboxed and full-trust Community applications use the same bounded overlay
  protocol and admission rules.
- Existing widgets and older protocol peers continue to use complete
  checkpoints.
- The native host remains bounded even when a widget has an application-scale
  private model.

## Non-goals

- Do not expose mutable native host objects to widgets.
- Do not move layout, rendering, focus, input, accessibility, HWND, or
  compositor ownership into widget processes.
- Do not turn the SDK into a mandatory binding or MVVM framework.
- Do not require widget authors to construct low-level update operations.
- Do not embed arbitrary application HWNDs or pixel streams as widget content.
- Do not remove complete checkpoints or make recovery depend on a complete
  uninterrupted update history.
- Do not add service-, package-, widget-, element-ID-, text-, or known-tree-
  specific behavior.

## Current checkpoint contents

The complete checkpoint contains:

- Protocol version, widget instance ID, and sequence authority.
- Active input scope, initial focus identity, and dashboard quick actions.
- Width/height surface modes and bounded minimum/preferred extents.
- Optional advanced-presentation request.
- The complete semantic node hierarchy.
- Node content and state: text, accessibility value, progress/slider value,
  selection, busy, disabled, text-entry value, and artwork references.
- Action IDs, controller shortcuts, focus adjacency, input scopes, scroll and
  pagination metadata, collection identities, responsive visibility, grid
  hints, and style classes.
- In the current native admitted representation, bridge-resolved base,
  focused, and pressed style maps.

The checkpoint does not own current focus, scroll offsets, a physical press,
pending slider adjustment, Taffy rectangles, Direct2D bitmaps, the composed
frame, HWND placement, tray/guide presentation, provider objects, worker
lifecycle, or widget-private application state.

## Required conceptual separation

### View definition

The mostly stable declarative contract:

- Stable node identities and kinds.
- Parent/child structure.
- Actions, shortcuts, focus relationships, and input scopes.
- Scroll/collection identities.
- Responsive and layout semantics.
- Style classes and surface policy.

Collections and alternate pages may still change this structure. "Mostly
stable" does not mean immutable for the lifetime of a widget.

### View state

Frequently changing presentation values addressed by stable identity:

- Text and accessibility values.
- Progress and slider values.
- Selected, busy, and disabled state.
- Text-entry values.
- Artwork handles and image state.
- Current keyed collection items.

### Host presentation state

State that never belongs in a widget update:

- Focus and focus memory.
- Scroll offsets and collection anchor positions.
- Physical pressed state and controller-repeat state.
- Pending/optimistic slider adjustments.
- Taffy layout results and visible/clipped rectangles.
- Decoded resources, damage regions, Direct2D surfaces, and composition state.
- UI Automation provider instances and HWND placement.

## Retention and refresh policy

Each installed widget has at most one `LastAdmittedCheckpoint` in the native
session owner and the bounded corresponding bridge/runtime state required by
the current transport.

An ordinary invalidation performs this state transition:

```text
Current -> RefreshRequested -> RefreshInFlight -> Current
```

It does not erase `LastAdmittedCheckpoint` at any point. A failed, cancelled,
stale-generation, or wrong-lifecycle refresh leaves the last admitted
checkpoint unchanged.

Hard removal is limited to:

- Explicit restart.
- Package removal or runtime replacement.
- Presentation/runtime generation incompatibility.
- Trust revocation.
- Unsupported protocol or unsafe checkpoint corruption for which retaining the
  presentation would violate current authority.

Appearance changes invalidate the derived host style projection and rendered
resources, not the widget's semantic checkpoint. The bridge/native model must
stop embedding appearance validity into semantic-cache validity.

### Hidden and resident behavior

- Hidden widgets retain their checkpoint.
- A resident worker may publish a coalesced update while hidden. The host
  updates the materialized presentation but performs no Taffy layout, paint,
  composition, focus, or UIA publication for a hidden widget.
- A suspended or unloaded widget is not awakened merely to keep a cache warm.
- Selection presents the widget's own retained checkpoint and envelope
  immediately, then requests current state through the existing lifecycle
  owner.
- Entering Interactive state must use currently admitted action authority. If
  synchronization is pending, retained content may remain visible and inert
  until that authority is current.

## Update protocol

### Checkpoint

A complete `ViewSnapshot` remains the initial publication, compatibility path,
explicit resynchronization response, and fallback for a producer that cannot
or should not describe a change incrementally.

### Atomic update batch

An incremental publication contains at least:

```text
protocolVersion
widgetInstanceId
baseSequence
sequence
operations[]
```

The host accepts the batch only when the instance/generation is current and
`baseSequence` exactly matches the materialized presentation. It validates the
complete batch before applying any operation. Admission either commits every
operation and the new sequence or commits nothing.

If the base is missing or mismatched, an operation is unsupported, or the
batch fails validation, the host requests a complete checkpoint. It never
guesses, partially applies, silently truncates, or attempts to repair an
untrusted batch.

### Permanent operation set

The protocol has one small generic operation vocabulary:

1. `SetProperties(target, properties)` where target is the document or a
   stable node ID.
2. `InsertChild(parentId, index, subtree)`.
3. `RemoveChild(parentId, childId)`.
4. `MoveChild(parentId, childId, index)`.
5. `ReplaceSubtree(nodeId, subtree)`.
6. Complete checkpoint replacement outside the incremental batch.

There is no `UpdateText`, `UpdateSlider`, or per-control operation family.
Adding an SDK element does not add an operation. A new property is added to
the versioned property schema and assigned its generic impact classes. Until a
consumer understands it, the SDK uses subtree or checkpoint replacement.

Document-level properties include active input scope, initial focus, quick
actions, surface hints, and advanced-presentation metadata. Changes that alter
input or action authority receive stricter validation but use the same generic
property operation.

### Property impact classes

Each protocol property declares one or more effects:

- `Authority`
- `Paint`
- `MeasureLayout`
- `Accessibility`
- `Interaction`
- `Resource`
- `SurfacePlacement`

Examples:

| Property | Effects |
| --- | --- |
| Text | MeasureLayout, Paint, Accessibility |
| Progress/slider value | Paint, Accessibility |
| Disabled/busy/selected | Paint, Interaction, Accessibility |
| Artwork handle | Resource, Paint, Accessibility when labelled |
| Grid minimum column width | MeasureLayout |
| Action ID or input scope | Authority, Interaction, Accessibility |
| Surface hint | SurfacePlacement, MeasureLayout |

An absent or unknown impact classification cannot default to paint-only. It
falls back to bounded subtree/checkpoint replacement.

## SDK authoring model

Widget authors continue to produce immutable semantic views through the normal
SDK. The SDK retains the last published normalized model, compares stable node
IDs, and chooses the smallest safe operation batch automatically.

The SDK may select:

- No semantic operations when the normalized models are identical.
- Property updates when identity and structure are unchanged.
- Keyed insert/remove/move operations for bounded collections.
- Subtree replacement for a local structural change.
- A complete checkpoint when identity is unstable, the diff is not smaller or
  safer, protocol capability is unavailable, or recovery is requested.

An advanced author may receive diagnostics explaining why the SDK selected a
fallback, but manual patch construction is not the default public API.

## Host materialized presentation

The native session owner materializes one current semantic presentation from
the last checkpoint plus accepted batches. The materialized presentation is
the sole source for rendering, input resolution, and accessibility projection.

The host computes normalized fingerprints excluding the publication sequence:

- Structure/layout fingerprint.
- Visual/data fingerprint.
- Interaction/accessibility fingerprint.
- Surface fingerprint.
- Appearance/resource dependency revisions.

If a new checkpoint has identical semantic fingerprints, the host commits the
new sequence and action authority without layout, paint, or structure events.
Sequence equality is not used as a proxy for semantic equality.

The bridge and native host must not grow competing presentation caches. Their
retained objects have explicit purposes: transport/runtime admission at the
bridge and current materialized host authority in the native session owner.

## Incremental layout, rendering, and accessibility

Accepted operations produce an invalidation plan rather than an unconditional
full-widget redraw.

- Authority-only: commit sequence/authority; no layout or paint.
- Paint-only: repaint the union of the node's old and new visible bounds.
- Measure/layout: dirty the affected Taffy node and required ancestors, then
  repaint old/new damage. Use a full widget layout fallback when incremental
  correctness cannot be proven.
- Structure: update the affected subtree, focus/scroll reconciliation, UIA
  structure event, layout, and bounded damage.
- Resource: retain or resolve the keyed resource, then repaint dependent
  bounds.
- Surface: use the admitted destination-geometry path and atomically commit the
  content frame plus placement.

Text is not automatically paint-only. Content-sized or wrapping text can
change measurement and therefore dirties the required layout ancestry.

Direct2D may still open one draw transaction, but it clips drawing to admitted
damage when resource/effect semantics allow. DirectComposition retains
unchanged pixels. The retained tray composition owned by DLV-236 remains
outside widget-content damage.

UI Automation emits property events for value changes and structure events
only for real structural changes. The semantic tree, hit-test projection,
accessibility projection, rendered content, and action sequence commit as one
admitted authority.

## Interaction continuity

Stable identities let ordinary data updates preserve host-owned interaction:

- Scroll offsets remain keyed by widget instance, input scope, and scroll node.
- Collection anchors preserve the visible item through keyed changes.
- Focus remains on the same stable node or focus-persistence identity when it
  is still valid.
- Pending slider state survives compatible value publications and reconciles
  with authoritative values through the existing sequence policy.
- A data-only update does not cancel a physical press only when the exact target
  and interaction contract remain valid. A change that removes, disables, or
  changes that contract fails closed through the existing pressed-state owner.
- If the focused node disappears, the existing deterministic focus fallback
  selects the nearest valid destination.

The protocol does not add another gesture scheduler or focus graph. Update
coalescing is bounded and latest-wins; safety/lifecycle changes may still
interrupt immediately.

## Full-trust applications

Sandboxed and full-trust Community applications publish the same checkpoints
and update batches. Full trust applies to the application's private process
authority, not to the overlay host boundary.

A full-trust application may own arbitrary private models, databases,
providers, threads, libraries, and child processes. The host bounds only what
the application submits to shared product ownership: message size, operation
count, tree size/depth, string/resource bounds, update frequency, retained
host state, and action authority.

An arbitrary external application HWND or pixel stream is not an incremental
widget update. It must provide a semantic widget adapter or remain an external
application launched by overlay controls.

## Bounds, ordering, and backpressure

All existing snapshot, string, recursion, node-count, resource, and IPC limits
apply to the materialized result, not merely to each individual operation.
Additionally bound:

- Operations per batch.
- Inserted/replaced subtree size and depth.
- Total materialized nodes and properties.
- Pending batch count and bytes.
- Publication frequency and work admitted per presentation frame.
- Retained checkpoints, fingerprints, and derived resources.

The bridge/session path uses one ordered stream per widget generation. It may
coalesce unsent compatible data updates to the latest complete batch. It may
not drop an authority or structural transition whose omission would make the
next base sequence impossible. Queue overflow requests a complete checkpoint
instead of accumulating unbounded history.

## Compatibility and rollout

- Existing peers continue complete-checkpoint publication.
- Update support is versioned and capability-negotiated before either side
  sends a batch.
- A producer can always fall back to a complete checkpoint.
- A consumer can request resynchronization without restarting the widget.
- No partial compatibility adapter remains after the current pre-release
  protocol generation is adopted; obsolete local overlay-owned state may be
  reset under the existing pre-release policy.
- Public examples and diagnostics must cover both the simple checkpoint path
  and automatic incremental behavior without requiring authors to learn the
  wire operations.

## Implementation sequence

The delivery plan assigns the following serialized milestones:

1. **DLV-239 — retained checkpoint and refresh state.** Stop ordinary
   invalidation eviction, preserve each widget's own checkpoint/envelope, and
   separate semantic retention from derived appearance/style invalidation.
2. **DLV-240 — managed update contract and automatic SDK diff.** Add the
   versioned operation model, validation, capability negotiation, automatic
   producer diffing, and complete-checkpoint fallback without enabling an
   unsupported native path.
3. **DLV-241 — native materialized presentation and atomic batch admission.**
   Parse, validate, apply, fingerprint, resynchronize, and enable updates only
   after the consumer is present.
4. **DLV-242 — incremental presentation work.** Convert admitted impact classes
   into bounded Taffy invalidation, Direct2D damage, resource reuse, targeted
   UIA events, and measured no-op/data/structure behavior.

The sequence follows DLV-232, DLV-238, DLV-237, and DLV-236 because those
milestones already own worker recovery, destination geometry, admission
observability, and retained tray composition in the same native paths.

## Acceptance outcomes

The completed program must demonstrate:

- A hidden invalidation never deletes the last admitted checkpoint.
- Selecting every installed widget immediately uses that widget's own retained
  content and envelope.
- An identical newer publication performs no Taffy layout or content paint but
  advances current authority.
- A progress/value update changes only the affected presentation/UIA property.
- A wrapping text update performs the required bounded layout without
  replacing unrelated semantic or interaction state.
- Keyed collection insert/remove/move preserves focus and scroll anchor.
- Unsupported/missing-base updates atomically fail and recover through one
  complete checkpoint.
- Full-trust and sandboxed differently named fixtures receive identical
  overlay admission behavior.
- The update path cannot starve controller input, grow an unbounded queue, or
  repaint retained tray chrome.
- Measured snapshot bytes, Taffy work, content damage, UIA events, CPU, and
  input-to-visible-update latency improve or remain within established native
  host budgets.

Physical user review remains the final authority for visible continuity while
cycling, entering, scrolling, and interacting with live widgets.
