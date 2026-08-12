# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-12 09:04 -07:00

Planning owner: independent review and delivery-planning agent

Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[`history/delivery-plan/2026-08-12T04-18-56-07-00.md`](history/delivery-plan/2026-08-12T04-18-56-07-00.md).
That snapshot is historical evidence, not implementation authority.

## Current accepted baseline

- Local product baseline: `d78fc98`; worktree clean when this plan was
  published.
- Latest implementation integration: DLV-148 and DLV-154 through `d78fc98`.
- Visible accepted Release: PID 31668, launched at 09:00:03 after the accepted
  Launcher Experience production presentation and Game Launcher categories. Its first
  dashboard frame is
  bottom-anchored at absolute
  `1785,1164,1549,236` inside host `1785,481,1549,919`.
- DLV-148 passes 1,685 focused native checks on integrated main; DLV-154 passes
  73/73 focused managed checks, and each lane retained its required ordinary-
  host/worker boundary evidence. The required computer-control pass cannot target the
  production no-redirection tool window: the control API omitted it while
  exposing a platform test fixture with the same title. Do not substitute that
  fixture or infer product defects from capture; user visual testing remains
  the first-page gate.
- DLV-148 `34e3746` and DLV-154 `a78693c` were reviewed and cherry-picked as
  `838af96` and `d78fc98`. DLV-149 remains held only until DLV-150 consumes it.

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

### Current assignment — DLV-156: scalable categories and direct collection switching

**State:** Assigned correction immediately after accepted DLV-154 `a78693c`.

**Baseline/dependencies:** widgets branch `a78693c`; accepted product
`d78fc98`. Preserve DLV-154's exact identity, atomic category-only recovery,
CAS, stale display, and launch revalidation. DLV-150 is concurrent and native/
Settings-owned; do not consume it or edit held DLV-149.

**Owner:** widgets lane for Game Launcher category policy/state/projection,
controller shortcuts and hints, focused tests, and directly affected docs only.

**User-visible outcome:** categories are useful for a real library rather than
a four-item demo. LT/RT cycle directly through All Games and every user category
without opening management, preserving a valid focused exact game when possible.

**Objective/scope:** remove the prototype four-category/four-member/eight-total
product caps. The existing 64-KiB serialized-state admission boundary remains
authoritative because it crosses shared host storage; use generous safety
ceilings only to bound validation work, not to make ordinary organization fail
first. Support at least 32 categories and at least 256 compact SavedId
memberships in one retained focused fixture when the encoded state remains
under the byte boundary. Add LT/RT category cycling to the existing Library and
Category scopes with truthful contextual hints. Empty categories remain
browsable and manageable.

**Acceptance:** no ordinary category fails at four members or aggregate eight.
Exact-ID create/rename/delete/assign/remove, duplicate-name rejection, category-
only malformed/oversize reset, CAS replay, missing-display retention, restart,
and current launch revalidation remain green at the larger matrix. LT/RT wrap
once across All Games plus category order, never dispatch while a modal/
ActionSheet/TextEntry owns input, and retain nearest current exact focus or one
truthful empty-state action. Byte-budget rejection is explicit and preserves
the last committed organization state.

**Architecture/verification:** keep category validation/mutation/projection in
the focused category owner and keep `GameLauncherWidget` as lifecycle/effect/
committed-state adapter. Do not add unbounded materialization or duplicate
navigation/state owners. Tier 1 Game Launcher Release suite; Tier 2 extend the
installed generic-worker category route through LT/RT and restart. No native,
provider, public SDK/protocol, aggregate, capture, account, metadata, held-
DLV-149, or reviewer-document edit.

**Stop:** practical capacity requires exceeding the shared 64-KiB submission,
new public persistent-storage API, provider query/schema change, or native host
work. Report that architecture requirement for a separate serialized milestone
rather than smuggling it into this correction.

### Accepted milestone — DLV-154: Game Launcher categories and exact membership

**State:** Done and accepted through main `d78fc98`; DLV-156 immediately corrects
the overly small prototype capacity without reopening its exact identity model.

Continue from the clean
widgets-lane boundary. The held DLV-149 commit may remain in branch ancestry but
must not be extended or documented as live; this assignment is Game Launcher
managed code and focused tests only.

**Baseline/dependencies:** accepted product `b92e0ff`; widgets branch
`febbb41`. DLV-148 is concurrent and native-only. Do not consume, edit, or
depend on platform motion, Launcher Experience selection, GOG launch, public
protocol, SDK, provider, Settings, or native-host code.

