# WinUI shell sizing checkpoint

The initial shell used one `1200 × 860` size multiplied by the global interface
scale. That changed the HWND extent without scaling controls, ignored authored
surface policies and per-display settings, and always anchored on its own window.

This checkpoint uses the existing `PlatformSettingsStore`, `DisplayScalePolicy`
and bridge appearance request. It adds no settings document or widget API.

## Ownership and policy

- `WindowDisplayContext` in the existing Windows display provider reads active
  source/target connections for the selected HWND. It shares the provider's CCD
  interop and constructs the same bounded connection token as the original host.
  It never changes display configuration or Windows scaling.
- `WidgetPresentationSession.ResolveDisplayAsync` sends that context through
  `get-platform-appearance`. The bridge resolves physical monitor identity using
  its existing EDID/connection policy and reports the active display to Settings.
  The frontend uses that returned physical ID with `DisplayScalePolicy.Resolve`;
  a connection token is not used as a saved-sizing key.
- `OverlaySurfaceSizing` applies the existing Compact/Standard/Wide defaults,
  atomic preferred/minimum hints, independent Preferred/Content/FillAvailable
  axes, and text-scale reflow allowance. Native WinUI supplies bounded intrinsic
  measurement for Content. Work-area constraints win over authored minima.
  Current WinUI chrome is measured rather than copying native renderer geometry.
- `OverlayScaleRoot` measures/arranges its one child in available design DIPs and
  applies the matching WinUI presentation transform. Windows DPI remains native;
  interface zoom remains an independent user preference. A constrained viewport
  reflows at its actual design size instead of shrinking a fixed-size canvas.
- The existing platform adapter still computes the final physical HWND bounds.
  Center/BottomLeft/BottomRight, work-area margins, negative coordinates and DPI
  remain host policy. The input adapter's existing remembered foreground target
  selects the monitor on Guide reopen; there is no additional input backend.
- Display/settings/surface notifications schedule bounded dispatcher work.
  Physical identity discovery is off the UI thread, unchanged placement does not
  call MoveAndResize, and disposal unregisters global display notifications.

`Overlay.Status` UIA HelpText includes the selected physical display ID, effective
zoom, surface hints, admitted extent and measured chrome for diagnosis.

## Evidence

- Shell pure policy suite: 16 passing tests, including mixed DPI/zoom, tiny
  viewports, finite single-pass content measurement, hints and physical-ID scale
  lookup.
- Presentation session: 76 passing tests, including the existing display-report
  wire shape, physical-ID response and invalid-path rejection.
- Display provider/widget suite: 18 passing checks; separate live read-only
  identity probe resolved the current window monitor and saved physical key.
- `--validate-shell-sizing`: 13 native checks verify actual measured viewport,
  transformed control bounds, retained focus, and constrained reflow at 50%,
  100%, and 125% interface zoom. Screenshot inspected.
- Actual Playnite workspace opened at Windows 125% DPI. Its published
  FillAvailable surface occupied the safe work area. A separate test profile's
  saved 75% interface scale resolved through the physical monitor ID: UIA text
  height changed from 24 to 18 physical pixels while the fill surface stayed
  within the same work area. Both screenshots inspected; all test processes
  closed. No installed profile or Windows display setting changed.

## Remaining work

TextScale is resolved and forwarded in appearance and affects the surface reflow
allowance; the presentation adapter still needs full typography scale projection.
Surface appearance overrides, desktop scrim, fixed tray/window arrangement,
radial/reorder/pinning, media surfaces and controller guide are separate shell
work. A full physical mixed-monitor Guide sequence, monitor unplug/replug, taskbar
relocation, and Playnite cursor/modal acceptance at scale are not covered by these
checks. A wide FillAvailable widget now really fills an ultrawide work area;
authored content can still choose its own narrower maximum width.
