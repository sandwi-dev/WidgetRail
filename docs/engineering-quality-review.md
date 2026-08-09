# Engineering Quality Review

Status: first independent full audit; active findings require disposition<br>
Date: 2026-08-09<br>
Last reassessed: 2026-08-09 after scenario execution was removed from `gbar preview`, responsive focus reconciliation left the paint path, and public documentation was revised<br>
Scope: architecture, maintainability, correctness, security, performance,
verification credibility, UI/UX foundations, and product readiness

## Executive assessment

The quality trajectory is **improving, but the repository is not yet at the
standard of a cohesive senior platform team**.

There is substantial good engineering here: the installed-widget runtime uses
an explicit AppContainer and broker boundary; protocol and package inputs are
heavily bounded; managed builds treat warnings as errors; deterministic tests
cover many lifecycle and controller contracts; performance claims are
separated from targets; and the recent SDK work is replacing repeated task,
cancellation, paging, state, navigation, and command plumbing with explicit
public abstractions.

The strongest concrete improvement since the previous audit is that
`gbar preview` no longer executes scenario assemblies in-process. It validates
and lists bounded declarations without resolving the assembly, and selected
execution fails closed until a forcibly terminable isolated worker exists. The
`UI.NavigationShell`/SDK Gallery composition and YT Music operation-lane
migration remain the strongest authoring improvements in the broader worktree.

The highest remaining security risk in this area is the older DLL mode of
`gbar render`, which still executes a widget assembly inside the full-trust CLI
process. It is explicitly documented as trusted-only development tooling, and
the safer `gbar dev` path exists, but the production AppContainer boundary is
still bypassed. The current worktree resolves the prior responsive-focus
identity risk by separating focus persistence from action and source-element
routing. It also moves reconciliation out of steady paint and onto relevant
state transitions. The implementation agent reports that focused managed and
native Release tests, the OverlayHost Release build, and the repository-wide
Release verifier all pass on the current worktree; this review did not rerun
them or inspect a retained machine-readable result.

The YT Music operation-lane migration removes a substantial amount of manual
lifecycle machinery. The current worktree also closes its stale-failure gap:
success, retry status, and authorization mutations now share the
`WidgetOperationContext.IsCurrent` contract, including a final check serialized
with state mutation and same-lane admission.

The current repository also carries too much coordination and product policy in
a few very large translation units/classes, and its verification process does
not yet provide an automated, bounded, reproducible GitHub gate. The extensive
bug ledger has accumulated 55 simultaneously active `Verifying` entries,
including 22 P0s, which makes priority and release status difficult to trust.

## Changes since the previous audit

The implementation agent removed reflection, provider loading, timeout tasks,
snapshot output, and the scenario fixture assembly from `gbar preview`.
Manifest listing remains assembly-free; `--scenario` returns a fixed failure
before resolving the declared DLL; and the CLI help, authoring guide,
quickstart, documentation index, and focused tests now describe this fail-
closed boundary. This materially resolves the scenario half of EQ-001.

The responsive implementation now gives each shell destination a bounded
`focusPersistenceId` shared only by its compact and rail presentations.
Distinct destinations may continue sharing an action ID and use source element
identity for routing. EQ-003 through EQ-006 and the trusted DLL renderer portion
of EQ-001 remain open.

The documentation fixes now use a valid quickstart glyph, explain explicit
focus persistence correctly in SDK Gallery, and accurately enumerate additive
snapshot protocol versions 1 through 13. The remaining proof gap is systemic:
copyable C# examples are not compiled as external SDK consumers.

A deeper controller audit initially treated disabled destinations as
non-focusable. Source and focused tests show the opposite is the deliberate
platform contract: disabled and busy controls remain navigable so users can
inspect their label and unavailable state, while activation is suppressed.
EQ-009 records that correction and closes without a code change.

The YT Music migration now schedules transport reconciliation through one
runtime-owned Active Latest lane and removes its task set, cancellation source,
generation, lock, and continuation cleanup. Attempt-local success and failure
commits now check current operation identity under the same state lock used for
replacement admission. Cancellation-ignoring fake requests cover superseded
ordinary and authorization failures plus lifecycle-exit authorization failure.

