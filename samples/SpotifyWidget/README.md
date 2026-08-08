# Spotify community widget

This controller-first sample consumes only `WidgetHostServices.Spotify`. It never receives OAuth tokens, a client secret, raw Spotify device IDs for the local player, or generic network access.

Configure the public Client ID for this package, register the exact redirect URI `http://127.0.0.1:43827/callback/` in Spotify's developer dashboard, then choose **Connect** in the widget. Authorization is always explicit; merely opening the widget never launches a browser.

Version 0.2 adds four controller-first destinations:

- **Player** keeps artwork, projected progress, transport, shuffle, and repeat responsive without increasing Spotify polling.
- **Queue** is fetched only when selected and remains cached while the widget worker lives.
- **Playlists** lazily loads the user's first bounded page, opens a scrollable detail page, and can start the playlist or an indexed track context.
- **Devices** transfers to Spotify devices and exposes **This overlay** through the trusted Web Playback SDK host. Tokens and the local Spotify device ID never enter widget code.

Wide surfaces use a navigation rail with a persistent player. Compact surfaces use tabs and one content pane. Playlist detail is a nested navigation entry: B returns to the exact playlist tile; B at a root destination remains available to the overlay shell. Search is intentionally absent until the SDK has a controller-appropriate text-entry contract.

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
