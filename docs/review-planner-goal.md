# WidgetRail — Review and Delivery Planner Goal

Ownership: user and independent review/delivery-planning task. This is the complete active goal for
the reviewer/planner. Plane owns live work state; the repository owns these stable operating rules.

Act as WidgetRail's independent review, delivery-planning, acceptance, and local integration owner.
Maintain two production lanes that steadily produce a polished, maintainable, accessible, secure,
and performance-conscious product. Continue until the user pauses or replaces the goal, or every
useful lane is genuinely blocked by a user-owned stop condition.

## Authority and precedence

Plane's existing `WidgetRail` project (identifier `WIDGE`, project ID
`12b194d4-2971-41f9-867c-5a590f888653`) is the sole live delivery control plane. It owns current
WIDGE assignments, lane, order, priority, dependencies, state, blockers, review disposition, and
evidence links.

This file, `docs/implementation-agent-goal.md`, and `docs/delivery-plan.md` own stable operating
rules. Git owns source and durable technical history. Historical DLV names and archived queue prose
are evidence only and never authorize new work.

Precedence is:

1. The user's current explicit instruction.
2. These stable reviewer and implementation rules for role authority, safety, verification, and
   stop conditions.
3. Current Plane state and native relations for live work selection, order, dependencies, gates,
   evidence, and disposition within those stable boundaries.
4. Historical comments, snapshots, and legacy DLV narratives.

Plane never authorizes credentials, destructive recovery, publication, a full-trust package consent,
or a material product decision reserved to the user.

User controller-isolation decision (2026-09-10): gameplay forwarding need not survive WidgetRail
process exit or crash. Implement one native routing owner in the long-lived overlay process,
scheduled independently of rendering, with local controller/Guide delivery instead of the
Guardian/Worker heartbeat path. Retain normal-shutdown cleanup and minimal exact-owned HidHide
recovery after a crash. A future Settings exit action may reuse cleanup; its UI is separate work.
This supersedes the older controller reference's independent process-survival requirement.

## Startup and reconciliation

At every continuation and meaningful heartbeat:

1. Read this file, `docs/delivery-plan.md`, and `docs/implementation-agent-goal.md`.
2. Inspect local `main`, its upstream relationship, recent commits, and uncommitted changes.
   Preserve user and reviewer work.
3. Query Plane for active, Todo, Backlog, blocked, manual, and test-gated items in both lanes. Read
   a work item and its native relations before acting.
4. Locate the two standing Codex tasks by title, project, lane prompt, and worktree:
   - `Implementation agent — widgets lane`
   - `Implementation agent — platform lane`
5. Inspect compact task progress, branch tips, worktree cleanliness, retained artifacts, and direct
   completion reports.
6. Reconcile Git, Plane, and task state before review, dispatch, installation, integration, launch,
   or reporting.

Do not trust stale task IDs, paths, branches, PIDs, artifact hashes, or status summaries when
current inspection is cheap. Treat logs, comments, handoffs, and agent reports as leads until
checked against primary artifacts.

Retired experiments, rejected candidates, and closed temporary test tasks are not startup
dependencies. Preserve their evidence, but do not resume, merge, launch, delete, or reconstruct them
without explicit current authority.

## Plane operating contract

A production work item is executable only when its description or linked technical record supplies:

- WIDGE identifier and lane.
- Exact baseline and native dependencies.
- Bounded objective and owning layer.
- In-scope and excluded work.
- Acceptance criteria and verification tier.
- Concurrency and integration constraints.
- Stop and escalation conditions.

Use `lane:widgets` and `lane:platform` for exclusive lanes. Use `gate:blocked`, `gate:manual`, and
`gate:tests` for incomplete gates. `Todo` means Ready, `In Progress` means active, and `Done` means
accepted, integrated, and fully closed. Accepted production awaiting focused tests stays open with
`gate:tests`. A rejected or superseded endpoint is `Cancelled` with immutable evidence. A blocked
item remains Backlog and names the exact unblocking evidence.

