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

### DLV-123 — Uninstall a disabled community widget from Settings

**State:** Assigned after the clean widgets lane merges exact accepted main
containing this planner assignment
**Baseline/dependencies:** accepted main after DLV-121 and the existing typed
catalog capability, Settings installed-package details, enable/disable,
version retirement, clear-local-data, and local package install flows
**Owner:** widgets lane as the serialized lead for the managed catalog
capability/protocol, bridge/catalog operation, Settings consumer, focused
managed tests, and directly affected public installation/retention guidance
**Concurrency:** no native host, renderer, tray, compositor, theme, marketplace,
signing, remote download, updater, credential, or reviewer-document changes.

**User-visible outcome:** a user can open a Community widget in Settings,
disable it, choose **Uninstall widget**, confirm the exact package identity, and
remove all installed immutable versions without using the CLI. Built-in and
enabled packages remain visibly protected.

**Objective/in scope:** extend the existing typed widget-catalog capability with
one exact disabled-only uninstall operation. Bind the request to the current
installed identity/catalog revision or equivalent stale-safe token; revalidate
disabled state and identity in the trusted catalog service; never send a path,
directory, executable, or deletion target to Settings. Add a nested destructive
confirmation with deterministic Cancel/Back and return focus. On success,
refresh the installed list and retain a clear path back to **Install local
widget**. On stale, busy, resident, I/O, recovery-pending, or partial cleanup,
show bounded specific feedback and preserve the current valid catalog.

**Retention policy:** uninstall removes package versions only. It does not
silently remove widget-private local data, credentials, provider data, themes,
or user files. Settings must explain and preserve the separately explicit
**Clear local data** flow. Do not add a combined destructive action.

**Architecture:** this touches the large Settings/catalog surface. Record the
before/after responsibility map and extend the existing installed-package
policy/operation owners; do not add uninstall state, tokens, cancellation, and
view composition directly to another monolithic branch in `SettingsWidget`.

**Acceptance:** controller, keyboard, and semantic action paths show Uninstall
only for a current disabled Community identity; built-in/enabled/stale/forged/
wrong-publisher/wrong-version requests are refused before mutation; successful
uninstall removes every immutable version, publishes one catalog revision,
tears down no unrelated widget, returns stable focus, and retains private data;
pending cleanup is honest and retryable.

**Verification:** Tier 1 Settings presentation/action/persistence and catalog
uninstall/recovery suites; Tier 2 generic worker/bridge capability framing,
catalog revision, stale-operation, resident lease, partial cleanup, and an
unaffected neighbor. Because this changes a public cross-process capability,
run the canonical Tier 3 verifier once from the final coherent commit, not on
both dirty and committed trees. No capture.

**Stop:** the existing trusted catalog cannot expose a path-free stale-safe
uninstall operation, the change requires native shell authority, or retention/
cleanup has materially different product choices not already fixed above.

### Widgets ready queue

**Queue note:** no later independent widgets item is pre-authorized. Game
Launcher store breadth lacks a supported consumer API, Audio endpoint selection
lacks a supported setter, YouTube is blocked at the trusted-media cost gate,
and Spotify/YT Music account work needs authentication or live evidence. A
DLV-123 review correction, if any, is queued next without interrupting new work.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### Current platform assignment

**State:** No Assigned platform item. DLV-121 `fb0ad51` is accepted and
integrated through `a758508`. The next platform outcome depends on accepted
DLV-123 catalog removal or fresh live evidence for existing visual regressions.

### Platform ready queue

No later independent platform item is authorized. Do not repeat the corrected
Audio Mixer fixture or manufacture compositor/backend work while live user
verdicts remain the closing evidence.

**Queue note:** further platform correction depends on the user's fresh verdict
for integrated DLV-115 (black border/transition), DLV-104 (Game Launcher
clipping), and DLV-106 (Audio Mixer tray-Left focus). These are live-verification
gates, not permission to repeat backend work. Do not manufacture filler.

## Serialized integration queue

### DLV-124 — Reconcile native session, tray, and UIA state after uninstall

**State:** Awaiting accepted DLV-123 integration; not executable
**Owner:** platform lane; native catalog-removal reconciliation and focused host
state/focus/accessibility/process-lifecycle evidence only
**Outcome:** uninstalling the selected disabled Community widget cannot leave a
stale tray identity, cached presentation, worker/session generation, pointer
target, or UIA node. Settings remains the exact selected tray identity and sole
focus owner throughout the operation.
**Scope/verification:** begin only from the accepted DLV-123 product path. First
prove whether existing DLV-118 catalog reconciliation and generation teardown
already satisfy the behavior. Add production code only for a reproduced native
gap; otherwise close with a focused production-host uninstall semantic fixture
and documentation. Tier 1/2 only; no capture or aggregate.
**Stop:** DLV-123 is unaccepted, the defect belongs to managed catalog state, or
the desired post-uninstall selection requires a material UX choice.

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
| DLV-121 | `fb0ad51` | `a758508` | The Audio Mixer production fixture now traverses the real clipped/revealed controls instead of directly focusing an absent offscreen UIA node; live product behavior is unchanged and still awaits user verification. |
| DLV-118 | `5d86cd6` | `4bc0baa` | Small and wide widget surfaces retain one selected tray identity, explicit reachable overflow, exact order, and synchronous catalog replacement without stale tray dispatch. |
| DLV-113/119/122 | `0994809`, `670e01d`, and `6f604b8` | `4949f6e` | Settings exposes the exact host-owned local package picker with disabled review, while Now Playing adds safe stage/code diagnostics, Retry/activation recovery, and last-good retention; stale public install claims are removed. |
| DLV-114/117 | `f285c9d` and `6a96727` | `cff0d99` | Game Launcher has an exact-ID controller details route and honest two-selection variant actions with committed feedback and deterministic return focus. |
| DLV-112/115/116 | `1dd2dd5`, `5c66f16`, and `0e1810f` | `d6f2780` | Host-owned local package import is consumable only by exact bundled Settings, and reopened widget motion retires hidden composition work; live border/transition verdict remains. |
| DLV-111 | `a1f008b` | `83c5c1f` | Settings now groups exact installed theme versions, protects built-in/current selections, selects older valid versions, and confirms atomic inactive-version retirement with CLI parity. |
| DLV-033 | `1ec2b70` | `4fa8f63` | Bridge-facing catalog, snapshot, failure, lifecycle, retry, and request policy now has one off-UI-thread coordinator; the host retains renderer/input/presentation authority. |
| DLV-110 | `e90e729` | `4e02184` | Basic, data, media, and multipage starters now generate atomically outside the checkout with MSTest.Sdk 4.3.2 scenarios, isolated preview, validation, and deterministic packaging. |
| DLV-107/102 | `b662e9a` and `e4f9880` | `0e0bf77` | Transparent unused client pixels, transform-only widget motion, and stable deduplicated trusted-artwork fallback. Rebuilt Release awaits live verification. |
| DLV-108/109 | `f51a983` and `5db3426` | `bc484f0` | Isolated named semantic scenarios and an optional deterministic lifecycle/action/fake-service test API. Coherent managed/runtime Release repackaged and relaunched. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. After it exceeds 1,000, create one complete timestamped snapshot and
compact it according to `review-planner-goal.md`.
