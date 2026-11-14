# Input and Discord refinements

Started 2026-11-12 on `codex/input-discord-refinements`, following accepted local
Discord source cleanup. Local implementation and verification only; no public
release, push, installation or changes to the cancelled desktop-client experiment.

## Authorized scope

- Shared modal Escape and outside-click dismissal. The user explicitly rejected
  adding a Close button; preserve existing headers. Child popups dismiss first,
  underlying controls never receive the dismissal click, and domain cancellation
  remains authoritative.
- Configurable global overlay keyboard shortcut, replacing the F1-only setting,
  with legacy migration, recording, conflict/error feedback and safe rebinding.
- Stable Playnite Home row geometry while indexed posters/favorites load; preserve
  lazy loading, focus and scrolling. Check applicability across collection consumers.
- Discord native capture pacing against advancing deadlines, with bounded recovery
  after stalls and idle behavior retained.
- Borderless live capture when supported and permitted by Windows, with ordinary
  bordered capture retained when unavailable. Do not alter Game Capture recording.
- Bounded backend invalidation events for faster Discord voice/speaking updates,
  using host-owned delivery and widget-owned provider subscriptions.
- Incoming stream health/details and on-demand connection measurements, without
  automatic replay of a share/join/watch side effect.

The earlier `modal-and-overlay-shortcut-plan.md` is background scope; its Close
button recommendation is superseded by the user's correction above. Serein was
reviewed at `29a390ce212342fe5c566ae7bf10ce9067c55cfa` as a source reference, not as
runtime proof or a replacement media engine. Pacing benefits remain a hypothesis
until measured. Do not promise better receiver quality from synthetic checks.

## Coordination and verification

Modal, keyboard and indexed-layout implementation proceed independently. Shared
protocol versioning, API baselines, host builds, integration and native launches
are coordinated centrally. Focus/native ownership changes require actual native
interaction evidence. Generated capture sources are used for unattended tests;
user microphone consent and real broadcast source selection remain user actions.
The user allows restarting the isolated candidate as needed.

Each feature must record affected consumers, compatibility decisions, focused
checks and any remaining live acceptance in this plan or its linked verification
record. Preserve current safeguards if evidence invalidates a proposed abstraction.

## Status

Implementation complete; local qualification and live limitations are recorded below.

- Shared modal contract is protocol 106; existing snapshots remain compatible.
  [Modal ownership and consumer review](modal-dismissal-implementation.md).
- [Keyboard shortcut implementation](maintainers/keyboard-shortcut.md): managed
  settings 33, Settings 94 and presentation-session 363 checks pass.
- [Poster loading refinement](poster-loading-refinement.md): no new placeholder
  protocol; existing responsive WRSS is shared by an inert reserve and real rows.
  Playnite layout tests (57) pass. Initial host compile corrections use a Grid-based
  Spacer because WinUI Border is sealed, plus explicit concrete array casts for
  CsWinRT trimming compatibility.
- Advancing capture deadlines have deterministic repeated-jitter/stall tests;
  native policy and audio queue tests pass. Border suppression uses the supported
  `IGraphicsCaptureSession3.IsBorderRequired(false)` request and gracefully keeps
  normal capture when unavailable. Windows consent/policy and other capture apps
  still decide whether a border is displayed; property assignment is not proof
  of suppression. Game Capture/preview exports and source selection are unchanged.
- Backend invalidations coalesce in the host, are payload-free and limited to one
  outstanding event until an admitted read. Discord observes bounded known stores
  and retires old listeners; low-frequency fallback polling remains for recovery.
  Hidden widgets without pins/sharing retain their existing five-second cadence.
  Node observer/backend checks (25) pass. Only Discord currently uses SessionBackend;
  other shipping widgets use isolated adapters or ordinary WebViews and are not
  given unsolicited actions. Existing ReadMessagesAsync is reused; no new SDK
  event queue or widget-authored focus/navigation machinery is introduced.

## Final local evidence

- SDK 177, SDK compatibility 17, PlatformSettings 33, Settings 94, presentation session 363,
  Playnite layout 57 and Discord 440 managed checks passed. Full Discord regression
  shard passed including source/minified backend, provider, receiver, media policies
  and bounded subscriptions. Managed/native presentation contract parity passed.
- Native modal 51, shortcut 22, poster 23 and backend 18 checks passed. Poster geometry
  remains identical before/after range arrival at 360/470/680 DIP viewport heights
  and 125% scale. Its fixture was corrected to use the real native item-container
  styling path; direct poster-panel styling had incorrectly added a second border.
