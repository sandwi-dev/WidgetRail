# Spotify development notes

[User guide and setup](README.md)

This controller-first reference is an autonomous full-trust Community
application. Its immutable package owns the Spotify Web API client, PKCE flow,
Windows credential-vault entry, response parsing, playlists, queue, devices,
playback policy, and the isolated Web Playback child process. It uses only the
generic application bootstrap and overlay protocol; no Spotify-specific product
capability or product-owned Spotify assembly is required.

## Local playback runtime

Local playback uses a hidden Windows Forms/WebView2 helper. WidgetRail includes
the .NET 8 Windows Desktop runtime for it in the application's private runtime
folder. Older installers that contain only base .NET need an application update
before **Play here** can start this helper. Updating the Spotify widget alone
does not supply that missing runtime.

## Trust and authorization

Full trust is explicit: the application runs with ordinary current-user file,
network, registry, database, and child-process authority outside AppContainer.
Review the package before accepting that trust. The application never requires
a client secret, and tokens never enter snapshots, logs, or widget action data.

Open **Setup** in the widget, open Spotify's developer dashboard, copy and
register the exact redirect URI `http://127.0.0.1:43827/callback/`, then enter
the public Client ID through the host-owned text-entry modal. No terminal or
Client Secret is required. Choose **Connect** afterward; authorization remains
explicit, and merely opening the widget never launches a browser.

Controller-first Client-ID onboarding remains available from Y/Settings while
Spotify is ready, so an existing Client ID can be replaced without disconnecting
or using a terminal. Its Client-ID field stays within the public host text-entry
limit while the backend independently revalidates the committed value. The
player remains visible beside four browse destinations:

- **Search** finds tracks, albums, artists, and playlists after explicit submission.
- The persistent player keeps artwork, concise metadata, projected progress,
  transport, shuffle, and repeat visible without increasing Spotify polling.
- **Queue** is fetched on demand for its page or the selected up-next pinned layout, and remains cached while the widget worker lives.
- **Playlists** lazily appends bounded keyed windows, opens a continuous detail list, and can start the playlist or an exact URI-keyed track context.
- **Devices** transfers to Spotify devices and exposes **This overlay** through
  the package-owned Web Playback SDK child. Its short-lived token handoff and
  local device ID stay inside the application process tree.

## Pinned layouts

When pinned, Spotify contributes **Compact now playing** and **Now playing +
up next** beside the host's always-available **Full widget** fallback. Both
projections reuse the ordinary playback snapshot, artwork, progress, transport,
and focus actions. The up-next projection admits the existing bounded queue
demand through its SDK-owned typed pinned-layout handle only while that layout is
selected. Selection revocation, removal, runtime replacement, unpin, and
destruction revoke that demand without adding another provider or poller;
ordinary overlay deactivation does not revoke a still-selected pinned surface.
Each immutable presentation supplies the focus target that exists in its exact
ready, empty, setup, connection, failure, or loading root while the typed handle
continues to own the stable layout metadata.

## State ownership

Internally, one non-partial widget owns lifecycle, provider calls, resources,
committed state, and invalidation. Closed value-only policies classify authored
route/actions and reconcile playback/device commands; a separate pure presenter
accepts only one immutable snapshot. These boundaries add no provider or OAuth
authority and preserve the package's authored IDs and controller graph.

## Navigation

The destination selector is one horizontal tab row at every responsive size;
the persistent player and selected browse page share the remaining pane. LT/RT
switch Devices, Search, Queue, and Playlists, including from playlist detail. A
fresh widget starts on Devices and loads that page after authorization succeeds.
Y opens
the nested Settings route and B restores the exact prior route focus. Playlist
detail is likewise nested: B returns to the exact playlist tile, while B at a
root destination remains available to the overlay shell. A transport shortcut
does not replace a still-valid browse focus merely because its separate player
control is temporarily busy. Search uses the host text-entry control; switching destinations retains the current search.

## Local playback commands

Local playback requests only Spotify's implemented streaming and account
eligibility scopes. After the SDK reports Ready, the package performs one
bounded `activateElement()` command before transferring playback. WebView2
grants ephemeral autoplay permission only to the exact trusted document and
Spotify SDK origins. Local Play/Pause uses the correlated SDK command only while
the current device is confirmed local by a successful transfer or a fresh
Web API device observation; explicit remote
selection revokes that routing and continues through the Web API. Autoplay
denial remains a visible, recoverable state. Routine successful action, queue,
token-delivery, and player-state traffic is intentionally not written to the
bounded diagnostics file; concise typed failures remain available.

