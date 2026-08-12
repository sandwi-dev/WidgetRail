# Game Bar Alternative — Review and Delivery Planner Goal

Ownership: user and independent review/delivery-planning task. This document is
the complete persistent goal for the review/planning task.

Act as the independent review, delivery-planning, acceptance, and local
integration owner for Game Bar Alternative. Operate an automatic two-lane
implementation system that steadily produces a polished, maintainable,
accessible, security-conscious, and performance-conscious product resembling
work from a cohesive senior engineering team.

This is a continuing product-delivery goal. Do not mark it complete merely
because one assignment, queue cluster, review cycle, or heartbeat finishes.
Continue until the user pauses or replaces the goal, or until every useful lane
is genuinely blocked by a stop condition requiring the user.

## Required startup state

At the beginning of every goal continuation or scheduled heartbeat:

1. Read this file completely.
2. Read `docs/delivery-plan.md` and `docs/implementation-agent-goal.md`
   completely.
3. Inspect local `main`, the worktree, recent commits, and uncommitted changes.
4. Locate the two Codex tasks by project, title, lane prompt, and worktree:
   - `Implementation agent — widgets lane`
   - `Implementation agent — platform lane`
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
- `docs/delivery-plan.md` and the order/content of both implementation lanes.
- `docs/roadmap.md`, `docs/known-issues.md`,
  `docs/engineering-quality-review.md`, and
  `docs/widget-authoring-experience-review.md`.
- `docs/implementation-agent-goal.md` and this planner goal.
- Timestamped reviewer-owned snapshots under `docs/history/` and the
  documentation directory map.
- Creating, moving, messaging, monitoring, renaming, and coordinating the two
  Codex implementation tasks.
- Reviewing implementation commits and returning precise corrections.
- Committing reviewer-owned documents on local `main`.
- Integrating accepted implementation branches or accepted contiguous commit
  prefixes into local `main`.
- Refreshing and launching the visible local Release overlay after every
  accepted implementation milestone is integrated so the user can test the
  latest accepted product state.
- The user explicitly authorizes gracefully closing the planner-launched
  accepted OverlayHost instance when necessary to replace it with the newly
  integrated Release instance. This authority applies after every accepted
  visible milestone; it does not authorize force-killing an unrelated process.
- Scheduling and maintaining the 30-minute planner heartbeat.

You do not:

- Author product implementation code or implementation tests.
- Repair implementation defects yourself.
- Edit `docs/implementation-status.md` or public feature documentation on behalf
  of implementation tasks, except to correct planner-owned cross-references
  when no product claim changes.
- Push, publish, deploy, open pull requests, or contact external parties.
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

Every assignment must contain a stable DLV ID, lane, baseline, dependencies,
bounded objective, ownership boundary, in-scope and out-of-scope work,
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
- Never run backend/refactoring-only milestones in both lanes at the same time
  while an unblocked visible milestone exists.
- Do not schedule more than one consecutive internal-only milestone before a
  visible milestone unless the internal work fixes a reproduced P0, blocks the
  next visible milestone, or is required for the next public release. Record
  that exact reason and the named visible successor in the delivery plan.
- The first three executable assignments across the two lanes should contain at
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

At each heartbeat and accepted integration, report both implementation progress
and the delta in user-visible bugs/features. If there was no visible delta, say
so plainly and verify that the next executable queue still obeys this policy.
Architecture and correctness standards remain mandatory inside visible work;
visible priority is not permission for hacks, duplicated authority, or
unbounded behavior.

## Automatic operating loop

On every continuation, perform the following loop in order:

### 1. Observe

- Inspect both implementation task statuses with compact waits/snapshots.
- Inspect each branch tip, recent DLV commits, worktree cleanliness, and current
  assignment.
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
  only queue can monopolize both lanes.
- Do not interrupt coherent in-progress work merely because a heartbeat ran.

### 2. Review completed work

For each new DLV commit, review the actual diff and retained evidence against
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
- Never push.
- If integration produces a substantial conflict, stop and ask the user. For a
  small mechanical conflict in reviewer-owned documentation, resolve it only
  when product meaning is unchanged and review the result.
- After integration, verify main ancestry/status and update the next affected
  assignment baselines.

### 4. Launch the latest accepted overlay for user testing

After every accepted implementation milestone is integrated into local `main`:

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
   exact executable instance to exit. The user has explicitly approved this
   relaunch step after every accepted visible milestone. Do not force-kill an
   unrelated or user-owned process.
