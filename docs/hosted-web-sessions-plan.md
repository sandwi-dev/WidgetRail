# Host-owned web sessions

Status: implementation and physical testing accepted; integration into main
authorized on 2026-10-15. Implemented on `codex/hosted-web-sessions`, based on main
`61d64de3`. The user accepted the rare pre-dispatch latency outlier and the limits
of the short memory sample. No push, release or installers are authorized.

## Objective

Expose asynchronous, widget-owned web sessions independently of presentation.
Reuse WidgetRail's shared WebView environment, profile isolation, browser input,
permissions and placement. Remove the music widgets' separate playback WebView
helpers while preserving their provider backends, OAuth and saved settings.

## Design

- Session, view and policy are separate. Ordinary remote browser declarations
  remain convenient; explicit sessions support background operation and optional
  visual attachment. One live visual attachment per session.
- Sources are remote HTTP(S) pages or sealed package resources. Messaging is
  available only to the session's own packaged top-level document, never arbitrary
  remote pages/subframes. No raw host objects, cookie API or arbitrary script API.
- Typed requests/events use bounded asynchronous channels, cancellation, deadlines
  and generation-bound identities. Sensitive messages never enter snapshots,
  durable state, logs or notification history.
- Profiles are namespaced by widget identity; private/persistent storage and
  explicit within-widget profile reuse are supported. Sharing the WebView runtime
  does not share cookies or capabilities.
- Stop/disable/update/uninstall/process retirement cancels work and disposes
  sessions. Hidden active media continues; idle pages use existing memory policy.
- Network policy is explicit for packaged applications. Local stream access must
  be narrowly admitted; sandboxed pages cannot obtain ambient local-service access.
- Author support includes a JavaScript messaging library, SDK API, CLI template,
  documentation and tests. Avoid provider-specific host policy.

## Sequence

1. Define contracts and lifecycle/authority boundaries, with broker tests.
2. Connect authenticated widget requests to frontend session ownership and reuse
   browser surfaces/environment; validate packaged and remote synthetic pages.
3. Integrate visible session references and existing controller/pin behavior.
4. Migrate Spotify and YouTube Music playback adapters and remove helper hosting.
5. Update scaffolding, SDK baseline and documentation; validate external authors.
6. Review, run focused regression/real WebView checks, build a complete candidate
   with the normal profile preserved, and provide physical acceptance checks.

## Validation boundaries

Synthetic pages, streams and provider-adapter fixtures are the automated test
ground. Do not read credentials, send messages, join calls, capture personal
windows or retry previously blocked Discord probes. Account-dependent Spotify
protected playback and real service acceptance remain physical user checks;
report runtime limitations with reproducible evidence rather than hidden fallbacks.

## Progress

- Confirmed both music widgets own hidden WinForms/WebView2 playback helpers.
  Spotify OAuth uses a separate system-browser flow; YT Music's provider/backend
  process is separate from its playback helper and will remain where needed.
- Confirmed host browsers and embedded media already share
  `HostedWebViewEnvironment`, with separate per-controller profile authority.
- Preserved main's unrelated `docs/implementation-agent-goal.md` edit. The user's
  direct request authorizes this feature work; no legacy planner lane is resumed.

### Implementation and validation checkpoint

- Added protocol 83 session contracts, SDK 0.4.3-dev service and packaged JavaScript
  runtime, authenticated broker ownership and bounded queues, bridge rendezvous,
  and native session ownership reusing the existing BrowserSurface/environment.
- Migrated Spotify and YouTube Music playback to packaged hosted pages. Removed
  their separate WinForms playback helpers; provider/OAuth stores remain unchanged.
- Added the CLI `web-session` profile. All seven generated profiles pass the
  deterministic generation/build/package check (`templates02.log`).
- Broker: 84/84 passed (`broker-tests02.log`). SDK: 158/158 passed (`sdk-tests01.log`).
  Earlier provider checks: WindowsSpotifyProvider 40/40 and YtMusicStandalone32/32.
- Native worker/broker/bridge/WebView path: 8 checks passed (`native-bridge04.json`).
  The test exposed XAML's null Parent during ancestor retirement; the shared owner
  now records its actual placement container and safely detaches before reattaching.