## Verification snapshot

Implementation-reported bounded local Release results on the current worktree
are:

| Scope | Result |
| --- | ---: |
| Widget SDK | 84/84 passed |
| Gbar CLI | 47/47 passed |
| SDK Gallery | 6/6 passed |
| YT Music | 48/48 passed |
| Focus Navigation | 41 checks passed |
| Declarative Renderer | 4,632 checks passed |
| Widget bridge catalog | passed |
| OverlayHost Release target | built successfully |
| Full `scripts/Verify.ps1 -Configuration Release` | passed end to end in 279.8 seconds |

The implementation agent reports that the full verifier additionally exercised
the managed, protocol, capability,
documentation, packaging-conformance, native input/layout/rendering, hidden
OverlayHost smoke, and InputProbe paths. The repository still retains no
machine-readable per-run result or immutable CI link, and this review did not
independently execute the commands, so EQ-004 remains open.
This milestone did not verify a real controller, real YTMDesktop2 companion,
Spotify authentication, physical mixed-DPI display, representative visual
capture, or PresentMon/ETW trace. Available default package artifacts also
predate the updated YT Music and SDK Gallery source manifests and are not
current packaged evidence.

## Prioritized findings

### EQ-001 — P0 — The DLL renderer executes author code outside the production sandbox

**Status: Partially resolved. Scenario execution now fails closed; trusted DLL rendering remains.**

**Evidence.** `tools/GbarCli/RenderCommand.cs` loads a supplied DLL with
`AssemblyLoadContext`, constructs its widget, and calls `Render()` in the CLI
process. A collectible load context is an unload mechanism, not a security
boundary, and `RenderCommand` has no process-level timeout or forcible cleanup.
The CLI README, guide, and quickstart warn that DLL rendering is trusted-only
development tooling and recommend isolated `gbar dev` for integration.

`tools/GbarCli/ScenarioPreviewCommand.cs` no longer loads or invokes its
declared assembly. Listing validates metadata without resolution, and selected
execution throws a fixed unavailable diagnostic before any author code runs.
The replacement tests use a deliberately missing assembly to prove listing and
failure do not require it. This closes the new preview regression identified in
the first audit, subject to execution of the changed test suite.

**Why it matters.** The platform correctly teaches users that Community widgets
run in capability-free AppContainers. A developer following a GitHub-hosted
widget workflow can nevertheless execute repository code with full desktop
authority by using the convenient DLL-render path. That is both a security hazard
and an inaccurate development environment: code that accidentally depends on
ambient authority can pass preview and fail when packaged.

**Underlying problem.** The older semantic DLL renderer is coupled to
in-process managed reflection instead of the existing worker isolation
boundary. Documentation reduces accidental misuse but does not contain code.

**Recommended direction.** Keep snapshot-only rendering and scenario-manifest
listing as the only in-process data paths. Execute widget/scenario assemblies through a dedicated preview worker
using the same package-specific Low-integrity AppContainer, Job Object, bounded
IPC, stripped environment, and termination rules as `gbar dev`. If a temporary
trusted-local mode must remain, require an explicit trust-named option and a
prominent confirmation/warning in command help and every tutorial; do not use
it for downloaded repositories. Prefer removing the old DLL-render shortcut
once the isolated scenario path covers its use cases.

**Tradeoff.** Reusing the production worker increases startup and packaging
work, but it tests the actual authority model and gives timeout cancellation a
process boundary. A separate minimal preview worker can be faster, but then its
token, Job, load, IPC, and cleanup policy must not drift from production.

**Resolution evidence.** First run the changed fail-closed CLI tests and prove
no scenario assembly resolution or output occurs. For eventual execution, add an adversarial fixture that attempts environment,
arbitrary-file, network, child-process, and long-running work; prove the preview
process is capability-restricted, bounded, forcibly reclaimed, and leaves no
child process or locked package. Verify semantic output through authenticated
bounded IPC and update CLI/security/quickstart documentation.

### EQ-002 — P1 — Responsive focus identity required an explicit contract

