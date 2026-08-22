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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-442 assigned. Preserve seven held diffs and all rejected/restoration evidence. DLV-284 remains queued. |
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

Second disposition: the deterministic route passed all six cases in 18 seconds
without managed publication. The observable focused real-host gate then exited
`1` in 2 minutes 41 seconds and retained the complete overlay log. Its failure
summary claimed zero post-marker candidates, but the retained file contains the
marker at byte 15308, a sequence-5 checkpoint at byte 18300, and the exact
sequence-6 checkpoint at byte 21645. Production therefore emitted the promised
current checkpoint. Do not rerun the real-host scenario or treat the diagnostic
log as a synchronization precondition. Evidence is retained under
`%TEMP%\wrail-dlv433-realhost-monitor-20260822-042100`.

## Assigned platform test diagnosis — DLV-434

Test-only; no production or real-host execution. Add the smallest selector
replay input that invokes the already-shared compiled selector against an
explicit existing log path, marker offset, and expected sequence, printing the
selected offset and bounded counts. It must not start OverlayHost, WidgetBridge,
workers, packaging, or managed publication. Do not copy the retained user-temp
log into the repository or weaken any selector rule.

Run the fast deterministic table route once, then run the replay once against
the exact retained DLV-433 log with marker `15308` and sequence `6`. Each command
must finish within one minute and obey the observability rule. Expected replay
authority is offset `21645`; any other outcome stops first red. If both are
green, stop and report the exact code path plus a static explanation of why the
live polling view could differ. Do not modify the real-host assertion yet,
start any host gate, commit, integrate, rebuild/relaunch PID 144396, change
product/package state, remove evidence, assign DLV-284, or push.

Disposition: table cases passed in 18 seconds. Exact retained-log replay stopped
red in 7 seconds with two checkpoint candidates but zero current-Audio
candidates. The retained Windows log uses CRLF; splitting at `\n` leaves `\r`
on the terminal `sequence` field, so strict numeric parsing rejects `6\r`.
This is a deterministic test parser defect, not production or live polling.

## Assigned platform test correction — DLV-435

Test-only; no real host. Normalize only the parsed record-line CRLF boundary so
the final field is evaluated without a terminal carriage return. Do not loosen
numeric parsing, identity rules, or general field syntax. Add a table case with
the exact Windows CRLF shape and a terminal sequence field.

Run the fast table route once, then the retained-log replay once with marker
`15308`, expected sequence `6`, and expected selected offset `21645`. Each must
finish within one minute with observable output and no host, bridge, worker,
packaging, or managed publication. Stop first red. If both are green, stop and
report; do not run the real-host or full gate, modify another file, commit,
integrate, rebuild/relaunch PID 144396, change product state, remove evidence,
assign DLV-284, or push.

Disposition: green. Seven CRLF-aware selector cases passed in 7 seconds. Exact
retained-log replay passed in 7 seconds and selected offset `21645` with two
checkpoint candidates and one exact-current Audio candidate. Only the terminal
record-line carriage return is normalized; numeric and identity rules remain
strict.

## Assigned platform focused evidence — DLV-436

No code edit. Recreate a fresh detached `c38b261` tree containing exactly the
seven held files with full path and SHA-256 parity. Run
`build.ps1 -Configuration Release -WidgetSwitchTestsOnly` exactly once through
an observable owner that inspects the exact child, stdout/stderr timestamps,
and result at intervals below 60 seconds. Never use opaque `Start-Process
-Wait`. Stop immediately at the numeric result and retain the complete log.

Do not rerun the deterministic table/replay, run the full native gate, edit or
commit, integrate, rebuild/relaunch PID 144396, change product/package state,
remove evidence, assign DLV-284, or push. A red result requires classification
before any further assignment; a green result returns to reviewer scope and
commit planning.

Disposition: red after 140.110 seconds with fresh child/output inspection every
20 seconds. The exact current Audio fallback checkpoint was already present,
but Back produced no post-boundary fallback paint, composition sample, or
pending content update region. The retained fallback log ends at its sequence-6
checkpoint. Mid-session fallback intentionally resets/hides the fixed chrome
endpoint and restores the combined content-HWND path, so neither a chrome-HWND
assertion nor another unchanged run explains this result. Evidence is retained
under
`%TEMP%\wrail-dlv436-widget-switch-monitor-20260822-060100`.

## Assigned platform Back classification — DLV-437

