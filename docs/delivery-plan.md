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
  Ready item only when its dependencies are already present in its branch.
- A task never reorders, merges, broadens, or invents assignments and never
  selects work from another document or lane.
- Every assignment normally produces one coherent local commit whose subject
  begins with `[DLV-nnn]`. Nothing is pushed.
- Implementation tasks update directly affected public documentation and
  `docs/implementation-status.md`. They never edit reviewer-owned planning,
  roadmap, issue, review, or goal documents.
- A reproducible P0 may interrupt the queue. Other discoveries become concise
  planner evidence rather than opportunistic implementation.
- The planner reviews completed commits in order, returns inadequate work for
  correction, integrates only accepted work, and refills both lane queues.

### Architecture non-regression gate

- Production types above roughly 1,000 physical lines, plus smaller types that
  own several independently testable concerns, are architecture-review
  hotspots. Line count triggers review; it is not a design target.
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

### Branch and integration protocol

- The `widgets` task works only in its Codex worktree on
  `codex/impl-widgets`; the `platform` task works only in its Codex worktree on
  `codex/impl-platform`.
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

## Recently completed

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

## Widgets lane

Task identity: `widgets`
Branch: `codex/impl-widgets`

The ordered widgets queue keeps newly reproduced Games & Apps continuity
regressions adjacent to DLV-017, converts that stabilized implementation into
named responsibility boundaries, then applies the same evidence-based standard
to Spotify failure policy, Network Controls, and the external package journey.
Spotify list work that depends on shared cursor/append foundations remains in
the serialized integration queue rather than being patched locally.

The decomposition milestones implement EQ-006 without using file length as a
mechanical gate: each extraction must reduce shared mutable knowledge or create
a focused domain-policy seam. Audio Mixer follows DLV-037 in this lane and is
independent of DLV-026: DLV-029 must preserve current focus IDs and navigation
behavior so the native/shared-scroll correction remains a separately reviewable
milestone. DLV-006 must still land before Spotify continuous-list and launcher
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

**State:** Assigned
**Baseline:** accepted DLV-030 closing commit `549da57`
**Dependencies:** DLV-023 and DLV-028
**Owner:** managed `PlatformCapabilityBroker` internals and direct broker policy
fixtures; no native-host files

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

**State:** Ready after DLV-031
**Baseline:** closing commit of DLV-031
**Dependencies:** DLV-031 when shared broker/bridge test infrastructure changes
**Owner:** managed `WidgetBridgeServer` request scheduling and direct bridge
fixtures; framing and session ownership remain in the server

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

**State:** Ready after DLV-032
**Baseline:** closing commit of DLV-032
**Dependencies:** DLV-023 and DLV-031
**Owner:** managed Windows Spotify provider internals and injected-transport
fixtures

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

**State:** Ready after DLV-034
**Baseline:** closing commit of DLV-034
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

### DLV-036 — Split Settings by page policy and privileged operations

**State:** Ready after DLV-035
**Baseline:** closing commit of DLV-035
**Dependencies:** DLV-001 and DLV-035 only for queue order
**Owner:** managed Settings widget internals and direct Settings fixtures

**Objective:** Keep one widget lifecycle and committed settings state while
making one ordinary settings page, persistence rule, diagnostic projection, or
authority-recovery workflow changeable without reading the complete roughly
1,090-line widget.

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
quantifies the before/after responsibility map.

**Verification:** Tier 1 Settings, exact-token recovery, smallest Widget SDK
render/navigation, and affected documentation Release suites. No aggregate or
unrelated installed-widget hardening.

**Stop/escalate when:** the split changes authority, persistence schema, public
SDK/protocol behavior, or requires reopening frozen security work.

### DLV-037 — Split managed worker-session transport from gesture authority

**State:** Ready after DLV-036
**Baseline:** closing commit of DLV-036
**Dependencies:** DLV-032
**Owner:** managed `WidgetProcessClient` internals and direct runtime fixtures;
no native host files

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

### DLV-029 — Split Audio Mixer by stable responsibility

**State:** Ready after DLV-037
**Baseline:** closing commit of DLV-037
**Dependencies:** DLV-037 for managed-lane queue order; DLV-026 is independent
**Owner:** managed Audio Mixer internals and direct widget fixtures; no native
host, broker, capability, or protocol files

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
the complete widget; success, failure, cancellation-ignoring completion,
timeout/rollback, provider churn, session removal, and deactivation remain
deterministic; existing focus IDs and current navigation behavior are preserved
for DLV-026 to diagnose and correct separately; the completion report quantifies
responsibilities, locks/semaphores/tasks, and cross-boundary mutable dependencies
before and after.

**Verification:** Tier 1 Audio Mixer, Widget SDK operation/lifecycle, and the
smallest affected audio-provider/broker mapping Release suites with manually
completed provider operations. No native, aggregate, or unrelated security
suite.