**Owner:** widgets lane for Game Launcher private organization state,
presentation/navigation routes, directly affected focused tests, and feature/
implementation documentation only.

**User-visible outcome:** the user can create a small named category, assign or
remove the exact focused game from Y's game-action flow, and browse that category
as a controller-reachable collection. Membership and category names survive an
overlay restart. Deleting a category removes only that category's membership;
it never removes, hides, launches, or merges a game.

**Objective/scope:** implement the missing M1 category slice from
GL-CAT-003/004/005 using bounded Game Launcher private state. Category identity
is a generated opaque local ID; membership is exact SavedId only. Provide All
Games plus user categories through the existing collection/navigation owners,
and a focused create/rename/delete/assign/remove route using current SDK
components. Preserve the existing Y action sheet as the entry point and View as
the sole full-details route. Retain current search/source/install/favorite/
hidden/recent/manual/variant semantics and provider pagination authority.

**Acceptance:** category count, name length, membership count, and serialized
state size are explicitly bounded and malformed/oversize pre-release state
resets only the affected category fields atomically. Duplicate normalized names
are rejected visibly. Create, rename, delete, assign, remove, Back, restart,
stale-provider, missing-item, and compare-and-swap replay cases preserve exact
identity and unrelated organization state. A category view contains only its
current exact members, retains sanitized last-good display when a member is
temporarily missing, and cannot authorize launch without current provider
revalidation. Controller hints and disabled/busy states match admitted actions.

**Architecture/verification:** keep immutable category policy/projection outside
`GameLauncherWidget`; the root remains the lifecycle/effect/committed-state
adapter. Reuse existing navigation, text-entry/action-sheet, private-state CAS,
and display projection owners. Add a before/after responsibility map. Tier 1
Game Launcher focused Release suite; Tier 2 one ordinary generic-worker route
covering create, assign, restart, category browse, and exact launch
revalidation. No aggregate, native fixture, screenshot/capture gate, account,
network, metadata provider, content operation, or reviewer-document edit.

**Stop:** satisfying category filtering requires title-derived identity,
unbounded full-library materialization, provider query/schema changes, a public
SDK/protocol change, native presentation changes, or edits to held DLV-149.
Report the missing seam rather than broadening the assignment.

### Accepted milestone — DLV-153: correct unsupported GOG launch authority

**State:** Done and accepted through main `e5f9ece`. DLV-149 remains held for
DLV-150.

**Baseline/dependencies:** continue from clean widgets `f282237`; do not rewrite
DLV-147 or DLV-149. Official GOG documentation describes client-owned file
tasks and says games must remain launchable without Galaxy, but it documents no
external `GalaxyClient.exe /command=runGame /gameId /path` integration contract.
The fixed registry/info-file reader may remain as explicitly best-effort local
installed evidence; it cannot grant Launch or claim a supported client API.
DLV-148 remains concurrent and native-only.

**Owner:** widgets lane for the GOG source/reader/launcher correction, focused
provider and installed-worker tests, and directly affected GOG feature/status
documentation. Do not change DLV-149 selection behavior in this correction.

**User-visible outcome:** opt-in GOG discovery remains useful and honest: the
two widgets may show validated locally installed GOG games with GOG attribution,
but those rows do not expose Play until a supported exact launch contract is
implemented. The UI must not imply that a disabled action is a transient error.

**Objective/scope:** remove `WindowsGogLauncher` and every production/test/doc
claim that the undocumented Galaxy command line is supported. GOG normalized
items must carry no Launch action/capability/authority and must never reach
`Process.Start`. Retain disabled zero-I/O, bounded fixed registry/info reading,
opaque identity, source health, same-title separation, cancellation, stale
display, and failure isolation. Describe this as best-effort installed evidence,
not an official API or proof that every GOG installation will be found.

**Acceptance:** no `/command=runGame`, `/gameId`, `/path`, Galaxy process start,
GOG Launch action, or launcher-started evidence remains. Provider tests require
validated GOG items to be installed but non-launchable; both widgets render the
same disabled/unavailable action truth without a GOG-specific UI branch. The
installed generic-worker route proves the GOG row reaches the widget while an
attempted Play cannot cross the broker/provider boundary. DLV-147's remaining
disabled, malformed, duplicate, mixed-validity, cancellation, same-title,
refresh, and stale-source cases remain green. Documentation names the support
boundary precisely and does not call the registry or client route supported.

