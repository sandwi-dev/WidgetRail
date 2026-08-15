# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical detail through `436d890` is in the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-Taffy plan is in the [2026-08-14 03:24 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
The complete pre-snapshot-cache plan is in the [2026-08-14 15:45 snapshot](history/delivery-plan/2026-08-14T15-45-17-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baseline

- Main contains the physically rejected DLV-238/DLV-236 tray implementation
  and corrected DLV-237 admission trace through `6e2969b`; DLV-232 `ef56bfc`, DLV-235
  `5440e7b`, DLV-230 `fe2e52c`, Taffy baseline
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
- The earlier `7fa146a` Release was physically rejected for black cleared tray
  tiles, clipped focus chrome, and widget-dependent tray geometry. Cumulative
  DLV-238/DLV-236 removed tile-level damage and retained one complete small tray
  raster, but exact main `6e2969b` was also physically rejected: the tray still
  visibly moves while differently sized widgets are cycled. Live PID 39536 logs
  expose three allegedly fixed rectangles (`2186,1259,747,141`,
  `2185,1259,748,141`, and `2186,1259,748,141`). Source review shows that the
  session rectangle is initialized from current widget/container geometry,
  reset on reopen, and applied through a DirectComposition commit followed by a
  separate `SetWindowPos`. The accepted test compared the cached intended
  rectangle to diagnostics derived from that same cache, omitted cross-reopen
  equality, and could not observe the compositor/HWND interval. DLV-244 is the
  mandatory correction; DLV-239 is paused behind it.
- Corrected DLV-237 now correlates selection, posted/dequeued refresh, lifecycle,
  request, completion, admission, and meaningful A stages to the exact selected
  widget. Pinned/background work cannot terminalize or suppress that trace. The
  user and planner will reproduce the intermittent A-required admission symptom
  together from this instrumented Release; no behavioral cause is claimed yet.
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
  Content envelopes grow or shrink upward/outward. The cumulative DLV-238/
  DLV-236 correction gives persistent host chrome independent child-visual/
  surface ownership under the sole DirectComposition target without adding
  another HWND, compositor root, accessibility provider, or focus/input tree.
- Keep the tray's existing small host-owned layout policy during the current
  correction. Do not migrate its internal tile placement to Taffy merely to
  replace straightforward arithmetic; reconsider that separately only if
  additional tray-layout complexity or repeated defects provide evidence that
  one more declarative layout owner would reduce maintenance cost.
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on `codex/impl-platform-integration` | Idle clean at `9f2b4ca` after safely stopping before DLV-239 edits. DLV-244 corrects the physically rejected tray stationarity implementation first; DLV-239 remains paused. |

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

### Integrated, physically rejected — DLV-238 + DLV-236: commit destination geometry with independent chrome

Baseline/owner/dependencies: accepted DLV-232 integrated into main; platform native presentation-extent and DirectComposition placement only; do not interrupt DLV-232 or start from an unreviewed tip.
Visible objective: after a cold/asynchronous switch, retain the old widget's pixels and envelope only until the destination snapshot is admitted; the admitted widget must immediately own its authored width, height, responsive viewport, and final presented extent without waiting for A, a provider update, or another selection.
Required correction: retire the retained extent as admission changes authority, resolve placement from the destination `DesiredPresentationExtentDip` rather than the pinned old `PresentedPresentationExtentDip`, render the new snapshot once at its destination viewport, and atomically commit that complete frame plus placement before animating old-to-new envelopes. Completion must settle through one explicit destination placement/layout; it may not clear the override with `redraw=false` while leaving old geometry current, scale a destination tree laid out at the source viewport, or add widget identities, another HWND/independent composition owner, or a second layout path. Retained child surfaces beneath the sole existing composition owner are explicitly authorized by the cumulative DLV-236 decision below.
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

Physical rejection supersedes the earlier source/test acceptance. The retained
surface separation and whole-small-tray repaint policy remain useful, but the
stationarity claim is false. `CompositionChromeSession` freezes a rectangle
derived from the first widget/container of one visible session; hide/reopen
resets that authority. More importantly, a visible resize commits child-visual
offsets relative to the future container before moving/resizing the HWND in a
separate operation. The host test cycles widgets but reads an intended rectangle
reconstructed from the same cached state, so it cannot observe an intermediate
screen-space jump. Its reopen route never compares pre-hide and post-reopen tray
bounds. Do not treat the integrated implementation as an accepted baseline for
stationarity, and do not resume DLV-239 until DLV-244 is accepted and launched.

### Assigned — DLV-244: make tray stationarity externally authoritative

Lane/owner/baseline: platform native placement, presentation-transaction, and
existing sole DirectComposition owner on integrated main `677d302`. DLV-239 is
paused and must not be mixed into this correction. Preserve the separate
content/guide/tray child surfaces and the complete bounded tray repaint for
tray-owned changes.

Visible objective: cycling any sequence of differently sized widgets, hiding
and reopening on any selected widget, and completing or reversing content
motion must leave the tray at one identical bottom-center physical screen
rectangle. No transient jump, flash, one-pixel drift, or widget-derived anchor
is acceptable.

Required correction:

- Establish tray/guide geometry from the authoritative monitor work area, DPI,
  interface/accessibility scale, catalog/order, and host chrome policy. Widget
  content width, height, surface hints, presented extent, container parity, or
  selected identity must never seed or recompute the absolute chrome anchor.
- Retain that anchor across widget selection, admission, motion, cancellation,
  hide/reopen, and provider/content updates. Change it only for an explicit
  monitor/work-area/DPI, accessibility/interface-scale, appearance, or
  catalog/order event whose chrome geometry actually changes; record the typed
  reason.
- Remove every externally visible interval in which child-visual offsets refer
  to a future/past HWND rectangle. The existing sole presentation transaction
  must admit one coherent screen-space state for HWND geometry, content
  presentation, tray/guide offsets, hit testing, and UIA. Do not claim atomicity
  merely because cached desired values are equal.
- Preserve one HWND, one DirectComposition target/root, one renderer, one
  accessibility provider/tree, one focus/input owner, and transparent unused
  client pixels. Do not reintroduce tile damage, a combined content/tray raster,
  a monitor-sized visible shell, or widget-specific geometry.

Acceptance and verification:

- Add a deterministic lifecycle matrix that starts the overlay with each of all
  eight widgets selected, cycles every differently sized widget in both
  directions, hides/reopens between selections, and covers reduced motion plus
  compact/tall/wide transitions. It must compare the same absolute tray corners
  across the complete matrix, including pre-hide versus post-reopen.
- The oracle must derive externally presented screen coordinates from the
  applied HWND rectangle plus the actual committed child-visual transaction
  step. It may not compare `CompositionChromeSession::trayScreenBounds` to a
  diagnostic reconstructed from that same field. A fake/order seam may record
  externally observable transaction phases, but no test-only compositor owner
  or second production scheduler is permitted.
- Include fractional-DPI and odd/even physical-width cases that reproduce the
  observed 747/748 width and 2185/2186 left-edge drift. Assert that no
  intermediate phase exposes different absolute tray or guide corners.
- Directly prove content-only admission, motion, provider, focus, scroll, and
  slider work does not repaint the tray; tray selection/order/style changes may
  replace the whole small tray once.
- Run only the affected placement, composition, widget-switch, chrome,
  targeting/UIA, reduced-motion/device-loss tests and one native Release build.
  Do not add capture tooling or run Tier 3. The planner must review the source
  transaction sequence independently, then launch the exact corrected Release.
  Physical user cycling is mandatory acceptance evidence; automated green is
  not sufficient to close this user-reproduced compositor defect.

Stop for another HWND/compositor/focus/input authority, a monitor-sized visible
surface, a self-referential stationarity oracle, widget-identity geometry, an
undocumented composition API, or a material shell/animation choice.

Independent review disposition for `de3cf47`: rejected; retain as the base for
one bounded same-lane correction and do not integrate it. The work-area-derived
tray/guide anchor is directionally correct, but the externally visible
transaction is still non-atomic. On a visible content-envelope change,
`SetWindowPos` first moves the HWND and therefore the already committed old
tray visual; only afterward does `CommitFrames` publish the compensating child
offset. Reversing the earlier commit-then-move order merely reverses which
transient is exposed.

The new oracle does not observe that interval. It records `GetWindowRect` only
after the window move and combines it with `appliedChromePresentation`, which
is set when `SetOffset*` succeeds before the DirectComposition device commit is
known to succeed. It therefore represents attempted internal state, not a
screen state observed after each externally visible phase. The route also
opens with one initial widget and performs only one hide/reopen on the final
selection; it does not execute the required all-eight initial-selection and
cross-reopen matrix, and its 747/748 text is not a direct fractional-DPI/parity
reproduction.

Bounded correction required atop `de3cf47`:

- Keep the valid work-area-derived absolute chrome anchor, but stop moving the
  top-level HWND as part of ordinary widget selection. Establish one transparent
  visible-session host container from the work area plus the maximum admitted
  catalog content envelope, not from the selected widget. Individual widgets
  retain their authored content width/height and move only the content child
  inside that stable host coordinate space. The container is capacity, not a
  visible bounded shell; unused pixels remain transparent and hit-test inert.
- Recompute that host container only for a typed monitor/work-area/DPI,
  appearance/accessibility-scale, or catalog-envelope change. Hide/reopen with
  unchanged inputs must reconstruct the same HWND and chrome rectangles.
- If the existing architecture cannot provide stable HWND coordinates without
  a monitor-sized container, another HWND, or an undocumented synchronization
  API, stop for the material product/architecture decision instead of swapping
  operation order again.
- Replace attempted `applied*` diagnostics with last-successfully-committed
  transaction state. Publish it only after the DirectComposition device commit
  succeeds; preserve or restore the previous committed state on failure.
- Assert the HWND rectangle itself remains identical during forward/reverse
  cycling of every content extent, then derive tray/guide screen coordinates
  from that stable applied rectangle and last successful child commit. Exercise
  each of all eight widgets as the initial selection in a fresh visible
  lifecycle and compare pre-hide/post-reopen corners. Add direct fractional-DPI
  odd/even cases that produce the rejected physical rounding inputs.

Run only the same affected focused tests and one native Release build after the
correction. Do not rerun unchanged green suites while iterating, and do not
claim physical acceptance until the planner launches the exact integrated
candidate and the user cycles it.

Independent review disposition for `3716063`: rejected as the retained base for
one cumulative correction. The commit correctly separates destination layout
from the retained presented extent, renders the admitted widget at its own
viewport, settles final geometry explicitly, and provides a real first-stage
`OverlayApp` responsibility reduction. The retained evidence and focused suites
are otherwise proportional, and the unrelated DLV-231 close/reconnect timeout
was correctly not rerun.

The rejected gap is in transition/chrome authority. The one transformed visual
contains widget content, controller guide, and tray, so non-reduced-motion
envelope steps scale/move all three. Its host-route oracle compares
untransformed local tray bounds rather than actual transformed screen bounds.
Visual and inverse pointer transforms advance each step, while UIA is published
only at admission and final settlement, leaving intermediate accessibility
bounds stale.

User decision: solve this coherently now by folding DLV-236 into the DLV-238
correction; do not implement a temporary snap-only envelope. Preserve
`3716063`'s destination-viewport transaction, but evolve the sole existing
`OverlayCompositionSurface` owner into one DirectComposition root with retained
child visuals/surfaces for destination content and fixed host chrome. No second
HWND, top-level target/device, renderer, accessibility provider/tree, focus
graph, hit-test authority, controller router, or widget protocol concept is
permitted.

Required cumulative correction:

- Transform/clip only the destination content envelope. Keep tray and guide at
  identity scale and fixed absolute bottom-center screen rectangles throughout
  start, midpoint, cancellation/reversal, completion, reduced motion, and
  device recreation.
- Separate chrome invalidation from content invalidation. Selection/reorder may
  update only the bounded old/new tray tiles or named affordance; snapshot
  admission, provider updates, content focus/scroll/slider changes, and content
  motion must not repaint the retained tray background or unchanged icons.
- Project content and host-chrome bounds through their real per-child visual
  coordinate spaces beneath the one accessibility provider. Every motion step
  must keep visible, pointer/hit-test, focus, and UIA bounds mutually current;
  do not republish stale whole-tree geometry or create a second semantic owner.
- Keep one atomic root commit for selected tile, retained inert old content,
  newly admitted destination content, guide state, clips/transforms, UIA
  authority, and HWND placement. Preserve premultiplied alpha, fixed panel-to-
  guide and guide-to-tray offsets, rapid/stale/failure authority, and normal
  device-loss/hidden cleanup.
- Retain the DLV-238 before/after responsibility map and extend it with the
  composition child ownership moved out of `OverlayApp`. Use clangd plus `rg`
  for every moved symbol. This remains one staged hotspot reduction, not a
  broad renderer or accessibility rewrite.

Acceptance adds actual start/mid/end absolute screen-bound samples for content,
guide, tray, selected tile, pointer mapping, and UIA across compact-to-tall and
tall-to-wide switches. The tray/guide rectangles must be identical while only
the content envelope changes. Per-surface counters must show zero tray redraws
after the bounded selection update through delayed admission and every motion
step, plus zero tray redraws for provider, slider, scroll, and focus updates.
Catalog/order, appearance, DPI/text scale, accessibility policy, and device
recreation must still rebuild/update chrome exactly when required. Reuse the
already-green DLV-238 suites; add/run only the focused child-visual,
actual-coordinate, UIA, invalidation, device-loss, and one final bounded
eight-widget route needed for the cumulative correction. Do not rerun the
inherited DLV-231 close/reconnect tail, Tier 3, providers, packaging, or capture
work.

Accepted disposition: destination transaction commit `3716063` plus cumulative
independent-chrome correction `6c018c4`, integrated together as `8537330` and
`5239886`. Independent review confirmed one DirectComposition target/root with
retained content, guide, and tray children; only content receives motion
transforms. Pointer, hit testing, focus/UIA, and diagnostics use the same child
coordinate plan. Stable content motion retains tray pixels, while selection or
reorder damages only changed tiles. Focused targeting 76/76, chrome 52/52,
accessibility provider 158/158, and transition 73/73 suites passed again at the
reviewed binary. The bounded eight-widget route retained identical actual
guide/tray rectangles through eight variable-extent motions, reported no
composition fallback, and cleaned up all observed processes. The inherited
DLV-231 close/reconnect tail remains outside this geometry-only acceptance.

Physical rejection supersedes that automated/source disposition. The retained
tray was cropped and sized from each destination widget's layout, so it is not
a genuinely session-owned chrome surface. Selection also used exact old/new
tile dirty rectangles on a premultiplied DirectComposition surface. That
unmeasured optimization clears regions that the transformed/cropped repaint
does not reliably repopulate and clips antialiased focus strokes. The route
checked coordinates and paint counts inside each motion but reset its expected
tray rectangle at every motion boundary and never verified resulting pixels.

### Accepted and integrated — DLV-238 + DLV-236: simple retained tray ownership

Owner/baseline: platform native composition/chrome only, integrated as
`629274b` plus documentation correction `6e2969b` atop the earlier destination
transaction and DLV-237 trace chain.

Required behavior:

- Keep the existing one HWND, DirectComposition target/root/device, renderer,
  accessibility provider/tree, focus graph, and input authority.
- Retain separate content, guide, and tray child surfaces. Widget admission,
  provider updates, content focus/scroll/slider changes, and content motion
  repaint only content (and guide when its own text/state changes); they must
  leave the tray surface untouched.
- Make tray layout, crop, size, and bottom-center screen anchor session-owned
  and independent of the selected widget's destination width/height. A widget
  envelope change may alter only the tray's root-relative offset required to
  preserve the same absolute screen rectangle.
- Remove tile-level DirectComposition damage and every exact-old/new-tile
  partial repaint path. When selection, reorder, catalog/order, appearance,
  DPI/text/interface scale, accessibility policy, or device recreation changes
  tray-owned presentation, repaint the complete small tray surface once.
- Do not add another highlight visual, overlay, cache layer, dirty-region
  system, or speculative optimization. Optimize the full-tray repaint only if
  later measurement demonstrates a real product cost and the user approves
  the added complexity.
- Preserve full focus outline/indicator antialiasing inside a deliberately
  padded tray surface or inward-safe authored bounds; no clipped stroke, clear
  hole, stale tile, or transparent rectangle may survive selection changes.

Verification is proportional: focused tray/chrome/composition and coordinate
tests, one native Release compile, and one bounded ordinary selection route.
The route must capture one absolute tray rectangle once for the visible session
and compare that same value before selection, immediately after selected
identity changes, before destination admission, at motion start/midpoint/end,
and after final HWND settlement across every differently sized widget. The
oracle must not redeclare or reset its expected tray/guide rectangle when a new
motion begins. It must also prove widget-only changes do not increment tray
paint count and each tray-owned change performs one complete tray repaint. Add
a focused regression that would fail the integrated implementation's observed
735/736-pixel cross-widget tray-width change. Do not build a pixel-capture
harness or rerun Tier 3; the freshly launched Release and user inspection are
the final visual verdict.

Independent review disposition: accepted. `RequiresTrayRepaint` replaces the
complete bounded tray surface once for tray-owned state changes; widget/content
changes do not repaint it. The production route captures one session rectangle
and rejects any changed corner before selection, after selection, before
admission, through every motion sample, and after settlement, including the
observed 735/736-pixel drift regression. Focused reviewer rerun passes 51 chrome
checks, the native Release rebuild is green, and live visual approval remains
user-owned.

### Accepted and integrated — DLV-237: correlate deferred widget admission

Baseline: corrected cumulative DLV-238/DLV-236 integrated on local main. Owner:
platform native selection/lifecycle/session observability only. Do not alter
presentation behavior or start from an unreviewed branch tip.

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

Independent review disposition: accepted cumulatively as `4bbec64` plus
correlation-integrity correction `31c5d17`. The bounded observer, background
diagnostic drain, post-to-dequeue timing, request/generation/lifecycle stages,
completion dispositions, meaningful-A filtering, and sanitization remain
observability-only. The correction binds each transition to its exact selected
or active widget, gives unrelated pinned/background work correlation zero, and
rejects a mismatched identity before terminal or slow state changes. Focused
reviewer rerun passes all 17 coordinator scenarios, including selected-plus-
pinned and rapid supersession cases. The intermittent symptom remains pending
the user's joint test; no admission behavior was changed speculatively.

### Folded into active DLV-238 correction — DLV-236: retain host chrome independently

The user approved completing DLV-236's retained tray/guide child-visual work as
part of the active DLV-238 correction. Its objective, invalidation policy,
atomicity requirements, acceptance evidence, and stop conditions now appear in
the cumulative Assigned section above. DLV-236 is not a later executable
assignment and must not produce a second implementation commit after that
cumulative milestone is accepted.

DLV-218 remains dependency-blocked on the user's separate DLV-217 integration
decision. Mixed-monitor, audio/Bluetooth, legacy-controller, and assistive-
technology gates still require hardware or user evidence; do not manufacture
additional internal filler after the active queue.

### Paused behind DLV-244 — DLV-239: retain checkpoint and separate refresh state

Lane/owner/baseline: platform; existing native session, bridge adapter,
appearance, and presentation-cache owners on accepted cumulative DLV-238/
DLV-236 plus DLV-237 main. This is the first milestone governed by
`widget-snapshot-cache-design.md`.

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

### Ready after DLV-242 integration — DLV-243: bound optimistic slider feedback damage

Lane/owner/baseline: platform native slider-interaction, retained render result,
renderer-damage, accessibility-value, and content-composition owners after
accepted DLV-242 main. This milestone does not expand the public update
protocol or DLV-242's admitted-update scope.

Visible/performance objective: moving an in-view slider with the controller
must provide immediate optimistic feedback without recomputing Taffy layout or
redrawing the complete widget content surface on every step.

Requirements: reuse the current admitted geometry and DLV-242 damage owner to
update the host-owned optimistic slider value, repaint only the slider's old
and new visual/value/focus bounds, and publish the corresponding accessibility
value change. A widget acknowledgement that confirms the optimistic value must
perform no additional layout or paint; a differing acknowledgement, rejection,
or timeout reconciliation may repaint only the bounded slider region unless a
real geometry, viewport, appearance, or device-loss change requires the
existing correctness fallback. Preserve action sequence/generation authority,
adjustment mode, repeat cadence, focus, scroll anchors, clipping, failure
feedback, and normal cleanup.

Acceptance: Audio Mixer and one differently named generic slider fixture cover
single steps, held repeats, busy action queue, confirming and differing
acknowledgements, rejection, timeout, focus transfer, scrolling, appearance/
viewport change, and device recreation. Exact counters must prove zero full
Taffy layout and zero full content-surface repaint for ordinary optimistic
steps, zero follow-up paint for a confirming acknowledgement, bounded slider
damage for reconciliation, and zero tray/guide repaint throughout. Measure
input-to-visible feedback, render CPU, damage area, and action latency against
the current full-widget path. Run only affected slider/input/renderer/damage/
UIA/composition cases and one native Release compile; no Tier 3 or packaged
route.

Out of scope/stop: do not generalize every host-local interaction, add
per-widget or node-ID behavior, create another retained semantic/render tree,
change the public protocol, weaken slider authority/reconciliation, or suppress
the full correctness fallback for actual structural, geometry, appearance, or
device-loss changes. Stop if DLV-242 does not expose a single trustworthy
damage owner or if measurement shows no material benefit.

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

1. Main includes physically rejected DLV-238/DLV-236 plus accepted DLV-237
   through `6e2969b`; do not build further work on its false stationarity claim.
2. Execute DLV-244 as the sole platform assignment, independently review its
   non-self-referential transaction evidence, integrate only if accepted, and
   launch the exact corrected Release for mandatory user cycling.
3. After the user accepts stationary tray behavior, execute and integrate
   DLV-239, then dispatch DLV-240 to the widgets lane as sole
   shared protocol/managed lead. Platform does not edit shared files.
4. Integrate accepted DLV-240, then dispatch DLV-241 to platform. DLV-241 is the
   one exact Tier-3 protocol activation checkpoint.
5. Integrate accepted DLV-241, then execute DLV-242. Launch the complete
   incremental candidate for user cycling/scrolling/interaction review.
6. After accepted DLV-242, execute DLV-243 as the separate bounded optimistic
   slider-feedback optimization and launch it for controller-slider review.
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

Do not mark the continuing delivery goal complete; continue until the user pauses/replaces it or all useful lanes are genuinely blocked. Never push.
