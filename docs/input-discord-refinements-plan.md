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

The agreed scope is to let a widget unpin its own surface, offer a transparent Discord participant overlay alongside the voice controls, and support up to **six simultaneous, independently placed pinned surfaces**. The six slots are shared across widgets and monitors in one running shell. The control mappings and admission behavior below guide the implementation. The progress section records completed checks and remaining verification.

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

### Discord incoming shares on independent pins

**Prototype two independently pinned shares, one directly presented video track per surface.** This exercises two instances of one widget layout. A transparent voice overlay can occupy a third slot. The watch flow uses one selected key/receiver and one latest operation in `samples/DiscordWebViewWidget/DiscordWebViewWidget.cs`, one health monitor, and one fixed web session key in `DiscordSession.cs`. In `web/provider/adapter.js`, starting a different watch retires the previous relay. A layout change alone cannot display both.

1. Open two uniquely keyed backend-video children of one backend, each with a different synthetic moving track. Once the host supports two pins, mount each child in its own pin. Verify concurrent playback, independent placement and resizing, and continued playback when its peer closes. The page adapter tracks video views by ID with a cap of four. The broker requires unique keys and caps **all** web sessions at four per owner and eight globally. The Discord backend itself consumes one. Two receivers therefore fit when capacity is available. Handle exhaustion locally. Capacity is independent of measured performance. See `src/PlatformBroker/WebSessionRegistry.cs` and `src/OverlayFrontend.WinUI/Browser/WebSessionPageAdapterHost.cs`.
2. Key watch state by backend session, account, call and stream. Give each active watch its own receiver, lease, selected track, health, demand and command lifetime. Preserve the existing per-connection quality policy and the distinction between Discord-owned and widget-owned subscriptions. Scope stop, reconnect, late callbacks and track replacement to one lease. Release all applicable leases on unpin, account/call change or worker teardown.
3. Offer Pin or Unpin on each broadcaster’s row. Label the host’s pin chooser with the broadcaster and widget names. Repeating Pin for an already pinned stream reuses its surface. Target move, resize, opacity and close at that surface, and target volume, mute and reconnect at the associated broadcaster. Preserve aspect ratio and direct-track playback, and test for double audio. If one stream ends or fails, the other pins continue playing.
4. Use two real broadcasters to check that this Discord client sustains watch demand on two connections. Compare per-track receive size/fps with each tile's presented/dropped frames during motion, with both shares visible, after resizing and after losing one. Measure bandwidth, CPU/GPU and memory before increasing the count. The release path should continue presenting received tracks directly. Six active receivers would require at least seven sessions for Discord alone (one backend and six children), six admitted video views, and spare global capacity for other widgets. Treat that as a separate resource and provider feasibility decision. The six-surface host acceptance test can use lightweight widget pins.

### Six simultaneous pinned surfaces

**Platform requirement, high effort.** A named layout, full-widget pin, detached browser popup or compact media pin each takes one slot. Main, notifications and transient dialogs do not consume slots. The protocol already permits a widget to declare eight layout choices (`src/WidgetProtocol/ProtocolConstants.cs`). The live-surface limit belongs to the host.

- Reserve a slot before asynchronous window creation and release it on cancellation, failure or close. Count pending opens toward six. At capacity, report that all six slots are in use and offer the host’s pin list so the user can choose one to close. Leave the six current pins intact. Reopening an existing instance needs no new slot.
- Identify each live surface by its owning widget/run, layout and a host-issued instance identity. Use a fresh selection identity when a closed pin reopens. An opt-in instance key distinguishes two Discord streams using the same layout. A legacy worker keeps its single selected layout and replacement behavior within its own widget while other widgets’ pins stay open.

Discord stream placement uses persistent screen slots within the WidgetRail profile. Slot 1 retains `widgetrail.samples.discord-controls:discord.stream`. Slot 2 uses `widgetrail.samples.discord-controls:discord.stream.2`. The first new share takes the lowest vacant slot and keeps it until unpinned, including through receiver reconnects. Closing the first slot never renumbers the second. The chooser displays broadcaster names while placement stores only slot geometry, monitor and opacity. Live account, call, broadcaster and lease identities continue to govern stream actions and teardown.

#### Move or resize a pin with a controller

1. Open WidgetRail, return to any tray icon and press **Menu**. Choose **Move / resize pinned widget**. With one pin, go straight to adjustment. With multiple pins, open the named pin chooser.
2. Outline the **actual pinned window** for the initially highlighted row, before the user presses **A**. As the user moves through the list with the **D-pad**, immediately transfer the outline to the corresponding window on its monitor. Keep controller focus in the chooser. **B** dismisses it, clears the preview and returns to pin controls.
3. Press **A** to adjust the outlined pin. Keep the outline on that same window. The **left stick or D-pad** moves it. The **right stick** resizes it: right widens, left narrows, down increases height and up reduces height. Both sticks operate in the same mode.
4. Press **A** to save the position and size, or **B** to restore both to their values at the start of adjustment. Clear the placement outline and return to pin controls, where the user can choose another pin or hide WidgetRail. **View** remains the shortcut for interacting with a pin's content.

The shell owns the chooser preview. Reuse the themed window outline in `PinnedWidgetWindow.cs`, which already draws above widget content and ignores pointer input. Associate the highlight with the live surface instance so two streams from one widget can be distinguished. If the highlighted pin closes before confirmation, clear its preview and require a fresh selection for adjustment.

**Native acceptance check.** Open two separately named stream pins and a transparent voice pin across two monitors. Before pressing **A**, cycle through all three and verify that only the matching window carries the chooser outline and that focus stays in the chooser. Exercise chooser dismissal, closing the highlighted pin, saving a move and cancelling a resize. Check the resulting bounds of all three surviving windows in the save and cancel cases.

Opening a pin resolves its preferred position on the saved monitor. If occupied, place the new window at the closest free position at the same size. When the monitor has no free rectangle that size, minimize overlap and then travel. Existing pins stay put. Automatic placement is temporary for an existing preference. Saving opacity changes only opacity. Saving in Move / resize adopts the displayed bounds. After closing and reopening, a free saved position is used again.

Directional movement at a display edge transfers the selected pin to the nearest aligned display in that direction. It retains logical size across DPI changes and uses the facing edge of the destination. Resize remains within the current display. Save adopts the destination monitor, while Cancel restores the opening monitor and bounds. A topology change during adjustment cancels the preview. The host caches the display topology for each adjustment and applies per-display interface settings after its save/cancel boundary.

Banner windows occupy no pin slots and never receive chooser highlights. Require eligible native and web notifications to consider every visible pin and shell obstacle, retain a valid previous banner position, and seek a free interior rectangle when the corners are occupied. Preserve source visibility rules, click-through presentation, inbox retention and activation of the exact originating web surface. Check notification delivery while six pins are present and during placement on mixed-DPI monitors.

#### Platform changes

| Area | Current assumption and required change |
| --- | --- |
| Window ownership and selection | The shell owns a single `pinned`, pending widget and intent counter. Presentation sessions track one selection per widget and the SDK revokes the previous handle. Introduce a bounded registry of live surface instances, per-instance selection/teardown and a distinct active input target. Preserve current replacement semantics for old clients. |
| Controller, keyboard and focus | View toggles interaction with the sole pin. The unpin chord, B/Back, Guide/menu, pointer, scrolling, held buttons, keyboard, pickers and modals derive their target from that pin. With one pin retain the current shortcut. With several, View from main opens a named pin chooser with the last target highlighted. A enters the highlighted pin. Inside a pin, View returns to main. B first dismisses the pin’s local UI, then returns to main. Apply the existing unpin chord to the active pin or offer the chooser if none is active. Bind held actions, text entry and prompts to the originating instance through completion. Closing a passive pin preserves focus. Closing the active one restores its origin if available, otherwise main. |
| Placement, appearance and stacking | Move/resize/opacity and display reconciliation must name a target. Placement is already keyed by widget and layout. Repeated instances of one layout need stable slot keys and non-overlapping initial placement. Define peer order for passive windows, the active pin and notifications. Keep passive windows from activating on creation. |
| Lifecycle and resources | Compute visibility/demand across main and all pins, retain a worker while any pin needs it, and release resources when the last matching consumer closes. Carry per-surface authority through indexed ranges, artwork, browser attachment, prompts and stale events. Define bounds, capacity failures, per-pin close and teardown on widget removal. Popup and compact media pins have dedicated ownership and return flows that must continue to work. |
| Verification | Exercise zero, one, two and six pins, including six from one opt-in widget, mixed widgets, repeated layouts, and legacy clients. With five open, race two requests for the last slot. At six, reject a seventh, reopen an existing instance, then close one and reuse the slot. Exercise active/passive close, stale close after reopen, failed creation, popup return, media controls, monitor removal, focus return during a prompt and worker restart. Verify surviving windows retain input routing, geometry and resources. |

Deliver exact-instance unpinning and the transparent voice layout first. Then introduce negotiated instance-aware selection and the shell registry, verify six native pins and controller targeting with SDK Gallery, and integrate Discord’s two-receiver prototype. Enable each new capability only for hosts and workers that support it.

Implementation references include `WidgetPresentationSession.Pinning.cs`, `src/WidgetSdk/Widget.cs` (selection lifetime), and the WinUI shell's `OverlayShellPage.Input.cs`, `OverlayShellPage.PinnedPlacement.cs`, `PinnedPreferences.cs`, `OverlayShellPage.PopupPinning.cs` and `OverlayShellPage.MediaPinning.cs`.

| Consumer | Applicability |
| --- | --- |
| DiscordWebViewWidget | Affected and migrated. Independent voice, transparent participants and two screen slots use exact-selection close. Managed, provider and synthetic native checks cover these entry points. Live simultaneous broadcasters remain unverified. |
| SDK Gallery | Affected and migrated. Published summary/level layouts use independent handles and close actions. Native tests exercise six windows, chooser targeting and stale-close rejection. |
| Playnite and Embedded Media | Existing transparent declarations already comply. Embedded Media uses the migrated host compact-presentation registry. A second isolated copy of the sealed local sample exercises independent playback and passive-peer teardown. |
| Performance Monitor, Spotify and Media Sessions | Legacy contracts remain applicable. Their behavior suites pass. Per-widget selections coexist through the host registry. |
| Browser, hosted webapps, MapGenie, Twitch and legacy DiscordWidget | Full-widget and popup entry points share the migrated host mechanisms. The native webapp fixture covers transferred pages, prompts, notifications and additional pages. Provider-specific live workflows remain unverified. |
| YouTube and standalone YtMusic | Existing media declarations use the shared compact-presentation owner. The local sample verifies native transfer mechanics. Live provider playback remains provider-dependent. |
| Remaining first-party widgets and samples | No direct pin-close adoption is required. The shared session retains legacy selection and aggregate visibility behavior. Catalog, runtime and compatibility suites cover the unchanged authoring contracts. |
| Widget templates and SDK examples | Legacy-compatible. SDK documentation now explains independent layouts and captured selection identities. Compatibility checks validate template/dependency metadata. |

### Implementation progress

Changes remain uncommitted in the Discord worktree on top of the accepted picker commit. The default-profile app runs the corrected icon-rendering host with Discord 0.1.0.10 and adapter 27. Live two-stream evidence is available after the subscription correction described below. The goal tool retains its earlier blocked status, and remaining verification is recorded below.

- Protocol 107 adds independent layout selection and opaque selection identities. Legacy layouts retain their original notification shape and one selection per widget. The close capability uses the existing action-completion and broker host-effect pipeline.
- Discord and SDK Gallery use independent handles and captured close requests. Discord has separate voice controls, transparent participants, and two incoming-screen slots. Stream placement keys remain `discord.stream` and `discord.stream.2`. Closing one preserves its neighbor's slot.
- The shell owns six live pins and one controller target. Chooser highlighting outlines the actual native window. Creation is serialized, capacity offers a replacement choice, and stale close requests cannot affect reopened windows. Capacity replacement preserves a popup's live page when moving it into a whole-widget pin.
- Collision avoidance preserves saved geometry until an explicit move/resize save. Opacity-only saves retain the preferred location. A window being replaced is excluded from the new window's collision obstacles.
- The instance-key proposal is narrowed to distinct published layout IDs, matching the existing layout, styling and demand contracts. Additional pop-outs receive numbered host placement keys.
- Browser residency and bridge retention share an eight-root ceiling. Six additional popup controllers are separately reserved before creation. Compact media has seven bounded sessions. These ceilings support six pins plus their main/backend dependencies.
- Notification visibility and placement use every pin. Banners keep their own lifetime, opacity and inbox state. Their obstacle list also includes XAML popups owned by pinned windows.

#### Verification evidence

| Check | Result and evidence |
| --- | --- |
| Native six-pin controller flow | 29 checks pass in `artifacts/input-discord/six-pins-multi-monitor/result.json`. Includes actual outline transfer, move cancellation, simultaneous requests for the last slot, exact close, stale close, six-pin banner placement, temporary collision displacement, opacity-only persistence and restoration of the saved position. Closing the highlighted pin clears its outline. Confirming the retired row leaves surviving windows untouched, and a fresh chooser can select a surviving pin. Movement between both connected displays preserves neighboring windows. Cancel restores the opening monitor and bounds, Save persists the destination, and the chooser outlines the window on the second display. |
| Native webapp and popup coexistence | 16 checks pass in `six-pins-fullweb-final/result.json`. Six full webapps coexist with a seventh main page. Notification activation targets its exact source. Six popup pins coexist, and capacity replacement preserves the replaced page. |
| Broader native browser integration | 223 checks pass in `six-pins-webapps-12/result.json`, covering repeated new-window choices, browser/popup transfer, dialog ownership, keyboard, notifications, picker cancellation, retained-session limits, recovery and shutdown. A second run passes 98 checks in `six-pins-webapps-repeat/result.json`. |
| Discord voice and transparent participants | 54 checks pass in `six-pins-voice-2/result.json`, including native focus, simultaneous voice/participant pins, transparent host fill and independent opacity. |
| Installed shipping Discord package | `six-pins-video-3/result.json` passes with adapter 25. The two synthetic tracks deliver 90 and 89 frames over three seconds. The first reports zero dropped frames. Closing one leaves the other playing and preserves the original source track. |
| Compact media | 46 native behavior assertions pass, including two independent players, targeted pause, passive-peer close with the active player's input preserved, and original browser retention. The first passive pixel check remains a known failure described below. |
| Managed contracts and consumers | Shell 362, presentation session 366, SDK 177, Discord 450, runtime 119, broker 93, Bridge 236, Gallery 13, Performance Monitor 13, Spotify 88, Media Sessions 31 and compatibility 17 pass. Shell placement checks include DPI conversion, display gaps, invalid destinations, reverse travel and resize remaining on its current monitor. |
| JavaScript and templates | 231 provider/prototype checks and 160 minified-provider checks pass. All seven CLI template profiles build and pass the grouped template test. |
| Generated contracts | Native parity verifies 192 managed constants. The SDK baseline has 3913 public symbols. |
| Release publication | A trimmed, self-contained release payload builds successfully in `six-pins-release-payload`, with source provenance marked dirty. The frontend including display-edge movement is refreshed in `six-pins-release-monitors`. ReleaseVerifier checks 302 local managed references and balanced emitted async lock cleanup. Its matching private runtimes run from `six-pins-candidate`, with the current frontend in `frontend-monitors`. |

#### Findings resolved during verification

| Owner and location | Evidence and correction |
| --- | --- |
| Host and bridge, `src/WidgetBridge/BridgeClientRegistry.BrowserRetention.cs` | Reproduced in the first six-webapp run. The bridge still refused the fifth retained browser after frontend capacity increased. Both now use `BridgeProtocol.MaximumResidentBrowserCount`, and the bounded-admission tests cover the ninth request. |
| Host, `src/OverlayFrontend.WinUI/Browser/BrowserSurface.Popups.cs` and `BrowserSurface.ContextMenus.cs` | Native traces showed a zero-sized Chromium viewport after XAML reported the child ready. Popup placement measures a visible destination. Page actions retain the current gesture briefly while native bounds settle, rejecting navigation/ownership changes during that wait. |
| Host, `src/OverlayFrontend.WinUI/Shell/NativeFilePickerLifetime.cs` | Reproduced in the full suite and an isolated picker probe. `GW_ENABLEDPOPUP` selected WidgetRail's mouse guard instead of the common dialog. The bounded search keeps process, owner-chain and dialog-class checks. The isolated cancellation probe passes without selecting a file. Browser open/save/multiple selection and extension file/folder pickers share this mechanism. Save/multiple/folder variants were source-checked. |
| Host, `src/OverlayFrontend.WinUI/Shell/OverlayShellPage.MediaPinning.cs` | Source review found a validity predicate tied to the selected compact player. It now checks the exact widget presentation. The two-player native checks verify independent playback and teardown. |
| Host, `src/OverlayFrontend.WinUI/Shell/OverlayShellPage.PopupPinning.cs` | Replacement carries the previous popup placement key through retirement. Other popup windows keep their existing keys. This uses the existing placement store. |
| Host, `src/OverlayFrontend.WinUI/Shell/PinnedPlacementAdjustment.cs` and `OverlayShellPage.PinnedPlacement.cs` | Source review found that movement always clamped to one monitor. Extending the existing adjustment transaction adds transfer through aligned display edges, stable logical size, save/cancel and topology cancellation. All authored, full-widget, popup and media pins use this host path. Native Gallery windows verify transfer, cancellation, persistence and chooser outline on both connected displays. Managed tests cover differing DPI scales and unsuitable destinations. |

#### Existing compact-media first-paint limitation

The first passive compact-media window captures as transparent while its local video is decoding. The same failure reproduces with the earlier `pin-chrome-validation` single-pin candidate, which passes its 40 behavior checks. Current two-player behavior checks pass 46 assertions. Both snapshots and a three-second Windows Graphics Capture recording reproduce the missing first passive pixels. The sealed reference video contains the expected marker at that timestamp.

This is a native-renderer follow-up. The controller records a visible document, nonzero viewport, advancing video frames, and full window/browser opacity. Attempts involving compositor commits, visible attachment ordering, a separate native backing, and hit-test eligibility did not resolve it. Those production experiments were removed. `scripts/Test-WinUiEmbeddedSample.ps1` retains its pixel assertion and adds an optional cold-pin reproduction, capture acknowledgements and recording evidence. Passive-media pixel verification remains incomplete.

The previous default candidate shut down normally. Discord was updated through the CLI to 0.1.0.6 and re-enabled. Catalog comparison shows only its active version changed. The refreshed frontend, matching bridge and installed Discord worker are running against the existing profile. `six-pins-candidate/startup-ui.json` confirms the production Discord page is available.

Live audio mixing, physical monitor disconnect/reconnect and sustained GPU load remain unverified. Physical movement between the two connected displays passes. The goal was previously marked blocked after live inputs remained unavailable across three consecutive turns. Live incoming video is now verified as described below. The native compact-media first-paint issue above remains deferred.

The completion audit rechecked the running production candidate and its installed Discord worker. `six-pins-candidate/live-readiness-ui.json` shows the channel browser with no active call or incoming-stream controls. Live reception therefore still requires two broadcasters. The additional chooser-retirement checks changed validation code only. Two preliminary attempts exposed incorrect focus assumptions in the new test, which were corrected to use the chooser's normal remembered selection. The final run passes all 25 assertions without production changes.

The later display audit found two connected displays and the movement limitation recorded above. The extended native run passes 29 checks with its host fix. The refreshed release candidate restarted through normal shutdown and startup, and `six-pins-candidate/startup-monitors-ui.json` verifies that the production Discord page loaded. Discord remains at 0.1.0.6 because the display movement change belongs to the host.

#### Live multistream subscription correction

The user reproduced starting a second stream stopping the first. The earlier synthetic native test established independent presentation of two tracks, but did not exercise Discord's real subscription replacement policy. The provider fixture also used one global viewer list, incorrectly making the second stream appear already watched.

The widget-owned fix is at `samples/DiscordWebViewWidget/web/provider/adapter.js`, in `watch`. Discord's current watch action accepts `forceMultiple`, and its `STREAM_WATCH` handler closes other subscriptions when `allowMultiple` is false. Every newly acquired widget watch now passes `forceMultiple: true` together with `noFocus: true`. This also preserves a stream already watched in Discord's page. The existing per-lease ownership and exact close logic remain responsible for cleanup. This is a provider policy correction within the widget. The SDK and host already supply independent views and lifetimes.

Research checked Vencord and PopIns. PopIns discovers existing Discord video elements for presentation. The authoritative subscription option and close behavior were confirmed in Discord's published web client. Sources are [PopIns video discovery](https://github.com/funteaqueue/PopIns/blob/main/utils/videoFinder.ts), [Discord multistream guidance](https://support.discord.com/hc/en-us/articles/360045784891-Video-Screenshare-Updates-Multistream-and-More), and [the inspected Discord client asset](https://discord.com/assets/web.25de09cac6a6078d.js).

The regression fixture now tracks subscription membership by stream and models ordinary-watch replacement. It verifies two actual watch calls, retention of a preexisting page watch, both relay timers, independent close, reopening, stale close and disposal. It fails on the previous adapter and passes with the fix. Evidence is `multistream-regression-before.log`, `multistream-provider-tests.log` with 176 passing checks, and `multistream-sealed-tests.log` with 161 passing minified/composed checks.

Discord 0.1.0.7 with adapter 26 was packaged and applied through the CLI, then the candidate restarted normally. Two different live game streams subsequently appeared in separate pins. `multistream-after-ui.json` also records two Discord Stop Watching controls. `multistream-live-1.mp4` and `multistream-live-2.mp4` record each pin for 12 seconds at a requested capture cadence of 5 fps. Their frame manifests contain 56 and 57 distinct images from 60 samples. The capture tool serialized the recordings. `multistream-live-composite.png` confirms both pins remained present together afterward. This verifies live subscription coexistence and changing video content, while stream frame rate, audio mixing and sustained-load quality remain unmeasured.

#### Participant overlay and contextual pin controls

The Discord overlay now uses compact 28-DIP avatar frames and 12-DIP names in individual rounded, theme-aware rows, with transparent space between participants. Speaking rings reserve a stable border width. The preferred overlay width is 240 DIPs. Native screenshots cover standard and high contrast in `overlay-layout-native-2/people.png` and `overlay-layout-contrast/people.png`.

Pin and unpin actions now sit beside their content. The participant heading owns its selected-state pin button, each watched stream card owns its exact-selection unpin button, and Stop watching is a compact trailing action inside that card. Stream history with no current card retains a contextual recovery row when required. The existing SDK selection identity and close capability continue to own stale-action rejection. A regression check ensures closing a live stream leaves no detached footer commands. The widget suite passes 451 checks, including that regression. The native voice/participants controller suite passes 54 checks, and a native stream unpin preserves both voice windows. The final package is Discord 0.1.0.9, retaining adapter 26.

Two broader presentation gaps are recorded separately. The current surface-hint validator requires at least 240 by 180 DIPs, and pinned placement does not consume the existing content-axis sizing modes. Consequently, the transparent participant window still reserves that minimum rectangle even though its visible rows are smaller. A reusable host/protocol extension should support bounded content-sized pins while preserving saved explicit geometry and resize semantics. Existing main-surface content measurement and content-sized anchored presentations are precedents. This is inferred from `WidgetProtocol/ViewSnapshotValidator.cs`, `ProtocolConstants.cs`, and `OverlayShellPage.PinnedAppearance.cs`. It needs shared contract and native-layout tests across pin consumers, and was not folded into this visual adjustment.

