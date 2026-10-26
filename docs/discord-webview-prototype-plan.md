# Discord WebView2 prototype plan and evidence

Status: core feasibility demonstrated; isolated host, not integrated with the production widget shell.
Updated: 2026-10-26.

## Objective

Replace the Vesktop dependency in the Discord-native experiment with an owned
WebView2 session. Preserve native voice controls, incoming video in a floating
receiver, and outgoing sharing through an application list plus an audio toggle.
Keep the working Vesktop prototype unchanged for comparison.

## Architecture gate

WebView2 ScreenCaptureStarting permits cancellation/deferral but does not accept
an application chosen by our own UI. The experiment therefore uses:

1. Native application catalogue and explicit source selection.
2. Windows Graphics Capture plus optional process-tree loopback audio.
3. Bounded WebView2 shared buffers carrying BGRA/PCM directly, outside widget JSON.
4. Generated MediaStream tracks supplied to Discord's getDisplayMedia request.
5. A provider-specific main-world adapter using feature-checked Discord modules.

No fake browser permissions, Chromium auto-select flags, screenshots-as-video,
recording files, credential export, or remote debugging port are used. The public
SDK/protocol is unchanged. In-process CDP is limited to this validation host.

Source identity checks bind HWND, PID, creation time and class, reject protected
sources and never silently select a replacement. Output is bounded to 2560x720
(to retain vertical detail on an ultrawide), with 30 fps for changing content and
a low refresh cadence for unchanged content. Audio is 48 kHz stereo PCM16 from the
selected process tree; no system-loopback fallback. Shared audio has a bounded
one-second queue and fails explicitly on overflow.

## Verified evidence

- C++ capture bridge builds with /W4 /WX. Multiple validation-enabled host builds
  and publications succeeded; unique binary logs are under ignored artifacts.
- The normal Release host also builds with validation disabled, zero warnings and
  zero errors. The prototype's implementations are excluded from that build.
- 41 JavaScript checks pass across the original adapter, web action authorization,
  generated-media lifecycle and module discovery.
- User signed into the separate WebView2 profile. No authentication was automated.
- Native voice search finds the designated Test server / Lobby. Native Join
  connected after replacing false-positive module matches with concrete APIs.
- The user started outgoing sharing through the native application/audio chooser.
  Discord reported sharing=true and the generated video frame count increased.
- User initially reported poor outgoing quality and spontaneous stopping. After
  first-frame metadata, resolution and lifecycle corrections, the user confirmed
  improved picture quality and that the stream stays running.
- Incoming receiver reported WebRTC connected and decoded hundreds of frames.
- Local WebRTC fixture received generated video and process-audio samples.
- Candidate 6 survived maximize, restore, minimize and restore while streaming
  audio. Over 7,000 video frames and 12 million audio samples were processed.
- Closing the fixture ended native capture with 0x80070578 (invalid window handle)
  and stopped the generated tracks. No alternative window was captured.
- Adaptive capture grew from 640x392 to 2560x720 and decoded at that new size
  without reconnecting. The stationary fixture was corrected to repaint on shrink.
- Microphone requests were recorded as allowed after the user's explicit session
  consent. Incoming and outgoing streams subsequently ran together.
- Reproduced the active timing loss: Sleep(2) scheduling yielded 21.393 fps in a
  standalone 30-fps pacing probe, versus 30.054 fps with a high-resolution timer.
  The local moving-pattern pipeline improved from 21.58 decoded fps to 29.79 fps
  (29.84 submitted), with zero drops during its 20-second measurement window.
- The updated Discord sender reached 30 fps; its 20-second YouTube sample ranged
  20-30 fps, averaging 27. YouTube's own Stats for nerds confirmed this particular
  source was 640x480 at 24 fps. This is not evidence of locked 30-fps source content.

## Corrections driven by implementation/testing

- Discord uses multiple webpack runtimes. Reading only the final runtime missed
  the main application. Discovery now inspects all observed loaded caches.
- Dynamic translation/service proxies manufactured arbitrary methods, producing
  false capability positives and no-op Join commands. Discovery excludes these
  objects and tests cover the failure. No arbitrary module factories are executed.
- Web voice selection uses the provider's concrete selectVoiceChannel action.
  Authorization and selected-channel validation remain provider-owned.
- Busy-control disabling transferred keyboard focus to microphone consent during
  a test. A subsequent Space accidentally enabled that session's microphone;
  no call was joined and the account stayed muted. The session was reset.
  Busy handlers now reject duplicate commands while retaining the focused control.