**Status: Implemented in the current worktree; local verification is reported, retained independent evidence is pending.**

**Implementation.** Protocol v13 adds an optional bounded
`focusPersistenceId` only to focusable nodes. SDK focusable elements expose
`PersistFocusAs`, and `UI.NavigationShell` generates one stable key per logical
destination for its distinct compact and rail controls. The native snapshot
parser carries the field, while transition-owned reconciliation on `WM_SIZE`
and presentation/snapshot state changes matches only one explicitly opted-in
target in the same input scope, outside paint. Omitted keys,
shared action IDs, ambiguous keys, and cross-scope candidates fail closed.
`RenderResult.focusActionIds` and action-based focus inference were removed.

**Compatibility and rendering implications.** Snapshots without the field keep
their prior required protocol version and exact-ID/tree-order fallback.
Authoring the field negotiates v13. Responsive equivalence is resolved from the
immutable snapshot and logical surface mode before drawing, so the first frame
receives the corrected focus ID; geometry-only clipping recovery remains
post-layout. The field never participates in action dispatch.

Disabled and busy nodes remain valid focus candidates by platform contract;
their activation is unavailable, but focus is retained so the user can inspect
their label and state cue. EQ-009 records the source/test audit that corrected
the contrary assumption.

**Resolution evidence.** Public focus guidance now treats action routing and
focus persistence as separate contracts. The changed managed tests cover
distinct destinations sharing one action without sharing persistence,
compact/expanded preservation, v13 negotiation, and legacy omission. Native
tests cover ambiguous-key rejection and action-only non-equivalence. The
implementation agent reports 84/84 managed Widget SDK cases and 41 native
focus checks. It also reports that `WidgetBridgeCatalogTests` passes the native
v13 parser round trip, `DeclarativeRendererTests` passes 4,632 checks, the
OverlayHost Release target builds, and the repository-wide verifier passes.
Source inspection supports the intended contracts; retained independent run
evidence is still absent.

### EQ-003 — P1 — `OverlayApp` is a central ownership and change-risk hotspot

**Status: Open.**

**Evidence.** `src/OverlayHost/main.cpp` is approximately 4,000 lines. Its
single `OverlayApp` class owns development argument parsing/readiness, startup
and performance records, window messages, GameInput/XInput state, catalog
refresh, widget lifecycle, display/DPI updates, presentation transitions,
pointer/controller routing, focus memory, bridge snapshot reconciliation,
graphics resources, shell drawing, widget drawing, and shutdown. The current
responsive-focus change must edit this class even though the focus algorithm is
already in `FocusNavigation.cpp`. `DeclarativeRenderer.cpp` is another roughly
2,000-line planning/rendering unit.

**Why it matters.** The problem is not line count by itself; it is the number of
independent state machines sharing one mutable owner. Changes to input,
lifecycle, catalog, presentation, or rendering can invalidate assumptions in
another area and are difficult to test without the complete window. This raises
review cost, encourages more fields and helper methods in the same class, and
makes senior-level ownership boundaries hard to see.

**Underlying problem.** Useful algorithms have been extracted, but state
ownership and orchestration remain concentrated in the Win32 application
object.

**Recommended direction.** Extract one tested ownership seam at a time rather
than attempting a rewrite. Good first candidates are a widget-session/focus
controller (snapshot, active scope, focus memory, action dispatch), a display
environment coordinator, and a development-readiness session. `OverlayApp`
should translate Win32 events and compose these services. Each extracted object
must own its state and expose explicit commands/results, not borrow references
to most `OverlayApp` fields.

**Tradeoff.** Premature fragmentation can replace one large class with many
anemic wrappers. Extract only coherent state machines with independent tests
and avoid a generic event bus or service locator.

**Resolution evidence.** Document ownership, move one complete state machine
with no duplicate transitional state, add unit tests at the new seam, and show
that subsequent feature work no longer edits unrelated input/render/lifecycle
regions of `main.cpp`.

### EQ-004 — P1 — There is no automated, bounded repository quality gate

**Status: Open; verification claims expanded without adding a bounded retained-results gate.**

