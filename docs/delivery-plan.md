# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-12
Planning owner: independent review and delivery-planning agent
Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[`history/delivery-plan/2026-08-11T20-01-54-07-00.md`](history/delivery-plan/2026-08-11T20-01-54-07-00.md).
Historical snapshots are evidence only and are never implementation authority.

## Queue protocol

- Each task has one stable lane identity and executes only that lane.
- Each lane has at most one `Assigned` milestone and an ordered `Ready` queue.
- After committing, a task immediately takes the first executable same-lane
  `Ready` item; planner review is asynchronous.
- Tasks never reorder, merge, broaden, skip, or invent assignments, and never
  select work from the roadmap, issue ledger, reviews, status, or comments.
- Every assignment normally produces one coherent local `[DLV-nnn]` commit.
  Nothing is pushed.
- Tasks update directly affected public documentation and
  `docs/implementation-status.md`; they never edit reviewer-owned files.
- A reproducible P0 may interrupt. Ordinary review corrections are inserted
  after the milestone already in progress and do not interrupt it.
- The planner integrates only independently accepted contiguous history.

### Visible-outcome priority

1. Reproduced user-visible P0/P1 defects and requested features.
2. The smallest shared prerequisite directly unlocking a named visible result.
3. Packaged usability, accessibility, responsiveness, reliability, and measured
   performance work with an observable outcome.
4. Internal refactoring, test organization, and documentation cleanup.

At least one lane owns a visible outcome or immediate prerequisite while safe
visible work exists. Both lanes never run internal-only work in that condition,
and no lane takes consecutive internal milestones without a named visible or
release blocker.

### Architecture non-regression

Production types above roughly 1,000 physical lines, logical partial types
across all declarations, and smaller types owning several independently
testable concerns are review hotspots. Every hotspot needs an Assigned, Ready,
dependency-blocked, or cohesive-exception disposition. Touching one requires a
before/after responsibility map. File splitting, cosmetic partials, wrappers,
or named patterns do not close a finding unless shared mutable knowledge is
reduced or a focused policy/test boundary is created.

### Branch and integration protocol

- `widgets`: `codex/impl-widgets` in its isolated Codex worktree.
- `platform`: `codex/impl-platform-switch` in
  `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative`.
- Local `main` is planner-owned integration.
- `codex/impl-platform-recovery` remains preserved at `7e64b4d` with the
  non-integrable DLV-062 checkpoint and must not be merged.
- The interrupted `codex/impl-platform-visible` worktree is read-only preserved
  state; the former `codex/impl-platform` worktree is absent.
- A lane consumes new `main` only at a clean committed boundary after explicit
  bounded planner instruction. Substantial conflicts stop for the user.
- Shared protocol/architecture work is serialized to one lead lane.

### Verification tiers

- **Tier 1:** affected Release build and directly affected deterministic suites.
- **Tier 2:** smallest cross-component group covering the changed boundary.
- **Tier 3:** only a named checkpoint, verifier/core-protocol/security change,
  concrete planner-requested risk, or release gate.

Every command has a bounded timeout. Do not repeat an unchanged failing command
or run the same aggregate on a dirty tree and its exact commit. New managed test
projects use `MSTest.Sdk` 4.3.2; existing executable suites retain their current
runners. Exclude malformed capture immediately; do not debug capture tooling
during product work. Functional/state/semantic/accessibility/timing/resource
evidence plus the freshly launched accepted Release are the normal gate.

## Widgets lane

Task identity: `widgets`
Branch: `codex/impl-widgets`

### DLV-126 — Restore Game Launcher controls, shortcuts, and collection continuation

**State:** Accepted as `a45166c`, integrated through `2c58ba7`, rebuilt and
packaged on exact main, and visibly running as planner PID 16824 for the user's
live shortcut/control/continuation verdict
**Baseline/dependencies:** accepted main `8c40a6d`; live user report against
planner-launched Release PID 36488. `overlay.log` repeatedly records
`missing_collection_anchor` at
`$.root.children[3].children[1].collectionAnchorKey` after Game Launcher top-
control actions at 00:14:47 through 00:17:48 on 2026-08-12.
**Owner:** widgets lane for Game Launcher action/presentation/navigation policy,
its authored shortcut/help contract, any genuinely reusable SDK collection-
continuation seam required by the same production widget, focused managed tests,
and directly affected public widget documentation
**Concurrency:** no native tray, compositor, HWND, placement, worker-isolation,
catalog-provider breadth, store integration, reviewer-document, capture, or push
changes.

**User-visible outcome:** every visible Game Launcher top control completes
without replacing the current view with a protocol error; the on-screen
shortcut legend lists only actions that work for the current focused game; and
Down from the last visible game row continues to the next game row/page instead
of escaping prematurely to **Previous page** or the footer controls.

