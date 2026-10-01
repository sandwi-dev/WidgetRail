# WinUI widget lineup completion

Updated 2026-09-30. Branch `codex/winui3-frontend`; no main integration. This supplements `winui3-implementation-status.md`.

## Current inventory and acceptance boundaries — 2026-09-30

The canonical profile resolves16 enabled widgets: Settings, seven bundled widgets,
and eight installed samples. Verified against the canonical installation catalog,
installed catalog-state.json and package manifests. Recent Apps is intentionally
absent from the default catalog; it is not an unfinished required second launcher.
The dated implementation notes below preserve their historical evidence, not the
current feature backlog.

| Current widget | Candidate version | Evidence / outstanding acceptance |
| --- | --- | --- |
| Settings | Built in | Actual isolated controls/persistence pass34 native checks, including main display text/interface scale. Exclusive-control UI/broker/adapter checks use fakes; physical controller containment remains. Startup registration and packaging integration are implemented; actual installed startup behavior remains unqualified. |
| Now Playing | 0.1.2 | Owned silent Windows session Play/Pause/provider removal passed. User media-session behavior remains physical. |
| Display Profiles | 0.1.0 | Enumeration/save/rename and fake apply/rollback coverage. Real mode-changing apply/revert remains user-only. |
| Games & Apps | 0.1.1 | Indexed browsing, focus return and cold lifecycle fixes have native evidence. Actual game launch-and-return remains physical. |
| Task Switcher | 0.2.1 | Cold activation now publishes loading before provider enumeration; delayed-provider baseline reproduced lifecycle timeout and corrected8-test suite passes. Broker handoff user-accepted for FF7/GoW. User confirmed cold startup fixed. The bounded visible/hidden capture-close pass completed normally; the original intermittent capture-service hang has retained diagnostics but no proven correction. |
| Audio Mixer | 0.1.2 | Fake provider covers sessions/devices, volume reconciliation/mute/removal. Real volume/device actions remain user-only. |
| Network Controls | 0.1.2 | Fake provider covers radio/connection state, failures and cancellation. Real radio/network changes remain user-only. |
| Power | 0.1.0 | Fake native provider covers confirmations/privileges/single-flight; no real shutdown/restart/suspend actions are automated. |
| Playnite Library | 0.2.114 | Real browsing/modal/layout/retention evidence. Fast Library scrolling stutter is explicitly deferred by user; game launch/actions remain physical. |
| YouTube Music | 0.3.34 | Shared layout, pinned artwork/shortcuts, recovery and managed provider checks. Final account/media workflows remain physical. |
| Spotify | 0.3.83 | Shared busy/focus/slider and SVG native checks; user has exercised playback. Final account/playback/volume acceptance remains physical. |
| YouTube Video | 0.3.36 | Enabled; shared media native130/139 and local sample33 cover host behavior. Real provider/key/search/playback remains a separate acceptance scope. |
| SDK Gallery | 0.1.25 | Staged, including explicit Grid authoring; broad style/native declaration checks. Visual/controller spot checks remain physical. |
| Clock | 0.1.1 | Existing ten real-worker checks plus current shared13-check reference gate, passing twice: time/date, manual Refresh/X, native control/focus retention. No settings or automatic invalidation timer. |
| Full Application Reference | 0.1.1 | Sandbox reference with10,000 private records. Current shared13-check reference gate passes twice: indexed Library/Details/Back at0/75/9999, current lease refresh, cache eviction/deep reentry and hide/reopen. Earlier native01 deep Back timeout remains unexplained; validation diagnostics retained. No dialog/settings UI. |
| Embedded Media Sample | 0.2.9 | Actual sealed local sample33 exercises transport, fullscreen, compact pinning, focus return, placement and opacity. Native process-failure139 gate additionally proves GPU/browser recovery behavior. |

Shared gates now include native style460, independent pinned display/root typography,
production pin41, latest Settings34, and bounded query-reset/deferred-activation
restoration. The30-minute process-tree soak completed with bounded visible surfaces,
but memory growth has no established cause. Further memory testing is deferred at
the user's request; long-run resource stability is not claimed. Physical mixed-monitor/DPI, controller containment,
real-provider workflows and subjective motion remain distinct. CLI and installer integration are implemented, and the original renderer has been retired.
Signed installation/cutover qualification remains outstanding; see the current implementation status.

Reference workflow runner: `scripts/Test-WinUiReferenceWorkflows.ps1`.
It requires a fresh settings profile under its evidence directory and invokes only
Clock refresh, reference row navigation/refresh, and safe peer selection. It is not
an assertion about real-provider/system-changing workflows. Analyzer Debug passes;
native02/native03 each pass13 checks. Evidence: `artifacts/winui-shell/reference-workflows-20260930/`.