**Stop/escalate when:** correct separation requires public capability/protocol
or authority changes, touches the shared native scroll owner, introduces a
second lifecycle/committed-state owner, or exposes a product defect that needs
its own bounded assignment.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform`

The platform queue prioritizes newly reproduced presentation continuity,
bidirectional controller scrolling, and shared geometry defects before broader
accessibility/performance evidence. Shared collection and dashboard-audio
authority changes remain serialized in the integration queue.

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

**Preserved blocker evidence:** The platform worktree remains uncommitted at
`57aa2d5`. Real populated Spotify Queue and Games & Apps first paints measured
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

**State:** Ready after DLV-025
**Baseline:** closing commit of DLV-025
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
standard surfaces, analog/D-pad parity, and retained semantic/capture evidence.

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
constrained, and 150%-scale surfaces with a reviewed down-and-reverse capture
sequence. No aggregate.

**Stop/escalate when:** the production snapshot lacks or misstates an explicit
Audio Mixer Up link, the defect requires managed widget source changes, or the
correction changes the public scroll protocol. Preserve the reproduction and
return it to the planner for a widgets-lane or serialized reassignment rather
than adding a native special case.

### DLV-021 — Correct shared text and component geometry end to end

**State:** Ready after DLV-026
**Baseline:** closing commit of DLV-026
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

### DLV-015 — Add deterministic real-host accessibility proof

**State:** Ready after DLV-021
**Baseline:** closing commit of DLV-021
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

### DLV-016 — Establish native idle and semantic-churn baselines

**State:** Ready after DLV-015
**Baseline:** closing commit of DLV-015
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

## Integration queue

These are planned but are not executable by either lane until the planner marks
one Assigned on an accepted integrated baseline.

### DLV-006 — Prove virtualized game-library collection foundations

**State:** Dependencies accepted; awaiting planner-selected free lane/baseline
**Intended lead:** planner-selected serialized protocol lane
**Dependencies:** DLV-004 and DLV-005

Add the minimum cursor/append virtualized collection, lazy artwork handles,
stable keyed focus, bounded retention, and 2,000/10,000-item production-style
fake needed by the future Game Launcher. The contract must also replace the
currently reported page-window jump: advancing or reversing at a focus edge
keeps one continuous keyed list/viewport anchor instead of replacing a 12-row
window and moving focus from bottom to top or top to bottom. Cover a fixed
header action above the list so reverse loading cannot oscillate between the
header and first item. Public semantics and native adoption must land as one
serialized design. This is an exact-commit Tier-3 checkpoint.

### DLV-018 — Supply trusted artwork for Games & Apps

**State:** Awaiting DLV-006; DLV-017 accepted as `5aedfe8`
**Intended lead:** planner-selected serialized provider/collection lane
**Dependencies:** DLV-006, DLV-017

Complete the trusted artwork path for Start Menu, AppsFolder, Steam, and future
source adapters without exposing paths or allowing ordinary widgets to fetch
arbitrary files/URLs. Use DLV-006 lazy handles and bounded decode/cache budgets;
show a semantic per-source fallback only when an exact registration has no
trusted local artwork. Revalidate artwork with the same provider identity as
launch, reject stale/malformed/oversized images, and cover missing/change/churn,
2,000/10,000-item demand, memory/transport bounds, and current Games & Apps
captures. Do not solve this by embedding hundreds of base64 icons in snapshots.

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

### DLV-019 — Add Audio Mixer dashboard master controls

**State:** Awaiting DLV-029 and planner-selected serialized baseline
**Intended lead:** planner-selected bridge/broker plus widgets integration lane
**Dependencies:** DLV-014, DLV-029

Expose LB/RB as bounded master-output volume down/up and X as master mute from
the icon tray. Reuse the exact snapshot-bound dashboard gesture authority; add
dashboard permission only for the existing master-output set-volume/set-mute
operations, not session/input/device controls or the whole audio subsystem.
The widget must publish visible labels/current mute/volume state, clamp a
documented step, re-read/reconcile authoritative output, coalesce rapid input,
reject stale snapshot/lifecycle/consent/authority, roll back failures, preserve
open-widget slider behavior, and prove no control occurs from an unselected,
hidden, Background, replayed, or expired action. This is a Tier-2 cross-process
authority milestone, not a speculative security program.

### DLV-022 — Repair Spotify spatial and continuous-list focus

**State:** Awaiting DLV-006 and DLV-021 acceptance
**Intended lead:** widgets lane on an accepted shared collection/geometry baseline
**Dependencies:** DLV-006, DLV-021

Migrate Spotify playlists and playlist items to the shared continuous
cursor/append collection and correct the explicit responsive focus graph. In
expanded mode, Left from the inactive seek Slider must enter the selected
navigation-rail destination rather than Previous track; compact mode must use
the spatially corresponding navigation tab. Reverse traversal from the first
playlist item must move to the Play/header action only once when intended and
must never oscillate during load/snapshot replacement. Queue/list traversal,
12/12/5 and sparse pages, forward/reverse cache transitions, Back/return focus,
rapid refresh, compact/expanded responsive identity, and the clipped `LIBRARY`
header must have composed host/renderer/controller evidence. Do not add
widget-local page caches, renderer offsets, or title-derived focus IDs.

### DLV-011 — Feasibility gate for host-owned pinned surfaces

**State:** Awaiting near-term corrections and DLV-006 architecture baseline
**Intended lead:** platform lane

Compare a plain host tool window with supported AppWindow compact-overlay
mechanisms for focus, click-through, DPI/monitor movement, topmost behavior,
borderless games, teardown, and resource cost. Prove a declarative test surface
before YouTube playback. Do not add a community WebView, use credentials, or
claim compatibility without measured evidence.

## Blocked work

| Item | Blocker | Unblocking evidence |
| --- | --- | --- |
| DLV-025 atomic widget-size presentation | Current HWND render-target resize exposes undefined content during real list-heavy first paint; the assignment's documented stop condition forbids adopting new compositor/window technology without planner/user authority. | User chooses and authorizes a bounded compositor architecture milestone; planner updates DLV-025 scope and acceptance around that design before the preserved platform worktree resumes. |
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