**Objective/scope:** reproduce each current top control (Add games, Add running
app, Hidden, Favorites, Recent, Source/sort, and query clear) through its real
action route and correct the invalid collection anchor at the managed
presentation source. Audit X/Y/LB/RB/View ownership from the exact focused tile
through `OnAction`; remove or disable any help entry that is not currently
actionable. Preserve A activation and B scope behavior. Implement one
deterministic collection-boundary continuation: when more games exist, Down
from the last visible grid row advances and focuses the nearest column in the
next row/page; footer controls remain explicitly reachable after the final
collection row or by an authored navigation route. Prefer an existing generic
SDK collection primitive; extend it only if the required behavior is reusable
and cannot be expressed safely in Game Launcher.

**Acceptance:** all named top controls publish protocol-valid current snapshots
with stable focus and no `missing_collection_anchor`; X/Y/LB/RB/View each
dispatch exactly once to the focused stable game identity and their rendered
help matches enabled state; stale/absent/focused-wrong-generation identities
fail closed with honest feedback; last-row Down loads/advances exactly once,
keeps the closest column, never jumps to page buttons while a next row exists,
and does not loop or duplicate results. Empty, single-row, partial-last-row,
final-page, filter, refresh, and Back cases retain deterministic focus.

**Architecture:** `GameLauncherWidget` remains a hotspot. Provide a before/
after responsibility map and keep collection navigation, action vocabulary,
and snapshot composition in their existing focused policy/presentation owners;
do not add another task registry, lock, generation owner, or monolithic action
branch to the root.

**Verification:** Tier 1 Game Launcher Release tests covering every named
control, exact shortcut dispatch/help, and 1/2/many-page grid continuation;
Tier 2 Widget SDK/protocol/installed-worker validation only if a shared
collection contract changes. Inspect the resulting typed failure log. No
aggregate or capture.

**Stop:** a shortcut fails before managed action admission, the required
navigation behavior needs a native focus-engine contract change, or the Add/
filter/sort behavior requires a new product choice or unsupported provider API.

### Widgets completed milestones

### DLV-125 — Restore the canonical verifier manifest and close DLV-123 evidence

**State:** Accepted as `ccabb0e`, integrated through `418d11f`; the omitted
scenario project now executes exactly once through the canonical manifest. The
single required clean aggregate stopped later at an unrelated Gbar CLI dev-
session timing failure, which is isolated to DLV-129 and must not block visible
Game Launcher work.
**Baseline/dependencies:** accepted DLV-123 `552d250`, integrated through
`8c40a6d`; its focused Release evidence passes Catalog 35/35, diagnostics 17/17,
Settings 57/57, Bridge 83/83, generic-worker conformance 6/6, and docs 59, but
the one exact-commit Tier-3 attempt stopped before product tests because the
verification manifest omits the existing `WidgetScenario.Tests` project
**Owner:** widgets lane for the verification manifest/runner contract and
directly affected verification documentation only
**Concurrency:** no product code, public protocol/API, widget behavior, native
host, package policy, test-framework migration, reviewer documents, capture, or
push.

**Outcome/objective:** restore complete project discovery in the bounded
canonical verifier by adding the existing `MSTest.Sdk` 4.3.2 scenario project
to the manifest with its documented Microsoft Testing Platform invocation.
Do not convert another suite or redesign the verifier.

**Acceptance:** the self-test proves every test project is represented exactly
once; the new step emits machine-readable bounded results and fails closed; the
scenario project passes through both its focused command and manifest-selected
runner path; no existing step ID, lane selection, timeout, or result schema
changes except what is strictly required for this omitted project.

**Verification:** Tier 1 verifier self-test plus only the new manifest-selected
scenario step. Commit the correction, then run one canonical Tier-3 aggregate
from that exact clean commit to close the DLV-123 public cross-process
checkpoint. Do not rerun an unchanged failure and do not run dirty plus clean
aggregates.

**Stop:** the project is intentionally excluded for a documented reason, the
runner cannot invoke it without a material architecture/schema change, or the
aggregate exposes an unrelated product failure requiring planner triage.

### DLV-128 — Ship the first playable Game Launcher console hero rail

**State:** Accepted as `344caa0`, integrated as `67c557d`; focused Game Launcher
evidence passed 62/62 and documentation contracts covered 61 Markdown files.
The complete accepted artifact graph through planner main `d8c803a` is visibly
running as PID 32952 for the user's hero-rail verdict.
**Baseline/dependencies:** accepted DLV-126 collection/action correction and the
approved `docs/game-launcher-requirements.md` M1 console-home contract. Use only
the existing installed-game query, opaque artwork handles, exact SavedId action
routes, declarative SDK primitives, and native responsive renderer.
**Owner:** widgets lane for Game Launcher presentation policy, a small explicit
built-in experience selection owner if required, focused managed tests, and its
direct public behavior documentation
**Concurrency:** no native HWND/compositor/tray/placement changes, provider or
store breadth, network metadata/artwork API, custom-pack schema/CLI, executable
themes, package trust, reviewer documents, capture, credentials, or push.

