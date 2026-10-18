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

## Cumulative candidate — 2026-10-18 10:56 PDT

Published from clean source commit `7a28e2b4`; subsequent `2ae51e0e` only reorders
the execution plan to prioritize host prerequisites. Full WinUI payload publication
succeeded, including native components, host services, bundled widget seals and
runtime verification. Candidate: `artifacts/pob/cumulative-candidate-20261018`.
The normal-profile frontend was launched hidden, without test-profile or validation
flags, PID 60648 at this checkpoint. Exactly one frontend remained running and
responding. `live-payload.json` verifies all **138 deployed DLLs** against the
published build. Recheck process identity before future UI work.

Installed through the normal reviewed CLI path, with saved widget settings retained:

- Spotify `0.3.90.1` (compact heart, busy focus, local suggestions and saved songs).
- YouTube Music `0.3.43.1` (queue removal/continuity, likes and local suggestions).
- PoE1 and PoE2 `0.1.4.3` (shared boundaries, sizing, configuration and tag search;
  PoE1 now uses its tested beta pin).

These are local candidate package versions, not published releases. No merge/push
or application installer was executed. All four selected packages are enabled.
Live account operations and physical controller acceptance remain pending.

Packaging used the existing `artifacts/pob/release36-python` environment (Pillow
12.3.0 and zstandard 0.25.0). The PoE2 checkout lacked its pinned runtime DLL; it was
restored from the exact pinned upstream URL and checked against Git object hash
`ccdf3c85c0a2a62b6c4140e145a5582e98f93d30`. Upstream source was not patched. Failed
attempt logs remain alongside successful `poe1-final` and `poe2-final` packages.

Next execution phase: shared anchored preview/popover contracts and implementation,
scroll-edge cues, generic native input/lifecycle/bounds checks, then dependent PoB
adoption. Existing passive `PresentOnFocus`/`FocusPresentationSurface` and modal
scope/restore machinery are the foundations; no duplicate temporary-page system.

## Shared preview and scroll foundations — 2026-10-18

Protocol 92 adds optional anchored geometry to existing `WidgetModal` and optional
settled-focus/hover previews to existing `FocusPresentationSurface`. Interactive
inspection retains modal input scope, Back/Close and opener restoration. Passive
previews consume already-loaded fragments, have no input or focus ownership, and
use one settling timer with no render polling. Source changes, input handoff,
modal entry, suspension, removal and compositor motion revoke passive paint.

Native checks found and corrected a shared focus race: native Focus can complete
before routed GotFocus, so scope transitions now capture the actual opener before
mutating controls. Anchored sizing also respects authored minimum dimensions.
The initial scroll-cue wrapper disturbed focus; it was replaced with reserved
template rows that never reparent native content. A shared content-viewport bounds
helper keeps reveal, reading visibility, focus settling and viewport memory aligned
with the actual native content origin, including existing padding.

Evidence:

- `anchored-native06`: 136 assertions across Mono Ink/Mineral Light and 900/420-DIP
  widgets, ordinary/anchored repetition, removed opener, minimums, resize and resume.
- `focus-previews-native06`: 32 assertions including settling during updates,
  busy focus, simulated hover target without selection, native text/chrome contrast,
  compositor resize, input/lifecycle suspension and removal. This fixture calls
  the same hover-target handler; it does not claim physical pointer/controller input.
- `scroll-cues-native04`: 22 assertions for fixed 40×2-DIP lines, reserved strips,
  DPI rounding, native focus/no-unload, reload, nested scrollers, virtualized ListView,
  light/high-contrast ink and padded reveal. No service requests or content fetching.
- `scroll-cues-reading-focus01`: 42 assertions; `scroll-cues-directional01`: 93;
  `scroll-cues-indexed-reveal02`: 10; `scroll-cues-indexed-continuity01`: 16.
- SDK anchored/passive contract groups: 1 each. Geometry/timing/cue policies: 9.
  API compatibility/determinism: 2. Managed/native generated contract parity passes.
- Debug builds pass without warnings. Representative native dark/light captures
  were inspected; a missing shell palette in the passive fixture was corrected,
  and native text/chrome contrast is now asserted.

