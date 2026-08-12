# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-12 04:18 -07:00

Planning owner: independent review and delivery-planning agent

Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[`history/delivery-plan/2026-08-12T04-18-56-07-00.md`](history/delivery-plan/2026-08-12T04-18-56-07-00.md).
That snapshot is historical evidence, not implementation authority.

## Current accepted baseline

- Local main: `73e1e17`; worktree clean when this plan was published.
- Latest implementation integration: DLV-143 through `cc0018a`.
- Visible accepted Release: PID 25004 from exact accepted main, launched at
  03:59:28. The first dashboard frame is bottom-anchored at absolute
  `1785,1164,1549,236` inside host `1785,481,1549,919`.
- Required live smoke passed all eight first pages with zero named UIA bounds
  outside the host and no worker/protocol/provider/presentation error.
- DLV-134 managed commit `e847502` is held on the widgets branch. It is not in
  main and must not be integrated without platform DLV-145.

## Execution protocol

Implementation tasks follow [`implementation-agent-goal.md`](implementation-agent-goal.md).
The following assignment rules are binding:

- Each lane executes only its one Assigned milestone, then immediately consumes
  its first same-lane Ready milestone in document order.
- A lane does not edit this file or reviewer-owned roadmap/review/issue files.
- New findings outside the Assigned scope are reported for planner triage; only
  a reproducible P0 or destructive/data-loss risk may preempt active work.
- Review corrections queue next and do not interrupt a coherent milestone the
  lane already started.
- Shared protocol/architecture work is serialized. Concurrent assignments have
  exclusive ownership boundaries and must not edit the same production files.
- A lane consumes new main or a held dependency only at a clean committed
  boundary after explicit planner instruction. Substantial conflict stops.
- Tests are proportional: Tier 1 affected Release suites; Tier 2 the smallest
  changed boundary; Tier 3 only when explicitly named. Every command is bounded.
- Do not repeat an unchanged failing command or run dirty and exact-commit
  aggregates. Existing executable suites keep their runner; new managed test
  projects use MSTest.Sdk 4.3.2.
- Malformed/clipped capture is discarded immediately. Product work uses live
  functional/state/semantic/accessibility/timing/log evidence and user verdicts.
- Widget-private application complexity is not arbitrarily bounded. Messages,
  current render trees, update rates, queues, native/GPU resources, and other
  submissions to the shared host remain explicitly bounded.
- Security stabilization stays frozen absent a reproducible P0, demonstrated
  threat-model violation, or planned-release blocker.
- No push, credentials, external publication, destructive recovery, or
  substantial conflict resolution.

## Widgets lane

Task: `Implementation agent — widgets lane`

Branch: `codex/impl-widgets`

### Current assignment — DLV-135: deterministic Launcher Experience authoring toolchain

**State:** Assigned after committed DLV-134 managed boundary `e847502`.
DLV-130 and platform DLV-131 are integrated. Do not claim production preview
through the ordinary overlay until platform DLV-145 is accepted.

**Baseline/dependencies:** merge current planner main `73e1e17` at the clean
widgets boundary while retaining `e847502`. Use the accepted strict data-only
catalog, recipe, GBSS, sealed-asset, digest, and install validators. DLV-145 may
change only the private native adoption hook and is concurrent.

**Owner:** widgets lane for `gbar launcher-theme new`, `validate`, `preview`,
`pack`, `inspect`, `install`, `list`, and `remove`; deterministic fixture data;
focused CLI/catalog tests; and directly affected public author documentation.

**User/developer outcome:** an author can create, validate, preview, package,
inspect, install, list, and remove a data-only Launcher Experience without
hand-authoring undocumented JSON or reverse-engineering native host code.

**Objective/scope:** every command must share the exact production manifest,
recipe, GBSS, static-asset, digest, package, and catalog validators. Generate a
minimal valid project atomically; preview the four accepted native presets over
deterministic semantic fixture libraries; create deterministic immutable
archives; inspect without execution; install by ID/version; and remove only the
exact selected package/version under existing catalog authority.

**Acceptance:** compact/standard/wide fixture preview covers empty, 20-game,
2,000-game, offline, long-title, missing-art, active-operation, 150%-scale,
reduced-motion, reduced-transparency, and high-contrast states. Repeated pack
produces identical bytes/digest. Validation fails closed for unknown fields,
remote URLs, HTML/JS, executables, path escape/reparse points, oversized or
multi-frame assets, missing/duplicate critical slots, action/provider bindings,
and inaccessible branches. Install/list/remove preserve immutable ID/version
semantics and never grant game/content authority.

**Architecture/verification:** reuse production validators rather than copy
schemas into CLI tests. Keep orchestration out of the Gbar root hotspot through
focused command owners and provide a before/after responsibility map. Tier 1
Gbar/catalog/packaging Release tests and documentation contracts. Tier 2 native
offscreen launcher fixture only for the existing preview contract; do not claim
ordinary-overlay production adoption before DLV-145. No aggregate, network,
gallery, signing, automatic update, animated media, credentials, or capture.

