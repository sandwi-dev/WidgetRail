# Shell memory across native eviction

The shell retains three native widget presenters. Separately, `WidgetStateHistory`
retains at most 256 lightweight presentation mementos, ordered by recent capture
or successful lookup. This history contains the presenter's bounded semantic
identities and anchors; it owns no controls, worker leases, previews or artwork.
The history lasts only for the current shell session.

Before suspending an active, loaded, visible widget, the shell captures its state.
Ordinary native LRU eviction leaves that record intact. A recreated presenter first
applies the current genuine frame while inactive, restores matching memory once,
then becomes visible and resumes. Existing retained presenters keep their live
native state and never replay an older memento during entry, lifecycle changes,
or ordinary publications.

The memory owner includes exact widget ID, instance, runtime, presentation and
package-content identity. A known owner/package replacement removes old memory;
an absent widget is removed only when the catalog is complete. The presenter
additionally validates session identity and current semantic/query declarations.
Shell close clears the lightweight history along with native surfaces.

`Overlay.Status` HelpText reports `presentationMemoryCount` and
`memoryRestoreCount` alongside the existing retained-surface count. These are
test diagnostics, not user-facing settings.

`Test-WinUiWidgetRetention.ps1` accepts an optional `EvictWithWidgetIds` array.
Choose at least three distinct widgets other than Playnite to force its native
presenter out of the three-entry LRU. The scenario checks the deep game's identity
and screen position, fresh actionable content, tray ownership, the memory restore
counter, both cache bounds, and retained reentry without stale memory replay. It
opens game details only and never invokes Play/Install or music playback.

Managed tests cover bounded LRU behavior, exact IDs, replacement of each owner
field, incomplete catalog retention, confirmed removal, explicit retirement and
repeated lookups. Native eviction integration remains a separate deployment gate.

`Test-WinUiWidgetSwitches.ps1 -SoakCycles 1000` extends the existing isolated
synthetic fixture with repeated selection across four widgets (native cache limit
three), and hide/reopen after every fourth switch. It checks cache/preparation
bounds, recovery state and committed surfaces. Every20 cycles it records frontend
private bytes, working set, managed heap, handles and retained-surface counts without
forcing GC. The output is an observation, not an automatic memory-plateau pass:
JIT, native caches and GC can change process usage. Worker/browser/GPU resources,
real artwork-heavy collections and physical frame pacing require separate coverage.

## Timed process-tree soak

`Test-WinUiWidgetSwitches.ps1 -BridgeInstallation <staged-installation>
-OutputDirectory <fresh-directory> -SoakSeconds 1800` runs the same isolated
four-widget workload for thirty minutes. Native realization/retirement, delayed
preparation, owner replacement and failure/recovery assertions remain enabled.
Every fourth switch hides and reopens the shell. No forced GC is used.

The runner verifies deployed DLL hashes and starts the read-only process-tree
observer at five-second intervals. `process-tree.json` includes per-process private
commit, working set, handles and CPU (percentage of one core), with PID/start-time
identity checks. It follows the frontend, broker and synthetic workers, including
known descendants whose original parent exits. The observer exits after the owned
frontend closes and retains its report. Native samples/result.json remain separate.

Interpret these as observations of this workload. They do not establish GPU memory,
frame pacing, artwork-heavy grid behavior, browser lifetime or a comparative native
renderer budget. A short pilot verifies the observer itself; it is not a substitute
for the full duration. Evidence is under `resource-lifetime-20260930` in winui-shell.