Desktop acrylic is also a host material feature. The current WRSS material creates an in-app brush over the host panel. Desktop blur requires an appropriate native backdrop and compatible transparent root fills, with tests for passive windows, opacity, rounded bounds, accessibility fallback and six-window cost. The rough implementation-and-verification estimate is one to two days. The reference locations are `WidgetViewPresenter.Styles.cs`, `PinnedWidgetWindow.cs` and `OverlaySurfacePaint.cs`; no desktop-acrylic change is included here.

#### Reference-style participant rows, stream ending and icon paint correction

Discord 0.1.0.10 uses separate round avatars followed by compact name/status capsules, matching the supplied Discord overlay reference. Names use 15-DIP text, rows have an eight-DIP gap, and the capsules fit their contents. LIVE badges identify local and remote broadcasters. Watching icons include provider-reported watches opened in Discord's own page. Mute and deafen indicators are passive. The voice-controls pin reuses the same vertical participant list and reserves enough height for four people above its three controls. Its authored minimum is now 320 by 340 DIPs. The participant pin button is immediately after the heading on the left, and the call scroll surface reserves sixteen DIPs of trailing padding.

When an authoritative provider read ends a watch, Discord withdraws that stream's published layout. The existing SDK/session/host layout-retirement path closes its window. No background user-action emulation or new close API was added. The read captures each watch revision before awaiting the provider, so a delayed read cannot withdraw a replacement watch. Managed checks cover late reads, exact retirement, surviving peers, restart after ending, account changes and indicators. `voice-list-tests.log` passes 457 checks. The native generated-video run in `participant-stream-end/result.json` verifies automatic removal of the first window while its neighbor keeps presenting frames, followed by clean teardown. These checks use a synthetic provider and the shipping package's normal entry points.

Native UI verification in `voice-list-native/result.json` passes 58 checks at 400- and 320-DIP widths, including four separate participant rows and unchanged controller action routing. `participant-status-preview/down-focus.json` records that one Down from Mute reaches the participant pin button. Rendered captures include `voice-list-native/voice-controls.png`, `voice-list-native/participants.png` and `participant-status-preview/scroll-gutter.png`. The provider and sealed backend checks pass 161 and 162 checks respectively, with adapter 27.

The user's clipping report exposed a shared host defect in `src/OverlayFrontend.WinUI/Presentation/WidgetPackageIconView.cs`. Its tinted ImageIcon child retained a font-sized layout and was scaled afterward. A native 14-DIP reproduction measured a 24-DIP child painted 4.8 DIPs off center. The earlier Discord geometry checks only measured the outer icon containers. The corrected view measures an intrinsic desired size bounded by the available slot, arranges the tinted child at its actual paint size, and preserves padding and intrinsic sizing when authored dimensions are removed. Discord's avatar styles also reserve the same border width in speaking and listening states.

`icon-small-before-result-2.json` records the failing regression. `icon-small-after-result-2.json` passes 82 native checks, covering small and large tint slots, padding, intrinsic-size restoration, original-color SVG pixels, theme changes, retained assets, buttons, menus, selectors, catalog recycling, failure recovery and bounded cache behavior. The final Discord native checks run on the corrected host. The fix belongs to the host and uses the existing icon view and raster cache.

| Shared icon consumer | Applicability and evidence |
| --- | --- |
| Discord icons in both pins, participant cards and controls | Affected. Native screenshots and voice/controller checks cover the corrected presentation. |
| SDK Gallery, Playnite, Spotify, standalone YouTube Music, YouTube search, Display Profiles and PoB comparison icons | Already use package-icon APIs routed through the shared view. Source entry points were checked, with no widget migration required. Generic native tests cover the same inline and button rendering path. |
| Settings, Games & Apps, Power, Audio Mixer and Network Controls | Already use the shared package-icon view through published nodes and host catalog content. Native catalog tests cover reuse, fallback, selection and late replies. |
| Rail/catalog and radial chooser | Affected shared view. Explicit icon sizes remain supported; the native catalog tests and normal candidate startup cover the catalog path. |
| Semantic glyphs and user webapp bitmaps | Separate FontIcon and Image branches retain their existing behavior. Generic fallback and original-image tests remain passing. |
| Templates and SDK examples | No API or protocol change. Template profiles use semantic glyphs and standard buttons. SDK Gallery exercises package icons through the shared presenter. |

The final trimmed host publication is `six-pins-release-icons`; ReleaseVerifier checks 302 local managed dependencies and emitted async cleanup. It is staged as `six-pins-candidate/frontend-icons`. The matching Discord 0.1.0.10 package is `voice-list-package/Discord-0.1.0.10.wrwidget`. The previous candidate shut down normally, the CLI applied the package, and the updated host and worker started against the existing profile. `voice-list-package/installed-pins.png` records a live game stream alongside the revised real-account participant overlay, including LIVE and watching indicators. Native fixture screenshots remain the evidence for the voice-controls vertical list and automatic stream-end transition.

#### Browser-slot inset removal for every pin

The host now sets `BrowserSlot.Host.Margin` to zero whenever `BrowserSlot.Pinned` is true. Ordinary embedded slots use their existing three-DIP focus spacing. The pin flag comes from the existing presentation selection in `BrowserOwner.Bind` and from detached-popup ownership in `BrowserOwner.PinPopupAsync`. Provider-document height constraints subtract the actual margin. Temporary slot and native-window focus outlines continue to draw above the browser. This is a host presentation change with no widget-specific exception or SDK/protocol addition.

Native verification in `pinned-inset-webapps-2/result.json` passes 30 checks. It measures edge-to-edge bounds for six full-webapp pins and six detached popup pins, checks the interaction outline and restoration to embedded spacing, and exercises capacity replacement, exact-source notification activation and teardown. `pinned-inset-transfers/result.json` passes 186 broader browser, popup, dialog, transfer and shutdown checks. `pinned-inset-video/result.json` verifies both generated Discord video pins fill their slots, continue producing frames and retire independently when a stream ends. The first geometry-test attempt queried its slot before asynchronous browser attachment completed; the final check waits for attachment and measured bounds.

| Browser-slot consumer | Applicability |
| --- | --- |
| Browser, hosted webapps, legacy DiscordWidget, Twitch and MapGenie | Affected through the existing full-widget pin path. Native webapp and transfer fixtures exercise that path. |
| DiscordWebViewWidget streams and SDK Gallery hosted views | Affected through authored pinned layouts. Native direct-video checks measure both Discord slots. |
| Detached browser pop-outs | Affected through the existing popup pin lease. Six native popup windows verify the same zero-inset rule. |
| PoB provider documents | Shared slot mechanism applies when pinned. Height clamping uses the actual slot margin. Provider-specific content was source-checked. |
| Webapp and web-session templates | Existing browser declarations use the shared slot; no template change is needed. |
| Compact embedded media | Uses its separate media presentation owner, outside BrowserSlot. |

#### Acrylic backdrop audit and proposed ownership

Source audit, 2026-11-14. These findings establish paint ownership, not measured blur or GPU cost. The only production constructors of in-app AcrylicBrush are NativeComputedStyleAdapter.SurfaceBackground in WidgetViewPresenter.Styles.cs:659 and WidgetPreviewFrame.Apply:32. The shared WRSS acrylic value therefore means in-app sampling today. Do not globally reinterpret it as desktop acrylic.

| Consumer and concrete source | Backing content and assessment |
| --- | --- |
| Discord main root, styles/default.wrss:2; voice-controls pin :78; stream connecting/error panel :36 | Acrylic is on the root above the host's plain panel fill. No intervening artwork supplies useful in-app blur. Confirmed from the source hierarchy in DiscordWebViewWidget.Views.cs and OverlaySurfacePaint.Panel. Appropriate desktop-acrylic candidates when frosted glass is desired. The transparent participant overlay and playing browser video are separate and must stay unaffected. |
| Playnite foreground panels, styles/default.wrss:289; PlayniteLibraryPresentation.cs:545 and :576 | BackgroundSurface supplies focused game artwork behind these panels. Keep in-app acrylic. Missing artwork can leave a flat background, but does not justify changing the semantic material. |
| Playnite modal, styles/default.wrss:200; PlayniteLibraryWidget.cs:250-259 | Modal overlays the widget page. Keep in-app sampling of that page. |
| Task Switcher and notification inbox, ShellUtilityPanel.cs:15 and OverlayShellPage.Utilities.cs:136, :185 | Shared preview acrylic in ShellOverlayLayer, placed above its tray icon. The underlying region can contain widget content or only transparent shell area over the separate desktop scrim. Placement-dependent candidate for a region-scoped desktop material, not evidence that all utility panels always have a flat background. |
| Introduction card, IntroductionCard.cs:15 and OverlayShellPage.Introduction.cs:169 | Same preview frame, dynamically positioned around highlighted targets. Can overlap real app content or empty shell space. Review material by intended appearance; do not blanket-replace the shared frame. |
| Focus previews, builtin-default.wrss:1179 and WidgetViewPresenter.Previews.cs:273 | Portaled above widget content within the same presentation layers. In-app blur remains meaningful when they cover the widget. Off-widget placement may cover only empty shell area. |
| Banner notifications, NotificationBannerChrome.cs:33 | Not acrylic. Uses a theme-derived translucent gradient in a passive peer window. Desktop acrylic would be a separate host feature, preserving category opacity, readable foreground, transparent shadow gutter and nonactivation. |
| Other shipping widgets, FirstPartyWidgets, templates and SDK examples | Repository search found no further authored acrylic declarations. They can inherit the common focus-preview material, so changing that shared meaning would affect them too. |

The main overlay scrim is a separate native window (OverlayDesktopBackdrop.Apply:26), painted with a translucent solid color. In-app brushes cannot blur that other window or the game through it. Desktop acrylic would sample the composite behind its host, including the dimming scrim where present, rather than automatically bypassing it. Opaque host/widget root fills would also need adjustment or they would hide the native backdrop.

Recommendation: one desktop-acrylic option for frosted top-level surfaces, retain in-app acrylic for content panels and keep solid/fully transparent alternatives. Host owns native material creation, lifetime, clipping and fallback. Widget supplies material intent. Extend the existing appearance contract explicitly, preserving legacy WRSS meaning and user accessibility overrides. For mixed shell regions, use an appropriately bounded native system-backdrop surface supported by the project's Windows App SDK rather than blurring the entire transparent overlay window. Existing TransparentTintBackdrop ownership in MainWindow and PinnedWidgetWindow is the lifecycle precedent; this is a genuine host rendering gap, not a missing widget call.

Desktop acrylic remains scoped only. Estimated moderate work, approximately one to two days for implementation and native verification, with extra time if passive-island composition requires changes. Verify game/desktop pixels, inactive pins, six-pin load, moves/resizes, corners/gutters, independent text opacity, reduced transparency/high contrast, and opaque browser/media coexistence. Do not infer performance or inactive rendering from the source audit.

#### Passive and interactive pins — scope only

The current contract has no pin input policy (PinnedPresentationLayout in src/WidgetProtocol/ViewModels.cs:200). The host unconditionally adds Interact with pinned widget in OverlayShellPage.Pinning.cs:AddPinnedCommands, and View opens the same unfiltered chooser in OverlayShellPage.Input.cs:214. EnterPinnedAsync makes the selected native window interactive without checking whether the projection is intended to accept input. This is a source-confirmed framework gap: a display-only pin can consume focus and View despite having no content interaction.

Proposed shared contract: an explicit Interactive/Passive policy on the published pinned layout, surfaced by PinnedLayoutHandle and WidgetView.PinnedLayout, defaulting to Interactive for existing clients. Keep pin selection identity, placement identity and material independent of that policy. Require protocol support when Passive is requested, so an old host cannot silently expose an interaction mode the widget forbids. Full-widget pins retain their existing interactive behavior; review a view-level declaration only if an actual display-only full-widget consumer needs it. Host-owned media and popup pins retain their explicit host interaction policy.

Expected flow:
- Move/resize, opacity, replace and unpin choosers include every eligible pin. Highlighting a passive pin still outlines its real window. Adjustment is host-owned and does not turn on content input.
- Interact and View use only Interactive pins. With none, omit the menu command and controller hint, and do not claim View for pin entry; ordinary overlay handling remains available. With one, enter directly. With multiple, show the chooser containing only those pins.
- Entry must recheck the exact selection and current policy after choosing, after asynchronous focus/lifecycle work, and at input dispatch. A stale chooser or continuation cannot activate a replaced or now-passive pin.
- If a live projection changes to Passive, leave content-interaction mode, cancel held input/engagement through existing host teardown, restore the proper overlay focus and update prompts. Do not close/recreate the pin or alter saved position/opacity. Remaining pin demands continue.
- Passive native windows remain nonactivating and click-through for pointer, keyboard and controller content input. Never give hidden browser controls, shortcuts, scroll areas or accessibility actions an alternate route into interaction. Host move/resize still works. Preserve current behavior when the game has focus and no overlay input session exists.

| Consumer | Proposed disposition |
| --- | --- |
| Discord voice participants | Declare Passive. Pure names, avatars, speaking/LIVE/watching indicators. No View interaction entry. |
| Discord voice controls | Interactive; mute, deafen and leave remain operable. |
| Discord stream pins | Inspect actual receiver controls before migration. A BrowserSlot does not inherently imply useful interaction. Declare Passive only if the receiver is strictly display-only and all supported controls remain in the main widget. |
| Spotify and standalone YouTube Music compact pins | Preserve Interactive playback controls. |
| Performance Monitor compact pin | Keep Interactive pending product decision: despite displaying only text, it has a Y/Refresh shortcut (PerformanceMonitorWidget.cs:211). Do not classify by visual buttons alone. |
| SDK Gallery summary/level pins | Retain current actionable demos; add an explicit passive example to demonstrate both policies without stripping existing actions. |
| Full webapps, Browser, legacy Discord, Twitch, MapGenie, SDK hosted views and detached popups | Preserve Interactive; web content is opaque to declarative-tree inspection. |
| Compact native media | Preserve host-owned playback interaction. |
| Notification banners | Already passive peer windows outside the six-pin registry and interaction chooser. Preserve nonactivation and source activation through notification UI. |
| Templates, SDK docs and relevant fixtures | Explain explicit policy with compatibility defaults; no automatic UI-tree heuristic. Cover metadata-only and projection layouts through their real production paths. |

Owner is SDK/protocol for declaration and session admission, host for eligibility/focus/input/lifecycle, widget for intent. Reuse existing selection guards, chooser highlighting and ExitPinnedInteraction. Moderate cross-cutting work, estimated one to two days including native tests. Required evidence includes six mixed pins, all-passive and one-interactive cases, View and tray entry, pointer click-through, policy change/close/reopen during chooser and focus acquisition, held-button release, host adjustment of a passive pin, notification arrival and teardown. No implementation of this policy is included in the current changes.

#### Connection-info button and no-device verification

The main Discord heading now uses the existing SDK IconButton immediately left of Pin voice controls, with a packaged theme-tinted info glyph. It retains discord.main.connected and the existing account/session-bound connection action and modal navigation. The full-width connection label is removed; the accessible name and tooltip retain the observed RTC state and identify Connection details. Dedicated color-only classes avoid applying the old label typography to the icon. This is a widget presentation change using an existing framework API, with no new host input plumbing.

Voice settings remains in the main page's context menu, including when not in a call. Controller Menu or right-click on a main-page control opens it. Live verification on 2026-11-14 opened that dialog and confirmed real Microphone and Output device selectors, both showing Discord's current default selections. Neither device was changed. The synthetic fixture's devices are not evidence of live support; the real dialog screenshot is connection-info-package/live-voice-settings.png.

Missing-device handling already uses provider capability checks and bounded inventories. Empty, null, missing and disabled-only inventories return unavailable selectors with null selected IDs; they do not call setters, force a fabricated default ID or request hardware. Device actions re-read the inventory before writes. Preserve Discord's current/default handling, with Refresh devices and Open Discord available. Added regression checks cover empty inventory presentation/closing, unplug after a dialog is populated, rejection of a stale selection after refresh, independent input/output availability, and a default-only list without a redundant setter. Physical hardware unplug/replug remains unverified.

Verification: 461 widget checks and 137 provider tests pass. connection-info-native-5/minimum/result.json passes 43 native checks and expanded/result.json passes 42, including one Left from Pin voice controls to info, opening/closing details, participant audio, device dropdowns, sliders and favorite continuity. Earlier fixture attempts exposed a stale Up direction for participants and a race where the device nodes were published before their native controls had nonzero bounds. The final test follows Down to participants and waits for measured device controls before navigation; no production focus behavior was changed to satisfy the fixture.

The verified borderless production host (302 local managed references and emitted async cleanup checked by ReleaseVerifier) is now staged and running from six-pins-candidate/frontend-borderless. Discord 0.1.0.11 is installed in the existing user catalog, preserving settings and permission choices. The old candidate closed normally before the CLI update and restart. The subsequent no-device work adds tests only, so the installed package already contains the verified behavior. Desktop acrylic and explicit passive pin policy remain scoped, not implemented.

#### Active implementation — passive pins, desktop acrylic, stream reopening

The user subsequently authorized implementation and cross-widget/banner verification of the two scoped features. This section supersedes their scope-only status above. Implementation and integration are complete. The running candidate uses the final trimmed host and Discord 0.1.0.12, with the existing profile, installed catalog and permissions preserved.

- Protocol 108 adds an explicit PinnedInteractionMode.Interactive/Passive layout declaration and WidgetSurfaceAppearance.DesktopAcrylic. Existing SDK signatures are retained, with new overloads for explicit interaction intent. Default behavior remains Interactive. Passive or desktop-acrylic declarations require the new protocol; generated native constants and public API baseline are updated.
- Passive input is rejected in SDK controller/action/indexed admission and in the presentation session, while read-only browser/media/artwork/range realization remains available. Host View and interaction choosers filter by current policy and revalidate after waits. Passive pins remain in placement/opacity/unpin choosers. A policy change exits interaction without destroying selection.
- Discord participants and stream receivers declare Passive. The actual backend-video document in WebSessionPageAdapterHost contains only a video element with no controls; stream volume/watch actions remain in the widget. Voice controls, Spotify, standalone YouTube Music, Performance Monitor (Y/Refresh), full webapps/popups and native media retain interaction. SDK Gallery adds a passive readout example without removing its existing actionable examples.
- DesktopAcrylicSurface owns a bounded SystemBackdropElement and its controller lifetime. It is used under the main widget surface, pinned surface and banner card, preserving transparent corners and banner shadow gutters. Passive windows keep the material input-active without receiving foreground/input authority. Native accessibility/transparency policy remains intact. Discord root/voice/loading panels now use host desktop acrylic with transparent authored root fill. Transparent participants and playing video remain separate. Existing WRSS acrylic and Playnite's artwork-blurring panels remain in-app acrylic.
- Sender stop/restart investigation found a host request-publication race: RequestWidgetPinAsync could discard an open request before the withdrawn layout had been republished. The fix refreshes one authoritative frame before resolving the open request, retaining widget/visibility/session authority and disposing only retired instances of that same layout. Stop watching now withdraws the matching stream layout, just as sender stop does. Other stream selections and receivers stay alive.
- WidgetPinnedLayoutTestHost also retained its local selection when a withdrawal had already revoked independent worker demand. It now clears only the same captured retired selection even when that old revoke notification is correctly rejected. The actual host already removes its retired window independently of the worker acknowledgment.

Final automated evidence: SDK checks pass (177 top-level scenarios), presentation-session tests 367/367, WinUI shell policy tests 363/363, and Discord 464 checks. Consumer suites pass for SDK Gallery (13), Spotify (88), standalone YouTube Music (44), Performance Monitor (13), Platform Settings (33), and Playnite (220). The coherent-runtime bridge verification passes 236/236; an earlier direct run lacked the matching runtime and is superseded by the repository verification script. Protocol parity verifies 194 managed constants, one shortcut rule and four availability rules. ReleaseVerifier checks 302 local managed references and emitted asynchronous cleanup in the final trimmed production build.

Native evidence under artifacts/input-discord:
- desktop-acrylic-native-6/result.json: 15 checks, including six inactive acrylic pins, a banner, forced collection, repeated detach/reconnect, corners, material reuse, accessibility fallback, startup background opacity and shadow-gutter bounds. desktop-acrylic-native-5/desktop.png shows actual blurred blue/gold stripes through the surfaces. Isolated window capture returns transparent/black backdrop pixels and is not evidence of desktop blur.
- passive-mixed-native-2/result.json: 11 checks covering three interactive and three passive pins, View filtering, all-passive behavior, native input gating, move/cancel and appearance refresh across all pins; normal exit recorded as zero.
- passive-voice-native-2/result.json: 64 checks covering real native voice controls, passive participant presentation, View selection, all-passive menu behavior and retained placement/opacity; normal exit recorded as zero.
- passive-capacity-banner-native/result.json: 29 checks covering six-pin capacity, chooser outlines, exact-selection close, collision preferences, notification placement outside the pin registry and multi-display move/save/cancel; normal exit recorded as zero.
- passive-webapps-final/result.json and passive-transfers-final/result.json: 30 and 186 checks covering full-webapp/pop-out interaction, edge-to-edge layout, browser transfers, dialogs and teardown.
- passive-stream-native-4/result.json: two independently moving native video pins; sender stop closes only its pin, sender restart creates a fresh pin, and Stop watching closes only its corresponding pin while the other continues. Over the measured three-second interval, frame counters advanced 26 to 116 and 8 to 99 with zero recorded drops. Captured foreground video remains undimmed (dominant RGB within two channels of the generated source). This is bounded fixture evidence, not a sustained performance claim.
- passive-release-smoke/result.json: five checks on the trimmed production host, including coexisting voice/participant pins, rejected UI Automation invocation on an inactive pin, exact unpin and normal shutdown without a new crash dump.
- passive-banner-themes/result.json: four checks covering dark/light/opacity specimens, transparent shadow gutter, high-contrast behavior and compositor-resource reuse. The fixture closed normally with no matching crash dump.

The initial acrylic implementation visibly blurred but failed during native target finalization. Own configuration alone did not fix it; the built-in backdrop avoided that failure but became opaque for inactive pins. Those experiments were rejected. DesktopAcrylicSurface.cs now explicitly closes its dedicated SystemBackdropElement target on the UI thread after detaching and disposing the controller. Dump evidence identified ContentExternalBackdropLink release on the finalizer thread with RPC_E_WRONG_THREAD. Forced collection, repeated reconnection, independent unpin and normal application shutdown pass with the final lifetime ownership. This helper is restricted to bounded element targets, not Window.SystemBackdrop.

Passive content is also gated at the native HWND using EnableWindow, in addition to declaration-aware SDK/session admission and host input guards. This avoids dimming foreground content with XAML disabled styling. Browser video capture and interactive browser/pop-up tests cover both sides of the gate. OverlayShellPage.Appearance.cs now refreshes every pin's appearance instead of only the selected pin; native reduced-transparency checks cover the previously missed inactive peers. The full-screen OverlayDesktopBackdrop dimming scrim is unchanged.

| Consumer group | Final applicability |
| --- | --- |
| Discord participants and video-only stream receivers | Affected and migrated to explicit Passive; native participant, two-stream teardown/restart and foreground-video checks pass. |
| Discord main, voice controls and stream loading/error surfaces | Affected and migrated to DesktopAcrylic; voice controls remain Interactive. |
| SDK Gallery | Affected and migrated with a new passive desktop-acrylic readout; existing actionable layouts preserved. |
| Spotify, standalone YouTube Music, Performance Monitor | Already compliant with the Interactive default; playback and refresh actions preserved, consumer suites pass. |
| Full-webapp widgets, browser-backed SDK views and pop-outs | Already compliant with Interactive; native webapp/transfer fixtures exercise shared production entry points. |
| Playnite | Existing artwork-backed in-app acrylic retained; 220 consumer checks pass. |
| First-party utility widgets and Platform Settings | No passive layout migration needed; common host appearance/input rules apply, Settings suite passes. |
| Banner notifications | Affected host rendering migrated to bounded desktop acrylic; remain passive peers outside six-pin capacity. Native theme, opacity, placement and teardown checks pass. |
| Templates and SDK documentation | Compatibility default remains Interactive; explicit policy/material contract documented. No tree-based automatic classification added. |
| Utility/inbox/introduction/focus previews | In-app acrylic retained because these can cover app content; no global reinterpretation of WRSS acrylic. |
| Compact native media | Host-owned Interactive semantics retained. The previously recorded initial passive-paint issue remains deferred, outside this change. |