The passive hover timeout was fixture clipping: the fixture placed its second
source beyond the native window. Sizing the test window fixed it without relaxing
the host's offscreen-anchor rejection. Earlier failed evidence remains available.
Temporary traces are bounded fixture artifacts; no normal-session logging added.

`indexed-previews-native06` adds 14 passing checks against the real local worker:
exact row identity, wrong source/key/generation/index rejection, no extra fetch or
scroll/focus changes, hover with toolbar focus, recycling and query replacement.
Native row focus can precede visible layout; the host now retries semantic source
changes on LayoutUpdated without polling. A delayed routed GotFocus cannot override
a newer pointer choice captured after native focus already changed. The indexed
fixture uses a separate overlay root so portals cannot inflate its toolbar Auto row.

Exact indexed-anchor/passive lease replacement qualification is complete.
High-contrast passive content, physical input and the cumulative app-wide walk
remain unverified. PoB adoption has not started. The running hidden normal-profile
candidate still uses the earlier published payload; these changes are not yet
deployed to it. Native fixtures restore that candidate after each run.

## Bare/Production and ordinary optional widgets — 2026-10-18

`9106d4bc` implements bounded one-time seeding of ordinary sandboxed packages from
the application bundle. Receipts preserve uninstall and existing selections;
preinstallation gives no first-party authority or capability grants. The protected
Wi-Fi password path now rechecks package/content-scoped read/connect consent before
using the secret. Settings calls optional packages Widgets; only Settings/Catalogue
remain built-in. Catalogue inventory admits 32 entries while each update batch stays
at 16. Metadata/review payload bounds remain separate and enforced.

188 focused checks pass: Catalog 49, Settings 93, Catalogue service 31, Catalogue
widget 11, initial bundle 1, protected Wi-Fi 3. Source builds report no warnings.
No user package catalogue or permissions were changed by these fixture checks.

Bare/Production packaging now ships two core widgets in both bundles. Production
includes nine optional archives plus a bounded hash index; Bare has an empty index.
Both retain shared runtimes/services and CLI. Developer shipping edition is removed.
App updater bundle selection and persisted plans support Bare: all 13 update tests
pass. ReleaseVerifier builds. PowerShell packaging selftests, deterministic archive,
tamper/path/reparse checks, inert handoff scenarios for both bundles and Bare Inno
syntax compilation pass. No real installer ran.

Fresh assembled-payload qualification remains required. An attempt to reuse the
older cumulative publication stopped because its Developer staging has no new
`supplemental-widgets.json`; no compatibility workaround was added. Use a fresh
publication with the new Supplemental layout for final structural/native acceptance.
Actual first launch, permission onboarding, remove/restart/update, size/startup
measurements, Catalogue asset publication and final documentation audit remain open.

## Gem candidate inspection — 2026-10-18

Shared passive previews and explicit X/context-menu Inspect now serve gem candidates.
A retains immediate selection; inspection's explicit Add/Replace uses the existing
edit path. Bounded typed upstream metadata is parsed once per catalogue. Inspection
is fenced by query, build/document, group/target, filters, sort and catalogue revision;
focus/inspection performs no engine or network work.

Real-engine skill-management checks pass: PoE1 68/68, PoE2 70/70. They cover indexed
A/X routing, preview non-mutation, stale actions and explicit Add/Replace. Evidence:
`artifacts/pob/gem-inspection-poe1-02.log` and `gem-inspection-poe2.log`.

`artifacts/pob/gem-inspection-native02/native-result.json` passes 56 native assertions
across both games, Mono Ink/Mineral Light and 1320x900/820x600 layouts. Actual exported
rows/modal content and package styles are used; indexed demand is materialized for
presentation testing, with real indexed routing qualified separately. Focus return,
passive non-mutation, modal bounds, horizontal overflow, scroll reachability and A
selection after closing pass. Representative dark/light captures were inspected.
An initial fixture failure used different export widget identities for picker and
modal; normalizing that test-only identity fixed the fixture without changing host
focus restoration. This does not establish full live-session or physical acceptance.

The normal-profile hidden candidate was restored (PID 50256 at this checkpoint).
It still uses the earlier publication; fresh cumulative publication remains required.

## Packaged web application artwork budgets — 2026-10-18

