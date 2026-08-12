# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-11
Planning owner: independent review and delivery-planning agent
Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. It intentionally
contains only the live execution contract, current assignments, next executable
work, integration dependencies, blockers, and recent acceptance delta.

The complete pre-compaction state is preserved in
[`history/delivery-plan/2026-08-11T17-15-01-0700.md`](history/delivery-plan/2026-08-11T17-15-01-0700.md).
Historical snapshots are evidence only and are never implementation authority.

## Queue protocol

- Each implementation task has one stable lane identity and executes only that
  lane.
- Each lane has at most one `Assigned` milestone and an ordered `Ready` queue.
- After committing a milestone, the task immediately takes the first executable
  same-lane `Ready` item. Planner review runs asynchronously.
- Tasks never reorder, merge, broaden, skip, or invent assignments and never
  select work from the roadmap, issue ledger, reviews, implementation status, or
  code comments.
- Every assignment normally produces one coherent local `[DLV-nnn]` commit.
  Nothing is pushed.
- Implementation tasks update directly affected public documentation and
  `docs/implementation-status.md`. They never edit reviewer-owned planning,
  roadmap, issue, review, or goal documents.
- A reproducible P0 may interrupt the queue. Ordinary review corrections are
  inserted immediately after the milestone already in progress and do not
  interrupt it.
- The planner integrates only independently accepted contiguous history.

### Visible-outcome priority

1. Reproduced user-visible P0/P1 defects and requested features.
2. The smallest shared prerequisite directly unlocking a named visible result.
3. Packaged usability, accessibility, responsiveness, reliability, and measured
   performance work with an observable outcome.
4. Internal refactoring, test organization, and documentation cleanup.

At least one lane must own a visible outcome or its immediate prerequisite while
safe visible work exists. Both lanes may not run internal-only refactors in that
condition. No lane runs more than one consecutive internal-only milestone
without a named visible or release blocker.

### Architecture non-regression

Production types above roughly 1,000 physical lines, logical partial types
across all declarations, and smaller types owning several independently
testable concerns are review hotspots. Every hotspot needs an Assigned, Ready,
dependency-blocked, or cohesive-exception disposition in the engineering
review. Touching a hotspot requires a before/after responsibility map. File
splitting, cosmetic partials, wrappers, or named patterns do not close a finding
unless shared mutable knowledge is reduced or a focused policy/test boundary is
created.

### Branch and integration protocol

- `widgets`: `codex/impl-widgets` in its isolated Codex worktree.
- `platform`: `codex/impl-platform-switch` in
  `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative`.
- Local `main` is planner-owned integration.
- `codex/impl-platform-recovery` remains preserved at `7e64b4d` with the
  non-integrable DLV-062 checkpoint and must not be merged.
- The interrupted `codex/impl-platform-visible` worktree is read-only
  preserved state.
- The former `codex/impl-platform` DLV-025 worktree is absent. Do not claim or
  reconstruct its uncommitted files.
- A lane consumes new `main` only at a clean committed boundary after explicit
  bounded planner instruction. Substantial conflicts stop for the user.
- Shared protocol/architecture work is serialized to one lead lane.

### Verification tiers

- **Tier 1:** affected Release build and directly affected deterministic suites.
- **Tier 2:** smallest cross-component group covering the changed boundary.
- **Tier 3:** only a named integration checkpoint, verifier/core-protocol/
  security change, concrete planner-requested risk, or release gate.

Every command has a bounded timeout. Do not repeat an unchanged failing command
or run the same aggregate on a dirty tree and its exact commit. New managed test
projects use `MSTest.Sdk` 4.3.2; existing executable suites retain their current
runners.

Screenshots are optional supporting evidence. Immediately exclude clipped,
malformed, stale, black, partial, wrong-window, or premature artifacts. Do not
debug capture tooling during a product milestone. Deterministic functional,
state, semantic, accessibility, timing, and resource evidence plus the freshly
launched accepted Release are the normal acceptance path.

## Widgets lane

Task identity: `widgets`
Branch: `codex/impl-widgets`

### DLV-100 — Admit `TextEntry` through bridge render styles

**State:** Done; accepted as `760a9bb`, integrated through `54fbd12`
**Baseline:** planner control-plane commit `66a3f57`
**Dependencies:** accepted DLV-075/DLV-079 and protocol v15; DLV-099 is the
clean prior widgets boundary
**Owner:** WidgetBridge computed-style role normalization, production-shaped
bridge/installed-worker fixtures, and directly affected public documentation
**Concurrency:** May run with platform DLV-025. Do not touch native compositor,
renderer, DLV-101 resource policy, or reviewer-owned documents.

**Visible outcome:** Game Launcher opens instead of showing
`Unsupported view node kind 'TextEntry'`; Search reaches the unchanged
host-owned modal. Protected Network Controls text entry remains admissible.

**Objective:** Add the missing existing `TextEntry` role to the singular bridge
computed-style resolver and prove protocol-v15 text entry completes the SDK ->
worker -> bridge-style -> native-parser route. SDK/protocol/native already
support the node; this is a shared bridge regression, not a widget workaround.