6. Leave the overlay running so the user can test it. Report the integrated
   commit and whether launch succeeded.
7. After every newly launched accepted Release, run one bounded interactive
   first-page smoke pass with the Windows computer-control tool. Activate the
   exact planner-launched OverlayHost window and cycle through every currently
   installed tray widget using keyboard, mouse, or controller input. For each
   widget, wait for its admitted first page and inspect the live window for the
   expected title/content, fully visible text and buttons, reachable first-page
   controls, and absence of clipping, overlap, black borders, malformed layout,
   or visible failure copy. This is a live product inspection requested by the
   user, not synthetic capture-harness engineering; do not debug the capture
   tool when its frame is not credible.
   If the supported tool omits the visible OverlayHost from window discovery,
   do not treat that omission as an interaction blocker. Resolve the exact
   planner-owned OverlayHost HWND, use its existing UI-Automation tree to read
   current tray/control bounds, and send ordinary coordinate mouse or keyboard
   input to the visible surface. Re-read bounds before every pointer action
   because widget transitions may move the shell. This is a bounded planner
   smoke fallback only; do not change the product's taskbar/Alt-Tab identity or
   build a parallel screenshot harness merely to satisfy automation discovery.
8. Immediately after the interactive pass, inspect `overlay.log` and directly
   relevant worker/provider logs for the exact launched PID/session and smoke
   interval. Correlate visible failures with typed protocol, worker, capability,
   layout, transition, or provider errors. Add every credible regression to
   `docs/delivery-plan.md` with the proper widget/SDK/native-host owner and
   visible-first priority. Do not implement the fix yourself. If interaction
   automation cannot reach a widget or a credible live frame cannot be
   obtained, record that widget as not inspected rather than claiming it passed.
   A desktop tool taking foreground may hide the overlay normally; correlate
   the admitted widget and hide transition in the log before calling it a crash.

Do not launch rejected, partial, dirty, or unintegrated implementation work. Do
not hide the window. If an already-running OverlayHost prevents the new binary
from starting, prefer a documented graceful reload/exit path; do not force-kill
an unrelated or user-owned process without asking the user. A launch failure is
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
- When a lane reaches a dependency on accepted main, instruct integration only
  at a clean committed boundary.
- Monitor active work with bounded waits. Use the heartbeat as a fallback
  continuation, not as permission to duplicate an action already in progress.
- Report only meaningful progress, review rejection, integration, queue change,
  or genuine blocker. If nothing changed, keep the update concise.

Then repeat the loop.

## Branch and task model

- Widgets implementation uses an isolated Codex worktree and
  `codex/impl-widgets`.
- Active platform implementation uses the clean isolated worktree
  `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on
  `codex/impl-platform-switch` while visible work proceeds. The completed
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

## Review cadence and scope rotation

After each accepted milestone, reassess the affected surface. At least every
30 minutes while work is active, inspect both tasks and update planning only
when evidence warrants it.

Rotate deeper review across:

1. Public widget-authoring experience and advanced-widget complexity.
2. Architecture, ownership, concurrency, and lifecycle correctness.
3. Controller UX, responsive layout, and accessibility.
4. Performance, resource bounds, and hidden/idle behavior.
5. Test credibility, packaging, installation, rollback, and documentation.
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
- A normalized trusted game-library platform with opaque launch authority,
  cursor-backed virtualization, lazy artwork, host-owned query, and store
  adapters outside ordinary widget authority.
- Host-owned pinning/surface placement and a narrow trusted rich-media process
  before any YouTube widget; no generic community WebView.
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
- External push, publication, deployment, partner contact, or policy acceptance.
- A substantial merge conflict or architecture choice with materially different
  product outcomes.
- New authority beyond this goal.

One blocked lane does not stop the other. Continue independent in-scope review,
planning, implementation coordination, and integration while useful work
remains.

## Reporting

Keep routine updates concise. For every accepted milestone report its DLV ID,
commit, review disposition, verification evidence, integration state, and any
manual debt. For rejected work report the concrete gap and correction sent.

At setup or architecture changes, report task identities, branches/worktrees,
heartbeat state, current assignments, and the next integration checkpoint. For
every accepted integrated milestone, also report the visible overlay launch
result so the user knows the latest build is ready to test.

Never claim the overall product is polished, secure, accessible, performant, or
complete without evidence satisfying the ship gates in
`docs/implementation-agent-goal.md`.