Current tree measurements exceed the embedded-media adapter's 512 KiB aggregate:
PoE1 1,026,731 bytes / 15 resources; PoE2 926,203 bytes / 14 resources. Packaged web
sessions now have a separate bounded contract: 32 resources, 256 KiB per resource,
2 MiB aggregate. Legacy embedded-media limits remain 16 / 256 KiB / 512 KiB.
Both sealed bridge resolution and native browser admission enforce their respective
limits. Inventory, SHA256, path and reparse checks remain in the shared resolver.
The registry still takes one native session per batch and caps live sessions at eight;
encoded package resources fit the existing 12 MiB bridge frame without raising it.

Focused sealed-asset checks pass at the exact aggregate/count boundary and reject
count overflow, aggregate+1, per-file+1 and same-length tampering. The existing two
provider-neutral media adapters also pass unchanged. Release bridge and Debug
frontend builds pass with no warnings. Tree decoded cache/raster measurements and
native all-version qualification are tracked with the artwork implementation.

## Display Controls — 2026-10-18

Display Profiles is now presented as Display Controls while retaining its package
identity. The reusable SDK/broker exposes host-enumerated rational resolution/refresh
modes and HDR capability/state. Both changes reuse the independent timed Keep/Revert
guard. Saved profiles retain their earlier meaning; no implicit HDR migration.
Unsupported HDR APIs, cloned sources and unsafe irregular resize layouts return
clear unavailability. Adjacent rows/columns resize around the primary origin while
preserving other displays' modes; refresh-only changes never move displays.

Review found and fixed late-completion busy ownership, notification refresh replacing
an uncommitted selection, mutable request rereads, and overlapping rectangles after
resolution growth. Additional empty/loading-state checks caught an initial focus
request for a missing monitor selector; focus now names an available control.
25/25 deterministic checks pass in `display-controls-tests06.log`, including guard
rollback/disconnect, exact 59.94 vs 60, draft preservation and lifecycle races.
Broker authority/vocabulary 4/4 and extended display timeout 1/1 passed earlier.
Public API baseline reviewed; the two compatibility/baseline tests pass.

The read-only native probe recognized 103 modes on Odyssey G93SC and 36 on P27h-20,
including each current mode; HDR was available only on the former. No display writes
were made. `display-controls-native03/native-result.json` passes 124 native checks:
selectors, cancellation, busy focus, duplicate admission, explicit navigation during
completion, Keep/Revert entry/return, scrolling and bounds at 780x700/640x420 in Mono
Ink and Mineral Light. Representative crops were inspected. Fixture corrections used
production snapshot serialization and distinct restore scopes for each transaction;
replaying the same restore scope correctly recalled its prior focus.

Native checks use real widget declarations and local action completions. Actual
hardware Apply/Keep/Revert and the integrated cumulative candidate remain unverified.
Brightness/DDC, dimmer overlays and vendor color controls are not falsely exposed.

## Inventory and unique-library inspection — 2026-10-18

Inventory and unique candidate lists now use shared passive previews and local X /
context-menu inspection. Focus reads only cached catalog facts; full details load on
explicit Inspect. Inspection preserves list/query/selection and fences build, catalog,
query and action tokens. Inventory offers upstream-eligible slot selection and explicit
Equip. Unique inspection opens the cached recoverable draft only on explicit choice.
A retains its prior selection behavior. A gem query also retires on an equal-valued
replacement result object, matching its identity-based action admission.

Real-engine item suites: PoE1 81/81, PoE2 79/79. Gem suites after identity hardening:
PoE1 69/69, PoE2 71/71. Evidence: `item-inspection-poe1-03.log`,
`item-inspection-poe2.log`, `gem-identity-poe1.log`, `gem-identity-poe2.log`.

`item-inspection-native04/native-result.json`: 184 native checks across both games,
both candidate types, Mono Ink/Mineral Light and 1320x900/820x600. Checks include
passive focus/non-mutation, anchored bounds, scrolling, explicit equip reachability,
slot dropdown cancellation/confirmation, Close restoration and subsequent A action.
Representative dark inventory and light unique captures were inspected. These use
actual exported content with demanded rows materialized; they complement real indexed
and engine checks and do not establish full live-session or physical acceptance.
The fixture now waits for actual option focus before sending A and for opener focus
after dismissal; confirming even the current option intentionally dispatches once.