- The prototype denied microphone requests until explicitly enabled, so Discord
  repeatedly showed its denial dialog. Join is now gated on native session consent
  and the cached permission decision is reset when the user enables it. This
  still needs user verification on the replacement candidate.
- Resize originally ended capture and later kept the initial output dimensions.
  The replacement reallocates bounded output resources and recreates the frame
  pool, preserving the active stream and adapting resolution.
- Generated tracks now wait for an actual first frame, expose its real dimensions
  before handoff, and use the screen-detail content hint.
- Native result codes were lost during cleanup. Result and stop reason now persist.
- Frame acknowledgements alone were an incorrect idle-liveness test. A host-owned
  heartbeat maintains the media lease independently of page refresh/busy commands;
  outstanding buffers retain timeout protection. The idle-failure concern is
  source-derived; the stationary-window fixture continued receiving WGC frames.
- Active capture now uses a per-session high-resolution waitable timer and a frame
  arrival signal. It does not call timeBeginPeriod or change global timer resolution.
- Discord Error 2002 denotes low FPS while watching. The user explicitly accepted
  the idle optimization as long as moving content reaches the selected rate.
  A constant-cadence idle-frame replay experiment was therefore rejected and its
  extra repeat protocol/cache code removed. Idle repeats remain about 4 fps;
  active capture targets 30 fps. Do not suppress Discord's warning UI.

## Framework ownership and consumer matrix

| Area | Owner / current decision | Consumers |
|---|---|---|
| Window enumeration and identity | Reuse host WindowsTaskWindowCatalog and native identity checks | New prototype; shipping Task Switcher unchanged |
| Process loopback | Reuse ProcessLoopbackCapture, including existing bounds and guards | Game Capture unchanged; prototype new consumer |
| Capture-to-WebRTC buffers/lease/teardown | Host-owned WebViewLiveCapture in Validation | Capture fixture and Discord prototype use one implementation |
| Discord stores, commands and account/call checks | Provider adapter | Existing Vesktop adapter reused; web-specific action wrapper separate |
| Native focus, collection publication, pinned lifecycle | Existing SDK/presenter is intended production owner | Standalone feasibility UI only; shell migration deferred until media gate passes |
| Main-world page adapter and hidden-media permission policy | Genuine host/SDK contract gap, not supplied by a normal isolated-world adapter | All shipping WebSession consumers remain unchanged |
| Debug launch hooks | Conditional partial App hook and validation launch election | Shipping builds exclude Validation; no production launch change |

Shipping samples, first-party widgets, templates and SDK declarations do not call
these new validation types. There is no shared-contract migration to apply to them
in this experiment. A production implementation must revisit main-world authority,
hidden-session consent, foreground ownership, controller navigation and all affected
WebSession consumers before widening the contract.

## Remaining gates

- Broaden sustained-motion and hardware/load measurements beyond the local moving
  pattern and the user's 24-fps YouTube source; preserve the measured scope above.
- Exercise the final native Stop/Disconnect controls and controller navigation as
  part of the later shell integration; process teardown and source close were tested.
- End-to-end shared audio on the other account is not yet confirmed by the user.
- Quality selection is not exposed yet. Align supported Discord presets, native
  capture bounds and frame timing when adding the requested compact selector.
- Native controller-shell integration, production pin lifecycle, accessibility,
  hidden/background-session behavior, compatibility updates and performance budgets
  remain separate follow-up work. CPU readback is not a zero-copy production claim.
- Native capture-session Close can block in Windows/driver failure modes. This
  prototype is a separate process, but production containment needs an explicit
  decision before promoting the transport into the normal host.
- Production assets must be sealed separately from browser profiles/results;
  the current ignored development-assets folder also holds the test profile.