Test-only; change only held `WidgetSwitchHostTests.cpp`. Before sending Back,
require the current fallback content UIA root to expose exact enabled
`host:host.open.back` authority and retain the exact focused widget identity.
After Back, make the user-visible semantic result the primary observation:
bounded-poll the current fallback content UIA root for exact selected and
keyboard-focused `tray:tray.audio-mixer`. Do not require a new diagnostic paint
record before querying that semantic result, and do not force a repaint from
the test.

On failure, retain one bounded classification containing the pre-Back Back-node
authority, pre/post focused AutomationId, content/chrome HWND identity and
visibility, content/chrome update-region state, and the first post-boundary
paint/action record when present. This classifier must distinguish an
unhandled-root Back dispatch failure from a successful semantic transition
whose diagnostic paint was absent. Do not change production code, input
routing, timeouts, tolerances, SDK/runtime/Bridge behavior, or another held
file.

Recreate one fresh detached `c38b261` tree containing exactly the seven held
files with full path/SHA-256 parity. Run the focused WidgetSwitch gate once
through an observable owner with exact child and durable-stream inspection at
intervals below 60 seconds. Stop at the numeric result. Do not run the selector
table/replay, full native gate, commit, integrate, rebuild/relaunch PID 144396,
change product/package state, remove evidence, assign DLV-284, or push. A red
result stops for classification; a green result returns to reviewer commit
planning.

Disposition: the Back semantic classifier passed. In the single observable run,
exact selected and keyboard-focused `tray:tray.audio-mixer` authority was
restored after Back. The first red moved later to the existing fallback
checkpoint sequence assertion after 120.139 seconds. That assertion reparses
the selected raw CRLF record with an older line extraction even though
`SelectCurrentFallbackCheckpoint` already selected it through normalized
`RecordLine`. This is test parser/correlation inconsistency, not evidence of a
Back dispatch or product defect. Evidence is retained under
`%TEMP%\wrail-dlv437-widget-switch-monitor-20260822-032900`.

## Assigned platform checkpoint record consistency — DLV-438

Test-only; change only held `WidgetSwitchHostTests.cpp`. At the later fallback
presentation checkpoint assertion, consume the already-selected record through
the existing CRLF-normalizing `RecordLine` helper before reading its fields.
Do not add another parser, loosen sequence or authority validation, change the
selector, alter input/focus behavior, change timeouts/tolerances, or touch
production, another held file, SDK/runtime/Bridge behavior, or packaging.

Statically confirm that the selected checkpoint offset is unchanged and that
all later geometry, identity, DPI, and exact-sequence assertions consume the
same normalized record. Recreate one fresh detached `c38b261` tree containing
exactly the seven held files with full path/SHA-256 parity. Run the focused
WidgetSwitch gate once through an observable owner with exact child and
durable-stream inspection every 15–20 seconds. Stop at the numeric result. Do
not run selector/replay/full native, commit, integrate, rebuild/relaunch PID
144396, change product/package state, remove evidence, assign DLV-284, or push.
Red stops for classification; green returns to reviewer commit planning.

Disposition: the normalized record correction passed its former sequence
boundary. The single observable run continued to the next strict assertion and
stopped after 120.176 seconds because checkpoint work-area authority differed
from the later live content-HWND monitor work area. The child stream recorded
exit 1; the durable owner's null exit field is an observation-script defect and
is not authoritative. Exact child PID, streams, and resource state were
inspected every 15–20 seconds. Evidence is retained under
`%TEMP%\wrail-dlv438-widget-switch-monitor-20260822-033600`.

## Assigned platform fallback geometry classifier — DLV-439

Test-only; change only held `WidgetSwitchHostTests.cpp`. Preserve every strict
fallback checkpoint geometry, DPI, identity, and sequence invariant, but do not
fail them serially. At the already-selected normalized checkpoint, capture in
one aggregate classifier: selected offset and full record; checkpoint work,
target, content window, client-screen, and DPI; current content HWND identity,
monitor identity, monitor work area, window, client-screen, and DPI; plus one
boolean for each existing invariant. Always retain the source overlay log when
any geometry invariant fails. The first red must report every value and every
failed invariant together so no further run is needed merely to reveal the
next comparison.

