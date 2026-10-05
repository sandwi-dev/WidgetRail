# Specialized WinUI surfaces migration progress

> Implementation record: checkpoint dates, temporary candidates, former source
> paths and pending-work statements below describe the stage recorded, not current
> release status. See [current platform status](current-platform-status.md) and
> [WinUI authoring](../reference/winui-authoring.md) for the implemented contract.

Updated 2026-09-29. Owner: specialized-surfaces agent; work stays on codex/winui3-frontend.

## Current checkpoint

YouTube Video completion (2026-09-29): 0.3.31 is enabled in the physical candidate.
Live link playback, search, volume/seek, fullscreen, compact pinning and unavailable
video recovery were exercised. Native media128 includes forced-GC/nested-frame
coverage for the reproduced FrameCreated cleanup crash; the host now enforces frame
origins using core navigation/document-request events without per-frame wrappers.
See the leading checkpoint in `winui3-implementation-status.md` for evidence and
limits. Earlier provider-pending statements below are historical.

Embedded completion (2026-09-29): production compact-media pinning is now wired
through document-scoped receipts and the shared pin coordinator. Passive peers use
AppWindow/XAML islands; cross-root browser movement awaits native Unloaded. See
`winui-embedded-media.md` and the latest embedded completion checkpoint in
`winui3-implementation-status.md`. Earlier notes below about missing production
media pinning are superseded. Real-provider YouTube qualification remains open.

Parent execution completed (2026-09-29): combined frontend builds cleanly;
fullscreen/media fixture passes 108 checks with pixel captures; pinned-window
fixture passes 16 checks. Cross-root probe passes 69 checks and three observations
with the same CoreWebView2, browser process, document/audio element, one resource
admission and continuing playback/render callbacks. No reload/duplicate browser.
Probe pixel capture was not requested, and no real provider was played. Evidence:
`artifacts/winui-shell/widget-lineup-20260929/combined/{media-02,pinned,cross-root}`.
All owned test windows are closed. This establishes transfer feasibility only;
production compact-pin presentation admission, ownership and input wiring remain.

- Read implementation-status, fullscreen-media, production-pinning, pinned-window, pinned-session and native media ownership documentation/code.
- YouTube Video v0.3.30 already uses discovered/indexed collection contracts and the official IFrame Player API. All 50 YouTubeWidget tests pass (2026-09-29), including SDK adapter conformance. These are fake-provider/protocol checks, not live playback acceptance.
- Existing fullscreen owner transfers one WebView2 inside the same XamlRoot. Provider fullscreen/control/recovery acceptance is still physical.
- Compact media pinning is incomplete: the shell's pinned presenter has no media owner, and media ownership currently indexes only the ordinary viewport. Cross-window ownership must be explicitly designed/tested; merely assigning the owner would steal the ordinary viewport and cannot establish valid pinned input.

## Work queue

1. Build/validate YouTube Video into an isolated stage; return archive path to coordinator for candidate admission.
2. Audit actual YouTube provider/control/fullscreen workflows without starting playback or changing settings. Fix source-proven defects with focused tests.
3. Complete pin placement/resize/opacity controls, monitor/scale reconciliation and media ownership. Do not describe current pin fixture checks as complete parity.
4. Coordinate native windows/builds with main agent; await user for real playback/controller acceptance.

## Physical acceptance still required

YouTube Video: public embeddable link, explicit Play, pause/resume, seek, volume, Player/Discover/Setup return, fullscreen enter/B exit, hide/reopen retention, recover unavailable video. Provider key setup/search uses user-owned key. Pin transfer/audio continuity comes after ownership implementation.

## 2026-09-29 YouTube Video checkpoint

