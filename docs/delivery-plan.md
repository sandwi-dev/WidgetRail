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
- Unaccepted DLV-286 candidate `d0ca29b` is visibly running from the clean
  platform worktree as responsive OverlayHost PID 80988. Executable SHA-256 is
  `A694909AAD4F8A75464BCAE7BFF665084637AD1E5309C864D1DC802D45AF9FFE`;
  `startup-error.txt` is absent. It is not integrated and awaits the user's
  physical overlay/Settings verdict.
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-286 production `d0ca29b` is source-reviewed and visibly running as unaccepted PID 80988; awaiting the user's physical verdict before integration. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-285 production `ccb46e7` is physically accepted/integrated; Game Launcher tests are explicitly deferred and the lane is idle for the user's future package plans. |

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

## Accepted platform production — DLV-284 typed publication transactions

Lane: platform, acting as the sole serialized cross-process owner. Baseline:
clean integrated production commit `cf77507`. The existing
`codex/impl-platform-responsive-focus` branch retains held ancestry and must not
be rewritten, merged, or used as the assignment baseline. From the clean
platform worktree, create `codex/dlv-284-typed-publication-transactions` at
exact commit `cf77507`; stop if that branch already exists at a different tip
or if the worktree is not clean. Dependencies: user acceptance of coherent PID
78860 is recorded above; DLV-278–283 and DLV-472 are integrated. The retained
unrelated Audio Mixer synthetic-focus first red does not reopen or repeat Tier
3. No new virtualization feature may precede DLV-284.

Objective and ownership boundary: replace publication intent inferred from
`allowUpdate`, base zero/nonzero, and recovery conditions with one explicit,
private typed transaction model from SDK snapshot construction through runtime,
Bridge, native bridge client, coordinator admission, and final host commit. The
platform lane is explicitly authorized to touch the minimum shared SDK/runtime,
Bridge, host, and directly affected deterministic test files needed for this
serialized boundary. It may not change package behavior, widget-specific
presentation, unrelated rendering/focus/input, public feature documentation,
LauncherExperience, or Avalonia/AVP history.

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
table/interleaving evidence for every legal/illegal transition and use the
normal protocol verification order defined below. Never push.

Required acceptance evidence:

1. Publication kind is explicit and typed at its origin and remains attached
   to the same transaction owner until final admission/commit. Compatibility
   adapters may serialize existing fields, but no downstream layer may
   independently reconstruct intent from booleans, zero/nonzero bases, payload
   shape, or failure state.
2. One table-driven policy is the authority for legal/illegal combinations of
   prior host sequence, request base, widget/lifecycle/runtime/presentation
   authority, publication kind, collection generation, virtual-window marker,
   and commit/reject outcome. Transport parsing may validate facts but may not
   own a parallel transition policy.
3. `IncrementalUpdate`, `OrdinaryCheckpoint`, and `RecoveryCheckpoint` have
   exact legal bases, origin authority, retry behavior, and admission results.
   Recovery remains limited to current typed stale-base provenance, a recorded
   origin, a newer complete checkpoint, and an all-`Replace` fresh baseline.
4. Ordinary incremental and checkpoint behavior, bounded virtual windows,
   last-valid presentation, current interaction authority, private widget data,
   and fail-closed rejection remain unchanged. Invalid or stale transactions
   cannot mutate retained presentation, focus/input authority, lifecycle state,
   or collection generation.
5. Deterministic table/interleaving coverage includes bridge-ahead/host-behind,
   switch-away during an in-flight publication, recovery checkpoints, rapid
   cycling, and virtual paging in both directions, plus representative illegal
   combinations. Do not build a new generalized simulator when existing seams
   can express these cases.
6. No public third-party SDK break, public wire break, protocol-version bump,
   package/configuration mutation, compatibility layer, widget/package special
   case, or second state machine is introduced. Stop before implementation if
   a public wire or third-party SDK break is required.

Verification: normal ordering, not physical-first. Run the directly affected
Tier-1 builds and deterministic suites once after the coherent change, then the
smallest existing Tier-2 cross-process group covering SDK/runtime/Bridge/native
admission. Because this changes a core cross-process protocol/security
boundary, commit the scoped DLV-284 result and run Tier 3 exactly once from that
exact clean commit in an isolated worktree. Every command must produce
meaningful output or a terminal result within 60 seconds; use bounded timeouts,
stop at the first red, classify it, and do not rerun an unchanged gate. Preserve
PID 78860 and all package, account, credential, provider, and configuration
state; do not launch or terminate OverlayHost, install packages, push, or touch
the retained Audio Mixer debt.

