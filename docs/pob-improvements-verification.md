# PoB and shared UI improvement verification

Updated 2026-10-19. This ledger records implementation evidence, not final acceptance of the full plan.

## Compact modifier diff and browser return, 2026-10-19

User screenshots exposed two follow-ups. Back from an expanded comparison without
rankings explicitly hid the sidebar; the full-page browser then used automatic
interaction, so B left the modal instead of releasing the page to native controls.
Back now restores rankings when present, otherwise the comparison sidebar. Hide
panel is explicit, and both layouts use A-enter/B-leave interaction. Hidden
rankings have a visible restore button as well as the existing R3 shortcut.

Changes now presents compact two-column rich text without repeated status headings
or card backgrounds. Removed lines use red/−, added green/+, changed gold/↔ and
unchanged muted/=. Section metadata is inline, and missing sides have explicit text.
Symbols and accessible status descriptions preserve meaning without colour.
This is diff formatting only; Full items retains upstream colours and Build impact
retains upstream calculations. No stat sums or gameplay-value rules were added.

**231 workflow checks passed** (115 PoE1 / 116 PoE2), including unranked Back,
ranked Back, unchanged committed XML/page navigation, explicit hide/show,
browser interaction policy, duplicate modifiers and every diff colour/symbol.
Release build and both package/WRSS validations pass. Evidence:
`artifacts/pob/trade-diff-*-workflow.log`, `trade-diff-build.binlog`,
`trade-diff-packaging.log`. Both cumulative packages are **0.1.4.26** in
`trade-diff-packages-20261019`; .25 was staged but superseded before installation.
No GGG calls or new physical/native visual checks were performed. The user's
physical-test waiver remains; header routing is verified by the existing host
interaction contract and widget declarations, not a new gamepad walkthrough.

## Changes view alignment correction, 2026-10-19

The user's screenshot exposed centred modifier groups that did not line up with
the equipped/proposed headings. The row now explicitly stretches its value grid
across the available width, uses two equal columns and left-aligned text/status,
and top-aligns unequal-height cells. Repeated consecutive section labels such as
Explicit appear once per side; all original modifier occurrences and rich text
remain intact. Matching and calculation rules are unchanged.

