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
**Concurrency:** Runs while widgets owns DLV-111. Do not touch SettingsWidget,
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