**User-visible outcome:** opening Game Launcher presents a deliberate console-
home experience: the currently focused game has a strong hero/details region
and the playable library remains a controller-first cover rail. Focus changes
update the hero without changing launch identity, moving the host tray, or
losing the current page/collection position.

**Objective/scope:** implement one built-in `hero-rail` experience over the
current installed-only catalog. Reuse the selected game's trusted artwork handle
as a bounded hero when available and a professional semantic fallback otherwise;
show title, exact source/availability truth, favorite/preferred/variant state,
and the existing primary launch/details actions without inventing metadata.
Keep search, source/filter/sort controls, pagination, details, View/X/Y/LB/RB,
Back, error/loading/warm state, and exact collection continuation functional.
The experience is built-in product presentation, not the future public launcher-
pack format; do not prematurely freeze or expose that schema.

**Acceptance:** empty, loading, retained-warm, error, single-game, 20-game,
long-title, missing-art, hidden/filter, multi-page, launch-busy, and 150%-scale
fixtures retain honest content and deterministic focus. Changing focused tiles
updates only selected presentation state and never dispatches launch. A/Details
and every contextual shortcut still target the exact focused SavedId once;
page/filter/Back restores the nearest valid game and current hero. Snapshot node
count, artwork requests, retained pages, and private state remain bounded.
Game Launcher root/lifecycle owners do not absorb a new layout monolith: provide
a before/after responsibility map and keep hero-rail composition in a focused
presentation component/policy.

**Verification:** Tier 1 Game Launcher Release tests for the named states,
controller action identity, focus-to-hero projection, bounds/node count, and no
network/raw-path authority. Tier 2 Widget SDK/protocol/native layout only if an
existing generic primitive genuinely fails; stop before expanding that scope.
No aggregate, capture, account, external API, or provider test.

**Stop:** the requested hero requires a new trusted artwork kind, shared native
layout primitive, public pack schema, provider metadata, raw path/URL, or a
material choice about replacing the current controls rather than composing them.

### Widgets completed follow-up

### DLV-129 — Make the Gbar dev cancellation fixture deterministic

**State:** Accepted as `7f7d9d5`, integrated as `d792e30`; the named fixture
passed three consecutive focused 1/1 runs without another aggregate
**Baseline/evidence:** the one clean exact-commit aggregate from `ccabb0e`, run
`20260812T074036Z-37ce8dc0`, passed all prior managed steps including new
WidgetScenario 9/9, then Gbar CLI passed 57/58. Only `Dev retains last good and
cleans its process tree on cancellation` timed out waiting for `Ready:` after
the preceding cold dev/process-tree tests. Do not rerun the aggregate.
**Owner/scope:** widgets lane for the Gbar dev-session test seam and production
dev-session lifecycle only if a focused reproduction proves a real product
defect. Diagnose scheduler/build-state dependency using retained stdout/stderr
and a filtered/direct fixture. Preserve the real 90-second product build bound,
last-good retention, cancellation, Job Object tree cleanup, and no compiler-
server leak. Remove wall-clock coupling or cross-test interference at its exact
source; do not merely inflate timeouts.
**Acceptance:** the named fixture passes repeatedly under a bounded focused
command and proves Ready, retained-last-good, cancellation, temporary-catalog
cleanup, and child-host exit. No unrelated Gbar tests, verifier schema changes,
aggregate rerun, product feature work, or broad test migration.

### Widgets current assignment

### DLV-130 — Publish the normalized launcher presentation contract

**State:** Assigned after the widgets lane merges exact accepted main `d792e30`;
shared managed contract work is serialized to this lane and must merge before
DLV-134 consumes it
**Baseline/dependencies:** exact accepted main `d792e30`, including DLV-128 and
DLV-129, plus
`docs/game-launcher-requirements.md` delivery step 1. This is the smallest
public SDK/broker prerequisite for the named console-home and safe-experience
outcomes, not store-adapter or content-operation breadth.
**Owner:** widgets lane for `PlatformBroker` value contracts, Widget Bridge
mapping, public Widget SDK models/services, current Windows-provider projection,
Game Launcher adoption, focused contract tests, and directly affected public
SDK documentation
**Concurrency:** no native layout/paint/focus implementation, pack catalog or
CLI, external metadata/artwork provider, credentials, helper binary, content
operation, reviewer document, capture, or push changes.

