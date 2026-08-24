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
  corrected DLV-289 review. Replacement candidate `e7b24f4`, stacked on
  `7cc2be0`, was rejected because its active menu pixels were clipped above the
  fixed chrome surface. The user physically accepted correction `a74e677`,
  which preserves the topmost chrome owner and adds bounded click-through
  composition headroom. Exact PID 121188 contains the accepted production tip.
  Test-only follow-up `941b0f9` is green; the full chain is integrated as
  `4c8048d`. The running accepted production candidate remains current.
- DLV-292 production/test `ff5e7e4` is accepted and integrated as `80cdb10`.
  Bridge snapshot retention is bound to the exact worker start ordinal; worker
  replacement now emits typed stale-base recovery instead of continuing the
  old virtual-window generation; DLV-292 is closed. DLV-290 selectable layouts,
  its API/materializer corrections, and focused tests are accepted/integrated as
  `9857beb`; the new Settings disable failure is assigned separately as DLV-293.
- DLV-293 production `a9d36cf` is physically accepted and integrated as
  `10c3e26`. Catalog-order replacement
  now recreates the existing fixed-chrome/composition session before its
  synchronous repaint instead of painting through the retired session and
  falling permanently into the legacy HWND path. Exact candidate PID 137288 is
  responsive with executable SHA-256
  `B26DD3AC26DB44BB065F188879882A940F78C027F9699687E9BC6635D4F5D734`.
  Its new catalog-removal, focus, and Guide assertions completed before the
  focused host gate stopped on an older Game Launcher stationarity correlation.
  Per the user's explicit Game Launcher test deferral, that unrelated red is not
  rerun or repaired; the pin-coordinator test did not run and remains test debt.
- DLV-294 cumulative production/test `94fb256` + correction `02cec4e` is
  independently accepted and integrated as `60536ff`. The generic protocol-v21
  contract admits bounded package-authored pinned projection roots while the
  host retains its single HWND, renderer, focus, input, placement, and layout-
  selection authorities. Correction `02cec4e` closes the initial-checkpoint and
  incremental-materialization style gap through one shared projection-aware
  style applicator. Focused SDK, Bridge, native catalog/materialization,
  selection-generation, and pinned-coordinator evidence passed; the required
  Release builds passed. Coherent main Release PID 31132 is responsive with
  executable SHA-256
  `43166C13F59BD33A35413107006DD69954B0614DAD1523186D56B9CC40458936`
  and no `startup-error.txt`. Prior PID 137288 had no top-level HWND, so after
  its path was reverified the reviewer stopped only that exact orphaned accepted
  process before launch. DLV-291 is now the named visible proof.
- User testing of accepted PID 31132 exposed a platform blocker before DLV-291
  can receive a meaningful physical verdict: controller focus enters the pinned
  surface, but ordinary actions fail and X closes the surface. The exact 09:26
  session records `Pinned action transport failed for media-sessions` twice.
  Current source confirms two independent contract defects: native input sends
  the JSON context `pinnedSurface`, which is absent from the public SDK enum and
  therefore rejected by Bridge deserialization, and the pinned controller
  policy reserves X as `Close` while forwarding only A. Clean production-only
  DLV-473 commit `4c66b128` corrects the generic SDK/Bridge/native boundary and
  passed its Release build. Its exact patch is stacked with clean DLV-291
  production `5435eaf` as unaccepted candidate `68248d1a` without integrating
  `main`; the coherent Release/package build passed with no tests run. Accepted
  PID 31132 exited cooperatively through verified `WM_CLOSE`. Exact candidate
  PID 116316 is responsive with executable SHA-256
  `F9D4643E9A290F38ABC495AB8E9EBAB849C034F6404E03CD5194C2811DFC81F4`;
  reviewed Spotify 0.3.15 package SHA-256 is
  `1C4F722B59037CE7F6D348AC12A372F4D941D58779CB3F9B88869B7C6377F65A`.
  Spotify 0.3.15 is installed, selected, and enabled while its existing
  configuration is preserved. The user confirmed pinned-surface input now works
  as expected, accepting DLV-473 production, which is integrated as `055ec2f`.
  The running candidate already contains that exact production commit, so no
  integration-only rebuild or relaunch is required. DLV-291 remains unaccepted:
  Spotify's package renders the two layouts but its manifest omits the existing
  `pinningSupported` admission flag, so the tray correctly withholds Pin. Clean
  production-only correction `64b14864` adds that generic flag and advances the
  immutable package to 0.3.16. Its package build passed with SHA-256
  `35CFC14F0989C5E3B59580D949BF2A8F8D24C81CC395D0FC2428A48ABA459EBC`;
  no tests ran. Spotify 0.3.16 is installed, selected, and enabled, and exact
  candidate owner PID 116316 was visibly resurfaced through authenticated
  `--show`. Physical review rejects 0.3.16: its fresh generation-8 worker PID
  109900 rendered sequences 1-31 successfully, then the 10:18:59.807 next
  render failed as `worker_protocol_validation_failed` and the host retained
  failure UI. This rules out a stale live-refresh worker but does not yet prove
  which authored projection transition is invalid. A production-only 0.3.17
  correction is Assigned with validator weakening forbidden. The selected
  installed version remains 0.3.16 because restoring the full-trust 0.3.14
  rollback requires explicit user approval; no indirect mutation is permitted.
  Concrete source diagnosis found the invalid transition: when playback was
  unavailable, the first Up Next row pointed left to an absent Repeat button
  while the projection authored an empty-state Refresh button. Clean 0.3.17
  production `009af955` derives the actual player focus target from the same
  snapshot and uses it for both initial focus and the row edge. Its package
  build passed with SHA-256
  `F79F9E9064334C6122FD909796BFB8894C5B786435CA128BEB46E9191C32F28D`;
  no tests ran. The user explicitly approved retaining full trust and removing
  all previous Spotify versions. Catalog retirement removed 0.3.3 and
  0.3.10-0.3.16 through the bounded inactive-version API; 0.3.17 is now the
  sole installed, selected, enabled Spotify package. After unrelated widgets
  showed errors following repeated live catalog mutations, the user requested
  a clean overlay restart. Exact PID 116316 exited cooperatively through
  verified `WM_CLOSE`; the same reviewed executable relaunched visibly as
  responsive PID 105080 with no `startup-error.txt`. Physical review rejects
  0.3.17: LT/RT changes the pinned surface dimensions but `Now playing + up
  next` continues to show the compact Now Playing document; only the Full
  widget projection renders its distinct document. The host log records the
  pin lifecycle but has no typed selected-layout/projection render diagnostic,
  so the exact package-publication versus host-selection boundary remains to
  be proven by the widgets correction rather than guessed.
