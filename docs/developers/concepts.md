# Core concepts

Think of a widget as a small application inside the overlay. It has data, a view
of that data, and actions the user can take. WidgetRail connects that view to
the screen and controller.

## Widget and host

The **widget** decides what to show and what its actions mean. A music widget,
for example, knows the current track and what should happen when Play is chosen.

The **host** is the WidgetRail application around it. It draws controls, places
them on screen, handles focus and scrolling, and applies the user's theme.
Ordinary widget code describes the interface rather than drawing pixels itself.

Most widgets run in their own worker process. The interface is passed to the
host as data. This is why there is a shared SDK instead of a requirement for
every widget to create its own Windows UI.

## View

`Render()` returns a `WidgetView`: a description of the current interface.
It contains elements such as text, buttons, and containers. A container holds
other elements and describes how they fit together.

```csharp
UI.Stack("content",
    UI.Text("My widget", "heading"),
    UI.Button("Refresh", "refresh", "refresh-button"))
```

Here, `content` and `heading` identify elements. For the button, `refresh` is the
action to run, while `refresh-button` identifies the control itself. They are
different responsibilities even when an example uses the same string for both.

## State and actions

**State** is the information that determines your view: a selected filter, loaded
tracks, or whether a request is in progress.

An **action** is a meaningful operation such as Refresh or Play. Pressing A on
a button can invoke that action. You handle it in `OnActionAsync`, update state,
and call `Invalidate()` to tell the host that the view may have changed.

```text
User selects a control → action handler → state changes → new view
```

`Invalidate()` is a request to update the presentation. It does not mean your
code should paint a frame or start another network request. Keep `Render()`
focused on describing state that you already have.

## Focus and IDs

**Focus** is the control that will receive the next activation. A stable ID helps
the host recognize the same control after your view updates.

Use an item's stable data ID for a game or track. Avoid assigning a new random ID
on every render or using a list index when items can move. See
[Navigation](navigation.md) for examples.

## Package and permissions

A `.wrwidget` file contains a widget's manifest, compiled code, styles, and assets.
The **manifest** names the widget and describes its version, runtime, and requested
permissions. Users install and enable that package through Settings.

Sandboxed widgets request supported **capabilities** to use Windows services.
For example, the widget asks the host for audio devices instead of opening a
native audio API directly. Users can refuse a permission, so your UI needs a
useful unavailable state.

Next: [State and updates](widget-model.md).
