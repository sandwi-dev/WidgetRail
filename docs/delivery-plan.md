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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-327 fast-forwarded to `676cd76` and produced the correct one-file plural-API migration, but its native gate stopped at a fixture viewport not positioned at the requested After edge. Execute bounded test-only DLV-328 below; DLV-284 remains queued and unassigned. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Idle at clean cumulative test tip `676cd76`; every managed gate and focused DLV-326 Runtime gate is green. Preserve PID 126208 and all product state; do not begin new work, integrate, rebuild/relaunch, or push. |

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

DLV-297 stopped without a commit after exactly three one-time focused runs.
The direct notification-lane prefix passed 1/1 and proved one sender, latest
queued invalidation coalescing, mixed FIFO failure order, exact eight-entry
full/closed behavior, and bounded graceful/cancelled drain. The real
`WidgetWorkerServer` disconnect prefix passed 1/1 and observed one terminal
`IOException`, one initialization/destruction, and no post-terminal subscriber
effect. The existing Bridge virtual-pagination prefix failed 0/1, again timing
out at `Program.cs:3371` while awaiting the first ordinary post-action
invalidation. No disposable diagnostics, polling, timeout change, production
edit, rerun, or process/state mutation occurred. Direct lane success rejects
DLV-296's former fire-and-forget transport as a sufficient explanation, but
does not yet identify which next boundary loses the notification.

## Assigned widgets diagnosis — DLV-298 notification boundary bisection

Mode: diagnostic test-only against accepted DLV-296. Preserve all five current
dirty/untracked test files exactly as starting evidence, including the two new
Runtime test files from DLV-297. Do not commit DLV-297 yet. Do not change
production/runtime source, packages, manifests, protocols, bounds, timeouts,
installed/configured state, or processes. PID 83788 remains accepted and must
not be touched.

Add deterministic production-boundary tests that locate the first missing
handoff without logging or timing-dependent probes. Use the real action and
cursor-resource path and bisect these owners in order:

1. `WidgetWorkerServer`: real Action admission and SDK action consumer/resource
   completion must produce a forward invalidation on the worker pipe;
2. `WidgetProcessClient`: that worker notification must be read, admitted under
   the current process session, and raise exactly one current `Invalidated`
   publication;
3. `BridgeClientRegistry`: the visible current registration must admit that
   publication into its existing notification lane and invoke the configured
   Bridge publisher exactly once; and
4. Bridge server/client framing: an admitted registry publication must reach
   the existing client event queue without relying on another request.

Use existing assembly-internal access and fixtures only—no reflection,
production hook, new protocol, sleeps, polling, timeout enlargement, worker
stdout/file diagnostics, or package-specific behavior. Give each new boundary
one uniquely named focused prefix and run each at most once, stopping at the
first red boundary. Do not rerun the already-red full pagination prefix. Report
the exact last-green/first-red ownership edge and source-review explanation.
Do not implement a production correction or commit under DLV-298; stop for
planner review. Never push.

DLV-298 completed without a commit. Its four uniquely prefixed boundaries each
passed 1/1: real worker Action through cursor completion to the worker pipe;
current-session `WidgetProcessClient` admission to exactly one invalidation;
visible current registry admission through its notification lane to the
configured publisher; and admitted registry publication through Bridge framing
to the client event queue without another request. Serialized MSBuild compiled
the affected projects; the first parallel build attempt aborted with zero
diagnostics and consumed no focused prefix. No production, protocol, package,
timeout, process, or configured-state change occurred. There is therefore no
first-red individual handoff: the last green edge is registry publication to
the Bridge client event queue, and the only known red remains the composite
virtual-pagination scenario.

## Assigned widgets diagnosis — DLV-299 composite first-event classification

Mode: diagnostic test-only against accepted DLV-296. Preserve the six current
dirty/untracked test files as the complete DLV-297/298 evidence set; do not
commit them yet. Do not edit production/runtime source, packages, manifests,
protocols, bounds, timeouts, installed/configured state, or processes. PID
83788 remains accepted and must not be touched.

First perform a source-only comparison between the passing DLV-298 seams and
the held `VirtualCollectionWindowCrossesBridge` path, including widget identity,
lifecycle, source element/action identity, action admission versus execution,
cursor busy/completion revisions, registry generation, and the Bridge test
client's event queue. Then make the smallest test-harness-only change needed to
observe the first post-action event without filtering it by expected type. The
single focused case must classify exactly one of these outcomes:

1. an invalidation reaches the client, proving the formerly red wait was not a
   reproducible missing handoff;
2. an action-failure event reaches the client, with its bounded existing
   payload identifying the action/resource failure; or
3. no event reaches the client before the unchanged existing deadline, leaving
   the first red above the individually green seams.

Do not add logging, reflection, production hooks, sleeps, polling, repeated
stress loops, timeout enlargement, stdout/file diagnostics, or a package
special case. Do not weaken the durable generation-2 Append assertions and do
not request snapshots merely to poll for completion. Compile with serialized
MSBuild, run the one uniquely named DLV-299 prefix at most once, and stop with
the exact first event/outcome plus source-review ownership explanation. Do not
implement a production correction or commit under DLV-299. Never push.

DLV-299 completed without a commit. Source comparison found the same current
`virtual` / `virtual.instance` identity and registry generation, established
Visible lifecycle, exact generated near-end action and `virtual.scroll` source,
and Enqueued admission preceding asynchronous cursor execution. Its one-shot
prefix compiled with serialized MSBuild and passed 1/1: the first post-action
event was an `Invalidation` for `virtual`, request ID zero, at a revision newer
than the initial Ready revision. The former no-event result did not reproduce,
and no stable red boundary exists within the WorkerServer, ProcessClient,
registry, or Bridge-framing handoffs. Production and PID 83788 remained
untouched.

## Assigned widgets evidence — DLV-300 final notification convergence

Mode: test-only against accepted DLV-296. Preserve the six current dirty and
untracked files; they contain held cumulative evidence plus DLV-297–299 work.
Do not edit production/runtime source, packages, manifests, protocols, bounds,
timeouts, installed/configured state, or processes. PID 83788 remains accepted
and must not be touched.

Fold DLV-299's raw first-event classification into the existing
`VirtualCollectionWindowCrossesBridge` evidence instead of retaining a second
permanent diagnostic scenario. The permanent case must fail immediately and
with the existing bounded payload if the first relevant post-action event is an
action failure; an invalidation may drive the next snapshot request. Continue
only from actual invalidations, not timers or speculative snapshot polling,
until the existing bounded observation limit reaches the durable generation-2
64-item Append or fails. Preserve the exact total count, first index,
before/after, generation, Append, and 96-item host-bound assertions. Remove
DLV-only naming and disposable classification text once folded.

Do not add logging, reflection, production hooks, sleeps, polling, repeated
stress loops, timeout enlargement, stdout/file diagnostics, package special
cases, weakened assertions, or a second notification model. Compile the
affected project with serialized MSBuild and run the one permanent virtual
collection prefix exactly once. If it fails, preserve all evidence and stop
without production correction or commit. If it passes, source-audit the final
DLV-297/298 notification evidence, remove only superseded diagnostic code, and
commit one coherent test-only milestone containing only notification evidence;
leave the earlier held cumulative DLV-278–283 hunks uncommitted. Report the
exact committed paths/hunks and remaining dirty evidence. Never push.

DLV-300 stopped red without a commit. Serialized compilation passed, but the
one permanent prefix failed 0/1 on the first raw post-action read: after an
Enqueued Bridge action acknowledgement, neither invalidation nor action failure
arrived before the unchanged four-second deadline. The durable generation-2
Append assertions remain intact and were not reached. This reproduces
intermittent composite action-execution/notification liveness, not a stable
failure in any individually tested DLV-298 transport handoff. No production,
package, process, or configured-state input changed.

## Assigned widgets diagnosis — DLV-301 lifecycle-first cursor action

Mode: diagnostic test-only against accepted DLV-296. Preserve all six current
dirty/untracked evidence files and do not commit. Do not edit production/runtime
source, packages, manifests, protocols, bounds, timeouts, installed/configured
state, or processes. PID 83788 remains accepted and must not be touched.

The DLV-298 direct worker cursor probe loads its first page inside the admitted
action, while the red composite widget loads on Visible activation and later
paginates from an already-Ready resource. Add one exact direct-worker probe for
that missing interleaving: a test widget whose `OnActivatedAsync` starts the
same synchronous bounded virtual cursor load without awaiting it; the host must
establish Visible, consume the initial forward invalidation, request and verify
the Ready generation-1 window, then admit the exact generated near-end action
and observe the first post-action worker-pipe event. Classify invalidation,
bounded action failure, or no event at the unchanged existing deadline. Keep
the action queue, cursor resource, lifecycle, worker server, and framed pipe
real; use no Bridge or registry in this probe.

Do not add logging, reflection, production hooks, sleeps, polling, repeated
stress loops, timeout enlargement, stdout/file diagnostics, package special
cases, or a second notification model. Reuse the current DLV-298 runtime fixture
and exact production APIs. Compile with serialized MSBuild, run one uniquely
named DLV-301 prefix at most once, and stop with the exact last-green/first-red
ownership edge. Do not implement a correction or commit under DLV-301. Never
push. If it passes, the next planned bisection is the same lifecycle-first
cursor sequence through `WidgetProcessClient`; if it fails, the next work must
target the SDK action/resource/worker boundary instead.