**Stop:** a command requires a second schema/validator, executable preview code,
pack-authored actions/provider bindings, remote content, a public protocol
change, or native production-hook edits. Report the exact dependency.

### Held dependency — DLV-134: project state into four native experiences

**State:** managed boundary committed as `e847502`, independently reviewed as
coherent, and held unintegrated. Game Launcher passes 68/68.

It adds persisted Hero Rail, Cover Wall, Carousel, and Compact Grid selection
and one pure projection into exactly six host-known semantic slots: details
panel, game rail, collection tabs, source status, operation status, and
controller hints. It preserves exact SavedId, action, focus, cursor, provider,
and organization authority. An invalid selection falls back to Hero Rail
without resetting unrelated state. It adds no public protocol or native power.

The complete DLV-134 visible outcome requires platform DLV-145. Do not integrate
`e847502` alone because its visible selection would otherwise have no effect.
After DLV-145 commits, review the pair for compact/standard/wide and 150%-scale
fit, exact A/View/X/Y/LB/RB/Back dispatch, focus/profile persistence, 2,000-game
bounded cursor projection, missing-art/long-title fallback, and ordinary-path
recovery.

### Widgets Ready queue

No later widget milestone is executable until DLV-135 commits and the planner
reviews DLV-134/DLV-145 integration order. The lane must stop at that clean
boundary rather than invent provider, enrichment, content-operation, or account
work.

## Platform lane

Task: `Implementation agent — platform lane`

Branch: `codex/impl-platform-switch`

### Current assignment — DLV-145: adopt Game Launcher slots in production

**State:** Assigned. Start from clean accepted main `73e1e17`, then consume held
managed dependency `e847502` on the platform branch only. Neither partial is
integrable as the visible DLV-134 outcome until this native hook is accepted.

**Owner:** platform lane for a focused private Launcher Experience production-
projection owner, ordinary host render/pointer/focus/scroll/UIA integration,
focused native tests, production-host proof, and directly affected native
documentation.

**Concurrency:** widgets owns DLV-135 CLI/catalog tooling. No managed Game
Launcher, Widget SDK/protocol, pack schema, provider, Settings, compositor/
HWND/placement, reviewer document, capture, credential, aggregate, or push
changes.

**User-visible outcome:** selecting Hero Rail, Cover Wall, Carousel, or Compact
Grid changes the live native presentation while the same games, exact actions,
focus identities, cursor position, source truth, and Back route remain usable.

**Objective/scope:** recognize only the first-party
`game-launcher-experience` root plus exactly one of each six documented slot
classes emitted by `e847502`. Map the closed selected-profile class to the
existing native preset. Route the admitted snapshot, current focus, responsive
viewport, accessibility/render options, and work area through
`LauncherExperienceAdapter::RenderExperience`. Use its canonical semantic
snapshot and render geometry for paint, pointer, focus navigation, scroll
reveal, action dispatch, and UIA. Missing, duplicate, malformed, unknown, or
incompatible projection uses the ordinary declarative path atomically with one
bounded diagnostic. Other widgets never enter this path. Preserve instance,
sequence, input scope, collection anchor/keys, exact node/action/focus IDs,
scroll semantics, and last-good presentation.

**Acceptance:** all four profiles run through the ordinary production host at
compact/standard/wide, 125% DPI, 150% interface/text scale, taskbar-reserved,
and non-primary work areas. Each retains exact A/View/X/Y/LB/RB/Back dispatch,
nearest valid focus across profile changes, collection continuation, pointer/
paint/focus/UIA agreement, long-title/missing-art fallback, and bounded current
tree/native resources. A 2,000-game cursor fixture proves only the current
bounded window crosses the host. Invalid projection and adapter failure retain
usable ordinary Game Launcher content; non-launcher widgets remain semantic-
equivalent to the prior path.

**Architecture/verification:** `main.cpp` remains a hotspot. Put recognition,
slot extraction, preset selection, fallback, and canonical-result ownership in
one focused production adapter seam; provide a before/after responsibility map
and do not add a large Game Launcher branch or domain state to `OverlayApp`.
Tier 1 launcher adapter/layout/renderer/focus/pointer/UIA Release groups; Tier 2
one fresh production-host fixture using the held installed Game Launcher plus
the retained managed 68/68 evidence. No aggregate or screenshot/capture gate.

**Stop:** the hook requires a public protocol/API, changes the six-slot managed
schema, duplicates widget domain/organization state in native code, cannot
preserve scroll/action identity, or requires a new window/compositor owner.
Return the exact mismatch instead of weakening fallback or editing managed code.

### Platform Ready queue

No later platform milestone is executable until DLV-145 commits. The next
launcher safe-start/recovery and effect-performance production tasks depend on
accepted ordinary-host projection and will be assigned only from reviewed
evidence; do not manufacture them in parallel.

