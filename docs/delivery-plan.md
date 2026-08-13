# Delivery plan

Status: reviewer-owned two-lane execution queue, 2026-08-12 19:57 -07:00

Planning owner: independent review and delivery-planning agent

Execution owners: `widgets` and `platform`

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[`history/delivery-plan/2026-08-12T04-18-56-07-00.md`](history/delivery-plan/2026-08-12T04-18-56-07-00.md).
That snapshot is historical evidence, not implementation authority.

## Current accepted baseline

- Local product baseline: `6e2c7ff`; worktree clean when this plan was
  published.
- Latest implementation integrations: DLV-168 as `06dc8d0`, the fully
  corrected DLV-172-through-DLV-190 widgets cluster as `ae1dee8`, DLV-160 as
  `9564b96`, DLV-188 as `dd51da1`, DLV-193 as `70a33e3`, and the corrected
  DLV-194/199 external-SDK proof pair as `73117f7`/`c4cf0af`.
- Latest widgets integrations are the corrected DLV-195/196/198/201/202/203
  chain through `ee0c445`, DLV-204 as `04fbdc0`, and DLV-208 as `8e38df5`.
- DLV-197 is integrated as `6e2c7ff`. DLV-205 `a70b223` independently proves
  the missing collection/action continuity but is not integrated: its private
  Game Launcher path is superseded by the Community conversion below and must
  not receive more product investment.
- No accepted Release is currently running after the machine restart. The last
  visible accepted Release was PID 40224, launched at 19:25 after DLV-208.
  Spotify 0.2.15 is installed, selected, and enabled through the supported
  Community package path. Exact UIA activation reaches the new package with no
  fresh startup/product error, but its new unsigned content identity correctly
  has no inherited capability grant; the live surface therefore stops at the
  explicit `Spotify permission is off` gate until the user grants the declared
  capabilities. Layout verification resumes after that consent and remains the
  user's verdict.
  The prior DLV-204 Release evidence remains:
  The widgets chain adds public samples, tooling tests, and documentation rather
  than host product bytes; the Full Application reference rebuilt with zero
  warnings/errors and the ordinary accepted DLV-193 host restarted visibly.
  Its exact startup interval has zero error-class records. The immediately
  prior coherent DLV-193 runtime cycle invoked all eight tray items
  successfully; every first page published expected semantic content and
  exact-session logs admitted all eight transitions with zero product
  error-class records.
  The supported control tool still omitted the
  no-redirection window, but the authorized exact-HWND/UIA fallback invoked all
  eight tray items successfully. Every first page published expected semantic
  content and exact-session logs admitted all eight transitions with zero
  error-class records. Physical visual appearance remains the user's verdict;
  semantic/UIA evidence is not called a screenshot or clipping proof.
- DLV-148 passes 1,685 focused native checks on integrated main; DLV-154 passes
  73/73 focused managed checks, and each lane retained its required ordinary-
  host/worker boundary evidence. The required computer-control pass cannot target the
  production no-redirection tool window: the control API omitted it while
  exposing a platform test fixture with the same title. Do not substitute that
  fixture or infer product defects from capture; user visual testing remains
  the first-page gate.
- DLV-159 `445d016` and DLV-152 `c2a8172` were reviewed and cherry-picked as
  `5b93ce0` and `3a5b46a`; Game Launcher passes 77/77 and native Launcher
  Experience passes 1,691 checks. The platform task then attempted an
  unnecessary merge of current main, hit substantive conflicts in seven shared
  files, preserved them, and stopped before DLV-160. Planner will not discard or
  resolve that failed merge without explicit user authorization.
- DLV-162 `31bc594` was reviewed and cherry-picked as `06d3d84`; Windows Media
  passes 14/14, Now Playing passes 23/23, and the installed recovery route
  passes. The live PID 30900 smoke admitted every installed first page with all
  published UIA rectangles inside the host. It exposed one new managed Game
  Launcher diagnostic: `flex-wrap` was ignored on the controller-hints slot.
- DLV-164 `9a8822f` was reviewed and cherry-picked as `11a04e9`; Game Launcher
  passes 77/77, the ordinary preset/fallback route passes, and both its retained
  installed diagnostic and PID 41224 smoke admit Game Launcher with zero
  `invalid_style` records. All eight PID 41224 first pages publish contained
  UIA geometry; Audio Mixer settles from Loading to three live sessions.
- DLV-165 `217f515` was reviewed and cherry-picked as `466e189`; Platform
  Broker passes 56/56, Game Launcher passes 77/77, and the exact installed
  Hide/Restore/Back route preserves the shared channel across one late canceled
  response and exposes exact Play authority. PID 26524 admitted all eight first
  pages with contained UIA geometry and no worker/protocol/broker/provider
  failure. Tray UIA `InvokePattern` did switch Network Controls, YT Music, and
  Spotify but returned a COM error after each successful action; DLV-168 owns
  that separate accessibility contract.
