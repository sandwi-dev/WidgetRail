# PoE widgets: developer documentation

[Project overview](README.md) | [User guide](USER_GUIDE.md) | [Screenshot capture guide](screenshots/README.md)

This document covers the implementation and its compatibility checks. Installation and everyday workflows are documented in the overview and user guide.

The [editor capability audit](../../docs/pob-editor-capability-audit.md) maps the
pinned upstream dialogs to delivered controls and records remaining omissions.

## Preview data and lifetime

Candidate rows publish preview keys through the shared `WidgetFocusPreviewSource`.
Settled focus resolves only the requested inventory/unique tooltip; gem, equipment
and skill-group previews use captured catalog/build data. The host retains at most
four current preview leases and renders only the active panel. Inventory keeps the
same rich formatter as full details. Equipped items and skill groups are read-only;
X/B entry and return apply only to interactive candidate previews.

Tooltip queue admission is cancellable. Once sent to PoB, the read completes under
a five-second deadline and widget lifetime so rapid navigation does not repeatedly
restart the engine. Canceled replies cannot become visible or actionable. See the
[shared contract](../../docs/developers/presentation-retention.md) for independent
content limits and context retirement.

## Architecture

Two controller-first widgets share one implementation and run pinned Path of Building Community calculation engines. PoE 1 and PoE 2 remain separate because their build formats and game data differ. Calculation rules remain upstream; the adapter translates editor actions and presentation.

The widgets are named **PoE1 Path of Building (Beta)** and **PoE2 Path of Building (Beta)**. The beta labels identify the pinned upstream beta engines recorded in `engine-lock.json`.

### Shared implementation boundaries

`PobGame` owns fixed identity, document roots, website endpoint/encoding contracts and game-file export support. Runtime availability comes from the engine's typed `PobEngineCapabilities`, not its game number. The Lua `engine-compatibility.lua` module contains upstream constructor conventions and feature probes. Eligibility and calculations stay in PoB.

`PobResult.Read` validates response envelopes and declared capabilities before the engine updates its remembered XML. Operation-only replies can omit build capabilities; malformed declared capabilities fail visibly. Package C# and the whole Engine adapter directory together.

`PobBuildLibrary` owns file discovery and metadata caching and publishes entries and metadata as one snapshot. UI search/filter state stays in `PobWidget`; the library performs no navigation or build mutations. Metadata cache entries are retired when their XML disappears.

`PobUnifiedSearchCatalog` owns typed, local search entries and destinations. The widget fetches inventory/calculation catalogs only on explicit Search opening, caches them for the exact result/document/calculation context, and performs no engine calls while typing. Destinations use existing indexed focus targets or a stable single-control remembered-focus group. The latter is composed before section transition wrappers, preserving animation and ordinary control identity. Retired queries and lifecycle epochs reject delayed navigation.

`PobBuildComparisonService` owns typed read-only projections and a cache capped at four comparisons and an estimated 8 MiB. Its key includes exact build bytes and engine identity. `PobEngine.ProjectReadOnlyAsync` holds the existing engine semaphore across baseline preparation, comparison and baseline restoration. Cancellation/failure retires the process while retaining the original restart baseline. No second engine, saved-file writes or Apply action is introduced. Difference rows use the shared indexed collection and preserve upstream display order.

`PobSkillOptionsState` owns selection, pending read, failure/retry and a cache of up to 32 groups for one exact document/XML. Typed skill, inventory-search and calculation-search readers reject malformed required data instead of presenting empty success. Inline skill-option reads coalesce for 100 ms and discard superseded responses. Once admitted, a read uses the active widget lifetime rather than the typing/navigation cancellation token: changing groups must not repeatedly retire the shared engine. Hide/unload still cancels active work. Explicit Retry bypasses a malformed snapshot; unavailable search categories are retried on reopening Search.

`PobInspectionContext` reads loadout and weapon context from the existing upstream snapshot/XML, cached for the exact result/group. It distinguishes the active calculation weapon set from the group's enabled weapon sets and omits missing optional context. Known warning destinations use supplied identities, never inferred warning text.

`PobDisclosureState` retains at most 24 recent presentation contexts per owner. Saved-inactive-setting toggles retire stale tokens on context changes, retain summary values, and return focus to the toggle. Search reveals matching retained configuration controls. Disclosure never mutates the build. Detailed calculations always show their available breakdown and source rows.

## Engine pins and PoE2 beta audit

The authoritative source pins are in [engine-lock.json](engine-lock.json). The audit below records the PoE2 beta used for the current implementation.

The PoE 2 package is pinned to upstream's **2026-10-16 beta**, manifest version `0.23.1-bb52d6b36`, commit `84a11f1295f67a25f434329197444438514983f1`. This is the upstream `beta` branch snapshot, not a new stable release. The engine includes the 0.5.5 data update; `Tree 0.5` names the tree family rather than a patch or engine release. Build & import shows the running engine version.

