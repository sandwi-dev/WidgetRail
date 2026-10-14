# Notification experience

## Scope

Replace Guide-adjacent status text with a focusable bell and pending-count badge beside the clock. Present a compact pending panel and a shared notification card design for quiet banners. Expired page callbacks must never target a replacement page: open the owning widget instead, with no blocking warning. Preserve existing preferences and shared native popup/pin layering.

## Implemented

- Bell and controller entry, including radial and tray routes.
- Anchored panel using the existing host dialog lifecycle, focus restoration, input neutral boundary, and transient layering.
- Shared icon/source/time/title/body card; muted Session ended state.
- A opens, X dismisses, B closes; Clear all is a secondary footer action. Arrivals update in place without taking selection.
- Stale activation opens the widget normally; missing widgets are dismissed with a quiet inline message.
- Measured banner height, quiet timings retained, reduced-motion-aware fades.

## Validation

- Managed suite: 226/226 passing, including stale fallback, exact live callback, repeated-open and replacement races.
- First native shell run: 143/143 passing, including native callback lifetime, controller actions, stale/missing widgets, quiet banner timing, popup pinning, renderer recovery and shutdown.
- Visual follow-up: anchor the panel to the bell's actual horizontal position; reduce native dialog padding/minimum height and remove default Close emphasis.

- A native run caught a bell focus-transfer regression during fullscreen teardown. Explicit keyboard/pointer admission replaced automatic GotFocus ownership. Another caught native ContentDialog template replacement undoing early placement; the template hook plus post-open dispatcher layout now anchors the final template.
- Fourth native run: 147/147 checks passed, including anchored geometry, live appended cards, bell focus restoration, and the original full webapp regressions. Synthetic panel/banner images reviewed.
- Visual review found catalog icon DataContext inheritance could clear the explicit descriptor; cards now set a local descriptor DataContext, matching tray usage. Final native check adds controller Clear all coverage.

- Final notification run `artifacts/notifications/shell-05.json`: 149/149 passed. Banner raster confirms the widget icon is visible; pending/stale/empty/inline-unavailable rasters reviewed. Controller Clear all keeps the panel open and B restores bell focus.
- Managed suite `tests-03.binlog` / `notifications-tests.log`: 226/226 passed. The rail now budgets space for both clock/connectivity and bell; small layouts retain just the bell before dropping the status area. Existing geometry checks cover bounded widths and icon capacity.
- Review covered callback lifetime and duplicate activation, focus admission/restoration, clear-all input, hidden-banner text cleanup, icon loading ownership, bounded list allocation, template/resize placement, theme/high-contrast palette use, reduced motion, automation labels and unsubscribing panel listeners. No remaining blocking findings. Real-account notification emission remains physical testing.
- Existing profile settings hash is unchanged after synthetic validation. No credentials, personal windows, real messages or calls were used.

Remaining: complete candidate build and launch with existing profile. No merge, push, installers or release until separately requested.
