# Passive shell status

`ShellStatusView` restores the shared time/date, Internet and Bluetooth display
beside the rail. It is a passive native control with one text automation peer;
its decorative children cannot take focus or invoke widget actions. The rail owns
its width allocation. Symbols disappear in compact allocations before the date;
a native down-only Viewbox fits the complete clock rather than truncating it.

The existing bridge shell palette adds `tray-clock`, `tray-date` and
`tray-status-icon` roles. Their WRSS classes are `wrail-tray-clock`,
`wrail-tray-date` and `wrail-tray-status-icon`. The default theme supplies sizes
and colors; the shared style adapter applies text scaling and High Contrast.
Clock/date formatting uses the current user culture. State marks and the accessible
summary distinguish offline, limited, unknown, radio off and absent radio states.

System reads use the same read-only Windows radio/connectivity APIs as the original
shell, off the UI thread. There is no playback, network connection or radio change.
Only a visible active surface starts the five-second timer. Hiding or unloading
cancels the observation; late results cannot publish into a new visibility epoch.
The observation deadline is four seconds, and stale results become unknown after
30 seconds. A provider that ignores cancellation cannot accumulate overlapping
reads or hold shell shutdown indefinitely. Its eventual exception is observed.

The pure status policy is part of the 50-test shell suite. The native
`--validate-shell-status` fixture passes 12 checks for semantic automation, compiled
theme sizes, text scaling, compact width, cancellation, late completion, overlap
prevention and bounded shutdown. It uses deterministic provider replies; these
checks do not establish real-system radio availability or production rail placement.
Production integration attaches the view to `RailStatusHost`, applies the shared
palette and active visibility, and awaits `DisposeAsync` with the shell lifetime.

The combined production shell now owns that integration. Hidden overlays,
fullscreen playback and a collapsed narrow-layout status slot suspend observation.
Shutdown retires status before awaiting bridge startup or disposal. Geometry-only
fixtures use a deterministic unknown reader rather than accessing system radios.
The wrapper stays transparent; only clock/date/icon roles receive theme styles.

At the combined checkpoint, the analyzer build, 63 managed shell checks, 82 native
production geometry checks and 12 native status checks pass. The normal and compact
status screenshot was inspected. Evidence is in `artifacts/winui-shell/`:
`combined-production-01`, `status-combined-result.json`, `status-combined.png` and
`combined-managed.log`. These checks do not establish physical or mixed-monitor
acceptance.

Evidence: `artifacts/shell-status/` in the status worktree. The first non-screen
screenshot returned an empty composition surface and is not visual parity evidence;
use a foreground `--capture-screen` image for the combined shell review.
