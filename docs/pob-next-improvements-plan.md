# PoB widget improvements: discussion and implementation plan

Last updated: 2026-10-18

**Status: scope recorded; discussion is ongoing.** Creating this document does not start implementation. Keep it updated as decisions are made and work is verified. Do not treat a checkbox or an implementation note as user acceptance.

This plan covers the next round of PoE1 and PoE2 widget improvements discussed after reviewing PoB Redux. It supplements the [existing trade plan](pob-trade-completion-plan.md); it does not restart completed work or reopen deferred CI repairs.

## Implementation order and tracking

| Order | Work item | Status |
| --- | --- | --- |
| 1 | Clean up shared code and game-specific boundaries | Agreed; not started |
| 2 | Adjust widget dimensions and stabilize workspace sizing | Agreed; not started |
| 3 | Bring PoE1 to a tested beta candidate and audit new features | Planned; not started |
| 4 | Complete passive-tree artwork and measure rendering improvements | Planned; not started |
| 5 | Add whole-build and loadout comparison | Planned; not started |
| 6 | Add unified search and direct navigation | Planned; not started |

Cleanup comes before UI additions so new features use the improved boundaries. Preserve existing behavior during cleanup; keep feature changes separately reviewable.

## 1. Shared architecture cleanup

The current apps have separate entry points, packages, engine pins, processes and build storage, but share most C# UI, Lua adapters and web rendering. Sharing common workflows is intentional. Do not duplicate the entire UI or create a framework rewrite.

- [ ] Extract cohesive components from the large partial `PobWidget` class for workflows being changed. Give components clear state ownership; moving methods into more partial-class files alone is not sufficient.
- [ ] Centralize fixed game differences such as identity, trade endpoints and game-file export support.
- [ ] Represent variable feature availability through explicit engine capabilities. Do not replace every legitimate feature condition with a game-number branch.
- [ ] Introduce validated, typed adapter-response models for the areas being changed. Distinguish absent optional capabilities from malformed required data; do not silently turn invalid data into plausible defaults.
- [ ] Isolate engine-version/API compatibility in focused Lua helpers, separate from UI and gameplay logic.
- [ ] Keep unavoidable dependencies on PoB internals inside the adapter and exercise them with real-engine compatibility tests.
- [ ] Remove superseded/dead code and check both engines before adding new behavior.

Capability handling should continue to use PoB's available controls, eligibility, choices and enabled state. The widget must not start maintaining its own passive-effect or calculation rules.

## 2. Dimensions and workspace layout

Current preferred dimensions are approximately 1180 × 820 for planning sections and 1320 × 900 for Tree. Management screens request smaller sizes. These are logical dimensions; the host constrains them to the display and scaling settings.

- [ ] Use the host's existing available-height policy for planning screens.
- [ ] Use a consistent, wider workspace across planning sections so switching to Tree does not repeatedly resize it.
- [ ] Bound width sensibly on ultrawide displays; do not stretch controls across the entire screen without a usability benefit.
- [ ] Keep website and other modals within widget bounds while making them large enough for navigation.
- [ ] Verify smaller displays, display/interface/text scaling, long lists, two-column layouts, controller reachability and focus after resize.

The host already has independent `Preferred`, `Content` and `FillAvailable` axis policies. Reuse those before proposing host API changes. Exact width policy and numeric limits remain to be decided during layout work. Small management views need not become full-size merely because planning views do.

## 3. PoE1 beta migration

First milestone: existing PoE1 functionality working against a pinned beta, with evidence and a list of newly available or missing features. Complete desktop parity is not implied by changing the pin.

- [ ] Resolve an exact beta revision through the existing candidate tooling; keep PoE2's pin unchanged unless separately required and reviewed.
- [ ] Address constructor, module-loading and headless API compatibility.
- [ ] Verify imports, repeated loading, calculations, skills, items, tree editing, trade ranking, saving, Undo/Redo and recovery.
- [ ] Include empty and partially configured builds, not only finished guide builds.
- [ ] Audit beta additions: distinguish inherited calculation/data fixes from controls or workflows requiring widget work. Record concrete follow-up scope rather than silently expanding it.
- [ ] Measure startup, memory and calculation performance; upstream benchmark claims are not widget performance guarantees.
- [ ] Run PoE2 regressions for shared changes and label PoE1 as beta when a beta-backed candidate is presented to users.

At discussion time, PoE1 uses stable v2.67.2 and PoE2 uses a pinned beta. Recheck [engine-lock.json](../samples/PobCompanion/engine-lock.json) before implementation.

The existing weekly/manual engine-candidate pipeline is the update mechanism. Passing tests permit candidate packaging; failures retain diagnostics. This plan does not authorize automatic publication, merging or release, and does not reopen deferred CI repair work.

## 4. Tree artwork and rendering

- [ ] Add missing upstream tree backgrounds, class/ascendancy artwork, node frames, specialized socket art and jewel-radius overlays, using upstream metadata and state.
- [ ] Preserve visible selection, readable details, controller hit targets and theme/high-contrast accessibility.
- [ ] Keep assets scoped to the relevant tree/session and bound decoded-image and rendering-cache memory, not just compressed package size.
- [ ] Measure rendering with representative trees, zoom levels and display scaling before and after the artwork additions.
- [ ] Use appropriate-resolution assets and asynchronous image decoding where beneficial. Add spatial indexing or cached/tiled rendering only when measurements justify the added complexity.
- [ ] Preserve coalesced rendering and avoid continuous idle redraws.