## Historical passes / resume points

The main task has installed all five bundled widgets plus Spotify, Clock, Full Application Reference, Embedded Media Sample and YouTube Video into the isolated candidate. Existing Settings/Now Playing/Display Profiles and runtime payloads were retained. Recent Apps remains an optional staged legacy reference and is not enabled in the default candidate. Native/provider physical checks remain pending; successful managed checks are not evidence that full WinUI workflows work.

Do not invoke real power, network, audio-device, application-launch or window-close operations during automated qualification. The user will perform physical checks where needed.

### Spotify SVG sharpness follow-up — native validation passed

The user reported a blurred logo after the earlier aspect-ratio correction.
That correction still enlarged a font-sized native ImageIcon with RenderTransform;
its raster calculation omitted ancestor interface zoom and did not observe DPI/zoom
changes without a size event. The original renderer instead requests SVG raster
dimensions from the destination size times physical pixel scale, bounded to 512.
Original-color package icons now retain intrinsic desired size but arrange the
native icon at final size, with no magnifying child transform. Raster preparation
uses the shared artwork physical-pixel policy (DPI, layout zoom, motion envelope),
coalesces notifications, observes root/zoom changes and retains one bounded source
at its high-water 32-pixel bucket. ThemeTint keeps its existing alpha-mask path.
The sharpness gate isolated the actual decode defect: changing SvgImageSource
raster dimensions after SetSourceAsync does not upgrade the decoded stream-backed
pixels. Image alone still scored only 4.0 versus 89.7 for the direct reference.
The host now decodes the already-admitted bytes at a larger requested bucket and
atomically swaps that ready source, retaining the old pixels throughout preparation.
One visible source and at most one bounded upgrade are retained; shrink/reopen does
not rerasterize. Inline/catalog OriginalColor uses native Image with the actual
measure/arrange viewport; native menu icon slots retain IconElement.
Added native checks for actual child paint size, interface zoom without layout
resize, fine-detail pixel contrast against a direct SVG reference, width changes
and bounded source/demand reuse. No artwork or Spotify styles were changed.
The unchanged pixel contrast gate now scores **91.1 versus 89.7** for the direct
reference. All **48 package-icon checks pass**, including ThemeTint/cache/lifecycle,
and the frontend build has zero warnings/errors. Evidence:
`artifacts/winui-shell/slider-settle-20260929/svg-direct-image/{frontend-02.binlog,result-02.json,passed.png}`.
Owned fixture PIDs 28144 and 39984 are closed. Actual Spotify screenshot/physical
sharpness acceptance remains with the parent execution pass.

### Compact music presentation follow-up — source ready, checks pending

YTM now-playing artwork was explicitly omitted from pinned declarations (`if (pane)`),
and playback shortcuts existed only on the main scope. Main and pin now reuse the
same shortcut declaration; pins include 112-DIP artwork and explicit Play/Pause
entry focus. Both ordinary players prefer 840x580. Fresh YTM pins prefer 360x440;
Spotify remains 360x360. Existing saved placements and opacity are untouched;
the 300-DIP minimum height remains supported through scrollable player content.
Spotify transport already uses the same SDK icon buttons and circular accent play
styling as YTM; the inset focused-play outline now matches that presentation too.
Candidate manifests advance to Spotify 0.3.82 and YTM 0.3.33; packages are not yet
built, installed or selected. Added pin artwork/shortcut admission and retirement
regressions, shared dimension/icon contracts, and renderer exports for ordinary
and compact player roots. Use the normal Spotify test fixture export environment
variable and YTM `--export-layout` to obtain the real declarations and styles.
Build/native checks remain with the parent; the running candidate is untouched.

