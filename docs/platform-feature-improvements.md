# Platform feature improvements

## Authority and baseline

User-approved goal: improve the platform-feature candidate, prepare and launch a
complete candidate for physical testing. No merge, push, release, or worktree
removal. Preserve unrelated main-checkout changes. Overlay restart is authorized.
Worktree: `platform-feature-roadmap/WidgetRail`, branch
`codex/platform-feature-roadmap`, starting commit `42713764` (clean).
Previous delivery and evidence: `platform-feature-roadmap.md`.

## Required outcome

- [x] Configurable shared capture API: separate image/video options, workflow,
  capability discovery, preparation/combination validation, actual output metadata,
  stop-and-keep versus cancel/discard, general temporary media and streaming access.
- [x] Preserve foreground window chosen after hide/countdown; no window picker or
  foreground switching. No silent quality downgrade.
- [x] Hardware-assisted silent recording with measured supported combinations.
  Audio default off; advertise audio only after reliable implementation/validation.
  Document unavailable hardware-dependent work and continue other features.
- [x] Capture Library persistent settings based on advertised capabilities;
  thumbnail image grid; migrate Library and Game Help to shared capture API.
- [x] Reusable SDK graphs, bounded 60-second histories, FPS/average/1% low and
  frame-time graph from individual frames; system CPU/GPU/memory histories.
  Clear gaps/target changes, compact pin without Automatic; retain PresentMon FPS.
- [x] Dedicated official marketplace widget, Settings entry and installed-widget
  management; shared host-controlled distribution/review authority. No custom repos.
- [x] Settings details/permissions intents and explicit host-owned SDK permission
  dialog, clear outcomes, continuation after approval, no background prompting.
  Adopt in relevant first-party widgets.
- [x] Current documentation, SDK Gallery examples, focused automated gates and
  practical local validation, complete assembled candidate launched for user.

## Sequence and design

1. Performance aggregation + generic graph protocol/SDK/WinUI + widget/Gallery.
2. Capture contracts and host pipeline; capability-backed settings + thumbnails.
3. Shared permission requests and Settings intents; marketplace extraction.
4. Review, focused regressions, complete edition assembly, launch and handoff.

Use existing capability broker and rendering contracts. Collection/read paths must
not request consent, elevate, or install. Slow actions retain controller focus.
Frame statistics operate on individual intervals, not one-second FPS averages.
Graphs are informational, not controller focus stops. Application history resets
on target change, and missing data creates gaps instead of false zeros.
Capture host owns confirmation/countdown/indicators/lifetime/limits; consumers own
review/save/upload. Permissions stay declared and host-authorized.

## Progress and evidence

### Started

- Verified clean worktree and inspected current contracts. PresentMon currently
  keeps a two-second window, capture duration/format is enforced in attachment
  validation, and pinned Automatic simply resets an already automatic selection.
- User physically observed FPS including DLSS-generated frames in one tested game;
  retain that behavior, without claiming universal displayed-frame accuracy.
- Existing preview.29 remains running while source work proceeds.

## Next concrete action

Implementation and local qualification are complete. Production-shaped candidate
04 is launched in the normal profile for the user's physical acceptance. Follow
`platform-feature-acceptance.md`; do not merge, push, publish, install an app update,
or remove worktrees. Additional GitHub repositories remain deferred. The historical
checkpoints below record intermediate states; the final handoff supersedes them.

### Performance and graphs implementation checkpoint

- Implemented protocol-78 LineGraph, SDK element, bounded/finite sample validation,
  incremental paint/accessibility updates, native path rendering with missing-data
  gaps, accessible summaries and theme foreground inheritance. Added Gallery demo
  and `reference/line-graphs.md`. No preview chart library or WebView dependency.
- Performance main view adds average FPS, exact slowest-1% interval low (minimum
  1,000 frames/five seconds), worst recent frame graph, and system usage graphs.
  UI graphs keep at most sixty seconds, insert gaps, and reset application traces
  on target switches. Pin has no Automatic button or focusable controls.
- Telemetry keeps original two-second FPS computation, plus bounded individual
  intervals for up to sixty seconds: 65,536 per stream / 262,144 globally. Pooled
  sorting buffers and queue capacity trimming bound sustained allocation. Summary
  propagates through validated collector protocol v2, broker and SDK. No raw
  interval trace crosses the collector pipe or gets persisted.
- Tests in `artifacts/platform-improvements`: PerformanceMonitorWidget 10/10
  (`PerformanceMonitorWidget.Tests-final-01.log`); telemetry 9/9
  (`PerformanceTelemetry.Tests-final-01.log`); Gallery 13/13
  (`SdkGalleryWidget.Tests-final-02.log`); SDK 153/153
  (`WidgetSdk.Tests-final-02.log`). Reviewed additive API baseline regenerated,
  3604 symbols. API compatibility subsequently passed 17/17.
- Initial widget checks caught pin minimum height below 180 and an immediate
  target-change sample suppressed by refresh throttling; fixed. Gallery expected
  protocol 64 on its controls page; updated to 78 because the new graph is there.
- Debug frontend built successfully (`frontend-debug-build-02.log`). Release with
  all debug validation enabled fails pre-existing validation IL2026 usage; use
  Debug for those fixtures, then normal trimmed release for shipping validation.
- Native graph fixture added (`--validate-line-graph=<absolute-result-path>`),
  including explicit Program validation admission. First launch incorrectly used
  separate CLI arguments instead of the repository's `--name=value` convention;
  it showed the expected raw-build installation error, not a graph test result.
  Closed that fixture through the existing WM_CLOSE helper; no production profile
  or installed app was changed. Last source review also excluded the graph from
  presenter's generic hit-testing enablement; rebuilt native checks passed.

### Final performance validation and review

- A transient Windows execution delay cleared without restarting jobs. Debug build
  and compatibility tests completed successfully after about four minutes; no
  build/test job remains pending from that delay.
- Native graph fixture then exposed erased intrinsic height: the generic size
  adapter replaced the graph's default height with Auto. Fixed in the shared size
  projection, preserving explicit authored size and an 80-DIP default minimum.
