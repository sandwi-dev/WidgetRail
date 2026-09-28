# WinUI motion foundation

This implementation preserves the global settings in `PlatformSettingsModels.cs`.
It does not add widget settings or reinterpret declarations.

`WidgetMotionOptions.From(appearance, systemAnimationsEnabled)` resolves System,
Full and Reduced motion. Full overrides the OS preference; System follows it.
Speed retains the existing 0.5–2 multiplier. Sections use 208 ms, dialogs 260 ms,
focus Fade 240 ms and Settle 220 ms before applying speed. Slide and Zoom remain
the defaults. Disabled/reduced motion commits the target immediately.

`WidgetMotionPolicy` compiles those preferences into presentation recipes:

- Slide/VerticalSlide translate full viewports without section-wide fading.
- Reveal/CoverSlide use complementary clips outside content transforms.
- Paging brings the next page up while the old page recedes and is clipped away.
- Lift/Zoom coordinate dialog body and scrim. Scrims never scale.
- Focus Fade/Settle apply to the focus decoration, never button text/artwork.
- Layout labels translate; selection surfaces may also resize on the section timeline.

The polynomial smoothstep from the existing native policy corresponds to the
composition cubic Bezier `(1/3, 0), (2/3, 1)`. There is no managed animation tick,
layout loop, pixel capture or fallback painter.

## Native ownership and integration hooks

`WidgetCompositionTarget` owns transform/opacity channels on dedicated motion
layers. Native visual callers supply separate content and viewport visuals. XAML
callers use `ForElement(contentLayer, viewportLayer, size)`: translation uses the
supported composition Translation property, never XAML-owned layout Offset.
The outer viewport must not already own a clip. Keep any normal widget clip on
its ancestor. Scale is centered on the supplied content size.

Use one `WidgetCompositionMotion` per semantic group. A single `PlayAsync` call
submits content, header and selection targets together in a native scoped batch.
Retargeting reuses compositor `this.StartingValue`, preserving displayed position
and opacity without reading stale managed property values. This is positional
continuity, not a velocity-preserving physical spring.

`WidgetMotionGroups.Update` consumes all validated transition declarations from
one committed page. Initial groups and ordinary same-key snapshots do not animate.
Changed keys return one event per group and order supplies direction. Removed
groups are forgotten. Reset on widget/input-owner replacement. This does not
observe scrolling or fabricate another input scope.

Presenter integration requires explicit lifecycle hooks:

1. Retain an outgoing **presentation** before replacing section content. Immediately
   disable its actions/hit-testing/accessibility; incoming content owns input.
2. After WinUI supplies current bounds, build all recipes for the changed group
   and call its one `PlayAsync` with dedicated presentation wrappers.
3. Retire outgoing resources on Completed, Superseded, Canceled or Disposed;
   do not let an old completion retire the new group's resources.
4. Settings/reduced-motion/size changes must settle or rebuild the active recipe,
   never replay a same-key page. Cancel on owner retirement and dispose motion
   groups **before** targets and before dispatcher shutdown.

This foundation deliberately does not retain old widget controls, semantic leases,
or draw trees. The presenter must implement that ownership boundary before content
transitions are enabled. Section transitions and focus decoration integration remain
unfinished; applying them to a whole button would repeat the former color/text bug.

`WidgetDialogMotion` now connects actual presenter dialog openings to the existing
native recipe and one scoped batch for panel and scrim. The host calls
`presenter.ApplyAppearance(settings.Appearance, systemAnimationsEnabled)` when policy
changes. New panels animate once after native layout supplies bounded dimensions;
ordinary snapshots and foreground restoration do not replay the entrance. Resize,
policy changes and retirement settle/cancel motion. The surrounding widget-local
modal layer retains clipping and input authority; its parent viewport does not move.
The unclipped target factory is restricted to dialog-owned surfaces whose ancestor
already clips them. It rejects inset recipes and does not overwrite that ancestor clip.
Dialog dismissal remains immediate; retaining an inert outgoing surface for exit
motion is part of the remaining transition-lifetime work.

`WidgetFocusMotion(control, decoration, appearance, systemAnimationsEnabled)` is
the concrete native-control adapter. Put its `Adornment` in the control's layout
cell above that control, with the same alignment, margins and bounds. The decoration comes from the existing resolved theme
and pseudo-state styles; this class invents no colors and transforms no control
content. Native GotFocus/LostFocus drives decoration motion, owner resize settles
it, and unload retires compositor objects. `ApplyAppearance` settles current motion
when global policy changes; a theme-only change does not replay focus. Dispose
before removing the binding; it restores the control's system-focus-visual setting.
Keep shared focused/pressed control scale on an ancestor of both the control and
its decoration. The focus-only scale must not become a second owner of that channel.

