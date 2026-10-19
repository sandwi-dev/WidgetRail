# PoB widget improvements: discussion and implementation plan

Last updated: 2026-10-18

**Status: planning only — implementation is on hold at the user's request on 2026-10-18.** Keep adding agreed improvements to this document; do not implement them, build, test the application, or launch another candidate until the user explicitly says to start. Earlier implementation authorization does not override this hold. All rows in the implementation tracking table have the same scope approval, but approval to record scope is distinct from permission to start it and acceptance of the finished result. Deferred, excluded and explicitly out-of-scope discussion items remain outside that approval. Checked items record previously implemented scope and its focused verification; they do not replace the separate integrated/native/controller acceptance items. The verification ledger records the evidence and its limits.

This plan covers the next round of PoE1 and PoE2 widget improvements discussed after reviewing PoB Redux. It supplements the [existing trade plan](pob-trade-completion-plan.md); it does not restart completed work or reopen deferred CI repairs.

## Implementation order and tracking

Current acceptance checkpoint (2026-10-18): independent implementation and the
scoped deterministic/native verification are complete for this handoff. Candidate
**j** was last relaunched hidden with the normal profile and controller input; PoE1/PoE2 are
**0.1.4.11** and YouTube Music is **0.3.43.3**. Final fixes cover long reading-block
word boundaries, unavailable music entries, stale account results and light-theme
options-glyph ink. Both installed games pass item/skill editing and Notes flows;
the named cross-app pass and 269 populated-queue checks are recorded in the ledger.

Unchecked compound acceptance items below retain their physical or unavailable
environment/service portions. Authenticated Spotify actions, physical controller
and couch readability, actual Windows contrast-theme changes and selected
hardware/prerequisite/UAC variants are not claimed verified. The earlier transient
visibility/shutdown observations remain unreproduced after scoped passing retests.
See the final verification-ledger handoff for the candidate and precise limits.

| Order | Work item | Status |
| --- | --- | --- |
| Foundation | Clean up shared code and game-specific boundaries | Implemented for changed workflows; typed search/skill projections, owned skill-option lifecycle and bounded caches pass both-engine checks |
| Host prerequisites | Extend reusable rich previews / anchored popovers | Implemented; 136 anchored, 32 passive and 14 indexed-source native assertions pass, including themes and motion; adopted in PoB; remaining integrated/controller acceptance is separate |
| Host prerequisites | Add subtle scroll-container edge cues | Implemented; 22 cue, 42 reading-focus, 93 directional, 10 indexed-reveal and 16 indexed-continuity native assertions pass; final integrated app audit remains |
| Host prerequisites | Finish shared layout, focus, scrolling, sizing and accessibility changes needed by planned widget workflows | Indexed reveal/continuity, busy-focus and shared presentation changes implemented and tested; broader integrated app audit remains |
| After host prerequisites | Adjust widget dimensions and stabilize workspace sizing | Sizing policy, twelve installed section transitions, website/inspection subpages, item/skill edit-return and long Notes scrolling pass in both games; remaining cross-app and physical acceptance are separate |
| After host prerequisites | Bring PoE1 to a tested beta candidate and audit new features | Implemented and engine-tested; packaged beta opens all six sections in the installed fresh PoE1 build |
| After host prerequisites | Improve PoB editing and inspection workflows using Redux as a reference | Implemented; both-engine item/skill regressions, native inspection, installed item edit/cancel and skill toggle/restore with exact opener focus pass; bounded reading and long Notes checks also pass; physical acceptance remains separate |
| After host prerequisites | Complete passive-tree artwork and measure rendering improvements | Implemented; 46 version audits, both-engine checks and 34 renderer assertions pass; packaged trees rendered in both installed widgets |
| After host prerequisites | Add whole-build and loadout comparison | Both-engine and native fixture checks pass; installed PoE2 loadout calculation, Compare focus retention, detail and return-to-row flow verified; broader acceptance remains |
| After host prerequisites | Add unified search and direct navigation | Both-engine and native checks pass; installed PoE2 exact-gem destination and modal return verified; installed fresh PoE1 search verified |
| Cross-app adoption and verification | Compact layouts and inspect the app for couch readability | Catalogue, music player, Settings/Appearance, Audio, Games & Apps, Task Switcher, Network/Power and Browser native passes recorded; couch-distance acceptance remains |
| Independent completed slice | Rename Marketplace to WidgetRail Catalogue | Rename, bounded cache, prerequisites and card styling implemented; familiar-store comparison, 22 integrated native workflows and 150 screenshot-browsing assertions pass; physical controller acceptance remains separate |
| Release distribution | Bare and Production bundles; all optional widgets available through Catalogue | Bundles, resources and 22 native end-to-end Catalogue checks pass, including partial retry, permissions, revert/re-update and idle reopening; earlier invisible-overlay observation remains unresolved |
| Music widget follow-up | YouTube Music and Spotify queue continuity, current-song navigation and saving songs | YouTube Music live workflows and 381 default/light/high-contrast player assertions pass; 269 populated-queue native checks pass, including light-theme options-glyph contrast; Spotify awaits reconnection and physical acceptance remains |
| Display widget expansion | Add trusted reusable display controls, then expose them in the widget | 25 deterministic and 124 native checks pass; real mode Apply/Keep/Revert and HDR preview/revert independently sampled and restored; physical cable disconnection not attempted |
| App update workflow | Controller-only routine updates through the existing Inno installer | Implemented; six isolated real-Inno upgrade/startup-preservation cases pass; production-registration and prerequisite/UAC variants remain unverified |
| Final documentation pass | Reconcile current guides and remove superseded plans/discussions | Current guides reconciled and 22 superseded plans retired; 244 tracked Markdown files / 761 local targets and 44 local Markdown fragments checked; commands and representative screenshot captions reviewed; recheck documentation affected by remaining fixes |

**Dependency order clarified on 2026-10-18:** complete the necessary shared host
contracts and implementations first. Validate them using generic fixtures for
focus/hover activation, dismissal/restoration, stale content, unloading, nested
scrolling, sizing and themes. Only then adopt them in PoB's candidate inspection,
inline editing and other dependent interfaces. Do not build widget-specific
preview/popover workarounds while the reusable API is unfinished. Existing
independent search/filter and music/Catalogue work does not need to be undone.
The already-tested cumulative candidate may be finalized before this next phase.

Cleanup comes before UI additions so new features use the improved boundaries. Preserve existing behavior during cleanup; keep feature changes separately reviewable.

## 1. Shared architecture cleanup

The current apps have separate entry points, packages, engine pins, processes and build storage, but share most C# UI, Lua adapters and web rendering. Sharing common workflows is intentional. Do not duplicate the entire UI or create a framework rewrite.

- [x] Extract cohesive components from the large partial `PobWidget` class for workflows being changed. Give components clear state ownership; moving methods into more partial-class files alone is not sufficient.
- [x] Centralize fixed game differences such as identity, trade endpoints and game-file export support.
- [x] Represent variable feature availability through explicit engine capabilities. Do not replace every legitimate feature condition with a game-number branch.
- [x] Introduce validated, typed adapter-response models for the areas being changed. Distinguish absent optional capabilities from malformed required data; do not silently turn invalid data into plausible defaults.
- [x] Isolate engine-version/API compatibility in focused Lua helpers, separate from UI and gameplay logic.
- [x] Keep unavoidable dependencies on PoB internals inside the adapter and exercise them with real-engine compatibility tests.
- [x] Remove superseded/dead code and check both engines before adding new behavior.

Capability handling should continue to use PoB's available controls, eligibility, choices and enabled state. The widget must not start maintaining its own passive-effect or calculation rules.

## 2. Dimensions and workspace layout

Planning sections, Tree and build comparison now share a preferred width of 1320 logical pixels and use the host's available-height policy, with a 900-pixel preferred height. Management screens retain smaller requests. The host constrains the final dimensions to the display and scaling settings.