- Native graph 10/10 (`native-graph-03.json`): gaps, actual geometry bounds,
  resizing, retained identity, focus, accessible values, brush inheritance and
  empty state. Debug frontend built with zero warnings/errors. Fixture closed
  normally after screenshot `graph-native-03.png`; no overlay remains running.
- Normal Release frontend build with validation excluded passed with zero
  warnings/errors (`frontend-release-final-01.log`). Full trimmed publication
  still belongs to final candidate assembly.
- Added an incremental graph serialization/materialization regression and negative
  automatic axis check; SDK now 154/154 (`sdk-graphs-final-03.log`).
- WinUI review addressed intrinsic size, hit testing, trim/AOT partial class,
  semantic brush propagation, bounded geometry, accessible data, and stable
  presenter identity. Explicit per-widget/target histories preserve other widgets'
  selection independence; collector frame summary is per selected process stream.
- No completion/block status has been set; all other goal sections remain required.

### Capture investigation for next phase

Current flow: `WindowCaptureCapabilityDomain` → `WindowCaptureRegistry` → Bridge
take/current/recording/completion → `OverlayShellPage.Capture` → native WGC/MF.
The registry currently gives a two-minute request lease, and frontend cancellation
also stops after two minutes. Both must become consistent with negotiated duration.
`BridgeCaptureCurrent` is only a bool; stop-and-keep needs a distinct signal from
cancel so the encoder can finalize successfully. Confirmation, countdown and
recording-indicator strings currently hard-code Game Help/five seconds.

Native recorder stages every BGRA frame through CPU memory; hardware encoding
requires replacing that video path, not merely advertising an encoder flag.
Also found a source-size guard (8192 per axis / 20M pixels) that contradicts the
later original-image geometry limit (16,384 / 33,554,432). Unify it during capture
refactoring and validate the actual entry point, not just the geometry helper.

## Physical acceptance / deferred evidence

All new controller layouts, real-game overhead, FPS behavior across games,
recording/audio synchronization, HDR, and actual installer recovery remain physical
checks until explicitly exercised. Existing automated evidence is not acceptance
of these new changes. No scope item is complete solely because it is planned.

## Configurable capture checkpoint

- Added screenshot/recording/preparation options, capability discovery and exact
  combination validation, start/stop operations, actual output metadata, and
  streaming temporary-media reads. Configured metadata requires protocol 79;
  legacy operations/constructors and attachment bounds remain unchanged.
- Broker rechecks advertised support at admission, scopes stop to the requester,
  distinguishes stop-and-retain from cancellation, extends request leases for
  preparation/duration, and verifies exact expected output dimensions/encoder.
  No window selection or activation APIs were added.
- Frontend uses demand-driven capability publication (see hardware checkpoint), dynamic
  confirmation/countdown text, and reopening finishes configured recordings.
  During preparation it cancels. Source identity stays fixed after capture starts.
- Native v2 ABI accepts dimensions, frame rate, bitrate, duration, cursor and byte
  limits. Writer backpressure replaces the unbounded-queue setting; missed slots
  are reported. Current advertised video envelope remains software 720p/15fps.
  This initial checkpoint advertised software only; the hardware checkpoint below
  supersedes that envelope. Audio remains unadvertised.
- Unified native source-size limits with original-image geometry. Real native
  tests found inconsistent WGC source size across thread-pool DPI contexts
  (802x491 versus 1444x884 for the same fixture). A scoped native PMv2 DPI context
  fixes this: original and bounded captures now both report 802x491 physical
  source pixels. This was verified using actual WGC, not just geometry helpers.
- Native build `artifacts/platform-improvements/capture-native-02` passes 16 new/
  retained geometry assertions, 15 policy checks, 2021 diagnostic checks. Includes
  the synthetic capture fixture; use a non-fixture native build for final shipping.
- Native scenario `capture-native-test-02/result.json` passed: known PNG pixels,
  legacy clip duration, original/bounded configured images, two-second configured
  recording, stop-and-keep MP4, cancellation/reuse, and existing native player
  playback/focus/scrubbing/expanded-view/retirement checks. Fixture/frontend closed
  normally (PIDs 67044/94348); no overlay remains running.
- Synthetic 640x360/15 software capture: 2115 ms wall time, 109 ms process CPU.
  This is NOT real-game overhead or proof of higher-quality encoding support.
- Game Help migrated to explicit lightweight options: 1920x1080 PNG or five-second
  1280x720/15 clip, five-second delay, 8 MiB. Preview/edit/Send preserved; 44/44 tests
  (`capture-gamehelp-tests-01.log`). Capture Library also uses configured original
  screenshots/default clips; settings/grid work remains. Library tests 17/17
  (`capture-library-api-tests-01.log`).
- Initial core checks: broker capture 4/4 (`capture-broker-tests-01.log`), SDK 155/155
  (`capture-sdk-tests-01.log`), Bridge Release and frontend Debug build clean.
- Final broker suite 71/71 (`capture-broker-final-02.log`); configured output is
  checked against the exact aspect-preserving expected dimensions, not merely
  an upper bound. API compatibility 17/17 (`capture-api-compatibility-tests-01.log`),
  reviewed additive SDK baseline now 3615 symbols. Normal Release frontend builds
  cleanly (`capture-frontend-release-01.log`); no candidate launch/publication yet.
- Source review: native ABI sizes/callback lifetime, per-thread DPI restoration,
  strict option/metadata validation, explicit native encoding modes, byte-budget
  enforcement, owner-scoped stop/revoke, bounded streaming, and retrying capability
  publication after connection failures. No paid provider request or personal
  screen capture was used; all native image/video evidence is the synthetic fixture.
- Capability discovery currently seeds the locally verified software baseline
  after WGC support detection. Next phase must probe installed encoders and exact
  configurations, including software availability, instead of assuming every PC
  has the same Media Foundation codecs. Hardware/audio remain unadvertised.
- Next thumbnail design: expose a bounded host-generated library thumbnail via
  library-read authority, and reuse existing `OnResolveArtworkAsync` + `UI.Artwork`
  for lazy image tiles. Avoid loading full originals into widgets or putting
  encoded images into snapshots. Reuse/extend WindowsCaptureImageCodec for generic
  image renditions. Private preferences should use HostServices.PrivateState.

