# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-463 is preserved in the
[2026-08-22 10:48 snapshot](history/delivery-plan/2026-08-22T10-48-01-0700.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole implementation authority.

## Current accepted state

- Local `main` contains physically accepted DLV-428 production as `c38b261`.
  Later commits may be reviewer-owned control-plane changes only.
- The coherent physically accepted artifact visibly runs as OverlayHost PID
  89008 from
  `C:\Users\dwive\AppData\Local\Temp\wrail-dlv466-08cf81d-20260822-140742\GameBarAlternative\src\OverlayHost\out\Release`.
  It combines accepted DLV-466 native executable SHA-256
  `FC8CABD1F4997E040D73300D0AC6741955E78C0D8A423713E999682CB97849AB`
  with the unchanged accepted DLV-428 runtime graph and catalog. DLV-466 is
  not yet integrated into local `main`; do not rebuild or relaunch this
  accepted instance for tests or reviewer documents.
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-467 owns the first canonical Tier-3 red in `WidgetActionFailureHostTests` from exact cumulative tip `9dd7b78`. DLV-284 remains queued. |
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
 editing around it. Do not run Tier 3, integrate main, rebuild/relaunch accepted
 PID 89008, change product/package state, remove evidence, assign DLV-284, or
 push.

DLV-465 reached terminal focused green as test commit `eb41cf5` over DLV-466
content commit `5a94e2e`; the latter has stable patch ID
`40ca4b6d50aacb84147159a747991e0d411074db`, identical to accepted `360544a`.
The exact focused gate passed in 83.28 seconds with host-focus p95 16 ms,
input-to-retained maximum 12 ms, and input-to-admitted maximum 711 ms; all
owned descendants exited. Warning-only follow-up `9dd7b78` renames the shadowing
local and removes the unused composition diagnostic without changing behavior.
It changes only `WidgetSwitchHostTests.cpp`, passes `diff --check`, and produced
a clean `/W4` translation unit object from source SHA-256
`D1A2E4DC7E1A58DA3B6A5BCE86A46D3072D61767BDAB7CC5B6C729302F48952C`.
The reviewer accepts the DLV-465 focused milestone through `9dd7b78`; do not
rerun its already-green focused end-to-end route.

## DLV-465 production blocker and physical-first correction — DLV-466

Focused32 is a valid DLV-465 stop. The exact worker is blocked inside `Render`,
Settings is retained/inert, and the host has no `host:host.open.back` semantic
action. Root-scope B therefore enters synchronous widget dispatch and cannot
reach the existing unhandled-root fallback. The blocked presentation cannot
republish selected/focused tray authority. Evidence is under
`%TEMP%\wrail-dlv465-focused32-20260822-144000`; the owner and all descendants
exited, and the two authorized DLV-465 test files remain preserved uncommitted.

DLV-466 was assigned to the platform lane from a new clean isolated tree at
planner `main` `08cf81d`, whose production baseline is accepted `c38b261`.
Change production code only. Give root widget scope an immediate host-owned
Back-to-tray path whenever the host can already prove current widget
presentation/input authority is unavailable, retained/inert, or otherwise
cannot make a bounded widget-first decision. The controller route and the
host-owned `host:host.open.back` UIA action must remain available without a
worker round trip in that state. Preserve widget-first B for a current nested
scope, current explicit widget actions, focused-slider adjustment exit, text
entry cancellation, failed-widget recovery, tray B overlay close, current
instance/snapshot authority, and all unrelated buttons.

Do not edit or run tests before the user's verdict. Do not copy the DLV-465
test diff into the production candidate, change protocol/SDK/runtime/Bridge or
package state, add timeouts/retries, weaken input authority, or broaden into
controller cleanup. Source-review the production route and build one coherent
Release with `pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration
Release -SkipTests -SkipPackaging`, using durable streams and the one-minute
observability rule. Commit the production-only candidate as DLV-466 and stop
for planner review, visible launch, and user acceptance. Nothing is integrated
before that verdict.

The platform lane produced clean production-only commit `360544a`. Reviewer
source review found the change confined to `src/OverlayHost/main.cpp`: its
non-current authority is fail-closed on the active bridge widget, widget-focus
surface, runtime generation, retained snapshot/root scope, and identical UIA
invocation authority, while current and nested widget dispatch remain on the
existing path. The assigned Release build completed with exit 0 and produced
`OverlayHost.exe` SHA-256
`FC8CABD1F4997E040D73300D0AC6741955E78C0D8A423713E999682CB97849AB`.
The planner gracefully closed accepted PID 144396. The first launch as PID
47516 was invalid because the native-only `-SkipPackaging` output lacked the
unchanged accepted `runtime` graph and `widget-catalog.json`; startup failed
closed with Settings absent. The planner removed that exact failed process,
hydrated only those unchanged artifacts from the accepted `c38b261` Release,
and visibly relaunched the same candidate executable as PID 89008. Startup now
admits Settings and removed `startup-error.log`. No tests ran and nothing is
integrated; DLV-466 now waits only for the physical verdict below.

The user physically accepted DLV-466 with the coherent PID 89008 candidate.
This accepts production commit `360544a`; it does not yet authorize cumulative
integration. Apply its exact production delta to the preserved DLV-465 tree,
retain the two existing uncommitted test files, and resume the already
authorized focused stabilization through terminal green. Because the accepted
running candidate already contains the production executable and the only next
delta is tests, do not rebuild or relaunch the visible overlay for that test
follow-up.

The physical verdict covers ordinary root Settings B returning to the selected
tray item, tray B closing the overlay, normal widget activation, and no
regression in nested/back or modal behavior that is reachable in the installed
candidate. After acceptance, apply the exact DLV-466 production commit to the
preserved cumulative test tree and resume DLV-465 through focused green. A
production-only rejection returns to DLV-466 without changing tests.

## After the cumulative native gate is green

1. Review every explicit commit and the cumulative diff. Reject extra files,
   unaccepted production changes, weakened assertions, timeout/tolerance
   changes, debug artifacts, and unrelated cleanup. This review is complete
   for clean detached tip `9dd7b78`: its exact chain after `676cd76` is
   `8658e76`, `fd37c34`, `eed3a48`, `914b077`, `58b75e3`, `e9412ec`,
   `5a94e2e`, `eb41cf5`, and `9dd7b78`; the cumulative surface is the three
   accepted production commits plus six test/build commits and no other files.
2. Assign one canonical Tier-3 run from that exact clean detached tip. Do not
   rebuild or relaunch accepted PID 89008 and do not rerun the focused gate.
3. If Tier 3 is green, integrate only the explicit accepted hashes. Never
   integrate the rejected/restoration/ancestry-bound commits listed above.
4. Test/build-tool integration does not warrant rebuilding or relaunching the
   already-current accepted overlay.
5. Rebaseline both lanes, then assign DLV-284 before new virtualization work.

The exact clean Tier-3 invocation ran once at `9dd7b78` and stopped first red
after 44 passed steps. `RealHostAccessibilityTests` passed 291 checks; the
native step then failed because `WidgetActionFailureHostTests` observed more
than one `LiveRegionChanged` callback after the first action-failure status.
The run exited 1 with no remaining owned descendants. Structured evidence is
under `artifacts/verification/20260822T230948Z-8b3efb96`; durable owner streams
are under `%TEMP%\wrail-dlv465-tier3-run-20260822-160920`. Do not rerun Tier 3
until the focused red is classified and corrected.

## Accepted platform test correction — DLV-467 accessibility event authority

Baseline: clean detached cumulative tip
`9dd7b7876cf80a8f4fe6d4f9221e4b4735ad4cbb`. This is a test-only assignment
owned by the platform lane. Preserve all accepted production commits and the
running accepted PID 89008; do not rebuild/relaunch it.

First inspect the retained red and the logical event contract already covered
by `AccessibilityEventsTests`: insertion/name change of one polite status must
plan exactly one logical live-region element. Determine whether the real-host
failure is an actual second logical publication, an unexpected sender, a
duplicate callback delivery for the same exact sender, or a subscription/
fixture race. The current failure copy is insufficient because it records no
sender identities or before/after counts.

Default edit surface is
`src/OverlayHost/WidgetActionFailureHostTests.cpp`; use
`src/OverlayHost/AccessibilityEventsTests.cpp` only if an exact logical-plan
assertion is genuinely missing. Improve deterministic failure evidence before
changing the assertion. Preserve exact status ID, polite role/name, focus,
single failure record, unchanged identical-replacement semantics, retention,
hide/reopen cleanup, and worker-lifetime coverage. Do not mask an unexpected
sender, use sleeps/quiet periods as authority, increase timeouts/tolerances,
weaken the logical one-publication contract, or modify production code. If
evidence proves production emits a second logical event or another production
change is required, stop with the retained proof for a physical-first
production assignment.

Accepted through test-only commits
`c80d9395530ca863362fae6ffe25564e3676800b` and
`b35586541f33fd71e75a3aac5aa4969805259567`. The retained callback evidence
proved that the apparent duplicate was two distinct logical publications from
the same polite status sender: the successful action status followed by the
failure status. The old fixture counted both as failure publications.

The corrected fixture retains `TreeScope_Subtree`, snapshots AutomationId,
Name, ControlType, LiveSetting, and RuntimeId exactly once per callback, and
uses that one snapshot for both classification and bounded diagnostics. It
requires exactly one matching failure publication, rejects unexpected senders,
and preserves zero-event identical-replacement/retention checks without longer
timeouts. The focused `-WidgetActionFailureHostTestsOnly` gate passed at
`b355865` with executable SHA-256
`E67D08709F929EBCB125722E7EEECDBF9C959F981406F17E292B70669841C156`.
The detached cumulative tree is clean. No production/runtime artifact changed,
so PID 89008 remains the accepted visible candidate and must not be rebuilt or
relaunched for this test-only delta.

The only authorized next action is one final exact clean Tier-3 run from
`b355865`; stop first red and do not rerun, edit, integrate, push, change
packages/configuration, or touch the accepted overlay during that run. Every
command must expose output within 60 seconds and long work must be checked every
15-30 seconds.

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

1. One final exact clean Tier-3 checkpoint at `b355865`, then reviewer integration of the
   explicit accepted cumulative hashes.
2. DLV-284 after cumulative clean integration.
3. Generic Game Launcher cutover; LauncherExperience deletion/state retirement;
   protocol requirements; then the remaining maturity deliverables.
4. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual and blocked evidence

| Item | Required evidence |
| --- | --- |
| DLV-257 identity | Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Production accepted; integration awaits the native gate, exact Tier 3, and review. |
| DLV-319–326 | Managed chain through `676cd76`; all managed Tier-3 gates green. |
| DLV-327–421 | Native fixture/build evidence remains held behind DLV-452 startup classification and exact Tier 3. |
| DLV-427 | `e26b92b` and `16050bb` are unbuilt/unaccepted ancestry-bound evidence only. |
| DLV-428 | Accepted/integrated as `c38b261`; superseded in the running candidate by accepted DLV-466. |
| DLV-466 | Production `360544a` physically accepted; coherent candidate PID 89008 remains running. |
| DLV-465 | Focused WidgetSwitch gate and warning cleanup accepted through `9dd7b78`. |
| DLV-467 | Test-only correction accepted through `b355865`; focused gate green; final exact Tier 3 is next. |
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
| DLV-467 | Distinct success/failure callbacks from one status sender were separated by exact semantic snapshots; focused native gate passed. |
