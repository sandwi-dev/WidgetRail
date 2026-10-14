# Browser D-pad geometric navigation

## Scope and authority

User requested autonomous implementation after the agreed design: D-pad jumps geometrically between webpage controls, left stick remains free pointer, right stick scrolls, A uses existing native click/editing. Work on `codex/browser-geometric-navigation` in the existing platform-feature-roadmap worktree, based on release `326aaf98`. Preserve main's unrelated documentation edit, the installed profile/sign-ins/Guide shortcut, and unrelated work. May close/restart for synthetic validation and launch a complete candidate. Do not access credentials, send messages/join calls, capture personal windows or run blocked Discord probes. No merge, push, release or installers requested for this feature.

## Design

- Shared `BrowserSurface` implementation covers Browser, authored webapps, hosted pop-outs, pinned/expanded surfaces and restricted provider documents. Native Chromium/Windows chooser windows retain their existing free-pointer controls; this feature targets webpage DOM content.
- D-pad chooses visible enabled interactive controls by direction, alignment and distance. Retain native click semantics: a jump moves the controller circle and sends mouse movement, never clicks or invokes script handlers. Keyboard still opens only on A.
- Include native controls, links, ARIA interactive roles, actionable custom controls, contenteditable roots, sensible click/pointer affordances and open shadow roots. Bare keyboard focusability is not action evidence. Filter disabled/inert/hidden and occluded targets; use a verified hit-testable point rather than assuming the center is clickable. Avoid duplicate nested targets.
- Reuse the host's owned-frame CDP mechanism for frames. Bound node/frame scanning and frame depth; reject unsupported frame transforms rather than inventing coordinates. No Discord selectors, credentials, page text or arbitrary widget scripts.
- When no visible target exists in the requested direction, perform a bounded directional scroll in the relevant scroll container, then reevaluate. Preserve free pointer as the fallback for canvas and inaccessible custom interfaces.
- One navigation operation at a time, at most one pending direction. Invalidate on free movement, click/drag, scrolling, zoom/viewport changes, page navigation, interaction loss and disposal. Revalidate target geometry and current ownership before moving. No persistent page listeners or full-tree scans on neutral controller frames.
- Update controller guides and browser/authoring docs. Keep existing popup, keyboard, toolbar and B behavior.

## Validation plan

1. Pure geometry tests: rows/grids, irregular placement, alignment vs diagonals, overlapping/nested controls, deterministic ties and boundaries.
2. Synthetic live WebView tests: native/ARIA/custom controls, disabled/hidden/overlaid elements, open shadow DOM, scrolling and nested scrolling, dynamic removal/layout, same-origin and cross-origin frames, zoom, A/native activation, free-stick cancellation, held input, and disposal/navigation races.
3. Existing browser/live keyboard/pop-out checks as relevant. Review resource lifetime, bounds, privacy, latency and fallback behavior. Record measured limitations rather than implying universal DOM coverage.
4. Build and launch a complete candidate with preserved profile for physical testing; leave a concise checklist. No account-specific behavior inferred from fixtures.

## Progress

- Inspected input routing: browser `MoveFocus` currently nudges the pointer; shell already distinguishes D-pad from left-stick pointer input. Existing live editing provides owned-frame CDP world/session access and pointer-coordinate conventions.
- Implementation, automated validation, review and complete candidate delivery finished. Physical acceptance remains with the user.

### First implementation and evidence

Implemented bounded isolated-world DOM scans, pure directional scoring, hit-tested landing points, open-shadow traversal, owned frame/session traversal, directional scrolling, active scroll-container preference, one-flight/one-pending input scheduling, stale-work invalidation and shared guide updates. DOM snapshots use per-transaction CDP object groups so cancellation cannot strand unobserved object IDs; owned frame sessions are retired, including late attachment responses. No page text or field values are exported by navigation.