## Hardware capture checkpoint

- Hardware recording keeps BGRA frames on the GPU, converts to NV12 through the
  D3D11 video processor, and submits DXGI surfaces to Media Foundation. Three
  tracked output surfaces bound queued frames; surfaces return only after the
  encoder releases them. Software recording retains CPU readback.
- Verify the encoder MFT's hardware identity; no silent software fallback. Probe
  the exact pipeline at both bitrate bounds before advertising each profile.
  Probe uses an in-memory MP4 without capturing personal content or writing files.
- Discovery is lazy: app startup and screenshots do not initialize video encoders.
  Host-pipe demand distinguishes image support from full encoding discovery.
  Recordings revalidate and initialize the exact requested output at use time.
- Qualified profiles: software 720p15 (1–6 Mbps); hardware 720p15 (1–6), 1080p30
  (3–12), 1080p60 (6–20), and 2160p30 (12–30). Audio is still None only.
- Game Help chooses an available hardware encoder before submitting its unchanged
  five-second 720p15 / 3 Mbps request, otherwise explicitly chooses software.
  Screenshots do not query video encoders.
- Reserve each capture's byte budget before prompting; concurrent library imports
  cannot consume it. Completion replaces the reservation with actual bytes.
  Cancelled files that cannot yet be deleted remain accounted against the quota.
- Encoder-only tests (`encoding-native-08.log`) encode/decode every profile and
  check frame counts, dimensions, color, ordered timestamps and absent audio.
  Latest native build `encoding-native-10` also passes geometry, policy (15) and
  diagnostic (2021) checks. It includes a fixture and is not the shipping DLL.
- Actual WGC synthetic 3840×2160 animated window scenario
  `hardware-wgc-test-03/result.json` passes original/scaled screenshots, software
  recording, stop/keep, cancel/reuse, all four hardware profiles and player checks.
  Discovery took 1801 ms once. Two-second recordings: software 640×360/15 used
  63 ms process CPU; hardware 720p15/1080p30/1080p60/2160p30 used respectively
  188/250/141/266 ms, with 30/60/120/60 encoded frames and zero missed slots.
  These are synthetic measurements, not in-game GPU/FPS overhead evidence.
- Broker 73/73 (`hardware-broker-final-01.log`), Capture Library 17/17
  (`hardware-library-final-01.log`), Game Help 46/46
  (`gamehelp-hardware-tests-01.log`), real host-pipe discovery 2/2
  (`hardware-bridge-tests-01.log`). Test frontend and fixture were closed normally.
- Next: Capture Library persistent supported-option settings and lazy thumbnail
  grid, then shared permission requests/Settings intents and dedicated marketplace.
  Audio qualification and complete candidate assembly remain outstanding.

## Capture Library settings and thumbnail checkpoint

- Added grouped persistent screenshot/recording/preparation settings through
  private state with revision-checked writes. First-use video prefers hardware
  1080p30; screenshots remain original by default. Existing explicit settings are
  not overwritten when support disappears. Validate exact size/bitrate/duration
  before starting. Reopening can finish recording early. Audio remains None only.
- Added responsive image-thumbnail grid using existing lazy artwork handles.
  Shared SDK GetThumbnailAsync uses library-read permission without creating
  temporary capture references. PNG derivatives are 256×144, <=160 KiB; stream
  in bounded broker chunks so high-entropy images fit transport string limits.
  Host caches derivatives, repairs invalid ones, and deletes them with originals.
  Worker never reads original images for the grid; videos use labeled placeholders.
- Capture Library 19/19 (`library-settings-tests-05.log`): restart persistence,
  unavailable saved quality, pre-capture budget refusal, real PNG decode, cache
  reuse/repair/deletion, noisy multi-chunk thumbnail transport and read revocation.
- Final Library 19/19 (`library-settings-final-01.log`), broker 73/73
  (`library-broker-tests-01.log`), API compatibility 17/17
  (`library-api-checks-01.log`). Reviewed additive baseline has 3617 symbols.
  Builds have zero warnings/errors. Snapshot contracts validate the settings and
  grid, but native layout/controller acceptance belongs to final assembled-candidate
  review. No assembled candidate has been launched with these changes yet.
- Review: cache files are bounded, read-locked and path checked; no original
  eviction, renderer changes, or inline images. Settings saves retain focus and
  commit state only after the host acknowledges; B remains usable while probing.
  Preferences use compare-and-swap revisions and corrupt settings require explicit
  reset. Gallery can still list saved captures if preference loading fails.

## Settings navigation intents checkpoint

- Added standard `widgetrail.settings.widget.open` and
  `widgetrail.settings.permissions.open`, each with bounded packageId payload.
  Settings waits for cold initialization, serializes with its existing operation
  gate, reloads current catalog/permission projections, and selects the exact
  installed/bundled package. Missing packages and mismatched schemas reject.
- Navigation never writes consent or enables/installs a widget. Both mappings
  require ordinary active delivery; there is no custom B-return stack.
- Capture Library provides an Open permissions action on permission failures,
  declared in its manifest and routed through the existing intent system.
- Settings 87/87 (`settings-intents-tests-02.log`), including cold delivery,
  transitions between distinct packages, built-in details, nonexistent target,
  schema mismatch, normal B navigation, and unchanged consent.
- Intent routing 23/23 (`settings-intents-routing-tests-01.log`); Library 20/20
  (`settings-intents-library-tests-01.log`) includes the permission-failure link
  with a valid scoped intent snapshot. No renderer/foreground-routing changes.
- Next is an explicit SDK permission-request service: host-granted channel,
  requester-bound declarations/lifecycle, bounded pending requests, native host
  dialog and clear results, then first-party continuation after approval. Reads
  must never prompt. Marketplace extraction and audio remain separate remaining
  work. Final candidate native layout checks/launch are still outstanding.

### Permission service implementation notes

- Use an intrinsic host-granted service like private state; requesting a permission
  cannot itself require that permission. BrokerWidgetProcessCompanion currently
  grants only PrivateStateV1 to every worker channel. Both full-trust and sandbox
  channels use this companion.
