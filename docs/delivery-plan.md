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
- DLV-286 cumulative production `d0ca29b` + `058efbc` + `a41bd72` is physically
  accepted and integrated into local `main` as merge `627ba4c`. The first two
  visible candidates were rejected and their exact PIDs 80988 and 103768 are
  stopped; the complete correction regenerates the finite seven-runtime host
  graph and deterministically removes retired roots. Production Release build
  and the one focused artifact-coherence/startup scenario passed. The user
  accepted responsive PID 85884 with executable SHA-256
  `3E12B4A4B78890801D642311EE63CEA3B5065785FCE59EEA1B2E4E1651BFFDDC`.
  That accepted candidate already contains exact integrated production tip
  `a41bd72`, so no merge-only rebuild or relaunch is required. Game Launcher
  widget tests remain explicitly deferred.
- DLV-287 production/test `dccf49a` is independently accepted and integrated
  into local `main` as merge `fc91157`. One internal WidgetProtocol calculator
  now owns the complete version-1-through-19 snapshot requirement matrix and
  exact validation provenance; SDK snapshot construction and raw validation
  consume the same result. The affected Release build passed with the expected
  existing native conversion warnings after one sandbox-only NuGet audit access
  failure was corrected by the approved unrestricted invocation. Prior accepted
  PID 85884 exited gracefully through exact owned `WM_CLOSE`; coherent local-main
  PID 113716 is responsive, visibly admitted the generic full-application and
  Spotify widgets, has executable SHA-256
  `5C8FC0B43BA54279E7DBB666FBD98338C592E25629CB9ABE722D1E02957DB43E`,
  and has no `startup-error.txt`.
- DLV-288 documentation commit `8be0ebb` is independently accepted and
  integrated into local `main` as merge `a37d614`. One active pre-release SDK
  evolution contract now owns release-unit classification, migration records,
  the external-distribution trigger, deprecation timing, emergency authority,
  and the separation between public API and wire-protocol evolution. Scoped
  Markdown-link and active-reference checks passed. The assigned documentation
  gate stopped only on three pre-existing OverlayHost packaging assertions and
  reported no DLV-288 link failure; it was not rerun or repaired under this
  documentation-only milestone. No production/runtime artifact input changed,
  so accepted PID 113716 remains the coherent visible candidate without a
  rebuild or relaunch. That accepted integrated executable remained the rollback
  through PID 54348, which exited cooperatively through verified `WM_CLOSE` for
  corrected DLV-289 review. Final unintegrated candidate `7cc2be0` was rejected
  only because its tray menu expands downward over the bottom-anchored tray;
  SHA-256
  `BF7D98B1BC43B1C9CAD451BF18B437816352A4E783C23F8A050AF415FD399D05`;
  responsive PID 37884 remains exact while the bounded correction is assigned.
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-289 candidate `7cc2be0` is held unintegrated. One production-only correction is Assigned: always place the tray menu above the tray while preserving stable item order and focus semantics. No tests run before the replacement verdict. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-288 `8be0ebb` is accepted/integrated as `a37d614`; the lane is idle. Game Launcher tests remain explicitly deferred and out of scope. |

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

## Accepted platform production — DLV-286 remove LauncherExperience

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
Physical verdict: rejected. At `2026-08-24T03:39:23Z`, Spotify worker PID 71612
started, failed its first visible lifecycle request with transport failure, and
exited 1; the host then reported `Widget worker became unavailable`. Media
Sessions, Network Controls, YT Music, and the generic full-application sample
crossed the same session successfully around it. Package diagnostics record a
second `exit-1` at `2026-08-23T19:02:41Z`, before DLV-286 PID 80988 launched, so
the correction must compare exact accepted baseline `609d34e` and candidate
`d0ca29b` rather than presuming the LauncherExperience removal caused the crash.

Assigned correction: the platform lane is the temporary sole serialized owner
of this exact Spotify bootstrap failure across OverlayHost, WidgetBridge, the
generic application runtime, packaging/bootstrap inputs, and Spotify startup
glue. Diagnose the first process exit completely, preserve the package-owned
domain boundary and all user state, and return one coherent follow-up production
commit on top of `d0ca29b`. Do not split the work into speculative micro-fixes or
test-patch loops; do not add sleeps, retries, delay increases, timeout changes,
or compatibility shims without evidence. Every command must emit progress or
terminate within 60 seconds; inspect immediately if it does not. Use the
smallest deterministic startup/retry reproduction and affected builds, stop at
the first unrelated red, and do not run Tier 3 or any Game Launcher widget test.
The replacement must prove Spotify can cold-start, publish its first ordinary
snapshot, survive one bounded retry/reopen path, and leave neighboring generic
workers usable. Do not install/reset package state, alter credentials/accounts,
push, touch Avalonia/AVP, integrate, or control live product processes; the
reviewer owns physical replacement after source/build review.