## Serialized integration queue

1. Hold widgets `e847502` and the future DLV-145 native commit together until
   end-to-end DLV-134 acceptance. Do not expose the no-op picker on main.
2. DLV-135 may integrate after the pair, or independently only when review
   confirms its artifacts and docs make no ordinary-overlay preview claim that
   depends on the unaccepted hook.
3. External metadata/artwork, GOG/Amazon adapters, trusted content operations,
   and pack gallery/update work remain later scoped milestones.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| Trusted fixed-video/PiP surface | One paused visible WebView2 surface measured about 348.7 MiB private memory and 4% CPU against the 128-MiB gate. | User changes the budget or authorizes a content/process-specific bounded experiment. |
| Audio default input/output selection | No documented supported Windows setter is established; undocumented PolicyConfig/registry/Shell mutation is forbidden. | Primary Microsoft API plus reversible provider/hardware plan. |
| Direct computer-control discovery | Tool/owned no-taskbar OverlayHost is omitted; making it discoverable adds taskbar/Alt-Tab eligibility. HWND/UIA input is sufficient for planner smoke. | User accepts shell presence or the supported control tool gains tool-window discovery. |
| Native uninstall reconciliation | Production-host synthetic catalog removal emitted no managed revision/native event. | Bounded managed catalog-monitor assignment reproduces disabled/nonresident removal and supplies a deterministic event. |
| Live Spotify Web Playback | Premium eligibility, allowlist, OAuth, EME, and account. | User-authorized account and retained manual evidence. |
| YouTube authenticated library | Google OAuth/account; Watch Later is not supported by the Data API. | Approved minimum-scope OAuth plan and user-authorized account. |
| Physical controller/display/audio/Bluetooth/game/Narrator matrix | Requires user hardware or interactive physical evidence. | Retained named packaged/manual results. |

## Verification queue

1. User visual verdict on PID 25004 for cold dashboard position, Spotify first-
   page fit, switching borders/flicker, and Game Launcher/Games & Apps layout.
2. Physical Audio Mixer LB/RB/X tray actions and reverse traversal. Planner's
   current four-session keyboard path reaches every row and returns to Master.
3. Game Launcher shortcuts/top controls/last-row continuation and exact launch.
4. Spotify seek/list traversal, pagination/reverse focus, transient failure,
   OAuth/Web Playback/device behavior when an authorized account exists.
5. YT Music real companion pairing/reconnection and physical controller.
6. Physical Y-hold exactly-once tray refresh, Narrator/MSAA, mixed-DPI/display,
   Bluetooth/audio hardware, and game foreground input.
7. Packaged widget-switch transparency/temporal continuity and long-run resource
   baselines at a named release checkpoint.

## Recent accepted milestones

| Assignment | Implementation | Integrated main | Result |
| --- | --- | --- | --- |
| DLV-143 | `157384f` | `cc0018a` | First compact dashboard commit is bottom-anchored; paint, pointer, UIA, and absolute diagnostics agree. |
| DLV-144 | `bbed0bc` | `abb1e8d` | Spotify compact player retains the controller rail, one focus-revealing viewport, and cross-branch focus identity; 49/49. |
| DLV-130/138/139/142 | `da08c44`, `c82f111`, `2281548`, `ecfdd18` | `a3f883e` | Normalized app-library model, focused validation, Windows/Xbox and opt-in Epic installed sources; SDK 89/89, Games 62/62, Launcher 65/65. |
| DLV-133 | `4c58eba` | `483f8e4` | Launcher-scoped style/artwork/recovery owner and production semantic lifecycle proof; 1,361 checks. |
| DLV-131/132/137/140/141 | `100c646`, `874f778`, `41da5b7`, `5e3c69c`, `878b484` | `2f766fe` | Strict data-only catalog, standard WebP, four responsive native presets, package-entry bounds; 1,307 semantic checks. |
| DLV-128/129 | `344caa0`, `7f7d9d5` | `67c557d`, `d792e30` | Bounded console hero rail and deterministic dev cancellation fixture. |
| DLV-127 | `df07d7c` | `2ac0a5a` | Stable shared shell/tray and work-area fit across widget switching. |
| DLV-126 | `a45166c` | `2c58ba7` | Game Launcher top controls, contextual shortcuts/help, and collection continuation; 60/60. |
| DLV-123/125 | `552d250`, `ccabb0e` | `8c40a6d`, `418d11f` | Exact uninstall confirmation and canonical verifier inclusion for MSTest.Sdk scenarios. |
| DLV-118/121 | `5d86cd6`, `fb0ad51` | `4bc0baa`, `a758508` | Exact tray catalog replacement and corrected Audio Mixer production traversal evidence. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. On crossing 1,000, snapshot and compact according to
[`review-planner-goal.md`](review-planner-goal.md).