`frontend-02` builds with zero warnings/errors. `shell-tests-01` passes all 220 tests including five new geometry tests. `native-01.json` passes 25 live checks: rows/columns, no accidental click, trusted A activation, disabled/hidden/covered/custom controls, removal, open shadow DOM, nested scroll, same/cross-origin frame clicks, free-pointer cancellation, held-drag protection and presentation loss. Initial simple-page navigation calculation measured about 20 ms cold and 1.5–2.1 ms warm (not a universal end-to-end latency claim). Build 01 only failed the new fixture's missing WinRT `partial` modifier; corrected before launch.

The installed preview.30 process was closed normally; original launch metadata, default profile path, settings hash and shortcut are recorded in `artifacts/browser-spatial/profile-before.json`. Only synthetic pages have been opened. Next: extended fixtures for cross-site/OOPIF ownership, zoom, menus, text entry, dense pages, partial/parent-frame occlusion and scroll-container behavior; then review and candidate delivery.

### Final implementation validation

- `shell-tests-02`: **221 passed, 0 failed, 0 skipped**, including six geometry tests. The full-shell fixture exposed a wide, overlapping text field outranking the adjacent button. Scoring now includes forward center distance; a regression test preserves the correction.
- `native-04.json`: **57 live WebView checks passed** against the corrected implementation. Includes trusted native A clicks, hover submenus, keyboard entry/navigation, dynamic removal, inert/disabled/occluded controls, label proxies, interactive ancestors, escaped fixed-position controls, open shadow roots, nested lists, RTL scrolling, owned same-origin and OOPIF frames, scaled frames, parent-frame occlusion, zoom, dense-page bounds, cancellation and held-input pressure.
- `shell-02.json`: **127 full-shell checks passed**, ending at shutdown. Actual controller-frame routing selects and clicks the adjacent control in main, expanded, pinned, hosted-popup and pinned-pop-out placements. Existing popup choices, keyboard, notifications, layers and shutdown checks also pass.
- Keyboard regression: **18 live editing and 105 buffered-entry checks passed**. Same-origin and cross-origin frame fixtures also confirm native input delivery, reopening, and stale-frame rejection. Clipboard checks were deliberately excluded; no account content was used. Frame fixtures record explicit state assertions rather than a `checks` array.
- Current Debug frontend (`frontend-07`) builds with zero warnings/errors. Reviewed cancellation, reference/session cleanup, bounded input queues, owned-frame access, privacy and existing navigation/toolbar/dialog routing; no remaining blocking findings. `git diff --check` passes.

On this synthetic fixture, the final run measured about 25 ms for the cold simple jump, 1.8–2.7 ms for warm simple jumps, 13 ms for an OOPIF jump, 23 ms for a bounded 13,000-node scan, and 64 ms for RTL scrolling plus rescan (including the deliberate 45 ms layout allowance). These are navigation-calculation measurements, not universal input-to-display timings.

### Limits and physical acceptance

No account-dependent Discord probing was performed. Canvas interfaces, closed-shadow internals without exposed host semantics, unmarked click handlers, complex transformed frames and content beyond scan limits may require left-stick pointing. Native screen/window chooser controls retain their existing free-pointer behavior. Navigation is bounded and cancels stale results rather than promising a target on every page.

Physical checklist for the candidate:

1. D-pad between nearby buttons, links and fields; A should activate only the chosen target.
2. Override with left-stick movement; use right stick to scroll. Confirm holding A still drags without D-pad teleportation.
3. Jump to a field, press A, and edit with the existing keyboard; verify keyboard D-pad and B routing.
4. Try main, expanded, pinned and hosted pop-out views, plus a scrolling list and hover menu.
5. Confirm the Guide shortcut, Website controls and existing sign-ins remain available.

Build the complete local Developer candidate from the committed branch, using the normal `%LOCALAPPDATA%\WidgetRail` profile and initially opening SDK Gallery. Do not reinstall community widgets, change sign-ins, merge, push, release or create installers. Candidate location and launch evidence belong in `artifacts/browser-spatial`; physical acceptance remains the user's.

### Candidate delivered — 2026-10-14

Built complete Production/Developer payload folders from implementation commit `b9554ebd`; this was a local candidate build, not a release or installer. Catalog, integrity and icon validation passed for 11 production and 15 developer widgets. Developer candidate is running from `artifacts/browser-spatial/candidate-01/0.1.0-preview.31/WidgetRail-Developer-0.1.0-preview.31-win-x64/OverlayFrontend.WinUI.exe`, initially on SDK Gallery (launch PID 28176).

