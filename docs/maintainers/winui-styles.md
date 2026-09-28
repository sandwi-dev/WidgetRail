# Native computed-style application

`WidgetViewPresenter.Styles.cs` contains the shared adapter for page bindings and
native indexed item containers. It consumes the bridge's complete Base, Focused
and Pressed maps. These are complete resolved states, not patches to merge.
Selected/disabled/busy selectors are already incorporated by the bridge resolver.

The current mapping covers native foreground/background, uniform border brush,
per-edge border widths, corner radius, padding, opacity, font size/family/weight,
text alignment and character spacing. Character spacing additionally supports em.
Box geometry, viewport units and full cross-axis percentage sizes are handled by
the layout adapter described in `winui-layout.md`. Separate edge colors, outline
styling, depth, other transforms, text transforms and rich typography remain outside
this bounded mapping. No shadow animator is introduced.
FontIcon receives foreground and font size while retaining its glyph-specific font.

The newer control-scale adapter maps existing WRSS scale states to native composition;
see `winui-motion.md`. Explicit authored font sizes now multiply the host's effective
TextScale, with fresh values derived from the style map rather than the previous native
font size. Font metrics trigger native reflow instead of scaling text as a bitmap.
The root presenter also establishes a scaled inherited default. Host display policy
continues to own selecting the effective TextScale and InterfaceScale.

## State and reset ownership

Each native element has one style owner. It remembers local values only when a
property is actually overridden. Removing a declaration restores that original
local value or clears the local value, allowing native style/inheritance defaults
to apply. It never substitutes transparent for an absent background declaration.
Explicit `transparent` remains a real transparent brush.

Native control focus and ButtonBase pressed state select the computed state.
SelectorItem pointer/key events supply its pressed state; normalized controller-A
press/repeat/release uses the same adapter. No extra action is dispatched. Event
and property subscriptions are retired with the binding/recycled row. Noninteractive
panels and text do not subscribe to pointer/key/focus input.

The host must call `presenter.ResetPressedStyles()` when relinquishing its input
lease, including hiding a retained page without unloading it. Ordinary releases,
focus loss, disabled state, scope replacement, unload and disposal clear held state.
Native focus visuals remain the fallback when there is no authored outline.

With an authored focused outline, the newer focus-decoration owner replaces the
system ring with a native compositor outline using global focus motion settings.
High Contrast and controls without an authored outline retain system focus. The
outline does not replace the ordinary focused background/border style selection.
`surface-shading` now produces a cached native gradient behind content; shadow and
edge-stroke depth mapping remain incomplete. See `winui-motion.md` for ownership.

Button/SelectorItem template state brushes are overridden in a resource wrapper,
so native hover/pressed visual states do not replace authored colors. The original
ResourceDictionary remains intact and is restored by identity when overrides leave.
Brushes and font-family objects are reused while values remain equal.

## Correct native layers

Background/focus-presentation surfaces expose a StylePanel around their artwork
and content. Box styles apply there; a background brush paints behind artwork and
never replaces the retained artwork ImageBrush. Collection box styles apply to
the native ListView/GridView, not a potentially empty ContentControl wrapper.

An indexed row's native SelectorItem owns root box styles, including padding and
opacity, authored margin and size constraints. Its noninteractive fragment applies root typography only, with focus/press
state explicitly forwarded from that container. Descendants retain their own Base
styles. Row styles always come from its semantic lease, not the parent page map.
This does not solve the separate transport requirement to re-resolve retained leases
when a global WRSS theme changes.

The fragment root relinquishes its duplicate margin and size constraints. Scale and focus decoration
therefore operate on the painted card inside the authored gap, rather than scaling
a margin-inclusive cell into the viewport edge. Container preparation clears only
the native template's default margin and placeholder minimum before a style owner exists; later slot
notifications must not reset live style-owned geometry. The responsive pass refreshes
viewport-relative constraints on realized containers without realizing additional rows. This keeps the outer item
extent and authored spacing stable while aligning native pointer/focus bounds with
the card's painted box.

Actual-widget validation at 125% scaling confirmed YouTube Music's authored
82-DIP rows (103 physical pixels), and Playnite Library's focused top-left poster
stays inside the viewport. Native Down moved from item 0 to item 7 in that seven-column
layout. These are geometry/focus checks, not performance acceptance.

WidgetModalPanel's native defaults now live in Style setters with ThemeResource
values. Clearing a computed override therefore restores a live theme reference,
not a captured brush from an earlier theme. Background alpha affects the brush
only; explicit node opacity affects that node's subtree once. Styling a dialog
body does not apply that opacity to the separate modal scrim.

Modal-layer WRSS styles target that actual scrim Border. Painting the layer Grid's
background would put the authored veil beneath the retained parent and leave the
visible scrim on an unrelated native default, making the dialog appear too transparent.
The dialog panel keeps its separately authored brush alpha.

## Contrast and validation

OS High Contrast changes update adapters through the window's WinUI
`Microsoft.UI.System.ThemeSettings` observer. Host settings can supply `SetHighContrastStyleOverride(true/false/null)`;
null follows Windows. Explicit painted colors use the system Window/WindowText
palette (GrayText for disabled controls), with a paired black/white fallback when
those resources are unavailable. Explicit opacity becomes opaque. Transparent/absent
backgrounds retain their distinct meanings. The native theme/focus policy remains on.

`WidgetStylesValidationPage` is an autonomous native page. Its Styles.Status UIA
element reports checks for real compiled WRSS, normal/focused/pressed states,
held-state preservation, removal, native resource wrapping/restoration, contrast,
modal ThemeResource restoration and later theme changes, indexed root isolation,
glyph font preservation and owner retirement. It changes no OS setting and starts
no controller owner. Root owns routing/deployment and the native runtime slot.

Integrated native validation passes 24 checks. Desktop execution exposed two API
ownership defects that the analyzer did not catch: AccessibilitySettings event
subscription requires a UWP window, and a ResourceDictionary cannot have two parents.
The host now uses the documented Win32 ThemeSettings API, and detaches/restores resource
dictionaries in ownership order. The opacity check uses floating-point tolerance;
native WinUI exposes 0.8 as 0.800000011920929. Evidence is in
artifacts/winui-surfaces/styles-precision-native-result.json.

Reference: https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.system.themesettings
No OS contrast preference was changed during validation; actual theme switching remains
part of production acceptance. These checks cover the implemented subset, not complete
WRSS geometry, depth or animation parity.
