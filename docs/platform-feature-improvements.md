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
- [ ] Hardware-assisted silent recording with measured supported combinations.
  Audio default off; advertise audio only after reliable implementation/validation.
  Document unavailable hardware-dependent work and continue other features.
- [x] Capture Library persistent settings based on advertised capabilities;
  thumbnail image grid; migrate Library and Game Help to shared capture API.
- [x] Reusable SDK graphs, bounded 60-second histories, FPS/average/1% low and
  frame-time graph from individual frames; system CPU/GPU/memory histories.
  Clear gaps/target changes, compact pin without Automatic; retain PresentMon FPS.
- [x] Dedicated official marketplace widget, Settings entry and installed-widget
  management; shared host-controlled distribution/review authority. No custom repos.
- [ ] Settings details/permissions intents and explicit host-owned SDK permission
  dialog, clear outcomes, continuation after approval, no background prompting.
  Adopt in relevant first-party widgets.
- [ ] Current documentation, SDK Gallery examples, focused automated gates and
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

Finish marketplace integration gates and checkpoint. Continue pinned-source
permission handling and audio qualification, then assemble and launch the complete
candidate after native layout review.
Hardware silent recording is qualified; audio remains outstanding. Final complete
candidate assembly and visual/physical acceptance remain pending.

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
- Remaining integration review: pinned sources currently receive Unavailable
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