- Separate read-only status from explicit request. Scope all IDs to the requesting
  broker's declared ordinary capabilities. Full-trust approval, UAC and drivers
  are outside this service. Background/visible-only widgets cannot prompt.
- Model bounded requester-owned pending requests, cancellation/expiry and worker
  retirement. Avoid holding the worker input gate while waiting for a native dialog;
  follow the capture ticket/status pattern if needed rather than a long RPC.
- Frontend already has ShowHostChoiceAsync with native controller handling, themed
  popup and focus restoration. Revalidate active visible foreground requester and
  catalog identity before display and consent persistence. Host must own names and
  capability descriptions, not accept them from the worker.
- Return granted/denied/cancelled/unavailable; do not catch a denied read and prompt
  automatically. First-party explicit actions can request and resume their action
  after approval. Implement tests for lifecycle, foreign/undeclared IDs, expiration,
  cancellation/retirement, duplicate/prompt bounds, and grant before continuation.

## Explicit permission service checkpoint

- Added host-granted `HostServices.Permissions.CheckAsync` and `RequestAsync` for
  sandboxed/full-trust workers. Broker-bound declarations, short RPC tickets,
  one active prompt, 16-result bound, two-minute deadline and one-minute retention.
  Reads never prompt; only Interactive requests can create consent. Leaving
  interaction, expiry, cancellation or retirement invalidates the request.
- Host resolves current catalog identity and permission labels, and persists only
  the displayed missing permissions. Grant reconciles the same live broker before
  allowing continuation. No full-trust/UAC/driver approval through this service.
- Extracted shared permission labels/descriptions from Settings to PlatformBroker.
  Capture Library explicit capture/save/manage actions and Game Help context actions
  request access; activation/polling remain silent. SDK Gallery has Check/Request
  access examples. Main-surface native dialog is implemented; pinned-source dialog
  placement/focus needs review before final completion.
- Broker 76/76 (`permissions-broker-tests-03.log`), SDK 156/156
  (`permissions-sdk-tests-01.log`), Game Help 46/46 (`permissions-gamehelp-01.log`),
  Library 20/20 (`permissions-library-tests-01.log`), Settings 87/87
  (`permissions-settings-tests-01.log`), Gallery 13/13
  (`permissions-gallery-tests-01.log`), API compatibility 17/17
  (`permissions-api-tests-01.log`); additive SDK baseline now 3627 symbols.
- Real host-pipe permission test (`permissions-bridge-tests-03.log`) passes grant,
  exact labels/identity, same-broker operation, replay rejection and lifecycle loss.
  Fixed missing bridge request classification. Added generated JSON metadata for
  permissions and also the prior BridgeCaptureCapabilities wrapper, which was
  missing from trimmed frontend contracts despite reflection-mode tests passing.
- Native permission dialog now uses direct native Allow/Deny/Cancel buttons rather
  than the generic handler list plus Confirm. Existing handler dialogs are unchanged.
  `permission-native-05/result.json` passes 18 checks: three consecutive dialogs,
  exact outcomes, default Cancel focus, unarmed A suppression, native buttons and
  eight-permission viewport fit at 686×624 DIPs. Earlier fixture runs exposed the
  generic list's initial-focus behavior during consecutive Opened/Loaded transitions;
  no timer/focus polling workaround was added to production.
- Final Library 21/21 (`permissions-library-tests-02.log`) additionally proves
  activation never requests permission, denial starts no capture, and approval
  resumes the original action exactly once without reloading. Host-pipe rerun with
  an initially revoked live broker passes (`permissions-bridge-tests-04.log`), as
  does host-only capability synthesis (`permissions-host-authority-01.log`).
- Debug frontend (`permissions-frontend-build-09.log`) and normal Release
  (`permissions-frontend-release-02.log`) build cleanly. Native fixture PID 60028
  closed normally. No user consent, driver, provider API, or personal media changed.
- Review found consent invalidation must cancel pending prompts without changing
  the still-Interactive broker lifecycle; added CancelPending separately from
  lifecycle loss. A later valid request can recover without reloading.
  Final broker rerun passes 76/76 (`permissions-broker-final-01.log`).
- At this checkpoint, pinned sources received Unavailable
  rather than a main-window prompt; preserve the intended foreground/focus policy
  when addressing this. Review also found a no-op Stop recording selector;
  corrected below before publishing the new capture contract.
  Marketplace, audio qualification, complete candidate assembly, native layout review
  and launch remain outstanding. No whole-goal completion has been claimed.

### Capture stopping contract cleanup

- Removed the unused StopManually flag from the unreleased configurable recording
  contract and removed its ineffective selector. The UI explains the real behavior:
  every recording stops at maximum duration, and reopening can finish it early.
  SDK StopAsync remains the independent lifecycle operation for widget authors.
  This changes no previously released capture constructor or legacy request API.

## Dedicated marketplace checkpoint

- Added a normal sandboxed Marketplace widget with catalog cards, updates filter,
  Y refresh, X filter, B back with selected-card restoration, LB/RB screenshots,
  progress/cancellation and review-before-install. Registered in both editions.
  Settings Browse marketplace and SDK Gallery use a declared marketplace intent;
  installed management returns to exact Settings details through its intent.
- Shared marketplace DTOs/validation now live in WidgetProtocol. The existing
  Settings diagnostics service and new scoped broker service share the unchanged
  BridgeWidgetMarketplace engine. Extracted a shared review presentation; exact
  archive tokens, full-trust acknowledgement and disabled installation remain.
- Management authority is intrinsic and granted only after sealed bundled
  marketplace admission, AppContainer/generic-worker checks and exact package/
  publisher matching. An installed name/publisher spoof cannot acquire it.
  Reads require Visible/Interactive; mutations require Interactive. Command action
  must match the operation so read admission cannot smuggle a mutation.
- Integration found configured dedicated workers (Settings) loaded sealed icon
  metadata but omitted manifest intents. They now load verified intent declarations
  into the existing worker/catalog fingerprints too; no arbitrary catalog-JSON
  intent authority was introduced.
