# Spotify community widget

This controller-first reference is an autonomous full-trust Community
application. Its immutable package owns the Spotify Web API client, PKCE flow,
Windows credential-vault entry, response parsing, playlists, queue, devices,
playback policy, and the isolated Web Playback child process. It uses only the
generic application bootstrap and overlay protocol; no Spotify-specific product
capability or product-owned Spotify assembly is required.

Full trust is explicit: the application runs with ordinary current-user file,
network, registry, database, and child-process authority outside AppContainer.
Review the package before accepting that trust. The application never requires
a client secret, and tokens never enter snapshots, logs, or widget action data.

Open **Setup** in the widget, open Spotify's developer dashboard, copy and
register the exact redirect URI `http://127.0.0.1:43827/callback/`, then enter
the public Client ID through the host-owned text-entry modal. No terminal or
Client Secret is required. Choose **Connect** afterward; authorization remains
explicit, and merely opening the widget never launches a browser.

Version 0.3.3 keeps controller-first Client-ID onboarding available from the
configured Ready navigation rail, so an existing Client ID can be replaced
without disconnecting or using a terminal. Its Client-ID field stays within the
public host text-entry limit while the backend independently revalidates the
committed value. It retains the accepted vertical-rail responsive layout and its four
controller-first destinations:

The project imports the shared Community-package deterministic path map, so the
same commit produces a checkout-independent managed payload and sealed archive.

- **Player** keeps artwork, projected progress, transport, shuffle, and repeat responsive without increasing Spotify polling.
- **Queue** is fetched only when selected and remains cached while the widget worker lives.
- **Playlists** lazily appends bounded keyed windows, opens a continuous detail list, and can start the playlist or an exact URI-keyed track context.
- **Devices** transfers to Spotify devices and exposes **This overlay** through
  the package-owned Web Playback SDK child. Its short-lived token handoff and
  local device ID stay inside the application process tree.

When pinned, Spotify contributes **Compact now playing** and **Now playing +
up next** beside the host's always-available **Full widget** fallback. Both
projections reuse the ordinary playback snapshot, artwork, progress, transport,
and focus actions. The up-next projection admits the existing bounded queue
demand only while that layout is selected; deselection, removal, deactivation,
and destruction revoke that demand without adding another provider or poller.

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
multi-row first row continues forward into the list. Queue, playlist, and
playlist-detail rows expose exact adjacent focus edges; terminal input stays on
the terminal row rather than wrapping into earlier content or page chrome.
Failures retain the last-good window and require the visible Retry action. The
automated 29-item contract covers
compact and expanded 12/12/5 forward/reverse traversal for both playlist tiles
and detail tracks. A
physical-controller retest with live Spotify data remains part of the manual
release checklist.

The `wrail config` command remains available for developer diagnostics and
automation, but ordinary setup is complete inside the overlay.

The manifest declares no product capabilities. Network, browser, credential,
and child-process behavior belongs to the explicitly approved full-trust
application, while the product still owns package ingestion, authenticated
overlay IPC, bounded snapshots, presentation, lifecycle, restart, and removal.

Build and validate the standalone community package without installing it:

```powershell
.\samples\SpotifyWidget\Build-CommunityPackage.ps1
```

Use `-Install` to install, select, and enable the package in the local catalog,
or pass `-Catalog PATH` to target another catalog. The script supplies the
required explicit full-trust acknowledgement to the generic CLI. The archive
contains the package executable, backend, widget, Web Playback child/protocol,
generic application runtime, public SDK/protocol, manifest, and theme. It does
not contain `PlatformBroker`, `WindowsSpotifyProvider`, `PlatformSettings`, a
Client ID, or OAuth credentials.

The package reads the existing publisher/package-scoped public Client ID and
the existing Windows Credential Manager target, so updating from the retired
provider path preserves user configuration and refresh credentials without a
migration or deletion step. Live authorization, Premium eligibility, Web
Playback EME, and account/device behavior remain manual verification.
