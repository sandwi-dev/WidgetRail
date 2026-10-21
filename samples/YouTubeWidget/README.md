# YouTube Video

Your selected playback volume is remembered across restarts and applied when a video loads.

Watch a video without leaving your game. Search public YouTube videos, play a link,
or pin the video beside your game with controller controls still available.

![YouTube video playback with controller navigation and playback controls](screenshots/player.png)

## What you can do

- Search public videos in **Discover**, then switch to **Player** without losing your results.
- Paste a video link and start playback when you're ready.
- Pin the video or open fullscreen playback.
- Seek, adjust volume, mute, loop, and change playback speed.

## Get started

1. Install YouTube Video from **Catalogue**, or its published `.wrwidget` through **Settings → Widgets**.
2. Review the full-access prompt. A fresh interactive installation starts enabled; updates preserve enabled state.
3. Choose **Play a link** on the setup screen, or open **Player**, to play a public video link. Configure search below when you're ready.

### Enable search

Search requires your own Google API key. Playing a link does not require a search key.

1. Open [Google Cloud Console](https://console.cloud.google.com/) and create or select a project.
2. Enable **YouTube Data API v3** and create an API key. Restrict it to that API.
3. Open the widget's **Settings** with Y, then test and save the key.

The key stays in Windows Credential Manager. Searches run when you submit a query;
additional pages load as you reach the end of the results. Google applies its API quota.

**Open Google Cloud Console** and **Open in YouTube** use the Windows default
browser through WidgetRail's shared intent handoff. The host waits for controller
release and its close animation before launching the browser.

## Controller controls

| Control | Action |
| --- | --- |
| LB / RB | Switch between Discover and Player |
| A | Activate the focused control |
| Y | Open search Settings |
| LT / RT | Seek backward or forward by 10 seconds while playing |
| B | Return from Settings or exit fullscreen |

In pinned-video focus and fullscreen, **X** toggles play/pause. Follow the on-screen
guide for the actions available on the current page.

![YouTube video pinned in the corner while the player controls remain accessible](screenshots/pinned-video.png)

## Verify and remove the search key

To check playback, enter a supported public video link and press **Play**. To
check search, save the key in **Y → Settings** and submit a query in **Discover**.
Successful link playback does not verify the search key or its quota.

Replace a rejected key in Settings after checking its Google API restrictions.
**Delete saved API key** removes the local Windows Credential Manager entry;
link playback remains available. This does not delete or revoke the key at Google.
Disabling keeps the connection. Uninstalling removes host-owned widget state and
browser data, but does not generically delete that separate credential. Remove
it through the widget before uninstalling if wanted.

## Common questions

**Why won't a video play?** Private, removed, and embedding-disabled videos cannot
play here. Try another public video or open it on YouTube.

**Why doesn't playback start immediately?** Link entry cues the video. Select **Play** to start it.

**Can I choose quality or captions?** Quality is controlled by YouTube. For caption
choices that cannot be changed in the widget, use **Open in YouTube**.

## Build or customize

From the repository root:

```powershell
pwsh -NoProfile -File .\samples\YouTubeWidget\Build-CommunityPackage.ps1 -Configuration Release
```

The package is written to `artifacts/community-addons/youtube-video/` without installing it.
See [development notes](DEVELOPMENT.md) for the player architecture, security boundaries, and packaging details.