First correction `058efbc` is clean on exact parent `d0ca29b`. It correctly
makes Bridge and generic WorkerHost outputs purge-and-publish even under
`-SkipPackaging`; an isolated production build exited 0 and its focused Spotify
cold-start/reopen/generic-neighbor scenario passed 1/1. Physical launch then
exposed that the correction was incomplete: bundled Settings output still
dated from before the current protocol graph, its worker exited 1 immediately,
and Settings reported `worker-runtime-failed`. PID 103768 closed gracefully and
is gone; `058efbc` remains rejected and unintegrated.

Assigned complete correction: treat `-SkipPackaging` as skipping only Community
package publication, never host runtime generation. On top of `058efbc`, split
the build seam so every host-launched managed runtime is purged and regenerated
coherently: Bridge, generic WorkerHost, Settings, Audio Mixer, Network Controls,
Games & Apps, and Media Sessions, plus deterministic removal of retired trusted
Game Launcher, Spotify playback-host, and YT Music output locations. Inventory
the actual catalog/runtime paths first and cover the whole finite set in one
patch; do not return another single-worker fix. Preserve package publication
skip semantics and do not build, edit, or run Game Launcher widget tests. Add
one bounded artifact-coherence/startup scenario that proves fresh current
protocol/runtime assemblies for every host-owned runtime, successful first
snapshots for all bundled workers, Spotify cold-start plus one reopen, and one
independent generic full-trust neighbor. Production build first, then that one
focused scenario; no Tier 3 or broad aggregate. Every command must emit output
or be inspected by 60 seconds. Do not touch installed state, credentials,
accounts, Avalonia/AVP, reviewer docs, or push. Commit one coherent follow-up;
the reviewer owns visible replacement and integration.

Complete correction `a41bd72` is clean on exact parent `058efbc`. It moves the
finite seven-runtime generation graph—Bridge, WorkerHost, Settings, Audio Mixer,
Network Controls, Games & Apps, and Media Sessions—outside the package skip,
purges all ten active/retired roots first, and leaves only Community/test package
publication conditional. Production Release build with `-SkipTests
-SkipPackaging` exited 0. The single focused scenario passed 1/1 with exact
artifact hashes, no bundled shadow contracts, first snapshots for all five
host-owned widget workers, Spotify cold-start plus exactly one reopen, and one
independent generic neighbor; Game Launcher was explicitly excluded and no Tier
3 ran. Reviewed executable SHA-256 is
`3E12B4A4B78890801D642311EE63CEA3B5065785FCE59EEA1B2E4E1651BFFDDC`.
PID 85884 is responsive, `startup-error.txt` is absent, and Settings worker PID
133656 completed visible lifecycle and published a current snapshot. The user
physically accepted the complete corrected candidate. The cumulative chain
`d0ca29b` + `058efbc` + `a41bd72` is integrated as merge `627ba4c`; because the
running accepted candidate already contains exact production tip `a41bd72`, no
merge-only rebuild or relaunch is required.

## Accepted platform production — DLV-287 protocol-version requirements

Lane: platform, as the sole owner of this shared SDK/protocol seam. Baseline:
the exact clean local-main planner commit that assigns DLV-287 on top of accepted
DLV-286 merge `627ba4c`; create a fresh branch rather than continuing the
retained DLV-286 branch. Dependencies: DLV-286 is physically accepted and
integrated. The widgets lane remains idle, and Game Launcher widget tests remain
explicitly deferred.

Objective: replace the duplicated SDK feature scan and raw-validator version
predicates with one authoritative model-level protocol-version requirement
calculator. It must traverse the finite declarative snapshot model once, report
the maximum required protocol version, and retain exact requirement provenance
for every gated surface, quick-action capability, node kind, and gated property.
`WidgetView.CreateSnapshot` must use that shared result when choosing the
snapshot version, while `ViewSnapshotValidator` must use the same requirements
when rejecting a raw snapshot whose declared version is too old.

