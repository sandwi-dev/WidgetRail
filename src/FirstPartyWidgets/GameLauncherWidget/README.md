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

On a current Library tile, View opens a bounded details route projected by
`GameLauncherDetailsPresentation`. It displays the full normalized title and
source plus availability, launch, favorite, grouping, and preferred-variant
state without retaining another provider snapshot or launch token. A reuses the
existing exact-SavedId launch path; X/Y and the context-valid LB/RB actions reuse
the existing organization policy. B restores the originating tile and collection
offset. If that exact identity disappears, the route remains display-only and all
authority-bearing actions fail closed.

Variant organization remains the existing explicit two-game policy. Details
labels the first step **Choose another variant**, returns to the Library with the
first identity named in status, and labels a valid second step **Group with
selected game** or **Remove from variant group**. The details status reports the
committed result or failure; a same or stale identity cannot be paired.