**In scope:** one stable distinct GBSS role; default and themed resolution;
computed-style output keyed by node ID; Game Launcher initial Ready snapshot;
protected Network Controls route; existing retry/current-generation/last-good
behavior; fail-closed unknown future node kinds.

**Out of scope:** protocol changes, public TextEntry/modal redesign, widget
layout changes, native modal/input work, DLV-025, DLV-101, screenshots,
aggregate runs, or historical Spotify/YT Music/Settings errors.

**Acceptance:** both production-shaped valid TextEntry routes are admitted;
Game Launcher Search reaches the modal; the computed map contains the node ID;
unknown enum values still fail closed; one canonical role owner exists; retry,
generation, and last-good behavior do not regress.

**Verification:** Tier 1 focused WidgetBridge style/worker plus directly affected
Game Launcher and Network Controls fixtures. Tier 2 uses the smallest installed
generic AppContainer/production-bridge route opening Game Launcher and invoking
text entry. No aggregate or screenshot gate.

**Stop:** public protocol/native modal change, materially ambiguous GBSS role
semantics, or pressure for a widget-local fallback.

**Reviewer disposition:** Accepted. One canonical `textEntry` role closes the
bridge omission without changing protocol, SDK, native modal, or widget layout.
Unknown future node kinds remain fail-closed. Retained focused evidence passes
WidgetBridge 77/77, Game Launcher 45/45, Network Controls 24/24, the exact
installed AppContainer TextEntry route, and documentation 55/55. Main was fully
repackaged after integration and the accepted Release is visibly running as PID
26556 for the user's Game Launcher check.

### DLV-101 — Remove arbitrary private-worker size ceilings

**State:** Assigned; implementation started after committed DLV-100
**Baseline:** accepted DLV-100 widgets commit `760a9bb`
**Dependencies:** DLV-100 and current AppContainer/Job/runtime/manifest/private-
state contracts
**Owner:** managed installed-widget runtime, manifest resource semantics, Job
containment configuration, scalable private-data guidance, diagnostics, and
affected public documentation
**Concurrency:** May run with native compositor DLV-025; stop before any
compositor ownership or unplanned cross-lane protocol change.

**Product outcome:** Authors can create full applications as widgets without the
prototype 256-MiB worker or one-process product ceilings. Widgets remain
out-of-process and the native overlay remains bounded.

**Objective:** Remove arbitrary limits on widget-private execution while
preserving strict limits wherever untrusted data or resource ownership crosses
into the shared host. Pre-release compatibility is not a reason to preserve an
inferior manifest or state design.

**In scope:** classify limits as widget-private, boundary-facing, or host-owned;
remove the default hard worker memory and one-active-process quotas; retain
pre-resume Job assignment, non-breakaway process-tree ownership, accounting,
kill-on-close, integrity, UI restrictions, and bounded teardown; make
`resourceRequest.memoryMb` advisory/reporting-only or remove it cleanly; keep
children in the owned Job; keep host admission independent of claimed private
memory; document and prove the scalable private-data path rather than presenting
the 64-KiB host state document as application storage; update all contradicting
public authoring/residency/capability/architecture/packaging documentation.

**Host bounds that remain:** IPC/JSON frames, current presentation node/depth/
string/resource limits, native/GPU caches and surfaces, pending actions, update
coalescing, capability size/time/authority, package acquisition/extraction/path
safety, host worker-session handles/threads, teardown, and diagnostics.

**Out of scope:** ambient OS/network/filesystem/device/process/credential
authority, weaker AppContainer or pipe authentication, child breakaway,
unbounded host queues/snapshots, package safety removal, speculative database
implementation, native compositor work, or compatibility shims.

**Acceptance:** a worker exceeding the old 256-MiB quota is not rejected or
killed solely by that quota; an owned helper child starts, remains in the Job,
and is reclaimed with it; no child breaks away; oversized host-boundary input
still fails before native allocation without harming a neighboring widget;
host admission remains effective; public docs teach full-app private state plus
paged/virtualized presentation and contain no old product-ceiling claim.

**Verification:** Tier 1 focused manifest/runtime/AppContainer/Job/admission/docs
tests with deterministic child and old-ceiling fixtures. Tier 2 launches one
installed package with a child, proves accounting/kill-on-close, and separately
proves oversized host input remains isolated. No aggregate or long stress soak.

**Stop:** the only private-data path exposes arbitrary user/host filesystem
authority, removing a quota permits Job escape or host allocation growth, or
the change requires native compositor work. Report the exact prerequisite
instead of restoring the prototype quota.

### DLV-103 — Restore dependable Media Sessions loading and retry