DLV-301 stopped red without a commit. Visible activation completed its initial
cursor load, the worker pipe delivered the initial invalidation, and the Ready
generation-1 Replace snapshot had the expected four items, total 12, index zero,
and exact boundaries. The generated near-end action/source was admitted as
Enqueued, but neither invalidation nor action failure reached the same worker
pipe before the unchanged deadline. This removes Bridge, registry, and
`WidgetProcessClient` from the failing reproduction. The last green edge is
`WidgetWorkerServer` admission into the real widget action queue; the first red
is queued execution / already-Ready cursor pagination to SDK invalidation or
action-failure publication. Production and PID 83788 remained untouched.

## Assigned widgets diagnosis — DLV-302 queued-action execution markers

Mode: diagnostic test-only against accepted DLV-296. Preserve all six current
dirty/untracked evidence files and do not commit. Do not edit production/runtime
source, packages, manifests, protocols, bounds, timeouts, installed/configured
state, or processes. PID 83788 remains accepted and must not be touched.

Extend only the DLV-301 in-process test widget with asynchronous
`TaskCompletionSource` markers using `RunContinuationsAsynchronously` for:

1. entry into `OnActionAsync` for the exact generated pagination action;
2. successful `TryHandlePagination` admission and its returned operation status;
3. terminal completion of that cursor operation, including its existing bounded
   result status and exception type when present.

After the real worker action acknowledgement, observe those markers and the
first raw worker-pipe event under one unchanged existing deadline, without
changing their execution order. The one focused outcome must identify the last
completed marker and whether an invalidation/action failure arrived. This is
test-fixture state inspection only; do not signal or unblock production work
from a marker, and do not replace the real action queue, cursor resource,
lifecycle, worker server, or framed pipe.

Do not add production hooks, logging, reflection, sleeps, polling, stress loops,
timeout enlargement, stdout/file diagnostics, package special cases, or a
second action/notification model. Compile with serialized MSBuild, run one
uniquely named DLV-302 prefix at most once, and stop with the exact last-green
marker/first-red edge. Do not implement a correction or commit under DLV-302.
Never push.

DLV-302 stopped without a commit and invalidated DLV-301 as product evidence.
The real worker acknowledged Enqueued and the exact generated action entered
`OnActionAsync`, but the test widget returned before `TryHandlePagination`
because it routed only the older `diagnostic.cursor.forward` wrapper ID. The
cursor-admission and terminal-operation markers were therefore never reached.
This is a test-fixture routing mismatch before the SDK cursor resource, not
evidence of a production action-queue deadlock. The one prefix ran 0/1 only
because the deliberately uncorrected fixture could not reach its assigned
boundary. Production and PID 83788 remained untouched.

## Assigned widgets evidence — DLV-303 exact cursor-action routing

Mode: test-only against accepted DLV-296. Preserve all six current dirty and
untracked files; do not commit. Do not edit production/runtime source, packages,
manifests, protocols, bounds, timeouts, installed/configured state, or
processes. PID 83788 remains accepted and must not be touched.

Correct only the DLV-301/302 test widget's action routing so the exact
SDK-generated before/after pagination action and exact viewport source reach
`TryHandlePagination`. Preserve the existing wrapper action used by the older
DLV-298 probe without conflating the two paths. Keep the passive DLV-302 markers
long enough to prove action entry, cursor admission, terminal success, and the
first forward worker-pipe invalidation for the lifecycle-first sequence. Remove
the markers and DLV-only diagnostic names after that permanent direct-worker
case has equivalent durable assertions.

Do not change the action ID, source ID, cursor result, or expected event merely
to make the test pass. Do not add production hooks, logging, reflection, sleeps,
polling, stress loops, timeout enlargement, stdout/file diagnostics, package
special cases, or a second model. Compile with serialized MSBuild and run the
one corrected lifecycle-first direct-worker prefix exactly once. If red,
preserve evidence and stop without correction or commit. If green, stop without
commit and report the exact marker/event chain; the next bisection will carry
the same sequence through `WidgetProcessClient`. Never push.

DLV-303 completed green without a commit. After the fixture routing correction,
its one lifecycle-first direct-worker prefix passed 1/1: Enqueued worker ACK;
exact generated near-end action and `diagnostic.scroll` source entered
`OnActionAsync`; `TryHandlePagination` handled it with Started admission; the
cursor operation completed Succeeded without exception; and the first
post-action worker event was a forward invalidation. The older DLV-298 wrapper
route remains separate. Temporary markers were folded into permanent completion
assertions and the cleaned source compiled successfully. Production and PID
83788 remained untouched.

## Assigned widgets diagnosis — DLV-304 lifecycle-first ProcessClient cursor

Mode: diagnostic test-only against accepted DLV-296. Preserve all six current
dirty/untracked evidence files and do not commit. Do not edit production/runtime
source, packages, manifests, protocols, bounds, timeouts, installed/configured
state, or processes. PID 83788 remains accepted and must not be touched.

Carry the exact corrected DLV-303 lifecycle-first cursor sequence across the
real external-worker `WidgetProcessClient` boundary. Use its existing test
process launch seam to select the same test widget; do not add a production
selector or hook. Establish Visible, observe the initial current-session
invalidation, request and verify the Ready generation-1 Replace window, admit
the exact generated near-end action/source, then observe the first
current-session publication as either a forward invalidation or bounded action
failure. On invalidation, request once and verify the durable generation-2
Append window and exact item/count/index/boundary constraints. Also assert the
same worker session/start ordinal remains current throughout.

Do not add logging, reflection, production hooks, sleeps, polling, stress loops,
timeout enlargement, stdout/file diagnostics, package special cases, or a
second action/notification model. Compile with serialized MSBuild, run one
uniquely named DLV-304 prefix at most once, and stop with the exact last-green /
first-red ownership edge. Do not implement a correction or commit under
DLV-304. Never push. If green, the next bisection will carry the same exact
sequence through current visible registry admission and Bridge framing.

DLV-304 stopped without a commit, but its notification and session boundaries
are green. Visible external worker startup, initial current-session invalidation,
generation-1 Replace, exact generated near-end action/source, Enqueued admission,
forward post-action invalidation, unchanged start ordinal/PID, and the eight-item
second window all succeeded. Its sole red assertion expected generation-2
Append from `GetSnapshotAsync`; that API intentionally requests a base-zero full
checkpoint, and `NormalizeVirtualWindowReentry` correctly converts a directional
window lacking an exact previous base to Replace. This is a test expectation /
API-selection mismatch, not a notification or session-authority failure.
Production and PID 83788 remained untouched.

## Assigned widgets evidence — DLV-305 exact-base ProcessClient update

Mode: test-only against accepted DLV-296. Preserve all six current dirty and
untracked files and do not commit. Do not edit production/runtime source,
packages, manifests, protocols, bounds, timeouts, installed/configured state,
or processes. PID 83788 remains accepted and must not be touched.

Correct only DLV-304's post-invalidation presentation request. Retain the exact
generation-1 checkpoint sequence and presentation generation, then use the
existing exact-base `WidgetProcessClient.GetPresentationAsync` incremental path
with current capabilities and `requireCheckpoint: false`. Prove the returned
update is based on that exact sequence, carries generation-2 Append semantics,
materializes exact items 0–7 with total 12, first index zero, no before cursor,
and an after cursor, and remains in the same worker start ordinal/PID. Separately
retain one assertion that base-zero `GetSnapshotAsync` returns the same durable
eight items with Replace semantics, so both legal contracts are explicit.

Do not change production behavior or weaken normalization. Do not add hooks,
logging, reflection, sleeps, polling, stress loops, timeout enlargement,
stdout/file diagnostics, package special cases, or a second model. Compile with
serialized MSBuild and run one uniquely named corrected DLV-305 prefix exactly
once. If red, preserve evidence and stop without correction or commit. If green,
stop without commit and report both exact-base Append and base-zero Replace
results; the next correction will apply the same legal request semantics to the
permanent Bridge convergence case. Never push.

DLV-305 completed green without a commit. Its one corrected prefix passed 1/1.
The exact-base request retained the generation-1 checkpoint sequence and
presentation generation, returned an atomic update based on that exact sequence,
and carried generation-2 Append which materialized exact items 0–7, total 12,
first index zero, no before boundary, and an after boundary. An independent
base-zero checkpoint returned the same durable generation-2 items with Replace,
as required. The worker stayed at start ordinal one and the same PID throughout.
Production and PID 83788 remained untouched.

## Assigned widgets evidence — DLV-306 permanent Bridge exact-base convergence

Mode: test-only against accepted DLV-296. Preserve all six current dirty and
untracked files and do not commit unless the gate below is green. Do not edit
production/runtime source, packages, manifests, protocols, bounds, timeouts,
installed/configured state, or processes. PID 83788 remains accepted and must
not be touched.

Correct the permanent `VirtualCollectionWindowCrossesBridge` case to use the
same legal request contracts proven by DLV-305. Retain the initial base-zero
checkpoint, its exact sequence and presentation generation. After the exact
generated near-end action is Enqueued, consume raw events; fail immediately on
the existing bounded action-failure payload. Each forward invalidation may
drive one exact-base `BridgePresentationRequest` with current capabilities and
the latest materialized sequence. Apply the returned update and advance that
base until the existing bounded observation limit reaches the durable
generation-2 64-item Append. Preserve total 10,000, first index zero, no before,
after present, exact generation, Append, and 96-item host-bound assertions.
Finally request one base-zero checkpoint and prove the same durable 64 items are
normalized to Replace. Remove superseded diagnostic-only DLV-299–302 scenario
names/helpers while retaining reusable focused coverage.

