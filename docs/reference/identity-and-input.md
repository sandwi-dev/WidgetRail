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

When selecting among several candidates in the same tree, use
`WidgetFocusTargetLookup.Capture(root, activeScopeId)` and call `Resolve(id)` or
`First()` on the returned snapshot. This converts the authored tree once for the
whole decision. Capture again for a new tree; do not cache this across renders.
The widget still chooses its fallback policy, and the host checks actual visibility.

Containers can use `RememberChildFocus` for stable child restoration. Keep it
separate from collection refresh, which has its own position-reset signal.

Left/Right first uses explicit neighbors and normal spatial navigation. If no
control qualifies, the host can enter a neighboring scroll panel whose visible
bounds overlap the focused control vertically. It chooses the nearest visible,
enabled control in that panel; it does not jump to a hidden child. A panel with
only offscreen controls or overflowing reading content keeps the existing
container focus and scroll-settling behavior. Fitted text-only panels, inactive
input scopes, stacked panels, and diagonal headers add no new destinations.

## Text and selection controls

Use the SDK's `TextEntryElement` for host-managed text entry and `SelectElement`
for a choice among options. These elements participate in normal focus and
action routing. A sensitive text-entry declaration cannot contain a prefilled
secret value; the supported host flow owns sensitive input.

Ordinary fields can opt into [whole-entry suggestions](text-suggestions.md).
The host keyboard displays bounded choices while typing; choosing one fills the
field and Done submits it. Passwords never use suggestion providers.

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

## Explicit focus requests

Call `CreateFocusRequest()` once when handling a user or lifecycle event, retain
the returned `WidgetFocusRequest`, and reuse it across renders. Its ID comes from
the widget-instance sequence shared with all SDK-created navigators. Do not
allocate independent counters for tabs or use navigation revisions as request IDs.
Existing explicit-ID APIs remain supported.

`EnterGroup(groupId)` restores a group's remembered/default child.
`EnterCollection(source, collectionId, item)` enters an exact indexed/discovered
occurrence. `FocusElement(groupId, targetId)` targets a declared focusable
descendant directly (protocol 99). It respects active scope, identity, enabled
state and visibility; an unavailable exact target is consumed, so issue a new
intent once it becomes available. Do not make arbitrary text focusable to use it.
`Reveal` and `RevealCollection` request scrolling without moving focus.

For navigation Back, `ReturnToCollection(source, collectionId, item)` also asks
the host to restore the occurrence's previous visible position (protocol 101).
The host keeps bounded position values, not rows or controls. It restores only
matching collection/source/query/item identities after focus succeeds. A changed
query or missing memory uses ordinary entry; later input cancels pending return
work. After a resize, the position is clamped so the focused row stays visible.
Use ordinary `EnterCollection` for deliberate jumps or new search results.
