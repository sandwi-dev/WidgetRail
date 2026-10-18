# MapGenie webapp

Browse MapGenie's game maps from WidgetRail. This community widget uses the public
webapp API with a persistent isolated profile, on-demand website controls, shared
controller input, full-widget pinning and hosted additional windows.

The widget starts at https://mapgenie.io/ so users can choose their game without
entering a URL. It uses the standard `WebBrowserOptions.WebApp` policy and a custom
map-pin SVG. No website-specific scripts, API integration or widget capability
grants are needed. Website permissions remain under the host's normal controls.

## Build and install

```powershell
./samples/MapGenieWidget/Build-CommunityPackage.ps1
wrail install <path-to-widgetrail.samples.mapgenie-0.1.0.wrwidget>
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
Automated site inspection was blocked by the browser tool's site-safety policy;
no alternate route was used to access the site. Live map interaction was not
verified by automated checks.
