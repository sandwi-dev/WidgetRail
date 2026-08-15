# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical detail through `436d890` is in the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-Taffy plan is in the [2026-08-14 03:24 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
The complete pre-snapshot-cache plan is in the [2026-08-14 15:45 snapshot](history/delivery-plan/2026-08-14T15-45-17-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baseline

- Main contains accepted DLV-232 `ef56bfc`, DLV-235 `5440e7b`, DLV-230 `fe2e52c`, Taffy baseline
  DLV-221 `8836e07`, and the intervening corrections in history above.
- Taffy is the accepted sole declarative Flex/Responsive Grid geometry engine.
  Widget SDK/protocol, catalog/package/runtime, WidgetBridge transport,
  lifecycle/trust/persistence/providers, Community process boundaries, native
  rendering/accessibility, GameInput, controller focus/navigation, scrolling,
  clipping, motion, and single-HWND ownership remain authoritative.
- Accepted DLV-225/228/229/230 correct Settings height, Audio width, Network
  first-page height, and YT Music composition; physical review remains final.
- DLV-231/233/234/235 were reconstructed after the user-approved merge
  recovery and integrated through `5440e7b`. The accepted chain preserves
  responsive selection, frame-safe sole-transport replacement, typed startup
  failure precedence, explicit Retry, and normal zero-process cleanup.
- DLV-232 `cd378a2`, integrated as `ef56bfc`, makes an unsolicited failed
  worker's last-good presentation explicitly failure-current and inert. It
  revokes widget action, focus, hit-test, motion, quick-action, and UIA
  authority until one valid fresh admission while preserving host Back, Retry,
  Hold-Y recovery, unaffected widgets, and cleanup. The OS/AppContainer crash
  induction remains an honestly untested residual risk after one rejected
  cross-boundary oracle; deterministic affected suites and the native Release
  compile are green.
- The visible native-only Release from exact main `ef56bfc` was rebuilt and
  launched as PID 36856 with SHA-256 `4D0FC7...3E6D`. The prior exact main PID
  30064 exited normally through its hidden top-level HWND `WM_CLOSE`. No new
  physical crash induction or eight-page visual verdict is claimed.
- The user reports three active selection-path defects: retained source-widget
  geometry after cold admission, intermittent tray selection that remains on
  inert old content until A, and tray flashing because widget content and tray
  share redraw ownership. DLV-238, DLV-237, and DLV-236 own those issues in
  that order beginning with current DLV-238.
- The user also identified that complete `WidgetSnapshot` checkpoints conflate
  stable view definition, volatile values, interaction authority, and derived
  appearance validity. Ordinary invalidation currently deletes useful last-
  admitted state and forces avoidable cold presentation/redraw work. The
  approved architecture is in
  [`widget-snapshot-cache-design.md`](widget-snapshot-cache-design.md). DLV-239
  through DLV-242 implement it serially after the active selection-path queue.

## Avalonia disposition — failed and closed

The user ended the Avalonia experiment on 2026-08-14 after repeated physical
layout, shell, controller-routing, process, and reliability failures. AVP-005
and every production cutover are cancelled. Retained experiment branches and
`experiments/AvaloniaOverlayPrototype` are historical evidence only. Do not
dispatch, integrate, relaunch, cut over, or delete them without a new explicit
user decision. The native overlay is the sole production presentation path.

## Execution rules

- Operate exactly two production lanes: `widgets` and `platform`.
- Each task implements only its lane's Assigned milestone, then the first Ready
  same-lane milestone whose baseline is present.
- Shared protocol/architecture work is serialized to the named lead lane.
- Implementation tasks never edit reviewer-owned documents. The planner
  independently reviews actual diffs and retained evidence.
- For native review and hotspot decomposition, keep the user-installed clangd
  index current for the exact worktree and use semantic definition/reference
  queries alongside `rg`; do not treat text search alone as ownership proof.
- Rejected commits remain unintegrated. Corrections stay in their lane and do
  not interrupt unrelated coherent work.
- Never push. Stop for credentials, destructive recovery, substantial merge
  conflicts, undocumented input/window APIs, publication, physical-only
  evidence, or a material product choice.
- User-visible defects and requested features outrank internal refactors.
- Run focused affected Release suites. Use one bounded linked-host group when a
  language/process boundary changes. Run Tier 3 only at a named checkpoint.
- After one bounded attempt and diagnosis of an unreliable integration case,
  stop rerunning or redesigning its harness. Retain direct production-path
  review and disclose the untested residual risk; do not delay a small coherent
  milestone to manufacture synthetic coverage for every theoretical branch.
- Screenshots are optional support. Do not build or repair capture tooling for
  an ordinary product assignment; use live user review.
- New managed test projects use MSTest.Sdk 4.3.2. Existing executable suites
  remain valid unless migration is explicitly assigned.
- Full-trust Community applications may use ordinary user-level APIs in their
  own process. Bound shared product inputs/resources, not private application
  CPU, memory, databases, files, sockets, dependencies, or child processes.

## Product and architecture decisions

- Games & Apps remains bundled. Spotify, Game Launcher, and YT Music are
  ordinary Community applications. Core assemblies contain no service-specific
  identities, DTOs, APIs, or known-tree behavior.
- Taffy owns declarative geometry only. The host retains semantic validation,
  DirectWrite measurement, scroll offsets, clipping, pixel/DPI policy,
  focus-follow, controller navigation, accessibility, rendering, animation,
  and HWND placement.
- Widget width and height are independent `Preferred`, `Content`, or
  `FillAvailable` axes. Content uses bounded Taffy intrinsic measurement rather
  than guessed page dimensions. Existing provider/list views remain stable
  Preferred surfaces unless directly justified.
- The tray and controller guide retain fixed bottom-center screen coordinates.
  Content envelopes grow or shrink upward/outward. DLV-236 gives persistent
  tray pixels independent child-visual/surface ownership under the sole
  DirectComposition target without adding another HWND or focus/input tree.
- One HWND wraps the admitted content-plus-chrome union; the overlay never
  becomes a monitor-sized desktop surface.
- A complete `WidgetSnapshot` is a last-admitted presentation checkpoint, not
  an expiry cache entry. Ordinary invalidation records refresh demand and does
  not delete it. Hard removal is limited to restart, removal/runtime
  replacement, generation/protocol incompatibility, trust revocation, or
  unsafe corruption.
- Post-checkpoint changes use the permanent generic operation set from the
  snapshot design: typed document/node properties, keyed insert/remove/move,
  subtree replacement, and complete-checkpoint fallback. The SDK normally
  computes updates; authors do not manually construct wire patches.
- Semantic checkpoints, host-resolved appearance/resources, and host-owned
  focus, scroll, press, slider, layout, UIA-provider, composition, and placement
  state have separate validity and ownership.
- Identical semantic publications may advance sequence/action authority without
  layout or paint. Changed properties invalidate only their declared authority,
  accessibility, resource, paint, layout, or surface effects; unknown effects
  fall back to subtree/checkpoint replacement.
- Sandboxed and full-trust widgets use the same bounded semantic update and
  admission protocol. Full trust does not grant overlay HWND/render/input/focus
  authority.
- The Microsoft GameInput/Guide owner remains authoritative. No second reader,
  bridge transport, overlay HWND, compositor root, or focus tree is permitted.

## Active task map

| Lane | Task/worktree | Current state |
| --- | --- | --- |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` on `codex/impl-widgets-taffy-ui` | Idle clean. DLV-240 begins only after accepted DLV-239 is integrated and the planner sends the serialized cross-lane baseline. |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on `codex/impl-platform-integration` | DLV-238 commit `3716063` rejected pending the material transition correction below; task idle. DLV-237, DLV-236, and DLV-239 remain Ready but cannot start from the rejected tip. DLV-241/242 await DLV-240 integration. |

DLV-217 remains accepted through `d57fd06` but unintegrated because its exact
aggregate is honestly 40/41 with one reviewer-history-link failure. Preserve
that branch. Integration requires separate explicit user approval.

## Platform lane

### Accepted — DLV-232: generic worker crash isolation and recovery

Baseline: accepted DLV-235 cumulative platform correction. Visible objective: one
credential-free Community worker may crash repeatedly without closing the
overlay, disturbing other widgets, or leaving an unauthorized stale
presentation; a normal reactivation or generic Hold-Y restart recovers through
the existing lifecycle owner.

Reviewed evidence: the abandoned packaged crash oracle could not cross the
AppContainer's admitted content boundary and must not be redesigned or rerun.
Static review found the product gap: an unsolicited post-admission worker exit
records failure but leaves its admitted snapshot treated as current, so stale
semantic/input authority can survive even though explicit restart correctly
clears it.

Required correction: through the existing session/lifecycle owner, atomically
classify an unsolicited exited worker's admitted presentation as retained last-
good pixels but failure-current and inert. Revoke action authority and active
focus/hit-test/UIA interaction for that presentation without substituting
another widget's pixels. Normal Retry/reactivation or generic Hold-Y must start
exactly one fresh generation; only valid admission restores current interactive
authority. Preserve the unaffected widgets, tray focus, bounded retry,
sanitized diagnostics, job/process cleanup, and existing failure precedence.

Verification: use only the affected deterministic coordinator, state,
lifecycle, restart, controller, and cleanup cases plus one compile. Do not add
or rerun a packaged/AppContainer crash oracle. Review the actual worker-exit,
job teardown, admission, and stale-authority paths directly and report the
remaining OS-level scenario as untested residual risk. The inherited retired-
gesture revocation race is not a DLV-232 blocker when unchanged. Do not add
service-specific behavior, another lifecycle/input authority, a test-only
production escape hatch, public protocol work, Tier 3, or unrelated refactoring.

Accepted disposition: `cd378a2`, integrated as `ef56bfc`. Independent review
traced the typed worker failure through the sole bridge event pump, coordinator
generation revocation, failure-retained rendering, action/focus/hit-test/UIA
revocation, restart, and fresh admission. Bridge retirement drains the old
registration's notification lane before replacement publication, so the
generation-less internal failure notification cannot overtake a newly admitted
registration. Focused affected suites and one native Release compile are green;
the real OS/AppContainer crash remains the documented residual risk.

### Assigned — DLV-238: commit admitted destination geometry

Baseline/owner/dependencies: accepted DLV-232 integrated into main; platform native presentation-extent and DirectComposition placement only; do not interrupt DLV-232 or start from an unreviewed tip.
Visible objective: after a cold/asynchronous switch, retain the old widget's pixels and envelope only until the destination snapshot is admitted; the admitted widget must immediately own its authored width, height, responsive viewport, and final presented extent without waiting for A, a provider update, or another selection.
Required correction: retire the retained extent as admission changes authority, resolve placement from the destination `DesiredPresentationExtentDip` rather than the pinned old `PresentedPresentationExtentDip`, render the new snapshot once at its destination viewport, and atomically commit that complete frame plus placement before animating old-to-new envelopes. Completion must settle through one explicit destination placement/layout; it may not clear the override with `redraw=false` while leaving old geometry current, scale a destination tree laid out at the source viewport, or add widget identities, another HWND/surface owner, or a second layout path.
Structural boundary: make this the first staged reduction of the `OverlayApp`
hotspot. Extract one focused presentation-transaction owner for desired versus
presented extents, retained-snapshot authority, destination layout, animation
settlement, and atomic composition/window placement. Leave `OverlayApp` as the
orchestrator that receives the typed admission and commits the resulting
directive. Provide a before/after field and responsibility map, use clangd
definition/reference results for every moved native symbol, and remove shared
mutable knowledge rather than merely moving methods or creating a cosmetic
wrapper. Do not attempt a big-bang split or mix later trace/chrome/cache owners
into DLV-238.
Preserve: one HWND/root compositor/focus/input/UIA authority, Taffy as sole declarative geometry engine, fixed absolute tray/guide bounds, retained inert old semantics before admission, stale/cancelled snapshot rejection, reduced-motion/device-loss fallback, clipping/scroll/focus reveal, and atomic content/geometry authority.
Acceptance: cold and cached switches across all eight widgets, including compact-to-tall, tall-to-wide, rapid selection, delayed admission, late revoked completion, failure/last-good, and provider updates, prove that every `content=admitted rendered=<destination>` frame uses the destination surface request and that final presented equals desired without a later snapshot. The Audio Mixer to Network Controls regression must move from retained `592x698` to Network's admitted `632x878` envelope, lay Network out at its own viewport, and retain stationary tray/guide coordinates with no flash, dark band, seam, stale UIA, or intermediate input mismatch.
Verification: focused widget-switch, extent-transition, composition-placement, surface-policy/Taffy, focus/UIA, reduced-motion/device-loss cases, one bounded eight-widget host route, and native Release build only; no Tier-3 aggregate, provider/package change, capture-harness work, or unrelated refactor.
Stop for per-widget sizing logic, destination content rendered against source geometry, non-atomic HWND/content authority, another compositor/window/focus/input owner, or an undocumented platform dependency or material animation decision.

Independent review disposition for `3716063`: rejected. The commit correctly
separates destination layout from the retained presented extent, renders the
admitted widget at its own viewport, settles final geometry explicitly, and
provides a real first-stage `OverlayApp` responsibility reduction. The retained
evidence and focused suites are otherwise proportional, and the unrelated
DLV-231 close/reconnect timeout was correctly not rerun.

The remaining blocker is in the actual transition authority, not destination
layout. `OverlayCompositionSurface::ApplyPresentation` applies the scale and
offset to the one visual containing widget content, guide, and tray. Therefore
the admitted destination's tray and guide scale/move with the content envelope
during every non-reduced-motion transition even though their authored local
bounds are unchanged. The host-route oracle compares those untransformed local
bounds and therefore cannot prove fixed absolute screen bounds. The same motion
steps update the visual transform and inverse pointer mapping, but publish UIA
only at admission start and final settlement, leaving intermediate UIA bounds
stale relative to the visible/input geometry.

Do not integrate or begin DLV-237. The correction must retain the accepted
destination-viewport transaction while proving actual start/mid/end screen
bounds and matching input/UIA authority. Because the current single transformed
visual cannot both animate the content envelope and keep its embedded chrome
stationary, the next implementation choice is material: either snap the whole
destination envelope immediately until DLV-236 separates retained chrome, or
bring forward the minimum child-visual/chrome separation that DLV-236 otherwise
owns. The planner must obtain the user's choice before changing animation or
surface ownership. No unchanged rerun or local-coordinate assertion can close
this review.

### Ready after DLV-238 integration — DLV-237: correlate deferred widget admission

Baseline: accepted DLV-238 integrated into local main. Owner: platform native
selection/lifecycle/session observability only. Do not interrupt DLV-232/DLV-238 or
start from its unreviewed branch tip.

Visible objective: produce sufficient trustworthy evidence to identify why a
tray-selected widget can remain on the previous inert presentation until the
user presses A. This is an observability milestone, not authorization to guess
at or implement a behavioral fix. Preserve the current selected/active widget,
Visible versus Interactive lifecycle, snapshot admission, stale-result, and
last-good presentation semantics.

Current reproduced evidence:

- Tray navigation updates `selected` and `active` to the new widget while the
  previously rendered widget remains retained and semantically inert.
- A later A input changes the selected widget's lifecycle target from Visible
  to Interactive and is shortly followed by valid snapshot admission.
- The exact intervals contain no explicit worker-start, bridge transport,
  protocol, lifecycle, or queue-full error. Rapid cycling also produces
  expected stale-completion rejection, and later sessions sometimes admit the
  same widgets automatically.
- The leading investigation boundary is the deferred cold-start handoff between
  `ApplyStateTransition` and the posted snapshot-refresh handler, but this is a
  hypothesis rather than an accepted root cause.

Required instrumentation:

- Assign one bounded correlation/transition ID to each tray selection and
  record selected widget, active widget, `deferColdStart`, and whether a current
  snapshot is present.
- Record whether the snapshot-refresh message was posted, whether and when it
  was dequeued, and the elapsed queue delay.
- Record the existing lifecycle owner's decision with desired target and one
  typed action such as `Establish queued`, `deduplicated`, `replaced`, or
  `skipped`, plus a bounded typed reason such as `deferred`, `already-current`,
  `failure-current`, or `queue-full`.
- Correlate the existing worker/session request ID, widget, generation,
  lifecycle target, request kind, and queued/started/completed timestamps.
- Record the completion disposition as admitted, failed, stale generation,
  wrong lifecycle, or cancelled. Record A only when it causes the meaningful
  Visible-to-Interactive lifecycle transition for the correlated selection.
- Keep a bounded in-memory transition trace and emit only state changes or a
  threshold breach such as admission exceeding 250 ms. Do not log every
  controller repeat, paint, ordinary provider update, or snapshot body; do not
  serialize presentation trees; do not perform synchronous file I/O on the UI
  thread; and do not rewrite a complete trace file per input event.
- Reuse the existing diagnostic/log owner and sanitize/bound every field,
  retained transition, timestamp, and message. Do not add another lifecycle
  owner, worker queue, input router, transport, trace process, or public protocol
  concept.

Acceptance:

- Deterministic focused cases correlate selection through posted/dequeued
  refresh, lifecycle decision, worker queue/start/completion, and final
  admission while preserving exact generation and lifecycle authority.
- Direct cases distinguish refresh not dequeued, establishment skipped or
  deduplicated, request queue delay, replacement/cancellation, slow completion,
  stale/wrong-lifecycle rejection, and admission without presentation refresh.
  Test seams may observe existing decisions but must not synthesize a second
  production scheduler or worker owner.
- One direct A case emits only the meaningful Visible-to-Interactive transition
  and proves ordinary controller repeats do not generate trace/file churn.
- The bounded buffer drops or coalesces old detail deterministically, retains
  the current transition and terminal disposition, and cannot materially affect
  input-to-selection or hidden/idle behavior.
- Run only affected selection, lifecycle, session-coordinator, delayed/cold
  widget, and diagnostic tests plus the native Release build. The planner then
  visibly launches the accepted instrumented Release for a joint user test. The
  implementation task and planner must not spend time attempting to reproduce
  the intermittent product symptom independently. No Tier-3 aggregate, provider
  change, capture-harness work, or speculative behavioral correction.
- The planner independently reviews the correlated evidence and only then
  authors a separate bounded correction assignment for the proven failing
  branch. The user will exercise ordinary tray cycling and provide the exact
  test interval; absence of an A-required interval during that joint test is an
  honest evidence result, not permission to claim the product defect fixed.

Stop for a public protocol/schema change, unbounded or per-frame logging,
snapshot-content retention, UI-thread file/serialization work, another
selection/lifecycle/session authority, or an implementation choice that changes
the user-visible admission behavior before the cause is established.

### Ready after DLV-237 evidence review — DLV-236: retain the icon tray independently

Baseline: accepted DLV-237 integrated into local main. Owner: platform native
composition/rendering only. Do not interrupt DLV-232 or DLV-237, and do not
start from an unreviewed branch tip. DLV-236 remains independent of the DLV-238
geometry correction, but follows the DLV-237 evidence review so only one
selection-path milestone is in flight at a time.

Visible objective: cycling through widgets may repaint the two tray tiles whose
selection state changes, but the persistent tray background and unchanged
icons must not clear, flash, or repaint again when a cold/warm worker snapshot
arrives, widget content animates, or the content envelope changes size.

Required implementation:

- Keep the single overlay HWND, sole DirectComposition device/target and root
  visual tree, native renderer, accessibility provider, focus graph, hit-test
  authority, and GameInput owner. Add retained child visual/surface ownership
  for the host tray; do not create another HWND, top-level compositor, input
  window, semantic tree, controller router, or widget protocol concept.
- Separate tray invalidation from widget/content invalidation. Tray paint is
  allowed only for selection/reorder interaction, catalog/order/overflow
  changes, host appearance, DPI/text-scale/accessibility changes, explicit
  animation authored for tray interaction, and graphics-device recreation.
  Snapshot admission, provider updates, widget focus/scroll/slider paint,
  content reveal, and content-envelope motion must leave the admitted tray
  surface retained.
- Preserve one atomic presentation commit. The selected tile, inert retained
  old widget pixels, newly admitted widget content, guide state, clips,
  transforms, and HWND placement may not expose a mismatched intermediate
  authority. Content-size animation moves only the content envelope; compensate
  child offsets as the one HWND changes so the tray and guide retain their fixed
  absolute bottom-center screen rectangles.
- Preserve premultiplied-alpha/color-key boundaries, device-loss recovery,
  reduced-motion/high-contrast behavior, overflow controls, pointer hit tests,
  controller selection/reorder, UIA Selection/Invoke bounds, and normal
  resource/process cleanup. Do not add widget identity branches or change
  widget/package/runtime code.

Acceptance:

- Add deterministic per-surface paint/commit evidence for all eight installed
  widgets covering cached, cold, delayed, failed, and late-revoked snapshots.
  Each accepted tray selection may produce one bounded tray update; subsequent
  snapshot admission and content-motion frames must report zero tray-surface
  redraws. Static tray regions remain pixel-identical; only the old/new selected
  tiles or a named overflow/reorder affordance may differ.
- Prove content-only provider, slider, scroll, focus, and live-state updates do
  not repaint the tray. Separately prove catalog/order, appearance, DPI/text
  scale, accessibility, and device recreation repaint/rebuild it exactly when
  required.
- Retain identical absolute tray/guide bounds through compact-to-wide and
  wide-to-compact transitions, no opaque flash/dark band/transparent seam, one
  current UIA selection, bounded draw/commit timing, and normal zero-process
  shutdown. The user's physical widget-cycling verdict is the final flash gate.
- Run only affected composition, placement, tray, controller, accessibility,
  device-loss, and one bounded eight-widget host route plus the native Release
  build. No Tier-3 aggregate, provider work, package rebuild, or capture-harness
  expansion.

Stop for a second HWND/compositor/input/focus/accessibility owner, loss of atomic
selection/content authority, per-widget composition behavior, an undocumented
DirectComposition dependency, or a material change to tray geometry.

DLV-218 remains dependency-blocked on the user's separate DLV-217 integration
decision. Mixed-monitor, audio/Bluetooth, legacy-controller, and assistive-
technology gates still require hardware or user evidence; do not manufacture
additional internal filler after DLV-236.

### Ready after DLV-236 integration — DLV-239: retain checkpoint and separate refresh state

Lane/owner/baseline: platform; existing native session, bridge adapter,
appearance, and presentation-cache owners on accepted DLV-236 main. This is the
first milestone governed by `widget-snapshot-cache-design.md`.

Visible objective: ordinary hidden/provider invalidation never removes the
widget's last admitted checkpoint or causes another widget's pixels/envelope to
stand in. Selection immediately presents that widget's own retained state while
the existing lifecycle owner requests current state.

In scope: introduce explicit Current/RefreshRequested/RefreshInFlight state;
retain last-good checkpoint through ordinary failure/cancellation/stale result;
hard-remove only for the documented authority transitions; separate appearance
and derived-resource invalidation from semantic checkpoint eviction; keep
retained content inert until current action authority is admitted.

Out of scope: public update protocol, SDK diffing, incremental Taffy/damage,
new caches, eager waking of unloaded workers, or changed residency policy.

Acceptance: all eight widgets cover hidden invalidation, resident/suspended/
unloaded selection, refresh success/failure/cancellation, rapid switching,
appearance change, restart/removal/generation/trust failure, own-envelope
retention, exact action authority, bounded cache/resource counts, and normal
shutdown. Measure cold-retained selection latency and background wakeups before/
after. Tier 1 affected native/bridge/lifecycle/appearance/selection suites and
one bounded host route; Tier 2 only if the existing bridge boundary changes.

Concurrency/stop: widgets lane remains idle. Stop for a public schema change,
second cache/lifecycle owner, stale interactive authority, unbounded retention,
or a material residency/security decision.

## Serialized snapshot update program

### Awaiting DLV-239 integration — DLV-240: managed update contract and SDK diff

Lane/owner/baseline: widgets lead for serialized WidgetProtocol, WidgetSdk,
WidgetRuntime, and WidgetBridge work after accepted DLV-239 main; platform lane
must not edit shared protocol/bridge files concurrently.

Objective: version the atomic checkpoint/update contract and automatically
produce the permanent generic operations: typed document/node properties,
keyed insert/remove/move, subtree replacement, and full-checkpoint fallback.
Capability negotiation must keep production on checkpoints until a native
consumer exists.

Requirements: exact base/new sequence and instance/generation; validate the
complete batch and materialized bounds; SDK tree diff using stable IDs; property
impact metadata; identical-model no-op; deterministic fallback for unstable IDs,
unknown properties, large diffs, missing base, and unsupported peer; authors do
not manually build patches. Full-trust and sandboxed differently named fixtures
use the same contract. Bound operations, bytes, depth, nodes, queueing,
coalescing, and diagnostics.

Acceptance: validator/JSON/version/capability compatibility; property, keyed
collection, subtree, no-op, base mismatch, malformed/oversized, coalescing, and
full fallback tests; compiled public examples and migration docs; Tier 1 affected
managed suites and Tier 2 one bridge/runtime group. Do not activate update
traffic or run Tier 3 until DLV-241.

Stop for per-control mutation messages, manual-patch authoring as the normal SDK
path, unbounded diff/history, service identity, or weakening checkpoint support.

### Awaiting DLV-240 integration — DLV-241: native materialized update admission

Lane/owner/baseline: platform lead after accepted DLV-240 is integrated. Own the
existing native session/admission/semantic tree only; no concurrent widgets
protocol edits.

Objective: parse, validate, and atomically apply negotiated update batches to
one materialized current presentation, with full-checkpoint resynchronization.
Enable update traffic only after both peers prove support.

Requirements: exact base sequence/generation; all-or-nothing application;
materialized-result bounds; host-computed structure/layout, visual/data,
interaction/UIA, and surface fingerprints; identical semantic publication
advances authority with zero layout/paint; missing/invalid base requests one
checkpoint; hidden updates modify retained semantics without render/UIA work;
no competing cache or action authority.

Acceptance: differently named sandboxed/full-trust fixtures cover every
operation, identical publication, hidden update, malformed/partial/duplicate/
out-of-order batch, reconnect/resync, stale generation, current action sequence,
focus/scroll/slider/press reconciliation, failure retention, cache bounds, and
zero-process shutdown. Tier 1 native/session/render-tree suites, Tier 2 managed-
to-native group, then one exact clean Tier-3 checkpoint because the public
cross-process protocol becomes active.

Stop for partial application, stale actions, another semantic store/transport,
unbounded retained history, widget-specific behavior, or unresolved protocol
compatibility/security decision.

### Awaiting DLV-241 integration — DLV-242: incremental layout, damage, and UIA

Lane/owner/baseline: platform presentation owners after accepted DLV-241 main.
No public protocol expansion.

Visible/performance objective: admitted updates perform only their declared
authority, accessibility, resource, paint, layout, or surface work. Ordinary
value updates must not rebuild/redraw the full widget or retained tray.

Requirements: translate property impacts into authority-only, paint damage,
bounded Taffy node/ancestor invalidation, structure, resource, UIA property/
structure, or destination-surface work; repaint old/new visible damage; use
full-widget layout/draw only as a measured correctness fallback; preserve focus,
scroll anchors, compatible slider adjustment, presses unless their target
contract changes, clipping, device loss, and DLV-236 tray retention.

Acceptance: exact counters prove zero layout/paint for identical publications,
bounded node damage for progress/value/fixed text, required ancestry layout for
wrapping text, keyed collection anchor/focus continuity, subtree fallback,
targeted UIA events, resource reuse, no tray redraw, and correct surface change.
Measure CPU, Taffy work, damage area, update latency, memory/resources, and input
latency against full-checkpoint baseline across all eight widgets. Tier 1
renderer/Taffy/focus/scroll/slider/press/UIA/composition/device-loss and one
bounded eight-widget Release route; no unchanged Tier 3.

Stop for per-widget/element identity branches, unproven paint-only guesses,
another focus/scroll/interaction owner, visual tearing, stale UIA/hit testing,
or performance evidence that the complexity has no material benefit.

## Widgets lane

No independent widgets milestone is executable before serialized DLV-240.
Accepted DLV-225/226/228/229/230 remain integrated. Later widget styling or
provider changes require fresh user evidence rather than speculative work.

### Awaiting DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths
only after both autonomous Community packages are integrated. Retain generic
App Library behavior for bundled Games & Apps and consenting sandboxed users.
Add an architecture check rejecting Community identities/domain types in core.
Do not delete credentials, provider data, accounts, or user files.

## Serialized integration order

1. Main is accepted through DLV-232 `ef56bfc`; DLV-238 is Assigned.
2. Execute/integrate DLV-238, DLV-237, DLV-236, and DLV-239 in platform order.
   Launch every accepted visible milestone.
3. After DLV-237 launch, the user performs the joint tray-cycling test and the
   planner assigns only the evidence-backed correction without disrupting the
   already ordered independent work.
4. After DLV-239 integration, dispatch DLV-240 to the widgets lane as sole
   shared protocol/managed lead. Platform does not edit shared files.
5. Integrate accepted DLV-240, then dispatch DLV-241 to platform. DLV-241 is the
   one exact Tier-3 protocol activation checkpoint.
6. Integrate accepted DLV-241, then execute DLV-242. Launch the complete
   incremental candidate for user cycling/scrolling/interaction review.
7. DLV-217/218 remain a separate explicit integration decision and do not mix
   with the snapshot update program.

## Manual and packaged verification queue

- Jointly test intermittent tray-selection admission after accepted DLV-237 is
  launched. User cycles normally and reports the interval; planner correlates.
- User verdict on each freshly launched accepted native Release remains final
  for panel/tray cohesion, controller feel, motion, sizing, continuity, and
  incremental update behavior.
- Physical controller/display evidence remains required for changed navigation,
  focus reveal, scrolling, slider/press continuity, or visual presentation.
- Accepted YT Music 0.2.9 awaits the bounded catalog cleanup approval below.
- Live Spotify account/Premium/Web Playback/EME/OAuth, IGDB, and SteamGridDB
  enrichment are credential-gated; offline behavior must not depend on them.
- Computer control may omit the no-taskbar overlay; use exact HWND/UIA/log
  fallback rather than changing taskbar behavior.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| Avalonia migration/cutover | Failed and cancelled by user. | New explicit user decision; never resume old AVP work. |
| DLV-217 integration | Exact aggregate is 40/41 with one reviewer-history-link red. | Explicit user approval to integrate despite the honest documentation-only red. |
| DLV-230 visible 0.2.9 | YT Music catalog is at its eight-version ceiling. | Approval to remove only inactive non-selected 0.2.0, then install/enable 0.2.9. |
| DLV-218 | Requires DLV-217 integration. | Accepted DLV-217 on main. |
| Trusted fixed-video/PiP | Paused WebView2 measured about 348.7 MiB private and 4% CPU. | Changed budget or authorized content/process experiment. |
| Audio default-device selection | No documented supported Windows setter. | Primary Microsoft API plus reversible provider/hardware plan. |
| Native uninstall reconciliation | Synthetic catalog removal emitted no managed revision/native event. | Deterministic disabled/nonresident removal event. |
| YouTube authenticated library | Google OAuth/account; Watch Later unsupported by Data API. | Approved minimum-scope OAuth plan and authorized account. |

## Recent accepted milestones

| Milestone | Accepted result |
| --- | --- |
| DLV-232 | `cd378a2`, integrated as `ef56bfc`: unsolicited worker exit retains only its own last-good pixels as inert and restores authority solely after fresh admission. |
| DLV-235 | `b0ea2b4`, reconstructed/integrated as `5440e7b`: worker-start failure survives lifecycle retarget/revocation; explicit Retry owns one fresh generation; valid admission clears it. |
| DLV-234 | `9435050`, reconstructed/integrated as `348df2e`: incomplete frames taint the sole transport and bounded owned-process replacement restores framing. |
| DLV-233 | `1323c8a`, reconstructed/integrated as `4b8e0b7`: self-contained PATH-isolated Audio host fixture with strict UIA/scroll/focus behavior. |
| DLV-231 | `2a379ac`, reconstructed/integrated as `fbd2f02`: delayed requests remain revocable while tray/input/close and later valid admission stay responsive. |
| DLV-230 | `7323468`, integrated as `fe2e52c`: coherent responsive YT Music panel; visible install awaits catalog cleanup approval. |
| DLV-229 | `1a8c201`, integrated as `1ddedb4`: preferred Network envelope exposes complete first-page scan state. |
| DLV-228 | `2784401`, integrated as `220a415`: Audio rows/cards/sliders consume admitted width generically. |
| DLV-226 | `2276b4c`, integrated as `9755406`: truthful eight-widget surface-policy audit. |
| DLV-225 | `3e887ee`, integrated as `3eb0eaf`: bounded content-sized Settings root. |

Do not mark the continuing delivery goal complete; continue until the user pauses/replaces it or all useful lanes are genuinely blocked. Never push.
