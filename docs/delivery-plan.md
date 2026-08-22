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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Assigned DLV-377 one-run durable WidgetSwitch diagnostic. Preserve all six held diffs and neutral rejected/restoration commits. DLV-284 remains queued and unassigned. |
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

DLV-372's exact one-line compile correction made `ColdDashboardTestsOnly`
green. The complete serialized Release gate then reached a different later
fixture and stopped at: `Reopened overlay did not republish its dashboard.` No
commit was created. The assertion currently searches the fixed-chrome root for
the exact fixture tray identity after widget-to-tray Escape, hide, and F1
reopen, while its prose still describes the retired dashboard.

## Assigned platform diagnosis — DLV-373 classify action-failure reopen

Source and retained-artifact inspection only. Make no edit and run no build,
test, publish, or product/process command. Trace the exact
`WidgetActionFailureHostTests` state from the first Escape through tray focus,
the second Escape/hide, F1 reopen, selected widget/runtime retention, chrome
tray publication, content publication, and feedback non-resurrection checks.
Inspect the retained full-gate output and isolated host log/profile when
available. Establish whether the red is:

- stale dashboard wording only but a valid missing exact tray semantic;
- a stale expectation that the prior fixture tray remains published/selected
  after reopen;
- a root/HWND or publication-revision race in the fixture;
- or a production lifecycle/accessibility defect.

Compare with the green current Settings/chrome hide/re-show route and exact
selected-not-focused authority established in DLV-367. Identify the smallest
strict correction and separate diagnostics for overlay visibility, current
chrome root, exact tray identity, selection/focus authority, current content,
and feedback absence. Do not infer success from window visibility alone, accept
any tray item, add tolerance/sleep/timeout, edit, rerun, commit, integrate,
start DLV-284, launch/terminate, or push. Preserve PID 126208 and all state.

DLV-373 did not prove a production defect. The state machine retains YT Music
and `reopenWidget`, so F1 should reopen it with tray focus. The failing fixture
combines current chrome-root resolution and exact YT Music tray lookup, while
its `dashboard` prose is retired. Hide clears feedback and both accessibility
providers; the retained evidence ends before F1 and the failed run's temporary
profile was deleted. The green Settings re-show fixture does not inspect fixed-
chrome UIA after re-show, so it cannot disposition this boundary.

## Assigned platform test correction — DLV-374 separate reopen authorities

Own only `src/OverlayHost/WidgetActionFailureHostTests.cpp`; preserve the other
four held files unchanged. Replace only the combined post-F1 `dashboard`
predicate with one bounded current-authority wait and predicate-specific
diagnostics that require:

- both content and chrome HWNDs visible;
- current content and chrome UIA roots;
- a post-reopen YT Music paint record with `input-owner=tray` and
  `selected=ytmusic-fixture`;
- exact `tray:tray.ytmusic-fixture` present, selected, and keyboard-focused;
- exact current YT Music play/pause content present and not keyboard-focused;
- both dashboard/open feedback status elements absent; and
- the exact original fixture worker PID remains the sole matching descendant.

Keep the existing bounds and exact identities. Give visibility, each root,
paint/state, tray presence, selection, tray focus, content presence, content
focus, each feedback status, and worker retention distinct failure messages.
Do not accept any tray, add sleeps/tolerance/retries, increase timeouts, change
production, or alter earlier event/lifetime checks.

With external execution approval, run `WidgetActionFailureHostTestsOnly`
exactly once. Stop first red. If green, run the complete serialized Release
native gate once. If both are green, commit exactly `build.ps1` with a DLV-349
subject, then commit exactly the four held test files with a DLV-374 subject.
Do not run Tier 3, integrate, start DLV-284, launch/terminate, or push. Preserve
PID 126208 and all installed/configured state. If the focused route proves the
current chrome root and authoritative YT Music paint are present while the
exact tray is absent, stop for a production-boundary diagnosis before any fix.

DLV-374's focused route is green and clears the suspected production reopen/
accessibility boundary. The exact retained YT Music state, both current roots,
tray-input paint, selected/focused exact tray, unfocused play/pause, absent
feedback, and sole original worker PID all passed. The complete gate advanced
to a separate managed compile red at
`tests/WidgetSwitchFixture/Program.cs:134`: a string is supplied where the
current `ButtonElement.Shortcut` overload requires `ControllerEventPhase`.

## Assigned platform test correction — DLV-375 align fixture shortcut call

Own only `tests/WidgetSwitchFixture/Program.cs`; preserve the five held native
files unchanged. On the `Ready` button's X shortcut, change only the obsolete
second positional string to the current named `actionId: "fixture.ready"`
argument. Preserve the default `ControllerEventPhase.Pressed`, exact action ID,
button, focus links, fixture behavior, and every other line. Do not change the
SDK overload or add a compatibility overload for one stale internal fixture.

Run the complete serialized Release native gate exactly once; do not rerun the
already-green focused route. Stop first red. If green, commit exactly
`tests/WidgetSwitchFixture/Program.cs` with a DLV-375 subject, commit exactly
`src/OverlayHost/build.ps1` with a DLV-349 subject, then commit exactly the four
held native test files with a DLV-374 subject. Do not run Tier 3, integrate,
start DLV-284, launch/terminate, or push. Preserve PID 126208 and all state.

