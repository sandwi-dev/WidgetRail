# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-10
Planning owner: independent review and delivery-planning agent
Execution owners: `widgets` implementation lane and `platform` implementation lane

This file is the only authority for selecting implementation work. The
[roadmap](roadmap.md), [known-issues ledger](known-issues.md), and independent
review documents provide direction and evidence; they do not independently
authorize implementation.

## Queue protocol

- Each implementation task has one stable lane identity assigned in its task
  prompt. It executes only that lane.
- Each lane has at most one `Assigned` milestone and an ordered `Ready` queue.
  After committing a milestone, a task immediately takes the first same-lane
  Ready item when its dependencies are already present in its branch. It never
  waits for planner review of the closing commit.
- A task never reorders, merges, broadens, or invents assignments and never
  selects work from another document or lane.
- Every assignment normally produces one coherent local commit whose subject
  begins with `[DLV-nnn]`. Nothing is pushed.
- Implementation tasks update directly affected public documentation and
  `docs/implementation-status.md`. They never edit reviewer-owned planning,
  roadmap, issue, review, or goal documents.
- A reproducible P0 may interrupt the queue. Other discoveries become concise
  planner evidence rather than opportunistic implementation.
- The planner reviews completed commits asynchronously, integrates only
  accepted contiguous history, and refills both lane queues. If a review finds
  inadequate work after the task has advanced, the correction becomes the next
  same-lane item after the milestone already in progress; ordinary findings do
  not interrupt that work.

### Visible-outcome priority gate

- Reproduced user-visible P0/P1 defects and explicitly requested features
  outrank backend decomposition, test organization, documentation cleanup, and
  other behavior-preserving refactors.
- At least one lane must execute a visible product milestone or its immediate
  named prerequisite whenever safe visible work is unblocked. Both lanes may
  not execute internal-only refactors concurrently in that condition.
- No lane may automatically take more than one consecutive internal-only
  milestone before a visible one unless the internal milestone fixes a
  reproduced P0, directly blocks the named visible successor, or is required
  for the next public release.
- An internal milestone must state the exact visible successor it unlocks.
  Architecture-hotspot status alone is not sufficient scheduling authority.
- User-visible acceptance requires the freshly built Release overlay or a
  proportional production-host fixture. User reproduction overrides synthetic
  or body-only evidence and reopens the affected issue.
- If a top visible item requires a user architecture choice, preserve that
  work and advance another visible item in the free lane. Do not fill the gap
  with a chain of unrelated backend refactors.

### Architecture non-regression gate

- Production types above roughly 1,000 physical lines, plus smaller types that
  own several independently testable concerns, are architecture-review
  hotspots. Line count triggers review; it is not a design target.
- Measure partial production types across all declarations. A logical type is
  not smaller merely because its members occupy several files; conversely, a
  long file containing independent contracts or stateless facade methods is
  reviewed by actual type ownership rather than file length alone.
- Every hotspot must have one explicit disposition in the engineering-quality
  review: current DLV assignment, ordered Ready work, dependency-blocked work,
  or a cohesive exception with named retained responsibilities.
- A milestone that touches a hotspot must report its before/after responsibility
  map, coordination primitives, and cross-boundary mutable dependencies. It may
  not add another undispositioned hotspot or materially grow an existing one
  without demonstrating why the behavior belongs to the same cohesive owner.
- Partial classes, arbitrary file movement, one-method wrappers, and named
  patterns do not satisfy this gate by themselves. A successful boundary must
  reduce shared mutable knowledge, expose a focused deterministic test seam, or
  let a normal maintenance change be made without understanding the entire
  subsystem.
- Managed test programs above roughly 1,500 physical lines, or smaller harnesses
  that mix unrelated scenario setup, process control, assertions, and domain
  fixtures, are test-architecture hotspots. They require a named disposition,
  but must not interrupt the production-hotspot queue merely to reduce line
  count. A valid cleanup leaves a thin stable runner and cohesive scenario/
  fixture owners; a test-framework migration is not closure by itself.
- A decomposition milestone does not automatically close the hotspot it
  touched. If the retained production owner remains above the review threshold
  or still owns several independently testable concerns, the planner must give
  that residual owner an explicit cohesive exception with named responsibilities
  or a new bounded DLV disposition before accepting later material growth.

### Branch and integration protocol

