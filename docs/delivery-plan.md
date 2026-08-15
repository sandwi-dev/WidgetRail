# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical detail through `436d890` is in the
[2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-Taffy plan is in the
[2026-08-14 03:24 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
The complete pre-snapshot-cache plan is in the
[2026-08-14 15:45 snapshot](history/delivery-plan/2026-08-14T15-45-17-07-00.md).
The complete DLV-244 physical-recovery and review record is in the
[2026-08-15 07:30 snapshot](history/delivery-plan/2026-08-15T07-30-00-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current baseline

- Accepted production tip `94c4873` contains DLV-244 fixed chrome and
  destination geometry, corrected DLV-237 admission tracing, DLV-232 worker
  isolation, DLV-235 startup recovery, DLV-230 YT Music composition, and the
  Taffy DLV-221 baseline.
- The user physically accepted DLV-244 tray visibility/stationarity and distinct
  widget envelope admission. Its cumulative commits were integrated as
  `bfaa2a1`, `0c071fb`, `f06a7e9`, `61041a5`, and `94c4873`.
- Exact main Release PID 86516 is visibly running. Its live Network Controls to
  YT Music transition kept guide/tray at the same applied chrome rectangle while
  content adopted the destination envelope independently.
- DLV-244 focused evidence is green. The isolated widget-switch host route stays
  honestly red because the temporary host exited before readiness without logs.
  A later Release refresh also stopped a hung process-owner executable test.
  Neither result is represented as a pass; direct source review, deterministic
  focused evidence, and the user's physical verdict are the acceptance basis.
- DLV-237 correlation identified the next bug: a selected widget can have no
  current snapshot while its lifecycle already reports current, causing refresh
  reconciliation to skip and leaving retained inert pixels indefinitely.
- The approved checkpoint/update architecture is in
  [`widget-snapshot-cache-design.md`](widget-snapshot-cache-design.md). DLV-239
  through DLV-243 implement it serially.

## Avalonia disposition

The user ended the Avalonia experiment as failed on 2026-08-14. AVP-005 and all
cutover work are cancelled. Retained Avalonia branches and
`experiments/AvaloniaOverlayPrototype` are historical evidence only. Do not
dispatch, integrate, relaunch, delete, or resume them without a new explicit
user decision. The native overlay is the sole production presentation path.

## Execution and review rules

- Operate exactly two production tasks: `widgets` and `platform`. The user has
  additionally authorized one temporary `red-test` investigation task; it is
  test-only and does not count as a production lane.
- Each task implements only its single Assigned milestone, then the first Ready
  same-lane milestone whose accepted baseline is present.
- Shared protocol/architecture work is serialized to the named lead lane.
- Implementation tasks never edit reviewer-owned goal, plan, or review files.
- The planner independently reviews actual diffs and retained evidence, commits
  reviewer documents, integrates only accepted commits, and never authors
  implementation code or pushes.
- Use clangd semantic definition/reference queries plus `rg` for native ownership
  review. If clangd cannot index the exact worktree, disclose that and manually
  inspect declarations and callers; do not imply text search proved ownership.
- Preserve unrelated user changes. Rejected commits remain unintegrated and
  corrections stay in their lane.
- User-visible defects and requested features outrank refactors.
- UI corrections use physical-first ordering when specified: code and Release
  build, user verdict, then regression tests. Do not redesign tests before the
  user accepts visible behavior.
- Verification is proportional. Run focused affected Release suites and at most
  one bounded linked-host group when a process boundary changes. After one
  diagnosed unreliable integration case, stop rerunning or redesigning its
  harness, retain source review, and disclose residual risk.
- Do not run the product aggregate except at a named Tier-3 checkpoint. Do not
  build capture tooling for ordinary assignments.
- After every accepted integrated production milestone, rebuild the coherent
  main Release, gracefully replace the planner-owned candidate, launch exact
  `OverlayHost.exe --show`, and perform one bounded live/log smoke.
- Stop for credentials, destructive recovery, substantial merge conflict,
  undocumented input/window APIs, external publication, physical-only evidence,
  or a material product/security decision.
- Never mark the continuing delivery goal complete while useful authorized work
  remains. Never push.

## Durable architecture decisions

- Taffy is the sole declarative Flex/Responsive Grid geometry engine. It owns
  declarative geometry only; the host owns semantic validation, DirectWrite
  measurement, scroll/clipping, pixel/DPI policy, focus-follow, controller
  navigation, accessibility, rendering, animation, and HWND placement.
- Widget width and height are independent `Preferred`, `Content`, or
  `FillAvailable` axes. `Content` uses bounded Taffy intrinsic measurement.
- Games & Apps remains bundled. Spotify, Game Launcher, and YT Music are ordinary
  Community applications. Core code contains no service-specific identity,
  DTO, API, or known-tree behavior.
- Preserve the Widget SDK/protocol, catalog/package/runtime, WidgetBridge
  transport, lifecycle/trust/persistence/providers, Community process boundary,
  native GameInput/Guide owner, and one logical overlay session.
- Content and fixed host chrome use separate tightly bounded coordinated HWNDs.
  The chrome HWND owns fixed bottom-center guide/tray placement; content may grow
  or shrink upward/outward without moving it. They share one session, renderer
  and graphics-device owner, GameInput router, logical focus model, and
  accessibility policy.
- Chrome rendering is local-first. Only the chrome HWND has an absolute screen
  rectangle. Guide/tray and focus geometry are chrome-client coordinates.
  Screen bounds are downstream projections from the actual HWND origin for UIA,
  pointer projection, and diagnostics; they never feed rendering placement.
- Keep the current small host-owned tray layout arithmetic. Do not migrate it to
  Taffy without evidence that added tray complexity warrants another declarative
  layout owner.
- One backdrop HWND may cover the monitor; content and chrome HWNDs remain tightly
  bounded. Apply backdrop, content, and chrome Z-order coherently through the
  sole window owner.
- A complete `WidgetSnapshot` is a last-admitted checkpoint, not an expiry cache
  entry. Ordinary invalidation records refresh demand without deleting it.
- Hard checkpoint removal is limited to restart, widget removal/runtime
  replacement, generation/protocol incompatibility, trust revocation, or unsafe
  corruption.
- Post-checkpoint change uses a permanent generic operation set: typed document
  and node properties, keyed insert/remove/move, subtree replacement, and full
  checkpoint fallback. SDKs normally compute changes; widget authors do not
  manually construct wire patches.
- Semantic checkpoints, resolved appearance/resources, and host-owned focus,
  scroll, press, slider, layout, UIA, composition, and placement have separate
  validity and ownership.
- Identical semantic publications may advance sequence/action authority without
  layout or paint. Changed properties invalidate only declared effects; unknown
  effects fall back to subtree or full checkpoint replacement.
- Sandboxed and full-trust widgets use the same bounded semantic update/admission
  protocol. Full trust never grants overlay HWND/render/input/focus authority.
- No second GameInput reader, bridge transport, semantic schema, lifecycle/cache
  owner, overlay session, graphics-device owner, or logical focus tree.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on `codex/impl-platform-snapshot-cache` | Assigned DLV-239 from accepted production `94c4873` plus reviewer-plan baseline `6769954`. Preserve completed `codex/impl-platform-fixed-chrome`. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle clean. DLV-240 starts only after accepted DLV-239 is integrated and the planner sends the exact main baseline. |
| Red tests | `Implementation agent — red-test lane`; isolated worktree/branch created from current planner main | Assigned DLV-245. Investigate only the red isolated widget-switch route and hung process-owner executable test; no production-code edits. |

DLV-217 remains accepted through `d57fd06` but unintegrated because its exact
aggregate is 40/41 with one reviewer-history-link failure. Preserve the branch;
integration requires separate explicit user approval.

## Temporary red-test lane

### Assigned — DLV-245: independently disposition unreliable native host tests

Owner/baseline: temporary red-test task from current planner main. This lane may
edit only directly implicated test sources, test-support/harness code, and their
build invocation. It must not edit production sources, public protocol/SDK,
widget packages, providers, runtime behavior, or reviewer-owned documents.

Investigate two exact red cases independently:

1. The DLV-244 isolated widget-switch route whose temporary host exited before
   authenticated readiness and produced neither `overlay.log` nor
   `startup-error.log`.
2. The Release refresh's `OverlayProcessOwnerTests.exe` hang after compilation.

Required process:

- Reproduce each case at most once initially, separately, using a unique
  process profile, isolated temporary runtime/state, and bounded timeout.
- Determine whether each failure is a product defect, harness defect, missing
  adjacent runtime/dependency, environment limitation, or obsolete/duplicated
  assertion. Do not infer a cause from an empty log.
- If a production defect is exposed, stop that case and report the exact
  production path to the planner; do not fix it in this lane.
- If the harness is repairable, make the smallest deterministic test-only fix
  and run that exact test once after correction.
- A test may be removed only when its behavior is obsolete, invalid, or fully
  duplicated by named retained coverage. The commit must identify the redundant
  assertions and replacement coverage. Never remove a test merely because it
  is difficult or red.
- Preserve unique process profiles, authenticated ownership boundaries, bounded
  shutdown, last-good logs, and cleanup of only lane-owned temporary processes
  and files. Never attach to, activate, close, or reuse the production profile.
- Do not run the product aggregate or unrelated suites. Do not modify tests to
  bless failed product behavior or weaken a required stationarity, destination,
  lifecycle, security, or cleanup assertion.

Deliver one committed test-only candidate with exact disposition per case,
diff, commands, timeout, pass/red result, and residual risk. Stop for production
code changes, undocumented APIs, a material coverage decision, destructive
cleanup, or overlap with DLV-239 files.

## Platform lane

### Accepted — DLV-244: fixed chrome and destination geometry

Accepted behavior:

- The guide/tray chrome HWND is bottom-centered from monitor work area, DPI,
  interface/accessibility scale, appearance, and catalog/order only.
- Widget identity, content extent, admission, provider updates, and motion do not
  seed or reposition chrome.
- Guide and tray render in independent `(0,0)` child-local surfaces. Physical
  DirectComposition update offsets are normalized to the drawing coordinate
  system before painting.
- Content renders and transitions in its own panel-local envelope above the
  guide. The transaction compares new identity/extent with a durable committed
  destination, so a destination admission moves once and same-destination
  refresh is repaint-only.
- Backdrop, content, and chrome Z-order are applied as one operation. Pointer and
  UIA projection use each HWND's actual applied coordinates.
- Physical acceptance supersedes rejected historical variants. Full review and
  failed-attempt detail is retained in the 2026-08-15 07:30 snapshot.

### Accepted — DLV-237: correlated deferred widget admission trace

The bounded in-memory trace correlates tray selection, posted/dequeued refresh,
lifecycle decision, worker request, completion, admission, and meaningful A
lifecycle changes by transition/request/generation. It does not log controller
repeats or replace a file per input. The user and planner use it for joint live
diagnosis; it is evidence, not a speculative behavior change.

### Assigned — DLV-239: retain checkpoint and separate refresh state

Owner/baseline: platform native session, bridge adapter, appearance, and current
presentation-cache owners from exact main `94c4873`. This is the first milestone
governed by `widget-snapshot-cache-design.md`.

Motivating evidence: transition 23 returned from Games & Apps to Game Launcher
with `currentSnapshot=false`. The refresh dequeued after 78 ms, but lifecycle
reconciliation skipped `reason=already-current`, queued no request, crossed the
250-ms threshold, and left Games & Apps pixels inert. Only closing/reopening
created a new visible session and admitted Game Launcher.

Required behavior:

- Introduce explicit `Current`, `RefreshRequested`, and `RefreshInFlight` state
  under the existing sole lifecycle/session owner.
- Ordinary hidden/provider/appearance-derived invalidation retains the last
  admitted semantic checkpoint and records refresh demand.
- Selecting a widget immediately presents that widget's own retained checkpoint,
  never another widget's pixels or envelope.
- Refresh demand queues through the existing request owner even when worker
  lifecycle is already current; `already-current` is not snapshot freshness.
- Retained content is inert until current sequence/action authority is admitted.
- Refresh failure, cancellation, and stale completion preserve last-good inert
  presentation. Hard removal uses only the durable transitions above.
- Appearance and derived-resource invalidation remain separate from semantic
  checkpoint eviction.
- Preserve bounded cache/resource counts and normal shutdown.

Out of scope: public update protocol, SDK diffing, incremental Taffy/damage,
new caches, eager waking of unloaded workers, residency changes, or DLV-240+.

Acceptance: focused all-eight state coverage for hidden invalidation,
resident/suspended/unloaded selection, success/failure/cancellation, rapid
switching, appearance change, hard-removal transitions, own-envelope retention,
and exact action authority; measure retained selection latency/background
wakeups; run Tier 1 affected suites and at most one bounded host route. Stop
after one diagnosed unreliable route and disclose it. No aggregate.

Stop for a public schema change, second cache/lifecycle owner, stale interactive
authority, unbounded retention, or material residency/security choice.

## Serialized snapshot update program

### Awaiting DLV-239 integration — DLV-240: managed update contract and SDK diff

Owner: widgets lead for serialized WidgetProtocol, WidgetSdk, WidgetRuntime, and
WidgetBridge work. Platform must not edit shared protocol/bridge files then.

Version the atomic checkpoint/update contract and automatically produce typed
property changes, keyed insert/remove/move, subtree replacement, and complete
checkpoint fallback. Require base/new sequence plus instance/generation,
complete-batch validation, stable-ID SDK diffing, property-impact metadata,
identical-model no-op, deterministic fallback, capability negotiation, and
bounds for operations/bytes/depth/nodes/queues. Authors do not hand-build
patches; sandboxed and full-trust fixtures use the same contract.

Verification: validator/JSON/version compatibility, operation/fallback/no-op/
base-mismatch/malformed/oversized/coalescing cases, compiled public examples,
and focused managed suites plus one bridge/runtime group. Do not activate update
traffic or run Tier 3.

### Awaiting DLV-240 integration — DLV-241: native materialized update admission

Owner: platform. Negotiate and consume the bounded operation stream under the
existing bridge/session/admission owners. Materialize a candidate off the
presented checkpoint, validate the complete result, then atomically publish
sequence, semantics, resources, action authority, focus/scroll reconciliation,
UIA events, paint/layout effects, and fallback. Reject stale/mismatched/partial
updates without mutating current state. Checkpoint fallback remains mandatory.

This is the one exact Tier-3 protocol activation checkpoint after focused native
and bridge coverage. Stop for another cache/materializer/session owner, partial
visible publication, protocol ambiguity, or unbounded work.

### Awaiting DLV-241 integration — DLV-242: incremental layout, damage, and UIA

Use admitted impact metadata to avoid whole-widget work. Value/paint-only changes
retain geometry and repaint only affected bounds; local layout changes recompute
the smallest safe Taffy boundary; structure/surface/device changes fall back to
larger or full work. Preserve focus, scroll, press, slider continuity, hit-test,
clip, and UIA correctness. No new UI element requires a new transport operation;
only effect classification, with conservative fallback for unknown effects.

Measure full-checkpoint versus incremental CPU, allocation, layout, paint area,
latency, and fallback frequency for Audio slider, Now Playing progress, Network
status/list changes, launcher/library lists, and media metadata. Build and launch
the complete candidate for user cycling/scrolling/controller review.

### Ready after DLV-242 — DLV-243: bounded optimistic slider feedback damage

Give accepted slider input immediate host-owned visual feedback while the
authoritative widget update is pending. Reconcile on acknowledgement, correction,
failure, timeout, focus change, widget switch, restart, or generation change.
Damage only the slider value/track/thumb and necessary accessibility value event;
do not relayout or repaint the whole widget. Preserve sequence authority and full
fallback. Implement separately so DLV-242 scope remains bounded.

## Widgets lane

No widgets milestone is executable before DLV-240. Accepted DLV-225/226/228/
229/230 remain integrated. New styling/provider work requires fresh user
evidence rather than speculation.

### Awaiting DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths only
after DLV-217 integration. Retain generic App Library behavior. Add an
architecture check rejecting Community identities/domain types in core. Never
delete credentials, provider data, accounts, or user files.

## Serialized order

1. Platform implements and planner integrates accepted DLV-239 from `94c4873`.
2. Widgets implements DLV-240 from that exact integrated main; platform does not
   edit shared protocol/bridge files concurrently.
3. Platform implements DLV-241 after DLV-240 integration and runs the one named
   Tier-3 activation checkpoint.
4. Platform implements DLV-242 and launches for physical incremental-behavior
   review.
5. Platform implements DLV-243 separately and launches for slider review.
6. DLV-217/218 remains a separate explicit integration decision.

## Manual and packaged evidence

- User verdict on each freshly launched accepted Release is final for tray/panel
  cohesion, controller feel, motion, sizing, continuity, and incremental updates.
- Physical controller/display evidence is required for changed navigation,
  focus reveal, scrolling, slider/press continuity, or visual presentation.
- Live Spotify/Premium/Web Playback/EME/OAuth, IGDB, and SteamGridDB enrichment
  is credential-gated; offline behavior must not depend on it.
- Computer control may omit the no-taskbar overlay. Use exact HWND/UIA/log
  fallback instead of changing taskbar behavior.

## Blocked work

| Item | Blocker / required evidence |
| --- | --- |
| Avalonia | Failed and cancelled; requires a new explicit user decision. |
| DLV-217 integration | Exact aggregate 40/41; requires explicit user approval. |
| DLV-218 | Requires accepted DLV-217 on main. |
| YT Music catalog cleanup | Eight-version ceiling; approval to remove only inactive non-selected 0.2.0. |
| Trusted fixed-video/PiP | Paused WebView2 measured about 348.7 MiB private and 4% CPU; requires a changed budget or authorized experiment. |
| Audio default-device selection | No documented supported Windows setter; requires primary Microsoft API and reversible hardware/provider plan. |
| Native uninstall reconciliation | Synthetic removal emitted no managed revision/native event; requires deterministic disabled/nonresident removal evidence. |
| YouTube authenticated library | Requires approved minimum-scope OAuth/account; Watch Later is unsupported by Data API. |

## Recent accepted milestones

| Milestone | Result |
| --- | --- |
| DLV-244 | Integrated through `94c4873`: stable applied chrome HWND, local guide/tray surfaces, coordinated Z-order, panel-local content, and durable destination authority. |
| DLV-237 | Correlated selection-to-admission trace integrated; no speculative behavior change. |
| DLV-232 | `cd378a2` integrated as `ef56bfc`: failed worker retains only its own last-good inert presentation until fresh admission. |
| DLV-235 | `b0ea2b4` integrated as `5440e7b`: startup failure survives retarget/revocation; Retry owns a fresh generation. |
| DLV-234 | `9435050` integrated as `348df2e`: incomplete frames taint and replace the sole transport. |
| DLV-233 | `1323c8a` integrated as `4b8e0b7`: PATH-isolated Audio fixture with strict UIA/scroll/focus behavior. |
| DLV-231 | `2a379ac` integrated as `fbd2f02`: delayed requests remain revocable while tray/input/close remain responsive. |
| DLV-230 | `7323468` integrated as `fe2e52c`: responsive YT Music composition. |
| DLV-229 | `1a8c201` integrated as `1ddedb4`: Network first-page scan state. |
| DLV-228 | `2784401` integrated as `220a415`: Audio rows/sliders consume width generically. |
| DLV-226 | `2276b4c` integrated as `9755406`: truthful eight-widget surface-policy audit. |

Do not mark the continuing delivery goal complete. Continue until the user
pauses/replaces it or all useful lanes are genuinely blocked. Never push.
