# Asynchronous work

Keep rendering separate from work that waits for a provider. A view describes
current state; an operation loads data or performs an action.

## One result

Use `WidgetResource<TValue>` for one fetched value. Its snapshot describes the
available value and loading/error state.

| Method | Use |
|---|---|
| `EnsureLoaded()` | Load when needed, respecting the cache |
| `Refresh()` | Request a fresh result |
| `Retry()` | Retry after failure |
| `Publish(value)` | Accept a value from another source, such as an event |
| `Reset()` | Clear resource state |

Options include a loader, error mapper, cache duration, work lifetime, and whether
to retain the last good value. Retaining usable content during refresh often
gives a better experience than clearing the whole page.

For pages of items, use the separate [collection resources](collections.md).

## Coordinate requests

`WidgetOperations` groups work by a stable key:

- `RunSingleFlight` prevents duplicate in-flight work for the same operation.
- `RunLatest` replaces obsolete requests, useful for changing search queries.
- `RunSerial` preserves ordering for operations that must run one after another.

Use the operation's cancellation token and current-generation checks. An old
request completing late must not publish over the result of a newer request.

Choose the appropriate lifetime. Work valid only while a page is visible should
stop when that lifetime ends. A retained process is not permission to continue
calling every broker capability in the background.

## Optimistic actions

`WidgetOptimisticCommand<TState,TRequest,TExecution,TResult>` can show a provisional
state immediately, perform the request, and reconcile success or failure.

For example, a favorite icon may update while the provider confirms the change.
If the request fails, reconcile the current state rather than blindly restoring
an old copy that could overwrite a newer user action.

Use immutable [model state](widget-model-reference.md) and keep one owner for it.

## Failures and cancellation

Turn provider failures into useful UI: explain what happened and offer a retry
when it makes sense. Cancellation caused by leaving a route normally does not
need an error toast. Avoid starting detached work only to return from an action
handler sooner.

## Timers and externally confirmed commands

`WidgetTicker.RunWhileActiveAsync` runs non-overlapping periodic work under an
activation token. Its 250 ms minimum interval is a maximum tick rate, not a
recommendation to call a provider four times per second. Prefer events when available.

`WidgetTimedMutation` owns one replaceable delayed state mutation, such as
clearing transient feedback. Its callback is synchronous; the helper does not
publish busy state or invalidate the widget for you.

`WidgetOutOfBandCommand` handles commands whose confirmation arrives through an
independent observation rather than the initial request response. Its tickets
and sequence checks distinguish current confirmation from an older observation.
Use it only when that provider pattern is needed; ordinary awaited operations
are simpler for direct request/response actions.

Exact signatures and bounds:
[`WidgetResource.cs`](../../src/WidgetSdk/WidgetResource.cs),
[`WidgetOperations.cs`](../../src/WidgetSdk/WidgetOperations.cs), and
[`WidgetOptimisticCommand.cs`](../../src/WidgetSdk/WidgetOptimisticCommand.cs).
For the additional helpers, see [`WidgetTicker.cs`](../../src/WidgetSdk/WidgetTicker.cs),
[`WidgetTimedMutation.cs`](../../src/WidgetSdk/WidgetTimedMutation.cs), and
[`WidgetOutOfBandCommand.cs`](../../src/WidgetSdk/WidgetOutOfBandCommand.cs).