- [x] Use the host's existing available-height policy for planning screens.
- [x] Use a consistent, wider workspace across planning sections so switching to Tree does not repeatedly resize it.
- [x] Bound width sensibly on ultrawide displays; do not stretch controls across the entire screen without a usability benefit.
- [x] Keep website and other modals within widget bounds while making them large enough for navigation.
- [ ] Verify smaller displays, display/interface/text scaling, long lists, two-column layouts, controller reachability and focus after resize.

The implementation reuses the host's `FillAvailable` height and preferred-width policies; it does not stretch to an ultrawide display's full width. Small management views remain smaller. Compact/enlarged-text fixtures, twelve installed main-section transitions, website modals, inventory inspection and offline trade flows are verified. Installed item roll editing/cancel and skill Full DPS toggle/restore now pass in both games, including exact return to Group settings and More skill options. Both installed games also pass synthetic long Notes wheel/PageDown scrolling to both boundaries with stable focus. Remaining cross-app coverage and physical couch acceptance remain open.

## 2a. Compact layouts and app-wide visual verification

Follow the architecture cleanup and dimension adjustment. Increase useful information per page and reduce navigation without making the interface difficult to read from a couch. Apply the same principles to subsequent new screens, then repeat the affected visual checks.

### Planned follow-up: broader PoB visual hierarchy and density

**Agreed for the plan; implementation on hold (2026-10-18).** The user finds the
PoB Redux reference more readable and visually polished. Modifier rich text and
isolated compact controls have not sufficiently improved the overall widget:
most other content has similar visual emphasis, and too little useful information
fits on a page. The previously checked layout changes below record delivered
slices, not acceptance of the overall density or visual design.

- [ ] Revisit the supplied Redux references for information hierarchy, grouping and useful information visible at once. Adapt those principles to controller/couch use rather than copying desktop-sized controls or claiming that two panes alone satisfy the design goal.
- [ ] Establish consistent semantic emphasis across the PoB screens: clear section headings, primary values, secondary context, interactive actions, selected state, warnings and comparison changes. Use existing theme roles, typography, restrained surface differences and spacing together. Avoid arbitrary rainbow styling, a new hard-coded palette or making every important element the accent color. Preserve upstream modifier meaning; do not invent gameplay semantics for colors.
- [ ] Apply that hierarchy beyond modifier text to overview statistics, equipment and gem lists, configuration, calculations, search results, comparisons and inspection panels. Keep secondary information readable from the couch; color must supplement labels/icons rather than become the only way to interpret a value or state. Preserve light/dark/high-contrast behavior and unmistakable focus.
- [ ] Perform a genuine density pass across these screens: remove unnecessary nested cards, repeated labels/context, oversized row padding, redundant headings and fixed-height empty space. Place related short values and controls on shared rows with consistent alignment; use columns when they improve comparison. Use selective disclosure for secondary information without hiding information needed for the immediate decision or adding extra navigation to routine actions.
- [ ] Correct excessive Configuration spacing shown in `codex-clipboard-5d499d5b-a3dc-436f-9dc6-b43480b63c20.png` (2026-10-18). Source inspection shows every option wrapped in `.pob-setting` with 12px top/bottom padding, inside a `.pob-panel` with 12px between children: adjacent single-control rows therefore have approximately 36 logical pixels of empty separation. This is widget-authored spacing, not an upstream PoB requirement. Reduce redundant wrapper spacing for simple options while retaining readable explanation/validation groups, comfortable control heights, and distinct section boundaries. Verify explanations both shown and hidden, mixed control types, scaling and focus navigation when implementation resumes.
- [ ] Preserve readable type, practical controller/pointer targets, text/interface scaling, logical navigation and stable focus/scroll during updates. Do not achieve density by globally shrinking text or targets, and do not add a density preference as a substitute for a better default.
- [ ] Record representative before/after views at the same viewport, theme, text scale and build. Compare useful rows/values visible, avoidable scrolling and actions needed for the task; numerical density is evidence, not a quota. Include dense and partially configured builds, short/long content and narrow layouts. Native visual review must assess coherent composition as well as clipping/accessibility. Use the user's reported preview/comparison screenshots as explicit unsatisfactory baselines.

### Previously implemented slices and acceptance

- [x] Improve shared host layouts and PoB screens by reducing excess padding/gaps, redundant headings/descriptions and unnecessary navigation. Keep meaningful explanations and distinctions; do not remove information solely to fit more rows.
- [x] Use the larger workspace, appropriate two-column layouts and compact rows for related values. Keep reading-line lengths reasonable and group related controls clearly.
- [ ] Preserve readable text, unmistakable focus indicators, comfortable pointer/controller targets and existing text/interface scaling. Do not apply indiscriminate font or target shrinking.
- [x] Start with one balanced default; do not add a separate density preference or duplicate desktop/couch layouts in this pass.
- [ ] Walk through the actual app, not only isolated screenshots: shell/rail, Settings, Catalogue, shared dialogs, on-screen keyboard/suggestions, pinned surfaces and available widgets. Exercise each PoB section in both games, including subpages, selectors, expanded details, lists, Notes, trade comparisons and ranked results.
- [x] Record a coverage checklist with tested surfaces, display/scaling/theme conditions, findings and evidence. Include dense, empty, loading, disabled, error and long-label states, plus light/dark/high-contrast appearance and representative TV scaling. Mark unavailable surfaces or states as unverified rather than assuming they are good.
- [ ] Check scrolling, clipping, alignment, focus visibility, controller reachability, mouse interaction, modal bounds and hide/reopen behavior after shared style changes. Fix problems found and recheck affected workflows before reporting completion.
- [ ] Native rendering and interaction checks are required. Final readability at the user's actual couch distance remains a physical user acceptance check; screenshots alone cannot establish that.

The [verification ledger](pob-improvements-verification.md) records completed native matrices, inspected renders, corrected defects and explicit limits. Recording that coverage is complete; completing every remaining acceptance condition is not. In particular, simulated high-contrast ink is not a real Windows contrast-theme change, and native automation/synthetic controller frames are not physical controller or couch-distance acceptance.

## 2b. PoB workflow improvements inspired by Redux

Improve frequent build decisions without copying Redux's desktop layout. These changes follow architecture cleanup and workspace sizing. Reuse existing controls, list/detail layouts, navigation and calculation capabilities; do not turn every presentation pattern into a new public framework element.

- [x] Extend gem search beyond name matching to upstream-provided tags such as projectile. Show upstream compatibility and clearly labelled calculation differences where available. Preserve existing ranking; do not describe it as a missing feature or add a worker-engine pool.
- [x] Bring commonly used skill choices, such as skill part and stat set, beside the selected group when applicable. Keep routine gem add/replace/enable and group editing within the skill workspace where practical, retaining the group list and selection. Put uncommon controls in contextual details rather than permanently crowding every row.
- [x] Let users inspect gem, inventory and unique-library candidates while retaining the candidate list, target group/slot, search and scroll position. Use the shared rich-preview capability where it improves the flow. Browsing or focus changes must not edit the build; explicit Add, Replace or Equip performs the change.
- [x] Make explicitly changed configuration settings identifiable, with a Changed settings filter and section counts. Obtain default values and applicability from PoB. Preserve the existing filtering of irrelevant defaults and visibility of authored settings; do not introduce maintained gameplay rules or new boss presets.
- [x] Use collapsible sections with useful summaries where appropriate: key totals remain visible when collapsed, with detailed rows available on expansion. Apply selectively to calculations and secondary settings. Reuse existing framework facilities first; preserve stable ordering and predictable controller navigation rather than adopting a shifting masonry layout.
- [x] Make existing build warnings and unmodelled item modifiers actionable. Provide compact counts and navigation to the affected item, skill or setting when its identity is known. Preserve exact affected lines and distinguish calculation limitations from invalid settings. Do not guess a target from ambiguous warning text or adopt Redux's custom build-health heuristics.
- [x] Keep calculation context clear beside editing and inspection: relevant skill/component, loadout and weapon set where applicable. Preserve the distinction between main-skill damage, Full DPS and a separately inspected calculation context. Coordinate warning destinations with the planned unified navigation rather than building a second navigation mechanism.