**State:** Ready after DLV-101 and the planner control-plane merge
**Baseline:** accepted DLV-101 widgets boundary plus the planner commit that
introduces this assignment
**Dependencies:** current `MediaSessionsWidget`, `WidgetMediaService`, managed
PlatformBroker, and WindowsMediaProvider contracts
**Owner:** widgets lane; managed Media Sessions lifecycle/retry policy, typed
capability failure propagation, Windows media-provider boundary, diagnostics,
and directly affected public documentation
**Concurrency:** Begins only after DLV-101 commits. May run while platform work
continues. Do not change native rendering, tray/navigation, DLV-025 compositor
ownership, or public capability authority.

**Visible outcome:** Now Playing loads current Windows media sessions again, or
shows the specific actionable empty/unavailable state. `Try again` performs one
real current-generation reload instead of leaving the generic
`Media sessions could not be loaded` regression.

**Reproduction evidence:** The accepted packaged Release displayed the generic
error card while its presentation remained admitted and repainting. The current
OverlayHost log records Media Sessions presentation and retry paints but does
not record the typed underlying provider/capability error, so the screenshot
cannot be correlated to a diagnosable failure code.

**Objective:** Reproduce the installed/package path, identify and fix the
managed load or provider lifecycle regression, preserve last-good sessions on a
transient refresh failure, and make every terminal failure diagnosable without
exposing app identity, process details, provider bodies, or credentials.

**In scope:** initial activation, worker/provider readiness, query and changed-
event subscription ordering, retry single-flight, generation/cancellation,
channel replacement, service unavailable, permission/lifecycle denial,
malformed response, transient provider failure, zero-session success, last-good
refresh, one bounded typed diagnostic per failure transition, and the installed
Release package route. Inspect whether failure classification currently loses
the originating error before changing user copy.

**Out of scope:** Spotify/YT Music authentication, native media rendering,
ambient process inspection, exposing player identity on failures, periodic
polling as a retry substitute, broad capability redesign, screenshots, or the
canonical aggregate.

**Acceptance:** an available provider returns and updates sessions; no sessions
is a successful empty state rather than an error; retry starts exactly one
current-generation load and recovers after a transient/channel failure; stale
loads/events cannot replace the current generation; a refresh failure retains
last-good sessions with an accurate status; terminal failures map to their
specific safe state; the log records a bounded correlation-safe error code and
owning stage; deactivation/disposal drains subscription and retry work.

**Verification:** Tier 1 MediaSessionsWidget, WidgetSdk media service,
PlatformBroker/WindowsMediaProvider, and documentation suites. Tier 2 uses the
smallest installed worker/provider fixture for success, empty, transient fail ->
retry success, channel replacement, stale completion, and teardown. Build the
affected Release package; no live account, screenshot, or aggregate.

**Stop:** root cause is native input/rendering, requires new OS authority or
player/process enumeration, or needs a public capability/protocol change. Report
the exact failing stage and preserve the safe error state for a serialized
planner assignment.

### DLV-104 — Make Game Launcher content fit and scroll without clipping

**State:** Ready after DLV-103
**Baseline:** accepted DLV-103 widgets boundary
**Dependencies:** current Game Launcher wide surface, responsive grid,
`game-launcher.library.scroll`, shared SDK layout components, and accepted
DLV-100 TextEntry route
**Owner:** widgets lane lead; Game Launcher presentation hierarchy, responsive
composition, scroll ownership/identity, styles, semantic fixtures, and only the
smallest shared managed SDK correction proved necessary by a second consumer
**Concurrency:** Do not touch native compositor, native tray layout, trusted
artwork loading, or DLV-105/DLV-106. If the emitted tree and bounds are correct
but the native layout clips them, stop with evidence for a platform correction
instead of adding widget-specific offsets.

**Visible outcome:** Game Launcher shows its complete header, source status,
search/filter controls, grid viewport, actions, and footer at supported sizes;
no top labels, tile rows, or help text are cut off or overlap the tray.

**Reproduction evidence:** The user-reported live wide Game Launcher clipped
the top title/source region, cut the lower grid row, and placed guidance outside
the content panel. The current presentation composes header, source status,
query controls, and a nested library scroll as one root stack with a fixed
980x700 preferred surface, so both managed measure intent and native viewport
evidence must be separated before choosing a fix.

**Objective:** Give fixed chrome and the collection viewport explicit bounded
ownership across runtime size/scale changes without increasing preferred height
until everything happens to fit and without wrapping the entire app in a second
ambiguous scroll surface.

**In scope:** library/add/running/hidden/loading/error/warm routes; compact,
standard, wide, minimum, and accessibility-scale envelopes; fixed header/query/
source regions; grid viewport and page controls; root versus nested scroll
identity; initial/restored focus visibility; route change, refresh, resize, and
widget-cycle behavior; text wrapping; bottom guidance/tray separation; emitted
semantic bounds and native-consumed layout evidence for the same snapshot.

**Out of scope:** artwork availability (DLV-102), provider discovery, launch or
persistence policy, decorative redesign, per-widget native offsets, unbounded
preferred height, capture-harness work, or aggregate verification.

