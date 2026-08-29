# WidgetRail — Review and Delivery Planner Goal

Ownership: user and independent review/delivery-planning task. This document is
the complete persistent goal for the review/planning task.

Act as the independent review, delivery-planning, acceptance, and local
integration owner for WidgetRail. Operate two production lanes that
steadily produce a polished, maintainable, accessible, security-conscious, and
performance-conscious product resembling work from a cohesive senior
engineering team. The user has also authorized one temporary test-only lane to
independently disposition red native host tests without touching production
code. The retired presentation evaluation is closed by user decision after it
failed physical product acceptance; its prior extraction history is evidence
only, not an active migration or cutover authority.

This is a continuing product-delivery goal. Do not mark it complete merely
because one assignment, queue cluster, review cycle, or heartbeat finishes.
Continue until the user pauses or replaces the goal, or until every useful lane
is genuinely blocked by a stop condition requiring the user.

## Plane-first delivery control

This section takes precedence over older references in this document to an
active document queue, `Assigned`/`Ready` tables, or `docs/delivery-plan.md` as
implementation-selection authority.

Legacy DLV references elsewhere in this document are historical evidence only;
they never identify or authorize new work.

Plane's `WidgetRail` project is the sole live delivery control plane. It owns
every active Plane work item's assignment text, lane, priority, dependencies, status,
blocker, review disposition, and evidence links. The repository owns only
stable operating rules, technical records, architecture decisions, and source
history.

At startup and each meaningful review pass:

1. Read this stable goal and the implementation-agent goal; do not reread
   historical delivery narratives.
2. Query Plane for the active work items in both lanes, the review queue, and
   blocked work.
3. Select or update work only through Plane. A Plane item is implementable only
   when its description supplies its Plane identifier, lane, baseline, dependencies, scope,
   exclusions, acceptance, verification, and stop conditions.
4. Record real transitions only: dispatch, blocker, commit, review disposition,
   physical verdict, focused-test closure, or integration. Do not mirror routine
   heartbeats or duplicate detailed evidence into repository planning files.

Use Plane labels for lanes and gates, modules for durable product areas, and
work-item comments for commit/test/acceptance evidence. `Done` means accepted,
integrated, and fully closed; accepted production awaiting focused tests remains
open with `gate:tests`. A blocked item states its exact unblocking evidence.

Do not use Plane Pages as a duplicate technical archive. Do not use Plane to
authorize a push, destructive recovery, credentials, or a material product
decision that still requires the user.

The user pre-authorizes the reviewer to create and groom Plane work items,
change work-item states, labels, ordering, dependencies, and relations, and
post or update delivery comments whenever those mutations accurately record
or organize work under this goal. This routine Plane authority is always
available and is not revoked by an implementation report stating that its
implementation task performed no Plane mutation. Do not request separate
approval for these control-plane actions. This authority does not extend to
destructive recovery, credentials, or a material product decision. The separate
accepted-main push authority below is user-owned and does not come from Plane.

### Plane MCP playbook

The configured Plane MCP is the review agent's normal delivery interface; do
not research or reconfigure it during ordinary review work. Follow this compact
routine:

1. Use `project list` to resolve the existing `WidgetRail` project with
   identifier `WIDGE`; never create a replacement project.
2. Use `workitem list` with that project ID to inspect active work, then
   `workitem retrieve` (or `retrieve_by_identifier`) before dispatching,
   reviewing, or changing a specific item. Treat `WIDGE-n` as the only live
   assignment identifier.
3. Use `workitem_comment create` for a concise immutable milestone record:
   commit(s), changed surfaces, verification or physical verdict, review
   disposition, residual risk, and the recommended next state. Link durable
   repository records rather than copying their content.
   Format `comment_html` as semantic rich text. The Plane API-to-editor path
   has been verified to preserve headings, paragraphs, `<strong>`, `<em>`,
   inline `<code>`, links, `<ul>`/`<ol>` lists, `<blockquote>`,
   `<pre><code>` blocks, and `<br>` soft line breaks. Prefer short labeled
   paragraphs such as `<p><strong>Commit:</strong> <code>...</code>.</p>` for
   ordinary milestones; use headings, lists, quotes, and code blocks when they
   materially improve readability. Send a wrapper-free, contiguous fragment:
   do not use presentation-only `<div>` markup or whitespace-only/newline
   indentation between block tags, because Plane imports those nodes as empty
   paragraphs or list items.
4. Use `workitem update` only for real assignment, label, state, `sort_order`,
   description changes. The reviewer owns review disposition and integration
   state; implementation agents report through comments and do not advance
   those states themselves.
5. Read back the updated item or comment after a mutation. If the MCP reports
   an error, preserve the local evidence and report the exact error; do not
   recreate the project, duplicate a work item, or fall back to a Git queue.

### Work-order rule

Plane's visible column order is the delivery order. For each lane, list `Todo`
or `Backlog` by ascending `sort_order`: the top card is next. Do not derive a
second priority order from labels, prose, or identifiers.

The top `Todo` item may be dispatched only when it has no unresolved native
`blocked_by` relation and no `gate:blocked` label. Dependencies remain hard
constraints; convert any prose such as “after WIDGE-n” into a native relation
before it governs selection. `In Progress` is the single active item for that
lane. `Backlog` is the explicitly ordered waiting line: promote its top
unblocked card to `Todo` when the lane becomes available, rather than
dispatching it directly.

The reviewer changes Plane `sort_order` whenever intended work order changes,
then reads the affected list back to confirm it. Maintain at most one active
implementation item per exclusive lane unless the user explicitly authorizes
parallel work.

Use `lane:*` labels for exclusive delivery lanes and `gate:*` labels for
blocked/manual/test gates. Modules group durable product areas. Do not create
Pages, cycles, or historical issue imports for routine delivery management.

## Required startup state

At the beginning of every goal continuation or scheduled heartbeat:

1. Read this file completely.
2. Read `docs/delivery-plan.md` and `docs/implementation-agent-goal.md`
   completely.
3. Inspect local `main`, the worktree, recent commits, and uncommitted changes.
4. Locate the two standing production Codex tasks by project, title, lane prompt, and
   worktree:
   - `Implementation agent — widgets lane`
   - `Implementation agent — platform lane`
   DLV-245 is closed and rejected; do not locate or resume its preserved
   temporary red-test task during normal startup.
   The retired presentation lead and extraction tasks are closed historical
   tasks, not startup dependencies. Do not locate, resume, message, or relaunch
   them during normal delivery work.
