# YouTube Music

Browse music and listen directly through WidgetRail. The standalone widget includes
Home recommendations in square artwork grids under YouTube Music’s section headings,
Search, your Library,
a local Queue, and a compact pinned player. **Mixed for you** appears first when available.
Collections scroll continuously as you browse.
**YTMDesktop is not required.**

<!-- Screenshot: add screenshots/library-player.png showing the library and player. -->

## Get started

1. Install the YouTube Music `.wrwidget` through **Settings → Widgets → Install from local file**.
2. Review the **full-trust** prompt and enable the widget.
3. Press **Y** for Settings and choose **Sign in with Google**. Complete sign-in in the separate Chrome window (Edge is the fallback).
4. Return to the overlay and open **Library**, or search for a song.

Library Songs, Albums and Artists use **Recently added** order. Playlists retain
YouTube Music’s default order because its playlist API does not expose sorting.

Public search and supported public playback can also work without signing in.
Your personal library requires an account. This is an unofficial YouTube Music
integration; available content, account restrictions and service changes can affect playback.

The package includes its own Python and JavaScript helper runtimes. You do not need
to install Python, YTMDesktop, or a Google developer app. Use a current WidgetRail
installation, which supplies the Windows Desktop runtime and checks WebView2 availability.
The standalone package currently supports Windows x64.

## Playback and radio

- Select a song to play it and queue the songs in that collection.
- Focus a song, press **Menu**, and choose **Play next** to insert it after the current song. The rest of the queue stays in place; an empty queue starts playback immediately.
- Choose **Start radio** to replace the queue with recommendations based on it.
- Use **Queue** to select a different queued song. The current song stays highlighted while you browse.
- Use the player on the left for playback, seeking, volume, shuffle and repeat while browsing on the right.
- Pin **Compact now playing** to keep controls beside your game.

**LT / RT** changes the four browsing tabs, **X** plays or pauses, and **LB / RB**
selects the previous or next song. **Y** opens Settings; **B** returns to your music.
While signing in, **Cancel sign-in** closes the temporary browser session without
stopping playback. Music continues when the overlay closes. Windows media controls and
WidgetRail's Now Playing widget can control the local player.

<!-- Screenshot: add screenshots/song-radio.png showing Start radio and its resulting queue. -->
<!-- Screenshot: add screenshots/pinned-player.png showing the compact player beside a game. -->

## Common questions

**I used the old YT Music widget.** This version replaces its companion connection
with an independent player. Sign in again and approve the new full-trust permission.
It does not reuse or delete YTMDesktop's pairing token or application data.

**What happens when the queue is full?** At the 500-song limit, Play next removes
the last queued entry to make room. If the current song is last, it removes the
first (oldest played) entry instead, keeping the current and next songs intact.

**Where is the stream cache?** It is encrypted for your Windows account in
`%LOCALAPPDATA%\WidgetRail\applications\widgetrail.samples.ytmusic\stream-urls.dpapi`.
It holds at most 512 URLs and 4 MB, with no audio downloads. Disconnecting clears it.
You can also delete just this file after quitting WidgetRail; it rebuilds as needed.

**Is the whole queue preloaded?** Only its metadata is loaded. The player buffers
the current song and prepares the next song’s stream URL when possible; it does not
download audio for the whole queue.

**Why does a song take time to start?** A new song needs a playable stream resolved
from YouTube before audio can begin. Valid stream URLs are cached across restarts,
and the next queued song is prepared in advance. Uncached selections can still take
several seconds. URLs expire—often after about six hours—or can be rejected earlier;
the player then resolves a fresh URL.

**A song does not start.** Select it again to resolve a fresh stream. If your library
also fails to load, reconnect in Settings. YouTube changes can require a widget update.

**Sign-in does not open.** Install Edge or Chrome. The widget uses a separate temporary
profile and does not inspect your existing browser profile.

**Where is the saved session?** In
`%LOCALAPPDATA%\WidgetRail\applications\widgetrail.samples.ytmusic\session.dpapi`,
encrypted for your Windows account. **Settings → Disconnect** removes it. To remove it
manually, quit WidgetRail and delete only that application's directory.

**Why is my very large library incomplete?** This version loads up to 500 entries
per collection. Cursor scrolling keeps only a bounded window in the UI. Search can reach
music outside that loaded collection.
Radio queues are finite; when they finish, playback stops unless repeat is enabled.

**What audio quality does it use?** The player requests the best available M4A/AAC
stream, falling back to other available audio formats. There is no bitrate selector;
the actual quality depends on formats YouTube makes available for that track and account.

## Development

See [development notes](DEVELOPMENT.md) for architecture, tests and packaging, and
[third-party notices](NOTICE.md) for dependencies and credit to LoZazaMastro's Now Playing.
The former companion integration remains only as a [regression fixture](../../tests/YtMusicCompanionFixture/README.md)
for the SDK's [local companion service](../../docs/reference/community-companion-services.md).
