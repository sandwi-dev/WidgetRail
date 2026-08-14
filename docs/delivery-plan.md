# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical review and assignment detail through planner commit `436d890` is in
the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete pre-compaction plan is in the
[2026-08-14 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baseline

- Local main contains accepted DLV-224 through correction integration
  `02750ba` over provisional product commit `c2b6456`. The correction preserves
  omitted native snapshot versions as legacy v1 while rejecting present
  malformed, fractional, or unsupported versions before conversion. Accepted
  DLV-222 `e8af5be` and DLV-223 through `6b916e8` remain integrated beneath it;
  accepted DLV-221 `8836e07` remains the Taffy engine baseline. The user
  physically reviewed the rebuilt DLV-221 Release, found the integration
  substantially correct, and accepted Taffy as the native declarative geometry
  engine.
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
- The coherent DLV-222/DLV-223 Release had Community YT Music 0.2.8 enabled and
  ran cleanly as planner-owned PID 27128. It exited normally through `WM_CLOSE`
  when the planner began the DLV-224 rebuild. Corrected main now compiles and
  packages the complete changed native/managed graph and passes the previously
  failing `RealHostAccessibilityTests` route with 286 checks. The later
  `WidgetActionFailureHostTests` clean-environment fixture remains honestly red:
  its temporary installation copies `OverlayHost.exe` and `runtime` but omits
  the executable's required `OverlayPlatformInterop.dll`, so the child cannot
  create its HWND unless an unrelated PATH happens to supply the DLL. The
  platform worktree's exact packaged build passed, and this unrelated retained
  harness defect does not invalidate DLV-224 product behavior. Repair it only
  through the bounded platform assignment below; do not reinterpret an
  unchanged rerun as product evidence.

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
| Widgets | Implementation agent — widgets lane | `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` on `codex/impl-widgets-taffy-ui`, accepted DLV-223 remains preserved through `29ec257`; accepted DLV-217 remains preserved on `codex/impl-widgets-community-launcher` | DLV-225 assigned after integrating accepted main `02750ba` |
| Platform | Implementation agent — platform lane | `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` on `codex/impl-platform-taffy-ui`, clean through DLV-224 correction `912aea9`; prior DLV-220 history remains preserved on `codex/impl-platform-community` | DLV-206 assigned; DLV-227 is next Ready |

DLV-217 remains accepted through `d57fd06` but unintegrated because its exact
aggregate is honestly 40/41 with one reviewer-history-link failure. Preserve
that branch. Integration still requires the user's separate explicit approval
and must not be mixed with this UI correction cluster.

## Platform lane

### Done — DLV-222: restore native surface anchoring and correct Taffy stretch

Accepted as `e8af5be` and integrated with DLV-223 through merge `073e423`.
Generic Taffy translation now separates child alignment from inherited parent
stretch; authored widget envelopes drive the one HWND; variable content is
bottom-anchored around fixed guide/tray chrome; and the bounded 560-DIP tray
band retains the current eight items at controller-safe sizes. Focused evidence
is green for Rust/Taffy, layout, renderer, placement, tray, controller, slider,
focus, accessibility/UIA, Audio Mixer 45/45, Network Controls 24/24, and the
real eight-widget linked-host group with clean shutdown and bounded composition
timing. Packaged physical review remains pending on the coherent Release.

### Done — DLV-224: symmetric surface-axis sizing

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

Accepted as implementation correction `912aea9`, integrated into main as
`02750ba`. Missing native `protocolVersion` retains the v1 default; a present
value must be numeric, finite, integral, and within `[1,17]`. Direct native
coverage includes omitted v1, present v17 independent axes, malformed,
fractional, below-range, above-range, and retained last-good behavior.
`RealHostAccessibilityTests` passes 286 checks and the platform worktree's clean
packaged Release build passed. The one exact `01ff871` Tier-3 result remains
unchanged and honestly red at the known pre-product verifier-manifest check.
Independent main packaging reproduced the complete artifact graph and the 286-
check parser route, then stopped at the unrelated DLV-227 fixture defect.

### Assigned — DLV-206: truthful performance provenance

Correct the rejected DLV-200 evidence without expanding measurement scope:
retain root PID/start, exact commit/SHA, scenario/profile, child roles, and
available/unavailable metrics; give the eight-widget run separate provenance;
anchor composition lookup after paint; remove false ordinary-host-live wording.
Run only affected bounded performance/temporal routes.

### Ready after DLV-206 — DLV-227: self-contained production-host fixture

Baseline: accepted corrected DLV-224 integrated into local main. Owner: platform
lane. This is bounded test-infrastructure reliability work and must follow the
assigned performance milestone; it may not preempt DLV-225 visible widget work.

Objective: make `WidgetActionFailureHostTests` launch the same self-contained
native installation it claims to exercise.

Required implementation and acceptance:

- Copy the exact required native runtime dependencies beside the temporary
  `OverlayHost.exe`, including `OverlayPlatformInterop.dll`, from the admitted
  installation. Do not modify PATH, load dependencies from the source worktree,
  weaken HWND visibility, or broaden the fixture into installation tooling.
- Fail before launch with a precise missing-dependency diagnostic and retain
  normal job-owned teardown and temporary-directory cleanup.
- Add one direct dependency-copy/omission regression, run
  `WidgetActionFailureHostTests` from an environment whose PATH does not contain
  the Release directory, then run only the affected packaged build segment.
- Do not rerun the unchanged Tier-3 aggregate.

### Awaiting DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths
only after both autonomous Community packages are integrated. Retain generic
App Library behavior for bundled Games & Apps and consenting sandboxed users.
Add an architecture check rejecting Community identities/domain types in core.
Do not delete credentials, provider data, accounts, or user files.

## Widgets lane

### Done — DLV-223: responsive YT Music controller composition

Accepted and integrated through main `6b916e8` (`d423da0` product/package and
`6b916e8` exact renderer-evidence correction). YT Music 0.2.8 now keeps one
responsive semantic tree with artwork beside a unified details/control column
at 760x440 and above that same full-width column at 480x340. Existing actions,
focus links, shortcuts, lifecycle, authentication, and Community isolation are
unchanged. Focused evidence is green for YT Music 60/60, the isolated package
lifecycle, the generic renderer suite, and the dedicated real-snapshot/native-
renderer scenario 79/79. Physical composition review remains queued for the
coherent DLV-222 plus DLV-223 Release.

### Assigned — DLV-225: content-sized Settings root

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

### Ready after DLV-226 — DLV-228: Audio Mixer width consumption

Use authored generic stretch/flex semantics so session rows, value tracks, and
sliders consume the admitted content width again. Preserve labels, values,
controller adjustment, focus, scrolling, and compact reflow. Do not add native
Audio identity rules or a guessed widget width. Verify the first-page master and
session controls at compact and preferred widths with the focused Audio and
native renderer suites.

### Ready after DLV-228 — DLV-229: Network first-page vertical admission

Choose and document the Network root's symmetric surface policy and authored
minimum/preferred height so the primary scan status/action is visible on the
first page at its preferred envelope while compact/constrained heights remain
scroll-reachable. Do not hide provider state, force Content sizing on dynamic
lists, or add a native Network special case. Run focused Network, surface,
focus-reveal, and renderer checks.

### Ready after DLV-229 — DLV-230: YT Music panel cohesion

Refine the single responsive Community YT Music semantic composition so its
content envelope feels visually attached to fixed host chrome, uses available
space intentionally, and preserves the accepted side-by-side/stacked reflow,
all eight actions, explicit focus adjacency, lifecycle, and Community
isolation. Do not change the fixed host offsets per widget or introduce another
page tree. Run focused YT Music, real-snapshot/native-renderer, surface, and
package lifecycle evidence; physical composition remains the final verdict.

## Serialized integration order

1. DLV-221 is accepted and integrated as product commit `8836e07`.
2. DLV-222 `e8af5be` and DLV-223 through `6b916e8` are accepted and integrated
   coherently through merge `073e423`.
3. The coherent Release was rebuilt, packaged, and visibly launched as PID
   27128 with YT Music 0.2.8 current. Exact-session startup diagnostics are
   clean; the named physical tray, Audio, Network, Settings, and YT Music verdict
   remains pending because the computer-control service omits the tool window.
4. DLV-224 is accepted through correction `912aea9` and integrated as main
   `02750ba`. Its clean platform build passed; independent main packaging passed
   the corrected 286-check parser route and retained the unrelated DLV-227
   fixture failure honestly.
5. DLV-225 now adopts Content height in Settings. DLV-226 audits later policies,
   followed by the explicit Audio, Network, and YT Music visible corrections.
6. DLV-206 may run independently on the platform lane now. DLV-217 integration
   remains a separate explicit user decision, and DLV-218 may begin only after
   DLV-217 is integrated.

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
| `WidgetActionFailureHostTests` self-contained launch | Temporary fixture omits required `OverlayPlatformInterop.dll`; reproducible clean-environment startup cannot create an HWND. | DLV-227 copies and validates admitted native dependencies, then passes without a Release-directory PATH entry. |
| YouTube authenticated library | Google OAuth/account; Watch Later is unsupported by Data API. | Approved minimum-scope OAuth plan and authorized account. |

## Recent accepted milestones

| Milestone | Accepted result |
| --- | --- |
| DLV-224 | `912aea9`, integrated as `02750ba`: independent Preferred/Content/FillAvailable axes, bounded Taffy intrinsic sizing, legacy native v1 omission preserved, malformed versions fail closed, and 286/286 direct native parser/accessibility checks. |
| DLV-222 | `e8af5be`, integrated by `073e423`: generic Taffy stretch correction, authored variable surfaces, bottom-anchored panel/guide/tray, stable full-capacity tray band, and green focused plus eight-widget linked-host evidence. |
| DLV-223 | `6b916e8`: responsive YT Music 0.2.8 composition plus real managed-snapshot/GBSS-to-native-renderer proof at 760x440 and 480x340; 79/79 dedicated checks. |
| DLV-221 | `8836e07`: pinned Taffy 0.12.2 static geometry engine, narrow Rust/C ABI, old custom solver removed, focused native/Rust/eight-widget/performance evidence accepted; canonical aggregate retained red at the pre-product manifest self-check. |
| DLV-217 | Autonomous Game Launcher accepted through `d57fd06`; integration awaits explicit approval for the known documentation-only red aggregate step. |
| DLV-216 | Autonomous Spotify Community package accepted and integrated. |
| DLV-220 | Correct retired gesture revocation evidence. |
| DLV-210 | Contained generic Hero Rail while retaining launch/action/focus authority. |
| DLV-215 | Generic consented full-trust Community application runtime. |

Do not mark the continuing delivery goal complete because these milestones
closed. Continue until the user pauses/replaces it or all useful lanes reach a
genuine stop condition. Never push.
