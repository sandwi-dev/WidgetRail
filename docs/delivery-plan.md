# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-429 assignment is preserved in the
[2026-08-22 01:19 snapshot](history/delivery-plan/2026-08-22T01-19-05-07-00.md).
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-433 assigned. Preserve seven held diffs and all rejected/restoration evidence. DLV-284 remains queued. |
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
7. `src/OverlayHost/WidgetSwitchHostTests.cpp` — cumulative DLV-383–432
   extent, switching, Back, rendering-mode, and fallback-authority corrections.

The accepted DLV-428 checkpoint is diagnostic-only. It emits after successful
fallback `EndDraw`, requires exact current widget instance/runtime/presentation/
snapshot sequence and geometry authority, deduplicates unchanged paints, and
does not redefine `set-window-pos` or `composition-transition`.

## Assigned platform test adoption — DLV-429

Lane: platform test evidence. Baseline: integrated main `c38b261` plus only the
seven held files above. Dependency: physically accepted DLV-428.

Change only held `src/OverlayHost/WidgetSwitchHostTests.cpp`. Preserve the
DLV-426 distinction between startup-unavailable fallback and a real mid-session
DirectComposition-disabled transition.

In startup-unavailable fallback only, admit the latest typed pre-Right
`phase=presentation-checkpoint` record when:

- its Audio widget/instance/runtime/presentation authority is exact and current;
- its sequence equals the latest current pre-switch Audio paint sequence;
- its work area, DPI, interface scale, and computed target are valid;
- its recorded content HWND/client-screen geometry exactly equals the immediate
  live HWND/client geometry.

Do not treat the checkpoint as a placement transaction or admit it for a live
DirectComposition-disabled transition. That route retains its strictly
newer-than-transition placement proof. Do not weaken assertions, tolerances,
timeouts, or any other file.

Create a new detached worktree from `c38b261`, copy only the exact seven held
files, and prove exact path-set and per-file SHA-256 equality. Through the
DLV-418 sanitized Windows PowerShell owner, run
`build.ps1 -Configuration Release -WidgetSwitchTestsOnly` once. Stop first red
and classify before edit/rerun.

If focused green, run the full native gate once in the same isolated worktree
and environment. If both gates are green, create only these four scoped commits
in the original platform worktree and stop before Tier 3:

1. `tests/WidgetSwitchFixture/Program.cs` — DLV-375.
2. `src/OverlayHost/WidgetSwitchHostTests.cpp` — DLV-429.
3. `src/OverlayHost/build.ps1` — DLV-349.
4. `RealHostAccessibilityTests.cpp`, `WidgetActionFeedbackTests.cpp`,
   `WidgetActionFailureHostTests.cpp`, and `ColdDashboardHostTests.cpp` —
   cumulative DLV-374.

Do not integrate, rebuild/relaunch PID 144396, change product/package state,
touch production code, remove retained worktrees/artifacts, assign DLV-284, or
push.

Disposition: the focused gate stopped first red because a current
`presentation-checkpoint` was also the latest generic fallback record after a
live DirectComposition-disabled transition. The startup rule was correct, but
the live-transition selector did not filter out non-placement phases. No
production defect is proven; no full gate or commits ran. Evidence is retained
under `%TEMP%\wrail-dlv429-widget-switch-20260822-011700`.

## Assigned platform test correction — DLV-430

Change only held `WidgetSwitchHostTests.cpp`. Preserve all DLV-429 checkpoint
validation. Select authority by origin before validating it:

- startup-unavailable fallback selects only the latest exact
  `phase=presentation-checkpoint`;
- live DirectComposition-disabled transition selects only the latest
  `phase=set-window-pos` or `phase=composition-transition` newer than its
  transition marker, ignoring later checkpoints.

Do not admit an unknown phase, fall back between origin types, weaken any
identity/sequence/geometry proof, or change another file. Recreate an isolated
`c38b261` plus exact-seven-file tree with full hash parity and run the focused
gate once under the sanitized owner. Stop first red. If focused green, run the
full native gate once; if both are green, create the same four scoped commits,
using DLV-430 for `WidgetSwitchHostTests.cpp`, and stop before Tier 3. Preserve
all DLV-429 process, state, artifact, integration, and push prohibitions.

Disposition: selector filtering passed, then the live-disabled route had no
placement phase newer than its marker. A DComp failure can retain the existing
HWND without moving it; demanding a placement transaction is therefore an
invalid oracle. The current checkpoint was correctly excluded under DLV-430,
and no production geometry defect is proven. Evidence is retained under
`%TEMP%\wrail-dlv430-widget-switch-20260822-014600`.

## Assigned platform test correction — DLV-431

Change only held `WidgetSwitchHostTests.cpp`. Keep each origin marker explicit,
but use the first exact current `presentation-checkpoint` after that marker as
the resulting fallback-state authority for both startup-unavailable and live
DirectComposition-disabled routes. Do not describe it as a placement
transaction. Require exact current Audio identities and pre-switch sequence;
valid work/DPI/scale; and exact equality of computed target, recorded content
window, immediate live content window, recorded client-screen geometry, and
immediate live client-screen geometry. Any real placement-phase record remains
independent supporting evidence and cannot substitute for the checkpoint.