**Acceptance:** every essential region is visible or predictably reachable at
the documented surface matrix; fixed chrome does not scroll away accidentally;
the collection alone owns bounded vertical scrolling; no clipped text/tiles or
footer/tray overlap occurs at top, middle, or end; focus remains on-screen and
stable across paging/resize/route changes; content does not auto-offset on
reopen; no duplicate responsive tree or special native Game Launcher branch is
introduced.

**Verification:** Tier 1 GameLauncherWidget and directly affected WidgetSdk
layout/semantic tests. Tier 2 uses production-shaped large library snapshots at
minimum/standard/wide sizes and 100/125/150% scale, exercising first/middle/
last focus, paging, route changes, resize, and reopen. Assert semantic bounds,
reachability, scroll ownership, and footer/tray separation; build the affected
Release package. Live visual confirmation remains with the user.

**Stop:** correct behavior requires changing native layout semantics or surface
placement, shared component semantics would break another consumer, or the
only passing approach is a larger fixed surface. Return the exact emitted-tree
and native-bounds discrepancy to the planner.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-025 — Eliminate transition tearing and UI-thread stutter

**State:** Rejected after live packaged verification; `16f9f47` remains
integrated through `5b8556a` and is superseded by correction DLV-107
**Baseline:** planner control-plane commit `66a3f57`
**Dependencies:** accepted DLV-020 and DLV-078 presentation ordering
**Owner:** OverlayHost transition scheduling, Win32/DWM composition, Direct2D
resize/invalidation, native bridge/UI-thread interaction, and temporal evidence
**Concurrency:** May run with DLV-100/DLV-101. Do not touch their managed
bridge/runtime/public-contract ownership.

**Visible outcome:** Widget-size transitions no longer flicker, stutter, or
expose black/gray/stale regions around Games & Apps and Spotify.

**Objective:** Correct the live transition regression with visual continuity
inside a measured frame budget. An immediate stable switch is preferable to a
laggy or tearing animation.

**Authorized direction:** Gate one Windows-10-compatible
`IDCompositionSurface` design. Render the complete destination offscreen,
fully cover its update rectangle, end drawing, commit once, and retain the prior
committed content until destination readiness. Coordinate content commit with
existing HWND geometry. Do not use the Windows-11-only composition-swapchain
API, raise the Windows floor, or retain two permanent presentation owners.

**Baseline evidence:** Populated Spotify/Games first paints measured about
31 ms; 14 Spotify inputs produced six paints over 674 ms; valid temporal
evidence exposed a dark interior band at final geometry before list paint.
Resizing the HWND Direct2D target exposes undefined content before successful
draw; later `DwmFlush` cannot retract an already composed frame. The missing
former worktree is not reconstruction authority.

**In scope:** exact Games/Spotify switch paths; timer/frame measurements around
`SetWindowPos`, `WM_SIZE`, target resize/recreation, invalidation, redraw,
bridge work, and commit; one DirectComposition device/surface lifecycle;
complete-surface updates; prior-content retention; device loss and capability
fallback; integrate-or-discard decision; reversal, same-identity refresh,
reduced motion, compact/standard/wide, 100-150% scale, and continuous tray/
backdrop. Move only measured blocking transition-critical bridge work without
changing widget APIs or authority.

**Out of scope:** composition swapchain, Windows-floor change, second permanent
renderer, per-widget timing/background hacks, delays hiding artifacts,
decorative motion, generic bridge rewrite, Games composition changes, public
protocol changes, or static screenshots as smoothness proof.

**Acceptance:** real Games, Spotify, Audio Mixer, and Network transitions expose
no black/gray/transparent/stale/unpainted bands or whole-shell flicker; temporal
evidence reports frame/cadence distribution; no transition-critical synchronous
work violates the measured budget; reversal/refresh remain continuous; reduced
motion is immediate; focus/input/UIA remain correct; settled/hidden cost is
unchanged. If extent animation cannot pass, use an immediate or composition-
only transition and document the decision.

**Verification:** Tier 1 transition, placement/targeting, renderer, resize, and
host Release suites plus the production host build. Use a bounded credible
timestamped real-product frame sequence or video-derived interval when valid;
exclude malformed capture and do not modify capture tooling. No aggregate.

**Stop:** passing requires Windows-11 composition swapchain, a second permanent
presentation owner, public protocol/threat-model change, or user-only physical
evidence. A surface that cannot coordinate committed content with HWND geometry
is non-integrable evidence.

**Reviewer disposition:** Rejected by the user's live recording from the exact
accepted-main PID 25164 session. The DirectComposition path makes the unused
client area an opaque near-black rectangle around the overlay, and widget-size
changes again look abrupt and visibly malformed. The implementation combines a
premultiplied-alpha composition surface with the HWND's legacy color-key model,
then clears the complete composition surface to opaque RGB(1,2,3). The session
log also shows one widget change issuing several waited composition/geometry
commits over roughly 100-150 ms, with populated Game Launcher frames taking
about 49-63 ms to draw. Automated ordering and endpoint fixtures did not prove
the alpha result or motion cadence. DLV-107 owns the bounded correction; do not
revert, reset, or layer per-widget masking over this integrated evidence.

