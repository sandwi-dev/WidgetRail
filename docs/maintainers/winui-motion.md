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
transitions are enabled. It is not yet wired into production section/modal/focus
presentation; applying it to a whole button would repeat the former color/text bug.

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
