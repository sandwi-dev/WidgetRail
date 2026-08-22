# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-463 is preserved in the
[2026-08-22 10:48 snapshot](history/delivery-plan/2026-08-22T10-48-01-0700.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole implementation authority.

## Current accepted state

- Local `main` contains physically accepted DLV-428 production as `c38b261`.
  Later commits may be reviewer-owned control-plane changes only.
- The coherent accepted artifact visibly runs as OverlayHost PID 144396 with
  matching WidgetBridge PID 64560 from
  `C:\Users\dwive\AppData\Local\Temp\wrail-dlv428-6926e05-20260822-004155\GameBarAlternative\src\OverlayHost\out\Release`.
  `OverlayHost.exe` SHA-256 is
  `7999E3FAB4F7C952D757F553C1FF5709C9C37CA988BDE4CF820BA25E077EA3F2`.
  Do not rebuild or relaunch it for tests or reviewer documents.
- DLV-318 is the exact recoverable prior accepted Release at
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`;
  executable SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
- Spotify 0.3.14 remains installed, selected, and enabled. Preserve every
  package, credential, account, provider, and configuration state.
- Managed tests are accepted through `676cd76`: DLV-319 `199a81b`, DLV-324
  `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`. All named managed
  Tier-3 gates are green.
- DLV-283 platform production is `cdbb04a`. The cumulative production/test
  chain awaits the native gate, independent review, and one clean Tier-3 run.

## Standing tasks

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-465 assigned: own the complete focused WidgetSwitch stabilization loop through green, without planner reassignment between in-scope reds. DLV-284 remains queued. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle and clean at `676cd76`; do not begin work or change product state. |

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

## Held platform state

The original platform branch contains rejected/restoration ancestry plus
unaccepted DLV-427 `e26b92b`; never integrate that ancestry. Exactly seven
authorized diffs remain held:

1. `src/OverlayHost/build.ps1` — DLV-349 numeric exit authority.
2. `src/OverlayHost/RealHostAccessibilityTests.cpp` — DLV-327–330 fixture
   alignment; 291 focused checks previously green.
3. `src/OverlayHost/WidgetActionFeedbackTests.cpp` — deterministic replacement
   deadline; 314 checks previously green in the native gate.
4. `src/OverlayHost/WidgetActionFailureHostTests.cpp` — current Settings/tray/
   accessibility/feedback/reopen authority; focused route previously green.
5. `src/OverlayHost/ColdDashboardHostTests.cpp` — current two-HWND Settings
   activation/re-show authority; focused route previously green.
6. `tests/WidgetSwitchFixture/Program.cs` — DLV-375 named action argument.
7. `src/OverlayHost/WidgetSwitchHostTests.cpp` — cumulative DLV-383–435
   extent, switching, Back, rendering-mode, and fallback-authority corrections.

The accepted DLV-428 checkpoint is diagnostic-only. It emits after successful
fallback `EndDraw`, requires exact current widget instance/runtime/presentation/
snapshot sequence and geometry authority, deduplicates unchanged paints, and
does not redefine `set-window-pos` or `composition-transition`.

## Complete focused WidgetSwitch stabilization — DLV-465

DLV-464 changed only the post-Up call site and passed the prior false match: it
selected exact admitted/current widget-owned `settings-ready` paint authority
and required enabled keyboard-focused Ready UIA authority. Its one focused run
then stopped later at the existing fallback RefreshRetained assertion. The log
shows unarmed priming accepted a momentary equal `772x558` paint/checkpoint,
then the already-scheduled preferred/content intrinsic cascade completed via
two fallback render-target resizes and settled at `772x828` after the block
boundary. This is another test precondition race; no production defect is
established. Evidence is under
`%TEMP%\wrail-dlv464-widget-switch-20260822-104500`.

Continue the authorized uncommitted DLV-464 edit in the preserved detached
`e9412ec6` tree. Preserve its exact post-Up paint/UIA correction. First correct
the HWND-fallback unarmed priming correlation: require post-priming resize
evidence, exact equal paint/checkpoint/live geometry after it, and a fence/reread
that rejects a candidate superseded by newer extent-refresh, resize, placement,
or Settings-paint evidence. Do not use a quiet-period sleep/retry or hard-code a
terminal extent.

The user authorizes this assignment to own the complete focused
`WidgetSwitchHostTests` stabilization loop. After every invocation, retain and
classify the exact first red. If it is another false test precondition,
correlation, lifecycle, cleanup, or assertion mismatch within the same focused
route, inspect the retained evidence, make the smallest root-cause test-only
correction, and continue without returning for a new planner assignment. Batch
all already-understood corrections before the next run. Use the smallest
available exact failing case during iteration; run the complete focused
`-WidgetSwitchTestsOnly` gate once after the worktree is coherent. A later red
in that final gate remains owned by DLV-465 when it is an in-scope harness defect.
For DLV-465 only, this continuing ownership supersedes the generic requirement
to return to the planner after each first red; it does not waive first-red
classification, evidence retention, or proportional verification.

The default edit surface is `src/OverlayHost/WidgetSwitchHostTests.cpp`. The
agent may also change `tests/WidgetSwitchFixture/Program.cs` or test-only owner/
launch code in `src/OverlayHost/build.ps1` only when retained evidence proves
that file is the direct root cause and the change is necessary for this focused
gate. Do not alter production behavior, SDK/runtime/Bridge code, packaging,
accepted product/package state, timing/tolerance merely to make the test pass,
or unrelated tests. Preserve strict instance/runtime/presentation/snapshot,
sequence, geometry, input, focus, cleanup, and fail-closed assertions. Do not
weaken or delete coverage; replace inferred or racy authority with exact
observable authority.

Every invocation must use durable streams/results and a bounded owner. Inspect
output, exact descendants, CPU, and result files every 15-30 seconds; diagnose
60 seconds of silence immediately and leave no owned descendants. DLV-465 stops
only when the focused gate is green or when retained evidence proves a genuine
blocker outside the authorized test-only surface, especially a production
defect or a required production/protocol change. Report that blocker without
editing around it. Do not run Tier 3, integrate main, rebuild/relaunch PID
144396, change product/package state, remove evidence, assign DLV-284, or push.

## After the cumulative native gate is green

1. Review each of the four commits and the cumulative diff. Reject extra files,
   production changes, weakened assertions, timeout/tolerance changes, debug
   artifacts, and unrelated cleanup.
2. Assign one canonical Tier-3 run from a clean detached tree containing the
   exact accepted production and four test/build commits.
3. If Tier 3 is green, integrate only the explicit accepted hashes. Never
   integrate the rejected/restoration/ancestry-bound commits listed above.
4. Test/build-tool integration does not warrant rebuilding or relaunching the
   already-current accepted overlay.
5. Rebaseline both lanes, then assign DLV-284 before new virtualization work.

## Queued platform production — DLV-284 typed publication transactions

Status: queued, not assigned. It becomes assignable only after DLV-452 startup
classification and correction, the
cumulative native gate, commit review/
integration, and exact clean Tier 3 are green. No new virtualization feature
may precede it.

Replace semantics inferred from `allowUpdate`, base zero/nonzero, and recovery
conditions with one private typed transaction model through SDK/runtime,
Bridge, and host. Distinguish at least `IncrementalUpdate`,
`OrdinaryCheckpoint`, and `RecoveryCheckpoint`, with exact legal base, origin
authority, retry policy, and admission result. Preserve compatibility
intentionally; stop for a required public wire or third-party SDK break.

Keep one final transaction owner through admission and commit. Express legal
combinations in one table-driven policy over host sequence, request base,
widget/lifecycle/runtime/presentation authority, publication intent,
collection generation, virtual-window marker, and outcome. Transport layers
may validate facts but must not independently infer or mutate intent.

This is bounded hardening, not a framework rewrite. Remove old inference only
when replaced; do not add a second state machine or package special case.
Preserve ordinary validation, bounded windows, private widget data, last-valid
presentation, and host-owned focus/input/render authority. Require focused
table/interleaving evidence for every legal/illegal transition and retain
physical-first ordering. Never push.

## Future architecture queue — maturity review additions

Status: ordered future work, not assigned. It does not displace the cumulative
native integration or DLV-284.

1. Generic Game Launcher cutover through ordinary `ViewSnapshot`, responsive
   grid/scroll/navigation, semantic tiles, virtual windows, bounded artwork,
   WRSS, controller focus, and generic `WidgetApplicationRuntime`. Obtain a
   physical verdict before deleting the dormant framework slice.
2. Deliberate LauncherExperience vertical-slice deletion: catalog, public
   advanced-presentation models, Bridge routes, native adapter/projection/state,
   Settings/CLI flows, references, fixtures, compatibility baselines, and docs.
   Add no custom-presentation escape hatch or compatibility layer.
3. Targeted LauncherExperience state retirement preserving themes, ordering,
   package configuration, credentials, and Game Launcher-owned state. Require
   no active `LauncherExperience`/`AdvancedPresentation` production references,
   generic full-trust Game Launcher, no launcher-specific host knowledge,
   physical acceptance, and focused evidence.
4. One authoritative model-level protocol-version calculator shared by SDK
   snapshot creation and raw validation, with exact gated-node/property
   coverage, before any new protocol feature.
5. SDK stability/evolution contract.
6. Stable structured diagnostic contract.
7. Localization and accessibility semantics.
8. Author diagnostics and preview inspection.
9. Public-source pre-alpha readiness.

Do not schedule generic forms, broad OverlayApp refactoring, mediated import/
export, background scheduling, marketplace/publisher infrastructure, or new
component-count expansion without separate evidence and explicit promotion.
Extract native authorities only when real work touches them.

## Ordered queues

1. DLV-465 complete focused WidgetSwitch stabilization through green or a proven out-of-scope blocker.
2. Reviewer integration of the explicit cumulative hashes after focused green and independent review.
3. DLV-284 after cumulative clean integration.
4. Generic Game Launcher cutover; LauncherExperience deletion/state retirement;
   protocol requirements; then the remaining maturity deliverables.
5. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual and blocked evidence

| Item | Required evidence |
| --- | --- |
| DLV-257 identity | Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Production accepted; integration awaits the native gate, exact Tier 3, and review. |
| DLV-319–326 | Managed chain through `676cd76`; all managed Tier-3 gates green. |
| DLV-327–421 | Native fixture/build evidence remains held behind DLV-452 startup classification and exact Tier 3. |
| DLV-427 | `e26b92b` and `16050bb` are unbuilt/unaccepted ancestry-bound evidence only. |
| DLV-428 | Accepted/integrated as `c38b261`; PID 144396 already runs it. |
| DLV-284 | Queued until cumulative review/integration. |
| DLV-248 | Deferred until explicit user promotion. |

## Recent dispositions

| Milestone | Disposition |
| --- | --- |
| DLV-449 | Cargo passed; outer PowerShell stream merging later reclassified MSVC diagnostics. |
| DLV-450 | Separated capture passed; child lacked the parent-only Utility import. |
| DLV-451 | Wrapper passed; reused partial artifact tree failed isolated Bridge startup. |
| DLV-452 | Fresh parity tree reproduced startup; malformed test catalog JSON was the exact cause. |
| DLV-453 | Catalog delimiter passed; worker-local arm acknowledgements did not prove host-visible invalidation. |
| DLV-458 | Atomic seam fast-green; focused startup hit unrelated Bridge pipe access denial. |
| DLV-460 | Seven native files were source-clean, but its exact checkpoint omitted the accepted managed chain. |
| DLV-461 | PowerShell-7 aggregate reached Runtime 77/78; missing DLV-326 reproduced its already-fixed PID publication race. |
| DLV-463 | Exact cumulative Tier 3 passed through 291 real-host accessibility checks; WidgetSwitch then selected a delayed tray-owned paint. |
| DLV-464 | Exact post-Up focus selection passed; fallback priming then crossed the block boundary before its intrinsic resize settled. |