Do not weaken/remove an invariant, choose a different checkpoint, change
production, add tolerances, change timing/input/focus/selector behavior, touch
another held file, or alter SDK/runtime/Bridge/packaging. Recreate one fresh
detached `c38b261` tree with exactly seven held files and full path/SHA-256
parity. Run the focused WidgetSwitch gate exactly once through an observable
owner that records a reliable numeric child exit and inspects exact child plus
durable streams every 15–20 seconds. Stop at the numeric result. Do not run
selector/replay/full native, commit, integrate, rebuild/relaunch PID 144396,
change product/package state, remove prior evidence, assign DLV-284, or push.
Red stops for reviewer classification; green returns to commit planning.

Disposition: the aggregate vector classified all six invariants in one run.
Checkpoint geometry was physical at DPI 120 (`5120x1440`; window
`1822,591,1475,581`), while the test's live Win32 reads were DPI-virtualized to
96-DPI coordinates (`4096x1152`; window `1458,473,1180,465`). Every dimension
and coordinate differs by the exact 1.25 DPI scale. The production host and
other real-HWND tests opt into Per-Monitor-V2 awareness; `WidgetSwitchHostTests`
does not. This is a test-process DPI-awareness defect, not stale host authority
or a product geometry defect. The run also failed to exercise log retention
because its owner omitted the diagnostics environment, and its wrapper exit
disagreed with the test stream. Evidence is retained under
`%TEMP%\wrail-dlv439-widget-switch-monitor-20260822-034300`.

## Assigned platform test DPI awareness — DLV-440

Test-only; change only held `WidgetSwitchHostTests.cpp`. At process entry,
request `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` before COM or any HWND/
monitor query, following the established real-host test pattern: accept only
success or `ERROR_ACCESS_DENIED`; otherwise emit the exact Win32 failure and
return nonzero. Do not convert coordinates manually, add tolerances, alter the
six invariants/classifier, change production, timing/input/focus/selector
behavior, touch another held file, or alter SDK/runtime/Bridge/packaging.

Recreate one fresh detached `c38b261` tree with exactly seven held files and
full path/SHA-256 parity. Run the focused WidgetSwitch gate exactly once with
`WRAIL_WIDGET_SWITCH_DIAGNOSTICS_DIR` set to the durable evidence directory so
any test failure retains its overlay log. Use an observable owner that inspects
the exact child and durable streams every 15–20 seconds. Record both the child
OS exit and the test stream's explicit terminal result; do not let wrapper-exit
disagreement override a test failure. Stop at the numeric result. Do not run
selector/replay/full native, commit, integrate, rebuild/relaunch PID 144396,
change product/package state, remove evidence, assign DLV-284, or push. Red
stops for classification; green returns to reviewer commit planning.

Disposition: the focused run passed the corrected checkpoint contract and
stopped at the first retained-source sequence check. The retained log proves
Audio sequence 5 was captured during earlier preflight, then sequence 6 was
admitted current and checkpointed before first Right. Exact equality to the old
sequence 5 therefore freezes a live source across the test's own asynchronous
preflight. This is a test input-boundary authority defect, not a sequence
regression or product failure. Evidence and retained log are under
`%TEMP%\wrail-dlv441-monitor-20260822-035300`.

## Assigned platform switch-boundary sequence authority — DLV-442

Test-only; change only held `WidgetSwitchHostTests.cpp`. After all first-switch
checkpoint/fallback preflight and immediately before sending Right, capture the
latest admitted/current Audio paint as the first-switch input-boundary
authority and its positive sequence. Set the first-switch log boundary from the
same read. A retained Audio source paint after that boundary must carry a
positive sequence no lower than the captured input-boundary sequence and must
be backed by an admitted/current Audio paint no later than that retained
record. Apply the same non-regressing current-source rule to every retained
source paint before destination admission. Preserve widget/rendered identity,
inert semantics, tray input ownership, destination selection/focus, and all
other assertions.

Do not remove sequence validation, accept a stale/regressed sequence, alter
production, add timing/tolerance, change checkpoint selection/PMv2/focus/input,
touch another held file, or alter SDK/runtime/Bridge/packaging. Recreate one
fresh detached `c38b261` tree with exact seven-file path/SHA-256 parity and
diagnostics retention enabled. Run the focused WidgetSwitch gate exactly once
with exact child and durable-stream inspection every 15–20 seconds. Do not
return while the owned child is still running. The explicit test terminal
result is authoritative over a wrapper/OS discrepancy. Stop at the result. Do
not run selector/replay/full native, commit, integrate, rebuild/relaunch PID
144396, change product/package state, remove evidence, assign DLV-284, or push.
Red stops for classification; green returns to reviewer commit planning.

