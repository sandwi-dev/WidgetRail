# Add a UI element

A new control needs more than a drawing branch. It must have consistent behavior
in validation, layout, input, accessibility, and updates.

## First choose the smallest change

| Choice | Use it when |
|---|---|
| SDK composite | Existing elements already express the interaction |
| Property or closed variant | An existing element keeps its meaning but gains a supported presentation option |
| New protocol element | The control has distinct data or interaction semantics |

For example, a labeled slider row can usually be a composite. A new value-bearing
interaction may need a protocol change. Do not give an element a button role
just to avoid defining its real accessibility behavior.

## 1. Define the public behavior

Describe what the user can focus, activate, adjust, or read. Specify the disabled,
busy, empty, and error states. Decide which inputs the control consumes and which
should continue to the surrounding scope.

Choose stable IDs and finite limits for text, children, ranges, and resource use.
Follow [SDK evolution](../developers/widget-sdk-evolution.md) for compatibility.

## 2. Add the contract

Update the public SDK and protocol types, materialization, serializers, and
validators as needed. A new feature must have the appropriate protocol version
requirement, while older supported views keep their existing behavior.

The managed bridge and native parser must agree on accepted values and limits.
Do not accept a shape on one side that the other cannot safely represent.

## 3. Add layout and drawing

Give the element sensible measurements and theme defaults. Check text constraints,
clipping, scroll offsets, DPI, and responsive layouts. Invalid dimensions and
missing artwork need a usable fallback.

Classify presentation changes correctly. Paint-only changes should not force
whole-tree preparation. Retained text, images, and styles need correct invalidation.

## 4. Connect input and accessibility

Add the control to focus targeting only if it should be focusable. Revalidate
actions against current identity and state. For a slider-like interaction, keep
one presented-value owner shared by rendering and accessibility.

Expose the appropriate UI Automation role and patterns. A visually correct
control can still be unusable if its name, range, or invocation behavior is missing.

## 5. Check every presentation

Exercise the element in an ordinary view, a scroll container, a nested scope,
and any supported pinned layout. Change its content while focused. Remove or
replace it while an action is pending and verify stale actions do not run.

## 6. Validate and document

Cover valid/invalid serialization, layout and clipping, input, accessibility,
and update behavior with focused checks. Add an SDK Gallery example and a short
reference entry. Update the public API baseline only for an intentional API change.

Source starting points:
[`UI.cs`](../../src/WidgetSdk/UI.cs), [`WidgetProtocol`](../../src/WidgetProtocol/),
[`DeclarativeLayout.cpp`](../../src/OverlayHost/DeclarativeLayout.cpp),
[`DeclarativeRenderer.cpp`](../../src/OverlayHost/DeclarativeRenderer.cpp), and
[`AccessibilityProjection.h`](../../src/OverlayHost/AccessibilityProjection.h).

Use [Build execution](build-execution.md) and [Contributing](../../CONTRIBUTING.md)
for the verification commands. Hardware acceptance remains separate from a
passing serializer or layout test.