**Evidence.** The repository has strong `Directory.Build.props` defaults and a
large `scripts/Verify.ps1`, but no checked-in `.github/workflows` pipeline.
`Verify.ps1` invokes many `dotnet run`, native build, and process smoke commands
directly; `Invoke-Checked` validates exit codes but supplies no per-step or
overall timeout. The console-style test executables also have no standard test
runner timeout or structured result artifact. Documentation frequently cites a
green full Release gate, but the current uncommitted worktree has no immutable
CI run associated with it.

`docs/implementation-status.md` now upgrades several focused counts and states
that the current milestone passed the full Release verifier end to end. The
registered managed case totals are consistent with those counts, but there is
still no checked-in workflow, per-step timeout, structured result bundle, or
immutable run reference. The available default Community package artifacts are
older than the source manifests, so they cannot substantiate current packaged
behavior. This is not evidence that the local runs failed; it is evidence that
their result cannot be independently audited or reproduced from the repository.

**Why it matters.** A senior team needs reproducible evidence that does not
depend on one long local agent session. An indefinitely hung test prevents a
credible gate, and unstructured console output makes regression history and
individual flaky tests difficult to track. GitHub distribution without GitHub
validation also leaves contributors unable to prove that a change meets the
same standard.

**Underlying problem.** Verification breadth grew faster than verification
orchestration and evidence publication.

**Recommended direction.** Add a Windows GitHub Actions workflow with a
bounded managed lane and native lane. Give every command and the complete job a
timeout; upload machine-readable test/build logs and key package hashes. Keep
hardware, live-auth, real-controller, and physical-display checks as explicit
manual release gates rather than pretending hosted CI can cover them. Refactor
`Verify.ps1` so the same bounded command manifest drives local and CI runs.

**Tradeoff.** Migrating every custom executable test to a third-party framework
is not required immediately. A thin runner can add subprocess timeouts and
JUnit/TRX-compatible records first. Native and AppContainer tests may require
separate permissions or self-hosted evidence; isolate those rather than
dropping the entire gate.

**Resolution evidence.** A clean commit must produce a repeatable Windows CI
run with bounded durations, managed and native results, documentation-link
validation, deterministic package checks, and retained logs. A deliberately
hung fixture must be terminated and reported as a failed test without hanging
the job.

### EQ-010 — P1 — Superseded YT Music reconciliation could commit stale failure state

**Status: Implemented in the current worktree; focused Release verification is reported, packaged proof remains.**

**Implementation.** `RunTransportRefreshBurstAsync` now routes attempt-local
retry status and authorization failure through context-aware helpers. Each
helper checks `WidgetOperationContext.IsCurrent` while holding `_stateLock` and
performs its status, pending-state, and connection mutation within that same
critical section. `ScheduleTransportRefresh` serializes same-lane admission on
the same lock, preventing a replacement from becoming current between an old
attempt's final currency check and mutation. A current reconciliation 401 still
clears pending state and returns to pairing; it no longer cancels its own lane,
because the delegate exits immediately. The ordinary non-operation connect and
poll 401 path retains lane cancellation and credential invalidation behavior.

**Why it matters.** Rapid controller input and slow local companion responses
are normal operating conditions. A stale response must not overwrite newer
intent, clear unrelated optimistic state, or force the user back through
pairing. This is precisely the class of generation/cancellation bug the public
Latest abstraction is intended to eliminate, so leaving failure commits
outside its currency contract also teaches authors an unsafe reference pattern.

**Underlying problem.** Attempt currency previously guarded successful data
commits but was not part of the failure-state ownership model. The correction
makes currency part of every attempt-local commit boundary.

**Policy.** A transport-reconciliation response is attempt-local and is ignored
after supersession or Active-lifecycle exit. Current 401 responses still force
pairing. A future flow that replaces credentials while requests are in flight
should add a host-owned credential/session generation rather than broadening a
stale attempt's authority.

**Tradeoff.** Ignoring every stale authorization response can temporarily leave
an invalid credential until the current request confirms failure. Treating
every response as globally authoritative can disconnect a newly paired or
otherwise newer session. Generation-bound invalidation is more explicit but
requires a small capability/client contract rather than a widget-local boolean.

