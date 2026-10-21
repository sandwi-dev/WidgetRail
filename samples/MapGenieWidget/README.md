# MapGenie webapp

Browse MapGenie's maps inside WidgetRail. Install from **Catalogue** when a
compatible package is available. The widget opens the website so you can choose
a game without entering an address. Public features need no WidgetRail API key
or companion; MapGenie's account/subscription rules apply to its account features.

Choose a game and open a map to check the connection. Use the free pointer for
map markers and dragging when directional navigation cannot find a canvas control.
Test your intended map's search and filters; website rendering alone does not
verify all interaction. [Controller browsing](../../docs/users/browsing.md).

Sign in or out through MapGenie's own website. Disabling the widget keeps its
profile; uninstalling or **Clear local data** removes local sign-in and website
preferences, not your MapGenie account or remotely stored progress. Use the
site's own account controls for remote data. [Saved-data behavior](../../docs/users/lifecycle.md).

## Developer implementation

This widget uses the public webapp API with a persistent isolated profile,
on-demand website controls, pinning and hosted additional windows. It has no
website-specific scripts, API integration or extra widget capability grants.

## Build and install

```powershell
./samples/MapGenieWidget/Build-CommunityPackage.ps1
wrail install <path-to-published-package.wrwidget>
wrail enable widgetrail.samples.mapgenie
```

The builder validates the package and generates SHA256 sidecars; it does not
install or publish. MapGenie is catalogue-only and excluded from both installers.
Its curated listing becomes downloadable when a matching archive is published.

## Manual checks

- Choose a game and open a map; try marker selection, search and category filters.
- Check map panning and zooming with the framework's pointer/scroll controls.
- Pin the widget, resize it, then hide and reopen the overlay.
- Optionally sign in yourself and verify your map progress after restarting.

Offline package and scenario validation do not establish live-site compatibility.
Verify live map interaction on the website version being tested.
