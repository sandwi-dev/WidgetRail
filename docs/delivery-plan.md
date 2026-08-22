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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Assigned DLV-395 test-only correction to correlate Game Launcher content extent with its destination transaction. Preserve all seven held diffs and neutral rejected/restoration commits. DLV-284 remains queued and unassigned. |
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

Status: queued, not assigned. It becomes assignable only after DLV-395 is
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

1. Platform evidence: execute DLV-395's destination extent correlation and stop
   first red before another diagnosis or rerun.
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
| DLV-327–395 | Native fixture/build evidence held pending DLV-395 and exact-commit Tier 3. |
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
| DLV-395 | Assigned exact Game Launcher destination transaction/extent correlation. |
