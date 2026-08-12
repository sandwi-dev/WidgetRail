# Engineering Quality Review

Status: active independent quality audit<br>
Last reassessed: 2026-08-12 against integrated `main` `2f766fe`<br>
Scope: architecture, maintainability, correctness, security, performance,
verification credibility, accessibility, and product readiness

The detailed pre-compaction evidence is preserved in
[`history/engineering-quality-review/2026-08-12T02-47-00-07-00.md`](history/engineering-quality-review/2026-08-12T02-47-00-07-00.md).
That file is historical evidence, not current implementation authority.
Implementation order comes only from [`delivery-plan.md`](delivery-plan.md).

## Executive assessment

The repository has credible platform boundaries and increasingly focused
framework seams, but it is not yet at a polished senior-team release standard.
The strongest areas are typed capability authority, isolated widget execution,
deterministic focus/action semantics, bounded native presentation admission,
and focused failure tests. The largest remaining risks are live product
regressions, the native `OverlayApp` ownership hotspot, framework contracts that
still centralize unrelated validation or coordination, incomplete external SDK
governance, and missing packaged/hardware/accessibility/performance evidence.

Visible work remains the scheduling priority. Internal findings below are
implemented only through an explicit DLV assignment and must not displace an
unblocked user-visible correction or feature.

## Current review delta

### Launcher Experience platform prefix accepted

DLV-131 `100c646`, DLV-132 `874f778`, DLV-137 `41da5b7`, DLV-140
`5e3c69c`, and DLV-141 `878b484` are accepted and integrated through
`2f766fe`.

- The data-only catalog is separate from global themes and cannot author game
  content, identities, provider bindings, or actions.
- Standard VP8/VP8L/VP8X WebP dimensions and bounded package/catalog discovery
  are covered by the focused 17/17 Release fixture.
- Every encountered file consumes the 64-entry package budget before path,
  extension, role, duplicate, or diagnostic admission; traversal stops at 65.
- All four native presets and the left-rail/glass recipe retain paint, pointer,
  controller/keyboard focus, semantic focus, UIA bounds/order, exact action,
  Back, work-area, scale, and fallback agreement across 1,307 checks.
- The actual rebuilt `OverlayHost.exe` production semantic route exits 0.

This is a platform foundation, not a visible theme delivery. Production Game
Launcher state projection and selection remain later assignments.

### Normalized app-library contract returned

DLV-130 `da08c44` is coherent in shape but not accepted. It removes the legacy
scalar model and round-trips a single versioned presentation, yet
`WidgetAppLibraryService.IsValidPresentation` still owns validation for source,
availability, artwork, metadata, capabilities, and operation in one catch-all
method. That contradicts the assignment's explicit focused-validation boundary
and leaves launch authorization relationships under-specified for
`Unavailable`, `StaleSource`, and retained-last-good items.

DLV-138 `c82f111` and DLV-139 `2281548` are now committed but held with
DLV-130. DLV-142 is the next widgets assignment. It must establish
focused value validators plus a narrow relationship composer, preserve one
normalized public model, and prove that stale or unavailable presentation can
never authorize launch. DLV-130 and dependent widgets history stay out of main
until that correction is accepted.

### Planner live smoke works without changing product window identity

Exact accepted main `2f766fe` rebuilt in 44.7 seconds without repeating green
suites. Its production semantic proof exited 0 and PID 17576 launched normally.
The supported computer-control discovery API still omits the visible OverlayHost
window, but that is not an input blocker. The planner resolved the exact HWND,
read current UIA tray bounds, and used ordinary coordinate mouse plus keyboard
input to admit all eight first pages. Every reported first-page UIA rectangle
remained inside the host root; the exact interval contains no worker, protocol,
provider, or presentation failure. Terminal missing trusted artwork remains a
known launcher/app-library issue. DLV-136 stopped at evidence-only `83edb39`
because direct discovery would add taskbar/Alt-Tab eligibility; no product
window change is warranted for planner automation.

## Active findings

