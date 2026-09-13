# State and updates

State is the information your widget remembers while it runs. Your view shows
that state. An action changes it.

## Start with a small value

The basic template stores a simulated level in a field. Its action handler
increments the field and calls `Invalidate()`:

```csharp
_level = Math.Min(4, _level + 1);
Invalidate();
```

On the next presentation update, `Render()` reads `_level` and describes the new
label. The user sees the level change. Keep network access and other side effects
out of `Render()`; it may run more often than a user presses a button.

## Group related state

As a widget grows, several values often need to change together. A search page
might have a query, results, and an error message. Updating these separately can
briefly describe inconsistent UI, such as new results with an old error.

`WidgetModel<TState>` groups related values into one state. You create it with
`CreateModel`, read its `Value` or `Snapshot`, and change it through `Set` or
`Update`. A changed model asks the widget to update its view automatically.

Prefer immutable state records. Return a new record for a change rather than
mutating an object already held by the model. The model uses equality to decide
whether there is a new value to publish.

## Choose the helper that fits the job

| You need to remember… | Start with |
|---|---|
| A few related UI values | `WidgetModel<TState>` |
| One fetched result and its loading state | `WidgetResource<TValue>` |
| A paged collection | `WidgetCursorResource<TItem>` |
| Which page is open | `WidgetNavigator<TRoute>` |

These helpers solve different problems. You do not need to put every resource
or route inside another model. Give each piece of state one clear owner.

## Reopening and refreshing are different

Hiding the overlay does not necessarily destroy the widget. A retained widget
can keep its route and loaded data when the overlay reopens.

A refresh explicitly asks for fresh data. A cursor refresh/reset starts a fresh
collection at the beginning; loading an adjacent page continues the current
collection. Avoid adding a second custom scroll-restoration system over this behavior.

For exact equality, publication, concurrency, and optimistic-update rules, use
the [model reference](../reference/widget-model-reference.md). Continue with
[Navigation and scrolling](navigation.md).
