# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-331 is preserved in the
[2026-08-21 17:18 snapshot](history/delivery-plan/2026-08-21T17-18-01-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
historical evidence only; this file is the sole authority for current work.

## Current baseline and accepted candidate

- Accepted production/test integration baseline on local `main` remains
  `c21ad02`. Later commits on `main` are reviewer-owned control-plane updates;
  the cumulative production/test chain below remains unintegrated.
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
  `676cd76f6761ca35b49b9810a0a8f0b42e9fabf4`: DLV-319 `199a81b`, DLV-324
  `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`.
- Every managed Tier-3 gate is green at `676cd76`: WidgetSdk 89/89,
  compatibility 12/12, scenarios 9/9, ticker 5/5, Runtime 84/84,
  presentation sessions 11/11, worker host 10/10, Windows Spotify provider
  32/32, Spotify 54/54, Bridge 96/96, and first-party conformance 6/6.
- Exact DLV-283 platform production is `cdbb04a`; the platform lane was cleanly
  fast-forwarded to `676cd76` for native evidence. The cumulative accepted
  production/test chain remains unintegrated until the native and final Tier-3
  gates below pass and receive independent review.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Assigned DLV-332 below at `676cd76`, preserving two uncommitted test-only files. DLV-284 remains queued and unassigned. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle and clean at `676cd76`. Preserve PID 126208 and all product state; do not begin new work, integrate, rebuild/relaunch, or push. |

## Execution and architecture rules

- Local `main` is reviewer-owned. Review and integrate only accepted DLV
  commits. Implementation tasks never edit reviewer-owned documents and never
  push; the planner never authors implementation or test code.
- Follow physical-first order: coherent production/build, user verdict, then
  focused tests. Do not integrate production-only work before verdict and
  post-verdict evidence.
- Launch a coherent native Release only when accepted integrated production or
  runtime artifact inputs change. Retain PID 126208 for test-only or reviewer-
  document deltas that already contain its production commit.
- Use affected Tier 1 once, the smallest linked Tier 2 only for a changed
  boundary, and Tier 3 only when explicitly assigned.
- Avalonia/AVP is closed failed-experiment history. Do not resume, message,
  launch, integrate, delete, or otherwise touch it without a new user decision.
- The native host owns HWND, graphics, semantic admission, layout, clipping,
  focus, input, accessibility, and scroll offset. Widgets own private data and
  item materialization. Do not add package-specific behavior to core host/SDK
  layers or duplicate focus, scroll, render, cache, or lifecycle ownership.
- Input, focus, free-scroll, and re-entry bind to one fully admitted interaction
  snapshot. Stale, cancelled, or failed work cannot mutate current authority.
- Retain the last valid presentation on failure. Never clear/reload widget
  state, forge collection generations, weaken bounds/admission, or restart a
  healthy worker to conceal divergence.
- Preserve user-owned changes and unrelated dirty state. Never push.

## Accepted cumulative evidence through DLV-326

- DLV-280 established exact retained host base across lifecycle establishment.
- DLV-281 serialized completion admission under current lifecycle and
  presentation authority.
- DLV-282 added typed bounded one-shot stale-base resynchronization.
- DLV-283 added unforgeable recovery provenance, exact retained-origin and
  forward-sequence checks, plus strict all-`Replace` fresh-baseline admission.
- DLV-296 added one runtime-owned, bounded, ordered notification lane; the user
  physically accepted it and focused notification/convergence evidence is
  retained in DLV-306 `5fe7a5f`.
- DLV-314 and DLV-318 corrected generic cursor Error-state action and virtual-
  availability projection. The user physically accepted Spotify 0.3.14 on the
  exact DLV-318 Release.
- DLV-319 through DLV-326 reconciled the held managed fixtures and advanced the
  canonical aggregate to the native gate without changing production.

## Held platform test evidence through DLV-331

The platform worktree at `676cd76` has exactly two uncommitted test files:

1. `src/OverlayHost/RealHostAccessibilityTests.cpp`
   - migrates six removed singular pagination lookups to the current plural
     geometry-owned `FindScrollPaginationActions` contract;
   - renders the List and Grid forward frames at their real trailing boundaries;
   - aligns UI Automation with visible item 5 rather than off-viewport item 2;
   - updates the above-range protocol fixture from supported 18 to invalid 20.
2. `src/OverlayHost/WidgetActionFailureHostTests.cpp`
   - retains bounded child wait/exit and isolated log evidence when startup
     fails to expose a visible HWND.

DLV-330 made `RealHostAccessibilityTests` green at 291 checks. The complete
native gate then reached `WidgetActionFailureHostTests` and failed because its
isolated host produced no visible HWND. DLV-331 ran the targeted route once and
reported the child still active after 15 seconds with an otherwise ordinary
1,444-byte startup log and no logged fatal error.

The user's visible message box supplies the missing fatal evidence:
`OverlayHost failed to initialize. Settings is unavailable in the admitted
widget catalog.` Source review proves this is test-catalog drift. The fixture
copies the coherent runtime, including the real Settings worker and WRSS, but
replaces `widget-catalog.json` with only `ytmusic-fixture` and an empty bundled
list. Production startup correctly requires and opens the trusted built-in
Settings identity before creating the dashboard window. Do not weaken that
production invariant.

## Assigned platform correction — DLV-332 restore trusted Settings fixture

Mode: bounded native test-only correction after DLV-331.

Owner/baseline: platform lane at exact `676cd76`, preserving both current
uncommitted test files. Own only
`src/OverlayHost/WidgetActionFailureHostTests.cpp`; do not alter the retained
`RealHostAccessibilityTests.cpp` diff except to include it in the final coherent
test commit after all gates are green.

Update only the isolated catalog constructed by `TemporaryInstallation` so its
`widgets` array also contains the exact trusted built-in Settings entry already
defined by `src/OverlayHost/widget-catalog.json`:

- widget ID `settings`;
- package ID `widgetrail.firstparty.settings`;
- publisher ID `widgetrail.firstparty`;
- instance `settings.default` and icon `settings`;
- worker `runtime/Settings/SettingsWidget.Worker.exe`;
- style `runtime/Settings/styles/default.wrss`;
- memory request 48 MiB;
- `suspend-when-hidden` residency;
- arguments `--bundled-widget-root`, `..`;
- empty declared capabilities and quick actions.

Keep `ytmusic-fixture` unchanged and retain the DLV-331 bounded startup
diagnostic. Reuse the runtime files already copied by the fixture. Do not copy
or load the ambient user catalog, add a fake Settings worker, loosen trusted
Settings identity checks, change host startup behavior, modify timeouts,
retries, process profile, environment, launch arguments, later assertions, or
any production file. Keep `bundledWidgets` empty; the action-failure scenario
does not exercise bundled widget discovery.

Verification order, once each, stopping at the first red result:

1. Build/run the existing targeted route with
   `src/OverlayHost/build.ps1 -Configuration Release
   -WidgetActionFailureHostTestsOnly`.
2. If green, run one complete serialized Release native gate with
   `src/OverlayHost/build.ps1 -Configuration Release`.
3. If green, inspect and commit exactly
   `RealHostAccessibilityTests.cpp` and `WidgetActionFailureHostTests.cpp` on
   top of `676cd76` with a DLV-332 subject.
4. Run one canonical Tier-3 verifier from a clean detached tree at that exact
   commit. Do not run additional focused suites outside these gates.

Preserve PID 126208, Spotify 0.3.14, installed/configured state, and all
production artifacts. Do not launch or terminate the accepted overlay,
integrate to main, begin DLV-284, edit another file, restore/update dependencies,
or push. If the targeted or complete native route reveals another distinct
fixture or product failure, retain both diffs uncommitted and stop with exact
evidence. If Tier 3 is green, stop for independent reviewer inspection.

## Queued platform production — DLV-284 explicit publication transaction model

Status: queued, not assigned. It becomes assignable only after DLV-332 and the
cumulative evidence pass, the accepted production/test chain is independently
reviewed and integrated, and the accepted main Release is coherently refreshed
only if its runtime inputs changed. No new virtualization feature may precede
it.

Replace publication semantics inferred from `allowUpdate`, base-zero/nonzero,
and recovery-side conditions with one private typed transaction model carried
through SDK/runtime, bridge, and host boundaries. Distinguish at least
`IncrementalUpdate`, `OrdinaryCheckpoint`, and `RecoveryCheckpoint`, with exact
legal base, origin authority, retry policy, and admission result. Preserve
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
   through ordinary `ViewSnapshot`, responsive grid/scroll/navigation, semantic
   tiles, virtual windows, bounded artwork, WRSS, and controller focus while
   retaining the generic `WidgetApplicationRuntime`. Keep layout choices
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
   `ContainsX` feature walkers with one authoritative calculator over the final
   snapshot model. SDK snapshot creation and raw protocol validation must share
   it; every gated node/property needs exact coverage. Complete this before any
   new protocol feature.
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
9. Public-source pre-alpha readiness. Add the license and third-party asset
   review, working-tree/history secret audit, honest README/release boundary,
   security/contribution/support policy, compact architecture front door,
   reproducible clean-machine build, and public CI/security automation. Keep
   source preview, binary release, and third-party marketplace gates distinct.

Do not schedule a generic forms framework, component-count expansion, broad
OverlayApp refactor, mediated import/export, background scheduling, or
marketplace/publisher infrastructure without separate evidence and explicit
promotion. Extract native authorities only when real work touches them; promote
import/export or scheduling only after independent widgets prove the need.

## Ordered queues

1. Platform evidence queue: execute DLV-332 and stop on its first red result or
   for independent review after a green exact-commit Tier 3.
2. Reviewer integration queue: independently review DLV-332 and the cumulative
   accepted production/test chain; integrate only if every required gate passes.
3. Platform production queue: assign DLV-284 after clean integration, before
   any new virtualization feature.
4. Future architecture queue: generic Game Launcher cutover, deliberate
   LauncherExperience deletion/state retirement, model-level protocol
   requirements, then the remaining maturity-review deliverables above.
5. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Mapping is approved/frozen; Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-278–283/270 | Production is physically accepted; cumulative integration awaits DLV-332 plus final exact-commit Tier 3 and reviewer disposition. |
| DLV-296 | Production `3922b58` and notification evidence DLV-306 `5fe7a5f` are accepted within the cumulative chain. |
| DLV-314 | Production `992b77b` was physically accepted with Spotify 0.3.13. |
| DLV-318 | Current accepted production `32a2a5e`; PID 126208 runs Spotify 0.3.14. |
| DLV-319 | Test-only `199a81b`; focused gates green, Tier 3 exposed two Runtime fixture drifts. |
| DLV-324 | Test-only `6b63edf`; Runtime 84/84 green, Tier 3 exposed provider fixture drift. |
| DLV-325 | Test-only `e441f25`; provider 32/32 green, Tier 3 exposed Runtime helper publication race. |
| DLV-326 | Test-only `676cd76`; managed Tier 3 gates green, native compile drift exposed. |
| DLV-327–330 | Uncommitted cumulative RealHost fixture corrections; all 291 RealHost checks now green. |
| DLV-331 | Diagnostic-only host startup evidence retained; user screenshot identified missing trusted Settings catalog entry. |
| DLV-332 | Assigned fixture-only Settings catalog correction and final native/Tier-3 evidence. |
| DLV-284 | Queued, not assigned until cumulative review/integration. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Recent acceptance record

| Milestone | Disposition |
| --- | --- |
| DLV-318 | Production `32a2a5e` physically accepted; current running PID 126208. |
| DLV-319 | Test-only `199a81b` accepted; later Runtime drift blocked Tier 3. |
| DLV-324 | Test-only `6b63edf` accepted; Runtime 84/84. |
| DLV-325 | Test-only `e441f25` accepted; provider 32/32. |
| DLV-326 | Test-only `676cd76` accepted; all managed Tier-3 gates green. |
| DLV-327 | Pagination API migration retained; forward fixture mismatch rejected. |
| DLV-328 | Forward boundaries corrected; stale UIA expectation rejected. |
| DLV-329 | UIA corrected; stale protocol maximum rejected. |
| DLV-330 | RealHost 291 green; action-failure host startup remained unexplained. |
| DLV-331 | Diagnostic retained; screenshot proved missing Settings fixture. |