- The actual shortcut settings editor recorded/enabled/saved a modified key and
  updated its displayed setting in an isolated profile. The trimmed production
  frontend opened/refreshed Discord connection details and real Escape restored
  focus to the caller. Images and JSON evidence are under `artifacts/input-discord`.
- Generated browser source preflight measured 60.04 FPS; its measured run cadence
  was 60.00 FPS. The 1440p60 local capture/WebRTC run passed with 59.79 submitted,
  55.02 encoded and 54.94 decoded FPS averaged over 13.13 seconds. The final instant
  receiver report was 59 FPS with zero decoder-reported drops; the worker's cumulative
  statistics recorded 52 drops, so this is not a claim of a lossless 60-FPS pipeline.
  The worker stopped normally without forced termination. Evidence:
  `artifacts/input-discord/capture-60fps-2/result.json`.
- A copied Debug build could not load the generated WebView source's theme resources.
  Publishing a self-contained validation candidate resolved that fixture startup;
  the failed attempt did not start capture. Test-only validation code is published
  untrimmed; the separate production frontend uses normal trimming and compiled-out
  validation. Both builds passed. No shipped trim checks were weakened.
- Updated Playnite archive validates/packs successfully. The isolated workspace has
  current Bridge/Settings/worker binaries. No existing installation/profile was
  updated, and no merge, push or release was performed. The installed app was closed with user approval
  and is intentionally not restarted.

## Remaining live acceptance

Real Discord account/provider availability, incoming pin counters under moving and
idle content, remote picture/audio quality and Windows border suppression still
need everyday live testing. Border suppression is a supported request, not proof
that Windows or another capturing app permits removal. Playnite's actual installed
widget needs the rebuilt package for the new explicit backdrop policy. Physical
controller/game testing is not replaced by injected native key and fixture checks.
These limits do not reopen the cancelled native-client migration or codec experiments.

## Follow-up: reported Library and app-wide lag

The default-profile candidate was verified as optimized Release. The local
Playnite and Discord package updates use revisions 0.2.146.1 and 0.1.0.1 through
the supported update flow. Investigation of the subsequent scrolling report
found repeated host guide scans during indexed row publication. The bounded
host correction, consumer matrix, native before/after measurements and remaining
cold-scroll limitation are recorded in
[Library scrolling investigation](playnite-grid-stutter-investigation.md).
The regular pauses were subsequently traced to retained computer-use/UI Automation
activity and disappeared after resetting that helper. Controlled reruns, native
regressions and the user's default-profile physical test passed. The guide fix is
retained; bounded publication scheduling, guide retirement, Bridge startup ordering
and a reproducibly packaged Playnite HTTP-response correction are also covered in
that investigation. Playnite remains selected at 0.2.146. No merge or release was
performed.

## Discord follow-up and consumer review, 2026-11-13

| Entry point | Applicability and evidence |
| --- | --- |
| Discord native controls, `samples/DiscordWebViewWidget/DiscordSession.cs` | Migrated. A missing notification grant in its separate profile was observed in the original investigation. The new explicit action requests host consent. Account/session-bound actions reject stale clicks. The website decides which incoming messages generate notifications. |
| Discord capture, `DiscordWebViewWidget.Sharing.cs` and `web/provider/adapter.js` | Migrated. Discord owns entitlement and quality persistence. Nine possible presets end at 1080p60 with fixed clarity. Reading a saved 1440p preference projects it to 1080p at the saved rate without mutating provider settings. Existing favorites and recents still use the same account-scoped private store. |
| Capture Library, SDK Gallery and Game Help | All three start native window captures. `OverlayShellPage.Capture.cs` now contributes the requesting widget ID during capture. Capture Library removed its redundant activity publication. The other two need no widget changes. |
| Browser, legacy Discord webapp, packaged receivers and isolated page adapters | Existing visible site permissions and browser notification delivery continue through their own profiles. Packaged receivers and isolated adapters cannot obtain backend permissions. Ordinary browser-managed screen shares lack a host-owned teardown signal. |
| Other shipping widgets, ControllerWidget template and SDK examples | Searched the capture starts, activity reports, session modes and permission entry points. No additional shipping backend consumer or template migration was found. Non-hosted players retain their own activity reports. The developer guide and SDK documentation index now cover the typed request. |
| Native UI worker and tests | Updated the synthetic Discord worker and session doubles. The SDK baseline has only four additions. Existing microphone callers retain their operation ID and typed receipt. Existing constrained and unconstrained capture callers retain their chooser behavior. |

