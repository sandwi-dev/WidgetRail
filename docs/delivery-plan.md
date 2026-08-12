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

### DLV-114 — Add controller-first Game Launcher details

**State:** Assigned after accepted DLV-111
**Baseline:** accepted DLV-111 `a1f008b` integrated into main through `83c5c1f`
plus the planner acceptance/assignment commit
**Dependencies:** accepted installed-only Game Launcher, durable organization,
typed launch lifecycle, bounded route navigation, and shared action-sheet/page
components
**Owner:** widgets lane; Game Launcher presentation, route/action policy,
focused managed tests, and directly affected Game Launcher documentation
**Concurrency:** Game Launcher managed code only; no Settings, package import,
native host, renderer/compositor, app-library provider, public protocol/SDK,
store adapter, launch authority, or reviewer-document changes.

**User-visible outcome:** pressing View on a focused library game opens a clear
controller-first details surface. It shows the complete title, source,
availability, launch state, favorite/preferred/group status, and the actions
that already apply to that exact game; B returns to the same tile. A still
launches directly and X/Y/LB/RB keep their current meanings.

**Objective:** Turn existing normalized Game Launcher state into one useful
selected-game details route without broadening trusted authority or making the
application root another presentation monolith.

**In scope:** one View shortcut and visible controller hint; bounded nested
details/action presentation; launch, favorite/unfavorite, hide, and existing
variant/preference actions where valid; stable exact-item identity; disabled or
honest unavailable states; focus/scroll return; compact/standard/wide and long-
label behavior; affected docs.

**Out of scope:** new metadata fields or capability calls, raw paths/processes,
store APIs, remote artwork/metadata, installs/updates, play-time/achievements,
game settings, new launch authority, remapping A/X/Y/LB/RB, native UI changes,
or compatibility shims.

**Acceptance:** View from every actionable library tile opens details for that
exact saved identity and never another same-title variant. Actions reuse the
existing admission/mutation/launch owners, stale or disappearing rows fail
closed, busy/paused states are honest, and B restores exact focus and scroll
when possible with deterministic nearest fallback. Long titles and all content
remain reachable at compact, standard, wide, and 150% text profiles. The root
retains one lifecycle/action/state owner: details projection/routing policy is
separate, adds no task, lock, timer, resource, provider, or committed-state
authority, and any material root growth must be offset by extracting a closed
policy rather than adding another partial declaration.

**Verification:** Tier 1 Game Launcher action, navigation, presentation/layout,
and documentation suites. Tier 2 covers duplicate titles/variants, unavailable
or disappearing selection, busy launch, long labels, exact Back focus, and the
unchanged A/X/Y/LB/RB mappings. No aggregate, capture, or live store dependency.

**Stop:** the route needs new host/provider data, raw launch authority, a public
protocol/SDK change, native focus special-casing, or another lifecycle/state
coordinator.

### Widgets ready queue

#### DLV-113 — Add local widget installation to Settings

**State:** Ready after DLV-114 and accepted/integrated DLV-112
**Owner/dependencies:** widgets lane; consume only DLV-112's bundled-Settings,
host-owned picker/install operation in Settings Installed Widgets.
**Outcome/scope:** add an **Install local widget** action, controller-safe busy/
cancel/result feedback, disabled-package review handoff, exact focus return, and
focused Settings/docs tests. The widget never receives a path or generic file
authority, never duplicates validation, and never auto-enables the package.
**Acceptance/verification:** cancel is quiet, one valid package appears disabled
for review, duplicate/invalid/stale/failure results are actionable and path-free,
repeated input cannot duplicate the picker, B/focus remain deterministic, and
compact/standard/150% Settings plus focused operation tests pass. No aggregate
or capture. Stop for any worker-visible path, non-Settings authority, auto-enable,
or package-policy redesign.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-112 — Establish host-owned local widget package import

**State:** Assigned after accepted DLV-033
**Baseline:** accepted DLV-033 `1ec2b70` integrated into main through `4fa8f63`
plus the planner assignment commit
**Dependencies:** immutable local widget package installer/catalog, authenticated
bridge, exact catalog-change notification, host modal/focus ownership, and
bundled Settings identity
**Owner:** platform lane; host-owned picker adapter, private install request and
origin policy, existing catalog installer integration, bounded platform tests,
and directly affected platform/package documentation
**Concurrency:** Runs while widgets owns DLV-114. Do not touch SettingsWidget,
PlatformSettings, `gbar theme`, renderer/compositor geometry, pinned surfaces,
reviewer documents, or capture tooling. Shared package-import protocol is led
only by this lane until accepted.

**Immediate visible prerequisite:** DLV-113 will add **Install local widget** to
Settings. DLV-112 supplies the one trusted picker/install path so that consumer
does not receive a filesystem path, shell out to `gbar`, duplicate installer
rules, or grant normal widgets package-management authority.

**Objective:** Add a typed host-owned `.gbarwidget` selection and disabled-only
local installation operation restricted to the exact bundled Settings origin
while Interactive.