- DLV-166 `9caa853` was reviewed and cherry-picked as `ded6821`; no managed
  product gap reproduced. Games & Apps passes 63/63, Windows App Library
  Provider passes 75/75, and one shared private-state backend now proves the
  exact saved row and unrelated Game survive two real AppContainer workers.
- DLV-167 `89005d1` was reviewed and cherry-picked as `8dd29c9`; Settings passes
  60/60 and Widget Catalog 35/35. A version-limit failure retains read-only
  last-good inventory plus Retry without raising quotas or deleting packages.
  PID 41148 admitted all eight first pages with contained UIA geometry and no
  host/worker/protocol/broker/provider error. The tray UIA completion defect
  repeated on seven post-start selections and remains DLV-168.
- DLV-169 `add177e`, DLV-170 `1ba2183`, and DLV-171 `055f72a` were reviewed and
  cherry-picked as `be69735`, `1829140`, and `d58e50e`. Spotify now authors
  explicit adjacent list focus and terminal non-wrapping edges; 50/50 Spotify,
  89/89 SDK, and the installed Community route pass. Games & Apps artwork and
  Game Launcher controls/continuation reproduced no product gap; their focused
  suites pass 64/64 plus provider 75/75 and Launcher 77/77. PID 39248 admits
  all eight first pages inside the host with no error-class host, worker,
  protocol, broker, or provider records. The post-start tray UIA completion
  defect remains separately owned by DLV-168.
- DLV-172 `d9356f6`, DLV-173 `7407f41`, and DLV-174 `251152d` are cleanly
  committed but rejected and remain unintegrated. DLV-172 admits empty source
  collections, can throw on an SDK-valid oversized last-played timestamp, and
  spreads collection state across four mutable fields in the existing Launcher
  hotspot. DLV-173 lets cancellation-ignoring explicit Refresh failures mutate
  a later Active generation. DLV-174 rejects stale successes but still lets a
  stale failed command overwrite replacement-session status. DLV-175/176/177
  own bounded corrections before this prefix can be reconsidered.
- DLV-176 `01fdef1` and DLV-177 `585dade` are accepted but held unintegrated:
  explicit YT Music Refresh now has exact Active-generation ownership, and Now
  Playing stale failures return current replacement state wholesale. DLV-175
  `d13c5e9` and dependent DLV-178 `20ca347` remain rejected: Favorites/Recent
  can still combine while the strip reports only one selection, source options
  disappear as the current filtered/page slice changes, and the search test
  simulates cancel without exercising host TextEntry cancel/focus. DLV-179/180
  own those corrections before the full branch prefix can integrate.
- DLV-179 `84c8780` is cleanly committed but rejected and remains held. It
  makes collection selection mutually exclusive and keeps proven sources stable
  across selection and cursor paging, but the bounded catalog is process-local:
  after restart, a still-current source first encountered on a later page
  disappears until revisited. Its restart fixture collapses to one page and it
  does not explicitly traverse Clear. DLV-183 owns that narrow correction after
  the widgets lane completes the already-next visible DLV-181 milestone.
- DLV-180's original widgets assignment stopped before edits because production
  TextEntry open/cancel/focus restoration is native-host-owned; the reassigned
  platform milestone is now accepted and integrated below.
- DLV-181 `4476567` and DLV-183 `23fa23d` are independently accepted but held
  unintegrated. DLV-181 gives loading/empty/offline/degraded/permission/disabled/
  busy/retry distinct presentation while retaining navigable disabled tiles,
  last-good games, and exact focus. DLV-183 persists bounded non-authorizing
  proven-source labels across a cold worker restart and proves Clear/query truth.
- DLV-182 `498c602` is cleanly committed but rejected and remains held. Newer
  launch admission correctly reserves a generation, but an older accepted
  observation can begin cancellation-ignoring private-state persistence before
  supersession and durably corrupt Recent. DLV-185 owns that exact persistence
  race after the already-active visible DLV-184 milestone.
- DLV-184 `d703d2a` is cleanly committed but rejected and remains held. It
  reduces compact fixed-height demand and preserves status, collection, tile,
  and controller-help semantics, but it makes Search explicitly ExpandedOnly;
  its test asserts Search is absent at 420×340. That contradicts GL-UX-007 and
  the assignment's reachable-Search criterion. DLV-189 owns the narrow compact
  action correction after the already-active DLV-185 milestone.
- DLV-180 `53f2b28` is accepted and integrated as main `0c321a3`. The native
  host projects the existing TextEntry through one stable UIA/controller action,
  distinguishes failed/cancel/close/commit, dispatches no worker action on
  cancel, preserves committed query, and restores exact Search focus. Focused
  main verification passes TextEntryModalTests, AccessibilityTreeTests 17/17,
  and the installed production-host cancel route. Packaged PID 30500 is visible.
- DLV-185 `739b07b` is cleanly committed but rejected and remains held. Its
  generation-safe persistence correction and adversarial blocked-write evidence
  are functionally convincing, but it adds roughly 42 net lines of admission/
  reconciliation policy to the already 1,900-line Launcher root despite the
  explicit no-root-regrowth acceptance rule. DLV-190 must extract that focused
  launch/persistence coordinator after the already-active DLV-187 milestone.