- The same PID 105080 session proves an independent platform regression after
  the overlay is hidden and reopened. Bridge session 1 (PID 49632) retired all
  seven workers cooperatively at 10:37:37; reopening reported broken pipe 232
  and started session 2 (PID 48760). The new workers then returned stale-base
  failures for YT Music, Now Playing, and Settings. Session 2 retired its
  workers at 10:38:08; the next reopen again hit broken pipe 232 before session
  3 (PID 147564) started at 10:39:07. The Bridge terminal exception and exit
  code are not durably captured because the no-window child discards stderr.
  DLV-474 is Assigned to bind retained host presentations to the exact Bridge
  session, recover atomically after transport replacement, and retain the
  terminal cause.
- Clean DLV-474 production `a830f026` is independently source-reviewed and its
  Release build passed. The user provisionally accepted it by disposition after
  the historical bridge-session loss could not be reproduced, but its first
  post-acceptance focused gate rejected the cumulative candidate before
  integration. It binds every host
  request to the exact Bridge session
  generation, classifies transport loss without publishing it as the current
  widget failure, serially reloads the replacement catalog, retires prior
  request/admission authority, and re-establishes active widgets from fresh
  ordinary checkpoints. Bridge terminal evidence is sanitized to session, PID,
  exit, bounded reason, and HRESULT metadata. The exact historical exit trigger
  remains unproven; the bounded sanitized terminal diagnostics remain in
  production so a future recurrence can prove it. Prior exact PID 105080
  exited cooperatively after `WM_CLOSE` reached its three top-level windows.
  Accepted candidate PID 844 later exited cooperatively for the approved
  Spotify package update. The same exact reviewed host is now responsive as
  PID 43416 with executable SHA-256
  `1C7C8849B014F538AD5AC7FEFA8947B6F85735808A64BF7D9F2BD70A134B1C18`
  and no `startup-error.txt`. The deterministic two-widget replacement test
  proves fresh workers restart at sequence 1 while ordinary-checkpoint
  admission still requires that value to exceed retained prior-session
  sequences 10 and 20. Both fresh checkpoints are rejected and recovery times
  out. The bounded correction now explicitly marks retained prior-Bridge
  snapshots inert and passed the native coordinator gate: 29 coordinator
  scenarios plus the retained OverlayState, lifecycle, and 314 action-feedback
  checks. The managed sanitized-terminal gate then exited 1 during an opaque
  pre-test build with no compiler diagnostic or test artifact. Per stop-first-
  red, four intentional diffs remain retained uncommitted with identity
  `d14a224507d682e9c67f83860e713fe0118bb4dd`; no rerun or repair occurred.
  PID 43416 remains unchanged, not accepted DLV-474 integration.
- Clean DLV-291 Spotify 0.3.18 production `2dc8ab8` is independently
  source-reviewed. It attempted to let the growing player share the selected
  Up Next row with the bounded queue panel by applying a layout-specific zero
  flex basis. The package build passed; package SHA-256 is
  `DB463A3AF2F035FC6F88BA8216CA08674C992BF5E0495CFF2FC2894D0BB88D05`.
  The user explicitly approved the package's ordinary current-user full-trust
  authority. Spotify 0.3.18 was installed with the reviewed hash, selected,
  and enabled while the overlay was stopped; retired 0.3.17 was removed and
  0.3.18 is the sole installed version. Exact DLV-474 host PID 43416 was then
  visibly relaunched. Physical review rejects 0.3.18: after pinning Compact
  while idle, starting playback, refreshing, and cycling to Now playing +
  queue, only the surface dimensions changed and the queue remained absent.
  Full widget remains distinct. Ownership is not yet proven: the host source
  uses one selected-layout index for both geometry and projection, clears
  renderer state, and invalidates, with no logged stale/drop failure, but the
  runtime log does not identify the selected projection root. A package-first
  deterministic snapshot boundary proof then proved both roots are structurally
  distinct and the Up Next root contains its header plus loading/error/empty/
  first-item queue branches, but the WRSS cascade computed the player as
  `width: 100%`, `flex-grow: 0`, and `flex-shrink: 0`, clipping the following
  queue pane. Clean correction `9dda3bf` uses a specific selector to restore a
  zero flex basis with bounded minimum width. Coherent Release and community
  package builds exited 0; immutable 0.3.19 package SHA-256 is
  `D877986774C1B64100B6680C4B717845AA04F4197E6AF2C0925F9A3229AB2C86`.
  The user continued the approved full-trust physical promotion. Exact PID
  43416 exited cooperatively after `WM_CLOSE` reached all three top-level
  windows; its Bridge and Spotify child exited too. Disabled 0.3.18 was retired
  without clearing credentials/private data, exact-hash 0.3.19 was installed,
  selected, and enabled, and it is the sole installed Spotify version. The same
  reviewed host binary is visibly responsive as PID 90896 with SHA-256
  `1C7C8849B014F538AD5AC7FEFA8947B6F85735808A64BF7D9F2BD70A134B1C18`,
  no startup error, Bridge PID 101932, and package worker PID 90072 from the
  exact 0.3.19 path. Physical review rejects 0.3.19: the Up Next projection and
  queue column are now visibly selected, but once playback is active the left
  Now Playing panel is entirely blank. The queue remains visible. This proves
  the platform selected the intended projection and narrows the next correction
  to the package player's active-content geometry. A bounded WRSS/native-layout
  proof is Assigned before another immutable package build. The user also
  reports that ordinary main-widget Queue remains stale when Spotify advances
  naturally and that the pinned queue has room for multiple rows. Source trace
  confirms adaptive polling refreshes playback but not the demanded queue on an
  external item-identity change. The same bounded 0.3.20 package correction now
  owns demand-gated queue refresh plus a small multi-row pinned projection.
  Native 640x340 proof is conclusive: 0.3.19 gives the player a 280x316 outer
  box but zero width to every immediate content descendant, while the queue is
  280x316. Using typed `flex-basis: 0px` instead yields a 326x316 player,
  96-DIP artwork, 306-DIP details/scrubber/controls, and the same 280x316 queue.
- DLV-473 post-acceptance test commit `34f15ae9` is retained clean and
  unintegrated. Its first authorized focused SDK gate exited 1 during build
  before test output, emitted no compiler diagnostic, and produced no test
  artifacts. Per stop-first-red ordering it was not rerun or repaired, and the
  Bridge/native pinned-input gates did not run. This opaque pre-test build red
  changes no production/runtime input, so exact candidate PID 116316 remains
  running without rebuild or relaunch.
