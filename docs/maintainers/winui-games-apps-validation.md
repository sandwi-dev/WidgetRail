# Games & Apps native validation

Library and Running use the SDK's captured indexed queries. Catalog retains its
explicit provider page controls. The integrated managed suite passes 92 tests,
including captured action identity, stale queued commands, curation/CAS behavior,
query retirement, deep reads, focus requests and compiled production styles.

`New-WinUiBundledWorkspace.ps1 -WidgetId games-apps -OutputDirectory <new-path>`
publishes the current bridge, generic worker and sealed bundled package into a new
isolated installation/profile. It does not modify the installed product. A fresh
profile correctly displays the permission-required state. For the native browse
check, the isolated profile grants only `system.apps.library.read.v1` and
`system.apps.running.read.v1`; launch and running-registration remain ungranted.

`Test-WinUiGamesApps.ps1` checks real startup, grid Down navigation, tray reentry,
Library artwork, Running observations and Catalog layout. It never invokes an
application tile. All six checks pass against the actual broker/provider, with
screenshots inspected. Evidence: `artifacts/winui-games-apps/native-current-entry/`.

## Reproduced integration defects

Native ListView activation can focus the tray item after `ItemClick` starts an
asynchronous widget entry. The tray focus event used to downgrade the worker back
to Visible, leaving all Games & Apps controls disabled. The shell now preserves
the selected widget's interaction intent during that entry interval. Ordinary
tray focus outside entry still downgrades it correctly.

After correcting that ordering, a second issue became visible: entry skipped
retained disabled rows while their current Interactive revision was loading,
eventually focusing item 32 instead of remembered item 3. Indexed navigation now
waits for the requested row's current lease before interpreting availability.
A failed row cancels the navigation intent; a genuinely current disabled row still
uses the established fallback policy. The test explicitly returns from disabled
Visible-state content and verifies focus on the same game.

The original and intermediate failed evidence is preserved under `production/`,
`native-entry-fixed/` and `native-focus-followup/`. Optional layout diagnostics
include a bounded last-eight focus-transfer trace; this is not continuous per-frame
logging. Physical controller and sustained performance acceptance remain separate.

## Tile layout

Ordinary horizontal action surfaces now use native Grid tracks, including the
declared growing text column. The former horizontal StackPanel measured that
column without a finite width, clipping long titles at the tile edge. Static
responsive grid cells also stretch to their allocated column instead of retaining
native Button's content-sized default. Poster surfaces retain their poster panel.

The native style fixture checks ordinary and indexed action-surface wrapping,
ellipsis and physical-pixel column allocation. The six real Games & Apps checks
also pass with inspected Library, Running and Catalog screenshots in
`artifacts/winui-games-apps/native-layout-final/`. No app/game was launched.