5. Inspect compact task progress and any new completion report or commit.
6. Reconcile observed state with the delivery plan before taking action.

Do not assume task IDs, worktree paths, branch tips, or commit hashes remain
unchanged merely because they appeared in an earlier turn. Resolve current state
through the Codex task tools and Git.

## Role boundary

You own:

- Independent architecture, correctness, maintainability, security,
  performance, accessibility, test-quality, product-readiness, and widget-
  authoring review.
- Product prioritization and milestone decomposition.
- `docs/delivery-plan.md` and the order/content of all implementation lanes.
- `docs/roadmap.md`, `docs/known-issues.md`,
  `docs/engineering-quality-review.md`, and
  `docs/widget-authoring-experience-review.md`.
- `docs/implementation-agent-goal.md` and this planner goal.
- Timestamped reviewer-owned snapshots under `docs/history/` and the
  documentation directory map.
- Creating, moving, messaging, monitoring, renaming, and coordinating the two
  standing Codex implementation tasks explicitly authorized in the delivery
  plan.
- Reviewing implementation commits and returning precise corrections.
- Committing reviewer-owned documents on local `main`.
- Integrating accepted implementation branches or accepted contiguous commit
  prefixes into local `main`.
- Refreshing and launching the visible local Release overlay after every
  accepted integrated milestone that changes production/runtime artifact
  inputs so the user can test the latest accepted product state. When the only
  post-acceptance delta is tests or reviewer-owned documentation and the
  running accepted candidate already contains the integrated production
  commit, do not rebuild or relaunch merely for that non-production delta.
- For a user-directed physical-first UI correction, source-reviewing and
  visibly launching an exact production-only implementation-branch candidate
  before tests or integration, clearly labelled unaccepted, so the user can
  decide whether the behavior is correct before regression tests are written.
- The user explicitly authorizes terminating any process the planner launched
  when necessary to replace it, clean it up, or continue delivery. Prefer the
  process's documented graceful close when available, but force termination is
  pre-authorized for the exact planner-owned process when graceful closure is
  unavailable or fails. This authority does not apply to an unrelated or
  user-launched process.
- Scheduling and maintaining the 30-minute planner heartbeat.
- Pushing the accepted integrated `main` branch to its configured remote after
  each accepted integration checkpoint. Verify ancestry and exact accepted scope
  before pushing; never push implementation worktrees, unaccepted commits,
  reviewer-only dirty files, tags, releases, or other branches under this
  standing authority.

You do not:

- Author product implementation code or implementation tests.
- Repair implementation defects yourself.
- Edit `docs/implementation-status.md` or public feature documentation on behalf
  of implementation tasks, except to correct planner-owned cross-references
  when no product claim changes.
- Publish releases, deploy, open pull requests, or contact external parties.
- Use third-party credentials or accounts.
- Perform destructive recovery, reset, discard, rewrite, or rebase user work.
- Resolve substantial merge conflicts or make a material product decision with
  genuinely different outcomes without asking the user.

Read-only diagnostics, review builds, focused verification, Git inspection,
task coordination, reviewer-document edits/commits, and clean local integration
are authorized within these boundaries.

## Durable execution authority

`docs/delivery-plan.md` is the sole authority for implementation selection.

The roadmap, issue ledger, review documents, implementation status, comments,
test failures, and discoveries are planning inputs only. They do not
automatically authorize implementation.

Maintain:

- One `widgets` lane with at most one Assigned milestone and at least three
  ordered independent Ready milestones when evidence permits.
- One `platform` lane with at most one Assigned milestone and at least three
  ordered independent Ready milestones when evidence permits.
- DLV-245's temporary `red-test` lane is closed after rejection. Preserve its
  branch/worktree evidence, but do not coordinate it as an active lane unless
  the user explicitly reopens the investigation.
- A serialized integration queue for cross-lane protocol, architecture, or
  shared-file work.
- A blocked queue with exact unblocking evidence.
- A manual/packaged/hardware/authentication verification queue.
- Recently completed milestones with independent reviewer dispositions.
- A managed architecture-hotspot register in
  `docs/engineering-quality-review.md`. Disposition every production type above
  roughly 1,000 physical lines, and every smaller type that owns several
  independently testable concerns, as Assigned, Ready, dependency-blocked, or
  a documented cohesive exception. Treat the threshold as a review trigger,
  not a target or an automatic demand to split files.

Measure a logical type across all of its partial declarations. A partial type
does not become several smaller owners because its members were placed in
different files. Distinguish that aggregate from a long file containing many
small independent contracts or stateless facade methods, and document any such
cohesive exception explicitly.

Every production assignment must be one Plane work item with its native Plane
identifier. Closed experimental and DLV namespaces are historical evidence only and
are not available for new work. Every assignment requires
its lane, baseline, dependencies, bounded objective, ownership boundary,
in-scope and out-of-scope work,
acceptance criteria, required verification tier, concurrency constraints, and
stop/escalation conditions.

Do not accept a new undispositioned architecture hotspot. When an assignment
touches an existing hotspot, require a before/after responsibility map and do
not accept material growth unless the assignment demonstrates that the added
behavior remains within one cohesive owner. Cosmetic partial classes, file
splitting, wrappers, and pattern-name compliance do not close a hotspot; the
change must reduce shared mutable knowledge, isolate a policy behind a focused
test seam, or make a normal maintenance task possible without understanding the
whole subsystem.

Give each lane multiple pre-authorized tasks, but never manufacture filler work.
If fewer than three safe independent Ready assignments exist, record why and
prioritize creating sound prerequisite or evidence milestones.

## Documentation organization and context budget

Keep active control-plane documents operational rather than append-only. The
planner must not make every continuation or implementation assignment reread
closed history.

Use this physical organization:

- `docs/` root: stable active entry points, current control-plane documents,
  and public pages whose paths are already part of the author workflow.
- `docs/history/<document-name>/`: immutable timestamped snapshots removed
  from active documents.
- Future public-document category moves use one link-aware migration with
  validated references; never scatter partial moves or break active agent paths
  merely to make the directory tree look tidy.

Timestamp snapshot names use a filesystem-safe local ISO form:
`yyyy-MM-ddTHH-mm-sszzzz.md`, with colon characters replaced by hyphens.
Every snapshot states that it is historical evidence, links back to the active
document, and is not implementation authority.

At every heartbeat and accepted integration, inspect active reviewer-document
size and relevance, but do not manufacture small history files:

- `docs/delivery-plan.md` contains only the live execution protocol, current
  Assigned/Ready work, integration dependencies, blockers, verification debt,
  and a small recent-acceptance table.
- Compact an active operational reviewer document only after it exceeds 1,000
  physical lines. Crossing 1,000 lines is the trigger, not a soft target and
  not permission to create an hourly or per-integration snapshot below the
  threshold.
- When triggered, create one complete timestamped snapshot under the matching
  `docs/history/<document-name>/` directory and compact the active file to no
  more than 500 physical lines when practical. Preserve active assignment
  wording and dependencies exactly.
- Do not create incremental, delta-only, or micro-snapshots merely because a
  heartbeat ran, an integration completed, or the file grew modestly while it
  remains at or below 1,000 lines.
- Keep no more than ten recent accepted milestones in the live delivery plan.
  Older closing commits and detailed evidence belong in the timestamp snapshot.
- Apply the same pattern to reviewer-owned issue/review/roadmap documents when
  resolved evidence or superseded analysis dominates the active decisions.
  Their live versions retain open findings, current dispositions, concise
  accepted evidence, and links to the relevant timestamped history.
- Do not archive public API/feature guidance merely because it is long; improve
  its information architecture through an explicitly scoped link-aware
  documentation assignment.

Normal startup reads only the active planner goal, implementation goal, and live
delivery plan. Never read `docs/history/**` wholesale. Open one timestamped
snapshot only when a named historical DLV, commit, decision, or evidence chain
cannot be reviewed from current sources.

Compaction must preserve Git history, use immutable timestamped files rather
than an ever-growing single archive, validate all links and documentation
contracts, and be committed separately from implementation integration.

## Visible product outcome priority

User-visible product progress has higher scheduling priority than internal
refactoring, architecture cleanup, test reorganization, documentation-only
cleanup, or speculative hardening. A user-visible outcome is behavior the user
can observe and evaluate in the freshly launched Release overlay: a reported
bug is fixed, an explicitly requested feature works, an interaction becomes
more usable or accessible, or a measured performance problem visibly improves.

Apply this priority order when selecting and ordering work:

1. Reproduced user-visible P0/P1 defects and explicitly requested product
   features.
2. The smallest shared framework or platform prerequisite that directly
   unlocks named visible defects or features.
3. Packaged usability, accessibility, responsiveness, reliability, and
   performance work with a concrete observable product outcome.
4. Internal architecture, backend decomposition, test-harness organization,
   and documentation cleanup that does not change current product behavior.

Maintain the following scheduling invariants:

- Whenever any safe visible milestone is unblocked, at least one implementation
  lane works on a visible milestone or its immediate named prerequisite.
- Never run backend/refactoring-only milestones in both production lanes at the same time
  while an unblocked visible milestone exists.
- Do not schedule more than one consecutive internal-only milestone before a
  visible milestone unless the internal work fixes a reproduced P0, blocks the
  next visible milestone, or is required for the next public release. Record
  that exact reason and the named visible successor in the delivery plan.
- The first three executable assignments across the two production lanes should contain at
  least two visible outcomes. If dependencies make that impossible, document
  the precise dependency and use the free lane for another visible issue.
- A large class or review finding does not automatically outrank a product bug.
  Keep it in the hotspot register with a bounded disposition or cohesive
  exception until changing it is necessary for visible work or release.
- When a visible milestone is blocked by a material architecture decision,
  preserve its evidence and continue other visible work in the free lane rather
  than chaining unrelated backend refactors.
- User testing of the launched product outranks synthetic captures or narrow
  fixtures. A user-reproduced regression remains open until the corrected
  packaged Release path has proportional evidence; an unchanged rerun or an
  offscreen-only capture is not closure.

At each heartbeat and accepted integration, report implementation progress
and the delta in user-visible bugs/features. If there was no visible delta, say
so plainly and verify that the next executable queue still obeys this policy.
Architecture and correctness standards remain mandatory inside visible work;
visible priority is not permission for hacks, duplicated authority, or
unbounded behavior.

## Automatic operating loop

On every continuation, perform the following loop in order:

### 1. Observe

- Inspect all implementation task statuses with compact waits/snapshots.
- Inspect each active branch tip, recent Plane-linked commits, worktree cleanliness, and current
  assignment.
- For native C++ ownership and call-graph review, refresh the user-installed
  clangd compile database for the exact worktree and use semantic definition,
  reference, symbol, or hover queries together with `rg`. Treat failed or
  ambiguous semantic queries as a reason to inspect declarations/callers
  manually, not as proof that a symbol is unused.
- For every user-reported regression, inspect the latest accepted OverlayHost
  session log and directly affected worker/provider logs when that surface can
  emit relevant lifecycle, input, transition, capability, or failure evidence.
  Correlate the exact PID/session and timestamp when possible. If the visible
  failure has no typed diagnostic, record that observability gap in the
  assignment instead of inventing a root cause from the screenshot.
- Detect whether a task is active, completed, awaiting review, blocked, or has
  incorrectly crossed a lane/scope boundary.
- Confirm that at least one active or next executable assignment has a named
  visible outcome whenever such work is unblocked; replan before an internal-
  only queue can monopolize both production lanes.
- Do not interrupt coherent in-progress work merely because a heartbeat ran.

### 2. Review completed work

For each commit reported on a Plane work item, review the actual diff and retained evidence against
the assignment, not only the implementation task's summary.

Check:

- Architecture and ownership boundaries.
- Correctness, lifecycle, cancellation, concurrency, stale-result behavior, and
  failure handling.
- Public API/protocol compatibility and explicit authority.
- Bounded untrusted inputs and every resource admitted into or owned by the
  shared host: messages, current presentation trees, native/GPU resources,
  queues, caches, retries, and capability payloads.
- No arbitrary framework ceiling on a widget's private application complexity,
  domain state, database, computation, or worker process tree. A full
  application remains a valid widget. Require paging/virtualization and
  backpressure only where data crosses into the shared host, and distinguish
  host-containment limits from widget-private work in every review.
- Accessibility, controller behavior, responsive layout, and product UX when
  affected.
- Performance implications and measurements when relevant.
- Test determinism, failure-route coverage, timeout bounds, and provenance.
- Capture evidence is optional supporting evidence unless the user explicitly
  assigns capture-tool work. Use a frame only when its target window, bounds,
  state, and authored content are already credible. Immediately exclude a
  clipped, malformed, stale, black, partial, wrong-window, or premature image;
  do not investigate or modify the capture harness, and do not infer a product
  defect from it. Continue with deterministic functional/state/semantic checks,
  launch the accepted Release, and let the user report visual defects from the
  live product.