- Marketplace view tests 4/4 (`marketplace-ui-tests-03.log`); engine 7/7
  (`marketplace-engine-tests-01.log`); role provenance and configured intent tests
  each 1/1 (`marketplace-authority-tests-01.log`, `marketplace-configured-intents-01.log`);
  broker 77/77 (`marketplace-broker-tests-01.log`); diagnostics 28/28
  (`marketplace-diagnostics-tests-01.log`); Settings 87/87
  (`marketplace-settings-tests-01.log`).
- Theme compilation caught unsupported `text-align: left` in new Marketplace and
  Capture Library styles and a missing danger fallback. Corrected to logical start
  alignment and the existing Settings fallback. Capture Library now 22/22 with a
  theme compile regression (`marketplace-capture-style-tests-01.log`).
- API compatibility 17/17 (`marketplace-api-tests-01.log`), reviewed additive
  baseline 3633 symbols; Gallery 13/13 (`marketplace-gallery-tests-01.log`); Release
  frontend clean (`marketplace-frontend-release-01.log`). Settings rerun 87/87
  (`marketplace-settings-tests-02.log`) explicitly checks its marketplace intent.
  Added marketplace engine/widget gates to the verification manifest.
- Native/assembled marketplace validation remains part of final candidate assembly;
  no actual user package installation or user profile change was performed. Engine
  tests install synthetic packages in isolated temp roots.

### Next qualification work

- For the first optional audio increment, investigate Windows process-loopback
  capture scoped to the fixed captured process/tree, muxed as AAC with the existing
  H.264 writer. Keep audio off by default; do not advertise system/microphone modes
  simply because they exist in the enum. Use a bounded event-driven PCM queue and
  a shared QPC timeline, and include audio in byte-budget validation.
- Qualify with generated tones from a synthetic captured application, a separate
  competing tone to prove process exclusion, decode/duration/synchronization checks,
  silence, stop/cancel/reuse and measured overhead. Update capability probes and
  confirmation/indicator text only after the actual path works. This paragraph is
  implementation direction, not evidence of implemented audio.
- Pinned permission prompts need root/focus/size handling; normal main-surface
  dialogs pass native checks. Avoid introducing application switching or a custom
  widget return stack. Revisit the existing pinned text-entry/popup patterns.
- Final candidate must use complete edition assembly, with current generic worker,
  Bridge, native encoder DLL (without test fixture), Marketplace, Capture Library,
  Performance, Browser and Game Help. Verify real sandboxed marketplace service
  admission and Settings intent routing there, plus native capture-grid/settings
  layout. Raw frontend build output remains insufficient for handoff.

### Pinned permission coordination checkpoint

- Permission prompts now capture the exact active pin and use its XamlRoot.
  The host suspends widget input while the dialog is open and restores the
  original presentation/focus on completion. Replacement or unpinning cannot
  restore a stale surface; unpinning dismisses the dialog before disposing its
  window. Visibility/foreground/disconnection revoke dialog controller admission.
- Pins below 320×280 DIPs return Unavailable; minimum-size layout qualification
  is still pending. Longer permission messages have a bounded scroll viewport,
  D-pad scrolling when focused and the standard right-stick scrolling kinetics.
- Debug build passes with no warnings (`permission-pin-build-05.log`). Real
  shell/Bridge synthetic intent fixture passes **45 checks**
  (`permission-pin-shell-02/result.json`), including pinned-root ownership,
  neutral controller admission, acceptance with the main foreground flag revoked,
  exact pinned focus restoration, ordinary intent routing and browser handoff.
  Windows inspection confirmed the pin HWND was foreground and main HWND was not.
  The shell foreground flag intentionally includes owned peer windows, so a test
  assumption equating it with the main HWND was corrected.
- The separate dialog fixture reached 23 passing checks, including Allow/Deny/
  Cancel geometry at 480×360, but failed message-scroll initialization. Its bounded
  retry exposed an Opened-before-Loaded race: the deferred tab-stop admission
  could run before IsLoaded. Moved admission to Loaded. That source fix builds,
  but **small-viewport/message scrolling is not yet revalidated**; the 320×280
  case was never reached. Do not report this fixture as passing. Preserve
  `permission-pin-native-03` and `-04` as failed evidence. Revisit at complete
  candidate layout qualification rather than repeating the same fixture now.
- Exact broker request/grant from the assembled pinned widget and final native
  layout review remain pending. All current tests use synthetic widgets and
  isolated profiles; no user's consent was changed.

### Process audio and AAC qualification checkpoint

- Added a native process-tree loopback implementation behind the standalone
  `Build-WinUiWindowPreview.ps1 -TestAudio` probe. No shipping capture path calls
  it yet; advertised audio remains None. It uses Windows asynchronous process
  activation, an event-driven capture thread, fixed 48 kHz/stereo/16-bit PCM,
  and a one-second (192,000-byte / 187.5 KiB) queue. Overflow and timing errors
  fail explicitly; there is no system-audio fallback.
- Added optional AAC input to the shared video encoder and a QPC-based audio
  timeline helper. Existing silent callers retain the default. The timeline
  clips audio before the video epoch, fills gaps with silence, caps output at
  the configured frame count and avoids cumulative packet rounding drift.
- `audio-native-06.log` passes **14 qualification checks**. A synthetic parent
  plays 440 Hz, its child 660 Hz, and an unrelated sibling 990 Hz. Two capture
  sessions preserve both target tones and exclude the competing tone. Observed
  PCM queue peak is 3,840 bytes (3.75 KiB); capture plus fixture processing used
  15.6 ms CPU over each roughly two-second run. This is synthetic process audio
  evidence, not a real-game overhead or hardware-video measurement.
- AAC mux/PCM decode of `audio-native-06/application-audio.mp4` preserves the
  isolation: target amplitudes ~799, excluded frequency ~0.43. Decoded audio is
  96,256 frames, ending 5.33 ms after the intended two-second video timeline.
  Timestamps are monotonic. The probe also verifies explicit overflow and silence
  from an independent process. Windows PCM conversion dithers silence by one LSB;
  the assertion permits at most two LSB, rather than incorrectly requiring exact
  zero. Earlier failed silent assertions are preserved in `audio-native-02/03.log`.