The screenshot's +141 versus +161/+49 shape is now a fixture, including retained
rich text, grouped labels, mixed implicit/explicit types and an empty side.
Both trade workflow suites pass: **108 PoE1 / 109 PoE2 checks**. Release build and
both package/stylesheet validations pass. Evidence:
`artifacts/pob/trade-alignment-*-workflow.log`, `trade-alignment-build.binlog`,
`trade-alignment-packaging.log`. Packages **0.1.4.24** are under
`trade-alignment-packages-20261019`. The supplied screenshot was inspected;
no new physical or native visual check is claimed (the user's waiver remains).

## Trade modifier comparison and equipment transitions, 2026-10-19

Both games now use their pinned engine's `TradeHelpers.modLineTemplate`, upstream
variant selection and `itemLib.formatModLine` to align equipped/proposed modifier
groups. Duplicate occurrences are retained rather than overwritten by a map.
Changes are labelled added/removed/changed/unchanged, with no invented gameplay
valuation. Full items retains both rich descriptions; Build impact retains PoB's
calculation output and unsupported-effect warnings. Matching is included in the
existing comparison response, with no additional service request. An unavailable
matching helper falls back explicitly to the other views.

The website sidebar and expanded workspace share the comparison presentation.
Expanded mode keeps virtualized ranked candidates beside it, preserving candidate
focus while switching. Purchase details are collapsed. Back returns to ranked
results; Finish comparing returns to equipment and discards the preview. Only
Equip changes the saved build. Ranking target/weight/build changes reject stale
candidate selection. Explicit new searches, including Find similar, clear the
previous ranking session and saved ranking; Return to listings preserves it.

The Ring 2 → Helmet regression is covered with real engines and a fake web session:
the fresh search clears the old rows, ignores their late action, ranks against
Helmet, and leaves committed XML untouched. Recovery, manual-draft protection,
rich-row materialization, expanded row navigation, all three comparison views and
explicit equip/undo history are covered too. Old duplicated comparison controls,
the absent scroll-reveal target and its state/style were removed.

Validation: **323 checks passed**: trade workflows 103/104, comparison engine tests
19/19, Find similar 28/28 and rendering resilience 11/11 (PoE1/PoE2). Release build
passed without warnings. Evidence: `artifacts/pob/trade-review-*-workflow-qualified.log`,
`trade-comparison-*-final.log`, `trade-review-*-similar-final.log` and
`trade-review-*-render-resilience-final.log`. Packaging validation also checks WRSS;
an unsupported border shorthand was caught and replaced before delivery.

Candidate packages: **0.1.4.23** for both games, under
`artifacts/pob/trade-review-packages-20261019-final`. Existing upstream archives,
runtime and web assets are retained by hash. No host changes, GGG requests,
purchases or seller messages were needed for these checks. Physical checks were
waived by the user; this entry does not claim native visual/controller acceptance.

Both packages installed enabled using the existing user settings. The cumulative
candidate was relaunched hidden with the normal profile and controller input
enabled, PID 4420. Evidence: `trade-review-install-1.log`,
`trade-review-install-2.log`, `trade-review-launch.json`. No merge/push/release.

## Build toolbar and search destination focus, 2026-10-19

Undo/Redo no longer declare a nested remembered-focus group inside the shared
build-controls row. History actions return to `pob.buildtools`; if the just-used
history action becomes disabled, the other available history action is the
group's fallback. The existing passive previews and R3 return shortcut remain.

The equipped-item search warning was a focus-decoration failure, not a missing
item. `WithUnifiedNavigationTarget` recognised concrete control types but missed
SDK decorators used for deferred rich previews. Navigation reached the item, then
failed to find its focus target and emitted the stale-result warning. The helper
now matches the explicitly authored target control ID through those wrappers.
Build/cache/query/slot identity checks are unchanged; no stale authority was relaxed.

Validation: 120 quick-control, 12 history-workflow and 83 unified-search checks
passed for each game (430 total). An isolated copy of the user's current PoE2
working XML reproduced the equipment focus failure before correction; the expanded
169-check suite now passes, including every named equipment result's actual focus
handoff and absence of a warning. Total: 599 checks. User build files were read
only. The old history test was reconciled with the already-accepted saved-tab
restoration behavior; no navigation-persistence behavior changed here.

Evidence: `artifacts/pob/history-toolbar-*`, `search-focus-*`,
`equipment-search-user-before.log` and `equipment-search-user-fixed.log`. Both
0.1.4.22 packages validate and retain the qualified upstream/web assets by hash.
The existing corrected host is reused; no host API changes or physical UI checks.

## Suggestion cancellation bridge failure, 2026-10-19

The user's second YouTube Music suggestion update exposed a host cancellation
boundary missed by the prior tests. Logs show the bridge retiring both workers
and the frontend subsequently reporting a closed presentation transport. A new
test using a real worker, named-pipe bridge and presentation session reproduces
that failure: first suggestion succeeds; cancel an admitted delayed request;
the following widget-list request fails with a closed transport.

The registry now translates suggestion-local `OperationCanceledException` into
a request-level protocol error, matching the dispatcher contract. Previously the
dispatcher treated it as fatal because the bridge session itself was not cancelled.
Actual session cancellation still propagates. No widget workaround, global
cancellation suppression or extra production diagnostics were added.

The same test now passes three cancellation/requery/render cycles with one worker.
Registry checks cover private timeouts, session shutdown, admission recovery,
stale owners and providers ignoring cancellation. Both suggestion scenarios and
all four request-dispatcher scenarios pass. Prior widget/native tests did not
exercise this complete cancellation path, so they did not establish this guarantee.

Evidence: `artifacts/pob/suggestion-cancel-before.log` (reproduced transport loss),
`suggestion-cancel-qualified.log`, `suggestion-cancel-dispatcher.log` and
`suggestion-cancel-publication.log`. Corrected cumulative host is staged at
`artifacts/pob/cumulative-candidate-20261019-suggestion-cancel`; YouTube Music
remains 0.3.43.7. Production publication and cleanup verification passed; all
138 deployed frontend DLLs match. The corrected candidate is running hidden on
the normal profile. Physical checks remain with the user as requested.

## Expanded completion and YouTube Music suggestions, 2026-10-19

Offline completion now uses all 82,765 supported words from the pinned SymSpell
frequency dictionary. The embedded text is 752,705 bytes (663,755 bytes more than
the previous vocabulary); compressed installer delta was not measured. A pinned
regeneration script replaces the arbitrary 10,000-word truncation. Index construction
took 16.04 ms and allocated 14,024,232 bytes transiently in the test process;
10,000 mixed lookups took 37.10 ms. This is a focused measurement, not a startup
or retained-memory benchmark. Loading remains lazy and off the UI thread.

URL/email fields and the browser address/search draft allow word completion while
keeping their layouts. The alphabetic fragment at the caret is replaced only on
explicit selection; dots, slashes, underscores and address suffixes are preserved.
Sensitive/numeric fields, explicit Ordinary opt-out and live webpage editing keep
their previous policy. Widget-provided suggestions retain precedence.

YouTube Music now uses `get_search_suggestions`, preserving provider order and
whole-entry semantics. Shared host settings supply two-character minimum, 500 ms
debounce, 1,000 ms spacing and 4-second timeout. Package-side safeguards supply a
3-second fallback deadline, one in-flight provider call, five-minute/64-query
memory cache, 30-second failure backoff, and account-generation checks. Existing
loaded titles/recent searches remain fallback data. No additional host API was
needed for music suggestions. Exact remote duplicates are removed case-insensitively.

Checks: 65 shell/keyboard logic tests, 197 native keyboard checks, 41 music C#
checks and 46 offline Python service checks passed (349 total). Covered punctuation,
caret placement, no submission on completion, provider precedence/lifecycle,
cache expiry/bounds, cooldown, overlapping calls, cancellation and account changes.
Production host publication and its cleanup verifier passed; all 138 deployed
frontend DLLs match. Canonical YouTube Music package 0.3.43.7 passes all nine
required asset checks and is installed/enabled with the existing profile retained.

Candidate: `artifacts/pob/cumulative-candidate-20261019-address-completion`.
Evidence: `address-completion-native.json`, `address-completion-deployed.json`,
`music-suggestions-final-tests.log`, `music-suggestions-python-qualified.log`,
`music-suggestions-qualified-package.log`, and `TestResults/expanded-completion.trx`.
The final review found ytmusicapi injecting a 30-second timeout into supplied
sessions; suggestion requests now explicitly override it, covered by an offline
test. Other provider requests retain their existing timeout behavior.
The user stopped further installed/physical checks before the browser/music
walkthrough; no live suggestion endpoint call or physical-controller acceptance
is claimed. Candidate left hidden on the normal profile. No merge/push/release.

## Default completion and build-name consistency, 2026-10-19

Reproduced the reported missing `sa` suggestions in Saved builds. Its Build name
field and the PoE2 export field omitted Prose, unlike the identically labelled
current-build editor. Both failed the new declaration checks before correction;
all three now share one build-name field builder.

Following the user's requested default, omitted input kinds now allow offline
English completion. Explicit Ordinary opts out. The SDK preserves that distinction
through its existing public property and view serialization; no new public member
or protocol enum was needed. Widget suggestions retain priority. Live browser
editors and sensitive/specialised fields remain excluded.

Audited all authored widget TextEntry calls and host-created fields. PoB numbers
use numeric kinds; mixed import links/paths, raw modifier syntax and export folders
opt out. Spotify's public client ID opts out; YouTube and SDK Gallery media links
and host webapp addresses use URL. Wi-Fi password entry now uses SensitiveTextEntry
(the audit found it was previously ordinary). Existing untyped names/search fields
and Game Help questions receive completion from the updated host without a new
Game Help installation. No dictionary queries reach external services.

Checks: SDK 166/166, unchanged public API baseline 1/1, Network Controls 31/31,
native keyboard 191/191 including controller-typed `sa`, default/opt-out behavior,
existing widget suggestions and sensitive fields. PoB management, configuration,
render resilience, skills and trade menus: 217 PoE1 and 186 PoE2. The resilience
run also exposed an unsaved-build selector with no selected option; it now selects
the All saved builds fallback. Temporary diagnostic state was removed.

Installed walkthrough passed: repeated space, `s`, `a` in Saved builds and saw
said/same/save/sale/sales; the unspecified loadout-name field also offered the same
completions. Cancel preserved the saved build name and created no loadout.
Candidate `cumulative-candidate-20261019-default-completion` is running hidden
under the normal profile (PID 22256); all 138 deployed DLLs match publication.
Installed/enabled packages: PoE1/PoE2 0.1.4.21, Spotify 0.3.90.3, YouTube Video
0.3.43.1 and Network Controls 0.1.6. Physical controller acceptance remains
separate from the native controller-route checks and mouse-driven key walkthrough.
No merge/release, service searches, playback changes or credential edits were made.
Evidence is under `artifacts/pob/`:
`build-name-completion-before.log`, `default-completion-native.json`,
`default-completion-sdk.log`, `default-completion-network-tests.log`,
`default-completion-{1,2}--*-only-final.log`.

## Find similar, recent builds and keyboard, 2026-10-19

Implementation uses protocol 96 and each pinned PoB engine's Buy similar query
builder. The reference item never replaces equipped gear or modifies Undo history.
The header offers up to 20 cached recent builds plus All saved builds, using the
existing saved-work recovery path. In-progress comparisons prevent switching.

Keyboard holds begin with characters and switch to words after four repeats and
one second; protected fields remain character-based. Menu/Space and View/Paste
act on release and cancel for conflicting buttons, lost ownership or View+Menu.
Optional input purposes provide number/address layouts and offline English prose
completion; widget suggestions retain whole-field semantics and precedence.

Validation: 36 focused keyboard/suggestion tests; 189 native text-entry checks;
33 native browser-editing checks (including a field becoming protected during
queued input); SDK public API baseline 1/1; Game Help source build. Real engines:
Find similar/workflow 28 per game, build comparison 60 per game, item authoring
94 PoE1 and 92 PoE2. Additional previous SDK and navigation/trade runs remain
recorded in the candidate artifacts. Native checks found and fixed a missing
embedded dictionary and a caret/acceptance race before candidate delivery.

Installed walkthrough: switched from Twister to the empty Ranger and back;
compared the Ranger against the Twister guide; opened its helmet's Find similar
form; changed a defence bound with the decimal keyboard; ran one live trade search
and compared one returned listing against the empty helmet slot; finished without
equipping. Inventory unique Skysliver opened upstream filters and Back restored
the inventory opener. Empty PoE1 inventory remained usable. Prose completion
accepted a word without submitting, and Cancel preserved the saved build name.
The numeric utility row was tightened and its unavailable Shift hint removed after
visual inspection, then qualified by the final native run. No GGG requests were
needed for engine/native fixtures; no sellers were contacted.

Final host: `artifacts/pob/cumulative-candidate-20261019-similar-keyboard-final/`.
PoE1/PoE2 0.1.4.20 are installed and enabled. Final PID 14840 started hidden with
the normal profile and connected Guide polling. All 138 deployed DLLs match the
qualified publication. The original Twister build is selected. Evidence:
`keyboard-native-final-layout.json`, `keyboard-web-editing-final.json`,
`similar-keyboard-final-deployed-dlls.json`, `similar-keyboard-installed.json`,
`cumulative-{1,2}--{similar,build-comparison,items}-only-final.log` under
`artifacts/pob/`. Hardware controller acceptance remains separate from the native
controller-route tests and live mouse/keyboard walkthrough. No merge or release.

## On-demand preview sources, 2026-10-19

Protocol 95 adds a shared on-demand source to existing anchored previews. Rows
publish lightweight keys. Settled focus resolves one complete fragment through
existing semantic lease transport; only the active panel gets native controls.
One request and a four-entry lease cache are bounded independently of row batches.
The resolved fragment has a 256-node bound; normal views and inline previews keep
their existing bounds. Publishing context, changing scope, suspension and disposal
retire content; cancellation and late replies cannot retarget the current panel.

PoB inventory/unique rows no longer construct or fetch previews in 64-row ranges,
and the old 128-entry widget metadata cache and plain-text cutoff are removed.
The full item preview uses the same rich formatter as the detail panel. Gem
previews use existing catalog data. Equipped items and skill groups have compact
read-only summaries; neither selects nor edits the build. Green custom modifier
runs remain visible alongside ordinary modifiers. Empty and generated groups are
covered. Undo/Redo keeps its inexpensive inline preview.

Tooltip requests can cancel before engine admission. An admitted read drains its
reply within a five-second deadline using the widget lifetime, avoiding a cold
engine restart when focus changes; the canceled preview result is discarded.
Build-changing operations retain their existing interruption behavior.

Validation: SDK 166/166; API baseline 1/1; real worker indexed regression 1/1;
real worker native preview lifecycle 12 checks; readonly native theme/size matrix
96, inventory/unique matrix 256, gem matrix 128. Both engines passed 13 summary
checks, item suites (94 PoE1 / 92 PoE2) and skill suites (70 / 72). Representative native screenshots were
reviewed. Tests use local fixtures with no live service requests. Artifacts are
`artifacts/pob/deferred-*`. Physical controller acceptance remains separate.

Delivery: source `84068d4d` was published cleanly in
`cumulative-candidate-20261019-deferred-previews`. Both PoB packages 0.1.4.19 are
installed and enabled. The cumulative normal-profile candidate is running hidden
as PID 10084 with its controller backend enabled. All 138 deployed frontend DLLs
match the qualified publication; selected installed widget/SDK/protocol/style
files match their packages. Spotify and YouTube Music retain their selected
versions and enabled state. No merge, push or release was performed.

## Complete rich previews and navigation restoration, 2026-10-19

Protocol 94 now places complete interactive content inside `PresentOnFocus`.
`FocusPreviewOptions.EnterButton` is a host-owned focus transfer, not a widget
shortcut. X enters the existing controls; B returns to the source while the same
panel remains visible. Nested selectors consume B first. Keyboard F6/Escape,
pointer and automation entry use the same controls. Entry does not publish a
snapshot, resize the panel, reset reading position or call the engine.
The rejected modal wrapper, separate PoB inspection state and eager three-line
unique-library summaries have been removed. Ordinary modals and passive
Undo/Redo previews remain separate.

The bounded-batch tooltip cache described in the initial implementation has been
replaced by protocol 95 on-demand sources (see the current verification entry
below). The earlier batch timings do not describe the current design.

Preview commands retain original displayed-frame authority and the exact indexed
lease, item key, owner and declared action. Tests reject forged owners/actions,
disabled/busy options, retired queries, copied frames and late admission after
source removal. Native tests also cover unchanged controls/geometry on X/B,
busy focus retention, nested selectors, pointer entry, suspension and explicit
entry during the hover delay.

Each game's last main tab is saved atomically in its own small navigation file.
Every main-tab route uses the same persistence method, including bumpers and
search destinations. Unknown saved values fall back to Calcs. Pending build work
continues to take precedence. This preference does not change build XML or Undo.
Both games pass all six tab restart checks plus empty-build and fallback cases.

Validation: SDK 165/165; API baseline 1/1; displayed indexed session checks 4/4;
real-engine item workflows 91 PoE1 / 89 PoE2; skill workflows 69 / 71; navigation
restoration 17 / 17. Native item/theme/size matrix 256 checks, gem matrix 128;
shared interaction 15 checks and passive preview regression 32 checks. Representative
native item and gem screenshots were reviewed. No live service calls were needed.

Evidence: `artifacts/pob/preview-correction-*`, `navigation-restore-{1,2}.log`.
Physical controller acceptance remains separate from these automated checks.

Delivery: source commit `b2e4cd01` is published in `cumulative-candidate-20261019-preview-final` (clean source). PoE1/PoE2 0.1.4.18 are installed and enabled. The normal-profile candidate was launched hidden, PID 24464. All 138 frontend DLLs and 16 selected installed widget/adapter/style files match their qualified outputs. Spotify 0.3.90.2 and YouTube Music 0.3.43.5 remain enabled. No merge, push or release was performed.

## Undo/Redo previews, 2026-10-19

History retains its existing XML snapshots and loadout-switch behavior. A passive
focus/hover preview on each available action describes its current-to-restored
values. Existing saved history is supported without a storage migration. Summaries
are prepared at checkpoint/reopen boundaries, cached for the current pair of
snapshots, and bounded to four entries with an additional-change count. Rendering
and focusing never call the calculation engine. Item changes show differing text
lines; configuration and passive labels use existing upstream projection metadata.
Other saved fields use their build-document labels; these are state differences,
not predicted damage changes or a replay of the original command.

Pure description checks: 12 passed. Focused edit/Undo/Redo/reload workflows: 10
passed per engine. The full work-state suite passed 40 checks on PoE2; its PoE1
run caught a normalized-XML cache mismatch after reopening, corrected and verified
by the focused reload suite. Native checks: 320 passed across both games, two
sizes, and four themes, verifying passive chrome, unchanged focus, A activation,
and busy-control retention. The compact preview was visually inspected.

Evidence: `artifacts/pob/history-preview-native-20261019`,
`history-preview-focused{1,2}-20261019.log`, `history-preview-work{1,2}-20261019.log`.
Packages: `history-preview-packages-20261019`, PoE1/PoE2 version 0.1.4.16, with
qualified engine/runtime/web assets verified unchanged against 0.1.4.15.
Both packages are installed/enabled; installed code and styles match the qualified
staging hashes. Cumulative candidate k was relaunched hidden with the normal
profile/controller configuration (PID 34628 at delivery). One frontend remains,
with no main-window handle/title; validation windows are closed. No merge or release.

## Music layout qualification, 2026-10-18

Resumed after the requested pause. YouTube Music's heart sits beside the seek
slider with reciprocal navigation and pending-state focus preserved. Spotify's
vertical player reserves equal widths around the centered title; docked playback
keeps its horizontal presentation. No production host changes were required.

Widget suites passed 39/39 (YouTube Music) and 86/86 (Spotify); the final Spotify
layout contract rerun passed 1/1. Native rendering passed 415 checks across normal,
light and enlarged high-contrast styles, including focused heart retention while
busy. Both main players were visually inspected using offline artwork. Main
YouTube viewport/extent heights are both 408.8 DIP; compact-pin heights are both
384 DIP. No artwork reduction was necessary. Smaller/enlarged-text layouts retain
scrolling so controls remain reachable.

The initial centering assertion incorrectly used TextBlock.ActualWidth (the text
extent) instead of its allocated layout slot. Corrected geometry checks also
caught a 46px heart versus 44px spacer mismatch. Both sides now use 44px. Final
evidence is `artifacts/pob/music-seek-native/native-result.json`, its layout JSON
and representative PNGs. Source exports are `music-seek-export` and
`spotify-center-row-export`. Qualified packages are `music-seek-package-20261018`
(YouTube Music 0.3.43.5) and `spotify-centering-qualified-20261018` (Spotify 0.3.90.2).
No new service calls, merge, push or release occurred during this layout follow-up.

Delivery: both qualified packages are installed and enabled in the normal profile;
installed styles match source hashes. Candidate k was relaunched with `--hidden`
and its normal installation root, controller enabled. Exactly one frontend runs,
PID 28332 at this checkpoint, with no main-window handle/title. PoE1/PoE2 remain
at 0.1.4.15. Validation windows are closed. Earlier live YouTube playback evidence
below applies to the repaired .4 package; this .5 follow-up changed layout only.

## Current checkpoint

The dated entries below are historical evidence; their earlier "pending" and
process-ID statements are superseded by later checkpoints. Current verified work
includes both installed games' item edit/cancel and skill toggle/restore flows,
exact skill-subpage return focus, Catalogue installation/update/screenshot flows,
Bare optional-package upgrade/load/remove/restart, shared preview accessibility,
modal Escape, music layout, long Notes scrolling and unattended-update recovery presentation.
The apparent Bare discovery failure was resolved as AppData redirection in the
test environment. A fresh actual media close succeeds; the earlier isolated media
shutdown and invisible-overlay observations remain unreproduced, with no speculative
production fix. Spotify authentication, physical controller/couch acceptance and
remaining integrated cross-app/environment-specific checks are not claimed complete.
The current goal remains active. See the final checkpoint for the live candidate.

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

## Shared desktop context actions and live provider checks — 2026-10-18

`fccef292` connects native right-click, Shift+F10 and Apps-key requests to the
existing context-menu owner and indexed-lease path. Opening does not invoke Select;
busy/retired owners remain rejected and dismissal returns to the exact opener.
Native SendInput fixtures pass 73 ordinary and 25 indexed assertions. Indexed
evidence: `context-indexed-20261018-b/result.json`; ordinary:
`context-native-20261018/context-menu-result.json`. Initial indexed invocation was
too early (no frame); a subsequent continuity assertion used a baseline from before
the deliberate row-focus change. The fixture now awaits readiness and takes the
menu-only baseline after returning to row 75. These are synthetic desktop checks,
not physical controller evidence. Live queue menu retest awaits the new publication.

Normal-profile YouTube Music `0.3.43.1` loaded with its existing sign-in. Playback,
Like/Unlike and focus retention worked; the test like was removed. Queue entry
revealed the playing occurrence at index 12 while focus stayed on Queue. Manual
scrolling back to index 0 was retained through playback updates. Playback was paused
after checking. Spotify `0.3.90.1` displays Connect Spotify account, so live testing
needs user reconnection; no credentials were inspected. Evidence:
`music-live-20261018.json`. Queue removal and playlist Play next remain pending.

Display Controls `0.1.1.1` was installed through the ordinary CLI update path with
existing profile data preserved. Read/control consent was requested and accepted
through native UI. The UI reported Odyssey G93SC 5120×1440 at 240 Hz, offered the
enumerated modes, and showed Keep/Revert after requesting 120 Hz. No Keep was
chosen; after timeout fresh controls reported 240 Hz again. Saved profile file
timestamp remained unchanged. The transient 120 Hz mode was not independently
sampled; this is bounded timed-flow evidence, not complete mode/HDR/Keep/disconnect
qualification. `display-live-rollback-20261018.json` records that limitation.
Capability copy now names mode/HDR changes rather than only saved profiles.

## Documentation reconciliation — 2026-10-18

`3af957e8` corrects startup, Bare/Production membership, permission/credential scope,
retention, update navigation, Display Controls naming and current source versions.
It removes 22 superseded plan/discussion documents and fixes incoming links while
retaining the active plan/ledger and ongoing ownership instructions. The audit
checked 241 Markdown files and 756 local targets with zero missing files; external
URLs and fragment anchors were not checked. Details:
`documentation-audit-20261018.json`. Final review against the launched cumulative
candidate remains required; older architecture histories with live operational
guidance were not indiscriminately deleted.

## Cumulative installed candidate — 2026-10-18 final checkpoint

Publication `cumulative-candidate-20261018-c` succeeded from clean application source
`fccef292`; later commits update documentation only. Production and Bare folders
both passed the real release verifier (two core widgets; nine/zero optional archives).
PoE1/PoE2 `0.1.4.4` packages include all-version artwork and were installed/enabled
through the ordinary verified CLI update path. Display Controls is `0.1.1.1`.

Live final-host YouTube Music context-menu retest passed: right-click opened the
exact row, Remove deleted only the upcoming song, current playback stayed selected,
focus reached the successor, and Play next restored the removed test song. Playlist
Play next inserted its first tracks (Dingus, Hot One) in source order while preserving
the current song. Playback was paused; test likes were removed. This modifies the
temporary playback queue, not the saved playlist. Evidence:
`music-live-20261018-final.json` and `music-context-fixed-ui.json`.

Installed PoE2 Search opened with the search field focused, Gems → first result
focused `pob.gem.1level`, and reopening/closing restored the Search opener.
Whole-loadout comparison displayed both contexts, retained Compare focus on
completion and restored the inspected indexed row after Back. Its new tree rendered
with packaged artwork. Installed PoE1 had no saved build; a new default Scion build
was created, Search opened, and its tree rendered. No GGG requests were needed.
These native checks complement fixture evidence rather than replace the unfinished
full application/high-contrast/scaling/controller walkthrough.

One controller-enabled normal-profile frontend remains running **hidden**, PID
`30684` at this checkpoint, from `cumulative-candidate-20261018-c/frontend-published`
with its `build` installation root. All 138 top-level deployed DLLs match the
qualified publication (`final-live-payload.json`). No validation flags, test profile,
merge, push or release. Recheck the PID/path before later interaction.

The goal remains active. Still open: Spotify authentication/live workflows, actual
installed unattended-updater acceptance, remaining guarded display variants, broader
app/Catalogue permission/update/rollback workflows, complete requirement-by-requirement
architecture and layout audit, and final documentation reconciliation. Keep the
initial unreproduced Bare pixel/UIA divergence visible in the audit.

## Follow-up requirement audit and workflow completion — 2026-10-18

The follow-up audit found specific unfinished implementation, beyond the broad
acceptance matrix. These are now implemented: an owner for exact-build skill-option
cache/read/failure state; strict typed skill/search responses; explicit skill-option
retry; exact navigation from a disabled main-skill warning; upstream loadout/weapon
context in gem inspection; and selective calculation/inactive-setting disclosure.
Collapse retains totals/current choices, rejects stale tokens and returns focus to
its toggle. Search reveals a matching retained setting. Stat-to-calculation
navigation now finds its containing section while preserving an already matching
section. No passive-effect rules or extra engines were introduced.

Focused engine evidence (`final-gap-*`, `context-final-*`): skill options 38/27,
skill management 75/77, unified search 40/40, empty builds 23/23, disclosures 25/25,
calculation context 11/11 (PoE1/PoE2). Architecture checks: 64. These passed before
native disclosure inspection; fixture failures first exposed an incorrect assumption
that the Life overview itself always has source detail. The fixture now deliberately
selects upstream Total Base and separately asserts overview section navigation.

Catalogue adds five exported/native fixture states: empty, loading, retained stale
error, long prerequisites and absent prerequisites. Its executable suite passes
12/12. `MarketplaceIntegratedWorkflow` passes real widget → broker → reviewed
installer → catalog persistence and permission-pipe flows with inert local archives
and fake HTTP: cancel/approve, forged review rejection, partial batch retry,
revert/re-update with disabled preference retained, and permission deny/approve.
This is integrated logic/pipe evidence, not a claim of physical controller or live
GitHub acceptance.

Updater path verification retains per-component reparse checks but replaces repeated
PowerShell provider discovery with direct filesystem attributes. The same 100 warm
paths measured 2.73 s before and 0.07 s after; hashes are still checked independently.
The linked-ancestor regression passes even when the final file is absent.
`update-path-check-performance-20261018.json` records the narrow measurement.

Actual Inno acceptance now passes six cases: Bare and Production, each with startup
enabled, disabled and foreign-owned. `scripts/Test-InstalledUpdate.ps1` compiles
the real setup code/payload with isolated AppId, registry, shortcut and data names;
it calls the real handoff function, validates the entire installed inventory and
relaunches the actual executable hidden with an isolated profile. Existing
update-check choices and edition remain unchanged. Evidence:
`installed-update-20261018/results.json`. This does not claim unmodified production
registration or UAC/clean-machine prerequisite acceptance. Initial harness attempts
needed a short install path for Inno and correct missing-registry-value handling.
The isolated registration/shortcut was subsequently uninstalled; the existing
Developer installation registration and shortcut stayed unchanged (`cleanup.json`).

Both assembled editions also pass the seven native hidden-startup checks: no Bridge
or widget/update startup while hidden, Guide adapter present, one initial-widget
commit on first show, input suspension on hide and one Bridge retained on reopen.
Evidence: `final-hidden-startup-{production,bare}-20261018/result.json`.
These are application/adapter checks, not new physical Guide-button acceptance.

Catalogue's 11 states pass 108 native assertions per theme (Mono Ink and Mineral
Light) at 900/560 DIP; representative stale-error and long-prerequisite renders
were inspected. PoB disclosure/layout fixtures pass 528 native assertions across
both games, 1320×900/820×600 DIP, dark/light plus simulated system high-contrast
ink and 125% text scaling. This is a high-contrast renderer override, not a change
to the user's Windows contrast setting. Native inspection caught undersized list
viewports beneath fixed Configuration/Calcs controls. Configuration now scrolls
its navigation as one region; Calcs gives controls and result/source lists bounded
scrolling regions, retaining complete row visibility. Configuration and quick-control
regressions pass both engines. Representative compact and high-contrast renders
were inspected after correction. Evidence: `catalogue-states-20261018/*/native-result.json`
and `disclosure-native-scroll-20261018/native-result.json`. Subsequent count-label
cleanup removes zero-count noise; both-engine disclosure checks pass again.