Redux references include its Overview, Skills, Items, Config and Calcs views at the revision recorded below. The rich-preview proposal came from the user; Redux supplies examples of content and workflows, not the definition of the shared component. Attribute actual adopted ideas/code accurately.

### Native visual and workflow acceptance

- [ ] Inspect the actual cumulative candidate for every changed screen, preview and subview after implementation. Automated tests and screenshots alone are not completion evidence. Exercise opening, browsing, editing, scrolling, expanding/collapsing, dismissing and returning with both controller and mouse/keyboard.
- [ ] Check coherent spacing, alignment, typography, hierarchy, panel proportions, readable labels, deliberate empty states and clear selected/disabled/loading/error states. Fix awkward gaps, clipped content, unstable layouts, obscured controls and inconsistent styling before delivery; do not merely document these as polish follow-ups.
- [ ] Verify dense and partially configured builds in both games, long names/descriptions, small and large displays, interface/text scaling and light/dark/high-contrast themes. Ensure focus remains visible and predictable across panels, updates, dismissal, hide/reopen and widget unloading.
- [x] Record coverage, findings, corrections and native visual evidence for verified workflows under the existing app-wide verification checklist. Use local/sanitized fixtures for repeated checks; preserve the minimal-live-GGG constraint.
- [x] Complete the named cross-app native pass and record its evidence. Installed item edit/cancel, skill toggle/restore and long Notes scrolling pass in both games; Settings/Appearance, Audio, Games & Apps, Task Switcher, Network/Power, Browser and populated music queues are covered. Physical controller/couch and unavailable authenticated-service acceptance remain separate.

## Shared rich previews and anchored popovers

### Planned follow-up: content-aware sizing and presentation

**Agreed for the plan; implementation on hold.** The user's inventory screenshot
(`codex-clipboard-8e800868-0b40-4122-ab27-4ad3ad65df47.png`, 2026-10-18)
shows a passive item preview with substantial empty space beneath a small amount
of text, covering much of the existing item-details pane. Its long usage metadata
also needs a clearer hierarchy. The current rendering is not accepted as visually
finished; passing geometry checks alone does not establish that it looks good.

- [ ] Extend shared preview/anchored-inspection sizing so height follows measured content within bounded minimum/maximum dimensions and available widget space. Longer interactive details scroll after reaching the bound; passive previews remain non-interactive and retain an explicit Inspect path for complete details.
- [ ] Use a readable, bounded width appropriate to the presentation rather than one universal size. Keep width stable while browsing a candidate list so adjacent entries do not repeatedly rewrap or move the panel. Respect display/interface/text scaling and small viewports.
- [ ] Prevent asynchronous content updates from repeatedly resizing an inspection during use. Preserve anchor placement, focused controls and reading position; retain existing cancellation, stale-result and lifetime safeguards. Avoid continuous measurement or new on-focus engine/service requests.
- [ ] Choose preview content around the browsing decision. Item previews lead with name, rarity/base and relevant properties/modifiers already available; usage metadata is secondary. Gem previews emphasize description, tags and upstream compatibility where supplied. Wrap or summarize long usage lists while retaining complete details through Inspect. Use existing/upstream facts rather than inventing information or duplicating a full editor in the passive preview.
- [ ] Replace the generic text-stack appearance with a deliberate composition: compact heading, grouped properties, readable modifier text and a restrained Inspect footer. Use typography, spacing and semantic theme roles together; merely shrinking the existing box or adding more colors is insufficient. Keep the shared component content-agnostic rather than baking item/gem layouts into the host.
- [ ] Review placement against both the focused row and the existing details pane. Keep the source row visible, make the relationship to it clear and minimize obstruction of information the user is comparing. When an existing details pane already presents the same information, assess whether an automatic floating preview adds value; avoid redundant overlays and retain an explicit Inspect path. Preserve A's existing action.
- [ ] Carry the same content hierarchy into X → Inspect, with clearly separated actions, stable reading/scroll position and predictable Back restoration. Keep passive browsing distinct from explicit Add/Replace/Equip. Do not resize or reposition interactive content repeatedly while the user is reading or operating it.
- [ ] Keep sizing, placement, timing, bounds and focus/lifetime safeguards in the shared host; widget authors compose the content and choose appropriate bounded options. Establish any necessary shared contract changes before PoB adoption.
- [ ] Compare short, dense and long-name preview examples at identical viewport, theme and text sizing, including the reported theme, light/high-contrast styling and constrained/enlarged layouts. Inspect the actual native inventory, unique and gem workflows for readability, balance, obstruction, usefulness, stable placement and return focus—not just bounds or absence of clipping. Add targeted geometry/lifecycle regressions. No new visualization or application testing during the planning hold.

### Existing implementation and verification

Audit (2026-10-18): `PresentOnFocus` and `FocusPresentationSurface` already provide widget-authored, bounded passive content selected locally by host focus, including indexed-row ownership. Reuse these instead of adding a duplicate passive inspector. Extend placement/activation for anchored previews; retain passive-fragment validation. Explicit interactive inspection should reuse the modal focus scope, Back handling and restoration with an anchored presentation option. Existing presentation identities, row leases and transient input admission must remain authoritative. The implemented API extends these facilities with `FocusPreviewOptions` and modal `AnchoredPresentation`; see [presentation retention](developers/presentation-retention.md). Generic native fixtures precede the implemented PoB adoption.

Control-flow decision (2026-10-18): passive previews follow settled focus or hover
without moving focus, selecting an entry or consuming A/B. PoB keeps the existing
Select/Add/Replace action on A. Interactive inspection is a separately labelled
contextual action, using the existing modal scope and B/Close restoration. Choose
its shortcut per screen only after checking existing bindings; no globally reserved
Inspect button. Host prerequisites remain ahead of PoB adoption.


Provide reusable widget-authored content anchored to an existing element. Content is composed from the normal widget UI elements; it is not restricted to item descriptions, artwork or statistics and does not require another layout language.

- [x] Audit existing native flyout/tooltip facilities, widget declarations, modal/navigation support and controller input handling before designing an API. Tooltips, widget context menus and controller menu navigation already exist; do not list them as missing features or build competing replacements. Extend existing facilities only where the audit establishes a gap.
- [x] Support a passive preview and explicit interactive inspection using a common foundation. Passive previews never take focus or change the build. Interactive inspection accepts supported controls/scrolling, owns an appropriate focus scope and restores focus to the opener on dismissal, or a valid fallback if it has disappeared.
- [x] Allow authors to supply content and bounded options for supported activation modes: mouse hover, settled controller focus or an explicit action. The host owns consistent timing defaults, placement, bounds, dismissal, input admission and focus behaviour. The implemented bounded options and timing defaults are documented in the presentation guide. PoB candidate lists use local X Inspect while retaining A for selection.
- [x] Anchor placement must respect widget bounds and scaling, avoid obscuring the anchor or important controls where possible, and constrain long content. Do not reproduce desktop hover windows that cover much of the usable couch interface.
- [x] Reuse loaded data and valid caches. Delay expensive inspection work until selection settles; cancel or disregard superseded requests. Bind updates to the current anchor, request and widget lifetime. Late results must not replace a newer preview, reopen a dismissed popover or steal focus.
- [x] Bound content/resource use through the existing protocol and host safeguards. Handle rapid navigation, repeated activation, removed/recycled anchors, build switches, errors, nested-surface conflicts and unload safely. No continuous polling or calculation engine per preview.
- [ ] Verify pointer travel between anchor and interactive content, controller entry/Back/scrolling, keyboard access, accessible names and reduced motion. Keep essential information available through an explicit inspect/help path rather than relying solely on a timed hover.
- [ ] Exercise the shared component with PoB and a representative non-PoB example or validation fixture. Perform native visual and workflow acceptance using the checklist above before declaring it ready for widget authors.