**Resolution evidence.** The implementation agent reports that Release
`YtMusicWidget.Tests` passes 48/48 in 9.9 seconds. Source inspection confirms
that deterministic fakes deliberately ignore cancellation, admit a newer
transport command, and then fail the older request with ordinary and
authorization exceptions. Assertions prove stale failure cannot publish retry
status, clear the replacement's pending projection, disconnect, or cancel the
replacement, and that the newer result commits. A separate lifecycle-exit test
delays 401 until Active cancellation is observed and proves it cannot
disconnect. Existing current connect and poll 401 tests still prove credential
invalidation and pairing behavior. Packaged rapid-input and real-companion
failure evidence remains a release-validation follow-up, not a blocker to this
code-level finding.

### EQ-005 — P2 — The bug ledger no longer communicates release priority

**Status: Open; the latest ledger edit demonstrates an intake gap.**

**Evidence.** `docs/known-issues.md` currently lists 55 active issues; all are
`Verifying`, and 22 are P0. Several P0 summaries describe implemented SDK
helpers or sample migrations awaiting packaged evidence rather than an active
security, data-loss, or product-blocking defect. The file is approximately
1,680 lines and combines original symptoms, implementation narratives,
acceptance plans, repeated focused counts, and manual evidence debt.

The current ledger diff updates GBA-055 from YT Music 0.2.5/45 tests to
0.2.6/46 tests, but the focused suite now contains 48 cases after the stale-
failure correction. More generally, the ledger still accepts repeated green-
count prose without separating implementation state from packaged/manual
evidence, so it cannot serve as a concise source of release truth.

**Why it matters.** When 40% of the active ledger is P0 and every item has the
same status, neither a developer nor the user can tell what should stop a
release, what should be tested next, or what is simply missing evidence. It
also encourages the implementation agent to add prose and another feature
instead of closing a bounded set of verified defects.

**Underlying problem.** Defect severity, implementation state, release-gate
evidence, and roadmap delivery are represented as one flat issue list.

**Recommended direction.** Define severity separately from evidence state.
Reserve P0 for an actively exploitable security/data-loss issue or a core path
that cannot ship; P1 for release-blocking correctness/architecture; P2/P3 for
important and minor work. Move manual packaged/controller/display/auth checks
to a release-evidence matrix keyed to a smaller issue, and move generic SDK
delivery to the roadmap. Keep the active table short; archive closed narratives
with commit/evidence links.

**Tradeoff.** Reclassification must not hide real unverified behavior. Preserve
the acceptance criteria and history, but stop using severity as a proxy for
"important work the agent recently performed."

**Resolution evidence.** Publish severity definitions, identify the true
release blockers, give each active item one owner and next evidence action, and
show that the dashboard can answer: "what blocks the next build?" without
reading 1,600 lines. Record EQ-010's correction and fresh evidence while
keeping the contract-audit closure of EQ-009 out of the defect count.

### EQ-006 — P2 — Advanced widgets remain application-sized monoliths

**Status: Partially improving through helper adoption; structural ownership remains open.**

**Evidence.** `samples/YtMusicWidget/YtMusicWidget.cs` is about 1,136 lines,
`samples/SpotifyWidget/SpotifyWidget.cs` about 2,023,
`src/FirstPartyWidgets/NetworkControlsWidget/NetworkControlsWidget.cs` about
2,020, and `AudioMixerWidget.cs` about 2,496. The YT Music file still combines
rendering, connection state, polling, progress projection, optimistic command
reconciliation, lifecycle, action routing, and much of its domain model. The
new operation-lane migration removes one task/cancellation family, but not the
responsibility concentration; the EQ-010 correction also leaves attempt-local
failure ownership embedded in the same class.

**Why it matters.** These are the examples external developers will copy.
Framework helpers improve correctness, but a human still has to understand a
large cross-cutting class to add a page, state, or action safely. Large files
also hide whether remaining complexity is domain behavior or duplicated
framework plumbing.

**Underlying problem.** Coordination abstractions are being introduced, but
production migrations have mostly been local substitutions rather than a clear
application structure with state, controller, routes, and view composition.