### DLV-105 — Keep every widget reachable in a narrow icon tray

**State:** Done; accepted as `42bcf9c`, integrated through `d23db8b`
**Baseline:** accepted DLV-025 `16f9f47` plus planner commit `de95720`
**Dependencies:** host-owned tray catalog/order/selection, `TrayLayout`, host
accessibility tree, focus/navigation, placement bands, and runtime extent
changes
**Owner:** platform lane; native tray measurement/overflow policy, controller
navigation, hit testing, UIA semantics, theme/scale adaptation, and directly
affected native documentation
**Concurrency:** Begins only after DLV-025 commits. Do not change managed widget
catalog persistence/order, widget surface hints, Game Launcher layout, or public
widget APIs.

**Visible outcome:** Switching to a small widget no longer makes installed
widgets silently disappear from the icon tray. Every enabled widget remains
visibly represented or has an explicit, controller-reachable overflow page.

**Reproduction evidence:** The user-reported live Audio Mixer surface displayed
fewer tray icons than the larger Game Launcher surface. Current `TrayLayout`
computes `maximumVisible` from the active surface width, takes only that many
items, and exposes no visible overflow affordance; the catalog itself remains
larger.

**Objective:** Make tray capacity a deliberate responsive navigation policy,
not silent catalog truncation tied to the active widget's preferred width.

**In scope:** compact/standard/wide widths; 100/125/150% scale; add/remove/
reorder; selected-item visibility; deterministic window/page movement; explicit
previous/next or count/overflow affordance when all tiles cannot fit; wrap
policy decision; controller/keyboard/hit-test equivalence; UIA set size,
position, ordering, selection, and off-page reachability; runtime widget-size
transitions; tray/footer bounds and theme spacing.

**Out of scope:** forcing every widget panel to a larger minimum width, deleting
or disabling widgets, icon artwork changes, managed catalog policy, per-widget
width exceptions, decorative animation, DLV-102 image fallback, screenshots,
or aggregate verification.

**Acceptance:** changing the active widget width never removes a catalog item
without an explicit overflow representation; Left/Right reaches every enabled
widget in stable catalog order; the selected item is always visible; moving
between overflow windows/pages is announced and does not activate content;
reorder/add/remove preserve valid selection; pointer hit testing and UIA match
the visible window; no tile/footer overlap or sub-minimum unreadable scaling;
no per-widget special case.

**Verification:** Tier 1 TrayLayout, OverlayState/controller, HostAccessibility,
placement, and affected host Release suites. Exercise 1, exact-fit, exact-fit+1,
large catalog, first/middle/last selection, compact-to-wide-to-compact,
100/125/150% scale, add/remove/reorder, pointer, keyboard, and controller paths.
Build OverlayHost Release; no aggregate or capture harness.

**Stop:** a correct accessible overflow model requires changing persistent
catalog/order semantics, the product must choose between materially different
wrap/page interaction models not resolved by existing controller principles,
or DLV-025 leaves tray geometry without one stable committed width.

**Reviewer disposition:** Accepted. One pure `TrayLayout` owner computes the
same bounded visible window and previous/next overflow controls for paint,
pointer hit testing, and accessibility. Stable catalog Left/Right traversal is
unchanged; pointer/UIA overflow moves to the exact adjacent hidden stable ID
without entering widget content. Visible list items expose full-set position and
overflow buttons expose direction/count. The 331-line diff contains no managed
catalog, persistence, public API, compositor, or reviewer-document changes.
Retained focused Release evidence passes TrayLayout 54, HostAccessibility 34,
AccessibilityProvider 150, ControllerNavigation 107, OverlayPlacement 108,547,
RealHostAccessibility 271, OverlayState traversal, and the host build. Live
compact/wide confirmation remains with the user.

### DLV-107 — Restore transparent composition and professional widget motion

**State:** Ready immediately after already-active DLV-106
**Baseline:** accepted DLV-105 and DLV-106 platform boundary plus the planner
commit that records DLV-025's live rejection
**Dependencies:** integrated DLV-025 surface owner, HWND/backdrop transparency,
extent-transition scheduler, renderer clear semantics, placement diagnostics,
retained-content ordering, and reduced-motion policy
**Owner:** platform lane; native DirectComposition alpha/transparency contract,
widget-extent motion/commit cadence, HWND geometry coordination, transition
diagnostics, and directly affected native documentation
**Concurrency:** Begins after already-active DLV-106 reaches a clean committed
boundary. Do not change managed widget trees, preferred extents, tray overflow
semantics, public widget APIs, worker lifecycle, or capture tooling.

**Visible outcome:** The overlay has no opaque black perimeter or unused-client
rectangle, and cycling between different-size widgets is smooth and deliberate
without snapping, flicker, stale content, or repeated jarring resize steps.