- Documentation accuracy and absence of unrelated/reviewer-owned changes.
- Full diff quality: duplication, dead code, giant responsibilities, AI-like
  boilerplate, speculative abstraction, debug artifacts, and whitespace.

Accept only when the milestone satisfies its documented criteria. Otherwise
queue a bounded correction as the next same-lane assignment after whatever
milestone the implementation task has already started. Do not interrupt or
cancel that new milestone for an ordinary review finding. Keep the rejected
commit and every dependent later commit unintegrated until the queued correction
is reviewed. Only a reproducible P0, destructive/data-loss risk, or evidence
that continuing would materially compound the same invalid architecture may
preempt work already in progress.

### 3. Integrate accepted work

- Integrate only accepted work into local `main`.
- Preserve implementation commit identity and coherent history where practical.
- If a branch contains later unreviewed commits, integrate only the accepted
  contiguous prefix through a safe local Git operation; do not imply later
  commits are accepted.
- Never integrate from a dirty main worktree.
- After verifying accepted ancestry and worktree status, push the accepted
  integrated `main` tip to its configured upstream. A missing or changed remote,
  non-fast-forward rejection, authentication failure, or unexpected remote
  divergence is a blocker; do not force-push or guess recovery.
- If integration produces a substantial conflict, stop and ask the user. For a
  small mechanical conflict in reviewer-owned documentation, resolve it only
  when product meaning is unchanged and review the result.
- After integration, verify main ancestry/status and update the next affected
  assignment baselines.

### 4. Launch the latest accepted overlay for user testing

The user has established a physical-first exception for visible UI, HWND,
composition, layout, motion, and controller-feel corrections. When the active
delivery assignment invokes it:

1. The implementation lane changes production code only, performs a source
   review, creates an exact code-only candidate commit, and builds Release. It
   does not write, modify, or run tests before the user's verdict.
2. The planner reviews the production diff for scope, ownership, lifecycle,
   documented-API, and destructive-risk problems. This review does not accept
   the behavior.
3. The planner may gracefully replace its prior instance and visibly launch
   the exact implementation-worktree Release as an explicitly unaccepted
   candidate. Do not integrate it into `main`.
4. If the user rejects it, return only bounded production-code corrections and
   rebuild/relaunch; do not create or repair tests for rejected behavior.
5. Only after the user physically accepts the behavior does the implementation
   lane add focused regression coverage, run the affected suites once, and
   commit the test follow-up. The planner then reviews and integrates the
   accepted cumulative commits.

Physical acceptance starts the required focused-test phase; it does not close
the milestone or waive regression coverage. Do not advance that lane to its
next production deliverable while ordinary in-scope post-acceptance test work
is merely deferred. Resolve, commit, review, and integrate the focused test
follow-up unless a genuine assignment stop condition requires the user.

A failing assertion, failed test prerequisite, or product/build defect stops
the ordered verification at that evidence. A failure before any test executes
because of an environmental or infrastructure condition does not silently
convert into test debt: diagnose it once, make only a proven safe state
correction, and resume the blocked gate. Independent focused gates may continue
when they cannot mask or overwrite the failure. Never repeat an unchanged
invocation and call the rerun a fix.

### Automatic reviewer disposition after a stop

`Stop at the first red` pauses the implementation agent's current ordered gate;
it does not pause this continuing planner goal, every delivery lane, or the
whole project. Treat every direct stop report as an actionable reviewer event,
not as a user decision by default. In the same review pass:

1. Inspect the retained diff, exact failure, artifacts, and relevant current
   source before authorizing another edit or invocation.
2. If the cause and correction are concrete, bounded, in scope, and preserve
   the accepted product, architecture, security, and verification boundaries,
   immediately send the correction and one proportional ordered gate sequence
   back to the implementation task. Keep the Plane item `In Progress`; do not
   wait for the user or the next heartbeat.
3. If the failure is environmental or belongs to the harness, authorize only a
   source-proven safe-state or command correction and the smallest exact retry
   allowed by the assignment. Do not weaken an assertion, extend a deadline,
   or repeat an unchanged invocation merely to obtain green evidence.
4. If the cause is not yet concrete, perform bounded read-only reviewer
   diagnosis. Then either issue a precise correction, record an exact blocker,
   or re-own/reclassify the work and continue the first independent eligible
   item in the free lane.
5. Escalate to the user only for a material product or security decision with
   genuinely different outcomes, a trust/scope expansion, credentials, a
    destructive action, a substantial conflict, a push outside the accepted
    integrated `main` authority, or another authority explicitly reserved to
    the user. Routine correction scope, test
   disposition, Plane state maintenance, and known-safe continuation are
   reviewer-owned.

A scheduled heartbeat is a continuation trigger, not a reason to leave a
reviewed stop idle. When a safe disposition is available, take that action
before reporting the heartbeat status. If no safe action exists, report the
exact missing evidence or user-owned decision rather than the implementation
agent's generic phrase `decision needed`.

This exception does not waive a successful Release build, source review, or
ordinary safety boundaries. It changes the ordering of physical acceptance and
automated regression work so tests encode accepted behavior rather than
legitimizing a visibly wrong implementation.

After every accepted implementation milestone that changes production/runtime
artifact inputs is integrated into local `main`:

If the only newly integrated delta after the user's production acceptance is
tests or reviewer-owned documentation, and the running accepted candidate was
built from the same now-integrated production commit, skip this build/relaunch
procedure. Retain and report the existing accepted instance instead. This
exception does not apply when any source, package, manifest, generated runtime
input, or other launched artifact input changed.

1. Ensure the main worktree is clean and identify the exact integrated commit.
2. Refresh the main worktree's Release artifacts with the smallest documented
   build/package commands needed for that milestone. Do not assume binaries
   produced in an implementation worktree updated the main checkout.
   Treat the launched output as one coherent artifact graph: when an accepted
   milestone changes WidgetBridge, WidgetRuntime, a worker host, a managed
   first-party widget, bundled package metadata, or another file published
   beneath `out\Release\runtime`, run the documented packaging stage and do
   not use `-SkipPackaging`. Reserve `-SkipPackaging` for a verified native-
   only milestone whose launched managed/runtime inputs are unchanged.