## Current cumulative candidate — 2026-10-18 follow-up checkpoint

`cumulative-candidate-20261018-d` was published from clean source `76e81d0d`.
PoE1/PoE2 `0.1.4.5` were installed and enabled through the ordinary verified CLI
update flow. Their unchanged engines/artwork reuse the previously qualified
package payloads after comparing engine-lock hashes and confirming no changes to
the engine, web or artwork-generation sources. Managed assemblies were republished
from the final source; qualified archives and provenance are in
`gap-reused-package-20261018-poe{1,2}/qualified`. The earlier redundant artwork build
was stopped and its partial output was never installed.

The installed PoE2 widget loaded the normal saved build, opened detailed
calculations, collapsed and expanded its actual upstream source row, and retained
native focus on `pob.calculation.details-toggle`. No build edit or GGG call was
needed. Evidence: `cumulative-candidate-20261018-d/poe2-disclosure-live.json`.
The candidate was hidden using its semantic Guide Close action; this is not a
physical Guide press. PID **66756** was responding and hidden at this checkpoint,
with the normal profile and no validation flags. All 138 deployed top-level DLLs
match the publication. See `live-payload.json`, `live-checkpoint.json` and
`final-hidden.json` in that candidate directory. Recheck the PID before reuse.

Commits: `a1c8bab9` startup recovery copy; `96a067cf` Catalogue integrated/state
coverage; `c1d6cba3` real isolated updater acceptance and path performance;
`2d08acbe` typed PoB ownership/inspection/disclosure and compact navigation;
`76e81d0d` evidence/plan checkpoint. No merge, push, release or CI repair.