**In scope:** injectable `IFileOpenDialog` adapter filtered to `.gbarwidget`;
owner-window, cancellation, focus, and activation restoration; exact Settings
origin/lifecycle/generation admission; native-to-bridge request framing; reuse
of the existing immutable package validator/installer; disabled publication;
catalog revision notification; bounded safe status/result returned without the
selected path; deterministic fake-picker and production-shaped catalog tests.

**Out of scope:** Settings presentation/action code, arbitrary file browsing,
worker-visible paths, normal community-widget authority, automatic enablement
or consent, remote URL/GitHub install, updates, signing/revocation, theme import,
custom game selection, shelling out to the CLI, screenshots, or compatibility
shims.

**Acceptance:** only the current Interactive bundled Settings generation can
open one picker and submit one exact selected stream/path to trusted install
code. Cancel is a quiet no-op. A valid package is installed disabled and emits
one catalog revision; invalid, duplicate, stale, background, forged-origin,
reparse/race, cancellation, and installer failure preserve the catalog and
produce bounded path-free diagnostics. The picker cannot outlive overlay close,
duplicate on repeated input, steal focus after cancellation, or expose generic
filesystem authority. Existing CLI install and catalog invariants remain one
shared implementation.

**Verification:** Tier 1 picker/origin policy, bridge request, WidgetCatalog,
catalog-revision, modal/focus, and Release host suites. Tier 2 injects cancel,
valid, invalid, duplicate, stale-origin, and changing-file outcomes through one
production-shaped Settings request and proves disabled publication plus no path
in response/log projections. No aggregate or capture.

**Stop:** implementation requires sending a path to a widget worker, allowing a
non-Settings package to invoke install, enabling without review, duplicating or
weakening package validation, launching a CLI child process, or changing the
native compositor/input authority.

**Queue note:** DLV-033 `1ec2b70` is accepted and integrated through `4fa8f63`.
DLV-112 is the permitted immediate visible prerequisite after that internal
milestone. Its Settings consumer remains serialized as DLV-113 after acceptance.

### Platform ready queue

#### DLV-115 — Correct the live-reopened widget-switch border and cadence

**State:** Ready after DLV-112
**Owner/dependencies:** platform lane; native presentation/composition and
existing GBA-004/GBA-036 alpha, geometry, and transition owners only.
**User-visible outcome:** cycling among Game Launcher, Games & Apps, Spotify,
Audio Mixer, and smaller widgets keeps unused client pixels transparent and the
tray continuously painted, with no black outer rectangle and no return to ugly
multi-step size transitions.
**Scope:** begin from the user's current packaged video/report after accepted
DLV-107; correlate recent typed transition/render logs with current code and
separate transparency failure from cadence/geometry failure before changing the
smallest owning seam. Preserve reduced motion and rapid reversal. Do not debug
screenshots, re-create capture tooling, or revive a rejected prototype merely
because it exists.
**Acceptance/verification:** focused existing alpha, geometry, transition,
targeting, and production-host suites pass with bounded timeouts; semantic and
typed timing evidence proves continuous tray ownership, alpha-zero unused
pixels, no opaque full-client clear, no stale/blank intermediate surface, stable
reversal, and no settled/hidden frame work. The planner then rebuilds and
launches the packaged Release for the user's live verdict; automated capture is
not a closing gate. No aggregate unless the change crosses the named compositor
checkpoint.
**Stop:** evidence requires a new compositor architecture, Windows-version
support decision, material public behavior tradeoff, or substantial conflict.

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
| DLV-111 | `a1f008b` | `83c5c1f` | Settings now groups exact installed theme versions, protects built-in/current selections, selects older valid versions, and confirms atomic inactive-version retirement with CLI parity. |
| DLV-033 | `1ec2b70` | `4fa8f63` | Bridge-facing catalog, snapshot, failure, lifecycle, retry, and request policy now has one off-UI-thread coordinator; the host retains renderer/input/presentation authority. |
| DLV-110 | `e90e729` | `4e02184` | Basic, data, media, and multipage starters now generate atomically outside the checkout with MSTest.Sdk 4.3.2 scenarios, isolated preview, validation, and deterministic packaging. |
| DLV-107/102 | `b662e9a` and `e4f9880` | `0e0bf77` | Transparent unused client pixels, transform-only widget motion, and stable deduplicated trusted-artwork fallback. Rebuilt Release awaits live verification. |
| DLV-108/109 | `f51a983` and `5db3426` | `bc484f0` | Isolated named semantic scenarios and an optional deterministic lifecycle/action/fake-service test API. Coherent managed/runtime Release repackaged and relaunched. |
| DLV-104/106 | `99e4932` and `f119a1f` | `1ef4666` and `dde4981` | Bounded Game Launcher collection viewport and atomic tray focus ownership. |
| DLV-101/103 | `f27f4d7` and `f87631c` | `9c5de8e` | Full-application worker process trees and dependable Now Playing retry/last-good behavior. |
| DLV-105 | `42bcf9c` | `d23db8b` | Explicit compact-tray overflow with shared controller/pointer/UIA order. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. After it exceeds 1,000, create one complete timestamped snapshot and
compact it according to `review-planner-goal.md`.