## 3. PoE1 beta migration

First milestone: existing PoE1 functionality working against a pinned beta, with evidence and a list of newly available or missing features. Complete desktop parity is not implied by changing the pin.

- [x] Resolve an exact beta revision through the existing candidate tooling; keep PoE2's pin unchanged unless separately required and reviewed.
- [x] Address constructor, module-loading and headless API compatibility.
- [x] Verify imports, repeated loading, calculations, skills, items, tree editing, trade ranking, saving, Undo/Redo and recovery.
- [x] Include empty and partially configured builds, not only finished guide builds.
- [x] Audit beta additions: distinguish inherited calculation/data fixes from controls or workflows requiring widget work. Record concrete follow-up scope rather than silently expanding it.
- [x] Measure startup, memory and calculation performance; upstream benchmark claims are not widget performance guarantees.
- [x] Run PoE2 regressions for shared changes and label PoE1 as beta when a beta-backed candidate is presented to users.

Both widgets now use tested beta pins recorded in [engine-lock.json](../samples/PobCompanion/engine-lock.json). The [PoE1 beta audit](pob-poe1-beta-audit.md) records compatibility, measured performance and desktop features outside this migration.

The existing weekly/manual engine-candidate pipeline is the update mechanism. Passing tests permit candidate packaging; failures retain diagnostics. This plan does not authorize automatic publication, merging or release, and does not reopen deferred CI repair work.

## 4. Tree artwork and rendering

- [x] Add missing upstream tree backgrounds, class/ascendancy artwork, node frames, specialized socket art and jewel-radius overlays, using upstream metadata and state.
- [x] Preserve visible selection, readable details, controller hit targets and theme/high-contrast accessibility.
- [x] Keep assets scoped to the relevant tree/session and bound decoded-image and rendering-cache memory, not just compressed package size.
- [x] Measure rendering with representative trees, zoom levels and display scaling before and after the artwork additions.
- [x] Use appropriate-resolution assets and asynchronous image decoding where beneficial. Add spatial indexing or cached/tiled rendering only when measurements justify the added complexity.
- [x] Preserve coalesced rendering and avoid continuous idle redraws.

All 46 supported tree-version audits pass. The renderer uses lazy decoding with a 52 MiB atlas ceiling and releases assets on tree changes; current trees decode to 19/24 MiB. Before/after drawing samples and scaling limits are recorded in the verification ledger. No Redux renderer was copied.

## 5. Whole-build and loadout comparison

- [x] Compare the current build/loadout with another build/loadout across statistics, passives, equipment, skills and configuration.
- [x] Make differences understandable and provide navigation to relevant details.
- [x] Identify the compared loadouts and calculation context so differences in configuration or main skill are not mistaken for equipment gains.
- [x] Keep comparison read-only unless a separate explicit edit workflow is designed and agreed. Existing item/tree Apply and Discard behavior remains distinct.

The implemented comparison has categorized difference rows and a two-column detail view with return to the inspected row. Selective transfer remains outside scope; comparison has no Apply action.

### Planned follow-up: identify both comparison sides

**Agreed for the plan; implementation on hold.** The comparison setup screenshot
(`codex-clipboard-6e0a4f58-9ed8-46dc-90da-b8bee11713ba.png`, 2026-10-18)
shows “Compare current with” without identifying the source.

- [ ] Show the actual source build name and active loadout before Compare is pressed, alongside the selected target's identity. “Current” may qualify that identity, but must not replace it.
- [ ] Carry those names into the results and side-by-side detail headings so users can tell which build/loadout each value belongs to without returning to setup. Keep long names readable with sensible wrapping and avoid repeating redundant build names when comparing loadouts of the same build.
- [ ] Bind displayed identities to the same source/target snapshots used by the comparison. Switching builds or loadouts must not leave stale labels beside newer results. Retain existing calculation-context distinctions and read-only behavior.

### Planned follow-up: comparison results presentation

**Agreed for the plan; implementation on hold.** The user's results screenshot
(`codex-clipboard-80cb4b00-83c1-45c8-8de8-7765e35350af.png`, 2026-10-18)
shows oversized centered statistic cards, repeated “Changed”/side labels, and a
large context pane dominated by dense metadata. This presentation is not accepted
as finished. Two panes alone do not make the comparison efficient or readable.

- [ ] Replace the tall statistic cards with compact, consistently aligned comparison rows: left-aligned statistic name, clearly identified source and target values, and a useful absolute/percentage change where mathematically meaningful. Use shared column headings with actual build/loadout identities rather than repeating generic labels on every row. Keep one clear focus target per row and full detail inspection.
- [ ] Format values consistently and align numeric columns for scanning. Do not show a misleading percentage for zero baselines, non-finite values or unavailable data. Distinguish a numeric increase/decrease from an improvement; use upstream comparison semantics where supplied and avoid new maintained gameplay rules or color-only meaning.
- [ ] Reduce repeated status text such as “Changed” in a differences-only view. Preserve meaningful Added/Removed/Unavailable states and distinguish main-skill output, Full DPS and other genuinely different metrics.
- [ ] Replace the dominant metadata pane with a compact, readable summary of both named builds/loadouts and decision-relevant differences such as level, main skill and weapon set. Put detailed set names, engine/tree versions and secondary calculation context behind an explicit expandable details action. Retain the explanation that these are whole-build differences, including different calculation contexts, rather than equipment-only gains.
- [ ] Give the comparison results most of the available space and consolidate category/search controls without shrinking readable text or controller targets. Reuse existing layout/list/disclosure facilities. Adapt narrower layouts without misaligning values or hiding essential source/target identity.
- [ ] Verify representative statistics, equipment, skills, passives and configuration results, long names, missing/added entries, zero/non-finite values, scrolling and return from detail inspection. Inspect the actual native page in the reported theme and representative constrained/enlarged layouts; check visible row density and information hierarchy, not only bounds. Keep the screenshot as the before-state reference. No implementation or application testing until the hold is lifted.

## 6. Unified search and direct navigation

- [x] Search passives, skills/gems, equipped and inventory items, configuration options, calculation rows, saved builds and useful actions from one entry point.
- [x] Selecting a content result opens its appropriate section/detail, brings it into view and establishes controller focus.
- [x] Passive navigation centers the node; skill navigation identifies its group; configuration navigation reaches the actual control.
- [x] Reuse existing screens and shared controller/text-entry functionality, including whole-entry suggestions where appropriate.
- [x] Search and content navigation do not equip items, allocate points or change build settings. Action results must use the existing action/confirmation flows.
- [x] Handle stale results, build switches, cancellation, unloading and missing targets without misdirected actions or focus jumps.

Search opens from the build controls, supports category filtering and local whole-entry suggestions, and navigates through existing focus targets. Known item and disabled-main-skill warnings use supplied identities; ambiguous warnings remain informational. Inventory/calculation catalog failures identify unavailable categories and retry on reopening Search.

### Planned follow-up: retain Search build context

**Recorded for the plan; implementation and application testing remain on hold.**
The user reports that Search build forgets the search after selecting a result.

- [ ] Preserve the entered query and category filter when a result opens another section. Returning to or reopening Search build within the same build should restore that context instead of starting with an empty search.
- [ ] Restore the selected result and list scroll position when the result still exists, so users can inspect several matches without retyping or searching through the list again. Use stable result identities and a valid fallback if the previous result is gone.
- [ ] Revalidate results when the build changes; retaining search text must not retain stale destinations or action authority. Keep state scoped to the appropriate build/session. Cross-restart persistence is not required by this report.
- [ ] Cover repeated search → destination → Search build round trips, changed/removed results, empty results and explicit query clearing. Coordinate with the shortcut-navigation regression below without coupling retained search state to stale focus/input ownership.