The goal remains active. Remaining acceptance includes Spotify reconnection/live
flows, guarded display Keep/HDR/disconnection variants, the broader integrated
app/controller/couch walkthrough, remaining final resource measurements and final
documentation reconciliation. Preserve the previously recorded unreproduced Bare
rendering observation and the distinction between fixture, integrated service,
native presentation and physical hardware/controller evidence.

## Display hardware and diagnostic correction — 2026-10-18

Installed Display Controls 0.1.1.1 on the normal-profile cumulative candidate was
exercised through its actual UI. A separate read-only test process sampled Windows
CCD/DXGI data during each preview and after restoration:

- Odyssey G93SC: 5120×1440 changed from 240 Hz to 120 Hz; Revert restored 240 Hz.
- A second 120 Hz preview was kept, independently observed, then 240 Hz was applied
  and kept again. Both monitors' full summaries matched the original baseline.
- HDR became enabled **and active** on the Odyssey during preview; Revert returned
  both flags to false. Both monitor modes stayed at their original values.
- P27h-20 remained 2560×1440 at 59.951 Hz, without HDR, throughout. Saved profile
  file modification time remained 2026-10-11; these actions did not replace profiles.

Evidence: `display-hardware-20261018/result.json` and its baseline/preview/kept/
restored JSON captures. This is actual hardware with UI Automation, not a physical
controller check. Cable disconnection remains covered by deterministic unavailable
monitor/remapping/rollback tests rather than physical unplugging; unsupported HDR
was not attempted on the second display. Brightness/vendor controls remain outside
the supported capability set as explained in the maintained display reference.

The earlier baseline-only restore log was traced to diagnostic serialization of a
valid direct-mode target: its target-mode index is intentionally `uint.MaxValue`
until Windows selects timings. Logging now represents unresolved timings as null
and records requested HDR instead of indexing a nonexistent record and silently
ending the logger. A regression verifies subsequent entries still arrive. The 25
display checks pass, including blocked/failing diagnostics not delaying rollback.
No native apply, guard deadline or rollback policy changed.

The corrected provider was then published in cumulative candidate `e` from clean
source `65af1548`. A final 120 Hz preview was left unconfirmed while the overlay
was hidden. Independent readback returned to the complete original two-monitor
baseline after expiry. The real guard log now contains the unresolved target,
stabilization, `decision-timeout`, rollback and `completed-reverted`, with final
`MatchesBaseline=true`. See `display-hardware-20261018/hidden-timeout-result.json`
and its event/readback captures. The initial evidence extractor compared a locally
converted JSON timestamp with UTC; selecting the exact last transaction corrected
the extractor. This was not a guard timing failure.

## Installed section sweep and current resource checkpoint — 2026-10-18

`installed-pob-sweep-20261018-settled/result.json` records all twelve installed
PoE1/PoE2 main-section transitions with visible native focus. The harness waits for
stable geometry; its first attempt sampled Tree during transition animation, which
correctly had not settled yet. Native inspection caught an empty Notes page showing
only its heading. `5e44b402` adds a deliberate empty message and an assertion in the
existing empty-build workflow; both engines pass 23 checks again. PoB packages
0.1.4.6 were republished with unchanged qualified engine/artwork assets, installed,
and the message was verified and visually inspected in the actual normal-profile
PoE2 widget (`cumulative-candidate-20261018-e/empty-notes-native.json/png`). This
main-section sweep does not substitute for the remaining subpage/controller audit.

Both final `e` bundles passed release inventory/catalog verification: two core
widgets plus nine/zero optional archives. One fresh-profile observation per edition
with warm filesystem caches measured internal startup-begin → initial-widget-ready
at 1332 ms Production and 1328 ms Bare. Eight one-second process-tree samples with
Settings visible gave median private commit of 267.8/252.8 MiB, with five processes
in either case. Unpacked payloads were 393.52/393.24 MiB; both include the same
private runtimes and host services. These are bounded observations, not cold-boot,
GPU-memory or physical Guide-latency benchmarks. Evidence:
`final-bundle-resources-20261018/result.json` and per-edition logs/samples. The
diagnostic writer creates its empty file before first show, so file existence was
explicitly removed as a claim about startup deferral. The separate native hidden
startup checks remain the evidence for that behaviour.

One final normal-profile, controller-enabled candidate remains **hidden**, PID
**51564** at this checkpoint, from `cumulative-candidate-20261018-e`. No child
processes were present three seconds after hidden launch, and all 138 deployed
top-level DLLs match its qualified publication. Host source is `65af1548`; PoB
0.1.4.6 source is `5e44b402`. No validation flags, merge, push, release or CI repair.
Recheck live PID/path before reusing it. Spotify authentication and the remaining
integrated application/subpage/physical-controller acceptance and final documentation
audit remain open; the goal is still active.

## Catalogue end-to-end and shared controls checkpoint — 2026-10-18

`scripts/Test-CatalogueNativeWorkflow.ps1` now drives an isolated native installation
through the real core workers, bridge, package admission, consent and Settings
handoff. Only release HTTP, time and OS audio are simulated. Its test-only bridge
entrypoint refuses a normal-profile root; production construction retains its
existing sources. It never falls back to external HTTP.

`catalogue-native-repeat-20261018-c/result.json` passes **22 checks**: review Cancel,
install, disabled-state retention, a delayed download with stable focus and no
duplicate request after hide/reopen, permission denial/retry/approval, partial
batch failure, batch Cancel/approve, retry of failed work, Settings scrolling,
revert and ordinary re-update of the retained newer version. Re-update retains
exactly two versions and keeps the widget disabled. Three further external-launch
reopens and a 125-second hidden/reopen pass verify rendered pixels, not merely UIA
bounds. The partial-failure page was visually inspected; useful results, error and
retry actions are readable. The isolated Performance Monitor enable change used
for the subsequent pin fixture was restored. No normal-profile music, display or
build state was changed.