- DLV-189 `6f7fa78` and DLV-186 `3814471` are independently accepted but held
  unintegrated. DLV-189 restores the same Search TextEntry to compact semantic
  focus order without overstating physical geometry. DLV-186 keeps typed owned/
  not-installed games controller-focusable and non-launching; Install copy
  appears only with an existing explicit typed capability and grants no action.
- DLV-190 `e65069c` is accepted. It places launch generation, admitted
  observation persistence, stale-write detection, and Recent restoration behind
  one focused coordinator while shrinking the Launcher root. That closes the
  final rejected-prefix architecture gap. The corrected final state from
  DLV-172 through DLV-190 is integrated as `ae1dee8`; Game Launcher 90/90 and
  installed organization, TextEntry, availability, offline exact-launch, and
  blocked-write routes pass. YT Music 59/59 and Now Playing 27/27 retain their
  exact-generation failure corrections.
- DLV-168 `652e42a` is accepted and integrated as `06dc8d0`. Host-shell/tray UIA
  identity now survives selected-widget runtime replacement while widget nodes
  remain generation-bound. Provider 155, host accessibility 34, overlay state,
  and the real Network Controls tray Invoke route pass.
- DLV-160 `0b50a76` is accepted and integrated as `9564b96`. One bounded
  author-to-production lifecycle proves scaffold, validate, preview, pack,
  inspect, install, exact v1/v2 selection and production-host adoption,
  corrupted-reload last-good retention, safe start, Hero Rail recovery, and
  supported removal without a second renderer, schema, network, signing, or
  executable-content surface.
- DLV-188 `16b1bf1` is accepted and integrated as `dd51da1`. The real packaged
  host eight-widget route preserves exact shell/tray/selected bounds, complete
  commit-before-geometry, premultiplied-clear composition, zero widget-switch
  shell motion, bounded timing, atomic reopen, and no stale hidden commit. This
  is a semantic/timing/log verdict; the user's physical-display verdict remains
  the closing visual gate.
- DLV-193 `edad30d` is accepted and integrated as `70a33e3`. Tap Y still
  toggles reorder; an eligible descriptor-advertised hold crosses at exactly
  700 ms, routes the current admitted Refresh action once through the ordinary
  bridge, and cancels on context, selection, focus, lifecycle, device, or shell
  changes. The packaged host route proves exact-once dispatch; physical timing
  feel remains manual evidence.
- DLV-194 `bd81c8f` corrected by DLV-199 `7cfd4b4` is accepted and integrated
  as `73117f7`/`c4cf0af`. The copied external consumer now restores the exact
  content-versioned SDK from its fixture-local feed into a fresh isolated
  `NUGET_PACKAGES` root for build and pack, and the package contains exactly
  `WidgetSdk.dll` plus `WidgetProtocol.dll` under `lib/net8.0`.
- DLV-195 `9aa28b7` is rejected and held. Its application-scale architecture
  is otherwise sound, but Library NotLoaded/Loading renders only a loading
  indicator while selecting absent `full-app.retry` as initial focus, causing
  `invalid_focus_target` during a blocked load or post-deactivation reset.
  DLV-201 owns the bounded focus correction and validator evidence.
- DLV-196 `51512b2` is rejected and held. Its restore/build/validate/pack/
  install/scenario/remove pipeline is isolated, but fixture setup silently
  copies and rewrites checkout sample files before the documented commands.
  DLV-202 must make the public setup path self-contained and make the fixture
  execute those exact documented commands. DLV-198 `1578523` is independently
  accepted but held until DLV-202, then DLV-203 refreshes its exact artifact
  provenance before integration.
- The corrected DLV-195/196/198/201/202/203 chain is accepted and integrated
  through `ee0c445`. Loading/reset snapshots now use valid null focus, the
  external repository is created through one documented self-contained export,
  and compatibility hashes/provenance reflect the corrected artifacts.
  DLV-204 is accepted and integrated as `04fbdc0`; the same external repository
  proves edit, build, validate, declaration preview, isolated semantic scenario,
  and pack with no catalog or authority expansion.
- DLV-197 is integrated as `6e2c7ff`. DLV-205 `a70b223` later proved the missing
  collection and exact action/SavedId continuity, but is not separately
  integrated because the user corrected the product boundary: Game Launcher
  must be a Community reference. DLV-212–214 supersede widget-facing private
  selection work and remove first-party coupling instead of extending it.
- DLV-200 `0c65e79`/`d9e2005` is rejected and held. The bounded measurement
  harness is reasonable and missing private-working-set/GPU metrics are
  truthfully unavailable, but durable evidence omits root PID/start time and
  child identities, the separate switch run lacks exact provenance, one docs
  statement misdescribes the isolated profile, and retained-complete timing is
  not ordered after the retained paint. DLV-206 owns that correction.
