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
regressions adjacent to DLV-017, then returns to the advanced-widget lifecycle/
package path. Spotify list work
that depends on shared cursor/append foundations remains in the serialized
integration queue rather than being patched locally.

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

**State:** Assigned
**Baseline:** `b844fd8`, the accepted widgets-lane DLV-017 closing commit
**Owner:** Games & Apps managed presentation/state mutation, surface hints,
credential-free fixtures, and directly affected feature documentation

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

### DLV-023 — Keep transient Spotify failures on the last-good surface

**State:** Ready after DLV-024
**Baseline:** closing commit of DLV-024
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

### DLV-010 — Prove the external widget package journey

**State:** Ready after DLV-023
**Baseline:** closing commit of DLV-023
**Owner:** managed scaffold/CLI, sample package, and public authoring docs

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

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform`

The platform queue prioritizes newly reproduced presentation continuity and
shared geometry defects before broader accessibility/performance evidence.
Shared collection and dashboard-audio authority changes remain serialized in
the integration queue.

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

**State:** Assigned
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

### DLV-021 — Correct shared text and component geometry end to end

**State:** Ready after DLV-025
**Baseline:** closing commit of DLV-025
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

### DLV-019 — Add Audio Mixer dashboard master controls

**State:** Awaiting planner-selected serialized baseline after DLV-014
**Intended lead:** planner-selected bridge/broker plus widgets integration lane
**Dependencies:** DLV-014

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
