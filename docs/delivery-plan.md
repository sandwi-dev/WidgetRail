# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through the rejected `54167ee` DLV-284 diagnostic
candidate is preserved in the
[2026-08-23 13:36 snapshot](history/delivery-plan/2026-08-23T13-36-12-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole implementation authority.

## Current accepted state

- Local `main` integrates corrected DLV-284 worker-failure metadata stack
  `54167ee` + `f66e082` + `a65228c` as merge `12728a2`, after the accepted
  correlation pair `f583f40` + `ac79ed0` merged as `8ac55d`. The final
  cumulative state persists only validated widget ID, request type, and worker
  error code; arbitrary/forgeable worker message text is absent.
- The latest physically accepted integrated Release is preserved at
  `C:\Users\dwive\Projects\GameBarAlternative\src\OverlayHost\out\Release`.
  Executable SHA-256 is
  `29FDA868ACEAF0BB7DE09F64AA6D4C6F1A0A7E44B37D6239CE71D4077AE80153`.
  Its PID 133304 and Bridge PID 26620 were responsive, and
  `startup-error.txt` was absent. Prior
  accepted PID 47948 had no top-level HWND and survived its graceful exact-PID
  signal, so the reviewer force-stopped only that verified process before the
  refresh. Exact `--show` activation PID 69700 forwarded Show and exited;
  Settings reached visible lifecycle under the resident owner. Windows-control
  discovery omitted the overlay HWND, so no automated first-page claim is made.
  The user physically accepted this coherent Release; DLV-285 is released.
- DLV-285 production `ccb46e7` is physically accepted and integrated into local
  `main` as merge `609d34e`. Its exact reviewed Release ran from the clean
  widgets worktree as OverlayHost PID 144052, Bridge PID 142324, with executable
  SHA-256 `55E31C824E96656B14DC8485FFBA9608B63033C08A553B32CDB0ED462FE42A2C`.
  Reviewed Game Launcher 0.2.1 package SHA-256 is
  `54989DDCB8A75FA813284585E06F55F56C704FA4310410126E72B30D0DF7FB4C`;
  it was installed and explicitly full-trust enabled for physical review. The
  catalog admits nine tray widgets and startup is clean. Accepted PID 133304
  survived its graceful exact-PID signal, so the reviewer reverified its path
  and force-stopped only that planner-owned process before candidate launch.
  The user explicitly deferred Game Launcher widget tests for future package
  work. No test-only follow-up, rebuild, or relaunch is required for DLV-285;
  its accepted production commit is integrated. For DLV-286 review, PID 144052
  exposed no closable main window; the reviewer reverified its exact executable
  path and force-stopped only that planner-owned process.
- DLV-286 cumulative production `d0ca29b` + `058efbc` + `a41bd72` is physically
  accepted and integrated into local `main` as merge `627ba4c`. The first two
  visible candidates were rejected and their exact PIDs 80988 and 103768 are
  stopped; the complete correction regenerates the finite seven-runtime host
  graph and deterministically removes retired roots. Production Release build
  and the one focused artifact-coherence/startup scenario passed. The user
  accepted responsive PID 85884 with executable SHA-256
  `3E12B4A4B78890801D642311EE63CEA3B5065785FCE59EEA1B2E4E1651BFFDDC`.
  That accepted candidate already contains exact integrated production tip
  `a41bd72`, so no merge-only rebuild or relaunch is required. Game Launcher
  widget tests remain explicitly deferred.
- DLV-287 production/test `dccf49a` is independently accepted and integrated
  into local `main` as merge `fc91157`. One internal WidgetProtocol calculator
  now owns the complete version-1-through-19 snapshot requirement matrix and
  exact validation provenance; SDK snapshot construction and raw validation
  consume the same result. The affected Release build passed with the expected
  existing native conversion warnings after one sandbox-only NuGet audit access
  failure was corrected by the approved unrestricted invocation. Prior accepted
  PID 85884 exited gracefully through exact owned `WM_CLOSE`; coherent local-main
  PID 113716 is responsive, visibly admitted the generic full-application and
  Spotify widgets, has executable SHA-256
  `5C8FC0B43BA54279E7DBB666FBD98338C592E25629CB9ABE722D1E02957DB43E`,
  and has no `startup-error.txt`.
- DLV-288 documentation commit `8be0ebb` is independently accepted and
  integrated into local `main` as merge `a37d614`. One active pre-release SDK
  evolution contract now owns release-unit classification, migration records,
  the external-distribution trigger, deprecation timing, emergency authority,
  and the separation between public API and wire-protocol evolution. Scoped
  Markdown-link and active-reference checks passed. The assigned documentation
  gate stopped only on three pre-existing OverlayHost packaging assertions and
  reported no DLV-288 link failure; it was not rerun or repaired under this
  documentation-only milestone. No production/runtime artifact input changed,
  so accepted PID 113716 remains the coherent visible candidate without a
  rebuild or relaunch. That accepted integrated executable remained the rollback
  through PID 54348, which exited cooperatively through verified `WM_CLOSE` for
  corrected DLV-289 review. Replacement candidate `e7b24f4`, stacked on
  `7cc2be0`, was rejected because its active menu pixels were clipped above the
  fixed chrome surface. The user physically accepted correction `a74e677`,
  which preserves the topmost chrome owner and adds bounded click-through
  composition headroom. Exact PID 121188 contains the accepted production tip.
  Test-only follow-up `941b0f9` is green; the full chain is integrated as
  `4c8048d`. The running accepted production candidate remains current.
- DLV-292 production/test `ff5e7e4` is accepted and integrated as `80cdb10`.
  Bridge snapshot retention is bound to the exact worker start ordinal; worker
  replacement now emits typed stale-base recovery instead of continuing the
  old virtual-window generation; DLV-292 is closed. DLV-290 selectable layouts,
  its API/materializer corrections, and focused tests are accepted/integrated as
  `9857beb`; the new Settings disable failure is assigned separately as DLV-293.
- DLV-293 production `a9d36cf` is physically accepted and integrated as
  `10c3e26`. Catalog-order replacement
  now recreates the existing fixed-chrome/composition session before its
  synchronous repaint instead of painting through the retired session and
  falling permanently into the legacy HWND path. Exact candidate PID 137288 is
  responsive with executable SHA-256
  `B26DD3AC26DB44BB065F188879882A940F78C027F9699687E9BC6635D4F5D734`.
  Its new catalog-removal, focus, and Guide assertions completed before the
  focused host gate stopped on an older Game Launcher stationarity correlation.
  Per the user's explicit Game Launcher test deferral, that unrelated red is not
  rerun or repaired; the pin-coordinator test did not run and remains test debt.
- DLV-294 cumulative production/test `94fb256` + correction `02cec4e` is
  independently accepted and integrated as `60536ff`. The generic protocol-v21
  contract admits bounded package-authored pinned projection roots while the
  host retains its single HWND, renderer, focus, input, placement, and layout-
  selection authorities. Correction `02cec4e` closes the initial-checkpoint and
  incremental-materialization style gap through one shared projection-aware
  style applicator. Focused SDK, Bridge, native catalog/materialization,
  selection-generation, and pinned-coordinator evidence passed; the required
  Release builds passed. Coherent main Release PID 31132 is responsive with
  executable SHA-256
  `43166C13F59BD33A35413107006DD69954B0614DAD1523186D56B9CC40458936`
  and no `startup-error.txt`. Prior PID 137288 had no top-level HWND, so after
  its path was reverified the reviewer stopped only that exact orphaned accepted
  process before launch. DLV-291 is now the named visible proof.
- User testing of accepted PID 31132 exposed a platform blocker before DLV-291
  can receive a meaningful physical verdict: controller focus enters the pinned
  surface, but ordinary actions fail and X closes the surface. The exact 09:26
  session records `Pinned action transport failed for media-sessions` twice.
  Current source confirms two independent contract defects: native input sends
  the JSON context `pinnedSurface`, which is absent from the public SDK enum and
  therefore rejected by Bridge deserialization, and the pinned controller
  policy reserves X as `Close` while forwarding only A. Clean production-only
  DLV-473 commit `4c66b128` corrects the generic SDK/Bridge/native boundary and
  passed its Release build. Its exact patch is stacked with clean DLV-291
  production `5435eaf` as unaccepted candidate `68248d1a` without integrating
  `main`; the coherent Release/package build passed with no tests run. Accepted
  PID 31132 exited cooperatively through verified `WM_CLOSE`. Exact candidate
  PID 116316 is responsive with executable SHA-256
  `F9D4643E9A290F38ABC495AB8E9EBAB849C034F6404E03CD5194C2811DFC81F4`;
  reviewed Spotify 0.3.15 package SHA-256 is
  `1C4F722B59037CE7F6D348AC12A372F4D941D58779CB3F9B88869B7C6377F65A`.
  Spotify 0.3.15 is installed, selected, and enabled while its existing
  configuration is preserved. The user confirmed pinned-surface input now works
  as expected, accepting DLV-473 production, which is integrated as `055ec2f`.
  The running candidate already contains that exact production commit, so no
  integration-only rebuild or relaunch is required. DLV-291 remains unaccepted:
  Spotify's package renders the two layouts but its manifest omits the existing
  `pinningSupported` admission flag, so the tray correctly withholds Pin. Clean
  production-only correction `64b14864` adds that generic flag and advances the
  immutable package to 0.3.16. Its package build passed with SHA-256
  `35CFC14F0989C5E3B59580D949BF2A8F8D24C81CC395D0FC2428A48ABA459EBC`;
  no tests ran. Spotify 0.3.16 is installed, selected, and enabled, and exact
  candidate owner PID 116316 was visibly resurfaced through authenticated
  `--show`. Physical review rejects 0.3.16: its fresh generation-8 worker PID
  109900 rendered sequences 1-31 successfully, then the 10:18:59.807 next
  render failed as `worker_protocol_validation_failed` and the host retained
  failure UI. This rules out a stale live-refresh worker but does not yet prove
  which authored projection transition is invalid. A production-only 0.3.17
  correction is Assigned with validator weakening forbidden. The selected
  installed version remains 0.3.16 because restoring the full-trust 0.3.14
  rollback requires explicit user approval; no indirect mutation is permitted.
- DLV-473 post-acceptance test commit `34f15ae9` is retained clean and
  unintegrated. Its first authorized focused SDK gate exited 1 during build
  before test output, emitted no compiler diagnostic, and produced no test
  artifacts. Per stop-first-red ordering it was not rerun or repaired, and the
  Bridge/native pinned-input gates did not run. This opaque pre-test build red
  changes no production/runtime input, so exact candidate PID 116316 remains
  running without rebuild or relaunch.
- DLV-318 is the exact recoverable prior accepted Release at
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`;
  executable SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
- Rejected Spotify 0.3.16 remains installed, selected, and enabled pending an
  explicit rollback decision or reviewed 0.3.17 correction; 0.3.15 and 0.3.14
  remain installed rollbacks. Preserve every
  package, credential, account, provider, and configuration state.
- Managed tests are accepted through `676cd76`: DLV-319 `199a81b`, DLV-324
  `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`. All named managed
  Tier-3 gates are green.
- DLV-283 platform production `cdbb04a` and the complete reviewed cumulative
  production/test chain are integrated through `cf77507`.

## Standing tasks

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-473 production `4c66b128` is physically accepted/integrated as `055ec2f`. Test-only `34f15ae9` is retained unintegrated after its first focused gate stopped on an opaque pre-test build red; no rerun. The standing tree's two DLV-293 test diffs remain byte-identical. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-291 0.3.16 `64b14864` is rejected after a fresh worker's sequence-32 render failed protocol validation. Concrete diagnosis and immutable 0.3.17 production correction are Assigned. Game Launcher tests remain deferred/out of scope. |

## Execution rules

- Local `main` and reviewer documents are reviewer-owned. Implementation tasks
  never edit them or push; the planner never authors implementation/test code.
- Review and integrate only explicit accepted DLV commits. Never integrate
  rejected `0494b69`, restoration `ad109f8`, ancestry-bound DLV-427 `e26b92b`,
  or its isolated cherry-pick `16050bb`.
- Follow physical-first order: production/build, user verdict, then tests.
  Relaunch only for a reviewed production/runtime candidate or an accepted
  integrated runtime-input change.
- Run affected Tier 1 once, the smallest linked Tier 2 only for a changed
  boundary, and Tier 3 only when assigned. Stop first red and classify before
  any edit or rerun. Retain exact provenance and numeric exits.
- Avalonia/AVP is closed failed-experiment history. Do not resume, message,
  launch, integrate, delete, or otherwise touch it.
- Preserve strict bounded admission, last-valid presentation, current
  interaction authority, installed/configured state, and unrelated user work.
  Never push.


## Preserved accepted baseline

Detailed DLV-465–472 stabilization, focused evidence, and held-branch
history are preserved in the current timestamped delivery-plan snapshot. Local
`main` contains the complete reviewed production/test chain through `cf77507`,
including accepted DLV-472 `29601e2`; its named managed Tier-3 gates are
green. Do not integrate rejected/restoration/ancestry-bound platform history or
the closed Avalonia/AVP experiment. Reopen older evidence only for a named
decision, regression, or provenance check.

## Archived accepted milestone details

Detailed accepted evidence for DLV-284 through DLV-290 is preserved in the
[2026-08-24 04:09 snapshot](history/delivery-plan/2026-08-24T04-09-43-07-00.md).
The current accepted-state summary above remains the live disposition.

## Ready overlay recovery

### DLV-473 pinned-surface action routing and controller ownership

Lane: platform lead, serialized SDK/Bridge/native-host correction. Status:
production `4c66b128` physically accepted and integrated as `055ec2f`.
Post-acceptance test-only `34f15ae9` is retained unintegrated after the first
focused SDK gate stopped on an opaque pre-test build red; no rerun or repair is
authorized under this milestone. The standing platform
worktree's two uncommitted DLV-293
test files are retained evidence: do not edit, stage, discard, stash, move, or
otherwise disturb them. Create a separate bounded clean worktree for DLV-473,
and remove only that new worktree after its branch/commit is safely retained.

Correct the reproduced pinned-input contract rather than masking its failure.
The accepted PID 31132 session at 09:26 records two `Pinned action transport
failed for media-sessions` events. Native sends context `pinnedSurface`, while
the public `ControllerInputContext` admits only DashboardQuickAction,
OpenWidget, and PinnedLayoutSelection; Bridge therefore rejects the JSON before
the widget can handle it. Native policy also deliberately maps X to host Close
and only queues A, contrary to the product rule that X, bumpers, triggers, stick
clicks, and other non-reserved controls remain available to a focused widget.

Add one generic versioned pinned-surface input context across SDK, Bridge, and
native host. Bind every request to the exact widget runtime generation,
snapshot sequence, selected pinned layout identity, selected projection input
scope, and focused element. The SDK's default resolver must resolve against the
exact selected projection root, or the ordinary full-widget root for the
host-injected Full widget fallback; it must not search sibling layouts or an
unselected root. Reuse the existing bounded action queue, current-generation
checks, selected `WidgetSurfaceCoordinator` snapshot, and ordinary widget
action semantics. Do not create a second input/focus/session authority.

While pinned focus is active, D-pad and left-stick navigation remain host-owned
focus movement, A activates the focused control, and B returns focus to the
overlay without unpinning. X, Y, LB, RB, LT, RT, LS, RS, and Menu are delivered
as ordinary authored widget input with exact current authority; an unhandled
button remains inert. View remains the host-owned entry/future pinned-surface
cycle control. Retain the explicit LB+RB+X emergency-hide chord, but never treat
X alone as Close or Unpin. Close and Unpin remain explicit tray-menu or
accessibility/chrome actions. Placement/setup and opacity modes retain their
exclusive documented controls.

Use physical-first ordering. Change production/API code only, source-audit the
complete boundary, run one coherent Release/package build, and commit
`[DLV-473]`; do not write, modify, regenerate, or run tests before the user
accepts the launched behavior. The planner will assemble the reviewed
production candidate with DLV-291 only in a bounded clean unaccepted
branch/worktree, then launch that one coherent candidate containing DLV-473
and DLV-291 for the user's controller verdict. Main integration remains gated
on that verdict. After acceptance, add only
focused SDK JSON/context, Bridge generation/layout admission, controller-route,
pinned focus/action, and projection-root tests. Do not run Game Launcher tests,
broad aggregates, DLV-293 retained tests, account/package-state mutation,
multi-pin, Avalonia/AVP, or push.

Acceptance: View enters the one pinned surface regardless of tray selection;
focus movement is visible; A and authored non-reserved shortcuts operate the
selected Full widget or package projection; X alone never closes/unpins; B
returns to the overlay; stale generation, layout, scope, focus, and sequence
requests fail closed without disturbing the last valid pin. Stop for a second
input/focus/session owner, an incompatible public design with materially
different ownership, destructive state, substantial conflict, or unrelated
first red.

### DLV-293 disable-widget main-overlay survival

Lane: platform. Status: Production `a9d36cf` physically accepted and integrated
as `10c3e26`; focused test follow-up incomplete after an unrelated first red.
Baseline: fresh clean branch from integrated
DLV-290 merge `9857beb` plus the planner assignment commit. Diagnose and correct
the observed main overlay/session failure after disabling Game Launcher from
Settings. The pin is not the defect: its continued visibility only proves the
process and peer surface remained partly alive. Do not assume a root cause from
that symptom and do not add a Game Launcher identity special case.

Acceptance: disabling any catalog widget from Settings completes through the
existing catalog/session authority without exception, main-overlay HWND loss,
or a stranded visibility/focus transaction. The main overlay remains usable;
Guide can close and reopen it; surviving tray selection/focus is deterministic.
An unrelated pin remains visible and current. If the disabled widget itself is
pinned, only that pin is torn down through existing catalog reconciliation.

Use physical-first ordering: source diagnosis and one coherent Release build,
reviewer inspection, user verdict, then only focused catalog-removal,
visibility/Guide, Settings action, and pinned-peer tests. Reuse the current host,
window, catalog, focus, and pin owners. Exclude package/config mutation in tests,
Game Launcher widget tests, broad aggregates, DLV-291, multi-pin, Avalonia/AVP,
push, and unrelated repair. Stop for a new window/session owner, destructive
state, public contract change, substantial conflict, or an unrelated red.

### DLV-291 Spotify compact pinned layouts

Lane: widgets. Status: 0.3.16 correction `64b14864` rejected after a fresh
worker's next render following 31 valid snapshots failed protocol validation;
concrete diagnosis and immutable 0.3.17 production correction Assigned. The
0.3.15 production `5435eaf` was rejected because the package
manifest omits `pinningSupported: true`, so host admission prevents tray Pin
despite the rendered layouts. Produce a clean production-only 0.3.16 correction
from current main that adds only the existing generic flag and required version
updates, then rebuild the deterministic package before a new physical verdict.
The original scope adds two package-owned layouts—
`Compact now playing` and `Now playing + up next`—beside the host `Full widget`
fallback. Reuse Spotify's existing session/queue model and polling; do not add
duplicate provider work, credentials, host knowledge, or a Spotify protocol
special case. Preserve controller actions, bounded artwork, progress, failure
states, accessibility, responsive sizing, and ordinary full-widget behavior.
Use physical-first production/package build and user verdict, then focused
package/runtime tests only; no Game Launcher tests, broad aggregate, account or
package-state mutation, publication, Avalonia/AVP, or push.

### DLV-294 generic pinned-layout projections

Lane: widgets lead, serialized SDK/protocol/native-host prerequisite. Status:
Accepted cumulative commits `94fb256` + `02cec4e`, integrated as `60536ff` from
clean planner baseline `546ab34`. Replace the current size-profile-
only limitation with one generic versioned contract that lets each bounded
pinned layout carry its own package-authored declarative root, surface hints,
active input scope, and initial focus while the host continues to inject the
always-available Full widget fallback. Preserve the existing positional
`WidgetView` API compatibility and ordinary full-widget snapshot behavior.

The single host-owned pinned surface/HWND, placement, opacity, focus router,
action admission, and LT/RT selection owner remain authoritative. A layout
switch atomically selects one validated projection; it does not create another
window, renderer, worker, provider, or navigation owner. Validate each root and
the aggregate catalog under explicit node/string/depth/resource bounds so eight
layouts cannot multiply shared-host limits. Reject malformed, duplicate, stale,
wrong-generation, or over-budget projections before native allocation and
retain the last valid pinned presentation where safe.

Publish one generic generation-bound selected-layout notification to the owning
package after explicit user selection so a package may activate data demand for
that selected projection. It carries only current widget/runtime/layout
identity, never service-specific data, and must be revoked on layout removal,
runtime replacement, unpin, or shutdown. The host must not fetch queue/provider
data or recognize Spotify. Existing sizing-only packages and snapshots remain
valid in this pre-release API generation.

This is a nonvisual serialized prerequisite: update public SDK/protocol/native
owners and directly affected public docs, run only focused SDK version/bounds,
Bridge materialization, selection-generation, and pinned-coordinator gates plus
one Release build, then commit `[DLV-294]`. Do not launch or mutate packages;
DLV-291 is the physical-first visible proof after integration. Exclude Game
Launcher tests, broad aggregate/Tier 3, provider/account work, marketplace,
multi-pin, Avalonia/AVP, DLV-293 test debt, push, and unrelated refactoring.
Stop for a second presentation/focus authority, unbounded aggregate retention,
service-specific core behavior, destructive state, or a public design with
materially different ownership outcomes.

Review disposition: initial candidate `94fb256` preserved the intended generic
ownership model but was rejected because initial lifecycle checkpoints and
incremental materialization omitted projection computed styles. Correction
`02cec4e` routes those paths and ordinary snapshots through one projection-aware
style helper and adds focused checkpoint/incremental regression coverage. The
cumulative stack is accepted and integrated; DLV-294 is closed.

The later maturity queue remains: structured diagnostics; localization and
accessibility semantics; author diagnostics/preview inspection; and public-
source pre-alpha readiness. Do not schedule generic forms, broad OverlayApp
refactoring, marketplace/publisher infrastructure, or component-count growth
without explicit promotion.

## Ordered queues

1. DLV-291 Spotify compact pinned layouts: 0.3.16 `64b14864` rejected on a
   concrete fresh-worker protocol-validation render failure; diagnose the exact
   authored transition and produce immutable 0.3.17 before another launch.
2. DLV-473 focused post-acceptance tests: clean `34f15ae9` is retained
   unintegrated after the first SDK gate stopped before tests on an opaque build
   red; later gates did not run and no rerun is authorized.
3. DLV-294 generic pinned-layout projections: accepted/integrated as `60536ff`; closed.
4. DLV-293 focused test debt: retained uncommitted after unrelated Game Launcher stationarity red; no rerun under the explicit deferral.
5. Remaining maturity deliverables, ordered after the pinned-layout UX settles.
6. DLV-248 remains deliberately deferred until explicit user promotion.

DLV-473 production is accepted/integrated and its tests are post-acceptance
work. DLV-291 awaits concrete diagnosis and a 0.3.17 production correction.
DLV-293 production is accepted/integrated;
its incomplete test follow-up is retained as blocked debt, and Game Launcher
tests remain deferred.

## Manual and blocked evidence

| Item | Required evidence |
| --- | --- |
| DLV-257 identity | Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Accepted and integrated through merge `cf77507`. |
| DLV-319–326 | Managed chain through `676cd76` accepted and integrated through `cf77507`. |
| DLV-327–421 | Accepted cumulative native fixture/build evidence integrated through `cf77507`. |
| DLV-427 | `e26b92b` and `16050bb` are unbuilt/unaccepted ancestry-bound evidence only. |
| DLV-428 | Accepted/integrated as `c38b261`; superseded in the running candidate by accepted DLV-466. |
| DLV-466 | Production `360544a` physically accepted and integrated through `cf77507`; superseded PID 89008 exited gracefully. |
| DLV-465 | Focused WidgetSwitch gate and warning cleanup accepted through `9dd7b78`. |
| DLV-467 | Test-only correction accepted through `b355865`; its focused and final Tier-3 target gates are green. |
| DLV-468 | Accepted test-only full-calendar direction-independent diagnostic span as `8a65106`. |
| DLV-469 | Accepted inclusive interpolated p95 as `3f63db9`; focused WidgetSwitch gate green at 33.8 ms. |
| DLV-470 | Accepted isolated hidden-smoke profile as `ffb8742`; focused smoke green in 1.465 seconds. |
| DLV-471 | Corrected pair `8531915` + `8886e25` accepted; focused TextEntry route green in 40.325 seconds, final exact Tier 3 assigned. |
| DLV-472 | Accepted `29601e2`, cumulatively applied as `82093d5`, and integrated through `cf77507`; focused and final Tier-3 Bridge target passed 96/96. |
| Audio Mixer synthetic focus | Final Tier 3 passed 44/45 then retained Master after synthetic Down; unrelated source was previously green, so no unchanged rerun is authorized. |
| DLV-284 | `21c3b8b` + `86d6532` integrated as `7f31e04`; compatibility `60130b4` as `052a392`; correlation/redaction `f583f40` + `ac79ed0` as `8ac55d`. The cumulative diagnostic stack is accepted only with metadata-only correction `a65228c`, integrated as `12728a2`; production build and two direct one-case gates passed, no Tier-3 rerun. The user physically accepted coherent PID 133304. |
| DLV-285 | Production `ccb46e7` from exact baseline `12728a2` is physically accepted and integrated as `609d34e`; package and Release builds exited 0. Game Launcher 0.2.1 is installed/full-trust enabled. Accepted PID 144052 was replaced only for DLV-286 physical review. Widget tests are explicitly deferred for future package work. |
| DLV-286 | The user physically accepted complete correction `a41bd72`; cumulative chain `d0ca29b` + `058efbc` + `a41bd72` is integrated as merge `627ba4c`. Responsive accepted PID 85884 already contains that production tip, so it remains running without a merge-only rebuild/relaunch. The dedicated Game Launcher test project remains untouched/deferred, and the broader Bridge aggregate must not be repeated. |
| DLV-287 | Production/test `dccf49a` is accepted and integrated as `fc91157`; focused Release build passed and WidgetSdk protocol contracts passed 89/89. Exact prior PID 85884 exited gracefully. Refreshed integrated PID 113716 is responsive, has no startup error, and admitted the generic full-application and Spotify widgets. |
| DLV-288 | Documentation `8be0ebb` is accepted and integrated as `a37d614`; scoped link/reference/contract inspection passed. Its single documentation gate stopped only on three pre-existing OverlayHost packaging assertions, with no DLV-288 link failure, and was not rerun. No runtime input changed, so PID 113716 remains accepted. |
| DLV-289 | The user physically accepted correction `a74e677` after `e7b24f4` was rejected for clipped active pixels. Test-only `941b0f9` passed six focused groups and the full chain is integrated as `4c8048d`. Exact PID 121188 contains the accepted production tip, so no tests-only rebuild/relaunch occurred. |
| DLV-292 | `ff5e7e4` binds each Bridge-cached snapshot to its worker start ordinal and uses existing typed stale-base recovery after replacement. Production build and three focused lifecycle/native gates passed; integrated as `80cdb10`. The user accepted PID 81980 by default because live reproduction is impractical. |
| DLV-293 | Production `a9d36cf` is physically accepted and integrated as `10c3e26`; PID 137288 already contains that production tip. New catalog-removal/focus/Guide assertions completed before the focused host gate stopped on an older Game Launcher stationarity correlation. The pin-coordinator suite did not run; both uncommitted test diffs remain retained, with no rerun or Game Launcher repair authorized. |
| DLV-248 | Deferred until explicit user promotion. |

## Integrated reliability — DLV-292 fresh-worker virtual-window recovery

Lane: platform, serialized shared runtime/native admission owner. Status: accepted/integrated as `80cdb10`; the user accepted PID 81980 by default because live reproduction is impractical. User evidence on prior PID 37884 showed Games & Apps, Network Controls, and Full Application Reference failing after a retained checkpoint crossed a fresh worker start.

Root cause: Bridge retained snapshot sequence without the worker start ordinal
that produced it, so a restarted worker appeared to continue the old process-
local virtual-window generation. `ff5e7e4` binds both, returns typed stale-base
after replacement, and reuses native RecoveryCheckpoint for a fresh Replace.
No stale-generation validation, checkpoint retention, or identity rule weakened.

The Release build passed. Focused Runtime idle-unload, two-widget Bridge restart,
and native coordinator/lifecycle/action-feedback gates passed. Tier 3, broad
aggregate, Game Launcher tests, and package/config mutation did not run.

Live packaged-worker reproduction remains unperformed by explicit user
disposition; the focused deterministic lifecycle evidence is final for DLV-292.