This is event-driven convergence, not snapshot polling: no presentation request
may occur without a preceding current forward invalidation. Do not add logging,
reflection, production hooks, sleeps, polling, stress loops, timeout enlargement,
stdout/file diagnostics, package special cases, weakened assertions, or a
second model. Compile with serialized MSBuild and run the one permanent virtual
collection prefix exactly once. If red, preserve evidence and stop without
correction or commit. If green, source-audit and commit one coherent test-only
notification-convergence milestone containing only DLV-297–306 notification
evidence; leave earlier cumulative DLV-278–283 hunks uncommitted and report the
exact staged/remaining scope. Never push.

DLV-306 test-only commit `5fe7a5fae3e307b1ffdabab0152bb64e28d7d740`
is independently source-reviewed and accepted. Its production-free delta is
exactly four test files covering the owned notification lane, real worker
transport/lifecycle-first cursor path, current-session ProcessClient semantics,
registry admission, and permanent Bridge convergence. Serialized Bridge build
passed and the permanent virtual prefix passed 1/1. The exact-base path reached
generation-2 64-item Append with total 10,000, index zero, no-before/has-after,
and the 96-item bound; the final base-zero checkpoint retained those 64 items
as Replace. No disposable DLV names, production hooks, timing sleeps, timeout
changes, protocol changes, or production files are present. The remaining dirty
files are only `WidgetSessionCoordinatorTests.cpp`, the held exact-base Bridge
scenario hunk, and `WidgetPresentationUpdateTests.cs`. PID 83788 intentionally
remains running because this is a test/doc-only delta.

## Assigned widgets evidence — DLV-307 cumulative convergence completion

Mode: test-only after accepted DLV-306. Owner/baseline: widgets lane at
`5fe7a5fae3e307b1ffdabab0152bb64e28d7d740` with exactly the three remaining
dirty cumulative matrix files. Do not edit production/runtime source, packages,
manifests, protocols, bounds, timeouts, installed/configured state, or
processes. PID 83788 remains accepted and must not be touched.

First source-audit the remaining exact-base Bridge scenario so it uses real
`BridgeTestWidget` actions and preserves ordinary exact-base update, stale-base
rejection, base-zero recovery checkpoint, and post-recovery ordinary update.
Then resume the previously stopped evidence without rerunning already-green
native coordinator, WidgetSdk, WidgetRuntime, or individual notification
prefixes:

1. run the complete focused Bridge suite once outside the command sandbox;
2. if green, run the smallest focused Spotify paging/virtual-window suite once;
3. if green, run the smallest linked Tier 2 cross-process convergence group
   once; and
4. if green, run one exact-commit Tier 3 verifier because the accepted cluster
   changes a private cross-process contract.

Use existing documented runners and current exact commit only. Record exact
commands, counts, hashes where applicable, and every skipped/ineligible suite.
Stop on the first distinct red result, nondeterminism, environment blocker, or
fixture gap; do not repair production under this assignment and do not rerun a
red group. Do not add logging, hooks, sleeps, polling, timeout enlargement, or
weakened assertions. If all required groups pass, commit one coherent test-only
DLV-307 milestone containing exactly the three remaining matrix files and stop
for independent review. Never rebuild/relaunch the overlay, integrate, or push.

DLV-307 source audit passed, but cumulative execution stopped at the first red
group as required. The complete Bridge suite passed 95/96 at `5fe7a5f`; the
only failure was `Worker residency budget refuses count overcommit and releases
failures`, whose assertion reported that the count-bound refusal did not contain
the expected `application worker limit (1/1)` explanation. Spotify, linked Tier
2, and Tier 3 were not run. No commit was created, the exact three matrix files
remain dirty, and production/PID 83788 were untouched.

## Assigned widgets diagnosis — DLV-308 Bridge residency refusal classification

Mode: diagnostic-only after DLV-307. Preserve baseline `5fe7a5f`, exactly the
three dirty matrix files, installed/configured state, and PID 83788. Do not edit
or commit any file; do not rebuild/relaunch the overlay, integrate, or push.

Source-audit the failing test, `WorkerResidencyBudget`, Bridge failure framing,
and the budget harness to establish the current exact refusal contract and
whether any earlier full-suite case can retain a worker or alter the response.
Then run only the failing Bridge prefix once outside the command sandbox while
capturing the exact response type and message. Do not rerun the complete Bridge
suite and do not run Spotify, Tier 2, Tier 3, or previously green groups.

If the isolated prefix is red, report the exact actual-versus-expected contract
and the smallest test-only or production correction justified by source. If it
is green, classify the result as full-suite order/state contamination and name
the narrowest deterministic predecessor/state boundary to test next; do not
bisect or rerun under this assignment. Stop for reviewer direction on any
nondeterminism, fixture gap, or production defect. No logging, hooks, sleeps,
polling, timeout enlargement, weakened assertions, or speculative repair.

DLV-308 reproduced the residency test red in isolation 0/1 and ruled out
full-suite state contamination. The refusal is correctly framed as error code
`request_failed` with bounded message `Widget 'worker-1' runtime request failed
(worker-admission-failed).`; the stale test instead searched that public Bridge
message for the inner budget exception text `application worker limit (1/1)`.
The direct registry evidence already owns the exact inner admission exception.
No file, process, package, or installed state changed.

## Assigned widgets evidence — DLV-309 correct boundary assertion and finish gate

Mode: test-only after accepted DLV-308 classification. Owner/baseline: widgets
lane at `5fe7a5f` with exactly the same three dirty cumulative matrix files.
Production/runtime source, packages, manifests, protocols, bounds, timeouts,
installed/configured state, and PID 83788 are frozen.

Change only the stale residency-refusal assertion in
`tests/WidgetBridge.Tests/Program.cs`: at the Bridge boundary require exact
error code `request_failed` and exact typed wrapper message `Widget 'worker-1'
runtime request failed (worker-admission-failed).`. Preserve the existing
worker-count refusal, reservation counters, crash release, replacement retry,
and direct-registry inner-exception coverage. Do not weaken the contract or add
fallback alternatives.

Run in this order, once each, and stop at the first red result:

1. the corrected residency-count Bridge prefix outside the command sandbox;
2. the complete focused Bridge suite outside the command sandbox;
3. the smallest focused Spotify paging/virtual-window suite;
4. the smallest linked Tier 2 cross-process convergence group; and
5. one exact-commit Tier 3 verifier.

Do not rerun already-green native coordinator, WidgetSdk, WidgetRuntime, or
individual notification prefixes. Use current documented runners and record
exact commands, counts, hashes where applicable, and all skipped/ineligible
evidence. No production repair, logging, hooks, sleeps, polling, timeout
enlargement, weakened assertions, rebuild/relaunch, integration, or push. If
every required group passes, commit one coherent test-only DLV-309 milestone
containing exactly the three remaining matrix files and stop for independent
review.

DLV-309 corrected the exact Bridge wrapper assertion and passed the isolated
residency prefix 1/1 plus the complete Bridge suite 96/96. It then stopped at
the Spotify gate before any Spotify test executed: `dotnet run` with Release
and `--no-restore` printed only `The build failed. Fix the build errors and run
again.` with no compiler diagnostic or test count. Tier 2 and Tier 3 were not
run. No commit was created; exactly the same three files remain dirty and PID
83788 was untouched.

## Assigned widgets diagnosis — DLV-310 Spotify build failure capture

Mode: diagnostic-only after DLV-309. Preserve baseline `5fe7a5f`, exactly the
three dirty matrix files, packages/restored assets, installed/configured state,
and PID 83788. Do not edit or commit any file, restore/update dependencies,
rebuild/relaunch the overlay, integrate, or push.

Source-audit the Spotify test project, its project references/targets, and the
documented runner to identify why the prior `dotnet run --no-restore` could
discard its actual build diagnostic. Then invoke exactly one explicit Release
`dotnet build` of `tests/SpotifyWidget.Tests/SpotifyWidget.Tests.csproj` with
`--no-restore` and sufficient ordinary console verbosity to capture the first
real error. Do not run the Spotify executable or any other evidence group.

If the explicit build is red, report its first causal diagnostic and classify
the smallest justified correction without applying it. If green, classify the
prior result as a runner/build-invocation anomaly and report the exact produced
test artifact plus the narrowest next command; do not run it. Stop for missing
assets, environment state, nondeterminism, or any production defect. No binlog,
new logging, hooks, sleeps, polling, timeout changes, weakened assertions, or
speculative repair.

DLV-310's one explicit serialized Release build failed deterministically with
six source errors. The first was CS0246 for removed
`SpotifyPlaylistItemsSummary`; the remaining errors were the same stale summary
type plus missing `GetPlaylistAsync` and a mismatched `GetPlaylistItemsAsync`
return type. Current production separates playlist metadata from paged items as
`SpotifyPlaylistSummary` and `SpotifyPlaylistItemsPageSummary`. The compiler
server timeout fell back normally and was not causal. This is test-harness
contract drift, not a production defect. No file or state changed.

## Assigned widgets evidence — DLV-311 reconcile Spotify fixture and finish gate

Mode: test-only after DLV-310. Owner/baseline: widgets lane at `5fe7a5f` with
exactly the three dirty cumulative matrix files. Production/runtime source,
packages, manifests, protocols, bounds, timeouts, installed/configured state,
and PID 83788 are frozen.

Edit only `tests/SpotifyWidget.Tests/Program.cs` to reconcile the stale fixture
with the current production `ISpotifyApplicationService` contract: separate
playlist metadata into `SpotifyPlaylistSummary`, use
`SpotifyPlaylistItemsPageSummary` for paged items and completions/handlers, and
implement `GetPlaylistAsync` with the fixture's matching playlist metadata.
Preserve the existing paging offsets, limits, totals, cancellation/race
controls, data, and assertions. Do not change production or reduce coverage.

