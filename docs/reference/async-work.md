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
| `Cancel()` | Stop the current read while retaining its previous value/cache |
| `Reset()` | Clear resource state |

Options include a loader, error mapper, cache duration, work lifetime, and whether
to retain the last good value. Retaining usable content during refresh often
gives a better experience than clearing the whole page.

For pages of items, use the separate [collection resources](collections.md).

## One selected query

Use `CreateQueryResource<TQuery,TValue>` when a read belongs to one selected
immutable query, such as a game and a details-opening ID. It uses the same resource
state machine and operation scheduler as `WidgetResource<TValue>`:

```csharp
var details = CreateQueryResource<GameQuery, GameDetails>("game.details", new()
{
    Load = (query, token) => provider.GetDetailsAsync(query.GameId, token),
    MapError = _ => new("details_unavailable", "Details could not load. Try again."),
    CacheDuration = TimeSpan.MaxValue,
});

// Call on navigation or explicit demand, not from Render.
details.EnsureLoaded(query, navigation.Value.RouteCancellationToken);
var snapshot = details.GetSnapshot(query);
```

Queries must be immutable and implement meaningful equality. Include an opening,
account, or document identity when those define different data ownership. The SDK
captures that query before scheduling; it does not infer provider identities.
`GetSnapshot` returns empty state for another query. Render from the resource
snapshot instead of copying its value/loading/error fields into a second model.

The runtime lifetime and caller token both own each read. Changing queries clears
the previous value; changing an in-flight caller scope retires its request. A
canceled scope cannot select a query or start work. Scope cancellation and `Cancel`
restore the pre-read state, preserving completed data for hide/resume. Use `Reset`
when closing a page must discard its value. Cancellation retires publication even
if a provider ignores its token; the latest queued read waits for that provider to
drain, so same-resource calls never overlap. Scope registrations are retired at
completion or cancellation. No provider calls happen automatically on selection,
resume, or cache expiry: the author requests them through `EnsureLoaded`.

`EnsureLoaded(query, scope, forceRefresh: true)` refreshes the current query and
can retain its last good value on error. Duplicate reads under the same scope
join one completion. Capacity rejection settles as a safe error; inactive
admission restores the previous state. Check `snapshot.HasValue`, especially for
value types where a successfully loaded zero is distinct from no value.

This owns one selected query, not a multi-key cache, mutation transaction, engine
session, or page-plus-pin demand graph. Those policies retain their domain owners.

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
[`WidgetQueryResource.cs`](../../src/WidgetSdk/WidgetQueryResource.cs),
[`WidgetOperations.cs`](../../src/WidgetSdk/WidgetOperations.cs), and
[`WidgetOptimisticCommand.cs`](../../src/WidgetSdk/WidgetOptimisticCommand.cs).
For the additional helpers, see [`WidgetTicker.cs`](../../src/WidgetSdk/WidgetTicker.cs),
[`WidgetTimedMutation.cs`](../../src/WidgetSdk/WidgetTimedMutation.cs), and
[`WidgetOutOfBandCommand.cs`](../../src/WidgetSdk/WidgetOutOfBandCommand.cs).

`Reset()` and `Publish()` retire the current read even when a provider ignores
cancellation. A subsequent load queues behind that provider; repeated resets retain
only the latest pending read. Ordinary duplicate loads still join one completion.
Use an immutable captured request for provider identity and keep domain cache keys
explicit; this resource is not a general multi-key cache.