The framework gap was explicit, interactive consent for a hidden backend
document. `BrowserSurface.BackendPermissions.cs` now shares the existing
document lease, retained profile lookup, chooser, cancellation and exact
permission callback. Only microphone and notifications are admitted. This is a
bounded SDK/broker/host extension modeled on the existing microphone operation.
Notifications additionally apply the user decision in an isolated top-frame
world to make the browser API usable during an existing call. The host remains
the owner of permission decisions and capture activity, including uncertain
worker shutdown. No widget traverses host UI or manages badge generations.

The first compact navigation run failed to pin an indexed Watch with the older
staged Bridge. With the rebuilt Bridge that interaction passed, and the test
then exposed its obsolete route from Mute to Leave after the grid reflow. The
fixture now uses the actual adjacent control. The rerun passed 49 native checks
each at 420×360 and 1040×680. The voice pin passed 49 checks across 320×180 and
400×180. The indexed failure's precise cause was not isolated.

Native WebView2 validation passed 52 checks including temporary and remembered
notification decisions, delivery to the host, permission isolation and stale
document retirement. The real synthetic-source capture run passed 29 checks,
including capture activity during hidden operation, failed teardown and retry.
The Discord worker passed 443 checks, the SDK 177, SDK compatibility 17, the
broker web session/capture suites 10 and the consent lifecycle suite 5. The
provider and session backend passed 153 JavaScript checks and the packaged
backend passed its five checks. Evidence is under
`artifacts/input-discord/followup-*` and the isolated native evidence directories
`wr-permission-fa7d8c` and `wr-badge-bc417a` under the local temporary directory.
Live Discord second-account message delivery and remote stream quality remain
user acceptance checks.

The trimmed, ReadyToRun Release frontend and matching Bridge started the real
package in an isolated profile. The requested default-profile restart then
selected Discord 0.1.0.2 through the reviewed local update flow. The catalog
kept its settings and the new worker reached Interactive. The installed backend
matches the tested package digest. Playnite Library remains enabled at 0.2.146.
The Discord package SHA-256 is
`0e98b7888e0963c08b2cf2eb99221a3bcf31feb9eadf20d439d2090a635169a9`.

## Incoming pin and outgoing stream quality, 2026-11-13

The incoming pin uses the current Discord receiver track as the source of a
second, video-only local WebRTC connection in `web/provider/adapter.js`.
`web/receiver.js` plays its decoded track, and the receiver page fits the image
to the pin. The default-profile Discord stream pin had 100% saved opacity. A
flat dark background sample in the supplied reference PNG was RGB (24,24,24)
throughout; the corresponding area in the pin PNG had five colors, most often
(26,26,26). The captures differ in time and display size. This observation does
not identify a uniform color conversion or justify a compensating filter.

In isolated Edge 157.0.4341.46, a moving 1280×720/30 text source first crossed
one WebRTC connection. Two more local senders simultaneously forwarded that
same decoded track. Frame IDs embedded in the source allowed comparison to the
direct video. Across two runs with reversed sender creation order, the default
sender delivered 320×180, targeted 0.6 Mbps, and reported bandwidth limitation.
The sender with a 20 Mbps ceiling and maintain-resolution delivered 1280×720 at
about 30 FPS. In the final window of the repeat, red-channel RMS error over a
text region after fitting to the same size fell from 30.85 to 1.46 levels out
of 255. These are matched samples at different frame counts. The second encode
remains lossy. A same-document direct video surface could avoid it, but the
existing generic hosted-popup entry point expects an interactive visible page.
A reusable document-scoped pin for media from a hidden session would need host
ownership, retirement and input/geometry qualification. Defer that larger change.

Only Discord's incoming Watch and Watch-from-activity paths use this relay.
Adapter 23 sets the local sender parameters after the exact lease's answer
and rechecks ownership after asynchronous work. Optional tuning failure retains
negotiated playback. Existing receiver replacement and single audio ownership
remain in place. The unminified provider/receiver suites passed 146 checks,
minified provider/backend passed 155, and the composed package passed 5.
Local package 0.1.0.3 contains only the new adapter bundle and manifest revision
relative to 0.1.0.2. Visual acceptance in a real Discord call is still pending.
The script and measurements are under `artifacts/input-discord/quality-pin-comparison`.