First build the Spotify test project once in Release with `--no-restore`; if
green, run its existing 54-case executable once. If Spotify is green, continue
once each with the smallest linked Tier 2 cross-process convergence group and
one exact-commit Tier 3 verifier. Do not rerun Bridge, native coordinator,
WidgetSdk, WidgetRuntime, or notification prefixes. Stop at the first red
result and report exact commands, counts, hashes where applicable, and skips.

No restore/update, production repair, logging, hooks, sleeps, polling, timeout
enlargement, weakened assertions, overlay rebuild/relaunch, integration, or
push. If all remaining groups pass, commit one coherent test-only DLV-311
milestone containing exactly the original three matrix files plus
`tests/SpotifyWidget.Tests/Program.cs`, then stop for independent review.

DLV-311's authorized playlist-service reconciliation compiled past all six
prior errors. The one Release build then stopped with two deterministic CS1503
errors: `tests/SpotifyWidget.Tests/Program.cs` and
`SpotifyResponsiveLayoutTests.cs` still pass a raw playlist cursor snapshot to
`SpotifyPresentationState`, whose current production contract requires one
immutable `SpotifyCursorPresentation` containing that snapshot plus its wide
and compact projected scroll shells. No test executed and no commit was made;
Spotify, Tier 2, and Tier 3 remain pending. PID 83788 was untouched.

## Assigned widgets evidence — DLV-312 reconcile Spotify presentation fixtures

Mode: test-only after DLV-311. Owner/baseline: widgets lane at `5fe7a5f` with
the original three dirty cumulative matrix files plus the authorized uncommitted
Spotify service-fixture reconciliation in `tests/SpotifyWidget.Tests/Program.cs`.
Production/runtime source, packages, manifests, protocols, bounds, timeouts,
installed/configured state, and PID 83788 are frozen.

Edit only the two stale presentation-fixture call sites in
`tests/SpotifyWidget.Tests/Program.cs` and
`tests/SpotifyWidget.Tests/SpotifyResponsiveLayoutTests.cs`. Wrap the existing
playlist snapshot in `SpotifyCursorPresentation<SpotifyPlaylistCollectionItem>`
with deterministic wide and compact vertical-scroll shells matching the
production state contract. Preserve the original snapshot, modes, IDs,
responsive intent, serialization assertions, and all DLV-311 service-fixture
coverage. Do not change production or reduce coverage.

Build the Spotify test project once in Release with `--no-restore`; if green,
run its existing 54-case executable once. If Spotify is green, continue once
each with the smallest linked Tier 2 cross-process convergence group and one
exact-commit Tier 3 verifier. Do not rerun Bridge, native coordinator,
WidgetSdk, WidgetRuntime, or notification prefixes. Stop at the first red and
report exact commands, counts, hashes where applicable, and skips.

No restore/update, production repair, logging, hooks, sleeps, polling, timeout
enlargement, weakened assertions, overlay rebuild/relaunch, integration, or
push. If all remaining groups pass, commit one coherent test-only DLV-312
milestone containing exactly the original three matrix files plus the two
Spotify test files, then stop for independent review.

DLV-312 compiled the reconciled Spotify fixtures with zero warnings/errors, then
the 54-case executable passed 51/54. The failures were: expected protocol 14
but snapshot protocol 19 in the maximum-page case; a failed adjacent playlist
load still exposed automatic forward pagination; and manifest version 0.3.3 was
expected while the current manifest is 0.3.12. Tier 2 and Tier 3 were skipped,
no commit was created, and the intended five test files remain dirty. PID 83788
was untouched.

## Assigned widgets diagnosis — DLV-313 classify three Spotify runtime reds

Mode: source-only diagnostic after DLV-312. Preserve baseline `5fe7a5f`, all
five dirty test files, packages/restored assets, installed/configured state, and
PID 83788. Do not edit, build, run tests, commit, rebuild/relaunch the overlay,
integrate, or push.

Trace each of the three red assertions against current production and test
contracts. For the protocol assertion, determine the minimum valid protocol of
the rendered virtual-window snapshot and whether 14 or 19 is authoritative.
For adjacent-load failure, trace `WidgetCursorResource` failure state, retained
cursors, `Present`, generated focus-edge action, retry action, and admission to
decide whether automatic pagination after an error is an intended retry path or
a production loop/regression. Compare the cursor-resource behavior with the
older paged-resource error contract, but do not assume they should match. For
the manifest assertion, identify the accepted production change that advanced
0.3.3 to 0.3.12 and whether the test should pin the exact current version or a
different invariant.

Report an evidence-backed disposition for each red as test drift, production
defect, or unresolved contract ambiguity, with exact source references and the
smallest justified next milestone. Do not propose weakening an assertion merely
to obtain green. If any production change is justified, stop before applying it
so the reviewer can restore physical-first ordering. No logging, hooks, sleeps,
polling, timeout changes, or speculative repair.

DLV-313 classified the protocol and manifest failures as exact test drift:
Spotify's estimated-extent virtual window requires protocol 19, and the accepted
package manifest is exactly 0.3.12. It classified the adjacent-load result as a
generic SDK production defect. `WidgetCursorResource` correctly retains rows,
cursors, failed intent, and Error state, but `Present` regenerates edge actions
from retained cursors while Error is visible. That creates an automatic retry
route alongside the explicit visible Retry action and violates the documented
cursor authoring contract. The older paged resource independently suppresses
edge actions in Error. No file or process changed.

## Assigned widgets production — DLV-314 suppress cursor edge retry in Error

Mode: physical-first production/build, user verdict, then tests. Owner/baseline:
widgets lane at `5fe7a5f`. Preserve all five dirty test files exactly; stage and
commit only production/package files. PID 83788 and installed/configured state
remain untouched until planner review.

In `src/WidgetSdk/WidgetCursorResource.cs`, make `Present` suppress generated
before/after focus-edge pagination actions while the snapshot status is Error.
Retain the last-good rows, cursor/anchor facts, virtual-window metadata,
collection generation/change markers, failed intent, explicit `Retry`, and
normal focus-edge pagination in every non-Error state. Do not clear cursors,
replace the snapshot, special-case Spotify, change protocol/wire shape, bounds,
timeouts, or lifecycle authority. Match the public cursor authoring contract
without introducing a second retry owner.

Because Spotify packages the changed SDK binary and installed packages are
immutable, advance only its manifest version from 0.3.12 to 0.3.13. Do not
change widget identity, permissions, entrypoint, package state, credentials,
account/provider configuration, or any other package content deliberately.

Before user verdict, perform source review only, commit one production-only
DLV-314 milestone containing exactly the SDK source and Spotify manifest, and
build that exact commit once from a clean isolated tree as coherent Release with
tests skipped and packaging enabled. Also build the exact 0.3.13 Spotify
community package from that same clean tree without installing it. Report exact
commit, diff, commands/results, clean provenance, OverlayHost/runtime/WidgetSdk
hashes, package path/hash, and package manifest/version inspection. Do not
author/edit/run tests, install/select packages, launch/terminate processes,
integrate, or push. Stop for independent review and visible launch.

DLV-314 production commit `992b77b8959a615a3fe2791e9369fcfec1b9da4c`
is independently source-reviewed and accepted for physical evaluation. Its
delta is exactly `WidgetCursorResource.Present` suppressing generated edge
actions only in Error plus Spotify manifest 0.3.12 to 0.3.13. Retained rows,
cursors, anchor, virtual-window metadata, generation/change facts, failed
intent, explicit Retry, and all non-Error behavior remain unchanged. No public
protocol, bound, timeout, lifecycle owner, or widget-specific SDK branch was
added.

The clean detached tree
`C:\Users\dwive\AppData\Local\Temp\GameBarAlternative-dlv314-992b77b` is at
exact `992b77b`; coherent Release and Spotify package builds passed with tests
skipped. `OverlayHost.exe` SHA-256 is
`318F3BD2B0F7E028EB9C2CC091B4DD74F7D880C4AAC45243AE628614E9FD69D8`;
Release `WidgetSdk.dll` is
`F47664B1A331666F4E2557AB214644F1E85AE79E2ADF8E51095EE7528232B60F`.
The 0.3.13 package at
`artifacts\community-addons\spotify\widgetrail.samples.spotify-0.3.13.wrwidget`
has SHA-256
`8D52E04144E1F78E5EE65C811C06572D943A6BABDEEEBC1DE73776E60598B50F`
and validated full-trust identity `widgetrail.samples.spotify`.

The attempted install/select/enable command was denied before execution because
the persistent full-trust package change requires fresh explicit user approval.
No package, process, or configuration mutation occurred in that attempt;
accepted PID 83788 remained responsive on DLV-296 with Spotify 0.3.12, and no
retry occurred until the user approved it.

The user then explicitly approved the full-trust package installation and
visible switch. Spotify 0.3.13 installed immutably, was selected and enabled,
and existing configuration/credentials remained in their user-owned stores.
Verified PID 83788 accepted cooperative `WM_CLOSE` through its top-level
windows; no force termination occurred. Exact DLV-314 PID 21672 launched
visibly from the clean detached Release and remains responsive. Its executable
path is
`C:\Users\dwive\AppData\Local\Temp\GameBarAlternative-dlv314-992b77b\src\OverlayHost\out\Release\OverlayHost.exe`
and its SHA-256 reverified as
`318F3BD2B0F7E028EB9C2CC091B4DD74F7D880C4AAC45243AE628614E9FD69D8`.
Catalog inspection reports Spotify 0.3.13 enabled. Stop for the user's physical
verdict; do not run tests first.

