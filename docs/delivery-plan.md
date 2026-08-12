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

**Queue note:** No later widgets item is Ready because DLV-101 may reveal a
separate scalable-storage prerequisite. Do not manufacture speculative
successors before that evidence exists.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-025 — Eliminate transition tearing and UI-thread stutter

**State:** Assigned; bounded DirectComposition prototype authorized 2026-08-11
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

### DLV-102 — Fall back cleanly when trusted app artwork is unavailable

**State:** Ready after DLV-025
**Baseline:** accepted DLV-025 platform boundary plus integrated DLV-100 main
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

**Queue note:** DLV-102 is the first Ready platform item after DLV-025 because it
is a reproduced visible regression. DLV-033 remains dependency-blocked on the
accepted compositor result and is internal; DLV-062 remains blocked by its
material resource gate.

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
7. Packaged widget-switch temporal continuity after DLV-025.
8. Games & Apps cold-restart, trusted artwork, and running-app live checks.
9. Audio Mixer LB/RB/X physical dashboard controls.

## Recent acceptance delta

Older assignment text and evidence are in the timestamped history snapshot.
Keep only the latest meaningful integrated delta here.

| Assignment | Accepted implementation | Integrated main | Visible/product result |
| --- | --- | --- | --- |
| DLV-100 | `760a9bb` | `54fbd12` | Game Launcher and protected Network Controls TextEntry snapshots pass the canonical bridge style route; the packaged Release is running for live confirmation. |
| DLV-094/096/098/099 | `fb7fa34` contiguous widgets prefix | `c6d76a3` | Steam artwork is demand-only, stale-safe, generation-coupled, and fully drained before provider disposal. |
| DLV-095/097 | corrected running-app prefix through `46d1938` | `c6d76a3` | Games & Apps and Game Launcher add a validated current running app through opaque trusted authority. |
| DLV-087/091/093 | corrected Network Controls prefix through `3ce8991` | `63ca3a2` | Protected Personal Wi-Fi and exact Bluetooth removal use host-owned credential/authority boundaries. |
| DLV-084/085/088/089/090 | corrected launcher/settings prefix | `dc1bc16` | Durable Game Launcher hide/restore and exact Settings private-state reset. |
| DLV-078 | `6d30f5e` | `a072d6f` | Prior admitted content remains visible through a cold destination start while stale authority is revoked. |

After each accepted integration, retain only enough current evidence to select
and review the next work. Create a new timestamped snapshot before this live
file exceeds the context budget in the planner goal.