Error reference: [Discord audio/video error guide](https://support.discord.com/hc/en-us/articles/30952914470807-Discord-Audio-and-Video-Error-Codes-Troubleshooting-Guide).

Build and run instructions: [README](../tools/DiscordWebViewPrototype/README.md).

## Native widget integration: background-session foundation (2026-10-26)

The standalone prototype remains the working media candidate. This next stage
extends the existing WebSession contract instead of adding a second local server
or exposing CDP to widget code. It is not yet a completed Discord widget migration.

`WebSessionPageAdapter.Mode = SessionBackend` is an explicit, full-trust-only
execution mode. Its sealed package script runs in the exact top-level document's
main JavaScript world and registers `host.onRequest`. Requests and bounded batches
use the existing SDK correlation, cancellation and message transport. The host
owns document readiness, context selection and navigation retirement. Hidden
sessions retain their existing offscreen backing. The mode does not grant any
website permission, capture authority, arbitrary host object, or network response
observer. It rejects unsolicited native page actions.

This mode intentionally shares the website's execution world. It is not a security
boundary against that website or an isolation guarantee for the package script.
The broker admits it only for full-trust workers; sandboxed workers cannot opt in.
The distinct `OpenBackend` operation requires protocol 102 rather than silently
falling back on older hosts. Default isolated adapters omit the new mode field on
the wire, preserving their existing declaration shape.

### Ownership findings

| Location | Finding and evidence | Owner / disposition |
|---|---|---|
| `Browser/WebSessionPageAdapterHost.cs` | Existing isolated-page behavior intentionally cannot read Discord's JavaScript stores. Hidden host requests need a separate lifecycle policy. Source finding; new mode exercised with actual WebView2. | SDK/protocol/host; explicit backend mode, medium complexity. Electron's explicit execution-world/preload distinction is a useful precedent, not an equivalent security model. |
| `Browser/WebSessionPageAdapterHost.Requests.cs` | Waiting for document readiness introduces cancellation-before-admission and stale-document risks. | Host owns bounded readiness admission and cancellation; no widget retry counters. |
| `WidgetSdk/WebSessions.cs` | Cancelling a request before its post acknowledgement previously omitted its cancellation message. Source-derived; deterministic delayed-acknowledgement test now covers it. | SDK sends best-effort cancellation even before acknowledgement. This cannot undo already-performed external effects. |
| `Browser/BrowserSurface.Permissions.cs` | Hidden remote pages cannot request microphone permission through the current visible-page consent flow. | Still open. Add an explicit host-owned user-consent path before migrating native Join; do not automatically grant permission. |
| `Validation/WebViewLiveCapture.cs` | Capture still belongs to the standalone validation process and receives native source identity there. | Still open. Production integration needs permission-bound opaque source references, lifecycle ownership, and a decision on blocking native capture teardown. Do not expose raw HWNDs to the widget. |
| `WidgetSdk/WebSessionPageActionBatchReader.cs` | Current page-result batching caps the aggregate at 100 items; Discord can enumerate 1,000 voice channels. | Still open for the real consumer. Preserve existing PoB bounds while designing a bounded backend snapshot or domain query; do not silently truncate channels or hand-build transport fragments. |

### Consumer applicability

| Consumer | Status |
|---|---|
| PoB trade (`PobWidget.Preferences.cs`) | Keeps default isolated actions and response observation. Native synthetic comparison checks its execution-world and hidden-request policy. Live trade workflow has not been retested. |
| PoB passive tree | Packaged session; new backend mode does not apply. Shared SDK cancellation remains compatible. |
| Spotify / YouTube Music players | Packaged sessions; no migration. Their cancellation transport can now receive cancellation while an acknowledgement is pending. |
| Existing Vesktop prototype receiver | Packaged session; unchanged. Existing prototype remains available for comparison. |
| First-party widgets, templates and remaining samples | No additional page-adapter consumer found in repository search. Ordinary WebBrowser consumers retain their current policy. |
| Broker / SDK tests | Cover explicit authority/operation, owner retirement, wire compatibility, background lifetime, RPC batching and cancellation before acknowledgement. |
| Native validation fixture | Uses real BrowserSurface and WebSessionBackgroundHost, fresh synthetic profiles, and intercepted local documents. No account, microphone, or screen-sharing consent is involved. |
| Discord native WebView2 widget | Migration pending the permission and streaming contracts above. Do not mark the standalone media tests as proof of shell integration. |

Validation for this foundation:

- Broker console runner: 5/5 WebSession groups passed, including backend authority,
  wire shape, background lifetime and distinct-operation validation.
- SDK console runner: 1/1 WebSession group passed, including batch assembly,
  explicit backend opening and cancellation before post acknowledgement.
- Actual WinUI/WebView2 fixture: 14/14 checks passed. Includes cancellation before
  readiness (no page mutation), requests while hidden, the root context with a
  child iframe present, navigation retirement, unlisted-origin rejection,
  disposal and isolated-adapter compatibility. Eighteen deliberately uncooperative
  handlers were cancelled without exhausting the 16 active-request slots.
- Native result: `artifacts/discord-webview/backend-runs/06e6acb99b0f4e35a75c6faea1f27e11/result.json`.
- Validation-enabled Release publication and production Release build (validation
  excluded) succeeded with zero warnings/errors. No live Discord call, microphone grant or user
  capture selection was exercised by this fixture.