The user physically accepted exact DLV-314 PID 21672 with Spotify 0.3.13. This
promotes `992b77b8959a615a3fe2791e9369fcfec1b9da4c` as the current accepted
production milestone. Preserve that running process and package state while
post-verdict evidence executes; test/doc-only deltas must not trigger another
build or relaunch.

## Assigned widgets evidence — DLV-315 accepted cursor recovery and cumulative gate

Mode: post-verdict test-only against accepted DLV-314. Owner/baseline: widgets
lane at `992b77b` with exactly the five held dirty test files. PID 21672,
Spotify 0.3.13, installed/configured state, and all production/runtime/package
files are frozen.

In `tests/WidgetSdk.Tests/WidgetCursorResourceTests.cs`, add permanent focused
coverage proving an Error snapshot retains its bounded last-good rows, cursor
facts, anchor and virtual-window metadata while `Present` emits neither before
nor after edge action; prove explicit `Retry` remains admitted and restores
Ready/non-Error pagination. Do not duplicate Spotify behavior or expose private
state.

In `tests/SpotifyWidget.Tests/Program.cs`, correct only the two DLV-313 test
drifts: require exact `ProtocolConstants.VirtualCollectionWindowVersion` for
the estimated-extent playlist window and exact immutable manifest version
0.3.13. Preserve the existing adjacent-failure assertion; it must now validate
the accepted production correction. Preserve all prior DLV-309/311/312 fixture
corrections and `SpotifyResponsiveLayoutTests.cs` unchanged except for its
already-held DLV-312 correction.

Run once each in order and stop on the first red result:

1. build and run the complete WidgetSdk test executable, since it has no
   supported prefix seam and the accepted production SDK changed;
2. build and run the complete 54-case Spotify test executable;
3. run the smallest linked Tier 2 cross-process convergence group; and
4. run one exact-commit Tier 3 verifier.

Do not rerun Bridge, native coordinator, WidgetRuntime, or individual
notification prefixes already green unless the documented Tier 2/Tier 3 runner
necessarily owns them. Record exact commands, counts, hashes where applicable,
and skips. No production/package edit, restore/update, logging, hooks, sleeps,
polling, timeout enlargement, weakened assertion, overlay rebuild/relaunch,
installed-state change, integration, or push. If all required groups pass,
commit one coherent test-only DLV-315 milestone containing exactly the original
five held test files plus `WidgetCursorResourceTests.cs`, then stop for
independent review.

DLV-315 added the authorized focused cursor Error/Retry assertions and corrected
only the Spotify protocol-version and immutable-package expectations. Its first
required command, the complete WidgetSdk executable, exited 1 before any test
ran and emitted only `The build failed. Fix the build errors and run again.`
Spotify, Tier 2, and Tier 3 were correctly skipped. No commit was created. The
six test files remain dirty and held; production, packages, installed state,
and accepted PID 21672 were untouched.

## Assigned widgets diagnosis — DLV-316 WidgetSdk build failure capture

Mode: diagnostic-only after DLV-315. Preserve baseline `992b77b`, all six dirty
test files, accepted PID 21672, Spotify 0.3.13, and every installed/configured
state surface. Do not edit or commit any file, restore/update dependencies,
launch/terminate/rebuild the overlay, install/select a package, integrate, or
push.

Invoke exactly one explicit serialized Release build of
`tests/WidgetSdk.Tests/WidgetSdk.Tests.csproj` with `--no-restore` and ordinary
console verbosity sufficient to capture the first compiler/MSBuild diagnostic
hidden by DLV-315's `dotnet run`. Do not execute the test binary. If the build
is red, report its first causal diagnostic and classify it as an authorized
DLV-315 test-fixture error, environment/tooling failure, or unrelated baseline
failure. If the build is green, report the exact command/result and stop for
planner disposition; do not rerun DLV-315 or advance to Spotify/Tier 2/Tier 3.
No source correction, logging/hooks, sleeps, polling, timeout enlargement, or
speculative repair is authorized.

DLV-316's one explicit serialized Release build passed in 7.38 seconds with
zero warnings/errors and produced the WidgetSdk test executable. MSBuild logged
an `UnauthorizedAccessException` while connecting to the Roslyn compiler-server
named pipe, then correctly fell back to local compilation. This classifies
DLV-315's diagnostic-free `dotnet run` failure as an execution-environment/
compiler-server incident rather than a source or product failure. No file,
process, package, or installed state changed.

## Assigned widgets evidence — DLV-317 resume cumulative gate

Mode: test-only continuation after DLV-316. Preserve baseline `992b77b`, the
exact six dirty test files, accepted PID 21672, Spotify 0.3.13, and installed/
configured state. No production/package edit, restore/update, logging/hooks,
sleeps, polling, timeout enlargement, weakened assertion, overlay rebuild/
relaunch, installed-state change, integration, or push.

Execute the already-built complete WidgetSdk test executable exactly once,
without invoking another WidgetSdk build. If green, compile the complete
Spotify test project exactly once with serialized Release `--no-restore`, then
execute its 54-case binary exactly once. If green, run the smallest linked Tier
2 convergence group and one exact-commit Tier 3 verifier required by DLV-315.
Stop at the first red command and report all later groups as skipped. Do not
rerun Bridge/native coordinator/WidgetRuntime/individual notification prefixes
unless the documented Tier runner necessarily owns them.

Record exact commands, counts, hashes/provenance, results, and skips. If every
required group passes, commit exactly the original five held test files plus
`tests/WidgetSdk.Tests/WidgetCursorResourceTests.cs` as one coherent test-only
DLV-317 milestone, then stop for independent review. Do not include generated
outputs or any seventh file.

DLV-317's already-built complete WidgetSdk executable stopped first red at
88/89. The new permanent cursor scenario proved DLV-314 retained internal rows,
cursors, anchor, extent, generation, and virtual-window position while
suppressing Error-state edge action IDs, but the projected
`VirtualCollectionWindow.HasBefore/HasAfter` still advertised those disabled
directions. `ViewSnapshotValidator` correctly rejected that mismatch as
`virtual_collection_action_mismatch`. Spotify, Tier 2, and Tier 3 were skipped;
no commit or further edit was created.

## Assigned widgets production — DLV-318 valid Error-state virtual projection

Mode: physical-first production/build after DLV-317. Preserve the six dirty test
files exactly and stage/commit only production/package files. In
`src/WidgetSdk/WidgetCursorResource.cs`, keep DLV-314's Error-state suppression
of generated before/after edge action IDs and retain the resource's private
rows, cursors, anchor, failed intent, extent, logical indices/total,
generation/change marker, and explicit Retry. Make only the projected virtual
window availability flags describe the admitted presentation: `HasBefore` and
`HasAfter` must be false when their corresponding boundary action is suppressed
and must retain existing non-Error behavior. Do not weaken or special-case
`ViewSnapshotValidator`, clear private cursors, replace the retained snapshot,
change wire shape/protocol/bounds/timeouts/lifecycle ownership, or add package-
specific behavior.

Because Spotify packages the SDK, advance only
`samples/SpotifyWidget/manifest.json` from immutable 0.3.13 to 0.3.14. Before
user verdict, source-review and commit exactly those two production/package
files as one DLV-318 milestone. Build that exact commit once from a clean
isolated tree as coherent Release with tests skipped and packaging enabled, and
build the exact Spotify 0.3.14 package from the same tree without installing it.
Report commit/diff, exact commands/results, clean provenance, relevant hashes,
package path/hash, and manifest inspection. Do not edit/run tests, install or
select packages, touch credentials/account/provider/configuration, launch or
terminate processes, integrate, or push. Preserve accepted PID 21672 and stop
for independent review and fresh approval before any visible candidate switch.

DLV-318 production commit
`32a2a5ed3f31ad95156d3ab61fe36f2d791449e2` has parent `992b77b` and changes
exactly `WidgetCursorResource.cs` plus Spotify manifest 0.3.13 to 0.3.14. The
review confirmed only projected `HasBefore/HasAfter` now follow corresponding
admitted actions; private cursor facts, explicit Retry, non-Error behavior, the
validator, protocol, and lifecycle ownership are unchanged. Its detached tree
is clean at the exact commit. The coherent tests-skipped packaged Release and
Spotify 0.3.14 package both passed. Reviewed hashes are:

- `OverlayHost.exe`:
  `86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`
- Release `WidgetSdk.dll`:
  `C98AB38346114F11482CD39609A7A6397F529EFDAC90F18BCF62E4EABAC7CC25`
- Spotify 0.3.14 package:
  `77F81AB127D19453876D982FE77C3E337A3CBBA4200FFD01B434FA89AA0F8727`

Package inspection confirms `widgetrail.samples.spotify` 0.3.14 with
`full-trust-application-v1`. All six held test files remain dirty and excluded
from the clean build. Accepted DLV-314 PID 21672 remains responsive from its
exact detached Release. No installation, selection, launch, termination,
integration, or push has occurred. Fresh user approval is required before the
immutable package change and visible candidate switch.

The user explicitly approved the persistent full-trust package update and
visible switch. Spotify 0.3.14 installed immutably, was selected and enabled,
and earlier versions remain available. Accepted DLV-314 PID 21672 received
`WM_CLOSE` through five verified top-level windows and exited cooperatively; no
force termination occurred. Exact DLV-318 PID 126208 launched visibly from the
clean detached Release and remains responsive. Its executable path is
`C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative-dlv318-build\src\OverlayHost\out\Release\OverlayHost.exe`
and its SHA-256 reverified as
`86AC9946CC54F2B4CF51B14EEAA48CE32FECDF2C381DA73F24107DBE755F13AD`.
Catalog inspection reports Spotify 0.3.14 enabled and active. Stop for the
user's physical verdict; do not run tests first.

