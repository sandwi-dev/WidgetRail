# Standalone YouTube Music development

`Application` starts the public `WidgetApplicationBootstrap`. `Standalone` owns
the declarative widget, queue and private helper clients. `PlaybackHost` owns one
hidden WebView2 audio element. `Service/service.py` owns catalogue calls, session
protection, stream resolution and an authenticated-by-random-path audio proxy.
No Spotify or native-host implementation is changed.

## State and lifetime

Browsing uses four horizontal tabs and one rich focus target per result. Song
radio is a host-owned Menu context action. Settings and detailed playback are
compact secondary pages; the empty player is not rendered. Explicit focus-group
entry requests enter newly loaded pages and restore collection selection on Back.

- Page operations use the SDK's Active lifetime and latest-request cancellation.
- Sign-in and selected playback use the Widget lifetime, surviving Background.
- Cancel sign-in sends an explicit backend cancellation before canceling the local
  operation. Authentication cancellation is independent of audio playback.
- Real audio events provide playback state; command acknowledgement does not
  disable transport until a cloud state refresh.
- Selection generations reject old player events. Radio uses a fresh watch-playlist
  request and replaces the local queue only after its response is accepted.
- Queue ordering, repeat and shuffle belong to the application, not the browser.
- At most 24 catalogue responses are retained for five minutes. Resolved stream
  URLs have short lifetimes; only the next track is speculatively prepared.
- The player stops on parent exit or stdin closure. Application disposal closes
  both helpers, with bounded forced cleanup if they do not exit. The player drains
  its dedicated browser process; the parent removes its temporary profile after
  the helper exits, when WebView2 has released its remaining file handles.

## Packaging

```powershell
pwsh -NoProfile -File samples/YtMusicWidget/Build-CommunityPackage.ps1 -Configuration Release
```

The script prints the package path and does not install unless `-Install` is given.
Each default output directory is new. Explicit output directories must not already
contain a staged package. Runtime downloads are pinned in `Service/runtime-lock.json`
and verified against SHA-256 before extraction. Dependency licenses travel with
the package. yt-dlp uses zipimport to stay within the 512-entry package limit.

## Verification

The default managed verification includes `ytmusic-standalone-tests`. It checks
valid snapshots, queue rules, stale responses, independent transport, sign-in lifetime,
hidden-input rejection and bounded IPC. `test_service.py` adds isolated Python
checks for DPAPI, session deletion, proxy admission, radio requests and caching.
`test_player.py` runs the real WebView2 helper with a muted local WAV fixture.

The managed test executable also accepts `--package-root <staging-directory>`
for an opt-in live search/radio/muted playback check. It does not launch sign-in.
Live provider tests are not CI gates: YouTube service and account availability
are outside the repository's control.

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
