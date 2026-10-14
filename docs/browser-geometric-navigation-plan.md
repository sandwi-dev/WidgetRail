# Browser D-pad geometric navigation

## Scope and authority

User requested autonomous implementation after the agreed design: D-pad jumps geometrically between webpage controls, left stick remains free pointer, right stick scrolls, A uses existing native click/editing. Work on `codex/browser-geometric-navigation` in the existing platform-feature-roadmap worktree, based on release `326aaf98`. Preserve main's unrelated documentation edit, the installed profile/sign-ins/Guide shortcut, and unrelated work. May close/restart for synthetic validation and launch a complete candidate. Do not access credentials, send messages/join calls, capture personal windows or run blocked Discord probes. No merge, push, release or installers requested for this feature.

## Design

- Shared `BrowserSurface` implementation covers Browser, authored webapps, hosted pop-outs, pinned/expanded surfaces and restricted provider documents. Native Chromium/Windows chooser windows retain their existing free-pointer controls; this feature targets webpage DOM content.
- D-pad chooses visible enabled interactive controls by direction, alignment and distance. Retain native click semantics: a jump moves the controller circle and sends mouse movement, never clicks or invokes script handlers. Keyboard still opens only on A.
- Include native controls, links, ARIA interactive roles, focusable custom controls, contenteditable roots, sensible click/pointer affordances and open shadow roots. Filter disabled/inert/hidden and occluded targets; use a verified hit-testable point rather than assuming the center is clickable. Avoid duplicate nested targets.
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