The user physically accepted exact DLV-318 PID 126208 with Spotify 0.3.14.
This promotes `32a2a5ed3f31ad95156d3ab61fe36f2d791449e2` as the current accepted
production milestone. Preserve that running process and package state while
post-verdict evidence executes; test/doc-only deltas must not trigger another
build or relaunch.

## Assigned widgets evidence — DLV-319 accepted projection and cumulative gate

Mode: post-verdict test-only against accepted DLV-318. Owner/baseline: widgets
lane at `32a2a5e` with exactly the six held dirty test files. Preserve accepted
PID 126208, Spotify 0.3.14, installed/configured state, and every production/
runtime/package file.

In `tests/WidgetSdk.Tests/WidgetCursorResourceTests.cs`, correct only the
DLV-317 Error-state projection expectation: retain exact equality assertions
for anchor, request generation, change marker, first logical index, total item
count, estimated extent, rows, private cursors, and failed intent, while
asserting projected `HasBefore` and `HasAfter` are false alongside the null
boundary actions. Preserve explicit Retry admission/success and restored
non-Error bidirectional action/availability assertions.

In `tests/SpotifyWidget.Tests/Program.cs`, advance only the immutable manifest
expectation from 0.3.13 to 0.3.14. Preserve the exact virtual-window protocol
constant, adjacent-failure behavior, and every DLV-309/311/312 fixture
correction. Leave all other held files unchanged.

Run once each and stop at the first red command: serialized Release build plus
complete WidgetSdk test executable; serialized Release build plus complete
54-case Spotify executable; the smallest linked Tier 2 convergence group; and
one exact-commit Tier 3 verifier. Do not rerun Bridge/native coordinator/
WidgetRuntime/individual notification prefixes unless the documented Tier
runner necessarily owns them. Record commands, counts, hashes/provenance,
results, and skips.

No production/package edit, restore/update, logging/hooks, sleeps, polling,
timeout enlargement, weakened assertion, overlay rebuild/relaunch, installed-
state change, integration, or push. If all required groups pass, commit exactly
the original five held test files plus
`tests/WidgetSdk.Tests/WidgetCursorResourceTests.cs` as one coherent test-only
DLV-319 milestone and stop for independent review.

DLV-319 committed exactly the six authorized test files as
`199a81b8a0e74df7fcd8ebd1905fff1792497f63`, with parent accepted production
`32a2a5e`; the standing worktree is clean. WidgetSdk build/tests passed 89/89,
Spotify build/tests passed 54/54, and the smallest linked generic installed-
package convergence group passed 6/6. The single canonical Tier 3 verifier ran
from a clean detached tree at exact `199a81b`, passed its first eight steps,
then stopped at `widget-runtime-tests` with 82/84:

- `Cancellation-ignoring retired gesture grants are revoked`: expected 1,
  observed 0.
- `Process client preserves exact-base cursor update semantics`: no matching
  element.

Verifier result SHA-256 is
`DB19C1D13AEFBEF06E81ABB98B3C14D2D2A63EB0428A4D151994C024C4F8489C`.
All later canonical steps were skipped. No rerun or correction occurred;
accepted PID 126208 and installed/configured state remain untouched.

## Assigned widgets diagnosis — DLV-320 classify two Runtime reds

Mode: source/artifact-only diagnostic after DLV-319. Preserve clean baseline
`199a81b`, accepted PID 126208, Spotify 0.3.14, all installed/configured state,
and the retained exact-commit Tier 3 artifact under
`GameBarAlternative-dlv319-tier3\artifacts\verification\20260821T224918Z-6927c681`.
Do not edit, build, run/rerun tests, commit, rebuild/relaunch, integrate, or
push.

Trace each red independently against current production/test contracts and
accepted DLV history:

1. For the cancellation-ignoring retired gesture-grant case, identify the exact
   lifecycle/retirement path that now yields zero observed callbacks, determine
   whether revocation-before-observation is the intended bounded contract or a
   product omission, and distinguish it from the already-reconciled retained-
   worker/destructive-retirement fixtures.
2. For exact-base cursor update semantics, identify the missing element/action
   lookup, trace its fixture widget, action source/id, lifecycle establishment,
   and `ProcessClient` update base through the current separated checkpoint/
   update contract, and determine whether the fixture uses a stale route or
   production fails to expose the generated cursor action.

Classify each as exact test drift, production defect, environment failure, or
unresolved ambiguity with source/log references and the smallest justified next
milestone. Do not infer a shared cause, weaken assertions, add diagnostics,
change timeouts, or propose a rerun as classification evidence. If a production
correction is justified, stop before applying it so physical-first ordering can
resume.

DLV-320 classified both failures as test drift with no production or environment
failure. The retired gesture-grant fixture used one shared revocation signal:
the intended immediate reservation revocation could satisfy it before the
cancellation-ignoring fake grant completed and its separate tracked late
revocation occurred. The exact-base cursor fixture consumed the first legal
post-action invalidation, which represents `LoadingAdjacent` on retained
generation 1/Replace; it incorrectly required the later generation 2/Append
property immediately. The action route, exact base, update materialization,
late cleanup, and product contracts are present and correct. No file or state
changed.

## Assigned widgets evidence — DLV-321 reconcile two Runtime fixtures

Mode: test-only after DLV-320. Preserve clean baseline `199a81b`, accepted PID
126208, Spotify 0.3.14, installed/configured state, and every production/runtime/
package file. Edit only
`tests/WidgetRuntime.Tests/WidgetProcessOwnershipScenarios.cs` and
`tests/WidgetRuntime.Tests/Program.cs`.

In the retired gesture-grant scenario, replace the shared-signal assumption
with deterministic separate observation of the cancellation-ignoring grant's
completion and its subsequent late revocation. Continue proving the immediate
uncommitted reservation is revoked, the late granted authority is separately
revoked, replacement authority remains current, and no retired-session
authority survives. Do not weaken accepted one-or-two-revocation bounds or add
sleeps/polling/timeouts.

In the exact-base cursor scenario, retain the generated near-end action and
exact-base contract. Consume current-session invalidations in a bounded event-
driven sequence, applying one update against the currently materialized base
until exact generation 2, 64 items, and `Append` are observed. Fail immediately
on action failure or illegal/stale update. Then request base zero and prove the
same durable generation-2 window is returned as `Replace`. Do not wait by time,
skip intermediate states, weaken exact assertions, or create a second model.

Build the Runtime test project once in serialized Release `--no-restore`, then
run its complete 84-case executable once. Stop first red. If green, commit
exactly the two authorized files on top of `199a81b`, then run one canonical
Tier 3 verifier from a clean detached tree at that exact commit. Do not rerun
WidgetSdk, Spotify, Tier 2, Bridge, coordinator, or individual prefixes outside
the canonical verifier. Record commands, counts, hashes/provenance, results,
and skips. No production/package edit, restore/update, diagnostics, hooks,
timeout change, overlay rebuild/relaunch, installed-state change, integration,
or push. If Tier 3 passes, stop for independent review.

DLV-321 stopped correctly before edits/build/tests because the assignment's
64-item expectation contradicted the existing Runtime fixture: page size 4,
maximum retained items 8, and logical total 12. The fixture and durable
assertions already define generation 1 as four items and generation 2 as eight
items; the separate 32-to-64 model is Bridge-owned and outside this scope. No
file changed and baseline `199a81b` remains clean.

## Assigned widgets evidence — DLV-322 corrected Runtime fixture contract

Mode and scope are identical to DLV-321, except the exact-base cursor outcome
must preserve the authoritative existing 4-to-8 Runtime model. Edit only
`tests/WidgetRuntime.Tests/WidgetProcessOwnershipScenarios.cs` and
`tests/WidgetRuntime.Tests/Program.cs`; preserve clean `199a81b`, accepted PID
126208, Spotify 0.3.14, installed/configured state, and all production/runtime/
package files.

Apply the deterministic retired gesture-grant synchronization specified by
DLV-321. For the cursor case, consume bounded current-session invalidations and
apply one update per current materialized base until exact generation 2, eight
items (`diagnostic.item.0` through `.7`), logical total 12, remaining forward
boundary, and `Append` are observed. Fail immediately on action failure or an
illegal/stale update. Then prove a base-zero checkpoint yields that same exact
eight-item generation-2 window normalized to `Replace`. Do not edit the fixture
widget, import the Bridge 32-to-64 model, skip intermediate states, weaken exact
assertions, or create a second model.

Retain DLV-321's build/run/commit/Tier-3 commands and all prohibitions exactly:
one serialized Runtime build, one complete 84-case Runtime execution, commit
exactly the two authorized files if green, then one canonical verifier from a
clean detached tree at that exact commit. Stop first red; never push.

DLV-322 made only the two authorized test-file edits and its serialized Runtime
build passed with zero warnings/errors. The single complete execution passed
the first nine cases, then stalled at the corrected cancellation-ignoring grant
case. The test awaited the controller-input request's terminal failure before
releasing the deliberately held grant, but terminal completion remains coupled
to that outstanding grant path. After more than two minutes without progress,
only the owned test process was terminated. The cursor convergence edit was not
reached. No commit/Tier 3, production/package/overlay/state mutation, or push
occurred; the two test edits remain held on clean baseline `199a81b`.

## Assigned widgets correction — DLV-323 release held grant after immediate revocation

