# YouTube Video development notes

[User guide and setup](README.md)

Discover/Player navigation opts into shared host transitions. Live embedded-video
surfaces retain the host's stationary fallback, preserving playback placement and
input alignment. Search refreshes keep the same section key.

This full-trust Community package searches public YouTube videos through the
YouTube Data API v3 and retains the existing link-to-play route. Playback uses
YouTube's official IFrame Player API inside WidgetRail's host-owned
embedded-media session. It does not use an account, OAuth, stream extraction,
arbitrary browsing, autoplay, or browser-owned fullscreen.

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

### WinUI discovered collection

Discover uses `WidgetDiscoveredCollection<SearchQuery, YouTubeSearchItem>`.
`SearchAsync(query, pageToken, pageSize)` remains an opaque forward continuation:
12 provider rows per request, with up to 512 retained result metadata entries.
Google's reported `totalResults` is never used as a native collection count.
Only validated, admitted videos contribute to the discovered prefix.

The SDK owns query generation, continuation, cancellation and atomic append.
Tokens never leave the worker. WinUI uses the same indexed range/lease path for
rows, realizes only native demand and loads another page at the discovered tail.
An append preserves the existing native source, prefix item keys, focus and
viewport. It does not replace the list or apply a custom scroll offset.

This widget explicitly selects `KeepFirst` duplicate policy: each VideoId is one
search result even after service cache eviction. General collections default to
rejecting duplicate occurrence keys. Search metadata history is never silently
evicted: at 512 entries discovery stops with a visible limit state and existing
results remain browsable. Refine or resubmit to begin a new query. A null next
token exhausts discovery; an empty filtered page can still advance a token.

Loading, failure, explicit Retry and the retention limit appear in the native
collection footer. Failed reads keep the previously admitted prefix. SDK timeout
and provider concurrency bounds still apply when a provider ignores cancellation.
Four consecutive empty/duplicate-only pages pause automatic demand and expose
**Load more**, bounding quota use without claiming the query is exhausted.
No real API request runs before explicit query submission.

Each row is one ActionSurface. Actions retain the captured query/video and keyed
return position, reject a replaced query, and never resolve an old index against
a mutable window. LB/RB return to Discover uses the exact indexed target. Search
remains a parked-media route: appends retain the established media session without
publishing another playback command. Tests cover changed totals, empty results,
eight pages beyond the old 48-row window, duplicate videos, captured actions,
keyed return, replacement rejection and parked media using a fake provider.

Player transport, Setup, Captions and playback-rate choices remain bounded
control trees. Their media/fullscreen/pinned acceptance is independent of this
collection change.

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

Volume completion waits for YouTube's observed integer percentage, just as mute
waits for observed provider state. `setVolume` is asynchronous even before first
playback, when no progress observations are emitted; acknowledging its old getter
value would incorrectly reconcile the host slider backward. The shared wait is
bounded and cancelled when command/media authority retires.

On Discover, press R3 to return to the search field. The shortcut uses a fresh
focus-group entry request, including when focus is inside a lazy result row.
LT/RT seek actions opt into `WhileHeld`; the host repeats only while their
captured input owner remains current, with no backlog while commands are busy.

**Discover** and **Player** are flat sibling sections. Use LB/RB to switch between
them without adding Back history; the destination restores its remembered content
focus. **Play a link** is the Player section's empty/link-entry state, not a third
section. The separate in-widget **Y Settings** hint opens search configuration and
B returns to the control that opened it. Lazy search results use their indexed
collection identity instead of an element ID in the page snapshot, with the
collection's normal validation and fallback if the item is no longer available.
Player settings remain nested behind
the player control. Search uses a compact task card; the player keeps the 16:9 video
primary and places icon transport, timeline, and compact volume on one row. The
native **Fullscreen** action asks
WidgetRail to transfer the same media plane into its host-owned overlay
fullscreen presentation; the YouTube iframe keeps its own controls, keyboard,
and fullscreen paths disabled. Its control uses WidgetRail's semantic Fullscreen
glyph rather than package artwork. Pinning remains host-owned.

1. Open **YouTube Video** and use LB/RB or the visible section header to choose
   **Discover** or **Player**. Use Y for search-key Settings.
2. In **Player**, focus the link entry, press A, paste a supported public video URL, and commit the host-owned text
   entry. The widget validates the URL and cues the video without autoplay.
3. Focus **Play** and press A. WidgetRail correlates that visible user action
   with the embedded player before playback starts.
4. Use LT/RT for bounded 10-second seek steps, or use the timeline and volume
   controls. The two compact transport slots beside Play/Pause open host-owned
   Fullscreen and native Captions/player settings. The web player remains inside
   its unobscured, minimum 200-by-200 media viewport.
5. With a current video ready on **Player**, choose **Fullscreen** to show
   only the aspect-fitted video plane. X toggles playback, LT/RT seek by ten
   seconds, and B exits back to the same focused native control. The tray and
   controller guide remain hidden while fullscreen is active.
6. With a current video ready on **Player**, the dashboard exposes X for
   Play/Pause, LT for a bounded 10-second seek backward, and RT for a bounded
   10-second seek forward. Other routes expose no YouTube dashboard actions.
   A slow command marks only its initiating transport control busy; other
   controls retain their normal appearance while the existing single-flight
   authority rejects competing input without queuing it.
7. Return to search without losing the retained query/result window. A compact
   pin remains the host-owned video-only presentation. In compact pinned focus,
   X pauses or resumes the current video in place and LT/RT retain their bounded
   seek behavior.
8. In Player settings, choose one of the bounded 0.5× through 2× speed options,
   mute without changing the authored volume, or loop the current video. Speed
   availability depends on the current video; YouTube can reject an unavailable
   choice without changing the last applied speed. Quality remains provider-owned
   Auto. Caption preferences are construction-time YouTube options, so the widget
   offers Open in YouTube instead of recreating the active player and losing the
   session.

Private, removed, unavailable, malformed, or embedding-disabled videos report
a bounded error in the native widget. Physical playback still depends on the
selected video's public embedding policy and the user's network.