### Reported regression: shortcuts after global-search navigation

**Recorded for investigation; implementation and application testing remain on hold.**
The user reports two affected destinations:

- Global search → select a “shock chance” result → Tree opens → RS no longer opens Build controls.
- Global search → a Skills result opens Skills → LS no longer performs its contextual shortcut.

Treat this as a potentially shared shortcut/input-ownership regression after search
navigation, not a Tree-only issue. The exact selected result types, expected Skills
LS target and root cause still need verification against the active controller guide;
do not assume search results were misrouted.

- [ ] Reproduce both sequences and compare RS/LS behavior after ordinary section entry versus entry through global search. Check the displayed controller guide, active input/focus scope, destination shortcut registration, tree/browser interaction ownership and completion of the search navigation handoff.
- [ ] Restore each destination's intended shortcuts without consuming local controls incorrectly or leaving stale search/modal ownership. Determine the cause before changing shared input admission or introducing destination-specific shortcut workarounds.
- [ ] Add focused regressions for search → Tree → RS → Build controls and return to the selected node, plus search → Skills → LS → its declared contextual target and return. Check other search destinations and normal section navigation for the same ownership problem, preserving existing contextual shortcuts and Back behavior.

## Host-wide naming: WidgetRail Catalogue

Rename the user-facing Marketplace to **WidgetRail Catalogue**, using the user's preferred spelling. It is a place to discover, install and update widgets, not a shop.

- [x] Update the widget display name, main heading, Settings entry points, controller guide labels and user-facing status/error messages.
- [x] Update user-facing documentation and installation/update instructions consistently, including the PoB README.
- [x] Show service/setup prerequisites at discovery time, before download/install review. Add concise listing requirements and a readable details section covering required external software, accounts/subscriptions, developer applications and user-provided keys where applicable. Audit every listed widget, including newly catalogued first-party widgets; do not assume package permissions explain service setup.
- [x] Explicitly cover Playnite's separate Playnite Bridge and token, Spotify's developer-app configuration and applicable account/playback requirements, and Game Help's user-provided Gemini API key. Verify current requirements against each adapter/guide before publishing the wording. Provide the existing setup-documentation route; never ask users to enter tokens or keys into the Catalogue.
- [x] Keep setup prerequisites distinct from sandbox/full-access permission review and from in-widget onboarding. Retain the existing setup inside widgets; this fixes a missing pre-install discovery step, not a missing settings implementation. Test listing metadata, details visibility, long/absent requirements and controller/mouse navigation, and include the content in the final documentation audit.
- [x] Preserve existing widget IDs, intent/action identifiers, package identities and protocol/API contracts so the display rename does not break installed configurations or integrations.
- [x] Check label fit and navigation in the catalogue and Settings. Update existing name assertions where applicable.
- [x] Fix the catalogue tests alongside the rename. Replace the stale exact listing-count assertion with meaningful inventory/identity/screenshot contract checks, rather than merely changing 10 to 11. Run the affected service and presentation suites and report genuine remaining failures separately.
- [x] Correct outdated user documentation about version history, retention and updating again after a rollback.
- [x] Compare actual browse, details, install, installed-management and update flows with familiar patterns in popular app stores (for example Microsoft Store, Steam and winget GUI clients). Use those patterns as references, while preserving controller access and clear permission/full-access review.
- [x] Use the real widget in native automation to exercise browsing, screenshots, Back/focus restoration, install review, cancellation, individual updates, Update all, partial failure/retry, Settings handoff, rollback/re-update and hide/reopen behavior. Use isolated packages/profile where destructive test cases would affect the normal installation. Do not confuse model/snapshot tests with native end-to-end acceptance.
- [ ] Complete physical controller and mouse/keyboard acceptance of those workflows. Native action dispatch and declared controller bindings do not establish physical controller delivery; screenshot browsing's session-routed B remains unverified.
- [x] Implement improvements revealed by that comparison and walkthrough: simplify unnecessary steps, clarify labels and primary actions, preserve navigation/focus, and make download/review/install/completion/error states easy to understand. Retest the revised flows; this is an improvement task, not only an audit/report.
- [x] Review GitHub's current documented limits and API guidance when implementing. Add suitable release-metadata freshness caching and conditional requests where supported, coalesce duplicate checks, and avoid a fresh network request on every reopening. A manual refresh must still respect server-directed cooldowns.
- [x] Handle primary/secondary rate limits and transient failures using relevant response headers (including Retry-After and rate-limit reset information), bounded backoff and clear retry feedback. Do not use aggressive polling, concurrent retries or credentials as a way around limits. Keep cached content usable and identify stale availability when refreshing fails.
- [x] Address release discovery beyond the first 20 releases with a bounded, cached strategy so older widget assets do not disappear as newer app releases accumulate. Measure request counts for first open, repeated open, explicit refresh and update batches.
- [x] Test caching, unchanged responses, expiry, rate-limit responses and retry behavior with deterministic fake HTTP/time fixtures. Do not intentionally exhaust GitHub's live limits to test them. Record native workflow and request-count evidence before declaring completion.

Evidence is recorded in the ledger: the UniGetUI familiar-store comparison, deterministic cache/request-count checks, 22 native installation/update workflows, 13 screenshot-flow widget tests and 150 native screenshot-browsing assertions. Screenshot images and release responses were locally substituted; these checks do not claim remote image delivery or physical controller acceptance. The earlier invisible-overlay observation remains unresolved despite later successful current Release and fresh Debug reopen checks.

This is independent of the PoB architecture work. Technical identifiers and historical records need not be mechanically renamed.

## Release distribution: Bare and Production

From the next release, offer the same application and host capabilities with two initial widget bundles: Bare contains Settings and WidgetRail Catalogue; Production contains those plus a selected set of generally useful widgets. All optional widgets must be available through Catalogue, including first-party widgets currently available only in the bundled installation. Bundle choice must not restrict which widgets a user can install later.

- [x] Reserve the user-facing term **built-in** for Settings and WidgetRail Catalogue. Present everything else simply as a **widget**, whether preinstalled in Production or installed later. Remove the community-versus-built-in classification from Catalogue, Settings, installation/management flows and current user documentation; Production is a starting selection of ordinary widgets, not a separate widget class.
- [x] Keep publisher identity, installation origin and permissions distinct. Show the publisher and relevant sandboxed capabilities/full-access review accurately. Use preinstalled versus user-added information only where it helps management; neither preinstallation nor Catalogue inclusion grants extra privileges or implies a quality guarantee. Preserve package identities, provenance checks and security boundaries internally; this terminology change does not authorize weakening verification or mechanically renaming protocol identifiers.
- [x] Replace Developer edition distribution with Bare. Remove obsolete edition-specific packaging, installer, update-selection and test assumptions. Backward compatibility or automatic migration for Developer edition is not required; keep useful SDK tooling, source samples and validation fixtures independently of a shipping Developer edition.
- [x] Prepare installable Catalogue packages for all optional widgets, with appropriate descriptions, capabilities, dependencies and review flows. Preserve package identity and trust validation; a Catalogue listing must not grant special host privileges. Verify first-party package admission where currently tied to bundled provenance.
- [x] Use the same host binaries and shared capabilities in both bundles. Keep dependencies needed by subsequently installed widgets; do not remove shared services merely because their consuming widget is absent from Bare.
- [x] Make first-party and other optional widgets installable, updateable and removable through the normal Catalogue/Settings workflows. Handle existing bundled copies coherently, without duplicate rail entries or competing update identities.
- [x] Preserve bundle choice during application updates and retain user-installed widgets. Do not reset a Bare installation to Production or remove added widgets during an app update.
- [x] Regression-check shortcuts, settings links, intents and provider discovery with optional widgets absent. Reuse existing missing-provider feedback, declared intent fallbacks and default-browser fallback; these are existing functionality, not new systems to implement. Fix only demonstrated gaps. The current generic unavailable message does not identify a Catalogue package to install; package-specific installation guidance is not assumed to exist or required without a concrete workflow need.
- [ ] Verify clean Bare installation, Guide opening, Settings, Catalogue, installing and launching optional widgets, restarting, removing widgets and Bare-to-Bare updates. Verify Production uses the same installation/update behaviour. Check native presentation and controller/mouse flows in both bundles.
- [x] Measure package size and startup/resource impact rather than promising proportional savings from the smaller widget count. Finalize the generally useful Production bundle during implementation review.

