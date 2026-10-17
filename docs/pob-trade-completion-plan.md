# PoB widgets: trade workflow and web-session completion

## Objective and authority

Complete the approved PoE1 and PoE2 widget trade workflows and reusable host support.
Implement, test against upstream PoB, inspect the native UI, and leave one controller-enabled
candidate running. Preserve user builds, pending changes, recovery records, and website logins.
Local commits are allowed; merge, push, and release are not authorized by this work.

## Agreed decisions

- Reuse existing web sessions and widget-owned modals. Internal PoB browsing is an ordinary
  widget action, not an intent. Do not recreate the removed temporary-page implementation.
- Modals use most of the current widget viewport and never exceed it. They do not resize the
  underlying widget or require fullscreen. Closing restores the prior widget and focus.
- Use an explicit widget-specific persistent profile. Do not share the Browser widget profile.
  Retain the same session while comparing so reopening preserves the listing and page state.
  Existing 120-second widget idle unloading and worker retirement release sessions; do not add
  another unload timer. Persistent storage survives session disposal. Exact website state after
  unloading is not guaranteed. Existing background memory policy is not a promise of suspension.
- Put Compare in PoB beside individual listings. The toolbar is only a fallback, not the primary
  discovery path. Actions must be visibly associated with WidgetRail and usable with controller
  and mouse, without covering site controls.
- Prefer structured item responses already loaded by normal website browsing. Do not scrape
  entire result sets or make extra authenticated API calls to implement comparison. Reuse PoB's
  converter and parser rather than duplicate item rules in C# or JavaScript.
- Keep site-specific selectors, normalization, and item interpretation in the widget. The host
  provides reusable origin-scoped page actions, bounded results, and lifecycle/cancellation.
  Response-body observation is a separate explicitly declared capability from DOM actions.
- Ordinary presentation follows the initiating action: browsing/selection is visible; processing
  can finish after returning to the widget. Permissions, origins, data access, and storage remain
  explicit. Do not depend on generic automatic login/CAPTCHA recognition.
- Comparison must not equip an item automatically. Use the existing preview/apply/discard flow
  and undo/recovery behavior. Keep clipboard item comparison as a fallback.
- Preserve listing ID, search URL, price/currency, seller/availability, trade method, and gold fee
  where actually available. Return to the retained official listing for purchasing. Revalidate
  current price/availability there; do not implement purchasing or seller messaging ourselves.
- Reject missing, stale, unsupported, oversized, or ambiguous results visibly. Do not silently
  omit modifiers or pretend an estimated website ranking is a full PoB calculation.
- No YouTube integration or new OAuth integration. Bulk listing evaluation was initially deferred;
  bounded upstream evaluation/ranking was subsequently approved (see the follow-up scope below).

## Starting evidence (superseded by verification below)

- Local commit 92d2678a removes the special temporary-page route and uses existing web sessions
  in PoB modals. Native open/close/focus/persistence checks passed (9 checks), along with 28 PoB
  trade checks, 41 note-link checks, 22 bridge-intent tests and 24 intent-contract tests.
- User subsequently reported and pictured clipped/misplaced modal and WebView after hiding and
  reopening the overlay. This is a confirmed acceptance failure. The previous native checks
  did not cover hiding/reopening while the modal stayed open. Fix this first.
- User has signed into PoE2 trade in the widget-specific profile. Preserve that profile.
- Five manually normalized item fixtures passed the upstream converter/parser probe (24 modifier
  lines plus tested weapon stats, sockets/runes, quality, and corruption). These are NOT live
  captured-response compatibility evidence. HTML merges some affix totals and cannot reliably
  reproduce constituent rolls. Live response shape and item-ID correlation remain unverified.
- Remote web sessions currently have no page-action/message capability. Their existing packaged
  messaging channel must not be exposed indiscriminately to arbitrary remote documents.

## Implementation sequence and acceptance