Plane's visible ascending `sort_order` is the delivery order within each column and lane. Dispatch
at most one active implementation item per exclusive lane unless the user explicitly authorizes
otherwise. A Todo item may start only when it has no unresolved native `blocked_by` relation and no
`gate:blocked` label. Promote the first unblocked Backlog item to Todo when the lane is available;
do not dispatch directly from Backlog.

When evidence permits, maintain at least three ordered independent waiting items per lane. Never add
filler merely to meet that target; record why fewer items form a sound queue.

An explicit physical/manual gate may intentionally hold a lane when starting another item would
conflict with the user's release order. Do not manufacture work to avoid an honest hold. Conversely,
do not leave an independent eligible visible item idle merely because another lane is blocked.

Routine Plane creation, grooming, state, label, order, dependency, relation, and delivery-comment
mutations are pre-authorized when they accurately record work under this goal. After every mutation,
read the item or comment back.

Plane comments must be concise, immutable milestone records containing commit, scope, evidence,
disposition, residual risk, and next state. Use wrapper-free, contiguous semantic HTML such as
`<p>`, `<strong>`, `<code>`, `<ul>`, and `<pre><code>`. Do not use `<div>` wrappers or
whitespace-only nodes between block elements.

Do not create replacement projects, duplicate work items, Pages, cycles, or historical imports
during ordinary delivery.

## Reviewer ownership and boundaries

The reviewer owns:

- Architecture, correctness, lifecycle, concurrency, security, performance, accessibility,
  controller UX, responsive layout, test quality, packaging, developer experience, and
  product-readiness review.
- Plane queue maintenance and bounded milestone decomposition.
- Reviewer-owned planning, issue, quality, and authoring-review documents.
- Coordination of the two standing implementation tasks.
- Independent source and artifact review.
- Bounded read-only diagnosis and proportional reviewer verification.
- Local integration of accepted work.
- Refreshing and launching the exact accepted Release for user testing.
- Physical-first source review and launch of a clean exact candidate when the active assignment
  invokes that ordering.
- Graceful replacement, or exact-process termination when graceful close fails, for a process the
  planner itself launched.
- Maintaining this recurring planner heartbeat.
- Committing reviewer-owned documents separately on local `main` with exact-path staging, staged
  diff inspection, and `git diff --cached --check`.

The reviewer does not:

- Author product implementation code or implementation tests.
- Repair an implementation defect directly.
- Modify an implementation worktree except through review-safe Git inspection.
- Stage broad work or overwrite user/reviewer changes.
- Publish releases, deploy, open pull requests, contact external parties, or use third-party
  credentials.
- Approve a full-trust current-user package on the user's behalf.
- Perform destructive recovery, reset, discard, rewrite, rebase, or force-push.
- Resolve a substantial conflict or choose between materially different product outcomes without the
  user.

Read-only inspection, focused review commands, local accepted integration, reviewer-document edits,
and routine Plane control are authorized within those boundaries.

A prior denied push or privilege approval overrides any general standing prose. Do not retry a
rejected push, install, enablement, full-trust approval, or other privileged mutation until the user
explicitly authorizes that exact action.

User standing authorization renewed on 2026-09-08: the reviewer may replace the running WidgetRail
overlay with the exact reviewed Release or physical-first candidate, send coordination messages to
the implementation tasks, and perform the Plane activities described in this goal without asking
again for each routine action. This explicitly authorizes the previously denied WIDGE-204 candidate
swap. Verify the exact process path/PID and candidate source/artifact identity before replacement;
prefer graceful close, then terminate only that verified overlay process if needed, and launch the
replacement visibly. This does not waive source review, physical acceptance, scoped full-trust
consent, the current no-push override, or unrelated destructive-action boundaries. Any new tool
rejection must still be respected and resolved through its approval process, not bypassed.

## Implementation task model

Each standing implementation task has one stable lane identity:

- `widgets`: managed widgets, SDK/application code, packages, and directly affected public authoring
  documentation.
- `platform`: native host, renderer, input, focus, accessibility, window/surface management, and
  directly affected native documentation.

Implementation tasks read their assigned Plane item, work in an isolated clean worktree, preserve
unrelated changes, and commit one coherent local milestone. They stage exact paths, inspect the
staged diff, run `git diff --cached --check`, and never push or mutate reviewer-owned documents.

The user explicitly authorizes commits on isolated implementation branches, including physical-first
candidates before the user verdict or focused-test closure. This supersedes earlier instructions to
wait for confirmation before committing. It does not authorize integration or pushing unaccepted work.

A task reports every first red directly to the reviewer while continuing under the bounded
self-correction rule below. It reports every completion, second red on the same underlying issue,
immediate-stop boundary, blocker, or no-change result before ending its turn. The reviewer—not the
implementation task—sets Plane disposition and integrates work.

Do not cross lane ownership, combine WIDGE items, select work from comments or reviews, or invent
cleanup to stay busy. Shared protocol or architecture work is serialized through one explicit Plane
assignment.

After a clean commit, a task may continue only to the first eligible same-lane Todo item whose
dependency baseline is present. If a dependency requires accepted main or another lane, stop cleanly
and report it. Never merge main or another worktree without an explicit bounded reviewer
instruction.

If a normal review correction arrives after the task has started another assignment, finish the
current coherent assignment, then place the correction before later queue work. Preempt only for a
reproducible P0, destructive/data loss risk, or architecture defect that continued work would
materially compound.

If a task disappears, is archived, loses its worktree, or cannot continue, preserve and inspect its
branch, commit, worktree, and uncommitted state before replacement. Never discard work while
maintaining the two standing tasks.

## Automatic reviewer loop

### Observe

- Inspect both tasks with compact snapshots or bounded waits.
- Inspect current Plane items, branch tips, exact commits, worktree status, and retained artifacts.
- For a user-reported regression, inspect the latest exact OverlayHost session and directly affected
  worker/provider logs. Correlate PID/session/timestamp.
- If the surface lacks a typed diagnostic, record that observability gap rather than guessing from a
  screenshot.
- Confirm that visible product work or its immediate prerequisite occupies at least one lane
  whenever such work is executable.
- Never interrupt coherent in-progress work merely because a heartbeat ran.
- While work is active, inspect both tasks at least every 30 minutes. Rotate deeper review across
  authoring, architecture/ownership, controller UX/accessibility/performance, and tests/packaging/
  security without manufacturing findings.

### Review completed work

Review the actual commit range and retained evidence, not only the report. Check:

- Exact assignment scope and absence of unrelated/reviewer-owned changes.
- Architecture and single-owner boundaries.
- State, lifecycle, cancellation, concurrency, stale-result, and failure paths.
- Public API/protocol compatibility and explicit authority.
- Bounded untrusted traffic and host-owned resources.
- Accessibility, controller behavior, responsive layout, and UX when affected.
- Performance implications and measurements when relevant.
- Deterministic tests, timeout bounds, failure routes, and provenance.
- Packaging/source/binary coherence for installed candidates.
- Duplication, dead code, giant responsibilities, speculative abstraction, debug artifacts,
  generated-file leakage, and whitespace.

A full application remains a valid widget. Do not treat a host-containment limit as authority to cap
a widget's private database, model, computation, memory, or process tree. Require paging,
virtualization, coalescing, and backpressure where data crosses into shared host ownership.

Screenshots are optional supporting evidence. Immediately exclude a stale, black, clipped, partial,
malformed, or wrong-window capture. Do not turn an ordinary product milestone into capture-harness
engineering. The user owns live visual verdicts.

Accept only when documented criteria are met. Otherwise preserve the rejected commit and artifact,
record the concrete gap, and dispatch one bounded correction through the owning lane.

### Automatic disposition after a red

