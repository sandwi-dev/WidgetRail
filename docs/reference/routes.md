# Routes and input scopes

A route describes which page is open. An input scope describes which controls
and shortcuts are eligible to receive input. They work together, but are not
the same as the focused control.

For the visible tabs, rail, and header around those routes, use
[NavigationShell and presentation composition](presentation-composition.md).

## Use a navigator

`WidgetNavigator<TRoute>` owns a route stack and exposes the current navigation
snapshot. Define a small route type for your pages, such as Home and Library,
then render the current route.

Give pages stable scope IDs. A Back action can then leave a nested page before
returning from the widget to the tray. Do not make unrelated pages share control
IDs just because their headers look similar.

## Cancel route-specific work

A request started for one route should not replace content after the user has
left it. Use the navigator's route lifetime for that work. Root-route activation
also has its own cancellation token in the navigation snapshot.

Persistent model or resource state can outlive a page when that is intentional.
For example, returning from a playlist can restore the library selection without
requiring the playlist itself to reopen at its old track.

## Initial and remembered focus

Choose a useful entry control for a fresh page. After ordinary data updates,
preserve a still-valid focused item. A loading update is not a reason to reissue
a request for the search box.

If a refresh starts a new collection, let the collection reset signal handle
its position. If no refresh occurs, a retained route can keep loaded content and
focus. Keep that choice explicit and avoid overlapping restoration mechanisms.

See [`WidgetNavigator.cs`](../../src/WidgetSdk/WidgetNavigator.cs) for options,
route bounds, and methods. [`NavigationShell.cs`](../../src/WidgetSdk/NavigationShell.cs)
contains shared page-shell composition helpers.
