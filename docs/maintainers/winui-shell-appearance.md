# WinUI shell surface appearance and desktop backdrop

`OverlayAppearancePolicy` preserves `WidgetSurfaceAppearance.h` precedence:
the exact widget-ID override wins over the global override, including `Widget`
(use the authored declaration). Theme preserves theme panel brush alpha and
opacity; Solid preserves its RGB and forces opaque fill; Transparent removes
the host-owned panel fill. High Contrast, reduced transparency, unavailable
transparent composition and zero-opacity backdrop provide the same ordered
Solid fallbacks. The declared/requested values remain distinct from the result.

The host background is a Border surrounding the existing `WidgetHost`. It does
not wrap or replace individual widget controls, apply subtree opacity, alter
input scope or own focus. The former outer opaque shell card is cleared so it
cannot defeat transparent widget surfaces. Existing widget-authored backgrounds
remain widget-owned. Full shell chrome styling and radial layout remain separate.

`ReadShellStylesAsync` uses the existing read-only `get-platform-appearance`
request without a display report. Its bounded, frozen palette passes the same
computed-style validator as ordinary views. Panel and backdrop RGB come from
these bridge-resolved theme roles. Failed refresh retains the last accepted
palette; startup has the native host's documented default panel and black
backdrop. Concurrent palette refresh publishes only the newest local request.
High Contrast panel paint uses Windows' current system background color.

`OverlayDesktopBackdrop` owns one ordinary WinUI Window with
`TransparentTintBackdrop`, an alpha background brush, no caption/border, no
task-switcher entry and WS_EX_NOACTIVATE. AppWindow supplies native placement
and nonactivating show/hide; SetWindowPos places it immediately behind the
overlay without activation. It covers the selected physical monitor's outer
bounds, independently of widget size, zoom or corner placement. Backdrop opacity
is the existing 0–0.8 setting; as in the native host, reduced transparency forces
solid widget paint without changing this separate desktop dimming amount.
Pointer press on the scrim asks the existing shell hide path to dismiss; there
is no new controller reader or second controller owner.

Appearance, accepted surface publications, monitor/position/visibility changes
and Windows contrast events update native paint. Hide and teardown retire the
backdrop; zero opacity leaves it hidden. Native composition applies the brush
alpha, without CPU screenshots, custom rasterization or frame polling. The
background window is part of this shell lifetime and closes with the main window.

Validation modes: pure `OverlayAppearancePolicyTests` cover precedence and
fallbacks; `ShellPaletteSessionTests` uses a real scripted bridge channel to
check request shape, palette values and rejection of foreign/duplicate roles;
`--validate-shell-appearance` checks native pixel blending, brush reuse, focus,
z order, opacity, accessibility fallback and backdrop hide/reopen. The three
specimens deliberately show Theme/Solid/Transparent above the same blue substrate.

Remaining boundaries: this does not implement full shell typography/tray theme
role mapping, widget-switch animations, radial chooser, pinned backgrounds or
desktop blur. Physical multi-monitor hotplug/Alt-Tab/Guide acceptance remains
part of the integrated shell gate. The local native fixture alone is not that
acceptance.