- The `widgets` task works only in its Codex worktree on
  `codex/impl-widgets`. The active `platform` task works only in
  `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on
  `codex/impl-platform-recovery`. The interrupted
  `codex/impl-platform-visible` worktree is preserved with uncommitted DLV-016
  files and is not active. The original `codex/impl-platform` branch remains at
  `57aa2d5`, but its former DLV-025 worktree is no longer registered or present;
  no task may reconstruct, reset, or otherwise act on that lost uncommitted
  state without explicit user authority.
- Tasks may continue through independent same-lane Ready work. The planner may
  integrate only an accepted contiguous prefix; later commits on the same
  branch remain unaccepted until reviewed.
- Cross-lane protocol, architecture, and shared-file changes are serialized in
  the integration queue. The planner selects one lead lane and an accepted
  baseline before dispatch.
- A lane consumes a new `main` baseline only at a clean committed boundary and
  after an explicit bounded planner instruction. Substantial conflicts stop for
  the user; tasks do not rebase, discard, or guess.
- Reviewer-owned control-plane changes are committed separately on local
  `main`. Implementation tasks never stage them.

### Verification tiers

- **Tier 1 — focused:** compile affected projects and run the directly affected
  deterministic Release suites. Required for every assignment.
- **Tier 2 — grouped integration:** run only the smallest cross-component group
  covering a changed boundary. Do not substitute the complete repository gate.
- **Tier 3 — canonical aggregate:** run only at an assignment explicitly marked
  `Integration checkpoint`, for a verifier/core protocol/security change, or
  when the planner requests it for concrete risk.

Do not run the same Tier-3 aggregate on both a dirty final worktree and the
resulting exact commit. Focused tests may be rerun after relevant edits; do not
repeat unrelated green suites after every correction. Every command has a
bounded timeout and retained failures are evidence, not an invitation to rerun
unchanged code.

Screenshots are optional supporting evidence. Use them only when the intended
window, bounds, state, and authored content are already credible. Immediately
exclude a clipped, malformed, stale, black, partial, wrong-window, or premature
artifact. Do not debug or extend the capture harness and do not infer a product
defect from invalid pixels. Continue with deterministic functional/state/
semantic evidence, then launch the accepted Release for the user's visual
check. Capture-tool implementation requires its own explicit assignment.

### Managed test framework coexistence

- Every newly created managed test project uses `MSTest.Sdk` version `4.3.2`.
- Existing executable scenario suites remain supported and keep their current
  runners, stable case names, exit semantics, and verifier commands.
- Do not schedule a standalone repository migration. Convert an older suite
  only when a future assignment explicitly owns that exact migration or already
  requires a substantial rewrite for a concrete product/testability outcome.
- When the first MSTest project is accepted, the bounded verifier integration
  must run both Microsoft Testing Platform projects and legacy executable suites
  without forcing either model onto helper processes or production-host
  fixtures.
- Adopting MSTest does not by itself close a test-architecture hotspot and must
  not displace visible product work.

## Recently completed

### DLV-058 — Ship generic pinned-surface lifecycle

**State:** Done
**Closing commit:** `e160690` (`[DLV-058] add generic pinned surface lifecycle`)
**Integrated on `main`:** `ae34f9a`

**Reviewer disposition:** Accepted. The public `pinningSupported` manifest
declaration defaults closed and projects only typed catalog data. One native
`WidgetSurfaceCoordinator` owns the admitted widget/package generations, real
tool-window HWND, declarative renderer, bounded one-surface cap, click-through/
Interactive state, snapshot refresh, catalog reconciliation, and exact teardown;
widgets receive no HWND, z-order, renderer, provider, or compositor authority.
Focused evidence passes WidgetProtocol/SDK 86/86, WidgetBridge 70/70, the strict
native catalog parser, the accepted DLV-011 policy fixture's 299 checks, and 33
real-HWND coordinator admission/style/UIA/generation/update/hide/teardown checks.
The measured 10,264,576-byte incremental private working set remains below the
DLV-016 material threshold. Main was fully repackaged without rerunning the
already retained focused suites and visibly launched as PID 3924. Durable
placement and final controller/UIA composition remain explicitly owned by
DLV-068 and DLV-069; physical game/controller/display evidence remains manual.

### DLV-011 — Feasibility gate for host-owned pinned surfaces

**State:** Done
**Closing commit:** `9af2a76` (`[DLV-011] Gate host-owned pinned surface
architecture`)
**Integrated on `main`:** `35df08c`

**Reviewer disposition:** Accepted as an architecture gate, not a shipping
pinning feature. The selected host-owned Win32 tool-window candidate keeps
native window, focus, z-order, lifecycle, placement, and accessibility
authority in OverlayHost; widget input remains a data-only descriptor. A real
HWND fixture covers click-through/focusable policies, independent main-overlay
hide/close lifetime, paired teardown, bounded mixed-DPI/monitor fallback, and
the production UI Automation provider. The focused reviewer rerun passed 299
checks. Five retained Release processes measured a `0.684-0.707 MiB`
incremental private working-set delta, 0% normalized 750 ms idle CPU, and
`0.0003-0.0005 ms` p95 for the deliberately small two-node semantic
projection. This does not prove cross-game pointer behavior, physical display
transitions, GPU/DWM cost, protected media, or YouTube playback; those remain
future bounded gates.

### DLV-016 — Establish native idle and semantic-churn baselines

**State:** Done
**Closing commit:** `df1258e` (`[DLV-016] establish native performance
baselines`)
**Integrated on `main`:** `fee1103`

**Reviewer disposition:** Accepted. One focused native target measures a stable
55-node snapshot and 48-node semantic projection through hidden, visible-idle,
and 256-update phases, while the existing schema-2 sampler supplies the real
Hidden/Visible host process-tree observation. Five fresh Release processes pass
13,120 native checks with projection p95 `0.399–0.591 ms`, private working set
`0.63–1.32 MiB`, bounded node/update/snapshot counts, exact source/executable
hashes, repeated-range evidence, and broad material-regression gates. The real
host sample records Hidden/Visible CPU p95 `0.09737%/0.09773%`, zero hidden
timer messages, and zero post-warmup Direct2D frames. Both reports preserve
unavailable GPU, scheduler, presentation, controller, private-process-tree, and
long-run metrics explicitly; they are dirty focused local evidence, not release
or universal game-time claims. Runner self-tests and 53 documentation contracts
pass; no aggregate ran.

### DLV-015 — Add deterministic real-host accessibility proof

**State:** Done
**Closing commit:** `b371983` (`[DLV-015] compose real-host accessibility proof`)
**Integrated on `main`:** `6d3b093`

**Reviewer disposition:** Accepted. One real HWND now hosts the production
declarative tree and `ProviderHost`, publishes it through `WM_GETOBJECT`, and is
queried through the UI Automation client path. The retained fixture covers
Settings, YT Music, and Spotify names, roles, values, bounds, order, hidden-node
exclusion, Invoke, RangeValue, focus, loading/error, selected/busy/disabled,
and generation focus restoration with 183 composed assertions. Five focused
native accessibility groups, the Release host build, the packaged YT Music
host/UIA fixture, and 52 documentation contracts pass. This closes the missing
deterministic real-host proof, not physical Narrator/MSAA or packaged
AppContainer/UIA evidence, which remain in the manual verification queue.

### DLV-001 — Finish the paused authority-recovery operator surface

**State:** Done
**Closing commit:** `d0c0420`
**Reviewer disposition:** Accepted at code and automated-integration level.
The exact-token recovery path is bounded and host-owned; unauthorized, stale,
malformed, unavailable, failed, and cancelled requests fail closed. Focused
groups passed and stable dirty aggregate `20260810T030727Z-449cac31` passed
41/41. Packaged/manual recovery and a future clean checkpoint remain evidence
debt; installed-widget security is frozen unless a reproducible P0, demonstrated
threat-model violation, or release blocker is assigned.

### DLV-002 — Automatically curate trusted Game entries

**State:** Done
**Baseline:** `d0c0420`
**Closing commit:** `1738618` (`[DLV-002] auto-curate trusted games`)
**Owner:** app-library provider/broker, Games & Apps widget, durable user state

**Reviewer disposition:** Accepted. Schema-v2 state separates automatic
provenance from explicit exclusions; reconciliation walks the bounded trusted
catalog, preserves order/focus, retains missing identities for stable
reappearance, treats replacement IDs independently, hides automatic entries
whose authoritative classification is no longer Game, and preserves exact
opaque launch authority. The final focused evidence passed provider 31/31,
broker 49/49, widget 39/39, documentation 51, and the minimal real-package
AppContainer group 6/6. Two earlier conformance failures were retained and
corrected at their concrete harness assumptions; no canonical aggregate ran.

### DLV-003 — Correct shared button-content geometry

**State:** Done
**Closing commit:** `27b0319` (`[DLV-003] correct shared button geometry`)
**Integrated on `main`:** `703c5bb`

**Reviewer disposition:** Accepted. One native placement model now owns Button
icon, label, and semantic-cue geometry; intrinsic measurement uses the same
width budget as paint, including wrapped and 150% text cases. Focused Release
renderer checks passed 4,685 assertions, the production host built, and the
retained 12-capture matrix reported no renderer diagnostics. The repository
does not currently claim RTL layout support, so no unverified RTL behavior was
added. Physical controller/display sign-off remains in the evidence queue.

### DLV-007 — Make Spotify presentation state coherent

**State:** Done
**Closing commit:** `ff706d2` (`[DLV-007] make Spotify presentation state coherent`)
**Integrated on `main`:** `0941e41`

**Reviewer disposition:** Accepted. Rendering now consumes one immutable
presentation revision and playlist detail is keyed by playlist ID plus a
monotonic selection generation. Forced interleavings cover Back, rapid
reselection, late success/failure, refresh, and Active-lifetime cancellation
and reactivation; Spotify passed 35/35 and the minimal package conformance seam
passed 6/6. Live Spotify and physical-controller proof remain manual evidence.

### DLV-008 — Split Spotify by stable responsibility

**State:** Done
**Closing commit:** `2f42ab8` (`[DLV-008] split Spotify by responsibility`)
**Integrated on `main`:** `80e54af`

**Reviewer disposition:** Accepted. The same partial widget remains the single
lifecycle, state, resource, and action owner, while named route, playback, and
snapshot-only presentation files expose stable responsibilities without a new
public abstraction or behavior change. The source-boundary contract rejects
provider/lock/resource ownership in presentation. Spotify passed 36/36,
documentation contracts covered 51 files, and the minimal installed-package
conformance seam passed 6/6. The accepted package builds and validates, but the
already-installed immutable `0.2.10` generation was not destructively replaced.
This historical acceptance covers source organization and behavior preservation,
not final encapsulation: the aggregate remains one roughly 2,040-line logical
partial type, now explicitly reopened by DLV-043.

### DLV-005 — Hold Y to refresh the selected tray widget

**State:** Done
**Closing commit:** `3fc3770` (`[DLV-005] add tray hold refresh`)
**Integrated on `main`:** `aaf36d9`

**Reviewer disposition:** Accepted. One host-owned five-state recognizer on the
existing visible controller cadence preserves tap Y for reorder and sends one
revalidated hold through the F5 restart authority. Threshold/release edges,
repeat suppression, stale selection, context/device cancellation, reload
failure, target resolution, guide density, and accessibility help are covered.
Focused evidence passed 107 controller checks, 108,547 placement checks, the
Release host build, and 51 documentation contracts. Physical-controller proof
remains in the verification queue.

### DLV-004 — Repair the Games & Apps product surface

**State:** Done
**Closing commit:** `7e0b83e` (`[DLV-004] repair Games and Apps surface`)
**Integrated on `main`:** `76032bb`

**Reviewer disposition:** Accepted. The managed widget now composes Library,
Catalog, loading, empty, and failure states from shared components; bounds the
Catalog to one 32-row semantic page with explicit Previous/Next actions; and
revalidates route plus stable SavedId-derived source before using a current
short-lived AppId. Focused evidence covers 42 widget cases, the 6/6 installed
generic-worker/AppContainer group, 52 documentation contracts, and retained
compact/standard/150%-accessible/wide-high-contrast captures. Physical packaged
controller/display review remains verification evidence.

### DLV-014 — Compose advanced action failure through the production host

**State:** Done
**Closing commit:** `2a160b4` (`[DLV-014] compose advanced action failure route`)
**Integrated on `main`:** `9060f12`

**Reviewer disposition:** Accepted. A deterministic YT Music worker now drives
the real production host action ingress through worker runtime, managed bridge,
native failure drain, painted status, and UI Automation live-region projection.
The bounded fixture proves exact generation/action/source diagnostics, sanitized
copy/logging, focus retention, replacement/expiry, Hide non-resurrection, Stop
cleanup, and no worker restart. Physical GameInput remains manual evidence.

### DLV-009 — Consolidate YT Music lifecycle and render ownership

**State:** Done
**Closing commit:** `08d44db` (`[DLV-009] consolidate YT Music lifecycle ownership`)
**Integrated on `main`:** `304102a`

**Reviewer disposition:** Accepted. SDK Active operation lanes now own and
drain auto-connect, progress, polling, and latest transport reconciliation;
one immutable presentation record supplies render state; and current-attempt
checks reject cancellation-ignoring late pairing, polling, authorization, and
transport outcomes. Reflection coverage confirms zero widget-owned Task/CTS
registry fields, while the remaining state lock and connection/client gates
retain narrow domain serialization roles. YT Music passes 51/51; real companion,
physical controller, and packaged visual evidence remain manual gates.

### DLV-017 — Show a durable Games & Apps warm start

**State:** Done
**Closing commits:** `24a8944`, corrected by `b844fd8`
**Integrated on `main`:** `5aedfe8`

**Reviewer disposition:** Accepted. Schema v3 stores only bounded sanitized
display projection plus durable SavedId membership/provenance/exclusions/order/
selection; every displayed row remains non-authorizing until the current Active
lifetime resolves an exact AppId. Unsupported or semantically invalid pre-
release state resets as a whole before fresh trusted-catalog reconciliation,
rather than retaining compatibility branches or partially migrating values.
Focused Release evidence passes Widget SDK 84/84, worker host 9/9, Games & Apps
49/49, and 52 documentation contracts. Packaged cold-start timing and physical
controller/display evidence remain in the verification queue. Subsequent user
testing exposed separate Library mutation/presentation regressions now bounded
by DLV-024; the accepted authority and whole-state-reset design remains intact.

### DLV-027 — Split Games & Apps by stable responsibility

**State:** Done
**Closing commits:** `df1dc81`, corrected by `69e86ef`
**Integrated on `main`:** `69e86ef`

**Reviewer disposition:** Accepted. The 1,861-line widget no longer owns its
complete presentation, catalog-window policy, schema-v3 mutation/reconciliation,
and CAS storage mechanics. Pure presentation, bounded catalog navigation,
library policy, and storage now have named focused seams while the 1,274-line
widget remains the single lifecycle, action-admission, provider, and committed-
state owner. The correction removed a synthetic render-time revision counter
and proves repeated immutable presentation input serializes identically after
normalizing only the host sequence. Retained focused evidence passes Games &
Apps 56/56 and 52 documentation contracts; the original unchanged lifecycle,
private-state, worker, and installed-package boundaries passed their assigned
focused groups. This is material ownership reduction, not closure of EQ-006 for
the other application-sized widgets.

### DLV-023 — Keep transient Spotify failures on the last-good surface

**State:** Done
**Closing commit:** `3cfdd27` (`[DLV-023] Preserve Spotify last-good refresh state`)
**Integrated on `main`:** `4dc1bd5`

**Reviewer disposition:** Accepted. One widget-private typed policy now
classifies transient, permission, authorization, and configuration failures;
transient refresh and polling faults retain the immutable Ready presentation,
route, focus, and cached collection state with bounded 5/15/30-second backoff,
while fatal states clear provider-derived playback and select safe actionable
copy. Manual refresh and polling converge through the same policy, successful
recovery clears the warning, exception/provider bodies are not rendered, and
generation checks reject cancellation-ignoring results after Active exit.
Focused Release evidence passes Spotify 39/39 on the clean closing commit,
Widget SDK 84/84, generic installed-worker conformance 6/6, and 52
documentation contracts. The three retained verifier bundles were stable but
correctly ineligible as release evidence because they captured the dirty
implementation worktree; no aggregate was required or run. Live Spotify
recovery remains a product verification gate rather than an implementation
blocker.

### DLV-020 — Make widget switching visually continuous

**State:** Done
**Closing commit:** `b0c95ca` (`[DLV-020] make widget switching visually continuous`)
**Integrated on `main`:** `7cda335`

**Reviewer disposition:** Accepted at focused automated-evidence level, then
reopened at product level by DLV-025. The host now retains one previously
admitted snapshot and its surface as visual-only presentation while the
destination worker has no admitted snapshot; input, lifecycle, focus, and UI
Automation authority already belong to the destination. Snapshot admission
starts a bounded 140 ms in-place extent transition from the currently
presented geometry, reduced motion snaps, and interruption retargets without a
new timer or settled work. The focused Release run passed 108,547 placement,
54 targeting, 56 transition, 25 color-key chrome, and 4,685 renderer checks
plus both production-host fixtures. Forty-four reviewed HWND captures retain
Network through delayed Spotify startup, Spotify through delayed Games startup,
and Games through same-identity reload without the `Starting isolated ...`
surface, black clear, square edge, stale extent, or tray loss. Physical display,
controller, and assistive-technology sign-off was still verification debt. The
2026-08-10 user run subsequently showed severe cadence loss, interface flicker,
and exposed gray/black bands during the real Games & Apps resize. That temporal
product evidence invalidates closure of GBA-004; DLV-025 owns the correction.

### DLV-029 — Split Audio Mixer by stable responsibility

**State:** Done
**Closing commits:** `9647718` (`[DLV-029] Split Audio Mixer
responsibilities`), corrected by `091ec51` (`[DLV-029] Close Audio Mixer
command transitions`)
**Integrated on `main`:** `6fc8d73`

**Reviewer disposition:** Accepted as a material bounded responsibility split,
not closure of the residual root hotspot. The original candidate validly moved
complete snapshot-only presentation but left command policy as a mutable bag;
correction `091ec51` makes target/revision/worker/confirmation state private and
exposes closed immutable admission, work, acknowledgement, projection, and
terminal results. Distinct output/input/session tests cover coalescing, newer
revision, provider match/mismatch, failure rollback, cancellation, removal,
and reset. A widget-owned transitive task drain replaces the scheduling-yield
assumption for cancellation-ignoring completion. The widget remains the only
lock, lifecycle, host-service/task, committed-state, action, status, and
invalidation owner; all 42 existing `audio.*` string literals remain exact.
Retained stable dirty run `20260810T155016Z-d0427e67` passes Audio Mixer 35/35,
Widget SDK 84/84, Platform Broker 51/51, Windows Audio 15/15, and all 52
documentation contracts. The 1,880-line residual widget still combines provider
session/subscription/retry lifecycle with committed application orchestration,
so DLV-042 dispositions that remaining concentration before further product
growth.

### DLV-042 — Extract Audio Mixer active provider-session ownership

**State:** Done
**Closing commits:** `37119f7` (`[DLV-042] Extract Audio Mixer provider
session`), corrected by `0a3635a` (`[DLV-042] Close retry and stop ordering`)
**Integrated on `main`:** `f64c35a`

**Reviewer disposition:** Accepted after one bounded lifecycle correction. One
484-line internal Active provider session now owns the linked lifetime, all four
subscription-before-snapshot paths and event pumps, optional retry signals and
attempt replacement, bounded failure classification, and exact terminal drain.
It publishes immutable observations that the widget admits only from the exact
current session; it owns no committed render state, action/focus policy,
command transition, status, or invalidation. The widget root falls from 1,880
to approximately 1,687 lines and has no provider pump, retry semaphore,
provider attempt cancellation source, or subscription startup method. Candidate
`37119f7` initially allowed Retry to race terminal semaphore disposal and let a
widget-side Loading write overwrite a faster Healthy observation. Correction
`0a3635a` makes retry admission/signal/cancellation atomic with Stop and leaves
Loading/Healthy/Failure ordering exclusively on the session observation path.
Deterministic cancellation-callback and immediate-retry handshakes prove drain,
disposal, zero retained subscriptions/attempts, no post-stop publication, and
ordered Loading to terminal state without sleeps. Retained stable dirty run
`20260810T162728Z-cb2f0ef9` passes Audio Mixer 41/41, Widget SDK 84/84,
generic worker 9/9, Windows Audio 15/15, and documentation 52; bounded
correction run `20260810T164357Z-e423fe7e` passes Audio Mixer 42/42 and Widget
SDK 84/84. The residual root receives the cohesive exception recorded in the
engineering-quality review; material growth or a returning provider,
presentation, persistence, or independent coordination concern reopens it.

### DLV-035 — Split the Windows network backend by stable responsibility

**State:** Done
**Closing commits:** `51a6ec4` (`[DLV-035] Split Windows network backend
responsibilities`), corrected by `f9df9b9` (`[DLV-035] Preserve bounded
network deadline delivery`)
**Integrated on `main`:** `d151173`

**Reviewer disposition:** Accepted after three bounded concurrency corrections.
The provider remains the only MTA thread, native-adapter lifetime, committed
snapshot, state-lock, channel, and publication owner while closed command,
operation/deadline, reconciliation, event-projection, and bounded queue-
admission policies have focused seams. The final 836-line backend delegates
one fixed 128-entry queue to a 275-line internal owner: ordinary work is capped
at 124; four physical deadline positions plus one highest-generation overflow
value per operation prevent arbitrary delayed stale timer callbacks from
displacing the current connection or scan deadline. Overflow promotion retains
its original cross-type arrival sequence and receives a new FIFO tail position;
the owner thread still rejects stale generations. A packed closed/count state
balances producers even after the bounded close wait without a disposable-
signal race. Retained correction run `20260810T175643Z-9adefc14` passes Windows
Network Provider 42/42 and all 52 documentation contracts; the original stable
run also passed PlatformBroker 51/51. Both are correctly dirty-worktree focused
evidence, and no aggregate, native adapter, widget, or OverlayHost suite ran.

### DLV-041 — Split Windows network native interop by stable responsibility

**State:** Done
**Closing commits:** `3e6d779` (`[DLV-041] split Windows network native
adapter responsibilities`), corrected by `4060f4b` (`[DLV-041] linearize
native adapter terminal disposal`)
**Integrated on `main`:** `4630866`

**Reviewer disposition:** Accepted after two bounded lifetime corrections. The
1,297-line adapter is now a 404-line singular lifetime owner over separate
connectivity, WLAN profile/scan/connect, radio-transaction, and injected
native-call policies. It retains exactly three handles, three callbacks, one
adapter generation, one lifetime gate, and active/disposing/terminal disposal
authority; extracted policies retain no handle, callback, lock, task, or
disposal ownership. Candidate `3e6d779` originally allowed an admitted snapshot
to re-register IP/connectivity notifications after disposal's cancellation pass
and allowed callback publication to race past disposal. Correction `4060f4b`
linearizes every registration/use/cancellation and publication admission on the
same gate, drains committed publications outside the lock, suppresses admitted-
but-unpublished callbacks, supports reentrant handler disposal without self-
deadlock, and makes concurrent external disposers wait for one terminal exact-
once cleanup. Manually controlled barriers prove those orderings without sleeps.
Retained stable dirty run `20260810T185231Z-21f21f42` passes Windows Network
Provider 51/51, Platform Broker 51/51, and all 52 documentation contracts in
24.827 seconds. No aggregate, live radio mutation, native OverlayHost, or
unrelated widget suite ran.

### DLV-036 — Split Settings by page policy and privileged operations

**State:** Done
**Closing commit:** `fdcf5e7` (`[DLV-036] split Settings responsibilities`)
**Integrated on `main`:** `fdcf5e7`

**Reviewer disposition:** Accepted. The 2,872-line logical partial type is now
2,324 lines: the root falls from 1,090 to 542 lines while remaining the sole
lifecycle, operation-gate, committed-state, service-effect, and invalidation
owner. Snapshot-only presentation, closed navigation and ordinary preference
policy, and exact-token authority-recovery admission/result projection are
directly tested non-partial boundaries; ordinary preference actions cannot
reach privileged recovery, a replaced confirmation token fails closed, and no
token enters the semantic snapshot. The unchanged 794-line installed-widget
and 988-line permission sections remain explicit DLV-044 work, so this does not
claim the aggregate partial hotspot is closed. Retained stable dirty run
`20260810T192328Z-f5898174` passes Settings 49/49, Platform Diagnostics 15/15,
Widget SDK 84/84, and all 52 documentation contracts in 20.525 seconds. No
aggregate, catalog/broker authority change, native suite, or live recovery ran.

### DLV-044 — Replace Settings partial sections with real policy boundaries

**State:** Done
**Closing commit:** `8599694` (`[DLV-044] replace Settings partial ownership`)
**Integrated on `main`:** `316ecb8`

**Reviewer disposition:** Accepted. The roughly 2,324-line logical partial
widget is replaced by one non-partial 1,133-line Settings adapter plus closed
installed-widget and permission policy/presentation owners. The root retains
the only lifecycle, state lock, operation gate, service effects, committed
state, and invalidation authority; extracted policies and presenters consume
and return narrow values and own no host services, lock, task, cancellation,
lifecycle, or invalidation state. Direct tests cover stale installed selection,
catalog failure, exact package-authority replacement, consent grant/revocation,
busy-safe repeatable composition, and pre-admission cancellation while the
existing suite retains activation and authority-recovery lifecycle coverage.
Retained stable dirty run `20260810T195050Z-0c20315d` passes Settings 53/53,
Widget SDK 84/84, Widget Catalog 35/35, Platform Broker 51/51, and all 52
documentation contracts in 32.721 seconds. It is correctly not release-
eligible because it records the stable dirty implementation worktree. The
1,133-line root receives the cohesive exception recorded in the engineering-
quality review; material growth or the return of section presentation, section
selection policy, a second service/lifecycle owner, or independent coordination
reopens it.

### DLV-037 — Split managed worker-session transport from gesture authority

**State:** Done
**Closing commits:** `b6a4de3` (`[DLV-037] split worker session transport
ownership`), corrected by `d339030` (`[DLV-037] linearize worker session
teardown`)
**Integrated on `main`:** `8ea0fd5`

**Reviewer disposition:** Accepted after one bounded terminal-ordering
correction. The 1,027-line client becomes a 964-line singular host lifecycle,
restart-budget, failure-publication, and public-request adapter over one
352-line per-generation session plus focused pending-request and gesture-
reservation owners. The session terminal gate now owns resource transfer,
process/companion/reader start, publication admission, cancellation, bounded
drain, and exact cleanup; Stop/Unload either serialize with construction or
terminalize its exact session so late attachments clean themselves and process
creation cannot follow terminal admission. Exact current-session publication
prevents retired response, invalidation, action-failure, process-failure, and
gesture work from crossing replacement; a cancellation-ignoring old companion
grant is observed and explicitly revoked. Five manually controlled no-sleep
fixtures force construction versus Stop, stale notification/failure, stale
response correlation, gesture replacement, and late grant completion. Stable
scoped run `20260810T204532Z-3fc406c4` passes Runtime 74/74, generic worker
9/9, Bridge 52/52, and documentation 52; because that worktree also preserved
uncommitted DLV-039 Bridge files, exact clean follow-up
`20260810T205357Z-3e49935e` independently passes Bridge 52/52 at `d339030`.
No public API, protocol, native host, threat model, or sandbox authority changed.
The retained root receives the cohesive exception recorded in the engineering-
quality review; new lifecycle/publication/resource ownership or material
unrelated growth reopens it.

### DLV-045 — Diagnose bridge frame ownership under timeout and teardown

**State:** Done
**Closing commit:** `67df1d9` (`[DLV-045] close bridge frame ownership`)
**Integrated on `main`:** `dfbe02d`

**Reviewer disposition:** Accepted. The retained decimal length
`1919951483` is deterministically reproduced when a test timeout abandons its
underlying frame read: that read consumes the successor Stop header and the
next read interprets JSON-body bytes as a length. The test client now owns one
cancelable read and terminally aborts/drains that exact connection on timeout.
Independent forced proof also showed ordinary reply cancellation could split a
frame after its header, so one internal `BridgeFrameWriteBoundary` now owns
serialized reply and event admission; after admission the exact frame either
completes under the fixed four-second deadline or aborts the session before a
successor. Public protocol and framing bytes are unchanged. Two retained
stable dirty-worktree runs pass WidgetBridge 66/66, the integrated Release
package rebuilt successfully, and all 52 documentation contracts pass.

### DLV-019 — Add Audio Mixer dashboard master controls

**State:** Done
**Closing commit:** `6afd60b` (`[DLV-019] Add Audio Mixer dashboard master controls`)
**Integrated on `main`:** `6afd60b`

**Reviewer disposition:** Accepted. The selected Audio Mixer tray card now
publishes exact capability-bearing LB/RB/X actions: LB and RB adjust the current
master output by a clamped five percentage points, while X toggles master mute.
All three labels expose the current value/state before activation. The widget
reuses its existing latest-target output policy, optimistic committed state,
authoritative confirmation/reread, rollback, and sanitized failure feedback;
open-widget Slider behavior is unchanged. The worker capability adapter keeps
only the exact host-issued grant admitted while the physical invocation was
active across the activation round-trip, while the broker still enforces
selection, snapshot generation, exact operation, consent, Visible lifecycle,
expiry, revocation, and single use. Retained focused run
`20260811T003239Z-60b7c837` passes Audio Mixer 45/45, Platform Broker 51/51,
the installed AppContainer worker/bridge/broker route 6/6, and documentation
over 52 files. The freshly rebuilt accepted Release overlay is running for
physical-controller verification; no aggregate or unrelated provider/native
suite ran.

### DLV-026 — Restore bidirectional Audio Mixer scrolling

**State:** Integrated candidate; product acceptance withdrawn after live failure
**Closing commits:** `979de24`, `68efc70`, corrected by `9cc633a`
**Integrated on `main`:** `4957101`

**Reviewer disposition:** Rejected as a complete product fix after the user
reproduced the reverse dead end with both keyboard and controller in the freshly
packaged Release. The candidate addressed one real scale-conversion boundary: a
Slider edge no more than one native raster pixel beyond its otherwise matching
card clip, causing the host to reject later offscreen controls as irrevealable.
One scale-aware native-pixel tolerance now applies to that fixed-edge test while
wrong-axis displacement, more than one pixel of overlap, and controls trapped
inside a non-scroll clip remain rejected. The final screenshot-excluded host
manifest retains 84 functional records: complete 14-control Down and reverse Up
traversal at preferred, constrained, and 150% surfaces, zero leading offset on
return to master output, plus unrelated removal, focused removal with nearest
fallback, and session addition. Direct renderer boundary coverage and the
authenticated production-HWND/UIA fixture passed; invalid capture artifacts
are not acceptance evidence. That fixture begins with a fully populated healthy
12-session snapshot and did not reproduce the user's live four-session state.
When cycling away and back, the product becomes navigable after moving upward
slightly; the live log records `value_clamped [audio.root] scrollOffset ...
outside its safe range` on return. DLV-049 owns the missing live-edge and retained-
state diagnosis and correction. The integrated tolerance remains candidate code,
not evidence that GBA-003 is fixed.

## Widgets lane

Task identity: `widgets`
Branch: `codex/impl-widgets`

The widgets lane follows the non-idling and visible-outcome gates. DLV-040,
DLV-046, DLV-047, DLV-048, DLV-050, and DLV-051 are accepted and integrated on
`main`. DLV-006's public/native prefix, cursor-cycle correction, and final
production-host/UIA composition proof are accepted and integrated through
`9c7438f`. The coherent DLV-022/018/053/054 prefix is accepted and integrated
through `8c1bbdf`: Spotify now uses bounded continuous occurrence-aware lists,
and Games & Apps has lazy trusted artwork whose provider work does not serialize
ordinary bridge traffic and whose exact revalidation revision rotates stale
native pixels. Integration review then found Spotify source changed while its
already-installed immutable manifest remained `0.2.11`. Accepted DLV-055
`efffa53` publishes, installs, selects, enables, and first-renders `0.2.12`; the
fresh Release overlay is visibly running for user verification. An independent
main-worktree rebuild then exposed path-sensitive package output: the same
commit produced a different sealed digest and immutable `0.2.12` correctly
refused replacement. Accepted DLV-043 source commit `6c619e9`, integrated
through `d534410`, replaces the logical partial widget with real value-based
route/action, playback, and presentation boundaries while retaining one
  orchestration owner. DLV-056 candidate `2737852`, integrated through
  `e694316`, proved identical clean-root output but failed the required ordinary
  warm-main refresh: main rebuilt a different DLL/archive from the same commit.
  DLV-057 commit `90cadf4` is accepted and integrated: warm main, repeated
  main, detached-root, installed content, and the planner's post-integration
  main refresh now match exactly at immutable `0.2.14`. DLV-059 candidate
  `c7c354d` established the normalized source split but omitted production
  ownership of its new disposable source lifetimes; DLV-071 is the active
  bounded correction. DLV-072 then replaces the 512-item snapshot capability
  that correctly blocked DLV-060, followed by visible DLV-060, DLV-066, and
  DLV-067. DLV-038 remains deferred test-architecture debt rather than filler
  work.

### DLV-007 — Make Spotify presentation state coherent

**State:** Done; accepted and integrated as `0941e41`
**Baseline:** `1738618` plus the reviewer control-plane commit
**Owner:** Spotify managed widget state, routes, and credential-free tests

**Objective:** Ensure every rendered Spotify screen comes from one immutable
render-facing revision, with playlist detail explicitly keyed to the selected
playlist and generation rather than mutable ambient selection.

**In scope:** a narrow `SpotifyPresentationState` or equivalent; an immutable
playlist-selection key; atomic projection of route/header/resource/error/busy
state; forced-interleaving tests for selection changes, slow detail completion,
Back, refresh, lifecycle cancellation, and stale resource results.

**Out of scope:** live OAuth/Premium proof, Web Playback changes, native paging
redesign, a public SDK state framework, visual redesign, or unrelated class
splitting.

**Acceptance criteria:** no render can combine playlist B's route/header with
playlist A's detail revision; stale completions cannot mutate the current
screen; current 12/12/5 paging, focus restoration, actions, setup, and failure
behavior remain compatible; the state boundary is explicit and testable.

**Verification:** Tier 1 Spotify Release suite plus the smallest existing worker
conformance group needed for changed state serialization. No aggregate.

**Stop/escalate when:** correctness requires a public protocol/SDK change,
native focus semantics, authentication, or a materially different Spotify UX.

### DLV-008 — Split Spotify by stable responsibility

**State:** Done; accepted and integrated as `80e54af`
**Baseline:** `ff706d2`
**Owner:** Spotify managed widget internals

**Objective:** After coherent state exists, separate lifecycle/action wiring,
domain/controller behavior, route data, and pure view composition so the
advanced reference reads like maintained application code rather than one
application-sized class.

**In scope:** responsibility-based internal files/types; deletion of obsolete
parallel state/plumbing; pure view builders over the DLV-007 snapshot; focused
structural and behavioral tests.

**Out of scope:** behavior changes, public helper extraction, mechanical
one-method-per-file fragmentation, provider/auth/playback redesign, or styling
work unrelated to the split.

**Acceptance criteria:** lifecycle, cancellation, action, and resource ownership
remain singular; snapshots/actions are behaviorally equivalent; no new public
abstraction or duplicate coordination mechanism appears; file/type boundaries
map to named responsibilities.

**Verification:** Tier 1 Spotify build/tests and the same minimal package
conformance used by DLV-007. No aggregate.

**Stop/escalate when:** preserving behavior exposes a real DLV-007 correctness
gap or requires cross-lane changes.

### DLV-004 — Repair the Games & Apps product surface

**State:** Done; accepted and integrated as `76032bb`
**Baseline:** accepted `main` through `aaf36d9` plus the reviewer dispatch commit
**Dependencies:** DLV-002, DLV-003, and DLV-008
**Owner:** Games & Apps managed widget, its credential-free fixtures, and
directly affected public feature documentation
**Concurrency:** May run with platform DLV-014; no shared protocol, native
renderer, catalog authority, or platform-lane files may change

**Objective:** Use the accepted trusted-game curation and shared Button geometry
to make Library, empty, discovery/loading/error, and Add applications one
coherent responsive controller surface.

**In scope:** empty, short, long-name, maximum bounded page, loading, error,
compact/standard/wide, 100-150% text/interface scale, mutation while focused,
clipping, safe-area, semantic hierarchy, stable focus, and controller
reachability behavior; shared managed component/style use where already public;
representative deterministic captures and exact-commit product evidence.

**Out of scope:** public protocol/SDK or native renderer changes, new catalog or
launch authority, launcher/store expansion, title/path-derived identity,
undocumented Windows APIs, or unrelated visual redesign.

**Acceptance criteria:** automatically curated Games and explicitly added Apps
share one professional hierarchy; Add/empty/loading/error/library transitions
preserve a valid stable focus or deterministic fallback; every essential action
is reachable without clipping across the named surfaces/scales; long and
maximum-page data remains bounded; no widget-local geometry workaround appears;
retained semantics/captures cover the full state matrix.

**Verification:** Tier 1 Games & Apps Release tests, affected managed component
and documentation contracts, plus the smallest native semantic/capture target
needed for exact shared-geometry consumption. **Integration checkpoint:** after
the coherent DLV-004 commit, run the canonical Tier-3 verifier exactly once from
that clean exact commit and inspect its machine-readable provenance.

**Stop/escalate when:** completion requires native/protocol changes, a new
catalog/launch contract, physical display/controller action, or a materially
different Games & Apps information architecture.

### DLV-017 — Show a durable Games & Apps warm start

**State:** Done; accepted and integrated as `5aedfe8`
**Baseline:** `08d44db`, the accepted widgets-lane DLV-009 closing commit
**Owner:** Games & Apps private-state projection, lifecycle reconciliation, and
credential-free fixtures

**Objective:** Make a cold worker or overlay restart show the user's persisted
Library, order, selection, explicit additions, and exclusions immediately while
fresh opaque launch authority and automatic trusted-Game changes reconcile in
the background.

**In scope:** one bounded current schema that persists sanitized display
projection separately from short-lived AppIds; an atomic documented reset of
unsupported older local overlay state rather than partial migration or silent
truncation; disabled/checking launch state until exact resolution; last-good-
first rendering; lifecycle-owned background resolve plus catalog
reconciliation; atomic replacement, missing/reappearing identity,
authoritative reclassification, exclusions, CAS conflicts, and failure/retry
behavior; accurate stale/checking/accessibility copy.

**Out of scope:** retaining obsolete pre-release schema compatibility solely to
preserve this development user's local state; partially reinterpreting legacy
state; persisting AppIds, raw paths, AUMIDs, Steam IDs, commands, or unbounded
icon pixels; launching stale entries; changing provider discovery; new store
adapters; or hiding a failed authority refresh.

**Acceptance criteria:** unsupported older state resets atomically as a whole
and cannot partially preserve membership, exclusions, projection, selection,
or launch authority; a fresh widget instance renders the bounded current-
schema Library before a delayed provider completes; stale rows cannot launch;
exact resolved rows become actionable without focus/order churn; current-
schema additions, removals, exclusions, recent-first ordering, and selection
survive overlay and worker restart; failed refresh retains last-good display
with actionable safe status; background exit cancels and rejects late
completion.

**Verification:** Tier 1 Games & Apps, private-state, lifecycle, and generic
worker Release suites plus one bounded fresh-worker delayed-provider fixture.
No aggregate.

**Stop/escalate when:** the design requires new ambient persistence authority,
stores executable/provider identity, exceeds private-state bounds, or requires
a public protocol change.

### DLV-009 — Consolidate YT Music lifecycle and render ownership

**State:** Done; accepted and integrated as `304102a`
**Baseline:** `7e0b83e`, the accepted widgets-lane DLV-004 closing commit
**Owner:** YT Music managed widget and existing public SDK primitives

**Objective:** Make YT Music the second advanced proof that supported SDK
operations/resources own cancellation, stale-result rejection, and one
render-facing state without widget-local task registries or hidden generations.

**In scope:** credential-free fake companion flows; Created/Active/State/Widget
lifetime mapping; immutable render state; pairing/reconnect/transport failure
and cancellation cases; deletion of superseded local coordination.

**Out of scope:** real companion credentials, Google/YouTube authentication,
new public primitives without two demonstrated consumers, native UI changes,
or feature expansion.

**Acceptance criteria:** no work survives its declared lifetime; late pairing,
polling, transport, and progress results are rejected deterministically; task
failures are observed; existing disconnected/connecting/pairing/connected/error
behavior and contextual actions remain intact; tests quantify removed custom
coordination rather than only moving it.

**Verification:** Tier 1 Widget SDK and YT Music Release suites plus the smallest
existing generic-worker group covering lifecycle serialization. No aggregate.

**Stop/escalate when:** a missing SDK primitive is genuinely required; report
the repeated pattern and consumer evidence before changing the public API.

### DLV-024 — Stabilize Games & Apps library continuity and density

**State:** Done
**Baseline:** `b844fd8`, the accepted widgets-lane DLV-017 closing commit
**Closing commit:** `d80d9ec` (`[DLV-024] stabilize Games library continuity`)
**Integrated on `main`:** `6f401ea`
**Owner:** Games & Apps managed presentation/state mutation, surface hints,
credential-free fixtures, and directly affected feature documentation

**Reviewer disposition:** Accepted. Mutation is now commit-before-publish;
write failure retains the whole prior projection, one forced CAS conflict
preserves unrelated membership/order/exclusions, and delayed, failed, and
fresh-worker cases keep one last-good Library with exactly one Add applications
action. The 600-DIP preferred height adds more than two normal row pitches when
safe area permits. Focused Release evidence passed Games 52/52, SDK 84/84,
private state 10/10, and generic-worker 6/6; 16 retained body captures and seven
semantic snapshots have zero renderer diagnostics. The 1,861-line orchestration
type remains architectural debt and is intentionally the next assignment,
DLV-027, rather than being accepted as the maintainable end state.

**Objective:** Correct the reproduced post-DLV-017 product regressions: removing
one saved entry from Add applications must not make the other Library rows
disappear, the Add applications action must remain visually stable throughout
Library refresh/reconciliation, and the normal surface should use available
vertical space to show materially more entries.

**In scope:** a deterministic remove-one-from-catalog/Back/re-render/restart
reproduction over schema v3; atomic in-memory and persisted mutation;
membership/provenance/exclusion/display-projection consistency; persistence
failure and CAS-conflict rollback/reconciliation; last-good Library rendering
during background work; stable keyed selection/focus after removal; measured
Library/Catalog preferred-height adjustment within the documented host safe
area; compact/standard/wide and 100-150% scale semantics/captures.

**Out of scope:** native-host transition or renderer changes, public SDK or
protocol changes, DLV-006 continuous collections, trusted artwork, new catalog
authority, storing AppIds, per-widget renderer offsets, or preserving obsolete
pre-release schemas.

**Acceptance criteria:** removing one explicit or automatic saved row changes
only that row and its intended exclusion/provenance state; every other row,
order, display projection, and exact current-lifetime launch admission survives
Back, invalidation, worker restart, delayed/failed provider reconciliation, and
one forced CAS conflict. A Ready Library snapshot always contains exactly one
reachable Add applications action; background work may annotate/disable
affected controls but cannot replace the last-good Library with a transient
loading tree. At standard 100% scale the preferred surface shows at least two
more normal rows than the accepted 430-DIP baseline when the host safe area
allows it, while compact and 150% layouts remain bounded, scrollable, and
unclipped. Focus moves to the nearest surviving stable row or the Add action.

**Verification:** Tier 1 Games & Apps and private-state Release suites,
documentation contracts, a fresh-worker delayed/failing-provider fixture, and
retained before/during/after semantic plus capture sequences for the three
reported behaviors. No aggregate.

**Stop/escalate when:** the disappearing rows reproduce outside managed widget
state, the height/visibility defect requires a native surface-contract change,
or a correct fix requires public collection/protocol behavior owned by DLV-006.

### DLV-027 — Split Games & Apps by stable responsibility

**State:** Done; accepted and integrated as `69e86ef`
**Baseline:** accepted `main` integration `6f401ea` plus the reviewer
control-plane commit assigning this milestone
**Owner:** Games & Apps managed internals and focused credential-free tests

**Objective:** Turn the stabilized Games & Apps implementation into a
maintainable advanced-widget reference where a developer can change Library
presentation, catalog navigation, or persistence/reconciliation policy without
understanding one application-sized class.

**In scope:** document the before/after responsibility map; preserve one
lifecycle/action orchestration owner and one committed render-facing revision;
separate pure Library/Catalog/state presentation from schema-v3 membership,
projection, exclusion, order, CAS, and provider-reconciliation policy; give
mutation/reconciliation and pure presentation focused seams; remove superseded
coordination and cross-file mutable knowledge.

**Out of scope:** behavior or visual changes, public SDK/protocol abstractions,
DLV-006 collection work, new persistence schema, arbitrary partial-class/file
splitting, one-method wrappers, or a generic widget application framework.

**Acceptance criteria:** DLV-024 behavior and focus remain equivalent; one
route/view change does not require provider or persistence knowledge; one
remove/reconcile/CAS rule can be tested without rendering the complete widget;
lifecycle, action admission, and committed state retain singular owners; the
completion report quantifies responsibilities, coordination primitives, and
cross-boundary mutable dependencies before and after.

**Verification:** Tier 1 Games & Apps, private-state, lifecycle, and source-
boundary Release suites plus the same fresh-worker fixture used by DLV-024. No
aggregate.

**Stop/escalate when:** a meaningful boundary requires public SDK/protocol
changes, duplicates ownership, or cannot preserve the accepted DLV-024 behavior.

### DLV-023 — Keep transient Spotify failures on the last-good surface

**State:** Done; accepted and integrated as `4dc1bd5`
**Baseline:** accepted DLV-027 integration `69e86ef` plus the reviewer
control-plane commit assigning this milestone
**Closing commit:** `3cfdd27`
**Owner:** Spotify managed refresh/polling state, typed provider-failure policy,
and credential-free tests

**Objective:** Eliminate random full-widget `Spotify could not be loaded`
replacement for recoverable refresh/poll/provider faults while preserving
explicit fatal configuration, permission, and authorization states.

**In scope:** typed transient/fatal classification; last-good playback and route
retention; bounded non-blocking status/feedback; automatic backoff and recovery;
refresh parity; generation/lifecycle cancellation; safe diagnostic codes;
forced transient-success, repeated-failure, permission-revocation, provider-
unavailable, malformed-response, and recovery sequences.

**Out of scope:** live OAuth/Premium proof, exposing provider bodies or exception
messages, infinite retry, swallowing authorization expiry, restarting the
worker as recovery, or redesigning Spotify screens.

**Acceptance criteria:** one transient failure never replaces a usable
last-good surface; repeated failures remain bounded and actionable; fatal
permission/configuration/authorization changes still select their exact safe
state; Y/manual refresh and automatic polling converge through one policy;
successful recovery clears the warning without focus/route loss; no work or
retry survives Active lifetime.

**Verification:** Tier 1 Spotify and Widget SDK operation/resource Release
suites plus the smallest generic-worker failure route. No aggregate.

**Stop/escalate when:** evidence identifies a provider/bridge crash, public
error-contract gap, or credential issue rather than widget state policy.

### DLV-028 — Split Network Controls by stable responsibility

**State:** Done
**Baseline:** accepted DLV-023 integration `4dc1bd5` plus the reviewer
control-plane commit assigning this milestone
**Closing commit:** `4ec931b` (`[DLV-028] Split Network Controls by stable responsibility`)
**Integrated on `main`:** `4ec931b`
**Owner:** Network Controls managed internals and credential-free multi-provider
fixtures

**Reviewer disposition:** Accepted. The 2,020-line widget no longer owns its
provider normalization/merge, command admission/feedback, closed action
vocabulary, stable element identity, and complete view composition. The
1,241-line orchestration owner remains the sole lifecycle, host-command,
committed-state, and invalidation owner; pure provider, command, action, and
presentation boundaries share values rather than mutable state. Coordination
changed from one lock, one semaphore, one generation, one field cancellation
source, and two detached observer roots to one lock, one semaphore, one
generation, no field cancellation source, and one SDK-owned Active latest-
operation lane. Focused dirty-worktree evidence passed the SDK build with zero
warnings/errors, Widget SDK 84/84, Network Controls 22/22, generic AppContainer
worker 6/6, and 52 documentation contracts. No aggregate was required.

**Objective:** Replace the application-sized Network Controls class with named,
testable boundaries for multi-provider state merge, command policy, route/action
orchestration, and pure presentation while retaining one coherent widget state.

**In scope:** map provider snapshots/events and their authoritative merge rules;
separate Wi-Fi/Bluetooth/connectivity command and reconciliation policy where
the policies are genuinely independent; isolate snapshot-only view composition;
retain one lifecycle owner and one immutable render-facing revision; remove
superseded locks, task/cancellation registries, and duplicated invalidation only
when focused interleaving tests prove the replacement.

**Out of scope:** new capabilities, Bluetooth/Wi-Fi product features, native
host work, public generic provider/controller frameworks, cosmetic partial
files, speculative shared abstractions, or behavior changes.

**Acceptance criteria:** a developer can modify one route, one provider command,
or one visual state through a named boundary; provider-event versus command
success/failure interleavings remain deterministic; no lifecycle work survives
deactivation; state and lifecycle ownership remain singular; the completion
report compares responsibilities, locks/semaphores/tasks, and cross-boundary
mutable dependencies before and after.

**Verification:** Tier 1 Network Controls, Widget SDK operation/resource, and
smallest generic-worker Release suites with forced provider-event/command,
failure, cancellation, and refresh interleavings. No aggregate.

**Stop/escalate when:** correct separation requires a public capability/protocol
change, introduces a second state/lifecycle owner, or exposes a product defect
that needs its own bounded assignment.

### DLV-010 — Prove the external widget package journey

**State:** Done
**Baseline:** accepted DLV-028 integration `4ec931b` plus the reviewer
control-plane commit assigning this milestone
**Closing commit:** `83cc32d` (`[DLV-010] Prove external widget package journey`)
**Integrated on `main`:** `e68b8be`
**Owner:** managed scaffold/CLI, sample package, and public authoring docs

**Reviewer disposition:** Accepted. A generated project now carries a
content-addressed matching SDK package in a relative offline feed, compiles and
runs a generated lifecycle/state/action snapshot test outside the checkout,
and uses one source-aware bounded build/stage/validate/pack path without
publishing compiler symbols or checkout paths. The retained external fixture
validates, renders, replays, produces byte-identical packages from directory
and project inputs, installs two versions, selects/rolls back, and removes them.
Focused run `20260810T123758Z-6ca1bb73` passed CLI/scaffold 53/53, catalog 35/35,
and 52 Markdown contracts in 45.536 seconds; it is correctly dirty-worktree
assignment evidence rather than clean release evidence. External SDK
publication/API governance and transactional versioned template input remain
separate open work.

**Objective:** Make the recommended community path reproducible from scaffold
through build, semantic validation, package creation, local install, version
selection/rollback, and removal without repository-local project references or
hand-edited generated files.

**In scope:** one generated nontrivial sample using lifecycle/state/actions;
offline/local SDK resolution supported by the repository; deterministic CLI
round-trip fixture; actionable failures; copyable documentation kept in sync by
tests.

**Out of scope:** publishing to NuGet/GitHub, network downloads, signing-policy
redesign, automatic update discovery, IDE extensions, or new widget authority.

**Acceptance criteria:** a clean temporary project completes the documented
journey with bounded commands; the package is installable and rollback/removal
are deterministic; examples compile; no absolute checkout path leaks into the
artifact; failure messages identify the author action required.

**Verification:** Tier 1 scaffold/CLI/package/docs Release suites and one bounded
temporary-directory end-to-end round trip. No aggregate unless the verifier
manifest itself changes.

**Stop/escalate when:** completion requires external publication, credentials,
or weakening package validation/trust boundaries.

### DLV-030 — Split YT Music by stable responsibility

**State:** Done
**Baseline:** accepted DLV-010 closing commit `83cc32d`
**Closing commit:** `549da57` (`[DLV-030] Split YT Music by stable responsibility`)
**Integrated on `main`:** `6b9144d`
**Dependencies:** DLV-009 and DLV-023
**Owner:** YT Music managed internals and credential-free companion fixtures

**Reviewer disposition:** Accepted. The 1,365-line orchestration owner is now
677 physical lines and remains the only lifecycle, client, committed-state,
invalidation, and disposal authority. Closed action routing, immutable
connection transitions and safe status policy, companion confirmation/rollback
and progress reconciliation, and snapshot-only presentation are value-based
directly tested seams. No public API, task registry, cancellation source,
revision counter, lock, or mutable cross-boundary owner was added. Retained run
`20260810T125844Z-07e6c6d2` passed YT Music 55/55, Widget SDK 84/84,
generic worker 9/9, and all 52 documentation contracts in 19.783 seconds; it is
stable dirty assignment evidence and correctly not release-eligible. Real
companion, packaged controller, accessibility, and visual proof remain in the
verification queue.

**Objective:** Preserve DLV-009's single immutable presentation revision and
SDK-owned Active operation lanes while making one transport-confirmation rule
or one screen change possible without reading the complete application-sized
widget.

**In scope:** a before/after ownership inventory; named provider/connection
policy, companion-specific optimistic confirmation and rollback, closed action/
route orchestration, and snapshot-only view composition boundaries; deletion
of superseded cross-boundary mutable knowledge; focused late-result, timeout,
rollback, lifecycle-drain, and repeated-presentation fixtures.

**Out of scope:** live companion credentials, Google/YouTube authentication,
new product features, public SDK/protocol abstractions, coordinator chains,
cosmetic partial files, or a provider-specific public base class.

**Acceptance criteria:** one lifecycle and committed-state owner remains; the
existing SDK Active lanes still own continuing work; provider/confirmation and
view policies are directly testable without the complete widget; one screen or
transport rule changes through a named boundary; late success/failure,
confirmation timeout/rollback, deactivate/destroy drain, and deterministic
repeated presentation remain exact; the completion report quantifies
responsibilities, coordination primitives, and cross-boundary mutable
dependencies before and after.

**Verification:** Tier 1 YT Music, Widget SDK operation/lifecycle, smallest
generic-worker, and affected documentation Release suites. No aggregate.

**Stop/escalate when:** a meaningful boundary requires a public SDK/protocol
change, duplicates lifecycle or committed-state ownership, needs credentials,
or exposes a separate product defect outside this architecture milestone.

### DLV-031 — Separate capability-domain policy from broker authority

**State:** Done
**Baseline:** accepted DLV-030 closing commit `549da57`
**Closing commit:** `ffa1edc` (`[DLV-031] Separate capability domain policy`)
**Integrated on `main`:** `27adec1`
**Dependencies:** DLV-023 and DLV-028
**Owner:** managed `PlatformCapabilityBroker` internals and direct broker policy
fixtures; no native-host files

**Reviewer disposition:** Accepted. The broker remains the sole identity,
declaration, consent, lifecycle, request-lease, dashboard-gesture, revocation,
subscription, and event-sequence authority. Seven closed internal domain routes
move typed decoding, validation, backend execution, and projection out of the
central class; the authority owner falls from 2,377 lines/117,433 bytes to 837
lines/36,377 bytes without public API or protocol changes. The app-library cache
and its serialization gate move together, while the broker state lock and
loopback authority gate remain singular. Retained stable dirty assignment run
`20260810T132445Z-4465b4ae` passes PlatformBroker 51/51, Windows app library
31/31, generic worker 9/9, and documentation 52 in 22.649 seconds.

**Objective:** Keep one broker authority for identity, declarations, consent,
lifecycle, request leases, event sequence, and dashboard gestures while making
one capability domain's strict decoding, validation, and projection changeable
without editing a central multipurpose class and distant switch regions.

**In scope:** a before/after responsibility map; named internal policies or
handlers for audio, network/Bluetooth, app library, media/Spotify, loopback,
secrets, and private state where the policies are independently testable;
explicit typed routing from the singular broker authority; removal of
superseded duplicated validation/dispatch knowledge; malformed, oversized,
stale, revoked, cancelled, and unknown-domain fixtures.

**Out of scope:** public capability/protocol changes, native host work, new
capabilities, a service locator, reflection dispatch, a generic mediator,
duplicated authorization/lease/event authority, or one class per request.

**Acceptance criteria:** `PlatformCapabilityBroker` remains the only authority
owner; each extracted domain boundary is value-in/value-out and directly
testable; changing one existing domain no longer requires coordinated edits to
one central switch plus distant validators; all fail-closed bounds, revocation,
stale-result, cancellation, and event-sequence behavior remain exact; the
completion report quantifies the responsibility and cross-boundary dependency
reduction.

**Verification:** Tier 1 PlatformBroker/capability Release suites plus the
smallest affected provider and generic-worker routing groups. No aggregate and
no unrelated native suites.

**Stop/escalate when:** correct separation requires a public protocol or threat-
model change, creates a second authority owner, or overlaps the preserved dirty
platform worktree.

### DLV-032 — Extract the bridge request dispatcher

**State:** Done; accepted and integrated on `main` as `a92378a`
**Baseline:** accepted DLV-031 integration `27adec1`
**Closing commits:** `8c11a27` (`[DLV-032] Extract bridge request
dispatcher`), corrected by `9abdc6b` (`[DLV-032] Bound bridge dispatcher
drain`)
**Dependencies:** DLV-031 when shared broker/bridge test infrastructure changes
**Owner:** managed `WidgetBridgeServer` request scheduling and direct bridge
fixtures; framing and session ownership remain in the server

**Reviewer disposition:** Accepted. One internal dispatcher owns duplicate IDs,
the global admitted bound, typed per-widget FIFO tails, fatal cancellation,
terminal cleanup, and a production-enforced two-second drain deadline. One
closed classifier strictly decodes every known request into a global or
canonical widget scheduling key; malformed and unknown requests receive no
implicit widget key. Deadline-expired cancellation-ignoring tasks release IDs,
tails, and slots while remaining observed in quarantine until termination, and
cannot publish a late reply or session fatal. The retained grouped run
`20260810T144506Z-bbd2dbe6` passed WorkerHost 9/9 and exposed a real Bridge
predecessor-failure regression (51/52); the corrected final run
`20260810T145134Z-be1c9952` passes Bridge 52/52, and documentation run
`20260810T145223Z-665e4ef0` passes all 52 Markdown files. These are stable dirty-
worktree assignment artifacts, not release evidence. No public protocol/API,
native files, session authority, Stop authority, or write ownership changed.
The retained 1,312-line server remains a separate hotspot now dispositioned by
DLV-039 and DLV-040; DLV-032 closes scheduling only.

**Objective:** Give global/per-widget request admission, duplicate IDs, FIFO
tails, completion cleanup, fatal-session cancellation, and bounded drain one
narrow directly tested owner without turning the bridge into a generic task
framework.

**In scope:** an internal `BridgeRequestDispatcher` or equivalent; an explicit
decision about `ClientRegistration.OperationGate`; manually completed handlers;
success, failure, cancellation, predecessor-failure, duplicate, capacity, and
forced-drain cases; deletion of superseded scheduling state from the server.

**Out of scope:** framing/envelope changes, reserved Stop-lane redesign,
catalog/client ownership changes, write-path changes, public protocol changes,
native host work, or reflection/generic mediator infrastructure.

**Acceptance criteria:** one dispatcher owns every scheduling invariant and
leaves zero residual IDs, slots, tails, or tasks after every terminal path;
same-widget ordering and cross-widget/global bounds remain deterministic with
no sleep-based proof; `WidgetBridgeServer` retains framing, strict decoding,
session/catalog ownership, Stop authority, and writes; no second implicit
serialization policy remains.

**Verification:** Tier 1 managed bridge/dispatcher Release suites and the
smallest generic-worker failure/drain route. No aggregate unless a verifier or
public protocol unexpectedly changes, which requires planner escalation first.

**Stop/escalate when:** extraction requires a protocol/threat-model change,
duplicates client/session authority, or touches native platform work.

### DLV-034 — Split the Spotify platform backend by stable responsibility

**State:** Done; accepted and integrated on `main` as `a92378a`
**Baseline:** unaccepted DLV-032 candidate `8c11a27`
**Closing commits:** `a5c80ac` (`[DLV-034] Split Spotify backend
responsibilities`), corrected by `344ab48` (`[DLV-034] Prove refresh
cancellation and disconnect isolation`)
**Dependencies:** DLV-023 and DLV-031
**Owner:** managed Windows Spotify provider internals and injected-transport
fixtures

**Reviewer disposition:** Accepted at code and focused assignment-evidence
level. The central backend falls from 1,724 lines/81,619 bytes to 1,032
lines/48,862 bytes while retaining singular package identity, OAuth/PKCE,
vault, token refresh, 401 replacement, lifecycle, local-player, and event
authority. Playback/collection endpoints, bounded HTTP retry/rate-limit policy,
and strict response parsing are narrow internal owners without token/session or
browser authority. Correction `344ab48` adds manually controlled backend proof
that a cancellation-ignoring refresh cannot publish/cache an access token,
rotate/save refresh credentials, or authorize the next request, and that
Disconnect stops local playback, clears vault and cached-token state, and
prevents session reuse. Retained runs `20260810T140244Z-2b2f8821` and
`20260810T141733Z-0e24d4e1` pass provider 32/32 then 34/34; the first also passes
PlatformBroker 51/51. Documentation run `20260810T141833Z-a8567e3e` passes all
52 Markdown contracts. These are stable dirty-worktree milestone artifacts,
not release evidence. No public provider/broker protocol changed, and live
Spotify remains manual evidence.

**Objective:** Preserve one integration identity and token-session owner while
making one Spotify endpoint family, retry rule, or response parser changeable
without constructing or understanding the complete authorization/browser/local-
playback backend.

**In scope:** a before/after ownership inventory; named OAuth/PKCE and vault
policy, bounded HTTP retry/rate-limit policy, playback/device/local-transfer
commands, collection queries, and strict response parsing boundaries where
they are independently testable; injected deterministic transport; deletion of
duplicated endpoint/session knowledge.

**Out of scope:** live OAuth/Premium proof, credentials, public provider/
capability protocol changes, one class per endpoint, a generic REST framework,
duplicated token authority, widget presentation changes, or new Spotify
features.

**Acceptance criteria:** one identity/token-refresh authority remains; endpoint
and parser boundaries receive explicit bounded values and cannot bypass scope,
response-size, retry, cancellation, or event-publication policy; concurrent
token demand, cancellation-ignoring refresh, 401 refresh, 429/backoff,
malformed/oversized responses, disconnect, and stale integration identity are
deterministic; one endpoint family can be tested without browser/vault/local-
player construction; the completion report quantifies ownership reduction.

**Verification:** Tier 1 Windows Spotify provider and affected broker mapping
Release suites with injected transport. No live account, generic aggregate, or
unrelated widget suite.

**Stop/escalate when:** the split requires credentials, public protocol or
threat-model changes, a second token/session owner, or behavior owned by the
Spotify widget rather than the platform backend.

### DLV-035 — Split the Windows network backend by stable responsibility

**State:** Done; accepted and integrated as `d151173`
**Baseline:** accepted DLV-042 integration `f64c35a` plus the reviewer
control-plane commit assigning this milestone
**Closing commits:** `51a6ec4`, corrected by `f9df9b9`
**Dependencies:** DLV-028 and DLV-031
**Owner:** managed Windows network provider internals and deterministic native-
adapter fixtures; no widget presentation or native overlay-host files

**Objective:** Preserve one owner thread and one committed provider state while
making one scan/connect/radio command rule, timeout, or event projection
changeable without understanding the complete roughly 1,186-line backend.

**In scope:** a before/after responsibility and coordination inventory; named
command admission/execution, connection and scan timeout policy, provider-state
reconciliation, and event projection boundaries where independently testable;
deletion of superseded queue/timer/equality knowledge; keep the native adapter
behind its existing bounded interface.

**Out of scope:** new Wi-Fi/Bluetooth features, public capability/protocol
changes, platform-host work, undocumented Windows APIs, a generic command bus,
one class per command, or cosmetic movement of P/Invoke declarations.

**Acceptance criteria:** one owner thread and committed-state owner remain;
commands, timers, and event publication have explicit deterministic ordering;
one scan/connect/radio rule can be tested without constructing the entire
backend; cancellation, late timeout, provider churn, degraded recovery,
disposal, and duplicate event suppression remain exact; the completion report
quantifies responsibility and cross-boundary mutable-dependency reduction.

**Verification:** Tier 1 Windows Network provider and smallest affected broker
network-mapping Release suites with manually completed adapter operations. No
aggregate or unrelated widget suite.

**Stop/escalate when:** correct separation requires a public protocol, changes
provider authority or Windows behavior, introduces another owner thread/state
owner, or overlaps the preserved native platform worktree.

### DLV-041 — Split Windows network native interop by stable responsibility

**State:** Done; accepted and integrated as `4630866`
**Baseline:** accepted DLV-035 integration `d151173` plus the reviewer
control-plane commit assigning this milestone
**Closing commits:** `3e6d779`, corrected by `4060f4b`
**Dependencies:** DLV-035
**Owner:** managed Windows network native-adapter internals and deterministic
interop fixtures; no widget, broker, public capability/protocol, or native
OverlayHost files

**Objective:** Preserve one adapter lifetime, WLAN handle, native callback
registration, generation, and disposal authority while making connectivity
projection, saved-profile/scan/connect operations, or Wi-Fi radio transactions
changeable without understanding the complete roughly 1,180-line
`WindowsNetworkNativeAdapter` implementation.

**In scope:** a before/after responsibility, native-resource, callback, and
coordination inventory; one injected bounded native-call seam; named
connectivity/interface projection, WLAN profile/scan/connect, and radio-query/
transaction policies where independently testable; strict native buffer and
identifier validation; callback-to-current-generation event projection;
deterministic open/register/recovery, malformed native data, scan/connect
completion, radio partial failure/rollback, late callback, and disposal cases;
deletion of superseded cross-boundary mutable knowledge.

**Out of scope:** changing the existing `IWindowsNetworkNativeAdapter` product
contract without planner authority, new Wi-Fi/Bluetooth features, provider
command/timer/state policy owned by DLV-035, undocumented Windows APIs, moving
P/Invoke declarations merely to reduce file length, one class per native call,
generic interop frameworks, or hardware-dependent acceptance evidence.

**Acceptance criteria:** exactly one owner opens/closes the WLAN handle,
registers/unregisters callbacks, owns the adapter generation, and publishes
terminal disposal; extracted policies are value-based or consume a narrow
injected native-call boundary and own no competing lifetime; one connectivity,
profile/scan/connect, or radio rule can be tested without constructing the
complete provider backend or using physical hardware; late callbacks and
partial native failures cannot mutate or publish through a disposed/replaced
adapter; native allocations/handles are released exactly once on every tested
terminal path; the completion report quantifies responsibilities, handles,
callbacks, locks, and cross-boundary mutable dependencies before and after.

**Verification:** Tier 1 Windows Network native-adapter/provider Release suites
with manually controlled native-call and callback fixtures, plus the smallest
affected broker network-mapping group. No aggregate, live radio change, or
unrelated widget suite.

**Stop/escalate when:** meaningful separation requires a public contract or
Windows-behavior change, duplicates WLAN/callback/disposal authority, cannot be
proved without physical network mutation, or overlaps the preserved native
platform worktree.

### DLV-036 — Split Settings by page policy and privileged operations

**State:** Done; accepted and integrated as `fdcf5e7`
**Baseline:** accepted DLV-041 integration `4630866` plus the reviewer
control-plane commit assigning this milestone
**Closing commit:** `fdcf5e7`
**Dependencies:** DLV-001 and DLV-041 only for queue order
**Owner:** managed Settings widget internals and direct Settings fixtures

**Objective:** Keep one widget lifecycle and committed settings state while
making one ordinary settings page, persistence rule, diagnostic projection, or
authority-recovery workflow changeable without reading the current roughly
2,822-line logical partial type. This milestone owns the main page/orchestration
surface; DLV-044 separately dispositions the installed-widget and permission
partial sections rather than hiding their aggregate size.

**In scope:** a before/after ownership map; pure snapshot-only page composition
and navigation policy; appearance/overlay preference persistence policy;
diagnostic and exact-token authority-recovery projection/action boundaries;
focused success, failure, stale-selection, cancellation, and repeated-render
tests; deletion of duplicated page/action knowledge.

**Out of scope:** new settings, catalog/security-policy changes, authority-
recovery redesign, public SDK abstractions, a universal view-model/base class,
partial-class-only splitting, or visual redesign.

**Acceptance criteria:** one lifecycle and committed-state owner remains; pure
page presentation does not perform I/O; privileged recovery actions cannot be
reached through ordinary preference policy; one page or persistence/recovery
rule changes through a named boundary; busy/error/focus/navigation behavior and
exact-token fail-closed semantics remain deterministic; the completion report
quantifies the before/after responsibility map and identifies every residual
member owned by DLV-044 rather than declaring the partial type closed.

**Verification:** Tier 1 Settings, exact-token recovery, smallest Widget SDK
render/navigation, and affected documentation Release suites. No aggregate or
unrelated installed-widget hardening.

**Stop/escalate when:** the split changes authority, persistence schema, public
SDK/protocol behavior, or requires reopening frozen security work.

### DLV-044 — Replace Settings partial sections with real policy boundaries

**State:** Done; accepted and integrated as `316ecb8`
**Baseline:** accepted DLV-036 closing commit `fdcf5e7`
**Closing commit:** `8599694`
**Dependencies:** DLV-001 and DLV-036
**Owner:** managed Settings installed-widget and permission/consent internals
and direct credential-free fixtures; no catalog, broker, protocol, or native-
host authority changes

**Objective:** Finish dispositioning the aggregate Settings hotspot by making
an installed-widget/catalog-version rule or permission/consent presentation
rule changeable without reaching across one roughly 2,822-line partial widget,
while preserving one Settings lifecycle and committed-state owner.

**In scope:** a before/after aggregate logical-type, field, effect, and
authority inventory; value-based installed-widget selection/version/status and
permission/consent projection/action policies; snapshot-only section
composition; narrow typed decisions applied through the existing root owner;
removal of production `SettingsWidget` partial declarations and superseded
cross-section mutable knowledge; deterministic stale catalog/revision,
selection removal, busy/failure, consent change/revocation, repeated render,
and lifecycle-cancellation fixtures.

**Out of scope:** new Settings behavior, install/catalog/consent authority
changes, security hardening, recovery-token redesign, public SDK/protocol
changes, service locators, generic settings frameworks, arbitrary file
splitting, or moving host effects into presenters/policies.

**Acceptance criteria:** the remaining `SettingsWidget` is one non-partial
lifecycle/committed-state/effect adapter rather than an aggregate multipurpose
type; installed-widget and permission policies expose narrow value APIs and own
no HostServices, committed model, lifecycle, lock, or invalidation authority;
one section rule is directly testable without constructing the complete
widget; exact install, consent, revocation, recovery, focus, and safe-copy
behavior remains unchanged; the closing responsibility map gives every
residual hotspot a cohesive exception or another bounded disposition.

**Verification:** Tier 1 Settings, Widget Catalog/version-selection, affected
Platform Broker consent/revocation, smallest Widget SDK render/lifecycle, and
documentation Release suites. No aggregate or unrelated installed-widget
security suite.

**Stop/escalate when:** meaningful separation changes catalog, consent,
recovery, or threat-model authority; requires public protocol behavior; or
cannot eliminate shared partial-state access without a second lifecycle or
committed-state owner.

### DLV-037 — Split managed worker-session transport from gesture authority

**State:** Done; accepted and integrated as `8ea0fd5`
**Baseline:** accepted DLV-044 closing commit `8599694`
**Closing commits:** `b6a4de3`, corrected by `d339030`
**Dependencies:** DLV-032 and DLV-044 only for queue order
**Owner:** managed `WidgetProcessClient` internals and direct runtime fixtures;
no native host files

**Reviewer evidence:** Candidate `b6a4de3` established the named owners but
allowed Stop to finish while construction could still attach resources, and
checked session identity before rather than at external publication. Correction
`d339030` linearizes terminal resource transfer/start and current-session
publication, explicitly revokes a cancellation-ignoring late gesture grant,
and adds five manually controlled no-sleep client interleavings. Stable scoped
evidence passes Runtime 74/74, worker 9/9, Bridge 52/52, and docs 52; exact clean
follow-up `20260810T205357Z-3e49935e` passes Bridge 52/52 at `d339030` after
excluding the preserved DLV-039 files. The accepted disposition is recorded
under Recently completed.

**Objective:** Preserve one worker-session/lifecycle authority while making
process startup/transport, request correlation and drain, content/companion
leases, or dashboard-gesture reservation changeable and directly testable
without understanding the complete roughly 1,027-line client.

**In scope:** a before/after responsibility and resource map; one bounded
session/transport owner; one typed pending-request owner; one dashboard-gesture
reservation/expiry policy behind the existing broker authority; explicit lease
cleanup; deterministic connect, exit, Stop/Unload, stale-session, correlation,
expiry, revocation, and cancellation-ignoring drain fixtures; deletion of
superseded cross-boundary mutable state.

**Out of scope:** native protocol/framing changes, capability or threat-model
changes, sandbox redesign, new dashboard gestures, public SDK changes, generic
event buses, or multiple competing process/lifecycle owners.

**Acceptance criteria:** worker process, pipe/session generation, pending
requests, leases, and gesture reservations each have one named owner and one
terminal cleanup path; Stop/Unload remain responsive and bounded; stale process
exit, response, companion, and gesture results cannot affect a replacement
session; focused fixtures construct each policy without starting the full
product; the completion report quantifies fields, tasks, locks, and mutable
dependencies before and after.

**Verification:** Tier 1 Widget Runtime, smallest bridge correlation/drain, and
generic-worker Release suites. No native aggregate.

**Stop/escalate when:** separation changes the public protocol/threat model,
requires native-host edits, weakens sandbox or gesture authority, or creates a
second lifecycle/session owner.

### DLV-039 — Extract bridge client and residency ownership

**State:** Done; accepted and integrated as `2daae2a`
**Baseline:** accepted DLV-037 closing commit `d339030`
**Closing commits:** `57befdd` (`[DLV-039] extract bridge client registry
ownership`), corrected by `57f3e951`, `e1df09c`, and `a43efc7`
**Dependencies:** DLV-032 and DLV-037
**Owner:** managed `WidgetBridgeServer` client/catalog/residency internals and
direct bridge lifecycle fixtures; no native host or public protocol files

**Reviewer disposition:** Accepted after three bounded lifecycle/publication
corrections. Candidate `57befdd` materially reduces the server from
1,312 to 825 lines and moves catalog, client-generation, residency, idle-unload,
restart, and disposal state behind one 863-line internal registry. Retained
stable dirty-worktree evidence passes Widget Runtime 74/74, WidgetBridge 57/57,
and documentation contracts over 52 Markdown files. Corrections `57f3e951` and
`e1df09c` linearize restart generation replacement and exact-generation
publication, bound each generation to 34 publication leases with coalesced
invalidation and bounded failure retention, transfer canceled restart into
tracked exact-once retirement, perform external cleanup outside the registry
gate, and preserve terminal failures through one bounded outcome. Final
correction `a43efc7` moves event-write admission/deadline/session-abort behavior
behind one 66-line production boundary and directly proves both sides: queued
cancellation writes zero bytes, while cancellation after a header cannot leave
a partial frame followed by another frame because the fixed four-second
deadline aborts the session. It also removes the unused stored retirement task.
The final server is 891 lines; the 1,261-line registry remains the singular
worker-generation/catalog/residency/restart/retirement transition owner and
receives the conditional cohesive exception in the engineering-quality review.

The final Bridge run first retained a 64/65 failure in the pre-existing
`Pipelined requests preserve per-widget receive order` teardown, where the Stop
read observed an invalid length whose bytes resemble JSON-body data; unchanged
rerun `20260810T230321Z-920a6f9b` passed 65/65 and documentation run
`20260810T230603Z-49815699` passed 52 files. The rerun does not explain or close
the framing evidence. DLV-045 owns the bounded diagnosis before DLV-040 changes
the same server again.

**Objective:** Keep `WidgetBridgeServer` as the pipe-session, framing,
handshake, reserved-Stop, request-routing, and serialized-write owner while
making catalog reconciliation, worker registration/replacement, residency,
idle unload, restart, and client disposal changeable without understanding the
complete 1,312-line server.

**In scope:** a before/after responsibility and resource map; one internal
client/registry owner for configured descriptors, current registrations,
generation replacement, worker residency leases/budget, operation gates, idle
unload, restart, catalog revision/reconciliation, and terminal disposal;
value-based status/query seams for request routing and diagnostics; deletion of
the server's duplicated client/catalog mutable knowledge; deterministic
replacement, removal, idle/unload race, restart, failed start, stale generation,
budget refusal/release, concurrent operation, and disposal cases.

**Out of scope:** pipe framing-format changes or write-path changes beyond the
bounded frame-integrity correction required to withdraw queued retired-
generation events without leaving a partial frame followed by another frame;
DLV-032 dispatcher changes; diagnostic/recovery projection owned by DLV-040;
public protocol/capability changes; native host work; a service locator;
generic repository/unit-of-work framework; or more than one client-lifecycle
authority.

**Acceptance criteria:** the server has no direct registration dictionary,
catalog mutation lock, residency budget mutation, idle-unload task ownership,
or registration disposal policy; one registry owns every client generation and
terminal path; request handlers consume narrow typed operations rather than
reaching mutable registration internals; stale replacement/removal/idle results
cannot affect the current client; the completion report quantifies fields,
locks, tasks, cancellation sources, and mutable dependencies before and after.

**Verification:** Tier 1 WidgetBridge catalog/lifecycle/residency/restart suites
and the smallest Widget Runtime worker lifecycle group. No aggregate or native
suite.

**Stop/escalate when:** extraction changes session/framing/write/Stop authority,
requires a public protocol or threat-model change, duplicates worker lifecycle
ownership, or overlaps native platform work.

### DLV-045 — Diagnose bridge frame ownership under timeout and teardown

**State:** Done; accepted and integrated as `dfbe02d`
**Baseline:** accepted DLV-039 integration commit `2daae2a`
**Closing commit:** `67df1d9`
**Dependencies:** DLV-032 and DLV-039
**Owner:** managed WidgetBridge server framing/session internals and direct
WidgetBridge fixtures; no native host, public protocol, or capability files

**Objective:** Explain and eliminate the retained one-run frame misalignment in
the existing pipelined-request teardown without assuming whether the production
reply writer or the test client's timed-out read owns the defect.

**Evidence to reproduce:** In retained run `20260810T225953Z-4b1e10bd`, the new
event-write-boundary scenario passed, but `Pipelined requests preserve
per-widget receive order` failed during `BridgeHarness.DisposeAsync()` Stop with
`Peer announced invalid bridge message length 1919951483`. The value corresponds
to bytes at the start of JSON body data. The unchanged 65/65 rerun passed, so
retry success is not closure.

**In scope:** manually controlled instrumentation around ordinary reply/event
header and body writes, the shared serialized writer, Stop teardown, test-client
read ownership, and timeout/cancellation; a deterministic no-sleep fixture that
forces the responsible interleaving; cancellation/drain of a test read that
times out if the harness currently leaves it active; one exact session-terminal
write policy for ordinary replies as well as events only if production partial-
write exposure is proven; deletion of any superseded hook or duplicated writer
state.

**Out of scope:** changing the public protocol or frame format, native client
work, DLV-040 diagnostics/recovery extraction, dispatcher redesign, retrying or
sleeping until green, broad pipe refactoring, or unrelated reliability/security
work.

**Acceptance criteria:** the retained byte pattern has a named owner and a
deterministic reproduction or a deterministic proof that the production path
cannot create it while the harness can; one read operation and one write frame
have unambiguous lifetime/terminal ownership; a timed-out read cannot remain
active and consume a successor response; cancellation after any admitted frame
header either completes that exact frame under the fixed deadline or aborts the
session before another frame; pipelined Actions/GetSnapshot/Stop teardown passes
without retry-as-policy; the completion report distinguishes the original
failure, forced proof, correction, and final evidence.

**Verification:** Tier 1 WidgetBridge only, including the direct DLV-039 event
boundary and exact pipelined teardown cases. Run one bounded unchanged repeat
after the deterministic case passes. Do not rerun Widget Runtime, documentation,
native, or aggregate suites unless production scope actually expands into them.

**Stop/escalate when:** evidence requires a frame-format/public-protocol change,
native-host edits, a second session/write owner, or cannot distinguish the
production and harness hypotheses without materially broadening scope.

### DLV-019 — Add Audio Mixer dashboard master controls

**State:** Done; accepted and integrated as `6afd60b`
**Closing commit:** `6afd60b`
**Baseline:** accepted DLV-045 integration commit `dfbe02d`
**Dependencies:** DLV-014, DLV-029, DLV-042, and DLV-045 only for lane order
**Owner:** widgets lead over Audio Mixer presentation/action state and the
existing managed dashboard-gesture broker route; native host changes require a
separate planner-approved serialized correction
**Concurrency:** May run while the platform lane executes DLV-026 or DLV-021;
do not touch native renderer, focus, scroll, or compositor files

**Visible outcome:** On the Audio Mixer icon-tray card, LB/RB change master
output volume by a documented bounded step and X toggles master mute, with
current values and action labels visible before activation.

**Objective:** Deliver the requested controller shortcuts through the existing
snapshot-bound dashboard gesture authority without granting broad audio access
or creating a second command/state owner.

**In scope:** exact master-output set-volume and set-mute gesture declarations;
visible labels and current mute/volume state; bounded clamping and rapid-input
coalescing; authoritative reread/reconciliation; rollback and safe feedback;
stale snapshot, lifecycle, selection, consent, authority, replay, and expiry
rejection; preservation of open-widget Slider behavior; credential-free
production-route fixtures.

**Out of scope:** application-session or microphone tray controls, input/output
device selection, new audio capabilities, native input redesign, undocumented
Windows APIs, broad audio authority, polling while hidden, or unrelated Audio
Mixer refactoring.

**Acceptance criteria:** the selected Audio Mixer tray card advertises LB/RB/X
with accurate accessible labels and state; each accepted action holds authority
only for the exact existing master operation and current snapshot generation;
rapid volume input coalesces without stale rollback; mute/volume reconcile to
the provider result; failures retain the authoritative value and show bounded
feedback; unselected, hidden, Background, expired, replayed, revoked, or
wrong-generation actions perform no control; opening Audio Mixer immediately
shows the reconciled result.

**Verification:** Tier 1 Audio Mixer and Platform Broker dashboard-gesture
Release suites plus the smallest generic-worker route. Tier 2 exact managed
worker/bridge/broker action path; no aggregate, native suite, or unrelated audio
provider suite unless the existing route cannot prove the product boundary.

**Stop/escalate when:** delivery requires native-host behavior, a public
protocol/capability change, broader audio authority, physical hardware, or a
second Audio Mixer command/committed-state owner.

### DLV-040 — Extract bridge diagnostics and recovery projection

**State:** Done; accepted and integrated on `main` as `7d5e29d`
**Baseline:** accepted widgets-lane commit `6afd60b`; consume reviewer control-
plane commit containing this assignment at the clean boundary before editing
**Closing commit:** `df304c3` (`[DLV-040] Extract bridge diagnostics recovery projection`)
**Dependencies:** DLV-001, DLV-031, DLV-039, and DLV-045
**Owner:** managed bridge diagnostics/recovery internals and direct typed
diagnostic fixtures; no Settings presentation or installed-widget policy work
**Concurrency:** May run while platform completes DLV-026 and DLV-021; do not
touch native OverlayHost, renderer, focus, scroll, component geometry, or
reviewer-owned files

**Objective:** Keep authenticated request routing and host-effect publication in
`WidgetBridgeServer` while making one diagnostics area, safe projection rule,
or exact authority-recovery retry changeable without reading session, client-
lifecycle, and catalog-mutation code.

**In scope:** a before/after responsibility map; one bounded diagnostic snapshot
owner over injected read-only appearance, consent, catalog, registry, residency,
and provider status; one exact-token recovery projection/retry owner using the
existing authority service; revision, safe label/code, timeout, stale catalog,
partial failure, cancellation, and retry result policy; deletion of superseded
server diagnostic/recovery fields and helpers; deterministic degraded,
unavailable, malformed, timeout, stale, unauthorized, failed, and canceled
fixtures.

**Out of scope:** new diagnostics, Settings UI changes, catalog/security-policy
redesign, broader recovery authority, force cleanup, public protocol/capability
changes, reopening installed-widget hardening, generic telemetry frameworks, or
moving pipe/write/client lifecycle ownership.

**Acceptance criteria:** diagnostic projection performs no client/catalog
mutation and exposes only bounded sanitized values; recovery requires the same
exact host-owned token and fail-closed authority as DLV-001; one diagnostics
area or recovery policy changes through a named focused seam; the server retains
only typed request routing and effect/write publication for this surface; the
closing responsibility map gives the residual server an explicit cohesive
exception or another bounded disposition.

**Verification:** Tier 1 WidgetBridge diagnostics/recovery, Platform Settings
exact-token recovery, and documentation Release suites. No aggregate and no
unrelated security work.

**Stop/escalate when:** separation changes recovery authority or threat model,
requires Settings product changes, broadens installed-widget hardening, or
duplicates catalog/client state from DLV-039.

**Reviewer disposition:** Accepted. One read-only projection now owns bounded
appearance, consent, catalog, registry/residency, provider, and worker status;
one exact-token projection owns recovery list/retry policy through the existing
host authority service. The server retains authenticated routing, worker/client
lifecycle, Stop, and frame publication. Cancellation-ignoring reads are
observed, malformed/partial inputs fail locally without leaking paths or profile
names, and retries preserve the DLV-001 commit gate. Retained focused evidence
passes WidgetBridge 68/68 after the final source edit and all 52 documentation
contracts. The accepted main Release rebuilt successfully; visible launch was
temporarily prevented by the Codex approval service, not by the product build.

### DLV-046 — Make widget scaffolding transactional and versioned

**State:** Done; accepted and integrated on `main` as `06f6cc6`
**Baseline:** closing commit of DLV-040
**Closing commit:** `84ef91b` (`[DLV-046] Make widget scaffolding transactional`)
**Dependencies:** DLV-010; DLV-040 is queue order only
**Owner:** `gbar new`, ControllerWidget template input, focused CLI/scaffold
fixtures, and directly affected public authoring documentation
**Concurrency:** May run while platform owns native UI work; do not touch
OverlayHost, renderer, broker authority, runtime isolation, or reviewer-owned
files

**Objective:** Turn the existing local ControllerWidget directory into a
strict, bounded, versioned input and make `gbar new widget` all-or-nothing, so a
developer never receives a silently mixed or half-generated project.

**In scope:** parse a closed template manifest with one supported version and
explicit relative file inventory; distinguish text templates from bounded
binary assets; reject traversal, reparse points, duplicates, undeclared or
missing files, unsupported versions, oversized counts/files/aggregate bytes,
and invalid replacement destinations; stage generation outside the final
target; atomically publish only after every read, replacement, SDK-package
write, and validation succeeds; remove the staging tree on bounded failure;
actionable errors; deterministic malformed-manifest, unexpected/binary file,
unreadable input, destination fault, and rollback fixtures.

**Out of scope:** external publication, network template feeds, credentials,
signing-policy changes, arbitrary scripting/hooks, new widget capabilities,
template visual redesign, or preserving undocumented pre-release template
formats.

**Acceptance criteria:** the checked-in template is completely described by a
validated versioned manifest; success produces the same supported scaffold and
matching local SDK package as DLV-010; any failure leaves no final target and
no staging residue; user-authored pre-existing output is never deleted or
overwritten; every rejection identifies the file or manifest rule and the
developer action required; generation remains deterministic outside the repo.

**Verification:** Tier 1 GbarCli/scaffold Release suite plus one bounded
temporary-directory end-to-end success and the focused failure matrix. No
aggregate, native suite, or screenshot work.

**Stop/escalate when:** correctness requires changing the public widget API,
weakening path/content bounds, deleting a non-empty user destination, or
introducing a general package/template execution engine.

**Reviewer disposition:** Accepted. Version-1 `template.json` is a closed,
bounded text/binary inventory; traversal, reparse points, duplicates, missing
or undeclared entries, unsupported versions, replacement destinations, and
count/file/aggregate overflow fail before publication. Generation writes the
declared files and matching SDK package to one owned sibling staging directory,
validates it, and publishes by one rename; failure/cancellation removes only
that staging tree and never overwrites author output. Retained focused evidence
passes GbarCli/scaffold 54/54 and all 52 documentation contracts; an independent
main Release CLI build completed with zero warnings and errors. No public SDK,
protocol, native, or runtime authority changed.

### DLV-047 — Establish a checked-in widget SDK compatibility baseline

**State:** Done; accepted and integrated on `main` as `7da7eaa`
**Baseline:** closing commit of DLV-046
**Closing commit:** `7e33f45` (`[DLV-047] Check WidgetSdk compatibility release unit`)
**Dependencies:** DLV-010 and DLV-046
**Owner:** public WidgetSdk package surface, package metadata/validation, CLI
release-unit checks, and directly affected author documentation
**Concurrency:** Managed/package work only; do not change runtime protocol,
host authority, native UI, or reviewer-owned files

**Objective:** Make public SDK changes intentional and reviewable before the
first external release by checking the exported API and the CLI/template/SDK
version relationship into the repository.

**In scope:** one generated or checked-in deterministic public-API baseline for
the supported WidgetSdk package; a bounded compatibility check that reports
additions, removals, and signature changes; one canonical release-unit version
contract binding CLI, template compatibility, SDK package, and generated
project dependency; package metadata and deterministic artifact checks needed
to run it locally; contributor instructions for intentionally accepting a
breaking pre-release reset versus a compatible addition.

**Out of scope:** NuGet/GitHub publication, credentials, signing/revocation,
promising semantic-version compatibility before 1.0, preserving obsolete
pre-release APIs, changing the runtime wire protocol, or adding a broad build
or dependency-management framework.

**Acceptance criteria:** an unreviewed public removal or signature change fails
with the exact symbol and update command/process; compatible additions are
classified explicitly; a generated widget cannot silently consume a template
and SDK version pair the CLI does not support; artifact/package checks are
checkout-path-free and deterministic; the baseline-update workflow requires an
intentional reviewed file change.

**Verification:** Tier 1 WidgetSdk build/tests, GbarCli package/scaffold tests,
and the new compatibility/release-unit check in Release. No aggregate or
external feed.

**Stop/escalate when:** the work requires choosing a post-1.0 compatibility
promise, changing public protocol semantics, external publication, or retaining
an obsolete API solely for legacy support.

**Reviewer disposition:** Accepted with DLV-050 as the required closing
correction. The release-unit contract, bounded deterministic 2,400-symbol API
baseline, exact addition/removal/signature diagnostics, matching CLI/template/
SDK metadata, and portable package checks are credible. DLV-050 replaces the
candidate handwritten test runner with 12 named `MSTest.Sdk` 4.3.2 cases and
moves mutation to a separate bounded tool without changing the reviewed API
baseline or any legacy suite. Final focused evidence is recorded under DLV-050.

### DLV-048 — Compile-test the canonical widget authoring path

**State:** Done; accepted and integrated on `main` as `18d461e`
**Baseline:** closing commit of DLV-047
**Closing commit:** `829e9fd` (`[DLV-048] Compile-test canonical author journey`)
**Dependencies:** DLV-010, DLV-046, and DLV-047
**Owner:** canonical public C# examples, generated starter verification,
documentation contracts, and focused author-journey fixtures
**Concurrency:** Documentation/developer tooling only; do not touch native UI,
runtime authority, first-party widget product behavior, or reviewer-owned
planning/review files

**Objective:** Replace link-only confidence for the primary widget-authoring
instructions with one executable source of truth that a new developer can copy,
build, validate, replay, pack, install, select, roll back, and remove.

**In scope:** identify the smallest canonical lifecycle/state/action/navigation
examples in public docs; move or generate them from compile-tested sample
sources without duplicating divergent snippets; execute the documented clean
temporary-directory path against the checked-in CLI/template/SDK release unit;
assert command/output claims and actionable failure guidance; keep advanced and
authentication-gated examples explicitly separate.

**Out of scope:** compiling every Markdown fence, rewriting all documentation,
new SDK features, live Spotify/YouTube credentials, IDE extensions, external
publishing, native preview expansion, or screenshot work.

**Acceptance criteria:** every code block on the canonical getting-started path
is derived from or compiled as part of the test source; the complete local
author journey succeeds outside the checkout without absolute path leakage;
docs cannot claim a generated test/replay/package behavior the fixture does not
prove; failures name the exact author command or file to correct.

**Verification:** Tier 1 GbarCli/scaffold/documentation Release suites and one
bounded external author-journey fixture. No aggregate, live service, or native
suite.

**Stop/escalate when:** proof requires external publication/credentials, a new
public API, or broad documentation restructuring beyond the canonical path.

**Reviewer disposition:** Accepted and integrated. The exact generated
`VolumeControl.cs` is the only
canonical starter source and is compared byte-for-normalized-byte to the
scaffolded file before that file builds and executes outside the checkout. Eight
marked quickstart blocks bind create/build/test, validate, render, replay,
pack/install, version selection/rollback, and removal claims to the same
external fixture. The fixture also proves actionable invalid-GBSS failure,
three replay actions, byte-identical portable packages, two installed versions,
rollback, and complete uninstall. Retained focused Release evidence
`20260811T040047Z-d8e6fe07` passes GbarCli/scaffold 55/55 and documentation
validation across 53 Markdown files in 39.912 seconds. DLV-050 changed only the
preceding compatibility-test framework/verifier boundary and did not rewrite
this author journey.

### DLV-050 — Adopt MSTest.Sdk 4.3.2 for the new SDK compatibility tests

**State:** Done; accepted and integrated on `main` as `7563471`
**Baseline:** closing commit of DLV-048, including unaccepted DLV-047 candidate
`7e33f45`
**Closing commit:** `263536f` (`[DLV-050] Adopt MSTest for SDK compatibility checks`)
**Dependencies:** DLV-047 and DLV-048 only for contiguous lane order
**Owner:** the newly created WidgetSdk compatibility test project, its bounded
baseline-update tool path, focused verifier entry, and directly affected
contributor documentation
**Concurrency:** Managed test/tooling correction only; do not migrate any
existing executable suite, change WidgetSdk public API, alter runtime protocol,
or touch native/reviewer-owned files

**Objective:** Bring DLV-047's new test surface into the incremental framework
policy: discoverable compatibility/release-unit assertions use `MSTest.Sdk`
4.3.2, while intentional baseline mutation belongs to a bounded ordinary tool
command and every pre-existing executable suite remains unchanged.

**In scope:** change only `WidgetSdk.Compatibility.Tests` to
`MSTest.Sdk/4.3.2`; express deterministic API generation, additions/removals/
signature classification, release-unit consistency, missing/oversized/malformed
baseline, checkout-path rejection, and unchanged-current-surface behavior as
named MSTest cases; move `--update` behavior out of the test process into the
smallest existing contributor-tool owner or one narrow ordinary executable;
make the verifier invoke the MSTest/Microsoft Testing Platform project alongside
unchanged legacy `dotnet run` suites; update the exact contributor command and
retained evidence.

**Out of scope:** converting GbarCli.Tests, WidgetSdk.Tests, or any other legacy
runner; a repository-wide test migration; rewriting the API renderer without a
demonstrated defect; changing the accepted API baseline or release version;
external packages beyond MSTest.Sdk 4.3.2; aggregate verification; product or
runtime changes.

**Acceptance criteria:** the new compatibility project declares exactly
`MSTest.Sdk` 4.3.2 and exposes independently named discoverable cases; ordinary
test execution is read-only and cannot update `PublicApi.txt`; the documented
intentional update command changes only that baseline and remains bounded;
removal/signature/addition fixtures report exact symbols; one focused verifier
selection runs the new MSTest project and representative unchanged legacy
WidgetSdk/GbarCli executable suites without changing their case names, exit
semantics, or project SDKs.

**Verification:** Tier 1 new MSTest compatibility cases plus the unchanged
WidgetSdk and GbarCli focused executable suites and documentation contracts.
Exercise the verifier's mixed-runner selection once. No aggregate, unrelated
managed suite, native suite, or screenshot work.

**Stop/escalate when:** MSTest.Sdk 4.3.2 cannot run under the repository's pinned
.NET SDK without a broader framework/toolchain decision, the correction would
change the public WidgetSdk surface/release unit, or baseline mutation cannot be
separated without adding a broad new build framework.

**Reviewer disposition:** Accepted. Only the new compatibility project adopts
`MSTest.Sdk/4.3.2`; every pre-existing executable suite keeps its project SDK,
case names, exit semantics, and `dotnet run` command. Twelve independently named
cases cover the current baseline, bounded deterministic generation, release-unit
metadata, additions, removals, signature changes, malformed/missing/oversized/
path-bearing input, bounded updater behavior, and read-only ordinary execution.
The reviewed `PublicApi.txt` did not change. A separate ordinary
`WidgetSdkApiBaseline` tool owns atomic intentional updates. The verifier opts
the new project into Microsoft Testing Platform, retains named cases in JUnit,
and keeps helper fixtures outside discoverable-test inventory. Stable dirty
focused run `20260811T041316Z-596d7304` passes the verifier self-test, SDK build,
new compatibility 12/12, unchanged WidgetSdk 84/84, unchanged GbarCli 55/55,
and 53 documentation files in 61.527 seconds. No aggregate or legacy migration
ran.

### DLV-051 — Correct Spotify seek-bar Left navigation

**State:** Done; accepted and integrated on `main` as `822d29c`
**Lane:** widgets
**Baseline:** widgets branch `263536f`, whose accepted implementation content is
integrated on `main` through `7563471`; consume the planner assignment commit at
the clean boundary before editing
**Dependencies:** DLV-007, DLV-008, and DLV-021; independent of DLV-006's
continuous-list contract
**Owner:** Spotify managed responsive focus graph, exact semantic fixtures, and
directly affected Spotify documentation; no native host or public SDK/protocol
files
**Concurrency:** May run while platform owns DLV-049. Do not touch Audio Mixer,
native focus/scroll/layout, shared component geometry, provider/OAuth, or
reviewer-owned files.

**Visible outcome:** In expanded Spotify Player, pressing Left from the inactive
seek Slider moves to the selected navigation-rail destination to its left, not
to Previous track. Compact Spotify moves to the spatially corresponding selected
navigation tab. Right and vertical transport navigation remain predictable.

**Objective:** Correct the reported responsive spatial edge in the widget's
authored focus graph without adding a native spatial exception or coupling it to
playlist paging.

**In scope:** inspect the exact compact and expanded Player snapshots before host
processing; identify the stable selected destination/tab IDs; author explicit
seek Slider Left edges for both responsive compositions; preserve activation-
first Slider behavior, transport-row Left/Right order, route/Back focus, selected
destination persistence, disabled/busy states, and stable IDs across playback,
device, and responsive snapshot refresh; deterministic compact/expanded semantic
and controller-replay cases beginning on the inactive seek Slider.

**Out of scope:** DLV-006 cursor/append collections, page-window anchoring,
playlist header/first-row oscillation, native focus heuristics, per-widget pixel
offsets, Spotify provider/auth/playback behavior, route redesign, public SDK or
protocol changes, live credentials, screenshots, or Spotify architecture
decomposition.

**Acceptance criteria:** in every supported expanded Player state, one Left from
the inactive seek Slider names and reaches the currently selected rail
destination; in compact mode it names and reaches the selected tab. The edge is
explicit in the emitted snapshot and uses stable authored IDs rather than titles
or ordinal parsing. Previous/Play/Next traversal and Slider activation/value
behavior remain unchanged; responsive reflow and a current-route snapshot
replacement preserve a valid corresponding focus target; no native or public
contract special case appears.

**Verification:** Tier 1 Spotify Release suite with exact emitted compact and
expanded focus-link assertions plus the smallest existing controller/navigation
replay that proves one Left step and unchanged transport edges. Build the
Spotify package and validate directly affected docs. No aggregate, provider,
live account, native suite, screenshot, or broad playlist matrix.

**Stop/escalate when:** the selected rail/tab identity is absent from the emitted
snapshot, the host ignores a valid explicit edge, correction requires public
responsive-focus semantics, or current Spotify composition cannot express one
stable corresponding target without a route/product decision. Preserve the
exact snapshot and report instead of adding a widget-local ordinal/native hack.

**Closing commit:** `dc22202` (`[DLV-051] Correct Spotify seek Left
navigation`)

**Reviewer disposition:** Accepted. Both responsive Player compositions now
author the inactive seek Slider's Left edge to a stable selected destination:
`spotify.nav.wide.*` for the wide rail and `spotify.nav.compact.player` for the
compact tab. No host heuristic, provider behavior, SDK/protocol contract, or
playlist paging changed. The focused Release run
`20260811T043026Z-ea419336` passes Spotify 40/40 and documentation across 53
files; the ordinary package path validates and packs Spotify 0.2.10. Fresh live
controller/keyboard confirmation remains in the verification queue.

### DLV-006 — Prove virtualized game-library collection foundations

**State:** Done; accepted and integrated through `9c7438f`
**Closing commit:** `33dccae` (`[DLV-006] compose cursor production-host proof`)
**Lane:** widgets, acting as the serialized cross-lane protocol lead
**Baseline:** accepted local `main` through integrated implementation correction
`679a73b` plus the reviewer commit recording this final gate; consume that main
baseline at the current clean widgets boundary before editing
**Dependencies:** DLV-004, DLV-005, DLV-021, DLV-049, and DLV-051
**Owner:** public WidgetProtocol/WidgetSdk cursor-append collection semantics,
their bounded managed test utilities, native declarative collection/focus/scroll
adoption, and directly affected public author documentation
**Concurrency:** May run while platform DLV-015 changes only accessibility
projection and its isolated fixtures. Do not edit HostAccessibility,
AccessibilityProvider, AccessibilityTree, AccessibilityEvents, the DLV-015
production-host fixture, DLV-025 compositor files/evidence, or reviewer-owned
files. Stop before changing a shared native build/test manifest already modified
by active DLV-015 so the planner can serialize that mechanical boundary.

**Visible outcome:** Auto-loading lists behave like one continuous controller
collection: crossing a fetch boundary keeps the same keyed viewport anchor and
moves to the adjacent item instead of jumping from bottom to top or top to
bottom. The same contract can render thousands of game-library entries without
serializing or retaining all of them.

**Objective:** Establish one strict cursor/append collection and native viewport
contract that directly fixes replacement-page focus jumps and supplies the
bounded list/grid foundation required by Spotify, trusted Games artwork, and the
future Game Launcher.

**In scope:** opaque forward/reverse cursors; append/prepend and refresh results;
stable typed item/focus keys; one explicit viewport anchor; bounded prefetch,
retention, eviction, pending requests, snapshot nodes, and serialized bytes;
lazy opaque artwork handles without decode or file/URL authority; loading,
partial, final, empty, and safe error rows; cancellation, stale completion,
cursor loop/duplication, sparse page, insertion, deletion, and refresh policy;
fixed header/action behavior above a collection; responsive List and Grid
fixtures; deterministic 2,000- and 10,000-item production-style fakes; public
author guidance and compatibility-baseline updates required by the intentional
pre-release API change.

**Out of scope:** migrating Spotify product lists (DLV-022), supplying real
Games artwork (DLV-018), store adapters, search/filter/launch behavior, arbitrary
file or URL loading, embedding artwork bytes in snapshots, widget-local page
caches, title/ordinal-derived identity, provider/authentication work, DLV-025
composition, decorative redesign, screenshots, or a generic data-grid
application framework.

**Acceptance criteria:** page transport boundaries never become focus wrap
boundaries; a keyed focused item and viewport anchor survive forward/reverse
loads, cache hits/eviction, refresh, insertion, deletion, cancellation, sparse
and final pages whenever that key remains valid, with a deterministic nearest
fallback otherwise. Reverse loading with a fixed header action cannot oscillate
between the header and first item. The host retains only the bounded visible/
prefetch window and never receives a 2,000- or 10,000-node snapshot. Cursor
loops, duplicate keys, oversized pages/fields, stale generations, and late
results fail safely. Lazy artwork values are opaque bounded handles and grant no
ambient fetch authority. Existing bounded offset-page resources retain their
documented replacement-window semantics rather than silently changing behavior.

**Verification:** Tier 1 WidgetProtocol, WidgetSdk, API-compatibility, native
layout/renderer/focus/scroll, CLI replay, and documentation Release suites.
Tier 2 one production-host-style cursor collection fixture covering List, Grid,
fixed header, forward/reverse loads, refresh/churn, and 2,000/10,000-item bounds.
**Integration checkpoint:** after the coherent DLV-006 commit, run the canonical
Tier-3 verifier exactly once from that clean exact commit and inspect its
machine-readable provenance before taking DLV-022. Every newly created managed
test project uses `MSTest.Sdk/4.3.2`; existing executable suites remain unchanged.

**Stop/escalate when:** the design requires a new widget authority, arbitrary
image/file/network access, a compositor/window change, material accessibility-
projection overlap with active DLV-015, an incompatible public behavior beyond
the documented pre-release collection contract, or cannot keep the 10,000-item
case within explicit bounded native/snapshot/resource limits.

**Accepted implementation prefix:** Source commits `1d214d9` and correction
`d8dfd8b` are integrated on `main` as `65e4942` and `679a73b`. The prefix adds
protocol-v14 keyed cursor collections, bounded append/prepend retention, native
anchor reconciliation and List/Grid edge handling, opaque inert artwork
handles, authoring/preview contracts, and deterministic 2,000/10,000-item
proof. Review rejected the first candidate's self-loop-only cursor check;
`d8dfd8b` adds direction-scoped multi-hop cycle rejection after eviction while
preserving reverse traversal and evicted-page refetch, plus empty, sparse/
partial-final, last-good error, and documentation corrections. Focused evidence
passes Widget SDK 85/85 and 53 documentation files. The single canonical run
`20260811T052531Z-3eae8cce` passes all 42 steps from clean exact candidate
commit `1d214d9` with stable eligible provenance; it is retained as integration
evidence and must not be rerun merely for the bounded correction.

**Final correction scope:** Use the now-accepted DLV-015 production-host fixture
to compose the real managed cursor snapshot through bridge parsing, native
renderer/anchor state, focus-edge pagination, and UI Automation semantics. One
bounded fixture must cover List and responsive Grid, a fixed header action,
forward and reverse boundary loads, refresh/insertion/deletion churn, stable
focus and viewport anchor, empty/sparse/partial-final/error state, and proof
that 2,000/10,000 logical items never become an unbounded host tree. Document
that one same-direction traversal admits at most 128 distinct cursor identities
before failing closed; direction change or refresh starts a new bounded
traversal and legitimate evicted-page refetch remains supported. Add no new
harness or build manifest, do not alter collection implementation unless this
composed proof exposes a concrete defect, and do not run Tier 3 again.

**Final correction verification:** Tier 1 only: the smallest existing DLV-015
production-host/UIA target, directly affected native cursor renderer/focus
groups, Widget SDK cursor group if production changes are required, and
documentation validation. No aggregate, screenshot, Spotify migration, broad
native matrix, or unrelated suite.

**Reviewer disposition:** Accepted. The existing DLV-015 executable now takes
managed-format cursor frames through the production bridge parser, Direct2D
renderer, keyed anchor/focus pagination, accessibility adapter, real HWND/UIA
provider, and bounded List/Grid/large-logical-collection states. Retained
focused evidence passes 274 production-host checks, 49 native focus checks,
4,777 native renderer checks, Widget SDK 85/85, and 53 documentation files.
The public guide states the 128-identity same-direction fail-closed boundary;
refresh or direction change starts a new traversal. The prior clean 42-step
canonical result remains the sole Tier-3 run.

### DLV-022 — Repair Spotify continuous-list focus

**State:** Done; source `c349bbd` corrected by DLV-053 and integrated through
`8c1bbdf`
**Baseline:** accepted DLV-006 integration `9c7438f` plus the reviewer commit
recording this assignment
**Dependencies:** DLV-006, DLV-021, and DLV-051
**Owner:** Spotify Queue, Playlists, and playlist-detail collection state and
presentation, shared cursor/append consumption, the exact shared SDK cursor-
admission correction exposed by this first consumer, exact focus fixtures, and
directly affected Spotify documentation; no provider, public protocol, or native
collection implementation changes
**Visible outcome:** Queue and Playlist traversal stays continuous across loads;
reverse traversal reaches the header Play action only when intended and never
oscillates between it and the first row.

**Objective:** Replace Spotify's current replacement-page windows with DLV-006's
shared keyed collection while preserving route, Back, selected destination, and
the accepted DLV-051 Player focus edge.

**In scope:** Queue, Playlists, and playlist-item forward/reverse cursor state;
12/12/5 and sparse/final pages; stable keyed anchors; refresh, cache eviction,
rapid route changes, Back/return, cancellation, stale completion, header action,
compact/expanded identity, and safe partial/error presentation; deletion of
superseded widget-local page-window state. After deterministic consumer proof
showed repeated near-edge input replaces an identical pending cursor request,
also make `WidgetCursorResource` join the exact current identical intent, as
`WidgetPagedResource` already does, while preserving latest-wins replacement
for a genuinely different cursor, direction, viewport, or refresh intent. This
correction may change only the shared resource implementation and its focused
existing SDK tests; it adds no public member or wire behavior.

**Out of scope:** Spotify OAuth/Premium/Web Playback/provider changes, search,
new product screens, custom page caches, ordinal/title focus IDs, native
collection exceptions, public SDK signatures or protocol revisions, changing
non-identical cursor intents from their existing latest-wins policy, DLV-051
changes, DLV-043 decomposition, shared geometry offsets, screenshots, or live
credentials.

**Acceptance criteria:** crossing every forward/reverse transport boundary moves
to the adjacent keyed item without top/bottom teleport; existing keys retain
focus and viewport through refresh/append/prepend, with deterministic fallback
after deletion. The Play/header action participates in one explicit authored
edge and cannot alternate with the first row on snapshot replacement. Route and
Back restoration, selected responsive destination, accepted seek navigation,
bounded retention, loading/error copy, and Active-lifetime cancellation remain
exact. Repeating the same pagination action while its exact cursor intent is
pending returns `Joined`, shares the same completion, and invokes `LoadPage`
once; a different intent still replaces/cancels through the established bounded
latest-wins lane, and Reset/lifecycle termination drains both paths.

**Verification:** Tier 1 Spotify and shared collection Release suites,
including direct no-sleep identical-join, different-intent replacement,
Reset, and lifecycle-drain cursor-resource cases, plus the smallest controller
replay/production-host semantic fixture covering forward, reverse, header,
refresh, and route return. Validate the package and affected docs. No aggregate,
provider, live account, broad screenshot matrix, or native collection redesign.

**Stop/escalate when:** DLV-006 cannot express a required keyed anchor/edge,
the native host ignores a valid shared collection state, or the bounded
identical-intent correction requires a public signature/protocol revision,
changes non-identical intent policy, provider/auth behavior, or a material
Spotify UX decision. Report any broader shared defect instead of adding a
widget-local workaround.

**Planner scope correction:** The first consumer run passes 40/41 focused
Spotify cases, including forward/reverse traversal, third-page eviction and
reverse restoration, stable refresh anchoring, and the authored Play/header
edge. The remaining deterministic case proves two provider calls for the same
near-edge action while its exact cursor intent is pending. The shared cursor
resource creates a replacement request unconditionally, whereas the established
paged resource first joins an equal current intent. The bounded parity fix above
is therefore an immediate prerequisite of the visible DLV-022 outcome and is
authorized inside this milestone; a Spotify-local busy flag remains prohibited.

**Initial reviewer disposition:** Rejected. Candidate `c349bbd` correctly adds the exact-
intent shared join and broad continuous-list coverage, but
`SpotifyCollectionIdentity.Media` hashes only the media URI. Spotify queues and
playlists may legitimately contain the same track or episode more than once;
the shared cursor resource rejects those occurrences as duplicate collection
keys within one page or across retained pages, and the action/focus IDs also
collide. The current provider, broker, and public widget summary carry no
occurrence identity, and the candidate has no duplicate-occurrence test. A
second independent graph defect gives the first playlist row an explicit Down
edge to itself when the detail contains one item. Do not integrate `c349bbd` or
dependent commits until DLV-053 supplies bounded occurrence identity and removes
the self edge. Per the asynchronous review rule, DLV-018 continues first.

**Correction disposition:** Accepted at code level through DLV-053 commit
`7f5c2fd`. URI remains semantic media identity while one private, collection-
context-bound policy supplies unique occurrence keys for same-page and retained
cross-page duplicates. Unique items preserve URI-derived keys across offset
changes; distinguishable duplicates retain keys through refresh/insertion/
deletion; otherwise-identical rows use deterministic nearest-equivalent
matching. Matcher state is capped to the resource window, Reset clears it, and
an exact request generation makes cancellation-ignoring superseded completion
side-effect free. Exact occurrence keys flow through focus and playback lookup.
The singleton row retains Up to Play but no longer authors Down to itself.
Retained focused evidence passes Spotify 45/45, installed-worker conformance
6/6, documentation across 53 files, and Spotify 0.2.11 package validation. No
shared SDK, provider, native, protocol, public API, live-account, screenshot, or
aggregate work changed. The complete prefix is still unintegrated only because
DLV-018 requires DLV-054's separate correction.

### DLV-018 — Supply trusted artwork for Games & Apps

**State:** Done; source `039b7b8` corrected by DLV-054 and integrated through
`8c1bbdf`
**Baseline:** unaccepted DLV-022 candidate `c349bbd`
**Dependencies:** DLV-006 and DLV-017
**Owner:** trusted app-library artwork registration/projection, brokered opaque
artwork handles, bounded host decode/cache consumption, Games & Apps
presentation, and direct provider/widget fixtures
**Visible outcome:** Games & Apps rows show their real trusted application/game
icons when available and a consistent semantic fallback when not, without
slowing or bloating large collections.

**Objective:** Complete the trusted local artwork path for current Start Menu,
AppsFolder, and Steam registrations using DLV-006 lazy handles while preserving
the same opaque provider identity and launch-authority boundary.

**In scope:** source-owned artwork discovery and revalidation; opaque bounded
handle registration; exact identity/generation binding; lazy demand; file,
format, dimension, decoded-byte, count, transport, memory, and disk-cache bounds;
deduplication and eviction; missing/change/churn/corrupt/oversized/stale cases;
2,000/10,000-item demand fixtures; Games & Apps row presentation and accessible
fallback semantics.

**Out of scope:** arbitrary widget file/URL access, base64 artwork in snapshots,
network image fetching, new store adapters, title/path-derived launch identity,
persisting raw paths or image bytes in widget state, changing launch authority,
generic media hosting, screenshots as acceptance, or DLV-025 compositor work.

**Acceptance criteria:** an artwork handle is useful only for the exact current
trusted registration and cannot reveal or fetch an arbitrary path; stale,
missing, malformed, oversized, or replaced resources fail to one bounded
source-appropriate fallback. Decode/cache work is lazy and bounded under the
2,000/10,000-item fixture, with deterministic eviction and no mass snapshot
payload. Artwork churn cannot change launch identity, focus, membership, or
warm-start authority. Supported real registrations expose artwork where their
trusted sources provide it.

**Verification:** Tier 1 app-library provider, broker, resource/cache, Games &
Apps, shared collection, and documentation Release suites. Tier 2 smallest
installed-worker/production-host route proving exact opaque-handle demand and
rejection. No aggregate, external store login, arbitrary filesystem access, or
screenshot harness.

**Stop/escalate when:** a supported source has no documented trustworthy artwork
surface, delivery requires ambient filesystem/network authority, handle
resolution cannot stay identity/generation bound, native cache changes overlap
active platform work, or a new public authority/protocol beyond DLV-006 is
required.

**Reviewer disposition:** Rejected. Candidate `039b7b8` correctly replaces
eager inline icon pixels with opaque lazy handles, revalidates supported source
registrations before rasterization, bounds broker registrations and the native
decode cache, composes Games & Apps through the public SDK, and retains focused
provider/broker/bridge/native evidence. However the image-cache worker calls
the synchronous `WidgetBridgeClient::ResolveArtwork` while holding the same new
recursive request mutex that every UI-thread bridge operation acquires. A slow
or cancellation-ignoring provider lookup can therefore delay event pumping,
input/lifecycle requests, catalog work, and Stop for the broker request
deadline, so moving work to the image thread does not make the UI path
nonblocking. The candidate also preserves a handle when provider AppId and
stable identity are unchanged, but neither that handle nor the native bitmap
key includes the trusted registration revalidation revision. When an icon
changes in place, the provider refreshes its private icon cache while the
renderer can retain the old decoded bitmap indefinitely. Current tests cover
different-identity replacement, not these two cases. Keep `039b7b8` and its
dependent commits unintegrated until DLV-054 closes both visible reliability
gaps without broadening widget authority.

### DLV-053 — Correct Spotify occurrence identity and singleton focus

**State:** Done; accepted as `7f5c2fd` and integrated through `8c1bbdf`
**Baseline:** closing commit of DLV-018, including unaccepted candidate
`c349bbd`; retain the complete prefix for correction and review
**Dependencies:** DLV-006, DLV-022 candidate `c349bbd`, and DLV-018
**Owner:** widgets lane over Spotify Queue/playlist occurrence normalization,
keyed action/focus resolution, playlist-detail header edges, credential-free
fixtures, and directly affected Spotify documentation; no provider authority,
native collection implementation, or unrelated SDK behavior
**Visible outcome:** Repeated tracks or episodes remain independently reachable
instead of turning Queue or Playlists into an error, and a one-track playlist
cannot trap Down navigation on its only row.

**Objective:** Preserve DLV-022's continuous keyed collection behavior while
representing each legitimate occurrence uniquely and stably enough for bounded
focus/action routing, including when Spotify supplies the same media URI more
than once and no provider occurrence ID exists.

**In scope:** retain URI as the semantic media identity; add one explicit,
collection-context-bound occurrence policy for duplicate queue and playlist
items; prove same-page and cross-retained-page duplicates, refresh, insertion,
deletion, eviction/refetch, route/Back, cancellation, and stale completion;
keep action lookup exact for the selected occurrence; bound and lifecycle-drain
any matcher/allocator state; remove the singleton row's explicit self Down edge
while retaining the authored first-row Up and header Play Down relationship.
An occurrence discriminator may distinguish otherwise identical entries, but
must not replace semantic identity with a title or a global row ordinal.

**Out of scope:** changing playback semantics for equivalent duplicate media,
Spotify provider/OAuth/Premium/Web Playback work, new screens, native focus
heuristics, weakening shared duplicate-key rejection, a public SDK/protocol
revision, DLV-043 decomposition, screenshots, live credentials, or unrelated
artwork behavior.

**Acceptance criteria:** two or more equal media URIs in Queue, one playlist
page, and separate simultaneously retained playlist pages produce unique
bounded collection, focus, and action IDs and never enter the duplicate-key
error route. Unique-URI keys survive offset changes; distinguishable duplicate
occurrences retain their key across refresh/insertion/deletion, while truly
indistinguishable occurrences use one documented deterministic nearest-
equivalent fallback rather than failing or selecting an unrelated item. No
occurrence registry grows beyond the retained collection/lifecycle bounds.
For an exactly one-row playlist, Play points Down to the row and the row points
Up to Play, but the row has no self edge; Up/Down cannot oscillate or trap.
All accepted DLV-022 exact-intent join, forward/reverse, header, route, refresh,
failure, and lifecycle behavior remains green.

**Verification:** Tier 1 Spotify Release cases for duplicate queue and playlist
occurrences, cross-page retention, refresh/churn, action identity, and the
single-row graph; rerun the focused shared cursor tests only if their code
changes; validate/pack the Spotify package, run the smallest installed-worker
conformance group, and validate affected documentation. No aggregate, provider,
native suite, screenshot, or live account.

**Stop/escalate when:** correctness requires a public/provider contract change,
an unbounded historical occurrence registry, weakening the shared collection's
duplicate-key failure, or a product choice about which non-equivalent Spotify
item should play. Preserve exact provider evidence rather than substituting a
title/global-ordinal key.

**Reviewer disposition:** Accepted. One private occurrence policy per Spotify
media collection retains URI as semantic identity and adds only a bounded
collection-context discriminator. Same-page and retained cross-page duplicates
receive unique collection/focus/action keys; unique URI keys survive offset
changes; duplicate keys remain stable through refresh, insertion, deletion,
eviction/refetch, and exact playback lookup. Truly indistinguishable occurrences
use deterministic nearest-equivalent matching within the retained window. An
exact request generation prevents superseded cancellation-ignoring completion
from mutating matcher state, and Reset clears all retained occurrences. A
one-row playlist now authors Play Down to row and row Up to Play with no self
Down edge. Retained Release evidence passes Spotify 45/45, installed-worker
conformance 6/6, 53 documentation files, and the validated Spotify 0.2.11
package. DLV-054 subsequently closed the preceding artwork correction and the
coherent prefix is integrated through `8c1bbdf`; live proof waits for DLV-055 to
publish the changed Spotify source under a new immutable package version.

### DLV-054 — Keep trusted artwork current without blocking the overlay

**State:** Done; accepted source commit `546f014`, integrated through `8c1bbdf`
**Closing commit:** `546f014` (`[DLV-054] keep trusted artwork current without
blocking`)
**Baseline:** closing commit of DLV-053, including rejected DLV-018 candidate
`039b7b8`; retain the complete prefix for correction and review
**Dependencies:** DLV-006, DLV-018 candidate `039b7b8`, and DLV-053 only for
serialized lane order
**Owner:** widgets lane as the serialized cross-lane lead over trusted app-
library artwork revision/generation admission, native bridge demand ownership,
bounded decode/cache invalidation, direct host fixtures, and affected public
artwork documentation; no new widget authority or unrelated bridge redesign
**Concurrency:** Platform remains idle while this correction owns the shared
WidgetBridgeClient, renderer, cache, and broker artwork files. Do not touch
DLV-025 compositor evidence or reviewer-owned files.
**Visible outcome:** Games & Apps loads and refreshes the exact current trusted
icons without freezing controller/keyboard interaction, widget switching,
event pumping, or overlay shutdown when icon resolution is slow or fails.

**Objective:** Preserve DLV-018's opaque lazy artwork path while making demand
nonblocking with respect to native UI/control-plane work and making an in-place
trusted registration/icon change invalidate the old handle, decoded image, and
render-target bitmap deterministically.

**In scope:** map `WidgetBridgeClient` responsibility and threading before and
after; one bounded artwork request owner that never holds the UI caller behind
provider I/O; exact correlation, current widget/runtime generation, request
count, queue, timeout, cancellation, shutdown, and late-reply rules; current-
generation publication admission before and after provider completion; a
trusted host-only artwork revision derived from the exact registration
revalidation key without exposing source identity; handle rotation or explicit
cache invalidation when that revision changes; stale-handle, same-identity icon
replacement, removal/reappearance, worker restart before first new list,
provider stall/failure, rapid switch, and cache eviction/refetch fixtures;
deletion of superseded broad bridge locking or cache state.

**Out of scope:** embedding pixels in snapshots, arbitrary file/URL/network
access, a generic community media service, public SDK/protocol revision,
provider launch-identity changes, Steam remote artwork, DLV-025 composition,
the full DLV-033 session-coordinator refactor, per-widget timing branches, or
accepting a two-second UI pause as a bound.

**Acceptance criteria:** a manually blocked artwork provider can consume its
bounded request deadline without delaying native event pumping, an unrelated
snapshot/action/lifecycle request, B/Guide/Close processing, or Stop; no
artwork task or frame read survives shutdown or widget-generation retirement,
and a late old-generation reply cannot populate cache or paint. Replacing icon
content/revalidation data while preserving provider AppId, SavedId, and stable
launch identity produces a new artwork revision and the next render decodes
the new pixels; the old handle and bitmap cannot reappear after refresh,
worker restart, eviction, or route return. Existing 512-registration,
32-entry/32-MiB native cache, 12-KiB/64-pixel decode, 2,000/10,000 logical
demand, semantic fallback, focus, membership, warm-state, and launch-authority
bounds remain exact. The completion report gives the required before/after
`WidgetBridgeClient` threading/responsibility map and does not claim EQ-020's
broader synchronous startup problem is closed.

**Verification:** Tier 1 provider, broker registry, bridge, Widget SDK, Games &
Apps, RemoteImageCache, renderer, and documentation Release suites. Tier 2 one
production-host fixture with manually controlled slow artwork, concurrent
control-plane/UI progress, generation replacement, and same-identity icon
revision. Build and validate the Games & Apps package and production
OverlayHost. No aggregate, screenshot, live store, broad bridge coordinator,
or physical controller run.

**Stop/escalate when:** a safe correction requires a public protocol/authority
change, a second native bridge process or broad session coordinator, cannot
separate artwork I/O from UI/control-plane progress without material
architecture choice, or overlaps active platform work. Preserve the exact
blocked-provider trace rather than accepting serialized UI latency.

**Reviewer disposition:** Accepted. Artwork demand receives a quick correlated
acknowledgement before trusted provider I/O, then completes through a bounded
private event queue with current worker/artwork-session admission. Stop and
same-widget lifecycle traffic proceed while a cancellation-ignoring provider is
blocked; retired completion cannot publish. A host-only digest of the exact
provider revalidation key rotates the opaque handle, decoded cache entry, and
per-node render bitmap even when AppId, SavedId, and launch identity are stable.
The 512-registration, 32-entry/32-MiB cache, 12-KiB/64-pixel decode, semantic
fallback, focus, membership, warm-state, and launch-authority bounds remain.
Focused Release evidence passes provider 32/32, broker 51/51, Bridge 70/70,
Widget SDK 85/85, Games & Apps 56/56, first-party installed conformance 6/6,
4,777 renderer checks, native cache/parser cases, the production host build, and
54 documentation contracts. No aggregate or screenshot ran. The main Release
artifact graph rebuilt successfully after integration; packaged live artwork
remains verification evidence rather than a claim that EQ-020 is closed.

### DLV-055 — Publish the current Spotify correction under a unique version

**State:** Done; accepted and integrated as `efffa53`
**Closing commit:** `efffa53` (`[DLV-055] publish Spotify 0.2.12 package`)
**Baseline:** accepted integration `8c1bbdf` plus the reviewer control-plane
commit containing this assignment
**Dependencies:** DLV-022 and DLV-053; DLV-054 only for contiguous integration
order
**Owner:** widgets lane over Spotify Community manifest/package metadata,
directly affected version references, supported install/select workflow, and the
smallest exact installed-package fixture; no Spotify behavior or architecture
changes
**Concurrency:** Platform may remain idle; do not touch native host, bridge,
provider, public SDK/protocol, DLV-025 evidence, or reviewer-owned files.
**Visible outcome:** The freshly launched overlay executes the accepted
continuous-list and duplicate-occurrence Spotify code instead of silently
reusing the older installed `0.2.11` payload.

**Objective:** Restore DLV-052's content-unique package discipline after
DLV-022/DLV-053 changed Spotify source without changing its immutable manifest
version, then make the exact built package the enabled active local version.

**In scope:** increment Spotify to canonical pre-release version `0.2.12`;
update exact public/version references; build, validate, and pack through the
supported Community script; disable, install, explicitly select `0.2.12`, and
enable through the supported catalog workflow; prove the installed payload
digest equals the just-built package and differs from `0.2.11`; start the exact
package through the generic AppContainer path and obtain its first snapshot;
retain `0.2.11` as an inactive rollback version.

**Out of scope:** Spotify behavior, list/focus/occurrence changes, DLV-043
decomposition, provider/OAuth/Premium work, deleting an installed version,
permissive same-version overwrite, catalog/security redesign, public SDK or
protocol changes, screenshots, live credentials, or aggregate verification.

**Acceptance criteria:** source, manifest, docs, built archive, installed
manifest, active catalog entry, and enabled worker all identify `0.2.12`; the
installed sealed payload digest equals the archive that was just built and is
not the prior `0.2.11` digest. The package starts through the production generic
AppContainer worker path and publishes a first credential-free snapshot. The
workflow never silently selects `0.2.11`, never overwrites an immutable version,
and leaves the previous version available only as an inactive rollback.

**Verification:** Tier 1 Spotify package build/validation and documentation
contracts. Tier 2 only the smallest installed first-party/community package
start/first-snapshot fixture plus exact catalog/digest inspection. Do not rerun
the 45-case Spotify behavior suite because production behavior did not change;
no aggregate, provider, native, screenshot, or live-account run.

**Stop/escalate when:** the supported workflow cannot install/select without
deleting or overwriting an immutable package, the exact digest cannot be proven,
or startup fails for a reason outside package freshness. Preserve the profile
and report rather than weakening catalog integrity.

**Reviewer disposition:** Accepted for its stated implementation-worktree
artifact. Source/manifest/docs, archive, installed manifest, active catalog, and
enabled worker identify `0.2.12`; `0.2.11` remains installed and inactive. The
independent archive-to-installed content digest matches, differs from `0.2.11`,
and the required-AppContainer generic worker returns a valid credential-free
first snapshot at sequence 2. The 45-case behavior suite and broader gates did
not rerun. Commit `efffa53` is fast-forwarded to main and the coherent Release
overlay is visibly running as PID 25956. A subsequent build from main produced
the same three-file 146,050-byte archive shape but a different sealed content
digest (`f989ac...` versus installed `a946b3...`); immutable install correctly
refused replacement and the supported workflow reselected/re-enabled the
accepted installed artifact. DLV-056 owns this newly demonstrated checkout-path
reproducibility gap; it does not invalidate the latest-source live behavior but
prevents calling the main artifact graph exact.

### DLV-043 — Replace Spotify partial-file organization with real boundaries

**State:** Done; accepted source commit `6c619e9`, integrated through `d534410`
**Closing commit:** `6c619e9` (`[DLV-043] replace Spotify partials with real
boundaries`)
**Baseline:** accepted DLV-055 commit `efffa53`
**Dependencies:** DLV-007, DLV-008, DLV-023, DLV-022, and DLV-040
**Owner:** Spotify managed widget internals and credential-free fixtures; no
provider, broker, public SDK/protocol, or native-host files

**Objective:** Replace the roughly 2,040-line logical `SpotifyWidget` partial
type with actual encapsulated route/action, playback-reconciliation, and pure
presentation boundaries so a normal Spotify behavior or screen change does not
depend on unrestricted access to the whole widget's mutable state.

**In scope:** a before/after aggregate logical-type, responsibility,
coordination, and mutable-dependency inventory; one non-partial lifecycle and
committed-presentation owner; closed value-based route/action policy;
playback/device/command reconciliation policy where independently testable;
snapshot-only presentation owners; deletion of production Spotify partial
declarations and superseded cross-file field access; deterministic repeated
presentation, route/Back, action admission, provider-event versus command,
late-result, cancellation, and Active-lifetime drain fixtures.

**Out of scope:** Spotify product behavior or visual changes, live OAuth/
Premium proof, provider/backend work, DLV-006 collection semantics, DLV-022
focus/list correction, public SDK abstractions, a universal widget controller,
one class per action/view, or mechanical file movement.

**Acceptance criteria:** `SpotifyWidget` is no longer a logical partial class;
the remaining root is the sole lifecycle, committed-state, resource-lifetime,
and invalidation owner and reaches extracted boundaries only through narrow
values/results; presentation owns no provider, lock, task, resource, or action
authority; one route, playback reconciliation rule, or view can be changed and
tested without constructing or reading the complete widget; current keyed
presentation, failure, paging, focus IDs, actions, authorization, polling, and
drain behavior remain exact; the completion report quantifies aggregate type
size, fields, tasks, locks/semaphores, and cross-boundary mutable dependencies
before and after.

**Verification:** Tier 1 Spotify, Widget SDK operation/resource/lifecycle,
smallest generic installed-worker, source-boundary, and affected documentation
Release suites. No aggregate, live account, provider, or native suite.

**Stop/escalate when:** real separation requires public SDK/protocol or provider
changes, duplicates lifecycle/committed-state/resource authority, exposes a
DLV-022 list/focus defect, or changes authentication/product behavior.

**Reviewer disposition:** Accepted as a material responsibility split, with a
conditional cohesive exception for the residual root rather than a claim that
line count alone is closed. The four-declaration 2,207-line logical partial type
becomes one non-partial 1,275-line orchestration owner. It retains the sole
lifecycle, provider-call, three cursor-resource, three task, two-lock/one-
semaphore, committed-state, and invalidation authority. A closed value-only
route/action classifier and playback/device command policy own no provider,
task, lock, resource, or invalidation state; the presenter consumes only one
immutable `SpotifyPresentationState`. Direct deterministic cases cover repeated
presentation, action/route admission, and playback command/projection rules,
while the retained route/Back, provider-event/command, late-result,
cancellation, and Active-drain matrix remains green. Focused Release evidence
passes Spotify 48/48, Widget SDK 85/85, generic AppContainer conformance 6/6,
and 54 documentation contracts. No aggregate, live account, provider, native,
or screenshot suite ran. Reopen the residual root if presentation/policy returns,
another lifecycle/resource/coordination owner appears, or material unrelated
growth occurs. Exact packaged live refresh follows active DLV-056 because
immutable `0.2.12` cannot carry the newly integrated source.

### DLV-056 — Make Community packages reproducible across worktrees

**State:** Integrated candidate `2737852` through `e694316`; acceptance withdrawn
after the required ordinary warm-main refresh produced a different payload;
corrected by active DLV-057
**Baseline:** accepted DLV-043 integration `d534410` plus reviewer control-plane
commit `509d915`
**Dependencies:** DLV-055 and DLV-043 only for source/integration order
**Owner:** widgets lane over the shared managed Community package build inputs,
Spotify manifest/version references, supported pack/install workflow, and the
existing exact installed-package acceptance entry point; no product behavior
or framework architecture changes
**Visible outcome:** A package rebuilt from accepted local main is byte/content
identical to the same commit built in an isolated worktree and can be installed
as the uniquely versioned payload the visible overlay actually runs.

**Objective:** Remove absolute-checkout-path influence from managed Community
addon output, prove the same commit packages identically in main and an isolated
worktree, then publish the post-DLV-043 Spotify source as immutable `0.2.13`.

**In scope:** identify the exact path-sensitive managed build input; apply the
smallest shared deterministic/path-map configuration that is safe for public
Community addon builds; bump Spotify source/manifest/docs to `0.2.13`; build and
pack the same commit from two distinct absolute checkout roots; compare archive
SHA-256 and independently derived sealed content-tree digests; use the supported
disable/install/select/enable workflow for the main-built archive; retain
`0.2.11` and `0.2.12` as inactive rollback versions; run the existing exact
installed-package AppContainer first-snapshot acceptance against `0.2.13`.

**Out of scope:** Spotify behavior or DLV-043 ownership changes, deleting or
overwriting installed versions, weakening immutable catalog rules, broad build-
system cleanup, repo-wide test migration, native/provider/public SDK/protocol
work, screenshots, credentials, live playback, or aggregate verification.

**Acceptance criteria:** two clean builds of the exact closing commit from
distinct absolute roots produce identical `.gbarwidget` SHA-256 and sealed
content-tree digest; no compiled payload, manifest, archive entry, or validation
evidence contains either checkout root. The main-built `0.2.13` archive is the
enabled selected installed generation and its digest matches the independently
installed archive; `0.2.11` and `0.2.12` remain distinct inactive rollbacks. The
production generic worker requires AppContainer and publishes a valid first
credential-free snapshot. Repeating the main build does not change either hash.

**Verification:** Tier 1 two-root Release package/validation/hash comparison and
documentation contracts. Tier 2 only the existing exact installed Spotify
package/digest/AppContainer first-snapshot acceptance. Do not rerun Spotify's
behavior suite, the product aggregate, provider/native suites, screenshots, or
live-account checks.

**Stop/escalate when:** checkout independence requires changing public PDB/debug
semantics or repository-wide release policy beyond a narrow documented setting,
either older immutable version would need deletion/overwrite, hashes still vary
after the identified path input is removed, or startup fails outside packaging.
Preserve all installed generations and report exact differing archive entries.

**Reviewer disposition:** The closing commit correctly removes absolute checkout
paths and three retained clean proof builds match byte-for-byte: archive SHA-256
`9c4de014d9d8fb2847c3f75b32778a492e8c602cabbf731497366c06419e83da`
and payload-DLL SHA-256
`1752c8203fb9c1161419f8d433761e611b0081551b13f331805c5b77cab65c74`.
The required post-integration build from ordinary warm main did not match:
archive SHA-256
`e0000c3ba6979600461caf775f57fef009d588fbf88a8f869e4b2ce1d8dde25f`
and payload-DLL SHA-256
`540d33093f2829c3b8f978af528df434d9467e9bc358a6312718c13b62398c26`.
Manifest and theme entries are identical; the same-length managed DLL alone
differs. The package command can therefore reuse path-map-sensitive stale
intermediates and does not yet meet its normal main-worktree acceptance gate.
No latest-payload launch is claimed from DLV-056.

### DLV-057 — Make Community packaging independent of prior build state

**State:** Done; accepted and integrated as `90cadf4`
**Closing commit:** `90cadf4` (`[DLV-057] isolate Community package build state`)
**Baseline:** integrated DLV-056 candidate `e694316` plus the reviewer
control-plane commit containing this assignment
**Dependencies:** DLV-056; no platform-lane dependency
**Owner:** widgets lane over the supported Community package script, narrow
deterministic build inputs/intermediate-output ownership, Spotify immutable
version/docs, and the existing installed-package acceptance entry point
**Concurrency:** Platform work may proceed only when it does not edit Community
packaging, generic installed-worker acceptance, or Spotify version documentation.

**Visible outcome:** An ordinary warm main checkout builds the exact Spotify
payload that is installed, selected, enabled, and visibly opened after the
milestone; prior local build state cannot silently change what the user tests.

**Objective:** Reproduce DLV-056's warm-main mismatch, identify the exact reused
intermediate/reference input, and make the supported Community package command
publish from an owned isolated/rebuilt artifact graph so output depends only on
the closing source commit and declared build inputs.

**In scope:** retain a bounded deliberately preseeded warm-state reproduction;
give the package command an owned generated intermediate/output graph under its
artifact root (or an equivalently narrow deterministic mechanism); clean only
that script-owned generated root before use; retain checkout path mapping and
bounded restore/cache behavior; bump Spotify to unique immutable `0.2.14`; build
from warm main, repeat main, and a clean detached root; compare archive and DLL
SHA-256 plus sealed content-tree digest; scan payload metadata for checkout
roots; install/select/enable `0.2.14` through the supported flow; retain
`0.2.11`, `0.2.12`, and `0.2.13` inactive; run the existing exact package/digest/
required-AppContainer first-snapshot acceptance.

**Out of scope:** deleting arbitrary repository `bin`/`obj`, deleting or
overwriting installed generations, repo-wide clean/build policy, Spotify
behavior or ownership changes, public SDK/protocol/provider/native changes,
aggregate verification, screenshots, credentials, or live playback.

**Acceptance criteria:** the deliberately warm main build, repeated main build,
and clean detached-root build of the exact closing commit have identical archive
SHA-256, payload-DLL SHA-256, and sealed content-tree digest. No payload metadata
contains an absolute checkout root. The exact main archive is installed as
selected/enabled `0.2.14`, its installed digest matches independently, all three
older immutable generations remain distinct and inactive, and the production
generic worker requires AppContainer and returns one valid credential-free first
snapshot. The package script does not mutate or delete ordinary developer build
outputs outside its owned generated artifact root.

**Verification:** one bounded warm-state reproduction followed by the three
named package/hash comparisons, documentation contracts, and only the existing
exact installed Spotify first-snapshot acceptance. Do not run Spotify's 48-case
behavior suite, product aggregate, provider/native suites, screenshots, or live
account checks. After planner integration, repeat the supported package command
from ordinary warm main, compare it to the accepted proof, and visibly relaunch
the exact selected payload for immediate user testing.

**Stop/escalate when:** correction requires deleting developer-owned `bin`/`obj`,
changing repository-wide debug semantics, weakening immutable catalog or
AppContainer rules, or the exact package starts failing for a reason outside
build-state ownership. Preserve all evidence and installed generations.

**Reviewer disposition:** Accepted. The supported package command deletes and
rebuilds only a verified script-owned `build-graph` beneath the selected
artifact root, passes that graph through `dotnet publish --artifacts-path`, and
maps both the graph and repository to fixed virtual compiler paths. It neither
consumes nor deletes ordinary developer `bin`/`obj`. Warm main, repeated main,
and a detached checkout at the exact closing commit all produce the same
57,075-byte archive SHA-256
`66d85242ef7993c0bbf277591c010d40c1e69494fec648c2cd326d289b33b270`,
the same 137,728-byte DLL SHA-256
`fea3f92d5aacb6902c77a35a2d50f2ec729dd516311fdc79693dbf1b0912e485`,
and installed content digest
`21b8e00d692a14bd53ea37aa640581d1e5be2b7d108610483d2d3a5b880c4028`.
Spotify `0.2.14` is selected/enabled, `0.2.11` through `0.2.13` remain distinct
inactive rollbacks, the required AppContainer first snapshot is sequence 2,
and 54 documentation contracts pass. Planner main rebuilt the same hashes,
gracefully replaced the prior host, and visibly launched PID 27684. The agent
rewrote failed local candidate `3de1a3f` before planner clarification, contrary
to the no-rewrite contract; no accepted history was lost, `90cadf4` was not
rewritten, and future failed evidence must append a correction commit.

### DLV-059 — Normalize trusted installed-game source ownership

**State:** Correction required; source commit `c7c354d` is unintegrated
**Closing candidate:** `c7c354d` (`[DLV-059] normalize installed game sources`)
**Lane:** widgets
**Baseline:** accepted DLV-057 integration `90cadf4` plus the reviewer
control-plane commit containing this assignment
**Dependencies:** DLV-002, DLV-017, DLV-018, and DLV-054
**Owner:** trusted Windows app-library provider internals, existing Start Menu/
AppsFolder and Steam source adapters, focused provider fixtures, and directly
affected provider documentation; no widget, public capability/protocol, broker,
native-host, or reviewer-owned files
**Concurrency:** May run with platform DLV-058. Do not touch WidgetProtocol,
WidgetSdk, package manifests, OverlayHost, shared native build files, or pinning
documentation.

**Visible successor:** DLV-060's dedicated installed-game Game Launcher. This
internal prerequisite is scheduled only because the requested visible widget
must not accumulate source-specific discovery and launch switches.

**Objective:** Put current Start Menu/AppsFolder and Steam installed-game
discovery, exact resolve, launch, change observation, health, and source-version
behavior behind one internal trusted `IGameLibrarySource`-style contract while
retaining one normalized authoritative library owner.

**In scope:** before/after responsibility and authority map; independently
bounded source discovery and exact resolve/launch inputs/results; normalized
host-only records with stable opaque source identity, sanitized attribution,
installed/available state, supported actions, artwork revision/handle source,
and source health/version; per-source cancellation, failure isolation, refresh,
duplicate-name, replacement, disappearance/reappearance, and stale-result
fixtures; deletion of duplicated source switch logic only when covered.

**Out of scope:** a Game Launcher UI, new stores, account libraries, raw path/
command/AUMID/Steam-ID exposure, title-based merging, public API/protocol or
broker changes, new persistence, artwork transport changes, native UI, or
unrelated provider refactoring.

**Acceptance criteria:** Start Menu/AppsFolder and Steam are ordinary bounded
implementations of one internal contract; one source can fail, refresh, or be
disabled without replacing another source's last-good records; exact resolve
and launch remain source-owned and cannot be selected by title; normalized
records reveal no raw launch authority to consumers; source generations reject
late results and drain on disposal; one source can be added or changed without
editing a central source-specific discovery/launch switch. The completion
report maps responsibilities, caches, locks/tasks, generations, and mutable
cross-source knowledge before and after and introduces no new hotspot.

**Verification:** Tier 1 Windows app-library provider Release suite with direct
adapter-contract, failure-isolation, stale-result, exact-resolve/launch, and
duplicate-name cases plus affected documentation. No broker, widget, native,
aggregate, screenshot, live store, or physical launch suite.

**Stop/escalate when:** meaningful normalization changes public capability or
launch authority, requires undocumented store data, cannot keep exact source
identity private, or overlaps platform pinning files. Preserve the current
provider behavior and report rather than adding a facade over the existing
switches.

**Reviewer disposition:** Rejected pending DLV-071. Candidate `c7c354d`
materially replaces the provider's source-specific discovery/resolve/launch
switches with private Windows-installed and Steam owners, retains raw path,
AUMID, and Steam identity inside those owners, and passes 36/36 provider cases
plus 54 documentation contracts. However each source now owns a lifetime CTS,
drain event, generation, active-operation count, and `Dispose`, while the
production `WindowsAppLibraryProvider` is not disposable. The composite broker
already disposes an app-library backend when it implements `IDisposable`, but
this candidate never reaches that path. Source cancellation/drain is therefore
proved only by a direct test and is absent from the production terminal path.
Do not integrate the candidate until DLV-071 closes ownership and exact-once
drain without changing its accepted source contract.

### DLV-071 — Close normalized game-source lifetime ownership

**State:** Assigned
**Lane:** widgets
**Baseline:** unintegrated DLV-059 candidate `c7c354d` plus the reviewer
control-plane commit containing this correction
**Dependencies:** DLV-059 candidate `c7c354d`
**Owner:** `WindowsAppLibraryProvider` terminal lifecycle, its owned normalized
sources, composite-backend disposal composition, direct provider fixtures, and
affected provider documentation; no public capability/protocol, widget,
native-host, or store behavior
**Concurrency:** May run while platform finishes DLV-058. Do not touch
WidgetProtocol, WidgetSdk, OverlayHost, package manifests, or pinning files.

**Visible successor:** DLV-072 and DLV-060's large installed-game library. This
correction is scheduled first because the Game Launcher must not extend source
owners whose production cancellation/drain path does not exist.

**Objective:** Make the provider the explicit exact-once owner of every
normalized source lifetime so broker/process shutdown cancels and drains
admitted discovery, resolve, launch, and artwork work instead of leaving the
candidate's disposal contract test-only.

**In scope:** provider `IDisposable` or the narrow existing composite-supported
terminal contract; one terminal state and cancellation policy; dispose every
distinct owned source exactly once even when another source reports bounded
drain failure; reject work admitted after terminal transition; coordinate
in-flight scan/resolve/launch/artwork operations with source cancellation and
the provider scan gate without self-deadlock; clear retained authority/icon
state only after it can no longer publish; idempotent concurrent disposal;
direct provider and composite-backend terminal fixtures.

**Out of scope:** changing source discovery/resolve/launch results, public app-
library contracts, the 512-item limit, Game Launcher UI, store adapters,
`ShellStaExecutor.Shared` process lifetime, broad broker disposal redesign, or
swallowing an uncooperative source indefinitely.

**Acceptance criteria:** production composite shutdown reaches provider
termination and every source's exact-once terminal path; admitted cooperative
work observes cancellation and drains within the existing bound; late or
cancellation-ignoring completion cannot republish snapshot, authority, artwork,
or launch success; concurrent/repeated disposal is deterministic; one source's
terminal failure does not skip cleanup of the others and is reported safely;
post-disposal calls fail before source or Shell work begins. The closing diff
preserves DLV-059's private source authority and adds no lifecycle owner or
hotspot.

**Verification:** Tier 1 Windows app-library provider Release suite with direct
provider, composite disposal, concurrent/repeated disposal, active operation,
uncooperative timeout, and post-disposal cases plus affected documentation. No
broker behavior matrix, widget, native, aggregate, screenshot, or live launch.

**Stop/escalate when:** correct cleanup requires terminating the process/thread,
disposing the process-wide shared STA lane, changing a public capability, or
cannot preserve the candidate's exact source authority and last-good behavior.

### DLV-072 — Replace the 512-item app-library snapshot with cursor queries

**State:** Ready
**Lane:** widgets, acting as serialized managed capability lead
**Baseline:** closing correction commit of DLV-071
**Dependencies:** DLV-006, DLV-059, and DLV-071
**Owner:** normalized app-library backend query contract, PlatformBroker app-
library domain and artwork registration, public WidgetSdk app-library service,
Games & Apps migration, API compatibility baseline, focused capability/provider
fixtures, and directly affected public documentation; no native collection or
OverlayHost files
**Concurrency:** May run with platform pinning only while it avoids
WidgetProtocol, OverlayHost, native manifests/build files, and pinning docs.
This assignment explicitly authorizes the required pre-release public managed
app-library API break; do not preserve the obsolete offset/snapshot API solely
for local legacy compatibility.

**Visible successor:** DLV-060's complete installed Game Launcher library. The
current public path caps and materializes the authoritative backend snapshot at
512 items, so a 2,000/10,000-item widget proof would otherwise be fake or
widget-local.

**Objective:** Replace the finite offset-over-one-snapshot capability with one
opaque cursor query contract that pages the normalized trusted library through
the broker and SDK without materializing or serializing the complete library in
the widget, bridge, capability domain, or artwork session.

**In scope:** one bounded query value with installed/kind/source/filter/sort
semantics needed by current Games & Apps and DLV-060; opaque revision-bound
forward/reverse cursors; stable item and SavedId semantics; page size,
prefetch, retained page, cursor, pending request, artwork registration, and
serialized-byte bounds; refresh and source-version invalidation; duplicate,
sparse/final, insertion/deletion, stale/loop/oversized/malformed cursor and
cancellation cases; migrate Games & Apps to the one new contract and delete the
obsolete public offset API/central 512-item snapshot path; intentional public-
API baseline update and pre-release migration notes.

**Out of scope:** Game Launcher presentation (DLV-060), favorites, additional
stores, search text entry, raw source identity or launch authority, a second
compatibility facade, native cursor/renderer changes, generic database/query
frameworks, screenshots, credentials, or physical launch.

**Acceptance criteria:** a 10,000-item fake can traverse first, middle, final,
reverse, refresh, insertion, deletion, and eviction windows while every
individual provider/broker/worker response and artwork-registration set remains
within named bounds; the capability domain and widget never retain or serialize
all 10,000 records. Cursors are opaque, query/revision-bound, bounded, and fail
closed on loop, tamper, stale generation, or wrong query. SavedId resolution and
exact launch remain current-authority operations. Games & Apps preserves its
curated behavior through the same new service. No old offset/snapshot public
path, parallel cache, or title/source-derived authority remains.

**Verification:** Tier 1 normalized provider query, PlatformBroker app-library,
WidgetSdk service/cursor, Games & Apps, API compatibility, artwork registry,
and documentation Release suites. Tier 2 smallest installed generic-worker
capability route over forward/reverse/refresh and exact launch/artwork demand.
**Integration checkpoint:** after committing, run the canonical Tier-3 verifier
exactly once from the clean exact commit because this intentionally replaces a
public cross-process capability contract. No screenshot, native redesign,
external store, or physical game run.

**Stop/escalate when:** a bounded cursor cannot preserve exact saved/launch
authority, the change requires native collection semantics beyond accepted
DLV-006, another lane owns a required shared file, or credible implementation
would retain the complete 10,000-item library in the capability/widget path.

### DLV-060 — Ship an installed-only Game Launcher library

**State:** Ready
**Lane:** widgets, acting as serialized managed capability/widget lead
**Baseline:** closing commit of DLV-072
**Dependencies:** DLV-006, DLV-017, DLV-018, DLV-054, DLV-059, DLV-071, and
DLV-072
**Owner:** dedicated Game Launcher first-party widget, private user projection,
focused broker/widget fixtures, and directly affected public documentation;
DLV-072's normalized cursor capability and existing native VirtualizedGrid/List
and opaque artwork contracts are consumed unchanged
**Concurrency:** May run with platform pinning only while it requires no public
WidgetProtocol/WidgetSdk/native change. Stop for planner serialization if an
existing public collection or native grid contract is insufficient.

**Visible outcome:** A dedicated controller-first Game Launcher presents the
complete installed local game library from the normalized current sources,
separate from the lightweight curated Games & Apps tray, with continuous grid
navigation, real lazy artwork, source attribution, and exact launch.

**Objective:** Prove the framework can support the first complex launcher slice
without serializing thousands of games, duplicating provider caches/authority,
or embedding source-specific rules in the widget.

**In scope:** dedicated bundled widget/catalog entry; installed-only normalized
Steam and trusted Windows game records; library-revision-bound opaque cursors;
stable item/focus IDs; existing `VirtualizedGrid`/cursor append-prepend window,
bounded prefetch/eviction and page-jump actions; lazy artwork/fallback; loading,
partial-source, empty, unavailable, and safe error rows; exact current launch
revalidation; compact/standard/wide and 100-150% semantic states; 2,000/10,000-
item fake libraries; bounded current-schema display projection for warm start
without persisting launch IDs or raw source identity.

**Out of scope:** Epic/GOG/EA/Ubisoft/Battle.net/Xbox adapters, account-owned or
uninstalled games, installs/updates, search/text entry (DLV-061), favorites or
variant preferences (DLV-066), running-process truth (DLV-067), remote artwork,
title-based merging, raw launch authority, public plugin trust, native
collection redesign, screenshots as acceptance, or credentials.

**Acceptance criteria:** Games & Apps and Game Launcher consume one trusted
library/cache and the same opaque launch authority while keeping independent
presentation/user projection; no 2,000/10,000-item snapshot or native tree is
created; crossing cursor boundaries preserves the adjacent keyed focus and
viewport; source failure degrades only its rows; same-title variants remain
separate; stale/unavailable records cannot launch; current exact records launch
only after revalidation; warm projection appears first but remains visibly
non-authorizing until resolved; every essential action is reachable across the
named surface/scale envelope.

**Verification:** Tier 1 Game Launcher, normalized provider, broker mapping,
Widget SDK cursor/grid, private-state, catalog, and documentation Release
suites. Tier 2 smallest installed generic-worker/production-host semantic route
covering continuous grid navigation, lazy artwork, exact launch admission, and
2,000/10,000-item bounds. No aggregate, screenshot, physical launch, external
store, or native redesign.

**Stop/escalate when:** DLV-072's accepted capability or existing collection/
artwork contracts cannot express the product without further public/native
changes, launch truth requires raw identity, or the widget begins owning
discovery/cache/source policy. Preserve the exact gap for a serialized framework
prerequisite.

### DLV-066 — Add Game Launcher favorites and preferred variants

**State:** Ready
**Lane:** widgets
**Baseline:** closing commit of DLV-060
**Dependencies:** DLV-059, DLV-071, DLV-072, and DLV-060
**Owner:** Game Launcher private durable organization policy, presentation, and
credential-free fixtures; no provider discovery, native host, or public
protocol changes

**Visible outcome:** Users can favorite games and choose a preferred launch
variant for duplicate titles without title heuristics silently merging launch
authority; choices survive overlay/source refresh and temporarily missing
stores.

**Objective:** Add durable user organization above replaceable normalized
source records using opaque stable identities and one current pre-release schema.

**In scope:** favorites, explicit variant grouping/preference, unavailable
retention, deterministic unmerge/reset, source revision replacement, CAS/
persistence failure, warm projection, focus/order retention, and whole-schema
reset for incompatible overlay-owned state.

**Out of scope:** automatic title matching, raw store IDs, account sync,
metadata services, cloud persistence, provider changes, or launch lifecycle.

**Acceptance criteria:** only explicit user grouping can establish a preferred
variant; source disappearance/reappearance and identity replacement preserve or
invalidate choices deterministically without authorizing stale launch; one
mutation cannot erase unrelated favorites/order/grouping; incompatible local
state resets atomically; all state is bounded and contains no raw authority.

**Verification:** Tier 1 Game Launcher/private-state Release suite with exact
mutation, CAS failure, restart, disappearance/reappearance, and duplicate-title
cases plus docs. No aggregate, native, screenshot, or external store run.

**Stop/escalate when:** durable preference cannot survive without exposing raw
source identity or requires provider/public protocol changes.

### DLV-067 — Present honest Game Launcher launch lifecycle

**State:** Ready
**Lane:** widgets
**Baseline:** closing commit of DLV-066
**Dependencies:** DLV-059, DLV-071, DLV-072, DLV-060, and DLV-066
**Owner:** trusted launch-result/lifecycle policy, Game Launcher projection,
and deterministic adapter/widget fixtures; no generic process authority

**Visible outcome:** Launching a game shows accurate Pending, Launcher started,
Running, Failed, and Ended states only when the selected adapter can prove them,
instead of treating store acknowledgement as successful game execution.

**Objective:** Separate accepted launch requests from observable running truth
without leaking PID/HWND/path data or adding ambient process monitoring.

**In scope:** typed host-only launch observations, per-adapter supported-state
declaration, bounded timeout/cancellation, already-running, intermediary
launcher, stale entry, adapter refresh/update, failure and ended projection,
overlay-close signal, exact current-generation admission, and safe fallback to
`Request accepted` when stronger truth is unavailable.

**Out of scope:** arbitrary process scanning, raw PID/HWND exposure, game-time
tracking, achievements, installs/updates, remote launch, macros, or undocumented
store hooks.

**Acceptance criteria:** every displayed lifecycle state has adapter evidence;
unsupported adapters never claim Running/Ended; late observations from stale
launch/source generations cannot change the current row; one launch cannot
overwrite another variant's state; all observations drain and remain bounded;
logs/snapshots expose no process or command identity.

**Verification:** Tier 1 provider launch-policy, broker mapping, Game Launcher,
and docs Release suites with manually controlled adapter observations. Tier 2
smallest generic-worker exact-launch route. No physical game, aggregate,
screenshot, or external store run.

**Stop/escalate when:** honest state requires undocumented process/store hooks,
generic process authority, or a public protocol change not already assigned.

### DLV-029 — Split Audio Mixer by stable responsibility

**State:** Done; accepted and integrated as `6fc8d73`
**Baseline:** accepted DLV-032/DLV-034 integration `a92378a`
**Closing commits:** `9647718`, corrected by `091ec51`
**Dependencies:** DLV-031; DLV-026 is independent
**Owner:** managed Audio Mixer internals and direct widget fixtures; no native
host, broker, capability, or protocol files

**Reviewer evidence:** Candidate `9647718` validly moves the complete 351-line
snapshot-only presentation surface, but its 135-line command boundary is a
broadly settable state bag with three empty endpoint subclasses. The retained
1,969-line widget still owns six command-entry methods, six worker loops, six
confirmation paths, six rollback paths, and six cancellation paths and directly
mutates policy targets, revisions, worker flags, confirmation flags, and
authoritative values. The direct policy test repeats the same base-type behavior
three times rather than proving those production transitions, while the new
cancellation-ignoring case signals before the fake call returns and assumes one
`Task.Yield()` proves the production continuation consumed the late result.
Focused dirty-worktree run `20260810T152503Z-d11fafee` was stable and green but
did not close the ownership or deterministic-proof requirements. Correction
`091ec51` closes those exact gaps; the accepted disposition and final evidence
are recorded under Recently completed.

**Objective:** Preserve Audio Mixer's domain-specific absolute-value command
coalescing, authoritative confirmation/rollback, and provider-event
reconciliation while making one audio row, confirmation rule, or provider event
path changeable without reading the complete roughly 2,496-line widget.

**In scope:** a before/after responsibility and coordination inventory; named
output, input, and per-session pending-command policies; provider-event versus
pending-command reconciliation; lifecycle/action orchestration with one Active-
lifetime owner; one immutable committed render-facing revision; snapshot-only
view composition; deletion of superseded locks, tasks, generations, and cross-
boundary mutable knowledge when focused interleaving tests prove the replacement.

**Out of scope:** fixing or masking DLV-026's reverse-scroll defect; changing
focus IDs or shared scroll semantics; endpoint selection, dashboard shortcuts,
new audio capabilities, public SDK/protocol changes, a universal optimistic-
command framework, partial-class-only splitting, or visual redesign.

**Acceptance criteria:** one lifecycle and committed-state owner remains;
output, input, and session policies are directly testable without constructing
the complete widget; those policies own target/revision/worker/confirmation and
terminal transition rules behind narrow methods/results rather than exposing
their mutable internals back to the widget; the root consumes policy decisions
while retaining singular service-call/task, committed-state, lock, lifecycle,
and invalidation ownership and no longer contains parallel confirmation,
rollback, and cancellation state-machine regions; success, failure, cancellation-
ignoring completion, timeout/rollback, provider churn, session removal, and
deactivation remain deterministic; existing focus IDs and current navigation
behavior are preserved
for DLV-026 to diagnose and correct separately; the completion report quantifies
responsibilities, locks/semaphores/tasks, and cross-boundary mutable dependencies
before and after. Late-completion proof uses an exact observable handshake and
does not rely on `Task.Yield`, sleep, or an unobserved scheduling assumption.

**Verification:** Tier 1 Audio Mixer, Widget SDK operation/lifecycle, and the
smallest affected audio-provider/broker mapping Release suites with manually
completed provider operations. No native, aggregate, or unrelated security
suite.

**Stop/escalate when:** correct separation requires public capability/protocol
or authority changes, touches the shared native scroll owner, introduces a
second lifecycle/committed-state owner, or exposes a product defect that needs
its own bounded assignment.

### DLV-042 — Extract Audio Mixer active provider-session ownership

**State:** Done; accepted and integrated as `f64c35a`
**Baseline:** accepted DLV-029 integration `6fc8d73`
**Closing commits:** `37119f7`, corrected by `0a3635a`
**Dependencies:** DLV-029
**Owner:** managed Audio Mixer provider lifecycle/ingestion internals and direct
credential-free session fixtures; no presentation, command-policy, SDK,
broker, protocol, provider, or native-host files

**Objective:** Preserve `AudioMixerWidget` as the single committed-state,
selection/action, status, and invalidation owner while making subscription
ordering, initial snapshot closure, one provider stream, optional-section retry,
or Active-lifetime drain changeable without reading the residual roughly
1,880-line application root.

**In scope:** a before/after responsibility, task, cancellation, semaphore, and
mutable-dependency inventory; one internal Active provider-session owner for
linked lifetime, sessions/output/devices/input subscription-before-snapshot
ordering, four event pumps, optional-section retry signals and attempt
replacement, capability failure classification, and terminal drain; narrow
typed immutable observations/results applied by the widget under its existing
state lock; deterministic delayed snapshot/event interleavings, optional
revocation/completion/retry, cancellation-ignoring late result/event,
deactivate/destroy drain, and reactivation replacement cases; deletion of
superseded widget-owned subscription/retry coordination.

**Out of scope:** changing Audio Mixer behavior, focus IDs/navigation, the
accepted DLV-029 presentation or command transitions, moving committed render
state or invalidation into the session, endpoint selection, dashboard controls,
public SDK/protocol changes, provider/native work, a generic observable/event
bus, coordinator chains, or one class per stream.

**Acceptance criteria:** exactly one Active provider session owns all four
subscriptions, initial fetch ordering, retry/attempt cancellation, continuing
event tasks, and terminal drain; the widget no longer owns provider-pump methods,
retry semaphores, attempt cancellation sources, or subscription startup logic
and consumes only typed current-session observations; the session owns no
committed widget state, focus/action policy, command transition state, view
composition, or invalidation; an event/result from a canceled or replaced
session cannot mutate the current widget; subscription-before-snapshot closes
the fetch gap; optional failure/recovery remains isolated; no unexpected task
failure is silently swallowed; the completion report quantifies residual root
responsibilities and either gives it a precise cohesive exception or proposes
one further bounded disposition.

**Verification:** Tier 1 Audio Mixer, Widget SDK lifecycle/subscription, smallest
generic-worker lifecycle, Windows Audio provider, and affected documentation
Release suites with manually completed provider operations. No native,
aggregate, or unrelated security suite.

**Stop/escalate when:** the extraction requires public SDK/protocol/provider
changes, creates a second committed-state or invalidation owner, cannot retain
one bounded Active session, or needs physical audio hardware.

**Reviewer evidence:** Candidate `37119f7` performs the complete ownership move
and supplies the required direct session fixtures, but review found Retry could
resume after Stop disposed a captured semaphore and the widget could overwrite
a faster retry Healthy observation with its own later Loading write. Correction
`0a3635a` closes both exact races behind the session's existing gate and one
ordered observation path. The accepted disposition and retained evidence are
recorded under Recently completed.

### DLV-038 — Modularize the largest managed test harnesses by responsibility

**State:** Deferred behind the visible product queue; not Ready for automatic
selection
**Baseline:** planner-selected accepted main at a later architecture checkpoint
**Dependencies:** DLV-031, DLV-032, DLV-037, DLV-029, DLV-040, DLV-041,
DLV-042, DLV-043, and DLV-044 so active
production architecture work has already stabilized the affected suites
**Owner:** managed test-only source organization and narrow reusable fixture
support; no production, public SDK, protocol, native-host, or product behavior

**Objective:** Make one broker, runtime, bridge, SDK, or flagship-widget scenario
family changeable without reading a 2,000-3,300-line top-level `Program.cs`,
while preserving the repository's bounded executable-test and verifier contract.

**In scope:** a before/after inventory of the largest managed test programs;
thin per-project runners with stable ordered test names and exit semantics;
cohesive scenario groups and fixtures for the three largest currently active
suites; deletion of duplicated setup/assertion/process helpers only where the
replacement has at least two real consumers; deterministic discovery/inventory
coverage and unchanged verifier/JUnit extraction.

**Out of scope:** production changes, changing tested behavior to simplify the
harness, a repository-wide rewrite, mandatory xUnit/NUnit/MSTest adoption,
reflection-based discovery, generated test cases that hide scenario intent, or
splitting every method into a separate class/file.

**Acceptance criteria:** each pilot runner is a small explicit registry over
named scenario owners; one scenario family can be located and changed without
reading the entire suite; setup, assertions, clocks, cancellation, and process
fixtures have one clear owner; test names/order, pass/fail exit codes, bounded
timeouts, verifier case extraction, and focused coverage remain exact; the
completion report identifies remaining test hotspots and gives each a cohesive
exception or later bounded disposition.

**Verification:** the three reorganized Release suites, verifier runner self-
tests/inventory checks, and documentation contracts. No product aggregate and no
unrelated native suite.

**Stop/escalate when:** cleanup requires production/public API changes, changes
test semantics or ordering, weakens timeouts/process containment, or expands into
a whole-repository framework migration.

## Platform lane

Task identity: `platform`
Active task: `019fef3b-7e94-70f0-b329-3551f8dd805b`
Active branch: `codex/impl-platform-recovery`
Active worktree: `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative`
Preserved interrupted branch/worktree: `codex/impl-platform-visible` at
`C:\Users\dwive\.codex\worktrees\pvisible\GameBarAlternative`; keep its
uncommitted DLV-016 files read-only while the recovery task resumes DLV-016 on
the accepted main baseline
Preserved blocked branch only: `codex/impl-platform` at `57aa2d5`. Its former
`C:\Users\dwive\.codex\worktrees\d9b7\GameBarAlternative` is an empty,
unregistered directory and the previously observed uncommitted DLV-025 files
are not present. Do not reconstruct, reset, or otherwise act on that lost
uncommitted state without explicit user authority.

The platform queue prioritizes visible controller and surface features even
while DLV-025 awaits a compositor choice. The preserved DLV-016 worktree must
not be reset or overwritten; its files are read-only reference material for the
clean recovery task, not an integration source. DLV-025 retains only its
committed branch baseline and documented evidence after its former worktree
disappeared. DLV-049 is accepted and integrated as `a8bcb27`; DLV-015 is
accepted and integrated as `6d3b093`; and P0 DLV-052 is accepted through
`56f6908`; DLV-016 is accepted and integrated as `fee1103`; DLV-011 is accepted
and integrated as `35df08c`. DLV-033 awaits the compositor decision, and
DLV-025 remains user-decision blocked. DLV-058 is accepted as source `e160690`
and integrated on main through `ae34f9a`, providing the first visible generic
Pin/Unpin lifecycle. DLV-070 now restores one authoritative host across ordinary
and `--show` launches before DLV-068 and DLV-069 complete placement and
accessibility/input composition, then DLV-062 runs the fixed-video trusted-rich-
media feasibility gate. This visible sequence is independent of the blocked
animated-resize compositor and may run beside widgets DLV-059 under the explicit
file boundaries below.

### DLV-058 — Ship generic pinned-surface lifecycle

**State:** Done; accepted source `e160690`, integrated on main as `ae34f9a`
**Closing commit:** `e160690` (`[DLV-058] add generic pinned surface lifecycle`)
**Lane:** platform, acting as serialized public-manifest/native lead
**Baseline:** accepted DLV-057 integration `90cadf4` plus the reviewer
control-plane commit containing this assignment
**Dependencies:** DLV-011 and DLV-016; independent of blocked DLV-025
**Owner:** public manifest/presentation-state declarations, native pinned-surface
coordinator, host chrome/lifecycle, focused protocol/native/host fixtures, and
directly affected public pinning documentation
**Concurrency:** May run with widgets DLV-059, which may not touch public
WidgetProtocol/WidgetSdk, package manifests, OverlayHost, native build files, or
pinning docs. Stop for planner serialization if another lane reaches those files.

**Visible outcome:** A declarative test widget that explicitly supports pinning
can be pinned, left visibly topmost when the main overlay closes, switched
between Interactive and click-through, reopened, unpinned, and closed without
stealing game input or leaving an orphaned window.

**Objective:** Productize DLV-011's selected host-owned Win32 tool-window
architecture as one bounded generic surface lifecycle before any YouTube or
community rich-media widget owns it.

**In scope:** manifest-declared `pinningSupported` default false; one native
`WidgetSurfaceCoordinator`-style owner for stable surface identity, HWND,
generation, visibility, pin state, z-order, focus/input mode, and teardown;
host-owned Pin/Unpin/Close/click-through chrome and help/UIA labels; Overlay,
PinnedInteractive, PinnedClickThrough, and Hidden presentation state without raw
HWND/z-order authority; cap simultaneous pins; exact removal/restart/crash/
host-exit cleanup; overlay close/reopen; stale generation; inaccessible or
non-supporting widget rejection; unchanged unpinned widget behavior.

**Out of scope:** move/resize/persistence (DLV-068), final controller/UIA input
composition and emergency hide (DLV-069), YouTube/WebView2/media playback,
arbitrary community HWNDs, animated main-panel resize/compositor work, pointer
game matrix, public raw window handles, opacity, or screenshots as acceptance.

**Acceptance criteria:** only a current admitted supporting widget can create
one host-owned pinned surface; main-overlay hide/close does not destroy it;
click-through forwards pointer activity and exposes no interactive-looking
controls; controller actions stay with the main overlay unless the pinned
surface is explicitly Interactive; reopen restores one valid focus owner;
unpin, widget removal/restart/crash, host exit, stale generation, and coordinator
disposal converge to exact-once teardown with no orphan/task/resource leak;
bounded surface count and safe failure copy are enforced; widgets receive only
typed presentation state.

**Verification:** Tier 1 WidgetProtocol/manifest, native surface lifecycle,
focus/input/chrome, UIA semantics, placement smoke, and production OverlayHost
Release suites. Tier 2 smallest real-HWND declarative pinned-widget fixture over
pin, overlay close/reopen, click-through, stale restart, removal, and teardown,
plus DLV-016 resource-delta comparison. No aggregate, YouTube, screenshot,
physical game/controller/display, or DLV-025 compositor run. After integration,
refresh and visibly launch main for direct user testing.

**Stop/escalate when:** implementation requires Windows App SDK/new compositor,
raw widget window authority, a public threat-model change, a substantial
conflict with preserved platform work, or physical-only evidence to choose the
architecture. Do not borrow the blocked main-panel resize path.

**Reviewer disposition:** Accepted. The closed-by-default manifest opt-in is
projected as data while one native coordinator owns the admitted identities,
HWND, rendering, mode, cap, live updates, catalog replacement/removal, and
terminal cleanup. Focused evidence passes SDK 86/86, Bridge 70/70, native
catalog parsing, DLV-011 policy 299 checks, and the 33-check real-HWND
coordinator fixture; incremental pinned private working set was 10,264,576
bytes. The planner integrated `e160690` through `ae34f9a`, rebuilt the complete
Release artifact graph with packaging, and visibly launched PID 3924. DLV-068
and DLV-069 retain geometry and final controller/accessibility composition.

### DLV-070 — Enforce one OverlayHost owner and forward `--show`

**State:** Assigned
**Lane:** platform
**Baseline:** accepted DLV-058 source `e160690`, integrated on main as `ae34f9a`
**Dependencies:** accepted main through DLV-057; ordered after DLV-058 so the
active milestone is not interrupted
**Owner:** OverlayHost process ownership, bounded local activation transport,
startup/shutdown logging, and focused production-host process fixtures

**Visible outcome:** Running the documented Release command while the overlay
is already resident brings that exact host forward and exits the launcher;
Task Manager never accumulates a hidden second OverlayHost.

**Reproduction evidence:** On accepted main `94d4ee0`, PID 27684 was a healthy
resident OverlayHost with no visible main-window handle. Invoking
`.\src\OverlayHost\out\Release\OverlayHost.exe --show` at 02:46 created healthy
PID 17952 with a visible window while PID 27684 remained resident. A bounded
`CloseMainWindow` request correctly could not close the handle-less older
process. Do not force-kill or reinterpret that process as valid singleton
behavior.

**Objective:** Give one per-user OverlayHost process authoritative ownership
and make later launches bounded activation clients, including while the owner
is hidden and has no current HWND.

**In scope:** race-safe per-user owner election; a least-authority local show
request independent of the current main-window handle; ordinary launch and
`--show`; simultaneous launches; hidden/visible owner; bounded acknowledgement
and safe exit status; stale/crashed owner recovery; orderly shutdown; explicit
owner/client logs; unique fixture/profile isolation so tests may run beside the
development overlay without addressing it.

**Out of scope:** force-terminating another host, a background Windows service,
external/global activation, broad diagnostics-transport redesign, widget
worker lifecycle, pinned-surface semantics, compositor/animation work, or
changing the dashboard toggle model.

**Acceptance criteria:** at most one production owner is resident for one user
and profile under sequential or simultaneous launches; a later `--show` sends
exactly one authenticated/bounded local activation to the hidden or visible
owner, receives an acknowledgement, and exits; it never initializes another
bridge, worker catalog, controller lease, or HWND. A crashed owner cannot leave
a permanent lease, a stale/spoofed client cannot activate another user's host,
and shutdown releases all ownership/transport resources exactly once.

**Verification:** Tier 1 focused process-owner/activation Release tests over
hidden owner, visible owner, simultaneous launch, stale owner, rejected client,
timeout, and orderly exit, plus the production OverlayHost build. Tier 2 one
bounded exact-executable fixture proving the second invocation exits and the
first process receives Show without a second bridge/controller initialization.
No aggregate, screenshot, widget-provider, or force-kill run. After integration,
refresh the coherent main Release and visibly invoke `--show` twice while
retaining one PID.

**Stop/escalate when:** a safe fix requires a privileged/global service,
weakens per-user process/transport isolation, conflicts substantially with the
active pinned-surface lifecycle, or needs destructive cleanup of the current
resident processes.

### DLV-068 — Add controller placement and durable pin geometry

**State:** Ready
**Lane:** platform
**Baseline:** closing commit of DLV-070
**Dependencies:** DLV-011, DLV-016, DLV-058, and DLV-070
**Owner:** pinned-surface placement/resize state machine, monitor/DPI persistence,
host chrome/actions, focused native/UIA fixtures, and public pinning docs

**Visible outcome:** A pinned widget has an explicit controller move/resize mode:
D-pad/stick moves, a bounded resize gesture changes size, A commits, B cancels,
and restart/monitor changes restore it fully on-screen at a usable size.

**Objective:** Give the generic pinned surface one host-owned placement and
persistence policy shared by controller, pointer, and accessibility actions.

**In scope:** logical size, normalized work-area anchor, bounded monitor
affinity, minimum/maximum/safe-area constraints, move/resize focus isolation,
A commit/B cancel, pointer and UIA parity, atomic overlay-owned persistence,
invalid/incompatible state reset, runtime DPI/work-area/orientation/topology/
monitor-loss reconciliation, interrupted gesture, rapid changes, and restart.

**Out of scope:** YouTube-specific 200x200 policy beyond an injected minimum,
rich media, main-panel transitions, snap-layout integration, unrestricted
offscreen placement, or preserving obsolete pre-release geometry schemas.

**Acceptance criteria:** every input route reaches one state machine; cancel
restores the exact prior committed placement; commit persists atomically;
invalid or incompatible state resets as a whole; every restore/reflow is finite,
within current work area, at or above declared minimum, and focus-valid; stale
surface generations cannot write placement; hidden/idle cost remains unchanged.

**Verification:** Tier 1 native placement/input/persistence/UIA/host Release
suites over mixed logical DPI/work areas and invalid state plus one real-HWND
restart/monitor-loss fixture. No aggregate, screenshot, physical hot-plug, or
rich-media run. Launch after integration.

**Stop/escalate when:** credible behavior needs physical display evidence to
choose between policies, new window/compositor technology, or public raw monitor
authority.

### DLV-069 — Complete pinned-surface input and accessibility composition

**State:** Ready
**Lane:** platform
**Baseline:** closing commit of DLV-068
**Dependencies:** DLV-058, DLV-070, and DLV-068
**Owner:** pinned-surface controller/pointer focus composition, emergency
visibility authority, UI Automation tree/actions, help, and production-host
fixtures

**Visible outcome:** Pinned widgets never trap controller focus or steal hidden-
overlay input, expose accurate Pin/Move/Close/click-through accessibility, and
have one global emergency action that hides all pins safely.

**Objective:** Finish the generic pinning gate's input/accessibility behavior
before trusted remote media is introduced.

**In scope:** deterministic controller entry/exit and B/Guide/Close semantics;
Overlay versus pinned Interactive focus restoration; click-through pointer and
control visibility; emergency hide/unpin authority; accelerator/pointer-capture
cancellation; UIA names, roles, states, bounds, order, Invoke/Move/Resize/Close
actions and live failure feedback; widget hide/restart/removal during focus;
reduced motion/high contrast; no hidden input forwarding.

**Out of scope:** WebView2/browser focus, YouTube controls, physical Narrator or
game compatibility, main-panel compositor changes, or a second input authority.

**Acceptance criteria:** exactly one surface owns controller focus; closing the
main overlay leaves click-through pins non-interactive; reopened overlay can
explicitly enter one valid pin and B exits predictably; emergency action is
host-owned, bounded, accessible, and always releases interaction; removal,
restart, mode change, and monitor reconciliation choose deterministic focus;
the real UIA tree contains no hidden interactive controls or duplicate actions.

**Verification:** Tier 1 controller/focus/pointer/UIA/host Release suites plus
one production real-HWND/UIA pinned-widget composition fixture. Physical
controller/Narrator/game proof remains manual. No aggregate or screenshot run.
Launch after integration.

**Stop/escalate when:** behavior requires elevated/global hooks, physical-only
policy choice, or duplicates existing OverlayHost input authority.

### DLV-062 — Gate one trusted fixed-video media surface

**State:** Ready
**Lane:** platform
**Baseline:** closing commit of DLV-069
**Dependencies:** DLV-011, DLV-016, DLV-058, DLV-070, DLV-068, and DLV-069
**Owner:** trusted rich-media process/session feasibility, fixed-video host
composition, resource measurement, policy evidence, and focused process/native
fixtures; no public YouTube widget or account integration

**Visible outcome:** One fixed embeddable public YouTube video can play in the
accepted pinned surface with user-initiated audio and standard controls, and it
pauses/stops when no visible pinned surface remains.

**Objective:** Prove or reject the narrow trusted WebView2/IFrame Player
architecture and resource envelope before authorizing YouTube v1.

**In scope:** reuse the SpotifyPlaybackHost process-containment lessons; one
typed `MediaSurfaceSession` prototype for load/cue/play/pause/seek/volume and
state/error events; fixed allowlisted video/https origin/referrer; WebView2 host
objects off; navigation/frame/popup/download/permission/certificate/external-
browser policy; user gesture/autoplay blocked; errors 5/100/101/150/153;
ProcessFailed recovery; one environment/user-data lifecycle; visible-pinned
lease and hidden/minimized/unpinned pause/stop; process/memory/CPU/GPU-where-
available/startup/idle measurements; kill-switch/policy checklist.

**Out of scope:** URL input/search, Google OAuth/API key, recent library,
multiple videos/surfaces, generic community WebView, stream extraction,
background audio, ad/control suppression, public media API, or claiming
universal game/anti-cheat compatibility.

**Acceptance criteria:** remote content receives no shell/widget broker/native
object authority; only allowlisted typed messages and origins are admitted;
standard YouTube controls/captions/links/ads remain; user-visible pin state owns
the playback lease; hidden/minimized/unpinned state cannot continue playback;
crash/teardown releases controller/environment/process resources; measured
incremental cost and unsupported GPU/game evidence are explicit; failure of the
gate leaves generic pinning intact and does not authorize YouTube v1.

**Verification:** Tier 1 offline typed-protocol/origin/policy/process-lifecycle
fixtures and DLV-016 resource comparison. Tier 2 one bounded fixed public-video
manual/automated hybrid run when network/WebView2 is available; record external
unavailability honestly without using credentials. No aggregate, screenshots as
closure, account, search, or physical-game claim.

**Stop/escalate when:** a new runtime dependency is required, compliant
origin/referrer/standard-player behavior cannot be retained, EME/composition or
resource cost materially fails the gate, or physical representative-game choice
is needed for the architecture decision.

### DLV-003 — Correct shared button-content geometry

**State:** Done; accepted and integrated as `703c5bb`
**Baseline:** `1738618` plus the reviewer control-plane commit
**Owner:** native declarative renderer and shared component styles

**Objective:** Correct icon, label, checkmark, and busy-content alignment once
in shared rendering/component geometry so first-party widgets need no local
pixel offsets.

**In scope:** Buttons, icon-and-label actions, selection rows, navigation tiles,
measured centering/optical tokens, wrapped labels, supported scale profiles, and
RTL-safe behavior where currently claimed.

**Out of scope:** per-widget offsets, unrelated redesign, new font packaging, or
protocol expansion without demonstrated necessity.

**Acceptance criteria:** assertions cover with/without-icon, checkmark, busy,
wrapped, disabled, focused, compact/standard/wide, and supported scale cases;
SDK Gallery, Games & Apps, and Spotify inherit the fix without local branches.

**Verification:** Tier 1 native renderer/component tests, production OverlayHost
build, semantic snapshots, and representative retained captures. No aggregate;
physical sign-off remains verification-only when unavailable.

**Stop/escalate when:** the fix would break the documented surface envelope or
requires incompatible protocol semantics.

### DLV-005 — Hold Y to refresh the selected tray widget

**State:** Done; accepted and integrated as `aaf36d9`
**Baseline:** `27b0319`
**Owner:** OverlayHost controller gesture state and existing host reload path

**Objective:** Preserve tap Y for reorder while a clearly hinted bounded hold Y
refreshes the selected widget exactly once through the F5 host reload path.

**In scope:** deterministic press/hold/release; progress hint; exactly-once
activation; cancellation on focus/overlay/device/lifecycle changes; suppression
of reorder and widget Y after hold wins; F5 parity; accessibility/help copy.

**Out of scope:** a second reload implementation, forwarding physical Y to
widget code, configurable timing, polling while hidden, or unrelated shortcuts.

**Acceptance criteria:** deterministic timing covers release immediately
before/at/after threshold, repeats, focus change, hide, device loss, reorder,
open widget, stale selection, failure, and exactly-once behavior; selected ID is
revalidated at activation.

**Verification:** Tier 1 native input/host tests, OverlayHost build, and docs.
No aggregate. Physical-controller proof stays in the verification queue.

**Stop/escalate when:** the design creates independent reload authority or
requires hidden-overlay polling.

### DLV-014 — Compose advanced action failure through the production host

**State:** Done; accepted and integrated as `9060f12`
**Baseline:** closing commit of DLV-005
**Owner:** bridge/native controller ingress, feedback surface, and host tests

**Objective:** Prove one advanced-widget action failure traverses the real
worker/bridge/native host route into painted and UI Automation-visible bounded
feedback without restarting the widget or losing focus.

**In scope:** an existing credential-free Spotify or YT Music fixture; actual
controller ingress and production feedback adapter; exact source/surface,
generation, expiry, replacement, stale, Hide, Stop, and failure-code assertions.

**Out of scope:** editing Spotify/YT Music product behavior, live auth, a second
feedback system, global error redesign, or synthetic direct-callback proof that
bypasses the production route.

**Acceptance criteria:** the failure is sanitized, visible, announced through
the real accessibility path, expires/replaces deterministically, preserves
focus, and does not restart the worker; stale/wrong-surface failures are
rejected.

**Verification:** Tier 1 bridge/native/host tests and the smallest Tier-2
installed-widget conformance group. Do not run the aggregate.

**Stop/escalate when:** the proof requires product-widget source changes owned
by the widgets lane or a public protocol change not already represented.

### DLV-020 — Make widget switching visually continuous

**State:** Done; integrated as `7cda335`; product acceptance reopened by
DLV-025 after user evidence
**Baseline:** `2a160b4`, the accepted platform-lane DLV-014 closing commit
**Owner:** OverlayHost presentation, invalidation, rounded clipping, render
target lifecycle, and transition tests

**Objective:** Remove the jarring switch and transient black border/spacing
flash now reproduced when cycling into Spotify, while keeping the tray and host
chrome continuously painted.

**In scope:** instrumented old/new extent and identity swaps; placement,
snapshot/style arrival, render-target recreation, rounded viewport clip,
background clear, shell/content reveal, and invalidation ordering; bounded
product-target transition using the existing host timeline; reduced motion;
rapid reversal; same-identity refresh; compact/standard/wide and 100-150%
scale; retained frame/capture evidence.

**Out of scope:** widget-authored transition hacks, decorative/staggered motion,
ambient animation, per-widget black backgrounds, hiding the flash with a delay,
or additional idle frame scheduling.

**Acceptance criteria:** Audio/Network/Spotify/Games & Apps switch matrices show
no cleared border, unmasked square root, stale extent, blank content frame, or
tray flash; focus and input ownership transfer once; reduced motion is
immediate; interruptions retarget from presented state; settled/hidden cost is
unchanged; diagnostics identify the original cause.

**Verification:** Tier 1 OverlayTransition, placement/targeting, renderer, and
host Release tests plus a bounded frame-sequence capture. No aggregate.

**Stop/escalate when:** correction requires a new compositor/window technology,
substantial theme redesign, or interactive game/display evidence unavailable to
automation.

### DLV-025 — Eliminate transition tearing and UI-thread stutter

**State:** Blocked at the documented compositor/window-technology stop condition;
user architecture authority required before implementation resumes
**Baseline:** `17e4388`, the clean reviewer control-plane commit containing the
accepted DLV-020 integration
**Owner:** OverlayHost transition scheduling, Win32/DWM window composition,
Direct2D resize/invalidation, native bridge/UI-thread interaction, and temporal
product evidence

**Objective:** Correct the live regression that makes widget-size transitions
miss frames, flicker the interface, and expose large gray/black/stale regions
around Games & Apps. Preserve visual continuity only when it can be delivered
within a measured frame budget; an immediate stable switch is preferable to a
laggy or tearing animation.

**Preserved blocker evidence:** The branch remains at committed planning
baseline `57aa2d5`, and the measurements below remain recorded, but the former
Codex worktree and its uncommitted DLV-025 files are no longer registered or
present. Reconstruction is not authorized. Real populated Spotify Queue and
Games & Apps first paints measured
about 31 ms; 14 Spotify inputs produced six successful paints over 674 ms; and
five corrected consecutive captures exposed the dark interior band at final
geometry before list paint completed. Removing repeated extent interpolation
did not satisfy acceptance. Resizing the current Direct2D HWND render target
exposes an undefined resized back buffer before a successful draw; a later
`DwmFlush` cannot retract frames already composed. A credible atomic fix now
requires offscreen precomposition with a proven atomic present boundary or
DirectComposition/swap-chain ownership. No known-bad product commit was made.

**In scope:** reproduce the exact real Games & Apps and Spotify switch paths;
instrument timer cadence and per-frame duration across `SetWindowPos`, `WM_SIZE`,
Direct2D target resize/resource recreation, invalidation, synchronous redraw,
bridge calls, and committed presentation; identify exposed/uncommitted regions
and UI-thread blocking; select one host-owned atomic presentation design;
retarget/reversal, same-identity refresh, reduced motion, compact/standard/wide,
100-150% scale, and continuously painted tray/backdrop behavior. If a blocking
bridge operation is measured on the transition-critical UI path, move or bound
only the necessary request ownership without changing widget APIs or authority.

**Out of scope:** per-widget backgrounds or timing branches, hiding artifacts
with a longer delay, decorative motion, a general bridge rewrite without
measured relevance, changing Games & Apps composition, new public widget
protocol, or claiming smoothness from static synthetic captures alone.

**Acceptance criteria:** real first-party Games & Apps, Spotify, Audio Mixer,
and Network switch sequences show no black, gray, transparent, stale, or
unpainted bands and no whole-interface flicker; temporal evidence reports
transition frame/cadence distribution and identifies any missed-frame budget;
the selected path performs no synchronous per-frame operation whose measured
cost violates that budget; rapid reversal and same-identity refresh remain
continuous; reduced motion is immediate; focus/input/UIA authority remains
correct; settled and hidden cost are unchanged. If smooth live HWND extent
animation cannot satisfy these criteria on the current compositor, replace it
with an immediate or composition-only transition and document the decision.

**Verification:** Tier 1 transition, placement/targeting, renderer, resize, and
host Release suites; production OverlayHost build; a bounded timestamped frame
sequence or video-derived capture using the real first-party product surfaces,
including the user-reported Games & Apps size change. Record frame times and
review every transition interval, not only endpoints. No aggregate.

**Stop/escalate when:** the only credible correction requires a new compositor
or window technology, materially changes the public protocol/threat model, or
cannot be evaluated without a user-only physical display. Exhaust automated
real-product temporal evidence before escalating.

### DLV-026 — Restore bidirectional Audio Mixer scrolling

**State:** Integrated as `4957101`; acceptance withdrawn after live keyboard and
controller failure; correction assigned as DLV-049
**Baseline:** accepted visible-priority main commit `8028b83`
**Closing commits:** `979de24`, `68efc70`, corrected by `9cc633a`
**Dependencies:** DLV-003 and DLV-029; independent of blocked DLV-025
**Owner:** native host focus navigation, controller scroll reveal/state, and
real-product Audio Mixer host fixtures

**Objective:** Correct the reproduced controller trap in Audio Mixer: after
moving down far enough to scroll the root surface, Up must move focus and the
viewport back through every preceding control, including the master and device/
input options that left the visible area.

**In scope:** reproduce the exact production Audio Mixer snapshot at preferred
and constrained heights with enough sessions to overflow; record focus ID,
explicit focus target, scroll offset, revealable target, presentation bounds,
and active input scope for every Down and reverse Up step; correct host-owned
focus/reveal/state behavior if the emitted graph is valid; stable scroll identity
across snapshot refresh, session addition/removal, 100-150% scale, compact and
standard surfaces, analog/D-pad parity, and retained deterministic semantic/
state evidence. The freshly launched Release supplies the user's visual check.

**Out of scope:** changing audio capability/provider behavior, dashboard
LB/RB/X authority from DLV-019, input/output endpoint selection, manual wheel-
only navigation, per-widget pixel/offset workarounds, DLV-006 cursor pagination,
or weakening clipping/revealability rules for hidden controls.

**Acceptance criteria:** a real populated Audio Mixer sequence can traverse
from master output to the final visible session and reverse one control at a
time to the first control without a dead end, focus teleport, hidden focused
target, stale offset, or scope escape; the root offset decreases monotonically
on the reverse path and reaches the true leading boundary; refresh and bounded
session churn preserve the focused stable control when it still exists and use
a deterministic nearest fallback otherwise; other representative scroll
surfaces retain their current behavior.

**Verification:** Tier 1 native focus, declarative renderer, scroll-state, and
host Release suites plus the production Audio Mixer fixture at preferred,
constrained, and 150%-scale surfaces with a retained down-and-reverse focus,
offset, revealability, bounds, and UIA trace. Screenshots are non-gating; exclude
any invalid artifact without capture-harness work. After integration, launch the
fresh main Release for the user's live functional and visual check. No aggregate.

**Stop/escalate when:** the production snapshot lacks or misstates an explicit
Audio Mixer Up link, the defect requires managed widget source changes, or the
correction changes the public scroll protocol. Preserve the reproduction and
return it to the planner for a widgets-lane or serialized reassignment rather
than adding a native special case.

### DLV-021 — Correct shared text and component geometry end to end

**State:** Done; accepted and integrated on `main` as `bc2de86`
**Baseline:** accepted DLV-026 closing commit `9cc633a` (integrated on `main`
as `4957101`)
**Closing commit:** `b714efe` (`[DLV-021] unify shared text geometry`)
**Owner:** native declarative measurement/paint, shared Button/ActionSurface/
SectionHeader styles, and component-level semantic/capture tests

**Objective:** Resolve the remaining cross-widget text/icon/checkmark baseline
misalignment and the clipped Spotify `LIBRARY` header in shared geometry rather
than per-widget offsets.

**In scope:** one measurement/placement model for Button, icon-label-checkmark,
Now Playing/action tiles, SectionHeader eyebrow/title/description/trailing
content, busy/selected states, wrapped/ellipsized text, font metrics, compact/
standard/wide, 100-150% text/interface scale, and high-contrast/reduced-
transparency profiles; exact Games & Apps, Spotify, Now Playing, Settings, and
SDK Gallery fixtures.

**Out of scope:** arbitrary optical constants per widget, changing information
architecture, global font replacement, unrelated renderer refactoring, or
claiming RTL support where it is not documented.

**Acceptance criteria:** text and icons share stable measured/painted centers;
no eyebrow/title/description is vertically clipped; trailing actions cannot
steal required header height/width; disabled/busy/selected states do not shift
content; first-party widgets delete or avoid local compensation; retained
bounds and reviewed captures cover all named profiles.

**Verification:** Tier 1 native layout/renderer/style/component suites,
production host build, documentation contracts, and representative semantic/
capture matrix. No aggregate.

**Stop/escalate when:** the defect is proven to be product-specific composition
with no shared geometry cause, or correction changes a public layout contract.

**Reviewer disposition:** Accepted. One `NativeTextLayoutPlan` now supplies
DirectWrite measurement and paint from the same transform, font, wrapping,
trimming, spacing, overhang, and baseline inputs; non-wrapping rows remeasure
cross-size after final flex widths, and intrinsic extents round outward at the
effective pixel scale. The direct production-renderer matrix covers Games &
Apps, Spotify, Now Playing, Settings, and SDK Gallery at compact, standard,
wide/150%, and combined high-contrast/reduced-transparency profiles, including
selected, busy, disabled, trailing-action, and complete text-bound assertions.
Focused Release evidence passes NativeTextLayout 25, DeclarativeLayout 250,
DeclarativeRenderer 4,769, shared component geometry 589, NativeStyle, the
production host build, and 52 documentation contracts. The standalone body
artifact retains 16 Games/Spotify renders with zero renderer diagnostics and
honestly excludes its existing Settings worker-start gap; live packaged visual
confirmation remains in the verification queue.

### DLV-049 — Correct the live Audio Mixer reverse-scroll state

**State:** Done; accepted and integrated on `main` as `a8bcb27`
**Baseline:** accepted DLV-021 closing commit `b714efe` plus integrated DLV-026
candidate `4957101`
**Dependencies:** DLV-021 only for same-lane order; independent of blocked
DLV-025
**Owner:** platform lane over emitted-snapshot inspection, native focus target
admission, retained scroll state, layout/extent reconciliation, and the smallest
production-host Audio Mixer fixture; managed widget source remains widgets-owned

**Visible outcome:** Without cycling away and back, Up from the focused
Microphone Slider returns through the device section to Master output and the
viewport follows it in the user's four-session Audio Mixer state. Returning to
the widget must not be required to repair navigation or unexpectedly normalize
an invalid offset.

**Objective:** Reproduce the exact live failure that DLV-026's healthy static
12-session fixture missed, prove whether the emitted `audio.input.volume.slider`
Up edge names the intended target, and correct the owning native retained-state/
reveal path when that edge is valid.

**In scope:** start from the real staged Audio Mixer worker/provider-shaped
snapshot with four application sessions; retain the live sequence where the
Microphone Slider is at the leading visible edge and Master is above the
viewport; record the emitted explicit Up target before host processing, current
focus ID, active scope, root offset and maximum, viewport/target bounds, and
revealability before and after one Up; reproduce without screenshots; compare
the same state before and after cycling away/back; reconcile offsets and focus
admission across snapshot arrival, content-extent change, and widget extent
transition; remove or supersede DLV-026 fixture assumptions that cannot detect
this state.

**Out of scope:** controller-versus-keyboard routing changes, capture/screenshot
harness work, generic scroll redesign, per-widget pixel offsets, provider or
audio-capability behavior, managed Audio Mixer edits without planner
reassignment, DLV-021 geometry work, or another broad traversal matrix.

**Acceptance criteria:** the retained live-shaped evidence first states whether
`Microphone.Up` is present and exact in the emitted snapshot. If it is valid,
the host admits Master as revealable, moves focus on the first Up, decreases the
root offset toward the true leading boundary, and reaches Master without a
widget cycle under stable content and after the reproduced extent/snapshot
transition. The stored offset is always finite and within the current measured
maximum; cycling away/back does not produce `value_clamped` for `audio.root` or
change navigation viability. Keyboard and controller use the same corrected
focus result. Other scroll surfaces retain their current behavior.

**Verification:** one bounded live-shaped production-HWND/UIA regression plus
the smallest directly affected renderer/focus/scroll-state Release tests and a
production OverlayHost Release build. Begin the regression in the broken
Microphone/offscreen-Master state; do not derive expected reverse order by first
walking forward from Master. No screenshots, capture work, aggregate, broad
scale matrix, unrelated widget suite, or repeated full traversal run. After
integration, launch the fresh main Release for the user's immediate functional
check.

**Stop/escalate when:** the emitted live snapshot lacks or misstates the Up edge;
preserve the exact snapshot/evidence and report it for an immediate widgets-lane
correction instead of adding a native fallback. Also stop for public protocol
changes or a defect inseparable from the blocked compositor architecture.

**Closing commit:** `32af19b` (`[DLV-049] lock four-session Audio Mixer reverse
scroll`)

**Reviewer disposition:** Accepted. The exact staged four-session snapshot
retains `audio.input.volume.slider` Up to Master. On the accepted DLV-021 shared-
geometry baseline, Master remains an offscreen-but-revealable native target at a
finite retained `296.6 / 298.2` root offset; one production HWND/UIA Up reaches
Master and offset zero, and reopening neither changes viability nor emits
`value_clamped [audio.root]`. The assignment therefore locks the already owning
geometry correction rather than adding another navigation workaround. Focused
Release evidence passes 4,774 renderer checks, 35 probe checks, the one exact
production-host scenario, and a fresh host build. The main Release rebuilt at
`822d29c`; live keyboard/controller confirmation remains required before closing
GBA-003.

### DLV-015 — Add deterministic real-host accessibility proof

**State:** Done; accepted source commit `b371983`, integrated as `6d3b093`
**Baseline:** accepted DLV-049 source commit `32af19b`, integrated on `main` as
`a8bcb27`
**Owner:** native accessibility adapter, host harness, and retained evidence

**Objective:** Exercise the production UI Automation projection over
representative Settings, YT Music, and Spotify semantic trees rather than
claiming accessibility from data-only snapshots alone.

**In scope:** deterministic host-level names/roles/states/values/bounds/order/
actions; focus movement and restoration; loading/error/disabled/busy states;
compact/standard and 100-150% scale fixtures; sanitized retained output.

**Out of scope:** physical Narrator sign-off, widget visual redesign, changing
semantic meaning to satisfy a snapshot, or broad automation of the desktop.

**Acceptance criteria:** the production adapter exposes a stable reachable tree
with no duplicate/missing actionable nodes, clipped essential bounds, stale
focus, or hidden interactive controls in the named fixtures; failures identify
the exact semantic element and state.

**Verification:** Tier 1 native accessibility/host tests plus the smallest
Tier-2 packaged-host fixture available. No aggregate. Physical Narrator remains
manual evidence.

**Stop/escalate when:** reliable proof requires user desktop control, physical
assistive technology, or widgets-lane source changes.

### DLV-052 — Restore current community addons after a Release relaunch

**State:** Done; accepted and integrated through `56f6908`
**Closing commits:** `a4dcf0f`, corrected by `64a4039`
**Baseline:** accepted integration `6d3b093` plus the reviewer control-plane
commit containing this assignment
**Owner:** platform lane over community package build/deployment discipline,
generic installed-worker load diagnostics, WidgetBridge lifecycle/snapshot
ordering, OverlayHost failure presentation, and the smallest real installed
Spotify/YT Music fixture
**Concurrency:** May run while widgets DLV-006 changes only its assigned
collection/SDK/protocol/native collection surface. Do not edit DLV-006 files,
public Widget SDK/protocol behavior, Spotify/YT Music feature logic, or shared
native build/test manifests being modified by DLV-006. Stop for planner
serialization if the exact fix overlaps those boundaries.

**Visible outcome:** The freshly relaunched accepted Release opens both Spotify
and YT Music instead of showing `Widget worker connection failed` or `A hidden
suspended widget has no cached snapshot`. A real worker-start failure produces
one accurate safe status and Retry can recover without restarting the overlay.

**Reproduction evidence:** The 2026-08-10 21:52-21:54 live overlay log records
both community workers repeatedly exiting with code 2 before connecting. YT
Music then receives a snapshot request after its Visible lifecycle transition
failed and exposes the secondary hidden-cache exception. The selected installed
Spotify `0.2.10` DLL hash differs from the current same-version package artifact,
and installed YT Music is `0.2.5` while source is `0.2.6`. Treat package freshness
and failure-state ordering as two required parts of one product recovery, not as
widget-provider failures.

**In scope:** determine and retain the exact bounded safe loader failure code;
rebuild/stage/validate/pack/install/select current Spotify and YT Music through
the supported generic Community path; establish version/content discipline so
a visible Release refresh cannot silently exercise an older same-version
payload; allow an intentional pre-release state reset or package version bump
instead of legacy compatibility; make lifecycle establishment and initial
snapshot admission one coherent result; retain the prior admitted presentation
or one accurate actionable failure; bounded retry with exactly one fresh worker
generation; rapid Spotify/YT Music cycling, overlay close/reopen, worker crash,
and failed/successful retry.

**Out of scope:** OAuth/Premium or companion credentials, provider feature
changes, permissive public same-version overwrite, weakening digest/AppContainer/
capability admission, broad installer redesign, public SDK/protocol changes,
DLV-006 collection work, compositor/animation work, or preserving obsolete
pre-release installed state.

**Acceptance criteria:** from a clean bounded test profile, the exact current
source packages receive content-unique versions, validate, pack, install,
select, and start through the production generic AppContainer worker path; the
selected installed payload digest equals the package that was just built. A
latest-Release relaunch cannot select an older payload under the same version.
Spotify and YT Music each complete lifecycle establishment and publish a first
snapshot. Forced load/connect/lifecycle failures expose one bounded safe reason,
never request a contradictory hidden snapshot, never reveal a path/credential/
exception/provider body, and never leave a half-current lifecycle record. One
Retry starts one fresh generation and can recover; stale completion from the
failed generation cannot publish. Existing first-party worker startup and
suspend/unload behavior remain unchanged.

**Verification:** one bounded installed-package fixture covering both exact
current addons through build/stage/validate/pack/install/select/start/first
snapshot, plus the smallest WidgetWorkerHost, WidgetRuntime, WidgetBridge, and
OverlayHost lifecycle/failure Release groups. Use metadata/digests and safe
diagnostic codes rather than executing package assemblies in the planner or
capturing screenshots. Build the production OverlayHost. No aggregate unless a
canonical manifest changes. After integration, refresh the exact community
packages in the user's local profile through the supported tool, rebuild main
Release, and visibly relaunch it for immediate testing.

**Stop/escalate when:** correction requires public SDK/protocol behavior while
DLV-006 is active, weakens installed-package isolation/integrity, needs external
credentials, requires destructive package/state recovery outside an explicit
pre-release reset, or overlaps the preserved DLV-025 worktree.

**Reviewer disposition:** Accepted after a real-profile correction. The first
candidate established closed generic-loader diagnostics, atomic lifecycle plus
first-snapshot admission, one persistent Retry surface, Spotify `0.2.11`, YT
Music `0.2.7`, and exact digest/AppContainer startup evidence. Live refresh then
proved the script installed Spotify `0.2.11` but left `0.2.10` selected. The
correction makes both supported workflows perform disable, install, explicit
version select, and enable, while the isolated fixture now seeds older enabled
versions and proves the new digest is selected. Focused evidence passes worker
host 10/10, runtime 74/74, bridge 69/69, the production host failure/Retry case,
and the exact-package recovery fixture. The local profile now reports enabled
Spotify `0.2.11` and YT Music `0.2.7`; accepted main `56f6908` was rebuilt and
visibly relaunched for live confirmation. The first planner relaunch used the
native-only `-SkipPackaging` path and therefore paired the new native
`admitSnapshot` request with an older packaged bridge, producing a live
`set-widget-lifecycle` invalid-payload failure. A full documented Release
packaging build from accepted main republished WidgetBridge, WidgetRuntime,
worker hosts, and bundled widgets together; the 23:23 launch no longer records
that failure. Future bridge/runtime integrations must treat the Release output
as one coherent artifact graph rather than a native executable alone.

### DLV-016 — Establish native idle and semantic-churn baselines

**State:** Done; accepted source commit `df1258e`, integrated as `fee1103`
**Baseline:** accepted DLV-052 integration `56f6908` plus the reviewer commit
recording this assignment
**Owner:** OverlayHost/native renderer measurement harness and budgets

**Objective:** Add reproducible bounded measurements for hidden/idle host cost
and semantic/render-tree churn so future UI and pinned-surface work has a
credible performance gate.

**In scope:** stable synthetic representative trees; hidden, visible-idle, and
input-update phases; CPU time, private working set, node/update counts, snapshot
bytes, and input-to-projection latency where deterministic; documented machine
metadata and non-flaky regression thresholds.

**Out of scope:** claiming universal game-time GPU performance, ETW/PresentMon
requiring user interaction, speculative optimization, or changing product
behavior merely to hit an invented number.

**Acceptance criteria:** measurements are bounded, repeatable, provenance-aware,
and fail only on material regressions; they separate harness overhead from
product cost and publish baselines/limitations without hiding variance.

**Verification:** Tier 1 measurement-harness tests and repeated bounded local
samples. No aggregate unless the verifier manifest changes.

**Stop/escalate when:** evidence requires elevated tracing, a representative
game/hardware choice, or a product budget decision not already documented.

**Reviewer disposition:** Accepted. The focused synthetic harness and the
existing production-host sampler are complementary rather than substituted:
the first supplies deterministic semantic churn, snapshot/node/update,
latency, private-resident-page, provenance, repeat range, and material gates;
the second supplies actual Hidden/Visible process-tree CPU, memory, process,
timer, and Direct2D-frame observations. Retained evidence and limitations match
the Recently completed disposition. No product behavior, aggregate verifier,
capture path, or elevated tracing changed.

### DLV-011 — Feasibility gate for host-owned pinned surfaces

**State:** Done; accepted and integrated as `35df08c`
**Closing commit:** `9af2a76`
**Baseline:** accepted DLV-016 integration `fee1103` plus the reviewer commit
advancing this assignment
**Dependencies:** DLV-006 and DLV-016
**Owner:** platform lane over a host-owned pinned-window feasibility harness,
window/focus/input/lifecycle policy, monitor/DPI placement, and bounded native
resource evidence; no YouTube provider, playback, credential, or public widget
API implementation
**Visible outcome:** Establish one credible Windows surface architecture that
can remain pinned after the main overlay closes without stealing game input or
leaking an orphaned window, before the YouTube Video widget is implemented.

**Objective:** Compare the smallest viable native tool-window and supported
compact-overlay/AppWindow approaches with one declarative placeholder surface,
then recommend or reject a bounded host-owned architecture using measured
behavior rather than committing to a playback stack prematurely.

**In scope:** pin/unpin and overlay-close lifecycle; explicit focusable versus
click-through modes; topmost ownership; taskbar/Alt-Tab behavior; teardown and
crash cleanup; monitor work area, DPI, rotation, hot-plug, and persisted bounded
placement; borderless-window compatibility assumptions; reduced motion and
accessibility ownership; DLV-016 hidden/visible idle and semantic-churn deltas;
one deterministic declarative color/test surface and documented comparison.

**Out of scope:** WebView2 or YouTube playback, Google authentication, arbitrary
community HWND creation, public pinning protocol, always-on ambient polling,
store packaging, broad compositor migration, game-specific hooks, or claiming
universal exclusive-fullscreen compatibility.

**Acceptance criteria:** the selected candidate, or documented rejection of all
candidates, records exact focus/input/topmost/teardown/placement behavior and
resource deltas against DLV-016. Hiding the main overlay does not destroy a
pinned surface; unpin, host exit, crash recovery, monitor loss, and invalid
persisted placement converge to one bounded cleanup/fallback. The test surface
cannot receive widget-authored window handles or authority. Any manual-only
borderless-game/display claims remain explicitly unverified.

**Verification:** Tier 1 bounded native lifecycle/placement/focus tests and
DLV-016 measurement comparison plus the smallest production-host placeholder
fixture. No aggregate, video, credentials, screenshot acceptance, or physical
game matrix.

**Stop/escalate when:** a credible choice requires Windows App SDK or another
new runtime dependency, a new compositor/window technology, public protocol or
threat-model changes, elevated hooks, or physical-only evidence that materially
changes the architecture decision.

## Integration queue

These are planned but are not executable by either lane until the planner marks
one Assigned on an accepted integrated baseline.

DLV-006, DLV-022, and DLV-018 moved to the widgets lane as one ordered serialized
cross-lane sequence on accepted `main`. Their complete executable assignments
are maintained there; neither implementation task may select a duplicate from
this integration queue.

### DLV-033 — Establish a host-owned widget session coordinator

**State:** Awaiting DLV-025 architecture decision and accepted bridge baseline
**Intended lead:** platform lane with serialized managed-bridge prerequisite
**Dependencies:** DLV-025 and DLV-032

Implement EQ-003/EQ-020's first native ownership boundary above
`WidgetBridgeClient`. A directly tested `WidgetSessionCoordinator` should own
descriptor/snapshot collections, catalog retry state, tracked lifecycle target,
runtime/presentation generation, typed per-widget session status, and bounded
asynchronous request completion. `OverlayApp` remains the Win32, focus,
renderer, D2D/DWrite, and presentation adapter. Do not pass HWND/renderer state
into the coordinator or create a generic event bus. Prove runtime versus
presentation replacement, removal of active/hovered widgets, last-good retry,
stale invalidation/effect rejection, start/snapshot/protocol failure, lifecycle
drain, and Close/Guide responsiveness while another request stalls.

DLV-011 completed the bounded architecture gate on accepted DLV-006 and
DLV-016 foundations. Shipping pinning, rich media, and physical game/display
compatibility remain separate future assignments.

## Blocked work

| Item | Blocker | Unblocking evidence |
| --- | --- | --- |
| DLV-025 atomic widget-size presentation | Current HWND render-target resize exposes undefined content during real list-heavy first paint; the assignment's documented stop condition forbids adopting new compositor/window technology without planner/user authority. Its former dirty Codex worktree has disappeared, leaving only branch `57aa2d5` and the recorded evidence. | User chooses and authorizes a bounded compositor architecture milestone and any needed reconstruction; planner updates DLV-025 scope and acceptance before a new isolated implementation surface is created. |
| Audio Mixer default input/output endpoint selection | The roadmap forbids undocumented `PolicyConfig`, registry writes, or Shell automation. | Primary Microsoft API evidence for a supported setter plus a bounded provider design and reversible hardware plan. |
| Live Spotify account and Web Playback completion | Account, Premium eligibility, development allowlist, OAuth, and EME interaction. | User-authorized live account and retained manual evidence. |
| YouTube authenticated library | Google OAuth consent/verification and a user account; Watch Later is not supported by the Data API. | Approved minimum-scope OAuth design, verification plan, and user-authorized account. |
| Real controller, mixed-DPI/hot-plug, companion, Bluetooth, audio-device, and representative-game checks | Physical hardware or interactive environment. | Retained packaged/manual evidence from the named matrix. |

## Verification queue

These are evidence tasks, not authorization to redesign implementations:

1. Consolidated packaged controller and visual matrix for issues marked
   Verifying.
2. Real YT Music companion pairing/reconnection and physical-controller proof.
3. Live Spotify pagination, failure-route, OAuth, Web Playback, and device proof
   only when the required account/environment is available.
4. Physical high-scale/high-contrast Games & Apps captures after DLV-004.
5. Physical Y-hold threshold and exactly-once refresh smoke after DLV-005.
6. Physical Narrator/MSAA traversal after DLV-015.
7. Packaged Spotify seek/list forward/reverse traversal and transient-failure
   recovery after DLV-022/DLV-023.
8. Packaged widget-switch frame continuity and shared Button/SectionHeader
   alignment matrix after DLV-020/DLV-021.
9. Packaged Games & Apps cold-restart first-paint timing and real trusted-source
   artwork coverage after DLV-017/DLV-018.
10. Physical Audio Mixer LB/RB/X dashboard quick-action authority, step,
    coalescing, mute, and failure smoke after DLV-019.
