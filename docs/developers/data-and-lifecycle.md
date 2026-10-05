# Loading data and lifecycle

Fetch data because it is needed, not because the renderer asked for a view.
Keeping these jobs separate makes widgets easier to reason about and avoids
unnecessary API calls.

## Describe every state

A list normally has more than a success state. Plan for loading, empty results,
an error, and a retry action. If you already have useful data, consider keeping
it visible while refreshing instead of replacing the whole page with a spinner.

`WidgetResource<TValue>` owns a bounded asynchronous result, such as a device
list. For virtualized browsing, publish an immutable query through
`CreateIndexedCollection` when the count is known, or `CreateDiscoveredCollection`
when the provider exposes continuation tokens. Use `UI.CollectionList` or
`UI.CollectionGrid` to display the source.

`WidgetCursorResource<TItem>` and `WidgetPagedResource<TItem>` remain provider-side
helpers. Their legacy eager `Present`/`Paginate` metadata does not drive WinUI
virtualization. Capture their data in an indexed/discovered source or render a
small explicit page. See [Collections](../reference/collections.md).

## Query lifetime and viewport demand

A query freezes item membership, ordering and the values needed to render its
items. WinUI owns realized controls and range demand. `ReadRange` serves bounded
positions, honors cancellation and does not publish a new parent tree for every
scroll step. Discovered collections append admitted results while retaining the
current query; changing filters or sorting publishes a new query.

Keep the collection ID and stable occurrence keys across compatible updates.
Use `UpdateContent` when membership and ordering stay the same, and `PublishQuery`
when they change. Do not rename unchanged items to force a refresh. Modal scopes
and parent collection focus/scroll memory are independent.

UI virtualization does not bound all provider memory. A source may retain data
for items with no realized XAML control. Choose query and cache limits explicitly,
and release retired query leases and provider resources. The detailed
[indexed collection guide](indexed-collections.md) covers discovery, cancellation,
artwork demand, keyed focus and range retirement.

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