Pre-commit review caught invalid dot-separated indexed child ID segments and corrected
them to stable hyphenated segments before successful engine/native verification.
No normal-profile or live-service changes were needed.

## Upstream tree artwork and bounded rendering — 2026-10-18

The exporter and adapter now supply class/ascendancy backgrounds, frame and socket
state, and static jewel-radius metadata from upstream. Alternative radius centres,
occupied sockets, half backgrounds, proxies and historical shared-asset locations
are covered. No maintained passive-effect calculation rules or animated jewel
textures were introduced. Source/version changes retire late image responses.

All 46 supported tree-version resource audits pass (41 PoE1, five PoE2). Current
full session resources are 1,052,829 bytes / 15 files for PoE1 and 927,030 / 14 for
PoE2. Largest files are 254,478 / 250,756 bytes. Current full decoded textures are
19 / 24 MiB, loaded lazily with a hard atlas ceiling of 52 MiB and release on tree
change. All-version generated assets are under `tree-art-rich-poe1-all` and
`tree-art-rich-poe2-all`; preserve these for cumulative packaging.

Real-engine tree/projection checks pass 10/10 (PoE1) and 11/11 (PoE2), including the
actual generated atlases. Separate upstream oracles resolve 2,557 / 4,361 frames and
411 / 25 backgrounds with no missing current references. Lua metadata and JS protocol,
late-image, coalescing, selection, forced-colour and idle regressions pass.

Native WebView checks pass 17 each in `tree-native-poe1-02/result.json` and
`tree-native-poe2-10/result.json`: local resources only, decoded images, bounded
accessible targets, selection, constrained layout, themes/forced colours, raster
sizes at 100/150/200%, and no idle redraw. Representative root/detail/light/high-
contrast crops were inspected. Visual review fixed faint high-contrast connectors
and node outlines. Small group backgrounds are omitted below a useful zoom threshold.

Before/after native drawing CPU samples (12 navigation frames, same fixture/viewport):

| Game / view | Previous p50 / p95 | New p50 / p95 |
| --- | --- | --- |
| PoE1 overview | 10.6 / 12.6 ms | 11.6 / 14.0 ms |
| PoE1 detail | 3.9 / 4.8 ms | 4.2 / 4.6 ms |
| PoE2 overview | 13.3 / 18.1 ms | 13.1 / 16.2 ms |
| PoE2 detail | 8.0 / 11.1 ms | 6.6 / 9.2 ms |

An initial PoE1 overview measured 18.5 ms p50 before the zoom threshold. These are
small drawing-callback CPU samples, not GPU frame latency or universal speedup claims.
Baseline evidence is `tree-native-baseline-poe1/poe2`; no parallel calculation engines
or idle animation loop was added.

The scale-only CDP experiment changes devicePixelRatio without delivering native
resize/resolution events. The final fixture distinguishes emulated rendering from
actual monitor placement. Both connected monitors are at 125%; a physical transition
between different DPIs remains unverified. The renderer observes device-pixel content
bounds plus coalesced viewport/resolution changes. Initial fixture setup also needed
indexed WinRT display enumeration and the existing WinUI environment creation API.
An explicit empty favicon avoids an unnecessary undeclared-resource request.

These are isolated renderer checks, not complete widget session/controller/couch
acceptance. Integrated tree edit/inspect/reopen workflows and fresh cumulative
package admission remain on the final acceptance list.

## Cumulative publication checkpoint — 2026-10-18

Fresh publication completed from clean commit `8bd04bd4`:
`artifacts/pob/cumulative-candidate-20261018-b/publication.json`.
Both assembled folders passed the actual release verifier and a post-verification
inventory/hash check under `assembled/0.1.0-preview.36`:

- Production: two core widgets and nine verified optional archives.
- Bare: two core widgets and no optional archives.

No installer, user package installation, capability grants, merge, push or release
occurred. This resolves the earlier stale Developer-staging structural blocker.
Actual isolated first-start/seeding, disable/uninstall/restart/re-update workflows
remain to be exercised. Rebuild/package the current PoB applications with the
already-generated all-version artwork before replacing normal-profile packages.

