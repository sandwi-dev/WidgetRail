# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-394 assignment is preserved in the
[2026-08-21 22:07 snapshot](history/delivery-plan/2026-08-21T22-07-26-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole authority for current work.

## Current baseline and accepted candidate

- Accepted production/test integration baseline on local `main` remains
  `c21ad02`. Later main commits are reviewer-owned control-plane updates plus
  accepted DLV-340 build tooling `d11e9ae`; the cumulative production/test
  chain remains unintegrated.
- Current physically accepted production is DLV-318
  `32a2a5ed3f31ad95156d3ab61fe36f2d791449e2`.
- PID 126208 visibly runs its exact Release from
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`;
  executable SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
  Spotify 0.3.14 is installed, selected, and enabled. Preserve every package,
  credential, account, configuration, and provider state.
- Managed tests are accepted through exact `676cd76`: DLV-319 `199a81b`,
  DLV-324 `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`. All named
  managed Tier-3 gates are green.
- Exact DLV-283 platform production is `cdbb04a`. The cumulative chain awaits
  the native gate, independent review, and one exact clean Tier-3 run.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Assigned DLV-402 source/output diagnosis of non-paint Back tray authority. Preserve all seven held diffs and neutral rejected/restoration commits. DLV-284 remains queued and unassigned. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle and clean at `676cd76`. Preserve PID 126208 and product state; do not begin work, integrate, rebuild/relaunch, or push. |

## Execution rules

- Local `main` is reviewer-owned. Review and integrate only accepted DLV
  commits. Implementation tasks never edit reviewer-owned documents or push;
  the planner never authors implementation or test code.
- Follow physical-first order: coherent production/build, user verdict, then
  focused tests. Do not integrate production-only work before verdict and
  post-verdict evidence.
- Relaunch only when accepted integrated production/runtime artifact inputs
  change. Retain PID 126208 for test-only, build-tool, or reviewer-doc deltas.
- Use affected Tier 1 once, the smallest linked Tier 2 only for a changed
  boundary, and Tier 3 only when assigned. Stop first red; classify before edit
  or rerun.
- Avalonia/AVP is closed failed-experiment history. Do not resume, message,
  launch, integrate, delete, or otherwise touch it.
- Preserve last-valid presentation, strict bounded admission, current
  interaction authority, user-owned changes, and all installed/configured
  state. Never push.

## Held platform state

The branch is based on `676cd76` and contains neutral rejected/restoration
commits `0494b69` and `ad109f8`; never integrate either. Seven authorized
diffs remain uncommitted:

1. `src/OverlayHost/build.ps1` — DLV-349 exit authority correction.
2. `src/OverlayHost/RealHostAccessibilityTests.cpp` — DLV-327–330 fixture
   alignment; 291 checks green.
3. `src/OverlayHost/WidgetActionFeedbackTests.cpp` — deterministic replacement
   deadline; 314 checks green in the gate.
4. `src/OverlayHost/WidgetActionFailureHostTests.cpp` — current Settings,
   tray, accessibility, feedback, and reopen authority; focused route green.
5. `src/OverlayHost/ColdDashboardHostTests.cpp` — current two-HWND Settings
   activation/re-show authority; focused route green.
6. `tests/WidgetSwitchFixture/Program.cs` — DLV-375 named action argument.
7. `src/OverlayHost/WidgetSwitchHostTests.cpp` — cumulative DLV-383–393 current
   extents and switch/stationarity oracle corrections.

The focused WidgetSwitch route now proves: post-catalog current Audio authority;
post-focus current Audio sequence; exact Back-to-tray authority; exact cold
Audio-to-Game-Launcher retained transition/paint; and destination transaction
ordering without demanding a separate retained-frame commit. DLV-393 then
stopped earlier at `Game Launcher expected 980x645, but admitted
desired-extent=1052x878 and presented-extent=1052x878, sequence 1`. Durable
evidence is under
`%TEMP%\wrail-dlv393-widget-switch-20260821-220441`.

## Assigned platform diagnosis — DLV-394 classify Game Launcher extent

Source and retained DLV-393 streams only. Make no edit and run no build, test,
publish, executable, or product/process command. Trace Game Launcher's authored
surface, any active advanced/launcher projection, host extent policy, admitted
desired/presented records, and the DLV-383 fixed expectation. Establish the
exact current extent contract and whether `980x645` omitted active generic or
specialized chrome/reservation authority. Classify stale fixture expectation,
wrong record/revision, or production extent-policy defect; identify the
smallest strict correction. Do not guess constants, weaken equality, edit,
rerun, commit, integrate, start DLV-284, launch/terminate, or push. Preserve
PID 126208, seven diffs, state, and DLV-393 diagnostics.

DLV-394 found the fixture used the wrong authority/revision. Game Launcher is a
generic Wide surface: 1052×878 is its outer fallback target, while 980×645 is
the correct DirectComposition content extent. The failing record was earlier
than the Game Launcher destination placement and still correlated to Audio.
No production defect is proven.

## Assigned platform test correction — DLV-395 correlate destination extent

Own only `WidgetSwitchHostTests.cpp`; preserve the other six held files. Keep
the exact retained Audio transition/sequence proof. Do not apply 980×645 to the
earliest admitted Game Launcher paint. Correlate the exact Game Launcher
destination paint with its own 980×700 placement, positive destination
sequence, later complete placement commit, and composition-child sample, with
no intervening paint or placement. On that correlated paint require exact
`desired-extent=presented-extent=980x645`. If only the 1052×878 fallback record
exists, fail distinctly for missing destination composition authority; never
accept it as content or weaken equality.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red. If green,
run the full native gate once. If both are green, create only the four documented
scoped commits using DLV-395 for this file, then stop before Tier 3. No
production edit, integration, DLV-284, launch/terminate, or push. Preserve PID
126208, state, seven diffs, and diagnostics.

DLV-395 compiled but its focused run stopped before destination correlation at
`Back route did not publish exact captured Audio sequence authority.` Exact
exit was 1; no full gate or commit occurred. Evidence is retained under
`%TEMP%\wrail-dlv395-widget-switch-20260821-221947`.

## Assigned platform diagnosis — DLV-396 classify Back sequence authority

Source and retained DLV-395 streams only. Make no edit or executable/process
run. Trace the captured post-Down Audio sequence through Back, focus-region
transition, same-widget admission/paint, and the switch boundary. Establish
whether Back must preserve the sequence, may legitimately advance it, or the
fixture captured a stale/intervening record; identify the exact current Audio
sequence that the retained Game Launcher paint must carry. Classify stale
cross-input pinning, parser correlation gap, or production defect and give the
smallest strict one-file correction. Do not ignore/regress sequence, accept any
target, edit, rerun, commit, integrate, start DLV-284, launch/terminate, or push.
Preserve PID 126208, seven diffs, state, and DLV-395 diagnostics.

DLV-396 classified stale cross-input pinning. An in-flight same-widget admission
may advance Audio after Down and before Back's tray paint. The authoritative
retained-source sequence is the latest exact admitted Audio tray paint
immediately before Right, not the older post-Down sequence. No production
defect is proven.

## Assigned platform test correction — DLV-397 immediate switch authority

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. After Back,
capture and validate the latest exact admitted Audio tray paint immediately
before Right: exact Audio target/rendered identity, tray input owner, Audio
selection/focus, positive nonregressed sequence, and current eight-item catalog.
Use that boundary sequence for the exact retained Game Launcher paint until
destination admission. Keep DLV-395 destination extent and placement/paint/
commit/sample correlation unchanged. Reject regression, wrong target, or tray
authority change; do not ignore sequence or accept stale records.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red. If green,
run the full native gate once. If both are green, create only the four documented
scoped commits using DLV-397 for this file, then stop before Tier 3. No production
edit, integration, DLV-284, launch/terminate, or push. Preserve PID 126208,
state, seven diffs, and diagnostics.

DLV-399 compiled but stopped before destination correlation at `Back route did
not publish current exact admitted Audio tray authority.` Exact exit was 1; no
full gate or commit occurred. Evidence is under
`%TEMP%\wrail-dlv399-widget-switch-20260821-223306`.

## Assigned platform diagnosis — DLV-400 classify Back tray observability

Source and retained DLV-399 streams only; no edit or executable/process run.
Trace Back's focus/input transition, invalidation/damage, paint-key suppression,
UIA/semantic authority, and the held qualifying-paint parser. Establish whether
Back guarantees a new production paint, whether current tray ownership is
strictly observable through another existing record, and how to bind the exact
retained-source sequence immediately before Right without waiting for an
optional repaint. Classify stale paint requirement, parser gap, or production
failure; give the smallest strict one-file correction. Do not invent events,
ignore sequence, weaken tray identity/focus, edit, rerun, commit, integrate,
start DLV-284, launch/terminate, or push. Preserve PID 126208, seven diffs,
state, and diagnostics.

DLV-400 classified a stale admitted-only paint requirement. Back may expose the
exact Audio visual source as either admitted/current or refresh-retained/inert;
both retain exact tray identity and sequence, while inert semantics cannot act.
No production failure is proven.

## Assigned platform test correction — DLV-401 typed Audio visual authority

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Replace the
admitted-only Back needle with an exact parser accepting only: (1)
`content=admitted semantics=current`, or (2)
`content=refresh-retained semantics=inert`. In both cases require exact Audio
target/rendered identity, tray input owner, Audio selection/semantic focus,
eight-item catalog, and positive nonregressed sequence. Select the latest
qualifying record immediately before Right and bind its exact sequence to every
retained Game Launcher paint until destination admission. Do not authorize
actions from inert semantics or weaken identity/focus/sequence. Keep DLV-399
destination correlation unchanged.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red. If green,
run the full native gate once. If both are green, create only the four documented
commits using DLV-401 for this file, then stop before Tier 3. No production edit,
integration, DLV-284, launch/terminate, or push. Preserve PID 126208, state,
seven diffs, and diagnostics.

DLV-401's exact two-state parser still found no qualifying post-Back widget
paint. Back tray ownership is therefore not reliably observable through a new
widget-paint diagnostic. Exact exit was 1; no full gate or commit occurred.

## Assigned platform diagnosis — DLV-402 find non-paint tray authority

Source and retained DLV-401 streams only; no edit or executable/process run.
Trace every existing state-transition, fixed-chrome paint, semantic/UIA, and
input-owner diagnostic emitted by Back. Identify the strict non-widget-paint
record that proves exact Audio tray selection/focus immediately before Right,
and separately identify how to capture the latest admitted Audio visual
sequence without requiring a new Back paint. Classify missing parser seam or
production observability gap; give the smallest strict one-file correction, or
stop if production diagnostics are genuinely insufficient. Do not invent an
event, conflate tray and visual authority, ignore sequence, edit, rerun, commit,
integrate, start DLV-284, launch/terminate, or push. Preserve PID 126208, seven
diffs, state, and diagnostics.

DLV-397 passed immediate pre-Right Audio authority, then stopped at
`Production transition omitted a committed complete-content surface for Game
Launcher` before the DLV-395 destination transaction helper completed. Exact
exit was 1; no full gate or commit occurred. Evidence is retained under
`%TEMP%\wrail-dlv397-widget-switch-20260821-222810`.

## Assigned platform diagnosis — DLV-398 classify pre-correlation commit check

Source and retained DLV-397 streams only; no edit or executable/process run.
Trace the older complete-content assertion, its record parser/range, and the
DLV-395 placement/paint/commit/sample helper. Establish whether the older check
protects a distinct invariant, consumes the wrong pre-destination range, or is
fully subsumed by the exact correlated helper. Classify stale duplicate,
ordering/correlation gap, or production commit failure and give the smallest
strict one-file correction. Do not remove independent evidence, infer commits,
weaken equality, edit, rerun, commit, integrate, start DLV-284,
launch/terminate, or push. Preserve PID 126208, seven diffs, state, and DLV-397
diagnostics.

DLV-398 found a wrong pre-destination range, not a production failure or
removable duplicate. The exact destination helper proves placement/paint/
commit/sample; the older composition check independently proves commit order,
alpha, timing, and bounded motion but started after the fallback paint.

## Assigned platform test correction — DLV-399 shared transaction boundary

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Make the
DLV-395 exact destination helper return the correlated destination-paint end
position. On the first Game Launcher switch run that helper first, then pass
that exact boundary to `recordComposition` so it validates the same complete
destination transaction. Keep the existing earlier `recordComposition` route
unchanged for later ordinary switches. Preserve all placement/paint/commit/
sample, sequence, alpha, timing, motion, HWND, and tray equality evidence; do
not infer commits or discard an independent invariant.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red. If green,
run the full native gate once. If both are green, create only the four documented
scoped commits using DLV-399 for this file, then stop before Tier 3. No production
edit, integration, DLV-284, launch/terminate, or push. Preserve PID 126208,
state, seven diffs, and diagnostics.

## Assigned platform test correction — DLV-403 split Back authorities

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Replace the
invalid post-Back widget-paint prerequisite with two independent exact
authorities:

1. Immediately before Right, resolve the current `WidgetRail.Chrome` UIA root
   and exact `tray:tray.audio-mixer` element. Require both
   `SelectionItemIsSelected=true` and `HasKeyboardFocus=true`, and identity-pin
   that element across the observation.
2. Retain the latest admitted/current Audio widget paint captured after the
   preceding Down/fence as the visual-sequence authority. Bind every retained
   Game Launcher paint before destination admission to that exact positive,
   nonregressed sequence.

Do not treat UIA as widget visual authority, do not treat the retained paint as
tray focus authority, and do not require Back to publish a new widget paint.
Preserve the DLV-399 destination transaction boundary and all existing exact
identity, catalog, lifecycle, focus, sequence, placement, paint, commit, sample,
alpha, timing, motion, HWND, and tray assertions.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
it before any further correction. If green, run the full native gate once. If
both are green, create only the four documented scoped commits using DLV-403
for this file, then stop before Tier 3. No production edit, integration,
DLV-284, launch/terminate, or push. Preserve PID 126208, state, seven diffs, and
diagnostics.

## Assigned platform test diagnosis — DLV-404 Chrome authority conjunct

Use only production/test source, the uncommitted DLV-403 diff, and retained
DLV-403 diagnostics. Do not edit, build, run, publish, or touch product state.
Split the failed combined predicate into its exact observable conjuncts:
current fixed-chrome HWND resolution, `WidgetRail.Chrome` UIA-root resolution,
exact `tray:tray.audio-mixer` lookup/identity, selected state, and keyboard-focus
state. Determine which facts are guaranteed synchronously after Back and which
require an existing deterministic readiness/fence boundary. Identify the
smallest strict one-file correction that reports each failed conjunct without
weakening selection/focus/identity or substituting timing sleeps. Stop if the
current production/test surface cannot observe the required authority.

No production edit, rerun, integration, DLV-284, launch/terminate, or push.
Preserve PID 126208, state, seven diffs, and retained diagnostics.

## Assigned platform test correction — DLV-405 Chrome publication fence

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Keep the
DLV-403 split authorities, but replace `FenceWindow` as the Chrome UIA-readiness
boundary. After Back, require the next post-boundary fixed-chrome
`Composition child sample`: the focus-region change invalidates the guide paint
key, guide rendering republishes tray accessibility, and this sample follows
the composition commit. Only after that fence resolve and identity-pin the
current Chrome HWND/root and exact `tray:tray.audio-mixer` element.

Report each strict conjunct independently: missing HWND, UIA root failure,
missing/wrong tray identity, `SelectionItemIsSelected=false`,
`HasKeyboardFocus=false`, or root/tray identity drift on immediate re-resolve.
Do not add sleeps, retries without an event boundary, a widget-paint
prerequisite, or weakened selection/focus/identity. Preserve the pre-Back
admitted/current Audio paint as the retained visual-sequence authority and the
DLV-399 destination transaction boundary with all existing exact invariants.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
before another edit or rerun. If green, run the full native gate once. If both
are green, create only the four documented scoped commits using DLV-405 for
this file, then stop before Tier 3. No production edit, integration, DLV-284,
launch/terminate, or push. Preserve PID 126208, state, seven diffs, and retained
diagnostics.

## Assigned platform test correction — DLV-406 realize invalidated Chrome

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Keep the
DLV-405 event fence and split UIA checks. After Back, synchronously realize the
already-invalidated current fixed-chrome window with the narrow existing/native
paint-flush mechanism appropriate to that HWND; do not invalidate unrelated
windows or synthesize application state. Then require the first resulting
post-boundary fixed-chrome `Composition child sample` before resolving the
Chrome UIA root and exact Audio tray element.

Preserve per-conjunct diagnostics, immediate identity pinning, exact selected
and keyboard-focus properties, pre-Back admitted/current Audio visual sequence,
DLV-399 destination correlation, and every existing invariant. Do not add a
sleep, polling-only workaround, widget-paint prerequisite, broad redraw, or
production change.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
before another edit or rerun. If green, run the full native gate once. If both
are green, create only the four documented scoped commits using DLV-406 for
this file, then stop before Tier 3. No integration, DLV-284, launch/terminate,
or push. Preserve PID 126208, state, seven diffs, and retained diagnostics.

## Assigned platform test diagnosis — DLV-407 Chrome paint ownership

Use only production/test source, the uncommitted DLV-406 diff, and retained
DLV-405/406 diagnostics. Do not edit, build, run, publish, or touch product
state. Trace the exact post-Back `InvalidateRect` target through its window
procedure, paint scheduling, guide-layer rendering, accessibility publication,
DirectComposition commit, and emitted diagnostics. Explain why
`UpdateWindow(window)` did not yield the expected fixed-chrome sample: wrong
HWND, no update region, parser mismatch, conditional/no-op render, or another
specific source-proven cause.

Identify the smallest deterministic one-file test correction and exact event
fence. Do not prescribe a broad redraw, sleep, speculative message, widget-paint
dependency, or production change. If the present diagnostics cannot strictly
observe the transaction, state that boundary explicitly. Preserve PID 126208,
state, seven diffs, and retained diagnostics.

## Assigned platform test diagnosis — DLV-408 retain Back transaction evidence

Use only production/test source, the uncommitted DLV-406 diff, and retained
DLV-405/406 artifacts. Do not edit, build, run, publish, or touch product state.
Inventory the existing diagnostics for the isolated Back transaction in exact
order: input/unhandled-root return, state transition/presentation directive,
content-HWND invalidation or paint, guide dirty/redraw, tray-accessibility
publication, composition frame/commit disposition, and child sample. Determine
whether the isolated `overlay.log` already distinguishes the DLV-407 failure
branches and exactly where the test lifecycle deletes it.

If existing records are sufficient, specify the smallest test-only change that
copies the isolated log into the durable run directory before cleanup and
parses the exact transaction. If insufficient, identify the smallest
test-owned observation seam; do not request broad production logging or alter
runtime behavior. No correction or rerun until this classification is recorded.
Preserve PID 126208, state, seven diffs, and retained diagnostics.

## Assigned platform test correction — DLV-409 retain Back observation

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Keep the
DLV-406 `UpdateWindow` attempt and DLV-405 split authorities. Immediately after
Back returns and before the flush, sample `GetUpdateRect` on the current content
HWND without validating, invalidating, or consuming it. Retain that observation
in the exact failure diagnostics.

Before `TemporaryInstallation` cleanup on any failure in this boundary, copy
the isolated `local-app-data/WidgetRail/overlay.log` to the externally supplied
durable diagnostics directory. Report the captured update-region state and
parse only exact post-boundary terminal composition-frame/child-sample records;
keep each UIA conjunct independent. Do not treat absence of a terminal-positive
log as proof of one specific internal branch, weaken assertions, or add product
logging. Preserve every existing sequence, identity, focus, catalog,
composition, geometry, timing, and destination invariant.

Run `WidgetSwitchTestsOnly` once via a durable owner that supplies the retained
diagnostics directory; stop first red and classify before another correction or
rerun. If green, run the full native gate once. If both are green, create only
the four documented scoped commits using DLV-409 for this file, then stop before
Tier 3. No production edit, integration, DLV-284, launch/terminate, or push.
Preserve PID 126208, state, seven diffs, and all retained artifacts.

## Assigned platform test diagnosis — DLV-410 fallback Back authority

Use only production/test source, the uncommitted DLV-409 diff, and the retained
DLV-409 `WidgetSwitchHostTests-back-overlay.log`. Do not edit, build, run,
publish, or touch product state. The isolated route logged
`DirectComposition presentation disabled; using HWND fallback: destination draw
failed`, so classify the exact rendering mode boundary and the correct
post-Back publication/readiness authority in fallback mode.

Trace the retained exact Audio record at sequence 5 (`input-owner=tray`,
`selected=audio-mixer`, `semantic-focus=tray:audio-mixer`) and explain why the
earlier parser did not accept it. Identify the strict mode-aware one-file test
correction: composition evidence may be required only while DirectComposition
is active; fallback must use an existing exact fallback paint/completion and
UIA publication boundary, never absence of composition as success. Preserve the
same identity, focus, sequence, catalog, geometry, timing, and destination
invariants. Stop if no exact fallback completion authority exists.

No correction, rerun, production logging/edit, integration, DLV-284,
launch/terminate, or push. Preserve PID 126208, state, seven diffs, and all
retained artifacts.

## Assigned platform test correction — DLV-411 mode-aware Back readiness

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Determine
the sticky rendering mode from the positive diagnostic before Back; never infer
fallback from absent composition output.

- While DirectComposition remains active, require the exact post-Back
  fixed-chrome composition child sample, then validate the exact Audio tray on
  the identity-pinned `WidgetRail.Chrome` UIA root.
- When the explicit `DirectComposition presentation disabled; using HWND
  fallback` marker is current, require the exact post-Back Audio widget paint
  with positive nonregressed sequence, `semantics=current`,
  `input-owner=tray`, `selected=audio-mixer`, `visual-focus=none`, and
  `semantic-focus=tray:audio-mixer`. Then resolve and identity-pin the content
  HWND UIA root and exact `tray:tray.audio-mixer`, requiring selected and
  keyboard-focus properties there.

Keep `GetUpdateRect` and retained log only as diagnostic evidence; absent update
region or composition is never success. Preserve DLV-399 destination
correlation and every existing identity, sequence, catalog, geometry,
composition-when-active, timing, motion, and focus invariant.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
before another edit or rerun. If green, run the full native gate once. If both
are green, create only the four documented scoped commits using DLV-411 for
this file, then stop before Tier 3. No production edit, integration, DLV-284,
launch/terminate, or push. Preserve PID 126208, state, seven diffs, and retained
artifacts.

## Assigned platform test diagnosis — DLV-412 geometry sequence record

Use only production/test source, the uncommitted DLV-411 diff, and retained
DLV-411 stdout/stderr/result artifacts. Do not edit, build, run, publish, or
touch product state. Identify the exact record that triggered `Production
geometry record omitted sequence=` before the Back/render-mode route. Trace its
producer, lifecycle/transaction semantics, complete field schema, and the
generic parser/range that selected it.

Classify whether this is a stale parser accepting a non-sequenced geometry
record, a truncated/concurrent line, an invalid production diagnostic, or a
wrong transaction boundary. Specify the smallest strict one-file correction
without ignoring malformed authoritative records, fabricating a sequence, or
weakening the later DLV-411 route. No correction or rerun until classified.
Preserve PID 126208, state, seven diffs, and all retained artifacts.

## Assigned platform test diagnosis — DLV-413 fallback destination correlation

Use only production/test source, the uncommitted DLV-411 diff, and retained
DLV-409/411 artifacts. Do not edit, build, run, publish, or touch product state.
Confirm the one-line control-flow correction that requires successful
correlation before parsing `destinationPaint`. Then trace the exact
HWND-fallback equivalent of DLV-399's destination stationarity transaction for
the first Game Launcher switch: destination widget paint/sequence, content-HWND
geometry or work area, fallback `EndDraw`/paint completion, and any exact
post-paint UIA/geometry authority.

Specify the strict mode-aware correlation for DirectComposition and explicit
fallback. Do not silently skip stationarity, treat missing composition as
success, conflate content/window geometry, or invent an `EndDraw` diagnostic.
If fallback lacks an observable completion authority, state the narrow test
observation needed. No correction or rerun until classified. Preserve PID
126208, state, seven diffs, and retained artifacts.

## Assigned platform test correction — DLV-414 mode-aware destination proof

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. First fix
control flow so the full destination correlation is required before
`destinationPaint` is parsed.

Keep the existing exact DirectComposition chain unchanged. Under the positive,
sticky HWND-fallback marker, replace only the unavailable fixed-chrome chain
with: exact fallback `Overlay work-area placement` and content-host bounds →
exact admitted Game Launcher paint with positive destination sequence and
expected extent → current content-HWND window/client geometry matching that
placement → identity-pinned content-root UIA publication with exact selected/
focused tray and destination semantic bounds.

This fallback chain proves destination stationarity and authority; do not claim
it independently proves `EndDraw`, treat missing composition as success, skip
geometry/UIA, or weaken the DirectComposition route. Preserve DLV-411 Back
readiness, rendering-mode proof, and every existing sequence, identity, catalog,
geometry, timing, motion, focus, and composition-when-active invariant.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
before another edit or rerun. If green, run the full native gate once. If both
are green, create only the four documented scoped commits using DLV-414 for
this file, then stop before Tier 3. No production edit, integration, DLV-284,
launch/terminate, or push. Preserve PID 126208, state, seven diffs, and retained
artifacts.

## Assigned platform test correction — DLV-415 fallback sequence scope

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Correct the
DLV-414 compile error at line 1062 by keeping the exact positive destination
sequence in the scope shared by the mode-aware correlation result and its later
assertions. Do not duplicate parsing, shadow authority, fabricate a default, or
alter the DLV-414 DirectComposition/fallback conditions.

Run `WidgetSwitchTestsOnly` once via durable capture; stop first red and classify
before another edit or rerun. If green, run the full native gate once. If both
are green, create only the four documented scoped commits using DLV-415 for
this file, then stop before Tier 3. No production edit, integration, DLV-284,
launch/terminate, or push. Preserve PID 126208, state, seven diffs, and retained
artifacts.

## Assigned platform test correction — DLV-416 complete sequence scope

Own only `WidgetSwitchHostTests.cpp`; preserve six other held files. Inspect the
entire mode-aware destination helper for references to the former branch-local
`sequence`, including failure diagnostics. Replace every out-of-scope use with
the shared typed destination-sequence state when present, or an explicit
`missing` diagnostic before unwrap. Keep one parse and one positive authority;
do not shadow, duplicate, fabricate, or change correlation behavior.

Perform a compile-oriented source search/diff check before the single durable
`WidgetSwitchTestsOnly` run. Stop first red and classify before another edit or
rerun. If green, run the full native gate once. If both are green, create only
the four documented scoped commits using DLV-416 for this file, then stop before
Tier 3. No production edit, integration, DLV-284, launch/terminate, or push.
Preserve PID 126208, state, seven diffs, and retained artifacts.

## Assigned platform test diagnosis — DLV-417 file-hash command resolution

Use only `build.ps1` source, the held DLV-349 diff, the DLV-416 durable owner/
streams, and read-only shell/module metadata. Do not edit, build, run tests,
publish, or touch product state. Trace the exact `Get-FileHash` call, spawned
PowerShell executable/version, `PSModulePath`, command-discovery/autoload state,
and whether the failure is caused by the owner environment, build helper, or
missing system module.

Specify the smallest deterministic correction. Prefer a bounded external-owner
environment correction when the repository helper is sound; change
`build.ps1` only if its command resolution is intrinsically nonportable. Do not
mask hash verification, substitute an unverified file, or rerun until the cause
is recorded. Preserve PID 126208, state, seven diffs, and retained artifacts.

## After the cumulative native gate is green

1. Review exact DLV-349 and the cumulative test commits and full diffs. Reject
   extra files, production changes, weakened assertions, timeout/tolerance
   changes, debug artifacts, or unrelated cleanup.
2. Assign one exact canonical Tier-3 run from a clean detached tree containing
   those commits and the cumulative production/test chain.
3. If Tier 3 is green, integrate only explicit accepted hashes. Never integrate
   `0494b69` or `ad109f8`.
4. These are build-tool/test-only deltas, so do not rebuild or relaunch PID
   126208 solely for integration.
5. Rebaseline both lanes, then assign DLV-284 before new virtualization work.

## Queued platform production — DLV-284 explicit publication transaction model

Status: queued, not assigned. It becomes assignable only after DLV-417 is
dispositioned, the native gate is green, cumulative evidence is reviewed and
integrated, and the accepted main Release is coherently refreshed only if
runtime inputs changed. No new virtualization feature may precede it.

Replace semantics inferred from `allowUpdate`, base zero/nonzero, and
recovery-side conditions with one private typed transaction model through SDK/
runtime, bridge, and host. Distinguish at least `IncrementalUpdate`,
`OrdinaryCheckpoint`, and `RecoveryCheckpoint`, with exact legal base,
origin authority, retry policy, and admission result. Preserve compatibility
intentionally; stop for a required public wire or third-party SDK break.

Keep one final transaction owner through admission and commit. Express legal
combinations in one table-driven policy over host sequence, request base,
widget/lifecycle/runtime/presentation authority, publication intent,
collection generation, virtual-window marker, and outcome. Transport layers
may validate facts but must not independently infer or mutate intent.

This is bounded hardening, not a framework rewrite. Remove old boolean inference
only when replaced; do not add a second state machine or package special case.
Preserve ordinary validation, bounded windows, private widget data, last-valid
presentation, and host-owned focus/input/render authority. Include focused
table/interleaving evidence for every legal/illegal transition and follow
physical-first order. Never push.

## Future architecture queue — maturity review additions

Status: ordered future work, not assigned. It does not displace cumulative
integration or DLV-284.

1. Generic Game Launcher cutover through ordinary `ViewSnapshot`, responsive
   grid/scroll/navigation, semantic tiles, virtual windows, bounded artwork,
   WRSS, controller focus, and generic `WidgetApplicationRuntime`. Obtain a
   physical verdict before deleting the dormant framework slice.
2. Deliberate LauncherExperience vertical-slice deletion: catalog, public
   advanced-presentation models, Bridge routes, native adapter/projection/state,
   Settings/CLI flows, references, fixtures, compatibility baselines, and docs.
   Add no custom-presentation escape hatch or compatibility layer.
3. Targeted LauncherExperience state retirement preserving themes, order,
   package configuration, credentials, and Game Launcher-owned state. Require no
   active `LauncherExperience`/`AdvancedPresentation` production references,
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

Do not schedule generic forms, component-count expansion, broad OverlayApp
refactor, mediated import/export, background scheduling, or marketplace/
publisher infrastructure without separate evidence and explicit promotion.
Extract native authorities only when real work touches them.

## Ordered queues

1. Platform evidence: execute DLV-417 retained/source diagnosis of the
   `Get-FileHash` PowerShell command-resolution boundary.
2. Reviewer integration: review the eventual four commits, then assign exact
   clean Tier 3 if the native routes are green.
3. Platform production: DLV-284 after clean cumulative integration.
4. Future architecture: generic Game Launcher cutover, LauncherExperience
   deletion/state retirement, protocol requirements, then maturity deliverables.
5. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Production accepted; integration awaits native gate, exact-commit Tier 3, and review. |
| DLV-296 | Production `3922b58` and notification evidence DLV-306 `5fe7a5f` are accepted in the chain. |
| DLV-314 | Production `992b77b` accepted with Spotify 0.3.13. |
| DLV-318 | Current accepted production `32a2a5e`; PID 126208 runs Spotify 0.3.14. |
| DLV-319–326 | Managed test chain through `676cd76`; all managed Tier-3 gates green. |
| DLV-327–417 | Native fixture/build evidence held pending DLV-417 and exact-commit Tier 3. |
| DLV-284 | Queued until cumulative review/integration. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Recent acceptance and disposition record

| Milestone | Disposition |
| --- | --- |
| DLV-318 | Production `32a2a5e` physically accepted; PID 126208 running. |
| DLV-326 | Test-only `676cd76` accepted; managed Tier-3 gates green. |
| DLV-340 | Build helper accepted/integrated as `d11e9ae`. |
| DLV-374 | Focused reopen route green. |
| DLV-383 | Current Audio extent passed; exposed tray authority drift. |
| DLV-386 | Catalog removal legitimately repaints without new placement/sample. |
| DLV-388 | Same-widget sequence advance is legitimate current authority. |
| DLV-390 | Back-to-tray authority required before the cold switch. |
| DLV-392 | Retained-paint commit is conditional; destination owns baseline. |
| DLV-393 | Destination correlation reached Game Launcher extent mismatch. |
| DLV-394 | 980×645 is correct content; the fixture captured an earlier 1052×878 fallback record. |
| DLV-395 | Destination correction compiled; Back-route Audio sequence prerequisite was stale or miscorrelated. |
| DLV-396 | Latest admitted Audio tray paint immediately before Right owns retained-source sequence. |
| DLV-397 | Switch-boundary sequence passed; an older complete-content check failed before exact correlation. |
| DLV-398 | Older composition evidence is valid but used the wrong fallback-anchored range. |
| DLV-399 | Shared destination boundary compiled; Back handshake lacked a qualifying new Audio tray paint. |
| DLV-400 | Back visual source may be admitted/current or refresh-retained/inert with exact tray authority. |
| DLV-401 | No qualifying post-Back widget paint exists; paint is not the tray handshake. |
| DLV-402 | Exact non-paint authority exists in the live Chrome UIA tray element; widget paint remains the separate visual-sequence authority. |
| DLV-403 | Split-authority correction reached a combined Chrome UIA predicate red; no product defect is proven because the failed conjunct is unknown. |
| DLV-404 | `WM_NULL` was not a publication fence; the post-Back fixed-chrome composition child sample is the deterministic UIA-publication boundary. |
| DLV-405 | Back invalidated Chrome but input injection did not service `WM_PAINT`, so no composition/UIA publication occurred. |
| DLV-406 | `UpdateWindow` on the content HWND succeeded but still emitted no qualifying fixed-chrome sample; exact paint ownership remains unresolved. |
| DLV-407 | Content HWND and parser were correct, but deleted isolated logs leave Back transition, update region, paint, and commit disposition indistinguishable. |
| DLV-408 | Existing log has only terminal-positive composition records; test cleanup deletes it, while `GetUpdateRect` can non-destructively expose pending invalidation. |
| DLV-409 | Retained log proves exact Audio tray authority at sequence 5, but the isolated route had disabled DirectComposition after destination draw failure, making a composition sample impossible. |
| DLV-410 | In explicit fallback, exact Audio paint plus content-HWND UIA owns readiness; composition sample and Chrome UIA are intentionally unavailable. |
| DLV-411 | Mode-aware correction compiled, but the focused route stopped earlier on a generic geometry record lacking `sequence=`; Back coverage has no verdict. |
| DLV-412 | The test parsed an empty `destinationPaint` before requiring correlation; fallback also makes the fixed-chrome destination transaction unavailable. |
| DLV-413 | Fallback stationarity is explicit marker, work-area placement, exact paint/sequence, live content geometry, and content-root UIA; `EndDraw` completion is not externally recorded. |
| DLV-414 | Mode-aware correlation change stopped at compile error because the exact `sequence` local was out of scope; no test verdict. |
| DLV-415 | Shared typed sequence was added, but one failure diagnostic still referenced the former branch-local name; no test verdict. |
| DLV-416 | Sequence compile correction succeeded; the wrapper then failed before test execution because Windows PowerShell could not resolve `Get-FileHash`. |
| DLV-417 | Assigned exact shell/module/build-helper command-resolution diagnosis. |