**Recommended direction.** Choose one advanced widget, preferably YT Music
before Spotify, and establish a reference structure: immutable state/model,
provider/controller adapter, lifecycle coordinator, action/command mapping, and
pure view compositions. Split only along ownership and test seams; do not make
one file per small method. Use the result to revise the media/multipage
template, then migrate Spotify by responsibility.

**Tradeoff.** File splitting alone is churn and can make navigation worse.
Require each extracted type to reduce shared mutable state or enable focused
tests. Avoid a universal MVVM/base-class framework.

**Resolution evidence.** A new developer should be able to locate and change
one route, one provider action, or one visual state without reading the entire
widget. Track render/domain/coordination lines, author-owned tasks and locks,
explicit invalidations, and cross-file mutable dependencies before and after.

### EQ-007 — P2 — Copyable documentation examples are not API-checked

**Status: Partially improved; known prose/API drift is corrected, but executable proof remains open.**

**Evidence.** The quickstart now uses the valid `WidgetGlyph.Refresh`, SDK
Gallery distinguishes protocol-v13 focus persistence from action routing, and
the authoring guide now consistently describes additive snapshot versions 1
through 13. `tests/Documentation.Tests/Program.cs` still checks relative links
and selected headings/phrases without compiling fenced C# examples or
validating referenced public symbols. The NavigationShell and starter API
examples are presented as copyable entry points, so link-only validation
remains insufficient.

**Why it matters.** Documentation is the primary SDK interface for a new widget
author. A first example that fails to compile makes the framework look
unfinished, sends developers into source inspection, and encourages invented
workarounds. It also allows API changes to silently invalidate many guides even
while the documentation suite remains green.

**Underlying problem.** Documentation contracts are checked as prose structure,
not as executable consumers of the public SDK.

**Recommended direction.** Keep important snippets in small compile-only sample
projects or extract marked fenced blocks into generated temporary projects that
reference the same public assemblies/package as an external widget. At minimum,
add symbol-contract checks for closed enums and command examples. Prefer one
canonical snippet source included by guides and tests over duplicated examples.

**Tradeoff.** Compiling every partial fragment requires cumbersome context and
can overconstrain explanatory prose. Designate only copyable/end-to-end blocks
as executable and leave illustrative fragments explicitly marked. A symbol
regex alone is cheaper but cannot catch overload, namespace, or lifecycle API
drift.

**Resolution evidence.** Compile the NavigationShell and canonical starter
examples against the public SDK in the documentation gate, and add a negative
regression proving an unknown enum member fails that gate. The corrected glyph,
protocol matrix, and focus-persistence wording are meaningful improvements, but
prose inspection alone is not closure evidence.

### EQ-008 — P2 — Focus persistence was scheduled from the steady paint path

**Status: Architecturally resolved in the current worktree; targeted performance and scheduling verification remain.**

**Evidence.** `OverlayApp::DrawWidget` no longer calls the resolver. The new
`ReconcileResponsiveFocusPersistence` path is invoked on `WM_SIZE`, snapshot or
presentation refresh, and focus restoration. Stable paints and animations no
longer traverse the immutable snapshot for this behavior. A reconciliation
still performs one O(nodes) traversal and constructs a candidate vector before
it knows whether the preferred node is visible or has a persistence key, but
that work is now tied to bounded state transitions instead of frame cadence.

**Why it matters.** The important product requirement is no continuing work
after settlement. Moving the scan out of paint removes the frame-cadence
multiplier and is the correct architectural fix. The residual transition cost
is unlikely to matter for bounded snapshots, but scheduling edges still need
proof so DPI, interface-scale, surface, and snapshot changes cannot miss a
required reconciliation.

**Underlying problem.** The scheduling problem is fixed. The resolver still
combines preferred-node discovery and candidate collection in one
allocation-bearing traversal, and the orchestration is embedded in
`OverlayApp` rather than exposed through a directly testable transition seam.