- DLV-318 is the exact recoverable prior accepted Release at
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build`;
  executable SHA-256 is
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
- Spotify 0.3.23 is the sole installed, selected, and enabled version after the
  user's explicit full-trust and inactive-version retirement approval. Preserve
  its credential, account, provider, and configuration state.
- Managed tests are accepted through `676cd76`: DLV-319 `199a81b`, DLV-324
  `6b63edf`, DLV-325 `e441f25`, and DLV-326 `676cd76`. All named managed
  Tier-3 gates are green.
- DLV-283 platform production `cdbb04a` and the complete reviewed cumulative
  production/test chain are integrated through `cf77507`.

## Standing tasks

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-474 correction passed the native gate, then the managed sanitized-terminal gate stopped on an opaque pre-test build red. Four intentional diffs remain retained uncommitted in the isolated worktree; no rerun or repair is authorized. DLV-473 test-only `34f15ae9` remains blocked after its first red; the standing tree's two DLV-293 test diffs remain byte-identical and must not be touched. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-291 production is accepted/integrated as `e7ebdbe`. Its native test gate passed 108 checks; the Spotify gate stopped after 75 seconds without output, leaving two reviewed test diffs blocked. Preserve them in one explicitly unaccepted evidence commit without rerun, switch this standing worktree to a clean branch from integrated main, then execute assigned DLV-295. |

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

## Archived accepted milestone details

Detailed accepted evidence for DLV-284 through DLV-290 is preserved in the
[2026-08-24 04:09 snapshot](history/delivery-plan/2026-08-24T04-09-43-07-00.md).
The current accepted-state summary above remains the live disposition.

## Ready overlay recovery

### DLV-474 Bridge-session replacement and retained-presentation recovery

Lane: platform, serialized host/Bridge lifecycle correction. Status: production
`a830f026` rejected before integration by its first post-acceptance focused gate.
The bounded fresh-session correction passed its native gate, then stopped on an
opaque managed pre-test build red and remains uncommitted. The same exact
reviewed host remains running as PID 43416; it contains no DLV-474 correction.
Baseline: clean local `main` at planner tip. Use a separate clean worktree; the
standing platform worktree's two DLV-293 test diffs and retained DLV-473 test
commit are immutable evidence and must not be edited, staged, discarded,
stashed, moved, or rerun.

Correct the reproduced product failure after the overlay is hidden and later
reopened. The accepted host remains alive, but its Bridge transport disappears:
session 1 retires all workers at 10:37:37, reopen receives Win32 broken pipe
232, and session 2 starts. The host then sends retained virtual-window bases
from the prior Bridge authority, producing stale-base failures across unrelated
widgets. Session 2 repeats the retirement and broken-pipe cycle before session
3 recovers. DLV-292 binds Bridge snapshots to worker starts inside one Bridge
session; do not weaken or remove that check. Extend the authority boundary so
host-retained catalog/presentation/session state is also bound to the exact
Bridge session generation and atomically re-established when the transport is
replaced.

First retain the actual Bridge terminal evidence: exact PID/session, process
exit code, and bounded final stderr or an equivalent durable sanitized terminal
diagnostic. Current `CREATE_NO_WINDOW` launch discards the caught exception
message, so the trigger for the two exits is not proven. Diagnose and correct
that trigger if it is in this lifecycle boundary. A Bridge replacement must
retire old request/catalog/presentation authority, preserve only safe last-valid
visuals during recovery, establish the new catalog, and request Replace from a
fresh base for every subsequently activated widget. It must not publish a
broken-pipe failure as the current widget state merely because the overlay was
hidden, nor allow a stale session completion to overwrite newer state.

Use physical-first ordering: production and bounded terminal diagnostics only,
source review, one coherent Release build, planner launch, and user hide/reopen
verdict before focused tests. After acceptance, add only deterministic
Bridge-process-exit, session-replacement, retained-base, multi-widget recovery,
and hidden/reopen tests. Exclude Game Launcher widget tests, broad aggregates,
package/config mutation, DLV-291 package code, Avalonia/AVP, push, and unrelated
repair. Stop for destructive state, a second catalog/session owner, weakened
stale-generation checks, a materially different public protocol decision, or
an unrelated first red.

Acceptance: repeatedly hiding and reopening the overlay never strands the
catalog or any widget in broken-pipe/stale-window failure; a genuinely replaced
Bridge obtains a new session authority and all activated widgets establish from
a fresh base; pinned and last-valid visuals remain safe; the exact terminal
cause of any future Bridge exit is diagnosable without exposing secrets.

Post-acceptance correction: the first native gate exited 1 because the new
Bridge correctly requested base-zero OrdinaryCheckpoint publications for two
active widgets, but admission compared their restarted sequence 1 against
retained prior-session sequences 10 and 20. Distinguish a retained last-valid
visual from current-session sequence authority. A checkpoint from the exact new
Bridge session may establish a fresh positive sequence while the old visual is
inert; after that admission, same-session monotonic sequence and all DLV-292
worker-start/stale-base checks remain strict. Add the retained two-widget test
plus a same-session rewind rejection. Run the native coordinator gate first and
stop on any red; only if green run the managed sanitized-terminal gate. Stage
production separately from the retained tests and produce separate production
and test commits. Do not relaunch, mutate packages, run Game Launcher tests or
broad aggregates, touch retained DLV-293/DLV-473 evidence, or weaken validation.

Correction evidence: native Release gate exited 0 with 29 coordinator scenarios,
the retained OverlayState/lifecycle coverage, and 314 action-feedback checks;
executable SHA-256 was
`DDE1BE5756A8780D93BEA790316C1637DA2ACA749182A4565005F8AA91E37524`.
The managed gate used `SkipGameLauncherFixture=true` but exited 1 during build
before tests with no compiler diagnostic and no `obj\Release` artifact. Tests
executed: zero. Retain the four-file diff at identity
`d14a224507d682e9c67f83860e713fe0118bb4dd`; do not commit, rerun, or repair
under this stopped milestone.

### DLV-473 pinned-surface action routing and controller ownership

Lane: platform lead, serialized SDK/Bridge/native-host correction. Status:
production `4c66b128` physically accepted and integrated as `055ec2f`.
Post-acceptance test-only `34f15ae9` is retained unintegrated after the first
focused SDK gate stopped on an opaque pre-test build red; no rerun or repair is
authorized under this milestone. The standing platform
worktree's two uncommitted DLV-293
test files are retained evidence: do not edit, stage, discard, stash, move, or
otherwise disturb them. Create a separate bounded clean worktree for DLV-473,
and remove only that new worktree after its branch/commit is safely retained.

Correct the reproduced pinned-input contract rather than masking its failure.
The accepted PID 31132 session at 09:26 records two `Pinned action transport
failed for media-sessions` events. Native sends context `pinnedSurface`, while
the public `ControllerInputContext` admits only DashboardQuickAction,
OpenWidget, and PinnedLayoutSelection; Bridge therefore rejects the JSON before
the widget can handle it. Native policy also deliberately maps X to host Close
and only queues A, contrary to the product rule that X, bumpers, triggers, stick
clicks, and other non-reserved controls remain available to a focused widget.

Add one generic versioned pinned-surface input context across SDK, Bridge, and
native host. Bind every request to the exact widget runtime generation,
snapshot sequence, selected pinned layout identity, selected projection input
scope, and focused element. The SDK's default resolver must resolve against the
exact selected projection root, or the ordinary full-widget root for the
host-injected Full widget fallback; it must not search sibling layouts or an
unselected root. Reuse the existing bounded action queue, current-generation
checks, selected `WidgetSurfaceCoordinator` snapshot, and ordinary widget
action semantics. Do not create a second input/focus/session authority.

While pinned focus is active, D-pad and left-stick navigation remain host-owned
focus movement, A activates the focused control, and B returns focus to the
overlay without unpinning. X, Y, LB, RB, LT, RT, LS, RS, and Menu are delivered
as ordinary authored widget input with exact current authority; an unhandled
button remains inert. View remains the host-owned entry/future pinned-surface
cycle control. Retain the explicit LB+RB+X emergency-hide chord, but never treat
X alone as Close or Unpin. Close and Unpin remain explicit tray-menu or
accessibility/chrome actions. Placement/setup and opacity modes retain their
exclusive documented controls.

Use physical-first ordering. Change production/API code only, source-audit the
complete boundary, run one coherent Release/package build, and commit
`[DLV-473]`; do not write, modify, regenerate, or run tests before the user
accepts the launched behavior. The planner will assemble the reviewed
production candidate with DLV-291 only in a bounded clean unaccepted
branch/worktree, then launch that one coherent candidate containing DLV-473
and DLV-291 for the user's controller verdict. Main integration remains gated
on that verdict. After acceptance, add only
focused SDK JSON/context, Bridge generation/layout admission, controller-route,
pinned focus/action, and projection-root tests. Do not run Game Launcher tests,
broad aggregates, DLV-293 retained tests, account/package-state mutation,
multi-pin, Avalonia/AVP, or push.

Acceptance: View enters the one pinned surface regardless of tray selection;
focus movement is visible; A and authored non-reserved shortcuts operate the
selected Full widget or package projection; X alone never closes/unpins; B
returns to the overlay; stale generation, layout, scope, focus, and sequence
requests fail closed without disturbing the last valid pin. Stop for a second
input/focus/session owner, an incompatible public design with materially
different ownership, destructive state, substantial conflict, or unrelated
first red.

### DLV-293 disable-widget main-overlay survival

Lane: platform. Status: Production `a9d36cf` physically accepted and integrated
as `10c3e26`; focused test follow-up incomplete after an unrelated first red.
Baseline: fresh clean branch from integrated
DLV-290 merge `9857beb` plus the planner assignment commit. Diagnose and correct
the observed main overlay/session failure after disabling Game Launcher from
Settings. The pin is not the defect: its continued visibility only proves the
process and peer surface remained partly alive. Do not assume a root cause from
that symptom and do not add a Game Launcher identity special case.

Acceptance: disabling any catalog widget from Settings completes through the
existing catalog/session authority without exception, main-overlay HWND loss,
or a stranded visibility/focus transaction. The main overlay remains usable;
Guide can close and reopen it; surviving tray selection/focus is deterministic.
An unrelated pin remains visible and current. If the disabled widget itself is
pinned, only that pin is torn down through existing catalog reconciliation.

Use physical-first ordering: source diagnosis and one coherent Release build,
reviewer inspection, user verdict, then only focused catalog-removal,
visibility/Guide, Settings action, and pinned-peer tests. Reuse the current host,
window, catalog, focus, and pin owners. Exclude package/config mutation in tests,
Game Launcher widget tests, broad aggregates, DLV-291, multi-pin, Avalonia/AVP,
push, and unrelated repair. Stop for a new window/session owner, destructive
state, public contract change, substantial conflict, or an unrelated red.

### DLV-291 Spotify compact pinned layouts

Lane: widgets. Status: versions through 0.3.20 are physically rejected. Clean
0.3.20 commits `8948d1d` + `17e875f` restored visible active-player geometry
and added two queue rows plus playback-identity-triggered queue refresh, but
user review found both a live render failure and an incorrect next item. At
2026-08-24T19:33:41Z the exact reviewed host recorded request `render` with
worker code `worker_protocol_validation_failed`; PID 123124 and Bridge PID
61764 remained alive. Exact-hash 0.3.20 remains the sole installed active
Spotify version pending clean replacement; it is not accepted or integrated.
The rejected 0.3.17 build was reviewed, built, and installed as the sole Spotify
version after explicit
full-trust and retirement approval, but physical review proves LT/RT changes
the surface dimensions without replacing compact Now Playing with the distinct
Now Playing + Up Next document; Full widget remains distinct and works. The log
does not identify the selected projection, so inspect the exact snapshot,
selection notification, host-selected layout identity, and rendered projection
before changing code. The 0.3.18 physical sequence—pin Compact while idle,
start playback, refresh, then cycle to Now playing + queue—again changes only
dimensions and leaves the queue absent. This does not prove widget or platform
ownership. First build a credential-free deterministic presentation snapshot
for the exact empty-to-ready/playback transition and inspect both package layout
roots, node identities, computed style inputs, and the Up Next header plus
loading/error/empty/first-item queue content. Do not infer root selection from
geometry.

If the package snapshot aliases Compact, omits the queue, or lays the queue
outside the admitted width, correct only Spotify presentation/WRSS, bump the
immutable package version, build once, and stop for physical review. If the
snapshot proves structurally distinct in-bounds roots, make no production
change: return the exact snapshot evidence so the planner can reassign the
selection/materialization boundary to platform. Do not add permanent product
diagnostics, mutate the installed package, or touch concurrent DLV-474 work.
Produce any justified immutable production-only package correction from the
clean DLV-291 branch after rebasing its logical change on current main.
Retain the original two package-owned layouts—
`Compact now playing` and `Now playing + up next`—beside the host `Full widget`
fallback. Reuse Spotify's existing session/queue model and polling; do not add
duplicate provider work, credentials, host knowledge, or a Spotify protocol
special case. Preserve controller actions, bounded artwork, progress, failure
states, accessibility, responsive sizing, and ordinary full-widget behavior.
Use physical-first production/package build and user verdict, then focused
package/runtime tests only; no Game Launcher tests, broad aggregate, account or
package-state mutation, publication, Avalonia/AVP, or push.

Boundary result: Compact root `spotify.pinned-compact.root` and Up Next root
`spotify.pinned-up-next.root` are structurally distinct. The Up Next snapshot
contains its shell, queue identity, `QUEUE`/`Up next` header, and loading,
error, empty, and first-item branches. At the 640-DIP minimum, player minimum +
queue minimum + gap is 530 DIP. The failure was the cascade: the player card's
more specific generic rule retained `width: 100%` and non-growing/non-shrinking
flex values, so the queue was clipped after it. `9dda3bf` uses a specific
selector producing `width: 0px`, `min-width: 280px`, `flex-grow: 1`, and
`flex-shrink: 1`. Coherent Release and package builds exited 0. Artifact:
`artifacts/community-addons/spotify/widgetrail.samples.spotify-0.3.19.wrwidget`,
1,170,516 bytes, SHA-256
`D877986774C1B64100B6680C4B717845AA04F4197E6AF2C0925F9A3229AB2C86`.
Exact-hash 0.3.19 is the sole installed, selected, enabled Spotify version.
Reviewed host PID 90896 is responsive, has no startup error, and spawned its
Spotify worker from the 0.3.19 package path. The repeated exact sequence
physically proved projection selection and queue rendering, but rejected active
player rendering.

Next correction: use a credential-free ready-playback snapshot at the admitted
640-DIP surface to record native bounds for the Up Next shell, player card,
player's immediate artwork/details/scrubber/control descendants, and queue.
Native proof confirms the new authored `width: 0px` collapses every immediate
player descendant to width 0 while `min-width` preserves only a 280x316 empty
outer panel; the queue remains 280x316. Replacing that width with the existing
typed `flex-basis: 0px` contract produces a 326x316 player, 96-DIP artwork,
306-DIP details/scrubber/controls, and the same queue geometry. Apply that exact
evidenced rule. Do not change platform code. Correct only
Spotify package source: WRSS geometry, the existing shared queue-refresh owner,
and pinned queue presentation. Bump the immutable version, run one coherent
Release/package build, commit the scoped production change, and stop before
install, tests, or integration. Preserve the two projection roots, ordinary
full widget, provider/account state, and concurrent DLV-474 evidence.

The stale Up Next report is not pinned-only. Adaptive polling performs one full
refresh and then calls `RefreshPlaybackAsync` every two seconds while playing;
that path replaces `_playback` but does not compare the media item or refresh
`_queue`. Queue refresh currently occurs only on explicit layout selection,
manual refresh/navigation, or package-originated playback controls. Detect a
real polled playback item identity transition using stable Spotify media
identity and call the existing `InvalidateQueueCollection` boundary once. That
owner already refreshes only when ordinary Queue or the pinned Up Next layout
demands it and otherwise resets retained queue state. Do not fetch the queue on
every playback tick, add another timer/provider owner, or make pinned-specific
main-model state.

The pinned Up Next panel currently renders only `queue.Items[0]` despite the
existing bounded resource retaining up to 50 items. Render a small explicit
maximum of the already-loaded items that fit/read well in the surface, with
stable occurrence keys, vertical focus/scroll behavior, and no additional
provider call. Keep the ordinary main Queue's existing complete bounded list.
Include this and the demand-gated refresh in the same scoped 0.3.20 production
commit after the player-geometry proof; stop before install or tests.

0.3.20 physical rejection adds one bounded 0.3.21 correction. First prove the
live protocol failure with a snapshot where the shared queue cursor anchor is
outside the first two projected Up Next rows. The current projection truncates
to two keyed rows but copies `queue.Anchor` unchanged; protocol validation
requires a non-empty keyed collection's anchor to name one of its retained
projected items. Keep the main Queue cursor unchanged, but give the pinned
two-row projection an anchor that is present in that projection and preserve
typed focus/scroll behavior. Return the exact validation code as evidence.

Treat the incorrect next item as provider queue behavior, not as a package-
invented generation contract. Spotify's documented `GET /me/player/queue`
response contains `currently_playing` and `queue`, but does not define
`currently_playing` as a generation token or promise atomic consistency with a
separate playback-state request. Spotify separately documents that Autoplay
chooses similar songs after a user's album, playlist, or selection ends. The
user's physical evidence shows playlist-backed queue order is correct while
Autoplay can expose a different next-item behavior. Therefore consume one
successful queue response as Spotify's authoritative queue for that request;
do not reject or retry it by comparing its current item with independently
sampled playback state. Do not poll the queue on every playback tick, add a
second timer/provider owner, or alter the host.

Review disposition: clean production commit `77122d3` proves the rejected
shared-anchor case fails exactly with `missing_collection_anchor`, then selects
an anchor present among the two projected rows without changing ordinary Queue
state. Its additional queue/playback generation comparison and two-attempt
retry are now rejected: the public Spotify contract does not provide that
cross-request generation guarantee, and user evidence distinguishes the
Autoplay case from playlist-backed playback. Coherent tests-skipped Release and
exact package builds exited 0; no tests ran. The 1,171,340-byte immutable 0.3.21 package SHA-256 is
`F6AE87ADDB82C8DA4C643130BD9009274AA9430D58E588C8BCD0E6A97628DB0D`;
final tree is `88eac9f1b30e97424dc9ebaa51123810d3bd04db`. Reviewer source inspection
accepts this candidate for physical promotion only; it remains unintegrated.

Physical disposition: 0.3.21 fixes the prior runtime/protocol failure, but is
rejected overall because pinned Up Next stops refreshing after ordinary overlay
deactivation and resumes only when the user enters Queue in the main widget.
The package selection callback correctly records the Up Next demand, but
`OnDeactivatedAsync` unconditionally clears `_upNextPinnedLayoutSelected` even
though the pinned surface and its unchanged layout selection remain alive. The
generic host contract revokes layout selection on layout removal, runtime
replacement, unpin, or shutdown—not ordinary overlay hide/deactivation—so the
host does not need to resend the unchanged selection on the next activation.

Produce one immutable 0.3.22 package correction. Preserve the selected pinned
layout demand across ordinary deactivation/background transitions; clear it
only through the existing selection-change/unpin notification or final runtime
destruction/replacement boundary. Keep active polling lifecycle-owned and do
not keep the ordinary overlay active merely because a pin exists. Prove the
sequence select Up Next -> deactivate overlay -> polled playback identity
change still calls the existing bounded queue refresh owner exactly once, while
switching away/unpinning prevents further queue demand. Change package code
only, build once, and stop before install, tests, integration, or push.

Remove the 0.3.21 queue/playback generation guard and its retry machinery in
0.3.22. A successful `GET /me/player/queue` response is the authoritative
provider result for that request even when its `currently_playing` field differs
from an independently sampled playback response. Preserve the proven projected-
anchor correction. Do not synthesize playlist order, special-case Autoplay,
hide a provider result, or add delay/retry polling to chase agreement.

The same 0.3.22 correction also owns the active pinned-control ordering exposed
by user review. Pressing Next in the focused pinned surface currently completes
the provider control, calls `InvalidateQueueCollection` while `_playback` still
identifies the old track, and only then calls `RefreshPlaybackAsync`. Reconcile
authoritative playback first and run one demanded queue refresh after the
control path; do not start the obsolete pre-reconciliation load or allow the
playback identity callback to create a competing duplicate load. The queue
response itself is authoritative and must reach a terminal refreshed or
retained-error state rather than remain Loading. Do not special-case the pinned
renderer.

Review disposition: cumulative append-only production `e0fa1a3` + `ca80217` +
`8dd3a8d` implements the corrected contract without host or renderer changes.
One successful queue response is published without a cross-endpoint comparison
or retry; the projected-anchor correction remains intact. Ordinary deactivation
preserves selected Up Next demand, selection change/unpin and destruction still
revoke it, natural playback identity changes use the existing demand gate, and
Next/Previous suppresses that callback while explicitly scheduling one queue
refresh only after successful playback reconciliation. The final tests-skipped
Release and immutable package builds exited 0; no tests ran. The 1,171,200-byte
0.3.22 package SHA-256 is
`E43AD97A0851937E34BC55D6490D18CFE49B39FC7B5D7505222E56CBE3F92847`.
The prior planner-owned PID 110872 exited cooperatively after verified
`WM_CLOSE` reached its four WidgetRail top-level windows. Exact-hash 0.3.22 is
the sole installed, selected, enabled Spotify version. The unchanged reviewed
DLV-474 host is visibly responsive as PID 14100 with executable SHA-256
`1C7C8849B014F538AD5AC7FEFA8947B6F85735808A64BF7D9F2BD70A134B1C18`,
no startup error, Bridge PID 54496, and Spotify PID 93148 running from the exact
0.3.22 package path.

Physical disposition: 0.3.22 is rejected. With the pinned Up Next layout
focused, Next/Previous updates playback and keeps the worker/render surface
responsive, but an initially empty queue resource enters Loading and does not
publish data, empty, or error. The screenshot and log correlate this with
Spotify PID 93148 and an admitted Interactive lifecycle; this rules out the
earlier inactive-pinned-lifetime theory. Current logs expose neither queue-load
admission/completion nor the per-identity provider-gate owner, so they cannot
honestly distinguish a blocked gate wait from a non-terminal provider call.
Source inspection proves the structural gap: `LoadQueuePageAsync` has no total
deadline around `GetQueueAsync`, and `WindowsSpotifyPlatformBackend.SendAsync`
waits on the shared identity gate using only the Active lifetime token. The
12-second transport timeout begins only after that gate is acquired.

Produce one immutable 0.3.23 package correction from the clean cumulative
0.3.22 branch. Add bounded, non-secret runtime diagnostics for queue refresh
admission, provider-gate wait/acquisition, HTTP queue attempt, and terminal
success/empty/failure/cancellation, including elapsed time and operation
generation but no access token, response body, media text, or account data.
Give the entire queue load—including the shared-gate wait—a finite package-owned
deadline. On deadline or provider failure, the cursor resource must leave
Loading: retain and continue rendering any last-good queue items with a
recoverable warning/error, or publish its existing recoverable empty-load error
when no last-good items exist. Preserve the 0.3.22 authoritative-response rule,
single post-control refresh ordering, ordinary Queue behavior, generic SDK and
host boundaries, and existing retry/rate-limit policy; do not restore the
cross-endpoint generation guard, invent queue order, or add polling/retry loops.
Build/package once and stop before install, tests, integration, or push.

Review disposition: append-only production `2031a8c` (tree
`4b4b3fed1ca2e30fb5c879885934be09ad45392f`) is accepted for physical
promotion. The 45-second linked deadline includes the provider identity-gate
wait and preserves enough budget for the existing bounded transport retries.
Deadline becomes typed `spotify_timeout`; the generic cursor's existing commit
path publishes Error with any last-good rows retained, or the existing empty
recoverable failure when no rows exist. Correlation stays inside package
assemblies through an internal seam and the established 64-KiB sanitized
diagnostic sink. Logged fields are token-validated boundary/code plus numeric
operation, active generation, and elapsed milliseconds; no token, URI body,
account value, media title, or response body is recorded. Successful queue
responses remain authoritative, and the one post-Next/Previous refresh order is
unchanged. Exact source diff and `git diff --check` are clean. The single
tests-skipped Release build and single immutable package build exited 0; no
tests ran. The 1,173,526-byte 0.3.23 package SHA-256 is
`C29CA62EFF45C010DBEEF41F3A6CC71F898B476651B8E58E22E511C194F3659B`.

Physical promotion: prior PID 14100 exited cooperatively after `WM_CLOSE` was
posted to its six WidgetRail top-level windows; Bridge PID 54496 and Spotify PID
93148 retired with it. The supported CLI first rejected live enabled mutation,
so the stopped package was disabled, exact-hash 0.3.23 installed, selected, and
explicitly full-trust enabled. Catalog repair then identified only inactive
0.3.22 as removable and retired it; 0.3.23 is the sole installed Spotify
version. The unchanged reviewed DLV-474 host was launched and visibly activated
with `--show`. It is responsive as PID 28912 with unchanged executable SHA-256
`1C7C8849B014F538AD5AC7FEFA8947B6F85735808A64BF7D9F2BD70A134B1C18`;
Bridge PID 34368 started session 1, and Spotify PID 128796 runs from the exact
installed 0.3.23 path. The worker admitted current visible snapshots, the
pinned Spotify surface was recreated, and the startup log has no candidate
failure.

Physical disposition: 0.3.23 is rejected. The initial queue/provider hypothesis
and subsequent cursor-terminal-publication hypothesis are both disproven. The
seek bar forces a complete widget invalidation every 250 ms while playing, so a
committed queue snapshot would already be visible. Exact log correlation shows
the sole queue operation was admitted only after ordinary Queue navigation at
13:29:59 and successfully published 20 rows; it was not a pinned Next/Previous
refresh.

Concrete cause: the native pinned coordinator restores the persisted authored
Up Next layout by assigning `selectedLayoutIndex_` in
`CreateWindowForAdmission`, but it does not queue the corresponding generic
selected-layout notification. Spotify therefore renders the host-selected Up
Next projection while `_upNextPinnedLayoutSelected` remains false. After
Next/Previous, `InvalidateQueueCollection` sees no ordinary Queue or pinned Up
Next demand, resets the cursor to NotLoaded, and the pinned presentation renders
NotLoaded with the same Loading indicator. Periodic seek-bar invalidations keep
publishing that state. Layout-cycle/revocation notifications already use
`QueueLayoutSelection`; persisted initial selection is the missing lifecycle
edge. Cursor admission/cleanup is not the incident owner.

Produce one production-only 0.3.24 correction at the generic native
pinned-layout selection-notification boundary. When a persisted authored layout
is restored and actually becomes the visible admitted projection, notify the
owning current widget/runtime generation through the same bounded generic
selection channel used by layout cycling. Emit the selection exactly once for
that admitted selection, after the coordinator has current authority; do not
notify a stale/replaced admission, the built-in whole-widget fallback, or a
layout that was not restored. Preserve existing cycle, revocation, unpin,
runtime-replacement, and shutdown semantics. Do not add Spotify identity/data to
the host, change cursor/operation policy, add another timer/provider retry, or
special-case projection rendering. Bump the immutable Spotify package to
0.3.24 as the physical proof, build one coherent Release and package, commit the
production-only candidate, and stop before tests, install, launch, or
integration. After planner source review, replace 0.3.23 and repeat the exact
pinned Next/Previous sequence. Tests remain post-acceptance.

Review disposition: clean production-only commit `bbdc2368` changes only the
native coordinator lifecycle edge above plus Spotify's immutable package
version. The reviewer confirmed it reuses `QueueLayoutSelection`, executes only
after successful `BeginSetup`, skips the built-in index-zero Full widget
fallback, and carries the coordinator's current widget/runtime/snapshot
authority. The coherent Release build and Spotify 0.3.24 package build exited
0; no tests ran. OverlayHost SHA-256 is
`FC259234F53EC995A2D41DB9228326D18A1294656E9C516F6E43116CFE0F2056` and
package SHA-256 is
`734E162386BD6B75BDC384263D7B6C03C00E60886D0ABC020EABCCDCA6A748E6`.
Rejected 0.3.23 was disabled and uninstalled while the prior exact planner-
launched PID 28912 was stopped cooperatively. Exact-hash 0.3.24 is now the sole
installed, selected, enabled Spotify version. Exact candidate PID 32880 is
responsive from the reviewed Release path, its Bridge PID 124912 is running,
no startup error is present, and authenticated `--show` visibly surfaced it.
The user accepted the pinned Next/Previous behavior. Add only focused regression
tests for successful restored-authored-layout notification, Full widget and
failed-setup non-notification, stale/replaced authority rejection, and Spotify
queue-demand retention across pinned Next/Previous. Run the smallest linked
native coordinator and Spotify package/runtime gates once, stop at the first
red, and commit the test-only follow-up separately. Do not run Game Launcher,
broad aggregate, live-provider, package mutation, or unrelated tests. The
running accepted candidate already contains `bbdc2368`, so tests or integration
alone do not authorize a rebuild or relaunch.

Post-acceptance evidence: the native coordinator gate passed all 108 checks.
The assigned Spotify command produced no output for 75 seconds, exceeding the
one-minute observability rule; the lane stopped only that owned invocation and
did not rerun it. The reviewer inspected the retained two-file test diff and
found it scoped to the accepted invariants, but it remains unaccepted because
the Spotify gate is red. Production is independently accepted and integrated as
merge `e7ebdbe`. Preserve the test diff in one clearly reported unaccepted
`[DLV-291]` evidence commit solely to cleanly serialize DLV-295; never integrate
that test commit without a future explicit disposition. No rebuild or relaunch:
responsive PID 32880 already contains the integrated production commit.

### DLV-294 generic pinned-layout projections

Lane: widgets lead, serialized SDK/protocol/native-host prerequisite. Status:
Accepted cumulative commits `94fb256` + `02cec4e`, integrated as `60536ff` from
clean planner baseline `546ab34`. Replace the current size-profile-
only limitation with one generic versioned contract that lets each bounded
pinned layout carry its own package-authored declarative root, surface hints,
active input scope, and initial focus while the host continues to inject the
always-available Full widget fallback. Preserve the existing positional
`WidgetView` API compatibility and ordinary full-widget snapshot behavior.

The single host-owned pinned surface/HWND, placement, opacity, focus router,
action admission, and LT/RT selection owner remain authoritative. A layout
switch atomically selects one validated projection; it does not create another
window, renderer, worker, provider, or navigation owner. Validate each root and
the aggregate catalog under explicit node/string/depth/resource bounds so eight
layouts cannot multiply shared-host limits. Reject malformed, duplicate, stale,
wrong-generation, or over-budget projections before native allocation and
retain the last valid pinned presentation where safe.

Publish one generic generation-bound selected-layout notification to the owning
package after explicit user selection so a package may activate data demand for
that selected projection. It carries only current widget/runtime/layout
identity, never service-specific data, and must be revoked on layout removal,
runtime replacement, unpin, or shutdown. The host must not fetch queue/provider
data or recognize Spotify. Existing sizing-only packages and snapshots remain
valid in this pre-release API generation.

This is a nonvisual serialized prerequisite: update public SDK/protocol/native
owners and directly affected public docs, run only focused SDK version/bounds,
Bridge materialization, selection-generation, and pinned-coordinator gates plus
one Release build, then commit `[DLV-294]`. Do not launch or mutate packages;
DLV-291 is the physical-first visible proof after integration. Exclude Game
Launcher tests, broad aggregate/Tier 3, provider/account work, marketplace,
multi-pin, Avalonia/AVP, DLV-293 test debt, push, and unrelated refactoring.
Stop for a second presentation/focus authority, unbounded aggregate retention,
service-specific core behavior, destructive state, or a public design with
materially different ownership outcomes.

Review disposition: initial candidate `94fb256` preserved the intended generic
ownership model but was rejected because initial lifecycle checkpoints and
incremental materialization omitted projection computed styles. Correction
`02cec4e` routes those paths and ordinary snapshots through one projection-aware
style helper and adds focused checkpoint/incremental regression coverage. The
cumulative stack is accepted and integrated; DLV-294 is closed.

The later maturity queue remains: structured diagnostics; localization and
accessibility semantics; author diagnostics/preview inspection; and public-
source pre-alpha readiness. Do not schedule generic forms, broad OverlayApp
refactoring, marketplace/publisher infrastructure, or component-count growth
without explicit promotion.

### DLV-295 pinned-layout authoring ergonomics

Lane: widgets, baseline accepted integration `e7ebdbe`. Status: Assigned after
the retained DLV-291 tests are preserved on their old branch, explicitly
requested by the user. Add an optional SDK-level typed pinned-layout handle that
owns one stable layout ID and exposes current selection without package string
comparisons or hand-maintained booleans. The SDK must update selection before
the author callback, provide a selection-scoped cancellation token that is
replaced on selection and canceled on deselection, revocation, worker teardown,
or runtime replacement, and invalidate once when effective demand changes.
Allow the handle to present current immutable root/surface/focus/scope data while
retaining the existing `WidgetView.PinnedLayout(...)` and callback as the
low-level compatible API. Handles are widget-instance state, never static shared
authority. Use the existing protocol-v21 notification; no protocol/native-host
change, second lifecycle owner, provider work, or package special case.

Add a focused public test host that can select, restore, revoke, and replace a
pinned layout and route controller actions against its root. Prove ordering,
idempotence, cancellation, stale notification rejection, automatic
invalidation, Full widget behavior, and compatibility with the low-level API.
Migrate Spotify's accepted pinned layouts from its manual selected-layout
boolean/string comparison to the new optional handle as the real Community
package proof, without changing its accepted presentation, provider, queue, or
controller behavior. Update public API baselines and directly affected author
guidance. Run only the SDK/public-API, focused pinned-layout harness, and
Spotify deterministic package/runtime gates; Tier 2 only if the existing worker
ingress boundary changes. Stop for a protocol revision, hidden window authority,
destructive state, or a design that makes handles mandatory.

### DLV-296 pinned-layout preview and diagnostics

Lane: widgets, after DLV-295. Status: Ready. Extend the supported `wrail preview`
workflow to select one declared pinned layout or enumerate all of them through
the production validation/rendering path. Report exact layout ID/name, preferred
and bounded surface sizes, root identity, active scope, initial focus, and
action/focus diagnostics. Reject missing roots, invalid scopes/focus, duplicate
IDs, and out-of-contract surfaces; warn when authored layouts are structurally
identical where that is likely accidental. Do not create a second renderer,
capture framework, package-state mutation, native window owner, or new protocol.
Use deterministic scenarios and focused CLI/preview tests only.

### DLV-297 pinned-layout templates and examples

Lane: widgets, after DLV-296. Status: Ready. Update the public media template and
copyable compiled example with Compact, detailed-with-secondary-data, and host
Full widget fallback flows. Demonstrate shared actions, typed handles,
selection-scoped loading, loading/empty/error/populated states, responsive
surface hints, focus, accessibility, and deterministic fake-service tests. Keep
advanced helpers optional and the basic widget template unchanged. Validate the
external package build, public links, and the smallest template/example gates;
no live provider, installation, Game Launcher tests, broad aggregate, or push.

## Ordered queues

1. DLV-295 pinned-layout authoring ergonomics: typed handles, SDK-managed
   selection/cancellation/invalidation, and focused public test host.
2. DLV-296 pinned-layout preview and diagnostics.
3. DLV-297 pinned-layout templates and examples.
4. DLV-474 fresh-session sequence-authority correction: retained correction
   passed its native gate, then the managed gate stopped on an opaque pre-test
   build red. Four diffs remain uncommitted; no rerun or repair is authorized.
5. DLV-291 focused post-acceptance tests: native 108/108 green; Spotify gate
   stopped after 75 seconds without output. Preserve as unaccepted evidence;
   no rerun or integration is authorized.
6. DLV-473 focused post-acceptance tests: clean `34f15ae9` is retained
   unintegrated after the first SDK gate stopped before tests on an opaque build
   red; later gates did not run and no rerun is authorized.
7. DLV-294 generic pinned-layout projections: accepted/integrated as `60536ff`; closed.
8. DLV-293 focused test debt: retained uncommitted after unrelated Game Launcher stationarity red; no rerun under the explicit deferral.
9. Remaining maturity deliverables after the pinned-layout author workflow.
10. DLV-248 remains deliberately deferred until explicit user promotion.

DLV-473 production is accepted/integrated and its tests are post-acceptance
work. DLV-291 versions through 0.3.23 are physically rejected overall. Reviewed
0.3.24 `bbdc2368` is physically accepted and integrated as `e7ebdbe`; it remains
the sole installed active Spotify package under responsive exact Release PID
32880. Its test debt is retained separately and does not require relaunch.
DLV-474 production is rejected before integration by deterministic
fresh-session sequence evidence; its exact historical exit trigger remains
unproven and retained diagnostics are ready for a future recurrence.
DLV-293 production is accepted/integrated;
its incomplete test follow-up is retained as blocked debt, and Game Launcher
tests remain deferred.

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
| DLV-289 | The user physically accepted correction `a74e677` after `e7b24f4` was rejected for clipped active pixels. Test-only `941b0f9` passed six focused groups and the full chain is integrated as `4c8048d`. Exact PID 121188 contains the accepted production tip, so no tests-only rebuild/relaunch occurred. |
| DLV-292 | `ff5e7e4` binds each Bridge-cached snapshot to its worker start ordinal and uses existing typed stale-base recovery after replacement. Production build and three focused lifecycle/native gates passed; integrated as `80cdb10`. The user accepted PID 81980 by default because live reproduction is impractical. |
| DLV-293 | Production `a9d36cf` is physically accepted and integrated as `10c3e26`; PID 137288 already contains that production tip. New catalog-removal/focus/Guide assertions completed before the focused host gate stopped on an older Game Launcher stationarity correlation. The pin-coordinator suite did not run; both uncommitted test diffs remain retained, with no rerun or Game Launcher repair authorized. |
| DLV-474 | Production `a830f026` was provisionally accepted by user disposition because the historical bridge-session loss could not be reproduced, then rejected before integration when the first focused native gate proved fresh sequence 1 was compared against retained prior-session sequences 10/20. The retained correction passed 29 native coordinator scenarios plus linked native checks, then the managed diagnostic gate exited 1 during an opaque pre-test build. Four diffs remain uncommitted at identity `d14a224507d682e9c67f83860e713fe0118bb4dd`; no rerun or repair occurred. |
| DLV-291 | Spotify versions through production-only 0.3.23 `2031a8c` are physically rejected overall. The user accepted clean 0.3.24 `bbdc2368`; production is integrated as `e7ebdbe` and exact-hash 0.3.24 remains sole installed/selected/enabled under responsive PID 32880. Native tests passed 108/108; the Spotify gate stopped after 75 seconds without output, so its two-file test diff remains unaccepted debt. |
| DLV-248 | Deferred until explicit user promotion. |

## Integrated reliability — DLV-292 fresh-worker virtual-window recovery

Lane: platform, serialized shared runtime/native admission owner. Status: accepted/integrated as `80cdb10`; the user accepted PID 81980 by default because live reproduction is impractical. User evidence on prior PID 37884 showed Games & Apps, Network Controls, and Full Application Reference failing after a retained checkpoint crossed a fresh worker start.

Root cause: Bridge retained snapshot sequence without the worker start ordinal
that produced it, so a restarted worker appeared to continue the old process-
local virtual-window generation. `ff5e7e4` binds both, returns typed stale-base
after replacement, and reuses native RecoveryCheckpoint for a fresh Replace.
No stale-generation validation, checkpoint retention, or identity rule weakened.

The Release build passed. Focused Runtime idle-unload, two-widget Bridge restart,
and native coordinator/lifecycle/action-feedback gates passed. Tier 3, broad
aggregate, Game Launcher tests, and package/config mutation did not run.

Live packaged-worker reproduction remains unperformed by explicit user
disposition; the focused deterministic lifecycle evidence is final for DLV-292.