- Built and validated `artifacts/winui-specialized/youtube-video-20260929/widgetrail.samples.youtube-video-0.3.30.wrwidget`: 14 files, 2,218,148 bytes. No live catalog mutation by this lane; coordinator will admit the archive to the candidate.
- Fixed a concrete shared-host gap: fullscreen media title, command labels and controller glyphs were absent from shell styling registration. They now use the existing `ShellChromeStyles` / `NativeComputedStyleAdapter`, inheriting theme typography, Bold Text, text scale and semantic glyph size. No custom animation or new styling path was added. Interface scaling stays owned by the containing shell scale root.
- Added three native assertions to the embedded-media fullscreen fixture: semantic glyph size/font; Bold Text plus 150% text size; reset to original typography without reconstructing media. Native execution/build pending coordination with root.

## Pinned-media remaining design boundary

The current compact video presentation is host-generated, not a package-authored `PinnedLayout`; YouTube Video declares `CompactPinned` in the media session. Existing authored/full-widget selection APIs explicitly do not represent this mode. A complete implementation needs a distinct session media-presentation receipt tied to the admitted document/capability epoch, exclusive placement/input ownership, then an actual cross-XamlRoot WebView2 transfer proof (or a host-owned native composition media plane if WinUI's control cannot retain its controller across roots). Constructing a second browser is not an acceptable substitute because it duplicates provider playback/audio and loses continuity. Existing supported authored pins remain available. Real playback and media transfer acceptance are not claimed by current fixtures.
- Pinned peer windows now reuse `OverlayWindowFrame.SuppressBorder` immediately after native show, matching the already-corrected main/backdrop frame policy. Added two native window assertions for initial show and passive reopen. This preserves supported pins and does not introduce a separate nonclient implementation. Native run pending root coordination.

## Provider audit

The sealed YouTube adapter sets autoplay=0, controls=0, disablekb=1 and fs=0, uses exact host-admitted iframe origins/domain families, and forwards typed provider errors to bounded native status messages. Video loading/cueing is separate from a correlated explicit Play action. Existing tests cover adapter command sequencing, cancellation, parked media, preference rejection and search pagination; no source-proven provider regression was found in this pass. Live-provider behavior remains an acceptance gate rather than being inferred from adapter fixtures.

Archive SHA-256: `7314665ABAEFE9501BE922079D2BF7FB0A5D4C28DF62C1FE06A73ED9BF3E513F`.

## Compact media transfer decision probe (in preparation)

Opt-in `--validate-embedded-media-cross-root` will reuse the sealed local SDK adapter and silent WAV already used by the native media fixture. It will stop after this bounded case instead of running unrelated owner tests. No remote traffic, real provider, controller reader, profile mutation or system setting action.

The probe will record one native WebView2 and CoreWebView2 identity, one script-document token, the audio DOM object and its media/timer counters, and resource/navigation counts. While local silent audio plays it will move that exact element into a passive `PinnedWidgetWindow` (different XamlRoot), confirm document/audio progress without navigation/load/pause/recreation, close the peer through its ordinary close-request path and return the same element before window disposal. Then it will pause using the ordinary typed SDK command to prove input/adapter viability after return. A finally path always detaches media before peer teardown; failure is recorded as a negative result, not hidden by rebuilding a browser. JSON will retain per-phase observations and diagnostics. This establishes local transfer feasibility only, not provider continuity or pinned-session admission.

Source edits are held while root builds the preceding native gates.

The decision probe draft and driver are ready. `scripts/Test-WinUiMediaCrossRoot.ps1` has passed PowerShell parsing; it requires an already-built validation frontend, creates only an owned local test process, optionally captures the peer's synthetic pixels, preserves its JSON, and closes only its owned frontend. The draft remains at `artifacts/winui-specialized/cross-root-probe/EmbeddedMediaValidationPage.CrossRoot.cs.pending` until root releases the compile-tree hold. It records a pre-transfer checkpoint so native process failure cannot be mistaken for successful transfer.

## Cross-root probe implementation ready (2026-09-29)

Root's combined frontend build completed cleanly (see `combined/frontend-02.txt`), so the probe source and opt-in hook are now in the validation tree. No build or launch was performed by this lane. Files: `Validation/EmbeddedMediaValidationPage.CrossRoot.cs`, the optional branch/result-file selection in `EmbeddedMediaValidationPage.cs`, `MainWindow.Validation.cs` flag, and `scripts/Test-WinUiMediaCrossRoot.ps1`.

The probe is excluded with other validation code from production builds. It performs one sealed local load/play, verifies transfer and real peer WM_CLOSE return, then Pause/Play/Pause on the original native surface. It never restarts or re-resolves the browser to mask failure. Observations persist before transfer, in the peer, and after return; exception reports also retain them. The driver exports partial reports on crash/failure and targets only the owned frontend for cleanup. Await root's coordinated build/native run; no feasibility verdict yet.

## Pinned move/resize implementation — 2026-09-29

Opacity follow-up: tray-menu **Pinned opacity: N%** enters the same adjustment
transaction in opacity mode. Left/right on D-pad, left stick or keyboard changes
alpha by 5%, bounded 30–100%; A/Enter saves and B/Escape cancels. The guide reports
the current percentage. Resize inputs are consumed without moving the pin. Hide,
deactivation and failed save restore the original alpha through shared rollback.
Existing native SetWindowOpacity and preference fields are reused. 110 managed
shell tests, clean default/isolated frontend builds and 20 production native pin
checks pass, including six opacity cases. Candidate 54156 and native test 40884
were closed with authorization; next candidate contains the opacity control.

Root validation completed: clean frontend build, 109 managed shell tests, 14 native production placement checks and 29 native peer-window checks pass. Evidence: `artifacts/winui-shell/settings-pin-preview-20260929`. Test windows are closed; physical controller acceptance remains pending. Original host reference: main.cpp adjustment path and PinnedSurfacePlacement/ControllerNavigation use 32-DIP steps, left stick/D-pad for move, right stick for resize, A commit, B cancel; held navigation uses 15000/9000 hysteresis, 250ms initial repeat and 80ms subsequent repeat.

The production tray menu now exposes **Move / resize pinned widget**. One reversible host transaction owns all controller input while editing. The existing pin stays passive and the existing main HWND keeps input focus; the pin's themed ownership outline identifies the edited surface. The semantic guide shows Move/D-pad, Resize, Save and Cancel. Keyboard arrows move, Shift+arrows resize, Enter saves and Escape cancels. No widget action, media owner, browser, polling thread or custom layout engine is involved. A/B releases are consumed and normal rail focus/navigation resumes on exit.

A saves through the existing atomic explicit-profile pinned store while holding the shell transition gate; B, hide, deactivation and controller disconnect cancel uncommitted previews. Exact pin selection is revalidated before save. Failed persistence restores the preview baseline and reports failure instead of claiming success. Existing monitor DIP/anchor/size-limit policy bounds all movement and resizing. A tracked logical placement avoids applying monitor DPI twice when WinUI has already resized an HWND during WM_DPICHANGED. Display/work-area changes cancel transient edits and resolve placement against current displays; interface zoom refreshes declared limits.

Added six managed regressions for geometry, limits, DPI/fallback, repeat/priming/hysteresis and guide labels; two native window assertions preserve passive behavior while showing the edit cue. Added production opt-in `--shell-no-controller --validate-pinned-placement=<result.json>`: requires an isolated profile with an existing saved pin, drives normalized D-pad/right-stick/A/B, checks actual native bounds, exact cancel, atomic file persistence, same selection, passive ownership, resumed rail navigation and hide cancellation, then restores the original persisted placement. No provider control or system-setting action is invoked. Root must run this test after combined build; physical controller feel is not claimed from these checks.

Source review corrections before build: normal main presenter hit-test/admission is restored alongside rail focus on exit; pinned presenter input stays disabled while passive and is enabled only after explicit pinned lifecycle admission. Placement commits now hold the shared transition semaphore across atomic persistence and exact-owner revalidation. Host-owned logical placement is tracked separately from HWND pixels to prevent WM_DPICHANGED double scaling. Production validation asserts resumed D-pad navigation after both Save and Cancel, and restores its starting placement file.

## Passive pinned child-focus correction — source ready

Cause: WinUI keeps logical focus separately in each XamlRoot when a peer HWND loses activation. The coordinator revoked input and removed the host outline, but its child Control.FocusState could remain Keyboard/Pointer, leaving authored :focused fill/scale/outline visible. The original renderer gates focus painting on controllerFocused while retaining the logical target.

The pinned window now uses its real host pane as the passive logical focus owner. The coordinator captures the remembered widget control before revoking input, then transfers native focus to that host pane, producing real LostFocus events for any widget/control styling. Passive GettingFocus redirects late native/popup fallback back to the host pane. The pane leaves the tab order during explicit interaction, and existing presenter entry restores the captured target. No widget disabling, per-style overrides, or surface teardown is used.

Added native peer-window checks for child focus clearance and no foreground acquisition, plus rejection of late passive native focus. Extended actual production pin validation to enter the pin, navigate without actions, leave, assert enabled content retained/unfocused, reject late focus, and reenter the exact same control. Source only; root owns builds/native validation. Physical PID 4776 was untouched. Slider files were not changed.

## Passive pinned focus redesign — source ready after rejected native approach

The preceding host-pane focus transfer was rejected by native validation: calling Focus on the passive root could reactivate the pinned HWND after the main window became foreground. That approach has been removed in full: pinned root is again Grid, with no Focus calls, GettingFocus redirection, added tab stop or replacement ContentControl.

Replacement: `NativeComputedStyleAdapter` has a weak XamlRoot policy for focus presentation. PinnedWidgetWindow updates it from visibility, interaction and actual activation; both explicit View exit and AltTab disable it. The shared adapter masks focused/pressed states (including nested/collection overrides), removes its compositor focus decoration, and suppresses native UseSystemFocusVisuals while passive. Reentry restores the original native policy before recreating any custom outline. Logical/native focus itself remains unchanged, preserving the target and background/fragment ownership. No widget disable, document teardown, focus transfer or extra HWND activation is introduced. Root's slider settlement and ResetSliderValues changes remain intact.

Native peer fixture now paints contrasting authored base/focused states, checks explicit exit and AltTab-style deactivation, retained logical control, no foreground theft, main-XamlRoot isolation, late forced focus/pressed overrides, and restoration on explicit interaction. Production pin validation likewise checks real shared computed focus state, retained logical control, enabled/mounted content, late pressed-state suppression and exact reentry. Source and fixture changes are complete; diff whitespace checks pass. Root owns the combined build/native run. No build, launch, process closure or commit occurred in this lane.

## Native pin pointer-fixture investigation

Production pin workflow passed 23 checks and shared style suite passed 305 (root run). The separate window fixture failed before its new focus checks: only nine setup checks were recorded, with underlyingClicks=1 and pinnedClicks=1 at the first advance. Its reader already checked file modification time against process start, so a plain pre-start stale file is not established. However, the global JSON file had no process/run identity and the reader accepted any fresh matching phase; UI wait-for found=false was also not checked. Existing evidence does not identify why the pinned click occurred.

Fixture/driver hardening (source only): results now carry process ID, run ID and monotonically increasing publication; each phase wait requires that exact run and a newer publication. Real-click counters are checked before advancing, UI wait-for failures fail explicitly, partial/failure JSON is always retained. The fixture logs up to 64 synthetic pointer/click/advance events with stage/phase and foreground handle, is initialized once and prevents overlapping async advances. Strict exactly-once/pass-through assertions are unchanged. Driver PowerShell parsing and diff checks pass. No build or native launch was performed; next isolated rerun can distinguish a result race, pointer delivery and unexpected invocation rather than guessing.

Final root validation: style refresh activation defect fixed using the shared OverlayWindowFrame helper with NOACTIVATE/NOZORDER/NOOWNERZORDER. Native peer 33 and production pin 23 checks pass, including foreground preservation, passive child focus suppression and exact remembered-control reentry. Evidence: artifacts/winui-shell/slider-settle-20260929/native-pinned-final-04 and pinned-production-final-02.json. No focus parking remains.

## New playback/persistence audit — initial findings

Host main/pinned controls share WidgetViewPresenter; busy controls remain natively enabled, action admission rejects IsBusy, and AsyncRelayCommand uses AllowConcurrentExecutions so pending IPC does not disable the native button. A separate host busy exception is not warranted from source inspection. Spotify's optimistic Play/Pause policy changes IsPlaying while keeping state-relative DisallowedActions; the renderer switches between Pausing/Resuming restrictions using the optimistic state. This can make the opposite operation appear disabled from the preceding provider state. Widget owner should reproduce both main/pinned declarations with realistic state-relative restrictions; generic busy fixtures use unrestricted fake playback and would miss that path.

YouTube Music artwork omission is widget-side: Player builds artwork but only adds its frame under `if (pane)`, so compact/pinned declarations contain no image. Current shared host path accepts HTTPS ImageSource for both views; no source-proven host artwork delivery defect found. Widget agent has reported fixing the omission and shared pinned playback shortcuts.

Saved pin restoration is intentional existing behavior. Startup reads only placement/layout/opacity, calls EstablishPresentationAsync on the new session target, resolves a fresh projection, selects a fresh pinned lifetime, and starts its presenter input-disabled/automatic-focus-disabled in a passive HWND. Stored data has no command or media/playback state. Audit and focused restoration validation continue; no live actions/build/launch.

Host audit implementation checkpoint (source only): restoration now applies the same full-widget embedded-media exclusion as the tray and does not rewrite durable user placement during startup clamping/DPI reconciliation. Missing/disabled package and unsupported pin capabilities are skipped before worker demand; a missing layout already exits through the existing bounded pinned_input_stale path, not startup failure. No change to intentional saved layout restoration or automatic input/playback authority.

Added `--shell-no-controller --validate-pinned-restore=<result>` read-only startup checks for available, missing/disabled, removed layout and temporarily unavailable YT layout profiles. It verifies fresh session authority, passive input/focus, matching layout/opacity, absence of an embedded Play declaration, and byte-for-byte unchanged saved preferences. A full-provider autoplay proof remains a provider harness/physical concern; the host restores no command or media state.

Added opt-in native Spotify transport verification via `--validate-styles --spotify-busy-fixtures=<directory>`. It consumes the real RendererFixtureExporter format (snapshot + resolved styles), renders the original transport row as a fragment, and uses a local completion gate instead of any provider action. Expected file sets are `Spotify-Busy-<case>-{ready,pending,acknowledged,settled}.renderer.json`; widget owner exports main and compact-pin cases. Checks include pending native IPC, real busy declarations, post-ack state, exact button/focus retention, busy rejection and explicit navigation surviving completion. No runtime widget dependencies were added to the frontend. Awaiting widget fixture export naming/final source and root's combined build/native run.

Restore edge-case detail: absent YT Current => no authored compact layout, and removed layouts, are already rejected as pinned_input_stale and consumed quietly by PinAsync. Disabled/uninstalled entries are absent from the current catalog and skipped. The new early capability guard avoids starting an unsupported widget just to discover rejection. The startup validation can be run against cloned profiles for those cases; no user profile was touched.


2026-10-01 policy update: saved active-pin restoration described above is retired
at the user's request. Geometry and opacity remain preferences for the next
explicit pin action; no ordinary, Browser, or compact-media surface is recreated
on application startup. Overlay hide/show within a session retains its live pin.
