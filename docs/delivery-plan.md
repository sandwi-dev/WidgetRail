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

### DLV-117 — Correct Game Launcher details action semantics

**State:** Assigned as the bounded correction to returned DLV-114
**Baseline:** accepted main `0946594` plus unintegrated DLV-114 candidate
`f285c9d`; preserve that commit and add one correction commit
**Dependencies:** DLV-114's exact-SavedId details route and the existing
two-selection variant organization policy
**Owner:** widgets lane; Game Launcher details projection/routing, focused
managed tests, and directly affected Game Launcher documentation
**Concurrency:** Game Launcher managed code only; no Settings, package import,
native host, renderer/compositor, app-library provider, public protocol/SDK,
store adapter, launch authority, or reviewer-document changes.

**Review disposition:** DLV-114 is not accepted yet. Its exact identity,
unavailable refusal, Back restoration, pure presenter/policy split, and focused
50/50 evidence are credible. The details page is nevertheless misleading: it
labels one activation **Group variant** or **Ungroup variant**, while the reused
operation only starts/finishes a two-distinct-game sequence, and the resulting
status is absent from the details presentation.

**User-visible outcome:** Game details never promises an immediate group or
ungroup operation that one selected game cannot perform. Starting or completing
variant selection has clear controller feedback and a deterministic route back
to the library; direct launch, favorite, hide, preference, and Back remain
honest and exact.

**Objective/in scope:** preserve DLV-114's details route and correct only the
variant action projection/route transition and affected feedback. A singular
details action may start the existing two-game selection, complete it only when
the selected identity is a valid distinct second member, or direct the user to
the library; its label/state must describe what that activation actually does.
Ensure successful hide closes details only after the exact mutation commits.
Keep A/X/Y/LB/RB meanings and exact return focus/scroll.

**Out of scope:** new organization semantics, new metadata/provider calls,
one-click guessed grouping, title-based pairing, a new state coordinator,
native changes, public SDK/protocol changes, or unrelated Game Launcher polish.

**Acceptance:** no enabled control says Group/Ungroup unless that single
activation performs exactly that operation. The first selection produces
visible feedback and returns to a reachable distinct-game choice; the second
selection groups or removes only the exact intended SavedIds, same-identity and
stale selections fail closed, and busy/cancellation/CAS failure remains honest.
Hide, preference, unavailable state, B focus restoration, and A/X/Y/LB/RB
regressions are covered. The 1,207-line root gains no task, lock, timer,
resource, provider, committed-state owner, or cosmetic partial.

**Verification:** Tier 1 Game Launcher action/navigation/layout and docs suites;
Tier 2 exact first/second selection, existing-group removal, stale/same identity,
hide-close-after-commit, compact/standard/150% semantic reachability, and
unchanged controller mappings. No aggregate or capture.

**Stop:** correction requires new provider data, organization semantics, public
API/protocol, native special-casing, or another lifecycle/state authority.

### Widgets ready queue

#### DLV-113 — Add local widget installation to Settings

**State:** Ready after accepted/integrated DLV-117 and DLV-116
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

### DLV-115 — Correct the live-reopened widget-switch border and cadence

**State:** Assigned and active after the DLV-112 candidate boundary
**Baseline:** accepted main `0946594`, unintegrated DLV-112 candidate `1dd2dd5`,
and its clean main merge `7635998`; DLV-115 may finish but neither platform
prefix can integrate until DLV-116 is accepted
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

### Platform ready queue

#### DLV-116 — Make local package import consumable and its evidence real

**State:** Ready immediately after DLV-115
**Baseline/dependencies:** preserve DLV-112 candidate `1dd2dd5` and DLV-115;
correct the DLV-112 prefix in one new commit before any Settings consumer.
**Owner:** platform lane; private native Settings action admission, local import
test credibility, implementation-status record, and directly affected package
documentation. Do not edit SettingsWidget or reviewer documents.

**Review disposition:** DLV-112 is not accepted yet. Its exact current-Settings
admission, modal picker, reparse-resistant locked stream, catalog-owned publish,
disabled result, revision event, bounded result queue, and managed failure tests
are credible. However `OverlayApp::BeginLocalWidgetPackageImport` has no caller
or typed consumer seam, so widgets-only DLV-113 cannot invoke it without native
work. The added `WidgetBridgeCatalogTests` checks use `assert` under Release
`/DNDEBUG` and therefore provide no executed framing assertions. The required
`docs/implementation-status.md` milestone/responsibility record is also absent.

**Objective/in scope:** expose one closed, production-reachable invocation seam
that a later Settings view action can consume without modifying native code,
receiving a path, or gaining generic package/filesystem authority. Admission
must bind the exact rendered Settings element/action, current bundled identity,
Interactive lifecycle, instance, runtime generation, presentation generation,
and input scope before the host opens the picker. Use a named private contract
with one owner; do not scatter unchecked string comparisons. Convert the added
native completion/framing cases to hard Release checks and record DLV-112/116's
before/after ownership/evidence in implementation status.

**Out of scope:** Settings presentation, public community capability, generic
file picker, auto-enable/consent, remote install, signing, updates, theme import,
CLI child process, renderer/compositor changes, or package-policy redesign.

**Acceptance:** a production-shaped exact Settings action reaches the picker;
forged widget/package/publisher/instance/generation/input-scope/action/source,
stale/Background/Visible state, repeated activation, and overlay close are
refused or cancelled before authority crosses layers. DLV-113 needs only managed
Settings presentation/state work. Every new native framing assertion executes
in Release and demonstrably fails on malformed/path-bearing/wrong-operation
fixtures. Existing DLV-112 managed tests remain green; docs do not claim the
visible Settings control exists.

**Verification:** Tier 1 local import, native bridge catalog/framing, exact host
action-admission, WidgetBridge import-prefix, catalog, Release host, and docs;
Tier 2 one production-shaped action-to-picker-to-disabled-publication path plus
cancel/stale/forged/path-free outcomes. No aggregate or capture.

**Stop:** correction requires public normal-widget authority, sending a path to
the worker, another package installer, a renderer/input redesign, or a material
choice between incompatible public contracts.

**Queue note:** only DLV-116 can release DLV-112 for integration and unblock
widgets DLV-113. Fewer than three further platform items are pre-authorized
because the active user-visible DLV-115 and this correction own the only current
open packaged regressions; do not manufacture internal filler.

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