UI Automation confirms the responsive SDK Gallery, tray and Guide close hint. `profile-after.json` confirms the platform settings hash is unchanged and `openShortcut` remains `guide`. Existing profile/browser storage was reused without reading credentials or cookie databases. No installation, registration, startup modification or community-widget reinstallation was performed. Main's unrelated `docs/implementation-agent-goal.md` modification remains preserved. Launch metadata and UI evidence are in `candidate-launch.json` and `candidate-uia.txt` under `artifacts/browser-spatial`.

### Physical feedback correction — 2026-10-14

The user reported jumps into the middle of a chat panel, via a resize divider, and Down returning to a top bar. Authorized read-only Chrome inspection returned element attributes and geometry only: the message list exposes `role=list` / `tabindex=0`; the resize divider exposes `role=separator` / `tabindex=0` / `cursor=ew-resize`; neither exposes an inline click attribute. The scrolling parent is a group, and message rows expose article roles. No message text, credentials, cookie databases, application state or site-specific selectors were read into the implementation. The Chrome page was not clicked, scrolled, edited or reloaded.

Corrections are generic: remove bare tabindex as action evidence; require ordinary landing points to advance from the actual pointer; use DOM membership for scroll scope; preserve the exact scrolled element and source reference across layout; rescan its subtree and owned child frames rather than using the moved source coordinate to select a new global source. Missing/replaced regions and empty gaps leave the pointer in place. References remain in the existing bounded transaction/object-group lifetime. Custom semantic controls, explicit click attributes and pointer-cursor actions remain supported.

New regressions cover focusable lists/panels/separators, hover-only rows, wide source controls, overlapping foreign controls, offscreen source anchors, virtualized source removal and scrolling within an owned cross-process iframe. `shell-tests-03` passes 222 tests (0 failed/skipped); `native-05.json` passes 68 live browser checks; `shell-03.json` passes all 127 full-shell checks through shutdown. `frontend-10` builds with zero warnings/errors. Review and `git diff --check` found no blocking issues. Existing bounded scans, cancellation and cleanup remain in effect. Measured warm simple-page calculations remain about 2–3 ms; scroll/rescan cases are about 51–55 ms including the 45 ms layout allowance. These synthetic timings are not universal page performance guarantees.

Replacement candidate delivery is complete. `candidate-02` was built from `572f9a30`, passed catalog/integrity/icon checks for both payload variants, and is running on SDK Gallery (PID 31892) using the existing profile. `candidate-02-verification.json` confirms responsive UI, Gallery and Guide availability, unchanged platform settings, and no new frontend error entries. No merge, push, release, installer or account interaction occurred.

Physical acceptance should concentrate on skipping the chat/list/resize scaffolding, scrolling through message gaps without leaving the chat pane, and preserving useful native/custom actions. The divider is still usable with the free pointer. No Discord-specific code or DOM selectors were added.

### Row scrolling, deferred hover and pointer motion — 2026-10-14

User authorized parallel implementation of three cohesive changes. Row scrolling and pointer visuals were delegated with disjoint source ownership; the primary agent owns hover/input routing, integration, validation and delivery. No merge/push/release/installers are authorized.