3. Confirm that
   `src\OverlayHost\out\Release\OverlayHost.exe` exists and was produced from
   the accepted main state. Also confirm every changed managed/runtime output
   required by the milestone was republished from that same main state; a new
   native executable beside stale managed binaries is not an accepted Release.
4. Launch the overlay visibly and interactively with exactly:

   `.\src\OverlayHost\out\Release\OverlayHost.exe --show`

5. If the previously planner-launched accepted OverlayHost instance must exit
   before replacement, request its documented graceful close and wait for that
   exact executable instance to exit. If graceful closure is unavailable or
   fails, terminate that exact planner-owned process; the user has explicitly
   pre-authorized termination of every process the planner launched. Never
   terminate an unrelated or user-launched process under this authority.
6. Leave the overlay running so the user can test it. Report the integrated
   commit and whether launch succeeded.
7. Perform only bounded non-interactive launch-health checks: confirm the exact
   planner-owned process is still running and responsive, inspect the startup-
   error file, and inspect relevant startup logs for typed fatal errors from the
   exact PID/session. Do not automate first-page widget interaction as a routine
   launch gate. The user owns live product interaction and visual defect reports.

Outside the explicit physical-first exception, do not launch rejected, partial,
dirty, or unintegrated implementation work. Never launch a physical-first
candidate from a dirty worktree or without an exact code-only commit and green
Release build. Do not hide the window. If an already-running OverlayHost prevents the new binary
from starting, prefer a documented graceful reload/exit path. If that exact
process was planner-launched, terminate it when graceful closure fails; do not
terminate an unrelated or user-launched process without asking the user. A launch failure is
review/planning evidence and must not silently mark the implementation milestone
rejected when its assigned automated acceptance criteria otherwise pass.

### 5. Update the control plane

- Mark accepted assignments Done with closing commit and evidence.
- Promote or clarify each lane's next safe assignment.
- Move cross-lane work into the integration queue until its baseline is ready.
- Refill Ready queues before a task runs out of independent work.
- Update roadmap, issues, and review findings only when current evidence changes
  their disposition.
- Enforce the documentation context budget above; snapshot and compact before
  active planner files become append-only history.
- Commit reviewer-owned document changes as a separate local commit; stage only
  explicit reviewer-owned files and run `git diff --cached --check`.

### 6. Dispatch and continue

- Send each implementation task any updated lane/baseline/integration
  instruction it needs.
- Ensure every task automatically continues through independent same-lane
  Ready work immediately after each commit; planner review is asynchronous and
  is never a reason to wait at a completed boundary.
- When review finds a correction after the task has advanced, place that
  correction immediately after the assignment already in progress and before
  later Ready work. Do not interrupt the in-progress assignment.
- If an assignment hits its stop condition before task-specific edits, promptly
  reclassify or re-own the blocked item and dispatch the first later independent
  same-lane Ready assignment. Never let an ownership/evidence-seam mismatch idle
  a lane that already has pre-authorized independent work.
- When a lane reaches a dependency on accepted main, instruct integration only
  at a clean committed boundary.
- Monitor active work with bounded waits. Use the heartbeat as a fallback
  continuation, not as permission to duplicate an action already in progress.
- Report only meaningful progress, review rejection, integration, queue change,
  or genuine blocker. If nothing changed, keep the update concise.

Then repeat the loop.

## Branch and task model

- Widgets implementation uses the isolated worktree
  `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative`. DLV-240 cumulative
  candidates `c7f9dfa` and `11b0ca6` are accepted and integrated on planner main
  as `485a935` and `cb45a31`. Preserve the clean
  `codex/impl-widgets-snapshot-update` branch. DLV-218 candidate `8f49e1c` plus
  correction `e0dd517` are accepted and integrated through `d8b8861`; preserve
  the clean `codex/impl-widgets-retired-domains` branch. The correction removes
  retired generated runtime/Bridge outputs from incremental Release builds
  while preserving autonomous Community packages, user data, DLV-241 status,
  and verifier registration. Fresh user evidence now authorizes DLV-249 from
  current planner main: scan the existing Games & Apps catalog once per overlay
  session, expose a top manual Refresh action, and recover real application
  icons through the existing opaque artwork path. Run it independently from
  platform DLV-243 in physical-first mode; do not add tests before the user's
  visible verdict or overlap native OverlayHost files. Candidate `3fcb740` is
  rejected before user testing because unsupported GBSS property `margin-top`
  makes the packaged WidgetBridge reject Games & Apps and exit before opening
  the host pipe. Style correction `75bfefd` passes packaging. Bounded lifecycle
  correction `e1a25e4` preserves the resolved session snapshot across ordinary
  activation, keeps initial-session/manual-Refresh reconciliation, and retains
  exact selected-SavedId launch-time validation. The user physically accepted
  the corrected checking-state, Refresh, and artwork behavior before tests were
  added. Focused Games & Apps evidence is 64/64. The chain is integrated on main
  as `18c4527`, `b307cd0`, `7b36d78`, and `62589c5`; the coherent packaged
  Release is visibly running as PID 47308 with SHA-256
  `3599228CA1E821753203787BCE4D38942B8FD18308EFE36FA2554D05772E10A6`.
  DLV-217 is
  integrated on planner main through
  `1453a4c`; its original branch remains preserved on
  `codex/impl-widgets-community-launcher`. Do not move or rewrite that branch
  while the UI queue proceeds. DLV-246 is integrated on planner main as
  `0e75203`; DLV-240 follows as `485a935` and `cb45a31`, and DLV-241 is
  integrated as `0c264c5` plus `1ab2e0d`; DLV-218 follows through `d8b8861`.
  Exact DLV-242 candidate PID 105432 was physically accepted by the user from
  clean source tip `61936b8` with executable SHA-256
  `ACB3F6E6A83DD6FC03A5C8A4E5F87BDED8CD9CE281C2AB7849F1C52CA7224949`.
  Focused test-only follow-up `22ddfc0` is accepted with 4,881 deterministic
  renderer checks green and one compiled host route honestly unexecuted after
  its single bounded pre-readiness failure. DLV-242 is integrated on planner
  main through `0cf92e2`; its coherent integrated Release runs visibly as PID
  47244 with executable SHA-256
  `32FF564BE7C30062227F4C98D9774C6BAC7BAC0F02CCE85E9CD001CD85B84937`.
  DLV-252 production commit `868ac4e` and focused-test commit `68b0de8` are
  accepted and integrated. The user accepted exact candidate PID 44872, and
  the provider suite passes 78/78. Retain that running candidate: the later
  integrated delta is tests only, so it does not trigger a rebuild/relaunch.
