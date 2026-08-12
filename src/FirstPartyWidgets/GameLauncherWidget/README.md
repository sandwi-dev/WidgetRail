# Game Launcher

Game Launcher is the controller-first first-party view over the trusted installed
application library. It can search, filter, organize, and launch only identities
resolved through the app-library capability; saved display rows never grant launch
authority.

The surface requests 980 by 700 DIPs and supports a 420 by 340 DIP minimum. The
header, library-source status, search entry, and filter strip are fixed chrome. One
stable vertical viewport (`game-launcher.library.scroll`) owns collection scrolling
for Library, Add games, Add running app, Hidden, and warm-state rows. Page actions
and query filters use bounded horizontal strips, while the collection keeps the
only vertical offset and stable item anchor.

Below 960 DIPs wide or 540 DIPs high, the same semantic tree uses a compact title
and bounded source-health summary. Larger surfaces show the expanded heading and
per-source detail. Both branches retain the same route, collection, action, and
focus identities; reopening or resizing does not create a second responsive page
tree or reset the collection offset.