Outgoing host-owned web capture uses Windows Graphics Capture → BGRA readback
→ shared mapping → browser VideoFrame and generated track → Discord's sender.
There is no intermediate WidgetRail RTP encoder in this path. At
`tools/DiscordWebViewPrototype/LiveCapture.cpp`, drawing and readback precede
the video-credit check in `CaptureWorker.cpp`. Each full 1920×1080 BGRA copy at
60 FPS represents about 498 MB/s of pixel payload. The cached image also
supports idle refresh. Eliminating a redundant copy or skipping readback under
backpressure belongs to the shared native transport and would need a native
benchmark, resize/idle tests and exact-credit lifetime checks. The source audit
does not establish these copies caused visible artifacts.

The production-worker fixture with generated 1920×1080 moving content, clarity,
and an 8 Mbps simulated sender ceiling submitted/encoded/decoded
59.57/59.63/59.54 FPS over 11.10 seconds. It delivered full 1920×1080 with zero
decoder-reported frame drops in the final report. The worker had 39 cumulative
video-credit drops, including startup. This local test does not measure a remote
Discord connection. Evidence is in `artifacts/input-discord/quality-out-1080/result.json`.
After the approved update the default-profile catalog selected 0.1.0.3. The worker reached Interactive and its installed backend hash matched the tested bundle.

## Direct incoming video, 2026-11-13

The user reproduced blur, compression artifacts and significant stutter in
0.1.0.3 while the original stream was smooth. The default-profile worker and
visible native pin were confirmed. The bitrate experiment was insufficient in
live use. Its browser-only measurements above do not establish the quality of
the shipping pin. Supersede that path for the native Discord widget.

| Consumer or boundary | Decision and evidence |
| --- | --- |
| Discord Watch and friend activity Watch | Migrate both to the actual received video track. The host-owned child references that same browser track. The widget still selects the account, call, exact provider connection and participant, manages its provider subscription, and fences late work by lease. Source replacement updates the existing video element. |
| SDK, broker, Bridge and WinUI | A bounded new backend-video session uses the existing trusted worker/session/view ownership, document lease and native placement mechanisms. Only an owned backend may create its silent view. An old host rejects the operation. The page has no ordinary navigation, permissions, capture or widget RPC. Native source navigation, retirement or child closure ends its exact view. |
| Browser and legacy Discord webapp pop-outs | Existing user-opened pages keep independent popup and navigation policies. Browser-managed pop-outs require an interactive visible opener. Only the private, host-initiated video attachment can claim the reserved blank child. |
| Spotify, standalone YouTube Music, PoB, other widgets, templates and examples | Package players and document renderers have no trusted backend video track. The repository search found no other shipping consumer for this API. Existing profiles, audio providers and capture paths use their existing contracts. |
| Standalone Discord research prototype | Its caller still consumes SDP offers. Retain that path for the prototype. The shipping backend rejects Watch without an exact video-view reference. The shipped package omits the former receiver page and its scripts. |

The framework gap had moderate implementation complexity. WebView2's
`NewWindowRequested.NewWindow` lets the host bind a fresh same-profile
controller to the source. The same-origin blank document lets a muted video
element use the source track without a local RTP encoder, decoder, raw frame
copies or widget messages per frame. Host-owned media demand keeps the source
and visible video view at normal memory priority while they are needed. The
existing passive-pin policy applies to other browser content.

The two-controller native fixture placed an incoming synthetic WebRTC track in
an actual passive pin. The source and pin sampled identical colors. Over the
18-second interval spanning the idle memory deadline, each video reported 526
frames and the pin reported four dropped frames. Closing the pin preserved the
source track. Navigating the source invalidated its old child. Results are in
`wr-video-1e17b75/result.json` under the local temporary directory. Managed
Discord tests passed 439 checks and broker session tests passed six groups.
Minified provider, backend and packaged endpoint tests passed 161 checks.

An independent installed-package scenario starts the real worker and Bridge
with a synthetic provider in an isolated profile. Controller A on Watch opened
the native passive pin, then Open Discord displayed the original page next
to it. The pin and original HTML video elements referenced the same track
object. The pin's playback counters advanced from 7 to 98 with zero reported
drops in three seconds. Unpinning released the child and preserved source
playback. Result: `artifacts/input-discord/direct-installed-test/integration-1.json`.
SDK regression checks passed 177/177, the compatibility suite passed 17/17,
and the documentation contract passed. The default profile now runs the
trimmed Release frontend, matching Bridge and Discord 0.1.0.4. The installed
adapter bundle matches the tested package. A real remote call after the
restart remains a live acceptance check.

### Direct video loading indicator

