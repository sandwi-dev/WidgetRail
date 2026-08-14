# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical review and assignment detail through planner commit `436d890` is in
the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-compaction plan is in the
[2026-08-14 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baseline

- Local main contains accepted DLV-230 integration `fe2e52c` over accepted
  widgets integrations DLV-225 `3eb0eaf`, DLV-226 `9755406`, DLV-228
  `220a415`, and DLV-229 `1ddedb4`, planner commit `b3707a2`, accepted DLV-227
  integration `856bbbb`, accepted DLV-206 integration `5cbd4cf`, corrected
  DLV-224 integration `02750ba`, and provisional product commit `c2b6456`.
  DLV-206 corrects performance/temporal evidence provenance without expanding
  its measurement scope. The DLV-224 correction preserves
  omitted native snapshot versions as legacy v1 while rejecting present
  malformed, fractional, or unsupported versions before conversion. Accepted
  DLV-222 `e8af5be` and DLV-223 through `6b916e8` remain integrated beneath it;
  accepted DLV-221 `8836e07` remains the Taffy engine baseline. The user
  physically reviewed the rebuilt DLV-221 Release, found the integration
  substantially correct, and accepted Taffy as the native declarative geometry
  engine.
- DLV-221 preserves the existing Widget SDK/protocol, package/catalog/runtime,
  authenticated WidgetBridge transport, lifecycle/trust/persistence/providers,
  widget domains, Community process boundaries, native renderer, GameInput,
  controller focus/navigation, UI Automation, scrolling, clipping, motion,
  and single-HWND ownership. Only generic Flex/Responsive Grid geometry moved
  behind the pinned Taffy Rust static library and narrow panic-safe C ABI.
- DLV-221 focused evidence is green: Rust 5/5, native declarative layout 250,
  renderer 4,839, all eight production widgets, controller/focus/slider/UIA
  suites, bounded semantic-churn and hidden/idle measurements, and normal
  zero-process shutdown. The exact canonical aggregate stopped at its first
  verifier self-test because the manifest omits the existing
  `SpotifyCommunityApplication.Tests` project. No product test ran or failed in
  that aggregate. Retain this honestly red infrastructure result; do not rerun
  it unchanged or weaken the verifier.
- The user reported five post-integration presentation issues. They are open
  product defects even though the Taffy replacement itself is accepted. DLV-225
  now removes the Settings root dead height through Content sizing, DLV-228
  restores Audio Mixer row/slider width consumption, and DLV-229 locks the
  Network preferred first-page admission that exposes the complete scan state.
  DLV-222 owns the fixed panel/guide/tray spacing already integrated; DLV-230
  source/package validation closes the remaining detached/weak YT Music
  composition. Its visible 0.2.9 launch awaits the bounded catalog cleanup
  approval below, and the user's physical verdict remains the final visual
  authority.
- The user now reports a visible tray flash while cycling widgets. Current
  production draws the widget panel, guide, and icon tray into one shared
  Direct2D/DirectComposition frame. A selection mutation legitimately changes
  the old/new selected tray tiles, but later worker-snapshot admission, widget
  content repaint, and content-envelope motion also redraw the otherwise
  unchanged tray. DLV-236 owns a retained tray composition visual so widget-
  only work cannot clear or repaint persistent tray pixels.
- The user also reproduced a distinct selection/admission defect: while tray
  navigation changes the selected and active widget identity correctly, some
  cold or delayed widgets remain on the previous inert presentation until A
  changes the lifecycle target from Visible to Interactive. Existing logs show
  the retained pixels, the later A transition, and eventual admission but no
  explicit worker, bridge, protocol, lifecycle, or queue failure. Automatic
  admission also succeeds in other sessions, so the current evidence does not
  identify a safe correction. DLV-237 adds a bounded correlated lifecycle trace
  and reproduces the failure; root-cause correction is deliberately deferred
  until the planner reviews that evidence.
- The coherent DLV-222/DLV-223 Release had Community YT Music 0.2.8 enabled and
  ran cleanly as planner-owned PID 27128. It exited normally through `WM_CLOSE`
  when the planner began the DLV-224 rebuild. Corrected main now compiles and
  packages the complete changed native/managed graph and passes the previously
  failing `RealHostAccessibilityTests` route with 286 checks. The later
  `WidgetActionFailureHostTests` clean-environment fixture remains honestly red:
  its temporary installation copies `OverlayHost.exe` and `runtime` but omits
  the executable's required `OverlayPlatformInterop.dll`, so the child cannot
  create its HWND unless an unrelated PATH happens to supply the DLL. The
  platform worktree's exact packaged build passed, and this unrelated retained
  harness defect does not invalidate DLV-224 product behavior. Repair it only
  through the bounded platform assignment below; do not reinterpret an
  unchanged rerun as product evidence.
- The user authorized the proposed merge recovery. The planner verified and
  aborted only the failed merge, preserved the original platform branch at
  `bdf6d88`, and created clean main-based branch
  `codex/impl-platform-integration`. The platform task reconstructed the four
  accepted changes in order as DLV-231 `fbd2f02`, DLV-233 `4b8e0b7`, DLV-234
  `348df2e`, and DLV-235 `5440e7b`; stable patch identities match the previously
  accepted product/test commits, reviewer-owned documents were unchanged, the
  implementation-status append was reconciled once, and main fast-forwarded
  through `5440e7b`. DLV-234 treats every incomplete bridge frame read or
  write as transport-tainting, tears down the one owned bridge process, and
  establishes one replacement before the next request. Its deterministic
  partial-header/body cancellation cases, 12 coordinator scenarios, native
  bridge/catalog route, managed bridge 89/89, and bounded eight-widget host
  route are green with normal zero-process cleanup. DLV-235 closes the last
  cumulative functional red: the typed worker-start failure survives automatic
  lifecycle retarget/revocation, explicit Retry owns one fresh generation, and
  only a valid admitted snapshot clears it. The PATH-isolated production-host
  route, 13 coordinator scenarios, and direct typed bridge-category coverage
  are green with normal zero-process cleanup.

## Avalonia disposition — failed and closed

The user ended the Avalonia experiment on 2026-08-14 after repeated physical
layout, shell, controller-routing, process, and reliability failures. The
candidate did not satisfy the primary reason for the evaluation and is a failed
product experiment. AVP-005 and every Avalonia production cutover are
cancelled.

The accepted AVP-004 extraction and prototype commits remain in Git and
`experiments/AvaloniaOverlayPrototype` only as historical/reference evidence.
They are not an active lane, baseline, migration path, verification debt, or
launch target. Do not dispatch the Avalonia lead or temporary extraction tasks,
do not relaunch the candidate, and do not delete retained source/history unless
the user separately authorizes repository cleanup. The native overlay is the
only production presentation path.

## Execution rules

- Operate exactly two production lanes: `widgets` and `platform`.
- Each task implements only its lane's current Assigned milestone, then the
  first explicitly Ready same-lane milestone whose baseline is present.
- Implementation tasks never edit reviewer-owned documents. The planner
  independently reviews actual diffs and retained evidence.
- Rejected commits remain unintegrated. Corrections stay in their lane and do
  not interrupt unrelated coherent work.
- Never push. Stop for credentials, destructive recovery, substantial merge
  conflicts, undocumented input/window APIs, publication, physical-only
  evidence, or a material product choice.
- User-visible defects and requested features outrank internal refactors.
- Screenshots are high-value user evidence but are not authority to build or
  repair a capture harness. Verify the named geometry and semantic invariants,
  rebuild and visibly launch the accepted Release, and use the user's physical
  verdict for final presentation quality.
- Run focused affected Release suites during implementation. Use one bounded
  linked-host group when a language/process boundary changes. Run the canonical
  aggregate only at a named checkpoint; never rerun an unchanged red result.
- New managed test projects use MSTest.Sdk 4.3.2. Existing executable suites
  remain valid unless their migration is explicitly assigned.
- Full-trust Community applications may use ordinary user-level APIs in their
  own process. Bound shared product inputs/resources, not private application
  CPU, memory, databases, files, sockets, dependencies, or child processes.

## Product and architecture decisions

- Games & Apps remains bundled. Spotify, Game Launcher, and YT Music are
  ordinary Community applications. Core assemblies contain no service-specific
  DTOs, API clients, process hosts, package identities, or known-tree rules.
- The native presentation boundary is authoritative. Retain Widget
  SDK/protocol, catalog/package/runtime, WidgetBridge/authenticated transport,
  lifecycle/trust/persistence/providers, domain implementations, Community
  process boundaries, native rendering/accessibility, and the original
  controller focus/navigation owner.
- Taffy is the sole production declarative Flex/Responsive Grid geometry engine.
  Do not restore the deleted custom solver, add a dual-runtime path, or introduce
  per-widget native geometry, identity branches, tree-shape special cases,
  another renderer, another focus graph, or another input owner.
- Taffy owns geometry calculation only. The host retains semantic validation,
  intrinsic DirectWrite measurement, scroll offsets/extents, ancestor clipping,
  visible rectangles, physical-pixel/DPI snapping, focus-follow, controller
  navigation, accessibility projection, rendering, animation, and HWND
  placement.
- Widget surface sizing is an authored semantic contract, not a global shell
  preset. The public contract will expose symmetric independent width and height
  modes: `Preferred`, `Content`, and `FillAvailable`.
  - `Preferred` uses the validated preferred axis extent and remains stable as
    live data changes.
  - `Content` uses the Taffy-measured intrinsic extent clamped between the
    authored minimum and preferred extent; the preferred extent is the ceiling.
  - `FillAvailable` consumes the safe host-admitted work-area extent.
  - Width and height have equal API capability. A view may deliberately select
    different policies because responsive text/grid height is computed from an
    admitted width.
- Content sizing uses a bounded two-pass host process: admit width/work-area
  constraints, measure the root with automatic content height, clamp the
  measured extent, add host chrome reservations, bottom-anchor the resulting
  window, then perform final layout at the admitted viewport. No widget ID,
  page ID, style class, or known tree shape participates in this algorithm.
- The persistent tray and controller guide are host chrome at fixed absolute
  bottom-center screen coordinates for the complete visible session. Widget
  width/height changes move the content envelope upward/outward around that
  anchor. The panel bottom, guide, and tray use explicit fixed spacing; a short
  widget may not remain top-anchored and create variable dead space.
- Persistent host chrome must also have independent retained paint ownership.
  The icon tray uses its own child visual/surface under the sole existing
  DirectComposition target. Tray selection, catalog/order/overflow, appearance,
  DPI/text scale, and device recreation may invalidate that surface; worker
  snapshot admission, widget rendering, content reveal, scrolling, and content-
  envelope motion may not. This is retained-layer separation inside the one
  HWND and one accessibility/focus/input authority, not another overlay window
  or application tree.
- One HWND wraps the admitted content-plus-chrome union. The overlay must not
  become a monitor-sized desktop surface. Monitor work area, DPI, accessibility,
  safe insets, and bounded safety limits remain host authority.
- Container child alignment and the container's own `align-self` are separate
  semantics. `align: center` on a Row centers its children; it must not make the
  Row content-width inside a stretching parent. Explicit width/aspect-ratio
  semantics may opt a node out of cross-axis stretch generically.
- The Microsoft GameInput/Guide owner remains authoritative. No second C#
  GameInput reader, bridge transport, overlay HWND, or focus tree is permitted.

## Active task map

| Lane | Task | Branch/worktree | Current state |
| --- | --- | --- | --- |
| Widgets | Implementation agent — widgets lane | `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` on `codex/impl-widgets-taffy-ui`, accepted DLV-225/226/228/229/230 are preserved through `7323468`; accepted DLV-217 remains preserved on `codex/impl-widgets-community-launcher` | Idle at a clean boundary; no later sound widgets milestone until the user's physical verdict |
| Platform | Implementation agent — platform lane | `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on `codex/impl-platform-integration`, accepted cumulative DLV-231/233/234/235 is integrated through `5440e7b`; original pre-recovery branch remains preserved at `bdf6d88` and prior DLV-220 history remains preserved on `codex/impl-platform-community` | DLV-232 is Assigned; DLV-238 admitted destination geometry is next Ready; DLV-237 tracing and DLV-236 retained tray composition follow in that order |

DLV-217 remains accepted through `d57fd06` but unintegrated because its exact
aggregate is honestly 40/41 with one reviewer-history-link failure. Preserve
that branch. Integration still requires the user's separate explicit approval
and must not be mixed with this UI correction cluster.

## Platform lane

### Done — DLV-222: restore native surface anchoring and correct Taffy stretch

Accepted as `e8af5be` and integrated with DLV-223 through merge `073e423`.
Generic Taffy translation now separates child alignment from inherited parent
stretch; authored widget envelopes drive the one HWND; variable content is
bottom-anchored around fixed guide/tray chrome; and the bounded 560-DIP tray
band retains the current eight items at controller-safe sizes. Focused evidence
is green for Rust/Taffy, layout, renderer, placement, tray, controller, slider,
focus, accessibility/UIA, Audio Mixer 45/45, Network Controls 24/24, and the
real eight-widget linked-host group with clean shutdown and bounded composition
timing. Packaged physical review remains pending on the coherent Release.

### Done — DLV-224: symmetric surface-axis sizing

Baseline: accepted DLV-222 integrated into local main. Owner: serialized
cross-component assignment led by the platform lane. No widgets-lane task may
edit the same protocol/native files concurrently.

Objective: add the generic `Preferred`, `Content`, and `FillAvailable` width and
height policies and the bounded Taffy intrinsic-measure/admission path described
in Product and architecture decisions.

Required implementation:

- Version the public `WidgetSurfaceHints` schema compatibly and add one shared
  typed axis-mode enum used independently by width and height. Existing views
  default to `Preferred` with unchanged behavior.
- Validate illegal/missing values at the managed boundary and parse them once
  into the native surface request. No stringly page/identity inference.
- Implement content measurement with a definite admitted width, automatic
  block extent, authored minimum/preferred clamps, host chrome reservation,
  work-area clamping, and one final layout. Bound node counts, extents, passes,
  errors, and retained results; preserve last-good presentation on invalid
  submissions.
- Prevent live-data resize churn: only a view explicitly declaring `Content`
  uses measured sizing. `Preferred` stays stable; `FillAvailable` follows only
  admitted work-area/accessibility changes.
- Preserve focus, scroll offsets, transition cancellation/restoration,
  accessibility bounds, tray stationarity, and normal close across extent
  changes.
- Document the contract with copyable examples and explain that responsive
  width is normally admitted before intrinsic height.

Acceptance:

- Managed validator/round-trip and C++ parser tests cover every mode, defaults,
  malformed input, bounds, and protocol-version behavior.
- Generic two-pass tests cover content smaller than preferred, content between
  bounds, overflow capped at preferred/work area, responsive grid reflow,
  wrapped text, and FillAvailable on both axes.
- Transition tests prove only the content envelope moves while tray/guide screen
  coordinates remain fixed.
- Tier 1 affected managed protocol/SDK, Rust/native layout, renderer, placement,
  focus/UIA, documentation, and Release build.
- Tier 2 one bounded managed-snapshot-to-native-host group. This public cross-
  process schema change is the next named Tier-3 checkpoint; run the canonical
  aggregate exactly once from the clean coherent commit and retain any unrelated
  verifier failure honestly.

Stop if intrinsic sizing requires widget-specific native knowledge, more than
two layout passes, an unbounded retained tree, or a material change to tray,
focus, scrolling, or accessibility authority.

Accepted as implementation correction `912aea9`, integrated into main as
`02750ba`. Missing native `protocolVersion` retains the v1 default; a present
value must be numeric, finite, integral, and within `[1,17]`. Direct native
coverage includes omitted v1, present v17 independent axes, malformed,
fractional, below-range, above-range, and retained last-good behavior.
`RealHostAccessibilityTests` passes 286 checks and the platform worktree's clean
packaged Release build passed. The one exact `01ff871` Tier-3 result remains
unchanged and honestly red at the known pre-product verifier-manifest check.
Independent main packaging reproduced the complete artifact graph and the 286-
check parser route, then stopped at the unrelated DLV-227 fixture defect.

### Done — DLV-206: truthful performance provenance

Correct the rejected DLV-200 evidence without expanding measurement scope:
retain root PID/start, exact commit/SHA, scenario/profile, child roles, and
available/unavailable metrics; give the eight-widget run separate provenance;
anchor composition lookup after paint; remove false ordinary-host-live wording.
Run only affected bounded performance/temporal routes.

Accepted as implementation commit `da74ded` and integrated into main as
`5cbd4cf`. The private Hidden/Visible scenarios now use distinct ephemeral
profiles and retain exact root PID/start, commit, executable SHA-256, dirty
state, observed roles, and explicit available/unavailable metrics. The clean
artifact at
`artifacts/performance/overlay-performance-20260814-174027276-892473c1`
records commit `da74ded`, executable SHA-256 `3697ce...b4d8`, `dirty=false`,
and 21 summarized samples per scenario. The separate eight-widget route
retains its own root/process/profile/commit/SHA/child-role evidence and accepts
composition only after the complete matching paint record. Focused evidence is
green for 23 harness assertions, the native Release build, and eight production
widgets with seven switches. Unavailable GPU/presentation, private-working-set,
scheduler, DWM/game, and long-run metrics remain stated as unavailable rather
than converted into claims.

### Done — DLV-227: self-contained production-host fixture

Baseline: accepted corrected DLV-224 integrated into local main. Owner: platform
lane. This is bounded test-infrastructure reliability work and must follow the
assigned performance milestone; it may not preempt DLV-225 visible widget work.

Objective: make `WidgetActionFailureHostTests` launch the same self-contained
native installation it claims to exercise.

Required implementation and acceptance:

- Copy the exact required native runtime dependencies beside the temporary
  `OverlayHost.exe`, including `OverlayPlatformInterop.dll`, from the admitted
  installation. Do not modify PATH, load dependencies from the source worktree,
  weaken HWND visibility, or broaden the fixture into installation tooling.
- Fail before launch with a precise missing-dependency diagnostic and retain
  normal job-owned teardown and temporary-directory cleanup.
- Add one direct dependency-copy/omission regression, run
  `WidgetActionFailureHostTests` from an environment whose PATH does not contain
  the Release directory, then run only the affected packaged build segment.
- Do not rerun the unchanged Tier-3 aggregate.

Accepted as implementation commit `86a2a23` and integrated into main as
`856bbbb`. The fixture validates and copies the admitted
`OverlayPlatformInterop.dll` beside its temporary `OverlayHost.exe`, rejects an
omitted dependency before launch with the exact diagnostic, removes the Release
installation from the focused test PATH, and independently rejects PATH
contamination. Its unique process profile prevents delegation to an ordinary
resident host while preserving the strict visible-HWND, UIA, failure-routing,
job teardown, and temporary cleanup checks. The focused packaged route passed
with the ordinary planner-owned host present; no source fallback, weakened
window check, product runtime change, or Tier-3 rerun occurred.

### Done — DLV-231: slow-worker dashboard responsiveness

Baseline: accepted DLV-227 integrated into local main `856bbbb`. Owner:
platform lane. This is the next visible/release-risk milestone after two bounded
evidence-infrastructure corrections and closes the active EQ-020 responsiveness
risk without changing widget-domain behavior.

Visible objective: a slow or nonresponsive widget snapshot must never freeze
the tray, controller focus, close/back behavior, or a later valid widget
selection, and a late result must never replace the current presentation.

Required implementation:

- Use the existing production host/session/bridge/input owners and the existing
  delayed-worker fixture seam. Do not add another UI thread, event loop, input
  router, focus graph, transport, or presentation cache.
- Exercise one delayed first snapshot and one snapshot request that remains
  nonresponsive until the host cancels or abandons it. While each request is
  pending, retain last-good content and prove tray navigation, widget reselection,
  and B close remain responsive through ordinary product routing.
- Bind every pending request and result to the exact session/view generation.
  Selection change, hide, worker exit, and normal close must cancel or revoke
  the pending authority; a cancellation-ignoring late result must be rejected
  without focus, extent, scroll, or presentation drift.
- Change production coordination only if the retained evidence reproduces UI
  starvation or stale admission. Keep the fix behind a focused session/bridge
  owner and provide an OverlayApp before/after responsibility map if `main.cpp`
  changes materially.

Acceptance:

- A production-host route records exact input-to-tray-focus/paint timing while
  the worker is delayed/nonresponsive and keeps controller input within the
  existing 50-ms p95 host-focus target; worker completion latency is reported
  separately and may not be substituted for input latency.
- Deterministic cases cover delayed success, never-completing request,
  selection-away, hide/close, worker exit, cancellation-ignoring late success
  and late failure, retained last-good content, no stale publication, and normal
  zero-process teardown.
- Tier 1 affected session/bridge/controller/focus/native Release suites and one
  bounded production-host group. No package aggregate, capture work, provider
  change, or credential/hardware route.

Stop if the correction needs another transport, another focus/input owner,
widget-specific native behavior, an unbounded wait/cache, or a material public
protocol decision.

Implementation source commit `2a379ac`, reconstructed and integrated as
`fbd2f02`, reproduces the serialized slow-worker
starvation, adds request-generation revocation, and reports green focused
coordinator/bridge/production-host evidence. Its original frame-alignment and
failure-precedence blockers are closed cumulatively by accepted DLV-234
`348df2e` and DLV-235 `5440e7b`. The complete chain is accepted and integrated.

### Done — DLV-233: self-contained Audio scroll host fixture

Baseline: committed DLV-231 source `2a379ac` on the platform lane; this
assignment is independent of the rejected framing recovery and may finish at
its current clean boundary. Owner: platform test infrastructure only. This
correction was exposed by the independent main Release build after DLV-228; it
does not reopen or reject the accepted Audio product/style correction.

Objective: make `AudioMixerScrollHostTests` launch the same self-contained
temporary native installation it claims to exercise. Its current
`TemporaryInstallation` copies `OverlayHost.exe` and `runtime` but omits the
required adjacent `OverlayPlatformInterop.dll`, so clean startup never publishes
the authenticated development-readiness marker.

Required implementation and acceptance:

- Reuse the exact admitted-native-dependency copy/validation policy already
  accepted for `WidgetActionFailureHostTests`; do not duplicate a drifting
  dependency list if one narrow shared fixture helper is now justified.
- Copy and validate `OverlayPlatformInterop.dll` beside the temporary
  `OverlayHost.exe`, fail before launch with the exact missing-dependency
  diagnostic, and remove the Release installation from the child PATH.
- Preserve the existing Audio fixture worker, authenticated ready nonce,
  strict visible-HWND/UIA/scroll/focus assertions, evidence output, job-owned
  teardown, and temporary-directory cleanup. Do not weaken or skip the live
  reverse-edge test and do not change Audio product code.
- Run the direct dependency omission/copy regression and the affected
  `AudioMixerScrollHostTests` route from the PATH-isolated temporary install.
  Then run only the smallest affected packaged build segment; no Tier-3
  aggregate or unchanged full build rerun.

Stop for a product/runtime change, a source-worktree/PATH fallback, weakened
window/input/scroll assertions, or a broader installation-framework decision.

Accepted as implementation commit `1323c8a`, reconstructed and integrated as
`4b8e0b7` in the accepted cumulative chain. One shared test-support policy now
validates and copies `OverlayPlatformInterop.dll`, proves byte-size-identical
copy and precise pre-launch omission failure, and rejects a PATH containing the
admitted Release installation. `AudioMixerScrollHostTestsOnly` passes its
complete authenticated visible-HWND/UIA/live reverse-edge scroll and focus
route from the self-contained temporary install. No Audio product/runtime code,
source-worktree fallback, assertion weakening, aggregate, or capture work was
added. A precautionary `WidgetActionFailureHostTestsOnly` run passed the shared
dependency setup before reaching the separately queued DLV-235 functional red.

### Done — DLV-234: recover frame alignment after request cancellation

Baseline: committed DLV-233 above rejected DLV-231 `2a379ac`. Owner: platform
session/bridge transport only. Dependencies: finish and commit the already
started DLV-233; do not begin DLV-232 first. This correction must remain on the
single existing WidgetBridge connection/process owner and the existing session
coordinator worker.

Objective: preserve DLV-231's responsive request revocation without ever
reusing a pipe whose frame boundary became indeterminate after cancellation.

Required implementation:

- Treat cancellation of a synchronous frame header or body read as a tainted
  connection even when `ERROR_OPERATION_ABORTED` follows partial progress.
  Before the next request, close and re-establish the one existing bridge
  transport through its current lifecycle owner, or use another bounded design
  that proves the original stream is at an exact frame boundary. Merely
  ignoring older correlation IDs is insufficient.
- Bound and observe the old bridge process/pipe teardown and replacement. Do
  not leave an orphan bridge, overlap two authoritative transports, reset
  unrelated package/provider state, or add another request loop, cache, input
  owner, focus graph, or public protocol message.
- Preserve DLV-231 generation/lifecycle-target revocation, last-good
  presentation, async event handling, later valid selection, normal close, and
  exact stale-success/failure rejection. Remove stale-ID skipping that is no
  longer necessary or prove why any retained use is finite and frame-safe.

Acceptance:

- Add one deterministic transport regression that cancels after a valid frame
  header and a nonzero body prefix have been consumed, then proves the next
  ordinary request succeeds on a correctly framed sole transport. The test must
  fail against `2a379ac`; blocking before response publication is not enough.
- Cover cancellation before any bytes, during header/body, cancellation-
  ignoring late completion, malformed/future correlation failure, retained
  last-good content, later valid snapshot admission, and bounded old/new bridge
  process cleanup with no remaining PID after normal host close.
- Rerun the 12 coordinator scenarios, directly affected native bridge
  correlation/catalog tests, 89-case managed bridge lifecycle/concurrency
  fixture, and the bounded eight-widget production-host route once from the
  coherent correction. Retain host-focus timing separately from worker/reconnect
  latency. No Tier-3 aggregate, capture work, provider change, or hardware route.

Concurrency and stop conditions: no widgets-lane files overlap. Stop for a
second concurrent transport/process authority, a public protocol change, loss
of authenticated session/catalog semantics, unbounded reconnect/retry, or a
material lifecycle redesign.

Accepted as implementation commit `9435050`, reconstructed and integrated as
`348df2e` in the accepted cumulative chain. `ReadExact` now reports completed
bytes and every incomplete header/body read, invalid frame length, or failed
write taints the sole transport. The next request closes that pipe, performs
bounded teardown of the one owned bridge process, and launches one replacement
through the existing lifecycle owner; obsolete stale-request-ID skipping is
removed. Deterministic `CancelSynchronousIo` coverage passes before any bytes,
after a partial header, and after a valid header plus body prefix, followed by a
clean replacement frame. The 12 coordinator scenarios, native bridge/catalog
route, managed bridge lifecycle/concurrency 89/89, and bounded eight-widget
host route are green. Host-focus p95 is 33 ms; selection and close-side bridge
replacement complete in 1,033 ms and 1,306 ms respectively, with normal
zero-process cleanup and no second transport, process, input, focus, cache, or
protocol owner.

### Done — DLV-235: retain the primary worker-start failure

Baseline: accepted cumulative DLV-234 correction above accepted DLV-233 source.
Owner: platform session/failure-state routing only. Dependencies: accepted
DLV-234 source `9435050` is present; do not begin DLV-232 first.

Visible objective: when opening a widget fails to start its worker, retain and
present that primary actionable failure until an explicit Retry or later valid
admission supersedes it. Automatic background lifecycle work, missing-cache
diagnostics, request revocation, or bridge recovery must not erase or replace
the primary failure.

Required implementation and acceptance:

- Reproduce the exact PATH-isolated `WidgetActionFailureHostTests` red retained
  after DLV-233: `The primary worker-start failure was not retained by the
  host.` Correlate the ordinary session events/log ordering before changing
  production; do not attribute it to the shared dependency helper after that
  boundary has already passed.
- Keep one existing lifecycle/session/bridge/failure owner. Bind failure
  precedence and replacement to the current widget/session/generation without
  another cache, status channel, retry loop, transport, or widget identity
  branch. A secondary hidden/background missing-cache diagnostic may not
  replace a current primary startup failure.
- Preserve DLV-231/DLV-234 cancellation, frame-safe sole-transport recovery,
  stale-result rejection, last-good presentation, tray/controller/B
  responsiveness, bounded process cleanup, and explicit Retry semantics.
- Add one deterministic directly affected regression proving the primary
  worker-start failure survives the automatic lifecycle/revocation sequence,
  an explicit Retry starts one fresh generation, and a later valid snapshot is
  the only success path that clears it.
- Run `WidgetActionFailureHostTestsOnly` to completion from the PATH-isolated
  temporary installation plus only directly affected coordinator/session/
  bridge cases. Retain exact failure/status/UIA/live-region behavior and normal
  zero-process teardown. No Tier-3 aggregate, capture work, widget-domain
  change, or unrelated full build.

Stop for a public protocol/status-model decision, another failure cache or
authority, service-specific behavior, weakened failure/UIA assertions, or a
material lifecycle redesign.

Accepted as implementation commit `b0ea2b4` and reconstructed/integrated as
`5440e7b`. The existing typed
`connectionFailed` event is carried across a private bridge adapter seam and
mapped to session stage `Start`; sanitized diagnostic text remains
presentation-only and the public protocol is unchanged. A same-runtime Start
failure may survive a Visible-to-Interactive target retarget without admitting
the stale completion. The existing failure owner revokes queued/in-flight work,
suppresses automatic re-establishment while the failure is current, preserves
the actionable UIA/live-region status through one explicit Retry, and clears it
only after the fresh generation admits a valid snapshot. The apparently generic
failure guard is consistent with the existing coordinator/UI policy that any
current failed admission owns the error surface until explicit Retry; retirement
still queues Background normally, so no unrelated lifecycle authority was
frozen. Focused evidence is green for 13 coordinator scenarios, direct
`connectionFailed` versus `processExited` bridge-category coverage, and the
complete PATH-isolated `WidgetActionFailureHostTestsOnly` route with one worker,
restored play/pause focus/action/status behavior, bounded hide/reopen feedback,
and normal zero-process cleanup. No Tier-3 aggregate, capture work, public
protocol change, widget-domain change, or unrelated full build occurred.

### Assigned — DLV-232: generic worker crash isolation and recovery

Baseline: accepted DLV-235 cumulative platform correction. Visible objective: one
credential-free Community worker may crash repeatedly without closing the
overlay, disturbing other widgets, or leaving an unauthorized stale
presentation; a normal reactivation or generic Hold-Y restart recovers through
the existing lifecycle owner. Preserve process/job cleanup, retry bounds,
last-good semantics, tray focus, and safe diagnostics. Use differently named
generic fixtures, run only affected lifecycle/process/controller routes, and do
not add service-specific behavior or rerun Tier 3.

### Ready after DLV-232 integration — DLV-238: commit admitted destination geometry

Baseline/owner/dependencies: accepted DLV-232 integrated into main; platform native presentation-extent and DirectComposition placement only; do not interrupt DLV-232 or start from an unreviewed tip.
Visible objective: after a cold/asynchronous switch, retain the old widget's pixels and envelope only until the destination snapshot is admitted; the admitted widget must immediately own its authored width, height, responsive viewport, and final presented extent without waiting for A, a provider update, or another selection.
Required correction: retire the retained extent as admission changes authority, resolve placement from the destination `DesiredPresentationExtentDip` rather than the pinned old `PresentedPresentationExtentDip`, render the new snapshot once at its destination viewport, and atomically commit that complete frame plus placement before animating old-to-new envelopes. Completion must settle through one explicit destination placement/layout; it may not clear the override with `redraw=false` while leaving old geometry current, scale a destination tree laid out at the source viewport, or add widget identities, another HWND/surface owner, or a second layout path.
Preserve: one HWND/root compositor/focus/input/UIA authority, Taffy as sole declarative geometry engine, fixed absolute tray/guide bounds, retained inert old semantics before admission, stale/cancelled snapshot rejection, reduced-motion/device-loss fallback, clipping/scroll/focus reveal, and atomic content/geometry authority.
Acceptance: cold and cached switches across all eight widgets, including compact-to-tall, tall-to-wide, rapid selection, delayed admission, late revoked completion, failure/last-good, and provider updates, prove that every `content=admitted rendered=<destination>` frame uses the destination surface request and that final presented equals desired without a later snapshot. The Audio Mixer to Network Controls regression must move from retained `592x698` to Network's admitted `632x878` envelope, lay Network out at its own viewport, and retain stationary tray/guide coordinates with no flash, dark band, seam, stale UIA, or intermediate input mismatch.
Verification: focused widget-switch, extent-transition, composition-placement, surface-policy/Taffy, focus/UIA, reduced-motion/device-loss cases, one bounded eight-widget host route, and native Release build only; no Tier-3 aggregate, provider/package change, capture-harness work, or unrelated refactor.
Stop for per-widget sizing logic, destination content rendered against source geometry, non-atomic HWND/content authority, another compositor/window/focus/input owner, or an undocumented platform dependency or material animation decision.

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

### Awaiting DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths
only after both autonomous Community packages are integrated. Retain generic
App Library behavior for bundled Games & Apps and consenting sandboxed users.
Add an architecture check rejecting Community identities/domain types in core.
Do not delete credentials, provider data, accounts, or user files.

## Widgets lane

### Done — DLV-223: responsive YT Music controller composition

Accepted and integrated through main `6b916e8` (`d423da0` product/package and
`6b916e8` exact renderer-evidence correction). YT Music 0.2.8 now keeps one
responsive semantic tree with artwork beside a unified details/control column
at 760x440 and above that same full-width column at 480x340. Existing actions,
focus links, shortcuts, lifecycle, authentication, and Community isolation are
unchanged. Focused evidence is green for YT Music 60/60, the isolated package
lifecycle, the generic renderer suite, and the dedicated real-snapshot/native-
renderer scenario 79/79. Physical composition review remains queued for the
coherent DLV-222 plus DLV-223 Release.

### Done — DLV-225: content-sized Settings root

Baseline: DLV-224 integrated into local main. Owner: widgets lane.

Visible objective: remove unused Settings root height through measured content,
not a guessed replacement height.

Required implementation:

- Set Settings root `WidthMode = Preferred` and `HeightMode = Content` with the
  existing preferred height retained as the ceiling and minimum height retained
  as the floor.
- Keep deeper Settings pages `Preferred` unless direct evidence shows a page is
  static and benefits from Content sizing. Do not make scroll-heavy pages resize
  as rows or diagnostics change.
- Give the root category list a distinct class/structure that does not request
  `flex-grow: 1; flex-basis: 0`. Preserve the responsive two-column intent,
  one-column reflow, category order, Reset styling, focus navigation, and active
  input scope.
- Do not hard-code a new root pixel height or add a Settings identity rule to
  the host.

Acceptance:

- Taffy-measured root height equals its visible content plus authored spacing,
  remains between minimum and preferred bounds, and leaves no material dead
  area below Reset.
- Compact width reflows to one column and expands height within the same content
  policy; constrained height scrolls/reveals every category.
- Root/deeper-page transitions keep tray/guide screen bounds fixed and preserve
  focus/back behavior.
- Tier 1 Settings tests, managed surface contract, focused native renderer/
  placement scenario, and Release package. No aggregate.

Accepted as implementation commit `3e887ee` and integrated into main as
`3eb0eaf`. Only the Settings root uses Preferred width plus Content height; its
existing 520x360 minimum and 880x520 preferred envelope remain the floor and
ceiling. The root-only category list no longer requests fill growth, while all
deeper dynamic pages remain Preferred. Settings 61/61, Widget SDK 93/93, and a
real managed snapshot/production GBSS/native Taffy scenario with 44 checks are
green. The independent main build later passed the 112,333-check placement
target; physical dead-space and fixed-chrome review remains the user verdict.

### Done — DLV-226: eight-widget surface-policy audit

Audit every current widget's width/height mode and authored min/preferred
extent after the new contract is physically accepted. Change only policies
supported by direct first-page evidence. Dynamic provider/list widgets remain
Preferred unless resizing is demonstrably beneficial and stable. Retain a
concise contract table in public widget-authoring documentation and run focused
surface/conformance checks only.

Accepted as implementation commit `2276b4c` and integrated into main as
`9755406`. The public table records all eight first-page axis policies and
corrects the protocol guide to v17. Settings root remains the sole justified
Content-height view; every provider/list-driven page retains stable Preferred
axes. SDK 93/93, Settings 61/61, Spotify 50/50, and YT Music 60/60 are green.
The older conformance group's retired Spotify-worker assumptions remain an
honest unrelated 1/6 harness result and were not weakened.

### Done — DLV-228: Audio Mixer width consumption

Use authored generic stretch/flex semantics so session rows, value tracks, and
sliders consume the admitted content width again. Preserve labels, values,
controller adjustment, focus, scrolling, and compact reflow. Do not add native
Audio identity rules or a guessed widget width. Verify the first-page master and
session controls at compact and preferred widths with the focused Audio and
native renderer suites.

Accepted as implementation commit `2784401` and integrated into main as
`220a415`. Production GBSS changes viewport-relative root sizing to parent-
relative `100%`, stretches the cards/rows/list generically, and gives each
slider the shrinkable flexible remainder with no obsolete fixed minimum. Audio
Mixer 45/45, generic renderer 4,851 checks, and the real managed snapshot/
production GBSS/Taffy route 41 checks at 320 and 520 DIPs are green. The
independent main build also passed ordinary Audio product compilation and the
eight-widget host route before exposing the separate DLV-233 fixture setup
defect.

### Done — DLV-229: Network first-page vertical admission

Choose and document the Network root's symmetric surface policy and authored
minimum/preferred height so the primary scan status/action is visible on the
first page at its preferred envelope while compact/constrained heights remain
scroll-reachable. Do not hide provider state, force Content sizing on dynamic
lists, or add a native Network special case. Run focused Network, surface,
focus-reveal, and renderer checks.

Accepted as implementation commit `1a8c201` and integrated into main as
`1ddedb4`. Network explicitly declares Preferred/Preferred while retaining its
measured 560x700 preferred and 320x420 minimum envelope. Network 24/24 and a
real NotScanned managed snapshot/production GBSS/Taffy route with 12 checks
prove the complete Ready-to-scan title/help and Scan action are visible at zero
body-scroll offset in the preferred viewport and remain focus-revealable at the
minimum viewport. No dynamic state was hidden and no native identity rule or
guessed taller size was introduced.

### Done — DLV-230: YT Music panel cohesion

Refine the single responsive Community YT Music semantic composition so its
content envelope feels visually attached to fixed host chrome, uses available
space intentionally, and preserves the accepted side-by-side/stacked reflow,
all eight actions, explicit focus adjacency, lifecycle, and Community
isolation. Do not change the fixed host offsets per widget or introduce another
page tree. Run focused YT Music, real-snapshot/native-renderer, surface, and
package lifecycle evidence; physical composition remains the final verdict.

Accepted as implementation commit `7323468` and integrated into main as
`fe2e52c`. YT Music 0.2.9 keeps one responsive tree and uses one full-width
raised panel for artwork, metadata, progress, and both controller rows. All
eight actions, explicit focus adjacency, compact/preferred reflow, lifecycle,
authentication, and Community isolation remain unchanged. YT Music 60/60, the
real managed snapshot/production GBSS/native Taffy route 82 checks, and the
isolated immutable-package lifecycle are green. The 0.2.9 archive SHA-256 is
`ca334cc...9aac`. Visible launch is separately blocked by the live catalog's
eight-version ceiling, not by product/package validation.

No later widgets milestone is currently sound enough to pre-authorize. DLV-230
closes the final named visual defect in the user-reported cluster; later widget
work must follow the user's physical verdict on the freshly launched accepted
Release rather than manufacture speculative style changes.

## Serialized integration order

1. DLV-221 is accepted and integrated as product commit `8836e07`.
2. DLV-222 `e8af5be` and DLV-223 through `6b916e8` are accepted and integrated
   coherently through merge `073e423`.
3. The coherent Release was rebuilt, packaged, and visibly launched as PID
   27128 with YT Music 0.2.8 current. Exact-session startup diagnostics are
   clean; the named physical tray, Audio, Network, Settings, and YT Music verdict
   remains pending because the computer-control service omits the tool window.
4. DLV-224 is accepted through correction `912aea9` and integrated as main
   `02750ba`. Its clean platform build passed; independent main packaging passed
   the corrected 286-check parser route and retained the unrelated DLV-227
   fixture failure honestly.
5. DLV-225/226/228/229 are accepted and integrated through main `1ddedb4`:
   Settings root adopts bounded Content height, the eight-widget policy audit
   retains stable provider/list envelopes, Audio consumes generic admitted
   width, and Network exposes its complete preferred scan state. DLV-230 is
   accepted and integrated as `fe2e52c`, completing the source-side visual
   correction cluster.
6. DLV-206 and DLV-227 are accepted and integrated through main `856bbbb`.
   The native DLV-206 Release was rebuilt from main and visibly launched as PID
   45452 with SHA-256 `bc7046...f6ea8`; DLV-227 changes only fixture/build/docs
   surfaces, so that running product binary remains the coherent accepted
   runtime. DLV-231 now owns slow-worker visible responsiveness. DLV-217
   integration remains a separate explicit user decision, and DLV-218 may begin
   only after DLV-217 is integrated.
7. The full main Release build passed product compilation, packaging inputs,
   native layout/placement/UIA, and the eight-widget host route, then retained
   an unrelated red `AudioMixerScrollHostTests` startup caused by its omitted
   `OverlayPlatformInterop.dll`. A full `-SkipTests` package refresh succeeded;
   the coherent accepted Release is visibly running as PID 44528 with SHA-256
   `C0F0F3...D7478`. That process exited normally for the DLV-230 package
   refresh. Accepted DLV-233 corrects only the temporary-install fixture above
   DLV-231. Accepted DLV-234 closes the cumulative transport blocker and
   accepted DLV-235 closes the remaining failure-state blocker. After explicit
   user recovery approval, the planner aborted only the failed merge, preserved
   the original branch, and the platform task reconstructed the accepted chain
   from clean main. Stable patch identities match the previously accepted
   product/test commits. Main is integrated through `5440e7b`; the native
   Release rebuilt successfully and is visibly running as PID 30064 with
   SHA-256 `C06C7D...AA96`. Exact-session logs show admitted transitions,
   bounded stale-completion rejection, and no crash, forced replacement, or
   bridge transport failure. Computer control again omitted the no-taskbar
   window, so no eight-page live visual pass is claimed. DLV-232 is now assigned.
8. The accepted YT Music 0.2.9 package builds, validates, and packs from main,
   but the live catalog already contains eight immutable YT Music versions and
   rejects installation with `installed_widget_version_limit`. The failed
   helper temporarily disabled the package; the planner immediately re-enabled
   active 0.2.8. No coherent DLV-230 overlay is running pending user approval
   for the exact inactive 0.2.0 generation removal.

## Manual and packaged verification queue

- Jointly test the intermittent tray-selection admission defect after accepted
  DLV-237 is launched. The user performs ordinary tray cycling and reports the
  exact interval; the planner correlates the resulting trace. Neither lane nor
  the planner performs a separate reproduction campaign. Only retained evidence
  from the joint test authorizes root-cause assignment selection.
- User verdict on each freshly launched accepted native Release remains
  authoritative for panel/tray cohesion, controller feel, motion, Audio slider
  sizing, Network first-page visibility, Settings dead space, and YT Music
  composition.
- The accepted DLV-225/226/228/229 Release ran as PID 44528 and its exact-session
  startup, DirectComposition, platform appearance, launcher experience,
  work-area placement, foreground, and visible GameInput lease diagnostics were
  clean. It exited normally for DLV-230 packaging. The computer-control service
  omitted the no-taskbar OverlayHost, so no live first-page visual pass is
  claimed.
- The integrated DLV-231/233/234/235 Release is visibly running as PID 30064
  from exact main `5440e7b`, SHA-256
  `C06C7DE2D67AEDFC09D90FC366E30C0893411834966CC4937466A62B1FDEAA96`.
  Exact-session logs are free of crash, forced-replacement, and bridge transport
  failures. Computer control omitted the tool window, so all eight first pages
  remain physically uninspected by the planner and the user verdict is pending.
- Physical controller/display evidence remains required for changed navigation,
  focus reveal, or visual presentation. It is not Avalonia acceptance debt.
- Live Spotify account/Premium/Web Playback/EME/OAuth are credential-gated.
- Live IGDB and SteamGridDB enrichment are credential-gated; offline launcher
  behavior must not depend on them.
- Computer control may omit the no-taskbar overlay; use exact HWND/UIA/log
  fallback rather than changing taskbar behavior.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| Avalonia migration/cutover | Failed and cancelled by user decision. | None. A new experiment requires a new explicit user decision; do not resume old AVP work. |
| DLV-217 local integration | Exact aggregate is 40/41 with one known reviewer-history-link red. | Explicit user approval to integrate despite that honest documentation-only red step. |
| DLV-230 visible 0.2.9 launch | Live YT Music catalog is at the eight-version ceiling (`0.2.0` through active `0.2.8` with 0.2.6 absent), so accepted 0.2.9 cannot install. | User approval to run the bounded destructive public operation `gbar repair remove org.gbar.samples.ytmusic 0.2.0`, then install/enable accepted 0.2.9 and visibly launch it. The operation removes only the inactive non-selected 0.2.0 package generation and preserves private data/provider secrets. |
| Trusted fixed-video/PiP | Paused WebView2 measured about 348.7 MiB private and 4% CPU against prior gate. | User changes budget or authorizes content/process experiment. |
| Audio default-device selection | No documented supported Windows setter established. | Primary Microsoft API plus reversible provider/hardware plan. |
| Direct computer-control discovery | No-taskbar overlay is omitted from tool discovery. | Tool gains tool-window discovery or user accepts taskbar/Alt-Tab presence. |
| Native uninstall reconciliation | Synthetic catalog removal emitted no managed revision/native event. | Deterministic disabled/nonresident removal event. |
| YouTube authenticated library | Google OAuth/account; Watch Later is unsupported by Data API. | Approved minimum-scope OAuth plan and authorized account. |

## Recent accepted milestones

| Milestone | Accepted result |
| --- | --- |
| DLV-235 | `b0ea2b4`, reconstructed and integrated as `5440e7b`: typed worker-start failure precedence survives lifecycle retarget/revocation, explicit Retry owns one fresh generation, only valid snapshot admission clears it, and focused bridge/coordinator/PATH-isolated production-host evidence is green with normal zero-process cleanup. |
| DLV-234 | `9435050`, reconstructed and integrated as `348df2e`: incomplete frame reads/writes taint the sole transport, bounded owned-process replacement restores framing, direct empty/header/body-prefix cancellation recovery is green, and focused coordinator/bridge/eight-widget evidence retains responsive input plus zero-process cleanup. |
| DLV-233 | `1323c8a`, reconstructed and integrated as `4b8e0b7`: shared precise native-dependency validation/copy, PATH-isolated self-contained Audio host route, and unchanged strict visible-HWND/UIA/reverse-scroll/focus behavior. |
| DLV-230 | `7323468`, integrated as `fe2e52c`: YT Music 0.2.9 full-width raised media panel, one responsive tree, 60/60 widget and 82 native renderer checks; visible install awaits bounded catalog cleanup approval. |
| DLV-229 | `1a8c201`, integrated as `1ddedb4`: explicit Preferred/Preferred Network envelope with real preferred first-page scan-state visibility and constrained focus reveal. |
| DLV-228 | `2784401`, integrated as `220a415`: generic parent-relative Audio width, stretched rows/cards, and sliders owning the flexible remainder at compact and preferred widths. |
| DLV-226 | `2276b4c`, integrated as `9755406`: truthful eight-widget axis-policy table; only static Settings root adopts Content height. |
| DLV-225 | `3e887ee`, integrated as `3eb0eaf`: bounded content-sized Settings root, root-only non-growing category list, preserved one/two-column focus/reveal, and no identity-specific host rule. |
| DLV-227 | `86a2a23`, integrated as `856bbbb`: self-contained native dependency copy/validation, precise pre-launch omission failure, PATH-isolated focused execution, and unchanged strict HWND/UIA/job-cleanup behavior. |
| DLV-231 | `2a379ac`, reconstructed and integrated as `fbd2f02`: delayed/nonresponsive requests remain generation-bound and revocable while tray focus, selection, close, and later valid admission stay responsive; cumulative DLV-234/235 corrections close framing and failure-precedence blockers. |

Do not mark the continuing delivery goal complete because these milestones
closed. Continue until the user pauses/replaces it or all useful lanes reach a
genuine stop condition. Never push.