| ID | Priority | Current disposition | Closing evidence required |
| --- | --- | --- | --- |
| EQ-003 | P1 | Open hotspot. `OverlayApp` remains the dominant native mutable owner even after focused session, placement, process, surface, and launcher seams were extracted. Visible platform work may touch it only through a focused owner and before/after map. | A named assignment must remove one independently testable policy or lifecycle authority without adding another coordinator graph; focused failure/lifecycle proof and no visible regression. |
| EQ-034 | P1 | Open; DLV-130's normalized app-library SDK validation is centralized. Assigned as DLV-142 after active DLV-138. | Separate value validation owners, narrow cross-value composition, stale/unavailable launch denial, exact broker/Bridge/SDK/installed-worker evidence. |
| EQ-011 | P1 | Partially resolved. Installed packages are digest-bound and isolated, but verified publisher identity and acquisition provenance remain ecosystem work. | Explicit signing/publisher/update design at the public-distribution milestone; do not reopen installed-widget hardening speculatively. |
| EQ-013 | P1 | Architecturally implemented with bounded process leases and refusal policy; packaged aggregate-residency evidence remains. | Named multi-widget churn/residency run with exact process, cleanup, refusal, CPU, and memory evidence. |
| EQ-015 | P1 | Partially implemented. Cloneable offline SDK, release unit, compatibility checks, starters, package lifecycle, and docs exist; external publication/version/update governance remains open. | A real external repository consumes a versioned SDK/template without checkout references and completes build, scenario, pack, install, rollback, and removal. |
| EQ-020 | P1 | Open native responsiveness risk. Much bridge coordination moved off the UI thread, but synchronous startup/request paths still need exact-content latency and cancellation proof. | Production-host timing and cancellation evidence under slow/nonresponsive worker conditions without UI starvation or stale publication. |
| EQ-023 | P1 | Materially advanced. Native UIA/provider/action/Back semantics have deterministic host coverage; physical Narrator/MSAA and packaged assistive-technology proof remain. | Named packaged keyboard/controller/UIA/Narrator matrix on the accepted Release. |
| EQ-026 | P1 | Implemented in focused fixtures but still Verifying for user-reproduced list and reverse-scroll cases. | Corrected packaged Release passes the user's exact Audio Mixer and Spotify focus-edge reproductions. |
| EQ-027 | P1 | Verifier overlap and result provenance are guarded, but release input mutability and clean-current provenance are not a universal gate. | One named exact-commit Tier-3 checkpoint with immutable inputs, hashes, final status, and no duplicate dirty/clean run. |
| EQ-028 | P1 | Automated crash/restart isolation is substantially implemented. Live Spotify/YT Music/provider recovery and repair remain verification debt. | Packaged repeated-failure/restart/recovery runs with exact sanitized logs, bounded retry, retained-last-good behavior, and no cross-widget impact. |
| EQ-006 | P2 | Open through the hotspot register. Several application-sized roots have focused policy/presentation seams and conditional cohesive exceptions; further growth must be reviewed. | Per-touch responsibility map, no new mutable coordination owner, and focused policy/lifecycle tests. |
| EQ-007 | P2 | Partially improved. Canonical starters and compatibility surface compile, but many prose snippets are still link-checked rather than API-compiled. | Compile or extract the canonical public snippets used for lifecycle, navigation, capability, packaging, and troubleshooting guidance. |
| EQ-019 | P2 | Open performance debt. Legacy XInput Guide compatibility may poll four slots every 25 ms while its fallback is active. | Event/adaptive cadence policy plus hidden/idle CPU and input-latency evidence on relevant hardware. |
| EQ-030 | P2 | Open with bounded pilots. Several executable test runners remain application-sized, though new managed test projects correctly use MSTest.Sdk 4.3.2. | Split only when an assigned scenario family can gain an independent fixture/runner seam; no repository-wide migration. |
| EQ-018 | Disposition | Synthetic captures are supporting evidence only. Malformed/clipped frames are excluded immediately and never trigger capture-harness work during product milestones. | Live user verdict or credible target-bound frame plus functional/log evidence; no code change solely for a bad capture. |

Closed findings and their exact historical evidence remain in the timestamped
snapshot. They are not active backlog unless a new regression reopens their
documented invariant.

## Managed logical-type hotspot register

Line counts are review signals. Logical partial types are measured across all
declarations; long files containing many focused contracts are not treated as
one class automatically.

| Production owner | Current signal | Disposition |
| --- | ---: | --- |
| `OverlayApp` / `main.cpp` host application | `main.cpp` 6,436 physical lines; dominant logical owner remains several thousand lines | **Open, dependency-ordered.** DLV-136 must keep window identity in its focused owner. Revisit another extraction only as a named visible/release prerequisite. |
| `GameLauncherWidget` | 1,350-line root plus focused model/presentation/organization owners | **Active conditional exception.** DLV-138 and DLV-142 require before/after maps; no provider adapter, validation policy, or new coordination primitive may enter the root. |
| `GamesAppsWidget` | 1,376-line root | **Conditional exception.** Sole lifecycle/provider-effect/action/committed-state adapter over separate presentation, catalog, persistence, reconciliation, and app-library projection policies. Reopen for store/domain growth. |
| `AudioMixerWidget` | 1,702-line root | **Conditional exception.** One state/action/effect/selection/status/invalidation transaction owner over separate provider session, command transition, and presenter. Reopen for another coordination domain. |
| `SettingsWidget` | 1,503-line root | **Conditional exception.** One lifecycle/service-effect/committed-state adapter over separate section, permission, installed-package, and theme policies. Reopen for another service lifecycle or material unrelated growth. |
| `SpotifyWidget` | 1,275-line non-partial root | **Conditional exception.** Sole lifecycle/provider-call/resource/task/committed-state/invalidation owner over value policies and snapshot presenter. Reopen if policy/presentation or another coordination owner returns. |
| `NetworkControlsWidget` | approximately 1,500 lines at last focused audit | **Conditional exception.** Singular lifecycle/host-command/committed-state/invalidation adapter over separate provider, command, identity, action, and presenter seams. Remeasure on next touch. |
| `UI` SDK facade | approximately 1,471 lines across component-family partials | **Cohesive stateless exception.** No lifecycle, task, lock, resource, or mutable model. Reopen if component families share hidden state. |
| `WidgetBridgeServer` | 887-line file; focused registry/dispatcher/recovery owners exist | **Conditional exception.** Retain session/framing/routing/write ownership only; reopen if catalog/residency/diagnostics policy returns. |
| `WidgetProcessClient` | approximately 964 lines at last audit | **Conditional exception.** Sole public lifecycle/restart/failure adapter over one per-generation transport session and focused request/gesture owners. |
| `PlatformServices.cs` | long multi-type contract/service file | **Not one type hotspot.** DLV-142 is nevertheless required because one service method currently centralizes unrelated validation policies. |