The last hidden normal-profile frontend restored here is PID 57904, still from
`cumulative-candidate-20261018` (earlier source `7a28e2b4`). It is deliberately not
presented as running the new inspection/tree changes. Verify PID/path live before
closing or replacing it. All native validation processes from this checkpoint have
been closed. Source worktree is `codex/pob-companion`; main remains untouched.

Remaining substantive plan work includes inline skill editing, actionable warning
navigation, unified search/direct navigation, read-only whole-build/loadout comparison,
fresh integrated packages, live music checks at human pace, actual guarded update and
display workflows, app-wide compact/high-contrast/scaling acceptance and final docs
reconciliation. The goal remains active; these commits are a checkpoint, not completion.

## Search, inline skills and whole-build comparison — 2026-10-18 continuation

`0689da36` implements a typed local search index with exact content destinations, whole-entry
suggestions and known-warning navigation. Catalog preparation happens on explicit
opening and is reused for an unchanged build/context. Search and navigation do not
edit the build. A section-animation wrapper initially prevented the focus decorator
from reaching gem/configuration controls; composition now happens before that wrapper.

Inline skill editing retains the group rail through gem selection and contextual
settings. Common controls share one scrolling editor so the fixed header cannot
consume the compact viewport. Option reads coalesce before entering the existing
engine, cache at most 32 groups for exact document/XML, and discard superseded
responses without repeatedly restarting the engine. Same-control edits retain focus.

Whole-build/loadout comparison uses one serialized read-only transaction, restores
the baseline on success and retains the baseline restart state after cancellation
or failure. Typed projections/cache are bounded; differences use the shared indexed
list, preserve upstream ordering and expose full read-only details. Compare stays
focusable during work and completion does not request a focus move. The view uses
the full planning dimensions. User/developer guides and Redux inspiration credits
describe the new workflows.

Latest real-engine runs (`visual-fix-poe*-*.log`) pass:

| Suite | PoE1 | PoE2 |
| --- | --- | --- |
| Skill management / gem inspection | 74 | 76 |
| Skill options | 35 | 24 |
| Whole-build comparison | 40 | 40 |
| Unified search / exact navigation | 37 | 37 |

Fresh/empty-build checks pass 23 per engine; item workflow checks pass 81/79
(`final-workflows-poe*-*.log`). They exposed the missing zero-match unique-list
focus target, fixed separately in `e6d0b2f4`. Native workflow validation passes
**272 assertions** across both games, Mono Ink/Mineral Light, and 1320×900 / 820×600
logical layouts at 125% desktop scale. It checks declared focus, modal bounds,
busy control identity, and full visibility inside actual content viewport bounds
after scrolling settles. Evidence: `workflow-native-20261018-e/native-result.json`.
Representative compact/dense and wide skill/search/comparison states were inspected;
fixed skill headers that consumed compact editor height were removed. Fixtures explicitly materialize
real exported rows for presentation; provider routing is qualified by the separate
engine/SDK checks. This is not physical controller or couch-distance acceptance.

The native fixture previously lowercased multiword roles such as `textEntry`,
bypassing standard theme styling. Its shared style helper now preserves bridge role
spelling and selected/disabled/busy semantics. Earlier fixture screenshots with
incorrect text-entry styling are superseded, not evidence of a production theme defect.

## Actual bundle first-start / package lifecycle — 2026-10-18 continuation

Using assembled `cumulative-candidate-20261018-b` folders and isolated profiles:
Bare contains exactly Settings/Catalogue and seeds no optional packages. Production
seeds all nine ordinary packages. Native Settings workflows disabled Capture Library,
canceled and confirmed Performance Monitor uninstall, restarted, and reinstalled
Performance Monitor from the bundled archive through the native file picker. Disabled
state and removal survived restart; explicit reinstall enabled the package. Cancel
returned focus to Uninstall. These operations did not affect the normal profile.

Catalogue Technical details correctly reports unload-after-idle, 120 seconds.
The first Bare process produced one UIA/pixel mismatch (current controls but an older
visible page). A same-profile restart and another fresh profile rendered correctly;
technical/back/technical navigation also rendered correctly. No production change
is claimed to fix this unreproduced observation. Evidence:
`cumulative-candidate-20261018-b/acceptance/distribution-native-result.json`.
Permission-request flows, installed update/rollback/re-update and normal-profile
integrated acceptance remain separate unfinished checks.