The supplied recording shows an indeterminate line moving along the upper edge
of the playing pin. BrowserSurface configured the navigation progress bar
before CoreWebView2 was ready. A backend video child skips normal navigation
and can retain that initial visual. The host now disables the animation for
backend video and collapses its reserved row when it configures that source.
The shipping consumer is Discord Watch (including friend activity Watch).
Ordinary browser pages, package sessions, templates and examples do not create
backend video sources and require no migration.

The installed-package native Watch test asserts the progress control is
collapsed, its animation disabled, and the actual WebView starts at Y=0.
The side-by-side run advanced 90 frames in three seconds with zero reported drops.

The user confirmed the loading bar was gone and accepted the changes on
2026-11-13. Pre-merge cleanup removed the unused packaged receiver and its CI
step. The shipping Watch path is covered by direct-track provider, worker
lifecycle and native pin checks. The standalone research prototype continues
to use its own receiver for SDP experiments.

## Next steps for Discord pins

Scope from the shipping Discord widget and the WinUI host. The three items below are proposals. Simultaneous live Discord reception and the transparent voice presentation still need runtime evidence.

### Close a pin from its widget

**Framework gap, moderate effort.** `src/WidgetSdk/Pinning.cs` exposes only `pin.open`. `src/OverlayFrontend.WinUI/Shell/OverlayShellPage.Pinning.cs` already removes a specific expected surface and runs the selection teardown. Add an explicit, idempotent close operation beside open. Use the existing runtime, broker and presentation host-effect path. The host owns the window. Discord owns the choice to request closure of its voice or stream layout.

- Show **Unpin voice controls** or **Unpin shared screens** when the corresponding layout handle is selected. Keep **Stop watching** as an independent per-stream action. Releasing the last display demand should clean up widget-owned watches while preserving a watch already open in Discord.
- Bind a close request to the invoking widget/run, the layout, and the particular selection that was current when requested. A delayed close after unpin and reopen of the same layout must leave the new selection intact. Carry an opaque selection identity if the existing effect authority cannot distinguish those two visits. A second close of the retired selection succeeds harmlessly. An admitted request is distinct from an observed change in selection.
- Test close from the main widget and an interactive pin, already-closed, replacement and reopen races, background requests, different widget ownership, focus return, persisted placement and receiver teardown. Use the existing pin-request validation and SDK Gallery open examples as precedents.

### Transparent voice overlay

**Existing SDK presentation option, small widget change.** `WidgetSurfaceHints.Appearance` accepts `WidgetSurfaceAppearance.Transparent`. The host passes each pinned layout's own hints to its appearance policy. The explicit XAML island already has a transparent native backdrop. The shell's surface brush and its whole-window opacity are independent. See `PinnedWidgetWindow.cs`, `OverlayShellPage.Pinning.cs`, `OverlaySurfacePaint.cs` and `OverlayAppearancePolicy.cs` under `src/OverlayFrontend.WinUI/Shell`.

- Give the participant overlay the transparent hint and a transparent widget root with no acrylic or full-size card. The current `.discord-voice-pin` in `samples/DiscordWebViewWidget/styles/default.wrss` paints its own background. Its names, avatars and speaking state already come from `VoicePin`. Pair each visible name with its own ring and keep participant order stable as speakers change.
- Preserve the existing voice controls as a layout choice if shipping the names-and-rings-only design. Keep the screen-share pin opaque. A per-layout hint works without changing a global setting. Explicit appearance overrides, high contrast and reduced transparency retain their host-defined precedence. The opacity adjustment still fades the rendered foreground.
- Verify against a patterned desktop at 100% and reduced pin opacity. Check that the background is visible between people, the ring and text stay legible, and the existing host focus outline appears in interaction/placement mode. Passive click-through already applies to the whole peer. Per-pixel pointer pass-through in interactive mode would be a separate input feature.

### Multiple incoming shares

**Start with two direct video tiles in the existing shared-screen pin.** This is a larger Discord widget and provider change, with a bounded native-host feasibility test first. The watch flow uses one selected key/receiver and one latest operation in `samples/DiscordWebViewWidget/DiscordWebViewWidget.cs`, one health monitor, and one fixed web session key in `DiscordSession.cs`. In `web/provider/adapter.js`, starting a different watch retires the previous relay. A layout change alone cannot display both.