The packages are prepared and verified locally; public Catalogue asset publication accompanies a separately authorized release. This plan does not independently authorize publishing a release. Settings and Catalogue remain the core bootstrap surfaces. Developer edition retirement does not imply removal of development support from the project.

## YouTube Music and Spotify workflow follow-up

Requested on 2026-10-18; implementation resumed under the approved goal. The earlier investigation-only hold is superseded. These widget changes are independent of the PoB cleanup. Service limitations below constrain delivery; do not simulate unsupported remote operations or silently replace the user's remote queue.

### Investigation baseline (before this implementation)

- Both queue presentations use indexed collections, not adjacent cursor pagination. YouTube Music replaces every occurrence key with a generation/index key on queue membership changes. Spotify presents an indexed collection backed by a one-page cursor resource with no adjacent cursors; it already has occurrence identity reconciliation and content-only updates, but queue invalidation/loading publication still needs workflow inspection.
- Game Help uses `ScrollRevealRequest`, currently validated only for declared children of ordinary scroll containers. It cannot directly target an unrealized indexed row. Indexed collections already provide `FocusTarget` and `Enter` for explicit reveal-and-focus navigation.
- YouTube Music owns its playback queue locally, currently capped at 500. Playlist Play next already reads the playlist in provider order and inserts the leading songs immediately after the current occurrence. Its service interface exposes neither queue removal nor rating/library mutations. The existing ytmusicapi dependency provides authenticated `rate_song` and separate library-status operations; those are distinct actions, and dependency support is not an official Google API guarantee.
- Spotify currently caps its observed upcoming queue at 50. Currently playing is returned separately and is not a queue row. The widget exposes Add to queue for tracks but no saved-song action; library read/modify scope identifiers exist but are not requested by the current connection flow. The documented Web API exposes queue reading and appending tracks/episodes, not removing arbitrary queued occurrences, inserting next, or submitting a playlist as one queue addition.

### Implementation and remaining acceptance

- [x] On navigation into Queue, reveal the actual playing occurrence once, without selecting, activating or moving focus to the song. This is automatic entry behaviour, not a new Jump to playing button. For Spotify, clearly present the separate current track rather than labelling the first upcoming item as playing. Queue mutations, playback ticks/track changes, likes, refreshes, rerenders and overlay hide/reopen while already on Queue must not trigger another reveal. Leaving Queue and navigating back starts a new entry request.
- [x] Use non-focus-moving reveal semantics, extending the existing shared `ScrollRevealRequest` contract narrowly for the existing indexed targets. Do not use `FocusTarget`/`Enter` as a scrolling workaround or introduce a music-specific API. Bind a pending reveal to the queue-entry request and current source; resolve the playing occurrence when initial data is ready, apply at most once, and cancel on leaving the page or subsequent user scrolling/navigation so late data cannot override their interaction. If no playing occurrence exists, leave the normal queue position unchanged. Retain the existing indexed list presentation; conversion to an ordinary materialized list is no longer part of this scope.
- [x] Diagnose full-list refreshes and preserve occurrence identity, selection, scroll position and usable old content during updates. Fix YouTube Music's key churn; inspect Spotify's invalidation/loading transitions separately. Use existing content-only updates for metadata/playing/liked-state changes. Membership changes must still reject stale actions safely. Do not replace the list type solely because a cursor wrapper exists.
- [x] Preserve existing queue limits and behaviour: YouTube Music stays at 500 and Spotify retains its current 50-item observation bound. The proposed 100-item limit is withdrawn; it was based on an assumed cursor queue. Do not change capacity, insertion/trimming, shuffle/original-order handling or unrelated library/search limits as part of the refresh fix. The separately requested removal, saving and entry-only reveal features remain in scope.
- [x] Add Remove from queue for YouTube Music using occurrence identity, including repeated song IDs. Keep shuffled and original order consistent. Define current-track removal explicitly and verify next/end/repeat/empty-queue behaviour without accidental playback changes. For Spotify, do not offer a misleading local-only Remove action; retain this as provider-limited unless a documented supported operation becomes available. Replacing playback with a filtered snapshot is not equivalent and is outside this approval.
- [x] Add YouTube Music Like/Unlike and Spotify Save/Remove from liked songs, with visible current state on the player and appropriate row/context actions. Distinguish YouTube Music liking from Add to library; do not treat them as interchangeable. Reuse existing authenticated adapters and verify pinned dependency/account support. Spotify needs the appropriate library read/modify consent and a clear reconnect path for existing accounts; avoid unrelated scopes.
- [x] Use Spotify's current library save/remove/contains endpoints rather than building on deprecated saved-track APIs. Batch visible-track state checks where supported, cache per account, invalidate affected entries after writes, coalesce duplicate clicks and reject late results after account/song switches. Report success only after acknowledgement; preserve a usable state on failure.
- [x] Ensure playlist Play next in YouTube Music uses the first playable songs in provider playlist order, within the existing 500-entry queue policy, rather than the visible page, current selection or a random subset. Preserve their relative order even when shuffle is enabled and clarify any truncation. Verify album handling too. Spotify's available operation is Add to queue, not guaranteed Play next; playlist expansion would require bounded individual additions and must not be presented as an atomic or insert-next API. Do not add bulk Spotify queue traffic without resolving that separate workflow.
- [ ] Test duplicate occurrences, removing before/after/current, natural track changes, shuffle/repeat, maximum-size queues, playlist beginnings, partial/empty playlists, slow/failing responses, cancellation, rapid repeated actions, account switches, hide/reopen and restart. Use deterministic service fakes for repetition; avoid live playlist/rating/queue mutations during investigation. Perform remaining native visual and controller/mouse workflow checks before completion.

### Agreed use of existing shared APIs

- [x] Add whole-entry keyboard suggestions using recent searches and already-loaded music names locally. Do not introduce live provider autocomplete or extra service requests to populate suggestions. Keep suggestions secondary to the queue fixes.
- [x] Add the supported Remove and Like/Save actions to the existing context menus; both widgets already have context actions. Do not create another menu mechanism.
- [x] Reuse existing busy/disabled states while a mutation is pending and coalesce repeated submissions. Keep playback controls responsive during unrelated browsing/account writes. Update liked/saved state after acknowledgement; show a concise error with a retry path on failure. Prefer the visible state change over redundant success toasts. Spotify already uses queue-action toasts and YouTube Music has status text; improve consistency using existing facilities rather than introducing new host feedback APIs.

### Live testing authorization and service traffic

- The user authorizes using their normal profile for live testing of these changes, including the music actions needed to verify the agreed workflows. Implementation resumed with the 2026-10-18 instruction to continue the plan.
- Use **WidgetRail as the Spotify playback device**. Verify the active target before playback/queue tests; do not use another Spotify Connect device as a fallback. If WidgetRail is unavailable, report the blocker rather than redirect playback elsewhere.
- Use Spotify and YouTube Music as a person would: deliberate browsing, playback and individual actions at normal pace. No rapid calls, automated request bursts, bulk live mutations, repeated refresh loops or service stress testing. Account for background polling and automatic refreshes as well as explicit actions. Use cached data and coalesced refreshes; respect rate-limit/retry responses and stop live retries on throttling or authentication challenges.
- Keep rapid-input, race, failure and maximum-size tests in deterministic fakes/local fixtures. Use a small number of representative live checks for real integration and native controller/mouse workflows. Do not run scripted live mutations merely to populate a maximum-size test queue. Record which checks were simulated versus live.
- Verify the original liked/saved state before a live mutation and restore any test-only library changes afterward. Any renewed login or additional consent must use the service's normal user-facing flow; never request credentials in chat.