Ownership and scope: the calculator belongs in `WidgetProtocol` beside the
model and validator. `WidgetSdk` may consume it but must not keep an independent
recursive feature matrix. Preserve existing validation paths, error codes, and
actionable messages for version failures. Cover the complete current protocol
matrix through version 19, including surface hints/axis sizing, scrolling and
pagination, slider modes, dashboard capability authority, loading, inline PNG,
action surfaces, responsive grid/visibility, Repeat One, focus persistence,
cursor collections and artwork handles, text entry, and virtual collection
windows. Include nested responsive branches and every legal container path.

Out of scope: no new protocol feature, version bump, wire-shape change, public
SDK signature break, manifest/host-API change, compatibility adapter, native
host change, package-specific knowledge, broad validator rewrite, generic forms,
LauncherExperience resurrection, Game Launcher widget test change/run, or
Avalonia/AVP work. Do not change validation behavior unrelated to deciding the
minimum required snapshot version.

Acceptance: one production implementation owns the requirement matrix; SDK-
authored snapshots choose the exact maximum gated version rather than always
current; raw snapshots below each requirement fail at the same precise path;
raw snapshots at the requirement pass version admission; ungated baseline
trees remain version 1; nested and combined features choose the maximum once;
and adding a future gated feature has one model-level registration point rather
than coordinated SDK/validator predicates. Add focused table-driven tests that
enumerate every current gated node/property in isolation, representative nested
forms, and representative maximum-of-many combinations.

Verification: normal production/test ordering, not physical-first. Run the
directly affected WidgetProtocol/WidgetSdk builds and focused SDK protocol-
version tests once, followed by the smallest existing raw-snapshot validation
group that proves the shared calculator is enforced outside SDK construction.
Do not run Tier 3 or the broad Bridge aggregate. Every command must emit useful
output or terminate within 60 seconds; inspect immediately otherwise, stop at
the first red, preserve exact evidence, and do not rerun an unchanged gate.
Commit one coherent `[DLV-287]` change, report exact files and numeric exits,
leave the worktree clean, and do not launch/terminate the accepted OverlayHost,
install/reset packages, alter user state, edit reviewer documents, or push.

Concurrency and stop conditions: this shared-model change is serialized in the
platform lane. Stop before implementation if a public wire or SDK break, a
protocol-version bump, multiple competing calculators, a second full model
traversal, a package-specific exception, substantial merge conflict, or a
material validation-behavior change is required. No reproduced unblocked
visible product defect remains after DLV-286, and the user deferred Game
Launcher verification; this is the single internal prerequisite before the
remaining framework-maturity queue is reconsidered.

Commit `dccf49a` is accepted and integrated as merge `fc91157`. Its five-file
change adds one internal version-requirement calculator, removes the duplicate
SDK feature walkers and validator gates, and adds complete table-driven version
coverage without changing the public API or wire. The affected Release build
exited 0 after the restricted first invocation failed only on inaccessible
NuGet vulnerability metadata; the authoritative unrestricted invocation
completed successfully. WidgetSdk protocol contracts passed 89/89. No Bridge
aggregate, Tier 3, or Game Launcher widget test ran. The refreshed integrated
Release is visibly running as responsive PID 113716; its log admits the generic
full-application and Spotify widgets with current presentations.

## Accepted widgets documentation/test — DLV-288 SDK evolution contract

Lane: widgets, as the owner of the public authoring contract. Baseline: the
exact clean local-main planner commit that assigns DLV-288 on top of accepted
DLV-287 merge `fc91157`; create a fresh branch rather than continuing the
retained DLV-285 branch. Dependencies: the authoritative protocol requirement
matrix is integrated. The platform lane remains idle, and Game Launcher widget
tests remain explicitly deferred.

Objective: approve and publish one active pre-release SDK stability, migration,
and deprecation contract from the existing non-operative governance proposal.
Replace proposal wording with current public policy through one link-aware
documentation migration. The contract must define the reviewed release unit
(`WidgetSdk` package, `wrail`, ControllerWidget template, API baseline, and
supported protocol range), public API diff classifications, pre-release version
movement, migration-record fields, the external-distribution trigger for a
deprecation interval, emergency exception authority, and the strict separation
between SDK API evolution and wire-protocol evolution.