Concurrency and stop conditions: the widgets lane remains idle until DLV-284
reaches a clean committed boundary. Stop and report before broadening scope if
the typed model requires a public compatibility decision, a protocol-version
bump, a second independent inference/state machine, widget-specific host
knowledge, substantial merge conflict, destructive recovery, or a material
change to the threat model/performance envelope. Stop on the first unrelated
red after preserving exact evidence; do not repair it under DLV-284. Report one
DLV-284 commit, exact changed files, focused/Tier-2/Tier-3 evidence actually
run, numeric exits, retained risks, and a clean worktree. No additional Ready
milestone is safe in either lane until this serialized boundary is reviewed and
integrated; the future architecture queue depends on it.

Initial candidate `21c3b8bcda58036ee488cb7f310a38f34d5cb3e6` required one bounded correction.
Its focused evidence is green: Widget SDK 89/89; the four directly changed
Runtime cases 1/1 each; WidgetBridge 96/96; presentation session 11/11; native
Bridge client/catalog; 28 coordinator scenarios; 314 linked feedback checks;
and the coherent no-launch Release build. The platform worktree and exact
candidate were clean before verification.

The one authorized Tier-3 run from an isolated detached copy of `21c3b8b`
stopped first red after 44/45 passed steps at `overlay-native-build-tests` ->
`WidgetActionFailureHostTests`: `A Retry did not recover with one fresh worker
generation.` The verifier exited 1 after 769.019 seconds; all owned descendants
exited and accepted PID 78860 remained alive and untouched. Structured evidence
is under `artifacts/verification/20260823T102602Z-5d58dade` in the isolated
worktree; durable streams and the retained isolated tree are under
`%TEMP%\wrail-dlv284-tier3-20260823-032456`. The structured-result SHA-256 is
`19A93497D4BA6B8BB94552D774296E5C78785D405015F8E37E0C05543C26DE51`.

This red remains in-scope until disproved: the same linked retry route passed
on the integrated baseline before its later unrelated Audio Mixer red, while
DLV-284 changes the worker/runtime/Bridge/coordinator publication path used by
that retry. The platform lane owns one bounded correction loop. Before a
focused execution it must add durable failure evidence for the isolated overlay
log, fresh fixture descendants, and play-pause UIA state; then run only the
smallest `WidgetActionFailureHostTests` gate. It may make the smallest coherent
production and directly affected deterministic-test correction without
weakening typed transaction, lifecycle, generation, replacement, timeout, or
assertion invariants. Commit one clean follow-up only after focused green. Do
not rerun Tier 3, integrate, push, relaunch PID 78860, repair Audio Mixer debt,
or release the widgets lane before reviewer acceptance.

Correction `86d653262bc73c77f877465234ca2d99d4146966` is accepted. The exact
root cause was the native synchronous presentation parser rejecting the three
new typed wrapper fields (`transactionKind`, `baseSequence`, and
`recoveryOriginSequence`) after a Retry started a fresh worker. The correction
admits exactly those fields for synchronous typed publication responses while
leaving asynchronous event parsing on its existing strict allowlist. It adds a
valid typed-envelope case, preserves unknown-field rejection, and adds bounded
retry diagnostics.

The corrected focused `WidgetActionFailureHostTestsOnly` gate exited 0 in
90.600 seconds and `WidgetBridgeCatalogTestsOnly` exited 0 in 6.57 seconds.
Per the unchanged-gate rule, Tier 3 was not rerun. The platform worktree was
clean. Reviewer inspection accepted both commits and integrated them into
local `main` as `7f31e04`; no push occurred. The integrated Release build
exited 0 and is visibly running as PID 108300 with the hash recorded above.

PID 108300 is subsequently rejected. At 2026-08-23 12:02:41 local time, the
installed Spotify 0.3.14 full-trust application worker PID 146828 started,
failed its first visible lifecycle request with `worker-transport-failed`, and
exited 1 about 100 ms later. YouTube Music and the bundled generic application
worker started successfully in the same Bridge session, isolating the failure
to the frozen Spotify application runtime. Spotify's bounded application log
records `boundary=worker-session code=exit-1` at the matching instant.