**Outcome/objective:** replace the launch-only, single-artwork public projection
with one versioned normalized launcher presentation contract. Preserve opaque
`AppId`/`SavedId` authority while representing exact availability/launch
evidence, source/account health, supported action vocabulary, revisioned
optional metadata with attribution, and distinct tile/cover/hero/logo artwork
roles with semantic fallbacks. The existing installed-only provider projects
only facts it can prove; absent facts and unsupported actions remain absent.

**Acceptance:** broker-to-bridge-to-SDK round trips preserve every closed enum,
optional field, revision, attribution, and artwork role without exposing paths,
commands, AUMIDs, store IDs, account identity, URLs, bytes, or provider-private
records. Current Games & Apps and Game Launcher migrate coherently; installed,
unavailable, stale-source, missing-art, retained-last-good, duplicate-title,
and exact SavedId launch fixtures remain truthful. Protocol/schema mismatch
fails closed with a stable diagnostic. The API documentation distinguishes
presentation identity from action authority and marks M2/M3 fields as optional
provider capability rather than inferred state.

**Architecture/verification:** publish focused immutable value types rather
than one catch-all bag or flag soup. Give source state, item availability,
artwork set, metadata provenance, capability set, and active-operation summary
separate validation owners. Tier 1 broker/bridge/SDK/provider/Game Launcher
Release tests; Tier 2 installed-worker protocol compatibility. No aggregate,
network, external fixtures, or speculative provider implementation.

**Stop:** a field cannot be represented without exposing provider authority,
the contract would grant content-management capability, or its final shape
depends on an unselected external provider or destructive-operation UX.

### Widgets planned queue

### DLV-135 — Add the deterministic Launcher Experience authoring toolchain

**State:** Planned; becomes same-lane Ready after accepted DLV-130 and accepted
platform DLV-131 are integrated into main
**Owner/outcome:** widgets lane for `gbar launcher-theme new`, `validate`,
`preview`, `pack`, `inspect`, `install`, `list`, and `remove`, sharing the exact
production manifest/recipe/GBSS/asset/digest/catalog validators. Authors can
build and inspect a data-only pack without hand-authoring undocumented JSON.
**Acceptance:** deterministic archives and immutable ID/version installs;
compact/standard/wide fixture preview for empty, 20-game, 2,000-game, offline,
long-title, missing-art, active-operation, 150%-scale, reduced-motion, and high-
contrast states; fail-closed rejection of unknown fields, remote URLs, HTML/JS,
executables, path escape/reparse points, oversized assets, missing critical
slots, action/provider bindings, and inaccessible branches. Focused CLI/catalog
tests only; no gallery, signing, automatic update, animated media, or network.

### DLV-134 — Project Game Launcher state into all four native experiences

**State:** Planned; becomes same-lane Ready after accepted DLV-130, DLV-132,
and DLV-133 are integrated into main
**Owner/outcome:** widgets lane for the Game Launcher semantic-state adapter,
experience selection/settings surface, and the `hero-rail`, `cover-wall`,
`carousel`, and `compact-grid` production projections. All four presentations
must retain the same exact SavedId actions, collection position, source truth,
Back route, and bounded retained library.
**Acceptance:** compact/standard/wide and 150%-scale fixtures prove complete
controller/keyboard focus, UIA semantics, long-title/missing-art fallback,
2,000-game bounded paging, selection persistence across profile changes, and
safe fallback when an experience is unavailable. No provider/API breadth,
content operations, or pack-authored actions.

Audio endpoint selection still lacks a supported setter, YouTube remains
blocked at the trusted-media cost gate, and Spotify/YT Music account work needs
authentication or live evidence. Game Launcher IGDB, SteamGridDB, store-adapter,
and content-operation breadth remains outside these M1 framework assignments.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-127 — Keep tray placement stable and fit the overlay during widget switching

**State:** Accepted as `df07d7c`, integrated through `2ac0a5a`, rebuilt on exact
main, and visibly running as planner PID 18392; the user's live switching
verdict remains
**Baseline/dependencies:** accepted main `8c40a6d`; live user report against
planner-launched Release PID 36488. The 2026-08-12 00:16-00:17 log shows the
same switch changing tray visibility from six to eight items while composition
geometry moves between 829x1152, 1381x969, 592x698, and widget extents; the user
observes the tray moving and the overlay clipped at both screen edges.
**Owner:** platform lane for native monitor/work-area fitting, shell/body/tray
placement, composition-motion policy, pointer/focus/UIA bounds, production-host
temporal evidence, and directly affected native documentation
**Concurrency:** no managed widget layout, Game Launcher actions, tray order/
catalog semantics, new animation library, capture harness, reviewer documents,
or push.

