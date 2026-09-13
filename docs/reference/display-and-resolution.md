# Display size and layout

The host owns the desktop window and chooses the available widget viewport.
Widget code describes a layout that can fit that space.

## Work in logical dimensions

Layout sizes are expressed in device-independent units rather than raw monitor
pixels. Windows DPI, the user's interface scale, and text settings affect how
those units appear on screen.

Avoid assuming one monitor resolution or converting a desktop pixel count into
a fixed widget width. Use container layout, size hints, and minimum dimensions
that still leave important controls reachable.

## Adapt the content

Rows can wrap where supported. Responsive grids change their column count to
fit the width. Long content belongs in a scroll container.

When a grid becomes wider, it may need more loaded items. Use the shared
[collection metadata](collections.md) so viewport demand can trigger that load.

Responsive visibility can show a different arrangement at different sizes.
Keep the semantic identity of equivalent controls stable where possible. If a
focused control no longer exists in the active arrangement, provide a useful
replacement instead of leaving focus on hidden content.

## Text and clipping

Test long labels and larger text. Let text wrap or truncate according to the
control's purpose, while keeping the full meaning available through its label
or accessibility description.

Do not rely on a hover tooltip as the only explanation for a controller action.
Also check that a focused control's outline and the end of an editable field
remain visible at the scroll boundary.

## Resize behavior

The host animates presentation changes between widget sizes. Authors should
publish the correct target layout, not simulate intermediate window sizes or
change their data model on every animation frame.

Pinned layouts may have different space requirements from the full widget.
Offer a compact layout intentionally rather than shrinking a complex full view.

## Check more than one arrangement

Try narrow and wide layouts, normal and larger text, and a scrolled position.
Verify Back, offscreen navigation, and theme focus outlines in each case.

See the source contracts in [`Elements.cs`](../../src/WidgetSdk/Elements.cs),
native layout in [`DeclarativeLayout.cpp`](../../src/OverlayHost/DeclarativeLayout.cpp),
and the [WRSS property reference](wrss.md).