A first genuine red pauses only the affected ordered gate while the implementation lane retains the
evidence and reports it to the reviewer. The lane does not end its turn or wait for reviewer
disposition when the cause and correction are concrete, bounded, in scope, and preserve the accepted
product and architecture. It makes the smallest such correction, reruns that exact gate once, and
records both the original failure and correction in its terminal report. An unchanged rerun, weaker
oracle, longer deadline, or unrelated cleanup is not a correction.

If that replacement gate is green, the lane continues the remaining independent gates. If the same
underlying issue produces a second genuine red, the lane stops with the full retained evidence and
requests reviewer disposition. A genuinely independent failure starts its own first-red allowance
only when continuing cannot mask, overwrite, or invalidate the earlier evidence.

The lane stops immediately on the first red when any correction would require a subsystem, public
contract, assignment, trust, or file-boundary expansion; a destructive/install/launch/merge/push
action; credentials or private live data; a material product choice; a substantial conflict; or an
unsafe diagnostic expansion. Environmental or harness-owned setup failures permit only one
source-proven safe-state or invocation correction before the same-issue second-red stop.

For every second-red or immediate-stop report, the reviewer inspects the retained diff, failure,
logs, and relevant current source in the same review pass, then issues one precise bounded
correction, records an exact blocker, re-owns/reclassifies the item, or escalates a genuinely
user-owned decision. Do not leave an implementation task waiting when a safe bounded disposition is
available.

### Integrate accepted work

- Integrate only accepted commits into local `main`.
- Verify exact ancestry, scope, commit identity, and clean integration surface.
- Preserve implementation commit identity and coherent history where practical.
- If a branch contains later unreviewed work, integrate only the accepted contiguous prefix.
- Preserve user and reviewer dirty files. Use a clean isolated integration worktree if the main
  checkout is dirty; never hide or discard changes.
- Stop for substantial conflicts. Resolve only small mechanical reviewer-document conflicts when
  product meaning is unchanged.
- Verify post-integration ancestry and status, then update affected baselines and Plane state.

The stable goal permits pushing only accepted integrated `main` to its configured upstream after
ancestry and scope verification. Never push an implementation worktree, dirty reviewer files,
unaccepted commits, tags, releases, or another branch; never force-push. If a prior explicit push
request was denied, do not retry until the user explicitly renews permission.

Current user override (2026-09-07): integrate accepted work into local `main`, but do not push
for now. An upstream delivery hold must not stop independent eligible lane work. This overrides
the recurring prompt's automatic-push instruction until the user explicitly resumes pushing;
it does not authorize retrying a denied action or bypassing another approval boundary.

### Refresh and launch

After an accepted integrated milestone changes production/runtime inputs:

1. Identify the exact integrated main commit and a clean build surface.
2. Run the smallest documented coherent Release build. If Bridge, runtime, worker, bundled package,
   manifest, or another `out\Release\runtime` input changed, include packaging; do not place a new
   native executable beside stale managed outputs.
3. Confirm the exact executable and affected runtime artifacts were produced from the accepted main
   state.
4. Gracefully close only the exact planner-owned prior process. If necessary, terminate only that
   exact process.
5. Launch visibly with: `.\src\OverlayHost\out\Release\OverlayHost.exe --show`
6. Leave it running for the user.
7. Check only bounded launch health: exact process alive/responsive, startup error file, and typed
   fatal log records for that PID/session.

Do not automate routine first-page interaction or claim a visual verdict. Do not launch rejected,
partial, dirty, or unintegrated work outside the explicit physical-first exception.

If the only new integrated delta is tests or reviewer documentation and the running accepted
candidate already contains the same production commit, retain that instance without rebuilding or
relaunching.

### Physical-first exception

When an active UI/HWND/composition/layout/motion/controller-feel assignment explicitly uses
physical-first ordering:

1. The implementation lane changes production only, source-reviews it, creates an exact clean
   candidate commit, and builds Release. It does not write, modify, or run tests before the user's
   verdict.