Final integration uses passive-release-payload and passive-acrylic-final-candidate. Discord-0.1.0.12.wrwidget SHA256 is 2351bea2beafe0a82f5f1529cecc80de9f52eee96fe3e1c711871fef078e39f7. The prior user candidate shut down normally before update; frontend PID 31332 and Discord worker PID 26192 were observed running afterward, and the real-account widget published its voice/stream controls. No messages were sent and no audio device preferences changed.

Limitations: sender restart and Stop watching were verified through the shipping widget/worker/host using a synthetic provider, not a new live broadcaster restart. Sustained GPU performance, physical monitor disconnection and physical audio-device unplug/replug remain unverified. These results do not close unrelated deferred work in the earlier plan.

#### Next planned changes — viewer status, widget pin management and video letterboxing

Requested after acceptance of the 0.1.0.12 candidate. Planning only; no production implementation or restart in this pass. The following findings are source-confirmed; live provider behavior and new UI flows still require verification.

**Viewer eye means “this participant is watching your stream.”** ParticipantOverlay in DiscordWebViewWidget.Views.cs:269 currently derives the eye from the participant's incoming stream and this account's watch state. That is the inverse of the requested meaning. Both participant and voice-controls pins use this shared renderer, so fix both together. Provider adapter.js:76 deliberately excludes the current account from incoming streams; do not undo that exclusion or reuse incoming DiscordStream.Watching for outgoing viewers.

- Owner: Discord provider and widget domain/presentation, not the SDK or host. Read the current account's active outgoing stream through currentShare and its viewer IDs through ApplicationStreamingStore.getViewerIds, whose bounded ID validation already has a precedent in watchOwnership at adapter.js:636. Confirm the outgoing-stream key and subscription updates in the live provider before treating this as available.
- Publish an explicit bounded outgoing-viewer fact with availability and account/channel/stream identity. A participant gets the eye only when their ID belongs to that current outgoing stream's viewer set. Accessible text: “Watching your stream.” Keep LIVE as the independent indication that the participant is broadcasting. Watching somebody else's stream must never create this eye.
- Clear the fact when sharing ends, the account/channel changes, the source is unavailable or the stream lifetime changes. Validate stale results against the current identity. Unknown availability hides the eye rather than inferring viewers from local receiver state. Keep incoming watch ownership for receiver lifecycle and controls.
- Verify zero/one/multiple viewers, watcher join/leave, viewers who also broadcast, our watching another stream, sender stop/restart, missing/malformed provider data and account/channel switch. Check both pin layouts, accessible labels and status spacing. Complexity: small widget/domain change plus provider verification.

**Manage pin from the widget.** Pinning.cs currently exposes only pin.open and pin.close. The host already owns move/resize, opacity, interaction and exact unpin in OverlayShellPage.Pinning.cs:136-143. This is a genuine missing SDK entry point; Discord should request the existing host controls instead of recreating them.

- Proposed shared operation: request management of an exact selection (layout ID plus selection ID), using the same admitted-user-action, owner/session and stale-selection safeguards as RequestCloseAsync. The final API naming and reusable selection type should be settled during implementation; preserve the existing close-request API for compatibility. Carry the new operation through protocol/version gating, broker, runtime, bridge and presentation-session effects.
- Widget UI: show a compact Manage pin action beside the relevant pinned voice controls, participants or stream entry. Keep direct unpin available where already useful. No management action for an unpinned layout. Stream management must capture that stream's current selection, not a mutable slot index.
- Flow: activate Manage pin; host outlines that exact window and opens its management menu; choose Move/resize, Opacity, Interact (only when Interactive), or Unpin. A known target needs no six-pin chooser. Reuse current adjustment controls, confirm/cancel and saved-placement behavior. Restore widget focus on cancel/completion when its page still exists; successful Interact intentionally transfers to the pin. Remove the highlight on every exit.
- Recheck authority after every awaited menu/transition. Closing, replacing, restarting or withdrawing the selected stream while the menu is open must invalidate the action, never retarget the newly opened selection. Preserve the other five pins and banner placement/nonactivation. Managing a passive pin must not enable its content input.
- Native verification: mouse/controller entry, selected-window outline, move/resize and opacity accept/cancel, exact unpin, passive menu filtering, stale reopen, page switch/worker stop during a menu, banner arrival, and six mixed pins. Complexity: moderate cross-layer addition using the existing close effect and host chooser/adjustment lifecycle as precedents.

**Black unused area is host video letterboxing.** WebSessionPageAdapterHost.cs:319 explicitly sets the receiver document background to black and the video to objectFit: contain. When the pin and source aspect ratios differ, the whole video is fitted without stretching/cropping, and the leftover area shows that black document background. BrowserSurface.Chrome.cs:67 already makes the underlying WebView background transparent; changing only that property would not remove this fill. This is separate from acrylic and from black pixels encoded by the sender.

- Preserve contain as the default so shared text and screen edges remain visible. No resizing, re-encoding or artificial blur is needed to change the unused-area appearance.
- Recommended first improvement to evaluate: a host-owned “Fit window to video” command under Manage pin, using actual receiver dimensions and current monitor bounds/minimum sizes. It changes only that pin after a user action and commits placement using the normal adjustment rules. Do not continuously resize or overwrite saved geometry when the source changes resolution. If constraints prevent an exact fit, retain aspect-correct content and padding.
- Optional appearance direction, not yet selected: transparent unused areas or desktop acrylic behind the video. This requires clearing the receiver document fill and selecting a compatible host surface material; painting only CSS transparency can reveal another opaque host fill. Scope this to the host-created video receiver, not arbitrary web pages. Verify video remains fully opaque and unchanged, corners and pointer behavior remain correct, and changing the material does not re-create the track/receiver. Keep source-encoded black pixels intact.
- Owner: host backend-video presentation and geometry; expose reusable video presentation intent only if a consumer needs to choose it. Do not make Discord rewrite browser DOM or calculate native window bounds. The current snapshot's PreviewAspectRatio belongs to previews and is not a pin sizing contract; do not repurpose it.
- Verification: wide/tall source and window combinations, source dimensions initially unknown, resolution changes, minimum sizes, DPI and monitor limits, saved placement, passive input, independent streams, frame delivery/color comparison and normal teardown. Complexity: small for fill styling alone, moderate for a reusable fit command; native composition verification is required before choosing transparent/acrylic padding.

| Consumer | Planned applicability |
| --- | --- |
| Discord participant/voice-controls pins | Viewer meaning correction in the shared renderer; both need tests. |
| Discord voice, participant and stream entry points | Adopt exact-selection Manage pin; preserve direct unpin and separate Stop watching. |
| SDK Gallery | Demonstrate management of interactive and passive selections and stale-action rejection. |
| Spotify, YouTube Music and Performance Monitor | Existing behavior remains compatible; inspect production pin actions for useful adoption during the shared API implementation. No forced UI addition. |
| Browser/full-webapp/pop-out/native-media pins | Existing host management must remain unchanged; no arbitrary page background change. Fit-to-video applies only where the host has authoritative video dimensions. |
| First-party widgets, templates and SDK examples | Search direct calls and equivalent manual management patterns when implementing; document the new contract and migration status. |
| Banner notifications | Not managed as pins; verify notification arrival during menu/adjustment and unchanged capacity/placement. |

**Cross-path correction after reviewing YouTube iframe pinning.** Fit must be a shared host command with path-specific, authoritative sizing information, not a Discord-only feature. The earlier “actual video dimensions” description applies to the direct receiver; it is not true for every pinned video. Distinguish fitting a player viewport from fitting decoded video pixels.

| Video presentation path | Current sizing evidence and proposed fit behavior |
| --- | --- |
| Discord backend-video BrowserSlot | WebSessionPageAdapterHost reports videoWidth/videoHeight from the actual receiver video. Fit can use that ratio once available, tied to the current receiver/selection. It cannot remove black pixels encoded upstream. |
| YouTubeWidget compact embedded-media pin | YouTubeVideoWidget.CreateMediaSession declares AspectRatio = 16/9. EmbeddedMediaOwner transfers the existing iframe-backed surface into host.embedded-media.compact; MediaFullscreenView already fits its viewport using that declaration. Fit the window to this player viewport. This does not establish the intrinsic ratio of the current YouTube video or remove bars painted inside the iframe, including portrait-video padding. |
| EmbeddedMediaWidget sample / other declared embedded-media adapters | Reuse EmbeddedMediaSession.AspectRatio and the same compact-media path. Check controls/insets and minimum geometry rather than assuming outer window size always equals video viewport size. |
| Full browser/webapp pins, authored BrowserSlots and detached pop-outs containing video | A page may have several videos, surrounding controls or cross-origin frames. No generic authoritative video ratio is established. Keep ordinary resize; offer fitting only if an explicit supported presentation contract supplies a target and ratio. Do not inspect arbitrary DOM, infer from page size or use a blanket 16/9 guess. |
| Native media-player view | NaturalVideoWidth/NaturalVideoHeight already exist for fullscreen/stage geometry in MediaPlayerView.Fullscreen.cs. Reuse if this view participates in a pin; that source alone is not evidence of an independent native-video pin entry point. Confirm reachability during implementation, and do not create a new pin path merely to complete the matrix. |

Implementation should resolve an optional fit capability from each existing pin owner, carrying ratio provenance (decoded source versus declared player viewport), current lifetime and any host chrome geometry. Share only the fit calculation, monitor constraints, preview/confirm/cancel and persistence. Unknown/unready dimensions do not trigger guessed resizing. Fit should preserve the existing player instance, iframe session, audio/playback position, receiver and independent pin ownership; normal geometry adjustment must not reconstruct playback.

YouTube's adapter.html also sets its document/player background black, while the cross-origin YouTube player may draw its own bars. Transparent/acrylic padding can only affect layers we own; it is not a universal way to clear every video's black fill. Any future intrinsic-ratio reporting needs a supported adapter signal and validation, not scraping the iframe.

Manage pin's proposed authored-layout request must also account for host-owned compact media: YouTube's compact selection is not a PinnedLayoutHandle, and WidgetPinRequest deliberately rejects host.* IDs. Do not let widgets fabricate those IDs or bypass that guard. During implementation, expose a bounded SDK request targeting the caller's current embedded-media presentation through existing document/presentation authority, or a host-owned caller-scoped pin chooser if a captured selection is unavailable. Exact stale-target rejection remains required. Inspect actual YouTube and EmbeddedMediaWidget entry points before finalizing the API; browser popup management remains host-owned.

Add native YouTube/embedded-media coverage alongside Discord: page-to-pin/fullscreen transfers, fit with controls visible/hidden, portrait content inside a declared widescreen player, playback continuity, cancel/save/reopen, unknown dimensions, stale document replacement, mixed iframe/direct-video pins, and banner arrival. No claim that fitting removes iframe-internal letterboxing. All of this remains planning only.

**User decision: simplify to transparent padding; defer Fit.** The user rejected the complexity of fitting and requested transparent unused space, including the themed surround observed on the YouTube compact pin. The Fit command, ratio-capability work, new geometry UI and fit-specific tests above are deferred, not implementation obligations. Preserve existing aspect fitting and saved geometry. Manage pin and the viewer-eye correction remain separately planned.

The current bounded implementation clears the host backend-video document fill, declares Discord's playing stream surface transparent and clears its authored root fill. Host-owned compact embedded-media pins (including YouTube and EmbeddedMediaWidget) declare a transparent surround through the existing appearance policy. Existing accessibility/explicit appearance overrides still apply; opacity is not used to simulate transparency. YouTube's own iframe pixels and source-encoded black bars remain untouched. Playback controls retain their existing interactive visibility behavior. No new SDK contract or iframe DOM inspection is needed for this change.

The concurrent stop/rewatch investigation reproduced a provider-lifecycle gap in a delayed-close fixture: after STREAM_CLOSE, viewer membership and the live RTC track can remain briefly available. watch previously borrowed that retiring subscription and skipped STREAM_WATCH. The provider now retains at most two pending-close records (one per slot), waits for retirement of local viewer membership and the captured connection before reusing the slot, and rejects a timed-out/cancelled wait without issuing a new close. New watches still preserve subscriptions opened outside the adapter and independent streams. Direct receivers wait for a decoded picture before successful attachment; a track without a replacement gets a bounded grace period before retirement. This is Discord provider ownership, not a missing generic SDK lifecycle abstraction. Live reproduction of the original failure remains unverified; the fixture establishes the race, not that it is the sole cause of the reported live incident.

Verification for this follow-up:
- Provider adapter 28: 143 tests pass both unminified and minified, including delayed close acknowledgment, unresolved close timeout, account switch during retirement, no decoded frame, temporary lack of a replacement, preexisting subscriptions and two independent streams. The sealed backend is 57,725 bytes, below the existing 65,536-byte limit.
- Discord widget: 464 checks pass. YouTube widget: 64/64 tests pass.
- transparent-embedded-native/result.json: 41 native checks pass with captured video pixels and no pixel failures. Covers the shared production compact-media path, transparent surface brush, playback commands, fullscreen transfers, hidden/revealed interactive controls, page switching, move cancellation, opacity and unpin without browser recreation. Fixture normal shutdown has no matching crash dump.
- transparent-direct-native/result.json: transparent receiver document/body and host pin background, two native streams progressing at approximately 30 fps with zero measured drops, sender end/restart and exact Stop watching. Its desktop/video captures retain opaque video pixels.
- rewatch-direct-native/result.json: the shipping adapter/package runs against a synthetic Discord store whose owned subscription closes after a 500 ms delay. Stop watching followed by Watch again starts a fresh subscription and delivers decoded frames, preserving the second stream. Sender restart and normal cleanup also pass. This improves the earlier fixture, which always reported a preexisting user subscription and therefore never exercised owned STREAM_CLOSE.

| Shared transparency consumer | Final disposition |
| --- | --- |
| Discord backend-video receiver | Migrated at all three paint layers: host receiver document, widget root and pinned surface declaration. Native direct-video checks pass. |
| YouTubeWidget and EmbeddedMediaWidget compact players | Migrated through the existing host-owned compact-media surface declaration. Shared native path and YouTube consumer tests pass; a new live YouTube playback test is not claimed. |
| Full webapps, authored ordinary BrowserSlots and detached pop-outs | Not applicable: their page backgrounds remain authored by those pages. |
| Native media fullscreen/player view | Not applicable: no new pin entry point or fullscreen background change. |
| Other samples, FirstPartyWidgets and templates | Repository search found no other production BackendVideo callers or EmbeddedMediaSession declarations requiring migration. |
| Banner notifications / voice pins | Not applicable: retain their existing acrylic/transparent appearance, placement and interaction policy. No shared appearance-policy change. |

Native tests use isolated settings/catalogs and no live messages or calls. The existing deferred cold compact-player first-paint case is not resolved by these warmed transfer checks. General GPU performance and a fresh real Discord stop/rewatch remain unverified.

Integrated the verified trimmed host from artifacts/input-discord/transparent-video-release and Discord 0.1.0.13 into the existing candidate/profile. ReleaseVerifier passes 302 dependency checks and emitted asynchronous cleanup; the build reports no warnings/errors. Package SHA256: 7b96295349b4b68b86c4e42a48185384b5fa761cf0e0f57525c4c35ad49afcfb. The previous candidate closed normally; frontend PID 23628 and the installed 0.1.0.13 worker PID 40128 were observed after restart, with real-account widget controls in startup-ui.json. Settings/catalog roots and permission choices were preserved. Integration evidence is in rewatch-transparency-package; no merge or commit was performed.

**Correction to the historical adapter 28 experiment above:** both viewer-based retirement and waiting for a decoded frame before attachment were rejected by subsequent evidence. The local-watch correction below supersedes that experiment; do not use its passing fixture as evidence of live correctness.

#### Active follow-up — remaining fill, pin management, viewer meaning and notifications

The user authorized implementing the remaining planned work, then added Discord's desktop-notification switch and the banner's extra outer contour. Fit remains deferred. This section supersedes earlier implementation claims where new evidence contradicted them.

- The initial transparency tests checked the receiver document and top-level pin brush but missed BrowserSurface.ApplyTheme assigning the theme fill to its native viewport (BrowserSurface.Chrome.cs). IsBackendVideo is an explicit host source-kind declaration; that viewport now keeps a null fill across theme/reparent callbacks. Ordinary browser viewports preserve their backdrop. Native composed-pixel verification is still in progress; an isolated window capture's dark background is not by itself proof of an opaque application layer.
- The first-frame-before-reply guard in adapter 28 can create a presentation/decoder dependency cycle for a real remote track. The guard was rejected and moved after attachment: the widget can publish the receiver, then a bounded watchdog retires a receiver that never supplies a picture. Adapter 29 / Discord 0.1.0.14 was packaged, tested and applied as a targeted correction while other work continues. It also distinguishes pending close, missing incoming track and unsupported Discord controls in user-facing retry messages. The user candidate is frontend PID 42460 with worker PID 42172 on 0.1.0.14; no fresh real-account rewatch success has been established.
- pin.manage is an additive operation of the existing intrinsic pinning capability. WidgetPinManageRequest accepts either an exact authored layout/selection pair or neither. The latter asks the host to choose only among the caller's current pins, including compact media and full-widget/popup pins; it does not permit fabricated host.* or foreign widget/window IDs. Existing frame schemas and protocol 108 remain unchanged. Unsupported older brokers reject the new operation; no fallback opens or closes a pin. SDK signatures for existing operations remain intact.
- The host reuses its native choice dialog, selection outline, placement/opacity adjustment and interaction/unpin lifetimes. An exact authored request skips the chooser. Caller-scoped management selects its only pin directly, offers a chooser for several, or reports “This widget has no open pins.” It never chooses a Discord pin on behalf of YouTube. Menu actions revalidate captured object/selection authority; removing the target dismisses the menu. Passive layouts have no Interact choice.
- Discord, SDK Gallery and the embedded-media sample adopt management. YouTube has a Manage pins icon in its player header, using caller-scoped management because compact-media selection is host-owned. Other actionable widget layouts keep their existing controls; they can adopt the public operation without reconstructing window ownership.
- The eye now uses an account/channel/current-outgoing-stream-bound viewer fact, restricted to bounded current participants. Incoming subscription ownership remains separate. Both voice layouts share the corrected renderer and “Watching your stream” accessible label. Null/unavailable viewer information does not produce an eye.
- Notification setup now distinguishes browser permission from Discord's NotificationSettingsStore desktop policy. On an explicit Enable notifications action after grant, a feature-probed setDesktopType/setTTSType module can change NEVER to ALL. Existing ALL and ONLY_MENTIONS are preserved, as are TTS, channel/server preferences and sounds. The result is read back; missing/unknown APIs or an unconfirmed write leave setup available and show the concrete User Settings → Notifications → Enable Desktop Notifications instruction. Rendering only reads state; it never writes settings. The provider mapping is supported by the open-source [NotificationSettingsStore type](https://github.com/Equicord/Equicord/blob/main/packages/discord-types/src/stores/NotificationSettingsStore.d.ts) and [module discovery registry](https://github.com/mwittrien/BetterDiscordAddons/blob/master/Library/_res/0BDFDB.data.json), not a claim that every live Discord build supports it.
- The banner outer rectangle was reproduced in a native synthetic desktop-acrylic fixture. Removing extended edge styles or disabling nonclient rendering alone did not remove it. Setting the native corner preference to DONOTROUND removed the outer contour while preserving the inner XAML card corners/shadow. PinnedWidgetWindow now requests that policy through OverlayWindowFrame.SuppressBorder; the transparent native bounds should not have an independently rounded contour. This applies to all peer pins/banners, not ordinary browser chrome. The [Windows corner preference contract](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_window_corner_preference) is Windows 11-only; existing OS gating remains.

Checks so far: SDK 177 scenarios, broker 93, session 367, Gallery 13, YouTube 64, and coherent-runtime bridge 236 pass. Provider 148 tests pass unminified/minified; adapter 30's sealed backend is 59,168 bytes. Discord widget tests passed 466 before the additional permission-only fallback cases. Native management verification remains in progress. Earlier voice fixture attempts lacked native activation or neutral controller frames required by the dialog's release barrier; the harness is being extended to send the actual menu/placement frame sequence, without weakening production admission guards.

#### Follow-up — stable native pin layers while the overlay is open

- **Reproduced host defect, not widget work:** `MainWindow.Appearance.cs` refreshed the desktop scrim and every pin after accepted view publications. The scrim and every passive pin separately required immediate adjacency beneath main, rotating peers and temporarily moving the scrim over existing pins. A native six-window fixture reproduced **21 z-order writes for three unchanged refreshes** using that old algorithm. This applies to nonvideo pins as well as streams; it does not establish a video decode defect.
- **Implemented:** `OverlayWindowFrame.IsAbove/EnsureAbove/EnsureBelow` check current relative native order with a bounded walk and only move an incorrectly layered target. `MainWindow` supplies its actual visible backdrop handle to `OverlayShellPage.ReconcilePeerWindowOrder`; passive pins stay between main and scrim. Interactive/adjusted pins and banners stay above main by moving the target peer, never lowering main. A repaired banner stays below an already-raised sibling. Hidden-main behavior retains the existing idempotent topmost policy. Exact-adjacency helpers remain available for deliberately corrupting fixture order. No new SDK/protocol contract, widget counters or author-side ordering plumbing is needed.
- **Avoid repeated frame work:** `OverlayDesktopBackdrop.Apply` only changes a different color/bounds/visibility and suppresses the frame after a move or show. It no longer reasserts an unchanged native frame on every publication. Validation-only counters measure app-issued z-order/frame writes; they do not claim to count unrelated OS/compositor activity.

| Consumer path | Applicability and evidence |
| --- | --- |
| Authored/full-widget pins: Discord voice/participants/streams, SDK Gallery, Spotify/YtMusic, Media Sessions and Performance Monitor | Affected, fixed centrally through `OverlayShellPage.Pinning` / `PinnedWidgetWindow`; six native windows from three independent workers pass 33, including **zero z-order and frame writes across 30 real unchanged appearance refreshes**, exact unpin, chooser outline, placement, capacity and multiple displays. No widget migration. |
| Browser/full-page pins: Browser, Game Help, MapGenie, Twitch and webapp providers | Same shared native peer; three pin/hide/reopen/interact/unpin cycles pass 9 without navigation or session replacement. |
| Compact media: YouTube and embedded-media sample; browser pop-out pins | `OverlayShellPage.MediaPinning` and `PopupPinning` instantiate the same peer, so the host fix applies without provider changes. The broader native browser/popup transfer fixture passes 186, including move/resize/opacity ordering, input/dialog ownership, popup transfer and shutdown. |
| Widget/browser/app-update banner notifications | Same peer with explicit notification layer. Dedicated native notification fixture passes 31, including three forced bad-order repair cycles, raised siblings, adjustment, reopening, click-through, foreground and controller focus. |
| Templates, SDK examples and widgets without native pins | No direct native ordering implementation or migration required. Legacy Discord fixtures are not evidence for live Discord streaming. |

