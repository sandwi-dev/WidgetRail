# Native computed-style application

`WidgetViewPresenter.Styles.cs` contains the shared adapter for page bindings and
native indexed item containers. It consumes the bridge's complete Base, Focused
and Pressed maps. These are complete resolved states, not patches to merge.
Selected/disabled/busy selectors are already incorporated by the bridge resolver.

The current mapping covers native foreground/background, uniform border brush,
per-edge border widths, corner radius, padding, opacity, font size/family/weight,
text alignment and character spacing. Character spacing additionally supports em.
Box geometry, viewport units and full cross-axis percentage sizes are handled by
the layout adapter described in `winui-layout.md`. Separate edge colors, focus
outlines and native depth paint are covered below. Text layout and display casing
are covered below. Rich text runs remain outside this mapping. No shadow animator is introduced.
FontIcon receives foreground and font size while retaining its glyph-specific font.

The newer control-scale adapter maps existing WRSS scale states to native composition;
see `winui-motion.md`. Explicit authored font sizes now multiply the host's effective
TextScale, with fresh values derived from the style map rather than the previous native
font size. Font metrics trigger native reflow instead of scaling text as a bitmap.
The root presenter also establishes a scaled inherited default. Host display policy
continues to own selecting the effective TextScale and InterfaceScale.

## Shared text layout

`WidgetTextStyleAdapter` projects existing WRSS typography onto native `TextBlock`
properties. Plain text, button/select/text-entry labels, shared component text and
indexed presentation fragments use the same implementation and complete state maps.
Button labels are explicit native text elements; glyph labels use a native Grid
with a constrained text column so an unbounded horizontal StackPanel cannot defeat
their line limits. This does not replace WinUI text measurement or layout.

- `max-lines: 1` selects NoWrap. Larger limits use native multiline layout.
- `overflow-wrap: anywhere` uses Wrap (native emergency breaks). `normal` also
  uses Wrap: TextBlock has no exact equivalent of the previous renderer's legacy
  DirectWrite WRAP mode. WrapWholeWords is intentionally not substituted because
  it would overflow long words instead of retaining the existing native fallback.
- `text-overflow: ellipsis` uses CharacterEllipsis; `clip` uses pixel-level Clip,
  rather than None, which can truncate at word boundaries.
- Unitless `line-height` multiplies the effective scaled font size and uses native
  BlockLineHeight. Native font metrics continue to determine glyph placement.
- `text-transform: uppercase/lowercase/none` changes display casing using invariant
  Unicode casing. The latest authored source is retained, including updates whose
  displayed text happens to be identical. Accessibility labels, action data and text
  entry values remain unchanged. Glyph codepoints are never case-transformed.
- Pixel character spacing grows with TextScale; em spacing retains its em ratio.

Removing a property restores its prior native local/default value. In particular,
absent line constraints preserve native defaults rather than inserting a global
line cap. A theme can still explicitly impose a cap. Focused/pressed state changes
and row recycling use the same ownership rules. The native style validation fixture
covers source restoration, line metrics, wrapping/trimming, scaled tracking,
ordinary/glyph button labels and indexed fragment state changes.

Mapping reference: Microsoft UI XAML's `CTextBlock::ConfigureDWriteTextLayout`
maps Wrap to DirectWrite EMERGENCY_BREAK and WrapWholeWords to WHOLE_WORD.
The previous host's `NativeTextLayout.cpp` uses WRAP for normal and
EMERGENCY_BREAK for anywhere. Native TextBlock remains the layout owner; no
custom line breaker is added to imitate the unavailable legacy mode.

Typography checkpoint validation: analyzer build has no warnings/errors; the
native style fixture passes 164 checks, the ordinary control fixture passes 16,
the four native control fixtures pass 104, and package icons pass 36. Evidence is
under `artifacts/typography-parity`. Initial typography-fixture failures were fixed
by awaiting queued focus styling and using the real indexed `ApplyFragment` entry
point; production code did not change during those repairs. These are automated
native checks, not physical controller or full-product acceptance.

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
edge-stroke depth mapping are described below. See `winui-motion.md` for ownership.

## Native depth paint

`WidgetNativeDepth` consumes existing resolved shadow-color, shadow-blur,
shadow-offset-x/y and border-top/right/bottom/left-color declarations. No SDK or
WRSS properties are added. The native DropShadow uses the authored color alpha,
bounded blur and signed offsets. Its mask is a rounded CompositionShape exposed
as a CompositionVisualSurface, without CPU rasterization/readback or an animation
frame loop. Geometry and brushes are updated in place while the owner is alive.

Four native edge rectangles share the outer rounded clip and use the existing
per-edge widths/fallback colors. Top/bottom cover their complete strips; side
strips occupy the middle, matching the original renderer's ownership of corners.
The native border keeps its thickness for measurement and uses transparent paint
when the separate edge layers own its pixels.

Semantic Buttons retain their commands, focus and
automation peers. Their content template provides dedicated shadow/content/edge
slots; the depth layers inherit the control's authored scale and opacity. Panels
use a stable layout wrapper with lazily materialized paint slots. The shadow is
behind the authored background, preserving translucent fills and content colors;
the edge layer is above content. Ancestor clipping remains native.

High Contrast removes decorative shadows/edge colors and restores the ordinary
native border palette. Style removal, unload and disposal retire composition
resources; reloading creates fresh native owners. This does not emulate the old
CPU blur kernel pixel for pixel: the authored blur radius is delegated to the
Windows compositor's blur implementation.

The Button depth template retains the installed SDK native body and visual states.
Authored Button brush channels temporarily remove only their native state timelines;
unowned channels retain native ThemeResource expressions, including disabled feedback.
Removing author ownership restores the original timelines. This avoids stale authored
brushes in native keyframe theme-resource caches.

Indexed ListView/GridView containers keep their optimized native templates. WinUI
requires ListViewItemPresenter as the template root. Their shadows therefore use a
CompositionMaskBrush outer mask: an offscreen rounded outline excludes the entire
content interior from the sampled shadow. The extent includes authored signed offsets
and blur, with no CPU rasterization. Four separate edge strokes share a rounded clip.
The depth layer and focus outline lease separate ordered layers from one owned child
visual; either can retire independently. Foreign child visuals are never replaced.
Do not wrap the optimized presenter in a Grid or obtain its backing visual while
WinUI owns its scale facade.

Technique reference: [CommunityToolkit AttachedCardShadow](https://github.com/CommunityToolkit/Windows/blob/main/components/Media/src/Shadows/AttachedCardShadow.cs),
CompositionMaskBrush mode. The WidgetRail implementation uses native composition
resources directly and adds no toolkit or Win2D dependency.

The native depth gate passes 146 style checks, 36 modal checks and the 20-check
indexed sequence. The style fixture retains list/grid specimens for screen capture:
`Styles.Depth.Row` and `Styles.Depth.Poster` can receive focus to inspect authored
scale, exterior shadows and independent focus outlines. Native template identity,
foreign visual ownership, independent decoration retirement, unload/reload and
High Contrast are asserted separately. Pixel review confirmed the mask keeps
translucent content untinted and leaves the authored border/focus strokes visible.

Other Button/SelectorItem template state brushes are overridden in a resource wrapper,
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