Disposition: PMv2 removed the DPI virtualization mismatch. Checkpoint and live
work are both `5120x1440`; DPI, recorded/live content window, and recorded/live
client-screen geometry match exactly. The only remaining red comparison was
computed target equals recorded content HWND. That comparison contradicts the
accepted DLV-428/429 checkpoint contract: the diagnostic checkpoint proves a
valid computed target and exact recorded/live HWND authority but is explicitly
not a placement transaction. Evidence and the retained overlay log are under
`%TEMP%\wrail-dlv440-monitor-20260822-034900`.

## Assigned platform checkpoint-contract correction — DLV-441

Test-only; change only held `WidgetSwitchHostTests.cpp`. Remove only the
`target-matches-recorded-window` equality from the fallback checkpoint failure
set and classifier. Retain the accepted contract unchanged: target must be
positive and fully within recorded work; work and DPI must equal immediate live
authority; recorded window and client-screen must equal immediate live window
and client-screen; identity and exact sequence remain strict. Keep target,
recorded geometry, and all surviving booleans in failure diagnostics. Do not
treat the checkpoint as a placement transaction.

Do not alter production, add tolerances, change checkpoint selection, timing,
input/focus behavior, PMv2 setup, another held file, or SDK/runtime/Bridge/
packaging. Recreate one fresh detached `c38b261` tree with exact seven-file
path/SHA-256 parity and diagnostics retention enabled. Run the focused
WidgetSwitch gate exactly once with exact child and durable-stream inspection
every 15–20 seconds. Record the explicit test terminal result as authoritative
if the wrapper/OS exit disagrees. Stop at the numeric result. Do not run
selector/replay/full native, commit, integrate, rebuild/relaunch PID 144396,
change product/package state, remove evidence, assign DLV-284, or push. Red
stops for classification; green returns to reviewer commit planning.

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

Status: queued, not assigned. It becomes assignable only after DLV-442, the
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

1. DLV-442 one input-boundary sequence-authority correction and observable focused gate.
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
| DLV-327–421 | Native fixture/build evidence remains held behind DLV-442 and exact Tier 3. |
| DLV-427 | `e26b92b` and `16050bb` are unbuilt/unaccepted ancestry-bound evidence only. |
| DLV-428 | Accepted/integrated as `c38b261`; PID 144396 already runs it. |
| DLV-284 | Queued until cumulative review/integration. |
| DLV-248 | Deferred until explicit user promotion. |

## Recent dispositions

| Milestone | Disposition |
| --- | --- |
| DLV-427 | Correct semantics on rejected ancestry; hash mismatch prevented build/integration. |
| DLV-428 | Integrated-base `dae5e5b` accepted/integrated as `c38b261`. |
| DLV-429 | Startup checkpoint was valid; live-transition selector wrongly chose a later non-placement checkpoint. |
| DLV-430 | Phase filtering passed; live fallback legitimately retained HWND without a new placement transaction. |
| DLV-431 | First post-marker checkpoint had older sequence authority; selection must require exact-current authority. |
| DLV-432 | No exact checkpoint was immediately visible; its stale owner hid a two-minute red result for 18 minutes. |
| DLV-433 | Fast seam green; retained real-host log disproved its zero-candidate polling summary. |
| DLV-434 | Replay isolated terminal CRLF parsing: two checkpoints, zero current candidates. |
| DLV-435 | Seven table cases and exact retained-log replay green in 7 seconds each. |
| DLV-436 | One observable run red after 140.110 seconds; no post-Back fallback repaint or diagnostic. |
| DLV-437 | Back semantics passed; later raw CRLF checkpoint reparse failed after 120.139 seconds. |
| DLV-438 | CRLF sequence passed; later recorded-work/live-monitor mismatch failed after 120.176 seconds. |
| DLV-439 | Exact 1.25 coordinate ratio proved missing test-process PMv2 DPI awareness. |
| DLV-440 | PMv2 fixed all recorded/live geometry; only an invalid target=window check remained. |
| DLV-441 | Checkpoint contract passed; old preflight sequence 5 lost current authority to 6 before Right. |
| DLV-442 | Assigned exact input-boundary/non-regressing retained-source sequence proof. |