**User-visible outcome:** cycling between small and large widgets keeps the icon
tray visually stationary, keeps the full overlay within the active monitor's
usable bounds, and never clips the top or bottom of widget content, shortcut
guide, or tray.

**Objective/scope:** trace one real small→Game Launcher→small interval through
the accepted composition path. Separate stable shell/tray placement from the
widget-body extent transform, or otherwise prove one atomic policy that keeps
the tray anchor invariant. Fit requested widget/body plus guides/tray to the
current work area and DPI before committing motion; below preferred size,
allocate the bounded body viewport/scroll region rather than positioning any
essential shell region outside the monitor. Retained inert content must use the
same final shell bounds without changing tray capacity during one transition.

**Acceptance:** tray center/baseline and selected item bounds are invariant
through every frame of small↔Game Launcher and small↔Spotify cycles; semantic,
pointer, and painted tray bounds agree; final host bounds remain within the
selected monitor's live Windows work area, with representative compact, 720p,
1080p, 150%-scale, taskbar-reserved, and monitor-change profiles; top header,
bottom guide, and tray remain reachable; no black
border, flash, stale input, focus transfer, or six→eight→six tray-capacity churn
occurs during the same identity switch. Record timing/geometry/state evidence,
not screenshots.

**Verification:** Tier 1 placement/composition/tray-layout and focused native
Release groups; Tier 2 production-host temporal fixture across the named widget
sizes and one runtime work-area/DPI change. No aggregate or capture.

**Architecture/stop:** do not add widget-specific offsets or another geometry
authority in `OverlayApp`. Stop if the correction requires a second HWND/
renderer ownership model, a substantial DirectComposition redesign, or a UX
choice about whether large content shrinks versus scrolls that is not already
fixed above.

### Platform current assignment

### DLV-124 — Reconcile native session, tray, and UIA state after uninstall

**State:** Stopped at the managed-catalog boundary. The native trace found a
credible retained-presentation cache gap, but the synthetic uninstall mutation
never produced a managed catalog revision/native event, so the three-file
experiment is unaccepted and will be preserved off-lane rather than merged.
**Baseline/dependencies:** accepted DLV-123 `552d250`, integrated through
`8c40a6d`, plus accepted DLV-118 catalog reconciliation/generation teardown
**Owner:** platform lane; native catalog-removal reconciliation and focused host
state/focus/accessibility/process-lifecycle evidence only
**Outcome/scope:** prove whether existing DLV-118 teardown already ensures that
uninstalling a disabled Community widget leaves no stale tray identity, cached
presentation, worker/session generation, pointer target, or UIA node while
Settings remains selected. Add production code only for a reproduced gap.
**Verification:** Tier 1/2 production-host uninstall semantics only; no capture
or aggregate.
**Stop:** the defect belongs to managed catalog state or post-uninstall
selection requires a material UX choice.

### Platform ready queue

### DLV-131 — Freeze the data-only Launcher Experience Pack schema

**State:** Implemented as `100c646` but not accepted or integrated. Review found
the advertised WebP contract rejects ordinary `VP8`/`VP8L` files, package and
catalog limits are reached only after unbounded enumeration/sorting work, and
the deterministic malformed-package fixtures do not cover several named
assignment boundaries. DLV-137 is the queued correction after already-active
DLV-132.
**Baseline/dependencies:** `docs/game-launcher-requirements.md` experience
architecture, GL-THEME-001 through GL-THEME-012, GL-SEC-004, and delivery step
1. It does not depend on DLV-130 because packs bind semantic slot names, never
provider records or widget actions.
**Owner:** platform lane for a distinct Launcher Experience manifest/catalog,
strict recipe and parameter models, production validator, built-in recovery
descriptors, focused tests, and directly affected platform documentation
**Concurrency:** no global-theme schema mutation, native renderer change,
Game Launcher state/action code, CLI commands, external/network asset, animated
media, signing/gallery/update, reviewer document, capture, or push changes.

**Outcome/objective:** define a versioned data-only package that may arrange
only `hero-background`, `game-rail`, `details-panel`, `collection-tabs`,
`source-status`, `operation-status`, `system-status`, and `controller-hints`
through typed `region`, bounded `grid`, `stack`, `overlay`, `inset`, and
alignment records in compact/standard/wide branches. It may supply scoped GBSS,
sealed local assets, and bounded host-defined parameters; it cannot author
content, actions, IDs, provider bindings, code, URLs, shaders, or scripts.

**Acceptance:** strict parsing rejects duplicate/unknown fields, slot reuse,
missing applicable critical slots, invalid overlap, out-of-bounds geometry,
unbounded rows/columns, clipped focus extents, unreachable actions/Back,
cross-widget selectors, remote content, executable/archive content, unsafe
paths/reparse points, and the documented file/expanded/asset/dimension limits.
The bottom-rail and left-rail/glass-panel reference recipes validate, while
malformed variants fail with stable path-specific diagnostics. Keep this
catalog separate from global `ThemeCatalog`; reuse proven identity, immutable
version, digest, file-guard, GBSS, and atomic-selection utilities where their
contracts actually match.

