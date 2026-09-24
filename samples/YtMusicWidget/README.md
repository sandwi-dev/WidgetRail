# YouTube Music

Browse music and listen directly through WidgetRail. The standalone widget includes
Home, Search, your Library, a local Queue, and a compact pinned player.
**YTMDesktop is not required.**

<!-- Screenshot: add screenshots/library-player.png showing the library and player. -->

## Get started

1. Install the YouTube Music `.wrwidget` through **Settings → Widgets → Install from local file**.
2. Review the **full-trust** prompt and enable the widget.
3. Open **Setup → Sign in**. Complete Google sign-in in the separate Edge or Chrome window.
4. Return to the overlay and open **Library**, or search for a song.

Public search and supported public playback can also work without signing in.
Your personal library requires an account. This is an unofficial YouTube Music
integration; available content, account restrictions and service changes can affect playback.

The package includes its own Python and JavaScript helper runtimes. You do not need
to install Python, YTMDesktop, or a Google developer app. Use a current WidgetRail
installation, which supplies the Windows Desktop runtime and checks WebView2 availability.
The standalone package currently supports Windows x64.

## Playback and radio

- Select a song to play it and queue the songs in that collection.
- Select **Start radio** beside a song to replace the queue with recommendations based on it.
- Use **Queue** to select a different queued song.
- Control playback, seek, volume, shuffle and repeat from the player below the page.
- Pin **Compact now playing** to keep controls beside your game.

**LT / RT** changes tabs, **X** plays or pauses, and **LB / RB** selects the previous
or next song. Music continues when the overlay closes. Windows media controls and
WidgetRail's Now Playing widget can control the local player.

<!-- Screenshot: add screenshots/song-radio.png showing Start radio and its resulting queue. -->
<!-- Screenshot: add screenshots/pinned-player.png showing the compact player beside a game. -->

## Common questions

**I used the old YT Music widget.** This version replaces its companion connection
with an independent player. Sign in again and approve the new full-trust permission.
It does not reuse or delete YTMDesktop's pairing token or application data.

**A song does not start.** Select it again to resolve a fresh stream. If your library
also fails to load, reconnect in Setup. YouTube changes can require a widget update.

**Sign-in does not open.** Install Edge or Chrome. The widget uses a separate temporary
profile and does not inspect your existing browser profile.

**Where is the saved session?** In
`%LOCALAPPDATA%\WidgetRail\applications\widgetrail.samples.ytmusic\session.dpapi`,
encrypted for your Windows account. **Setup → Disconnect** removes it. To remove it
manually, quit WidgetRail and delete only that application's directory.

**Why is my very large library incomplete?** This first version loads up to 500 entries
per collection and displays 12 at a time. Search can reach music outside that loaded window.
Radio queues are finite; when they finish, playback stops unless repeat is enabled.

## Development

See [development notes](DEVELOPMENT.md) for architecture, tests and packaging, and
[third-party notices](NOTICE.md) for dependencies and credit to LoZazaMastro's Now Playing.
The former companion integration remains only as a [regression fixture](../../tests/YtMusicCompanionFixture/README.md)
for the SDK's [local companion service](../../docs/reference/community-companion-services.md).
