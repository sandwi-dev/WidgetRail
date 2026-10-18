# PoB widget improvements: discussion and implementation plan

Last updated: 2026-10-18

**Status: all implementation scope listed below is agreed; implementation has not started.** All rows in the implementation tracking table have the same scope approval. Deferred, excluded and explicitly out-of-scope discussion items remain outside that approval. Scope approval is distinct from acceptance of the finished implementation. Keep this document updated as work is verified; this status clarification does not itself start implementation.

This plan covers the next round of PoE1 and PoE2 widget improvements discussed after reviewing PoB Redux. It supplements the [existing trade plan](pob-trade-completion-plan.md); it does not restart completed work or reopen deferred CI repairs.

## Implementation order and tracking

| Order | Work item | Status |
| --- | --- | --- |
| 1 | Clean up shared code and game-specific boundaries | Agreed; not started |
| 2 | Adjust widget dimensions and stabilize workspace sizing | Agreed; not started |
| 2a | Compact layouts and inspect the app for couch readability | Agreed; not started |
| 2b | Improve PoB editing and inspection workflows using Redux as a reference | Agreed; not started |
| 3 | Bring PoE1 to a tested beta candidate and audit new features | Agreed; not started |
| 4 | Complete passive-tree artwork and measure rendering improvements | Agreed; not started |
| 5 | Add whole-build and loadout comparison | Agreed; not started |
| 6 | Add unified search and direct navigation | Agreed; not started |
| Independent host change | Rename Marketplace to WidgetRail Catalogue | Agreed; not started |
| Independent host change | Add subtle scroll-container edge cues | Agreed; not started |
| Shared host capability, before dependent widget flows | Audit and extend reusable rich previews / anchored popovers | Agreed; not started |

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

## 2a. Compact layouts and app-wide visual verification

Follow the architecture cleanup and dimension adjustment. Increase useful information per page and reduce navigation without making the interface difficult to read from a couch. Apply the same principles to subsequent new screens, then repeat the affected visual checks.

- [ ] Improve shared host layouts and PoB screens by reducing excess padding/gaps, redundant headings/descriptions and unnecessary navigation. Keep meaningful explanations and distinctions; do not remove information solely to fit more rows.
- [ ] Use the larger workspace, appropriate two-column layouts and compact rows for related values. Keep reading-line lengths reasonable and group related controls clearly.
- [ ] Preserve readable text, unmistakable focus indicators, comfortable pointer/controller targets and existing text/interface scaling. Do not apply indiscriminate font or target shrinking.
- [ ] Start with one balanced default; do not add a separate density preference or duplicate desktop/couch layouts in this pass.
- [ ] Walk through the actual app, not only isolated screenshots: shell/rail, Settings, Catalogue, shared dialogs, on-screen keyboard/suggestions, pinned surfaces and available widgets. Exercise each PoB section in both games, including subpages, selectors, expanded details, lists, Notes, trade comparisons and ranked results.
- [ ] Record a coverage checklist with tested surfaces, display/scaling/theme conditions, findings and evidence. Include dense, empty, loading, disabled, error and long-label states, plus light/dark/high-contrast appearance and representative TV scaling. Mark unavailable surfaces or states as unverified rather than assuming they are good.
- [ ] Check scrolling, clipping, alignment, focus visibility, controller reachability, mouse interaction, modal bounds and hide/reopen behavior after shared style changes. Fix problems found and recheck affected workflows before reporting completion.
- [ ] Native rendering and interaction checks are required. Final readability at the user's actual couch distance remains a physical user acceptance check; screenshots alone cannot establish that.

## 2b. PoB workflow improvements inspired by Redux

Improve frequent build decisions without copying Redux's desktop layout. These changes follow architecture cleanup and workspace sizing. Reuse existing controls, list/detail layouts, navigation and calculation capabilities; do not turn every presentation pattern into a new public framework element.