**Reproduction evidence:** The user's 2026-08-11 17:48 local recording of the
fresh accepted-main PID 25164 Release shows an opaque near-black rectangle
around the entire authored overlay and visibly ugly widget motion. The host
creates a premultiplied-alpha `IDCompositionSurface` but clears every complete
surface to opaque RGB(1,2,3), while the HWND still declares that RGB value as a
legacy color key. The same session logs widget-size changes as multiple waited
composition placements over about 100-150 ms; representative populated Games
frames draw in 49-63 ms. No transition diagnostic was emitted at the recording
timestamp itself, so the video is the visual evidence and the session's
equivalent logged transitions are timing evidence, not an invented exact-frame
correlation.

**Objective:** Establish one intentional transparency model for the composed
overlay and one nonblocking composition-owned motion policy. Preserve the last
complete committed content until a complete destination exists, but never
present opaque pixels outside authored widget/tray/backdrop geometry and never
drive a visual curve through repeated blocking surface recreation plus HWND
resize waits on the UI thread.

**In scope:** premultiplied alpha and clear semantics; relationship between
`WS_EX_LAYERED`, color-key/global alpha, DirectComposition content, and the
separate backdrop; transparent unused client pixels; rounded outer bounds;
grow/shrink/same-size transitions; composition visual transform, clip, opacity,
or another single-owner Windows-10-compatible motion primitive; destination
surface readiness; retained source content; one final HWND geometry handoff;
rapid reversal; cold/failed destination; reduced motion; compact/standard/wide,
100/125/150% scale; bounded logs for transition start, presented steps, final
commit, alpha mode, and fallback.

**Out of scope:** per-widget black masks or offsets, increasing the backdrop to
hide the defect, transparent screenshot heuristics, capture-harness work,
sleep/delay-based concealment, reintroducing per-frame synchronous HWND target
resize, Windows-11-only composition swapchains, a second permanent renderer,
external animation libraries, decorative redesign, managed layout changes, or
public protocol changes.

**Acceptance:** unused client pixels are genuinely transparent on the composed
path and the backdrop remains the only intended dimming owner; no black/gray/
opaque perimeter appears around Settings, Now Playing, Games & Apps, Game
Launcher, Audio Mixer, Network Controls, YT Music, or Spotify; grow and shrink
use a bounded professional curve with no visible snap, stale authority, clipped
intermediate content, tray loss, or whole-shell flicker; the destination is
fully rendered before it becomes authoritative; normal motion performs no
blocking commit wait or full expensive redraw on every animation tick; reversal
is continuous; reduced motion performs one immediate complete present; focus,
hit testing, UIA bounds, and input authority match the committed visible state;
settled/hidden cost does not increase; failure enters one explicit bounded
fallback without oscillation.

**Verification:** Tier 1 composition-surface, transition policy, placement,
renderer, transparency, targeting, focus/UIA, and Release host suites. Tier 2
uses production-shaped transitions for Now Playing <-> Games & Apps, Game
Launcher <-> Audio Mixer, Network Controls <-> YT Music, and Spotify <->
Settings at supported scales, asserting alpha outside authored geometry,
complete destination ordering, bounded commit count, no per-tick blocking wait,
reversal, reduced motion, and fallback. Build OverlayHost Release. After
planner review and integration, the freshly launched packaged Release requires
the user's live confirmation; synthetic capture cannot close this regression.

**Stop:** Windows 10 DirectComposition cannot provide correct transparent
content and bounded motion without choosing a materially different window/
compositor architecture, the correction requires a second permanent renderer
or Windows 11 floor, or DLV-105 leaves incompatible tray geometry ownership.
Preserve exact evidence and ask the user rather than hiding the border or
shipping abrupt motion.

### DLV-106 — Keep tray cycling out of outgoing widget focus

**State:** Assigned; conditional gate reproduced and implementation started
after DLV-105 commit `42bcf9c` before the DLV-107 planner correction arrived
**Baseline:** accepted DLV-105 `42bcf9c`, integrated through `d23db8b`
**Dependencies:** integrated DLV-025 presentation semantics, OverlayState tray
focus region, controller/keyboard routing, retained-content transition path,
declarative focus state, and accessibility publication
**Owner:** platform lane; native input authority and focus-state commit ordering
during tray selection/identity transitions
**Concurrency:** Do not change Audio Mixer widget links/actions, Game Launcher
focus graph, managed snapshots, tray overflow semantics, or compositor design.

**Visible outcome:** Pressing Left while Audio Mixer is selected in the icon
tray changes directly to the previous tray widget. The outgoing master-volume
slider never receives or briefly paints focus.

**Reproduction evidence:** The user observed the master slider focus for less
than a second before Game Launcher became active. Recent logs show tray-driven
identity changes retaining and painting outgoing Audio Mixer content as
`semantics=inert` until the destination snapshot arrives, but they do not record
the transient visual focus owner; the correction must prove render focus,
input authority, and UIA focus agree throughout the interval.

**Objective:** Commit tray selection and focus-region authority atomically
before any outgoing widget navigation/focus mutation, and keep retained visual
content inert and unfocused until replacement without introducing a blank
transition.