## Validation and references

`tests/WinUiMotion.Tests` covers settings, all presets, clipping, focus-only geometry,
shared durations, group identity, limits and atomic rejection. It links only the
pure policy/group sources and does not require a desktop or controller.

`Validation/WidgetMotionValidation.RunAsync(loadedPanel)` exercises actual XAML
layers, native scoped batches, superseding, cross-thread cancellation, immediate
settlement and disposal. It is opt-in; it does not register/launch a package or
start a controller owner. Native checks do not prove perceived smoothness, pixel
clipping correctness, pointer routing through transforms, or media coexistence;
those still require integrated desktop evidence.

Grounded samples: `winapp find-ui --id gallery-xamlcompinterop-1`,
`gallery-xamlcompinterop-3`, `gallery-implicittransition-1` (WinUI Gallery).
Existing behavior references: `OverlayHost/WidgetAnimationPolicy.h`,
`WidgetInteractionMotion.h`, `WidgetProtocol/WidgetTransition.cs` and
`winui-feature-contracts.md`.

Integration validation: --validate-motion executed 18 checks on actual XAML/compositor
objects, including all section recipes, batch completion, interruption, cross-thread
cancellation, reduced motion, disposal and focus decoration. The initial focus probe
incorrectly assumed GotFocus/LostFocus had run synchronously when Focus returned;
bounded event settlement corrected that test assumption. Evidence is in
artifacts/winui-surfaces/motion-native-final.json. This does not claim production
section/modal integration or visually accepted animation/performance.

The connected dialog checkpoint passes 24 native modal checks (including actual
opening batch completion, same-key update non-replay and reduced motion), and the
18 native motion foundation regressions still pass. These checks do not establish
production-widget smoothness or full transition integration.

## Control scale integration

`WidgetControlScaleMotion` connects the existing WRSS `scale`, `transition-duration`
and `transition-easing` properties to the shared native style owner. Ordinary controls
and indexed SelectorItem containers use the same Base/Focused/Pressed resolution;
indexed fragments keep their typography-only root and cannot apply scale twice.
The WinUI Gallery's supported `UIElement.StartAnimation` path animates Scale on the
compositor. Native layout dimensions, action ownership and focus identity remain
unchanged. No managed animation tick or replacement paint layer is introduced.

Global Full/System/Reduced motion and widget animation speed remain authoritative.
Reduced motion commits the authored state scale immediately. A theme snapshot with
the same target does not restart motion; retargeting samples the compositor's displayed
StartingValue. Linear, cubic ease-out and smoothstep ease-in-out preserve the previous
WRSS curves. WRSS spring remains its normalized, bounded critically damped response,
submitted as 24 native keyframe segments; it does not acquire an unbounded settle tail.

The owner restores the original native Scale, CenterPoint and ScaleTransition when
styles disappear or the control retires. A real unload clears the displayed scale;
queued stale Unloaded events do not retire a control that is already loaded again.
Resize and policy changes settle active motion. Viewport clipping of enlarged first/
last collection items still requires actual widget validation; native motion completion
alone is not visual acceptance.

The control-scale checkpoint alone does not complete section transitions, focus
decoration, depth or dialog exits. The following incremental checkpoint addresses
the authored focus outline and surface-shading; the remaining limits are explicit below.

## Authored focus outline

`WidgetFocusDecoration` connects the ordinary style adapter's authored focused outline
to the existing global Fade/Settle/None policy. A host-only native ShapeVisual is placed
in the control's child visual slot, with dedicated content/clip visuals for motion.
It contains no XAML controls, actions or accessibility nodes. The outline is inset
inside the control bounds and uses resolved outline color/width/offset/corner radius.
This layer inherits authored control scale automatically; its own Fade/Settle channel
never modifies the control's text, artwork, background or dimensions.

The adapter restores WinUI system focus in High Contrast, when authored outline
styles disappear, or when the child visual slot belongs to another native visual
owner. Same-state updates do not replay motion. Resize settles to the current focus
state; stale queued Unloaded after reparenting does not retire an already-loaded owner.
This checkpoint animates the outline; focused background/border changes still use the
shared native style state immediately. Coordinated focus-surface crossfades, section
outgoing lifetime, navigation selection motion and dialog exits remain unfinished.

Surface-shading is now a cached native vertical LinearGradientBrush behind content.
Its top/bottom colors use the original renderer's bounded Shade formula and preserve
the base color alpha. High Contrast removes this decorative gradient. Shadow and
per-edge depth strokes remain separate unfinished mappings.
