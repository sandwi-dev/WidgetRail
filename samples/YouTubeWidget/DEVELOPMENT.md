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

### WinUI collection migration boundary

Discover is the only unbounded collection in this widget. Its service exposes
`SearchAsync(query, pageToken, pageSize)`: an opaque forward continuation with up
to 12 admitted rows per request. The UI retains at most 48 rows; the service caches
at most 12 pages. Player transport, Setup, Captions and playback-rate choices are
ordinary bounded control trees, so they do not need collection conversion.

Discover does **not** currently satisfy the exact-count indexed SDK contract:

- Google `pageInfo.totalResults` is a reported search total, not the count of the
  widget's admitted rows. The service filters invalid IDs, duplicate videos,
  missing snippets and unsafe thumbnails. Reported totals can differ between calls.
- Tokens give no random-access range API or immutable order/revision guarantee.
  Reading position 500 would require traversing preceding tokens and spending
  provider quota; eagerly reading all pages would defeat existing bounds.
- The retained window count plateaus at 48 after eviction. It cannot be used as
  the absolute index of the next page. Exhausting a token does not prove that the
  current retained window contains the complete result.
- The service currently removes repeated VideoIds from cached prior pages. This
  is its existing result policy, not a general stable occurrence-index contract.

The cursor loader therefore supplies **neither** an absolute `FirstItemIndex` nor
`TotalItemCount`. The SDK retains its local monotonic window coordinate and exact
continuation/loading/anchor state. This prevents false extent validation failures
for empty searches, changing totals and forward loading after eviction; it is not
a WinUI collection migration or a claim of indexed/random access.

A complete migration needs a shared continuation-aware collection contract:

1. Captured query generation and an unknown extent, with explicit continuation
   availability independent of the number of retained/realized rows.
2. Bounded demand-triggered token reads, empty filtered-page progress, retry,
   cancellation and late-response rejection; no automatic unbounded traversal.
3. Stable admitted occurrence keys and a local logical sequence across append and
   eviction, with explicit handling of provider deduplication/repeated videos.
4. Exact captured item/query action and artwork leases, plus keyed focus return.
   A clicked result must not be resolved from a newer retained cursor buffer.
5. An explicit backward-navigation policy: the service currently exposes no
   previous token. Either bounded retained history or provider-token page replay
   must be specified; a host must not invent access to evicted results.
6. Atomic publication of each admitted page and its boundary/error state, with
   native lazy realization, focus and viewport anchoring over that logical data.

Until that contract exists, populated Discover remains unsupported by the WinUI
presenter's indexed-only collection path. The other routes' collection declarations
need no migration, but their control/media/fullscreen/pinned host capabilities
still need their own production acceptance.

Search remains a parked-media route. Paging must retain the established session
ID, sealed resources, origin rules and playback state without publishing a new
Cue/Load/Play command. `YouTubeSearchContinuationTests` exercises that boundary
and eight forward pages across eviction using a fake provider. No real playback,
API key or network access is required by those tests.

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

**Discover** and **Player** are flat sibling sections. Use LB/RB to switch between
them without adding Back history; the destination restores its remembered content
focus. **Play a link** is the Player section's empty/link-entry state, not a third
section. The separate in-widget **Y Settings** hint opens search configuration and
B returns to the exact control that opened it. Player settings remain nested behind
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