Ownership and scope: implementation owns the public compatibility/governance
pages and affected documentation indexes. Rename the proposal to an active
governance path if that produces the clearest information architecture, update
all inbound links atomically, and remove stale proposal status or historical
artifact hashes that readers could mistake for current evidence. Preserve the
existing executable `eng/WidgetSdkRelease.props`, `PublicApi.txt`, baseline
updater, compatibility suite, and template/release-unit metadata as the
enforcement mechanisms; change them only if a focused test proves the written
policy is not currently enforceable or discoverable.

Out of scope: no SDK public symbol change, baseline regeneration, release or
template version bump, package publication, signing, remote feed, multi-version
resolver, compatibility shim, deprecation attribute campaign, runtime fallback,
wire/protocol change, product process control, Game Launcher widget test, or
Avalonia/AVP work. Do not claim post-1.0 compatibility or that an external SDK
artifact has already been distributed.

Acceptance: one active page is authoritative rather than a proposal; the
compatibility guide links it and gives a copyable change-review sequence;
compatible additions, deprecations, breaking changes, and emergency removals
have unambiguous required evidence; the migration table requires old surface,
replacement, deprecated/removed release units, protocol/template impact, and a
before/after example; all links resolve; executable commands and metadata names
match current source; and no stale current-state hashes or unsupported promises
remain.

Verification: documentation/test ordering only. Run the smallest documentation
link/contract checks covering changed pages, then the existing WidgetSdk
compatibility suite once only if executable contract names or commands are
changed. Do not run the native Release build, WidgetSdk aggregate, Bridge tests,
Tier 3, or Game Launcher tests. Every command must emit useful output or
terminate within 60 seconds; inspect immediately otherwise, stop at the first
red, and do not rerun an unchanged gate. Commit one coherent `[DLV-288]` change,
report exact files and numeric exits, leave the worktree clean, and do not edit
reviewer documents or push.

Concurrency and stop conditions: this is the only active lane. Stop before
implementation if policy adoption requires choosing between materially
different supported-consumer promises, preserving an obsolete API, publishing
an artifact, changing a public symbol/version/wire, adding runtime compatibility
code, or making a legal/trademark commitment. Report the exact decision rather
than inventing a promise.

Commit `8be0ebb` is accepted and integrated as merge `a37d614`. The four-path
documentation migration creates one active SDK evolution contract, expands the
copyable compatibility review sequence and migration table, updates the
documentation index, and removes the obsolete proposal. Scoped Markdown-link,
active-reference, metadata-name, verifier-step, and protocol-range inspection
passed. The single documentation gate exited 1 only on three pre-existing
OverlayHost incremental-packaging assertions and reported no missing/stale link
from DLV-288; per the stop-first and unchanged-gate rules, it was not rerun or
repaired. No executable contract, product input, Game Launcher test, runtime,
package, or process state changed, and nothing was pushed.

## Assigned platform production — DLV-289 user-ready single-widget pinning

Lane: platform, as the sole serialized owner of native surface, shell input,
focus, accessibility, placement, and window lifecycle. Baseline: the exact clean
local-main planner commit that assigns DLV-289 on top of accepted DLV-288 merge
`a37d614`; create a fresh branch rather than continuing the retained DLV-287
branch. Dependencies: DLV-288 is accepted and integrated. The widgets lane
remains idle while this cross-surface milestone is active. Game Launcher tests
remain explicitly deferred and must not be built, edited, or run.

Objective: productize the already implemented one-surface pinned-window
foundation as a discoverable controller-first feature. A user must be able to
pin the current eligible widget from the visible overlay, leave that validated
declarative projection on screen after closing the overlay, deliberately enter
and leave Interactive mode, move or resize it anywhere inside the selected
monitor's usable work area, and unpin or close it through visible host-owned
controls. This is one simultaneous pin, not a multi-pin redesign.

Ownership and design boundary:

1. Reuse the existing `WidgetSurfaceCoordinator`, `PinnedSurfacePolicy`,
   `PinnedSurfacePlacement`, peer tool-window HWND, declarative renderer,
   generation admission, normalized placement store, and exact teardown. Do
   not add another HWND, coordinator, renderer, input owner, persistence owner,
   public protocol, or widget-controlled native authority.