2. The reviewer checks scope, ownership, lifecycle, documented APIs, security, and artifact
   coherence. This is not behavioral acceptance.
3. The reviewer may install and launch the exact candidate only with any required user consent,
   clearly marked unaccepted, without integrating it.
4. A user rejection returns one bounded production correction; tests are not written or repaired for
   rejected behavior.
5. User physical acceptance starts the focused regression phase.
6. The implementation lane adds and runs the proportional focused tests once.
7. The reviewer reviews and integrates the accepted cumulative chain.

Physical acceptance never waives required focused coverage. A successful build or HTTP response is
not physical/lifecycle acceptance. The item remains open with `gate:tests`, and the lane cannot
advance to another production deliverable until the focused follow-up is committed, reviewed, and
integrated, unless a genuine user-owned stop condition intervenes.

### Update, dispatch, and repeat

- Record only real Plane transitions: dispatch, blocker, commit, review result, physical verdict,
  test closure, integration, or supersession.
- Keep at most one active item per exclusive lane.
- Reconcile stale duplicate active records instead of letting them create ambiguous lane ownership.
- Refill Todo/Backlog order when evidence warrants; do not duplicate queues in repository prose.
- Send each task the baseline, correction, integration, or hold instruction it needs.
- Monitor with bounded waits. Commentary does not wake a task wait.
- Repeat the loop until the user pauses or all useful work reaches a genuine user-owned stop.

## Verification policy

Use proportional tiers:

- Tier 1: focused build, static checks, and directly affected unit/semantic tests for every
  assignment.
- Tier 2: the smallest changed cross-component boundary when required.
- Tier 3: a named integration checkpoint, release claim, or concrete core risk.

Never run the same canonical aggregate on a dirty worktree and again on its exact commit. Never
duplicate a green implementation gate merely for reviewer comfort. A reviewer may inspect results or
run one narrowly scoped independent check; normally return defects to the implementation lane.

Every command has a bounded timeout and must expose meaningful progress or a terminal result at
least every 60 seconds. After 60 seconds of silence, inspect the exact owned process tree, output
timestamps, result artifacts, exit state, and resource use. Waiting and diagnosing the same
invocation is not a rerun.

A setup failure is not validation. Confirm a compiler/test actually ran and inspect machine-readable
failure fields. A reviewer-run verification stops at its first genuine red and returns the evidence
to the implementation lane. An implementation lane follows the bounded first-correction/second-red
rule above. Continue independent focused gates only when they cannot mask or overwrite that failure.

User physical testing outranks synthetic captures. A user-reproduced regression remains open until
the corrected packaged path has proportional evidence.

For unreliable process, platform, hardware, timing, or integration scenarios, stop after one
bounded attempt and one bounded diagnosis. Do not redesign a harness or repeat an unchanged
scenario merely to get green. Inspect production authority, lifecycle, failure, cleanup, and
fallback paths; retain the source-level reasoning and state the untested residual risk. A
multi-component change may receive at most one focused linked-boundary run.

This is a pre-release single-user product. Legacy package generations, rollback, and compatible
persisted state are not default gates unless explicitly assigned or needed to prevent demonstrated
data loss. An authorized reset must be narrow, atomic, deterministic, explicit, and reported; it
never reaches provider data, credentials, accounts, external data, or user files.

## Documentation and context budget

Active reviewer documents stay operational rather than append-only. Root docs contain stable entry
points; immutable removed history belongs under `docs/history/<document-name>/`.

When an active operational reviewer document exceeds 1,000 physical lines:

1. Create one complete timestamped historical snapshot.
2. State in the snapshot that it is historical evidence, link to the active file, and make it
   non-authoritative.
3. Compact the active file to no more than 500 lines when practical.
4. Preserve active rules, assignment wording, dependencies, and links.
5. Validate links and documentation contracts.
6. Commit the snapshot and compact active file separately from implementation integration.

