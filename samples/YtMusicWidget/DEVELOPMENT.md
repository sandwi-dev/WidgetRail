# Standalone YouTube Music development

`Application` starts the public `WidgetApplicationBootstrap`. `Standalone` owns
the declarative widget, queue and provider connection. `WebSessionPlayer` opens
the sealed `web/player.html` through `HostServices.WebSessions`; WidgetRail owns its
controller and private profile in the shared WebView2 environment. The page receives
typed playback commands after session readiness, and the host authorizes only the
current exact loopback media URL. `Service/service.py` owns catalogue calls, session
protection, stream resolution and an authenticated-by-random-path audio proxy.
Provider logic remains package-owned. The shared Bridge caches widget styles by
package identity as well as appearance revision, so replacing a package applies
its current WRSS without restarting the overlay.

## State and lifetime

Home, Search, Library and Queue use the shared `music.nav` transition group.
The navigation selection and right browsing pane animate together; the player
stays stationary. Provider results, cursor loading and playback updates keep the
section key and cannot restart a transition. Reduced motion is host-controlled.

Browsing uses four horizontal tabs and one rich focus target per result.
A `WidgetIndexedCollection` realizes ranges from the provider's bounded in-memory
collection. This is UI windowing; it does not bypass the provider's 500-entry fetch cap. Home retains
provider section titles, with one responsive square-poster grid per section.
“Mixed for you” is promoted first when present; other sections retain provider order. Song
radio and Play next are host-owned Menu context actions. Play next inserts into both
the active queue and its unshuffled order, without changing the current song.
Playlist and album Play next fetch the bounded collection, skip non-song entries, then commit
one ordered block under the queue lock. Capacity trimming removes the same occurrences
in both orders; only the first inserted song is prefetched. Empty queues start playback.
The SDK compact navigation sits above a persistent left player and right browsing pane,
following the Spotify widget. Settings is a compact secondary page. Explicit focus-group
entry requests enter newly loaded pages and restore collection selection on Back.
Home, Search, Queue and each of the four Library filters retain separate indexed
collections and stable focus-group/scroll IDs. Section returns reuse loaded results;
Refresh, a new search, account changes and changed queue contents reset the relevant
collection. Library remembers its last filter.

Queue keys follow occurrence object identity, not song IDs or array indices. Each
new playback queue and Play next insertion owns distinct occurrence objects, even
when the caller supplies the same song object twice. Shuffle retains these objects.
Removal uses a captured queue snapshot and index, rejects stale snapshots, and
removes the exact occurrence from both orders. Membership changes publish a new
query; equivalent membership and playback changes use content updates. Old leases
cannot invoke actions against shifted indices.

Removing the current occurrence selects its successor, wrapping only for Repeat All.
Playing playback continues; paused playback waits for Play. A generation-bound stop
rejects delayed loads and cannot stop a newer selection. Pausing while that stop is
pending cancels the automatic continuation. Queue capacity remains 500.

Search suggestions are bounded local catalogs of loaded titles and 20 recent
searches. They replace the whole entry, make no provider requests, and reject stale
source keys. Account changes clear recent-search history.

Queue navigation emits one indexed `ScrollRevealRequest` for the synchronously
available current occurrence. It leaves focus on the navigation group. Empty entry
does not schedule a future reveal; membership generations retire pending targets.
Playback ticks, edits and overlay reactivation retain the request ID, so the host
does not reapply it. The queue publishes its bounded occurrence map for shared
membership-change focus and viewport continuity.

`Test-PackageAssets.ps1` checks the final archive for the hosted player's HTML and
bridge script, Python service/runtime and declared icons. Run it for cumulative
candidate packages too: ordinary package validation cannot infer runtime web paths
from the executable. An earlier candidate omitted the root `web` folder even
though the normal packaging script included it, preventing playback-session startup.

Song ratings use pinned ytmusicapi 1.12.3 `rate_song` with `LikeStatus.LIKE` and
`INDIFFERENT`; they do not invoke `edit_song_library_status`. Existing catalogue
`likeStatus` supplies known ratings. An unknown current song uses one bounded watch
playlist read; unknown remains unknown if the provider omits the matching track.
The account-scoped cache holds at most 1,024 song states. Writes are acknowledged
before publishing, coalesced, and serialized separately from playback. Provider
generation binding and local account epochs reject stale account requests/results.
Library focus memory belongs to the results scroll, excluding the filter toolbar.
New results enter at the first item; returning to a loaded list restores its last
focused result. Loading stays on the selected filter, and Up returns to that filter.
One reusable detail slot bounds playlist/album/artist state; Back reuses the parent section or reloads older detail
routes around their saved entry. This state is in memory, not persisted across restarts.

- Page operations use the SDK's Active lifetime and latest-request cancellation.
- Sign-in and selected playback use the Widget lifetime, surviving Background.
- Cancel sign-in sends an explicit backend cancellation before canceling the local
  operation. Authentication cancellation is independent of audio playback.
- Real audio events provide playback state; command acknowledgement does not
  disable transport until a cloud state refresh.
- Selection generations reject old player events. Radio uses a fresh watch-playlist
  request and replaces the local queue only after its response is accepted.
