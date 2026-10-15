# Host-owned web sessions

Status: implementation and autonomous validation complete; candidate ready for physical acceptance on `codex/hosted-web-sessions`, based on main
`61d64de3`. User authorized the reusable framework and Spotify/YouTube Music
adoption. No merge, push, release or installers are authorized by this task.

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
