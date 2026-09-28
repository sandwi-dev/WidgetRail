# WinUI radial widget chooser

The configured `AppearanceSettings.WidgetSwitcher` controls the host chooser.
Rail is unchanged. Radial opens when B leaves the widget's root interaction
scope; directional/pointer entry into the ordinary rail stays a rail. The wheel
is an overlay above the fixed guide, with existing widget pixels retained behind
it. It does not change widget viewport size or introduce an SDK declaration.

## Input ownership

The existing normalized `ControllerFrame` is the only controller source.
`RadialChooserPolicy` implements the original host's eight sectors (Up first,
clockwise), a 20,000 left-stick selection threshold, and neutral rearming when
entering or changing pages. D-pad traverses the current page with wraparound.
Right-stick paging uses 15,000/9,000 hysteresis and 360/125 ms repeat timing;
missed ticks coalesce. Crossing radial ownership with a held right stick requires
neutral before paging or widget scrolling can resume. Rail-only input does not
acquire that additional gate.

Pages contain up to eight widgets. Browsing a page changes chrome only; it does
not select or load a widget. Selecting an item uses existing preview selection;
A opens it. B returns to the current widget. Existing Y reorder/hold restart,
Menu actions, catalog authority and persisted order are reused. Context menus
can anchor at the hub when the selected widget is on another browsed page.

## Native presentation

`RadialChooserView` is a bounded set of eight native Buttons and two native paging
Buttons on an authored Grid. A native Viewbox constrains the 400-DIP wheel;
there is no custom measure/arrange/painting loop. The opaque theme surface and
separate center hub keep labels legible over artwork. Native ThemeShadow supplies
elevation, omitted in high contrast. Brushes are updated in place. Native controls
retain pointer, keyboard and UI Automation behavior; names, selected state and
position/count are exposed. Existing package-icon resolution and lifetime are
reused, including fallback glyphs.

Each button directly owns the shared `WidgetPackageIconView`; the radial surface
does not need the rail's additional catalog-label content wrapper. Asset demands
remain bounded to eight slots, replace by admitted package generation, and cancel
when the chooser is disposed. No file/URI loading authority is added.

Entrance uses the shared composition motion backend and configured dialog motion
specification/speed, with reduced motion respected. Initial focus and animation
wait for native Loaded/LayoutUpdated readiness rather than relying on a one-shot
dispatcher callback. The background remains visually enabled; incoming native
focus is redirected into the chooser while its input domain is active.

## Validation scope

Managed policy checks cover sector order, dead zones, page wrapping, empty/short
pages, native repeat timing, reversal, coalescing, neutral ownership, and rail
isolation. `--validate-production-shell` adds native chooser checks to the actual
production shell fixture. `--radial-fixture-scale=1`, `1.25`, or `2` selects the
final radial screenshot scale; normal production settings are not changed.

The production fixture checks root Back entry, unchanged underlying viewport,
eight real controls, page-only browsing, D-pad target ownership, short final
pages, return-to-widget behavior, high contrast, reduced motion and theme changes.
Live controller-free production probing additionally exercises real package icons,
Playnite/YouTube preview switching and tray context-menu anchors. Physical controller
acceptance and the combined switching-readiness integration remain separate gates.

The final automated run passed 72 managed shell checks and all four native matrix
cases (100 checks each): interface scales 1, 1.25 and 2, plus text scale 2 at
interface scale 1.25. `scripts/Test-WinUiRadial.ps1` also verifies visible glyph
pixels in every slot of the native screenshots, after bounded compositor
readiness polling. UIA dimensions alone previously passed with blank icons.
The 200% case intentionally constrains the wheel to the smaller remaining test
window viewport; it verifies containment, not fullscreen physical usability.
