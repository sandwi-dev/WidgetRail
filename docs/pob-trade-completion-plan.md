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
- No YouTube integration, bulk listing evaluation, or new OAuth integration in this work.

## Current evidence and limitations

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
   This does not yet establish physical controller or multiple display-DPI acceptance.
2. **Live extraction proof — pending.** Observe only declared trade-item response bodies in the
   authenticated WebView, without reading cookies/auth headers. Match displayed listing IDs.
   Verify both games' schema and upstream conversion on representative supported items. Record
   what is proven and unsupported; adjust the integration before building on assumptions.
3. **Shared session page actions — pending.** Define a minimal reusable contract, sealed adapter
   resources, exact origin/data boundaries, bounds, stale-document protection, user invocation,
   cancellation, cleanup, and renderer/controller integration. Pick concrete limits from payload
   evidence. Test wrong origins, navigations, superseded requests and worker retirement.
4. **PoB integration — pending.** In-page Compare in PoB, exact selected-item conversion, listing
   context, existing equipment comparison, explicit apply/discard, and return to retained listing.
   No build mutation on browsing, conversion failure, or cancellation.
5. **End-to-end acceptance — pending.** Exercise search/select/compare/apply/discard/return,
   close/hide/reopen/restart, repeated items, missing listings, expired login, both engines and
   representative special modifiers. Use actual native UI and visual inspection. Report any
   live-auth coverage gaps accurately. Finish with one normal candidate and evidence paths.

## Unresolved engineering details

Exact SDK API shape, adapter execution isolation, response/result limits and retention, schemas,
and native placement correction remain engineering decisions, not previously approved designs.
Choose the smallest shared mechanism supported by evidence, without adding a generic automation
framework. Update this plan with implemented versus verified status as work proceeds.