- Scroll units are semantic list items/articles/table rows, independent of actionable controls. Measure rows near the advancing edge and their neighbors first; bound fallback discovery to 2,048 nodes, 128 rows and about 8 ms. Reveal the clipped/next row with up to 12 px margin; cap oversized-row advances at 65% of visible extent, use 35% for unknown markup. No background scans or observers. Two animation frames settle layout/virtualization, with a 90 ms timeout and cleanup, replacing the unconditional 45 ms wait.
- Only newly revealed controls may win after auto-scroll. Otherwise move the circle to a hit-tested point near the advancing edge in the actual scroller. Rows remain non-clickable. Preserve owned-frame checks, scope identity, real list-boundary exit, and left-stick/right-stick behavior.
- Track actual D-pad held state between sparse native repeat events. Intermediate geometric destinations move the visual circle without delivering mouse movement to the page. Release commits the latest completed destination (including release before async navigation finishes). Keep the previous page hover so an already-open submenu remains navigable. A finishes motion and synchronizes native movement before pressing; analog takeover/cancellation restores ordinary routing. Early-return input paths retire the previous browser gesture.
- Compositor-only 110 ms D-pad glide and a short speed-sensitive analog trail (7 pooled dots, 100 ms fade, 32 DIP maximum length, maximum opacity .2). New jumps replace motion; no managed per-frame animation loop. `Input/PointerMotionOptions.cs` owns code-configurable tuning. System reduced motion suppresses both effects; high contrast suppresses the trail. Hidden/disposed surfaces clear effects and pending batches.

The first integrated build (`frontend-12`) passes 102 live checks (`native-06.json`), including row scroll and pointer-motion scenarios. Review then corrected old-hover removal to preserve existing submenus. Final build `frontend-13` has zero warnings/errors; `native-07.json` passes 104 live checks, including the open-submenu regression. `shell-04.json` passes all 137 full-shell checks through shutdown, verifying hold-between-repeat-events and release delivery in all five browser placements. The unchanged managed geometry/shell suite passes 222 tests (`shell-tests-04`, 0 failed/skipped). Source review and `git diff --check` found no remaining blocking issues. No account pages or messages were used for these changes.

Candidate delivery is complete. `candidate-03` was built from `6dda4c83`, passed catalog/integrity/icon checks for 11 production and 15 developer widgets, and launched on SDK Gallery (PID 4768) with the existing normal profile. `candidate-03-verification.json` records responsive UI, Gallery and Guide availability, unchanged platform settings and no new frontend error entries. No merge, push, release or installer creation occurred; main's unrelated documentation modification is preserved.

Physical checklist: text-only/variable/tall chat rows, hold D-pad past a hover menu then release, navigate into an already-open submenu, interrupt glide with A or the left stick, inspect the subtle analog trail, and repeat in a pin/pop-out. Appearance/timing acceptance remains the user's; automated checks prove compositor lifecycle and input ordering, not subjective motion quality.

### Remove analog trail — 2026-10-14

Physical feedback: the analog trail looks laggy. Removed the trail layer, particle pool, opacity animations, sampling/expiry logic, tuning constants and trail-only fixture assertions. Left-stick movement remains immediate and interrupts an active D-pad glide. D-pad glide, deferred hover and row-aware scrolling remain unchanged. Current user/reference documentation now describes trail-free analog movement. `frontend-14` builds with zero warnings/errors; `native-08.json` passes 102 live checks including analog takeover, glide, click synchronization and reduced motion. Review and whitespace checks pass. `candidate-04`, built from `7ef887d9`, passed both catalog/integrity checks and is running (PID 1396) with the existing profile. Launch verification confirms a responsive browser UI, Guide availability and unchanged platform settings. No merge, push or release occurred.

### Stable source geometry — 2026-10-14

User supplied a recording of inconsistent jumps around a control and an opening/closing hover panel. The recording does not expose the pressed directions; an optional clarification was requested. Inspection found a generic source-identity defect, reproduced independently: Up from a 240x40 semantic button background chooses one neighbor, but from its 24x24 decorative pointer-cursor span chooses another. `native-12-source-repro.json` records the failure and `.source.json` records both competing source rectangles. This establishes the defect, not that every differing jump in the recording has this cause; popup occlusion can legitimately change reachable candidates.

Earlier probes were inconclusive because the standalone fixture's physical-pixel window size produced a clipped CSS viewport at the current display DPI. The fixture now uses a DPI-aware client size and asserts a usable CSS viewport before geometry checks. `native-09`/`native-10`/`native-11` are not valid source-stability proofs.