- The audio activation callback owns its completion event even after a waiting
  caller times out. The capture thread owns and releases WASAPI/COM resources;
  borrowed packets are released on failure, and queued memory remains bounded.
  Probe tones run only in owned synthetic children and change no volume settings.

### Application audio integration checkpoint

- Configured WGC recording now captures the fixed video target's process tree,
  muxes AAC and reports actual audio mode. Native configured ABI is version 3,
  with 56-byte options/results; the existing legacy capture export remains
  compatible. The managed adapter uses the same layout. No system/microphone
  mode is accepted and no application selection or foreground switching was added.
- Audio uses the first WGC frame's QPC timestamp. Pending PCM is committed only
  through encoded video frames; partial packets are retained for subsequent
  frames. Early stop trims the pending tail and pads only the remaining video
  interval. Each of the WASAPI and timeline queues is bounded to 192,000 bytes.
- The first audiovisual fixture found ~142 ms lag at 15 fps. The cause was stale
  WGC frames remaining in the two-frame pool. Sampling now retires old frames in
  a bounded drain and uses the newest frame. This fixes the recording path, not
  the test threshold. `audio-wgc-01.log` preserves the failed evidence.
- `audio-wgc-03.log`: **44 checks pass**, recording a synthetic 3840×2160 window
  at the full sizes of every advertised profile with AAC. Frames/missed slots:
  software 720p15 30/0; hardware 720p15 30/0; 1080p30 60/0; 1080p60 120/0; 4K30
  60/0. Decoded visual/tone transitions differ by at most 58.3/51.7/28.3/21.7/18.3
  ms respectively. AAC duration differs by 5.33 ms from the two-second video.
  Child-process inclusion, competing-process exclusion, early stop, cancellation,
  target exit and reuse pass. Synthetic 4K30 process CPU was 406.2 ms with audio
  versus 265.6 ms silent over ~2.4 seconds including initialization. These are
  local synthetic measurements, not steady-state or in-game GPU/FPS guarantees.
- Lazy discovery probes process audio against the host itself and checks AAC
  combined with the minimum/maximum bitrate of each advertised video envelope.
  Application audio is offered only when all combinations initialize. Screenshots
  and startup still do not trigger encoder/audio discovery. The measured managed
  discovery time was 3,480 ms once per frontend session, with five video profiles.
- Audio is off by default. Capture Library exposes only advertised modes, saves
  the audio preference, includes AAC bytes in its estimate, and preserves a saved
  unsupported preference. Capture confirmation/indicator identify audio inclusion.
  Review shows actual FPS/encoder/audio and missed frames; user-started playback
  of audio clips is unmuted, with no autoplay. Shared admission includes the
  128 kbps AAC budget (`CaptureLimits.AudioBitrate`). Game Help remains silent.
- SDK Gallery now demonstrates capability discovery, validation and configured
  StartAsync, with supported audio selection. Unsupported settings preserve its
  existing preview; hardware preference retains the explicit 720p15 demo quality.
  A legacy mocked RequestAsync handler was updated to exercise the new contract,
  including rejected requests, default-off audio and preview preservation.
- Validation: SDK configured capture **1/1** (`audio-sdk-tests-01.log`), Capture
  Library **22/22** (`audio-library-tests-01.log`), Gallery **13/13**
  (`audio-gallery-tests-02.log`), SDK compatibility **17/17**
  (`audio-api-tests-01.log`). Reviewed SDK baseline remains 3,633 symbols.
  Debug and shipping Release frontend builds pass without warnings
  (`audio-frontend-build-02.log`, `audio-frontend-release-01.log`).
- Managed/native WGC and native player fixture passes
  (`audio-managed-01/result.json`, 56 assertion/measurement entries), including
  actual application-audio metadata through P/Invoke, original screenshots,
  silent hardware recordings, stop/cancel/reuse and player controls/lifecycle.
  Both owned fixture processes were closed normally. No user media was captured.
- Current qualified native DLL: `artifacts/platform-improvements/audio-wgc-03/`
  `WinUiWindowPreviewNative.dll`. Its build did not use TestCapture, so the DLL
  contains no capture-fixture exports. The standalone test EXE is not shipping.

### Next concrete action

1. Verify actual pinned permission request/grant against the assembled sandboxed
   worker, not only direct shell dialog coordination. The small-layout and message
   scrolling qualification below is now complete.
2. Assemble the complete candidate with the current Bridge, worker, native ABI 3
   DLL and current built-ins/samples (including Marketplace, Library, Performance,
   Browser, Game Help and SDK Gallery). Validate real Marketplace service admission,
   Settings intent routing and native capture-grid/settings layouts.
3. Audit requirements/docs, then launch that complete candidate for physical
   testing. Goal remains active; no merge, push, release, installer publication
   or worktree cleanup. Raw frontend build output is not the deliverable.

### Final dialog layout qualification

- `permission-final-layout-01/result.json`: **32 checks pass** on the current
  Debug frontend. The Loaded-based scroll-stop admission resolves the earlier
  initialization failure. Default Cancel, all three decision outcomes, unarmed
  opening gesture, pinned foreground retention and scrolling pass. Allow/Deny/
  Cancel remain inside both 480×360 and 320×280 DIP pin viewports; long messages
  accept focus, D-pad scrolling and right-stick scrolling at both sizes.
- Earlier failed fixtures remain recorded; they no longer describe current source
  behavior. Permission reference documentation now describes main and pinned
  dialog placement and the minimum pin dimensions.
- Next build uses the normal local distribution assembly, with fresh candidate
  output under `artifacts/platform-improvements/final-candidate`. It does not
  create an installer, register MSIX, change trust, or publish anything remotely.

### First assembled-candidate review

- Local distribution assembly from `6b82e2c6` completed under
  `artifacts/platform-improvements/final-candidate/0.1.0-preview.29`.
  Production verifies 13 widgets, Developer 17, including sealed packages/icons,
  private runtimes and emitted frontend cleanup. This candidate is superseded by
  the cold-discovery correction below; do not use it as the final handoff.