2. While the tray owns focus, controller Menu/Options opens one host-owned
   context menu anchored to the selected tray widget; pointer right-click on
   that tray item opens the same menu. Always place the menu panel above the
   tray with its bottom edge anchored to the tray rather than expanding down
   over content or offscreen; keep item order, selection, and semantic order
   stable from top to bottom. Eligible/current/blocked items retain the exact
   Pin/Adjust/Opacity/Unpin availability below. D-pad/left-stick navigates, `A`
   activates, and `B` or Menu restores exact tray selection/focus. While the
   one pin exists, View enters that pin regardless of which tray item is
   selected; `B` returns to the tray. This selected-item-independent rule is
   the future-compatible seam for cycling several pins, but DLV-289 must not
   implement multi-pin behavior. Menu and View remain package actions while
   widget content owns focus, the existing View+Menu recovery route remains
   intact, and LB, RB, RS, and other package actions remain untouched. Keyboard
   `P`/`U`, pinned chrome, and pointer controls may remain secondary fallbacks.
3. Preserve the current safety model: a new pin starts nonactivating and
   click-through; closing the main overlay cancels placement/focus, leaves the
   pin visible and updating, and forwards no hidden-overlay controller input.
   Explicit Interactive mode alone may receive current-generation input. `B`
   or the documented focus-return action restores click-through; Close,
   Unpin, worker/runtime/package replacement, catalog removal, display failure,
   emergency hide, and host exit retain exact paired native/semantic teardown.
4. Preserve host-owned placement bounds and persistence. When a widget has no
   valid saved placement, seed its first pin from the same host-resolved content
   extent used by the ordinary widget surface, add only the compact pinned
   chrome footprint, and clamp to the current monitor work area. A valid saved
   user rectangle always wins, and later content updates must not auto-resize
   it. Reduce the pinned surface to a compact header, thin border, and small
   content inset; stronger move/resize affordances belong only to Adjust mode.
   `Adjust pinned widget`
   enters one preview/commit/cancel transaction: left stick or D-pad moves,
   right stick resizes, `A` commits, and `B` cancels and restores the exact
   starting rectangle. Show a visible control legend and live outline or
   equivalent dimensions. Pointer and UI Automation use the same placement
   owner. Commit fully clamps the rectangle to the selected monitor work area
   and current DPI. Invalid persisted data, monitor removal, or an unusable
   work area fails closed or uses the existing safe fallback rather than
   creating an offscreen/undersized window.
5. Add one host-owned whole-surface opacity value per pinned widget. The pinned
   Options menu exposes `Opacity — N%`; `A` enters a live preview transaction,
   horizontal D-pad/left-stick changes 10 percentage points within 30–100%,
   `A` commits and persists, and `B` restores the exact prior alpha. Default and
   invalid persisted values resolve safely to 100% without corrupting a valid
   placement. Honor the user's selected opacity in Windows High Contrast too;
   do not add an automatic override, warning, or separate accessibility mode.
6. Enable the bundled `Now Playing` manifest as the first safe production
   acceptance widget by setting only the existing generic
   `pinningSupported` declaration. This narrow manifest edit is explicitly
   included in the serialized platform assignment so the physical candidate
   exercises the real packaged path. It must not create a Now Playing identity,
   tree-shape, action, or style special case in native code. Other widget
   opt-ins remain independent package decisions after the generic experience is
   accepted.
7. Before materially extending the application-sized native host, report a
   before/after responsibility map. Keep surface lifecycle and placement in the
   existing cohesive owners; do not grow `main.cpp` with a second pin state
   machine or duplicate focus/controller/accessibility knowledge.

Physical-first verification: change production and directly affected public
feature documentation only. Do not add, edit, generate, or run automated tests
before the user verdict. Build one coherent packaged Release with every command
emitting useful output or a terminal result within 60 seconds, perform direct
source review of HWND styles/lifetime, focus and hidden-input authority,
coordinate/DPI handling, generation revalidation, accessibility, and teardown,
then commit one clean production-only `[DLV-289]` candidate. Report exact files,
numeric build exit, executable/runtime hashes, the chosen conflict-free
controller interaction, responsibility-map delta, and residual physical risks.
Do not launch or terminate OverlayHost, install/reset packages, alter provider,
account, credential, or configuration state, push, or touch Avalonia/AVP; the
reviewer owns exact-candidate launch and the user owns the physical verdict.