- The user has now reproduced two visible regressions on accepted PID 39636.
  Hold Y no longer restarts the selected add-on: DLV-193 replaced the generic
  F5-equivalent path with a private descriptor opt-in, and the shipped catalog
  opts in only Settings. Spotify also still shows the superseded horizontal-tab
  composition because the accepted DLV-144 vertical-rail source/style changes
  retained immutable package version `0.2.14`; the active installed `0.2.14`
  payload/style predate that correction. DLV-208 and DLV-209 are now the first
  executable visible corrections. Neither regression is closed by semantic
  first-page admission or the prior deterministic hold fixture.

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

### Current assignment — none; awaiting integrated DLV-213

**State:** DLV-212 is accepted and integrated through main `5a6ce0b`. The
widgets lane is intentionally waiting at a clean boundary because DLV-214 must
consume the generic public presentation contract delivered by platform
DLV-213. Do not begin the Community cutover, unrelated widget work, or legacy
Spotify rollback work while that dependency is open.

### Accepted milestone — DLV-212: managed Game Launcher public portability

**State:** Done and accepted through main `5a6ce0b`. The external Community
candidate restores the exact packaged SDK from a fresh cache, builds,
validates, packs, installs disabled as Community, and runs through the generic
AppContainer worker. SDK friend access is removed; sanitized launch observation
is now a bounded public contract with malformed-response validation and no raw
process, executable, provider, or OS identity. The 10,000-item route covers
cursor paging, Search, collection/organization state, navigation, private
state, and exact SavedId-to-current-AppId launch revalidation. The currently
bundled package remains unchanged until DLV-213 and the atomic DLV-214 cutover.

**Baseline/dependencies:** widgets branch `1c31a52`; accepted product
`6e2c7ff`. This is the managed/public-SDK gate for the user's decision that Game
Launcher is a Community reference while Games & Apps remains bundled. Platform
DLV-209 is concurrent and owns its dirty tray-restart files; do not touch native
host placement/input, `widget-catalog.json`, or `OverlayHost/build.ps1`.

**Objective:** make the existing Game Launcher managed application compile,
validate, and run from a clean external consumer using the exact packaged
public SDK rather than repository project/friend access. Remove the
`InternalsVisibleTo("GameLauncherWidget")` exception. Replace each internal API
dependency with an existing public contract or the smallest generic documented
public SDK contract that any Community widget can use. Produce a deterministic
non-first-party Community manifest/package candidate through supported author
tooling without yet removing the currently bundled runtime entry.

**Acceptance:** a copied self-contained repository with a fresh NuGet cache has
no checkout path, `ProjectReference`, first-party ID/publisher, internal/friend
access, or unpublished assembly assumption. It restores the exact SDK package,
builds, validates, packs, and runs the ordinary generic AppContainer scenario
with declared app-library permissions. The scenario exercises loading, a large
cursor-backed library page, Search, collection navigation, details/Back,
private state, and exact opaque launch revalidation. The resulting archive is
classified Community. Keep the current installed first-party package unchanged
until the generic native gate and atomic final cutover; do not duplicate its
state or make the candidate visible in the default tray.

**Architecture/verification:** retain one managed source of Game Launcher
domain behavior; an export/package adapter may assemble an external repository
but may not fork the implementation. Tier 1 Game Launcher and affected SDK/CLI
Release suites; Tier 2 the clean external build/validate/pack/AppContainer
route. No native renderer/projection, host catalog/build packaging, private
Launcher Experience bridge extension, provider redesign, aggregate, capture,
credential, or publication.

**Stop:** a required feature can be supplied only through package identity,
publisher, assembly/type name, known element/style IDs, private WidgetBridge,
raw provider authority, or a native contract. Record the exact missing generic
seam for DLV-213 rather than retaining or hiding the exception.

### Accepted milestones — DLV-169/170/171: visible widget audits

**State:** Done and accepted through main `d58e50e`. DLV-169 adds explicit
adjacent Spotify list focus plus terminal non-wrapping edges and passes 50/50
Spotify, 89/89 SDK, docs 66, and the installed Community route. DLV-170 proves
discovered Game and explicit Application artwork/fallback across refresh and
warm restart with Games & Apps 64/64 and provider 75/75; no product gap was
found. DLV-171 proves 32-game controls, exact help, and full/partial/final
continuation with Launcher 77/77; no product gap was found. PID 39248 admits all
eight current first pages within the host and has no recent error-class records.

### Accepted milestone — DLV-167: installed-widget quota recovery

**State:** Done and accepted through main `8dd29c9`. A typed
`installed_widget_version_limit` fault retains a validated immutable inventory
for read-only review, disables package mutations, and exposes one Retry action.
Cold start without last-good data retains bounded recovery; quotas are not
raised and packages are not deleted. Settings passes 60/60, Widget Catalog
35/35, and documentation 66 files.

### Accepted milestone — DLV-166: persisted Games & Apps audit