Validation artifacts: `artifacts/input-discord/pin-layer-validation` (successful x64 Release validation publish with analyzer and binlog), `pin-layer-six-native`, `pin-layer-browser-native`, `pin-layer-banner-native-2`, `pin-layer-transfers-native` (186 checks, successful shutdown). Two discarded harness routes remain recorded: the first banner attempt disabled the automatic-update prerequisite (0 checks); a combined notifications/general-browser route passed 73 before attempting toolbar focus on the original page after notification intent had replaced it. Dedicated existing routes passed without weakening production guards. Live user frontend 31964 / worker 32176 were not restarted or updated. This establishes native reorder churn and the corrected invariants; continuous real-account visual flicker still needs observation after deployment.

### Three incoming Discord streams (2026-11-14)

Discord now authors three passive stream layouts. Existing placement keys `discord.stream` and `discord.stream.2` stay unchanged; `discord.stream.3` adds Screen 3. Slot identities remain stable when another share closes. A fourth watch reports that all three slots are occupied and preserves the three existing receivers. Main-page stop, unpin, manage and audio IDs derive their suffix from the slot, including `.3`; unused slots do not create Shared screen cards. Provider slot admission is updated alongside this C# change.

Capacity review (source evidence, not native performance proof): `PlatformBroker/WebSessionRegistry.cs` permits four sessions per widget and eight globally, so the Discord backend plus three receivers fits exactly. `BrowserOwner.WebSessions.cs` awaits retirement of hosted entries absent from the authoritative live-session batch before dispatching new opens; `BrowserSurface.BackendVideo.cs` removes the child from the adapter on disposal. The adapter's existing four-child bound therefore accommodates the three active receivers and does not need increasing. Native residency remains eight including retiring entries. The SDK's existing per-slot Latest lanes serialize replacement cleanup. No new host capacity or lifecycle abstraction was added.

| Consumer / behavior | Disposition |
| --- | --- |
| Shipping Discord C# widget | Migrated: three independent receiver slots, stable placement, distinct actions/audio IDs, slot-local stop/end/retry and account teardown. |
| Discord provider adapter and native direct-video validation | Coordinated implementation and evidence recorded in the provider follow-up; C# tests alone do not establish three moving native tracks. |
| Shared SDK / broker / host session admission | Existing API and bounded capacities retained after the ownership and retirement-order review above. |
| YouTube, embedded media, browser/webapp pins, other samples and FirstPartyWidgets | No production BackendVideo callers outside Discord found; existing pin limits and media paths unchanged by this widget policy. |
| Banners, voice controls and passive participants | No slot allocation or appearance change in this expansion; each still consumes its existing host surface when pinned. |
| Connection-details diagnostics | Existing first-selected-stream policy retained; it selects slot 3 when earlier slots are absent. A multi-stream measurement selector is deferred rather than silently changing this UI. |

Managed verification: clean Release build and 486 Discord widget checks pass (`artifacts/input-discord/three-stream-tests.log`, matching uniquely named binlog). Coverage includes three independent sessions, provider slot numbers 1/2/3, WinUI presentation validation with all three, third-slot control identities, fourth-watch rejection, third stop/reopen and stale-action rejection, surviving slots after sender end or unpin, account teardown of all receivers, and empty-slot visibility with legitimate selected/error controls preserved. No live call or installed package was changed by these checks.

### Correcting Discord watch authority and completing integration (2026-11-14)

**Source-confirmed cause of the repeated close-pending failure:** Discord's current public web bundle (`web.25de09cac6a6078d.js`, downloaded without account data into `artifacts/input-discord/public-discord-watch`) keeps local `activeStreams` separately from `rtcStreams.viewerIds`. `STREAM_CLOSE` removes only the local active entry. Cached viewer IDs can remain after the watch closes. The earlier adapter used viewer membership both to borrow an existing watch and to acknowledge its retirement; therefore it could skip a required new watch or wait indefinitely on stale audience metadata. The earlier fixture conflated these facts. This is a Discord provider contract error, not missing generic SDK lifecycle machinery.

