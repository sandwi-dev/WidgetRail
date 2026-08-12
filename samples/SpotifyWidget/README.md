# Spotify community widget

This controller-first sample consumes only `WidgetHostServices.Spotify`. It never receives OAuth tokens, a client secret, raw Spotify device IDs for the local player, or generic network access.

Configure the public Client ID for this package, register the exact redirect URI `http://127.0.0.1:43827/callback/` in Spotify's developer dashboard, then choose **Connect** in the widget. Authorization is always explicit; merely opening the widget never launches a browser.

Version 0.2.14 is the current immutable Community package. It adds four
controller-first destinations:

The project imports the shared Community-package deterministic path map, so the
same commit produces a checkout-independent managed payload and sealed archive.

- **Player** keeps artwork, projected progress, transport, shuffle, and repeat responsive without increasing Spotify polling.
- **Queue** is fetched only when selected and remains cached while the widget worker lives.
- **Playlists** lazily appends bounded keyed windows, opens a continuous detail list, and can start the playlist or an exact URI-keyed track context.
- **Devices** transfers to Spotify devices and exposes **This overlay** through the trusted Web Playback SDK host. Tokens and the local Spotify device ID never enter widget code.

Internally, one non-partial widget owns lifecycle, provider calls, resources,
committed state, and invalidation. Closed value-only policies classify authored
route/actions and reconcile playback/device commands; a separate pure presenter
accepts only one immutable snapshot. These boundaries add no provider or OAuth
authority and preserve the package's authored IDs and controller graph.

Wide surfaces use a navigation rail with a persistent player. Left from the
inactive seek control returns to the currently selected rail destination;
compact surfaces retain the corresponding selected rail destination and show
one route in the remaining pane. The compact Player uses one focus-revealing
vertical viewport so artwork, metadata, seek/times, the complete transport row,
and attribution remain reachable at the documented 620x400 minimum. Playlist
detail is a nested navigation entry: B returns
to the exact playlist tile; B at a root destination remains available to the
overlay shell. Search is intentionally absent until the SDK has a controller-
appropriate text-entry contract.

Rendering captures one immutable presentation revision. Playlist detail is keyed
to both its playlist ID and selection generation, so Back, rapid reselection,
refresh, and lifecycle cancellation cannot pair a newer heading or route with
items from an older request. Cancellation-ignoring provider results are drained
without changing the current screen, and a retained detail selection reloads
when the widget becomes visible again.

Playback refresh and Active polling share one typed failure policy. A transient
provider outage, invalid response, or unexpected request failure keeps the last
accepted Player, route, and focus visible, adds one bounded warning with a safe
diagnostic code, and backs automatic polling off through 5, 15, then at most 30
seconds. Y retries immediately through the same policy. A successful response
clears the warning without navigating; permission revocation, authorization
expiry, and incompatible configuration still select their explicit safe state
and clear provider-derived data. Provider exception messages and response bodies
are never rendered.

Queue, playlist, and detail collections use protocol-v14 stable keys and the
shared bounded cursor resource. Twelve-row responses append or prepend into a
24-row retained window, so crossing a transport boundary enters the adjacent
item instead of replacing the visible list. Evicted rows are refetched on
reverse traversal, a short final page remains reversible, and refresh retains
the exact URI/playlist anchor or chooses a deterministic surviving fallback.
Media URI remains the semantic identity; repeated occurrences receive bounded
collection-context discriminators so equal tracks or episodes keep distinct
focus and action targets across retained pages and refresh churn.
Repeated identical edge input joins one in-flight provider request; a genuinely
different cursor intent remains latest-wins. Detail Play points to the first
row and that row points back to Play; a singleton row has no self edge, while a
multi-row first row continues forward into the list. Failures retain the last-good window
and require the visible Retry action. The automated 29-item contract covers
compact and expanded 12/12/5 forward/reverse traversal for both playlist tiles
and detail tracks. A
physical-controller retest with live Spotify data remains part of the manual
release checklist.

```powershell
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples
```

Playback control, local playback, and playlist access remain separately
optional permissions. The addon never gains generic network or WebView access.

Build and validate the standalone community package without installing it:

```powershell
.\samples\SpotifyWidget\Build-CommunityPackage.ps1
```

Use `-Install` to install and enable the package in the local catalog, or pass `-Catalog PATH` to target another catalog. The archive contains only the widget DLL, manifest, and public theme; the public Client ID and OAuth credentials remain in host-owned storage.