1. **Modal placement regression — fixed; broader visual acceptance pending.** Reproduce with real hide/show lifecycle; compare
   widget/modal/WebView rectangles and clipping before and after repeated cycles. Test full and
   reduced motion, widget resize, scaling, focus/controller ownership, and modal close/reopen.
   Fix shared placement at its owner and add a regression. Preserve the same native session.
   The retained presenter resume path reapplied authored dimensions without calling the modal
   geometry pass. Added that missing pass. The real hide/show regression failed before the fix
   (1320x900 modal overflowing an 840x800 widget) and passes afterward (808x768 at 16,16).
   Native validation passes 17 checks including three reopen cycles, 720x640 and 1040x880 resize,
   live session preservation, Back/focus return, and worker-restart storage/cookie persistence.
   Evidence: artifacts/pob/session-modal-native/reopen-red.json and reopen-resize-02.json.
   Expanded native validation passes 26 checks at 75%, 100%, and 125% interface scale, at the
   current 125% Windows display scale, with Full/Reduced motion and text scaling. It checks
   WebView input revocation while hidden and restoration after reopening, in addition to
   placement and retained session identity: artifacts/pob/session-modal-native/modal-scale-01.json.
   Physical controller and multiple Windows display-DPI acceptance remain unverified.
2. **Live extraction proof — PoE2 verified; PoE1 sign-in pending.** Observe only declared trade-item response bodies in the
   authenticated WebView, without reading cookies/auth headers. Match displayed listing IDs.
   Verify both games' schema and upstream conversion on representative supported items. Record
   what is proven and unsupported; adjust the integration before building on assumptions.
   Captured ten actual PoE2 helmets, with 56 parsed modifier descriptions and 15 matching defence
   values. The strict adapter accepts nine and rejects one with unrevealed veiled modifiers that
   upstream omits. Live native comparison of Brood Corona, Soaring Mask passed and returned to
   the same official listing. Evidence: artifacts/pob/live-trade-validation/result-09.json,
   return-listing.png, comparison.png. The test used a copy of the user's build.
   Further upstream audit found the trade-text converter flattens PoE1 socket groups and omits
   influence and some PoE2 metadata. Conversion now invokes upstream ImportTab.ImportItem with
   a detached item destination, never the user's inventory or skill groups. API-shape normalization
   stays in the adapter; PoB still owns parsing, item rules, raw serialization, and calculations.
   Regression cases cover socket links, influence, crafted/fractured flags, corrupted unique weapons,
   rune/Bonded modifiers, Runic Ward, granted skill levels, and jewels. Two independently authored
   item-text comparisons produce identical stats and comparison output. Duplicate descriptions
   cannot conceal an omitted modifier. The actual captured helmet responses also pass the richer
   importer (nine accepted, one correctly rejected for unrevealed modifiers).
   Latest live native proof and visual inspection: artifacts/pob/live-trade-metadata/result.json,
   listing.png and comparison.png. The live test again uses a disposable copy of the user's build.
3. **Shared session page actions — implemented, native tests passing.** Define a minimal reusable contract, sealed adapter
   resources, exact origin/data boundaries, bounds, stale-document protection, user invocation,
   cancellation, cleanup, and renderer/controller integration. Pick concrete limits from payload
   evidence. Test wrong origins, navigations, superseded requests and worker retirement.
   Protocol 86 adds explicit OpenAdapted, sealed adapter script, exact HTTPS origins, and explicit
   JSON response path/size rules. Isolated-world actions require trusted clicks and foreground
   session input. CDP observes only responses from the admitted top-level frame and loader;
   late navigations are rejected. Native checks cover denied origin, real response delivery,
   page-world isolation, synthetic-click rejection, trusted selection, retained return, and
   navigation away. Evidence: artifacts/pob/session-modal-native/trade-actions-06.json (8 checks).
4. **PoB integration — implemented, functional tests passing.** In-page Compare in PoB, exact selected-item conversion, listing
   context, existing equipment comparison, explicit apply/discard, and return to retained listing.
   No build mutation on browsing, conversion failure, or cancellation.
   PoE1 passes 32 and PoE2 passes 33 conversion/workflow checks, including malformed response handling, draft/preview preservation, refusal to overwrite
   pending work, explicit discard, and restart restoring listing context with a fresh-session link.
   Shared broker checks (4) and managed/native protocol parity pass. Results live under
   artifacts/pob/trade-metadata-verified{1,2}.txt. Button colors/text sizing follow the host palette.
   Both engines also pass 36 existing persistence checks (work-state-final{1,2}.txt).