**Verification:** Tier 1 schema/catalog/asset/GBSS validation plus deterministic
malformed-package fixtures. No aggregate, native host launch, screenshot,
network, or implementation of CLI commands owned by DLV-135.

### DLV-132 — Render host-owned launcher slots with validated responsive layout

**State:** Assigned from clean `100c646` before the DLV-131 review completed;
finish and commit this coherent in-progress milestone, then take DLV-137
**Owner/outcome:** platform lane for the native recipe adapter and declarative
layout/paint/focus/pointer/UIA support needed by validated launcher slots. A
fixed semantic fixture must render the bottom hero rail and the left vertical
rail with independent translucent details panel, plus safe built-in `cover-
wall`, `carousel`, and `compact-grid` structures.
**Acceptance:** the host, not the pack, injects slot content and action routes;
overlay/inset/region layout stays within the live work area; z-order never
changes semantic focus geometry; every required action and Back is reachable;
pointer, painted focus, semantic focus, UIA bounds, and accessibility order
agree at compact/standard/wide, 720p/1080p, taskbar-reserved, 150%-scale, long-
title, and missing-art fixtures. Invalid or incompatible recipes atomically use
the matching built-in fallback. Existing widgets and current flex/grid/scroll
behavior do not change.
**Architecture/verification:** extend the focused declarative layout/adapter
owners rather than `OverlayApp`; keep recipe validation, layout, navigation,
paint, and accessibility as separate testable concerns. Tier 1 native layout,
renderer, focus, pointer, and UIA groups; Tier 2 one production-host semantic
fixture. No aggregate, capture, pack CLI, Game Launcher domain code, or motion.

### DLV-133 — Add launcher-scoped style, artwork, and recovery ownership

**State:** Ready after DLV-136
**Owner/outcome:** platform lane for the launcher-only cascade after global user
appearance, sealed static pack assets, revision-bound selected-game background,
decode-before-crossfade, bounded focus effects, user parameter overrides,
last-good retention, and safe-start fallback. Accessibility policy remains the
final layer and can remove blur/transparency/motion.
**Acceptance:** `Use global appearance` reproduces current styling; pack rules
cannot reach other widgets or shell roles; PNG/JPEG/WebP assets obey package and
decode bounds and never expose paths to workers; failed/deleted/corrupt assets
retain a professional fallback; experience switch/reload is atomic and keeps
focus/actions; reduced motion/transparency and high contrast override every
pack; repeated decode/style failure disables only the offending revision and
retains a built-in launcher. Measure input/render budgets during background
crossfade and degrade effects before focus latency. Tier 1 style/asset/recovery
groups and Tier 2 production-host lifecycle fixture; no animated media, audio,
remote assets, gallery, network, screenshot, or aggregate.

### DLV-137 — Close the Launcher Experience package-validation gaps

**State:** Ready immediately after the already-active DLV-132; this is the
DLV-131 review correction and precedes DLV-136 and DLV-133
**Baseline/dependencies:** committed DLV-131 `100c646` plus the completed
DLV-132 commit; do not change DLV-132 renderer behavior
**Owner/outcome:** platform lane for the smallest catalog/file-guard/parser and
focused-fixture correction needed to make the documented static package
contract truthful and bounded before DLV-131 or its dependent history can be
integrated.
**Acceptance:** valid bounded lossy `VP8`, lossless `VP8L`, and extended `VP8X`
WebP assets report exact dimensions and validate; truncated, malformed, zero-
dimension, or oversized variants fail with stable diagnostics. Package and
catalog discovery stop at documented file, directory, installed-version, and
expanded-byte admission budgets without first materializing or sorting an
unbounded tree. Deterministic fixtures cover duplicate/unknown fields, missing
critical slots, slot reuse, overlap, out-of-bounds/focus/Back geometry, grid/
node/depth limits, file/count/expanded/image limits, reparse/path escape,
remote/executable/archive content, cross-widget selectors, and exact recovery.
Duplicate-field and boundary failures retain useful stable paths. Preserve all
four built-ins, the public schema/value types, deterministic digest, and valid
reference recipes.
**Verification:** Tier 1 Launcher Experience catalog Release build and focused
fixtures only. No aggregate, native renderer/adapter change, launcher domain
work, capture, network, new media dependency, or unrelated hardening.
**Stop:** accepting standard WebP requires a new decode/runtime dependency or
the correction needs a public schema/budget change rather than validation of
the already documented format.

### DLV-136 — Make the production overlay reachable by the planner UI smoke

