# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-09
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

## Widgets lane

Task identity: `widgets`
Branch: `codex/impl-widgets`

### DLV-007 — Make Spotify presentation state coherent

**State:** Assigned after task migration
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

**State:** Ready after DLV-007
**Baseline:** closing commit of DLV-007
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

### DLV-009 — Consolidate YT Music lifecycle and render ownership

**State:** Ready after DLV-008
**Baseline:** closing commit of DLV-008
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

### DLV-010 — Prove the external widget package journey

**State:** Ready after DLV-009
**Baseline:** closing commit of DLV-009
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

### DLV-003 — Correct shared button-content geometry

**State:** Assigned after task creation
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

**State:** Ready after DLV-003
**Baseline:** closing commit of DLV-003
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

**State:** Ready after DLV-005
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

### DLV-015 — Add deterministic real-host accessibility proof

**State:** Ready after DLV-014
**Baseline:** closing commit of DLV-014
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

### DLV-004 — Repair the Games & Apps product surface

**State:** Awaiting integration of accepted DLV-003 into `main`
**Intended lead:** widgets lane
**Dependencies:** DLV-002 and DLV-003

Use the corrected shared geometry to make Library, empty, discovery/loading/
error, and Add applications one coherent responsive controller surface. Cover
empty, short, long-name, maximum bounded page, loading, error,
compact/standard/wide, 100-150% scale, mutation while focused, clipping, safe
areas, and reachability. This is the next exact-commit Tier-3 checkpoint.

### DLV-006 — Prove virtualized game-library collection foundations

**State:** Awaiting DLV-004 and DLV-005 acceptance
**Intended lead:** planner-selected serialized protocol lane
**Dependencies:** DLV-004 and DLV-005

Add the minimum cursor/append virtualized collection, lazy artwork handles,
stable keyed focus, bounded retention, and 2,000/10,000-item production-style
fake needed by the future Game Launcher. Public semantics and native adoption
must land as one serialized design. This is an exact-commit Tier-3 checkpoint.

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
