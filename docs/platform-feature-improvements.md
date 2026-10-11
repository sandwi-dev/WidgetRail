# Platform feature improvements

## Authority and baseline

User-approved goal: improve the platform-feature candidate, prepare and launch a
complete candidate for physical testing. No merge, push, release, or worktree
removal. Preserve unrelated main-checkout changes. Overlay restart is authorized.
Worktree: `platform-feature-roadmap/WidgetRail`, branch
`codex/platform-feature-roadmap`, starting commit `42713764` (clean).
Previous delivery and evidence: `platform-feature-roadmap.md`.

## Required outcome

- [ ] Configurable shared capture API: separate image/video options, workflow,
  capability discovery, preparation/combination validation, actual output metadata,
  stop-and-keep versus cancel/discard, general temporary media and streaming access.
- [ ] Preserve foreground window chosen after hide/countdown; no window picker or
  foreground switching. No silent quality downgrade.
- [ ] Hardware-assisted silent recording with measured supported combinations.
  Audio default off; advertise audio only after reliable implementation/validation.
  Document unavailable hardware-dependent work and continue other features.
- [ ] Capture Library persistent settings based on advertised capabilities;
  thumbnail image grid; migrate Library and Game Help to shared capture API.
- [ ] Reusable SDK graphs, bounded 60-second histories, FPS/average/1% low and
  frame-time graph from individual frames; system CPU/GPU/memory histories.
  Clear gaps/target changes, compact pin without Automatic; retain PresentMon FPS.
- [ ] Dedicated official marketplace widget, Settings entry and installed-widget
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

Begin configurable capture contracts and host pipeline. Performance/graph source
and automated/native validation are complete; final assembled-candidate visual and
physical acceptance remain pending alongside the other goal sections.

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