5. **End-to-end acceptance — PoE2 live workflow and native visual inspection complete;
   live PoE1 authentication and physical controller acceptance remain.** Exercise search/select/compare/apply/discard/return,
   close/hide/reopen/restart, repeated items, missing listings, expired login, both engines and
   representative special modifiers. Use actual native UI and visual inspection. Report any
   live-auth coverage gaps accurately. Finish with one normal candidate and evidence paths.
   Native action regression passes 8 checks on the final host: session-modal-native/actions-final-10.json.
   The current candidate is artifacts/pob/modal-scale-metadata-01, with trade-adapter-installation04 and
   trade-metadata-catalog. User profiles/logins are preserved. No purchase or seller message
   was sent. Marketplace screenshots have not been published; captures remain local evidence.
   Final comparison layout was visually inspected after restart (final-comparison.png): price,
   seller, trade method, return action, and pending Apply/Discard precede calculation details.
   Review corrected the adapter button background to the opaque composed theme surface, since
   a transparent item brush cannot safely be flattened over an unrelated website background.

## Remaining acceptance and explicit limitations

- Live PoE1 trade verification needs the separate widget profile to be signed in. Authentication
  has been requested; no credentials, cookies or login tokens are requested in chat.
- Physical controller testing and another Windows display-DPI configuration remain unverified.
  Normalized controller routing, input ownership, native focus return and interface scales are tested.
- Items with unrevealed/unrecognized modifiers are refused. Listings containing socketed jewels
  must be compared separately; seller gem groups never replace the user's skills during comparison.
- Exact listing scroll state is retained only while the session lives. After idle unloading,
  comparison work and listing context restore, and the search can be reopened with its saved login.

## Follow-up: repeated listing comparison and purchase context

The user rejected the draft/choose-slot detour and requested a complete browsing workflow,
two-column presentation, useful recovery after unloading, and reachable item actions.

- Compare now calculates immediately and opens a native comparison panel beside the retained
  website. A slot selector handles interchangeable equipment. Comparing another listing always
  starts from committed equipment, never from the preceding candidate. Only Equip in build
  commits; it does not purchase or change the game character. Manual item drafts remain protected.
- The last eight compared item copies, listing identities and chosen slots persist per build.
  Recent comparisons recalculate against the current committed build. Expand comparison shows
  item descriptions beside the complete upstream impact output, with controls above both scroll
  areas. Long areas have L3 access to comparison controls. Older saved previews are preserved;
  an explicit continuation action retains their item before enabling repeated browsing.
- Saved return actions narrow the official search by seller and base, following desktop PoB's
  account-filter approach. A widget adapter fragment locates the exact saved ID among results
  the website normally loads. No extra authenticated fetches, stored purchase tokens, automatic
  whispers, or purchase actions. If a listing disappears, its saved description remains available;
  a filtered search is not a guarantee of availability.
- Search defaults to Instant buyout, with all five upstream trade modes selectable and persisted.
  Browse trade is available without generating an equipment-upgrade query. Live inspection found
  that `method=psapi` also occurs on instant-buyout listings: it is no longer used to infer in-person
  trade. The adapter carries only a boolean derived from hideout-action availability, never the
  token itself. Old records without that evidence say to check purchase options on the site.
- Item tools use a 2x2 grid. Explicit sibling links fix Left from Item settings skipping Replace
  from clipboard. Native keyboard navigation confirmed the clipboard button is enabled and gets
  focus; disabled controls remain visibly dimmed.

Evidence: `artifacts/pob/trade-review-final{1,2}.txt` (43/44 checks),
`trade-review-recovery{1,2}.txt` (36 each). Native live PoE2 flow passes six checks in
`trade-review-native05/live-result.json`, including two actual listing selections, revisiting the
first candidate, expanded layout, and finding the exact saved listing through a narrowed search.
Visual captures under `trade-review-native04/evidence` cover sidebar, expanded and independently
scrolled columns; `trade-review-native05/evidence/found-listing.png` shows the located listing,
matching item identity, price, instant-buyout label, and gold fee. UI tests used copied build data.
Live PoE1 sign-in and actual purchase/controller acceptance remain unverified; no purchases or
seller messages were performed.