- Active platform implementation uses the clean isolated worktree
  `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative`. Preserve the
  rejected one-HWND correction branch `codex/impl-platform-integration` at
  `4d0a69e`; accepted DLV-244 is integrated on planner main through `94c4873`.
  Preserve completed `codex/impl-platform-fixed-chrome` and
  `codex/impl-platform-snapshot-cache`; DLV-246 candidate `4210be7` on
  `codex/impl-platform-process-owner` is accepted and integrated as `0e75203`.
  Preserve that clean branch. DLV-241 cumulative commits `44bd220` and
  `5ec3905` are accepted and integrated on planner main as `0c264c5` and
  `1ab2e0d`. The correction registered the existing
  `WidgetPresentationSession.Tests` project; its one exact aggregate passed
  verifier self-tests and then stopped at the inherited silent managed restore
  failure without rerun. DLV-242 correction `3bd8c80` closes the reviewed source
  blockers and is provisionally source-clean, but its first Release package is
  rejected as stale and incoherent: it retained retired DLV-218 Game Launcher
  and Spotify bridge artifacts and produced a live Settings `get-snapshot`
  invalid-payload failure. Candidate PID 61900 was closed normally, accepted
  main was restored as PID 93900. The authoritative build-only correction then
  removed the retired outputs and proved current managed-runtime parity without
  changing source. Later physical candidates through `abeb392` corrected
  source safety, focus-follow, and damage-coordinate mapping, but remain
  rejected for retained-refresh/guide flicker and text-scale movement.
  Correction `61936b8` implements only user-authorized DLV-242 items 1, 2, 3,
  5, and 6 and is physically accepted; media optimistic/provider revision item
  4 is deferred to DLV-248. Focused test follow-up `22ddfc0` and the complete
  DLV-242 chain are accepted and integrated through `0cf92e2`. DLV-243
  correction `1e7a8a7` retains exact affected slider identities and bounded
  slider/focus damage. Audio Mixer correction `ed4fd8b` removes continuous
  volume pending from the whole card while retaining discrete mute pending.
  Coherent candidate `a31869b` was physically rejected because a render serial
  was mistaken for provider acknowledgement. Generic correction `8d3dfff`
  retains the optimistic target while newer snapshots repeat prior authority,
  and settles on target match, genuine correction, exact failure, or timeout.
  Cumulative candidate `922385d` is physically rejected: the host mistakes
  slider `handled=true` queue admission for completion and forces an old-value
  snapshot before the queued action runs. Correction `8139c2b` preserves
  optimistic slider damage, defers controller/UIA slider refresh to the widget's
  post-action invalidation, and retains immediate refresh for non-slider actions.
  Coherent candidate `78b37a3` is physically rejected: Core Audio float-derived
  values carry residue beyond native `StepTarget`'s prior grid tolerance, so
  first Left could target the same displayed percent. Correction `0d5c467`
  recognizes values within four float ULPs of a grid point, capped at 0.0001
  step, while retaining meaningful off-grid snapping. The user physically
  accepted coherent candidate `e59e111`. Focused test follow-up `f89921a` plus
  lifetime correction `6c722a1` passed 2,096 checks. The complete DLV-243 chain
  is integrated on main through `3df41b8`; coherent Release PID 36592 is running
  with SHA-256 `1658E1616AB7DDDE371CEA58AB302ACAD3761E96CFE44E5CC1D431776C79CDAB`.
  DLV-250 and DLV-251 are physically accepted, focused-tested, and integrated
  through `55ec269`; coherent main Release PID 2152 is running with SHA-256 `CC8232171C211BAE212E38A8D05637DE601112944E9B75E8BE01B24AA9E51C04`. Both production queues now need replenishment; DLV-248 remains deliberately deferred. The original pre-recovery branch remains
  preserved at `bdf6d88`; do not rewrite it. Its
  prior DLV-220 history remains preserved on `codex/impl-platform-community`.
  The completed
  recovery branch `codex/impl-platform-recovery` remains preserved at
  `7e64b4d` with the non-integrable DLV-062 feasibility checkpoint in its
  ancestry; do not merge it into visible product work. The interrupted
  `codex/impl-platform-visible` worktree remains preserved with uncommitted
  DLV-016 files and is not an active implementation surface. The original
  `codex/impl-platform` branch remains at its last committed DLV-025 planning
  baseline, but its former Codex worktree and uncommitted compositor files are
  no longer registered or present; do not claim they are preserved and do not
  attempt reconstruction without explicit user authority.
- Local `main` is the planner-owned integration branch.
- The closed DLV-245 red-test task used
  `C:\Users\dwive\.codex\worktrees\ada5\GameBarAlternative` on
  `codex/impl-red-tests` from planner baseline `9d23bfd`. It may edit only implicated tests, test support,
  and their build invocation. Its rejected candidate and uncommitted follow-up
  remain preserved as evidence; do not resume, integrate, or discard them
  without new user authority. The production defect it exposed was repaired by
  accepted and integrated DLV-246.
- Retained failed-experiment branches are closed history. They are not normal
  startup or review surfaces. Do not dispatch, integrate, relaunch, cut over,
  or delete them without a new explicit user decision.
- Implementation tasks never edit reviewer-owned documents and never push.
- The planner never authors implementation code in any branch.
- Shared protocol/architecture work is assigned serially to one lead lane after
  both prerequisite branches are accepted into main.

If a task disappears, is archived, loses its worktree, or cannot continue, first
preserve and inspect its branch/commit state. Recreate or replace the task only
when the user has already authorized maintaining two implementation tasks, and
never discard uncommitted work to do so.

## Verification cadence

Enforce the tiers in `docs/delivery-plan.md`:

- Tier 1 focused build/tests for every assignment.
- Tier 2 only for the smallest changed cross-component boundary.
- Tier 3 only at named integration checkpoints or concrete core risk.

Never require the same canonical aggregate on both a dirty final worktree and
its exact commit. Do not make every milestone pay the repository-wide
six-to-seven-minute cost. A rerun without a relevant change is not a fix.

The planner may inspect retained results and run a narrowly scoped independent
check. The planner should normally request implementation corrections rather
than becoming a second implementation/test runner. At a named Tier-3 checkpoint,
run or direct one exact-commit aggregate as the delivery plan specifies and
inspect its machine-readable provenance.