The final user acceptance route must visibly establish: Now Playing pins and
starts click-through; the tray menu always opens above the tray without leaving
the usable work area; overlay close/reopen preserves the same live pin; Interactive
entry/exit and one safe current action work; tray-scope View enters the pin
regardless of tray selection and `B` returns; first-pin sizing shows the complete
ordinary widget extent with compact chrome; the visible/accessibility guide is
controller-first and accurate for the current mode; controller and pointer
Move/Resize commit and cancel are understandable; whole-surface opacity previews,
commits, cancels, persists, and stays within 30–100%; Menu/View while widget
content is focused and LB/RB/RS continue reaching package actions; the committed
rectangle stays inside the chosen monitor work area; and Unpin/Close removes the
surface. Environment-limited monitor/DPI/worker cases are reported, not converted
into capture or harness work.

Only after the user physically accepts the production behavior may the same
platform lane add focused regression coverage for the accepted visible Pin
action/controller route, overlay-hide click-through lifetime, current-generation
input admission, placement commit/cancel/clamp, Now Playing opt-in projection,
accessibility actions, and exact teardown. Run the directly affected pinned
surface/coordinator/placement and smallest shell/controller/manifest boundary
suites once; no Tier 3, broad aggregate, Game Launcher test, screenshot harness,
or unchanged rerun. Commit the test follow-up separately and report numeric
counts and retained manual debt.

Out of scope: multiple simultaneous pins, arbitrary widget/native windows,
auto-pin or startup rehydration of window authority, video or WebView, Windows
App SDK, a new compositor, hidden-overlay controller forwarding,
exclusive-fullscreen/anti-cheat/HDR guarantees, new manifest/protocol fields,
package-specific host behavior, enabling additional widget manifests, broad
shell redesign, Game Launcher work/tests, Avalonia/AVP, publication, or push.

Stop conditions: stop before implementation if a discoverable controller route
requires stealing an existing widget action, the feature needs another
window/input/focus/semantic owner, a public wire or manifest change, a broad
host rewrite, a package identity special case, destructive state change,
substantial merge conflict, or a materially different multi-monitor/product
choice. Stop on the first unrelated build red after preserving exact evidence;
do not repair or rerun it under DLV-289.

Rejected pin-history commits `78f90d4`, `53ab7b0`, and `ddb2d91` remain
unintegrated. The user accepted `da1f848` as the foundation and rejected only
the downward tray-menu placement in correction `7cc2be0`. The next correction
must keep the same existing menu/layout/input/accessibility owners, always
anchor the whole menu above the tray, preserve stable top-to-bottom item and
semantic order, and clamp horizontally within the usable work area. Do not add
adaptive above/below behavior or a second menu state. Production and directly
affected public docs only; build one coherent Release, commit, and stop for
reviewer launch. No tests, Game Launcher, Avalonia/AVP, state mutation, or push.

## Ready pinned-presentation work

### DLV-290 generic selectable pinned layouts

Lane: platform, serialized shared-contract owner. Status: Ready only after the
final DLV-289 chain is accepted/integrated into a fresh clean baseline; never
start from an unintegrated candidate or dirty tree. Add an optional bounded
widget-authored pinned-presentation catalog without granting native authority.
`Full widget` is the host fallback; added layouts have stable IDs, names,
existing bounded axis sizing, and a selected checkpoint keyed independently
from the ordinary view. Reuse SDK/runtime/Bridge/native declarative admission.

Selecting `Pin` immediately focuses setup: LT/RT cycle a visible layout
name/index, left stick or D-pad moves, right stick resizes, `A` commits and
returns the pin to click-through, and `B` cancels the new pin/restores tray
focus. Adjust reopens setup and `B` restores the prior layout/rectangle. LT/RT
remain package actions outside setup. Preserve every DLV-289 invariant.

Verification: normal focused shared-contract suites, then one physical-first UX
candidate before final UI tests; Tier 3 only if the public wire boundary
requires it. Exclude multi-pin, native rehydration, widget windows, marketplace,
package host special cases, Game Launcher, Avalonia/AVP, and push. Stop for a
second presentation authority, unbounded retention, destructive state, a public
compatibility choice, substantial conflict, or triggers owned outside setup.

### DLV-291 Spotify compact pinned layouts

Lane: widgets. Status: Awaiting accepted/integrated DLV-290. Baseline: fresh
clean widgets branch from that integration. Add two package-owned layouts—
`Compact now playing` and `Now playing + up next`—beside the host `Full widget`
fallback. Reuse Spotify's existing session/queue model and polling; do not add
duplicate provider work, credentials, host knowledge, or a Spotify protocol
special case. Preserve controller actions, bounded artwork, progress, failure
states, accessibility, responsive sizing, and ordinary full-widget behavior.
Use physical-first production/package build and user verdict, then focused
package/runtime tests only; no Game Launcher tests, broad aggregate, account or
package-state mutation, publication, Avalonia/AVP, or push.

