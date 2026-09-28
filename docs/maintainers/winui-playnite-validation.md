# Real Playnite migration workspace

`scripts/New-WinUiPlayniteWorkspace.ps1 -AcceptFullTrust` creates a new isolated
installation, catalog and bridge profile under `artifacts/winui-playnite/`.
It publishes the current Bridge, generic worker and trusted Settings worker;
Settings assets receive the normal integrity seal. It builds the actual Playnite
package and installs/enables it through wrail's ordinary full-trust admission.
No installed native frontend/package is replaced. Existing output roots are rejected.
The script does not launch an overlay or acquire a controller.

The output `shell-options.json` supplies InstallationRoot, SettingsRoot,
InstalledCatalogRoot and InitialWidgetId to the WinUI shell. Package SHA-256,
source commit and build logs remain beside it. Full-trust application state and
credentials are owned separately by the application; an isolated bridge profile
alone does not relocate them. The headless probe below uses Playnite's existing
`WRAIL_PLAYNITE_LIBRARY_DATA_ROOT` override to isolate its organization state.
Its normal Credential Manager lookup remains unchanged; it never prints or copies
the credential. A shell launch must deliberately choose normal or isolated app
state for its validation scenario.

## Transport and real-data check

Build `tools/WinUiPlayniteProbe/WinUiPlayniteProbe.csproj` with a unique binlog,
then run its executable with the options path and report path. It uses
OwnedBridgeProcess and WidgetPresentationSession, waits for normal publications,
acquires bounded Home/Library ranges, resolves real cover artwork, opens details
only after checking the exact details action, then dismisses via B. It never
activates Play/Install or mutates game metadata. Reports contain structural IDs,
counts, style vocabulary and artwork byte counts, not credentials or provider bodies.

`--diagnose-refresh` is an explicit diagnostic mode. It performs one extra snapshot
request after startup to distinguish missing notifications from unfinished provider
work. A passing run in that mode alone is not normal startup evidence.

## 2026-09-28 checkpoint

The initial run remained on loading despite the worker finishing. A diagnostic
refresh exposed completed data: activation invalidations arrived before the first
snapshot and the session discarded them because its state did not exist yet.
Establishment now reserves state before the request and drains coalesced activation
invalidations after publication. The new regression failed before the correction;
all 74 session tests pass afterward.

Normal real-data checks subsequently passed, including a run with isolated
application state: 36 Home items, 178 Library items, nonempty cover artwork, and
details opening/dismissal on both pages. Evidence is under
`artifacts/winui-playnite/production-first/`. These are transport/data checks;
native rendering, controller navigation, appearance and performance require the
actual shell and are not implied by this result.

The subsequent `production-warm` workspace includes the indexed saved-display
startup correction. Both first-run and saved-state real-provider probes pass.
The native shell cold-started without Retry, showed actual cover images, moved
Library focus from item 0 to item 6 at 125% DPI, and opened that game's details
with Install focused without invoking it. Screenshots and sanitized layout
diagnostics are in that workspace. This is functional/layout evidence, not a
controller or smoothness acceptance result.

`scripts/Test-WinUiPlaynite.ps1 -AppPid <owned test PID> -OutputDirectory <new path>
-Page Home|Library -CloseAfter` checks cold startup and real rows, opens details,
verifies initial Play/Install focus without invoking it, and captures page/details
screenshots for visual review. Run each page against a fresh shell; the script
never invokes Retry, game launch/install, or metadata mutations. It does not replace
physical controller, sustained-scroll, DPI-change or performance validation.

First scripted results: Home passes five checks. Library passes startup, navigation,
row readiness, capture and shutdown, but its details-open wait failed once; a later
direct run succeeded. Preserve `production-warm/library-smoke/results.json` as the
failed evidence. Activation timing remains under investigation; do not present the
earlier successful manual sequence as a completed repeatability gate.

For explicit geometry diagnosis, add `LayoutDiagnosticsPath` to shell-options.json
with an absolute output path in an existing directory. The shell coalesces captures
after publications and writes control geometry/style metadata without text, image
handles or image bytes. Omit this option for normal runs and performance measurement.
It is off by default and is not a screenshot or proof of actual pixel appearance.