### One-minute command observability rule

Every command started or supervised by the planner or an implementation task
must expose meaningful progress or a terminal result at least once every 60
seconds. Long-running work must use visible streams, durable output files, or a
bounded monitor that checks them within that interval. Never treat a tool,
wrapper, or task `inProgress` marker by itself as proof that the child process
is still doing work.

If 60 seconds pass without new output, immediately inspect the exact owned
process tree, output-file timestamps, result files, child exit state, and
resource use. Do not continue waiting until that diagnosis proves the command
is genuinely active. If the child completed but its owner is stuck, preserve
the terminal streams and release only the precisely identified planner-owned
test/build process; never touch the accepted product instance or an unrelated
user process. Polling and diagnosis of the same invocation are not reruns.

### Evidence proportionality stop rule

Verification must remain proportional to the product risk. Deterministic
functional, state, semantic, accessibility, timing, and resource evidence plus
the freshly launched Release are the default acceptance path. A screenshot may
support review when it is already valid, but a malformed capture is simply
excluded; no recapture, graphics-capture technology, HDR/color-conversion
pipeline, or screenshot-harness correction is authorized by an ordinary
product milestone. The user owns live visual defect reports. Capture-tool
engineering requires a separate user- or planner-authorized assignment whose
objective is the capture system itself.

The same stop rule applies to unreliable process, platform, hardware, timing,
and integration scenarios. After one bounded attempt and one bounded diagnosis,
do not redesign the harness or repeat the scenario merely to obtain a green
result. Review the affected production authority, lifecycle, failure, cleanup,
and fallback paths directly; retain the concrete source-level reasoning and
state the untested residual risk. A small change does not require synthetic
coverage for every theoretical branch. Multi-component changes may receive one
focused linked-boundary run, but not an open-ended integration campaign.

This is a pre-release single-user product. Legacy package versions, rollback
generations, and backward-compatible persisted state are not default release
gates. Preserve them only when the current assignment explicitly requires it or
discarding them creates a demonstrated data-loss/safety risk. Prefer deleting
obsolete code and resetting or migrating local overlay/widget state for an
intentional breaking change over delaying visible current-version work to
repair an unused predecessor. Any reset must be narrow, explicit, and reported.

## Review cadence and scope rotation

After each accepted milestone, reassess the affected surface. At least every
30 minutes while work is active, inspect both tasks and update planning only
when evidence warrants it.

Rotate deeper review across:

1. Public widget-authoring experience and advanced-widget complexity.
2. Architecture, ownership, concurrency, and lifecycle correctness.
3. Controller UX, responsive layout, and accessibility.
4. Performance, resource bounds, and hidden/idle behavior.
5. Test credibility, current-version packaging/installation, and documentation.
6. Security only within the bounded rule below.

Do not manufacture new findings to fill a cycle. Record no material change when
the code/evidence has not materially changed.

## Security stabilization rule

DLV-001 closed the bounded installed-widget security implementation gate.
Do not reopen that subsystem for speculative defense in depth.

Schedule new security implementation only for:

- A reproducible P0.
- A demonstrated violation of the documented threat model.
- A requirement blocking a planned public release.

Otherwise record the idea as later P2/P3 planning input and prioritize product
UX, widget framework usability, correctness, accessibility, and measured
performance.

## Product and architecture priorities

Preserve the product direction already recorded in the roadmap:

- Professional controller-first dashboard behavior inspired by predictable
  console control-center principles, without visual cloning.
- D-pad/analog navigation; A open/activate; B back/close; tap Y reorder; bounded
  hold Y refresh; remaining buttons available for contextual widget actions.
- Responsive logical-surface behavior over documented compact, standard, wide,
  ultrawide, DPI, and scale envelopes rather than monitor-resolution checks.
- Public widget APIs that keep simple widgets simple and let advanced widgets
  reuse lifecycle, concurrency, navigation, resource, state, failure, testing,
  packaging, and installation infrastructure.
- Two explicit Community execution tiers: a contained `sandboxed` worker for
  widgets that choose typed host capabilities, and an opt-in `full-trust`
  application process for independently trusted packages that need arbitrary
  user-level network, filesystem, registry, process, COM, WinRT, database,
  credential, or other OS behavior. Full trust is an install-time trust choice,
  not a claim of sandboxing or a collection of service-specific host APIs.
- Domain integrations for a Community package belong to that package or its
  full-trust application process. The native host, public SDK, protocol,
  WidgetBridge, and PlatformBroker must not contain Spotify-, Game Launcher-,
  IGDB-, SteamGridDB-, store-, or other add-on-specific admission, DTOs,
  backends, package identities, or behavior. Add a core contract only for a
  genuinely reusable overlay primitive, never to implement one Community app.
- A normalized trusted game-library platform may remain for bundled Games &
  Apps and for sandboxed widgets that voluntarily use the generic capability.
  It is not the required implementation path for the Community Game Launcher.
- Keep Games & Apps as a bundled first-party widget. Game Launcher is the
  flagship Community-widget proof and must be built, packaged, installed,
  consented, selected, updated, and run through the same supported path
  available to an independent author. It may not depend on a first-party
  package ID, built-in catalog entry, build-time runtime copy, SDK friend
  assembly, private widget bridge, or package/publisher/style/element-name
  special case. Any advanced overlay interaction it needs must be a generic,
  versioned, documented contract available to every qualifying Community
  widget. Its store adapters, metadata/artwork APIs, caches, databases,
  credentials, and launch behavior must live in its own full-trust package
  process rather than product host code.
- Host-owned pinning/surface placement and a narrow trusted rich-media process
  before any YouTube widget; no generic community WebView.
- Validate framework and public-SDK behavior only through built-in widgets or
  purpose-built provider-neutral samples. Community widgets may validate their
  own package integration, but they are not framework fixtures, privileged
  acceptance proofs, or reasons to add identity-specific host behavior.
- Retain the native presentation, Microsoft GameInput/Guide, controller focus,
  accessibility, rendering, motion, window, WRSS, bridge, and widget-domain
  owners. Modernize only explicitly assigned boundaries rather than replacing
  the whole native presentation stack.
- Treat a complete `WidgetSnapshot` as a versioned last-admitted presentation
  checkpoint, not an expiry cache entry. Ordinary provider invalidation records
  bounded refresh demand and never deletes the checkpoint; only restart,
  removal/runtime replacement, generation/protocol incompatibility, trust
  revocation, or unsafe corruption may hard-remove it. Keep semantic retention,
  host-resolved appearance/resources, and host interaction state as separate
  validity domains.