Adapter 32 now reads `getActiveStreamForStreamKey` for incoming ownership, validates exact stream identity and recognized state, treats null/ENDED/FAILED as no active local watch, and treats CONNECTING/ACTIVE/RECONNECTING/PAUSED as an existing subscription. Missing/malformed state fails closed; it does not fall back to audience metadata. Viewer IDs remain only for the separate outgoing viewer-eye fact. The method is also documented in [Equicord's store types](https://github.com/Equicord/Equicord/blob/main/packages/discord-types/src/stores/ApplicationStreamingStore.d.ts).

Owned incoming cleanup captures Discord's `stopStream` action, invoking the exact remote stream key with feedback suppressed. This action dispatches both local `STREAM_CLOSE` and network `STREAM_STOP`; the previous close-only action omitted unsubscribe. Borrowed user subscriptions remain untouched. Synchronous local acknowledgement clears the bounded retirement record immediately; delayed acknowledgement retains the existing timeout/account/channel/lease guards and never blindly retries external mutations. Local watch state and RTC lifetime remain separate. This does not restore the rejected pre-publication first-frame wait.

Provider coverage includes stale viewer cache retained after stop, all known local watch states, malformed/missing identity, delayed local close, unresolved close timeout, account change, captured cleanup actions, borrowed watches, three independent direct tracks, fourth-slot rejection, stale teardown, first/third replacement and preservation of a separately opened Discord watch. Source tests pass **199** across the web provider/backend files; the minified/sealed backend suite also passes. Adapter 32 seals to **59,287 bytes**. Discord 0.1.0.17 is staged and package-validated; deployment and native three-track evidence are still pending here.

The icon follow-up adds a custom pin/sliders Manage icon, uses the exact Discord webapp logo with original colors in the package and both widget headers, and retains semantic microphone/headset glyphs and the no-avatar Person fallback. The icon preview checks 16/20/24/32 px on light/dark surfaces (`discord-icons-preview/icons.png`). Inactive receiver rows now require actual stream or selected-pin state; a no-op spacer returned for absent management controls no longer creates Shared screen cards. Regression coverage preserves legitimate failed/selected-pin management controls.

The final transparency correction also scopes the WinUI WebView2 bridge's `SolidBackgroundFillColorBase` and `SystemColorWindowColor` resources to transparent on backend-video views. `DefaultBackgroundColor` alone did not clear this native bridge backing. `manage-direct-pixels-2/transparency.json` records alpha 0 above/below the fitted video and alpha 255 in the video. The [WinUI WebView2 implementation](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/WebView2/WebView2.cpp) explains that separate theme-backed bridge. Ordinary browser pages retain their backdrop; iframe-internal/source-encoded bars remain outside this change.

Prior integration evidence, superseding intermediate in-progress statements above: SDK 177, broker 93, session 367, WinUI shell 363, Gallery 13, YouTube 64 and coherent-runtime bridge 236 checks passed. Native management passed 79 voice checks, 50 embedded-media checks, 29 six-pin checks and 15 banner checks; the trimmed production UIA smoke exercised Manage, passive omission of Interact, Cancel and exact Unpin. The last installed candidate was Discord 0.1.0.16/adapter 31 with YouTube 0.3.46.1. Its watch regression remains acknowledged; .17 must be installed and observed before claiming live success.

### Desktop input follow-up (active)

The user requested an easy mouse/keyboard entry point and clarified that it must follow common desktop conventions, not merely expose existing controller gestures. Implement a visible host-owned Manage pins button, normal chooser/action menu navigation and focus return, with selected-window outlining. Mouse placement needs drag and resize affordances plus clear Save/Cancel; keyboard retains navigation, Enter/Space activation and Escape cancellation. Passive pins never expose content interaction. Reuse exact-target transactions and stale-target rejection across every pin kind; widgets should not implement their own window movement or input plumbing. Native interaction, focus, saved/cancelled geometry, transparent/passive surfaces, banners and mixed pin kinds require verification before final integration.

Native follow-up: `artifacts/input-discord/three-stream-direct-native/result.json` passes using shipping Discord package 0.1.0.17 / adapter 32, its real worker, Bridge and native passive pins with three synthetic provider tracks (1280×720, 640×360 and 800×600). Over three seconds the pins advanced 90, 89 and 89 frames; the first measured zero dropped frames. All three receiver elements use their original source track object. Sender end/restart and first-stream Stop watching/Watch again preserve the two sibling windows. Third-stream Stop watching/immediate Watch again passes three consecutive replacement cycles without exhausting the existing session limits. Final unpin retires every native video output and local watch subscription while cached viewer IDs intentionally remain populated. Shutdown exits 0. This validates the native host and provider lifecycle with generated tracks; three real Discord broadcasters and live network cadence remain unverified.

The reusable `tests/DiscordWebViewWidget.Fixture/Test-DirectVideo.ps1` installs a supplied shipping package into a fresh isolated catalog/profile, uses the synthetic provider route, checks completion/cadence/replacement counts and closes the test shell normally. The validation publish includes Windows App SDK analyzers and a unique binlog (`three-stream-validation-*`). No live application restart or install was performed by this fixture.

Availability correction after the native tray-menu reproduction: suppressing empty cards in Discord's page did not suppress the host menu, because `Render().PinnedLayouts` still published every pristine slot. The shipping widget now publishes a stream layout only for a nonclosed, nonended watch with a stream key. Watch intent fills that state before its existing pin-open request; connecting, playing and error presentations remain available. The generic host continues to enumerate published declarations and is not given Discord-specific filtering. Tests no longer synthesize impossible empty selections: they verify rejection of unpublished slots and refresh test-host snapshots before selecting a newly declared layout. Connecting-to-playing continuity is tested with a gated real watch. All 490 managed checks pass after this correction; package 0.1.0.18 contains it.

Additional capacity boundary: the Discord Account route presents the existing backend WebSession reference, so opening its page does not require a fifth owner session. Discord declares system-browser popups; generic native popup children, when enabled by other consumers, have a separate six-child reservation and share their root's profile/lifetime rather than opening broker sessions. Thus the native root residency budget of eight excludes the separately bounded popup budget; it is not a total-WebView-count promise. Capture uses the existing backend session and separate capture authority. Other resident widgets can still exhaust the global session/root budgets, and the existing admission failure remains visible instead of evicting unrelated active content. No capacity was increased.

Provider ownership limitation retained after independent review: Discord does not expose an adapter-specific watch token. If the user independently stops and restarts the exact same stream between adapter observations, the mutable local watch map alone cannot prove that the new subscription belongs to a different actor. Exact widget lease/account checks prevent stale widget operations, but do not establish an external Discord generation. No generic SDK ownership claim or blind retry was added to conceal this provider limitation; an event-backed provider generation would require a separately validated contract.

Desktop input design references: Windows' [keyboard interaction guidance](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions) favors native control Tab order, inner arrow navigation and visible focus. Native resize processing follows [WM_SIZING](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-sizing), which supplies a borrowed screen-coordinate RECT and identifies the dragged edge. These references inform implementation; native input evidence is still required.

Final availability verification: package 0.1.0.18 passes `artifacts/input-discord/three-stream-availability-native/result.json` using the updated native host fixture. All three pins advance 90 frames over three seconds (first stream: zero measured drops), and all three repeated third-slot replacement cycles pass. Eleven real host `GetPinChoices` checkpoints verify no stream choice at startup, only the requested first layout, all three active layouts, removal after sender end/Stop watching, reappearance only after a fresh Watch, and no choices after final unpin. This proves the existing worker/Bridge publication and pin-open ordering admits a newly declared stream without author-managed counters or extra SDK plumbing. Normal shutdown exits 0. The earlier test assumption that a pristine empty slot could legitimately be selected is superseded by this corrected availability contract.

Additional native regression after withdrawing unused stream layouts: `desktop-discord-navigation/minimum` passes 59 checks and `expanded` passes 54, both with normal exit 0. This exercises actual native main and indexed Watch actions, recoverable receiver errors, Stop watching closing the pin and removing its declaration, navigation/focus restoration and the search/server controls staying fixed above a full-height voice-channel list. The old navigation harness expected an empty pin after Stop watching; it was corrected to the user's already-approved close behavior, without changing production input guards. The test launcher now requests the existing validation activation contract and closes through normal host shutdown.

### Desktop pin management entry (2026-11-14)

Host-owned `OverlayShellPage.PinManagement.cs` now exposes a named native Pins/Manage pins rail button, the existing all-pin chooser and exact-target management menu, native opacity slider, Save/Cancel, and Return to overlay. Passive declarations still omit Interact. Ctrl+Shift+P is scoped to the overlay's native keyboard focus; browser pages retain their own key handling. Existing widget/tray management routes keep controller guidance, with the same pointer controls and adjustment tooltip. No widget-side input correlation or new SDK mechanism is needed.

The first guide-adjacent button layout exposed required controller hints painting over the button at narrow widths. A proposed two-row guide/expanded footer was **rejected**: reserving 70 more vertical DIPs reduced the proven 420x360 widget viewport at 468x548 to 420x290. Those experimental guide-wrapping and band-height edits were removed. The final approach reserves horizontal rail space through the existing bounded rail/overflow policy; compact text is `Pins`, while the accessible name remains `Manage pins`. Original guide width and content height remain unchanged.

Consumer applicability:

| Consumer | Status |
| --- | --- |
| Host-authored full-widget/layout pins, including shipping samples and FirstPartyWidgets | Existing chooser/management adopted; no widget migration |
| Discord and YouTube authored Manage buttons | Already compliant; retain exact-target/caller ownership scope |
| Browser popup, hosted webapp, and media pins | Existing host management/lifecycle path reused |
| Passive voice/video pins | Management available; Interact omitted by existing declaration checks |
| Banner notifications | Rail/guide avoidance includes management controls; banners are not pin candidates |
| SDK templates/examples | No SDK or protocol change, no migration |
| Expanded browser and standalone shell-guide validation | Guide implementation unchanged after rejecting the wrapping experiment |

Final native menu evidence: `artifacts/input-discord/pin-menu-mouse-native-7/result.json` passed 48 checks and shutdown exited 0. This covers physical Enter/Space invocation, slider Right, Tab/Shift+Tab, Save/Cancel, Escape, opacity rollback/persistence, focus restoration, and existing controller-menu/priming behavior. Actual rail captures at 420/480/900 DIPs in native Light/Dark themes were inspected without button overlap or clipping. Native minimum navigation separately passed 59 checks with the original 420x360 widget viewport intact at 468x548. Physical slider Right testing exposed the native default SmallChange difference; SmallChange=5/LargeChange=10 explicitly match the existing five-point opacity command. These captures vary the native control theme; the fixture's separate status/clock palette is unchanged. Global Windows high-contrast switching was not performed.

Overlapping an adjusted pin with the overlay can cover mouse Save/Cancel controls. Do not introduce transient HWND clipping or extra floating windows to work around this: Enter/Escape must remain available, and dragging can expose the controls. Native pointer validation owns the overlap/escape evidence; this bounded desktop entry does not change compositing ownership.

#### Implemented — mouse placement for shared pins (supersedes native move-loop proposal)

The host now provides an unpainted, nonactivating input window only during a move/resize transaction (`PinnedPlacementPointer`, using the existing `OverlayMouseSession` native input-window precedent). It covers the selected pin, including transparent pixels and native browser descendants. The actual pin HWND and authored content remain passive; this does not grant widget interaction, create a seventh pin, enter chooser/capacity state, or add a persistent placement identity. Standard edge/corner cursors and a body drag operate the existing `PinnedPlacementAdjustment` policy, with the existing Save/Cancel boundary and per-widget/layout monitor/DIP persistence. Bounds changes never reconstruct media or browser sessions.

The first attempted parent-HWND hit-test implementation was rejected after real SendInput produced no parent hit-test messages over the explicit island, despite correct native enabled/disabled state. A separate input window successfully received input, but Windows' default system move loop changed foreground. That loop was also removed. The final input window uses host-owned mouse capture and physical cursor deltas, retains main keyboard focus, and synchronously releases capture before destroying the input window. Queued movement cannot act after cancellation or target retirement. Construction cleans up a created HWND if later initialization fails; recoverable native callback errors retire the frame and cancel/report through the host rather than escape the native callback.

Pointer movement retains logical size across monitor DPI changes, including subsequent movement samples after crossing. Resizing keeps the opposite edge stationary at min/max constraints and stays on its current monitor. Monitor topology changes retain the existing transaction cancellation/reconciliation policy. Keyboard/controller placement and mouse placement share limits, saved keys, atomic writes, rollback and stale-target checks.

Input ownership is transaction-local: an actual controller adjustment movement marks the transaction controller-driven, so disconnection cancels it. Keyboard, mouse or slider adjustment takes desktop ownership. Disconnected idle controller samples are consumed without cancelling a desktop edit. A controller-opened edit disconnected before any adjustment movement now remains available to mouse/keyboard; no geometry has changed. This intentionally narrows the previous unconditional-disconnect cancellation without adding widget-side input counters.

Native evidence and consumer applicability:

| Consumer / check | Result |
| --- | --- |
| Transparent passive authored pin, with another pin present | `pointer-pin-native-final`: 20 checks. Actual SendInput drag/corner resize; actual cursor handles (corner, horizontal, vertical, body arrow); unchanged foreground; exact Cancel; Save reloaded from disk; sibling isolation; controller absence vs actual controller-disconnect behavior; native capture loss; Escape while held; target close while held. |
| BrowserSurface/full-widget pin with native WebView beneath the input frame | `pointer-browser-native-2`: same 20 native checks pass with the browser presentation visible. This directly covers native-child interception, without navigating/replacing the page to obtain mouse input. |
| Discord authored pins, SDK Gallery, Media Sessions, Performance Monitor, Spotify/YtMusic, browser/webapp consumers, YouTube/embedded compact-media pins and pop-outs | Central `PinnedWidgetWindow` adjustment entry adopts the mechanism automatically. No SDK/protocol or widget migration. Authored and browser paths tested directly; live Discord incoming video and YouTube playback during pointer dragging were not separately observed. |
| Six-pin shared ordering and banner notifications | `pointer-layer-six-final`: 33 checks, still zero z-order/frame writes for 30 unchanged appearance refreshes. `pointer-layer-banner-final`: 31 notification/ordering checks. Input frame exists only in adjustment and is excluded from saved pins, chooser and capacity. Banner avoidance already excludes visible pin bounds. |
| Pure placement policy | WinUiShell managed suite passes 365, including opposite-corner limits/negative monitor coordinates and repeated mouse samples after a DPI crossing preserving DIP size. |

Known bounded desktop limitation: an adjusted pin can overlap and cover the main overlay's mouse Save/Cancel controls. A guard-only input cutout would not expose controls because the actual pin is also above main. No extra floating command window or visual compositor cutout was introduced. The native fixtures intentionally overlap the real command area and prove that physical Enter saves and Escape cancels without trapping input; dragging the pin away also exposes the buttons. Toolbar visual/layout refinements are tracked separately and these artifacts do not certify a later toolbar layout build.

Validation binaries were published with binlogs under `pointer-pin-validation` / `keyboard-pin-validation`; native fixtures use isolated profiles and close normally. Live frontend 31964 and Discord worker 32176 were not restarted or updated by this work. The earlier failed native attempts remain under `pointer-pin-native-1` through `-5`; `-5` accidentally ran the prior binary after a concurrent source compile failure and is not evidence for the final capture implementation. A first browser attempt assumed its already-maximized default size could grow; the corrected fixture resizes toward the available range and preserves opposite-corner assertions. Final production pointer source is unchanged between the passing 20-check authored/browser runs; broader menu/toolbar fixture changes continue independently.

Final rail capture also reproduced an existing host geometry mismatch: RailControls retained two 6-DIP column gaps while both overflow buttons were collapsed, although ProductionShellGeometry only budgets those gaps when arrows exist. ConfigureRail now sets ColumnSpacing to 6 only with overflow, otherwise 0. The strict native width assertion remains unchanged; this is one shared host correction for every rail widget, with no widget-side migration.

### Desktop chooser preview and pin keyboard return (2026-11-14)

`HostChoiceDialog.PreviewHoveredChoices` is an opt-in preview contract used only by the pin chooser. Pointer hover changes the outlined native window without changing ListView selection, keyboard focus or the eventual command target. Pointer exit restores the focused row's outline; actual keyboard/controller input takes preview ownership back. Existing captured pin identity checks reject a target that closes while highlighted. Permission, install, destructive and other host dialogs keep their prior default behavior.

`OverlayShellPage.PinnedKeyboard` attaches a normal bubbling Escape handler to registered interactive pin roots and defers its decision until child/portal handlers have finished. It retains text editors, widget modals, transient controls, native browser focus, host dialogs and handled/repeated keys. An unhandled native Escape returns focus to the main overlay without unpinning. Browser page Escape is not intercepted through a global hook or page script. The visible host Return action and the existing overlay activation shortcut remain alternative return paths.

| Consumer | Disposition |
| --- | --- |
| Native authored/full-widget pins | Shared host return route; real SDK Gallery pin tests cover mouse and keyboard Interact, text editor/handled Escape preservation and unhandled return. No widget SDK change. |
| Pin chooser across widgets | Opt-in hover preview uses the existing host outline and exact live pin identity; native pointer and keyboard tests pass. |
| Browser/full-page and popup pins | Same registration path, with explicit native browser focus exclusions. Physical native browser focus exposed a separate pre-Escape ownership defect described below; browser key-return behavior is not claimed verified. |
| Compact media | Existing player-owned Escape/ExitRequested remains first; shared fallback only sees unhandled native root events. No media provider changes. |
| Passive streams / voice participants | No Interact option and no active keyboard return path. |
| Banner notifications | Not registered as ordinary pins; no new hover or Escape route. Existing notification input policy retained. |
| Other HostChoiceDialog consumers | Default hover-preview opt-out; selection and confirmation semantics unchanged. |

Native verification: `artifacts/input-discord/pin-keyboard-native-6/result.json` passes 11 checks, normal shutdown exit 0. Actual SendInput motion verifies outline-only hover, pointer exit, physical Up/Down, cancel cleanup and closure of the hovered target without retargeting its neighbor. Physical mouse click and keyboard arrows/Enter enter the native pin. A focused TextBox and a child-handled Escape remain inside; unhandled Escape and a physical click on the visible return action return safely. Validation publishes include analyzers and uniquely named binlogs. Discarded harness attempts exposed native dialog focus settlement and the existing overlay mouse-admission guard consuming the first movement; the final probe guarantees a changed wake position and continued movement. No production timers/debounce or relaxed ownership checks were added to make those tests pass.

Separate reproduced host finding: `artifacts/input-discord/pin-keyboard-browser-5/result.json` verifies the pointer hit the expected pin root before clicking its synthetic full-viewport input. The DOM's active element becomes that input, but native foreground moves to the original main HWND, the pin loses interaction and `document.hasFocus()` is false. The WebView's XAML window identity is still the pin HWND. No Escape was injected or observed (`trace: []`). This is a browser native-focus ownership issue before the new Escape route, consistent with `BrowserOwner.cs` retaining the original native creation HWND while transferring the visual tree. Earlier passive-pin checks established logical browsing and visual transfer, not physical DOM keyboard focus. The host owns the follow-up; keep browser Escape behavior unclaimed until the bounded focus-ownership investigation and its actual native tests pass.

Discord-only integration while desktop host validation continues: package **0.1.0.18**, adapter32, was installed into the existing default-profile catalog with SHA256 `1b6a36efedb14f76a9c90aac64cd5b365335f2e3e2aca28378b53b32f11b43bd`. The previous frontend closed normally; the unchanged stable host restarted as PID44536 with worker8820 from the selected0.1.0.18 package. CLI confirms enabled state. Native startup UI shows the signed-in channel picker, with no active call. Settings, permissions and browser profile were preserved. Evidence: `three-stream-availability-package/installed.json` and `startup-ui.json`. The new desktop host is not deployed yet; browser-pin physical keyboard ownership remains under investigation. No live incoming broadcaster test was claimed or performed during this restart.

A physical Return-click regression was reproduced in pin-return-race-baseline/result.json: main-window activation ended pin interaction before the mouse-down reached XAML; collapsing Return substituted Manage at that coordinate and opened an unsolicited Manage dialog. Owner: host toolbar, not SDK/widget plumbing. The bounded fix keeps distinct native Manage/Return buttons laid out in both states and disables Return while already main. Exit availability uses PinnedInteractionRequested so transient focus proof loss cannot remove the escape route. No timers, ghost spacers, native event suppression, or explicit button widths are added. Physical postReturn/no-dialog and stable-rectangle validation is required on the patched host.

Stable-toolbar follow-up evidence: pin-menu-mouse-native-8/result.json passed 49 native checks with normal exit 0; actual 420/480/900-DIP rail captures show distinct Manage/Pins and disabled Return slots without clipping. disabled-return-native-2/result.json separately exercised a physical mouse click on disabled Return after steady-main readiness: overlay remained visible, widget unchanged, pinned input false, no Host.Choice.Dialog matches, normal exit 0. The new command panel uses only a Transparent hit-test background so disabled controls remain inside the existing rail-owned UI boundary. The prior startup-only CLI attempt is excluded as an invalid precondition. Pin-foreground-to-main Return seam verification remains the separate coordinated native fixture.

#### Implemented — native keyboard focus for browser pins and input handoff isolation

Native testing exposed an existing shared-browser ownership defect, before Escape handling: clicking a text input in a browser pin activated the durable creation HWND (main), while the WebView visual and XamlRoot remained in the pin. `BrowserOwner.cs` deliberately initializes controllers on the durable host and `MoveAsync` transfers their visuals. Each peer's immediate `WM_ACTIVATE(false)` handler then revoked browser input. The page retained its active input element but reported `document.hasFocus() == false`.

A validation-only experiment retained input for one dispatcher settlement. It observed both ordinary native pin reactivation and a subsequent stable main-HWND/browser-focus state. Preserving that exact browser state restored real DOM focus and delivered physical Escape to the page. Native-pin-only deferral was insufficient; arbitrary same-process foreground permission was rejected. Temporary diagnostic overrides were removed. Two early diagnostic builds hit a logging-only IntPtr serialization error (including an edit-during-compile stale output); those are not evidence about the production focus model.

The accepted host implementation is in `OverlayShellPage.PinnedForeground.cs` and the registered-browser query in `WidgetViewPresenter.Browser.cs`. Three equivalent pin foreground handlers now share the same lifecycle. Native widget and compact-media chrome keep their native-window path. Only the selected, current, interactive pin's exact registered browser can retain ownership while main is foreground, and only while its captured native keyboard-focus HWND and browser focus still match. Existing `HasForeground` admission points evaluate this receipt, so widget actions, permission requests and controller routing retain their existing authority checks. Main-root pointer interaction, native reactivation, external foreground loss, hide, exit and teardown revoke it. Focus changes enqueue a bounded recheck; there is no DOM polling, controller reflection, browser recreation or general dialog/process foreground grant. The pin's focus cue reflects owned browser focus. Pin-owned host dialogs/file pickers first focus the existing slot wrapper and reactivate their native pin before disabling browser content input.

Two specific edge cases were established and fixed:

- A real attached popup B can replace focused browser A in the same pin. Normal A→B→A works without a new rebind framework. Replaying a delayed `SettlePinnedBrowserFocus(A)` after B held a valid receipt did incorrectly retire B (`pin-stale-focus-baseline`). Settlement now leaves an already validated receipt alone. No new focus generations were added.
- During a requested pin's temporarily invalid focus proof, controller frames/buttons could fall through to main. The input router now consumes that interval after existing modal, picker, placement and fullscreen ownership routes; it claims new presses so their releases cannot replay on main. A native fixture deterministically holds that proof gap, exercises directions/sticks/A and direct B/RB, and verifies unchanged main widget, rail and focus before valid pin input resumes.

A further held-gesture regression was reproduced with the physical cursor parked away from the pin: an authorized controller mouse-down followed by loss of focus proof and A-up left the browser press held (`pin-held-release-isolated`: down=1, click=0, held=true; normal shutdown). `BrowserSurface.CancelPendingControllerPress` now reuses the existing outside-page cancellation only for a pending controller press. The flag clears synchronously, so duplicate releases do not regenerate cancellation or enqueue extra ups while CDP finishes. No `SetInput(false)` or DOM focus loss is needed. `pin-held-release-fixed` passes 17 checks, including cancellation identity remaining unchanged on duplicates, zero old clicks, exactly one fresh subsequent click, and subsequent native typing/Escape/dialog/return/re-pin behavior. The earlier overlapping `pin-held-release-baseline` run is discarded; another fixture's pointer motion could trigger existing desktop-pointer cancellation and mask the problem. Native UI reservations are now serialized by the root agent.

Consumer / evidence matrix:

| Path | Applicability / checks |
| --- | --- |
| Full browser and widget-authored BrowserSlot pins | Fixed centrally; `pin-browser-focus-native-1` passes 11 including physical typing, page-handled Escape, native pin-owned alert, actual main return, external foreground loss/reentry and same-document unpin/re-pin. |
| Detached popup pins | Same receipt/admission path; `pin-browser-focus-popup-1` passes the same 11 checks with an owned external peer. |
| Attached popup inside an existing pin | `pin-focus-attached-baseline` passes 13 for actual A→B typing/Escape→B close→A focus. General rebind/generation work rejected as unnecessary; only the separately reproduced stale-settlement callback needed correction. |
| Native HTML dropdown | `pin-native-select-open` passes 16; `:open` was true during the snapshot, with valid receipt and pin input, main input disabled. An earlier closed-select arrow-key run was not accepted as proof of dropdown ownership. |
| Native widget pins / media chrome / banners | Shared handler preserves native activation; embedded media iframe input is permanently disabled and is not a physical browser-input consumer. Existing media, banner and native-widget regressions are part of the final combined verification queue. |
| Browser/popup lifecycle regression | `pin-browser-focus-transfers` passes all 186 checks through shutdown, including prior dialogs, permissions, placement and native transfers. |
| Templates and SDK examples | No new public SDK/protocol requirement. Browser implementations use the host's existing registered-slot mechanism; widgets do not author native focus receipts or cancellation plumbing. |

Host management and toolbar geometry remain separate follow-ups: the Return/Manage controls now have stable distinct regions, and interactive-pin adjustment requests restore main keyboard ownership. Final native toolbar/menu verification is tracked by the coordinating agent; the browser-focus artifacts above do not claim that every later toolbar layout was verified.

#### Final native chooser, Return seam and pin-owned management verification

`pin-focus-final-validation` includes the final production focus, input-cancellation and stable toolbar hit-test changes. Two isolated native runs completed normally with exit 0:

- `artifacts/input-discord/pin-return-only-final/result.json`: 8 checks. Production Interact establishes actual native pin foreground; a physical Return click leaves interaction, keeps the overlay available, opens no unintended Manage dialog and preserves distinct unchanged button rectangles. Native pin-owned Move and Opacity each restore main keyboard ownership, with physical Enter saving and Escape cancelling.
- `artifacts/input-discord/pin-keyboard-full-final/result.json`: 17 checks. The complete real-pointer chooser hover/exit, physical keyboard preview takeover, cancel cleanup and stale hovered-target rejection pass. Mouse and keyboard Interact, text-editor and child-handled Escape preservation, unhandled native Escape return, physical Return and all four adjustment cases pass together.

The original 180-DIP pin height was retained throughout. The hypothesis that pin-owned management needs a new main-owned dialog handoff is rejected for this bounded flow: all four existing management routes work at that size once the Return toolbar activation race is corrected. An earlier diagnostic had observed an unsolicited pre-existing main Manage dialog, not a tiny-pin layout failure. No management API or modal-routing refactor was added.

The last full-flow harness failure occurred before Return while clicking an opening Interact dialog. The native pointer helper now remeasures its UI Automation target after waking the existing mouse-admission guard, immediately before the actual click. This changes validation only; selection, foreground and click-target assertions remain intact. The passing final full flow includes physical input, not automation Invoke or synthetic XAML events. Browser-owned Escape continues to use the separately verified native browser focus path; these native-widget checks do not replace the browser-specific evidence above.

Final stale browser settlement after-fix evidence: `artifacts/input-discord/pin-stale-focus-fixed/result.json` passes 18 checks on `pin-focus-final-validation`, normal shutdown exit 0. A real attached popup B acquires a validated receipt and receives physical typing/Escape before the fixture explicitly replays the delayed settlement captured for parent A. B retains input and its newer valid receipt. Closing B restores the unchanged parent; native alert ownership, physical main Return, same-document unpin/re-pin and fresh browser Escape also pass. Together with the failing `pin-stale-focus-baseline`, this verifies the minimal current-receipt guard without adding a general focus revision/rebind mechanism.

Follow-up verification: `pin-stale-focus-fixed/result.json` passes 18 native checks with normal exit, including replaying the captured parent-A settlement after a real attached child B owns focus; B stays interactive and its document receives input. Subsequent child close→parent return, alert, main return and same-document re-pin also pass. This validates the minimal existing-receipt guard without adding a general rebind mechanism.

### Final integration and verification (2026-11-14)

The final trimmed/self-contained host is built at `artifacts/input-discord/desktop-pins-final-release` with matching runtime in `desktop-pins-candidate/runtime`. Release verification passes **302 local managed dependency references** and validates balanced published asynchronous cleanup. Its DLL timestamp is later than the latest production frontend source. `desktop-release-smoke/result.json` passes against the trimmed production host and matching runtime: installed Discord UI fixture (shipping widget class with synthetic data) opens a passive participant pin; the actual host menu omits Interact; opacity/Cancel work; exact Unpin retires the surface and disables the empty chooser. Normal shutdown exits0.

Final native desktop verification: toolbar49 with strict420/480/900 Light/Dark geometry and physical keys; disabled Return click preserves visible main/no menu; ReturnOnly8; full chooser/native-interaction flow17; browser fullpage11, detachedpopup11, attached/stale callback18, open HTML dropdown+input-gap16, held controller release17; broad browser/popup regression186; compact/embedded-media50 with `pixelFailures=[]`. The final Return fixes keep distinct command positions and supply transparent hit-test backing for disabled command chrome. No widget-content height reduction or generic controller-guide wrapping was retained. The suspected tiny pin-owned management-dialog problem was rejected: all four Move/Opacity×Enter/Escape cases pass at the original180-DIP pin height; no management relocation/refactor was needed.

The user candidate was restarted normally into this verified host as frontend **20992**, with enabled Discord **0.1.0.18** / adapter32 worker **45108**. Native startup inspection verifies the signed-in Discord channel picker and the new Manage pins/Return controls; both are correctly disabled with no open pins. The existing SettingsRoot, InstalledCatalogRoot, browser sign-in, permissions and saved placements were preserved. Evidence: `desktop-pins-candidate/installed.json`, `ready.json`, `startup-ui.json` and the release verifier log. No commit, merge or push was performed.

Limits retained explicitly: three moving synthetic native tracks and repeated independent restart/teardown were tested; three live Discord broadcasters were not available during integration, so live simultaneous reception is not claimed. There was no active live call after restart and no test messages were sent. General GPU performance and live YouTube playback during pointer dragging are not established by the synthetic/embedded checks. An overlapping pin can cover mouse Save/Cancel; native overlap tests confirm Enter/Escape remain usable, and dragging can expose the controls. Browser-owned Escape remains the page's action; use the visible Return control/main-overlay click or configured overlay shortcut to return. Earlier source-only concerns that failed to reproduce were not used to add a broader focus/rebind framework.

### Click-to-interact and complete overlay stacking (2026-11-14 follow-up)

User-authorized follow-up: make an interactive pin directly clickable while the overlay is open, preserve explicit Passive declarations and closed-overlay click-through, and keep the scrim above ordinary apps without reintroducing z-order churn.

Ownership: host only. The existing `CanInteractWithPin` declaration check and `EnterPinnedAsync` lifecycle/foreground admission remain authoritative. Widgets supply no mouse counters, focus reconstruction or native-window handles. The first complete click activates; it is not replayed into content (e.g. a Leave button). Main-root pointer capture and exact pin/session revalidation reject cancelled/stale gestures. Background hit testing chooses the topmost visible pin, so a passive pin cannot activate a different pin underneath it. Existing overlay controls retain priority.

The full-monitor transparent main HWND receives pointer input over its transparent gaps. Therefore its existing `ProductionGapPressed` boundary routes eligible clicks instead of dismissing. For pins beyond that HWND (including another display), the existing placement input-frame mechanism is shared as `PinnedSurfacePointer` with an activation mode. Frames stay directly above their own visual peer, below main, paint nothing, and never grant widget input themselves. They are bounded by the six open pins, removed for hide/modal/adjustment/retirement, and excluded from selection/capacity/persistence. The initial frame-only experiment failed native hit-target verification on the main monitor; it was narrowed to complement the existing gap route rather than raising invisible frames over overlay controls.

Backdrop diagnosis: `EnsureBelow(backdrop, main)` did not constrain the desktop band. A non-topmost scrim below an ordinary app satisfied it. Native evidence additionally showed that promoting only the scrim could be undone by placing it below a non-topmost main HWND. `MainWindow.Appearance` now establishes the visible main window's topmost band, then `OverlayDesktopBackdrop.Apply` establishes the scrim band and relative placement. Existing relative peer ordering is retained; already-correct windows are not raised on every update.

| Consumer | Applicability and verification |
| --- | --- |
| Discord voice controls, Gallery authored interactive layouts, Performance Monitor, Spotify and standalone YtMusic authored layouts | Shared registered-native-pin path; no widget migration. Gallery/native fixtures exercise activation, switching and shared placement. Shipping declarations inspected; no widget-specific mouse plumbing needed. |
| Discord participants and all three stream layouts; Gallery passive layout | Existing explicit Passive declaration remains authoritative; no activation targets. Passive fixture checks this contract; no change to Discord reception or media processing. |
| Legacy/full-widget pins, Browser and hosted webapps | Shared native registration plus existing browser-focus receipt; physical click/typing/return tests required. |
| Detached browser pop-outs and attached browser dialogs | Same registration/activation and browser ownership; popup fixture required. |
| YouTube and EmbeddedMedia compact pins | Host media registration; original browser/video retained. Embedded sample verifies moving playback, click activation and per-player controls. |
| Banner notifications | Not registered as selectable pins; never gets an activation target. Shared topmost/relative-order and six-pin+banner churn tests required. |
| Templates and SDK examples | No new SDK/protocol surface, defaults or declaration changes. Existing supported pin API applies. |

Verification is recorded below as completed; source inspection alone does not establish consumer runtime correctness. Live provider reception is outside this host-only change.

Completed verification for this follow-up:

- `click-pins-native-final/result.json`: **52 native checks**, exit0, using freshly built current Gallery worker and matching installed bridge. Includes physical activation/switching, no first-click delivery into content, second-monitor activation, main return, chooser blocking, hide/reopen, six-pin capacity/identity/persistence, backdrop demotion/recovery, and banners. Three independent batches of30 unchanged appearance refreshes produce **zero z-order writes and zero frame refreshes** (ordinary-window recovery, six pins, and six pins plus banner). The old adjacency algorithm still reproduces21 writes/3 updates.
- `click-pins-passive-final/result.json`: **12**, exit0: explicit passive layouts never get mouse activation targets; native input, View/chooser exclusions, acrylic, placement/cancel remain correct.
- `click-pins-placement/result.json`: **20**, exit0: original native drag/corner/capture/save/cancel/overlap/close-during-drag behavior remains intact after sharing the input-frame implementation.
- `click-pins-browser-final/result.json`: **34**, exit0: physical activation/typing/page Escape, real dropdown, attached popup/stale callback, held controller release, pin-owned alert, Return and same-document re-pin. `click-pins-popup-final/result.json`: **26**, exit0, detached pop-out including external foreground loss/re-entry. `click-pins-web-regression/result.json`: **48**, successful normal teardown, generic browser lifecycle/media retention/profile isolation/capacity/recovery.
- `click-pins-menu-recheck/result.json`: **17**, exit0: physical chooser hover/keyboard ownership, stale row, native editor/Escape, stable Return hit regions, and all four Move/Opacity×Save/Cancel flows. The preceding `click-pins-menu-final` run timed out during initial chooser keyboard focus before its first assertion; a fresh isolated rerun passed unchanged. This is recorded as a non-reproduced native readiness/timing failure, not proof of a production defect or a reason to weaken the assertions.
- `click-pins-media/result.json`: **53**, exit0, `nativePassed=true`, `pixelFailures=[]`: original embedded-media players, independent playback, physical click activation and per-pin controls. This is the host-owned path used by YouTube; it does not establish live YouTube network playback.
- `click-pins-discord-streams/result.json`: installed Discord0.1.0.18, three moving synthetic sources through the real backend/video pins; each adds90frames/3seconds, primary stream dropped0; three immediate third-stream stop/rewatch cycles and11 dynamic availability checks pass, exit0. No live Discord broadcaster claim.
- WinUiShell tests: **Passed365, Failed0, Skipped0**. Final validation build succeeds with no warnings/errors. `git diff --check` reports no whitespace errors.
- `click-pins-release`: trimmed/self-contained production host with WinUI analyzers; published verifier checks302 managed dependencies and balanced cleanup. `click-pins-release-smoke/result.json`: **6**, exit0, installed Discord UI fixture verifies passive management/opacity/unpin and physical click-to-interact/Return on voice controls in the production binary.

The initial validation output directory omitted MSBuild's required trailing separator and generated malformed PRI resource names; fixed the validation build invocation with an explicit trailing-slash OutDir. Those two startup failures are excluded from behavior evidence. The first passive run used an older default fixture without the Gallery readout button; rebuilt the current shipping Gallery fixture and used the matching bridge for final passive/native/placement runs.

Normal user profile restarted into `artifacts/input-discord/click-pins-release/OverlayFrontend.WinUI.exe`, frontend19664, existing Discord0.1.0.18 worker42144. Existing default-profile settings, catalog, browser sign-in and saved placements were retained. Initial native startup finds the Manage pins control; runtime log confirms the Discord worker started and completed interactive lifecycle without failure. Installation/hash evidence: `click-pins-installed.json`; the candidate's installed.json now points to this host. No commit, merge or push performed.

### Scoped — unified adjustment, right-click management, contextual shell controls (2026-11-14)

Status: design/scope only, requested after click-to-interact delivery. No application behavior changed in this pass.

#### User flow and naming

Use **Adjust pin** as the single action, with **Position, size & opacity** as its visible supporting description wherever the menu presentation supports one. Replace the separate Move/resize and Opacity choices across the global chooser, tray menus, controller/radial routes, and widget-requested pin.manage menus. Keep a global **Pins** launcher for selecting among open pins, but remove the per-pin action menu. One pin opens adjustment directly; multiple pins show the existing window-highlighting chooser and then enter adjustment directly. Interact and Unpin remain reachable through the direct routes scoped below. SDK callers continue using the supported pin.manage contract rather than authoring their own adjustment UI.

Opening Adjust pin outlines the exact selected native surface and starts one preview transaction. Mouse drag moves; edges/corners resize; an always-visible, labeled **Opacity: N%** slider adjusts opacity live using the existing30–100% range. A temporary host-owned toolbar beside the selected pin shows the opacity slider, **Save**, **Cancel**, and a close control; these controls are not placed in the tray. Do not place opacity behind overflow, a tab, a tooltip-only gesture, or a second mode. Compact layout must retain the label/value and slider and preserve the proven minimum widget viewport. The toolbar exists only during adjustment and consumes no permanent rail space.

Keyboard: arrows move, Shift+arrows resize; Tab reaches the opacity slider, whose normal arrow keys change its value. Enter saves and Escape cancels the entire transaction, with normal button activation preserved. Controller: keep left stick/D-pad for moving and right stick for resizing; add clearly displayed LB/RB opacity adjustment in5-point steps; A saves and B cancels. All input belongs to the adjustment until it ends; no release is replayed into widget content. The controller hint layout must keep opacity discoverable at compact widths.

Save commits all user changes once; Cancel restores position, size and opacity together. **Opacity-only or no-op saves must not overwrite a preferred saved position with temporary collision/monitor displacement.** Track whether geometry was actually edited (including edit-then-restore), not which menu entry opened the transaction. Retain existing monitor/DPI remapping, monitor-disconnect cancellation, atomic-save, stale-selection, hide/close and controller-disconnect safeguards. Unpin remains a distinct operation outside the reversible preview transaction.

#### Right-click behavior

While the overlay is open, right-clicking an inactive pin directly opens **Adjust pin** for that exact topmost visible pin, with no chooser or intervening action menu. It works on explicitly Passive pins as well as inactive Interactive pins, without granting content input. Consume the full right-click gesture and reject stale release/queued work after close, hide, modal/adjustment changes or replacement. A pin behind another pin must never receive the gesture.

An already-interactive pin keeps its content's normal right-click behavior (browser context menus, text controls, etc.). When the overlay is closed, all pins remain click-through for both mouse buttons. Banner notifications are never pin-management targets. Main-overlay content retains precedence over an obscured pin. Support both the main-window gap route and the native frame route outside that window/on other monitors.

Source finding: `OverlayShellPage.PinPointer.cs` currently uses CanInteractWithPin both to pick a pointer target and to create its input frame; this deliberately excludes Passive layouts. Extend host admission into distinct manage and content-interaction actions, preserving the SDK's existing explicit Interaction declaration. This is a host behavior gap, not a missing widget API. `PinnedSurfacePointer` currently consumes only left-button activation; add right-button transaction handling centrally. No widget-side focus flags, mouse counters or native-handle plumbing.

#### Bottom controls and Return visibility

Replace the unstyled text rectangles with native icon buttons matching the neighboring notification/task controls: shared shell palette, rounded shape, control size, spacing, hover/pressed/focus states and High Contrast behavior. Use a host-owned version of the existing pin-management glyph for Manage pins and a clear return/back glyph for Return. Supply accessible names and tooltips; retain Ctrl+Shift+P and keyboard focus access. Do not resolve a host glyph from the Discord package.

Return is shown only while pin interaction is requested/available, including the existing transient browser-focus handoff where a safe exit must remain available. In ordinary main-overlay use it is **Collapsed**, absent from the tab order and contributes zero reserved width. Its visibility and rail measurement must agree; do not retain a permanent empty slot. While adjusting, hide Return and show Save/Cancel/opacity beside the selected pin, not in the tray.

The earlier native regression is still relevant: Windows can activate main and revoke pin interaction before the initiating pointer-down reaches XAML. A naive Visibility=Collapsed binding can reflow Manage/a rail item under a click intended for Return. Scope a host input-boundary fix that preserves the initiating action/hit region until the mouse gesture is routed/completed or cancelled, then collapses Return and recalculates rail space. Do not merely swap button meanings, use timing-based delays, or keep a permanently disabled placeholder. Prefer anchoring neighboring shell actions so appearance/disappearance does not move them unnecessarily. Native foreground-transition evidence is required before accepting this change.

#### Ownership, size and consumer matrix

This is a moderate host/UI change: unify the existing adjustment transaction and command routes, extend the existing host pointer admission, and fix conditional command layout at the native activation boundary. No SDK/protocol redesign or widget migration is proposed. The input-boundary/Return work carries the greatest regression risk; the existing complete-click activation and stable-toolbar tests provide relevant precedents, not proof that conditional collapse is already safe.

| Consumer | Required treatment |
| --- | --- |
| Global Manage pins, widget tray, controller/radial menus | Route adjustment entry directly to the unified mode, retaining only a target chooser when necessary; remove the intermediate per-pin action menu and preserve return focus. |
| Discord, YouTube and other SDK pin.manage callers | Already use host management; inherit the combined mode with unchanged caller scope and public API. Check exact-layout and media-owner routes. |
| Native authored/full-widget pins (Gallery, Performance Monitor, Spotify, YtMusic, Discord voice controls) | Shared adjustment and right-click path; verify native actions never receive the initiating click. |
| Discord participants/three stream layouts and other Passive layouts | Right-click manages; left-click never enables content. No change to provider/media lifecycles. |
| Browser pages, attached/detached pop-outs and host-owned embedded-media pins | Preserve live document/player identity. Active browser context menus remain available; inactive right-click enters host adjustment. |
| Banners | Excluded from targeting/capacity; retain layer, click-through and theme behavior. |
| Templates/SDK examples | No API adoption required; update only shared user-facing instructions if they list separate move/opacity actions. |

Acceptance tests: combined geometry+opacity save and cancel; opacity-only save on a collision-displaced pin; no-op and edit-then-revert; DPI/monitor disconnect; target closes while held/saving; overlapping pins; inactive/active/passive right-click on main and secondary monitors; closed-overlay pass-through; modal protection; native and browser Return clicks across WM_ACTIVATE; no accidental Manage/rail invocation on collapse; keyboard/controller focus recovery; compact420/480/900DIP layouts in Light/Dark/High Contrast; original browser/video identity, six pins plus banners and zero steady-state order/frame churn. Reuse the current native fixtures and production smoke, adding behavior assertions rather than source-string tests.

Design reference: Windows slider guidance supports a labeled slider with direct visual feedback for a relative quantity such as opacity: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/slider . The choice to enter adjustment directly on inactive-pin right-click follows this user's explicit request; it is not described as the universal Windows context-menu convention.

#### Revised placement — controls beside the adjusted pin

User refinement: put the adjustment controls around the selected surface, with an accessible cross to close it. Replace the previous tray/contextual-command-area placement with a small host-owned floating toolbar. Recommended semantics: **× means Close pin (Unpin)**, consistent with a window-close affordance; **Save** commits and exits adjustment, **Cancel/Escape** discards the preview and exits adjustment. Label the cross **Close pin** in its tooltip/accessibility name. This is a scoped recommendation, not implemented behavior. Closing uses the existing exact-pin Unpin operation, preserves the prior saved placement instead of committing an unfinished preview, and does not invent a widget-specific Stop watching/Leave action.

Place the toolbar just above the outlined pin with a small gap; put the close control at its upper-right end. The slider retains a visible Opacity label and percentage. Prefer below or alongside the pin when the upper edge lacks room; clamp the toolbar to that monitor's work area, account for DPI, and keep it reachable even for a maximized-size pin or a pin near a corner. It follows the selected pin during move/resize without recreating its content. If no outside edge has room, an inset host-owned toolbar is an explicit fallback, kept above the pin rather than obscured behind it. Avoid covering important content whenever space permits.

The toolbar remains fully legible regardless of the pin's opacity. Dragging its slider or clicking its buttons must not move the pin. A complete gesture has one owner; switching from drag to slider cannot leak releases to the widget/browser. Keyboard Tab reaches these native controls; controller adjustments retain visible hints. The pin itself stays content-passive while adjusting, including when its SDK declaration is Passive.

One toolbar exists for the one active adjustment across up to six pins. It is framework chrome, not another pinned surface: no chooser row, capacity slot, independent saved placement or widget-owned state. The host owns its input, native focus, theme/accessibility, display mapping and lifetime. Ordinary foreground transitions between main, the selected pin's adjustment frame and its toolbar must remain inside the same adjustment transaction; a genuine external foreground loss still follows the existing cancel policy. Save, Cancel, close/unpin, widget stop, overlay hide, monitor removal and shutdown all retire the toolbar/capture safely. Include its bounds in notification overlap avoidance and its role in shared peer ordering without repeated stable-state z-order writes.

This adds a bounded host presentation/lifecycle change to the previously scoped adjustment unification. Existing `PinnedWidgetWindow` peer ordering, `PinnedSurfacePointer` capture lifetime and the shell's native adjustment controls are precedents, not a ready-made floating-toolbar API. Reuse native controls and shared palette; do not ask widgets to create/manage toolbars. Native testing must cover all pin types, main/secondary monitors, mixed DPI, every screen edge, narrow/large pins, high contrast, minimum/maximum opacity, simultaneous banners, click targeting, focus transitions, drag/slider capture loss, and teardown while held. The old bug where a pin can cover tray Save/Cancel should be specifically eliminated by the new placement.

### Scoped bug fix — widget uninstall reports diagnostics_timeout

User-reported runtime symptom: Settings displays **Widget uninstall failed (diagnostics_timeout)**. User additionally confirms uninstall takes longer than two seconds. No uninstall was attempted by the agent in this scoping pass and no installed data was deleted.

Leading hypothesis: the end-to-end operation exceeds the current two-second diagnostics request budget. This is supported by source and the reported duration, but the exact phase and actual committed outcome of the user's failed attempt remain unverified.

Concrete findings and ownership:

- `src/FirstPartyWidgets/SettingsWidget/SettingsReadinessRetry.cs:6` defines Operations as2seconds, explicitly separate from short readiness attempts; `SettingsWidget.Worker/Program.cs:47` applies that budget to the operations client.
- `src/PlatformDiagnostics/DiagnosticsPipeTransport.cs:92` defaults the server request budget to2seconds; `DiagnosticsWidgetProcessCompanion.cs:98` uses this default. Client `ExecuteAsync` covers connect, authentication, request, response and receipt within its budget, then maps expiry to diagnostics_timeout around line899. Increasing only one end would leave the other deadline in place.
- `src/WidgetCatalog/WidgetCatalogService.cs:545` acquires the catalog operation lock, performs pending cleanup/recovery, discovers/validates package identity and executes uninstall. `src/WidgetBridge/BridgeWidgetPackageUninstallService.cs:84` then awaits catalog reload using CancellationToken.None after the mutation. Slow lock admission, filesystem work or reload can delay the response; cancellation/connection expiry does not establish that nothing was removed.
- `src/FirstPartyWidgets/SettingsWidget/SettingsInstalledWidgetUninstallOperation.cs:72` currently converts the transport exception directly into a failure status with ReloadCatalog=false. That can leave stale UI after an operation whose mutation already committed. This risk is inferred from the source, not yet reproduced for this user's package.

Planned fix: give uninstall an appropriate bounded operation-specific budget at both ends, informed by measured phase timings; retain short authentication/connect and normal read/readiness protection instead of globally relaxing all diagnostics operations. Determine whether the synchronous path is adequate under normal large-package/reload durations; if sustained work cannot fit a sensible bounded request, evaluate a host-owned tracked operation/status result as a separate evidence-driven extension. Do not prescribe a generic jobs framework before measuring.

Before telling the user it failed, reconcile the authoritative installed-catalog/uninstall-marker outcome: distinguish not started/refused, in progress or outcome not yet known, removed with cleanup/recovery pending, and fully complete. Refresh Settings when authoritative state changes. Do not blindly replay a destructive uninstall after a timeout; preserve confirmation-token, publisher/version, enabled/resident package, root/path, authority cleanup and catalog recovery checks. Reuse existing Uninstalled/CleanupPending/RecoveryPending results and durable uninstall markers where sufficient. A retry must inspect the current identity and pending state first.

Owners: diagnostics transport/companion for operation budgeting and bounded execution; WidgetCatalog/Bridge for mutation/commit/recovery truth; Settings for truthful progress/status and catalog refresh. Widgets and SDK pin APIs are not involved. Complexity is small-to-moderate if measured operation budgets plus existing reconciliation are sufficient; larger only if evidence requires an asynchronous operation contract.

Consumer matrix: Settings uninstall inspection/confirmation/mutation/refresh is affected; CLI/catalog uninstall already bypasses this diagnostics transport and must retain the same mutation safeguards; other diagnostics users (readiness, regular snapshots, local-data cleanup, support export, marketplace/update commands) require a compatibility check if shared dispatch changes, without automatically granting them the uninstall timeout. SDK examples/templates and ordinary widget pin consumers are not applicable.

Acceptance tests: delayed uninstall and catalog reload beyond2seconds; lock contention; many/large package files; timeout/disconnect before mutation vs after durable commit; late response; duplicate user retry; stale confirmation/version replacement; disabled-but-resident worker or browser resources; cleanup pending/recovery pending; unrelated catalog items unchanged; Settings navigation/closure during operation and next-open reconciliation; existing diagnostics authentication/nonce/server-PID validation and bounded timeout tests remain passing. Reproduce with isolated disposable packages, record phase timings and final catalog/filesystem/UI state, and perform a native Settings flow. Do not test by uninstalling an unrelated user package.

#### Remove the intermediate Manage pin action menu

User design follow-up: the four-row Move/resize, Opacity, Interact, Unpin menu becomes redundant with direct interaction and the floating adjustment toolbar. Remove that per-pin action-selection layer. Retain the **pin chooser**, which serves a different purpose: identifying a target among up to six surfaces, including obscured/off-monitor surfaces that cannot easily be clicked. Highlighting a chooser row must still outline the actual window.

Direct routes:

- Mouse: left-click an eligible inactive pin to interact; right-click any inactive pin to adjust. The floating toolbar provides opacity and × Close pin. Already-interactive browser/content right-click is preserved. The global Pins launcher remains a discoverable/accessibility fallback, with one pin going straight to adjustment and multiple pins going chooser → adjustment.
- Keyboard: Ctrl+Shift+P from the main overlay's native keyboard scope goes to the same chooser → adjustment flow (one pin skips the chooser). Tab reaches opacity, Save, Cancel and Close pin. Provide a direct **Interact with pin** keyboard command/shortcut using the existing eligible-only chooser (or direct entry for one eligible pin). This is an explicit gap to implement before deleting the menu; do not claim pointer click-to-interact or controller View already covers keyboard-only users. Choose a binding after checking existing host mappings and document/show it in the native command tooltip/help; do not intercept a browser page's existing shortcut while it owns keyboard input.
- Controller: View retains direct interaction/eligible-pin chooser. The tray's single Adjust pin entry goes chooser → adjustment, without another action menu. Left stick/D-pad moves, right stick resizes, LB/RB adjusts opacity, A saves and B cancels. Provide a visible X **Close pin** command in adjustment, equivalent to the toolbar cross, with the existing gesture priming/release ownership safeguards. Closing does not commit unsaved geometry/opacity.
- SDK pin.manage: preserve its public API, exact selection identity, caller scope, and stale-request protection; change host presentation to open adjustment directly for an exact or sole eligible management target, or show only the scoped pin chooser before adjustment. Discord exact-layout and YouTube host-owned-media routes inherit the same flow. Do not leave a hidden legacy per-pin menu in widget entry points.

Keep management selection distinct from content interaction eligibility: passive pins can be selected for adjustment/close but never appear in Interact's chooser. Banners are neither management nor interaction targets. No-pin global/SDK requests retain the existing disabled/empty result behavior and cannot retarget another widget's pin.

Acceptance: exercise every former menu action through mouse, keyboard-only and controller-only routes, including passive layouts; verify there is no intermediate action menu at global/tray/widget/SDK entry points; preserve exact-target preview, stale row rejection, controller button-release isolation and focus return. The floating toolbar and conditional Return work remain scoped, not implemented.

### Implementation in progress — unified pin adjustment and uninstall deadline (2026-11-14)

Active goal covers all scope above; do not treat this checkpoint as completion. Work remains on native consumer regressions, visual/theme/DPI checks, native Settings uninstall flow and final release/candidate integration.

Implemented host core: one PinnedAdjustment transaction with actual GeometryChanged comparison; existing save preserves preferred placement for opacity-only/no-op/edit-then-revert. PinnedAdjustmentToolbar uses the existing native peer class as host toolbar chrome (not a selectable pin), with opacity, Save/Cancel and Close, independent opacity, monitor-clamped placement, native focus/capture cleanup and banner avoidance. Controller move/resize/Save/Cancel stay in the main guide; LB/RB opacity and X Close hints are placed beside the controls to avoid overfilling the compact main guide. Global/tray/SDK management goes directly to adjustment after any required target chooser. Ctrl+Shift+I adds main-native-scope interaction, preserving browser-owned keyboard shortcuts; Ctrl+Shift+P manages. Passive pins now have management-only right-click admission; left-click still never enables their content. Main gap and other-monitor native input-frame routes share exact-target checks. Shell controls use matching48DIP rounded theme buttons and a host PathIcon; Return collapses without retained width.

Native Return evidence: merely observing WM_MOUSEACTIVATE on main was insufficient; a UI-thread message-hook experiment also failed the physical Return test and was removed in full. The accepted smaller change captures a left-button gesture over Return inside the existing pin interaction-revocation boundary, before changing layout, with the main-root pointer boundary for borrowed-browser focus. Pointer completion/cancellation/hide clears it. `adjust-pins-native-6/result.json` passes19 native checks including physical Return collapse without an unsolicited action, combined drag/slider Save/Cancel, passive right-click, exact management, Close and zero stable ordering/frame writes. This is initial core evidence, not final all-consumer evidence.

The first floating-toolbar test used UI Automation screen coordinates before/after native island movement and missed its target. The fixture now waits for native layout and transforms its element relative to the exact toolbar root plus native ClientToScreen, checking the actual hit HWND. Physical slider input, drag, Save and Cancel pass with that stricter target check. The first toolbar raster was inspected visually. Further visual passes and expanded keyboard/controller tests are pending; later fixtures build incrementally under adjust-pins-build-* and adjust-pins-native-*.

Uninstall: authenticated, fully validated uninstall execution gets a bounded30second budget on both diagnostics endpoints; connection/hello/request admission and response receipt retain normal short budgets, as do other operations. Settings performs one bounded inspection after uncertain delivery or cancellation, reloads catalog state, and avoids automatic destructive replay or falsely claiming rollback/failure. Added a clear Uninstalling widget progress status. `adjust-uninstall-diagnostics-tests.log` passes29/29, including a2300ms operation beyond the old deadline and bounded timeout with late completion; `adjust-uninstall-settings-tests.log` passes95/95 including uncertainty/cancellation reconciliation and existing package identity/data safeguards. WinUiShell currently passes367/367 including toolbar placement and preferred-geometry tests. Native end-to-end uninstall and full release integration remain required.

### Final validation — unified adjustment and uninstall (2026-11-14)

Supersedes the pending checks in the checkpoint above. Implementation is host-owned;
no new widget mouse plumbing, SDK contract or protocol change was necessary for this pass.

| Consumer | Result and evidence |
| --- | --- |
| Global Pins, direct tray Adjust, keyboard and controller | Migrated to one transaction and adjacent toolbar. `adjust-pins-native-15/result.json`: 74 checks, including physical slider/drag/Save/Cancel/Close, Ctrl+Shift+P chooser, Ctrl+Shift+I eligibility, LB/RB opacity, X Close, and stale toolbar callbacks. `adjust-rail-regression/result.json`: 53 checks, compact 420/480/900 rail, native Tab order and retained-opening-button rejection. |
| Shared native authored/full-widget pins | Inherits host adjustment without consumer edits. `adjust-pins-six-regression/result.json`: 52 checks across six surfaces, target retirement, saved/collision placement, monitor transfer and backdrop ordering. Gallery provides production Interactive and Passive declarations. |
| Discord SDK exact-layout management, voice controls and participants | Already compliant SDK entry points; no widget migration. `adjust-voice-regression-2/result.json`: 79 native checks, direct adjustment, passive eligibility, caller return focus, exact retirement and stale Manage rejection. |
| Discord three incoming video pins | `adjust-three-stream-regression/result.json`: three moving synthetic tracks advanced 90/89/89 frames, primary reported zero drops; three third-stream replacements and 11 availability checks passed. No new live broadcaster/provider claim. |
| Browser/webapp root, detached pop-out, attached popup | Existing page ownership retained. `adjust-browser-regression`, `adjust-popup-regression-2`, `adjust-attached-popup-regression`: 21/21/25 checks, real native typing/Escape, alerts, Return activation, hide/reopen and same-document unpin/re-pin. |
| YouTube/host-owned embedded-media management | YouTube's SDK caller already routes through the same owner-scoped host command. `adjust-media-regression`: 53 checks on the production embedded-media sample, visible frame markers, unchanged playback ownership and direct SDK adjustment. Live YouTube network/provider playback was not retested. |
| Other shipping native consumers in samples and src/FirstPartyWidgets (Spotify, YtMusic, Performance Monitor, media controls, etc.) | No direct manipulation of adjustment mode or toolbar. Inherit the shared presenter/window behavior; coherent bridge/runtime suite passed 236/236. Individual live-provider workflows were not repeated. |
| Banners | Excluded from pin targeting and capacity. Native combined-mode fixture verifies passive banner input, avoidance of toolbar and zero order/frame writes across 30 unchanged updates with frame delays. Six-pin fixture retains backdrop/band checks. |
| Templates and SDK examples | No API adoption required. User pinning documentation updated; removed old separate opacity/action-menu assumptions in native validation fixtures. |
| Settings and diagnostics uninstall | Matching published Settings and Bridge used by `adjust-uninstall-native-3`: physical keyboard opens confirmation, Cancel preserves package, Confirm removes only the disposable package, Settings remains usable. CLI independently verifies intact neighbor. 29 diagnostics, 95 Settings and 52 catalog tests pass; coherent bridge suite includes exact revision, retirement, recovery and local-data isolation. |

The toolbar uses `ShellChromeStyles.Register(..., typographyOnly: true)` and the
existing `NativeComputedStyleAdapter` boundary so theme typography cannot import
widget-body geometry into host controls. An experiment overriding native button
template brush resources caused runtime XAML failures and was removed; it is
rejected, not an unfinished styling task. Accepted control-property paint and
native visual-state ownership pass native screenshots. Light, Dark, High Contrast,
1.5 text scale, 1.25 interface scale, both physical monitors and all four work-area
corners are covered by `adjust-pins-native-15`. Toolbar opacity remains 100% while
the pin is at 30%. Native shell icons were also inspected in the compact rail.

Concrete shared findings resolved here:

- `Shell/OverlayShellPage.PinPointer.cs`: prior content eligibility also prevented
  Passive pins from being right-click management targets (source finding, now
  reproduced/fixed by physical pointer test). Owner: host; existing SDK declaration
  remains sufficient.
- `Shell/OverlayShellPage.PinReturn.cs` and `Pinning.cs`: immediate Return collapse
  during native activation could retarget the initiating click (native reproduced).
  Owner: host input boundary; preserve only that pending gesture, then reclaim width.
- `Shell/PinnedPlacementAdjustment.cs`: unified mode cannot infer preference writes
  from the entry command (source risk). Owner: host transaction; compare original
  geometry/monitor, preserving saved preferred placement on opacity-only/no-op saves.
- `PlatformDiagnostics/DiagnosticsPipeTransport.cs`: the ordinary two-second request
  deadline cut off legitimate uninstall execution (real pipe test reproduces a
  2300ms operation). Owner: transport, with operation-specific bounded execution
  after authentication. Original public constructors and other deadlines remain.
- `SettingsInstalledWidgetUninstallOperation.cs`: a lost/cancelled reply does not
  establish that a mutation rolled back (delayed-result tests). Owner: Settings
  operation outcome; inspect once, refresh, never auto-replay the destructive call.

Final build: `adjust-pins-final-release/payload.json`, production validation disabled,
matching rebuilt native/runtime/Settings payloads, package seals and dependency/async
cleanup verifier pass. Debug build 21 has zero warnings/errors. WinUiShell final
run passes 367/367. Earlier broad bridge invocation without its required coherent
installation was invalid evidence; the supported `Test-WidgetBridgeRuntime.ps1`
run passes all 236. An incompatible combination of popup test flags was corrected
to separate root-attached and detached-popup runs. Uninstall test waits now reacquire
the current loaded details control after asynchronous inspection replaces its frame.

Additional final native evidence: `adjust-pointer-regression/result.json` passes
20 pointer/capture checks, including disconnect, resize cursors, loss of capture,
Escape while held and close while held without changing siblings.
`adjust-native-keyboard-regression-2/result.json` passes 17 checks including chooser
hover/keyboard preview ownership, handled/native text Escape, physical Return and
pin-origin combined edit Save/Cancel. The old test's remaining per-pin menu steps
were replaced with Ctrl+Shift+I to match the accepted direct interaction flow.

`adjust-release-smoke/result.json` passes 7 checks on the actual trimmed production
candidate and matching published runtime: installed Discord passive pin, direct
adjacent toolbar, opacity/Cancel, exact Close, voice click-to-interact, physical
Return collapse and inactive right-click adjustment. Exit code 0; release toolbar
raster inspected. WinApp's click helper pre-activates the main HWND and therefore
retires Return before the click; this boundary is tested using the observed UIA
rectangle and a direct owned-process mouse gesture with per-monitor DPI awareness,
without pre-activation. Test-driver capture/coordinate mistakes were corrected;
no production workaround was added. Final Debug build 22 is clean; only validation
files changed after the production build. No new runtime crash dump was produced
by the accepted final native/release runs.

Normal-profile deployment is staged under `artifacts/input-discord/adjust-pins-candidate`
with the previous Settings-only host catalog, the user's unchanged SettingsRoot and
InstalledCatalogRoot, and rebuilt/sealed host runtimes. The frontend and DLL hashes,
launch PID and profile path are recorded in its `installed.json`. No commit/merge
is performed for this goal. Live Discord/YouTube provider behavior and a physical
monitor-unplug test remain unverified; the shared host input, media fixture,
monitor-placement and topology/cancellation policy checks are the evidence here.

Deployment completed: normal-profile frontend 44996, matching Bridge 43244 and
installed Discord 0.1.0.18 worker 46004 are running. Both the host Manage pins
control and Discord Open Discord header control were observed after startup.
`adjust-pins-installed.json` and the prior candidate's installed pointer now refer
to the new candidate; the prior pointer is retained as `installed.before-adjust.json`.
The current goal is complete. Existing accepted/unrelated worktree changes remain
intact and no new merge was performed.

### Rail button placement follow-up (2026-11-14)

Moved Manage pins and conditional Return immediately to the right of Task Switcher.
`OverlayShellPage.xaml` now groups the existing status host and pin commands;
`OverlayShellPage.ProductionLayout.cs` measures that group on the configured status
side while keeping compact pin access and reclaiming the hidden Return width.
This is shared host chrome: all widget/browser/media consumers inherit the position;
SDKs, templates, authored pin layouts and banner lifecycle require no changes.
Native `rail-buttons-native` passes 74 checks, compact `rail-buttons-compact` passes
53 at 420/480/900 widths, and the trimmed `rail-buttons-smoke` passes 8 including
explicit left-to-right bounds and physical Return activation. Rail screenshot
inspected. Production dependency/cleanup verifier passed; updated frontend is
`artifacts/input-discord/rail-pin-buttons-release`, with normal-profile deployment
record in `rail-buttons-installed.json`.

### Adjustment outside-click correction (2026-11-14)

Native reproduction (`outside-pin-repro/result.json`) confirms a click on the
still-visible main widget dismisses the overlay during adjustment. Source cause:
`OverlayShellPage.PinnedPlacement.cs` and `Radial.cs` revoke widget/tray/wheel hit
testing while the preview owns input. `ProductionGapPressed` then sees the root
as the event source and incorrectly treats that content as empty backdrop.

Host fix: `TryReturnFromAdjustment` classifies the host's explicit visible content
bounds before backdrop dismissal. A click on the widget, rail controls or retained
wheel cancels the preview and keeps the overlay visible. Host pointer capture consumes
the complete gesture so newly enabled controls receive neither press nor orphan
release. Cancel/hide/capture-loss clear the temporary capture; an already committed
save remains atomic. Empty-backdrop dismissal is unchanged. This is shared shell
input ownership, not an SDK gap or a widget workaround; no public contract changes.

| Consumer | Applicability / evidence |
| --- | --- |
| Authored/native/passive pins, main widget/tray/wheel | Fixed centrally. `outside-pin-fixed` passes 80 native checks, including six new physical outside-click, full-preview rollback and backdrop checks. Existing banner and stable-order checks also pass. |
| Browser/webapp pins | Same host entry path; `outside-pin-browser` passes 27 with the same physical checks and same live document retained. |
| Host media / YouTube route | `outside-pin-media` passes 59 production embedded-media sample checks, including physical outside clicks and preserved playback ownership. Live provider playback not claimed. |
| Discord and other samples/first-party widgets | Already use shared host adjustment, no consumer-side input changes required. Installed synthetic Discord UI is exercised in the release smoke. |
| Banners, SDK and templates | No new targeting or API behavior; existing passive banner behavior remains covered in the native suite. |

Debug build is clean; production `outside-pin-release` passes the published
dependency and asynchronous cleanup verifier. The normal-profile startup and
current binaries are recorded in `outside-pin-installed.json` after deployment.

### Right-click adjustment guide restoration (2026-11-14)

Native reproduction in `pin-guide-repro/result.json` confirms that mouse Cancel
leaves Move/Resize/Save/Cancel displayed with widget guide readiness still false.
`OverlayShellPage.PinPointer.cs` incorrectly set the global Manage pins launcher
return flag for a direct right-click. `RestorePinManagementAdjustmentFocus` then
focused that launcher and skipped normal retained widget/rail/wheel restoration.
The shared guide correctly retained its last publication while widget focus was
pending; refreshing again or weakening guide readiness would mask the owner bug.

The host's direct-pointer entry now leaves that launcher flag false. Save/Cancel
use the existing ordinary-context restoration, matching the source of the action.
The actual Manage pins launcher retains its own return-focus behavior. SDK callers,
Discord/YouTube authoring, templates, browser/media lifetimes and banner input do
not need changes; all pin types share this host entry path. No guide cache, timer,
widget focus counter or protocol addition was introduced.

`pin-guide-smoke/result.json` passes both physical mouse Cancel and Save after
right-click in the installed synthetic Discord UI on the trimmed production build,
without a controller pump. The exact prior guide (Unmute, Options, Servers & channels,
Select, Back, Close) returns; normal shutdown exits 0. Debug build is clean and
`pin-guide-release` passes the production dependency/cleanup verifier. Native
coverage additionally compares guides from widget, tray and wheel contexts and
retains the full adjustment/banner/order regressions. Fixture setup waits for the
tray's declared quick action and opens the wheel through its normal host command
before capturing the baseline, avoiding comparison against an earlier UI context.

Final `pin-guide-final/result.json` passes all 86 native checks. Guide equality
uses a validation-only snapshot of the complete published hints, rather than the
temporarily width-limited visible subset; the production guide implementation is
unchanged. Both mouse completion routes pass in all three originating contexts,
along with existing controller, pointer, banner and stable-order checks. The
normal-profile deployment record is `pin-guide-installed.json`.

### Built-in catalog repair and Settings icons (2026-11-14)

Corrects the earlier decision to preserve the candidate's Settings-only host catalog.
That development configuration was inappropriate once the candidate contained the
full production runtime: `SettingsWidget.DiscoverBundledManifests` discovered all
runtime manifests, while `widget-catalog.json` had an empty `bundledWidgets` list.
Settings therefore labelled active installed first-party packages as unused copies;
removing those copies legitimately removed the only active tray registrations.
`BridgeCatalogIcons.ResolveAsync` correctly refused built-in SVG requests for packages
absent from the trusted host catalog, leaving generic glyphs. Both failures were
caused by the candidate deployment mismatch, not deletion of the packaged built-ins.

Restored the matching production catalog from `adjust-pins-final-release/build`
to the normal-profile candidate, after checking each packaged manifest identity and
seal exists. The old catalog is retained as `widget-catalog.before-builtin-repair.json`.
All eight production built-ins reappeared: Catalogue, Capture Library, Performance
Monitor, Now Playing, Games & Apps, Audio Mixer, Network Controls and Power. Existing
user settings and installed-catalog state were not rewritten; deleted duplicate
packages were not reinstalled. This restores available built-ins, not deleted
duplicate-specific local data.

`builtin-repair-native/builtins.png` visually confirms the native Settings rows now
render the actual package SVGs (colored Audio Mixer, camera Capture Library, blue
Games & Apps). The isolated production frontend uses the repaired installation and
exits normally. `builtin-copy-regression-test.log` passes an extended real catalog/
bridge regression: exact uninstall removes an installed duplicate, the configured
built-in remains with the same package root, and its SVG hash is unchanged. The
existing disabled-package, stale identity and tampered-icon checks remain intact.

Added `scripts/Test-WinUiCandidateCatalog.ps1` and wired it into production publication
and Settings-inclusive bundled workspaces. It compares the actual Settings discovery
root with declared host package roots. It accepts the repaired 9-package configuration
(8 built-ins plus Settings) and rejects the saved faulty catalog. Run this guard after
any candidate-specific catalog override, not only before staging. The previous manual
override is rejected and is not a deployment policy to preserve on future updates.

Consumer/ownership assessment: all built-ins and Settings permissions/icon/unused-copy
views are affected by catalog consistency; installed add-ons, pin SDKs, templates and
banners require no changes. Generic SVG fallback remains necessary and was not weakened.
Broader inferred architecture risk: Settings infers inventory from disk rather than a
host-authoritative configured inventory including disabled packages. A future trusted
inventory API belongs to the host/diagnostics contract with Settings adopting it;
complexity moderate, precedent `BridgeCatalogIcons.LoadTrustedForManagement`. Deferred
here because restoring and validating the coherent deployment fixes the reproduced
failures without expanding this repair into a framework inventory redesign.

Repaired normal-profile launch is recorded in `builtin-repair-installed.json`; tray
startup was observed after restart. No user package was uninstalled during testing.

### Implemented — remembered Discord voice pins (2026-11-14)

User requests independent memory for voice controls and voice participants, not
server-specific: leaving voice closes both windows; rejoining restores only the
previously selected ones. Explicit unpin disables restoration for that layout.
Shared-screen pins are excluded. Clarified that YouTube Music and other ordinary
pins remain session-only: this is an opt-in declaration, not global pin restoration.
Discord must be running and report an active call; the feature never joins a call
or starts watching media on its own.

Ownership finding: `WidgetPinningService.RequestOpenAsync` is intentionally admitted
only from a user action. Calling it from Discord's polling task would violate the
supported lifecycle/authority contract. The host already owns user selection,
capacity, removal on layout withdrawal, geometry and native focus. The reusable
addition is `PinnedPresentationLayout.RestoreWhenAvailable` (protocol 109, independent
layouts only), with persisted host-owned user choice. Discord declares this only
on its two voice layouts and withdraws them while disconnected. No widget-managed
selection generations, delayed synthetic input or background pin requests.

Remembered preferences store widget/layout/instance and verified package/publisher
authority separately from positions in the existing bounded atomic preference file.
Unsigned publisher authority includes the content digest, so replacement package
bytes do not inherit this user choice silently. Restarts of the same installation
retain it. Host reconciliation observes existing published snapshots; it does not
eagerly start workers. It creates passive windows without taking foreground, defers
at six pins and during host interactions, never evicts another pin or opens a capacity
chooser, and avoids retrying failures every poll. Explicit close/replacement clears
the choice; automatic layout withdrawal, worker retirement and app shutdown retain it.

Consumer applicability: Discord voice layouts adopt the opt-in; Discord stream
layouts, YouTube/YouTube Music, Gallery, other samples, first-party widgets and
templates retain the default false behavior. Browser/media popup pins and banners
do not opt in. All existing pin request APIs remain action-bound. Bridge descriptors
gain optional verified package/publisher identity for preference isolation; old
consumers can ignore them. Native contract constants are regenerated for protocol 109.

Final checks: Discord 492, SDK 177, WinUiShell 368, presentation session 367 and
coherent WidgetBridge 236 checks pass. Protocol parity verifies 195 constants;
production verification passes 302 managed references and async cleanup checks.
The six-pin native regression passes 52 checks. `voice-memory-run-final` passes
11 lifecycle checks and 2 fresh-process restoration checks, including full-capacity
deferral, adjustment deferral, hidden-overlay restoration without focus theft,
explicit unpin and independence from stream pins. The fixture join driver uses
an actual click on the realized channel row; earlier synthetic input attempts
were unreliable and are not used as final evidence.

`voice-memory-production-smoke/result.json` passes five flows through the trimmed
production host and actual Discord widget with a synthetic provider: startup,
leave, rejoin and independent explicit unpin of both voice layouts. The three-stream
regression passes with three independently advancing tracks and zero reported drops.
This is native fixture evidence, not a live Discord call test.

Discord 0.1.0.19 is selected and enabled through the supported local update command,
preserving settings and matching permission decisions; 0.1.0.18 remains available
for rollback. The matching host uses the complete production built-in catalog,
validated by the nine-package candidate guard. Users pin either voice layout once
to opt in; historical geometry is not treated as remembered intent. Unsigned package
updates change verified publisher authority and therefore require opting in again.

### Radial return: steady Manage pins chrome

Source-confirmed host issue: `OverlayShellPage.PinManagement.cs` tied the rail
button's enabled appearance to `switching`. Radial B returns through the ordinary
widget admission transaction, briefly disabling and recoloring this unrelated
button. Keep its availability appearance independent of that transient flag;
the click handler and both keyboard accelerators still reject entry while switching.
Persistent unavailable states (including no open pins) retain disabled styling.

Consumer matrix: the shared host rail is fixed for all widgets and webapps.
Discord, YouTube and EmbeddedMedia widget-authored management buttons use the
existing SDK request route and require no changes. First-party widgets, templates,
SDK contracts and banners are not changed by this rail-only presentation fix.

Native evidence: `radial-pin-chrome-native-2` passes 18 lifecycle and 2 restart
checks. A dependency-property observer sees no disabled transition during three
actual controller B route cycles with voice pins open; attempts to open management
while admission is pending are rejected. The existing leave/rejoin, capacity,
adjustment deferral and explicit-unpin checks also pass. The first test launch
failed before validation; a fresh Debug publication runs successfully.
The trimmed Release publication passes the dependency/async-cleanup verifier and
is running on the normal profile (`radial-pin-chrome-installed.json`), with
Discord 0.1.0.19 and the existing complete built-in catalog. Startup UIA confirms
the rail control, Audio Mixer and Discord view are present.

### Radial SVG continuity and deferred edge-line investigation

Reproduced host cause in `RadialChooserView.Update` / `WidgetNativePackageIcon.Update`:
eight reused sector views discarded admitted images on every page change and
published a semantic fallback before each asynchronous load. The radial now
retains up to 24 icon views (three pages), lazily admitted and least-recently-used
evicted, with removed catalog entries disposed. Update still validates full package,
instance, runtime/presentation and asset identity before retaining artwork.
Reordering detaches outgoing sectors before attaching incoming ones, including
before XAML has set Parent/IsLoaded. Cached revisits reuse already prepared visuals;
first loads reserve empty icon space and show semantic fallback only on failure.
No additional widget workers or page loads are started for prefetching.

Ownership/applicability: host radial chrome is affected for all packaged widgets,
including first-party widgets. The internal `ShowPendingFallback` policy defaults
to true: ordinary tray items, widget controls, select options, notifications and
opening indicators retain their existing behavior. Shipping Discord, YouTube,
EmbeddedMedia, Gallery, first-party consumers and SDK/templates need no migration;
no protocol or SDK contract changes. Native user-webapp imagery uses its existing
separate authority/loading path; this change does not claim to suppress its first-load
fallback, although retained radial views avoid recreating it on page revisits.

`icon-edge-suite-result-4.json` passes 88 native checks, including pending and failed
loads, synchronous cached revisits, package replacement, catalog reorder/removal,
24-view retention, shared tint/theme/raster budgets and stale response rejection.
The test reads the requested controls' actual content because IsLoaded/visual-tree
enumeration trails logical reparenting. Normal native teardown exits successfully.

The reported bottom/side line is not authored in Discord's headphone SVG. Native
standalone sizes 12–40 DIPs, scaled buttons and actual main Discord Deafen controls
using Velvet Dusk at 90% and 50% scale did not reproduce it. Evidence includes
`icon-edge-scaled-buttons.png`, `icon-edge-discord-settled.png` and
`icon-edge-discord-small.png`. User asked for one final attempt and then deferral:
the edge-line cause remains unconfirmed and no raster/tint/SVG changes are shipped.
The temporary edge diagnostic page was removed after saving its evidence.

### Actionable shortcut recording button

`OverlayShellPage.KeyboardShortcut.cs` previously relabeled Change shortcut to
Press a shortcut while retaining a click handler that merely restarted recording.
It now becomes Cancel recording: clicking stops capture, retains the selected
shortcut (including an unsaved selection), and keeps the editor open. Recording
instructions remain in the existing live status text. Capture and Reset restore
Change shortcut as before; save/cancel and shortcut registration are unchanged.

This is host editor behavior, reached through the existing Settings intent; no
widget/SDK abstraction is missing. Applicability: Settings is the sole production
editor entry point; other widgets, webapps, pin controls, banners and templates
do not own shortcut capture and require no changes.

The trimmed production host passes five native UIA flows in an isolated profile:
cancel stops subsequent capture, restart records Ctrl+Alt+F9, a second cancellation
retains that selection, Reset ends capture, and dialog Cancel preserves saved
controller settings. Evidence: `shortcut-recording-test/result.json` and
`recording.png`. Release dependency and async-cleanup verification also pass.

### Scoped next: independent Playnite local integration

See [the independent integration plan](playnite-local-integration-plan.md).
The user prefers a separate plugin/server identity over extending old Bridge's
same-ID repair path: local API only, no Xbox FSE/remote/backend sync, typed eval
replacements, optional deeper PlayniteAchievements integration, and collision-safe
port selection with profile-specific endpoint discovery. This is planning only;
installed old Bridge 1.5.3.1 remains unchanged. The new-plugin migration supersedes
the proposed narrow 1.5.3.1-to-1.5.3.2 automatic upgrade as the planned direction.

### Corrected Playnite modal deployment

The current host included the modal fix but the installed Playnite widget 0.2.146
still bundled SDK 0.4.7/protocol 101, confirmed by metadata/reflection: its
WidgetModal had no DismissOnBackdrop property. The host intentionally did not infer
pointer-dismiss authority for old snapshots. Escape closed the live old details
modal during investigation; the reproduced deployment gap was outside-click policy.

Rebuilt and installed 0.2.147 through supported local update with settings transfer.
The package now carries SDK 0.4.8/protocol 109 and default backdrop-dismiss policy.
No shared host workaround, default-policy weakening or added header button was needed.
Native mouse opening of BG3 details, outside left press/release, reopening and Escape
all pass; both dismissal paths restore the library while keeping the overlay open.
Package tests: 99 pass, 5 opt-in live tests skipped. Initial package assertions needed
the published artifact copied into their expected default artifact directory; no
test assertions were weakened. Bridge response-abort fixture passes. The bundled
Bridge package is not installed into Playnite by this widget package update.


### Follow-up: participant audio rich preview (2026-11-15)

User requested replacing the participant mute/volume modal with a hover rich preview.
Existing SDK `FocusPresentationSurface` / `FocusPreviewOptions.EnterButton` supports
interactive content; PoB candidate and gem inspectors are shipping precedents.
No generic widget hover timers or manual focus bookkeeping should be introduced.
Proposed adoption: participant-bound hover/focus preview, compact mute/volume controls,
X to enter with controller, B/Escape to return, stable pointer transit and slider drag.
Keep account/session/channel/participant authority and stale toggle intent checks.
The current dialog-specific `VoiceDialog` visit cannot simply be reused for a preview:
separate domain audio authorization from navigation and bind commands to the SDK
preview publication/lease. Verify that state refresh does not collapse an engaged
slider or replace the current hover owner. Native pointer/controller evidence and
participant-leave/account-change delayed-response tests are required before shipping.
Status: API applicability confirmed; implementation pending the active SVG investigation.

### SVG composition boundary fix (2026-11-15)

Reproduced on Playnite's trophy and Discord headphones/leave icons at fractional
scale on the ultrawide. The held-A video isolated appearance after press scaling
settles. Actual live headphone masks had alpha zero on all outer edges. Installed
PDB hashes matched current renderer source. The line is introduced during composition,
not authored in SVG or present in the captured alpha mask.

A diagnostic switch compared rendering on the same live buttons. User confirmed
the underline gone with a hard bitmap boundary and its return with original settings
after A/reopening/restarting. `NativePackageIconTint.cs` now sets the mask SpriteVisual's
BorderMode to Hard; SVG silhouette antialiasing remains in the alpha image. No
per-widget SVG rewriting, padding, opacity change, or extra rasterization is used.
Temporary live-switch and mask-recording hooks were removed. An opt-in native
icon/label/button reproduction fixture remains under `PackageIconValidationPage.Labels.cs`.

Native icon suite: 88 checks passed. Trimmed release dependency/cleanup verification
passed (302 managed references). The normal fixed host is installed; metadata is
`artifacts/input-discord/icon-edge-hard-installed.json`. Diagnostic evidence includes
`svg-edge-video`, `live-icon-masks`, `composition-icon-masks` and
`icon-edge-hard-native.json`. A combined cleanup/restart shell call was rejected by
automatic review with only `blocked by policy`; it did not run. The app was then
closed through its ordinary tray Quit action and the normal fixed build launched.

| Consumer | Applicability / evidence |
|---|---|
| Discord button, inline and participant glyphs | Shared ThemeTint renderer; live ultrawide comparison and controller recording. |
| Playnite glyphs | Same renderer; original trophy reproduction. Tracker redesign is independent of this renderer fix. |
| Built-ins, Settings, other widgets and SDK examples using ThemeTint | Same native class/cache; suite covers tint, alpha, sizing, scoped lifetime, radial continuity and reuse. No widget migration. |
| OriginalColor SVG logos, font glyphs and bitmap artwork | Separate paths, unchanged; original-color sizing checks still pass. |

### Interactive hover-preview transit (2026-11-15)

User reports PoB inventory preview closes while moving into it. Source inspection
(native reproduction still required) identifies a host ownership gap:
`WidgetViewPresenter.Previews.cs` grants 150 ms exit grace only when FindBinding(target)
is null. Movement onto a non-preview binding can update the target and clear the
preview immediately; moving over another row can replace it on the way to the panel.
Popup enter/exit handling alone does not protect that transit path.

Owner: host. Proposed fix: treat trigger, popup and a bounded directional travel
corridor as one hover region; retain current valid authority during transit;
brief close grace outside, cancel on entry; keep open while focused, dragging or
using child menus; preserve Escape/B/outside dismissal and source retirement.
Widgets should not add hover timers. Precedents: Floating UI safePolygon
(https://floating-ui.com/docs/usehover) and W3C hoverable/persistent guidance
(https://www.w3.org/WAI/WCAG21/Understanding/content-on-hover-or-focus.html).

Test slow/diagonal travel, neighboring rows, viewport-edge placement, slider capture,
keyboard/controller return, scrolling, source deletion/query replacement and overlay
teardown. Include PoB item/gem previews and SDK fixtures. Defer Discord participant
preview migration until shared pointer transit is verified; retain its current modal
and stale-action protections in the meantime.


### Participant pin appearance follow-up (2026-11-15)

User supplied a direct comparison with Discord's overlay. Widget-owned row style
now uses a 40-DIP stable avatar frame, 16-DIP Segoe UI name with explicit 1.25 line
height, centered 16-DIP status glyphs, neutral compact pills and 8-DIP row gaps.
Voice controls and passive participants both use ParticipantOverlay; no separate
host layout or per-widget renderer workaround. System contrast still uses the
host's accessibility color override.

The provider previously returned null for accounts without a custom avatar, so
all such participants/friends rendered a generic person. It now builds only the
six documented default CDN URLs from a validated public discriminator/user ID;
custom URL/hash validation remains intact. The domain allowlist accepts exactly
those six paths. Applies to participants, both voice pins and friend activity;
other widgets and generic image rendering are not changed.

Evidence: 512 widget checks and 179 provider tests pass. Native captures at 125%
monitor scale / 90% interface scale show both participant and voice-controls pins.
The old voice-pin test assumed interface scale 100% while resizing: its second
size wait failed, despite visible content fitting. Corrected that fixture's
physical-size calculation to include the pin's interface scale, and added native
avatar/name/pill center and inset assertions. Native suite: 111 checks passed at 125% display / 90% interface scale, including
both sizes, centered content and controller management. This update
is not installed yet; it must ship with the coherent protocol-110 preview changes.


### Shared rich-preview implementation checkpoint (2026-11-15)

Host owns the pointer corridor, one 200-ms exit timer (explicitly stopped and
detached at disposal), focus transfer and exact lease admission. Inline indexed
content refresh now preserves the visible panel/controls while reacquiring the
current row; the old lease never grants input. Native numeric preview testing
found B was intercepted by the parent before the slider could finish adjustment;
the host now routes the nested slider's B/release first. Escape follows that same
order and does not repeatedly dismiss on a held key.

Protocol 110 adds finite/range/step checked RequestedValue to indexed preview
commands. SDK forwards the admitted value; the existing SliderMath contract is
shared with protocol validation. The real Discord participant collection now
uses this API, with call/account/session/membership identity owned by Discord and
fresh provider observation after writes. The former participant audio modal is
removed; device settings retain their existing modal and empty-device handling.

| Consumer | Applicability / migration |
|---|---|
| Discord participants | Migrated to inline indexed interactive preview, local mute and volume; delayed response/account/call/reopen tests pass. Native real-worker/input checks and installed smoke passed; final evidence is recorded below. |
| PoB1 and PoB2 shipping applications (shared PobCompanion) | Existing deferred item/candidate and gem preview API; host transit fix applies without widget timers. Read-only equipment/skill/history summaries also benefit. No provider-specific migration. |
| Marketplace | Existing deferred read-only browse previews; same pointer retention/retirement behavior. No authored plumbing. |
| Playnite Home/Library focused game summaries | Existing read-only inline previews; no migration. New Achievements cards open existing details, not a new preview abstraction. |
| SDK Gallery | Existing inline read-only examples; no new interaction semantics forced on them. |
| Other FirstPartyWidgets, samples and templates | No equivalent rich-preview entry points found in the repository-wide search. Generic controls outside previews retain existing numeric action contract. |

Current evidence: 372 shell policy tests, 177 SDK checks, 512 Discord widget checks,
179 provider tests; native real pointer diagonal transit passed. Native slider
value/acknowledgement retention passed and exposed the B ordering fix above; final
native rerun and coherent protocol-110 deployment remain pending.


Native numeric-preview checkpoint: 20 checks passed with actual foreground
activation (`rich-preview-slider-native-activated/result.json`), including real
pointer travel, typed slider changes, stable controls on acknowledgement and B
finishing slider adjustment before leaving. Additional guide-label and real-worker
numeric cases are now included for the final run. Earlier runs without
`--validation-platform-activation` were rejected by the fixture's foreground guard;
no input was injected into another app.

Playnite modal verification: the production-style native check confirmed an
`AcrylicBrush` with TintOpacity .8, automatic luminosity and opaque text. The modal
layer keeps the existing widget page beneath its dimming scrim; this is in-app
sampling, not desktop acrylic. The reduced-transparency check assumed a single
SolidColorBrush, but the renderer legitimately preserves theme surface shading as
an opaque gradient. The check now accepts either fallback only when all painted
colors and brush opacity are fully opaque. Full native rerun remains due.


### Final native and installed-provider follow-up (2026-11-15)

- Real bridge/worker deferred-preview suite: 14 checks passed, including typed
  volume, cache/context replacement, suspension and read-only focus. The fixture
  now waits for the requested mode publication rather than any late publication.
- Production Playnite modal fixture: 22 checks passed. Acrylic is verified with
  tint opacity .8; reduced transparency accepts the theme's fully opaque gradient.
  The poster assertion now matches the existing 80x120-DIP product style.
- Discord five-participant pin fixture: 116 checks passed, including status/ring
  geometry and the overflow count at the 224-DIP participant minimum height.
- Actual Discord widget preview (synthetic provider through current runtime):
  native F6 enters Mute for me; pointer dragging changes volume 50 to 75 while
  the preview survives the provider acknowledgement; Escape restores Avery.
- Native keyboard testing reproduced an additional shared-host issue: directional
  key routing applied controller engagement requirements to a focused slider.
  Owner: host (`WidgetViewPresenter.DirectionalNavigation.cs`). Left/right from a
  real keyboard now uses the existing slider step/validation/dispatch path directly;
  controller engagement is unchanged. Native regression coverage is being run.

Keyboard-slider applicability: Discord voice/stream sliders, YouTube volume/seek,
YT Music standalone seek/volume, Spotify volume, EmbeddedMedia and SDK Gallery
all use RequireControllerActivation and benefit automatically. Other host-rendered
sliders use the same path; browser-native controls are excluded by the existing
native browser focus guard. No widget-side input counters or duplicate dispatch
were introduced. Templates contain no equivalent manual keyboard slider handling.


### Completion evidence — rich previews, participant appearance and achievements

Completed and applied to the usual profile on 2026-11-15. Host:
`artifacts/input-discord/rich-preview-achievements-release`; coherent bundled
runtime: `rich-preview-achievements-candidate/installation` (all 10 bundled
packages retained, plus the existing user catalog). Running host PID34264;
Discord 0.1.0.20; Playnite widget 0.4.0; WidgetRail for Playnite 1.1.0.
`rich-preview-achievements-candidate/installed.json` records paths/hash/versions.
This is the user's development candidate on their normal profile, not a formal
installer release. No commit, merge or push was performed.

Validation: SDK177, bridge236, WinUI shell372, Playnite widget237, plugin
Framework33, application117 ordinary checks (6 explicit opt-ins skipped),
Discord512 and provider JS179 all passed. The live application opt-in passed118
(5 unrelated opt-ins skipped). Gallery13, Marketplace18 and PoB history-preview12
passed. Native preview23 (including physical keyboard arrows), real-worker
preview14, production Playnite modal22 and Discord pin116 checks passed.
The trimmed/self-contained production verifier passed302 dependency references
and balanced asynchronous cleanup; diff whitespace check passed.

Live final-build checks: the installed PoB2 inventory retained Tempest Edge's
interactive preview during a slow diagonal pointer transit to its Select item
button; F6 entered and Escape returned without changing the equipped build.
Discord's real Test server participant preview admitted F6/Tab, keyboard volume
and retained the slider after acknowledgements. Tested volume was restored to100
and recorded by the actual focused native RangeValuePattern. The existing muted
state was preserved. The participant pin was restored, now showing both correct
Discord default-avatar variants, centered white names and neutral compact pills.
Playnite's original 007 First Light / A collector’s habit tracker was restored
with the actual achievement artwork and13/92 progress. The new Achievements
page is left selected with the search cleared. Captures and native UI evidence:
`artifacts/input-discord/rich-preview-achievements-candidate/`.

Boundaries: native fixtures exercised stale-query/removal/suspension and slider
routing; this is not a full engine test of every PoB calculation or a fresh
all-pin/banner regression campaign. Banner notifications do not instantiate
interactive focus previews; no banner rendering/lifecycle changes were made.
Live gameplay achievement increments were not generated. Provider event and
cache-refresh integration is covered; freshness still depends on the add-on/game.
The native UIA inspector does not enumerate preview portal descendants through
its ordinary window-tree search, although the focused controls expose their
correct automation IDs, names and patterns. Existing host portal accessibility
should receive a separate review; this did not block physical mouse/keyboard
interaction and was not expanded into an accessibility-tree rewrite here.


### Reopened: Discord rich preview lifetime during live updates (2026-11-15)

User reproduced dismissal to Refresh using mouse slider dragging, mute/unmute and
controller input; re-hover could revive a drag without a button held. Reproduced
in the actual Discord Test server under a diagnostic host. At the failing
`preview-clear:PreviewLayoutChanged`, native snapshot66 still declared participant
query3/content0, while the session had retired that row before the replacement
publication reached the UI dispatcher. Anchor remained loaded, presentation active,
parent authority current, no transition or resize. Evidence:
`artifacts/input-discord/participant-live-trace/focus.log` (bounded diagnostic input
identities; no tokens). Static provider updates alone did not reproduce this timing.

Owner: host, not Discord. `WidgetIndexedCollectionView.Previews.cs` previously
retained paint only after the native collection knew its newer content revision.
The pending-dispatch interval was uncovered. `WidgetIndexedRows.HasPendingPresentation`
now checks for a newer accepted frame of the same runtime/session owner. The current
native row may keep paint during that interval; action admission still requires its
exact current lease, and the new native declaration applies ordinary query/scope/
removal retirement. No sleep, widget polling workaround or relaxed input authority.
Existing retained-background paint in `WidgetViewPresenter.Surfaces.cs` is precedent
for separating paint continuity from input authority. Complexity: bounded host fix,
no SDK/protocol change. Verify before considering complete.

Preview teardown additionally releases native pointer captures before detaching the
retained fragment. This targets the stale Slider/Thumb drag state rather than
inventing mouse/controller flags in widgets. Native regression now covers physical
mute clicks, held slider drag, controller change, pending publication and interrupted
drag/reopen. Its fixture must wait for visible geometry (full-motion startup exposed
a fixture-only early pointer target); prior failure using a wrong pointer helper was
not a product reproduction. Tests and normal-profile fixed host still pending.

Consumer applicability: Discord inline indexed audio preview affected; PoB1/PoB2
indexed interactive previews use the same host path and require smoke verification;
Marketplace indexed read-only previews and Playnite indexed read-only summaries
inherit paint continuity. SDK Gallery/static previews do not have indexed row leases
but share pointer-capture teardown. Browser/video pins and notification banners do
not use this indexed preview handoff; their rendering is unchanged. Real source or
scope removal, runtime replacement and stale actions must continue to revoke input.


Verification follow-up: the actual live Test server reproduction now retains the
same mute button after its acknowledgement, and retains the actual slider during a
held physical mouse drag across updates. Unpressed motion after release leaves its
value unchanged; the user's pre-test volume30 was restored. The initial local-mute
state was restored as well. `participant-live-trace/live-drag-result.json` and the
focus snapshots record this. The final synthetic real-worker native scenario passes
8 checks and deliberately schedules a high-priority layout evaluation during the
lease-retirement/UI-commit gap. The generic preview native suite passes26 checks,
including removing the source during a real captured slider drag, capture release,
reopening, and subsequent unpressed mouse motion. Existing stale admission, source
removal, selector/B/Escape and keyboard cases remain passing. WinUI shell372 pass.

Two fixture corrections are explicit: controller preview entry first focuses its
source (hover alone is not controller intent); the standalone preview includes all
four controls in a320-DIP viewport. In-process UIA geometry can be empty for a portal
descendant, so the drag probe uses its XAML-root transform and asserts the physical
PointerPressed event reaches the intended slider before testing capture. These are
test harness fixes, not changes to input policy or weakened product assertions.

The Playnite high-contrast visual check found an existing host palette mismatch:
`OverlaySurfacePaint.Panel` used UIColorType.Background while native text used
SystemColorWindowTextColor. With in-app high contrast this produced black text on
black outside cards. Fix: reuse `ShellChromePalette.WindowColors().Background`, the
existing paired palette used by dialog and banner chrome. Main surfaces and all pin
appearance fallbacks are affected; banner chrome is already compliant, and browser
page-owned colors are outside this host fill. Native regression checks the fallback
against substituted Windows resources. No theme or widget-specific palette workaround.
Final high-contrast and release verification, installation and final screenshots are
being completed; previous release verifier already passed302 managed references.


Completed/applied: final normal-profile release runs from
`artifacts/input-discord/participant-preview-release`, PID47080; provenance/hash in
`preview-handoff-installed.json`. No validation flag or focus-trace config is used
by the running release. Matching existing protocol110 runtime is unchanged.
Native rich-preview suite26, forced real-worker handoff8, high-contrast palette4,
WinUI shell372 checks pass. Release verifier passes302 managed references and
balanced asynchronous cleanup; whitespace validation passes. Live production smoke
retains Discord slider focus through an update and restores the pre-test volume.
PoB2's installed inventory preview enters Select item with F6 and returns with Escape;
no build/equipment mutation was performed. Native UI/test evidence is in
`participant-live-trace`, `preview-capture-final`, `participant-appearance-native`
and `surface-contrast-native-result.json`. Reproduced baseline failure files remain
for comparison; the final native result is `mouse-preview-check4.json`.

The high-contrast correction is visually verified with the actual selected fixture
palette (`playnite-ink-validation/high-contrast-corrected.png`); the recent section
heading and all text now use the matching Window background/foreground pair.
The user's Discord participant pin and007 First Light / A collector’s habit tracker
are restored. No changes were committed, merged or pushed.


### Accepted merge qualification — 2026-11-15

The user accepted all completed changes, including Playnite0.4.5's unified
achievement Refresh/Y action. The navigation-panel desktop acrylic experiment
remains reverted; accepted whole-surface/pin/banner desktop materials remain.

Pre-merge cleanup:
- Regenerated the native presentation contract for protocol110, including the
  interactive-preview value contract. The parity gate caught the stale artifact.
- Recorded the reviewed additive RoutePreviewValueAction test-helper API in the
  SDK baseline; no existing API was removed or changed by this cleanup.
- Corrected Game Help's capture-preview protocol assertion to ModalDismissalVersion
  and asserted an actual dismissal declaration. The same failure reproduced on
  unchanged main before correction; the product code did not change.
- Updated current-platform-status.md to the accepted protocol and package behavior.

All84 managed verification steps and7 native steps passed across the recorded
resumed runs. Each failure above was corrected and its suite rerun successfully.
Native verification built the extracted boundaries, ran policy probes, published
and verified the trimmed Release frontend, and ran236 bridge checks against a
freshly sealed bundled installation. Evidence is under artifacts/input-discord/
merge-verification and merge-native-verification; merge-qualified-summary.json
maps every step to its latest passing run. The normal installed overlay was not
replaced by these qualification artifacts. Earlier native interaction evidence and
consumer matrices above remain applicable; no new UI behavior was introduced in cleanup.

The reported Discord webpage freeze remains unconfirmed: the user restarted the
worker at17:22:13 local time; no browser-failure event was recorded and detailed
request tracing was off. No speculative recovery change is included in this merge.

### Desktop acrylic bases and independent effects preference — completed

The user clarified that the reverted navigation panels became fully transparent
during transitions. Treat that as a rendering defect requiring native evidence,
not merely a style preference. This pass does not restore the nested-panel
experiment: four shipping widgets use the existing host-owned base surface.

Consumer matrix:
- Audio Mixer, Network Controls, Settings and Catalogue: declare DesktopAcrylic
  on their shared surface hints; clear only their root fills. Cards/controls retain
  theme paint. Their package versions advance to0.1.8/0.1.10/0.1.13/0.1.2.
- Discord main/voice pin, Playnite Achievements, SDK Gallery acrylic pin:
  already opt into desktop material; adopt the shared effects policy automatically.
- Transparent participant/video/media surfaces: preserved by the acrylic-only
  switch. Existing high-contrast/reduced-transparency policy retains precedence.
- In-app WRSS panels/modal bodies and rich-preview/introduction/utility frames:
  shared host paint paths disable their acrylic brush and use plain fallback fill.
- Banner notifications: shared native material disconnects; category opacity and
  foreground opacity keep their existing meanings.
- Other shipping widgets, templates, examples and browser-owned CSS: no forced
  material migration. Host-owned declared effects receive policy; web content's
  own rendering is not rewritten.

Ownership: AppearanceSettings.AcrylicEffects is an additive persisted boolean,
defaulting true for old/missing files. SettingsPreferencePolicy owns the explicit
user mutation. OverlayAppearancePolicy owns desktop fallback; native style/preview
paint and NotificationBannerChrome own effect lifetime. No new SDK/protocol contract.
No widget-managed animation, lifecycle counter or visual-tree rewriting is added.

Verification includes preference roundtrip/independence, actual Settings toggle,
desktop/pinned/banner target detachment, in-app/preview brush removal, and a native
screen-pixel probe over a bright isolated fixture window while real widget bases
switch and resize. Pixel evidence must accompany attachment/state checks because
an attached backdrop alone cannot establish visible rendering during animation.
Completed and applied: the normal profile runs artifacts/acrylic-base/release with
candidate-final/installation; process/config/hash are recorded in installed.json.
All four widget bases were visually checked. The Settings help text is only
"Disable background blur to reduce graphics work." as requested.

Eight targeted managed verification steps pass (settings/storage, four widget
consumers, shell policy and styling). Native checks pass:18 desktop/pin/banner,
32 in-app/preview,4 actual Settings-toggle checks, and12 base-transition checks on
each display. The base screen-pixel probe sampled497 frames at2560px and497 at
5120px; the ultrawide used90% interface size and125% raster scaling. The bright
underlying-window reference was distinguishable from acrylic throughout switching
and resize. This is bounded transition evidence, not a universal GPU/performance
claim or a retrospective diagnosis of the reverted navigation-panel defect.
The Release verifier passes302 local managed references and balanced async cleanup.

The full preexisting Settings appearance fixture times out on focus-animation
Option.none in both the previous build and this build. This is recorded separately
from the passing focused acrylic-control scenario; its assertions were not weakened.
The first ultrawide fixture attempt exposed a WinRT IEnumerable projection failure
in test-only monitor enumeration; indexed vector access fixed the fixture. It was
not a product-rendering failure.

Evidence: artifacts/acrylic-base/managed, desktop-result.json, in-app-native.json,
settings-toggle-native/result.json, base-native/result.json,
wide-native-final/result.json, and installed-settings.png. Transparent media and
participant policies remain unchanged; all material toggles use shared host paths.
No new changes have been committed or merged in this pass.

### Remaining widget base-material audit — proposed, not implemented

Source audit of shipping catalogue entries and production widgets, including the
PoE1/PoE2 application entry points (both use PobCompanion/PobWidget) and YouTube
Music's standalone application. Exclude legacy DiscordWidget, Clock, SDK Gallery,
EmbeddedMedia and FullApplication reference fixtures from the rollout inventory.

| Consumer | Recommendation and boundary | Source evidence |
|---|---|---|
| Power | Desktop acrylic base for choices and confirmation state; retain option cards. | PowerWidget.cs:182; styles/default.wrss .power-widget |
| Display Controls | Desktop acrylic base for profile/control pages; keep profile cards and timed confirmation readable. | DisplayProfilesWidget.cs Render; .display-widget |
| Now Playing | Desktop acrylic base; retain bounded album artwork and transport card fills. | MediaSessionsWidget.cs CompactSurface; .media-sessions-widget |
| Games & Apps | Desktop acrylic base; preserve posters and application cards. Unlike Playnite, this root does not declare game-wallpaper focus backgrounds. | GamesAppsPresentation.cs:43; .games-apps-widget |
| Capture Library | Gallery/settings chrome can use acrylic. Keep image/video inspection on its existing stable background. | CaptureLibraryWidget.cs:238 distinguishes preview/detail versus home; .capture-library |
| Spotify | Main browsing/player base and authored Compact/Up Next pin bases are candidates; retain cover art and inner card fills. | SpotifyWidget.Presentation.cs:22/30/38; .spotify-widget/.spotify-pinned-root |
| YouTube Music | Standalone main base and compact music pin are candidates; cover art is bounded content. | StandaloneMusicWidget.Presentation.cs:52; .music-root |
| YouTube Video | Native search/library/settings/player-control shell can use acrylic. Preserve video viewport and the host-owned transparent compact media pin. | YouTubeVideoWidget.cs:240 and Search.cs:341; .youtube-root/.youtube-viewport |
| Game Help | Acrylic outer base, with opaque conversation, reading/code and capture-review surfaces. | GameHelpWidget.Presentation.cs shared Surface; .help-root/.help-conversation/.help-section |
| PoB1 and PoB2 | Acrylic outer workspace is a candidate. Preserve opaque panels/tooltips/tree canvas and clear colored-stat contrast; browser-owned trade content is not rewritten. | PobWidget.cs:221; .pob-root/.pob-panel/.pob-card |
| Performance Monitor | Acrylic main dashboard with opaque graph cards. Defer compact FPS pin until composition cost/readability are measured. | PerformanceMonitorWidget.cs:212; separate _compact declaration:25 |
| Playnite | Achievements already uses desktop acrylic. Retain Home/Library/details cinematic layering; setup/connection screens can be evaluated independently. Progress tracker is a separate review because its transparent host contains a painted card. | PlayniteLibraryPresentation.cs:551/580/612/650; Progress.cs:23 |
| Browser, Twitch, MapGenie | Keep current treatment: their web content occupies the surface, hiding an underlying acrylic base. | Their production UI.WebBrowser roots |
| Discord | Main/voice-control surfaces already migrated. Keep participants and live stream surfaces transparent. | DiscordWebViewWidget.cs:72/74 and Views.cs |
| Audio Mixer, Network Controls, Settings, Catalogue | Already migrated and qualified in the previous section. | Previous native frame/policy evidence |

All proposed base changes can use the existing SDK surface hint and the host's
Acrylic effects policy; no new renderer or nested acrylic mechanism is required.
Root fills currently using var(--surface) must be removed only where they cover
the intended host material. This is widget presentation policy, not a framework
gap. Do not change media/canvas backgrounds merely because they share a root class.
The table is a source-based design recommendation; it does not establish visual
acceptance, performance, or transition correctness for the proposed consumers.

### Expanded native widget bases — implementation/qualification

User authorized remaining recommendations with existing content backgrounds retained.
Work was split into first-party, music/video, and companion groups with isolated
test outputs; default-output package builds were serialized to avoid graph conflicts.

Migrated: Power, Now Playing, Games & Apps, Capture Library gallery/settings,
Performance Monitor main dashboard, Spotify main/Compact/Up Next, shipping standalone
YouTube Music main/compact pin, YouTube Video native shell routes, Game Help,
Display Controls, and PoE1/PoE2 native workspaces/loading/import states.

Preserved: all existing inner reading/card/artwork/media/tree backgrounds;
Capture Library media preview/detail base; compact performance pin; YouTube's
embedded media definition and host-owned transparent video pin; PoB browser modal.
The root-only CSS changes and conditional surface declarations use the existing
DesktopAcrylic API and Acrylic effects policy, with no new host rendering behavior.

Narrowed after implementation inspection: Playnite connection/setup screens remain
unchanged. PlayniteLibraryConnection.cs:130 and Connection.Setup.cs:128 use a
transparent FillAvailable host around a centered max900x620 painted shell
(default.wrss:105-106). A host-base-only migration would paint unused surrounding
space. Retain that working layout until a separate card-sized surface adjustment
is agreed. Home/Library/details cinematic layering, progress pin, and already-acrylic
Achievements are also unchanged.

Managed results: Power 8, Now Playing 31, Games & Apps 94, Capture Library 27, Performance 13;
Spotify 89, standalone YouTube Music 44, YouTube 65; Game Help 48 and Display Controls 25
passed, plus engine-independent PoE1/PoE2 surface/browser fixtures. Original behavior
assertions retained; explicit protocol assertions now account for desktop material
v108. Native nine-base screen sampling passed 1,222 frames at 100% interface size and
1,186 frames at 90% on the 5,120px display, with live effects toggle and hide/reopen.

PoB packaging encountered an existing missing legacy upstream sprite
(src/TreeData/3_19/ascendancy-3.png) in the pinned commit. A narrow explicit
PreparedArtworkPackage build input reuses only verified installed art when the
content-tree seal, engine lock, six rendering contracts, complete tree-version set,
bounded resource checks and copy hashes match. Default fresh generation is unchanged.
Eleven rejection/acceptance checks cover altered data, changed identity and links/
junctions before traversal. PoE1/2 engine archives and 375/46 artwork files match the
previous installation byte-for-byte; no runtime fallback or engine change was added.
Generated Python bytecode remains local and is excluded through .gitignore.

New package versions: Power0.1.2, NowPlaying0.1.4, GamesApps0.1.6, CaptureLibrary0.1.3,
Performance0.1.4, Spotify0.3.97, YouTubeMusic0.3.48, YouTubeVideo0.3.47,
GameHelp0.1.7, DisplayControls0.1.4, PoE1/PoE2 0.1.6.
Installed all seven community updates and the coherent bundled runtime into the
normal profile; preserved each enabled state, including disabled PoE1. Restarted
the existing qualified production host with the new installation root. Verified
installed native views for Game Help, Display Controls (permission-required view),
Spotify (connected devices), YouTube Music (connected library), YouTube Video
(discovery), and PoE2 (saved build, item panels). Captured and reviewed screen-based
images; inner fills and text remained intact. PoE1 remained disabled and was covered
by the shared production surface fixtures, not a normal-profile launch.

This visual pass did not exercise playback, display mutations, cloud chat, or the
full PoB trade/engine suite. The latter still depends on older authored fixtures
absent from the pinned engines. No production host rendering change was needed
for this expansion. Documentation checks passed (249 Markdown files); final diff
review and whitespace checks passed. Evidence lives under the ignored
artifacts/acrylic-expansion directory, including installation metadata, catalog
before/after, native results, and installed UI captures. Accepted for main integration on 2026-11-15.

### Capture Library sizing regression — corrected

The acrylic migration introduced a non-null Surface declaration in gallery,
preview, and settings. OverlaySurfaceSizing.cs:23 uses 1180 x 700 for an absent
hint, whereas an authored hint without dimensions uses 880 x 520. This is a
reproduced widget migration error, not an acrylic compositor size change.
CaptureLibraryWidget.cs now declares its prior 1180 x 700 extent explicitly for
all three routes, retaining the intended material on each. Package 0.1.4 is
installed in the coherent bundled runtime.

Consumer check: Audio Mixer, Network Controls, Settings, Catalogue, Power,
Now Playing, Games & Apps, Performance Monitor, Spotify, YouTube Music,
YouTube Video, Game Help, Display Controls, and PoB already had authored sizing
hints; their migration changed appearance only. Playnite was not migrated.
No shared sizing default changed. The API coupling between optional material and
sizing is a broader source-inferred authoring hazard: a future SDK/protocol design
could provide explicit independent inheritance, with compatibility for consumers
of both defaults. That is deferred rather than changing current defaults globally.

Qualification: all 27 Capture Library checks pass, now asserting preserved size
in gallery, unsaved preview, saved detail, return, and settings. Native comparison
on the same 125% DPI / 90% interface-size monitor measured 989 x 585 physical pixels
before and 1327 x 787 after (rounding of 880 x 520 and 1180 x 700 respectively).
Settings matched the restored gallery extent, and both screenshots were reviewed.
The initial acrylic qualification checked painting/transition continuity but did
not compare pre-migration geometry; these assertions cover that missed regression.
Evidence: artifacts/capture-sizing-fix. Accepted for main integration on 2026-11-15.
