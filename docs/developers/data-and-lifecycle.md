# Loading data and lifecycle

Fetch data because it is needed, not because the renderer asked for a view.
Keeping these jobs separate makes widgets easier to reason about and avoids
unnecessary API calls.

## Describe every state

A list normally has more than a success state. Plan for loading, empty results,
an error, and a retry action. If you already have useful data, consider keeping
it visible while refreshing instead of replacing the whole page with a spinner.

`WidgetResource<TValue>` helps own an asynchronous result. Use it for a device
list or another result that can be loaded as one value. For long collections,
use `WidgetCursorResource<TItem>` to fetch pages as people browse.

## A cursor is a loaded window into a collection

Imagine a library with hundreds of games. The cursor keeps the pages currently
needed around the viewport instead of eagerly loading the whole library.
The collection supplies stable item identities and information about whether
more pages exist. The host supplies viewport and navigation demand.

Loading the next page continues the same collection. Refreshing or changing
the query starts a fresh collection at the beginning. Keep the scroll container's
identity stable; do not rename it on every render to force scrolling to reset.

The retention target is a preference, not permission to remove visible items.
A wide viewport may need more items than that target. The cursor and host work
together to retain the range currently in use and compensate for removed pages.

## Run work at the right time

The lifecycle distinguishes a widget that exists, one whose view is visible,
and one currently receiving interaction. Hiding the overlay can move a retained
widget into background state without destroying it.

Some work belongs only to the visible screen. Other work, such as maintaining
a playback connection, may need to continue in the background. Choose the
appropriate lifecycle hook and cancellation token for the work you start.

Cancel work that no longer belongs to the current route or request. An old
search finishing late must not replace the results for a newer query.

## Refresh deliberately

Reopening a route does not have to make a network request. Reuse cached data when
it is still useful. When freshness matters, refresh once at the relevant route
activation, not again when the same widget receives focus.

For service integrations, respect rate limits and use the provider's retry
guidance. Keep explicit user actions responsive without polling idle pages rapidly.

See the [lifecycle reference](../reference/widget-residency.md) and
[SDK reference](../reference/sdk-reference.md) for the exact hooks and resource APIs.
