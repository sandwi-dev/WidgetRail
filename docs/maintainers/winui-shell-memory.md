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
