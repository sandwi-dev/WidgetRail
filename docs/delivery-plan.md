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

### DLV-113 — Add local widget installation to Settings

**State:** Assigned
**Baseline:** accepted main `cff0d99`; first integrate that exact main at the
clean DLV-117 boundary
**Dependencies:** accepted DLV-112/116 private host-owned Settings import
operation integrated through `d6f2780`
**Owner:** widgets lane; Settings Installed Widgets presentation/state, focused
managed tests, and directly affected Settings/package documentation
**Concurrency:** managed Settings code only; no native picker/import/bridge,
catalog installer, package policy, public community capability, renderer,
compositor, or reviewer-document changes.

**User-visible outcome:** Installed Widgets exposes one controller-accessible
**Install local widget** action. A chosen `.gbarwidget` is installed disabled for
review, while cancellation, busy, invalid, duplicate, stale, and failure results
are understandable and never expose the selected path.

**Objective/in scope:** render the exact action/source/input-scope contract
owned by DLV-116; project busy and bounded path-free host feedback; refresh the
installed inventory after a successful disabled publication; restore exact
focus after cancel/failure/success; keep B and existing version-management
behavior deterministic.

**Out of scope:** native changes, generic file or package authority, worker-
visible paths, auto-enable/consent, remote acquisition, signing, updates,
rollback redesign, theme import, or package-policy duplication.

**Acceptance:** cancel is quiet; repeated activation cannot open a second
picker; one valid package appears disabled for review; duplicate/invalid/stale/
transport failures are actionable and path-free; forged or ordinary Settings
actions remain ordinary worker input; B, exact focus return, built-in/current
version protection, and compact/standard/150% reachability remain correct.

**Verification:** Tier 1 Settings action/state/navigation/layout and docs;
Tier 2 production-shaped exact action to host operation to disabled inventory
refresh, including cancel/repeat/stale/failure/path-free outcomes. No aggregate
or capture.

**Stop:** any native change, worker path/filesystem authority, auto-enable,
generic picker capability, package-policy redesign, or conflicting public API.

### Widgets ready queue

#### DLV-119 — Diagnose and correct the Now Playing load recurrence

**State:** Ready after DLV-113
**Owner/dependencies:** widgets lane; Now Playing/Media Sessions managed widget,
existing typed provider/resource seams, focused tests, and affected docs.
**Outcome/scope:** begin from the user's accepted-Release **Media sessions could
not be loaded** recurrence. Correlate the newest accepted host and worker/provider
logs before choosing the smallest owning seam. Preserve last-good sessions and
Retry; distinguish no sessions from provider/transport/lifecycle failure; reject
stale completion and avoid a parallel retry/task owner.
**Acceptance/verification:** activation and Retry recover from the reproduced
stage without restarting the overlay; safe visible state identifies a typed
stage/code; healthy last-good rows remain usable during transient failure; no
provider body/path leaks; focused resource, retry, lifecycle, transport-failure,
and empty-state suites pass. No aggregate or capture. Stop for native protocol,
provider authority, credentials, or an unreproduced root-cause guess.

**Queue note:** further widgets work waits on fresh live results for the newly
packaged Game Launcher details/clipping and media-session surfaces. Do not
manufacture internal filler merely to reach three Ready items.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-118 — Keep the complete icon tray reachable at small surfaces

**State:** Assigned
**Baseline:** accepted main `cff0d99`; first integrate that exact main at the
clean DLV-116 boundary
**Dependencies:** existing DLV-105 compact overflow and current host-owned tray
inventory/focus authority
**Owner:** platform lane; native shell/tray measurement, overflow projection,
focus/UIA order, pointer hit testing, focused native tests, and affected display
documentation
**Concurrency:** native tray/shell only; no widget presentation trees, package
import, renderer/compositor transition redesign, managed widget code, catalog
policy, public SDK/protocol, or reviewer-document changes.

**User-visible outcome:** opening a small/compact widget never makes installed
widgets silently disappear from the icon tray. Every tray identity remains
reachable through the existing explicit overflow interaction with stable
controller, pointer, and accessibility order.