Legacy recovery was additionally exercised with a copy of the user's existing pre-change work
record. `trade-review-legacy/result.json` confirms that the exact draft and pending preview survive,
the base/name are recovered from PoB, and the continuation action keeps the item in recent comparisons.
`trade-review-legacy/evidence/restored.png` shows the matching saved listing after session recreation.
Final candidate: `artifacts/pob/trade-review-host03`, `trade-review-final-catalogd`, original
`guide-workflow` data/settings and persistent logins, with controller input enabled.

### Controller escape and reading-order correction

The side-by-side browser now uses ActivateToInteract while its comparison is visible, and
InteractOnFocus when it is alone. Native validation exercises A enter, B leave without closing
the modal, Right into the comparison, and hiding/showing the panel on the same session. All ten
live checks pass in `artifacts/pob/trade-order-native/live-result.json`; this is routed controller
input validation, not physical-gamepad acceptance.

The sidebar presents item/name, price/slot, essential controls, and main-skill impact first.
Complete PoB comparison lines and their formatting are preserved. Item descriptions and grouped
purchase details follow; secondary controls no longer occupy the initial reading area. X jumps
to item details, L3 returns to controls, and each new comparison reveals the top of its impact.
The expanded view retains independent columns. Visually inspected `current.png`, `details.png`,
and `expanded.png` under `trade-order-native/evidence`.

Replace from clipboard now distinguishes absent/non-text content, excessive length, unrecognized
item text, and recognized items without a compatible slot. Access failures keep their real
clipboard-busy/read errors instead of being mislabeled as invalid item data. Regression checks
cover non-text, empty, link, oversized, busy, valid, and protected-draft paths in both engines
(`trade-order-tests1.txt`: 52 checks; `trade-order-tests2.txt`: 53 checks).
Candidate uses `trade-order-host` and `trade-order-catalogb` with the original user profile.

### Approved next implementation scope

The user approved the following after reviewing desktop PoB's capabilities and its 20-listing
default / 10-to-100 configurable fetch range. These are approved work, not implemented features:

- Reuse upstream candidate evaluation, custom weights, and ranking. Start with a bounded explicit
  evaluation of 20 candidates, configurable up to 100, with progress and cancellation. Continue
  using structured results obtained through normal visible website browsing; this approval does
  not authorize purchasing, seller messaging, collecting session credentials, or a new OAuth flow.
  Explain that ranking covers evaluated candidates under chosen weights, not the whole market.
  Price-aware ranking requires valid conversion rates and must not silently compare currencies.
- Expose upstream custom stat weights and applicable advanced query-generation options, preserving
  upstream eligibility and formulas instead of duplicating them in the widget.
- Support empty-slot searches where upstream can infer the category; request category/base choices
  when the slot is ambiguous. Do not equip a placeholder into the user's build to enable search.
- Expose predefined modifier sources and searchable choices from PoB's Add modifier dialog for
  imported/existing items. The current adapter forces CUSTOM; include applicable Prefix, Suffix,
  and game-specific sources without replacing upstream eligibility/range handling. Keep arbitrary
  custom text as a separate option and preserve real-item identity, pending edits, and Undo.

### Comparison action cleanup and LS correction

Find seller's item replaces Browse listings in the fixed sidebar actions; its lower duplicate and
the Comparison controls header button are removed. Left from that action or the selectors returns
to the retained website without navigating; Equip's Left neighbor remains the adjacent Find action.
Find opens the narrowed seller/base search and moves focus to the browser. Show comparison is only
present while the panel is hidden, with an R3 shortcut so controller users can restore it while
automatic website interaction is active.

LS previously entered the entire remembered-child panel and could return to the same scrolled
child. It now explicitly enters the fixed controls group. New comparisons and Show comparison
also enter that group. Regression coverage includes the top action, directional link, and LS target;
native coverage exercises LS from the last control in the scrolled details.

Verification: `artifacts/pob/trade-actions-tests{1,2}.txt` passes 54/55 workflow checks.
`trade-actions-native03/live-result.json` passes 13 live native checks, including Left from the
new primary action, A/B/Right, LS from the bottom, and R3 restoring a hidden comparison. Native
tests wait for the requested controls focus to settle before sending the next directional input.
The final screenshot is `trade-actions-native03/evidence/final.png`. Candidate uses
`trade-actions-host03` and `trade-actions-catalog`, with the original data/settings and controller
input enabled. The broader approved ranking/search/modifier scope above remains pending.

### Trade routing and exit labels

