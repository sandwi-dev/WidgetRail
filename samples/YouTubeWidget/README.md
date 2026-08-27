# YouTube Video Community widget

This package accepts a public `youtube.com` or `youtu.be` video link and plays
it through YouTube's official IFrame Player API inside WidgetRail's host-owned
embedded-media surface. It does not use an API key, account, search API, stream
extraction, arbitrary browsing, autoplay, fullscreen, or a pinned layout.

The sealed package adapter is the top-level document. Its only external frame
navigation is `https://www.youtube.com`; package-declared exact origins and
protocol-v26 domain families bound the official player's intercepted resource
and redirect traffic. WidgetRail supplies the installed desktop application
identity as the outbound Referer only after that traffic is admitted.

## Build and package

From the repository root:

```powershell
pwsh -NoProfile -File .\samples\YouTubeWidget\Build-CommunityPackage.ps1 `
  -Configuration Release
```

The deterministic archive is written under
`artifacts/community-addons/youtube-video/`. Installation is intentionally a
separate, explicit action; package versions are immutable.

## Controller use

1. Open **YouTube Video** and focus the link field.
2. Press A, paste a supported public video URL, and commit the host-owned text
   entry. The widget validates the URL and cues the video without autoplay.
3. Focus **Play** and press A. WidgetRail correlates that visible user action
   with the embedded player before playback starts.
4. Use the native `-10s`, `+10s`, timeline, and volume controls. The web player
   remains inside its unobscured, minimum 200-by-200 media viewport.
5. Press B through the ordinary overlay navigation to leave the widget.

Private, removed, unavailable, malformed, or embedding-disabled videos report
a bounded error in the native widget. Physical playback still depends on the
selected video's public embedding policy and the user's network.