The exact compatibility defect is now classified. DLV-284 added
`presentationTransactionKind`, `presentationBaseSequence`, and
`recoveryOriginSequence` to the strict runtime-v2 envelope and sends them to a
previously installed full-trust application. Spotify 0.3.14 embeds the prior
runtime-v2 envelope with `UnmappedMemberHandling.Disallow`, so it rejects those
unknown top-level fields before completing the request. The DLV-284 frozen-peer
test did not prove this boundary because its so-called frozen peer reused the
current `RuntimeEnvelope` type and therefore knew the new fields.

The platform lane owns one bounded compatibility correction. Preserve explicit
typed transaction authority inside current SDK/runtime/Bridge/host code while
making the runtime-v2 wire bidirectionally compatible with a genuinely frozen,
strict pre-DLV-284 application. Add deterministic evidence whose peer schema is
independent of the current `RuntimeEnvelope` type and rejects unknown fields,
covering at least current host to old worker and old host to current worker.
Do not solve this by reinstalling Spotify, weakening strict unknown-field
validation, silently treating every worker as current, adding a package-ID
special case, or removing typed host admission. If safe negotiation or a
bounded compatibility adapter cannot preserve both explicit authority and the
public runtime-v2 contract, stop for reviewer architecture judgment.

Use production/build first. Run only the directly affected Runtime/Bridge
focused compatibility gates after the coherent correction and do not rerun the
unchanged 20-minute Tier 3 route. Every command must emit progress or terminate
within 60 seconds. Commit one clean correction, report exact files and numeric
exits, and do not launch/terminate OverlayHost, install packages, push, touch
Spotify state, release DLV-285, or touch LauncherExperience/Avalonia history.
The reviewer will integrate an accepted correction, rebuild/relaunch, and use
the already installed Spotify 0.3.14 worker for the physical verdict.

Correction `60130b4` is accepted and integrated as `052a392`. It removes the
new transaction fields from the public runtime-v2 wire while retaining typed
transaction and recovery authority in the current host/Bridge path, and maps
the frozen legacy checkpoint request at the bounded worker adapter. Independent
strict frozen-v2 schemas prove current-host-to-old-worker and
old-host-to-current-worker compatibility; the directly affected Runtime gates,
exact-base Bridge convergence, and ordinary full-trust Bridge runtime all
passed. Per the unchanged-gate rule, Tier 3 was not rerun.

Rejected PID 108300 exited through exact-owner `WM_CLOSE`. The coherent
corrected Release build exited 0 and is visibly running as responsive PID
116844 with executable SHA-256
`5202C96E47FEA318B355988A05619D40792D5B1DF1876D9B90FF26FA32AA700E`;
`startup-error.log` is absent and its production Bridge session started. The
next action is only physical exercise of the already installed Spotify 0.3.14
widget and the user's accept/reject verdict. Do not rebuild, relaunch, run Tier
3, change package state, or release DLV-285 before that verdict.

The user rejected PID 116844 after a different failure at 2026-08-23 12:42:35
local time. This run proves the frozen-v2 startup correction itself: installed
Spotify 0.3.14 worker PID 7632 started, remained alive and responsive, rendered
through sequence 40, and completed repeated bidirectional playlist pagination.
After `Spotify handled Y`, sequences 39 and 40 were admitted, then the next
runtime request failed as `worker-runtime-failed`; the host retained sequence
40 as inert failure UI. The worker did not crash, and its later background
lifecycle completed successfully. This is not the earlier unknown-field,
startup, transport, or worker-exit defect.

Preserve OverlayHost PID 116844, worker PID 7632, installed Spotify 0.3.14, and
all account/package/configuration state as evidence. The platform lane must
first recover the exact failing request type and worker `ErrorPayload` detail,
because the current Bridge status collapses it to the generic
`worker-runtime-failed` code. If the root cause is owned by runtime/Bridge
compatibility, make one coherent bounded correction with an independent frozen
worker regression and directly affected gates only. If the evidence proves a
Spotify package/widget defect, stop without package edits and report the exact
widgets-lane handoff. Do not run Tier 3, relaunch or terminate product
processes, alter package state, release DLV-285, or touch LauncherExperience or
Avalonia history.