- Browser network/decode/unsupported failures publish one typed event per load,
  coalescing media errors and rejected play promises. Recovery bypasses both URL
  caches once, then advances without repeat/wrap. Three consecutive failed songs
  or queue end stop recovery. Permission errors require explicit Play. Status
  notices replace raw browser errors; manual selection and pause supersede pending
  recovery. Provider URLs and exception bodies stay private. This handles transient
  failures but does not establish why a particular provider stream failed.
- Queue ordering, repeat and shuffle belong to the application, not the browser.
- At most 24 catalogue responses are retained for five minutes. Resolved stream
  URLs have short lifetimes; only the next track is speculatively prepared.
  Foreground requests join an in-flight resolution for the same track and session;
  speculative work has no unbounded backlog. Player startup and stream resolution
  run concurrently, but loading still waits for both and checks the selected generation.
- Foreground resolution and speculative prefetch each retain their own yt-dlp instance.
  An instance is never used concurrently: a contending foreground request gets a
  temporary instance within the existing worker-pool bound. Instances are retired
  after extraction failures, after 32 extractions, on account changes, and on exit.
  Busy old-session instances close after their request finishes; their results cannot
  become playable in a newer session. yt-dlp's internal disk cache remains disabled.
- Resolved upstream URLs use a separate DPAPI-protected `stream-urls.dpapi` file in
  the widget's application-data directory. It is bound to the saved account session,
  limited to 512 entries / 4 MB, atomically replaced, and disposable on corruption or
  IO failure. Only URLs with an explicit signed expiry are persisted, capped at six
  hours with a two-minute reuse margin. Audio, cookies and local capability tokens
  are not cached there. Account changes clear it. The audio proxy retries resolution
  once on HTTP 401/403/404/410, retains Range requests, and keeps upstream URLs private.
- Playback session disposal closes its controller through the host. Worker exit,
  stop, disable, replacement and host shutdown retire the same session. The provider
  process remains package-owned and uses its existing bounded shutdown path.

## Packaging

```powershell
pwsh -NoProfile -File samples/YtMusicWidget/Build-CommunityPackage.ps1 -Configuration Release
```

The script prints the package path and does not install unless `-Install` is given.
Each default output directory is new. Explicit output directories must not already
contain a staged package. Runtime downloads are pinned in `Service/runtime-lock.json`
and verified against SHA-256 before extraction. Dependency licenses travel with
the package. Python runs with `-I -B` so late imports cannot write bytecode into
the sealed installation; environment flags alone are ignored under `-I`. yt-dlp uses zipimport to stay within the 512-entry package limit.

## Verification

The default managed verification includes `ytmusic-standalone-tests`. It checks
valid snapshots, song/playlist/album queue insertion and capacity rules, shuffle occurrence
identity, retained section focus, stale responses, independent transport, sign-in
lifetime, hidden-input rejection and bounded IPC. `test_service.py` adds isolated
Python checks for DPAPI, session deletion, proxy admission, radio artwork, bounded
playlist fetching and caching.
The native `--validate-web-sessions` fixture runs the actual packaged audio page
with a synthetic loopback WAV stream, without account access. It also checks
shared browser identity, private storage isolation and automatic Low memory mode.
The legacy standalone-helper `test_player.py` is retired with that executable.

The managed test executable accepts `--package-root <installed-package-directory>`
for the offline Python launcher/import-integrity probe. Playback now requires a
host session; it is covered by the native fixture and physical service testing.
Real provider playback and account availability remain outside CI acceptance.

Layout is rendered by the shared WinUI presenter. The retired native-renderer
`Test-Layout.ps1`/Taffy probe is no longer a supported command. Follow
[WinUI frontend development](../../src/OverlayFrontend.WinUI/README.md) for an
isolated host and native presentation fixtures. Those fixtures cover shared host
controls and collections; they do not certify every YouTube Music page's geometry.
Check empty/playing/library/Home/Search pages at the supported interface scales,
including transport containment, focus outlines, pinned layout and focus retention
after navigation and Refresh. Use the normal package themes and reduced-motion
setting when reviewing presentation.

Before acceptance, install the candidate, sign in, browse a private playlist,
start song radio, test transport and seeking, close/reopen the overlay while
playing, test the pinned layout and Now Playing controls, then restart WidgetRail
and verify session reuse. Account sign-in and physical controller behavior require
this user smoke test; mocked tests do not establish them.

## Boundaries

Authentication prefers Chrome, with Edge as fallback, and uses an isolated temporary
profile with an explicit nonzero loopback debugging port. Port zero is avoided
because Chromium uses it as an automation signal. Session data is
stored with current-user Windows DPAPI. Provider response bodies, cookies and
stream URLs never enter a view snapshot or diagnostic log. The local HTTP server
accepts only random handles for pre-resolved Google video audio; callers cannot
supply arbitrary upstream URLs. The player admits only its packaged page and
the exact selected loopback audio URL.

The old companion code now lives in `tests/YtMusicCompanionFixture` because existing
worker recovery tests exercise that deterministic sandboxed fixture. It is not
compiled into or distributed with the standalone application.