- [ ] Extend gem search beyond name matching to upstream-provided tags such as projectile. Show upstream compatibility and clearly labelled calculation differences where available. Preserve existing ranking; do not describe it as a missing feature or add a worker-engine pool.
- [ ] Bring commonly used skill choices, such as skill part and stat set, beside the selected group when applicable. Keep routine gem add/replace/enable and group editing within the skill workspace where practical, retaining the group list and selection. Put uncommon controls in contextual details rather than permanently crowding every row.
- [ ] Let users inspect gem, inventory and unique-library candidates while retaining the candidate list, target group/slot, search and scroll position. Use the shared rich-preview capability where it improves the flow. Browsing or focus changes must not edit the build; explicit Add, Replace or Equip performs the change.
- [ ] Make explicitly changed configuration settings identifiable, with a Changed settings filter and section counts. Obtain default values and applicability from PoB. Preserve the existing filtering of irrelevant defaults and visibility of authored settings; do not introduce maintained gameplay rules or new boss presets.
- [ ] Use collapsible sections with useful summaries where appropriate: key totals remain visible when collapsed, with detailed rows available on expansion. Apply selectively to calculations and secondary settings. Reuse existing framework facilities first; preserve stable ordering and predictable controller navigation rather than adopting a shifting masonry layout.
- [ ] Make existing build warnings and unmodelled item modifiers actionable. Provide compact counts and navigation to the affected item, skill or setting when its identity is known. Preserve exact affected lines and distinguish calculation limitations from invalid settings. Do not guess a target from ambiguous warning text or adopt Redux's custom build-health heuristics.
- [ ] Keep calculation context clear beside editing and inspection: relevant skill/component, loadout and weapon set where applicable. Preserve the distinction between main-skill damage, Full DPS and a separately inspected calculation context. Coordinate warning destinations with the planned unified navigation rather than building a second navigation mechanism.

Redux references include its Overview, Skills, Items, Config and Calcs views at the revision recorded below. The rich-preview proposal came from the user; Redux supplies examples of content and workflows, not the definition of the shared component. Attribute actual adopted ideas/code accurately.

### Native visual and workflow acceptance

- [ ] Inspect the actual cumulative candidate for every changed screen, preview and subview after implementation. Automated tests and screenshots alone are not completion evidence. Exercise opening, browsing, editing, scrolling, expanding/collapsing, dismissing and returning with both controller and mouse/keyboard.
- [ ] Check coherent spacing, alignment, typography, hierarchy, panel proportions, readable labels, deliberate empty states and clear selected/disabled/loading/error states. Fix awkward gaps, clipped content, unstable layouts, obscured controls and inconsistent styling before delivery; do not merely document these as polish follow-ups.
- [ ] Verify dense and partially configured builds in both games, long names/descriptions, small and large displays, interface/text scaling and light/dark/high-contrast themes. Ensure focus remains visible and predictable across panels, updates, dismissal, hide/reopen and widget unloading.
- [ ] Record coverage, findings, corrections and final native visual evidence under the existing app-wide verification checklist. Use local/sanitized fixtures for repeated checks; preserve the minimal-live-GGG constraint. Final couch-distance readability remains physical user acceptance.

## Shared rich previews and anchored popovers

Provide reusable widget-authored content anchored to an existing element. Content is composed from the normal widget UI elements; it is not restricted to item descriptions, artwork or statistics and does not require another layout language.

- [ ] Audit existing native flyout/tooltip facilities, widget declarations, modal/navigation support and controller input handling before designing an API. Tooltips, widget context menus and controller menu navigation already exist; do not list them as missing features or build competing replacements. Extend existing facilities only where the audit establishes a gap.
- [ ] Support a passive preview and explicit interactive inspection using a common foundation. Passive previews never take focus or change the build. Interactive inspection accepts supported controls/scrolling, owns an appropriate focus scope and restores focus to the opener on dismissal, or a valid fallback if it has disappeared.
- [ ] Allow authors to supply content and bounded options for supported activation modes: mouse hover, settled controller focus or an explicit action. The host owns consistent timing defaults, placement, bounds, dismissal, input admission and focus behaviour. Final API/options and shortcut assignment follow the audit; do not assume an unused controller button.
- [ ] Anchor placement must respect widget bounds and scaling, avoid obscuring the anchor or important controls where possible, and constrain long content. Do not reproduce desktop hover windows that cover much of the usable couch interface.
- [ ] Reuse loaded data and valid caches. Delay expensive inspection work until selection settles; cancel or disregard superseded requests. Bind updates to the current anchor, request and widget lifetime. Late results must not replace a newer preview, reopen a dismissed popover or steal focus.
- [ ] Bound content/resource use through the existing protocol and host safeguards. Handle rapid navigation, repeated activation, removed/recycled anchors, build switches, errors, nested-surface conflicts and unload safely. No continuous polling or calculation engine per preview.
- [ ] Verify pointer travel between anchor and interactive content, controller entry/Back/scrolling, keyboard access, accessible names and reduced motion. Keep essential information available through an explicit inspect/help path rather than relying solely on a timed hover.
- [ ] Exercise the shared component with PoB and a representative non-PoB example or validation fixture. Perform native visual and workflow acceptance using the checklist above before declaring it ready for widget authors.

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