Mode: bounded test-only correction after DLV-322. Preserve both current edits
in the only authorized files and change only the gesture scenario's ordering.
After stop/replacement, await the existing immediate `Revoked` signal and prove
zero granted authorities plus current replacement ownership before releasing
the held cancellation-ignoring grant. Then call `ReleaseGrant`, await separate
grant completion and `LateGrantRevoked`, await the original input request's
terminal failure, and retain every authority/input-sequence/revocation-bound
assertion. Do not await the input request before releasing the held grant, add a
timer/sleep/poll, change product code, or alter the cursor convergence edit.

Build the Runtime test project once in serialized Release `--no-restore`, run
the complete 84-case executable once, and stop first red. If green, commit
exactly `WidgetProcessOwnershipScenarios.cs` and `Program.cs` on top of
`199a81b`, then run one canonical Tier 3 verifier from a clean detached tree at
that exact commit. Retain every DLV-322 prohibition and do not rerun unrelated
focused suites outside the verifier. Stop for independent review if Tier 3 is
green; never push.

DLV-323 made only the authorized gesture-ordering edit and its serialized
Runtime build passed with zero warnings/errors. The single complete execution
again passed the first nine cases, then stalled because it awaited the existing
`Revoked` signal before releasing the held grant. Source review established
that the activation has already consumed the host reservation and no companion
authority exists until the cancellation-ignoring grant returns. Session
teardown fails pending host requests and clears reservations; the dedicated
`RevokeLateGestureGrantAsync` path intentionally waits for that held grant and
revokes it only after completion. Therefore a pre-release companion revocation
is not a legal event to await. After 90 seconds only the owned test process was
terminated. No commit/Tier 3, production/package/overlay/state mutation, or
push occurred; the two test edits remain held on baseline `199a81b`.

## Assigned widgets correction — DLV-324 prove absent authority then late revocation

Mode: bounded test-only correction after DLV-323. Preserve both current edits
in the only authorized files. In the gesture scenario, after stop/replacement
and the existing terminal-start/replacement synchronization, prove the retired
companion still has zero granted authorities and the replacement companion is
current. Do not await a companion `Revoked` signal before releasing the held
grant: no authority exists yet. Call `ReleaseGrant`, await separate
`GrantCompleted` and `LateGrantRevoked`, then await the original input
request's terminal failure. Preserve the exact input sequence, accepted one-or-
two revocation bound, proof every granted late authority was revoked, and every
replacement/current-session assertion. Do not add hooks, timers, sleeps,
polling, product behavior, or weaken the late-revocation claim. Leave the
cursor convergence edit unchanged.

Build the Runtime test project once in serialized Release `--no-restore`, run
the complete 84-case executable once, and stop first red. If green, commit
exactly `WidgetProcessOwnershipScenarios.cs` and `Program.cs` on top of
`199a81b`, then run one canonical Tier 3 verifier from a clean detached tree at
that exact commit. Retain every DLV-322 prohibition and do not rerun unrelated
focused suites outside the verifier. Stop for independent review if Tier 3 is
green; never push.

DLV-324 committed exactly the two authorized Runtime test files as
`6b63edf253ef2a6aa0c760da3dd870d7fabf5bc8`. The serialized Runtime build was
green with zero warnings/errors and the complete executable passed 84/84.
One canonical Tier 3 verifier from a clean detached tree at that exact commit
reconfirmed Runtime 84/84 and then stopped at `windows-spotify-tests` because
`tests/WindowsSpotifyProvider.Tests/Program.cs` still accesses removed
`SpotifyPlaylistItemsPageSummary.Playlist`. Source history proves DLV-270 split
playlist metadata into `GetSpotifyPlaylistAsync` and retained paged items in
`GetSpotifyPlaylistItemsAsync`; the old test also queues separate detail and
items responses but never calls the new detail operation. This is retained
test compile/route drift, not a DLV-324 or production defect. No rerun,
integration, overlay/package/state mutation, or push occurred. The verifier
result SHA-256 is
`1722B3DEB61418EDC21DEE2F264BFBB34075FF651279FF905143C32808C0B29C`.

## Assigned widgets correction — DLV-325 align Spotify provider test with split routes

Mode: bounded test-only correction after DLV-324. Baseline is exact commit
`6b63edf`. Edit only `tests/WindowsSpotifyProvider.Tests/Program.cs`. In the
existing API-read scenario, call `GetSpotifyPlaylistAsync` for the queued
playlist-detail response and assert its collaborative metadata there; then call
`GetSpotifyPlaylistItemsAsync` for the queued items page and retain the exact
item/title/request-exhaustion assertions. Do not combine the production
contracts again, remove either queued response, weaken request URI/order checks,
or edit Spotify/runtime/SDK/bridge/host production code.

Build the Windows Spotify provider test project once in serialized Release
`--no-restore`, run its complete executable once, and stop first red. If green,
commit exactly that one test file on top of `6b63edf`, then run one canonical
Tier 3 verifier from a clean detached tree at that exact commit. Do not rerun
Runtime or other focused suites outside the verifier. Preserve PID 126208,
Spotify 0.3.14, installed/configured state, and all production artifacts. Stop
for independent review if Tier 3 is green; never push.

DLV-325 committed exactly the authorized Windows Spotify provider test file as
`e441f25bce88b51a8bd6b90c90f8a84d612812ae`. Its serialized build was green
with zero warnings/errors and the complete provider executable passed 32/32.
One canonical Tier 3 verifier from a clean detached tree at that exact commit
then stopped at Runtime 83/84. The existing Windows Job Object process-tree
case proved both contained processes exited and every containment assertion
passed, but `TemporaryDirectory.Dispose` failed deleting `child.pid` because
the helper publishes that path directly with an asynchronous writer. File
existence is therefore not a deterministic closed-writer readiness boundary.
Both DLV-324 Runtime corrections passed in this run. This is retained test
fixture cleanup drift, not a product/Job Object/DLV-325 defect. No rerun,
integration, overlay/package/state mutation, or push occurred. The verifier
result SHA-256 is
`BEA77238B35BCD82C0A5FBF71EA1B2F7791B483F697E6678D9EF3F6B96B5392D`.

## Assigned widgets correction — DLV-326 atomically publish Job helper PID

Mode: bounded test-only correction after DLV-325. Baseline is exact commit
`e441f25`. Edit only `tests/WidgetRuntime.Tests/Program.cs`, and only the
`--containment-parent` helper publication plus directly necessary local helper
code. Publish the child PID by writing a sibling temporary file to completion,
closing its writer, then atomically renaming it to the existing `child.pid`
path. Preserve the current parent/child Job Object ownership, accounting,
kill-on-close, exit, timeout, error, and cleanup assertions. Do not add deletion
retries, sleeps, polling beyond the existing bounded readiness wait, suppress
cleanup failures, broaden `TemporaryDirectory`, or edit production code.

Build the Runtime test project once in serialized Release `--no-restore`, run
the complete 84-case executable once, and stop first red. If green, commit
exactly `tests/WidgetRuntime.Tests/Program.cs` on top of `e441f25`, then run one
canonical Tier 3 verifier from a clean detached tree at that exact commit. Do
not rerun provider or other focused suites outside the verifier. Preserve PID
126208, Spotify 0.3.14, installed/configured state, and all production
artifacts. Stop for independent review if Tier 3 is green; never push.

DLV-326 committed exactly the authorized Runtime test file as
`676cd76f6761ca35b49b9810a0a8f0b42e9fabf4`. Its serialized build was green
with zero warnings/errors and the complete Runtime executable passed 84/84,
including the Job Object process-tree case. One canonical Tier 3 verifier from
a clean detached tree at that exact commit passed every preceding managed gate:
WidgetSdk 89/89, compatibility 12/12, scenarios 9/9, ticker 5/5, Runtime 84/84,
presentation sessions 11/11, worker host 10/10, Windows Spotify provider
32/32, Spotify widget 54/54, Bridge 96/96, and first-party conformance 6/6.
It then stopped compiling the existing native real-host accessibility fixture,
which still calls the removed singular `FindScrollPaginationAction` while
production and focused navigation tests use geometry-based plural
`FindScrollPaginationActions`. This is native test compile drift from the
DLV-269 pagination API change, not a DLV-326 or production defect. No rerun,
integration, overlay/package/state mutation, or push occurred. The verifier
result SHA-256 is
`3C47FE182308DFAB672D498B658F2853B1E486F833CC0C88476922693F7FE232`.

## Assigned platform correction — DLV-327 align real-host pagination fixture

Mode: bounded native test-only correction after DLV-326. The clean platform
branch at `cdbb04a` may be fast-forwarded only to exact cumulative commit
`676cd76`; stop on any non-fast-forward condition or dirty state. Edit only
`src/OverlayHost/RealHostAccessibilityTests.cpp`. Replace the six stale
singular pagination lookups with the current geometry-owned
`FindScrollPaginationActions` contract using each frame's exact snapshot active
scope and committed `RenderResult`. Select/assert the exact Before or After
edge, action ID, source scroll, and presence/absence needed by each existing
List/Grid/sparse/final-page claim. A small file-local test helper is permitted
only if it preserves those exact assertions. Do not restore the singular
production API, infer from focus/direction, duplicate pagination discovery,
weaken coverage, or edit production/native headers/other tests.

Run one complete serialized Release native OverlayHost build/test invocation
using `src/OverlayHost/build.ps1 -Configuration Release`, stopping first red.
If green, commit exactly `RealHostAccessibilityTests.cpp` on top of `676cd76`,
then run one canonical Tier 3 verifier from a clean detached tree at that exact
commit. Do not run additional focused suites outside those two gates. Preserve
PID 126208, Spotify 0.3.14, installed/configured state, and all production
artifacts; do not launch/terminate, integrate to main, begin DLV-284, or push.
Stop for independent review if Tier 3 is green.