Every production type newly crossing roughly 1,000 lines, and every smaller
type acquiring several independently testable concerns, must be added here as
Assigned, Ready, dependency-blocked, or a justified cohesive exception before
its change is accepted.

## Product-readiness assessment

| Area | Current assessment | Principal remaining gate |
| --- | --- | --- |
| Visible UI and controller behavior | Many focused corrections are integrated, but several user-reported navigation, clipping, transition, shortcut, and recovery issues remain Verifying. | Fresh accepted Release plus user/live computer-control verification; visible defects stay ahead of internal cleanup. |
| Launcher platform | Data-only pack validation and four native responsive presets are accepted. Normalized managed presentation is returned for correction; Windows/Xbox import is active. | DLV-142, accepted DLV-138, DLV-136 live smoke, launcher style/recovery, production state projection, then author tooling. |
| Widget SDK and author journey | Strong local lifecycle/state/navigation/capability/scaffold/package foundations. | External versioned consumption, isolated semantic preview, broader advanced-widget reference, publisher/update governance. |
| Installed-widget security | Bounded threat-model gate is closed and frozen. Full-application widgets retain private scale while shared-host traffic/resources stay bounded. | New implementation only for reproducible P0, demonstrated threat violation, or planned-release blocker. |
| Reliability | Typed lifecycle, stale-result, bounded retry, retained-last-good, and failure routes are widely tested. | Packaged repeated crash/provider failure and restart evidence for flagship widgets. |
| Accessibility | Deterministic semantic and real-host UIA coverage is substantial. | Physical Narrator/MSAA, controller, scaling, and assistive-technology evidence. |
| Performance | Bounded queues/caches/snapshots and event-driven helpers exist; transition logs expose some expensive first paints. | Named hidden/idle/interactive CPU/GPU/memory/latency baselines and regression budgets on accepted artifacts. |
| Verification | Focused suites are credible and tiered; exact aggregate runs are intentionally rare. | One clean immutable-input Tier-3 result at the next named checkpoint, not per milestone. |
| Authentication/hardware | Correctly isolated from unrelated work. | User-authorized accounts and physical audio/Bluetooth/controller/display/game evidence. |

## Review and acceptance rules

- Review the actual commit diff and retained primary evidence, never only an
  implementation summary.
- A user-visible regression stays open until the packaged accepted path has
  proportional evidence and a live verdict where needed.
- Keep strict bounds on every resource entering or owned by the shared host;
  do not impose arbitrary quotas on widget-private application complexity.
- Reject compatibility facades and duplicate models that exist only for this
  pre-release user's obsolete local state. Reset only affected overlay-owned
  state atomically when a breaking schema requires it.
- Do not accept cosmetic file splitting, partial declarations, wrappers, or
  pattern names as architecture improvement.
- Do not turn capture failures into product work.
- Do not run or require the canonical aggregate unless the assignment names a
  Tier-3 checkpoint or changes the verifier/core boundary.
- Implementation tasks never edit this review.

## Immediate review priorities

1. Review committed DLV-138 for supported Windows game classification, exact
   source health, bounded cancellation/reconciliation, deduplication, and
   opaque launch authority.
2. Review committed DLV-139 for bounded opt-in Epic parsing, source health,
   settings reachability, and fresh launch authority.
3. Review DLV-142 before allowing DLV-130/138/139 history into
   main.
4. Continue visible DLV-133 now; keep DLV-134 and author tooling dependency-
   ordered behind accepted platform and managed contracts.
5. Rotate the next deeper audit to live UI/UX and widget authoring. Revisit
   installed-widget security only under its explicit stabilization exception.

If no implementation or live evidence changes, record no material review
delta rather than manufacturing a finding.
