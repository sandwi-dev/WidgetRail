# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-371 assignment is preserved in the
[2026-08-21 20:03 snapshot](history/delivery-plan/2026-08-21T20-03-38-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole authority for current work.

## Current baseline and accepted candidate

- Accepted production/test integration baseline on local `main` remains
  `c21ad02`. Later commits on `main` are reviewer-owned control-plane
  updates plus accepted DLV-340 build tooling `d11e9ae`; the cumulative
  production/test chain below remains unintegrated.
- The user physically accepted cumulative DLV-278/279/270/280/281/282/283 and
  the later DLV-296, DLV-314, and DLV-318 production corrections. The current
  accepted production commit is
  `32a2a5ed3f31ad95156d3ab61fe36f2d791449e2`.
- Responsive PID 126208 visibly runs the exact DLV-318 Release from
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`.
  Its `OverlayHost.exe` SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
  Spotify 0.3.14 is installed, selected, and enabled. Preserve its package,
  Client ID, credentials, account/configuration/provider state, and every other
  installed package/configuration surface.
- The cumulative test chain is clean through exact commit
  `676cd76f6761ca35b49b9810a0a8f0b42e9fabf4`: DLV-319 `199a81b`,
  DLV-324 `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`.
- Every managed Tier-3 gate is green at `676cd76`: WidgetSdk 89/89,
  compatibility 12/12, scenarios 9/9, ticker 5/5, Runtime 84/84,
  presentation sessions 11/11, worker host 10/10, Windows Spotify provider
  32/32, Spotify 54/54, Bridge 96/96, and first-party conformance 6/6.
- Exact DLV-283 platform production is `cdbb04a`. The cumulative accepted
  production/test chain remains unintegrated until the native and final Tier-3
  gates below pass and receive independent review.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Assigned DLV-372 compile correction for the held DLV-371 test-only oracle. Preserve the five held diffs and neutral rejected/restoration commits. DLV-284 remains queued and unassigned. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle and clean at `676cd76`. Preserve PID 126208 and all product state; do not begin work, integrate, rebuild/relaunch, or push. |

## Execution and architecture rules

- Local `main` is reviewer-owned. Review and integrate only accepted DLV
  commits. Implementation tasks never edit reviewer-owned documents and never
  push; the planner never authors implementation or test code.
- Follow physical-first order: coherent production/build, user verdict, then
  focused tests. Do not integrate production-only work before verdict and
  post-verdict evidence.
- Launch a coherent native Release only when accepted integrated production or
  runtime artifact inputs change. Retain PID 126208 for test-only, build-tool,
  or reviewer-document deltas that already contain its production commit.
- Use affected Tier 1 once, the smallest linked Tier 2 only for a changed
  boundary, and Tier 3 only when explicitly assigned. Stop at the first red;
  classify before changing or rerunning.
- Avalonia/AVP is closed failed-experiment history. Do not resume, message,
  launch, integrate, delete, or otherwise touch it without a new user decision.
- The native host owns HWND, graphics, semantic admission, layout, clipping,
  focus, input, accessibility, and scroll offset. Widgets own private data and
  item materialization. Do not add package-specific behavior to core layers.
- Input, focus, free-scroll, and re-entry bind to one fully admitted interaction
  snapshot. Stale, cancelled, or failed work cannot mutate current authority.
- Retain the last valid presentation on failure. Never clear/reload widget
  state, forge collection generations, weaken bounds/admission, or restart a
  healthy worker to conceal divergence.
- Preserve user-owned changes and unrelated dirty state. Never push.

## Current held platform state and evidence

The platform branch was advanced to `676cd76` for native evidence. It retains
two rejected/restoration commits in history:

- `0494b69` DLV-363 removed a live-region name guard. Review proved it was a
  no-op for the failure and overbroad for empty announcements. Reject it.
- `ad109f8` DLV-365 restores that guard exactly. It only neutralizes the
  rejected change. Do not integrate either commit.

The worktree has five authorized held diffs:

1. `src/OverlayHost/build.ps1` — DLV-349 fixes exit authority after serialized
   managed publication and bounded process completion. Its intended suites have
   passed; it remains uncommitted until the complete native gate is green.
2. `src/OverlayHost/RealHostAccessibilityTests.cpp` — cumulative DLV-327–330
   fixture alignment; 291 checks green.
3. `src/OverlayHost/WidgetActionFeedbackTests.cpp` — deterministic
   replacement-deadline coverage; 314 checks green in the native gate.
4. `src/OverlayHost/WidgetActionFailureHostTests.cpp` — coherent trusted
   Settings catalog, exact tray invocation, current typed failure, dual-root
   accessibility subscription, selected-not-focused chrome authority, and
   invocation-anchored feedback lifetime. Focused and complete-gate routes are
   green.
5. `src/OverlayHost/ColdDashboardHostTests.cpp` — current Settings identity,
   two-HWND root ownership, strict descendant geometry, exact tray invocation,
   and focus-handoff checks. DLV-369 activation checks are green; the later
   re-show geometry oracle is the only current red.

Disposable production diagnostics from DLV-336/360/361 are restored exactly to
HEAD. DLV-340 centralized serialized managed publication is accepted and
integrated on main as `d11e9ae`. DLV-349 proved the build script's corrected
exit authority: `OverlayProcessOwnerTests` continued after 32 checks and the
intended subsequent suites executed.

DLV-370 classified the current red as stale fixture geometry authority, not a
production focus-restoration clipping defect. The test retains the first
re-show `visible-content-bounds`, then resolves a later focused semantic
descendant after retained refresh and UIA republication. Both rectangles are
screen-space, but they belong to different publication revisions. Production
clips semantic geometry before publishing UIA bounds.

## Assigned platform test correction — DLV-371 current re-show focus geometry

Mode: bounded native test-only correction after DLV-370.

Owner/baseline: platform lane with the five existing held diffs. Own only
`src/OverlayHost/ColdDashboardHostTests.cpp`; preserve `build.ps1`,
`RealHostAccessibilityTests.cpp`, `WidgetActionFeedbackTests.cpp`, and
`WidgetActionFailureHostTests.cpp` unchanged. Do not alter any production file.

Keep the first-visible/bottom-anchor composition record unchanged as historical
placement evidence. Replace only the later re-show focus containment oracle so
one current authority sample:

- resolves the current content HWND UIA root;
- resolves the exact `widget:category.appearance` Settings element;
- requires that exact element to own keyboard focus;
- obtains current root and focused-element UIA bounds;
- obtains the current content HWND client rectangle through `GetClientRect`
  and `ClientToScreen`; and
- requires the exact focused descendant strictly inside both the current UIA
  root and current client rectangle.

Give root missing, exact action missing, focus not restored, UIA bounds
unavailable, client conversion failure, outside-current-root, and outside-
current-client failures distinct diagnostics. Do not require the provider root
itself to fit inside the HWND client, compare with the cached composition
rectangle, accept any focused descendant, weaken containment to intersection or
tolerance, increase timeouts, or change hide/re-show, focus restoration,
placement, rendering, or accessibility production behavior.

Verification order, once each, stopping at the first red:

1. With external execution approval run
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
   .\src\OverlayHost\build.ps1 -Configuration Release
   -ColdDashboardTestsOnly`.
2. If green, run the complete serialized Release native gate once through the
   same external PowerShell route.
3. If both are green, commit exactly `src/OverlayHost/build.ps1` with a
   DLV-349 subject.
4. Then commit exactly `RealHostAccessibilityTests.cpp`,
   `WidgetActionFeedbackTests.cpp`, `WidgetActionFailureHostTests.cpp`, and
   `ColdDashboardHostTests.cpp` with a DLV-371 subject.
5. Do not run Tier 3 yet. Stop for independent reviewer inspection.

Do not integrate, start DLV-284, launch/terminate, or push. Preserve PID 126208,
Spotify 0.3.14, all installed/configured state, and rejected/restoration history.

DLV-371 stopped before test execution. The focused build found one malformed
conditional in the new test helper: the success path writes
`Require(WaitUntil(...)) { return; }`, invoking the repository's two-argument
`Require` helper with one argument and producing a later syntax-error cascade.
No production or product behavior was exercised.

## Assigned platform test correction — DLV-372 compile DLV-371 oracle

Own only `src/OverlayHost/ColdDashboardHostTests.cpp`. Replace only the
malformed success-path `Require(WaitUntil(...))` wrapper in
`VerifyReshownSettingsFocus` with the intended ordinary conditional: if the
bounded current-authority wait succeeds, return; otherwise fall through to the
existing predicate-specific `Require` diagnostics. Do not change the lambda,
timeout, assertions, failure text, includes, another held file, or production.

With external execution approval, run `ColdDashboardTestsOnly` exactly once.
Stop first red. If green, run the complete serialized Release native gate once.
If both are green, commit exactly `build.ps1` with a DLV-349 subject, then
commit exactly the four held test files with a DLV-372 subject. Do not run Tier
3, integrate, start DLV-284, launch/terminate, or push. Preserve PID 126208 and
all installed/configured state.

## Reviewer disposition after DLV-372

If either authorized native route is red, retain all five diffs uncommitted and
assign a source-first classification of that first red. Do not rerun unchanged.

If both routes are green:

1. Independently review the exact DLV-349 and DLV-372 commits and their full
   diffs. Reject any extra file, production change, weakened assertion,
   timeout/tolerance change, debug artifact, or unrelated cleanup.
2. Assign one exact canonical Tier-3 run from a clean detached tree containing
   those commits and the cumulative production/test chain.
3. If Tier 3 is green, integrate only explicit accepted hashes. Never integrate
   `0494b69` or `ad109f8` merely because they are branch ancestors.
4. Because DLV-349/DLV-372 are build-tool and test-only deltas, do not rebuild
   or relaunch PID 126208 solely for their integration.
5. Rebaseline both lanes, then assign DLV-284 before any new virtualization
   feature.

## Queued platform production — DLV-284 explicit publication transaction model

Status: queued, not assigned. It becomes assignable only after DLV-372 and the
cumulative evidence pass, the accepted production/test chain is independently
reviewed and integrated, and the accepted main Release is coherently refreshed
only if its runtime inputs changed. No new virtualization feature may precede
it.

Replace publication semantics inferred from `allowUpdate`, base-zero/nonzero,
and recovery-side conditions with one private typed transaction model carried
through SDK/runtime, bridge, and host boundaries. Distinguish at least
`IncrementalUpdate`, `OrdinaryCheckpoint`, and `RecoveryCheckpoint`, with
exact legal base, origin authority, retry policy, and admission result. Preserve
compatibility deliberately; stop for any required public wire or third-party
SDK break.

Keep one final transaction owner through admission and commit. Express legal
combinations in one table-driven policy over retained host sequence,
bridge/request base, widget/lifecycle/runtime/presentation authority,
publication intent, collection generation, virtual-window marker, and outcome.
Transport layers may validate facts but must not independently infer or mutate
publication intent.

This is bounded architectural hardening, not a framework rewrite. Remove old
boolean inference only when the explicit type replaces it; do not add a second
state machine or package special case. Preserve strict ordinary validation,
bounded windows, private widget data, last-valid presentation, and host-owned
focus/input/render authority. Include focused table/interleaving evidence for
every legal and illegal transition and follow physical-first order. Never push.

## Future architecture queue — maturity review additions

Status: ordered future work, not assigned. These deliverables do not displace
DLV-372 integration or DLV-284. Allocate implementation IDs only when each
bounded milestone becomes assignable.

1. Generic Game Launcher cutover. Remove the package's advanced-presentation
   declaration and slot projection. Render every accepted launcher layout
   through ordinary `ViewSnapshot`, responsive grid/scroll/navigation,
   semantic tiles, virtual windows, bounded artwork, WRSS, and controller focus
   while retaining the generic `WidgetApplicationRuntime`. Keep layout choices
   package-owned and provide no native LauncherExperience fallback. Obtain a
   physical verdict before deleting the dormant framework slice.
2. LauncherExperience vertical-slice deletion. Deliberately remove the catalog,
   public advanced-presentation protocol/SDK models, Bridge selection/catalog
   routes, native adapter/layout/projection/state, Settings and CLI flows,
   project references, fixtures, compatibility baselines, and active docs. Do
   not add a generic custom-presentation escape hatch or compatibility layer.
3. Targeted LauncherExperience state retirement and proof. Delete only obsolete
   experience selection/last-good/package-catalog state while preserving themes,
   widget order, package configuration, credentials, and Game Launcher-owned
   state. Completion requires no active `LauncherExperience` or
   `AdvancedPresentation` production references under `src/`, a generic
   full-trust Game Launcher package, no launcher-specific host knowledge,
   physical acceptance, and post-verdict focused evidence.
4. Model-level protocol-version requirements. Replace WidgetSdk's overlapping
   feature walkers with one authoritative calculator over the final snapshot
   model. SDK snapshot creation and raw protocol validation must share it; every
   gated node/property needs exact coverage. Complete this before any new
   protocol feature.
5. SDK stability and evolution contract. Classify stable versus experimental
   APIs, define supported-version and deprecation/removal policy, connect those
   rules to API-baseline enforcement, and publish bounded migration guidance.
6. Stable diagnostic contract. Define safe structured diagnostic codes and
   owning boundaries across package validation, SDK, runtime, Bridge, host,
   CLI, and preview tooling without coupling stable codes to mutable prose.
7. Localization and accessibility semantics. Add resource/fallback/plural and
   locale-formatting contracts plus distinct accessible label, description,
   hint, and live-announcement intent that the host maps to UI Automation.
8. Author diagnostics and preview inspection. Expose semantic tree, focus node,
   input scope, computed bounds/clipping/overflow/scroll state, protocol
   diagnostics, widget health, and deterministic controller record/replay.
9. Public-source pre-alpha readiness. Add license and third-party asset review,
   working-tree/history secret audit, honest README/release boundary,
   security/contribution/support policy, compact architecture front door,
   reproducible clean-machine build, and public CI/security automation. Keep
   source preview, binary release, and third-party marketplace gates distinct.

Do not schedule a generic forms framework, component-count expansion, broad
OverlayApp refactor, mediated import/export, background scheduling, or
marketplace/publisher infrastructure without separate evidence and explicit
promotion. Extract native authorities only when real work touches them; promote
import/export or scheduling only after independent widgets prove the need.

## Ordered queues

1. Platform evidence queue: execute DLV-372 and stop at the first red result.
2. Reviewer integration queue: review DLV-349/DLV-372, then assign exact clean
   Tier 3 if the native routes are green.
3. Platform production queue: assign DLV-284 only after clean cumulative
   integration and before any new virtualization feature.
4. Future architecture queue: generic Game Launcher cutover, deliberate
   LauncherExperience deletion/state retirement, model-level protocol
   requirements, then the remaining maturity-review deliverables.
5. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Mapping is approved/frozen; Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Production is physically accepted; cumulative integration awaits DLV-372, exact-commit Tier 3, and reviewer disposition. |
| DLV-296 | Production `3922b58` and notification evidence DLV-306 `5fe7a5f` are accepted within the cumulative chain. |
| DLV-314 | Production `992b77b` physically accepted with Spotify 0.3.13. |
| DLV-318 | Current accepted production `32a2a5e`; PID 126208 runs Spotify 0.3.14. |
| DLV-319–326 | Accepted managed test chain through `676cd76`; all managed Tier-3 gates green. |
| DLV-327–372 | Native cumulative fixture/build evidence remains held pending DLV-372 and exact-commit Tier 3. |
| DLV-284 | Queued, not assigned until cumulative review/integration. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Recent acceptance and disposition record

| Milestone | Disposition |
| --- | --- |
| DLV-318 | Production `32a2a5e` physically accepted; current running PID 126208. |
| DLV-326 | Test-only `676cd76` accepted; all managed Tier-3 gates green. |
| DLV-340 | Serialized managed-publish helper accepted and integrated as main `d11e9ae`. |
| DLV-363 | Production `0494b69` rejected before launch as no-op/overbroad. |
| DLV-365 | `ad109f8` restored the rejected change; neither commit is integrable. |
| DLV-367 | Action-failure focused/full evidence green; Cold Dashboard activation oracle red. |
| DLV-369 | Exact Settings invocation/focus handoff green; later re-show geometry oracle red. |
| DLV-370 | Cross-publication fixture geometry drift classified; production clipping remains sound. |
| DLV-371 | Same-sample Settings focus/root/client correction authored; focused build exposed malformed test conditional before execution. |
| DLV-372 | Assigned one-token conditional compile correction and resumed gates. |
