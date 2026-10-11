# WidgetRail platform feature implementation

## Authority and workspace

User-approved goal (2026-10-11): implement the five features below independently,
in order, with controller-friendly UX, focused automated validation, runnable
candidates, and documentation. Physical acceptance remains pending the user's
return. This document is the durable progress/continuation record.

Worktree: `C:/Users/dwive/.codex/worktrees/platform-feature-roadmap/WidgetRail`.
Branch: `codex/platform-feature-roadmap`, based on `main` at `c661a62c`.
The main checkout's uncommitted browser-handoff/SDK changes and
`docs/implementation-agent-goal.md` are deliberately excluded and untouched.
No merge, push, release, app-update installation, or worktree deletion authorized.
Foreground handling, launch enforcement, and Spotify OAuth are out of scope.
Direct user assignment authorizes this roadmap independently of legacy planner
assignment documents. Work sequentially; no subagents requested.

## Completion gates

Each feature needs a controller-flow design, implementation, bounded resource and
failure handling, focused automated evidence, documentation, and a runnable
candidate. Record physical checks separately; never infer physical acceptance
from tests. If externally blocked, record evidence and move to the next feature.
Keep the full objective active until all five are ready for physical testing.

## 1. Useful Settings diagnostics — implemented; candidate ready for physical checks

- Existing foundation: private authenticated Settings diagnostics pipe, service
  health/worker state projection, controller status, and authority recovery.
- Add a readable overall status that distinguishes unreported telemetry from
  failed services; timestamp actual worker failures at the event source.
- Keep recent failures and actionable navigation/recovery visible; technical
  service details can be expanded. Preserve busy button focus.
- Export a support ZIP locally on explicit request, with a clear contents/privacy
  explanation and a predictable destination. Use an allowlisted structured
  projection, not raw logs, settings, exception text, browser history, captures,
  credentials, user paths, or chat data. No uploads.
- Tests: event timestamp preservation, health classification, transport bounds,
  export privacy/contents, cancellation/failure, and Settings focus/actions.
- Physical: controller navigation, expanded details, busy state and exported file.

## 2. Performance Monitor — pending

- Reusable host sampling plus a pinnable production widget.
- FPS required; frame-time graph optional. Investigate PresentMon/ETW collection
  and permissions early; validate actual game compatibility and collection cost.
- CPU/GPU utilization, RAM and VRAM when supported; unavailable is never zero.
- Auto-detect active game, permit correction; bounded history and subscription
  lifetime, no hidden unbounded polling. Compact pin and richer main view.
- Evidence: metric correctness, process changes, overhead, unsupported adapters,
  no game, and required manual compatibility matrix.

## 3. Widget marketplace and updates — pending

- Curated release-backed catalog in Settings, no accounts/ratings/payments/public
  submissions. Descriptions, screenshots, compatibility, trust and permissions.
- Reuse package validation/install/version selection; download with limits,
  integrity checks, cancellation and useful errors. Review increased permissions
  before activation; previous version remains available. Preserve focus.
- Define catalog schema, immutable package metadata, release generation, and
  offline behavior. Test malicious/stale metadata, interrupted downloads,
  compatibility and permission transitions. Document publisher workflow.

## 4. App updates with recovery — pending

- Release checks/notes and verified downloads; deliberate Inno installer handoff,
  never interrupt gameplay. Share suitable download/version infrastructure.
- Recovery must work when the app cannot launch. Define retained installer,
  settings backup and downgrade compatibility before coding rollback.
- Test with isolated fixtures; do not install updates on this PC without further
  user authorization. Prepare documented installer/recovery physical checks.

## 5. Capture Library — pending

- Full-resolution screenshots; assess clip resolution/encoding overhead.
- Reusable host retained-media storage distinct from temporary attachments.
- Production Capture Library widget: explicit save, browse, preview, delete,
  export, and saved-capture reuse in Game Help. Bound storage and explain usage.
- Reuse general media controls; handle expired temporary attachments, partial
  writes, cancellation, missing files and permission failures.
- Test dimensions, storage lifecycle, export and Game Help attachment authority;
  document physical screenshot/video/controller checks.

## Evidence and next action

- 2026-10-11: inspected main status and existing diagnostics, package updates,
  and version rollback. Created isolated worktree; no source changes yet.
- 2026-10-11 diagnostics milestone: added event-source UTC failure timestamps,
  health classification, plain-language recent failure rows, expandable service
  details, applicable management links, and authenticated support export.
  Export writes an allowlisted ZIP under Documents/WidgetRail/Support, includes
  no raw logs/private content, preserves prior exports and cleans partial writes.
- Focused Release checks: Settings 84/84 (`artifacts/diagnostics/settings-tests-final.log`),
  PlatformDiagnostics 26/26 (`transport-tests-final.log`), Bridge diagnostics 2/2
  (`bridge-tests-final.log`), actual registry failure-event timestamp 1/1
  (`failure-event-tests.log`). Each build has a distinct adjacent binlog.
- Build correction: extracted presentation needed the WidgetProtocol namespace;
  fixed before successful builds. Two old Settings assertions expected details
  expanded by default; updated to exercise expansion. No waived failures.
- Complete unpackaged Release payload built at
  `artifacts/diagnostics/candidate-01/build`; executable is
  `frontend/OverlayFrontend.WinUI.exe`. Published async cleanup verification passed
  (`candidate-01/logs/frontend-cleanup-verification.txt`). Candidate includes
  production Browser and Game Help. This is a dirty-source candidate retaining
  preview.29 version metadata, not a new release. No installation or launch.
- Review: existing semantic SDK controls/theme classes, stable IDs and Busy
  controls; UI contains no filesystem work. Export reconstructs data explicitly,
  checks authentication/result bounds, uses UTC event timestamps, excludes
  recovery authority and free-form diagnostics, and handles uncertain receipts.
- Physical checks pending: navigate diagnostics with controller; expand/collapse
  without focus loss; inspect across themes/scales; export and locate ZIP;
  verify reported failures against a real widget failure; check protected or
  redirected Documents behavior. These are not claimed as physically accepted.
- Next: Performance Monitor feasibility and implementation. Investigate an actual
  FPS collector (PresentMon/ETW, permission/deployment implications), then shared
  metrics contracts and pinnable widget. Do not settle for usage-only monitoring.