**State:** Done and accepted through main `ded6821`. No product defect
reproduced. Games & Apps passes 63/63, provider 75/75, and the installed route
reuses one production-shaped private-state backend across two worker
generations to prove the saved Application, unrelated Game, and exactly one Add
applications action survive restart.

### Accepted milestone — DLV-165: exact Game Launcher launch availability

**State:** Done and accepted through main `466e189`. A bounded set of explicitly
canceled broker correlations now consumes one late cancellation reply without
invalidating the replacement request or shared channel; unknown and duplicate
correlations still fail closed. Platform Broker passes 56/56, Game Launcher
77/77, and the exact installed Hide/Restore/Back launch route passes with no
temporary diagnostics in the commit. PID 26524 admitted the selected game as
Ready on its first-page pass without broker/protocol/provider errors.

### Accepted milestone — DLV-164: valid responsive Game Launcher controller hints

**State:** Done and accepted through main `11a04e9`. The wrapping footer style
now belongs to a non-scrolling managed `Row`, not a `Stack`; native validation
remains strict. Four experiences preserve exact hint/action/focus/SavedId
identity across compact/standard/wide at 100/150%. Game Launcher passes 77/77,
ordinary host preset/fallback passes, and installed/live diagnostics contain no
controller-hint `invalid_style`.

### Accepted milestone — DLV-162: identity-less media-session recovery

**State:** Done and accepted through main `06d3d84`. An identity-less GSMTC
session now receives the fixed sanitized `Media app` label instead of throwing
inside friendly-name derivation and being omitted. Windows Media passes 14/14,
Now Playing 23/23, and the installed initial/empty/transient Retry/stale/
reactivation/teardown route passes. No identity or native control authority is
exposed.

### Accepted milestone — DLV-159: byte-budget-driven title override capacity

**State:** Done and accepted through main `5b93ce0`. The following
DLV-158 Spotify audit completed with 49/49 and no reproducible credential-free
gap, so it produced no code commit.

**Baseline/dependencies:** widgets branch `8b45265`; accepted product `523aed7`.
Preserve DLV-157's exact SavedId, provider-title retention, search projection,
category behavior, CAS and launch revalidation. DLV-152 is concurrent native/
catalog matrix work.

**Owner/outcome:** widgets lane removes the arbitrary 32-title prototype ceiling.
The existing 64-KiB encoded shared-state boundary remains authoritative; a user
can customize a realistically large library until that byte budget is reached.

**Objective/acceptance:** replace `MaximumTitleOverrides = 32` with a generous
validation-work ceiling that does not reject ordinary states before bytes do.
Retain at least 256 compact exact SavedId/title overrides under the byte boundary
in focused evidence. The 257th ordinary override must succeed when encoded state
still fits. Byte-budget rejection preserves the committed state. Malformed/
duplicate/missing-display fields reset only title overrides, preserving
categories/favorites/hidden/recent/variants. Rename/reset/restart/search/category/
stale-display/CAS/exact-launch evidence remains green.

**Architecture/verification:** title policy stays sole validation/projection
owner and widget stays lifecycle/effect/CAS adapter. Tier 1 Game Launcher Release
suite; Tier 2 extend the ordinary installed-worker title route with a large
retained state. No provider, SDK/protocol, native, aggregate, screenshot,
account, artwork, or reviewer-doc edit.

**Stop:** practical capacity requires exceeding the shared 64-KiB state boundary
or a new storage API; report that as a separate architecture milestone.

### Accepted milestone — DLV-157: exact per-game title override

**State:** Done and accepted through main `7c8fc7f`.

**Baseline/dependencies:** widgets branch `66a34ba`; accepted product `6abfb60`.
Reuse the existing action sheet, TextEntry, exact SavedId, category/private-state
CAS and display-projection owners. DLV-150 is concurrent and private native/
Settings work; do not consume it or edit held selection code.

**Owner/outcome:** widgets lane adds one controller-reachable Rename title/
Reset title flow for the exact focused game. The bounded user override persists,
appears consistently in rails/grids/categories/details/search, and never changes
provider title, opaque identity, source attribution, variant grouping, or launch
authority.

**Acceptance:** normalize bounded nonblank input, visibly reject duplicate/no-op/
oversize/byte-budget/CAS failure, preserve the original provider title for
reset and recovery, and reset only title-override fields when malformed. Rename,
replace, clear, Back, restart, stale/missing provider, category membership,
favorite/hidden/recent/variant state, search, and exact current launch
revalidation remain coherent. A provider refresh cannot silently erase the
override; reset immediately restores current provider display truth.

**Architecture/verification:** immutable override validation/projection stays in
a focused owner; `GameLauncherWidget` remains lifecycle/effect/committed-state
adapter. Tier 1 Game Launcher Release suite; Tier 2 ordinary generic-worker
rename/restart/search/reset/exact-launch route. No artwork/file path, provider,
public SDK/protocol, native, aggregate, capture, account, or reviewer-doc edit.

**Stop:** artwork/background/logo selection, new storage API, provider mutation,
title-derived identity, or shared/native work is required.

### Accepted milestone — DLV-156: scalable categories and direct collection switching