Pointer-cursor-only descendants of actionable ancestors now remain decorations. Explicit native/ARIA/click-attribute children retain independent semantics. Per-snapshot eligibility caching bounds repeated ancestor checks; revalidation refreshes the cache. Ranking weights, scrolling, controller routing and animation are unchanged. `frontend-19` builds with zero warnings/errors; `native-13.json` passes all 106 live checks, including the new source stability regression and existing nested/custom controls, frames, scrolling and hover checks. Source review and whitespace checks pass. Replacement candidate delivery is in progress; no Discord-specific markup or production account probes were used.

User clarified that the inconsistent movement was **always a Left tap**, sometimes jumping upward. A second synthetic reproduction (`native-14-truncated-repro.json`) confirms a closer match: with 13,000 earlier offscreen nodes, the scan found one far above-left button, missed the source/adjacent toolbar, and returned that far button with `Truncated=true`. The first source-only candidate (`candidate-05`) was built but deliberately not handed off after this clarification.

Discovery now seeds the current control and bounded neighboring DOM branches before the global scan (up to 512 local visits / about 6 ms; global 12,000-node / approximately 20 ms total scan budget retained). Processed elements are deduplicated, including shadow traversal. Origin identity is explicit rather than inferred from reverse discovery order. When a frame's global scan truncates, its nonlocal candidates/frames are excluded; truncated transactions reject departures from the source's row/column. This prevents a partial snapshot from turning Left into an unrelated diagonal jump while retaining the adjacent toolbar controls. Safe fallback remains free pointing/scrolling when local coverage is insufficient.

`frontend-22` builds with zero warnings/errors; `native-16.json` passes 109 live checks, including the reproduced late-toolbar case and refusing the diagonal when its adjacent control is hidden. `shell-tests-05` passes 222 managed tests (0 failed/skipped). `shell-05.json` passes all 137 full-shell checks through shutdown. Review and whitespace checks pass. Combined `candidate-06` from `8a63495d` passed both package catalogs/integrity checks and is running (PID 25124), initially on SDK Gallery. Verification confirms responsive UI, Guide availability, no widget failure and unchanged platform settings. Physical retest: repeated Left taps from the rightmost lower-row control, with its hover panel both open and closed. No merge, push, release or installers were created.

### Allow useful diagonal navigation — 2026-10-14

Physical feedback found the truncated-scan row/column safeguard too restrictive. Removed the mandatory perpendicular-axis overlap gate. Neighborhood-first discovery, stable source identity, local candidate retention on truncation, direction checks and ordinary distance/alignment scoring remain. Offset controls and leaving a toolbar are again valid destinations. Updated the live regression to cover horizontal/vertical diagonal neighbors on a truncated page, while retaining the original late-toolbar neighbor regression. `frontend-23` builds with zero warnings/errors; `native-17.json` passes 114 live checks. Source review and whitespace checks pass. `candidate-07` from `e394b8f3` passed both catalog/integrity checks and was launched with the existing profile (PID 448). Launch verification confirms a responsive UI, Guide availability and unchanged platform settings. No merge, push or release occurred.

### Keep valid targets beyond the local pass — 2026-10-14

User still could not navigate Down from a channel-status control. Authorized read-only Chrome inspection retrieved only channel-control attributes/geometry (the Chrome state did not expose the exact status control shown in the overlay screenshot); no website interaction was performed. Source review identified the remaining `local`-only filter on truncated snapshots. A synthetic status control inside five wrappers with a visible button below reproduced the failure (`native-18-nested-repro.json`): the broad scan's valid neighbor was excluded, and navigation scrolled instead of selecting it.

Removed the local-only candidate/frame exclusion and its unused metadata. Neighborhood-first enumeration remains an optimization, not a reachability restriction. All discovered controls receive ordinary geometric ranking and hit revalidation, without increasing the bounded scan budget. The new nested-source regression complements the earlier late-DOM toolbar, decorative-icon and diagonal cases. `frontend-25` builds with zero warnings/errors; `native-19.json` passes 116 live checks. Source review and whitespace checks pass. `candidate-08` from `a6178e46` passed both catalog/integrity checks and is running with the existing profile (PID 27228). Launch verification confirms responsive UI, Guide availability and unchanged platform settings. Physical retest is Down from the status action in the supplied screenshot. No merge, push or release occurred.