Music-specific use of shared rich previews remains an opportunity to evaluate after that capability exists, not an additional approved deliverable or dependency of the queue fixes. Do not build a music-only popover.

Read-only references checked: [Spotify queue](https://developer.spotify.com/documentation/web-api/reference/get-queue), [add to queue](https://developer.spotify.com/documentation/web-api/reference/add-to-queue), [save library items](https://developer.spotify.com/documentation/web-api/reference/save-library-items), [check saved state](https://developer.spotify.com/documentation/web-api/reference/check-library-contains), and [ytmusicapi library operations](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/library.py). Recheck provider/dependency compatibility when implementing.

## Shared host scroll-container edge cues

Show whether content remains above or below a vertically scrolling container. This is a reusable host presentation feature, not PoB-specific drawing.

- [x] Initial implementation: short, centered themed lines at the inside top/bottom edges, 40 logical pixels wide and 2 thick. The planned viewport-relative sizing below supersedes this fixed width when implementation resumes.
- [ ] Planned, on hold: increase cue width modestly and derive it from the actual scrolling viewport width. Start with **8% of viewport width, clamped to 48–80 logical pixels**, retaining **2-pixel thickness**. Resize with the panel, not with scroll progress. Keep centered placement, existing theme/high-contrast ink, reserved strips and non-interactive behavior. Verify small and wide panels, scaling, nested scrollers and fitting content without adding polling or layout feedback loops.
- [x] Show the top cue only when content is hidden above, and the bottom cue only when content is hidden below. Show both between the ends and neither when content fits.
- [x] Keep length independent of scroll progress. The proposed growing/shrinking lines were rejected because they would duplicate a scrollbar. Preserve existing scrollbars for position and extent information.
- [x] Place cues in stable edge padding or a narrow reserved strip, outside the text/control viewport. Never obscure useful content, intercept input, participate in focus navigation or change layout as cues appear/disappear.
- [x] Use a muted theme role distinct from the focus outline; ensure light/dark/high-contrast readability. No pulsing or bouncing. A brief visibility fade is acceptable, respecting reduced motion.
- [x] Determine visibility from actual scroll extent/offset with a small boundary tolerance to avoid fractional-offset flicker. Update on scrolling, resizing and content changes without continuous polling or idle rendering.
- [x] Handle independently scrolling nested containers without drawing duplicate cues for wrappers that do not scroll. Keep implementation scope to host-owned scroll surfaces; do not inject indicators into remote websites.
- [ ] Verify Notes-style text, virtualized lists, calculation breakdowns and modals, including no overflow, both boundaries, dynamic content, scaling and controller/mouse navigation. Confirm indicators do not conflict with the existing hidden-interaction focus outline or focus-after-settle behavior.

The conversation mockup is a design reference only; native visual acceptance is still required. No additional visualization is requested.

## Display widget expansion

Added at the user's request on 2026-10-18. Expand the existing Display Profiles widget into a useful controller-accessible display-control surface, retaining its saved-profile workflow. Consider a clearer name such as **Display Controls** if profiles become one part of the widget. Preserve widget/package identity, permissions and saved profiles when changing the display name.

Reference: [Quick Settings](https://github.com/LoZazaMastro/Quick-Settings), inspected read-only at `72013a2b19d1ebcc4a47358fe5bec763ce826dfe`. Its display features include resolution and refresh-rate selection, native HDR, a software screen dimmer and AMD ADLX color controls. The dimmer is an overlay, not monitor-backlight control; AMD brightness/color settings are driver-specific. Reference functionality is not proof of compatibility with every WidgetRail user's hardware.

- [x] Audit the existing display-profile provider and reusable host services before adding controls. Existing profiles already preserve topology, resolution, exact refresh rate, orientation, positions and primary display; expose suitable direct controls without duplicating that native implementation.
- [x] Plan and implement direct per-display mode controls, prioritizing resolution, refresh rate and supported HDR. Enumerate actual supported modes/capabilities, identify the affected monitor clearly, and verify applied state rather than displaying optimistic success. Evaluate primary/orientation/topology controls against existing profile functionality and controller usability.
- [x] Evaluate brightness/dimming and color controls from the reference. Distinguish hardware brightness, software dimming and GPU color adjustment in both capabilities and labels. Choose supported, bounded implementations after checking Windows APIs, monitor/driver support and dependencies; do not silently replace a missing hardware capability with a dimming overlay.
- [x] Keep reusable display operations and native/device access in the trusted host provider, exposed through appropriately scoped SDK capabilities. The widget remains presentation/orchestration rather than bundling a separate localhost agent, vendor helper or raw native implementation by default.
- [x] Preserve the existing timed Keep/Revert and independent rollback guard for mode/topology changes. Cover monitor disconnection, unsupported modes, external changes, expiry, widget unload, host shutdown and failed readback. Keep controls responsive during slow native operations, coalesce repeated slider changes and avoid continuous polling.
- [x] Keep common controls compact and readable from the couch, use selectors where appropriate, and give unsupported or unavailable features accurate treatment. Make direct current-display adjustments distinct from saving/replacing a profile. Define deliberately which new fields profiles save; do not silently change the meaning of existing saved profiles.
- [x] Use fake backends for repeated apply/revert, failure and race tests; inspect native presentation and perform a small coordinated hardware check with restoration of test-only changes. Record unsupported hardware-dependent paths explicitly.
- [x] Attribute actual inspiration/adapted code, verify the license of each reused component, and retain required notices. Quick Settings' repository is MIT, but its bundled dependencies have separate terms; do not assume every binary can be copied under that license.

This addition concerns display controls. Quick Settings' audio, CPU/TDP, game-launch automation, Lossless Scaling and broader Radeon gaming features are reference context, not automatically included in the requested display-widget expansion. No reference executables, installers or helpers were run during the audit.

## Controller-only routine app updates

Added at the user's request on 2026-10-18. Keep Inno Setup, the unsigned distribution and the current lack of Windows package identity. For an existing per-user installation whose prerequisites are already satisfied and whose execution Windows permits, the target is **Install and restart → unattended installation → WidgetRail reopens**. The overlay briefly closes; a separate always-visible updater UI is not required.

The implemented helper verifies the target's unattended capability and prerequisites before closing the app; the installer rechecks them. Silent upgrades preserve startup and update-check choices and report outcomes through the app. Six real isolated-Inno cases cover both editions and enabled/disabled/foreign startup registrations. Production registration, prerequisite/UAC and actual failure/restart variants remain unverified.

- [x] Reuse verified-installer admission and the existing close/install/relaunch handoff. Supply the appropriate Inno unattended flags for routine upgrades, including deliberate restart/log behavior. Never use silent mode to skip package checks, overwrite another installation, or suppress required consent.
- [x] Detect required prerequisite work before closing WidgetRail. Missing/outdated GameInput or another prerequisite requiring prompts must select a clearly explained interactive setup path. Recheck in the installer so a race cannot unexpectedly launch elevation in the routine silent path.
- [x] Preserve enabled and disabled startup choices explicitly in silent mode; do not rely on task-page callbacks. Preserve automatic update-check preference, selected Bare/Production bundle, user-installed widgets and the existing installation directory. Cover foreign startup registrations without taking ownership of them.
- [x] Ensure unattended paths cannot block on custom `MsgBox` calls. Record startup-registration or other nonfatal warnings and surface them in WidgetRail after relaunch. Keep appropriate interactive messages for the explicit interactive fallback.
- [x] Handle installer cancellation, failure and restart-required exit outcomes deliberately. Where the installed application is verified usable, reopen it and show the result with a controller-accessible recovery/retry action. Do not blindly relaunch a partially invalid installation or rely on a mouse-only helper message box as the routine recovery path.
- [x] Replace the current blanket mouse/keyboard update warning with accurate path-specific guidance once the controller-only path is verified. Silent flags cannot approve UAC or bypass Smart App Control or other Windows application-control restrictions; provide an explicit fallback rather than promising every installation is unattended.
- [ ] Test both startup preference states, prerequisite readiness/change, installer arguments and identity checks, process shutdown, cancellation/failure/restart-required results, warning delivery, relaunch, and Bare/Production preservation using deterministic fakes. Perform a coordinated real routine-upgrade check plus a safe interactive-fallback check without unnecessarily changing prerequisites on the user's machine.

References: [Inno command-line options](https://jrsoftware.org/ishelp/topic_setupcmdline.htm), [privilege behavior](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), and [Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview). Read the current documentation when implementing. This is update-workflow work, not a signing/package-identity migration or authorization to resume unrelated deferred CI repairs.

## Final documentation audit and retirement

### Everyday experience first (approved 2026-10-18)

The README, Quick Start, bundle membership, update flow and install/permission wording have been reconciled. The owner demonstration remains a placeholder. Final review must include the latest PoB/display changes and the current acceptance limits.

- [x] Rewrite the README opening around **Your PC, ready for a controller**: use the existing Windows desktop and games without entering a separate fullscreen launcher. Explain launch once/startup at sign-in and the supported View + Menu toggle before introducing the C# framework.
- [x] Make Quick Start demonstrate one useful action: install/launch, open with View + Menu, adjust an application's volume with Audio Mixer, then hide and return. Account for Bare edition needing Audio Mixer installed through Catalogue; recommend Production for this first-run example.
- [x] Keep the README order: opening and short demo, Quick Start, everyday uses, optional widgets and setup, compatibility, building widgets. Preserve the existing documentation-index structure. Move CLI commands, renderer/cache internals and exhaustive setting tables into maintained guides.
- [x] Reconcile Settings → General, Appearance/interface size, update checks versus explicit installation, current update recovery behavior and path-specific install/enable/permission behavior. Keep one detailed explanation per topic and link to it.
- [x] Add a clearly marked owner-supplied demonstration placeholder: one uninterrupted supported desktop/game → open overlay → adjust audio → switch app or pin content → hide sequence, with View + Menu visible. Use placeholders only for assets or information the owner must supply, never in place of facts verifiable from code.

Run this after implementation decisions settle, then recheck changed links and examples before delivery. Public documentation should describe the actual delivered app, not a mixture of former plans and current behavior.

- [x] Audit the top-level README, Getting Started, troubleshooting, update guide, Catalogue/installation guide, widget READMEs/user guides and relevant developer/reference documentation against the current code, manifests and packaging.
- [x] Resolve the reported update conflict: README currently promises pre-update profile backups/recovery while the dedicated update guide describes the newer flow. Document the implemented update/recovery behavior consistently; do not restore obsolete behavior merely to match old prose.
- [x] Resolve fresh full-access install guidance: distinguish reviewed interactive installs that start enabled from low-level CLI staging that remains disabled, while retaining required approval and permission behavior. Troubleshooting must match the Catalogue and Settings workflows.
- [x] Resolve Game Help distribution guidance from the final Bare/Production package contents and Catalogue listing. Getting Started and README must agree on what is preinstalled versus installed separately. Apply the same check to every optional widget and remove retired Developer edition guidance.
- [x] Remove superseded plans, old discussion documents and redundant status/specification copies after transferring any still-current requirements, operational instructions or useful rationale into maintained guides. Use Git history for obsolete discussion rather than creating another stale documentation archive. Keep the active execution plan/verification record authoritative until delivery; do not delete requirements that remain open.
- [x] Update navigation/index pages and incoming links for removed/renamed documents. Check file links, commands, screenshots/captions, feature names, version assumptions and permission claims. Retain required license/copyright notices and accurate adopted-project credits.
- [ ] Recheck documentation affected by the remaining implementation/acceptance corrections before delivery. Retain exact limitations for Spotify authentication, physical controller/couch checks and environment-specific acceptance; do not describe representative earlier screenshots as the current candidate.
- [x] Keep the front-facing README concise and approachable; place technical architecture, provider limitations and maintainer procedures in their appropriate dedicated guides. Report any remaining documentation uncertainty rather than implying unsupported behavior is available.

The three reported contradictions are corrected. The final source/documentation pass also corrected two obsolete collection fragment links and remaining music-guide installation wording. Current command paths/options were checked against their scripts/CLI definitions without executing builds. Representative README, Spotify and PoB calculation/trade screenshot captions were checked against their pixels; older PoB captures are dated and explicitly illustrative. External destinations were not requested, and the owner demonstration remains a placeholder. Recheck subsequent corrections against these maintained guides without restoring obsolete behavior.

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

## Efficient verification strategy

- Use deterministic tests and local fixtures for logic, lifecycle, races, stale results, failure paths and resource limits. Repeated checks must not call live services.
- Use native UI automation for focus, reachability, scrolling, bounds, enabled state and navigation. Batch related checks and report failures compactly.
- Inspect representative changed layouts visually: dense content, materially different responsive layouts and relevant empty/error states. Crop evidence to the affected surface where possible; repeat only after relevant changes, failures or uncertainty.
- Check geometry/accessibility across representative themes/scales automatically; inspect appearance where it differs materially or automation flags a concern.
- Coordinate one integrated workflow pass, then targeted retests. Keep one shared coverage record so parallel contributors do not duplicate work.
- Distinguish automated, native visual and physical controller/couch acceptance. Do not claim all possible visual states or couch readability were verified from screenshots.

## Verification and delivery records

For each implemented item, record the commit, relevant tests, performance evidence where applicable, visual inspection and remaining limitations below. Distinguish automated checks from physical acceptance. Check both games when changing shared code. Current evidence and unverified states are recorded in [the verification ledger](pob-improvements-verification.md).

Cumulative candidates may be launched hidden with the normal profile, as previously authorized; keep one candidate instance. A documentation-only update does not require rebuilding or relaunching the application.

| Item | Commit / evidence | Remaining work | Acceptance |
| --- | --- | --- | --- |
| Current implementation | Commits and evidence in the verification ledger | Remaining integrated/native/controller acceptance and final reconciliation | Scope approval does not imply physical acceptance |

## PoB Redux attribution and reference implementation

Credit PoB Redux for the ideas, techniques and implementation work that actually contribute to these improvements. Attribution is part of delivery, not an optional follow-up.

- [x] Record the specific Redux contributions adopted as each feature is implemented, with source links and revisions where relevant.
- [x] Add a concise acknowledgement linking to PoB Redux in the widget README credits, with fuller attribution in developer documentation. Catalogue documentation does not need credits.
- [x] Distinguish inspiration from adapted code or reused assets; do not imply contributions were incorporated when they were only evaluated.
- [x] For reused code or assets, verify the applicable license and retain required copyright/license notices in source and distributed packages. Keep existing PoB Community and Grinding Gear Games credits; Redux attribution supplements them.

The comparison reviewed [PoB Redux at a5afd8b](https://github.com/pobredux/pob-redux/tree/a5afd8b80de993623c1fc5c1541269312361e50f). Its code is a reference for ideas, not proof of measured performance in WidgetRail or an instruction to reproduce every feature. Recheck current upstream code when implementing.

### Music control refinement (2026-10-18)

Saved songs use a compact themed heart beside Spotify's title, with outline/filled
states, action labels and mouse tooltips. Pending Save/Like controls retain stable
identity and focus eligibility; duplicate operations are coalesced rather than
disabling the currently focused control. Native busy/idle identity and focus checks
cover both players and pins. Spotify's pane and compact pin remain scrollable at
constrained heights. Final native/controller integration remains separate.
