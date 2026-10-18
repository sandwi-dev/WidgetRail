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
| Release distribution | Bare and Production bundles; all optional widgets available through Catalogue | Agreed; not started |
| Independent host change | Add subtle scroll-container edge cues | Agreed; not started |
| Shared host capability, before dependent widget flows | Audit and extend reusable rich previews / anchored popovers | Agreed; not started |
| Music widget follow-up | YouTube Music and Spotify queue continuity, current-song navigation and saving songs | Agreed for planning; implementation explicitly on hold |

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

## Release distribution: Bare and Production

From the next release, offer the same application and host capabilities with two initial widget bundles: Bare contains Settings and WidgetRail Catalogue; Production contains those plus a selected set of generally useful widgets. All optional widgets must be available through Catalogue, including first-party widgets currently available only in the bundled installation. Bundle choice must not restrict which widgets a user can install later.

- [ ] Reserve the user-facing term **built-in** for Settings and WidgetRail Catalogue. Present everything else simply as a **widget**, whether preinstalled in Production or installed later. Remove the community-versus-built-in classification from Catalogue, Settings, installation/management flows and current user documentation; Production is a starting selection of ordinary widgets, not a separate widget class.
- [ ] Keep publisher identity, installation origin and permissions distinct. Show the publisher and relevant sandboxed capabilities/full-access review accurately. Use preinstalled versus user-added information only where it helps management; neither preinstallation nor Catalogue inclusion grants extra privileges or implies a quality guarantee. Preserve package identities, provenance checks and security boundaries internally; this terminology change does not authorize weakening verification or mechanically renaming protocol identifiers.
- [ ] Replace Developer edition distribution with Bare. Remove obsolete edition-specific packaging, installer, update-selection and test assumptions. Backward compatibility or automatic migration for Developer edition is not required; keep useful SDK tooling, source samples and validation fixtures independently of a shipping Developer edition.
- [ ] Publish installable Catalogue packages for all optional widgets, with appropriate descriptions, capabilities, dependencies and review flows. Preserve package identity and trust validation; a Catalogue listing must not grant special host privileges. Verify first-party package admission where currently tied to bundled provenance.
- [ ] Use the same host binaries and shared capabilities in both bundles. Keep dependencies needed by subsequently installed widgets; do not remove shared services merely because their consuming widget is absent from Bare.
- [ ] Make first-party and other optional widgets installable, updateable and removable through the normal Catalogue/Settings workflows. Handle existing bundled copies coherently, without duplicate rail entries or competing update identities.
- [ ] Preserve bundle choice during application updates and retain user-installed widgets. Do not reset a Bare installation to Production or remove added widgets during an app update.
- [ ] Regression-check shortcuts, settings links, intents and provider discovery with optional widgets absent. Reuse existing missing-provider feedback, declared intent fallbacks and default-browser fallback; these are existing functionality, not new systems to implement. Fix only demonstrated gaps. The current generic unavailable message does not identify a Catalogue package to install; package-specific installation guidance is not assumed to exist or required without a concrete workflow need.
- [ ] Verify clean Bare installation, Guide opening, Settings, Catalogue, installing and launching optional widgets, restarting, removing widgets and Bare-to-Bare updates. Verify Production uses the same installation/update behaviour. Check native presentation and controller/mouse flows in both bundles.
- [ ] Measure package size and startup/resource impact rather than promising proportional savings from the smaller widget count. Finalize the generally useful Production bundle during implementation review.

This records the next-release distribution scope; it does not independently authorize publishing a release. Settings and Catalogue remain the core bootstrap surfaces. Developer edition retirement does not imply removal of development support from the project.

## YouTube Music and Spotify workflow follow-up

Requested on 2026-10-18. Investigation and planning only for now: the user explicitly said not to fix these yet. These widget changes are independent of the PoB cleanup. Service limitations below constrain delivery; do not simulate unsupported remote operations or silently replace the user's remote queue.

### Verified current behaviour

