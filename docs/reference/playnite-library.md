# Playnite Library

The Playnite add-on brings a Playnite library into WidgetRail. It is installed
separately and connects to a user-configured Playnite Bridge. Read the
[package README](../../samples/PlayniteLibraryWidget/README.md) for setup and
the supported companion configuration.

## Home

Home shows installed games in a poster rail. Its ordering is favorites, recency,
name, then a stable game ID. Loading more pages continues that ordering; it does
not re-sort only the currently visible posters.

Y refreshes the library. Menu opens the Home menu, which includes Library.
X opens options for the focused game, including Favorite. These hints are
separate from focusable posters so navigating the rail does not stop at a header button.

## Browse

Browse includes installed and uninstalled games. Favorites and Installed filters
can be combined. An uninstalled game has a visible badge; activating it explains
that it must first be installed through Playnite.

The responsive grid loads pages as the user scrolls or moves focus. The displayed
count describes the matching query, not just the pages currently held in memory.
There are no LB/RB page-jump shortcuts.

## Refresh and remembered position

Home refreshes once when its route becomes active. A refresh starts at the beginning.
Browse can reuse loaded pages and position when reopened without refreshing.
An explicit refresh or a changed query starts a fresh collection.

The cursor and host manage loading indicators, viewport filling, focus, and
scroll-offset compensation. These behaviors are shared with other cursor-based
widgets; they are not a separate Playnite navigation engine.

## Runtime and data

This is a full-access application widget. Its companion connection and credentials
belong to the package. The host's ordinary sandbox capability model is not a
substitute for reviewing this application before enabling it.

Its implementation is a useful example of the SDK, but the generated basic and
data templates are smaller starting points. See [Data and lifecycle](../developers/data-and-lifecycle.md)
for the cursor concepts before reading the complete sample.