Search trade and Browse trade website always use the widget's persistent overlay session,
regardless of the general-link browser preference. The trade page no longer includes that
preference; elsewhere it is labelled Open general links in and explicitly excludes trade.
The obsolete external trade intent-preparation path was removed; calculation progress and
cancellation use the existing widget operation flow. No host/intent API change was needed.

Finish comparing replaces Close comparison in the expanded view: it ends the current preview
and returns to Items without equipping it. Hiding the side panel remains a separate action.

Verification: trade-routing-tests1.txt passes 56 checks and trade-routing-tests2.txt passes 57,
including overlay routing with either preference, invalid price feedback, retained sessions,
and returning to Items without changing the saved build. Native search-page labels were
visually checked in trade-routing-search.png. The running controller-enabled candidate uses
trade-actions-host03 with trade-routing-catalog and the original data/settings.

### Next delivery order: useful trade recommendations

1. Add an explicit Evaluate results action for a batch of listings supplied by normal website
   browsing (20 by default, configurable up to 100). Reuse upstream conversion, calculation,
   weights and ranking. Calculate every item against the same committed build/slot/configuration,
   never against the preceding preview. Show evaluated/available counts, progress, cancellation,
   and excluded-item reasons. Do not imply the whole market has been evaluated.
2. Present a ranked shortlist beside the website with item identity, price, score under the chosen
   weights, important damage/defence/resource changes and downsides. Selection updates the existing
   comparison panel without navigating or scrolling the website. Keep full details, seller lookup
   and Equip explicit; preserve selection while results arrive. Recalculate or mark scores stale
   when the baseline, slot or weights change. Price-aware ranking requires valid exchange rates.
3. Expose upstream custom weights and applicable advanced search options behind compact advanced
   controls, retaining useful presets. Add empty-slot searches with category/base choices only
   when upstream cannot infer them. Never equip a dummy item to generate a search.
4. Expose upstream predefined modifier sources and searchable modifier choices for existing items,
   keeping custom text separate and preserving eligibility, ranges, identity, recovery and Undo.
5. Gate each delivery with both-engine calculation comparisons and full controller workflows:
   search, batch evaluation, shortlist navigation, detail scrolling, cancellation, seller lookup,
   changed build, hide/reopen and recovery. Inspect the native UI and do not buy or message sellers.

The immediate equipped-items LS fix removes the obsolete multiple-set prerequisite. Long lists
now jump to Equipment controls even with one set; controls offer LS back to the remembered list.
The default entry is Item inventory. Both quick-controls suites pass 116 checks
(`equipment-shortcut-tests{1,2}.txt`). Candidate uses equipment-shortcut-catalog with the existing
host and original data/settings; the user's restored comparison was preserved during replacement.

### Delivered: batch ranking, search controls, modifier sources and return URLs

The approved planning/editing additions are implemented for both widgets:

- Evaluate loaded website results in batches (20 by default; 50/100 available), with a virtualized
  shortlist beside the retained website, progress, cancellation, explicit target equipment,
  priority/limit controls, skipped-item reasons, and stable listing identities. Selecting a row
  opens its full comparison; returning restores shortlist focus. No automatic website scrolling
  occurs when changing comparisons. Upstream GetResultEvaluation and SortFetchResults own scoring
  and ordering. Every candidate uses the same committed build; changed builds/targets/weights
  mark scores stale. Recent comparisons can also be ranked.
- Upstream custom statistic weights and applicable mirrored/corruption/rune/talisman/Scourge/
  Eldritch weights, level and socket/link options. Search controls remain fixed above the scroll
  area with LS navigation. Empty builds and empty slots are searchable; ambiguous empty slots
  request an item category using a detached base reference, without equipping a dummy item.
- Existing/imported item Add modifier exposes upstream sources and searchable virtualized
  choices. Numeric descriptions use rich text; rolls, eligibility, item identity and Undo remain
  upstream. Custom modifier text is separate.
- The user's later decision supersedes removing Return to listings: retain it and persist the
  current public trade-search URL across unload. Find seller's item is the separate narrow lookup.
  Saved URLs exclude authentication paths, non-query parameters and action fragments. Ranked item
  copies and metadata persist per build; malformed ranking records cannot prevent the build loading.
