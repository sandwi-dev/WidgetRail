# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical review and assignment detail through planner commit `436d890` is in
the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-compaction plan is in the
[2026-08-14 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baseline

- Local production main: `8836e07` (`[DLV-221] Integrate Taffy native layout
  engine`). The user physically reviewed the rebuilt Release, found the
  integration substantially correct, and accepted Taffy as the native
  declarative geometry engine.
- DLV-221 preserves the existing Widget SDK/protocol, package/catalog/runtime,
  authenticated WidgetBridge transport, lifecycle/trust/persistence/providers,
  widget domains, Community process boundaries, native renderer, GameInput,
  controller focus/navigation, UI Automation, scrolling, clipping, motion,
  and single-HWND ownership. Only generic Flex/Responsive Grid geometry moved
  behind the pinned Taffy Rust static library and narrow panic-safe C ABI.
- DLV-221 focused evidence is green: Rust 5/5, native declarative layout 250,
  renderer 4,839, all eight production widgets, controller/focus/slider/UIA
  suites, bounded semantic-churn and hidden/idle measurements, and normal
  zero-process shutdown. The exact canonical aggregate stopped at its first
  verifier self-test because the manifest omits the existing
  `SpotifyCommunityApplication.Tests` project. No product test ran or failed in
  that aggregate. Retain this honestly red infrastructure result; do not rerun
  it unchanged or weaken the verifier.
- The user reported five post-integration presentation issues. They are open
  product defects even though the Taffy replacement itself is accepted:
  variable widget-to-tray separation, Settings root dead height, Audio Mixer
  rows/sliders not consuming width, insufficient Network first-page height,
  and detached/weak YT Music composition.

## Avalonia disposition — failed and closed

The user ended the Avalonia experiment on 2026-08-14 after repeated physical
layout, shell, controller-routing, process, and reliability failures. The
candidate did not satisfy the primary reason for the evaluation and is a failed
product experiment. AVP-005 and every Avalonia production cutover are
cancelled.

The accepted AVP-004 extraction and prototype commits remain in Git and
`experiments/AvaloniaOverlayPrototype` only as historical/reference evidence.
They are not an active lane, baseline, migration path, verification debt, or
launch target. Do not dispatch the Avalonia lead or temporary extraction tasks,
do not relaunch the candidate, and do not delete retained source/history unless
the user separately authorizes repository cleanup. The native overlay is the
only production presentation path.

## Execution rules

- Operate exactly two production lanes: `widgets` and `platform`.
- Each task implements only its lane's current Assigned milestone, then the
  first explicitly Ready same-lane milestone whose baseline is present.
- Implementation tasks never edit reviewer-owned documents. The planner
  independently reviews actual diffs and retained evidence.
- Rejected commits remain unintegrated. Corrections stay in their lane and do
  not interrupt unrelated coherent work.
- Never push. Stop for credentials, destructive recovery, substantial merge
  conflicts, undocumented input/window APIs, publication, physical-only
  evidence, or a material product choice.
- User-visible defects and requested features outrank internal refactors.
- Screenshots are high-value user evidence but are not authority to build or
  repair a capture harness. Verify the named geometry and semantic invariants,
  rebuild and visibly launch the accepted Release, and use the user's physical
  verdict for final presentation quality.
- Run focused affected Release suites during implementation. Use one bounded
  linked-host group when a language/process boundary changes. Run the canonical
  aggregate only at a named checkpoint; never rerun an unchanged red result.
- New managed test projects use MSTest.Sdk 4.3.2. Existing executable suites
  remain valid unless their migration is explicitly assigned.
- Full-trust Community applications may use ordinary user-level APIs in their
  own process. Bound shared product inputs/resources, not private application
  CPU, memory, databases, files, sockets, dependencies, or child processes.

## Product and architecture decisions

- Games & Apps remains bundled. Spotify, Game Launcher, and YT Music are
  ordinary Community applications. Core assemblies contain no service-specific
  DTOs, API clients, process hosts, package identities, or known-tree rules.
- The native presentation boundary is authoritative. Retain Widget
  SDK/protocol, catalog/package/runtime, WidgetBridge/authenticated transport,
  lifecycle/trust/persistence/providers, domain implementations, Community
  process boundaries, native rendering/accessibility, and the original
  controller focus/navigation owner.
- Taffy is the sole production declarative Flex/Responsive Grid geometry engine.
  Do not restore the deleted custom solver, add a dual-runtime path, or introduce
  per-widget native geometry, identity branches, tree-shape special cases,
  another renderer, another focus graph, or another input owner.
- Taffy owns geometry calculation only. The host retains semantic validation,
  intrinsic DirectWrite measurement, scroll offsets/extents, ancestor clipping,
  visible rectangles, physical-pixel/DPI snapping, focus-follow, controller
  navigation, accessibility projection, rendering, animation, and HWND
  placement.
- Widget surface sizing is an authored semantic contract, not a global shell
  preset. The public contract will expose symmetric independent width and height
  modes: `Preferred`, `Content`, and `FillAvailable`.
  - `Preferred` uses the validated preferred axis extent and remains stable as
    live data changes.
  - `Content` uses the Taffy-measured intrinsic extent clamped between the
    authored minimum and preferred extent; the preferred extent is the ceiling.
  - `FillAvailable` consumes the safe host-admitted work-area extent.
  - Width and height have equal API capability. A view may deliberately select
    different policies because responsive text/grid height is computed from an
    admitted width.
- Content sizing uses a bounded two-pass host process: admit width/work-area
  constraints, measure the root with automatic content height, clamp the
  measured extent, add host chrome reservations, bottom-anchor the resulting
  window, then perform final layout at the admitted viewport. No widget ID,
  page ID, style class, or known tree shape participates in this algorithm.
- The persistent tray and controller guide are host chrome at fixed absolute
  bottom-center screen coordinates for the complete visible session. Widget
  width/height changes move the content envelope upward/outward around that
  anchor. The panel bottom, guide, and tray use explicit fixed spacing; a short
  widget may not remain top-anchored and create variable dead space.
- One HWND wraps the admitted content-plus-chrome union. The overlay must not
  become a monitor-sized desktop surface. Monitor work area, DPI, accessibility,
  safe insets, and bounded safety limits remain host authority.
- Container child alignment and the container's own `align-self` are separate
  semantics. `align: center` on a Row centers its children; it must not make the
  Row content-width inside a stretching parent. Explicit width/aspect-ratio
  semantics may opt a node out of cross-axis stretch generically.
- The Microsoft GameInput/Guide owner remains authoritative. No second C#
  GameInput reader, bridge transport, overlay HWND, or focus tree is permitted.

## Active task map

| Lane | Task | Branch/worktree | Current state |
| --- | --- | --- | --- |
| Widgets | Implementation agent — widgets lane | Preserve accepted DLV-217 branch `codex/impl-widgets-community-launcher`; start the UI queue from clean main `8836e07` in a separate planner-authorized widget worktree/branch | DLV-223 Assigned after task/worktree rebinding |
| Platform | Implementation agent — platform lane | Planner-authorized clean platform worktree/branch from `8836e07`; do not reuse preserved dirty/interrupted platform worktrees | DLV-222 Assigned |

DLV-217 remains accepted through `d57fd06` but unintegrated because its exact
aggregate is honestly 40/41 with one reviewer-history-link failure. Preserve
that branch. Integration still requires the user's separate explicit approval
and must not be mixed with this UI correction cluster.

## Platform lane

### Assigned — DLV-222: restore native surface anchoring and correct Taffy stretch

Baseline: clean local main `8836e07`. Owner: native platform/layout lane.

Visible objective: fix the detached panel/tray relationship, Audio Mixer dead
row width, and Network's insufficient first-page height without widget-specific
native rules or moving the tray.

Required implementation:

- In `DeclarativeRenderer` and the Taffy bridge, separate a container's
  `align-items`/child alignment from that node's own `align-self`. Ordinary auto-
  width children stretch in a stretching parent even when their own children
  are centered. Definite width, aspect ratio, or another explicit generic
  sizing rule may bound the node.
- Add a direct regression in which a Row with centered children still fills its
  parent and a `flex-grow: 1` slider consumes the remaining row width. Cover
  nested column/row behavior and constrained widths without Audio/package IDs.
- Stop bypassing the already-resolved widget `windowWidthDip/windowHeightDip`
  with the fixed `1180x700` widget extent. Restore validated widget-owned
  preferred envelopes while preserving one bottom-centered work-area placement
  and one fixed composition owner.
- Bottom-anchor variable-height content. Restore an explicit invariant between
  the visible panel bottom, controller guide, and tray rather than ending the
  card at a top-anchored `footerY` and describing the result as detached.
- Keep tray and guide absolute screen bounds unchanged through compact,
  standard, wide, and height-only widget switches. Width changes remain centered
  on the same screen anchor. Clamp only to the active monitor work area and
  accessibility/safe margins.
- Allow the existing Network `560x700` preferred panel to receive its authored
  height when the work area permits. At constrained height, retain scrolling
  and focus reveal rather than clipping or moving chrome.
- Preserve composition continuity and one HWND. Do not revive Avalonia, add a
  second panel/tray window, or modify widget presentations in this milestone.

Acceptance:

- Generic layout tests prove parent stretch versus child centering and exact
  remaining-space slider growth.
- Placement tests prove a fixed panel-to-guide and guide-to-tray offset, stable
  absolute tray/guide bounds across every current widget extent, and work-area
  containment at mixed DPI/interface scale.
- The real eight-widget switch group retains one renderer, one tray capacity,
  controller focus memory, no clipping, and normal shutdown.
- Audio Mixer sliders reach their intended row width. Network's first-page
  `Ready to scan` state is visible at the preferred envelope and remains
  reachable by scrolling when constrained.
- Tier 1: Rust bridge/layout, DeclarativeLayout, DeclarativeRenderer,
  OverlayPlacement, tray, focus, controller navigation, slider, accessibility,
  and Release host build.
- Tier 2: one bounded linked-host all-eight-widget switch/placement group. No
  canonical aggregate; DLV-221 already paid the core-engine checkpoint and the
  known manifest defect is unchanged.
- Rebuild/package the exact accepted Release and leave it visibly running for
  the user's panel/tray, Audio, Network, motion, and controller verdict.

Out of scope: public protocol/schema changes, Settings/YT semantic changes,
widget identities in native code, another layout engine, another HWND/focus/
input owner, Avalonia work, credentials, or verifier-manifest repair.

### Ready after DLV-222 integration — DLV-224: symmetric surface-axis sizing

Baseline: accepted DLV-222 integrated into local main. Owner: serialized
cross-component assignment led by the platform lane. No widgets-lane task may
edit the same protocol/native files concurrently.

Objective: add the generic `Preferred`, `Content`, and `FillAvailable` width and
height policies and the bounded Taffy intrinsic-measure/admission path described
in Product and architecture decisions.

Required implementation:

- Version the public `WidgetSurfaceHints` schema compatibly and add one shared
  typed axis-mode enum used independently by width and height. Existing views
  default to `Preferred` with unchanged behavior.
- Validate illegal/missing values at the managed boundary and parse them once
  into the native surface request. No stringly page/identity inference.
- Implement content measurement with a definite admitted width, automatic
  block extent, authored minimum/preferred clamps, host chrome reservation,
  work-area clamping, and one final layout. Bound node counts, extents, passes,
  errors, and retained results; preserve last-good presentation on invalid
  submissions.
- Prevent live-data resize churn: only a view explicitly declaring `Content`
  uses measured sizing. `Preferred` stays stable; `FillAvailable` follows only
  admitted work-area/accessibility changes.
- Preserve focus, scroll offsets, transition cancellation/restoration,
  accessibility bounds, tray stationarity, and normal close across extent
  changes.
- Document the contract with copyable examples and explain that responsive
  width is normally admitted before intrinsic height.

Acceptance:

- Managed validator/round-trip and C++ parser tests cover every mode, defaults,
  malformed input, bounds, and protocol-version behavior.
- Generic two-pass tests cover content smaller than preferred, content between
  bounds, overflow capped at preferred/work area, responsive grid reflow,
  wrapped text, and FillAvailable on both axes.
- Transition tests prove only the content envelope moves while tray/guide screen
  coordinates remain fixed.
- Tier 1 affected managed protocol/SDK, Rust/native layout, renderer, placement,
  focus/UIA, documentation, and Release build.
- Tier 2 one bounded managed-snapshot-to-native-host group. This public cross-
  process schema change is the next named Tier-3 checkpoint; run the canonical
  aggregate exactly once from the clean coherent commit and retain any unrelated
  verifier failure honestly.

Stop if intrinsic sizing requires widget-specific native knowledge, more than
two layout passes, an unbounded retained tree, or a material change to tray,
focus, scrolling, or accessibility authority.

### Ready after DLV-224 — DLV-206: truthful performance provenance

Correct the rejected DLV-200 evidence without expanding measurement scope:
retain root PID/start, exact commit/SHA, scenario/profile, child roles, and
available/unavailable metrics; give the eight-widget run separate provenance;
anchor composition lookup after paint; remove false ordinary-host-live wording.
Run only affected bounded performance/temporal routes.

### Awaiting DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths
only after both autonomous Community packages are integrated. Retain generic
App Library behavior for bundled Games & Apps and consenting sandboxed users.
Add an architecture check rejecting Community identities/domain types in core.
Do not delete credentials, provider data, accounts, or user files.

## Widgets lane

### Assigned — DLV-223: responsive YT Music controller composition

Baseline: clean local main `8836e07`. Owner: widgets lane, limited to the YT
Music Community package/presentation/styles and directly affected tests/docs.
It may proceed independently of DLV-222 because it owns no native files.

Visible objective: make YT Music read as one cohesive controller-native media
panel rather than artwork and controls floating at the left of a large detached
surface.

Required implementation:

- Keep one semantic tree, existing public controls/actions, Community package
  boundary, lifecycle, optimistic reconciliation, and explicit focus links.
- At wide/standard width, compose artwork on the left and one right-hand column
  containing title/artist/album, progress, primary transport actions, and
  secondary actions. Align control rows within the metadata column.
- At constrained compact width, reflow to a vertical composition with readable
  metadata, full-width progress, centered reachable controls, and no duplicate
  page/tree.
- Use only typed semantic Row/Stack/Scroll and GBSS responsive behavior. No
  widget-specific native renderer/layout rule and no fixed monitor-resolution
  check.
- Keep touch targets, focus order, LB/RB/X/Y shortcuts, A/B behavior, labels,
  selected/busy states, artwork fallback, and scroll reveal truthful.
- Do not change surface-axis protocol fields in this milestone. YT Music may opt
  into Content height only after DLV-224 is integrated and separately reviewed.

Acceptance:

- Focused YT Music tests cover wide and constrained composition, stable semantic
  identities, focus adjacency, shortcuts, empty/loading/error states, and
  deterministic fakes.
- Existing Community package isolation/install/update/remove evidence remains
  green.
- Native generic renderer fixtures show all authored controls within bounds at
  representative compact/standard widths without identity-specific code.
- Tier 1 only: YT Music package/tests, affected conformance/package checks, and
  the smallest renderer scenario. No aggregate.
- After DLV-222 and DLV-223 are both accepted/integrated, rebuild/package one
  coherent Release for the user's YT Music and tray-spacing verdict.

Out of scope: host placement, Taffy/native edits, public schema changes,
controller routing redesign, companion/auth changes, another page, or Avalonia.

### Ready after DLV-224 integration — DLV-225: content-sized Settings root

Baseline: DLV-224 integrated into local main. Owner: widgets lane.

Visible objective: remove unused Settings root height through measured content,
not a guessed replacement height.

Required implementation:

- Set Settings root `WidthMode = Preferred` and `HeightMode = Content` with the
  existing preferred height retained as the ceiling and minimum height retained
  as the floor.
- Keep deeper Settings pages `Preferred` unless direct evidence shows a page is
  static and benefits from Content sizing. Do not make scroll-heavy pages resize
  as rows or diagnostics change.
- Give the root category list a distinct class/structure that does not request
  `flex-grow: 1; flex-basis: 0`. Preserve the responsive two-column intent,
  one-column reflow, category order, Reset styling, focus navigation, and active
  input scope.
- Do not hard-code a new root pixel height or add a Settings identity rule to
  the host.

Acceptance:

- Taffy-measured root height equals its visible content plus authored spacing,
  remains between minimum and preferred bounds, and leaves no material dead
  area below Reset.
- Compact width reflows to one column and expands height within the same content
  policy; constrained height scrolls/reveals every category.
- Root/deeper-page transitions keep tray/guide screen bounds fixed and preserve
  focus/back behavior.
- Tier 1 Settings tests, managed surface contract, focused native renderer/
  placement scenario, and Release package. No aggregate.

### Ready after DLV-225 — DLV-226: eight-widget surface-policy audit

Audit every current widget's width/height mode and authored min/preferred
extent after the new contract is physically accepted. Change only policies
supported by direct first-page evidence. Dynamic provider/list widgets remain
Preferred unless resizing is demonstrably beneficial and stable. Retain a
concise contract table in public widget-authoring documentation and run focused
surface/conformance checks only.

## Serialized integration order

1. DLV-221 is accepted and integrated in local main as `8836e07`.
2. DLV-222 platform geometry/stretch and DLV-223 YT Music composition may run
   concurrently from `8836e07`; their file ownership must not overlap.
3. Review and integrate accepted DLV-222 and DLV-223 independently. Rebuild one
   coherent Release after both are present for the named physical verdict.
4. DLV-224 is the sole serialized shared protocol/native assignment. Pause any
   widget work touching `WidgetSurfaceHints` until it is accepted/integrated.
5. DLV-225 adopts Content height in Settings. DLV-226 audits later policies.
6. DLV-217 integration remains a separate explicit user decision. DLV-218 may
   begin only after DLV-217 is integrated. DLV-206 remains independent after the
   UI/sizing cluster.

## Manual and packaged verification queue

- User verdict on each freshly launched accepted native Release remains
  authoritative for panel/tray cohesion, controller feel, motion, Audio slider
  sizing, Network first-page visibility, Settings dead space, and YT Music
  composition.
- Physical controller/display evidence remains required for changed navigation,
  focus reveal, or visual presentation. It is not Avalonia acceptance debt.
- Live Spotify account/Premium/Web Playback/EME/OAuth are credential-gated.
- Live IGDB and SteamGridDB enrichment are credential-gated; offline launcher
  behavior must not depend on them.
- Computer control may omit the no-taskbar overlay; use exact HWND/UIA/log
  fallback rather than changing taskbar behavior.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| Avalonia migration/cutover | Failed and cancelled by user decision. | None. A new experiment requires a new explicit user decision; do not resume old AVP work. |
| DLV-217 local integration | Exact aggregate is 40/41 with one known reviewer-history-link red. | Explicit user approval to integrate despite that honest documentation-only red step. |
| Trusted fixed-video/PiP | Paused WebView2 measured about 348.7 MiB private and 4% CPU against prior gate. | User changes budget or authorizes content/process experiment. |
| Audio default-device selection | No documented supported Windows setter established. | Primary Microsoft API plus reversible provider/hardware plan. |
| Direct computer-control discovery | No-taskbar overlay is omitted from tool discovery. | Tool gains tool-window discovery or user accepts taskbar/Alt-Tab presence. |
| Native uninstall reconciliation | Synthetic catalog removal emitted no managed revision/native event. | Deterministic disabled/nonresident removal event. |
| YouTube authenticated library | Google OAuth/account; Watch Later is unsupported by Data API. | Approved minimum-scope OAuth plan and authorized account. |

## Recent accepted milestones

| Milestone | Accepted result |
| --- | --- |
| DLV-221 | `8836e07`: pinned Taffy 0.12.2 static geometry engine, narrow Rust/C ABI, old custom solver removed, focused native/Rust/eight-widget/performance evidence accepted; canonical aggregate retained red at the pre-product manifest self-check. |
| DLV-217 | Autonomous Game Launcher accepted through `d57fd06`; integration awaits explicit approval for the known documentation-only red aggregate step. |
| DLV-216 | Autonomous Spotify Community package accepted and integrated. |
| DLV-220 | Correct retired gesture revocation evidence. |
| DLV-210 | Contained generic Hero Rail while retaining launch/action/focus authority. |
| DLV-215 | Generic consented full-trust Community application runtime. |

Do not mark the continuing delivery goal complete because these milestones
closed. Continue until the user pauses/replaces it or all useful lanes reach a
genuine stop condition. Never push.