- Launched Developer in the isolated `assembled-validation-01/profile` with an
  empty installed catalog. The real sandboxed Marketplace populated all four
  official listings, downloaded Spotify, verified compatibility and presented the
  full-access review. Review was cancelled and the download discarded; no package
  was installed or executed. Evidence and screenshots are in that validation
  directory. A wait initially targeted the review dialog before the separate
  Review installation action; inspection confirmed the correct intermediate state.
- The sealed sandboxed SDK Gallery requested previously undecided capture access
  from its full-widget pin. The host dialog appeared in the pin, with Cancel
  initially focused. Allow returned Granted to the same worker (PID 91248),
  restored exact Request access keyboard focus and retained pinned foreground.
  `pin-permission-granted.json`, `pin-permission-focus.json`, window inventories
  and `pin-permission-dialog.png` record this actual worker/host flow.
- Capture Library settings render coherently and scroll to all options. Application
  audio was selected and persisted only in the disposable profile. Screenshot
  evidence covers the top and bottom. The actual Windows/controller navigation
  acceptance pass remains user-owned; UI Automation is not physical controller proof.
- Cold Inspect capture options failed once, while its immediate cached retry
  returned five profiles. Native discovery measured ~3.5 seconds versus the
  transport's ordinary 3-second timeout. Added a 20-second deadline only to capture
  capabilities/validate/start on both pipe ends, covering the registry's existing
  15-second bounded discovery wait. Status/read/cancel/stop and other capabilities
  keep their normal deadlines. Caller cancellation still abandons discovery.
- Regression evidence: real broker pipe with an intentionally shorter ordinary
  deadline, cancellation and delayed capability publication passes; capture checks
  **7/7**, full broker suite **78/78** (`cold-capture-pipe-tests-01.log`,
  `cold-capture-broker-full-01.log`). Also aligned Marketplace card contents to the
  start edge; widget/theme checks **4/4** (`marketplace-alignment-tests-02.log`).
  One parallel build collided in shared MSBuild output; the sequential rebuild
  passed. That compiler lock was infrastructure failure, not a passing test.
- Validation app PID 92332 closed normally. A small artifact-only fixture used the
  real CaptureLibraryStore to seed three distinct synthetic red/green screenshots
  into the disposable profile, then denied that profile's Library read permission.
  This prepares a cold Settings-permission intent and native thumbnail review.
  No normal profile or personal image was read or changed. Fixture source/results
  are under `seed-library-fixture` and `assembled-validation-01`.
- Next: build a fresh complete candidate with the deadline/alignment changes,
  prove the first cold discovery request, cold Settings intent and regrant flow,
  thumbnail grid and Performance main/pin layouts. Then update the acceptance
  handoff path and launch the final complete candidate in the normal profile.

### Second assembled-candidate review

- Distribution assembly from `dbe99bd1` passes both complete editions (13/17
  widgets) under `final-candidate-02`. Developer PID 89788 used the same isolated
  validation profile; all new evidence is under `assembled-validation-02`.
- The first cold SDK Gallery capability request succeeds in 4,349 ms, returning
  five profiles without retry (`cold-capture-capabilities.json`). This exceeds
  the old three-second deadline and validates the actual corrected pipe path.
- Capture Library correctly shows its denied-read recovery link. That intent
  cold-starts Settings directly on Capture Library's permission page, displaying
  the exact denied capability. Allow access writes Grant; returning to Library
  lists the synthetic saved captures. Application audio remains selected across
  app/worker restart. Evidence: `settings-permission-intent.json`,
  `library-consent-restored.json`, `library-audio-persisted.json`.
- Native visual review found two remaining admission/layout gaps, so this
  candidate is **not** the final handoff. Thumbnail files are valid red/green PNGs
  and the host generates its derivative cache, but UIA shows zero-width image
  bounds. Added explicit 100% thumbnail/caption width and start alignment inside
  cards. Capture Library/theme checks **22/22** pass
  (`thumbnail-width-tests-01.log`); actual thumbnail pixels need candidate review.
- Performance could not open: the Bridge raised `Unsupported view node kind
  'LineGraph'` before reaching the native renderer. Added the missing `lineGraph`
  render-style role. A focused regression proves themed graph routing and checks
  that every admitted enum kind has a Bridge role; **1/1** passes
  (`graph-style-tests-02.log`). The first test compilation omitted required
  assertion messages; corrected without changing production behavior.
- Next candidate must prove visible thumbnail pixels, Performance main graphs
  and compact pin (no Automatic button), Marketplace alignment and source/release
  consistency. Existing component/native graph tests did not exercise this Bridge
  admission stage; do not use them as proof the assembled Performance UI worked.

### Third assembled-candidate startup correction

- Preserved the live stalled Capture Library processes for diagnosis. Artifact-only
  ClrMD inspection found the worker idle with loaded entries, Bridge snapshot 2,
  frontend snapshot 1, no refresh task and a `stale_refresh_failure` diagnostic.
  Only selected framework state/type names were inspected; no heap dump or secrets.
- The catalog reload deserialized identical icon/quick-action lists into new objects.
  Descriptor record equality therefore treated an unchanged descriptor as retired,
  rejecting the in-flight snapshot. Canonicalize exactly equal descriptor contents
  during catalog admission; real scalar/list changes still get a new descriptor.
  This preserves existing strict authority checks rather than retrying stale work.
- A deterministic pipe regression reproduces snapshot 1 remaining stale for both
  same-revision and advancing-revision catalog reloads (2 failures before fix).
  Complete presentation-session tests pass **332/332** after the correction:
  `catalog-refresh-full-01.log`; no replacement/removal/restart guards relaxed.
- Next: build candidate 04 and verify cold Library startup, thumbnail pixels,
  Performance main/pin graphs, Marketplace alignment, then normal-profile handoff.

### Final assembled handoff

- Candidate 04 built from clean commit `97a6e57a`; production 13 / developer 17
  widgets pass complete catalog, package-integrity and icon admission. Both include
  Browser, Game Help and all roadmap widgets. No installer/public release created.