- Both queue presentations use indexed collections, not adjacent cursor pagination. YouTube Music replaces every occurrence key with a generation/index key on queue membership changes. Spotify presents an indexed collection backed by a one-page cursor resource with no adjacent cursors; it already has occurrence identity reconciliation and content-only updates, but queue invalidation/loading publication still needs workflow inspection.
- Game Help uses `ScrollRevealRequest`, currently validated only for declared children of ordinary scroll containers. It cannot directly target an unrealized indexed row. Indexed collections already provide `FocusTarget` and `Enter` for explicit reveal-and-focus navigation.
- YouTube Music owns its playback queue locally, currently capped at 500. Playlist Play next already reads the playlist in provider order and inserts the leading songs immediately after the current occurrence. Its service interface exposes neither queue removal nor rating/library mutations. The existing ytmusicapi dependency provides authenticated `rate_song` and separate library-status operations; those are distinct actions, and dependency support is not an official Google API guarantee.
- Spotify currently caps its observed upcoming queue at 50. Currently playing is returned separately and is not a queue row. The widget exposes Add to queue for tracks but no saved-song action; library read/modify scope identifiers exist but are not requested by the current connection flow. The documented Web API exposes queue reading and appending tracks/episodes, not removing arbitrary queued occurrences, inserting next, or submitting a playlist as one queue addition.

### Requested implementation scope, when resumed

- [ ] On navigation into Queue, reveal the actual playing occurrence once, without selecting, activating or moving focus to the song. This is automatic entry behaviour, not a new Jump to playing button. For Spotify, clearly present the separate current track rather than labelling the first upcoming item as playing. Queue mutations, playback ticks/track changes, likes, refreshes, rerenders and overlay hide/reopen while already on Queue must not trigger another reveal. Leaving Queue and navigating back starts a new entry request.
- [ ] Use non-focus-moving reveal semantics, extending the existing shared `ScrollRevealRequest` contract narrowly for the existing indexed targets. Do not use `FocusTarget`/`Enter` as a scrolling workaround or introduce a music-specific API. Bind a pending reveal to the queue-entry request and current source; resolve the playing occurrence when initial data is ready, apply at most once, and cancel on leaving the page or subsequent user scrolling/navigation so late data cannot override their interaction. If no playing occurrence exists, leave the normal queue position unchanged. Retain the existing indexed list presentation; conversion to an ordinary materialized list is no longer part of this scope.
- [ ] Diagnose full-list refreshes and preserve occurrence identity, selection, scroll position and usable old content during updates. Fix YouTube Music's key churn; inspect Spotify's invalidation/loading transitions separately. Use existing content-only updates for metadata/playing/liked-state changes. Membership changes must still reject stale actions safely. Do not replace the list type solely because a cursor wrapper exists.
- [ ] Preserve existing queue limits and behaviour: YouTube Music stays at 500 and Spotify retains its current 50-item observation bound. The proposed 100-item limit is withdrawn; it was based on an assumed cursor queue. Do not change capacity, insertion/trimming, shuffle/original-order handling or unrelated library/search limits as part of the refresh fix. The separately requested removal, saving and entry-only reveal features remain in scope.
- [ ] Add Remove from queue for YouTube Music using occurrence identity, including repeated song IDs. Keep shuffled and original order consistent. Define current-track removal explicitly and verify next/end/repeat/empty-queue behaviour without accidental playback changes. For Spotify, do not offer a misleading local-only Remove action; retain this as provider-limited unless a documented supported operation becomes available. Replacing playback with a filtered snapshot is not equivalent and is outside this approval.
- [ ] Add YouTube Music Like/Unlike and Spotify Save/Remove from liked songs, with visible current state on the player and appropriate row/context actions. Distinguish YouTube Music liking from Add to library; do not treat them as interchangeable. Reuse existing authenticated adapters and verify pinned dependency/account support. Spotify needs the appropriate library read/modify consent and a clear reconnect path for existing accounts; avoid unrelated scopes.
- [ ] Use Spotify's current library save/remove/contains endpoints rather than building on deprecated saved-track APIs. Batch visible-track state checks where supported, cache per account, invalidate affected entries after writes, coalesce duplicate clicks and reject late results after account/song switches. Report success only after acknowledgement; preserve a usable state on failure.
- [ ] Ensure playlist Play next in YouTube Music uses the first playable songs in provider playlist order, within the existing 500-entry queue policy, rather than the visible page, current selection or a random subset. Preserve their relative order even when shuffle is enabled and clarify any truncation. Verify album handling too. Spotify's available operation is Add to queue, not guaranteed Play next; playlist expansion would require bounded individual additions and must not be presented as an atomic or insert-next API. Do not add bulk Spotify queue traffic without resolving that separate workflow.
- [ ] Test duplicate occurrences, removing before/after/current, natural track changes, shuffle/repeat, maximum-size queues, playlist beginnings, partial/empty playlists, slow/failing responses, cancellation, rapid repeated actions, account switches, hide/reopen and restart. Use deterministic service fakes for repetition; avoid live playlist/rating/queue mutations during investigation. Perform native visual and controller/mouse workflow checks before completion when implementation resumes.