Candidate `f583f40620ab23f55cad8c0c64cf6eb79233e68d` is rejected. Its evidence
correctly narrows the failed operation to the next `render` request after
sequence 40 and proves that an independent frozen 0.3.14-style worker can
serialize the equivalent three-publication sequence. Its focused production
build and three directly affected correlation/rejection cases passed 1/1 each;
no Tier 3 or broad suite ran.

The implementation is not compatible with the existing runtime exception
contract. Every worker Error response previously completed public
`WidgetProcessClient` operations with `WidgetProcessException`; `f583f40`
instead lets an internal unrelated `WidgetRequestRejectedException` escape.
Existing callers and tests are entitled to catch the public exception type.
The candidate also places `WorkerSafeMessage` in `BridgeWidgetRequestException`
and therefore in the native Bridge error response, but the worker's general
`SafeMessage` path is arbitrary full-trust `Exception.Message`, not a proven
credential/provider-response-safe user diagnostic. Preserve exact bounded
request type and worker error code plus structural protocol path/code where
already sanitized, but keep arbitrary worker detail in developer diagnostics
and retain generic user-facing failure copy. Correct these two issues without
losing pending-request correlation, strict validation, last-good retention, or
the frozen peer evidence. Run only the directly affected focused cases; do not
run Tier 3 or touch live/product/package state.

Correction `ac79ed0c77deb36d5b389a5d728d1f1886327953` repairs both rejection
blockers and is accepted cumulatively with `f583f40` through merge `8ac55d`.
Worker Error responses again preserve the exact public `WidgetProcessException`
type. The correlated request type and worker error code remain internal, while
arbitrary worker text is excluded from the public process exception and
Bridge-visible failure message. The production WidgetBridge Release build and
four directly affected one-case gates passed; no Tier 3 or broad suite ran.

The accepted correlation/redaction foundation plus corrected diagnostic stack
now records only validated widget ID, request type, and worker error code.
Candidate `54167ee` had persisted arbitrary worker text and `f66e082` had
retained forgeable structural text; final correction `a65228c` removes both
message paths while preserving generic public replies and exact correlation.
Its production build and the two direct one-case gates passed; no Tier 3,
package, credential, provider, or live-state change was made. The refreshed
integrated Release is ready for the user to repeat Spotify Y-refresh; its
metadata distinguishes a render/runtime failure without persisting package
content.

## Accepted widgets production — DLV-285 generic Game Launcher cutover

Lane: widgets. Baseline: exact integrated production commit `12728a2` in a new
clean isolated branch. The user physically accepted the final corrected
DLV-284 Release and the reviewer released this assignment. The platform lane
remains idle while DLV-285 is active.

Objective: move the first-party Game Launcher package completely onto the
ordinary declarative application path before any framework deletion. Remove
the package manifest request for `advancedPresentation`, stop projecting
`WidgetView` through `GameLauncherExperienceProjection`, and remove the visible
experience-selection route/actions so the package emits its existing generic
responsive grid/scroll/navigation, `AppTile`, virtual-window, bounded artwork,
WRSS, and controller-focus primitives directly through
`WidgetApplicationRuntime`.

This is the package cutover only. Do not delete or modify the host, Bridge,
protocol, SDK, catalog, Settings, CLI, fixture, or documentation
LauncherExperience vertical slice in DLV-285. Do not add a generic custom-host
presentation escape hatch, compatibility layer, package-ID special case, or a
new semantic primitive unless the agent first proves two plausible widget
categories need it and stops for review. Preserve game-library behavior,
launching, navigation, pagination, organization, titles, categories, hidden
items, private package state, and all unrelated package/account/configuration
state. Retain the obsolete private experience value inertly for the later
targeted state-retirement milestone rather than broad-resetting state here.

Physical-first verification: implement production and build the smallest
coherent Game Launcher package plus integrated native Release, with every
command producing output or a terminal result within 60 seconds. Commit the
clean production candidate and report exact files, numeric exits, package and
Release artifact hashes, and retained risks. Do not launch/terminate the
resident OverlayHost, install packages, run tests, push, or start the vertical
slice deletion. The reviewer will inspect, refresh, visibly launch, and obtain
the user verdict. Only after physical acceptance may focused tests be assigned.

