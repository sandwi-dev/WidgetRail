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
- DLV-318 is the exact recoverable prior accepted Release at
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`;
  executable SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
- Spotify 0.3.14 remains installed, selected, and enabled. Preserve every
  package, credential, account, provider, and configuration state.
- Managed tests are accepted through `676cd76`: DLV-319 `199a81b`, DLV-324
  `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`. All named managed
  Tier-3 gates are green.
- DLV-283 platform production `cdbb04a` and the complete reviewed cumulative
  production/test chain are integrated through `cf77507`.

## Standing tasks

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-293 production is accepted/integrated as `10c3e26`; two uncommitted test diffs are retained as blocked evidence after the first unrelated red. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-291 Spotify compact pinned layouts is Assigned from accepted DLV-293 integration; Game Launcher tests remain deferred/out of scope. |

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

Lane: widgets. Status: Assigned. Baseline: fresh clean widgets branch from
accepted DLV-293 integration `10c3e26` plus the planner assignment commit. Add two package-owned layouts—
`Compact now playing` and `Now playing + up next`—beside the host `Full widget`
fallback. Reuse Spotify's existing session/queue model and polling; do not add
duplicate provider work, credentials, host knowledge, or a Spotify protocol
special case. Preserve controller actions, bounded artwork, progress, failure
states, accessibility, responsive sizing, and ordinary full-widget behavior.
Use physical-first production/package build and user verdict, then focused
package/runtime tests only; no Game Launcher tests, broad aggregate, account or
package-state mutation, publication, Avalonia/AVP, or push.

The later maturity queue remains: structured diagnostics; localization and
accessibility semantics; author diagnostics/preview inspection; and public-
source pre-alpha readiness. Do not schedule generic forms, broad OverlayApp
refactoring, marketplace/publisher infrastructure, or component-count growth
without explicit promotion.

## Ordered queues

1. DLV-291 Spotify compact pinned layouts: Assigned widgets from accepted DLV-293 integration.
2. DLV-293 focused test debt: retained uncommitted after unrelated Game Launcher stationarity red; no rerun under the explicit deferral.
3. Remaining maturity deliverables, ordered after the pinned-layout UX settles.
4. DLV-248 remains deliberately deferred until explicit user promotion.

DLV-291 is the sole executable production assignment. DLV-293 production is
accepted/integrated; its incomplete test follow-up is retained as blocked debt,
and Game Launcher tests remain deferred.

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