**State:** Done and accepted through main `6abfb60`.

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

1. **Awaiting integrated DLV-213 — DLV-214: cut Game Launcher over to the
   ordinary Community product path.** Consume the accepted generic advanced-
   presentation contract at a clean boundary, then make the DLV-212 candidate
   the only shipped Game Launcher. Install/select it through the supported
   Community catalog and remove `org.gbar.firstparty.game-launcher`, the
   built-in tray/catalog entry, `runtime/GameLauncher` build copy, first-party
   conformance classification, SDK friend access, and private widget-facing
   Launcher Experience selection path. Move or export the maintained source as
   a clearly documented Community reference without creating a second domain
   implementation. Games & Apps remains bundled and unchanged.

   Use one explicit pre-release state reset for the retired first-party package
   if identity migration would add compatibility machinery; never touch
   provider data, accounts, credentials, or user files. Prove clean SDK restore,
   build, validate, pack, install, permission consent state, enable/select,
   generic AppContainer execution, current-version update, disable/remove/
   reinstall, and exact library/search/organization/launch behavior. Settings
   must list Game Launcher as Community and Games & Apps as Built-in. Repository
   searches must find no production admission based on Game Launcher ID,
   publisher, assembly/type, element/style IDs, or known tree shape. Tier 1
   affected managed/SDK/catalog/CLI suites; Tier 2 ordinary packaged host with
   the installed Community package; Tier 3 once because this closes the public
   cross-process/package conversion. No credentials, publication, provider
   expansion, capture gate, or legacy rollback work.

DLV-207 is canceled: connecting Game Launcher's picker to a trusted private
bridge would prove the opposite of the Community-widget requirement. Launcher
Experience selection may remain host-owned in Settings, but a Community widget
can consume it only through DLV-213's generic declared presentation contract.
After DLV-214, M2 enrichment still requires credentials/legal API choices and
M3 operations require separately reviewed trusted providers.

## Platform lane

Task: `Implementation agent — platform lane`

Branch: `codex/impl-platform-community` (create from accepted main `56a09fb`;
retain `codex/impl-platform-switch` as historical DLV-209/DLV-205 work)

### Current assignment — DLV-213: generic Community advanced presentation

**State:** Assigned from accepted main `56a09fb`. DLV-209 is accepted and
integrated; DLV-212 is accepted and integrated through `5a6ce0b`. At the clean
platform boundary, create `codex/impl-platform-community` from exact main
`56a09fb` so the superseded private DLV-205 ancestry does not enter this public
contract milestone. Then execute the first Ready item below without waiting.

**Objective/acceptance:** implement DLV-213 exactly as the first Platform Ready
item below. As a bounded adjacent cleanup, rename the internal tray gesture
`Refresh*` state/action identifiers to `Restart*`; DLV-209 changed their meaning
to host-owned worker restart and misleading names must not become durable API
debt. This rename changes no behavior, timing, input mapping, or public
contract, and uses the existing focused gesture evidence.

**Verification/stop:** the DLV-213 Ready item is authoritative. Do not restore
the private Game Launcher path from DLV-205 or recognize known package/widget
identity or tree shape.

### Accepted milestone — DLV-209: generic tray hold restart

**State:** Done and accepted through main `56a09fb`. Hold Y at tray focus now
restarts the exact selected bundled or installed bridge worker through the same
authority as F5, without a widget-authored Refresh action or Settings-specific
opt-in. Tap/release/cancellation behavior remains intact; visible and UIA help
say restart. Focused Release evidence passes bridge catalog checks, 107
controller checks, 111,383 placement checks, and packaged exact-once worker
replacement for both Settings and an installed Community fixture.

### Accepted milestone — DLV-160: author-to-production pack lifecycle

**State:** Done and accepted through main `9564b96`; evidence is summarized in
the current accepted baseline above.

### Accepted milestone — DLV-152: custom-pack production matrix

**State:** Done and accepted through main `3a5b46a`.

**Baseline/dependencies:** platform branch `8ae94d7`; accepted product `523aed7`.
Prove the bottom-rail and left-rail/glass reference recipes plus compact,
standard, wide, 150% text, reduced motion/transparency, high contrast, long
title, missing art, invalid pack, removal denial, and exact recovery through the
ordinary installed selection/native host path.

**Acceptance/verification:** every matrix cell preserves exact focus/action/UIA
identity, valid visible bounds, one renderer/window/compositor, last-good atomic
replacement, accessibility finality, DLV-148 timing/degradation budgets, and
DLV-150 one-activation safe start. Tier 1 focused catalog/Settings/native suites;
Tier 2 one deterministic ordinary-host matrix. No aggregate or capture gate.

### Accepted milestone — DLV-150: installed Launcher Experience selection, last-good reload, and safe start

**State:** Done and accepted with DLV-149 through main `523aed7`. At the clean platform
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

