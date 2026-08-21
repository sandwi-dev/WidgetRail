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
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-290 `b14dfdc` is committed and review-clean. DLV-291 exact prefix passes 1/1 and remains in the held cumulative matrix. Execute DLV-292 below. Preserve production, packages, state, and PID 129420. Do not rebuild, install, launch, terminate, integrate main, or push. |

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

1. Widgets test queue: correct DLV-292, then resume and commit the cumulative
   DLV-278–283 evidence only if all remaining runs pass.
2. Reviewer integration queue: independently review all evidence; integrate the
   accepted production/test chain into local main only if all required evidence
   passes.
3. Platform production queue: assign DLV-284 after integration, before any new
   virtualization feature.
4. DLV-248 remains deliberately deferred until explicit user promotion.

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
| DLV-292 | Assigned event/state correlation for the Bridge virtual-window completion fixture. |
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
