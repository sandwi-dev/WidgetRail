# WIDGE-240 explicit full-widget pinning

Base: 8b56a90a. Branch: codex/explicit-full-widget-pinning.

`pinningSupported` enables the package's authored and compact-media pinned
layouts. The separate `fullWidgetPinningSupported` manifest flag defaults false
and requires pinning support. Only bundled Now Playing opts into Full widget;
Spotify and YouTube use the default false with their existing installed packages.
No widget-ID-specific behavior is introduced.

The bridge projects the flag to the native host and rejects unsupported full-view
input/selection. The native layout catalog only adds Full widget for an explicit
opt-in. Empty catalogs cannot create a fallback window, and removal of the last
available layout retires an existing pin. Saved full-widget selections choose
an available authored layout instead when full-widget support is absent. Catalog
revocation retires unsupported pins. Authoring documentation describes both flags.

## Verification

- WidgetSdk.Tests: 115/115, including omission, explicit opt-in, invalid JSON
  types and rejection of full-widget support without pinned-surface support.
- WidgetSurfaceCoordinatorTests: 367 checks passed. Covers custom-only layout
  cycling, saved Full widget selection, empty/removed layout catalogs, reserved
  layout identity, catalog revocation, and existing pinned behavior.
- WidgetBridgeCatalogTests: passed, including metadata parsing/defaults,
  contradictory flags, wrong types, and 13 embedded-media boundary cases.
- Complete Release native host and coherent managed runtime publication: passed.
- WidgetBridge.Tests: 129/129. Includes default/explicit descriptor projection,
  denial of undeclared full-view actions, and full-trust/bundled catalog checks
  proving Spotify stays custom-only and Now Playing opts in.

## Physical acceptance and closure

The user accepted candidate 34a1d54823a25ba8e1794d67ef47ad85864c9f31 on
2026-09-12. Only Now Playing opts into Full widget; YouTube and Spotify retain
their existing custom and compact-media layouts.

The accepted production is unchanged. This documentation-only successor records
acceptance and accompanies fast-forward integration into main and WIDGE-240
closure. The automated results above remain the accepted candidate's evidence;
no tests were rerun for this documentation-only change. No push was requested.