## Host-wide naming: WidgetRail Catalogue

Rename the user-facing Marketplace to **WidgetRail Catalogue**, using the user's preferred spelling. It is a place to discover, install and update widgets, not a shop.

- [ ] Update the widget display name, main heading, Settings entry points, controller guide labels and user-facing status/error messages.
- [ ] Update user-facing documentation and installation/update instructions consistently, including the PoB README.
- [ ] Preserve existing widget IDs, intent/action identifiers, package identities and protocol/API contracts so the display rename does not break installed configurations or integrations.
- [ ] Check label fit and navigation in the catalogue and Settings. Update existing name assertions where applicable.
- [ ] Fix the catalogue tests alongside the rename. Replace the stale exact listing-count assertion with meaningful inventory/identity/screenshot contract checks, rather than merely changing 10 to 11. Run the affected service and presentation suites and report genuine remaining failures separately.
- [ ] Correct outdated user documentation about version history, retention and updating again after a rollback.
- [ ] Compare actual browse, details, install, installed-management and update flows with familiar patterns in popular app stores (for example Microsoft Store, Steam and winget GUI clients). Use those patterns as references, while preserving controller access and clear permission/full-access review.
- [ ] Use the real widget with controller and mouse/keyboard to exercise browsing, screenshots, Back/focus restoration, install review, cancellation, individual updates, Update all, partial failure/retry, Settings handoff, rollback/re-update and hide/reopen behavior. Use isolated packages/profile where destructive test cases would affect the normal installation. Do not confuse model/snapshot tests with native end-to-end acceptance.
- [ ] Implement improvements revealed by that comparison and walkthrough: simplify unnecessary steps, clarify labels and primary actions, preserve navigation/focus, and make download/review/install/completion/error states easy to understand. Retest the revised flows; this is an improvement task, not only an audit/report.
- [ ] Review GitHub's current documented limits and API guidance when implementing. Add suitable release-metadata freshness caching and conditional requests where supported, coalesce duplicate checks, and avoid a fresh network request on every reopening. A manual refresh must still respect server-directed cooldowns.
- [ ] Handle primary/secondary rate limits and transient failures using relevant response headers (including Retry-After and rate-limit reset information), bounded backoff and clear retry feedback. Do not use aggressive polling, concurrent retries or credentials as a way around limits. Keep cached content usable and identify stale availability when refreshing fails.
- [ ] Address release discovery beyond the first 20 releases with a bounded, cached strategy so older widget assets do not disappear as newer app releases accumulate. Measure request counts for first open, repeated open, explicit refresh and update batches.
- [ ] Test caching, unchanged responses, expiry, rate-limit responses and retry behavior with deterministic fake HTTP/time fixtures. Do not intentionally exhaust GitHub's live limits to test them. Record native workflow and request-count evidence before declaring completion.

This is independent of the PoB architecture work. Technical identifiers and historical records need not be mechanically renamed.

## Shared host scroll-container edge cues

Show whether content remains above or below a vertically scrolling container. This is a reusable host presentation feature, not PoB-specific drawing.

