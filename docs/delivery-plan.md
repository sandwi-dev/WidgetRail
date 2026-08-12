# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-11
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

### DLV-111 — Make installed theme versions manageable in Settings

**State:** Assigned after accepted DLV-110
**Baseline:** accepted DLV-110 `e90e729` integrated into main through `4e02184`
plus the planner assignment commit
**Dependencies:** immutable theme install/discovery, exact theme pinning,
Settings Appearance picker, and no-poll theme reload
**Owner:** widgets lane; `PlatformSettings` theme mutation policy, Settings
Appearance presentation/actions, `gbar theme` parity, focused managed tests,
and directly affected theme documentation
**Concurrency:** Managed Settings/tooling only; no native renderer, compositor,
public widget capability, signing, remote update, or package-trust redesign.

**User-visible outcome:** Settings groups installed theme versions, clearly
identifies the exact active version, lets the user select an older valid version,
and removes an inactive user-installed version after confirmation. Built-in and
currently selected versions remain visibly protected instead of requiring
manual filesystem cleanup.

**Objective:** Complete the smallest safe controller-first theme-version
management slice over the existing immutable catalog and exact appearance pin.

**In scope:** grouped ID/version presentation; stable controller focus and Back;
exact valid-version selection; explicit confirmation for removal; an atomic
catalog-owned inactive-version retire operation; CLI `theme remove` parity for
an exact ID/version; invalid inactive versions remaining reviewable/removable;
watcher-driven reconciliation; safe actionable failure feedback; affected docs.

**Out of scope:** importing or updating packages, file pickers, remote discovery,
automatic updates, gallery/signing/revocation, graphical preview, arbitrary
asset support, built-in removal, selected-version removal, compatibility shims,
or native UI changes.

**Acceptance:** Settings can traverse one/many IDs and versions at compact and
standard profiles, select an exact valid version without rewriting immutable
content, and remove only the confirmed inactive user version. Built-in or
selected versions are non-actionable; malformed identities, reparse paths,
concurrent catalog changes, cancellation, and deletion failure preserve the
appearance record and unrelated versions. Success publishes one coherent
catalog/settings refresh with deterministic focus. CLI and Settings use the
same mutation policy rather than duplicating filesystem rules.

**Verification:** Tier 1 PlatformSettings, Settings, CLI theme, and docs suites.
Tier 2 uses one temporary catalog with multiple IDs/versions and proves exact
selection, inactive removal, watcher reconciliation, cancellation/failure, and
unchanged unrelated content. No aggregate or capture.

**Stop:** safe removal requires deleting a selected/built-in theme, following an
untrusted reparse point, weakening immutable install rules, or adding a new
public/remote authority.

**Queue note:** DLV-110 `e90e729` is accepted and integrated through `4e02184`.
DLV-111 is the next visible widgets milestone; internal SDK expansion may not
displace it.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-033 — Establish a host-owned widget session coordinator

**State:** Assigned after accepted DLV-107/102
**Baseline:** accepted main through `bc484f0` plus the planner assignment commit
**Dependencies:** accepted DLV-032 request dispatcher, DLV-078 retained-content
ordering, and DLV-107 composition/presentation authority
**Owner:** platform lane; native bridge-facing widget session state and focused
native tests/documentation
**Concurrency:** Runs while widgets owns developer-visible DLV-110. Do not touch
managed SDK/templates, renderer/compositor geometry, pinned surfaces, public
protocol, reviewer documents, or capture tooling.

**Release-enabling outcome:** Normal widget-session changes no longer require
understanding the roughly application-sized `OverlayApp`; later virtualized
Game Launcher and rich-media work can add session behavior without growing the
Win32/render/input owner again.

**Objective:** Extract one directly tested `WidgetSessionCoordinator` above
`WidgetBridgeClient` owning descriptor/snapshot collections, catalog retry,
lifecycle target, runtime/presentation generation, typed session status, and
bounded asynchronous request completion. `OverlayApp` remains the Win32,
focus, renderer, D2D/DWrite, input, and presentation adapter.

**In scope:** before/after logical-type responsibility map; descriptor and
snapshot replacement/removal; last-good retry; current-generation admission;
typed start/snapshot/protocol failure; lifecycle target/drain; bounded pending
request correlation; cancellation and late completion; one stalled widget
while Close/Guide and another widget remain responsive; focused dependency
injection without a generic service locator.

**Out of scope:** public protocol/API changes, compositor or UI redesign,
renderer/focus/input state in the coordinator, generic event bus/mediator,
managed bridge rewrite, new feature behavior, compatibility shims, screenshot
work, or the canonical aggregate.

**Acceptance:** `OverlayApp` loses the named session collections/generations/
retry/lifecycle request policy and materially shrinks; one coordinator owns
that mutable knowledge with a narrow value/event boundary; no second renderer,
focus, input, HWND, or presentation authority appears; replacement/removal,
last-good retry, stale rejection, failure classes, drain, and concurrent stall
are deterministic; existing widget lifecycle, retained presentation, Close,
Guide, and hidden/idle behavior do not change.

**Verification:** Tier 1 coordinator, WidgetBridgeClient, OverlayState,
lifecycle, switch/retained-content, failure-feedback, and Release host suites.
Tier 2 uses two production-shaped widget sessions with one stalled request and
proves the other session plus Close/Guide remain responsive. No aggregate or
capture.

**Stop:** extraction requires public protocol/product behavior changes, creates
another authority owner, materially couples to DLV-110 managed work, or cannot
reduce `OverlayApp` mutable knowledge without a broader architecture choice.
Preserve the clean boundary and report evidence instead of moving code by file
count alone.

**Queue note:** DLV-107 `b662e9a` and DLV-102 `e4f9880` are accepted and
integrated through `0e0bf77`. DLV-033 is the one permitted internal platform
milestone while widgets delivers DLV-110; another internal milestone may not
follow it. DLV-062 remains blocked by the rich-media resource gate, so fewer
than three safe platform Ready items exist.

## Blocked work

| Item | Blocker | Unblocking evidence |
| --- | --- | --- |
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
7. Packaged widget-switch transparency and temporal continuity after DLV-107.
8. Games & Apps cold-restart, trusted artwork, and running-app live checks.
9. Audio Mixer LB/RB/X physical dashboard controls.

## Recent acceptance delta

| Assignment | Accepted implementation | Integrated main | Visible/product result |
| --- | --- | --- | --- |
| DLV-110 | `e90e729` | `4e02184` | Basic, data, media, and multipage starters now generate atomically outside the checkout with MSTest.Sdk 4.3.2 scenarios, isolated preview, validation, and deterministic packaging. |
| DLV-107/102 | `b662e9a` and `e4f9880` | `0e0bf77` | Transparent unused client pixels, transform-only widget motion, and stable deduplicated trusted-artwork fallback. Rebuilt Release awaits live verification. |
| DLV-108/109 | `f51a983` and `5db3426` | `bc484f0` | Isolated named semantic scenarios and an optional deterministic lifecycle/action/fake-service test API. Coherent managed/runtime Release repackaged and relaunched. |
| DLV-104/106 | `99e4932` and `f119a1f` | `1ef4666` and `dde4981` | Bounded Game Launcher collection viewport and atomic tray focus ownership. |
| DLV-101/103 | `f27f4d7` and `f87631c` | `9c5de8e` | Full-application worker process trees and dependable Now Playing retry/last-good behavior. |
| DLV-105 | `42bcf9c` | `d23db8b` | Explicit compact-tray overflow with shared controller/pointer/UIA order. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. After it exceeds 1,000, create one complete timestamped snapshot and
compact it according to `review-planner-goal.md`.
