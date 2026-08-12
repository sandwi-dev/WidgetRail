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

### Current widgets assignment

**State:** No Assigned item. Accepted DLV-113/119/122 is integrated through
`4949f6e` and awaits the freshly packaged user's Settings picker and Now Playing
recovery verdict. The remaining named widgets features require fresh live
evidence, unsupported audio-endpoint authority, store APIs not established as
supported consumer contracts, or authentication. Do not invent backend work to
keep the lane busy.

### Widgets ready queue

**Queue note:** widgets work waits on fresh live results for the newly packaged
Settings local install, Game Launcher details/clipping, and Now Playing stage/
code recovery surfaces. The planner will insert any live correction before
unrelated work; no speculative filler is authorized.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-121 — Restore the Audio Mixer production focus target fixture

**State:** Assigned; the platform lane moved on immediately after committing
DLV-118 and has no task-specific DLV-121 commit yet
**Baseline/dependencies:** clean platform boundary `064a257`, containing
accepted DLV-118 `5d86cd6` and accepted main `31ed686`; begin from the exact
accepted-main Release failure
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

### Platform ready queue

No later platform item is authorized. DLV-121 remains active; its result must
reach a coherent commit or an exact managed-authorship stop before the planner
refills this queue.

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
| DLV-118 | `5d86cd6` | `4bc0baa` | Small and wide widget surfaces retain one selected tray identity, explicit reachable overflow, exact order, and synchronous catalog replacement without stale tray dispatch. |
| DLV-113/119/122 | `0994809`, `670e01d`, and `6f604b8` | `4949f6e` | Settings exposes the exact host-owned local package picker with disabled review, while Now Playing adds safe stage/code diagnostics, Retry/activation recovery, and last-good retention; stale public install claims are removed. |
| DLV-114/117 | `f285c9d` and `6a96727` | `cff0d99` | Game Launcher has an exact-ID controller details route and honest two-selection variant actions with committed feedback and deterministic return focus. |
| DLV-112/115/116 | `1dd2dd5`, `5c66f16`, and `0e1810f` | `d6f2780` | Host-owned local package import is consumable only by exact bundled Settings, and reopened widget motion retires hidden composition work; live border/transition verdict remains. |
| DLV-111 | `a1f008b` | `83c5c1f` | Settings now groups exact installed theme versions, protects built-in/current selections, selects older valid versions, and confirms atomic inactive-version retirement with CLI parity. |
| DLV-033 | `1ec2b70` | `4fa8f63` | Bridge-facing catalog, snapshot, failure, lifecycle, retry, and request policy now has one off-UI-thread coordinator; the host retains renderer/input/presentation authority. |
| DLV-110 | `e90e729` | `4e02184` | Basic, data, media, and multipage starters now generate atomically outside the checkout with MSTest.Sdk 4.3.2 scenarios, isolated preview, validation, and deterministic packaging. |
| DLV-107/102 | `b662e9a` and `e4f9880` | `0e0bf77` | Transparent unused client pixels, transform-only widget motion, and stable deduplicated trusted-artwork fallback. Rebuilt Release awaits live verification. |
| DLV-108/109 | `f51a983` and `5db3426` | `bc484f0` | Isolated named semantic scenarios and an optional deterministic lifecycle/action/fake-service test API. Coherent managed/runtime Release repackaged and relaunched. |
| DLV-104/106 | `99e4932` and `f119a1f` | `1ef4666` and `dde4981` | Bounded Game Launcher collection viewport and atomic tray focus ownership. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. After it exceeds 1,000, create one complete timestamped snapshot and
compact it according to `review-planner-goal.md`.