Current atlas budgets are implementation choices to review with evidence, not proof that richer artwork is impossible. Do not copy Redux's renderer wholesale or promise its performance without measurement.

## 5. Whole-build and loadout comparison

- [ ] Compare the current build/loadout with another build/loadout across statistics, passives, equipment, skills and configuration.
- [ ] Make differences understandable and provide navigation to relevant details.
- [ ] Identify the compared loadouts and calculation context so differences in configuration or main skill are not mistaken for equipment gains.
- [ ] Keep comparison read-only unless a separate explicit edit workflow is designed and agreed. Existing item/tree Apply and Discard behavior remains distinct.

Detailed layout and whether any selective transfer of changes should exist are still open discussion topics; transfer is not included merely by adding comparison.

## 6. Unified search and direct navigation

- [ ] Search passives, skills/gems, equipped and inventory items, configuration options, calculation rows, saved builds and useful actions from one entry point.
- [ ] Selecting a content result opens its appropriate section/detail, brings it into view and establishes controller focus.
- [ ] Passive navigation centers the node; skill navigation identifies its group; configuration navigation reaches the actual control.
- [ ] Reuse existing screens and shared controller/text-entry functionality, including whole-entry suggestions where appropriate.
- [ ] Search and content navigation do not equip items, allocate points or change build settings. Action results must use the existing action/confirmation flows.
- [ ] Handle stale results, build switches, cancellation, unloading and missing targets without misdirected actions or focus jumps.

The exact invocation shortcut, result grouping and action set remain to be designed. Do not promise every desktop command as a search action.

## Resource and trade decisions

- **One calculation engine per active widget session. No parallel worker-engine pool.**
- Reuse calculator setup, committed baselines and valid caches; batch requests where possible. Batching reduces overhead but does not make sequential calculations parallel.
- Long operations need progress and cancellation. Invalidate cached scores when their relevant build/target/settings change.
- Retain the official trade-website approach: browser-profile sign-in, normal page loading, comparison actions and evaluation of received listings. Loading additional listings still causes normal website requests.
- Preserve widget-specific persistent web profiles, retained-session return flow and existing host idle unloading. Do not reintroduce temporary-page machinery.
- Website purchase/contact controls remain the purchasing path. No copying session cookies into widget backend state.

## Deferred or excluded

| Item | Decision |
| --- | --- |
| Parallel calculation engines | Excluded: resource cost is not appropriate for this scope. |
| Redux-maintained patches to upstream PoB | Excluded at the user's request. |
| GGG OAuth integration | Outside this scope; current website workflow is accepted. |
| AI integration | Outside this scope. |
| Broad passive scoring | Deferred pending usefulness and single-engine performance measurements. |
| Automatic multi-point passive planning | Deferred; additional selection rules and maintenance are not justified yet. |
| Automatic merge/release or renewed CI repair | Not authorized by this plan. |

## Earlier items and completed work

- Floating selected-node name badge removed; selection outline and hover details retained.
- PoB documentation split into [overview](../samples/PobCompanion/README.md), [user guide](../samples/PobCompanion/USER_GUIDE.md) and [developer documentation](../samples/PobCompanion/DEVELOPMENT.md).
- Grouping duplicate passive names into expandable rows was discussed earlier. It is not yet part of the implementation scope. If revisited, preserve distinct node identities and investigate a reusable flattened virtualized-list pattern rather than assuming nested expandable lists exist.

## Verification and delivery records

For each implemented item, record the commit, relevant tests, performance evidence where applicable, visual inspection and remaining limitations below. Distinguish automated checks from physical acceptance. Check both games when changing shared code. No implementation or acceptance evidence has been recorded for this round yet.

Cumulative candidates may be launched hidden with the normal profile, as previously authorized; keep one candidate instance. A documentation-only update does not require rebuilding or relaunching the application.

| Item | Commit / evidence | Remaining work | Acceptance |
| --- | --- | --- | --- |
| Plan creation | Discussion captured on 2026-10-18 | Continue discussing scope | Not implementation acceptance |

## PoB Redux attribution and reference implementation

Credit PoB Redux for the ideas, techniques and implementation work that actually contribute to these improvements. Attribution is part of delivery, not an optional follow-up.

- [ ] Record the specific Redux contributions adopted as each feature is implemented, with source links and revisions where relevant.
- [ ] Add a concise acknowledgement linking to PoB Redux in the widget README credits, with fuller attribution in developer documentation and relevant marketplace documentation.
- [ ] Distinguish inspiration from adapted code or reused assets; do not imply contributions were incorporated when they were only evaluated.
- [ ] For reused code or assets, verify the applicable license and retain required copyright/license notices in source and distributed packages. Keep existing PoB Community and Grinding Gear Games credits; Redux attribution supplements them.

The comparison reviewed [PoB Redux at a5afd8b](https://github.com/pobredux/pob-redux/tree/a5afd8b80de993623c1fc5c1541269312361e50f). Its code is a reference for ideas, not proof of measured performance in WidgetRail or an instruction to reproduce every feature. Recheck current upstream code when implementing.