Production candidate `ccb46e7` is source-clean. It removes the package's
advanced-presentation manifest declaration, projection file, Experience route,
selection controls/actions, and compiled projection dependency while retaining
the prior private `ExperienceId` inertly for targeted retirement. The ordinary
declarative application view remains the sole package presentation. Package and
Release builds exited 0 without tests. Game Launcher 0.2.1 is installed and
enabled. The user physically accepted coherent PID 144052 and explicitly
deferred Game Launcher widget tests for future package work. Production
`ccb46e7` is integrated as `609d34e`; the same accepted candidate remains
running, so no test-only rebuild or relaunch is required.

## Assigned platform production — DLV-286 remove LauncherExperience

Lane: platform, acting as the sole serialized cross-framework owner. Baseline:
exact clean local-main merge `609d34e` in a new branch
`codex/dlv-286-remove-launcher-experience`. Do not reuse or merge the retained
DLV-284 platform branch. Dependencies: DLV-285 is physically accepted and
integrated; Game Launcher 0.2.1 already presents through the ordinary generic
full-trust application runtime. The widgets lane remains idle and no Game
Launcher widget tests are authorized under this milestone.

Objective: deliberately remove the complete dormant LauncherExperience and
AdvancedPresentation framework vertical slice. The post-removal boundary is
the ordinary declarative model only: bounded `ViewSnapshot`, responsive
grid/scroll/navigation, semantic tiles, virtual collections, bounded artwork,
WRSS, controller focus, and generic host rendering/input/accessibility/lifecycle.
Do not replace the deleted slice with a custom-host-presentation escape hatch,
compatibility layer, renamed equivalent, or package-specific native path.

In scope:

1. Delete the `LauncherExperienceCatalog` production project and its remaining
   catalog, validation, archive, built-in, identity, and file-system owners.
2. Remove LauncherExperience/AdvancedPresentation manifest, protocol, SDK,
   public-API, snapshot/update, Bridge catalog/event/selection/configuration,
   native bridge-client, layout/projection/adapter/host-state, Settings, CLI,
   build, fixture, recovery-descriptor, and documentation surfaces.
3. Remove only obsolete launcher-experience selection/configuration state with
   one explicit narrow pre-release schema retirement. Preserve themes, widget
   ordering, installed/enabled/current package state, credentials, provider
   configuration, accounts, and all unrelated host/package state.
4. Remove the now-inert Game Launcher `ExperienceId`/identity residue as part
   of the breaking slice deletion, without redesigning or otherwise testing
   the Game Launcher package.
5. Delete or replace launcher-specific tests and fixtures only where required
   to remove the dead contract; retain generic runtime, rendering, focus,
   accessibility, paging, launch, and package behavior unchanged.

Out of scope: new Game Launcher behavior or tests; generic forms; a custom
presentation channel; host knowledge of Game Launcher identity, tree shape, or
styles; unrelated protocol features; broad Settings/CLI redesign; arbitrary
state reset; Avalonia/AVP history; package publication; push.

Acceptance requires no active production reference under `src/` to
`LauncherExperience`, `AdvancedPresentation`, `advancedPresentation`, or the
specialized presentation protocol, except an explicitly documented migration
tombstone whose lifetime and removal condition are stated. Game Launcher must
still build/package as an ordinary full-trust Community application, while the
native host, Bridge, protocol, SDK, Settings, and CLI have no launcher-specific
knowledge. The narrow state retirement must prove unrelated persisted state is
preserved. Provide a before/after responsibility map because this deletion
crosses several existing owners; do not add a new multipurpose coordinator.

Verification is production/build first. Build the affected managed projects,
native Release, CLI, and Game Launcher package with bounded commands that emit
progress or terminate within 60 seconds. Do not run Game Launcher widget tests.
Run only focused non-Game-Launcher contract/state checks needed to prove the
dead framework and narrow state retirement are gone; do not run Tier 3. Commit
one coherent DLV-286 candidate, report exact deletions/remaining tombstones,
numeric exits and hashes, and do not install packages, launch/terminate the
resident accepted PID 144052, reset user state, push, or touch Avalonia/AVP.
Stop for review if removal requires a generic replacement channel, a material
public compatibility promise, a broad state reset, a package-ID special case,
or substantial conflict.

Production candidate `d0ca29b` is clean at exact parent `609d34e`. It deletes
35 obsolete files and removes the catalog, protocol/SDK, Bridge, native,
Settings, CLI, fixture, and public-guide slice across 86 paths. The sole active
production-name residue is the documented schema-1 settings tombstone, which
removes only `launcherExperience`, advances the in-memory document to schema 2,
and preserves unrelated state. The private Game Launcher `experienceId` is
retired without redesigning its generic application.