**Architecture/verification:** keep the focused reader/source separation and
remove the now-invalid launcher owner rather than replacing it with direct game
executable parsing, Start Menu title correlation, URI guessing, raw file-task
arguments, or another heuristic. Tier 1 provider, Games & Apps, and Game
Launcher focused Release suites. Tier 2 the one installed generic-worker route.
No aggregate, account, network, helper, public protocol, DLV-149 behavior,
native, capture, or reviewer-document edit.

**Stop:** preserving launch requires any undocumented client switch, raw game
executable/arguments, title/path correlation, arbitrary URI, credential,
network API, helper executable, or public protocol change. Remove launch and
report the future supported-contract blocker instead of substituting another
route.

### Accepted dependency — DLV-134/DLV-145/DLV-146: live native experiences

**State:** Done and accepted through main `4aa7284`. Managed 68/68, native
1,588 checks, deterministic production adoption/fallback, and Game Launcher
68/68 all pass independently.

It adds persisted Hero Rail, Cover Wall, Carousel, and Compact Grid selection
and one pure projection into exactly six host-known semantic slots: details
panel, game rail, collection tabs, source status, operation status, and
controller hints. It preserves exact SavedId, action, focus, cursor, provider,
and organization authority. An invalid selection falls back to Hero Rail
without resetting unrelated state. It adds no public protocol or native power.

The ordinary installed widget now adopts Hero Rail, Cover Wall, Carousel, and
Compact Grid through the exact private six-slot seam. A deterministic test-only
trusted provider drives every preset and return-to-Hero through the ordinary
worker, bridge, capability, native host, UIA, focus, and Back path. Provider-
unavailable fallback is a separate required scenario rather than a substitute
for adoption.

### Widgets Ready queue

1. **Held — DLV-149: trusted Launcher Experience selection and Settings
   management.** Commit `f282237` adds exact ID/version selection, global
   appearance, one-action Hero Rail recovery, last-good state, metadata/unsigned
   disclosure, and selected-version removal denial. It remains unintegrated
   until DLV-150 privately consumes it. Do not redo or expose it alone.
2. **Ready after DLV-156 — DLV-157: exact per-game title override.** Add a
   controller-reachable TextEntry action from the existing game action sheet
   that stores or clears one bounded user title override by exact SavedId. The
   override affects presentation/search only, never provider identity, source,
   launch authority, or another variant. Preserve provider title for recovery
   and expose Reset title. Use the category/CAS state owners and focused worker
   evidence; no artwork/file picker, provider, public protocol, or native work.

## Platform lane

Task: `Implementation agent — platform lane`

Branch: `codex/impl-platform-switch`

### Current assignment — DLV-150: installed Launcher Experience selection, last-good reload, and safe start

**State:** Assigned after accepted DLV-148 `34e3746`. At the clean platform
boundary, cherry-pick held widgets DLV-149 `f282237` as the exact managed
dependency; do not merge the widgets branch or consume DLV-154/156. Stop on a
material conflict.

**Baseline/dependencies:** platform branch `34e3746`; accepted product
`d78fc98`; held dependency `f282237`. Consume exact trusted PlatformSettings
selection through one private host boundary, load only the accepted immutable
catalog version, apply recipe/GBSS/sealed assets through existing owners, retain
last-good on invalid reload, and provide one documented controller safe-start
gesture that bypasses the selected pack for one Game Launcher activation.

**Owner/concurrency:** platform lane owns the private Settings/catalog consumer,
reload/recovery/safe-start state, native fixtures and directly affected status/
feature docs. Widgets DLV-156 owns managed Game Launcher categories. Do not edit
category/widget/provider/public SDK/protocol/shared-host bounds or reviewer docs.

**Acceptance/verification:** selection and global-appearance modes activate the
exact installed immutable version; valid replacement is atomic; missing,
invalid, removed, tampered, or incompatible reload retains last-good and one
bounded diagnostic. Safe start bypasses the custom pack exactly once without
mutating selection. Built-in recovery remains controller complete. Preserve one
window/compositor/renderer, current focus/action/UIA identity, accessibility
finality, and DLV-148 presentation budgets. Tier 1 focused Settings/catalog/
Launcher Experience native suites; Tier 2 ordinary installed selection,
reload/failure/recovery/safe-start host fixture. No aggregate or capture gate.

**Stop:** a public protocol, arbitrary code/script, remote asset, second
renderer/window, unsigned executable content, substantial merge conflict, or
change to managed selection semantics is required.

### Accepted milestone — DLV-148: production Launcher Experience motion and effect budget