1. **Assigned from main `56a09fb` — DLV-213: replace known
   Game Launcher projection with a generic Community presentation contract.**
   Define the smallest versioned public manifest/SDK/protocol declaration by
   which any installed Community widget can opt into a closed host-owned
   advanced presentation made from validated semantic slots. The host admits
   it from the package's validated declaration and current snapshot, not Game
   Launcher ID, publisher, assembly/type, element IDs, style classes, or a
   memorized tree shape. Launcher Experience packs remain data-only and
   Settings/host-owned; they grant no data, launch, provider, file, script, or
   action authority. Existing snapshot/message/tree/string/update/native/GPU
   bounds, accessibility finality, last-good fallback, and the one shared
   renderer/window/compositor remain authoritative.

   Remove or generalize the private widget-facing selection seam introduced by
   DLV-197; do not expose global Settings mutation as ambient Community
   authority. If a widget needs to request a host settings route, use one
   generic allowlisted host-navigation intent rather than a Launcher ID special
   case. Prove admission and identical focus/action/UIA/Back semantics with the
   DLV-212 candidate and a second randomly named Community fixture whose
   publisher, assembly/type, element IDs, and style classes differ. Prove
   missing declaration, malformed slots, stale generation, oversized input,
   incompatible version, revoked consent where applicable, and package
   replacement fail closed while the ordinary declarative surface stays usable.

   This is the serialized cross-process contract owner. Tier 1 manifest/SDK/
   protocol/bridge/projection/renderer/accessibility suites; Tier 2 two
   installed Community packages through the ordinary host; Tier 3 once for the
   public protocol boundary. Document a copyable author contract and migration.
   No Game Launcher domain code, first-party catalog removal, raw paths/URLs/
   scripts, second renderer/window, provider expansion, capture, or credentials.
   Stop if the design works only by recognizing the known widget or by granting
   Community code host-global mutation authority.
2. **Ready after integrated DLV-214 — DLV-210: repair the generic Hero Rail
   no-artwork layout.**
   The user's accepted PID 39636 showed the original Hero Rail compact branch as
   a disconnected source/search header, a large empty hero region, compressed
   text-only cards whose widths follow title length, clipped controls, and two
   competing controller-help layers. The exact 19:07 live interval admits a
   978x466 Game Launcher viewport and reports terminal unavailability for all
   six visible trusted artwork handles; this is a real product frame, not a
   malformed capture. Own the generic Launcher Experience composition/fallback
   path and make Hero Rail remain professional and completely usable when every
   visible item lacks cover artwork. Keep title, source
   status, Search/collections, one bounded equal-width game rail, operation
   status, and one readable non-overlapping controller-guidance hierarchy
   inside the admitted body. Reclaim or purposefully compose the empty hero
   region; do not silently make title length control card geometry, fabricate
   artwork, hard-code these games, reduce the catalog, or change SavedId/action/
   collection/focus authority. Retain the selected Hero Rail identity unless a
   single host-owned responsive fallback within that preset is necessary.
   Prove the exact current 978x466 compact work area plus standard/wide and
   available/mixed/all-terminal artwork states through semantic geometry,
   focus/action/UIA agreement, and the ordinary packaged host. All published
   rectangles and guidance must be contained; 32-game paging and exact focus
   must remain intact. Inspect the post-route log. No screenshot gate, provider
   enrichment, external metadata credentials, public schema/protocol, managed
   Launcher root, tray/work-area owner, or unrelated animation refactor.
3. **Ready after DLV-210 — DLV-206: retain truthful performance provenance.**
   Correct rejected DLV-200 without expanding measurement scope. Retain one
   bounded sanitized committed artifact or summary containing root PID/start
   time, exact commit/executable SHA, scenario/process-profile IDs, child
   identities/roles, available metrics and explicit unavailable metrics. Give
   the separate eight-widget timing run its own exact commit/executable/run/log
   provenance. Anchor the complete composition search strictly after the
   retained paint record, and correct the false ordinary-host-live wording.
   Rerun only the affected bounded measurement/temporal routes. No aggregate,
   speculative optimization, budget change, capture, or managed widget work.

DLV-209 is accepted through `56a09fb`. DLV-213 is now the active required
public-framework correction and must precede the final DLV-214 Community
cutover; DLV-210 then fixes the same visible no-artwork fallback through the
generic path. Evidence-only DLV-206 remains behind those product outcomes. Do
not extend the superseded private Game Launcher bridge or manufacture unrelated
refactors.

## Serialized integration queue

1. DLV-212 is accepted and integrated through `5a6ce0b`; it closes managed
   public-SDK portability without changing the live built-in package.
2. DLV-213 owns the generic advanced-presentation manifest/SDK/protocol/native
   boundary. Integrate it before widgets DLV-214 performs the atomic Community
   package cutover. No lane may implement a parallel private bridge.
3. DLV-134/DLV-145/DLV-146 are accepted and integrated through `4aa7284`.
4. DLV-147/DLV-153/DLV-151 are accepted through `b92e0ff`; DLV-148 and DLV-154
   are accepted through `d78fc98`.