Production builds passed for WidgetSdk, PlatformSettings, WidgetBridge,
SettingsWidget, Wrail CLI, Game Launcher Core/Application/package, and native
Release. Focused non-Game-Launcher evidence passed: WidgetSdk 87/87,
PlatformSettings 17/17, SettingsWidget 59/59, native catalog, and two exact
Bridge contract cases. No Tier 3 ran. One broader Bridge aggregate was stopped
at 60/96 on sandbox named-pipe `UnauthorizedAccessException`; it incidentally
traversed a packaged Game Launcher catalog row before that environmental red.
Do not repeat it. The dedicated Game Launcher test project was untouched and
not built or run, per user direction; its obsolete advanced-presentation
assertions remain explicitly deferred with the user's future package plans.
Candidate PID 80988 is ready for the user to verify ordinary overlay startup,
Settings without Launcher Experience controls, widget switching, and retained
installed/configured state. Do not integrate before that verdict.

## Future architecture queue — maturity review additions

Status: DLV-286 is assigned; later items are ordered future work and are not
assigned.

1. One authoritative model-level protocol-version calculator shared by SDK
   snapshot creation and raw validation, with exact gated-node/property
   coverage, before any new protocol feature.
2. SDK stability/evolution contract.
3. Stable structured diagnostic contract.
4. Localization and accessibility semantics.
5. Author diagnostics and preview inspection.
6. Public-source pre-alpha readiness.

Do not schedule generic forms, broad OverlayApp refactoring, mediated import/
export, background scheduling, marketplace/publisher infrastructure, or new
component-count expansion without separate evidence and explicit promotion.
Extract native authorities only when real work touches them.

## Ordered queues

1. DLV-286 LauncherExperience/AdvancedPresentation vertical-slice deletion and
   narrow obsolete-state retirement; unaccepted PID 80988 awaits user verdict.
2. Protocol requirements; then the remaining maturity deliverables.
3. DLV-248 remains deliberately deferred until explicit user promotion.

There is no concurrent Ready production work in either standing lane.

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
| DLV-286 | Production `d0ca29b` from exact baseline `609d34e` is clean; affected production builds and focused non-Game-Launcher checks passed. Unaccepted PID 80988 is running for physical verdict. The dedicated Game Launcher test project remains untouched/deferred; one broader Bridge attempt stopped on sandbox pipe denial after incidental catalog traversal and must not be repeated. |
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
| DLV-468 | Tier 3 reached 44 passed steps, then exact transition/paint records exposed a six-millisecond concurrent diagnostic append inversion. |
| DLV-469 | DLV-468 table evidence passed; the focused route then produced eight 0-7 ms samples and one 54 ms scheduler outlier that nearest-rank p95 treated as the whole verdict. |
| DLV-468/469 | Direction-independent full-calendar spans and inclusive p95 tables passed with the complete focused WidgetSwitch route. |
| DLV-470 | Tier 3 passed the full native aggregate, then hidden smoke launched the default production profile and exited 0 after activating the resident owner. |
| DLV-470 accepted | A unique process profile kept the focused hidden owner resident without touching PID 89008; cleanup accounted for all five test processes. |
| DLV-471 | Tier 3 passed 44 steps, then the modal initial-focus assertion failed without recording the actual focus identity; candidate `8531915` added useful diagnostics but not a causal UI-loop fence. |
| DLV-471 accepted | `8886e25` replaces the sent-message fence with a tokenized queued-message acknowledgment after modal-loop entry; focused route and 2,582 linked checks passed. |
| DLV-472 | Tier 3 passed 42 steps, then an immediately crashing full-trust fixture exited before the action acknowledgment reached the managed presentation facade. |
| DLV-472 accepted | `29601e2` makes the fixture accept only the two process-exit ordering outcomes while preserving the same full recovery proof; focused `WidgetBridge.Tests` passed 96/96. |
| Cumulative integration | Exact tip `82093d5` passed 44/45 Tier-3 steps with DLV-472 green, then hit unrelated Audio Mixer synthetic-focus debt; reviewed accepted history is integrated as `cf77507`, and coherent PID 78860 is ready for user testing. |