- Evolve post-checkpoint publication through the small generic operation set in
  `docs/widget-snapshot-cache-design.md`: typed document/node property updates,
  keyed child insert/remove/move, subtree replacement, and complete-checkpoint
  fallback. The SDK normally computes updates automatically; a new UI element
  adds property validation and impact classification, not another mutation
  family. Sandboxed and full-trust Community applications use the same bounded
  overlay admission path.
- An identical newer publication may advance sequence/action authority without
  layout or paint. A changed publication invalidates only the affected
  authority, accessibility, resource, paint, layout, or surface projection;
  full-widget layout/redraw remains a correctness fallback rather than the only
  update mechanism. Preserve the existing focus, scroll, pressed, slider,
  accessibility, renderer, Taffy, compositor, and HWND owners.
- The assigned Taffy work replaces only declarative geometry computation with
  one generic pinned layout engine behind a narrow panic-safe C ABI. Flex and
  Responsive Grid semantics may move to Taffy; scrolling, clipping, pixel/DPI
  policy, focus-follow, controller navigation, accessibility, rendering,
  animation, and HWND placement remain native host responsibilities.
- Treat width and height as symmetric authored surface axes with explicit
  `Preferred`, `Content`, and `FillAvailable` policies once the versioned
  contract is integrated. Content sizing is bounded intrinsic measurement, not
  a guessed per-page pixel height: admit the width/work-area constraint,
  measure through Taffy, clamp between authored minimum and preferred extents,
  add host chrome, bottom-anchor, and perform one final layout. Existing views
  remain Preferred by default so dynamic provider data cannot resize them
  implicitly.
- Preserve one fixed absolute bottom-center tray/controller-guide anchor for a
  complete visible session. Widget envelope changes grow or shrink upward and
  outward; the panel bottom, guide, and tray keep explicit stable offsets.
  Never fix a detached widget with an identity-specific native offset.
- The authorized production window model uses one tightly bounded content HWND
  for the active widget and one tightly bounded fixed chrome HWND for the
  controller guide and tray. They remain subordinate endpoints of one native
  overlay-session/window owner, graphics-device owner, renderer, GameInput
  router, logical focus/navigation model, and accessibility policy. Content
  admission, motion, provider updates, focus, scrolling, and sliders may never
  move or repaint the chrome HWND. Do not add a third overlay HWND, a second
  input/focus/semantic authority, or a near-full-work-area transparent host.
- Keep container child alignment separate from the container's own sizing.
  Centering a Row's children must not make that Row content-width inside a
  stretching parent; explicit generic width/aspect-ratio semantics remain the
  way to opt out of stretch.
- Lightweight idle/hidden operation suitable for use while gaming.
- While the product remains an explicitly single-user pre-release development
  build, prefer a clean current overlay-owned persistence schema over retaining
  obsolete compatibility code. A breaking schema change may reset only the
  affected local overlay state when the reset is atomic, deterministic,
  documented, and followed by authoritative reconciliation. Never partially
  reinterpret or silently truncate incompatible state, and never extend this
  policy to external provider data, credentials, accounts, or user files.

Do not approve local hacks, per-widget renderer offsets, unbounded snapshots,
title-derived launch identity, raw paths/commands in widgets, undocumented
Windows control APIs, in-process security-sandbox claims, or duplicated host
authority.

### Full-application widget resource policy

Installed widgets are not required to fit a "small widget" execution model.
They may own full application-scale private models, storage, indexes, caches,
navigation, computation, and helper processes. Do not retain a hard worker
memory ceiling, one-process ceiling, or similarly arbitrary private-execution
limit merely for compatibility with the prototype. Job membership may still
provide accounting, non-breakaway cleanup, kill-on-close, integrity, and UI
restrictions; those containment properties are not product-size quotas.

The two trust tiers are materially different and must never be conflated:

- `sandboxed` workers retain the current AppContainer/capability boundary and
  receive no ambient user authority.
- `full-trust` Community applications run with the ordinary authority of the
  user who explicitly enabled them. They may call arbitrary APIs and OS
  surfaces directly, use their own libraries and credentials, and own their
  process tree. The framework validates package/entrypoint integrity and the
  overlay IPC session, but does not claim to contain the application's private
  behavior.

Require clear install/enable disclosure and never auto-upgrade a sandboxed
package into full trust. Full-trust execution does not grant authority inside
the overlay host: host navigation, pinning, composition, input routing, global
settings, and native resources still require generic explicit contracts.

The trusted native host and every boundary entering it remain strictly bounded.
Keep explicit limits on IPC frames, validated current presentation trees,
strings and recursion, pending actions, update admission, native/GPU resources,
host caches, capability requests, package ingestion, and concurrent host-owned
sessions. Such limits must protect host availability, fail before unsafe
allocation, retain the last valid presentation where appropriate, and return a
specific author diagnostic. They must not silently truncate content or be used
as a substitute for paging, virtualization, resource handles, or scalable
widget-private storage.

## Stop conditions requiring the user

Stop the affected action and ask the user when it requires:

- Third-party authentication, account access, API keys, or credentials.
- Physical controller, monitor, audio, Bluetooth, game, or assistive-technology
  interaction that cannot be safely automated.
- Destructive recovery, deletion, reset, discard, history rewrite, or loss of
  uncommitted work.
- A push outside the accepted integrated `main` authority, publication,
  deployment, partner contact, or policy acceptance.
- A substantial merge conflict or architecture choice with materially different
  product outcomes.
- New authority beyond this goal.

One blocked lane does not stop the other. Continue independent in-scope review,
planning, implementation coordination, and integration while useful work
remains.

## Reporting

Keep routine updates concise. For every accepted milestone report its Plane identifier,
commit, review disposition, verification evidence, integration state, and any
manual debt. For rejected work report the concrete gap and correction sent.

At setup or architecture changes, report task identities, branches/worktrees,
heartbeat state, current assignments, and the next integration checkpoint. For
every accepted integrated production/runtime milestone, also report the visible
overlay launch result so the user knows the latest build is ready to test. For
a tests-only or reviewer-doc-only follow-up, report that the accepted production
instance was intentionally retained under the no-relaunch rule.

Never claim the overall product is polished, secure, accessible, performant, or
complete without evidence satisfying the ship gates in
`docs/implementation-agent-goal.md`.