**In scope:** held/repeated Left and Right; D-pad, keyboard, and analog edge;
tray focus versus widget focus; outgoing selected/focused style invalidation;
retained-content presentation; destination pending/admitted/failure; rapid
reversal; same-extent and changed-extent switches; UIA focus events; logging of
input owner, selected tray ID, rendered content ID, and semantic focus owner in
the bounded transition fixture.

**Out of scope:** changing Audio Mixer navigation graph, disabling retained
content, introducing delays, changing tray order/wrap, compositor replacement,
per-widget focus exceptions, screenshots, or aggregate verification.

**Acceptance:** one tray Left/Right input changes exactly one tray selection and
never dispatches widget navigation; outgoing content may remain visible but
shows no widget focus and emits no widget UIA focus; destination receives focus
only after explicit entry with Up/A; repeat/reversal remains deterministic;
keyboard/controller paths match; failure or slow startup leaves focus on the
selected tray item; no blank frame or stale interactive semantics.

**Verification:** Tier 1 OverlayState, ControllerNavigation/InputRouter,
declarative focus, HostAccessibility, transition-targeting, and host Release
suites. Tier 2 uses a production-shaped Audio Mixer -> Game Launcher slow-
destination fixture and asserts every time-ordered selection, render, semantic,
and UIA focus state. Build OverlayHost Release; live visual confirmation remains
with the user.

**Stop:** the regression is absent on the accepted DLV-025 package, correction
requires changing managed widget focus links, or presentation/input authority
cannot be made atomic within the accepted single-owner compositor design.

### DLV-102 — Fall back cleanly when trusted app artwork is unavailable

**State:** Ready after DLV-107
**Baseline:** accepted DLV-107 platform boundary plus integrated DLV-100 main
**Dependencies:** accepted DLV-094/096/098/099 lazy-artwork ownership and the
existing trusted-artwork cache/renderer contract
**Owner:** native trusted-artwork result/cache state, shared Image/AppTile
failure presentation, diagnostic transition ownership, and production-shaped
Game Launcher/Games & Apps host fixtures
**Concurrency:** Begins only after DLV-025 commits. Do not change game-library
provider discovery, widget catalog/persistence, DLV-101 worker policy, public
capability authority, or reviewer documents.

**Visible outcome:** A game/application whose trusted icon cannot be loaded
shows the stable shared fallback glyph instead of a missing/blank image, and the
overlay log no longer floods the same `image_failed` line on every repaint.

**Reproduction evidence:** In the fully packaged DLV-100 Release session for
PID 26556, Game Launcher emitted 45 `image_failed` diagnostics for 15 stable
artwork node IDs from 17:23:26.682 through 17:23:31.476. Every result was
`Trusted artwork is unavailable`; the same 15 failures were reported three
times as the surface repainted. This is separate from the corrected TextEntry
route and matches the user-visible missing-icon concern.

**Objective:** Give failed trusted artwork one shared, stable presentation and
one state-transition diagnostic without weakening demand-only loading,
generation/revision safety, or host resource bounds.

**In scope:** distinguish pending, supplied, and terminal-unavailable trusted
artwork in the existing bounded native cache; render the existing semantic
tile/image fallback when the current handle is terminally unavailable; retain
layout, clipping, focus, hit testing, UIA name/role, and tile action; emit an
error diagnostic only when a handle/revision enters a new terminal failure
state rather than on every paint; invalidate exactly once when result state
changes; allow a new handle/revision or explicit retry generation to recover;
representative unavailable, late-success, failure-then-new-revision,
repaint/snapshot-refresh, cache-eviction, Game Launcher, and Games & Apps
fixtures.

**Out of scope:** eager catalog/icon probing, changing Steam/source discovery,
inventing artwork bytes, per-widget IDs or layouts, hiding all renderer errors,
unbounded failure history, remote-HTTPS retry redesign, public protocol/API
changes, DLV-025 compositor changes, screenshots, or the canonical aggregate.

**Acceptance:** every unavailable trusted artwork tile remains visibly
actionable with the shared glyph and no layout/focus/accessibility shift; each
widget/handle/revision terminal failure produces at most one diagnostic until
its state changes; repeated paint, snapshot refresh, focus, and transition
frames produce no duplicate; pending work is not prematurely shown as failure;
late success and newer revisions replace the fallback; stale results cannot
replace current artwork; cache/error bookkeeping stays within existing bounds;
available artwork and remote images retain current behavior.

**Verification:** Tier 1 RemoteImageCache, declarative renderer, shared tile/
image, and host Release tests. Tier 2 uses one production-shaped Game Launcher
and Games & Apps fixture containing available, pending, and unavailable trusted
handles, then repaints and republishes the same snapshot while asserting pixels/
semantic fallback state and one diagnostic transition. Build OverlayHost
Release. No screenshot harness, provider suite, aggregate, or live Steam
account.

**Stop:** a correct fallback cannot be expressed without a public presentation
contract change, failure identity is not available without crossing provider
authority, or the change would add unbounded host state. Preserve evidence for
a serialized contract assignment rather than adding widget-specific behavior.

