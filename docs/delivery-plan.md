# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through the rejected `54167ee` DLV-284 diagnostic
candidate is preserved in the
[2026-08-23 13:36 snapshot](history/delivery-plan/2026-08-23T13-36-12-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole implementation authority.

## Current accepted state

- Local `main` contains reviewer planning through DLV-305; its latest runtime
  input remains `283c9ff`,
  integrating physically accepted DLV-300
  production `5bb7a57`, production validation correction `5b1c170`, and focused
  tests `d1a3d5d` after accepted DLV-298 Spotify migration merge `c9e9b97`.
- DLV-300 makes View host-owned for tray/pin transfer and leaves B widget-owned
  inside a pin. Its Release build and physical controller verdict passed.
- The exact accepted DLV-300 candidate PID 67596 was closed cooperatively to
  release its output DLL. The resumed interop gate, native 310/106-check gates,
  two SDK cases, Bridge catalog case, and focused Bridge Release build are all
  green. Fully packaged coherent main Release PID 109280 is responsive with
  executable SHA-256
  `31BF9C88F3336B9E8645FD30B693EA4E54B115D374C567F060020DCEA14448E8`
  and no `startup-error.txt`.
- Spotify 0.3.26 remains the sole installed, selected, enabled version. Its
  high-level pinned-layout migration is accepted. The intermittent consumed-
  input report has no proven cause; DLV-303 owns diagnostics only and no fix is
  authorized.
- The current coherent Release and Spotify package are confirmed current, but
  the Up Next pin can remain on Loading. Provider diagnostics prove the queue
  operations complete successfully while host diagnostics record failed
  pinned-layout selection and action delivery during rapidly advancing Spotify
  progress snapshots. DLV-304 owns the serialized generic authority correction
  after DLV-303; this is not a provider or package-version failure.
- A newly reported resize commit visibly returns the pin to its prior size.
  DLV-305 proved the exact native cause before editing: preview and capture use
  the widget-expanded placement limits, but persistence revalidates that legal
  placement against fresh default 960 by 540 DIP maxima, rejects it as invalid,
  and the explicit failure/cancel path restores the original HWND without
  replacing the durable file. The scoped correction remains active.
- Full detailed evidence through this state is preserved in the
  [2026-08-24 19:39 snapshot](history/delivery-plan/2026-08-24T19-39-18-07-00.md).
  That snapshot is historical evidence only.

## Standing tasks

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-305 pinned resize commit diagnosis/correction is Assigned in a separate clean worktree and may not overlap DLV-303 files. DLV-304 is Ready after serialized DLV-303 instrumentation; DLV-301 follows both corrections. The standing worktree's retained DLV-474/DLV-293 diffs and blocked DLV-473 test commit remain immutable evidence and must not be touched. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-302 closed with a precise observability gap. DLV-303 generic action-correlation instrumentation is active from clean integrated `b089f94` in a separate worktree and has no fix authority. The dirty standing DLV-298 evidence remains untouched. DLV-296 and DLV-297 remain Ready. |

## Execution rules

- Local `main` and reviewer documents are reviewer-owned. Implementation tasks
  never edit them or push; the planner never authors implementation/test code.
- Review and integrate only explicit accepted DLV commits. Never integrate
  rejected `0494b69`, restoration `ad109f8`, ancestry-bound DLV-427 `e26b92b`,
  or its isolated cherry-pick `16050bb`.
- Follow physical-first order: production/build, user verdict, then tests.
  Relaunch only for a reviewed production/runtime candidate or an accepted
  integrated runtime-input change.
- Physical acceptance begins the required focused-test phase; it does not close
  the milestone. Resolve, commit, review, and integrate its in-scope regression
  coverage before advancing that lane to the next production deliverable,
  except for a genuine user stop condition.
- Run affected Tier 1 once, the smallest linked Tier 2 only for a changed
  boundary, and Tier 3 only when assigned. A product assertion, prerequisite,
  or build defect stops the ordered gates. For a pre-test environmental or
  infrastructure failure, diagnose once, apply only a proven safe state
  correction, resume the blocked gate, and continue independent focused gates
  that cannot mask it. Never treat an unchanged rerun as a fix. Retain exact
  provenance and numeric exits.
- Avalonia/AVP is closed failed-experiment history. Do not resume, message,
  launch, integrate, delete, or otherwise touch it.
- Preserve strict bounded admission, last-valid presentation, current
  interaction authority, installed/configured state, and unrelated user work.
  Never push.

## Retained recovery and focused-test closure

- DLV-474 Bridge-session recovery remains rejected before integration. Its
  retained correction passed the native coordinator gate and then hit an opaque
  managed pre-test build failure. The standing platform worktree evidence is
  immutable until a separately assigned recovery closes it.
- DLV-473 production is accepted/integrated. Its clean focused test commit
  `34f15ae9` remains unintegrated after an opaque pre-test SDK build failure and
  must be recovered under the current post-acceptance test-closure rule.
- DLV-298 Spotify's accepted high-level migration retains one uncommitted
  focused regression diff after an opaque pre-test failure. Recreate or recover
  that high-level regression after DLV-300 closure; do not revive the obsolete
  low-level DLV-291 test.
- DLV-293 production is accepted/integrated. The user's explicit instruction
  still defers Game Launcher widget testing; unrelated generic pin/catalog
  coverage may be recovered separately without running Game Launcher tests.

### DLV-300 host-reserved View and widget-owned pinned Back

Lane: platform, serialized native-input/public-authoring contract. Status:
closed. Production `5bb7a57` was physically accepted and integrated as
`b1385ff`. Focused testing then exposed that ordinary configured widgets did
not use the existing host-reserved-View catalog validator. Shared Bridge
correction `5b1c170` and five-file regression follow-up `d1a3d5d` are reviewed
and integrated as `283c9ff`. The interop gate passed seven native components;
the pinned host/coordinator gates passed 310 and 106 checks; both focused SDK
cases and the Bridge catalog case passed 1/1; the focused Bridge Release build
passed without warnings or errors. Fully packaged main Release PID 109280 is
responsive with the current Bridge runtime. Retained DLV-474/DLV-293 evidence
was not touched.

Make View an unconditional native host navigation control. Widgets may not map
View as an open-widget shortcut or dashboard quick action, and the host must
never forward a physical View press as an authored widget action. Keep the
public button identity only where the host-owned pinned-layout selection
notification contract requires it; do not remove or renumber the protocol enum.
Reject authored View mappings through the existing validator with a precise
author-facing diagnostic and update the directly affected SDK guidance,
templates, compatibility baseline, and implementation status.

For the current single-pin product, View from the tray or an open overlay widget
enters the pinned surface regardless of tray selection. View while the pinned
surface owns controller focus returns to the tray. B inside an ordinary focused
pinned layout is delivered to that selected projection with the same current
widget/runtime/layout/scope/focus/sequence authority as its other authored
buttons; it is never interpreted by the host as Return to tray. This preserves
nested widget Back behavior such as Spotify playlist detail navigation. At the
root, an unhandled B remains inert rather than triggering a host fallback.
Placement/setup/opacity cancellation, the existing recovery chord, Guide, Menu
options, emergency hide, explicit accessibility actions, and click-through
lifecycle remain host-owned and unchanged.

Design the one-pin View transition so its host policy can later extend to the
user-approved cycle `tray -> pin 1 -> pin 2 -> ... -> tray`, but do not add
multiple pins, a second focus/session owner, or speculative collection state in
this milestone. Update controller-guide and accessibility copy so View and B
ownership are unambiguous.

Use physical-first ordering for the controller-feel change: production/API/docs
only, source review, one coherent Release build, exact production commit, then
planner launch and user verdict before tests. After acceptance, add only focused
native route/policy tests, SDK/protocol validation tests proving authored View
rejection, and pinned-surface tests proving B delivery at root and nested scopes,
View entry/exit, no widget View delivery, current-authority rejection, and
unchanged modal/emergency controls. Run the smallest affected native and managed
gates once in that order and stop at the first red. No Game Launcher tests,
broad aggregate, package/account mutation, multi-pin, Avalonia/AVP, or push.

Acceptance: View deterministically transfers focus between overlay/tray and the
single pin; a widget cannot declare or receive View; B performs the widget's own
nested Back action in both full-widget and authored pinned layouts and never
returns to the tray; stale/wrong-layout input still fails closed; guide and
accessibility semantics match the physical controls. Stop for protocol enum
removal/version expansion, a second native input/focus authority, destructive
state, substantial conflict, or materially different exit/cycle semantics.

### DLV-301 pinned-surface right-stick free scrolling

Lane: platform, after serialized DLV-303 instrumentation. Status: Ready. The defect is
concrete: the pinned-focus branch returns before the ordinary overlay's
`HandleRightStickFreeScroll` path, sends only left-stick/D-pad navigation to the
pinned coordinator, and exposes the right stick only as a pressed button. Route
analog right-stick motion through the single pinned coordinator and its selected
projection, current runtime/layout/scope/focus/sequence authority, renderer,
scroll owner, bounded offsets/damage, pagination demand, and dead-zone/re-entry
policy. Preserve right-stick-click shortcuts. Do not borrow the main overlay's
renderer/session state, add a second authority, recognize Spotify, or add
multi-pin behavior. Use physical-first production/build, user verdict, then the
smallest coordinator/input tests. Stop for a protocol change or materially
different scroll ownership.

### DLV-302 Spotify intermittent consumed-input diagnosis

Lane: widgets. Status: closed diagnostic-only with no confirmed product cause.
The current log proves a representative Spotify X reached SDK serial-queue
admission and was followed by new snapshots, but it does not record the exact
action ID, dequeue/start, classification, pending-operation decision, provider
terminal result, or semantic outcome. Therefore pending playback serialization,
provider failure, and other gates remain hypotheses. No files, processes,
packages, tests, or user state changed and no commit exists. The missing
correlation is assigned to DLV-303; no corrective implementation is authorized.

The investigation attempted to correlate one reported no-op from physical controller sample,
host focus/action admission and Bridge request/reply through Spotify action
classification, pending-operation/provider execution, and resulting status or
presentation. Existing code has several plausible gates, including pending
playback serialization, but none is accepted as the cause without timestamped
end-to-end evidence. Inspect current logs and source only; do not edit product or
tests, mutate the package/account/provider, run broad gates, or make a fix. If
existing evidence is insufficient, report the exact missing correlation and a
minimal diagnostic proposal. The user must receive and accept a concrete cause
before any corrective implementation is authorized.

### DLV-303 end-to-end widget action correlation diagnostics

Lane: widgets lead, serialized generic host/SDK plus package-local diagnostics.
Status: Assigned from integrated `b089f94` in a separate clean worktree. Reuse
the existing controller/action request identity to emit one bounded correlation
across generic host admission/reply (button, widget, focus, scope, snapshot and
runtime generation, handled/error), SDK serial-queue admission/dequeue/terminal
state (action ID), and Spotify-local classification, pending-operation decision,
provider start/terminal code, resulting status and invalidation. Core layers must
remain widget-agnostic; Spotify-specific detail stays in its package. Never log
tokens, account/provider payloads, media text, credentials, or unbounded data.

This milestone is diagnostics only: do not change action routing, queuing,
timeouts, pending-operation policy, provider behavior, presentation semantics,
or user-visible controls. Perform direct lifecycle/volume review, one Release
build, and the smallest generic action plus Spotify diagnostic gates. Commit
production/tests separately if required, but do not install, launch, or fix the
reported behavior. After review and integration, the planner will visibly
launch the diagnostic Release and ask the user for one bounded reproduction.
Stop for a new public protocol, service-specific core behavior, sensitive data,
unbounded log volume, or any behavioral correction.

### DLV-304 stable pinned demand and input authority under live updates

Lane: platform, serialized generic native/Bridge/SDK correction after reviewed
DLV-303 integration. Status: Ready. Baseline: the integrated DLV-303 diagnostic
milestone on local `main`. The current packaged evidence rules out an old build
and a stuck Spotify provider: Release PID 109280 runs the current integrated
host, Spotify 0.3.26 is the sole installed generation, queue operations complete
successfully, and the same session logs pinned-layout selection/action delivery
failures while Spotify's 250 ms progress invalidations rapidly advance snapshot
sequences. Source review shows layout-selection observation and pinned action
admission both bind to exact snapshot sequence, so an otherwise current pin can
lose demand or input authority when a newer semantically compatible projection
is published between native resolution and Bridge/worker admission. Up Next then
has no durable selected-layout demand and renders its NotLoaded/Loading state.

Correct this only through the generic pin authority owners. Keep runtime
generation, selected layout, input scope, focus identity, actionable state,
button/action semantics, and current presentation authority explicit. A newer
snapshot may be used only after the existing Bridge/SDK owner proves the
selected layout and resolved input target remain semantically equivalent; do
not blindly accept an old sequence, replay an action across a changed target,
or create a second native/Bridge authority. Host-owned layout-demand
notification must survive unrelated live-data/progress publications and be
revoked deterministically. Pinned widget input must remain responsive under the
same publication churn while genuinely stale runtime/layout/scope/focus/action
requests continue to fail closed. Reuse DLV-303 correlation evidence and keep
core layers widget-agnostic; no Spotify identity, provider behavior, queue
special case, public protocol change, retry storm, or duplicate renderer/state.

Use physical-first ordering: production/API changes only, direct authority and
lifecycle review, one coherent Release build and exact production commit, then
planner launch and user verdict before tests. After acceptance, add focused
native/Bridge/SDK regressions with deterministic rapid compatible publication,
layout selection/revocation, pinned action delivery, and incompatible
runtime/layout/scope/focus/action rejection. Run only the smallest affected
native and managed gates once in the documented order. No Game Launcher tests,
broad aggregate, package/account mutation, Avalonia/AVP, or push.

Acceptance: the authored Up Next pin leaves Loading after a successful queue
result and stays demanded while unrelated progress snapshots advance; pinned
controls continue to work across equivalent live updates; deselection and every
materially stale or changed authority reject without executing an action; the
implementation retains one renderer, interaction owner, and generic contract.
Stop for protocol/version expansion, a second authority, inability to prove
semantic equivalence before rebasing, destructive state, or substantial
conflict.

### DLV-305 preserve a committed pinned resize

Lane: platform native placement owner. Status: Assigned concurrently with the
non-behavioral DLV-303 diagnostics in a separate clean worktree from local main
`8dfe871`; do not touch the standing platform worktree's retained evidence. The
user reports that resizing a pinned surface visibly works during adjustment,
but choosing Commit now snaps the HWND back to its original size. The current
`pinned-surface-placement.ini` still holds Spotify's earlier 776 by 464 DIP
placement with its prior timestamp. Direct source tracing proved the terminal
path: preview and `CommitPlacementSession` use the coordinator's effective
widget-expanded `placementLimits_`, but `PinnedPlacementStore::Save` calls
`ValidPlacement` with a fresh default `PlacementLimits{}` whose 960 by 540 DIP
maxima can reject the otherwise legal committed preview. `CommitPlacement` then
executes `CancelPlacement`, restoring the original HWND, and never replaces the
atomic store. Snapshot refresh is not the cause.

The exact cause was reported to the planner before modification. Correct only
the existing coordinator/placement owner so a successful Commit now retains the
exact constrained preview bounds
in the live HWND and durable store, including after focus/click-through
transition and ordinary snapshot publication. A failed commit must retain the
current explicit failure/cancel semantics and precise user feedback; never
claim success then restore prior bounds. Preserve DPI/work-area constraints,
atomic persistence, layout ID and opacity, runtime/presentation generation
checks, topmost policy, and one native window/placement authority. Do not change
authored preferred sizes, layout cycling, input mappings, Spotify, public
protocol/SDK, or DLV-303 diagnostic files. Stop without edits if the proven
cause requires a file currently changed by DLV-303, then serialize after its
integration.

Use physical-first ordering after the cause is reported: production only,
direct lifecycle/persistence review, one coherent Release build and exact
commit, planner launch, and user verdict before focused tests. After acceptance,
add the smallest native regression proving preview-to-commit bounds, persistence
and reload, ordinary snapshot update, focus/click-through transition, and
explicit failed/canceled restoration. No Game Launcher tests, broad aggregate,
destructive state reset, Avalonia/AVP, or push.

Acceptance: Commit now leaves the resized pin at the previewed constrained
dimensions and persists those dimensions across ordinary refresh and restart;
Cancel still restores the original bounds; failed persistence never reports a
successful commit; no new placement/window owner or widget-specific behavior is
introduced. Stop for destructive placement-store recovery, a second window or
authority, overlap with active DLV-303 work, or materially different resize UX.

### DLV-296 pinned-layout preview and diagnostics

Lane: widgets, after DLV-295. Status: Ready. Extend `wrail preview` to select one
pinned layout or enumerate all through production validation/rendering. Report
identity, bounded sizes, root/scope/focus, and action/focus diagnostics; reject
invalid declarations and warn on likely accidental identical roots. Do not add
a second renderer, capture path, package mutation, window owner, or protocol.
Use deterministic scenarios and focused CLI/preview tests only.

### DLV-297 pinned-layout templates and examples

Lane: widgets, after DLV-296. Status: Ready. Update the public media template and
compiled example with Compact, detailed, and host Full widget fallback flows.
Cover typed handles, scoped loading/states, responsive focus/accessibility, and
fake-service tests while keeping basic templates unchanged and helpers optional.
Validate the external package, links, and smallest template/example gates;
no live provider, installation, Game Launcher tests, broad aggregate, or push.

## Ordered queues

1. DLV-303 generic end-to-end widget action correlation diagnostics and DLV-305
   pinned resize commit diagnosis/correction are Assigned concurrently in
   separate non-overlapping worktrees.
2. DLV-304 stable pinned demand and input authority under live updates; Ready
   after reviewed DLV-303 integration.
3. DLV-301 pinned-surface right-stick free scrolling; Ready after DLV-304 and
   DLV-305.
4. Recover accepted DLV-298 high-level Spotify focused regression coverage.
5. Recover DLV-473 focused SDK/Bridge/native regression coverage from its proven
   pre-test infrastructure failure.
6. DLV-296 pinned-layout preview and diagnostics.
7. DLV-297 pinned-layout templates and examples.
8. DLV-474 fresh-session sequence-authority correction remains retained and
   unintegrated pending a separately controlled recovery.
9. DLV-293 generic focused debt may be recovered without Game Launcher tests;
   Game Launcher testing remains explicitly deferred.
10. Remaining maturity deliverables after the pinned-layout author workflow;
    DLV-248 stays deferred until explicit user promotion.

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
| DLV-474 | Production `a830f026` was provisionally accepted by user disposition because the historical bridge-session loss could not be reproduced, then rejected before integration when the first focused native gate proved fresh sequence 1 was compared against retained prior-session sequences 10/20. The retained correction passed 29 native coordinator scenarios plus linked native checks, then the managed diagnostic gate exited 1 during an opaque pre-test build. Four diffs remain uncommitted at identity `d14a224507d682e9c67f83860e713fe0118bb4dd`; no rerun or repair occurred. |
| DLV-291 | Spotify versions through production-only 0.3.23 `2031a8c` are physically rejected overall. The user accepted clean 0.3.24 `bbdc2368`; production is integrated as `e7ebdbe` and exact-hash 0.3.24 remains sole installed/selected/enabled under responsive PID 32880. Native tests passed 108/108; the Spotify gate stopped after 75 seconds without output, so its two-file test diff remains unaccepted debt. |
| DLV-248 | Deferred until explicit user promotion. |
