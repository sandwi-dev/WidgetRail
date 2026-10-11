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

## 2. Performance Monitor — implemented; candidate ready for physical checks

- Reusable host sampling plus a pinnable production widget.
- FPS required; frame-time graph optional. Investigate PresentMon/ETW collection
  and permissions early; validate actual game compatibility and collection cost.
- CPU/GPU utilization, RAM and VRAM when supported; unavailable is never zero.
- Auto-detect active game, permit correction; bounded history and subscription
  lifetime, no hidden unbounded polling. Compact pin and richer main view.
- Evidence: metric correctness, process changes, overhead, unsupported adapters,
  no game, and required manual compatibility matrix.
- FPS probe: official Intel PresentMon v2.6.0 console binary (980,320 bytes),
  MIT license, valid Intel Authenticode signature, GitHub SHA256
  `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`.
  Docs: https://github.com/GameTechDev/PresentMon/blob/main/README.md and
  https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md.
  Release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0.
- Read-only account check: current process is unelevated and token is not in
  Performance Log Users. Actual two-second self-PID probe using a unique trace
  session and no input/GPU tracking returned access denied immediately. Evidence:
  `artifacts/performance/nonadmin-probe.log`, `nonadmin-probe-result.json`.
  No trace was started, elevation requested, group membership changed or service
  installed. Downloaded signed binary and reference docs are under the same
  ignored artifact directory, not yet distribution content.
- Design constraint: FPS needs explicit Windows-permission enablement, without
  elevating the main overlay or silently changing the user's groups. Investigate
  a narrowly scoped collector helper with explicit user consent and bounded
  lifetime/IPC, or an existing compatible PresentMon service. Required FPS must
  remain in scope; permission failure is a status, never a fabricated zero FPS.
- Implemented so far: native shared on-demand CPU/GPU/RAM/VRAM sampler; bounded
  PresentMon CSV accumulator and private authenticated collector; explicit UAC
  enable/stop with the main overlay unelevated; broker-scoped target IDs and
  separate read/control permissions; typed SDK service; production widget with
  automatic/corrected selection and compact pin; publication dependency lock and
  license. Existing bundled .NET is reused for elevated helper launch, avoiding
  a second runtime copy (Windows may label this UAC prompt Microsoft .NET Host).
- Focused checks: core 7/7; widget/SDK/broker feature tests 7/7 (including stale-read
  protection across FPS enablement). Real helper probe
  verifies PID/nonce authentication, idle without demand, permission-required
  without fabricated FPS, and exit when owner disconnects. Evidence:
  `artifacts/performance/helper-probe-01.log`. No elevation requested.
- Native metrics probe measured all four system metrics. Warm sampling used
  31.25 ms CPU over 10.58 seconds (~0.30% of one core), with a ~36.9 MB total test
  process working set. This includes the test harness; it is not incremental
  production memory or FPS overhead. Evidence: `system-probe-02.json`.
- Broader broker suite initially exposed two expected closed-vocabulary counts
  (42→44 capabilities, 10→11 domains). Updated those assertions and added explicit
  performance read/control checks. Rerun passed 70/70. SDK API baseline regenerated;
  compatibility checks passed 17/17; SDK suite 153/153; Settings 84/84. Feature tests initially used incorrect test-helper
  API names; fixed, then built and passed.
- Packaging self-test passed after explicitly adding the new production widget
  and helper/license to its expected edition inventories. Complete candidate:
  `artifacts/performance/candidate-01/build/frontend/OverlayFrontend.WinUI.exe`.
  Published cleanup verified; published helper's real nonadmin authentication,
  permission failure and disconnect-exit probe passed (`published-helper-probe.log`).
  PresentMon file hash matches the locked release. No collector remained running.
- Review covered stale periodic replies after control actions, neutral/unknown
  readings, no UAC from reads, runtime reuse under ShellExecute elevation,
  bounded protocol/history, no private trace persistence, retained verified
  executable file handle, and cleanup of only owned ETW sessions even after a
  collector crash. No elevation, service install or account-group changes made.
- Physical checks pending: approve/cancel Windows prompt; measure FPS for actual
  DirectX/Vulkan/OpenGL games, multi-swap-chain and frame-generation behavior;
  compare overhead under game load; choose another app and close it; pin, hide,
  unpin and verify collection stops; inspect themes/scaling/controller navigation.
  Frame-time graphs are optional and omitted. Actual privileged FPS remains a
  physical check; this has not been tested across games yet.