**Queue note:** DLV-106 crossed its clean boundary and became active before the
DLV-107 planner correction arrived, so finish it without interruption while the
planner reviews committed DLV-105. DLV-107 is mandatory next because live
packaged evidence rejects DLV-025's transparency and motion result. DLV-102
follows DLV-107. DLV-033 is dependency-blocked and internal; DLV-062 remains
blocked by its material resource gate.

## Integration queue

### DLV-033 — Establish a host-owned widget session coordinator

**State:** Awaiting accepted DLV-025 architecture decision and bridge baseline
**Intended lead:** platform with a serialized managed-bridge prerequisite
**Dependencies:** DLV-025 and accepted DLV-032

Create a directly tested `WidgetSessionCoordinator` above
`WidgetBridgeClient` owning descriptor/snapshot collections, catalog retry,
lifecycle target, runtime/presentation generation, typed session status, and
bounded asynchronous request completion. `OverlayApp` remains the Win32,
focus, renderer, D2D/DWrite, and presentation adapter. No HWND/renderer state in
the coordinator and no generic event bus. Prove replacement, removal, last-good
retry, stale rejection, start/snapshot/protocol failure, lifecycle drain, and
Close/Guide responsiveness while another request stalls.

This is internal follow-up and must not displace an unblocked visible milestone.

## Blocked work

| Item | Blocker | Unblocking evidence |
| --- | --- | --- |
| DLV-062 trusted fixed-video surface | One visible paused WebView2 surface measured about 348.7 MiB private memory and 4% CPU against the current 128-MiB gate; supported suspension controls require invisibility and do not solve visible cost. | User changes the budget or authorizes a content/process-specific bounded experiment with a hard stop and no account work. |
| Audio Mixer default input/output selection | No documented supported Windows setter is established; roadmap forbids undocumented `PolicyConfig`, registry writes, or Shell automation. | Primary Microsoft API evidence plus a reversible provider/hardware plan. |
| Live Spotify Web Playback | Account, Premium eligibility, allowlist, OAuth, and EME. | User-authorized account and retained manual evidence. |
| YouTube authenticated library | Google OAuth and account; Watch Later is not supported by the Data API. | Approved minimum-scope OAuth plan and user-authorized account. |
| Physical controller/display/audio/Bluetooth/game/accessibility matrix | Requires user hardware or interactive environment. | Retained named packaged/manual evidence. |

## Verification queue

These items are evidence work, not implementation authority:

1. Packaged controller/visual matrix for issues still marked Verifying.
2. Real YT Music companion pairing/reconnection and physical controller.
3. Live Spotify pagination/failure/OAuth/Web Playback/device behavior when an
   authorized account exists.
4. Physical Y-hold exactly-once refresh.
5. Physical Narrator/MSAA traversal.
6. Packaged Spotify seek/list traversal and transient-failure recovery.
7. Packaged widget-switch transparency and temporal continuity after DLV-107.
8. Games & Apps cold-restart, trusted artwork, and running-app live checks.
9. Audio Mixer LB/RB/X physical dashboard controls.

## Recent acceptance delta

Older assignment text and evidence are in the timestamped history snapshot.
Keep only the latest meaningful integrated delta here.

| Assignment | Accepted implementation | Integrated main | Visible/product result |
| --- | --- | --- | --- |
| DLV-105 | `42bcf9c` | `d23db8b` | Compact trays keep the selected widget visible and expose explicit previous/next controls; controller order, pointer hit testing, and full-set UIA semantics share one layout. |
| DLV-025 / correction DLV-107 | `16f9f47` rejected by live verification | `5b8556a` retained as correction baseline | PID 25164 proved complete-surface ordering alone is insufficient: the composed client has an opaque black perimeter and transition cadence is visibly poor. DLV-107 follows already-active DLV-106. |
| DLV-100 | `760a9bb` | `54fbd12` | Game Launcher and protected Network Controls TextEntry snapshots pass the canonical bridge style route; the packaged Release is running for live confirmation. |
| DLV-094/096/098/099 | `fb7fa34` contiguous widgets prefix | `c6d76a3` | Steam artwork is demand-only, stale-safe, generation-coupled, and fully drained before provider disposal. |
| DLV-095/097 | corrected running-app prefix through `46d1938` | `c6d76a3` | Games & Apps and Game Launcher add a validated current running app through opaque trusted authority. |
| DLV-087/091/093 | corrected Network Controls prefix through `3ce8991` | `63ca3a2` | Protected Personal Wi-Fi and exact Bluetooth removal use host-owned credential/authority boundaries. |
| DLV-084/085/088/089/090 | corrected launcher/settings prefix | `dc1bc16` | Durable Game Launcher hide/restore and exact Settings private-state reset. |
| DLV-078 | `6d30f5e` | `a072d6f` | Prior admitted content remains visible through a cold destination start while stale authority is revoked. |

After each accepted integration, retain only enough current evidence to select
and review the next work. Do not create another snapshot while this live file
has 1,000 or fewer physical lines; after it exceeds 1,000, create one complete
timestamped snapshot and compact it according to the planner goal.