DLV-375's exact one-line fixture correction compiled and published. The single
complete gate then stopped during `WidgetSwitchHostTests`; the next
`AudioMixerScrollHostTests.exe` artifact was not rebuilt. Task compaction
expired the original output handle, and a late-attached Windows process object
returned a blank exit-code value, so neither green nor a particular assertion
is proven. No commit was created. Six diffs remain held.

## Assigned platform diagnosis — DLV-376 recover WidgetSwitch stop boundary

Source and retained-artifact inspection only. Make no edit and run no build,
test, publish, executable, or product/process command. Inspect the completed
gate's surviving artifact timestamps, any isolated WidgetSwitch host profile,
logs, completion markers, and the ordered assertions/cleanup paths in
`WidgetSwitchHostTests`. Establish the latest boundary the run proves and
whether a specific red can be recovered without inference. Distinguish:

- fixture startup/publication;
- exact switch input and focus transition;
- blocked snapshot cancellation/release;
- worker/process retention;
- hide/re-show and accessibility checks;
- bounded cleanup/exit; and
- build-wrapper output/exit observability loss.

If one exact failing predicate is recoverable, identify its smallest correction.
If evidence is insufficient, specify the smallest focused diagnostic route that
retains its console/log result durably across task compaction and preserves the
one-run rule. Do not rerun the complete gate, guess the first red from artifact
order, change production, edit, commit, integrate, start DLV-284,
launch/terminate, or push. Preserve PID 126208 and all state.

DLV-376 proved the exact WidgetSwitch red is unrecoverable. The corrected
fixture and `WidgetSwitchHostTests.exe` rebuilt, but the fixture unconditionally
deleted its profile/log/markers during unwinding. The Codex transcript retained
only a vanished cell handle, and the late watcher captured completion without
the child exit. Artifact order cannot distinguish startup, switching, blocked-
snapshot handling, re-show, cleanup, or success.

## Assigned platform diagnostic — DLV-377 durably capture WidgetSwitch result

Make no source edit. Create one unique diagnostics directory under the user's
temporary directory, outside the repository and `out/Release`, and preserve it
for review. Through one waiting external PowerShell owner, run exactly once:
`src/OverlayHost/build.ps1 -Configuration Release -WidgetSwitchTestsOnly`.
Before launch, persist the exact command and working directory. Redirect the
child's complete stdout and stderr to separate files in that directory and,
after the child terminates, persist its exact numeric exit code in a small
manifest/result file. The external waiting owner must remain attached through
completion; do not rely on a late `Get-Process` attachment or an expiring model
cell for the verdict.

After completion, read the retained result/stdout/stderr files and report the
exact first red or green result plus the diagnostics directory. Do not edit the
test to retain its temporary installation yet; its distinct `Require` messages
should make console capture sufficient. Do not run the complete gate, rerun the
focused route, change source, commit, integrate, start DLV-284,
launch/terminate product processes, or push. Preserve PID 126208, all six held
diffs, installed/configured state, and the durable diagnostic files.

## Reviewer disposition after the cumulative native gate is green

If either authorized native route is red, retain all five diffs uncommitted and
assign a source-first classification of that first red. Do not rerun unchanged.

If both routes are green:

1. Independently review the exact DLV-349 and cumulative test commits and their full
   diffs. Reject any extra file, production change, weakened assertion,
   timeout/tolerance change, debug artifact, or unrelated cleanup.
2. Assign one exact canonical Tier-3 run from a clean detached tree containing
   those commits and the cumulative production/test chain.
3. If Tier 3 is green, integrate only explicit accepted hashes. Never integrate
   `0494b69` or `ad109f8` merely because they are branch ancestors.
4. Because these are build-tool and test-only deltas, do not rebuild
   or relaunch PID 126208 solely for their integration.
5. Rebaseline both lanes, then assign DLV-284 before any new virtualization
   feature.

## Queued platform production — DLV-284 explicit publication transaction model

Status: queued, not assigned. It becomes assignable only after DLV-377 is
dispositioned, the cumulative native gate is green, and the
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
cumulative integration or DLV-284. Allocate implementation IDs only when each
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

1. Platform evidence queue: execute DLV-377 once with durable output/exit
   capture and stop at the exact focused result.
2. Reviewer integration queue: review DLV-349 and the eventual cumulative test
   commit, then assign exact clean
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
| DLV-278–283/270 | Production is physically accepted; cumulative integration awaits the green native gate, exact-commit Tier 3, and reviewer disposition. |
| DLV-296 | Production `3922b58` and notification evidence DLV-306 `5fe7a5f` are accepted within the cumulative chain. |
| DLV-314 | Production `992b77b` physically accepted with Spotify 0.3.13. |
| DLV-318 | Current accepted production `32a2a5e`; PID 126208 runs Spotify 0.3.14. |
| DLV-319–326 | Accepted managed test chain through `676cd76`; all managed Tier-3 gates green. |
| DLV-327–377 | Cumulative fixture/build evidence remains held pending DLV-377 result and exact-commit Tier 3. |
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
| DLV-372 | One-line conditional correction; focused route green, full gate exposed action-failure reopen red. |
| DLV-373 | Combined post-F1 oracle classified as insufficient evidence; retained YT Music expectation remains valid. |
| DLV-374 | Focused reopen evidence green; full gate advanced to WidgetSwitchFixture compile drift. |
| DLV-375 | Named-action fixture alignment compiled; full-gate output expired during WidgetSwitchHostTests. |
| DLV-376 | Exact red unrecoverable; all per-run evidence was deleted or never persisted. |
| DLV-377 | Assigned one focused route with durable command/stdout/stderr/exit capture. |
