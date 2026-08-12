# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-12 05:08 -07:00

Planning owner: independent review and delivery-planning agent

Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[`history/delivery-plan/2026-08-12T04-18-56-07-00.md`](history/delivery-plan/2026-08-12T04-18-56-07-00.md).
That snapshot is historical evidence, not implementation authority.

## Current accepted baseline

- Local main: `e6dc10e`; worktree clean when this plan was published.
- Latest implementation integration: DLV-135 through `e6dc10e`.
- Visible accepted Release: PID 9192, launched at 05:03:26 after the accepted
  DLV-135 toolchain integration. DLV-135 changes only Gbar/catalog artifacts;
  the overlay runtime graph remains the accepted DLV-143/DLV-144 graph. The
  first dashboard frame is bottom-anchored at absolute
  `1785,1164,1549,236` inside host `1785,481,1549,919`.
- Required live smoke passed all eight first pages with zero named UIA bounds
  outside the host and no worker/protocol/provider/presentation error in the
  exact PID 9192 interval.
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

### Accepted assignment — DLV-135: deterministic Launcher Experience authoring toolchain

**State:** Done and accepted as widgets `3c93abb`, independently integrated on
main as `e6dc10e`. The implementation makes no production-overlay preview
claim and does not integrate held DLV-134.

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

No later widget milestone is safely executable while platform DLV-145 is
changing the ordinary Game Launcher production path and held DLV-134 remains in
the widgets ancestry. The lane is intentionally stopped at clean `3c93abb`;
after paired DLV-134/DLV-145 acceptance, the planner will assign the next
visible launcher recovery or experience-management outcome from production
evidence. Do not invent provider, enrichment, content-operation, account, or a
second consecutive tooling-only milestone merely to keep the lane busy.

## Platform lane

Task: `Implementation agent — platform lane`

Branch: `codex/impl-platform-switch`

### Current assignment — DLV-146: prove production Launcher Experience adoption

**State:** Assigned correction after DLV-145 commit `7a566be`. DLV-145's focused
1,540-check fixture is green, but its production-host fixture accepted a
provider-unavailable ordinary fallback and therefore did not prove the assigned
native adoption path. `e847502` and `7a566be` remain held and unintegrable.

**Baseline/dependencies:** continue from clean platform `7a566be`, which already
contains held managed `e847502`. Do not merge newer main or rewrite either
commit during this correction.

**Owner:** platform lane for the production-host proof and a test-only seeded
app-library/broker fixture needed to drive the held installed Game Launcher
through its ordinary worker, bridge, capability, and native-host path. Existing
DLV-145 production code remains in scope only if that proof exposes a concrete
native defect.

**Concurrency:** DLV-135 is accepted and the widgets lane is stopped cleanly.
No managed Game Launcher, Widget SDK/protocol, pack schema, provider, Settings, compositor/
HWND/placement, reviewer document, capture, credential, aggregate, or push
changes.

**User-visible outcome:** the held pair has credible production-path evidence
that selecting Hero Rail, Cover Wall, Carousel, or Compact Grid really changes
the installed Game Launcher instead of silently falling back to the ordinary
tree.

**Objective/scope:** replace the permissive `if adoption else fallback passes`
production-host result with two explicit deterministic scenarios. The required
success scenario seeds at least one trusted installed, launch-capable game
through a test-only provider behind the normal capability boundary, starts the
real held installed Game Launcher, and fails unless the production host logs
and UIA prove adoption. A separate provider-unavailable scenario may retain
ordinary fallback coverage, but it cannot satisfy adoption. Reuse existing
broker simulator/protocol owners; do not add a second production provider or a
test bypass in production package/widget/protocol code.

**Acceptance:** the fresh production-host fixture observes
`Launcher Experience projection adopted` for Hero Rail, Cover Wall, Carousel,
Compact Grid, and the return to Hero Rail; exposes the seeded game's exact
stable UIA/focus identity inside host bounds after every profile change; and
retains the same instance, sequence, input scope, collection anchor/key, action
IDs, and Back route. The fallback scenario independently proves provider-error
content stays usable without an adoption marker. Existing 1,540 focused checks
remain green. No success path may be conditional on local installed games,
machine state, or elapsed provider discovery.

**Architecture/verification:** keep `LauncherExperienceProjection` as the sole
production projection owner. Any new managed code is a test-only provider
fixture published only into the isolated host-test installation. Tier 1 reruns
the existing 1,540 launcher checks only if production code changes. Tier 2 runs
the deterministic adoption and fallback production-host scenarios plus retained
managed Game Launcher 68/68 evidence. No aggregate or screenshot/capture gate.

**Stop:** deterministic seeding requires a public API/protocol, a production
provider bypass, changes to the held six-slot schema, or product credentials.
Return the exact missing test boundary instead of accepting fallback again.

### Platform Ready queue

No later platform milestone is executable until DLV-146 commits. The next
launcher safe-start/recovery and effect-performance production tasks depend on
accepted ordinary-host projection and will be assigned only from reviewed
evidence; do not manufacture them in parallel.

## Serialized integration queue

1. Hold widgets `e847502`, platform DLV-145 `7a566be`, and correction DLV-146
   together until end-to-end DLV-134 acceptance. Do not expose the picker on
   main without deterministic production adoption.
2. DLV-135 is already integrated independently as `e6dc10e`; its artifacts and
   docs make no ordinary-overlay preview claim and require no pair action.
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

1. User visual verdict on PID 9192 for cold dashboard position, Spotify first-
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
| DLV-135 | `3c93abb` | `e6dc10e` | Deterministic data-only Launcher Experience new/validate/preview/pack/inspect/install/list/remove workflow; 61/61 CLI, 17/17 catalog, 1,361 native offscreen checks. |
| DLV-143 | `157384f` | `cc0018a` | First compact dashboard commit is bottom-anchored; paint, pointer, UIA, and absolute diagnostics agree. |
| DLV-144 | `bbed0bc` | `abb1e8d` | Spotify compact player retains the controller rail, one focus-revealing viewport, and cross-branch focus identity; 49/49. |
| DLV-130/138/139/142 | `da08c44`, `c82f111`, `2281548`, `ecfdd18` | `a3f883e` | Normalized app-library model, focused validation, Windows/Xbox and opt-in Epic installed sources; SDK 89/89, Games 62/62, Launcher 65/65. |
| DLV-133 | `4c58eba` | `483f8e4` | Launcher-scoped style/artwork/recovery owner and production semantic lifecycle proof; 1,361 checks. |
| DLV-131/132/137/140/141 | `100c646`, `874f778`, `41da5b7`, `5e3c69c`, `878b484` | `2f766fe` | Strict data-only catalog, standard WebP, four responsive native presets, package-entry bounds; 1,307 semantic checks. |
| DLV-128/129 | `344caa0`, `7f7d9d5` | `67c557d`, `d792e30` | Bounded console hero rail and deterministic dev cancellation fixture. |
| DLV-127 | `df07d7c` | `2ac0a5a` | Stable shared shell/tray and work-area fit across widget switching. |
| DLV-126 | `a45166c` | `2c58ba7` | Game Launcher top controls, contextual shortcuts/help, and collection continuation; 60/60. |
| DLV-123/125 | `552d250`, `ccabb0e` | `8c40a6d`, `418d11f` | Exact uninstall confirmation and canonical verifier inclusion for MSTest.Sdk scenarios. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. On crossing 1,000, snapshot and compact according to
[`review-planner-goal.md`](review-planner-goal.md).