### Agreed use of existing shared APIs

- [ ] Add whole-entry keyboard suggestions using recent searches and already-loaded music names locally. Do not introduce live provider autocomplete or extra service requests to populate suggestions. Keep suggestions secondary to the queue fixes.
- [ ] Add the supported Remove and Like/Save actions to the existing context menus; both widgets already have context actions. Do not create another menu mechanism.
- [ ] Reuse existing busy/disabled states while a mutation is pending and coalesce repeated submissions. Keep playback controls responsive during unrelated browsing/account writes. Update liked/saved state after acknowledgement; show a concise error with a retry path on failure. Prefer the visible state change over redundant success toasts. Spotify already uses queue-action toasts and YouTube Music has status text; improve consistency using existing facilities rather than introducing new host feedback APIs.

### Live testing authorization and service traffic

- The user authorizes using their normal profile for live testing of these changes when implementation resumes, including the music actions needed to verify the agreed workflows. This does not lift the current instruction to remain at investigation/planning until resumed.
- Use **WidgetRail as the Spotify playback device**. Verify the active target before playback/queue tests; do not use another Spotify Connect device as a fallback. If WidgetRail is unavailable, report the blocker rather than redirect playback elsewhere.
- Use Spotify and YouTube Music as a person would: deliberate browsing, playback and individual actions at normal pace. No rapid calls, automated request bursts, bulk live mutations, repeated refresh loops or service stress testing. Account for background polling and automatic refreshes as well as explicit actions. Use cached data and coalesced refreshes; respect rate-limit/retry responses and stop live retries on throttling or authentication challenges.
- Keep rapid-input, race, failure and maximum-size tests in deterministic fakes/local fixtures. Use a small number of representative live checks for real integration and native controller/mouse workflows. Do not run scripted live mutations merely to populate a maximum-size test queue. Record which checks were simulated versus live.
- Verify the original liked/saved state before a live mutation and restore any test-only library changes afterward. Any renewed login or additional consent must use the service's normal user-facing flow; never request credentials in chat.

Music-specific use of shared rich previews remains an opportunity to evaluate after that capability exists, not an additional approved deliverable or dependency of the queue fixes. Do not build a music-only popover.

Read-only references checked: [Spotify queue](https://developer.spotify.com/documentation/web-api/reference/get-queue), [add to queue](https://developer.spotify.com/documentation/web-api/reference/add-to-queue), [save library items](https://developer.spotify.com/documentation/web-api/reference/save-library-items), [check saved state](https://developer.spotify.com/documentation/web-api/reference/check-library-contains), and [ytmusicapi library operations](https://github.com/sigma67/ytmusicapi/blob/main/ytmusicapi/mixins/library.py). Recheck provider/dependency compatibility when implementing.

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