**Recommended direction.** Preserve the transition-owned scheduling. Add a
small orchestration test that enumerates resize, snapshot, presentation,
focus-restoration, DPI, and interface-scale changes and proves exactly when
reconciliation is required. Only optimize the resolver to a two-pass,
zero-allocation search if measurement shows the bounded transition cost is
material; do not reintroduce paint-time work or a fragile cache.

**Tradeoff.** Transition-owned scheduling adds invalidation edges that must stay
aligned with the renderer's responsive inputs. A two-pass zero-allocation
resolver would reduce each transition's work without adding cache state, but
the current bounded cost may already be negligible. Optimize from a focused
measurement rather than adding a cache whose invalidation is harder to prove
than the saved work.

**Resolution evidence.** A seam or counter should prove stable paints perform
no persistence scan while real compact/expanded and snapshot transitions still
preserve focus. Run the changed native tests and measure a worst-case bounded
snapshot during settled and transition frames. If transition allocation is
below budget, record that result and close this finding rather than optimizing
speculatively.

### EQ-009 — Disposition — Disabled navigation destinations remain focusable by contract

**Status: Not a defect; closed by source and focused-test audit.**

**Evidence.** The original finding assumed `isDisabled` and `isBusy` meant a
control was absent from controller navigation. The host deliberately separates
focus/navigation availability from activation availability. Declarative render
planning records focusable buttons, sliders, and action surfaces as navigable
even when disabled or busy; the hit-region enabled flag suppresses pointer and
activation behavior rather than semantic focus. Native renderer and surface-
focus tests explicitly retain exact focus on disabled and busy controls, and
controller-navigation tests keep directional traversal independent of
activation state.

`NavigationShell` therefore correctly keeps every destination in its explicit
wrap ring. A disabled destination remains reachable so its accessibility label,
unavailable cue, and explanatory content can be inspected. Its action cannot be
invoked. The same logical destination remains a valid responsive persistence
target, avoiding a resize-induced focus jump merely because its command is
temporarily unavailable.

**Disposition.** No code change is required. Filtering disabled or busy nodes
from the ring or persistence resolver would contradict the platform contract,
change focus ordering dynamically, and cause focus teleportation. Public docs
continue to state that disabled and busy controls remain focusable while
activation is suppressed. A physical-controller smoke remains useful release
evidence, but it is not evidence of a missing enabled-ring implementation.

## Product-readiness assessment

| Area | Current assessment | Principal remaining evidence |
| --- | --- | --- |
| Installed-widget isolation | Strong architecture, substantial automated evidence | Clean packaged abuse/run evidence and public distribution trust model |
| SDK lifecycle/coordination | Latest-wins currency now covers YT Music success and failure commits | Broader advanced-widget adoption and packaged churn evidence |
| Responsive/controller UI | Explicit focus identity and transition-owned reconciliation are implemented and focused tests pass | Scheduling-seam proof, real controller, and viewport matrix |
| YT Music | Active Latest migration removes manual lifetime machinery and rejects stale failure commits | Real companion, packaged lifecycle/controller, and visual evidence |
| Spotify | Capable but still highly complex | Credential-free full-state visuals, live auth/playback gates, structural migration |
| CLI author workflow | Broad command surface; scenario discovery now fails closed | Isolated scenario execution, native/interactive preview, and automated clean CI |
| Performance | Honest targets and one local baseline | Clean immutable run, GPU/ETW evidence, current-build regression gate |
| Documentation | Extensive and now internally current, but copyable examples are not executable evidence | Compile-test canonical snippets and reduce ledger/status duplication |

## Recommended next three actions

1. **Preserve the new fail-closed `gbar preview` boundary and finish EQ-001.**
   Move or retire trusted DLL rendering so all author-code execution uses a
   process/AppContainer boundary.
2. **Add a bounded retained-results gate.** Turn the broad local verifier into
   reproducible Windows CI with per-step/overall timeouts and retained managed,
   native, documentation, and package evidence.
3. **Reduce the `OverlayApp` ownership hotspot.** Extract one cohesive,
   independently tested widget-session/focus state machine without duplicating
   transitional state or introducing a generic event bus.

The next review should first reassess these three items, then rotate into the
package/install/update trust model and advanced-widget ownership seams.
