# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through the physically accepted DLV-283 cumulative
candidate is preserved in the
[2026-08-21 02:49 snapshot](history/delivery-plan/2026-08-21T02-49-45-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
evidence only; this file is the sole authority for current work.

## Current baseline and accepted candidate

- Accepted production/test integration baseline on local `main` is `c21ad02`.
  It contains accepted DLV-265 package production/focused evidence and accepted
  DLV-277 host production/focused evidence.
- The user physically accepted cumulative DLV-278/279/270/280/281/282/283 at
  exact widgets commit `0dec7370513d8e31a02dda22cab47c146a28ef77`.
  Responsive PID 129420 visibly runs that exact tests-skipped Release from
  `artifacts/dlv283-git-exact`. `OverlayHost.exe` SHA-256 is
  `249FEB9EC5B10FA646F29B48BFCA6A3E0A4F829C0F47F8397A77BC2EC1142D87`.
  The accepted session exercised typed stale-base recovery repeatedly without
  reproducing the stale virtual-window error.
- DLV-296 production commit
  `3922b58dc6456be17442926f2a0c7257d3b97e11` is source-reviewed and its exact
  clean detached Release build passed with tests skipped and packaging enabled.
  Candidate `OverlayHost.exe` SHA-256 is
  `147DCA59310AB4188E5B93850E6155DF04D8BE7B92488C2D650454C521EB177D`.
  After fresh explicit user approval, prior accepted PID 129420 exited
  cooperatively through verified `WM_CLOSE` and exact DLV-296 PID 83788
  launched visibly from the detached build. The user physically accepted this
  candidate after rapid Spotify/Games & Apps cycling and virtual paging.
- Exact DLV-283 production is platform commit `cdbb04a`. The cumulative widgets
  merge is `0dec737`. DLV-280, DLV-281, DLV-282, DLV-283, DLV-278, DLV-279, and
  DLV-270 are physically accepted only as that cumulative tree; they remain
  unintegrated until post-verdict evidence passes and is independently reviewed.
- Rejected/retired PIDs 34764, 88756, 82992, 54676, 84296, and 144732 retired
  cooperatively through `WM_CLOSE`; no force termination occurred. Accepted
  DLV-277 commit `830364d` remains the rollback artifact but is not running.
- Reviewed full-trust Spotify 0.3.12 remains installed, selected, and enabled.
  Preserve its package, Client ID, credentials, account/configuration/provider
  state, and the provider-free Full Application sample. No post-verdict tests
  have run yet.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Idle pending cumulative test evidence and integration. DLV-284 is queued but not assigned. Do not begin it, test, launch, integrate, or push. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-296 `3922b58` is physically accepted and exact PID 83788 remains running. Execute test-only DLV-297 below, preserving production/packages/state and committing only reviewed evidence. Do not rebuild, relaunch, integrate, or push. |

## Execution and architecture rules

- Local `main` is the reviewer-owned integration branch. Review and integrate
  only accepted DLV commits. Implementation tasks never edit reviewer-owned
  documents and never push; the planner never authors implementation code.
- Follow physical-first order: coherent production/build, user verdict, then
  focused tests. Do not integrate a production-only candidate before verdict
  and post-verdict evidence.
- Launch a coherent native Release only when accepted integrated production or
  runtime artifact inputs change. Retain PID 129420 when the only delta is tests
  or reviewer documents and it already contains the production commit.
- Use affected Tier 1 suites once, the smallest linked Tier 2 group when a
  changed boundary requires it, and Tier 3 only when explicitly required.
- The native overlay is the sole production presentation path. Avalonia/AVP is
  closed failed-experiment history: do not resume, message, launch, integrate,
  delete, or otherwise touch it without a new explicit user decision.
- The native host owns HWND, graphics, semantic admission, layout, clipping,
  focus, input, accessibility, and scroll offset. Widgets own private data and
  item materialization. Do not add package-specific behavior to core host/SDK
  layers or duplicate focus, scroll, render, cache, or lifecycle ownership.
- Input, focus, free-scroll, and re-entry bind to one fully admitted interaction
  snapshot. Stale/cancelled/failed work cannot mutate current authority.
- Retain last valid presentation on failure. Never clear/reload widget state,
  forge collection generations, weaken bounds/admission, or restart a healthy
  worker to conceal divergence.
- Preserve user-owned changes and unrelated dirty state. Never push.

## Assigned evidence — cumulative DLV-278 through DLV-283

Owner/baseline: widgets lane from exact clean cumulative commit
`0dec7370513d8e31a02dda22cab47c146a28ef77`. PID 129420 already contains this
production tree. This is test-only: do not change production, package identity
or content, manifests, public/private protocols, reviewer documents, installed
packages, configuration, credentials, account/provider data, or processes.

Add deterministic table-driven and cross-process evidence for the complete
publication transaction, not isolated happy paths. At minimum cover:

- ordinary incremental update, ordinary checkpoint, and typed stale-base
  recovery checkpoint as distinct cases with their exact legal base;
- host/bridge exact-base convergence, bridge-ahead/host-behind recovery, and
  rejection when the retained recovery-origin sequence changes;
- switch-away during in-flight Snapshot/Establish completion, followed by
  switch-back with the last admitted host screen retained;
- cancellation, wrong lifecycle, wrong instance/runtime/presentation
  generation, non-forward sequence, second retry, malformed or directional
  recovery checkpoint, and unchanged ordinary-validator rejection;
- rapid cycling among Spotify, Full Application, and Games & Apps while paging
  in both directions, including recovery after the private bridge checkpoint
  advances beyond the retained host checkpoint; and
- stable route, focus, scroll anchor, worker lifetime, collection generation,
  item keys, and absence of loading/reset publication across recovery.

Prefer one explicit invariant matrix whose rows name prior host sequence,
bridge/request base, lifecycle authority, publication intent, collection
generation/marker, expected disposition, and committed authority. Reuse exact
production fixtures/contracts; do not weaken assertions or add test-only
production behavior.

Run the smallest affected focused native coordinator, bridge protocol/registry,
runtime publication, managed SDK diff, stale-base, cancellation/generation,
checkpoint fallback, and Spotify paging suites once. Then run the smallest
linked Tier 2 group and one exact-commit Tier 3 verifier because this cluster
changes a private cross-process contract. Record exact commands, counts, commit,
hashes where applicable, and every skipped/ineligible suite.

Stop on the first distinct production defect, nondeterminism, fixture gap that
would require production changes, or out-of-scope failure. Commit test changes
separately and report; never push or integrate main.

Current evidence: native coordinator/OverlayState/lifecycle and 305 action-
feedback checks pass, including 27 coordinator scenarios. WidgetSdk passes
89/89. The bounded WidgetRuntime run passed six cases, then exceeded 180 seconds
inside `Retired sessions cannot publish notifications into replacements`.
Owned test PID 18064 retired; accepted PID 129420 was untouched. Bridge,
Spotify, Tier 2, and Tier 3 did not run. Preserve the three uncommitted test-only
matrix files; do not claim or commit the cumulative evidence until DLV-285 is
resolved and the remaining ordered runs pass.

## Assigned widgets diagnostic — DLV-285 retirement-test liveness

Owner/baseline: widgets lane at cumulative `0dec737`, preserving the incomplete
uncommitted test-only matrix in `WidgetSessionCoordinatorTests.cpp`,
`WidgetBridge.Tests/Program.cs`, and
`WidgetSdk.Tests/WidgetPresentationUpdateTests.cs`. This diagnostic owns only
`tests/WidgetRuntime.Tests` test/test-support files and bounded owned test
processes. No production, package, manifest, protocol, user state, accepted PID,
or reviewer document may change.

The existing focused Runtime executable timed out after six passing cases while
entering `Retired sessions cannot publish notifications into replacements`.
Determine the exact awaited signal/subcase with one isolated `--test-prefix`
run bounded to at most 60 seconds. All test-controlled gates must use finite
deadlines and release in `finally` so a failed assertion cannot strand a reader,
worker, or replacement session. Distinguish these outcomes:

- If the fixture waits for a transient or no-longer-guaranteed callback, correct
  only the fixture to assert the durable invariant: a retired publication never
  reaches replacement authority and cleanup/replacement completes boundedly.
- If production can deadlock, lose terminal completion, admit the retired
  notification, or cannot be tested without a new production hook, stop with
  exact evidence. Do not fix or instrument production under DLV-285.

After a test-only fixture correction, run the isolated prefix once and then the
bounded focused WidgetRuntime suite once. Commit only the DLV-285 runtime-test
correction separately. If both pass, resume the held cumulative evidence from
the first not-yet-run Bridge suite; do not rerun already green native/SDK groups.
Stop on any distinct failure. Never push.

Current DLV-285 evidence: the isolated run reproduced the unbounded notification
wait and retired owned PID 121492 after timeout. Source review proved the test
blocked the worker reader with `ManualResetEventSlim.Wait()` and could miss or
strand the transient callback. A test-only correction added finite labeled
deadlines and unconditional `finally` release. The corrected isolated run then
failed in 8.8 seconds before notification admission because its spawned test
worker exited before connecting with CLR exit `0xE0434352`; there was no matching
Application event. This is a distinct test-fixture launch gap, not evidence that
production admitted retired authority or deadlocked.

Authorize one further test-only diagnostic: capture the spawned worker's exact
managed exception using only existing process/test-support boundaries or a
top-level test-executable diagnostic. Do not alter production or suppress the
exception. Run the isolated prefix once under the same 60-second bound. Correct
only a proven fixture/startup defect, then follow the ordered DLV-285 runs above.
If the exception cannot be captured, requires production instrumentation, or
reveals a production failure, stop with exact evidence and leave integration
blocked.

DLV-285 completed as test-only commit `9365cb5`. The root causes were the
unbounded fixture wait and command-sandbox denial of the child worker's current-
user named pipe (`UnauthorizedAccessException`); the bounded unsandboxed run is
the valid process evidence. The isolated case passes 1/1 in 1.3 seconds and the
focused WidgetRuntime suite passes 78/78. Production and PID 129420 were not
changed.

## Assigned widgets correction — DLV-286 Bridge fixture reconciliation

Owner/baseline: widgets lane after test-only DLV-285 `9365cb5`, preserving the
three uncommitted cumulative matrix files. Own only
`tests/WidgetBridge.Tests/BridgeClientRegistryScenarios.cs` plus bounded Bridge
test processes.

The Bridge build stopped before tests because three existing calls at the
current lines 308, 326, and 338 still use the retired four-argument
`EstablishPresentationAsync` fixture form. Reconcile only those cold-establish
calls with the production signature's explicit `PresentationUpdateCapabilities.None`,
base sequence zero, session cancellation, and operation cancellation. Cold
establishment requests a checkpoint; `Current` is illegal with base zero and is
reserved for an exact positive retained base. Do not change the production
signature, hide a compiler error, add overload compatibility, or alter scenario
meaning.

Build/run the focused Bridge suite once. If green, commit only the DLV-286
fixture correction separately, then resume held evidence at Spotify followed by
the assigned Tier 2/Tier 3 order. Do not rerun native, SDK, Runtime, or Bridge.
Stop on any distinct failure. Never push.

The first DLV-286 attempt correctly built but used the planner's invalid
`Current`/base-zero pair and therefore failed the first scenario with
`Presentation updates require a positive current base sequence`; six later
failures were not diagnosed. That is an assignment error, not a production
defect. Correct the three calls to `None`/zero and run the focused Bridge suite
one further time. Stop on the first remaining distinct failure.

The corrected `None`/zero rerun resolved the illegal-capability failure and
completed 87/94. The first remaining failure is an older expectation inside
`LifecycleAndFirstSnapshotAreAtomic`: after injected first-snapshot failure it
waits for client disposal. Production DLV-280 `09c3f07` deliberately replaced
that destructive behavior with bounded prior-lifecycle compensation while
retaining the healthy client. The fixture also relies on the interface's
default false restore method rather than modeling the real process client's
restore contract. Commit only the three DLV-286 signature corrections now;
their compile and legal request semantics are independently established. Do not
claim the Bridge suite green.

## Assigned widgets correction — DLV-287 lifecycle-compensation fixture

Owner/baseline: widgets lane after test-only DLV-286, preserving DLV-285 and the
held cumulative matrix. Own only the affected fake client and assertions in
`tests/WidgetBridge.Tests/BridgeClientRegistryScenarios.cs`.

Update the fake to model `TryRestoreLifecycleStateAsync` under exact running
client/start-ordinal authority. Correct `LifecycleAndFirstSnapshotAreAtomic` to
prove the DLV-280 contract: snapshot failure does not commit the requested host
lifecycle or retire a healthy worker; the worker receives bounded compensation
to the prior lifecycle; residency/client generation stay retained; a later
establishment reuses that same current client and commits normally; stale or
changed generation cannot be restored. Do not weaken disposal/replacement
assertions in scenarios that actually retire a client.

Run the focused Bridge suite once. If green, commit only DLV-287, then resume
Spotify/Tier 2/Tier 3 without rerunning earlier green suites. Stop on the first
remaining distinct failure; never change production or push.

DLV-286 is committed separately as `81b8c23`. The first DLV-287 run built but
completed 87/94 because the updated scenario raised an invalidation and
immediately inspected its count. Current Bridge notifications publish through
the asynchronous per-widget lane. Await that widget's existing
`DrainNotificationsAsync` boundary before asserting delivery; do not add sleeps,
polling, synchronous production behavior, or change notification ownership.
Run Bridge one further time and stop on the first remaining distinct failure.

The corrected DLV-287 atomic lifecycle scenario now passes. The same Bridge run
continued to 86/94 and first failed at `Two unrelated full-trust applications
use one ordinary runtime` because its child worker exited during action
admission. Commit only DLV-287 now; do not claim the complete Bridge suite green.

## Assigned widgets diagnostic — DLV-288 full-trust test environment

Owner/baseline: widgets lane after separate DLV-287 commit, preserving DLV-285,
DLV-286, and the held cumulative matrix. Do not edit production or tests before
classification.

Run only the exact full-trust test prefix once, bounded to 60 seconds, outside
the command sandbox needed for current-user named-pipe child-process evidence.
This is authorized because DLV-285 already proved sandbox denial can produce a
false worker-launch failure. Capture exact exit/stdout/stderr/managed exception.
If the isolated unsandboxed test passes, classify the prior failure as an
environment artifact and resume the remaining Bridge suite once from the first
not-yet-proven boundary if the runner supports it; otherwise run the focused
Bridge suite one final time unsandboxed. If it fails, stop with exact evidence;
do not change production or tests. On a complete green Bridge result, resume
Spotify/Tier 2/Tier 3. Never push.

DLV-288 passed the exact prefix 1/1 in 3.4 seconds with exit 0 and no stderr or
managed exception, proving the earlier worker exit was command-sandbox
interference. The full unsandboxed Bridge run reached 88/94 and first failed in
`BudgetRefusalAndFailedStartReleaseReservations` because it expected a raw
`WidgetProcessAdmissionException`.

## Assigned widgets correction — DLV-289 typed admission fixture

Owner/baseline: widgets lane after DLV-287 `9f56c5b`, preserving all prior test
commits and the held cumulative matrix. Own only the affected expectation in
`BudgetRefusalAndFailedStartReleaseReservations`.

DLV-275 intentionally maps a client `WidgetProcessAdmissionException` through
`BridgeWidgetRequestException` with widget identity, safe failure code
`worker-admission-failed`, and the exact original exception as `InnerException`.
Update only the stale raw-exception assertion to prove that typed wrapper. Keep
all residency release/count, failed-start, catalog retirement, and retry
assertions unchanged.

Run the focused Bridge suite once outside the command sandbox. If green, commit
only DLV-289, then resume Spotify/Tier 2/Tier 3. Stop on the first remaining
distinct failure; never change production or push.

The DLV-289 scenario now passes. The same Bridge run reached 89/94 and first
failed in `Snapshots and hover quick actions cross bridge`: render styles
contained six nodes while three old fixture assertions still expect five.
Commit only DLV-289 now; do not claim Bridge green.

## Assigned widgets correction — DLV-290 Bridge style fixture count

Owner/baseline: widgets lane after separate DLV-289 commit, preserving all
prior test commits and the held cumulative matrix. Own only the three stale
render-style count assertions and directly related semantic assertions in
`tests/WidgetBridge.Tests/Program.cs`.

Accepted DLV-276 added the `committed-text-status` node to `BridgeTestWidget`,
so its ordinary snapshot/update/session style maps now contain six exact node
IDs. Update the three pre-DLV-276 count expectations from five to six and assert
the required committed-status node is present where the map is inspected. Do
not replace exact validation with a lower bound, remove other style assertions,
or change production/widget content.

Run the focused Bridge suite once outside the command sandbox. If green, commit
only DLV-290, then resume Spotify/Tier 2/Tier 3. Stop on the first remaining
distinct failure; never push.

The DLV-290 assertions pass. The same Bridge run reached 91/94 and first
stopped in the held cumulative scenario `Exact-base divergence converges
through one full checkpoint`, where it timed out waiting for an invalidation.
Commit only DLV-290 now; do not claim Bridge green.

## Assigned widgets correction — DLV-291 exact-base fixture action

Owner/baseline: widgets lane after separate DLV-290 commit, preserving all
prior test commits and the held cumulative matrix. Own only the action and
semantic assertions in the held `ExactBaseDivergenceConvergesThroughCheckpoint`
scenario in `tests/WidgetBridge.Tests/Program.cs`.

The scenario copied `nested` / `nested-command` and `scoped-action` semantics
from the Runtime fixture, but this Bridge suite runs `BridgeTestWidget`, which
has no `nested` action handler and no `scoped-action` node. Therefore the first
event wait cannot complete and the later node assertion is impossible. Replace
only those fixture-incompatible actions/assertions with two different,
deterministic state-changing actions and matching nodes already supported by
`BridgeTestWidget`. Preserve the scenario's exact initial base, bridge-ahead
update, stale-base rejection, base-zero recovery checkpoint, post-recovery
ordinary update, and strictly forward sequence assertions. Do not add sleeps,
polling, production hooks, or production changes.

Run the exact scenario prefix once outside the command sandbox. If it passes,
run the focused Bridge suite once. If Bridge is green, retain DLV-291 inside
the held cumulative matrix and resume Spotify/Tier 2/Tier 3; stop on the first
remaining distinct failure. Never push.

DLV-290 is committed separately as `b14dfdc`. The corrected DLV-291 exact-base
scenario passes 1/1. The same full Bridge run reached 92/94 and first failed in
`Protocol-v19 virtual collection window crosses worker and bridge`: after one
uncorrelated invalidation read the requested snapshot still contained the
initial 32-item generation rather than the completed 64-item appended window.

## Assigned widgets correction — DLV-292 correlate virtual completion

Owner/baseline: widgets lane after DLV-290 `b14dfdc`, preserving DLV-291 and
the held cumulative matrix. Own only `VirtualCollectionWindowCrossesBridge`
and, only if necessary, a narrowly reusable Bridge-test event wait helper in
`tests/WidgetBridge.Tests/Program.cs`.

The fixture assumes the next invalidation after pagination proves page-load
completion. Activation and cursor loading can leave earlier loading/ready
invalidations queued, while action acknowledgement means only admission. The
test therefore consumed an older event and inspected generation 1. Replace
that timing assumption with one bounded event-driven wait for the durable
condition: a successful Snapshot whose virtual window has request generation
2, 64 retained children, exact `Append` direction, and the existing logical
bounds. Correlate through events and state; do not sleep, time-poll, weaken the
64-item or generation assertions, or change SDK/runtime/Bridge production.

Run the exact virtual-window prefix once outside the command sandbox. If it
passes, run the focused Bridge suite once. If Bridge is green, retain DLV-292
inside the held cumulative matrix and resume Spotify/Tier 2/Tier 3; stop on the
first remaining distinct failure. Never push.

DLV-292 failed its exact prefix 0/1. After the first post-action invalidation,
the requested snapshot still held the initial 32-item generation-1 window; no
subsequent invalidation arrived within the existing four-second bounded read.
The operation was admitted while the widget remained Visible, so the original
claim that the fixture merely consumed an older queued notification is not
sufficient. Keep the DLV-292 edit uncommitted and do not weaken its durable
64-item/generation-2 requirement.

## Assigned widgets diagnostic — DLV-293 pagination completion disposition

Owner/baseline: widgets lane with the held DLV-291/292 matrix. Own only the
virtual Bridge fixture/scenario and a narrowly reusable Bridge-test event
reader if needed. This is a disposable test-only diagnostic, not an SDK,
runtime, Bridge, package, or product correction.

Instrument `VirtualCollectionBridgeWidget` through test-only state so one exact
run can distinguish these outcomes after the admitted near-end action:

- pagination action did not match its action/source IDs;
- the resource operation was rejected, cancelled, superseded, or failed;
- the resource operation succeeded and reached the 64-item generation-2
  `Append` state, but the ordinary completion invalidation was not delivered;
- an action/runtime failure event arrived instead of an invalidation; or
- the durable state was published and the existing client event reader lost or
  misclassified its notification.

Expose only bounded, deterministic fixture evidence (for example, matched
state, operation admission/completion status, and the next raw event type and
revision). A diagnostic-only completion signal may invalidate a test status
node so the cross-process result is observable, but it must be clearly
separated from the production invalidation under investigation and must not be
mistaken for the passing condition. Do not sleep, time-poll, extend deadlines,
change product code, or turn the diagnostic into a committed workaround.

Run only the exact virtual-window prefix once outside the command sandbox and
report the complete ordered evidence. Stop after classification; do not run the
full Bridge/Spotify/Tier 2/Tier 3 sequence and do not commit DLV-291–293. Never
push.

DLV-293 produced no pagination disposition. Its single exact run stopped when
the disposable operation diagnostic was encoded as one 118-character style
class, exceeding the existing exact 64-character style-class limit; Bridge
correctly rejected that snapshot. This is a deterministic diagnostic-fixture
defect, not product evidence and not authority to change the protocol bound.

## Assigned widgets diagnostic correction — DLV-294 bounded status encoding

Owner/baseline: widgets lane with the held DLV-291–293 matrix. Own only the
DLV-293 diagnostic encoding/assertions in
`tests/WidgetBridge.Tests/Program.cs`. Preserve its production-independent
classification design and raw-event ordering.

Replace the oversized compound operation class with separate exact bounded
tokens, each at most 64 characters, for match, admission, completion, resource
status, retained count, request generation, and window change. Validate every
token before the run and assert each semantic fact separately; do not truncate,
hash away, omit, or loosen evidence. Keep diagnostic completion invalidation
distinguishable from ordinary invalidation and do not let it satisfy the
ordinary completion assertion.

Run the exact virtual-window prefix one final time outside the command sandbox.
If it reaches classification, report the ordered raw revisions plus all status
tokens and stop without committing or running other suites. If another
diagnostic artifact blocks classification, stop this diagnostic campaign and
report source-level disposition/residual risk; do not redesign or rerun the
harness again. No product/protocol change, deadline extension, sleep, polling,
package/process/state change, integration, or push.

DLV-294 classified the exact path. Baseline revision was 1; raw Bridge
invalidation revisions arrived in order as 2, 3, 4, and 5. Revision 2 was
ordinary Ready/32/generation-1/Replace, revision 3 ordinary
LoadingAdjacent/32/generation-1/Replace, revision 4 ordinary
Ready/64/generation-2/Append, and revision 5 the explicitly diagnostic
Ready/64/generation-2/Append signal. Separate operation tokens proved exact
action/source match, `Started` admission, `Succeeded` completion, Ready state,
64 retained rows, generation 2, and Append. The run's only red assertion was
the diagnostic's incorrect expectation of `Enqueued` instead of the resource
operation's correct `Started` admission. Therefore production did not lose or
misclassify the completion invalidation.

## Assigned widgets correction — DLV-295 deterministic virtual fixture

Owner/baseline: widgets lane with held DLV-291–294 changes. Own only
`VirtualCollectionWindowCrossesBridge`, `VirtualCollectionBridgeWidget`, and
the DLV-293/294 disposable diagnostic additions in
`tests/WidgetBridge.Tests/Program.cs`.

Remove every disposable diagnostic status class, event trace, raw-event helper,
extra diagnostic invalidation, and diagnostic assertion. Retain the DLV-292
bounded event/state wait and its exact 64-item, generation-2, Append, range, and
boundary assertions. Make the virtual test widget's action handler retain and
await the matched resource operation completion, and fail the action on any
non-`Succeeded` result. This is fixture synchronization only: action
acknowledgement remains admission, and the scenario must still correlate the
ordinary invalidation to durable state rather than treat acknowledgement as
completion. Do not change `WidgetCursorResource`, runtime, Bridge, production
widgets, public contracts, deadlines, or bounds.

Run the exact virtual-window prefix once outside the command sandbox. If green,
run the focused Bridge suite once. If Bridge is fully green, retain DLV-295 in
the held cumulative matrix and resume Spotify/Tier 2/Tier 3 in the assigned
order; stop at the first distinct failure. Commit DLV-291/292/295 only after the
complete required evidence is green. Never push.

DLV-295 built cleanly but its exact prefix again timed out awaiting the first
ordinary invalidation after acknowledged pagination. The awaited test resource
operation was matched and required to succeed, and every disposable diagnostic
signal had been removed. Together with DLV-294—where adding diagnostic timing
made ordinary revisions 3/4 observable—this is timing-sensitive production
evidence, not a stable fixture-only explanation. Stop the diagnostic campaign.

Source review identifies the owning gap in `WidgetWorkerServer`: widget
invalidation and action-failure callbacks each launch an untracked
fire-and-forget send against the shared writer gate; their tasks, ordering,
completion, and exceptions have no notification owner, and expected connection
exceptions are swallowed. This can lose or delay the only signal that tells the
host to pull the completed generation-2 window.

## Review-clean widgets production — DLV-296 owned worker notification lane

Mode: physical-first production/build, user verdict, then focused tests.
Owner/baseline: widgets lane at DLV-290 `b14dfdc` plus its accepted cumulative
production ancestry. Preserve the three dirty held test-matrix files exactly;
stage and commit only production/runtime files. Build the exact production
commit from a clean isolated tree so held tests cannot affect provenance.

Replace `WidgetWorkerServer`'s fire-and-forget invalidation/action-failure sends
with one private runtime-owned, bounded, ordered notification lane and one
tracked pump. The lane must:

- retain only the latest queued invalidation revision while preserving ordered,
  non-coalescible action failures under an explicit small bound;
- serialize notification writes through the existing channel/writer owner with
  no unobserved tasks or concurrent notification senders;
- define exact admission, coalescing, closed/full behavior and never silently
  convert a current accepted completion into an unowned task;
- close, cancel, and drain boundedly during worker shutdown before channel and
  subscriptions are released, without hanging a faulty/disconnected worker;
- surface one safe terminal transport failure to the existing request-loop
  owner rather than swallowing it or creating a second restart/lifecycle owner;
  and
- preserve revision monotonicity, action-failure ordering, last-valid
  presentation, active lifecycle, and current wire payloads.

Do not change WidgetSdk resource semantics, public/wire protocol or version,
Bridge/native host admission, package code, widget identity, collection bounds,
timeouts, lifecycle authority, or add a Spotify/Games special case. Report a
before/after responsibility map and prove the new lane is the only notification
task owner; do not add a generic framework abstraction beyond this boundary.

Before user verdict, perform source review only, commit one production-only
DLV-296 milestone, and build that exact commit once as Release with tests
skipped. Do not author, edit, or run tests; do not install/select packages,
change credentials/account/provider/configuration state, or launch/terminate
processes. Report exact commit, diff, build command/result, artifact hashes, and
clean isolated build provenance. Stop for planner review and visible launch.

DLV-296 commit `3922b58dc6456be17442926f2a0c7257d3b97e11`
meets that pre-verdict gate. The reviewed delta is exactly four runtime
production files: one assembly-private notification-lane owner, its narrow
`WidgetWorkerServer` integration, and the two compile-item projections. The
lane owns one tracked pump, one latest queued invalidation, FIFO action failures
bounded at eight, closed/full admission, and terminal failure observation by
the existing request loop. The shared channel and writer gate remain the sole
wire serialization authority. No protocol, payload, package, widget, host,
collection-bound, or lifecycle-owner change is present. The three held test
files remain unstaged and unchanged.

The exact detached tree
`C:\Users\dwive\.codex\worktrees\563c\dlv296-exact-3922b58` is clean at that
commit. Its single documented `Release -SkipTests` build passed with packaging
enabled. `WidgetApplicationRuntime.dll` SHA-256 is
`EBAC3851ED228681C6C5F43BC9F0223AFE53E5795D25752390F2CED663E9FD07`;
`WidgetBridge.dll` is
`4B3C12837B94368BD8C21048C960B0D84E23591990D1B5523F03A73767594B81`;
and `WidgetWorkerHost.dll` is
`2454DCDD9E7A5B2AFBF70BA6FA75F7C44B65DF3F2101DEC3392613313D83CA57`.
The initial launch attempt was denied before mutation pending fresh explicit
approval. After the user supplied that approval, PID 129420's executable
identity was reverified and it exited cooperatively through `WM_CLOSE`. Exact
candidate PID 83788 then launched visibly with the expected executable path and
hash. It remained responsive, elected the production process owner, started its
Bridge, and admitted Settings on sequence 1 without an immediate startup error.
The user physically accepted DLV-296. PID 83788 remains the accepted running
artifact; do not rebuild or relaunch solely for the following test/doc delta.

After planner review, launch the exact coherent Release as an unaccepted
candidate for rapid Spotify/Games & Apps cycling and virtual paging. Only after
the user accepts may DLV-296 receive focused regression tests for coalescing,
failure ordering, disconnect/shutdown drain, and the formerly intermittent
Bridge pagination path. Never push.

## Assigned widgets evidence — DLV-297 notification convergence

Mode: test-only after accepted DLV-296 production. Owner/baseline: widgets lane
at accepted production commit
`3922b58dc6456be17442926f2a0c7257d3b97e11`, retaining the three existing dirty
held test-matrix files until their exact roles are reconciled. Do not change
production/runtime source, packages, manifests, public/private protocols,
timeouts, bounds, configuration, credentials, installed/selected widgets, or
processes. Accepted PID 83788 must remain running and untouched.

Add deterministic focused evidence that proves the production notification
owner rather than relying on sleeps or diagnostic timing:

- multiple invalidations admitted while a send is blocked retain exactly the
  newest queued forward revision and produce no concurrent senders;
- action-failure notifications remain FIFO through mixed invalidations, with
  exact closed/full behavior at the eight-entry bound;
- a terminal transport failure is observed once by the existing request-loop
  owner, with no unobserved task, duplicate lifecycle authority, or hidden
  restart;
- graceful close drains queued notifications before subscription/channel
  release, while disconnect/cancellation remains bounded; and
- the formerly intermittent Bridge virtual-pagination scenario observes the
  completed forward window without disposable instrumentation, polling races,
  or package-specific behavior.

Use existing production fixtures and contracts. Do not add reflection access,
test-only production hooks, weakened assertions, enlarged timeouts, or a second
notification model. First reconcile the three held files and report which
require changes for DLV-297. Run only the smallest directly affected test
prefixes once. If they pass, commit one test-only DLV-297 milestone and stop for
planner review before resuming broader cumulative DLV-278–283 convergence
evidence. If any focused case exposes a product failure, preserve the evidence
and stop; do not repair production under this assignment. Never push.

## Queued platform production — DLV-284 explicit publication transaction model

Status: queued, not assigned. It becomes assignable only after the cumulative
evidence above passes and DLV-278 through DLV-283 production/tests are reviewed
and integrated. No new virtualization feature may precede it.

Replace publication semantics inferred from `allowUpdate`, base-zero/nonzero,
and recovery-side conditions with one private typed transaction model carried
through SDK/runtime, bridge, and host boundaries. Distinguish at least
`IncrementalUpdate`, `OrdinaryCheckpoint`, and `RecoveryCheckpoint`, with exact
legal base, origin authority, retry policy, and admission result. Preserve
compatibility deliberately; stop for any required public wire or third-party
SDK break.

Keep a single final transaction owner through admission and commit. Express
legal combinations in one table-driven policy over retained host sequence,
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

## Ordered queues

1. Widgets test queue: execute test-only DLV-297 focused notification and
   pagination convergence evidence against accepted DLV-296 production.
2. Widgets cumulative test queue: after independent DLV-297 review, resume and
   commit the cumulative DLV-278–283 evidence only if all remaining runs pass.
3. Reviewer integration queue: independently review all evidence; integrate the
   accepted production/test chain into local main only if all required evidence
   passes.
4. Platform production queue: assign DLV-284 after integration, before any new
   virtualization feature.
5. DLV-248 remains deliberately deferred until explicit user promotion.

There is no other Ready production work in either standing lane.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Exact mapping decisions are approved and frozen; Store, domain, trademark, and GitHub availability remain external/manual. |
| DLV-265 | Complete and integrated through `bb8234f`. |
| DLV-276 | Complete and integrated through `ca967e6`. |
| DLV-277 | Complete and integrated through `c21ad02`. |
| DLV-278–283/270 | Cumulative production `0dec737` is physically accepted; focused/Tier 2/Tier 3 convergence evidence is assigned before integration. |
| DLV-285 | Test-only correction `9365cb5` passes isolated 1/1 and focused Runtime 78/78; command-sandbox named-pipe denial was not a product failure. |
| DLV-286 | Test-only `81b8c23` reconciles the three cold Bridge fixtures to explicit None/base-zero checkpoint semantics; Bridge remains red on DLV-287. |
| DLV-287 | Assigned test-only correction for stale destructive-retirement expectations after DLV-280 introduced bounded lifecycle compensation and worker retention. |
| DLV-288 | Assigned isolated unsandboxed classification of the full-trust child-worker exit before any production/test correction. |
| DLV-289 | Assigned test-only update of one pre-DLV-275 raw admission-exception expectation to the current typed Bridge failure contract. |
| DLV-290 | Assigned test-only update of three Bridge style-map counts after accepted DLV-276 added the committed-text-status node. |
| DLV-291 | Assigned correction of one held convergence scenario that used Runtime-only actions against BridgeTestWidget. |
| DLV-292 | Held after exact 0/1 disproved the simple stale-event explanation; generation stayed 1 and no later invalidation arrived. |
| DLV-293 | No product evidence: its disposable compound diagnostic class violated the existing 64-character style-class bound. |
| DLV-294 | Classified ordinary revisions 2–4 through the Bridge and exact successful generation-2 Append; revision 5 was diagnostic only. |
| DLV-295 | Reproduced missing ordinary invalidation after clean fixture synchronization; diagnostic campaign stopped. |
| DLV-296 | Production `3922b58` is source-reviewed, exact-build clean, and physically accepted as running PID 83788. |
| DLV-297 | Assigned test-only focused notification convergence evidence against accepted DLV-296; no production, package, process, integration, or push changes. |
| DLV-284 | Queued, not assigned until cumulative integration. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Current acceptance record

- DLV-280 established exact retained host base across lifecycle establishment.
- DLV-281 serialized completion admission under current lifecycle/presentation
  authority.
- DLV-282 added typed bounded one-shot stale-base resynchronization.
- DLV-283 added unforgeable recovery provenance, exact retained-origin and
  forward-sequence checks, plus strict all-`Replace` fresh-baseline admission.
- Cumulative commit `0dec737` with Spotify 0.3.12 was physically accepted on
  PID 129420 after rapid navigation no longer reproduced the stale virtual
  collection transition error.