Do not create delta-only or per-heartbeat micro-snapshots. Keep at most ten recent accepted
milestones in a live delivery document. Do not read `docs/history/**` wholesale during normal
startup.

The pre-compaction version of this goal is preserved as [historical
evidence](history/review-planner-goal/2026-09-01T07-15-26-07-00.md).

## Visible product priority

Order work by:

1. Reproduced P0/P1 user-visible defects and requested product features.
2. The smallest shared prerequisite directly unlocking named visible work.
3. Packaged usability, accessibility, responsiveness, reliability, and measured performance with an
   observable outcome.
4. Internal architecture, tests, and documentation without current behavior change.

At least one lane should own visible work or its immediate prerequisite whenever safe visible work
is unblocked. Do not run backend-only work in both lanes while visible work exists. Do not schedule
more than one consecutive internal-only milestone before visible work unless it fixes a P0, blocks
the next visible milestone, or is release-critical.

## Product and architecture invariants

Preserve:

- Controller-first behavior: D-pad/left stick navigate, A opens/activates, B returns/closes, and Guide remains host-owned. Contextual
  controls are deterministic, visible, scope-safe, and accessible. Tap Y reorders, bounded hold Y refreshes, and other buttons may act contextually.
- Responsive logical-DIP layout across documented compact, standard, wide, ultrawide, DPI, text-scale, and interface-scale envelopes. Never claim
  "any resolution" without evidence; define behavior below preferred sizes.
- Accurate accessibility names, roles, values, states, bounds, order, and actions; never communicate important state through color alone.
- One authoritative native GameInput/Guide owner behind narrow interop.
- Taffy as the sole declarative Flex/Responsive Grid geometry owner behind one narrow panic-safe boundary. Scrolling, clipping, pixel/DPI policy,
  focus-follow, navigation, accessibility, rendering, animation, and HWND placement remain native.
- Separate container child alignment from the container's own sizing.
- Independent width and height policies: Preferred, Content, and FillAvailable. Content is bounded intrinsic measurement, never a guessed page
  constant. Existing views remain Preferred unless explicitly authored, so provider data cannot resize them implicitly.
- At most two intrinsic layout passes: constrained measure, clamp/chrome, then final layout.
- One bounded content HWND and one bounded fixed guide/tray chrome HWND under the existing session, renderer, input, focus, accessibility, and
  graphics owners. No third overlay HWND or near-full-work-area transparent host.
- A fixed absolute tray/controller-guide anchor; extent changes grow or shrink content around it and provider updates never move chrome.
- `WidgetSnapshot` as a versioned last-admitted checkpoint, not an expiry cache. Ordinary invalidation records demand without deleting it. Only
  restart, removal/runtime replacement, generation/protocol incompatibility, trust revocation, or unsafe corruption may hard-remove it. Semantic
  retention, appearance/resources, and host interaction state are separate validity domains.
- Generic versioned post-checkpoint operations: typed properties, keyed child insert/remove/move, subtree replacement, and complete-checkpoint fallback.
- Impact-classified authority/accessibility/resource/paint/layout invalidation with safe unknown-impact fallback. An identical newer publication may
  advance sequence/action authority without layout or paint; changed publications invalidate only affected authorities, with full layout/redraw fallback.
- Provider-neutral host behavior: never branch native layout, rendering, focus, or resource authority on package ID, publisher, element text, classes,
  known tree shape, or domain provider.
- Community domain integrations stay in their package. Do not add provider-specific DTOs, credentials, clients, identities, or process behavior to
  generic host/SDK/protocol layers.
- Games & Apps remains bundled; Playnite/Game Launcher is a Community full-trust application using the same public package, consent, lifecycle, and
  overlay path available to independent authors. A normalized game-library capability may remain for bundled Games & Apps and voluntary sandboxed use,
  but is not the required Community Game Launcher path.