The later maturity queue remains: structured diagnostics; localization and
accessibility semantics; author diagnostics/preview inspection; and public-
source pre-alpha readiness. Do not schedule generic forms, broad OverlayApp
refactoring, marketplace/publisher infrastructure, or component-count growth
without explicit promotion.

## Ordered queues

1. DLV-289 user-ready single-widget pinning: above-tray correction Assigned.
2. DLV-292 fresh-worker virtual-window recovery: Ready platform after accepted
   DLV-289 integration; ahead of new pinned-layout contract work.
3. DLV-290 generic selectable pinned layouts: Ready platform after accepted
   DLV-289 integration.
4. DLV-291 Spotify compact pinned layouts: widgets Awaiting DLV-290 integration.
5. Remaining maturity deliverables, ordered after the pinned-layout UX settles.
6. DLV-248 remains deliberately deferred until explicit user promotion.

There is no concurrently executable Ready production work: DLV-292 consumes accepted DLV-289, DLV-290 follows DLV-292, and DLV-291 consumes accepted DLV-290. Game Launcher tests remain deferred.

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
| DLV-286 | The user physically accepted complete correction `a41bd72`; cumulative chain `d0ca29b` + `058efbc` + `a41bd72` is integrated as merge `627ba4c`. Responsive accepted PID 85884 already contains that production tip, so it remains running without a merge-only rebuild/relaunch. The dedicated Game Launcher test project remains untouched/deferred, and the broader Bridge aggregate must not be repeated. |
| DLV-287 | Production/test `dccf49a` is accepted and integrated as `fc91157`; focused Release build passed and WidgetSdk protocol contracts passed 89/89. Exact prior PID 85884 exited gracefully. Refreshed integrated PID 113716 is responsive, has no startup error, and admitted the generic full-application and Spotify widgets. |
| DLV-288 | Documentation `8be0ebb` is accepted and integrated as `a37d614`; scoped link/reference/contract inspection passed. Its single documentation gate stopped only on three pre-existing OverlayHost packaging assertions, with no DLV-288 link failure, and was not rerun. No runtime input changed, so PID 113716 remains accepted. |
| DLV-289 | `7cc2be0` is held unintegrated: all requested polish reached physical review, but its menu opens downward over the bottom tray. A bounded production correction is Assigned to always anchor the menu above the tray with stable item/semantic order. No tests have run. |
| DLV-248 | Deferred until explicit user promotion. |

## Ready platform reliability — DLV-292 fresh-worker virtual-window recovery

Lane: platform, serialized shared runtime/native admission owner. Baseline: a fresh clean branch from accepted integrated DLV-289. Dependencies: DLV-289 is accepted/integrated; this runs before DLV-290. User evidence on exact candidate PID 37884 shows Games & Apps, Network Controls, and Full Application Reference failing with the same invalid/stale virtual collection transition. In each captured route a retained checkpoint is visible, a worker starts fresh, and the first refreshed snapshot is rejected after the Bridge reports it admitted.

Diagnose and correct the one generic authority seam across worker/runtime,
Bridge typed transaction/recovery, and native `WidgetSessionCoordinator`.
Fresh worker state must not be compared as an ordinary directional continuation
of a retained process-local collection generation. Preserve the last valid
checkpoint, exact runtime/presentation identity, bounded virtual windows, and
typed recovery; never merely accept a decreasing/reused generation, clear the
checkpoint on ordinary invalidation, add package IDs, or reset user state.

Normal verification ordering applies. Build the affected production graph,
then run one focused deterministic lifecycle group covering at least two
differently named generic workers: unload-after-idle restart and suspended/
restarted worker, with retained checkpoint, fresh Replace baseline, paging in
both directions, stale reply rejection, and last-valid retention. Run the
smallest linked Runtime/Bridge/native admission group only; no Tier 3, broad
aggregate, Game Launcher test, live process control, package/config mutation,
Avalonia/AVP, publication, or push. Commit one coherent `[DLV-292]` result.

Stop before a public wire/SDK break, second transaction owner, unbounded retry,
identity-specific exception, destructive state reset, or substantial conflict.
Report exact root cause, responsibility map, files, numeric build/test counts,
and residual packaged risk. The reviewer owns integration and visible launch.