DLV-327 fast-forwarded cleanly from `cdbb04a` to cumulative `676cd76` and
modified only `RealHostAccessibilityTests.cpp`. The six stale singular lookups
were coherently migrated to exact Before/After selection from
`FindScrollPaginationActions`, using each frame's active scope and committed
render geometry. The sole complete Release native build/test invocation then
stopped at the first behavioral red after 162 real-host checks: the initial
List frame still renders with `cursor.item.2` focused but asserts the After edge
that the removed singular API used to infer from a separate `cursor.item.5`
argument. Under the current geometry contract item 5 must actually be brought
to the viewport edge. The Grid forward frame has the same retained mismatch:
it renders item 4 while its old singular call named item 8. No commit/Tier 3,
production/overlay/package/state mutation, or push occurred; the one test-file
edit remains uncommitted on `676cd76`.

## Assigned platform correction — DLV-328 render exact pagination boundaries

Mode: bounded native test-only correction after DLV-327. Preserve the current
one-file plural-API migration. In the same
`RealHostAccessibilityTests.cpp` cursor fixture, render the forward List frame
with its existing trailing item 5 as the focused/rendered boundary and render
the forward Grid frame with its existing trailing item 8 as the focused/
rendered boundary before resolving After actions. Keep reverse frames on their
existing first loaded items and preserve every exact edge/action/source-scroll,
anchor, bounded-node, UIA, sparse-page, and final-page assertion. Do not modify
snapshot data, thresholds, viewport dimensions, production pagination,
renderer behavior, or any other file merely to force an action.

Run one complete serialized Release native OverlayHost build/test invocation
using `src/OverlayHost/build.ps1 -Configuration Release`, stopping first red.
If green, commit exactly `RealHostAccessibilityTests.cpp` on top of `676cd76`,
then run one canonical Tier 3 verifier from a clean detached tree at that exact
commit. Do not run additional focused suites outside those two gates. Preserve
PID 126208, Spotify 0.3.14, installed/configured state, and all production
artifacts; do not launch/terminate, integrate to main, begin DLV-284, or push.
Stop for independent review if Tier 3 is green.

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

## Future architecture queue — maturity review additions

Status: ordered future work, not assigned. These deliverables do not displace
DLV-316, cumulative integration, or DLV-284. Allocate implementation IDs only
when each bounded milestone becomes assignable.

1. Generic Game Launcher cutover. Remove the package's advanced-presentation
   declaration and slot projection. Render every accepted launcher layout
   through ordinary `ViewSnapshot`, responsive grid/scroll/navigation, semantic
   tiles, virtual windows, bounded artwork, WRSS, and controller focus while
   retaining the generic `WidgetApplicationRuntime`. Keep any layout choices
   package-owned and provide no native LauncherExperience fallback. Build and
   obtain a physical verdict before deleting the dormant framework slice.
2. LauncherExperience vertical-slice deletion. Deliberately remove the
   `LauncherExperienceCatalog`, public advanced-presentation protocol/SDK
   models, Bridge selection/catalog routes, native adapter/layout/projection/
   presentation state, Settings and CLI install/select/preview flows, project
   references, fixtures, compatibility baselines, and active docs. Do not add a
   generic custom-presentation escape hatch or compatibility layer.
3. Targeted LauncherExperience state retirement and proof. Delete only obsolete
   experience selection/last-good/package-catalog state while preserving themes,
   widget order, package configuration, credentials, and Game Launcher-owned
   library/organization state. Completion requires no active
   `LauncherExperience` or `AdvancedPresentation` production references under
   `src/`, a generic full-trust Game Launcher package, no launcher-specific host
   knowledge, physical acceptance, and post-verdict focused evidence.
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

1. Platform evidence queue: execute bounded DLV-328 real-host viewport-boundary
   fixture correction, its complete native gate, and one exact-commit Tier 3
   verifier, stopping on the first red result.
2. Reviewer integration queue: independently review the eventual cumulative
   evidence milestone; integrate the
   accepted production/test chain into local main only if all required evidence
   passes.
3. Platform production queue: assign DLV-284 after integration, before any new
   virtualization feature.
4. Future architecture queue: generic Game Launcher cutover, deliberate
   LauncherExperience deletion/state retirement, then model-level protocol
   requirements and the remaining maturity-review deliverables above.
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
| DLV-297 | Held uncommitted: direct lane and real worker transport prefixes passed 1/1 each; Bridge virtual pagination remained red 0/1 awaiting its first post-action invalidation. |
| DLV-298 | Completed uncommitted: all four individual ownership handoffs passed 1/1; the last green edge is registry publication through Bridge framing to the client event queue. |
| DLV-299 | Completed uncommitted: the one-shot case passed 1/1 and observed a forward `virtual` invalidation as the first post-action event; the prior no-event result did not reproduce. |
| DLV-300 | Stopped red uncommitted: after Enqueued action admission, the permanent case received neither invalidation nor failure before the unchanged deadline. |
| DLV-301 | Invalidated as product evidence: its direct-worker fixture sent the generated cursor action into a wrapper-only route and never reached `TryHandlePagination`. |
| DLV-302 | Completed uncommitted: passive markers located DLV-301's first red at that fixture-routing mismatch before the cursor resource. |
| DLV-303 | Completed green uncommitted: corrected lifecycle-first direct worker proved exact action entry, Started cursor admission, Succeeded completion, and forward invalidation 1/1. |
| DLV-304 | Notification/session boundaries green uncommitted; sole red was an invalid Append expectation on base-zero `GetSnapshotAsync`, which correctly returned durable eight-item Replace. |
| DLV-305 | Completed green uncommitted: exact-base atomic update produced generation-2 Append and base-zero checkpoint produced the same durable generation-2 Replace, 1/1. |
| DLV-306 | Accepted test-only `5fe7a5f`: permanent Bridge exact-base Append/base-zero Replace convergence passed 1/1; four notification-evidence files committed. |
| DLV-307 | Stopped first red: full Bridge passed 95/96; the residency-count refusal message assertion failed, so Spotify, Tier 2, and Tier 3 were skipped and no commit was created. |
| DLV-308 | Completed diagnostic-only: isolated prefix reproduced 0/1 and proved a stale Bridge wrapper-message expectation, not suite state contamination or a production budget defect. |
| DLV-309 | Stopped first red uncommitted: corrected residency prefix passed 1/1 and full Bridge passed 96/96; Spotify failed during build without a diagnostic, so Tier 2 and Tier 3 were skipped. |
| DLV-310 | Completed diagnostic-only: one explicit build exposed six deterministic errors from a stale Spotify test fixture using the removed combined playlist-items contract; no production defect. |
| DLV-311 | Stopped first red uncommitted: service fixture now matches the separated metadata/page contract; build exposed two stale raw-snapshot presentation fixtures before tests ran. |
| DLV-312 | Stopped first red uncommitted: build green; Spotify 51/54 with protocol-version, adjacent-failure pagination, and manifest-version assertions red; Tier 2 and Tier 3 skipped. |
| DLV-313 | Completed source-only: protocol 19 and manifest 0.3.12 are test drift; automatic cursor-edge retry while Error is visible is a generic SDK production defect. |
| DLV-314 | Physically accepted production `992b77b`, visibly running as PID 21672 with Spotify 0.3.13 selected/enabled. |
| DLV-315 | Stopped first red uncommitted: authorized test edits remain held; WidgetSdk build failed before execution with no causal diagnostic, so later groups were skipped. |
| DLV-316 | Completed diagnostic-only: serialized Release build passed with 0 warnings/errors after Roslyn named-pipe denial fell back to local compilation; no source/product failure. |
| DLV-317 | Stopped first red uncommitted: WidgetSdk 88/89 exposed projected virtual availability without matching admitted Error-state boundary actions; later groups skipped. |
| DLV-318 | Physically accepted production `32a2a5e`, visibly running as responsive PID 126208 with Spotify 0.3.14 selected/enabled. |
| DLV-319 | Test-only `199a81b`: WidgetSdk 89/89, Spotify 54/54, Tier 2 6/6 green; exact-commit Tier 3 stopped at Runtime 82/84 with two reds and skipped later steps. |
| DLV-320 | Completed source/artifact-only: both Runtime reds are exact test drift—shared revocation-signal ordering and first-invalidation cursor timing; no product defect. |
| DLV-321 | Stopped before edits: assignment incorrectly required 64 items while the authoritative Runtime fixture is page-size 4, retained 8, total 12. |
| DLV-322 | Stopped first red uncommitted: build green, complete Runtime run stalled after 9 passes because the test awaited request termination before releasing its intentionally held grant; owned process terminated. |
| DLV-323 | Stopped first red uncommitted: build green, complete Runtime run stalled after 9 passes because it awaited a pre-release companion revocation that source semantics do not promise. |
| DLV-324 | Test-only `6b63edf`: Runtime 84/84 green; exact-commit Tier 3 stopped later at retained Windows Spotify provider compile drift. |
| DLV-325 | Test-only `e441f25`: provider 32/32 green; exact-commit Tier 3 stopped later at retained Runtime Job helper file-publication cleanup race. |
| DLV-326 | Test-only `676cd76`: Runtime 84/84 and every preceding Tier 3 managed gate green; Tier 3 stopped later at retained native real-host pagination compile drift. |
| DLV-327 | Stopped first red uncommitted: plural geometry migration compiled, but retained forward frames rendered interior focus while asserting trailing-edge actions. |
| DLV-328 | Assigned one-file exact viewport-boundary correction, complete native gate, and one exact-commit Tier 3 verifier. |
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