Two initial driver attempts reached the same Settings technical link below the
fold and correctly failed their visible-geometry gate. UIA focus alone did not
reveal that clipped control. The driver now explicitly scrolls the Settings
container before activation; no production focus workaround was added.

An earlier manual Debug fixture (PID 67724) became invisible after reopening while
UIA remained interactive. A foreground-owned screen capture confirmed desktop
pixels beneath it. That observation remains **unresolved**, not a passed visual
check or a claimed fix. The second-process route already calls `ShowOverlay()`;
no motion error was present in its failure log. Published Release passed the full
workflow above, and the freshly built x64 Debug frontend passed a separate
hide/external-reopen capture after the pin tests. The initial fresh Debug comparison
accidentally selected an older `bin/Debug` output; it is excluded from current-build
evidence. Subsequent launches explicitly bind Platform=x64 and verify loaded DLLs.
No speculative production animation change was made.

`music-native-themes-20261018/native-result.json` passes **381 native assertions**
for main/compact Spotify and YouTube Music players, using actual package WRSS with
the default palette, Mineral Light and a simulated high-contrast style override
with 125% text. Checks cover artwork bounds, horizontal overflow, reachable
transport controls and the same focused save/like button surviving busy updates.
The light and high-contrast main-player captures were visually inspected. Images
and collection viewports are local substitutes; this does not establish populated
queue or authenticated Spotify behaviour, nor a real Windows contrast-theme change.
All 297 deployed Debug DLLs matched the qualified output. The first launch selected
an old Debug output and failed protocol admission; its result is excluded.

`shared-final-native-20261018/text-entry-result.json` passes **169 keyboard checks**,
including suggestion navigation, stale-result rejection, stable typing area,
reduced motion, password behaviour and disposal. `pin-menu-performance-result.json`
passes **16 actual Manage pin menu checks**, covering Move/resize and Opacity,
duplicate opening A, re-priming, Save and Cancel with restored main input ownership.
Audio Mixer was an unsuitable initial pin fixture because it offers no pin layout;
the qualified run uses Performance Monitor. These are native automation and synthetic
controller-frame checks, not physical couch/controller acceptance.

Catalogue single-update headings and completion summaries now use singular grammar.
The focused integrated bridge workflow passes again; the frontend production
publication and corrected Debug fixture build pass. Earlier fixture build failures
were an unspecified native preview DLL path and use of a bridge-implementation
resolver after the contract split; the final fixture uses the existing frontend
style resolver and explicit current native inputs.

Current guides now distinguish completed implementation from integrated acceptance,
describe actual workspace sizing and inspection/retry/disclosure behaviour, and
record real isolated-Inno evidence. One broken historical goal link was replaced
with its verified Git-history reference. Remaining acceptance includes PoB trade
and editing subpages after sizing changes, authenticated Spotify, environment-specific
updater fallback behaviour and physical controller/couch/hardware limits. The
invisible-overlay observation remains open. No merge, push, release or CI repair.

Cumulative candidate **f** is running hidden with the normal profile and controller
input enabled, PID **50556** at this checkpoint. Its launch contains only `--hidden`
and its installation root; no validation/profile flags. All **138 deployed DLLs**
match the published frontend; no visible windows or child processes were present
after launch. Publication began at `610e777c` with the fixture source changes still
uncommitted; the final Bridge was republished after the completion-summary correction
now committed in `70970879`. `publication.json` deliberately retains its original
dirty-source provenance. `3ac478aa` adds only the Debug music fixture. PoB packages
remain the qualified 0.1.4.6 installed earlier. See
`cumulative-candidate-20261018-f/live-payload.json` and the unique publication binlogs.
The latest documentation link audit passes **240 Markdown files / 760 relative
local targets**, with no missing targets. Recheck the live process before acting.

## Installed PoB subpages and inspection reading position — 2026-10-18

Both installed 0.1.4.6 engines passed **26 website-modal checks each** using copied
builds, an isolated profile and locally intercepted pages. Coverage includes modal
bounds, 75/100/125% interface scaling, enlarged text, full/reduced motion, three
hide/reopen cycles, Back restoration, retained sessions and cookies/local storage
surviving worker restart. No trade-site requests were needed. Both games also pass
**15 offline trade workflow checks each**: origin isolation, trusted versus synthetic
actions, cancel/retry, batch evaluation, explicit Ring 2 selection, ranked-row
navigation, comparison and return to retained listings without equipping.
`2f0f4046` updates the fixture for the compact **Edit ranking…** flow and both trade
API routes. An initial timeout expected an always-visible target selector; the
product had successfully ranked the item. A retry initially restored the previous
test's unfinished draft, so the qualified runs use clean copied build directories.
Evidence: `pob-installed-subpages-20261018/poe{1,2}-website.json` and
`poe{1,2}-trade-clean.json`.

Native inspection of a long inventory item found a real presentation defect:
initial focus on the footer scrolled past the first details. `a2497a48` moves item
and gem actions, plus the item equipment selector, outside a bounded reading
viewport using the existing Grid/Scroll/modal composition. It adds no host API,
gameplay rule or engine request. Descriptions open at the top; controls remain
visible while reading the end. Close and reopen reset reading position.

Both-engine item suites pass **81/79**, and skill suites **78/80**. The wider item
fixture now selects the helmet it created instead of assuming an arbitrary build's
first equipped item supports ordinary affixes. Native matrices pass **256 item**
and **84 gem** assertions across both games, dark/light themes and wide/constrained
views, with deliberately long descriptions. They verify initial/reopened offset,
fixed action bounds, dropdown cancellation/confirmation, no accidental equip,
original-row focus restoration and unchanged A selection. See
`inspection-fixed-actions-{items,gems}/native-result.json` and the corresponding
engine logs. The initial native fixture still expected Equip inside the description
scroll region; that assertion now checks its fixed panel bounds.

Both packaged **0.1.4.7** widgets pass the actual inventory workflow again: navigate
six rows, use X Inspect, close back to the same row and screen position, then use
the independent Select action. The new PoE1 inspector capture was visually inspected
and now begins with usage, name, type and physical damage rather than halfway through
the description. `inspection-packaged-final/` records three checks per game.
Packaging reuses byte-verified engine/runtime/web assets from qualified 0.1.4.6;
`inspection-fixed-actions-packages/poe{1,2}/asset-provenance.json` records the hashes.

The normal profile now selects both 0.1.4.7 packages, enabled as before. Cumulative
host **f** is restored **hidden**, controller-enabled, PID **65792** at this checkpoint.
Its 138 frontend DLLs and both installed PoB implementation DLLs match the qualified
payloads. No visible windows or child processes were present after hidden launch;
see `cumulative-candidate-20261018-f/final-running.json` and `final-live-payload.json`.
The earlier invisible-overlay observation is still unresolved. Spotify authentication,
remaining broader editing/app acceptance, updater environment-specific fallback and
physical controller/couch/hardware limits remain open; the goal is not complete.

## Update fallback and recovery presentation — 2026-10-18

Native recovery coverage exposed a real narrow-layout defect: the interactive
setup warning inherited the helper text's two-line limit. The warning now wraps
fully within the existing scrolling page. No arbitrary line cap was added.
The focused Settings update scenario passes; the corrected native run passes
**437 assertions** at 880/520 DIP and ordinary/150% text. It covers ready and
expanded notes, interactive prerequisites, cancellation, failure, restart-required
and startup-registration warning states. Check/Back remain reachable and preserve
focus; recovery navigation never dispatches installation. These use a local
service fixture and do not claim real UAC/prerequisite failure acceptance.
Evidence: `updater-recovery-native-fixed-20261018-result.json`; representative
startup-warning capture visually inspected. Initial clipping failure is retained
in `updater-recovery-native-20261018-result.json`.

## Catalogue screenshot browsing and familiar-store review — 2026-10-18

The real widget's Next/Previous/Back screenshot workflow passes within **13 widget
tests**. Native rendering passes **150 assertions** at 900/560 DIP: decoded local
images, matching count/accessibility identity, stable image/navigation controls,
focus and exact non-first-card restoration. Only the fixture's two exact image
URLs are replaced with local PNGs at the presentation boundary. This does not
claim remote image delivery or session-routed controller B; declared B and native
Back-button dispatch are checked separately. Evidence:
`catalogue-screenshots-native-20261018/native-result.json`.