## 3. Widget marketplace and updates — implemented; candidate ready for physical checks

- Curated release-backed catalog in Settings, no accounts/ratings/payments/public
  submissions. Descriptions, screenshots, compatibility, trust and permissions.
- Reuse package validation/install/version selection; download with limits,
  integrity checks, cancellation and useful errors. Review increased permissions
  before activation; previous version remains available. Preserve focus.
- Define catalog schema, immutable package metadata, release generation, and
  offline behavior. Test malicious/stale metadata, interrupted downloads,
  compatibility and permission transitions. Document publisher workflow.
- Implemented: curated Playnite/Spotify/YouTube Video/YouTube Music inventory
  with pinned screenshot URLs, release/version checks, bounded cancellable
  background jobs, SHA256 verification with a retained archive handle, exact
  expiring review tokens, full-access confirmation and permission deltas.
  The existing catalog update transaction retains old versions, selects the new
  version disabled, and refreshes the host/Settings after publication.
- Shared CLI downloader and GitHub release client extracted to
  `src/PackageDistribution`; the CLI maps shared exceptions to its existing
  exit codes. 20 real Remote/GitHub CLI tests passed. The first test-name selector
  also matched an error-message string; `cli-distribution-audit.json` verifies
  all 20 actual tests passed (not a product failure).
- Settings now has Browse marketplace, details/screenshots (LB/RB), progress,
  cancellation, and controller-native review starting on Cancel near the trust
  warning. Up-to-date items route to installed-widget management. Current build
  checks: marketplace backend 7/7, Settings 85/85, diagnostics transport 27/27.
- Actual public-release probe succeeded for all four listings and downloaded /
  verified / reviewed YouTube Video 0.3.40 (SHA256 in `live-review-01.log`). It
  installed nothing and discarded the temporary archive. User profiles untouched.
- Next: share recorded package-source metadata with the CLI, finish regression
  checks/docs, build a complete candidate. Physical screenshot loading, long
  permission review, cancellation and install/update/rollback flows remain for
  the user's return. No active tool sessions at this checkpoint.

## 4. App updates with recovery — implemented; candidate ready for physical checks

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
- Diagnostics local commit: `c43fc5b6`. Latest completed Performance builds,
  tests and probes are under `artifacts/performance`. No running build/test
  sessions remain. Next: marketplace implementation. Reuse/extract the CLI's
  `RemotePackageDownload.cs`, `GitHubReleases.cs`, and recorded package-source
  update review. Existing widget install/version-selection/permissions remain
  the mutation authority. Downloads must be bounded cancellable jobs rather
  than blocking the private Settings pipe's short request deadline.
- Performance local commit: `407dd9c1`. Its complete candidate and 338 focused
  checks (7 core, 7 feature, 70 broker, 153 SDK, 17 API compatibility, 84 Settings)
  plus packaging/probe evidence are recorded above. This progress-note addition
  is the only uncommitted file after that milestone. Do not rerun completed checks
  without a relevant change; next implementation work is the marketplace.

### Marketplace completion checkpoint

- Final focused checks passed: marketplace 7/7, Settings 85/85, transport 27/27,
  shared CLI distribution 20/20. Review rechecks selected version/content digest
  under the catalog mutation lock; expired/replayed reviews cannot install.
- Source metadata is shared with CLI updates; saved inactive versions route to
  management. Live release/download/review probe passed without installation.
- Complete candidate: `artifacts/marketplace/candidate-01/build/frontend/OverlayFrontend.WinUI.exe`.
  Published async cleanup verification passed. Review found no additional blocker:
  semantic controls, stable focus, bounded cancellable work, exact held-file review,
  explicit full-access approval, disabled installation and prior-version retention.
- Physical marketplace checks remain pending. No live profile changed or app launched.
- Next: define and implement application updates and standalone recovery. Earlier
  chronological next-action entries above are historical; this is the current checkpoint.

### App update/recovery design (before implementation)

- Explicit Settings check, release notes, matching-edition download and final
  confirmation. No polling notifications, shutdown, silent installation or elevation
  of WidgetRail. Inno remains the per-user installer.