- Native media and page adapters: 15 checks passed (`native-session10.json`). This
  includes actual YouTube Music HTML with an exact authorized local WAV URL, actual
  Spotify adapter HTML with a synthetic SDK, shared process identity, cookie/storage
  isolation, blocked ungranted localhost requests and audio continuing in Low mode.
- Fresh-profile autoplay during initial HTML parsing was rejected despite the
  origin's Allow permission. Sending playback after OpenAsync completes passed
  repeatedly, matching both migrated adapters. Documented the readiness boundary;
  no global autoplay relaxation or synthetic gesture was added.
- Existing webapp regressions: stop/reload/pin cleanup 5 checks and website settings
  across main/pin 16 checks passed (`native-webapp-stop01.json`,
  `native-webapp-settings01.json`).
- SDK compatibility 17/17; Spotify client 3/3; Spotify playback contract 5/5;
  Spotify application 6/6; packaged Spotify runtime 1/1; YT Music 32/32. The packaged
  runtime gate uses the repository's coherent managed verification installation.
  Its obsolete Display Profiles inventory assertion was updated to current catalog
  declarations; earlier runs against the developer candidate were fixture mismatches.
- Added cooperative request cancellation and serialized navigation-ID correlation.
  SDK baseline has 17 additive symbols. Documentation is in
  `docs/developers/hosted-web-sessions.md`.
- Review covered shared lifecycle/placement, profile and resource isolation,
  cancellation, private payload handling, package dependency removal and authoring
  support. Native placement regressions and actual adapters were validated.
- Built the complete release-style developer candidate at
  `artifacts/hosted-web-sessions/candidate01` (1,132 files), with catalog/package/icon
  integrity and frontend verification passed. No installer or release was produced.
- Updated normal-profile Spotify to 0.3.90 and YouTube Music to 0.3.43 through the
  hash-pinned local updater, preserving settings and restoring enabled states.
  Provider sign-in stores were not moved or read. Main's unrelated goal document
  remains untouched. The updated candidate is running under the usual profile.
- Remaining acceptance belongs to the user: real Spotify Play here/protected media,
  YouTube Music provider playback, controller controls, background continuity,
  stop/restart, sign-in and saved preferences. No merge or push performed.

## Physical checklist

1. Spotify: choose Play here and test play/pause, next and seek.
2. YouTube Music: play a song and test play/pause, next and seek.
3. For each, switch widgets and close/reopen the overlay; playback should continue.
4. Stop/restart the widget; playback should stop and a fresh player should work,
   with existing sign-in and settings retained.

## Regression investigation — acceptance requirements

User reported silent Spotify playback, the first Pause reverting, seek progress
rewinding and YouTube Music stuck buffering. Discord audio works. Prior counter,
fake SDK and direct-surface tests are insufficient acceptance for these migrations.
Keep the existing architecture initially; stop and report evidence if it cannot
meet the responsiveness target rather than introducing another transport.

- Warm widget-to-page delivery target: <= 50 ms. Report p50/p95/p99/max and every
  over-budget count; distinguish delivery, command completion and playback confirmation.
- Measure the real SDK/broker/bridge/frontend/page route, not a direct surface shortcut.
- Exercise sustained events, two concurrent sessions, hidden/visible/Low transitions,
  pause staying paused, seek settling without rewind, stream readiness and recovery.
- Measure queues, latency and failures without recording tokens, URLs with query
  strings, page contents or account data. No real messaging/calls or credential access.
- Preserve profile and sign-ins; no merge/push/release/installer authorization.

### Reproduction evidence

- `regression/latency01.json`: 300 measured SDK/worker/broker/native/page round
  trips, 2,520 unsolicited events and two simultaneous sessions. Worst RTT 12.02 ms;
  no RTT over 50 ms. Concurrent p95 1.86 ms. This is transport evidence, not music acceptance.
- `regression/spotify-commands01.json`: actual Spotify client and packaged page,
  synthetic external SDK, after 18 seconds idle. 100 pause/resume commands, 100
  seeks, 100 matching state queries and all 200 state events succeeded. Pause/resume
  RTT p95 1.41 ms, maximum 9.99 ms, none over 50 ms. No account/DRM claim.
