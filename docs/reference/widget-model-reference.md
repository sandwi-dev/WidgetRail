# WidgetModel

`WidgetModel<TState>` stores related immutable state. A changed value publishes
one widget invalidation; an equal replacement publishes none.

## Create and read a model

Create a model from the widget with `CreateModel(initialState)`. For an isolated
model test, use `WidgetModel<TState>.CreateForTesting(initialState)`.

`Value` returns the current state. `Snapshot` returns its value and revision
together. Capture once when rendering instead of reading the model separately
for every label:

```csharp
var snapshot = _model.Snapshot;
var state = snapshot.Value;
```

The revision belongs to this model. It is not a provider version, a protocol
sequence, or permission to act on a stale external object.

## Set and Update

`Set(value)` proposes a replacement. `Update(state => next)` computes a replacement
from the latest value while the model lock is held. Both report the previous
and current values, revision, and whether anything changed.

The result-bearing `Update` overload can derive an operation input from the
same committed state. Keep that function synchronous and small; start provider
work after the update returns.

Do not perform network I/O, wait on a task, or publish a second model recursively
from an update function.

## Equality

Prefer immutable records. Replacing a value with something equal does not notify
the view. In-place mutation defeats this rule because the model no longer owns
an unchanged previous value.

Records containing collections may need a custom comparer: two different list
objects are not automatically equal by contents. Reuse unchanged immutable
collections or supply a bounded comparer with a matching hash implementation.

## Observers

`Changed` observers run after the model lock is released. A faulty observer does
not prevent other observers from receiving the committed change. Keep callbacks
short and avoid creating competing owners for the same state.

## Choose one owner

Use a model for related UI state, a `WidgetResource<TValue>` for one loaded result,
a `WidgetCursorResource<TItem>` for a continuous collection, and a
`WidgetPagedResource<TItem>` for one offset-based page at a time. Use
`WidgetNavigator<TRoute>` for routes. See [Collections](collections.md) to choose
between the collection resources. Do not copy each helper's state into another
model just to group fields.

For provisional changes, see [Optimistic commands](async-work.md#optimistic-actions).
Exact signatures and behavior are documented in
[`WidgetModel.cs`](../../src/WidgetSdk/WidgetModel.cs).
Examples: [Now Playing](../../src/FirstPartyWidgets/MediaSessionsWidget/MediaSessionsWidget.cs)
and [Playnite](../../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.cs).
