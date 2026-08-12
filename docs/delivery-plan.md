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

### Widgets current assignment

### DLV-125 — Restore the canonical verifier manifest and close DLV-123 evidence

**State:** Assigned after committed DLV-126; the widgets lane took this item
immediately without waiting for planner review, as required
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

### Widgets ready queue

No later independent widgets item is pre-authorized after
DLV-125. Game Launcher store breadth lacks a supported consumer API, Audio endpoint selection
lacks a supported setter, YouTube is blocked at the trusted-media cost gate,
and Spotify/YT Music account work needs authentication or live evidence. A
DLV-126 review correction, if any, is queued next without interrupting new work.

## Platform lane

Task identity: `platform`
Branch: `codex/impl-platform-switch`

### DLV-127 — Keep tray placement stable and fit the overlay during widget switching

**State:** Assigned after the clean platform lane merges exact accepted main
containing DLV-123 and this planner assignment
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

### Platform ready queue

### DLV-124 — Reconcile native session, tray, and UIA state after uninstall

**State:** Ready after DLV-127
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

No later independent platform item is authorized after DLV-124. Do not repeat the corrected
Audio Mixer fixture or manufacture compositor/backend work while live user
verdicts remain the closing evidence.

**Queue note:** the user's fresh verdict confirms the moving-tray/work-area
regression and is now DLV-127. DLV-104 Game Launcher content clipping and
DLV-106 Audio Mixer tray-Left focus retain their existing live-verification
dispositions unless the new bounded evidence directly reproduces them. Do not
manufacture adjacent work.

## Serialized integration queue

No cross-lane item is awaiting integration. DLV-126 and DLV-127 consume the
same accepted main baseline and may run concurrently; DLV-125 and DLV-124 then
continue immediately in their respective lanes.

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