| Beta change | Companion handling |
| --- | --- |
| Game data, new uniques/runes, conversion, critical chance, mana cost, support-level and other calculation fixes | Inherited from the pinned engine; calculations are not reimplemented. |
| Per-skill weapon sets | Manage group exposes both sets and upstream restrictions for weapon-granted and Spirit-reserving skills. At least one set must remain enabled. |
| Grouped custom modifiers | Configuration → Custom modifiers supports group creation, renaming, enabling, deletion, clipboard replacement, and individual line editing. Unsupported lines remain visible. Long lists are virtualized. Edits use the normal durable Undo/Redo history. |
| Configuration comparisons | X on a boolean setting previews its toggle effect. Custom modifier groups also offer Compare toggle. These read-only comparisons do not save a change. |
| Martial Artist character rune slots | Items exposes the additional slots when Runic Meridians grants them. Searchable selectors use upstream eligibility and callbacks. |
| Item modifier toggles, new unique variants and jewel catalysts | Item settings exposes applicable upstream controls and individual modifier switches. Rune/variant choices use a searchable virtualized picker; X shows full rune details. Edits affect the underlying item shared by any sets that equip it, and support Undo/Redo. |
| Lazy gem ranking | The gem picker can rank by the upstream statistic choices, including Full DPS. Name sorting remains immediate; comparison sorting runs on demand with Cancel. Rankings follow PoB's support-first order and identify the main calculation skill. |
| Gem quality explanations | Quality effects opens the selected gem's upstream tooltip, preserving its descriptions and reference-quality labels. |
| Per-node author notes | Allocated nodes expose a saved note field. Notes survive XML export/import and normal idle unloading. |
| Named weapon-set point warnings and sidebar breakdowns | Existing warning and calculation-inspector projections receive the updated upstream content. |
| Build-list class filtering | Saved builds can filter by class and search class/ascendancy metadata as well as names. The managed library is flat, so recursive desktop folders do not apply. |
| Item filtering by loadout | Equipped items follow the chosen item set. Inventory lists unequipped items and identifies their use across sets. Desktop stash integration remains outside the widget. |
| Tree texture offsets | Artwork generation handles both legacy DDS array layers and cropped DDS coordinates. |

The [user guide](USER_GUIDE.md) covers the corresponding workflows, including set management, item drafting, gem and passive choices, trade handoff, and PoE2 game-file export. OAuth/account integration remains deferred. These additions are not a claim of complete desktop parity.

### Beta constructor compatibility

The beta also changes headless startup and class constructors. Its `copyTableSafe` can write into the original object while copying new parent proxies, causing repeated attribute-node restoration to consume gigabytes. `Engine/beta-compatibility.lua` provides a narrowly scoped deep-copy correction for the new constructor API: copy raw fields first, preserve class identity, and reconnect cloned parent proxies. Upstream source files and calculation modules remain unchanged. `Tests/BetaCopyRegression.lua` verifies source isolation and proxy ownership; reconsider this compatibility helper when upgrading the engine pin.

## Import compatibility and account access

Import validation preserves authored gems, including supports attached to item-granted skills, while allowing upstream to merge duplicate generated base-skill groups.