Do not accept a checkpoint before the applicable marker, mismatch sequence or
geometry, weaken timeouts/tolerances, or touch another file. Recreate isolated
`c38b261` plus exact seven-file parity and run the focused gate once under the
sanitized owner. Stop first red. If focused green, run the full native gate
once; if both are green, create the same four scoped commits using DLV-431 for
`WidgetSwitchHostTests.cpp`, then stop before Tier 3. Preserve every DLV-430
process, state, artifact, integration, and push prohibition.

Disposition: the focused gate selected the first post-marker checkpoint by
phase and then found its sequence was older than current Audio authority.
Exact-current authority had to become part of selection. No production defect
was proven; no full gate or commits ran. Evidence is retained under
`%TEMP%\wrail-dlv431-widget-switch-20260822-020500`.

## Assigned platform test correction — DLV-432

Change only held `WidgetSwitchHostTests.cpp`. Preserve DLV-431's explicit
startup-unavailable and live-disabled origin markers. After the applicable
marker, select the first `presentation-checkpoint` whose Audio widget,
instance, runtime, presentation, snapshot, and sequence authority is already
exactly current, including equality to the latest pre-switch Audio sequence.
Skip post-marker checkpoints with nonmatching authority; do not select them and
then fail the resulting-state assertion. Preserve every exact identity and
geometry proof.

Disposition: exact-current filtering found no eligible checkpoint after the
live-disabled marker. The evidence did not distinguish a missing checkpoint
from asynchronous publication not yet visible to the immediate read. The real
build/test completed in about two minutes with exit `1`; its opaque
`Start-Process -Wait` owner remained stale for roughly another 18 minutes.
Evidence is retained under
`%TEMP%\wrail-dlv432-widget-switch-20260822-024200`.

## Assigned platform test architecture — DLV-433

Test-only; no production change. Make fallback-checkpoint selection a small
deterministic seam with table-driven cases covering pre-marker records, wrong
phase, stale identity/sequence, the first exact-current record, and no match.
The real-host fixture must call that same seam. On fallback-authority failure,
retain the complete overlay log and a bounded classification summary.

For live-disabled fallback, replace the immediate read with one bounded wait
for the first exact-current checkpoint after the explicit marker; then apply
every existing exact identity, sequence, work/DPI/scale, target, window, and
client-geometry assertion. Do not weaken those checks, admit a stale record,
change production code, or expand unrelated test scope.

Verification must obey the one-minute command observability rule. Never use the
stale opaque `Start-Process -Wait` pattern. Run the deterministic selector test
first. Only if green, recreate isolated `c38b261` plus the authorized held files
with full hash parity and run the focused real-host gate once. Stop first red.
If focused green, run the full native gate once. If both are green, propose the
exact scoped commit split and stop without committing until reviewer approval.
Preserve PID 144396, product/package state, artifacts, integration, and push
prohibitions.

First deterministic disposition: the selector correctly chose the second
post-marker exact-current record, but the table assertion passed the remainder
of the synthetic log to `TextField`, so terminal `sequence=7` read into the next
line. Correct only that bounded selected-record assertion/fixture shape. Before
rerunning, move the selector-only build branch ahead of runtime packaging and
managed fixture publication; a pure selector route must not rebuild the managed
artifact graph. Run only the deterministic route once. It must remain observable
and should terminate within one minute after eliminating packaging; otherwise
stop and diagnose. Only a green deterministic result reauthorizes the one
observable focused real-host run above.

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

Status: queued, not assigned. It becomes assignable only after DLV-433, the
cumulative native gate, commit review/integration, and exact clean Tier 3 are
green. No new virtualization feature may precede it.

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

1. DLV-433 deterministic fallback-authority seam, observable focused/full native gate, and scoped commits.
2. Reviewer commit/diff review, then one exact clean Tier-3 run.
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
| DLV-327–421 | Native fixture/build evidence remains held behind DLV-433 and exact Tier 3. |
| DLV-427 | `e26b92b` and `16050bb` are unbuilt/unaccepted ancestry-bound evidence only. |
| DLV-428 | Accepted/integrated as `c38b261`; PID 144396 already runs it. |
| DLV-284 | Queued until cumulative review/integration. |
| DLV-248 | Deferred until explicit user promotion. |

## Recent dispositions

| Milestone | Disposition |
| --- | --- |
| DLV-424 | Detached integrated-main worktree established the lock-safe build route. |
| DLV-425 | Isolated build succeeded; startup marker was wrongly treated as a live transition. |
| DLV-426 | Origin distinction exposed that startup fallback lacked typed current authority. |
| DLV-427 | Correct semantics on rejected ancestry; hash mismatch prevented build/integration. |
| DLV-428 | Integrated-base `dae5e5b` accepted/integrated as `c38b261`. |
| DLV-429 | Startup checkpoint was valid; live-transition selector wrongly chose a later non-placement checkpoint. |
| DLV-430 | Phase filtering passed; live fallback legitimately retained HWND without a new placement transaction. |
| DLV-431 | First post-marker checkpoint had older sequence authority; selection must require exact-current authority. |
| DLV-432 | No exact checkpoint was immediately visible; its stale owner hid a two-minute red result for 18 minutes. |
| DLV-433 | Assigned deterministic authority seam, retained failure logs, bounded checkpoint wait, and observable commands. |
