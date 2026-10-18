# PoB and shared UI improvement verification

Updated 2026-10-18. This ledger records implementation evidence, not final acceptance of the full plan.

Local commits: `2bbf0b6c` architecture boundaries; `41d1fe9a` workspace sizing; `317be39e` Catalogue rename/tests. Nothing merged, pushed or released.

## First implementation slice

| Area | Evidence | Still required |
| --- | --- | --- |
| Fixed game contracts | Central `PobGame`; endpoint, realm, export and document-root assertions | Beta naming follows the later PoE1 migration |
| Engine boundary | Validated response envelopes and typed capabilities; Lua constructor/availability helper; 24 local architecture assertions | Type subsequent workflow responses as those areas change |
| Saved-build discovery | Separate metadata/cache owner; atomic UI snapshot; recovery-only entries, malformed XML, removed files and cache invalidation covered | Unified-search integration later |
| PoE1 and PoE2 regressions | Each: management 39, sets 28, trade 5, currency ranking 11, empty builds 23 passed | Native integration after the UI changes are assembled |
| PoE2 planning workflow | 26 passed with the existing Monk guide fixture | No equivalent PoE1 test claimed; this fixture is explicitly PoE2-specific |
| Planning dimensions | Shared 1320 logical-pixel preferred width; available height; smaller management screens preserved; both engines' management assertions pass | Native bounds, modal, scaling and focus inspection |
| Catalogue rename | Preserved technical identities; presentation 8, service 16, Settings Catalogue workflow 1, SDK Gallery 13 passed | Native label fit, full flow review, release caching/rate-limit work |
| Rich previews | Existing `PresentOnFocus`, `FocusPresentationSurface`, source ownership and modal scopes audited | Anchored placement/activation and interactive extension |

PoB totals: **262 passing assertions** across the distinct scopes above, plus the focused Lua compatibility test. Catalogue-related totals: **38 passing assertions**. C# builds completed without warnings or errors. No GGG, Spotify or YouTube requests were made for these checks. No screenshots or native interaction were needed for this logic/refactoring slice; visual acceptance is explicitly pending.

Logs and uniquely named binlogs are under `artifacts/pob/architecture-*` and `artifacts/catalogue-*`. Engine revisions were verified against `samples/PobCompanion/engine-lock.json`: PoE1 `b32759ab0f31a1c8499a0d420cb0f0633d4fe478`; PoE2 `84a11f1295f67a25f434329197444438514983f1`.

Two initial planning-test invocations used unsuitable fixtures: PoE1 against a hard-coded PoE2 harness, then a Ranger fixture for assertions requiring the Monk's Runic Meridians. These were test-selection mistakes, not product fixes. The intended Monk fixture passed all 26 assertions. Logs are retained rather than overwritten.

## Shared native coverage queue

### Indexed scroll-only reveal (2026-10-18)

Implemented as an extension of `ScrollRevealRequest`, using the existing indexed occurrence identity and navigation/retention machinery. No second scroll API or music-specific host behavior. SDK indexed suites passed **16/16**. Native fixture passed **10/10** checks covering deep reveal with unchanged focus/selection, consumed requests, wrong keys, delayed payloads, manual scrolling, query replacement and hide/reopen. Evidence: `artifacts/pob/indexed-reveal-native01/result.json`. No screenshots or live service traffic were used. The normal-profile candidate was restored hidden afterward (PID 27588 at that time; recheck before acting).

The validation harness is a Debug build; a first attempt to enable its reflection-heavy fixtures in trimmed Release produced IL2026 diagnostics. Production Release built cleanly. No unrelated validation/CI code was changed to suppress these diagnostics. Widget adoption and queue-membership viewport continuity are still pending; this check only proves the scroll-only primitive.

Batch these checks into the integrated candidate pass; repeat only affected cases after fixes:

- PoB section transitions, planning/management size changes, modal bounds and hide/reopen focus.
- Dense, empty and partial builds; long names; representative small-display and TV scaling.
- Changed Catalogue/Settings labels, Back and focus restoration, install/review/cancel/update flows.
- Shared rich previews, once implemented, with a generic fixture and one PoB workflow.
- Light/dark/high-contrast appearance where changes affect rendering. Use geometry/accessibility automation broadly and focused screenshots for visual differences.

Actual controller ownership and couch-distance readability remain physical acceptance checks. Model/snapshot tests do not establish those results.

## Catalogue discovery, presentation and release checks (2026-10-18)

Implemented bounded host-lifetime release caching (15-minute freshness, 30-second manual-check minimum, conditional ETag/Last-Modified requests, five serial pages maximum), provider cooldowns without automatic retries, retained availability labelling, and one checksum-manifest read per URL per update batch. Package identity/hash review remains unchanged. Offline service tests: **31/31**; Catalogue presentation: **10/10**; Settings review workflow: **1/1**. No live GitHub stress requests or installs were used.

