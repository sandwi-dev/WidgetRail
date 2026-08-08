# Spotify community widget

This controller-first sample consumes only `WidgetHostServices.Spotify`. It never receives OAuth tokens, a client secret, or generic network access.

Configure the public Client ID for this package, register the exact redirect URI `http://127.0.0.1:43827/callback/` in Spotify's developer dashboard, then choose **Connect** in the widget. Authorization is always explicit; merely opening the widget never launches a browser.

```powershell
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples
```

The core surface supports locally projected progress, play/pause, previous/next, seeking, shuffle, repeat, refresh, disconnect, lifecycle-bounded polling, and partial host consent. Playback-control permission is optional.

Build and validate the standalone community package without installing it:

```powershell
.\samples\SpotifyWidget\Build-CommunityPackage.ps1
```

Use `-Install` to install and enable the package in the local catalog, or pass `-Catalog PATH` to target another catalog. The archive contains only the widget DLL, manifest, and public theme; the public Client ID and OAuth credentials remain in host-owned storage.