**State:** Done and accepted through main `838af96`. Consume current planner main at a clean committed boundary
before task-specific edits. If the equivalent cherry-picked DLV-134/145/146
chain conflicts with the platform ancestry, stop and report rather than
resolving a material conflict.

**Baseline/dependencies:** accepted product baseline `4aa7284`. Reuse
`LauncherExperienceProjection`, `LauncherExperiencePresentation`, the one
shared compositor/window, and the deterministic seeded ordinary-host fixture.
DLV-153 is a managed GOG-provider correction and is concurrent; do not touch
provider, Settings, managed widget, SDK, protocol, or catalog code.

**Owner:** platform lane for connecting the already accepted launcher-scoped
presentation/effect owner to the ordinary adopted Launcher Experience path;
decode-before-crossfade, focus motion, accessibility overrides, measured
degradation, diagnostics, and focused native/production-host evidence.

**Concurrency:** no managed Game Launcher, Widget SDK/protocol, pack schema or
catalog, provider, Settings, HWND/work-area/tray placement, reviewer document,
capture, credential, aggregate, or push changes.

**User-visible outcome:** changing focus in any live native Game Launcher
experience produces smooth host-owned focus/background motion without flicker,
dark borders, semantic lag, or input delay. Reduced motion removes translation/
scale, and an overloaded renderer degrades effects before navigation.

**Objective/scope:** the ordinary projection must consume one immutable
presentation frame from the existing presentation owner and commit background,
slot paint, focus geometry, pointer geometry, and UIA from one current adopted
sequence. Use only the accepted opacity, scale, translation, outline, and
background-crossfade targets. Decode a new background completely before a
single atomic swap; retain the last-good/fallback frame on failure. Pause or
finish immediately while hidden/unselected and close degradation in explicit
steps when measured input/render budgets are exceeded.

**Acceptance:** seeded ordinary-host fixtures traverse at least two games in
all four presets and prove no dark/uninitialized interval, no stale-background
commit, stable exact focus/action/collection/UIA identity, and pointer/focus/
paint agreement for every sampled frame. Reduced motion has no translation or
scale; high contrast/reduced transparency remain final. Corrupt/missing/stale
art retains last-good or built-in fallback. A named 60-second stress window
records p95 input-to-focus under 50 ms and deterministic effect degradation;
the test uses semantic/timing/log evidence, not screenshots. Ordinary provider
fallback remains usable and effect-free where no projection is admitted.

**Architecture/verification:** keep `LauncherExperiencePresentation` as the
sole style/artwork/effect/recovery state owner and
`LauncherExperienceProjection` as the sole production adoption owner; do not
move either concern into `OverlayApp`. Provide a before/after responsibility
map and correct the stale DLV-134/DLV-145 implementation-status wording while
documenting DLV-146 and this milestone. Tier 1 focused launcher presentation,
layout, renderer, focus, pointer, UIA, and targeting Release suites. Tier 2 the
seeded ordinary-host motion/degradation/fallback fixture. No canonical
aggregate or screenshot/capture gate.

**Stop:** the outcome requires a second window/compositor/renderer, public
protocol, managed widget/pack schema change, background URL/path exposure,
arbitrary animation/media, or a widget-specific work-area offset. Report the
missing private seam rather than adding parallel presentation authority.

### Platform Ready queue

1. **Ready after DLV-150 — DLV-152: custom-pack production matrix.** Prove the
   bottom-rail and left-rail/glass reference recipes plus compact/standard/wide,
   150% text, reduced motion/transparency, high contrast, long title, missing
   art, invalid pack, removal denial, and exact recovery in the ordinary host.

## Serialized integration queue

1. DLV-134/DLV-145/DLV-146 are accepted and integrated through `4aa7284`.
2. DLV-147/DLV-153/DLV-151 are accepted through `b92e0ff`; DLV-148 and DLV-154
   are accepted through `d78fc98`.
3. Hold DLV-149 `f282237` until DLV-150 consumes its selection/recovery state;
   integrate
   the pair as one visible outcome.
4. External metadata/artwork, Amazon adapters, trusted content operations,
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

1. User visual verdict on PID 31668 for cold dashboard position, Spotify first-
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
| DLV-134/145/146 | `e847502`, `7a566be`, `8c6adb4` | `f88aa58`, `3a22bf9`, `4aa7284` | Persisted four-profile Game Launcher projection adopted through the ordinary production worker/bridge/host path; 1,588 native checks, explicit seeded adoption/fallback host scenarios, managed 68/68. |
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