- `regression/latency02.json`, `music-normal01.json`, `music-visible01.json`:
  actual YouTube page, synthetic loopback WAV, real production command route.
  Load reaches/returns from the page in milliseconds but remains buffering at
  position/duration zero. Disabling Low memory does not fix it; making only the
  WebView visible inside its collapsed parent does not fix it either.
- `regression/music-visible02.json`: making both the parking parent and WebView
  loaded/non-collapsed in a transparent 1x1 area fixes that same playback test,
  including stable pause and paused seek/resume, with Low memory retained.
  This matches the previous helpers' nominally visible offscreen windows. Further
  checking must cover overlay closure and avoid unnecessary visible-page work.
- `regression/spotify-real-before.log`: user reproduced two pauses and four seeks;
  native receipt observer confirms all reached the installed real playback page
  and received responses. Seek responses took 0.43-0.59 ms from native post.
  Browser audio remained false. Installed SDK has no send timestamp, so this
  trace cannot measure widget-to-host time or yet distinguish nested SDK errors.
- `music-prime01.json` and `music-hidden01.json`: briefly making the area visible
  then collapsing it, or hiding the containing overlay HWND, reproduces the failure.
  A one-time warmup or a parking area inside the overlay is insufficient.
- Added one lazy offscreen `DesktopWindowXamlSource`/`AppWindow` shared by explicit
  sessions, with no input, no switcher entry and no new browser environment/process.
  Existing controller transfer handles transitions to/from widget/pinned views.
  Low-memory target continues to follow actual WidgetRail presentation.
- `music-background01.json`: after 18 seconds idle with the actual overlay closed,
  stream startup, stable pause and paused seek/resume all pass; load RTT 6.57 ms.
- User physically confirmed Spotify and YouTube Music now work as expected,
  including the requested controls/background playback. Native trace confirms
  actual audio and Spotify command-completed replies. The completed resource
  sample and final candidate/regression results are recorded below.
- SDK regression runner: 158/158 passed (`regression/sdk-tests02.log`).
- `regression/memory-30s01.json`: 31 samples over 30 seconds of the running normal
  diagnostic profile, 25 processes. Private committed memory total 1114.5 ->
  1122.2 MiB; WebView 393.6 -> 401.3 MiB; frontend 320.6 -> 321.0 MiB; workers and
  services 400.3 -> 400.0 MiB. This is the complete app workload, not incremental
  backing-host overhead, and is too short to establish a leak or a steady plateau.
- `regression/native-lifecycle-final01.json`: 8 checks passed after the host fix,
  including cross-root view attach/detach, cancellation, worker replacement and
  controller close. Native host readiness also checks retirement and avoids
  duplicate attachment if reconciliation ran while its island was loading.
- Complete `candidate02` assembled from a fresh release publication: 1,132 files;
  catalog/package/icon validation and published frontend checks passed. Running
  with the usual profile; no music package replacement or sign-in changes were
  needed for this host fix. No merge, push, release or installer.
- `regression/latency-final01.json`: 405 measured commands and 3,360 events after
  the fix. Warm/event/concurrent/idle-Low p95 RTT: 1.46/1.61/1.90/1.50 ms.
  One events-phase request took 921.37 ms; all other measured requests were under
  50 ms. Do not report a clean worst-case latency gate. Its correlated trace
  (`latency-final01.trace.log`, request `23f68240b27c4b46a3328d7af44d7664`) places
  916.94 ms before native PostWebMessage dispatch; page receipt followed in 0.91 ms
  and response in 1.20 ms. Exact scheduling/queue cause is not established. The
  simultaneous normal-profile candidate was left running; no attribution to that
  workload is justified. Per the user's stop-and-report requirement, preserve the
  current transport and report this remaining responsiveness limitation rather than
  replacing the architecture or hiding the outlier with averages.
- Final native view/lifetime checks passed (8/8), SDK checks passed (158/158),
  synthetic media behavior passed and the user confirmed real playback/control
  recovery. Candidate02 remains running; only isolated validation processes closed.
- Integration acceptance (2026-10-15): the user explicitly accepted both the
  unresolved latency outlier and the limited duration of memory sampling, and
  authorized merging all current hosted-session work into main. These remain
  documented limitations, not additional merge blockers or claims of a strict
  50 ms worst-case guarantee. Preserve main's unrelated goal-document edit.
