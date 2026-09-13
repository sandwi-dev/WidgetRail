# Games & Apps

Games & Apps is the built-in launcher and application library. It is separate
from the Playnite add-on and uses WidgetRail's Windows app-library provider.

## Library and running applications

The library discovers supported Start menu applications and game registrations,
including supported Steam, Epic, GOG, and Microsoft/Xbox sources. Discovery is
based on those registrations; it is not an arbitrary executable browser or a
promise to find every game owned by an account.

Running apps aims to show application windows a user would recognize from Alt+Tab.
Browser tabs and virtual-desktop behavior are not part of an exact Alt+Tab clone.
Task Switcher is the dedicated widget for live previews, switching, and closing windows.

## Launching and saved entries

A running window and an installed application are different things. Closing a
game should not make its installed library entry disappear. The provider keeps
launch registration separate from current running-window information.

Saved entries use a durable application identity. The provider resolves and checks
the current launch target when an action is requested. An unavailable saved entry
can still be removed; its missing launch target is not a reason to trap it in the UI.

## Authoring boundary

The widget asks the typed app-library service for records and launch actions.
It does not receive permission to launch arbitrary paths or command lines. Users
review the required capability permissions through Settings like other system widgets.

For the full contracts, see [Capabilities](capabilities.md) and
[Windows providers](../maintainers/windows-provider-architecture.md).
For source examples, browse [GamesAppsWidget](../../src/FirstPartyWidgets/GamesAppsWidget/)
and [WindowsAppLibraryProvider](../../src/WindowsAppLibraryProvider/).