## Now Playing state

For **Play here**, `player_state_changed` supplies one complete timestamped
snapshot: track, artists, album, artwork URL, position, duration, playing state,
repeat, shuffle, and control restrictions. The widget publishes it immediately,
including on pinned layouts. It does not combine old cloud track metadata with
new local transport flags.

The existing active refresh loop queries `getCurrentState()` while the local
device owns playback: normally every 5 seconds playing or 15 seconds paused.
Healthy local refreshes make no Web API player-state request. A newer SDK event
wins over an older pending query response. Failed or unavailable local reads
fall back to the Web API; losing local ownership wakes the refresh loop.
Remote playback uses `GET /v1/me/player` with the existing 5/15/30-second
playing/paused/idle delays. A fresh cloud device observation can discover an
external transfer back to WidgetRail, without overriding a newer device choice
or local event.

The 250-ms progress tick only repaints the timestamp-based position estimate.
Deactivation stops widget refreshes and UI event observation; the resident
helper can continue playing and retain its latest bounded state for reopening.

Play/pause, next/previous and seek use the local SDK when eligible. Device
discovery/transfers, search, playlists, queue operations, starting a specific
track or context, and shuffle/repeat commands still require Spotify's Web API.
No additional timer or unbounded event history is introduced.

## Presentation and cancellation

Rendering captures one immutable presentation revision. Playlist detail is keyed
to both its playlist ID and selection generation, so Back, rapid reselection,
refresh, and lifecycle cancellation cannot pair a newer heading or route with
items from an older request. Cancellation-ignoring provider results are drained
without changing the current screen, and a retained detail selection reloads
when the widget becomes visible again.

## Refresh and failure handling

Playback refresh and Active polling share one typed failure policy. A transient
provider outage, invalid response, or unexpected request failure keeps the last
accepted Player, route, and focus visible, adds one bounded warning with a safe
diagnostic code, and backs automatic polling off through 5, 15, then at most 30
seconds. Y retries immediately through the same policy. A successful response
clears the warning without navigating; permission revocation, authorization
expiry, and incompatible configuration still select their explicit safe state
and clear provider-derived data. Provider exception messages and response bodies
are never rendered.

## Collection windows

Playlist pages are cached after they are fetched, rather than downloading whole
playlists in advance. Reuse requires a fresh matching Spotify `snapshot_id`.
The hot cache holds up to 16 playlists, 64 pages, and 1,024 tracks in total.
The optional disk cache is bounded to 100 playlists and 50 MiB; disk failures
fall back to ordinary fetching. See [memory cache](SpotifyPlaylistCache.cs) and
[disk cache](SpotifyPlaylistDiskCache.cs) for the current policy.

Queue, playlist, and detail collections use protocol-v14 stable keys and the
shared bounded cursor resource. Collections retain bounded windows and fetch adjacent pages on demand. The current limits are defined by each resource in `SpotifyWidget.cs` and `SpotifyWidget.Search.cs`. Evicted rows are refetched on
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
Failures retain the last-good window and expose a Retry action. Tests cover forward/reverse traversal, repeated media identities, and stale-response rejection. Live Spotify playback and account eligibility still require manual verification.

The `wrail config` command remains available for developer diagnostics and
automation, but ordinary setup is complete inside the overlay.

The manifest declares no product capabilities. Network, browser, credential,
and child-process behavior belongs to the explicitly approved full-trust
application, while the product still owns package ingestion, authenticated
overlay IPC, bounded snapshots, presentation, lifecycle, restart, and removal.
The package seals the supplied black, green, and white RGB Spotify SVG originals
plus the supplied green CMYK full wordmark under `assets/icons`. The compact
green RGB mark brands the tray; the wide full wordmark keeps its natural aspect
in the widget header. Both use the public `WidgetIcon` contract, while playback
actions retain their semantic host glyphs.

## Build and package

The project imports the shared Community-package deterministic path map, so the
same commit produces a checkout-independent managed payload and sealed archive.

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