- Actual cold startup now lists all three seeded captures without retry. Capture
  settings responds immediately. Re-entering Library after idle worker retirement
  also works. Thumbnail bounds are 437/438 × 180 physical pixels at 125% scale;
  actual red/green pixels and saved-image preview are visually confirmed.
- Performance main page opens without Bridge admission errors. CPU/GPU/RAM/VRAM
  histories visibly update; the 400×263-pixel compact pin renders real readings,
  no Automatic option and no buttons. FPS was not enabled/elevated in this check.
- Marketplace displays four left-aligned cards. Earlier actual download/review/
  cancel and permission/Settings-intent checks remain applicable; no installation
  or trust grant was performed in the normal profile.
- Evidence: `artifacts/platform-improvements/assembled-validation-04/` contains
  library/grid/preview, main/system performance, compact pin and Marketplace
  screenshots plus UIA state. A temporary command-completion delay affected UIA,
  ClrMD and even a trivial shell command; existing commands eventually completed.
  Live presentation revisions continued advancing without failure. No duplicate
  build/restart was used to conceal that delay.
- Validation PID 97832 closed normally. Complete production candidate launched
  with **no profile/catalog override**, PID 97684, Capture Library selected;
  UIA confirms the actual root is visible. Exact path/launch receipt is
  `artifacts/platform-improvements/final-candidate-launch.json`.
- SDK references cover graphs, configurable captures/library, permission requests,
  Settings destinations and Marketplace. Gallery has reusable graph/capture/
  permission examples. Updated acceptance checklist and original roadmap pointers.
- Final audit: all implementation/local-verification goal sections are complete.
  Real-game FPS/overhead, physical controller navigation, game/HDR capture/audio
  and installed Inno upgrade/recovery remain explicitly manual acceptance checks.
  Additional repositories/private authentication remain deferred. The main checkout,
  user settings and unrelated worktrees were preserved; no merge/push/publication.

### Accepted integration and installer preparation

- User accepted the changes and authorized merging to local main and creating
  fresh Inno installers. This supersedes the earlier no-merge/no-installer boundary;
  no push, tag, remote release or worktree cleanup is requested.
- Advance the local distribution version to `0.1.0-preview.30` (file version
  `0.1.0.29`, following the established numbering). Physical acceptance of the
  candidate does not claim real installed upgrade/recovery qualification.
- Preserve main's unrelated uncommitted Spotify/YouTube setup-handoff and SDK
  edits. Build both installer editions from this clean accepted source, without
  including those unaccepted changes. Keep the existing candidate running.
- Integration completed: main fast-forwarded to `9fc74116`; all 20 pre-existing
  uncommitted file edits were restored and compared against their saved patch.
  The release-packaging self-test needed its old counts updated for Marketplace;
  it now also verifies Marketplace appears identically in both editions.
- Gates passed: installer contracts, native cleanup fixture, 13 recovery checks,
  release-packaging self-test, Inno syntax compilation, and both fresh shipping
  payloads (13 production / 17 developer widgets).
- Both Inno 6.7.3 installers are complete at
  `C:/Users/dwive/Projects/WidgetRail/artifacts/installers/0.1.0-preview.30/`:
  Production 103.9 MiB; Developer 111.3 MiB. SHA-256 checksums were reverified and
  source/packaging commits both match `9fc74116`. Installer metadata and checksums
  accompany them. This documentation checkpoint is subsequent to the build.
- No installer executed, no push/tag/release created, and no worktrees removed.

### Capture viewer polish and FPS diagnostics — in progress

- User requested cleaner Capture Library cards, native video thumbnails, a proper
  image viewer, responsive inline/expanded video, and reasonable FPS diagnostics.
  Authorized closing the failing installed app for validation and relaunching a
  candidate. Branch `codex/capture-viewer-polish` starts from accepted main b81cc6d9.
- Windows thumbnail service now produces clip frames through the existing bounded
  PNG cache/lease path. Library cards omit duplicate names and verbose size text.
  Preview height grows with its viewport. Images use shared native zoom/pan/fit/
  expand controls, preserving capture authority and full-resolution metadata.
- Explicit card sizing and dialog content-viewport constraints keep transport
  controls inside the expanded player. Volume no longer stretches across the card.
- FPS diagnostics: bounded helper queue over protocol v3; only the normal Bridge
  writes rotating 128-KiB logs. Collection behavior and its 120-second restart policy
  are unchanged. No frame CSV, nonce or capture contents are logged.
- Evidence `artifacts/capture-viewer-polish`: Capture Library 22/22, telemetry 10/10,
  native media 36/36; actual Windows clip thumbnail 210x144 PNG, 1974 bytes, original
  hash unchanged. Debug frontend clean; expanded native screenshot at 225% scaling
  shows visible transport. Initial fixture used a relative StorageFile path; fixed
  the fixture to resolve absolute paths. Initial frontend command lacked native
  paths; reused matching clean-built native DLLs (no native source changes).
- Installed PID 20992 closed normally with user authorization; native validation
  PID 53476 currently open. Next: final review/clean commit, assemble both editions,
  seed one synthetic video in the disposable Library profile, verify real widget
  thumbnail and responsive preview, then launch normal-profile candidate. Do not
  merge, push, create installers/releases, or remove worktrees for this request.
- Assembled review confirmed actual video thumbnail pixels and unclipped expanded
  controls. Inline review still scrolled its title because 64vh did not reserve
  metadata/actions. Follow-up uses a finite Stack with a growing player row;
  the library grid remains scrollable. Rechecking the complete page bounds next.
- User requested removing the expanded viewer's large Back footer. Removed it;
  B, keyboard Escape and the toolbar Collapse action remain. Expanded height now
  uses the recovered space. Late size events from a retired dialog are ignored.
- Native media checks now 38/38, including no footer and inline return bounds.
  A small standalone preview exposed clipped controls to UIA after Escape; its
  automated selector wait did not qualify mouse/keyboard return. The actual
  assembled Capture Library must verify those routes before handoff.
- Review-page Stack/flex layout was checked in candidate 02: heading, transport
  and action buttons are all inside the page bounds at 225% scaling. FPS logs also
  record bounded state/target-PID/stream-count changes; telemetry 10/10 remains green.