Direct poe.ninja URLs are rejected before any network request: its [API documentation](https://poe.ninja/docs/api) reserves character and PoB endpoints for internal use.

Account sign-in is not included: it requires an authorized GGG application registration for WidgetRail, separate from the upstream desktop client's identity.

## Presentation and input

Description emphasis also covers charge types, ailments, and mechanics such as Empower and Repeatable. Main-skill descriptions recognize multiword skill references from the pinned engine's own granted-effect names, including hidden effects; single-word names remain neutral to avoid ambiguous matches in prose. Upstream warning colors always take precedence. This formatting adds no links or gameplay behaviour.

Passive search results and saved builds use virtualized lists with stable row identities. Returning from a passive inspector restores the selected row. Notes, configuration sections and equipment-slot choices scroll continuously rather than using Next page actions.

Long lists offer contextual **L3** jumps to their surrounding controls and back to the remembered row: passive search, saved-build search/sort, set selectors, gem-group actions and expanded tree-preview actions. Configuration cycles through search/set controls, Show explanations and the section list. Ordinary lists qualify above eight rows; taller gem cards count as two visual rows. Short lists, absent set selectors and ordinary reading content do not add shortcuts, except for Notes' explicit jump to its navigation tab. These jumps preserve the list viewport, and the tree WebView keeps its own L3 toolbar action.

The six categorical color roles follow the selected WidgetRail theme; labels and geometry also convey meaning. The tree consumes the host's packaged-page theme projection, including high contrast. Remote websites receive no theme injection.

The tree renderer uses zoom-dependent detail without changing the host's interaction targets. Its visual checks use headless browser rendering; those measurements are not native overlay frame-rate guarantees. Node images, decorations, and resource budgets remain separate from controller hit testing. The renderer does not reproduce every desktop tree asset or overlay.

## Trade integration

League discovery is cached in memory for each widget instance. Equipment searches reuse the list; explicit Refresh leagues or a new instance after unloading/restart fetches it again. Failed requests do not replace the last successful list. There is no timed refresh or disk cache.

Empty-slot searches use a detached category reference rather than equipping a placeholder.

The page adapter observes only the declared trade-item JSON responses requested by normal browsing. Batch evaluation does not issue extra authenticated API requests. No session cookie or purchase token is copied into build data. **Return to listings** reuses the retained page or restores the saved public search URL after unloading. **Find seller's item** instead opens a seller-and-base search and attempts to locate the saved listing. Exact page scroll position is not retained. Basic page actions require protocol 86; batch evaluation additionally requires the host's `addBatchAction` API.

Ranking runs ten items per engine request and reuses the calculator and committed baseline. Prices use upstream conversion logic with GGG's public hourly Currency Exchange history, cached for 61 minutes. UI workflows and the meaning of price-adjusted ordering are covered in [the user guide](USER_GUIDE.md#evaluating-and-ranking-results).

## Engine lifecycle and storage

The Lua process starts on first calculation and does no periodic recalculation while idle. The widget unloads after 120 seconds of inactivity under the host residency policy. Calculation state and game data occupy hundreds of MiB; this is not a lightweight web page. Cancellation retires the calculation process, and the next command restores the displayed build before applying an edit. A crashed process is restarted on the next command using the caller's working snapshot. Timeouts show an error distinct from user cancellation. Explicit saved-build recovery takes precedence over cached engine state; a failed cached restore is cleared so it cannot repeatedly block that recovery.

Completed user actions atomically save the committed build, pending comparison or item draft, and Undo/Redo together in a versioned `.work.json` record. XML snapshots use the existing compressed PoB encoding; the record is bounded to 16 MiB. The ordinary working `.xml` remains a mirror for compatibility, while recovery treats the work record as authoritative if a process exit interrupted that mirror write. Existing XML-only builds remain readable. No shutdown callback or resident process is required to preserve completed work. Reopening recalculates with the current engine and restores only the context needed to finish pending work; ordinary tabs, search, focus, scroll and tree viewport are not persisted.

If pending work or history cannot restore, recovery tries the committed build in the record, then the saved XML. Before resetting the active record it preserves the exact failed bytes in a uniquely named `.recovery.*.json` file and shows a warning. If preservation or repair cannot finish, the recovered build remains viewable but read-only; Duplicate provides an independent editable copy. If neither build source can load, the widget reports the failure without replacing either source.

Default working copies live under `%LOCALAPPDATA%/WidgetRail/applications/pob1` and `pob2`. PoE1 includes the pinned compressed Timeless Jewel tables and upstream zlib runtime, loaded only when needed. Large engine archives are segmented to respect the per-file package bound and read without rebuilding a second archive in memory. Immutable engine archives are extracted to a digest-keyed cache under `%LOCALAPPDATA%/WidgetRail/pob-engines`. Extraction is bounded, cancellation-aware and staged before publication. `WIDGETRAIL_POB1_DATA`, `WIDGETRAIL_POB2_DATA` and `WIDGETRAIL_POB_CACHE` allow isolated development profiles. `WIDGETRAIL_POB_DATA` remains a common data-directory override when no per-game override is set.

## Building packages

`engine-lock.json` pins the PoB releases, LuaJIT and lua-utf8 source commits. `Prepare-Engines.ps1` fetches those exact sources and builds the x64 runtime using Visual Studio C++ tools. It writes `paths.json` containing the prepared source/runtime directories. `Build-CommunityPackage.ps1` accepts `-Game 1` or `-Game 2`, those directories, and a current `wrail` CLI path, producing a validated `.wrwidget` package in a fresh output directory.

Artwork preparation uses Python with the versions in `requirements-artwork.txt`; pass `-Python` when using a dedicated build environment. `Build-TreeArtwork.py` reads artwork directly from the pinned Git objects, converts upstream sprite metadata and DDS arrays into small browser atlases, and verifies the resource bounds for each tree. Only the open tree's assets enter its web session. Python and the conversion libraries are not runtime dependencies. Node artwork retains the upstream game's visual identity; surrounding controls, connectors, focus and preview markers follow the selected WidgetRail theme.

Install each resulting `.wrwidget` through **Settings → Widgets → Install local widget**. The packages use the full-access application runtime to launch their local Lua engine.

## Testing

The executable test harness accepts `game engine-directory runtime-directory fixture-directory`. Add `--work-state-only` for focused tests that destroy and recreate widgets and Lua processes, restore previews and history, exercise item drafts, and inject save failures. Use `--management-only` for recovery fallbacks, build-library operations and contextual inspectors. These tests use independent temporary working copies. XML comparisons preserve authored values and ordered lists while accounting for upstream map ordering and documented load defaults.

Use `--empty-build-only` with each engine to audit new and partially configured builds without opening the overlay. It validates rendered snapshots and materialized collection rows across all sections, empty/no-match searches, empty skill groups, calculation sections, custom modifiers, sets, empty equipment targets, plain crafted items, and deleting the final saved build. Inventory coverage includes adding/removing the first/last item and Undo/Redo; those regressions also run under `--items-only`. Trade setup uses a fixed league list and does not open a website.

Use `--comparison-only` to compare allocation, large-refund, item-upgrade, item-downgrade and minion summaries against a separate upstream desktop tooltip invocation. The oracle loads the before/after XML without loading the widget adapter. It also checks restored and unchanged comparisons.

Use `--loadouts-only` with a linked-loadout fixture to check independent copies, existing loadout preservation and configuration persistence. Use `--guide-workflow-only` with an XML exported from the guide walkthrough containing `Midgame` and `Late game` loadouts to compare their totals against a separate upstream calculation process and exercise damage labels, skill identity and item-slot eligibility. The UI walkthrough itself uses the native controls; these calculation checks do not replace that exercise.

`Tests/Test-LibraryFocus.ps1` checks native arrow navigation into and out of the virtualized library, including remembered rows and detail controls. Open Saved builds in a disposable candidate profile with at least two builds, then supply its process ID, main-window handle and a fresh evidence directory. It changes focus only and records each WinApp result.

## Engine update pipeline

The weekly/manual `pob-engine-candidates` workflow resolves an exact upstream revision (both games use beta by default), builds the pinned Lua runtime, and runs real-engine checks before producing a candidate package. Failed checks prevent packaging; structured results, logs and binlogs are retained for diagnosis. Candidate locks do not replace the repository lock or the installed engine. The workflow does not publish releases automatically: review the results, inspect both widgets, and promote a tested package deliberately. The bridge adapter is the compatibility boundary; game calculations remain upstream.

## Credits

- **Path of Building Community contributors**, building on the original Path of Building by **Openarl / David Gowor**: the [PoE1 engine](https://github.com/PathOfBuildingCommunity/PathOfBuilding) and [PoE2 engine](https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2) supply calculations, game rules, modifier parsing, trade-query generation and tree-editing tools. Their source revisions are pinned in [engine-lock.json](engine-lock.json); each package retains the upstream license and third-party notices.
- **[PoB Redux](https://github.com/pobredux/pob-redux/tree/a5afd8b80de993623c1fc5c1541269312361e50f)**: its gem search, configuration, skill editing, unified navigation and build-comparison workflows informed the corresponding controller workflows here. This is design inspiration, not copied Redux source or assets. `PobGemSearchCatalog` preserves PoB's supplied ordering and eligibility; `PobConfigurationCatalog` uses PoB's own default comparison rather than maintaining gameplay rules. Build comparison uses upstream calculation output through the existing single engine; rich previews are WidgetRail's shared API, proposed separately by the user.
- **Grinding Gear Games**: Path of Exile and Path of Exile 2, their game data and artwork, the official trade website, and the public Currency Exchange history used by PoB. Game artwork and website imagery in these screenshots are not original WidgetRail artwork and are not covered by WidgetRail's code license.
- **Ghost Activity**: the [Martial Artist guide and public PoB build](https://pobb.in/ttCJAV3WX9bn) used for the PoE2 walkthrough and planning screenshots. The widget demonstrates the imported build; it does not claim authorship of the build or recommend its historical gear for the current league.
- **LuaJIT**, by Mike Pall and contributors; **lua-utf8**, by Xavier Wang and contributors; and **zlib**, by Jean-loup Gailly and Mark Adler. Packages include `LuaJIT-COPYRIGHT`, `LuaUTF8-LICENSE`, and [zlib-LICENSE.txt](licenses/zlib-LICENSE.txt), alongside `PoB-LICENSE.md`.
- **[poe.ninja](https://poe.ninja/), [pob.cool](https://pob.cool/) and [pobb.in](https://pobb.in/)** provide the independent build-browsing, browser-planning and sharing services linked from the widget. Their website interfaces and services remain their own.

Fixtures used during development are not bundled as sample builds. WidgetRail's companion UI and adapters are separate from these projects; no affiliation or endorsement by their maintainers or Grinding Gear Games is implied.