- Require the current registered Inno installation and matching running payload
  before offering installation. Development candidates can inspect releases only.
- Download both the selected new installer and the exact currently installed
  version's installer, verify published SHA256 values, and retain them locally.
  Refuse installation if the matching recovery installer cannot be obtained.
- A standalone Windows PowerShell recovery script uses Windows dialogs; it runs
  independently of WidgetRail/.NET and is accessible through a desktop recovery
  shortcut. The user quits WidgetRail before it can continue. It never kills
  processes or interrupts gameplay. Installer UI remains the final user control.
- With the application stopped, copy the entire WidgetRail profile before setup
  (including installed widgets and their data). Refuse reparse points, excessive
  size/file count, inaccessible files, insufficient space or incomplete backup.
  Backups/installers live outside the profile in WidgetRail-Recovery and are private
  local data, never support-report content or uploads. No automatic deletion.
- Recovery runs the retained previous installer, then restores its matching profile.
  Preserve the post-update profile separately. An old app never intentionally opens
  new profile data. Windows Credential Manager, OS permissions and external full-trust
  widget data are outside profile rollback; sign-in may be needed again.
- Persist phase markers before mutations. Recovery is repeatable after partial
  install or interrupted restore; no success reported merely because setup launched.
- Focused isolated tests exercise backup fidelity, exclusions/limits, failed setup,
  corrupted installer, interruption/recovery and profile preservation. Actual installer
  execution/upgrade/downgrade remains a pending user-authorized physical test.

### App updates implementation checkpoint

- Marketplace committed locally as `cd59d094`; candidate and checks recorded above.
- Implemented Settings release checks/notes, matching-edition verified downloads
  of new and exact previous Inno installers, cancellable jobs and exact single-use
  confirmation. Candidate installations cannot replace the registered installation.
- Independent Windows PowerShell recovery/desktop shortcut, whole-profile backup
  with ACLs and SHA256 manifest, installer recheck, persisted phases, previous-version
  install/profile restore and preservation of newer profile. Frontend startup observes
  recovery ownership. No update installed and no desktop shortcut created by tests.
- Initial service checks 8/8, Settings 86/86, transport 28/28, recovery 6/6 passed.
  Windows PowerShell null-string overload required an explicit previous-state path
  for atomic File.Replace; corrected and passed. A Settings test helper initially
  used Busy instead of IsBusy; corrected before the 86/86 run.
- Next: finish interruption/resource review, regression checks, candidate, docs and
  local commit, then Capture Library. This milestone is not yet finalized.

### App updates completion checkpoint

- Final focused checks: service 8/8 (`tests-final-02.log`), standalone Windows
  PowerShell recovery 9/9 (`recovery-final-04.log`), Settings 86/86
  (`settings-tests-final.log`), authenticated transport 28/28, shared CLI download/
  release regressions 20/20 (`cli-tests-final-02.log`). All under `artifacts/app-updates`.
- Additional test findings corrected: Windows PowerShell array enumeration for
  multi-file profiles; shared release client now includes installer assets only
  when explicitly requested by app updates (CLI widget listings remain unchanged).
  Damaged backups fail before profile changes; partial restore copies are preserved
  and rebuilt. The recovery entry point parses with Windows PowerShell 5.1.
- Final candidate: `artifacts/app-updates/candidate-02/build/frontend/OverlayFrontend.WinUI.exe`.
  Complete payload built, published cleanup verification passed, both embedded
  recovery scripts matched source SHA256. Candidate-01 is superseded (built before
  the final recovery/CLI corrections). No app or installer launched.
- Review: bounded downloads and metadata, no automatic shutdown/install, exact
  confirmation, matching installed-version/edition recheck before setup, explicit
  Windows confirmation, verification immediately before execution with held file
  leases, no reparse traversal, ACL/profile preservation, durable phase markers,
  no automatic deletion, and startup exclusion while recovery owns profile files.
- Physical pending: actual Inno upgrade/downgrade with published matching installer
  pair; Windows confirmation/shortcut; cancelled setup; first-launch failure and
  recovery; redirected folders/disk space; account sign-in after rollback. Current
  public version may have no newer installer to offer; fixtures validate that flow.
- Current next feature: Capture Library. All five remain in scope; objective is
  active. Do not merge, push, publish, install app updates, or remove worktrees.