**Objective/in scope:** reproduce the user's small-widget state from logical
surface/tray metrics, then correct the smallest host-owned inventory/overflow
projection or focus-retention seam. Preserve the selected widget and exact tray
order across resize, compact-to-wide switching, catalog replacement, and
rapid widget cycling. Do not infer layout failure from malformed captures.

**Out of scope:** enlarging every widget, per-widget tray hints, horizontally
unbounded icons, hiding overflow without an affordance, widget-specific native
IDs, transition/compositor rework, visual redesign, or catalog semantics.

**Acceptance:** first/middle/last and selected identities are reachable at the
minimum documented logical surface and 100/125/150% scale; overflow is visible,
named, keyboard/controller/pointer/UIA operable, and returns to the exact item;
no icon vanishes merely because widget extent shrinks; resizing and catalog
churn preserve one focus owner with no stale dispatch.

**Verification:** Tier 1 tray layout/focus/overflow/accessibility/targeting;
Tier 2 production-host semantic state across small widget, large widget, rapid
cycling, and catalog replacement. Functional/semantic evidence only; no capture
or aggregate.

**Stop:** correction requires a new tray information architecture, public
protocol, per-widget special case, compositor redesign, or material UX choice.

### Platform ready queue

#### DLV-121 — Restore the Audio Mixer production focus target fixture

**State:** Ready immediately after DLV-118
**Baseline/dependencies:** accepted main `048e0a3` plus the completed DLV-118
boundary; begin from the exact accepted-main Release failure
`AudioMixerScrollHostTests failed: Requested production UIA focus target was
absent.` Do not rerun the unchanged command merely to seek a pass.
**Owner:** platform lane; production-host focus/UIA projection, scroll reveal,
fixture admission, and directly affected focused tests/docs. If the missing node
is authored by AudioMixerWidget rather than lost by the host, stop with the
exact semantic snapshot evidence for widgets-lane reassignment.
**Outcome/scope:** make the real accepted-main Master-to-session and session-to-
Master traversal expose one current, reachable UIA focus target at the previously
failing state. Trace the emitted snapshot, admitted input scope, rendered bounds,
and projected UIA tree before changing the smallest owner. Preserve DLV-049's
keyboard/controller behavior and DLV-106's tray focus ownership.
**Acceptance/verification:** the focused production fixture fails before the
change and passes after it; exact Up/Down reaches Master and the first/last
session without cycling, hidden/clipped/stale nodes are excluded, and keyboard,
controller, UIA, four-session, and variable-session states agree. Run this
focused fixture once after the coherent correction, plus directly affected
focus/scroll/accessibility suites. No aggregate, screenshot, or repeated
unchanged rerun.
**Stop:** evidence locates the defect in managed Audio Mixer authorship, requires
a public protocol/layout redesign, or cannot reproduce from retained semantic/
log state.

**Queue note:** further platform correction depends on the user's fresh verdict
for integrated DLV-115 (black border/transition), DLV-104 (Game Launcher
clipping), and DLV-106 (Audio Mixer tray-Left focus). These are live-verification
gates, not permission to repeat backend work. Do not manufacture filler.

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
7. Packaged widget-switch transparency and temporal continuity after DLV-115.
8. Games & Apps cold-restart, trusted artwork, and running-app live checks.
9. Audio Mixer LB/RB/X physical dashboard controls.

## Recent acceptance delta

| Assignment | Accepted implementation | Integrated main | Visible/product result |
| --- | --- | --- | --- |
| DLV-114/117 | `f285c9d` and `6a96727` | `cff0d99` | Game Launcher has an exact-ID controller details route and honest two-selection variant actions with committed feedback and deterministic return focus. |
| DLV-112/115/116 | `1dd2dd5`, `5c66f16`, and `0e1810f` | `d6f2780` | Host-owned local package import is consumable only by exact bundled Settings, and reopened widget motion retires hidden composition work; live border/transition verdict remains. |
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