5. Hold DLV-149 `f282237` until DLV-150 consumes its selection/recovery state;
   integrate
   the pair as one visible outcome.
6. External metadata/artwork, Amazon adapters, trusted content operations,
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
| Game Launcher Switch experience managed action | DLV-191 correctly exposed that the built-in widget needed a private path to the host-owned Settings selection owner. That design is now rejected for a Community reference. | DLV-213 must provide only a generic declared presentation/host-navigation contract; DLV-214 removes the private widget-facing path. |
| Manual artwork/background override | DLV-192 stopped cleanly: title/category/hidden/preferred state exists, but there is no user-authorized trusted opaque artwork/background selection seam. | A separately reviewed trusted selection/binding design; do not invent raw paths or a misleading partial override. |

## Verification queue

1. The prior PID 39636/40224 evidence reopened Spotify packaging/layout,
   generic Hold Y restart, and the Game Launcher Hero Rail all-artwork-
   unavailable fallback. Retest the next accepted Release after DLV-209 and
   the DLV-212–214 Community cutover, then DLV-210, alongside
   switching borders/flicker and Games & Apps layout.
2. Physical Audio Mixer LB/RB/X tray actions and reverse traversal. Planner's
   current four-session keyboard path reaches every row and returns to Master.
3. Game Launcher shortcuts/top controls/last-row continuation and exact launch.
4. Spotify seek/list traversal, pagination/reverse focus, transient failure,
   OAuth/Web Playback/device behavior when an authorized account exists.
5. YT Music real companion pairing/reconnection and physical controller.
6. Physical Y-hold generic add-on restart is a reproduced regression owned by
   DLV-209; Narrator/MSAA, mixed-DPI/display, Bluetooth/audio hardware, and game
   foreground input remain manual.
7. Packaged widget-switch transparency/temporal continuity and long-run resource
   baselines at a named release checkpoint.

## Recent accepted milestones

| Assignment | Implementation | Integrated main | Result |
| --- | --- | --- | --- |
| DLV-208 | `1c31a52` | `8e38df5` | Spotify 0.2.15 packages the accepted vertical rail; installed DLL/GBSS provenance and generic AppContainer snapshot pass. Historical 0.2.14 rollback identity is explicitly not a release gate. PID 40224 reaches the expected new-identity consent gate. |
| DLV-195–204 corrected authoring cluster | final widgets `93e3ae7` | `04fbdc0` | Full-application loading/reset focus validates, one documented self-contained external repository completes offline onboarding and refreshed compatibility reporting, and its shortest edit/build/validate/preview/scenario/pack loop proves the changed semantic result. |
| DLV-194/199 corrected pair | `bd81c8f`/`7cfd4b4` | `73117f7`/`c4cf0af` | A copied external repository restores the exact content-versioned SDK from its fixture-local feed into a fresh `NUGET_PACKAGES` root for build and pack; the nupkg contains exactly the two public runtime DLLs. |
| DLV-193 | `edad30d` | `70a33e3` | Tap-Y reorder is retained and eligible descriptor-advertised 700 ms hold Refresh routes exactly once through the ordinary host bridge with truthful guide/accessibility text and cancellation on ownership changes. |
| DLV-172–190 corrected cluster | final widgets `e65069c` | `ae1dee8` | Collection/source truth, lifecycle and command staleness, TextEntry/compact Search, availability, offline exact launch, and generation-safe Recent are corrected. Launcher 90/90, YT Music 59/59, Now Playing 27/27, installed routes, coherent package rebuild, and eight-widget UIA/log smoke pass. |
| DLV-168 | `652e42a` | `06dc8d0` | Host tray/shell providers survive widget runtime replacement while widget nodes remain generation-bound; focused 155/34/state and real UIA Network Controls Invoke pass. |
| DLV-180 | `53f2b28` | `0c321a3` | Existing TextEntry has one native UIA/controller action; cancel dispatches no commit, preserves query, and restores exact focus. TextEntry modal, accessibility 17/17, and installed host route pass; PID 30500 is visible. |
| DLV-171 | `055f72a` | `d58e50e` | No managed Game Launcher gap reproduced; 77/77 covers 32-game controls, exact help, and full/partial/final continuation, and PID 39248 admission passes. |
| DLV-170 | `1ba2183` | `1829140` | No Games & Apps artwork gap reproduced; 64/64 plus provider 75/75 cover opaque art, refresh/warm restart, and semantic fallback. |
| DLV-169 | `add177e` | `be69735` | Spotify authors adjacent focus and terminal non-wrap edges across Queue, Playlists, and detail; 50/50, SDK 89/89, docs 66, and installed recovery pass. |
| DLV-167 | `89005d1` | `8dd29c9` | Settings retains read-only last-good installed inventory and truthful Retry across typed version-limit faults; 60/60, Catalog 35/35, docs 66, and PID 41148 pass. |

Do not create another snapshot while this file has 1,000 or fewer physical
lines. On crossing 1,000, snapshot and compact according to
[`review-planner-goal.md`](review-planner-goal.md).