1. Open two uniquely keyed backend-video children of one backend, each with a different synthetic moving track. Put both in the same pinned layout. Verify both play concurrently, resizing retains them and closing one leaves the other rendering. The page adapter tracks video views by ID with a cap of four. The broker requires unique keys and caps **all** web sessions at four per owner and eight globally. The Discord backend itself consumes one. Two receivers therefore fit when capacity is available. Handle exhaustion locally. Capacity is independent of measured performance. See `src/PlatformBroker/WebSessionRegistry.cs` and `src/OverlayFrontend.WinUI/Browser/WebSessionPageAdapterHost.cs`.
2. Key watch state by backend session, account, call and stream. Give each active watch its own receiver, lease, selected track, health, demand and command lifetime. Preserve the existing per-connection quality policy and the distinction between Discord-owned and widget-owned subscriptions. Scope stop, reconnect, late callbacks and track replacement to one lease. Release all applicable leases on unpin, account/call change or worker teardown.
3. Retain the current full-size view for one share. Split two tiles across the long axis and preserve aspect ratio. Let the user add or remove each share from the call page, focus a tile to enlarge it, and return to both. Keep host interaction, move, resize, opacity and unpin operating on one window. Keep volume and mute attached to the indicated broadcaster. Maintain one audio path per stream through Discord and test for double playback. A second tile failure should leave the first playing.
4. Use two real broadcasters to check that this Discord client sustains watch demand on two connections. Compare per-track receive size/fps with each tile's presented/dropped frames during motion, with both shares visible, after resizing and after losing one. Measure bandwidth, CPU/GPU and memory before increasing the count. The release path should continue presenting received tracks directly. If the provider cannot sustain two watches, independent host windows will not solve it.

**Independent pins would be a separate platform feature, high effort.** They add independent monitor placement and make voice plus video visible at once. The grouped design above still occupies the host's one pin. The following changes would be needed before offering independent windows.

| Area | Current assumption and required change |
| --- | --- |
| Window ownership and selection | The shell owns a single `pinned`, pending widget and intent counter. Presentation sessions track one selection per widget and the SDK revokes the previous handle. Introduce a bounded registry of live surface instances, per-instance selection/teardown and a distinct active input target. Preserve current replacement semantics for old clients. |
| Controller, keyboard and focus | View toggles interaction with the sole pin. The unpin chord, B/Back, Guide/menu, pointer, scrolling, held buttons, keyboard, pickers and modals derive their target from that pin. With one pin retain the current shortcut. With several, let the user choose a named pin from the host menu, remember the last target, route input to exactly one, and capture that identity across asynchronous operations. Restore focus to the originating surface or main when it closes. |
| Placement, appearance and stacking | Move/resize/opacity and display reconciliation must name a target. Placement is already keyed by widget and layout. Repeated instances of one layout need stable slot keys and non-overlapping initial placement. Define peer order for passive windows, the active pin and notifications. Keep passive windows from activating on creation. |
| Lifecycle and resources | Compute visibility/demand across main and all pins, retain a worker while any pin needs it, and release resources when the last matching consumer closes. Carry per-surface authority through indexed ranges, artwork, browser attachment, prompts and stale events. Define bounds, capacity failures, per-pin close and teardown on widget removal. Popup and compact media pins have dedicated ownership and return flows that must continue to work. |
| Verification | Exercise two Discord pins, pins from different widgets and repeated instances. Close the active or passive one, switch targets during a prompt, unplug a display, hide/show main, and stop/restart the worker. Verify routing, focus return, geometry, teardown and live decode/load independently. |

Implementation references include `WidgetPresentationSession.Pinning.cs`, `src/WidgetSdk/Widget.cs` (selection lifetime), and the WinUI shell's `OverlayShellPage.Input.cs`, `OverlayShellPage.PinnedPlacement.cs`, `PinnedPreferences.cs`, `OverlayShellPage.PopupPinning.cs` and `OverlayShellPage.MediaPinning.cs`.

| Consumer | Applicability |
| --- | --- |
| DiscordWebViewWidget | Shipping consumer for all three proposals. The separate DiscordWidget sample is a different implementation. |
| SDK Gallery | Also requests named pins programmatically. Add a close example and exercise ownership and races if the new operation ships. |
| Playnite and Embedded Media | Existing transparent surface declarations provide appearance precedents. Their current layouts need no migration for a Discord opt-in. |
| Other pin-capable samples and first-party widgets | Existing named layouts, full-widget pins, popups and compact media keep current semantics for close and grouped video. Exercise representative consumers before adopting independent pins. |
| Widget templates and SDK examples | No programmatic close migration. Document the optional operation and its selection lifetime when implemented. |
