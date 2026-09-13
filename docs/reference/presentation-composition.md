# Presentation composition

Presentation helpers arrange existing elements into a reusable interface. They
save you from rebuilding navigation and focus relationships for every widget.

## Choose the level you need

| Helper | Responsibility |
|---|---|
| `UI.Stack`, `UI.Row`, scroll and grid containers | Arrange ordinary content |
| Shared setting rows, cards, and menus | Compose a familiar control pattern |
| `UI.NavigationShell` | Present a destination set as compact tabs or an expanded rail |
| `UI.NavigationShellParts` | Reuse that navigation inside a custom outer header |
| `WidgetNavigator<TRoute>` | Own route state and lifetimes, rather than draw the navigation |

The shell and navigator are complementary. The shell shows destinations and
emits their actions; your widget or navigator decides what selecting them does.

## NavigationShell

Use a shell for two to eight stable destinations. The host chooses compact
horizontal tabs or an expanded vertical rail from the available logical size.
Both arrangements share one content subtree.

Each `NavigationShellDestination` has an ID, label, action ID, and glyph.
`selectedDestinationId` identifies the active destination, not the focused control.

This complete example switches between two destinations and keeps a simple
counter in their shared content:

<!-- checked-example:navigation-shell -->
```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

public sealed class NavigationExample : Widget
{
    private string _selected = "home";
    private int _count;

    public override WidgetView Render() => new(
        UI.NavigationShell(
            "navigation",
            _selected,
            NavigationShellContentEntry.Available("increment"),
            UI.Stack("content",
                UI.Text($"{_selected}: {_count}", "status"),
                UI.Button("Increment", "increment", "increment")),
            new NavigationShellDestination[]
            {
                new("home", "Home", "nav.home", WidgetGlyph.Play),
                new("settings", "Settings", "nav.settings", WidgetGlyph.Settings),
            }),
        InitialFocusId: "increment");

    public override ValueTask OnActionAsync(WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action.ActionId)
        {
            case "nav.home": _selected = "home"; break;
            case "nav.settings": _selected = "settings"; break;
            case "increment": _count++; break;
            default: return ValueTask.CompletedTask;
        }
        Invalidate();
        return ValueTask.CompletedTask;
    }
}
```

## Entering the content

`NavigationShellContentEntry.Available(id)` names the focusable content descendant
to enter from navigation. If a loading or empty view has no such target, use
`Unavailable` rather than pointing to a control that does not exist.

An optional expanded pane sits between the rail and content in expanded mode.
Its entry target can be supplied separately. Compact leading/trailing adornments
are input-inert decorations, such as controller hints, not extra buttons.

## Custom headers

`UI.NavigationShellParts` returns `CompactNavigation` and `Body`. Place the compact
part in your own header arrangement and the body beneath it. Keep each part once;
do not recreate the same content tree for the compact and expanded modes.

Use the full shell when a standard arrangement works. Use parts when you need
a title, search field, or other header layout around the shared navigation.

The [Games & Apps presentation](../../src/FirstPartyWidgets/GamesAppsWidget/GamesAppsPresentation.cs)
and [YouTube presentation](../../samples/YouTubeWidget/YouTubeVideoWidget.Search.cs)
show real composition. Exact overloads and constraints live together in
[`NavigationShell.cs`](../../src/WidgetSdk/NavigationShell.cs).

Continue with [Routes](routes.md), [Collections](collections.md), or
[Controller components](controller-ui-components.md).