**State:** Ready immediately after DLV-137; take this before DLV-133
**Owner/outcome:** platform lane for the smallest production-host change that
lets the supported Windows computer-control surface discover, activate, and
capture the exact visible `Game Bar Alternative` main window after `--show`.
The planner must then be able to send normal Left/Right or equivalent tray
navigation and inspect the admitted first page of every installed widget.
**Reproduction:** accepted Release PID 32952 is visibly resident and receives
the authenticated Show activation, but `@oai/sky` returns no OverlayHost entry
from either `list_windows()` or `list_apps()`. Direct tool launch fails with
`launched app did not expose a targetable window`. The production window
already has a stable title but is a topmost `WS_POPUP` with
`WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP`.
**Acceptance:** one and only one live main OverlayHost surface is returned by
the supported computer-control discovery path while visible; activation,
window-state capture, and keyboard tray cycling operate on that exact surface;
the backdrop is never independently targetable. Preserve the overlay's normal
topmost, no-taskbar/Alt-Tab, focus/input, transparency, composition, security,
and graceful lifecycle behavior. Add only bounded production-window identity
and discovery evidence; do not build a new screenshot harness or a parallel
automation-only UI. Planner acceptance requires the real computer-control
first-page smoke, not only a synthetic enumeration test.
**Verification:** Tier 1/2 native window identity, accessibility, activation,
and lifecycle groups plus one bounded production-host discovery proof. No
aggregate, launcher experience implementation, renderer refactor, or unrelated
capture work.
**Stop:** computer-control reachability requires removing
`WS_EX_NOREDIRECTIONBITMAP`, exposing the backdrop, adding taskbar/Alt-Tab
presence, weakening composition/security, or making another material product
UX choice. Preserve evidence and ask the planner instead of shipping that
tradeoff.

**Queue note:** the user's fresh verdict confirms the moving-tray/work-area
regression and is now DLV-127. DLV-104 Game Launcher content clipping and
DLV-106 Audio Mixer tray-Left focus retain their existing live-verification
dispositions unless the new bounded evidence directly reproduces them. Do not
manufacture adjacent work.

## Serialized integration queue

1. DLV-130 owns the normalized public launcher data contract; no other lane
   edits that broker/bridge/SDK boundary until it is accepted.
2. DLV-131 owns the separate experience manifest/recipe/parameter schema and
   catalog. Already-active DLV-132 finishes first; DLV-137 closes the bounded
   package-validation review gaps, then DLV-136 restores required planner
   UI-smoke reachability before DLV-133 continues platform presentation work.
3. DLV-135 may consume accepted DLV-131 for author tooling while DLV-132 runs,
   provided the planner rebases at a clean boundary and confirms exclusive
   files. DLV-134 waits for accepted DLV-130/132/133.
4. External metadata/artwork sources and trusted store/content adapters remain
   later delivery steps and cannot broaden these M1 assignments.

## Blocked work

| Item | Blocker | Unblocking evidence |
| --- | --- | --- |
| DLV-124 native uninstall reconciliation | The production-host synthetic catalog removal never emitted a managed revision or native event, so native retirement could not be accepted and its experiment remains off the implementation lane. | A bounded managed catalog-monitor assignment that reproduces exact disabled/nonresident removal and supplies a deterministic event to the native fixture; then re-review the retained native cache correction. |
| DLV-062 trusted fixed-video surface | One visible paused WebView2 surface measured about 348.7 MiB private memory and 4% CPU against the current 128-MiB gate; supported suspension controls require invisibility and do not solve visible cost. | User changes the budget or authorizes a content/process-specific bounded experiment with a hard stop and no account work. |
| Audio Mixer default input/output selection | No documented supported Windows setter is established; roadmap forbids undocumented `PolicyConfig`, registry writes, or Shell automation. | Primary Microsoft API evidence plus a reversible provider/hardware plan. |
| Live Spotify Web Playback | Account, Premium eligibility, allowlist, OAuth, and EME. | User-authorized account and retained manual evidence. |
| YouTube authenticated library | Google OAuth and account; Watch Later is not supported by the Data API. | Approved minimum-scope OAuth plan and user-authorized account. |
| Physical controller/display/audio/Bluetooth/game/accessibility matrix | Requires user hardware or interactive environment. | Retained named packaged/manual evidence. |

## Verification queue

1. Packaged controller/visual matrix for issues still marked Verifying.
2. Real YT Music companion pairing/reconnection and physical controller.
3. Live Spotify pagination/failure/OAuth/Web Playback/device behavior when an
   authorized account exists.
