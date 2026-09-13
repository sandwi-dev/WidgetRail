# Identity and input helpers

Use stable identities to connect a view across updates. The same screen position
is not enough to identify a control or a collection item.

## Build IDs with WidgetIds

`WidgetIds.Scope("library")` creates a `WidgetIdScope`. Use child scopes and
`Id(name)` for ordinary controls. `KeyedId(name, durableKey)` produces a stable
opaque ID from a data key, avoiding unsafe characters or raw provider identity
in the view.

Keep the same key for the same item. Do not use a new GUID on each render or a
list index when the collection can reorder.

`WidgetCollectionItemKey` identifies a collection item.
`WidgetCollectionCursor` is an opaque provider cursor, not a numeric offset.
`WidgetArtworkHandle` identifies lazy artwork; it grants no file or network access.

## Find a focus target

`WidgetFocusTargetLookup` examines a view using the protocol's focus/scope rules.
It can distinguish a missing, duplicate, non-focusable, disabled, or out-of-scope
target from a valid one. Use this when an advanced presentation needs to choose
a fallback; do not treat “an element with this ID exists” as proof it can receive input.

Containers can use `RememberChildFocus` for stable child restoration. Keep it
separate from collection refresh, which has its own position-reset signal.

## Text and selection controls

Use the SDK's `TextEntryElement` for host-managed text entry and `SelectElement`
for a choice among options. These elements participate in normal focus and
action routing. A sensitive text-entry declaration cannot contain a prefilled
secret value; the supported host flow owns sensitive input.

## Action queue and numeric helpers

The widget runtime serializes admitted actions and exposes `ActionFailed` when
an accepted action later fails. Admission acknowledgement is not the same as
successful completion. Do not add a second queue just to make every handler
return immediately.

`SliderMath` provides the shared numeric rules for slider values. Prefer it to
slightly different rounding rules in UI, tests, and action handling.

Exact APIs:
[`WidgetIds.cs`](../../src/WidgetSdk/WidgetIds.cs),
[`WidgetFocusTargetLookup.cs`](../../src/WidgetSdk/WidgetFocusTargetLookup.cs),
[`Elements.cs`](../../src/WidgetSdk/Elements.cs),
[`WidgetControllerQueue.cs`](../../src/WidgetSdk/WidgetControllerQueue.cs), and
[`SliderMath.cs`](../../src/WidgetSdk/SliderMath.cs).
