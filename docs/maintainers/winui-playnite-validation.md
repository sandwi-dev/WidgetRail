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