A read-only comparison with the [UniGetUI feature overview](https://github.com/Devolutions/UniGetUI#features)
confirms the relevant familiar-store flow: discover/filter, inspect details,
install, review installed packages and update. Catalogue now follows that flow,
adds service prerequisites before installation, retains explicit trust review and
supports partial-update retry. Management links reuse Settings. UniGetUI's
arbitrary installer switches, scheduled installations and broad package-manager
configuration are outside this controller-first scope. Reference retrieved once
on 2026-10-18 in `catalogue-store-comparison-20261018/UniGetUI-README.md`; no live
GitHub throttling or repeated service calls were used for acceptance. Existing
local cache/request-count and 22 native install/update workflow checks cover the
network and installation boundary separately.

## Keyboard dismissal and delayed dialog completion — 2026-10-18

Installed inventory inspection revealed that keyboard Escape did nothing despite
working pointer/context-menu, wheel and Tab interaction. Presenter input roots now
route otherwise-unhandled Escape through the active modal's existing declared B
action. Native child handling, browser keyboard focus, gamepad filtering, input
admission and presentation ownership remain authoritative. Repeats are coalesced
per modal incarnation; completing an older action cannot clear a newer guard.

The actual installed 0.1.4.7 PoE1 and PoE2 inventories each pass **3 checks**:
explicit inspection, native Escape returning to the exact row/screen position,
and separate Select opening the editor. The generic native suite passes **160
assertions**, including ordinary/anchored modals, themes/sizes, repeated Escape,
reopening the same dialog ID and out-of-order completions. A fixture correction
focused the intended left opener before the race scenario; previously it correctly
restored the right opener retained from the preceding test. The item-inspection
matrix passes **264 assertions**, including closing a nested slot popup without
closing the inspector. Evidence: `inspection-packaged-final/poe{1,2}-item-inspection-escape.json`,
`modal-keyboard-race-fixed-20261018/result.json`, and
`inspection-fixed-actions-items/native-result.json`. These are native keyboard and
synthetic controller assertions, not physical-controller acceptance.

## Preview text scaling and high-contrast palette — 2026-10-18

The constrained native preview check found two host defects. Unstyled text lost
its inherited font size when moved into the preview layer; the fragment now
inherits the owner's scaled default, tracks changes while open, and restores its
original property/subscription on dismissal. Explicitly styled text continues to
use the existing scaling scope exactly once. Also, native chrome and widget text
chose different Windows palettes when high contrast was requested in the app.
Both now use the same Windows window/background-text pair and fallback.

The corrected suite passes **41 native assertions** across Mono Ink, Mineral
Light and simulated high contrast at 420×320 DIP. It checks default and explicit
text at 125%, live changes to 150% and back, contrast, bounded passive content,
unchanged focus/actions and existing lifecycle behaviour. The final high-contrast
capture was visually inspected; the previously invisible text is readable.
Evidence: `preview-accessibility-final-20261018/result.json` and
`high-contrast.png`. The two distinct failures are retained under
`preview-hc-final-20261018` (scaling) and `preview-hc-fixed-20261018` (palette).
No Windows contrast setting was changed; actual OS-theme transition and physical
couch readability are not asserted.

## Bare optional-package continuity — 2026-10-18, open finding

Added a bounded opt-in case to `Test-InstalledUpdate.ps1`. One additional real
Bare Inno update preserved the enabled Performance Monitor package and its bytes;
normal registration/startup state remained isolated. However the subsequent
installed native launch reported zero installed packages and did not expose the
widget. A native-only resume reproduced that failure without repeating setup.
The bundled CLI still lists the enabled selected version, and read-only discovery
through the installed WidgetCatalog assembly finds it. Captured child arguments
point to the intended isolated catalogue. Cause remains unresolved; no speculative
production change was made. This is **not** completed Bare install/upgrade/launch
acceptance.

The harness now treats `activeVersion: null` correctly as automatic newest-version
selection, reads actual `selectedVersion` from CLI inventory, and checks archive
bytes on resume. It also captures failure trees and actual child roots when the
native readiness probe exits nonzero. PowerShell parsing passes. Evidence:
`installed-update-20261018/run-0ca80065/bare-disabled`,
`run-6721368e/bare-native-resume`, `bare-discovery-ui.json` and
`bare-discovery-settings.json`. The isolated installation is retained for diagnosis;
no production installation or user package was removed.

## Cumulative candidate g checkpoint — 2026-10-18

Both installed 0.1.4.7 widgets pass the bounded item-edit round trip through the
actual on-screen keyboard: change a roll's text, Cancel, restore the exact control
and original value, then return to inventory. PoE1 returned 50 after entering 5;
PoE2 returned 16 after entering 5. The fixture uses each engine's applicable
range/affix-roll control; an initial fixture assumed the same ID in both engines.
Evidence: `installed-editing-final-20261018/result.json`. This closes that editing
slice, not all skill-option mutation or physical-controller acceptance.

The music player/pin matrix passes **381 native regression assertions** after the
shared palette correction (`music-palette-regression-20261018/native-result.json`).
The first copied fixture omitted its WRSS files and is excluded. The validation-only
media window did not terminate after WM_CLOSE, so its exact test process was stopped
after a bounded wait. That shutdown observation is retained for diagnosis; it does
not establish the same behaviour in the production shell. The installed PoB test
shell closed normally.

Production publication succeeds with unique binlogs. Candidate **g**, PID **3692**
at this checkpoint, is the sole frontend, launched hidden with the normal profile
and controller input enabled. Its command line contains only `--hidden` and the
explicit installation root; all **138 deployed DLLs** match the qualified output.
Both normal-profile PoB packages remain enabled at **0.1.4.7**. The publication
started at `b1323f7e`; dirty provenance covers only acceptance-script/plan changes,
not uncommitted production code. Evidence: `cumulative-candidate-20261018-g/publication.json`,
`normal-launch.json` and `live-payload.json`. Recheck PID before further actions.

The goal remains active. Bare optional-package native discovery is an open failure;
Spotify authentication, broader installed skill editing, physical controller/couch
and unavailable prerequisite/security variants remain unverified. The earlier
invisible-overlay observation is still unreproduced. No merge, push, release or
CI repair was performed.

## Bare discovery resolved and optional upgrade continuity passed — 2026-10-18

The apparent discovery failure was a test-environment filesystem mismatch, not a
WidgetRail catalogue defect. `GetFinalPathNameByHandle` on the CLI-created catalogue
resolved its logical AppData path into `Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local/...`.
The native bridge saw an empty real AppData directory. A short-lived diagnostic
confirmed the same logical root but no state/package directory in that process;
a direct bundled-.NET8 probe discovered one package. All diagnostic source edits
were restored, and the temporarily replaced published catalogue DLL was restored
and hash-checked against its original. No production behaviour or permissions
were changed to compensate for this environment.

The optional-package test now uses a worktree profile shared by CLI and native
launch. `run-a54a29c4/bare-disabled` passes actual Inno update, enabled-version and
package-byte preservation, and native Performance Monitor loading. The CLI correctly
rejected uninstalling an enabled widget; the harness now disables it first. The
native-only continuation `run-b1faa64a/bare-native-resume/optional-continuity.json`
passes original archive-byte verification, widget loading, disable/uninstall and
restart with the widget absent. The installer was not repeated for that tail.
Normal installation registration and shortcut comparisons remain in the harness.
Evidence is under `installed-update-20261018` and
`bare-catalog-probe-20261018/executor-identity.json`. This supersedes the earlier
open Bare discovery finding. Physical Guide/controller acceptance remains separate.

## Media shutdown follow-up — 2026-10-18

Added an opt-in Debug-only `--media-shutdown-audit` to the existing native music
fixture. It records preparation/native-operation counts, performs the same
hide → presenter disposal → icon-cache drain ordering, and reports the stage of
any bounded observation timeout without force-releasing active native resources.
The actual drain remains unmodified. This produces files only when explicitly
requested and adds no production logging.

`music-shutdown-final-20261018` passes the 381 media assertions and the disposal
probe: no pending preparation/native operation remained before or after shutdown;
SVG, render, pixels and surface stages were observed. A fresh unmodified-close-path
run (`music-shutdown-recheck-20261018/close-result.json`) also passes all 381 checks
and exits promptly after actual WM_CLOSE, with no prior disposal/drain. The earlier
isolated shutdown observation is therefore unreproduced, not attributed to an
unproven production cause. No timeout/resource-lifetime workaround was added.

## Installed skill return-focus correction — 2026-10-18

The installed editing pass found Back from Group settings/More skill options
landing on Add gem. The selected group and Full DPS calculation state were correct,
but the return request entered the gem-list focus group. It now enters the skill
controls with the exact opener as that newly recreated group's default child.
The existing gem-picker return path remains unchanged; the return target is cleared
by the next admitted action rather than retained across unrelated selections.

Both pinned engines pass **79/81 skill-management checks**. Installed packages
**0.1.4.9** pass native Full DPS toggle/restore, stable toggle focus, group settings
and options navigation, exact opener focus and selected-group preservation in both
games. Evidence: `skill-return-poe{1,2}-exact.log` and
`installed-skill-return-exact-20261018/result.json`. The first implementation fixed
Group settings but still defaulted More skill options to Group settings; the native
check caught this and the corrected test covers both. The initial engine command
incorrectly included `.zip` instead of the bundle stem and is excluded.
Package preparation reuses hash-verified 0.1.4.7 engine/web/artwork assets; only the
managed widget binaries and package version change. No live service calls.

## Current running candidate after skill focus verification — 2026-10-18

Candidate **g** is restored hidden with normal profile/controller input, PID
**30684** at this checkpoint. Both PoB packages are enabled at **0.1.4.9**, updated
through the ordinary disable/reviewed-update/enable CLI sequence after installed
fixture verification. Resolving the normal catalogue's file handle confirmed its
physical path is the real `AppData/Local/WidgetRail/widgets` directory; the isolated
acceptance namespace's redirection does not apply to that normal catalogue.
The sole frontend has only `--hidden` and the explicit installation root, no test
profile/controller flags; UIA reports no visible windows. All **138 frontend DLLs**
match the qualified build. The restored Bridge catalogue DLL also matches its
pre-diagnostic hash. No temporary catalogue probe remains in production source.

Evidence: `cumulative-candidate-20261018-g/live-payload-skill-return.json`,
`normal-launch-skill-return.json`, `normal-pob-versions.json` and
`hidden-windows-skill-return.json`. Current documentation file-link audit passes
**244 tracked Markdown files / 761 relative local targets**, with zero missing
files; external URLs and fragment anchors are excluded. The larger tracked-file
count uses all Git-tracked Markdown rather than the earlier maintained-guide subset.

Remaining work is final integrated coverage reconciliation, including any still
unverified Notes/calculation scrolling combinations, Spotify authentication and
physical controller/couch/environment-specific acceptance. The two earlier native
visibility/shutdown observations are unreproduced, with scoped passing retests;
no broad reliability claim follows from those retests. The goal remains active.
No merge, push, release or deferred CI repair.

## YouTube Music account-change queue race — 2026-10-18

The final deterministic audit reproduced a pending playlist/album Play next request
accepting old-account results after disconnect/reconnect. Radio had the same delayed
browse-to-queue boundary. Queue mutations now check the existing account epoch under
the same lock used by account transitions, immediately before publishing entries.
Cancellation and disposed-service state are also rejected there; no provider calls
or new polling/cache layer were added.

The new fake-provider regression deliberately ignores cancellation. It covers
playlist, album and radio across disconnect and reconnect, explicit cancellation,
disposal, and unchanged-account source order/normal playback. It fails against the
original implementation; the corrected complete music suite passes **39/39 tests**.
Evidence: `music-account-repro.log` and `music-account-final2.log`. A test property
name typo was corrected before the qualified build. All traffic was local/fake.

## UTF-8 XML byte-order mark import — 2026-10-18

The integrated Notes fixture was saved by .NET's XML writer with a UTF-8 BOM.
Bounded file reading preserved that marker, causing XML to be classified as a
share code and rejected. Decode now removes a leading encoding marker before
classification and after decoding a compressed XML payload. Input/expansion byte
bounds, game-root checks and external-entity rejection remain unchanged.

Both import/export suites pass (**19 PoE1 / 28 PoE2**), including actual local
BOM files, clipboard/share payloads and entity rejection. Evidence:
`bom-import-poe{1,2}.log`. New packages 0.1.4.10 retain the previously verified
engine/artwork assets; native reopening/Notes acceptance follows below.

## Bounded formatted reading and installed Notes — 2026-10-18

`bb2de55c` keeps natural word/paragraph boundaries when splitting long formatted
text into protocol-bounded presentation blocks. The focused regression passes
**11 assertions**: exact text reconstruction, bounded valid snapshots, preferred
paragraph/whitespace breaks and intact Unicode surrogate pairs. Evidence:
`reading-chunks-test.log`.

`installed-reading-20261018/fixed/result.json` passes in both installed engines
with synthetic long Notes at a 1600×888 viewport. Native wheel scrolling reaches
the middle, PageDown advances, and scrolling reaches exactly 100%/0% at the
bottom/top while retaining focus. This is installed reading/viewport evidence,
not physical controller or every possible formatted-content acceptance. No
service requests were made.

## Final documentation source and evidence reconciliation — 2026-10-18

The current maintained README, user/setup/update/Catalogue guides, PoB/music/display
guides and related developer references were reviewed against current sources,
manifests and existing qualified artifacts. Two obsolete collection fragment links
were corrected, along with remaining Spotify/YouTube Music instructions to enable
a fresh reviewed install and the ambiguous Display Controls package-builder path.
The SDK summary now distinguishes declaration caching from indexed/discovered native
presentation. Command paths/options were checked against scripts and CLI dispatch;
this was a source review, not a new build or command-execution qualification.

The local-file audit records **244 tracked Markdown files / 761 relative local
targets**, zero missing (`documentation-links-latest-20261018.json`). A separate
heading/explicit-anchor pass checks **44 local Markdown fragment links**, now with
zero missing. Evidence: `documentation-final-source-audit-20261018.json`. External
URLs were not requested. The earlier **240/760** plan count is superseded.

Seven existing images were inspected for caption accuracy: the README overview and
pinned video, Spotify search/pin, and PoB calculations/comparison/ranking. The pinned
video caption now describes the visible overlay controls. PoB's dated earlier
captures remain illustrative rather than claiming the latest layout; their trade
headers contain no signed-in username. No new screenshots, live services or native
launches were needed for this review. The owner demonstration remains a placeholder;
required project/license acknowledgements remain intact.

The plan now records completed installed item/skill/Notes checks and implemented
compact layout slices without treating their broad physical acceptance as complete.
Remaining cross-app/populated-music verification is separate, along with Spotify
authentication, physical controller/couch and environment-specific checks. Recheck
documentation affected by any remaining corrections before delivery. Earlier
visibility/shutdown observations retain their scoped, unreproduced disposition.

## Final cross-app and populated music presentation — 2026-10-18

Current Debug frontend, normal native renderer and isolated settings were exercised
at 125% Windows DPI, interface size 125% and text size 125%. Settings home and
Appearance, Audio Mixer, Games & Apps, Task Switcher, Network Controls (details
and Bluetooth subpages) and Power were inspected without invoking hardware or
power changes. Settings controls remained named and visible; the audio output
selector closed with Escape back to its opener. The actual Games GridView reached
100% by wheel without changing its focused occurrence. Its named outer wrapper
has no ScrollPattern; early shallow/unsettled inspections and the ineffective
UIA `--to` attempt are excluded from passing evidence. Settings and Audio Mixer
crops were visually inspected. Evidence: `cross-app-20261018/result.json`,
`games-scroll-qualified.json` and `audio-selector-return.json`.

The music fixture now exports sixteen actual widget-produced queue rows for each
service, including duplicates, the current-song location and long metadata. It
materializes those rows only for presentation; indexed leases and virtualization
remain covered separately. **221 native assertions pass** across two viewport
sizes (840×580 and 620×400), Mono Ink, Mineral Light and a host high-contrast
palette with enlarged text. Scroll/reveal does not activate playback or change
focus. Menu cancellation and dispatch preserve the exact duplicate occurrence.
Evidence: `music-queues-final-20261018/native-result.json`; native frontend build
has zero warnings/errors. A test-export return-type compile error and missing
native input path were corrected before the qualifying build. No live requests.

The provider availability audit also fixed explicitly unavailable playlist/album
entries in commit `20d4f8dd`. Unknown availability, provider ordering, duplicates
and the first-500 input bound remain unchanged; no fill-up requests are added.
All **41 offline Python tests** pass. Package 0.3.43.3 reuses the verified 0.3.43.2
managed/runtime files and changes the provider script and manifest only.

Browser 0.1.6 was also opened against the normal installed catalogue with isolated
shell settings. Its local start page, Bookmarks dialog and address keyboard were
exercised; Cancel returned to the browser scope, and the start page/toolbar crop
was inspected. No external URL was opened. Evidence: `browser-native-20261018`.

Visual inspection of the populated Spotify light-theme queue found a white options
glyph on its light badge. Labels had explicit text colors, but the shared action
surface had no default foreground. The shared rule now supplies `var(--text)`;
widget/state overrides retain precedence. Native queue tests add actual badge
contrast assertions before/after menus and complete synthetic button releases.
This was not a word-clipping or queue-virtualization defect.

The corrected queue matrix passes **269 assertions**, including glyph presence
and at least 3:1 options-badge contrast in every tested theme/size, before and
after opening/using context menus. The first new check ran before post-release
layout settled; awaiting the badge's bounded native readiness corrected that test
race. Mineral Light was visually re-inspected and the glyph is readable.
Evidence: `music-queues-contrast2-20261018/native-result.json`; fix `5a946113`.

## Cumulative candidate i handoff — 2026-10-18

Candidate **i** is running hidden with the normal profile and controller input,
PID **67784** at this checkpoint. The sole frontend command line contains only
`--hidden` and its installation root. UIA reports no visible windows. All **138
frontend DLLs** match the qualified production publication. Publication source is
`5a946113`; its dirty marker consists only of the verification/plan documentation
subsequently committed as `8269c872`, not additional executable changes.

Normal installed packages are enabled: PoE1 and PoE2 **0.1.4.11**, YouTube Music
**0.3.43.3**, Spotify **0.3.90.1** and Browser **0.1.6**. Updates used the ordinary
disable/reviewed-update/enable CLI flow. The normal profile was not replaced.
Evidence: `final-installed-20261018/launch.json`, `normal-process.json`,
`live-payload.json`, `hidden-windows.json`, `normal-packages.txt` and candidate i's
`publication.json`. Current file-link audit again passes **244 / 761**; the prior
**44-fragment** audit is unaffected by the appended evidence and tracking edits.

Independent implementation and the scoped deterministic/native checks are complete
for this handoff. Open acceptance limits remain authenticated Spotify actions,
physical controller/couch-distance use, Windows contrast-theme switching, display
cable/hardware variants and installer prerequisite/UAC/environment-specific paths.
The earlier invisible-overlay/shutdown observations were not reproduced in scoped
retests; no speculative production workaround was introduced. The plan retains
those acceptance boxes rather than presenting synthetic input as physical proof.
No merge, push, release or deferred CI repairs were performed.

## Settings home overflow correction — 2026-10-18

The user screenshot exposed 8.8 logical pixels of vertical overflow at the normal
880×552 home size. The earlier increase from 520 to 552 was present, but insufficient.
The home preference is now 880×568; subpages and minimum sizes are unchanged.
Scrolling remains available when constrained or using enlarged text.

The old layout fixture forced height 520 and compiled widget styles without the
platform/user-theme layers, so it could not enforce the promised default fit.
Fixtures now use the authored preferred height and actual theme layers. A focused
home export covers Mono Ink, Mineral Light and Velvet Dusk, two widths and normal/
150% text. It asserts no default overflow, a gap above Restart/Quit, constrained
last-card access and stable focus. The old height fails; the corrected height passes
**44 native assertions**. Two focused home-action/loading tests pass. Velvet Dusk
was visually inspected. Evidence: `settings-home-fit-20261018/before-result.json`,
`after-result.json` and `after-velvet.png`.

Candidate **j**, PID **7192** at this checkpoint, runs hidden with the normal
profile and controller input. It reuses candidate i's qualified frontend and
unchanged runtime files, and publishes/reseals the Settings assembly from
`97f23dff` into a separate installation root. Hash comparison permits only the
two Settings DLL copies and generated integrity metadata to differ. All 138
frontend DLLs still match; one frontend is running and UIA reports no visible
windows. The copied seal was removed only from the new staging root before
regeneration; the first seal attempt correctly rejected existing integrity metadata.
Evidence: `cumulative-candidate-20261018-j/provenance.json`, `live-process.json`
and `live-payload.json`. No release, merge or profile changes.

## Follow-up implementation and native review — 2026-10-18

Resumed the agreed follow-ups. Search's retained navigation focus request was
outranking newer quick-control requests; both direct and indexed targets now yield
to the newer focus sequence. Search retains query/category/selected identity,
revalidates changed builds and refreshes callback authority on reopening. The
selected logical result is restored; exact pixel-offset retention across removed
modal instances is not claimed. Both engines pass 80 search checks; quick controls
pass 117 per engine. Native installed handoff verification follows packaging.

Shared opt-in `SizeToContentHeight` uses bounded initial measurement, stable width
and cached geometry across same-source late updates. Protocol 93 is feature gated;
legacy constructor/default JSON compatibility is retained. Six deterministic host
checks, 164 SDK checks, native passive 45, anchored 184 and scroll cues 24 pass.
Cues are 8% of viewport width, clamped 48–80 DIP, still 2 DIP thick. Inspection is
adopted in gems/items; inventory no longer overlays its existing details pane with
a redundant metadata-only preview. Unique/gem passive content has clearer hierarchy
and item rarity uses appropriate semantic theme colors.

Build comparison now has named source/target snapshots, shared aligned columns,
signed numeric changes with zero/non-finite guards, useful non-stat summaries and
bounded context disclosure. Both engines pass 60 comparison checks. Native visual
review exposed two misses in the initial matrix: composed action content could
shrink despite star columns, and a materialized fixture dropped the collection's
GridCell placement. Rich action content now stretches while text retains its own
alignment, and the fixture preserves placement. Explicit native alignment/header
assertions now pass: **592 workflow checks**. Separate disclosure checks pass 704,
item inspection 544 and gem inspection 168; music queue regressions pass 269 after
the shared row change. Host high-contrast is a renderer override, not an OS switch.

The Configuration before/after uses identical engine snapshot, theme and viewport:
PoE2, Velvet Dusk, 1320×900 logical. Inter-control empty space falls from **36 to 8
DIP**; **11 options instead of 6** fit fully, without reducing control height or
text size. Both normal and enlarged text/constrained layouts pass. Representative
comparison, Configuration and item inspection renders were visually inspected.
Evidence: `followups-20261018/{host-passive,host-anchored,host-cues,workflows-aligned3,
disclosure,inspection,density-before,density-after,music-shared-regression}`.

Excluded attempts: the first PoE2 engine run used a PoE1 cache path; corrected runs
use the pinned PoE2 beta. Missing C# type annotations/imports were fixed before
qualified builds. One stale native launch after a failed build was closed and is
excluded. Gem fixture result naming was corrected in the evidence collection;
`workflow-result.json` preserves the earlier workflow report separately.

## Final follow-up candidate — 2026-10-18

Candidate **k** is running hidden with the normal profile and controller enabled,
PID **2776** at this checkpoint. Both PoB packages are **0.1.4.13**, enabled in the
normal AppData catalogue. The isolated frontend and preceding normal candidate
were closed first. Exactly one frontend remains; UIA reports no visible windows.
All **138 frontend DLLs** match the qualified publication. Evidence is in
`cumulative-candidate-20261018-k/final-{launch,live-process,live-payload,windows}.json`.
The host publication comes from `4e94de9e`; subsequent changes affect only PoB
packages and documentation. Package provenance records source state, copied adapter
hashes and unchanged upstream engine archive/web/runtime hashes. No merge, push,
release or CI repair was performed.

Unique-library focus previews now include up to three upstream-formatted modifier
lines, explicitly labelled as library ranges. Generation occurs during catalogue
loading, not focus changes; it adds no service calls or replacement calculations.
The compatibility helper uses upstream variant eligibility and formatting. Empty
previews omit the modifier heading. Passive fragments remain bounded Text; full
inline rich formatting stays in explicit Inspect. Both engine item suites pass:
**PoE1 84, PoE2 82**. This covers fresh/empty inventory, authoring, inspection,
stale actions, draft recovery and the new bounded modifier summaries. The final
managed packages publish and validate successfully; no separate throughput claim
is made for this small bounded catalogue extension.

Installed PoE2 native checks confirmed Search "shock chance" → Tree → the declared
RS guide focuses Build controls; Search "twister" → Skill group → the declared LS
guide focuses Group controls. Reopening Search restores each query and selected
logical result (rows 5 and 0). These checks use native semantic guide dispatch,
not physical stick presses. Exact pixel-scroll restoration across a recreated
Search modal is not implemented; selected-result visibility is restored.
Evidence: `followups-20261018/installed/{result,tree-rs-focus,skills-ls-focus,
search-retained}.json` (package .12; the later .13 change is the unique preview).

The final installed .13 Goldrim preview and explicit inspection were visually
reviewed in Velvet Dusk. The passive preview keeps the source row visible and fits
three modifiers without an empty lower panel; explicit inspection shows full
upstream properties/modifiers with readable action separation. Native Back returns
to the exact unique-library row. Evidence:
`followups-20261018/installed-final/{passive-crop,inspection}.png` and
`return-focus.json`. Initial startup/overlapped captures are excluded from visual
evidence; settled captures were used after closing the preceding candidate.

Documentation was reconciled with the delivered behavior. The local-link audit
checks **244 tracked Markdown files / 761 relative targets**, with none missing;
external URLs and fragments are outside that check. Prior follow-up matrices and
Configuration before/after measurements above remain applicable. Remaining limits
are physical controller/couch acceptance, actual Windows contrast-theme switching,
authenticated Spotify and the previously recorded hardware/prerequisite variants.

## Header refinement and item-title investigation — 2026-10-18

The build header now uses one quiet action row beside the build identity, reflowing
below it at constrained widths through the existing responsive grid. Search,
Build & import, loadout and labelled Undo/Redo keep their stable action/focus IDs.
History remains its own remembered focus group. Trade-return/ranking controls
are grouped separately below section navigation, rather than mixed with history.
The duplicate widget-name eyebrow and unused Tree-specific title style are removed.
All toolbar targets retain at least 44 DIP height. There are no new services,
render-time engine calls or shared production-host changes.

Both real-engine quick-control suites pass **119 checks each**. Native matrices
pass **304 assertions** for the short-name layout and **304** for the final
long-name layout, including 1320/820 DIP viewports, both game projections,
Mono Ink/Mineral Light/Velvet Dusk and simulated high-contrast at 150% text.
Visual review corrected a floating capped selector and toolbar style specificity;
the long-name/150% test then caught Redo overflow. The selector now receives the
remaining width so fixed actions stay visible. Wide and narrow Velvet Dusk renders
were inspected. Evidence: `header-20261018/{final,long-fit}/native-result.json`,
`long-fit/native-header-1320.png`, and `poe{1,2}-qualified.log`.
The `long` matrix is failing pre-correction evidence, not a passing result.

Upstream PoB's Craft dialog has a Name field and assigns `New Item` when the rare
item title is blank; Item.lua combines the title and base for display. Our adapter
does not expose that title field. Per the user's correction, no item-name
suppression is delivered: equipment, inventory, drafts and other item surfaces
retain PoB names and source build data. Adding a suitable item-title editing
control remains an identified gap, not part of this header change.

Both PoB packages are **0.1.4.14**, published/validated using the qualified .13
upstream payload, with unchanged engine/web/runtime assets. The cumulative host
remains candidate k; only its validation harness changed in this source revision.
The normal-profile candidate is relaunched hidden with controller enabled, PID
28228 at this checkpoint; launch and payload evidence are under `header-20261018`.
No merge, push or release. Native automation does not replace physical acceptance.

## Item names, render recovery and music playback correction — 2026-10-18

PoB packages **0.1.4.15** add Item name to the shared rare-item editor. Naming uses
upstream Edit Item Text with only the title line changed; bases, modifiers and raw
item metadata are preserved. Draft recovery, stored-item Undo/Redo, equipment IDs,
slot references and unchanged calculated statistics are tested. Unique identities
and normal/magic derived names are not renamed as rare titles. The earlier
proposal to hide `New Item` was withdrawn; names stay intact until edited.
Both complete item suites pass: **101 PoE1 / 99 PoE2**.

The reported Edit ranking failure reproduces offline using a copy of the user's
PoE2 build: its saved Weapon 1 target is absent from the current active slot list,
and the old Select constructor throws for zero selected options. The replacement
shows the unavailable saved target explicitly, retains results and requires a valid
choice before requesting website results again. Four focused ranking-settings
checks pass per engine, plus the actual-build repro after correction. Eleven
render-resilience checks pass per engine for missing active sets/main skills,
legacy gem choices, removed calculation selections/passives and unavailable
league discovery. A removed tree node also needed its stale detail/focus mode
retired, not merely a guarded lookup. No blanket render exception suppression or
silent gameplay selection was added. Existing guarded inventory/library flows
and immutable enum lookups were retained.

League discovery preserves returned order and selects the first entry by default.
An explicit saved choice survives while available; a removed league is not appended
back into discovery. Cached reopening and explicit refresh are covered. Trade-menu
checks pass **84 PoE1 / 50 PoE2**; full offline trade workflows pass **92 / 93**.
No repeated live GGG calls were made. The centered Evaluation limit/Advanced search
regression came from putting `align:center` on the reused `.pob-buildtools` class.
That change is removed. Native editor/trade matrices pass **256 assertions**, with
actual control alignment, item-name accessibility and wide/constrained/theme cases.
Velvet Dusk item editing and trade-option renders were visually inspected.

YouTube Music **0.3.43.4** places an outlined/filled themed heart beside the title
in both player layouts. It retains its action/element identity, accessible label,
pointer tooltip and focus eligibility while busy. The standalone suite passes
**39/39**; native music checks pass **381**, including pending/idle focus retention
and light/high-contrast rendering. The main layout was visually inspected.

The installed .3 candidate and its archive lacked root `web/player.html` and
`web/web-session.js`; this prevents hosted player startup. The canonical package
builder already copied them, but the ad-hoc cumulative path had omitted `web`.
The new archive-level `Test-PackageAssets.ps1` rejects that old package and passes
the repaired .4 package, checking nine required runtime/icon assets. The repaired
package was built through the canonical script. No cookie refresh or provider
workaround was needed. One normal-profile song start succeeded: elapsed playback
advanced **0:20 → 0:35** and Pause returned the control to Play. The song was left
paused; no rating mutation was needed for the live check. This verifies player
state/progress, not physical audio output.

Source audit of both pinned editors is in [the capability audit](pob-editor-capability-audit.md).
It records remaining dedicated enchant/anoint/corruption, cluster-jewel, affix-sort,
gem-picker-default/visibility and general raw-item-editor gaps. These were not
silently treated as implemented because equivalent data can be imported.

Evidence: `item-name-music-20261018`, particularly `ranking-repro.log` (old failure),
`ranking-fixed.log`, `poe*-*-qualified.log`, `poe*-render-final.log`,
`editors-native/native-result.json`, `music-native/native-result.json`,
`music-tests.log`, and `live/music-{playing,progress}-full.json` / `music-paused.json`.
Candidate k retains its qualified production host; all **138 frontend DLLs** match.
One normal-profile/controller-enabled frontend remains, PID **27652** at this
checkpoint. The overlay is hidden; UIA lists only the small background-web-session
host. Installed versions and launch/payload evidence are saved in the same folder.
No merge, push, release or deferred CI repairs were performed.