| Widget | Stage and version | Workflow/backend evidence and remaining work |
| --- | --- | --- |
| Games & Apps | Installed, sealed 0.1.1 | 92 managed tests pass: indexed ranges and stale input, discovery/persisted curation, refresh, launch/return using fake provider, failures/denial. Real shell catalog/launch-return still pending. |
| Task Switcher | Installed, sealed 0.2.0 | 6 managed tests pass: stable ordering, separate window identity, switch/close routing, failure, hide/reopen. Real preview and activation lifecycle still pending. |
| Audio Mixer | Installed, sealed 0.1.2 | 48 managed tests pass: session/device streams, concurrent sliders and mute, authoritative rollback, device removal, optional permission recovery. Real WinUI/native volume device workflows pending. |
| Network Controls | Installed, sealed (source manifest) | 31 managed tests pass: Wi-Fi/Bluetooth events, optional failures, scan lifetime, radio/connection management and stale completions using fake providers. Real state changes deliberately unexecuted. |
| Power | Installed, sealed (source manifest) | 8 managed tests pass: confirmations/cancel, interactivity/consent, single-flight, native privilege scoping through fake native implementation. Production availability was read only; no live power command. |
| Spotify | Installed 0.3.79 | 75 widget, 10 playback host, 7 playback client and 6 application tests pass; all use contract/fake-provider scenarios. Actual authentication, streaming, volume and return remain pending. |
| Clock | Installed 0.1.1 | Ordinary worker reference with time/date and manual Refresh/X; see current native evidence above. No timed invalidation/settings UI is implemented by this sample. |
| Full Application Reference | Installed 0.1.1 | 5 managed tests pass. Actual indexed Library/Details/Back, Refresh and retained focus workflow pending; this reference has no dialog/settings UI. |
| Embedded Media Sample | Installed 0.2.9 | Standard worker with sample media protocol; actual player/fullscreen ownership belongs to specialized-surface qualification. |
| Recent Apps (legacy reference) | Archive 0.1.0 validated, not proposed as default | 7 managed tests pass; source is absent from shipping catalog. Read-only activity reference available if desired, not a second Games & Apps implementation. |

Already owned by main task: Display Profiles, Now Playing, Playnite, YouTube Music, SDK Gallery. YouTube Video and shared Settings consumers are separate parallel work.

## Inventory and integration paths

`samples/` has ClockWidget, ClockWidget.Worker (legacy worker executable for Clock, not a separate widget), EmbeddedMediaWidget, FullApplicationWidget, PlayniteLibraryWidget, SdkGalleryWidget, SpotifyWidget, YouTubeWidget, and YtMusicWidget. The full set is covered above or in the main task. `src/FirstPartyWidgets/RecentAppsWidget` is not in the shipping catalog.

- Bundled payloads: `artifacts/winui-shell/widget-lineup-20260929/bundled-restoration/installation/runtime/{GamesApps,TaskSwitcher,AudioMixer,NetworkControls,Power}`. `installation/widget-catalog.json` contains just the five merge-ready bundled entries. Copy only those directories and append missing entries to the candidate catalog; do not replace its whole catalog/runtime.
- Spotify: `artifacts/winui-shell/widget-lineup-20260929/spotify-restoration/widgetrail.samples.spotify-0.3.79.wrwidget`. Full-trust application package, as before; Spotify Premium and an authorized developer application are provider prerequisites for local streaming.
- References: `artifacts/winui-shell/widget-lineup-20260929/reference-packages/{ClockWidget,FullApplicationWidget,EmbeddedMediaWidget,RecentAppsWidget}.wrwidget`. Install/enable separately in the isolated installed catalog using normal CLI package operations.
- Package logs: staging directories above and `spotify-package.log`.
- Managed workflow results: `artifacts/winui-shell/widget-lineup-20260929/lineup-managed/*.log`.

## Concrete correction and backend audit

AudioMixer and NetworkControls managed fixture projects still referenced `src/WidgetBridge/BridgeRenderStyles.cs`, removed in the Bridge contracts split. Their projects now link the immutable DTO file from `WidgetBridge.Contracts` and the production `BridgeRenderStyleResolver` separately, retaining the existing local exception fixture. No renderer code was copied/reimplemented. The final test logs pass; initial compilation failures were test harness integration defects, not a widget runtime result.

The production Bridge factory already instantiates Audio, Network, Activity, Bluetooth, AppLibrary, Power and Display backend providers. The WinUI shell negotiates previews when its native capture service is present and injects the shared `WindowPreviewRenderer` into retained presenters. This establishes wiring only: real visible capture, device events, media ownership and protected operations still need their own qualification. Old early-checkpoint wording in `winui-window-previews.md` predates that renderer and must not be read as the current implementation inventory.

Legacy companion regression: YtMusicWidget.Tests had the same stale linked BridgeRenderStyles.cs path. Updated it to the immutable contracts DTO plus production resolver; all 61 legacy API tests pass. Evidence: lineup-managed/YtMusicWidget.log.

Media pass finalized by root: Spotify0.3.83 and YT0.3.33 selected in isolated catalog; ordinary840x580, new compact pins360x360/360x440, saved placements preserved. Spotify text action buttons (including SettingsRow Play here) have theme fills/borders and44px sizing. State-relative toggle restrictions remain authoritative while pending/acknowledged optimistic toggle stays busy and focusable. Managed77/30, native40 busy-focus,64 media geometry and23 production pin checks pass. See winui3-implementation-status.md final media checkpoint for full evidence/limits.