- Shared host addBatchAction and SDK WebSessionPageActionBatchReader use trusted user actions,
  bounded ordered chunks, origin-scoped delivery and complete-batch validation. Page-location
  observation supports reusable return URLs. The host's former 14-level action parser rejected
  real batch metadata; it now uses the existing shared 16-level message budget. Unicode and long
  document URLs are included in wire-size accounting. No separate temporary-page API was added.

Validation evidence (under artifacts/pob):

- ranking-delivery-workflow1/2.txt: 63/64 checks, including custom-weight UI, explicit target changes,
  batch selection without navigation, restart, corrupted-ranking fallback and retained drafts.
- ranking-passed-items1/2.txt: 48/46 item checks, including modifier source selection, search,
  existing-item edits, identity and Undo. ranking-passed-trade-improvements1/2.txt: 15 checks each.
- ranking-native05/result.json: 13 native checks, including nested metadata, trusted actions,
  controller navigation/LS, virtualized selection, return flow and origin isolation.
- ranking-live04/result.json: 16 live PoE2 checks. Ten real listings evaluated in 2.5 seconds;
  controller selection and exact seller-item lookup passed. Authenticated live PoE1 was not tested.
- 100-item engine fixture: PoE1 13.56s, PoE2 7.54s; 20 items 2.91s/1.60s. Retained engine memory
  increased about 0.4/0.7 MiB. Both measured 0ms engine CPU during a 1.2s idle interval. These are
  fixture measurements, not guarantees for every build. Cancellation restores committed work.
- Shared SDK byte/depth boundary test and page-action-batches.cjs pass, including Unicode, long
  URLs, synthetic-click rejection, ordered 100-item delivery and oversize rejection.
- Native visual inspection: ranking-live04/evidence/ranked.png, ranking-live/evidence/search.png,
  and ranking-native03/evidence/modifiers.png. EngineUpdateTests now includes the new trade suite.

Limits: evaluation uses data already loaded by ordinary website browsing, up to the configured
limit; it does not claim to rank the entire market. Cross-currency price-aware ranking is withheld
without reliable exchange rates. Purchase actions remain on the official website. Exact web page
scroll restoration is not promised. No merge or release is part of this delivery.

2026-10-17 presentation follow-up: skipped-list errors omit Lua source paths and line numbers,
including previously saved entries, while preserving the item name and readable reason. Website
modals name the opened site (PoE1/PoE2 Trade, poe.ninja, pob.cool or hostname); existing supported
location events refresh the heading without navigating or replacing the web session.
`artifacts/pob/site-labels-final-workflow1.txt` and `site-labels-final-workflow2.txt` pass 74/75
checks. Native visual evidence: `site-labels-modal.png` and `site-labels-skipped.png`.
An intermittent Guide activation report recovered without a restart; the captured
`guide-recovery-20261017.log` shows legacy Guide polling enabling immediately before accepted
Guide events. The underlying detection cause is unconfirmed; no controller behavior was changed.

2026-10-17 next candidate: the ranked-list skipped section fits its content with a 120px
maximum and remains scrollable; the virtualized results list takes the remaining height.
Visual checks cover no skipped entries and one short message (`rank-space-final.png`,
`rank-space-one-skip.png` under artifacts/pob). Temporary layout-test data was restored.
The shared Guide fallback now discovers live XInput connections independently of GameInput
notifications. Native policy suites pass, including 171 Guide compatibility checks; the
WinUI timer/Settings fixture passes 31 checks (`guide-discovery-ui02/result.json`). The first
fixture run exposed a stale shortcut-control locator, corrected for the current Settings page.
Physical-controller acceptance and hidden idle measurements are recorded separately from
these automated checks. No Windows service, driver, or controller preference is changed.
The user confirmed repeated Guide open/close and reconnect on the final candidate. Its log
records 17 Guide events without any GameInput device-connection notification. The normal
120-second PoB unload completed. A subsequent 40.58-second hidden sample used 1843.75ms host
CPU (0.284% of this 16-logical-processor machine), with handles decreasing from 1653 to 1649;
this is an observed sample, not a before/after performance comparison. The user also confirmed
Guide reopening after the two-minute idle period; the lifecycle log shows a second worker start
and a return to interactive state. Evidence: `guide-physical-final.log`,
`guide-unloaded-performance.json`, and `ranking-candidate-state.json` under artifacts/pob.