4. Physical Y-hold exactly-once refresh.
5. Physical Narrator/MSAA traversal.
6. Packaged Spotify seek/list traversal and transient-failure recovery.
7. Packaged widget-switch transparency and temporal continuity after DLV-115.
8. Games & Apps cold-restart, trusted artwork, and running-app live checks.
9. Audio Mixer LB/RB/X physical dashboard controls.
10. Planner first-page computer-control smoke. The 2026-08-12 01:42 local
    attempt left accepted PID 32952 visibly running and its exact Show interval
    contained no new worker, protocol, provider, or presentation failure, but
    the native surface was absent from computer-control discovery. No widget
    received a visual pass or failure verdict from that attempt; DLV-136 owns
    the targetability prerequisite.

## Recent acceptance delta

| Assignment | Accepted implementation | Integrated main | Visible/product result |
| --- | --- | --- | --- |
| DLV-128/129 | `344caa0` and `7f7d9d5` | `67c557d` and `d792e30`; packaged through planner main `d8c803a` | Game Launcher now presents the bounded selected-game hero and horizontal cover rail while retaining exact SavedId actions and current installed-only authority. The dev cancellation fixture is deterministic without changing product behavior. One full Release package refresh passed and PID 32952 is visibly running; its startup/admission log contains no new worker or protocol failure. |
| DLV-127 | `df07d7c` | `2ac0a5a` | Widget switches retain one work-area-fitted shared shell and tray while preferred width/height remain bounded inner-body hints. Eight real host transitions retain exact shell/tray/selection geometry with zero widget-shell motion commits and bounded draw/commit/geometry timings; the prior PID 18392 was gracefully replaced by current accepted PID 32952. |
| DLV-125 | `ccabb0e` | `418d11f` | The existing MSTest.Sdk 4.3.2 WidgetScenario project now has one bounded manifest step and exact-once discovery enforcement; direct/manifest execution passes 9/9. The one clean aggregate stopped later at Gbar CLI 57/58, isolated as DLV-129 rather than rerun or allowed to block visible work. |
| DLV-126 | `a45166c` | `2c58ba7` | Game Launcher derives warm anchors from the exact rendered non-hidden rows, publishes shortcuts/help only for actionable game tiles, and retains host-owned single-step collection continuation. Focused Release evidence is 60/60; exact main is rebuilt, packaged, and visibly running as PID 16824 for the user's top-control, shortcut, and paging verdict. |
| DLV-123 | `552d250` | `8c40a6d` | Disabled Community widgets now expose an exact path-free nested uninstall confirmation in Settings; built-in/enabled/stale/resident identities fail closed, all package versions retire together, unrelated widgets and private data remain, and one catalog revision is published. Focused Release evidence is green; the inherited verifier-manifest omission is isolated to DLV-125. |
| DLV-121 | `fb0ad51` | `a758508` | The Audio Mixer production fixture now traverses the real clipped/revealed controls instead of directly focusing an absent offscreen UIA node; live product behavior is unchanged and still awaits user verification. |
| DLV-118 | `5d86cd6` | `4bc0baa` | Small and wide widget surfaces retain one selected tray identity, explicit reachable overflow, exact order, and synchronous catalog replacement without stale tray dispatch. |
| DLV-113/119/122 | `0994809`, `670e01d`, and `6f604b8` | `4949f6e` | Settings exposes the exact host-owned local package picker with disabled review, while Now Playing adds safe stage/code diagnostics, Retry/activation recovery, and last-good retention; stale public install claims are removed. |
| DLV-114/117 | `f285c9d` and `6a96727` | `cff0d99` | Game Launcher has an exact-ID controller details route and honest two-selection variant actions with committed feedback and deterministic return focus. |
| DLV-112/115/116 | `1dd2dd5`, `5c66f16`, and `0e1810f` | `d6f2780` | Host-owned local package import is consumable only by exact bundled Settings, and reopened widget motion retires hidden composition work; live border/transition verdict remains. |
| DLV-111 | `a1f008b` | `83c5c1f` | Settings now groups exact installed theme versions, protects built-in/current selections, selects older valid versions, and confirms atomic inactive-version retirement with CLI parity. |
| DLV-033 | `1ec2b70` | `4fa8f63` | Bridge-facing catalog, snapshot, failure, lifecycle, retry, and request policy now has one off-UI-thread coordinator; the host retains renderer/input/presentation authority. |
| DLV-110 | `e90e729` | `4e02184` | Basic, data, media, and multipage starters now generate atomically outside the checkout with MSTest.Sdk 4.3.2 scenarios, isolated preview, validation, and deterministic packaging. |
| DLV-107/102 | `b662e9a` and `e4f9880` | `0e0bf77` | Transparent unused client pixels, transform-only widget motion, and stable deduplicated trusted-artwork fallback. Rebuilt Release awaits live verification. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. After it exceeds 1,000, create one complete timestamped snapshot and
compact it according to `review-planner-goal.md`.