Catalogue now has fixed navigation/actions, responsive cards, semantic blue headings, gold setup labels, existing status badges, and full **Before you install** requirements. Requirements are bounded plain-text metadata, separate from permissions. Existing Back/selected-card restoration, explicit review and update-all actions are preserved. PoE1 discovery is labelled Beta. Unavailable release data is not labelled Available.

Native local-fixture evidence: `artifacts/pob/catalogue-native04/native-result.json` and `artifacts/pob/catalogue-native-light01/native-result.json`: **37 checks each**, using Neon Circuit and Mineral Light, 900/560 logical-pixel widths, 650 logical-pixel content height, current 125% display scale. Covered Browse, details, Installed, Updates, aggregate review and completed state. Checked initial focus, no horizontal scroll clipping, fixed header while scrolling, top-aligned different-height cards, explicit review approval and native Cancel dispatch. Cropped native renders inspected for wide Browse, narrow details/Updates and light Browse. The initial visual review caught vertically centered shorter cards; responsive-grid children now align at the top, with a native regression assertion.

This is representative native rendering evidence, not physical controller/couch acceptance or completion of the entire compact-layout audit. Empty/error states, broader shared responsive-grid consumers, final catalogue package integration, and remaining distribution terminology still require the integrated pass.

## PoB configuration and gem search implementation

Typed validated configuration metadata, changed-setting section counts/filter, and whole-entry local gem name/upstream-tag search are implemented. Real-engine checks: configuration **11/11 per game**, quick controls **117/117 per game**, architecture/model **30/30**; gem management/search **46/46 PoE1** and **48/48 PoE2**. Tag matching preserves upstream eligibility and ranking; it does not mutate build XML. Evidence: `artifacts/pob/configuration-*`, `gem-search-1-diagnostic.log`, `gem-search-2-final.log`. Redux workflow inspiration is credited in the widget README/developer guide; native workflow inspection remains pending.

## Indexed occurrence continuity correction

`artifacts/pob/indexed-continuity-native04/result.json`: **16/16** native checks pass, including three settled insert/remove/shuffle passes, focused removal, external focus, delayed content, native scroll interruption and empty replacement. The new interruption fixture reproduced a real stale pending-offset defect: after a target had settled, a later native offset change was ignored because the previous target was still marked pending. Clearing that completed marker allows reconciliation; user scrolling/departure still cancels restoration.

`artifacts/pob/indexed-reveal-native03/result.json`: **10/10** scroll-only reveal checks pass after the fixture waits for native navigation and geometry to settle before its independent top reset. Earlier native02 reported content replay while the previous native reveal could still be unwinding; no production reveal change was made. Diagnostic traces exist only in bounded validation output, not normal candidate logs.

## Music workflows and compact saved-song control

Spotify: 86/86 widget, 44/44 provider and 6/6 community-application tests pass.
YouTube Music: 38/38 C#, 39/39 Python and the JavaScript playback-race fixture pass.
No live service calls made. Spotify uses the current unified library endpoints,
batched saved-state reads and account-scoped bounded state. Suggestions are local.
YouTube Music keeps its 500-entry queue; Spotify's observation remains 50 and its
current track stays in the separate persistent player, not an invented queue row.

`artifacts/pob/music-native-heart03/native-result.json`: **121 native assertions**
(including five shared preflight checks) pass across main and compact pinned players
at preferred/minimum heights. Exact same save/like Button objects, native focus,
IsEnabled and tab eligibility survive busy/idle snapshots. Native controller-style
bring-into-view reaches transport after resizing. Visual inspection covers main
Spotify, its compact pin and the YouTube Music compact pin; final normalized assets
use the real SVG sanitizer. The sanitizer rejected an unsupported heart stroke join;
the asset was corrected rather than weakening validation.

Native checks found and fixed Spotify main-pane overflow and a compact-pin flex
rule preventing scrolling. Remaining: integrated queue/context-menu navigation,
account-authenticated deliberate save/like round trips (restore test mutations),
light/high-contrast music appearance and physical controller acceptance. Current
native player fixtures intentionally substitute collections and artwork locally.

## Routine controller-only updates

Supporting target installers advertise a fixed unattended preflight capability
under the verified installer hash. Unknown targets take the explicit interactive
fallback; they are never executed with speculative flags. Target prerequisites are
checked before graceful shutdown and again before setup. Silent paths preserve
startup/check choices and record warnings without custom blocking message boxes.
Failure/cancel/restart recovery launches only a verified installed payload. Installer
outcome notifications are distinguished from ordinary network-check failures so
routine failed background checks remain quiet.

12/12 service tests, 1/1 focused Settings flow, eight inert PowerShell handoff
scenarios and existing installer contract checks pass. Inno compiled using inert
payload fixtures. No real installation/UAC/settings mutation was performed. Real
isolated upgrade acceptance, native warning/result flows and post-install full
inventory-check timing remain open; Bare edition integration is pending.
