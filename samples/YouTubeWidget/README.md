# YouTube Video Community widget

This full-trust Community package searches public YouTube videos through the
YouTube Data API v3 and retains the existing link-to-play route. Playback uses
YouTube's official IFrame Player API inside WidgetRail's host-owned
embedded-media surface. It does not use an account, OAuth, stream extraction,
arbitrary browsing, autoplay, or fullscreen.

## Search setup

The user owns the API key. In Google Cloud Console, create a project, enable
**YouTube Data API v3**, create an API key, and restrict that key to the YouTube
Data API. Open **Setup** in the widget and use its masked entry to test and
save the key. Testing uses a low-cost public `videos.list` lookup rather than a
search request. The package stores the key locally through Windows Credential
Manager, sends it only in Google's documented `x-goog-api-key` request header,
and never puts it in presentation state, logs, support data, or the package.

Configured startup opens public-video search. A query runs only after explicit
submission; bounded forward pages load near the end of the controller-focused
result list. Setup also provides explicit Replace and Delete paths. Quota,
offline, invalid-key, empty, and unavailable states remain native widget UI.

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

Search, link playback, and setup share one route bar. Search and setup use compact
task cards; the player keeps the 16:9 video primary and places icon transport,
timeline, and compact volume on one row. Fullscreen/settings remain in
the external player and pinning remains host-owned.

1. Open **YouTube Video** and choose **Discover**, **Play a link**, or **Setup** from the route bar.
2. Choose **Play a link**, press A, paste a supported public video URL, and commit the host-owned text
   entry. The widget validates the URL and cues the video without autoplay.
3. Focus **Play** and press A. WidgetRail correlates that visible user action
   with the embedded player before playback starts.
4. Use the native seek-back/seek-forward icons, timeline, and volume controls.
   The web player remains inside its unobscured, minimum 200-by-200 media viewport.
5. With a current video ready on **Now playing**, the dashboard exposes X for
   Play/Pause, LT for a bounded 10-second seek backward, and RT for a bounded
   10-second seek forward. Other routes expose no YouTube dashboard actions.
6. Return to search without losing the retained query/result window. A compact
   pin remains the host-owned video-only presentation. In compact pinned focus,
   X pauses or resumes the current video in place and LT/RT retain their bounded
   seek behavior.

Private, removed, unavailable, malformed, or embedding-disabled videos report
a bounded error in the native widget. Physical playback still depends on the
selected video's public embedding policy and the user's network.
