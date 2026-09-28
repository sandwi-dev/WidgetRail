# Native computed-style application

`WidgetViewPresenter.Styles.cs` contains the shared adapter for page bindings and
native indexed item containers. It consumes the bridge's complete Base, Focused
and Pressed maps. These are complete resolved states, not patches to merge.
Selected/disabled/busy selectors are already incorporated by the bridge resolver.

The current mapping covers native foreground/background, uniform border brush,
per-edge border widths, corner radius, padding, opacity, font size/family/weight,
text alignment and character spacing. Box lengths are currently px/DIP or unitless;
character spacing additionally supports em. Relative box units, separate edge
colors, outline styling, depth, transform/scale, text transforms and rich typography
remain outside this bounded mapping. No shadow or scale animator is introduced.
FontIcon receives foreground and font size while retaining its glyph-specific font.

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
Native focus visuals remain enabled; these styles do not install focus animation.

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
opacity. Its noninteractive fragment applies root typography only, with focus/press
state explicitly forwarded from that container. Descendants retain their own Base
styles. Row styles always come from its semantic lease, not the parent page map.
This does not solve the separate transport requirement to re-resolve retained leases
when a global WRSS theme changes.

WidgetModalPanel's native defaults now live in Style setters with ThemeResource
values. Clearing a computed override therefore restores a live theme reference,
not a captured brush from an earlier theme. Background alpha affects the brush
only; explicit node opacity affects that node's subtree once. Styling a dialog
body does not apply that opacity to the separate modal scrim.

## Contrast and validation

OS High Contrast changes update adapters through one shared AccessibilitySettings
observer. Host settings can supply `SetHighContrastStyleOverride(true/false/null)`;
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