- [ ] Use short, centered, fixed-length themed lines at the inside top/bottom edges: approximately 32–48 logical pixels wide and 1–2 pixels thick, finalized through native visual inspection.
- [ ] Show the top cue only when content is hidden above, and the bottom cue only when content is hidden below. Show both between the ends and neither when content fits.
- [ ] Keep length independent of scroll progress. The proposed growing/shrinking lines were rejected because they would duplicate a scrollbar. Preserve existing scrollbars for position and extent information.
- [ ] Place cues in stable edge padding or a narrow reserved strip, outside the text/control viewport. Never obscure useful content, intercept input, participate in focus navigation or change layout as cues appear/disappear.
- [ ] Use a muted theme role distinct from the focus outline; ensure light/dark/high-contrast readability. No pulsing or bouncing. A brief visibility fade is acceptable, respecting reduced motion.
- [ ] Determine visibility from actual scroll extent/offset with a small boundary tolerance to avoid fractional-offset flicker. Update on scrolling, resizing and content changes without continuous polling or idle rendering.
- [ ] Handle independently scrolling nested containers without drawing duplicate cues for wrappers that do not scroll. Keep implementation scope to host-owned scroll surfaces; do not inject indicators into remote websites.
- [ ] Verify Notes-style text, virtualized lists, calculation breakdowns and modals, including no overflow, both boundaries, dynamic content, scaling and controller/mouse navigation. Confirm indicators do not conflict with the existing hidden-interaction focus outline or focus-after-settle behavior.

The conversation mockup is a design reference only; native visual acceptance is still required. No additional visualization is requested.

## Resource and trade decisions

- **One calculation engine per active widget session. No parallel worker-engine pool.**
- Reuse calculator setup, committed baselines and valid caches; batch requests where possible. Batching reduces overhead but does not make sequential calculations parallel.
- Long operations need progress and cancellation. Invalidate cached scores when their relevant build/target/settings change.
- Retain the official trade-website approach: browser-profile sign-in, normal page loading, comparison actions and evaluation of received listings. Loading additional listings still causes normal website requests.
- Preserve widget-specific persistent web profiles, retained-session return flow and existing host idle unloading. Do not reintroduce temporary-page machinery.
- Website purchase/contact controls remain the purchasing path. No copying session cookies into widget backend state.
- **Minimize live GGG website/API use during development and acceptance.** Use local fixtures, synthetic pages and sanitized previously captured responses for repeated layout, navigation, extraction and failure tests. Reuse existing sessions/results for the small number of necessary live checks. Do not repeatedly refresh/search/load batches, deliberately trigger throttling or run automated stress tests against GGG. Respect Retry-After and relevant limit headers when available; stop live retries on throttling/challenge responses and continue independent local checks. Record what was locally simulated versus live-verified; do not claim this guarantees freedom from account flags.

## Deferred or excluded

| Item | Decision |
| --- | --- |
| Parallel calculation engines | Excluded: resource cost is not appropriate for this scope. |
| Redux-maintained patches to upstream PoB | Excluded at the user's request. |
| GGG OAuth integration | Outside this scope; current website workflow is accepted. |
| AI integration | Outside this scope. |
| Reading/synchronizing a build from the running game | Scrapped at the user's request: no suitable approach identified. Do not pursue screen/OCR reconstruction, memory reading or a new live clipboard-sync workflow. Existing manual item paste/import remains available. |
| Game-relative annotation overlays | Not planned; no concrete current workflow justifies the feature. Keep researched overlay projects as references only. |
| Broad passive scoring | Deferred pending usefulness and single-engine performance measurements. |
| Automatic multi-point passive planning | Deferred; additional selection rules and maintenance are not justified yet. |
| Automatic merge/release or renewed CI repair | Not authorized by this plan. |

## Earlier items and completed work

- League discovery now caches the successful list in memory for the widget instance. Reopening equipment searches reuses it; a fresh instance after restart/unloading or explicit Refresh leagues fetches again. No timer, expiry or disk cache. Standard/default selection behavior is unchanged.
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
- [ ] Add a concise acknowledgement linking to PoB Redux in the widget README credits, with fuller attribution in developer documentation. Catalogue documentation does not need credits.
- [ ] Distinguish inspiration from adapted code or reused assets; do not imply contributions were incorporated when they were only evaluated.
- [ ] For reused code or assets, verify the applicable license and retain required copyright/license notices in source and distributed packages. Keep existing PoB Community and Grinding Gear Games credits; Redux attribution supplements them.

The comparison reviewed [PoB Redux at a5afd8b](https://github.com/pobredux/pob-redux/tree/a5afd8b80de993623c1fc5c1541269312361e50f). Its code is a reference for ideas, not proof of measured performance in WidgetRail or an instruction to reproduce every feature. Recheck current upstream code when implementing.