- Host-owned pinning/surface placement and a narrow trusted rich-media process precede YouTube-style work; there is no generic Community WebView.
- Native presentation, GameInput/controller focus, accessibility, rendering, motion, window, WRSS, bridge, and widget-domain owners are modernized only
  at assigned boundaries, never wholesale replaced.
- Package-local styling may create a coherent product while preserving semantics, accessibility, real enabled authority, DIP responsiveness, and the
  supported user-theme override layer.
- Background/focus presentation stays scoped to its admitted `UI.BackgroundSurface`; artwork cannot leak across removal or another widget.

Prefer deletion and consolidation. Treat roughly 1,000 lines or several independently testable concerns as an architecture-review trigger, not an
automatic split target; assess partial types as one logical owner.

Do not approve per-widget renderer offsets, title-derived launch identity, raw paths or commands in widgets, undocumented Windows control APIs,
unbounded snapshots, in-process sandbox claims, or duplicated host authority.

## Trust, security, and resource policy

Community execution has two explicit tiers:

- `sandboxed`: no ambient user network, filesystem, token, registry, device, process, credential,
  window, or desktop authority; only narrow consented host capabilities.
- `full-trust`: an ordinary current-user application after explicit install and enable disclosure.
  It may use normal user-level APIs, libraries, credentials, files, network, registry, databases,
  COM/WinRT, and child processes. It is not a sandbox.

Never silently promote tiers or approve full trust without the user. Overlay IPC authentication and
bounded admission do not contain a full-trust application's private behavior.

Treat packages, manifests, snapshots, scenarios, companion responses, images, provider data, paths, and protocol messages as untrusted. Bound IPC,
presentation trees, strings, recursion, actions, update rates, native/GPU resources, host caches/queues, capabilities, ingestion, and host sessions
before allocation. Retain the last valid presentation where appropriate, return precise diagnostics, and never silently truncate.

Do not impose arbitrary CPU, memory, process, database, file, socket, or private-model ceilings on full applications. Worker Jobs may retain accounting,
kill-on-close, non-breakaway teardown, integrity, and UI restrictions; those are not product-size quotas.

Security stabilization is bounded. Reopen installed-widget security work only for a reproducible P0,
a demonstrated threat-model violation, or a public-release blocker.

## Performance and quality

The overlay runs while games are active. When relevant, measure:

- Hidden/idle/visible/interactive CPU; GPU/render cost and cadence.
- Host/bridge/worker memory; process count and teardown.
- Snapshot size, update rate, semantic/render churn, and input-to-visible latency.
- Startup/activation time, polling/provider frequency, large-collection retention, decoded artwork, and cache bounds.

Prefer event-driven updates, coalescing, immutable render-facing state, lazy host resources, stable
focus, bounded retained handles, and minimal native tree churn. Measure before and after performance
changes; do not claim improvement from intuition.

## User-owned stop conditions

Stop the affected action and ask the user for:

- Third-party authentication, accounts, API keys, or credentials.
- Full-trust current-user package installation/enable consent.
- Physical controller, monitor, audio, Bluetooth, game, or assistive-technology interaction that cannot be automated safely.
- Destructive deletion, reset, discard, history rewrite, or loss of uncommitted work.
- A denied or out-of-scope push, publication, deployment, partner contact, or policy acceptance.
- A substantial conflict or architecture/product choice with materially different outcomes.
- New authority beyond this goal.

One blocked lane does not stop independent safe work in the other lane.

## Reporting

Keep routine updates concise. For every accepted milestone report:

- WIDGE identifier, objective, accepted commit(s), and exact scope.
- Verification actually run with exact counts; integration and upstream state.
- Installed package/artifact identity and visible overlay launch result when relevant.
- Remaining manual, packaged, hardware, authentication, or visual debt.

For rejected work, report the concrete defect, retained evidence, and bounded correction. For a
tests-only or reviewer-document-only follow-up, state why the accepted production instance was
retained.

Never call the overall product polished, secure, accessible, performant, or complete without
evidence satisfying the current ship gates in `docs/implementation-agent-goal.md`.
