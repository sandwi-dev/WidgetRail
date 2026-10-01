# CLI workflows

Use the installed `wrail.cmd` or a complete source-built CLI directory. Both
WidgetRail editions include the CLI; it is not automatically added to PATH.
[Your first widget](../developers/widget-quickstart.md) shows how to locate it.

## Choose a task

| Task | Commands and examples |
|---|---|
| Check SDK selection and locate the host | `wrail doctor [project-directory]` |
| Generate and build a widget | [Create a project](cli-projects.md) |
| Run a named scenario, inspect a snapshot, replay input | [Scenarios and replay](cli-scenarios.md) |
| Package, install, select versions, and remove | [Package operations](cli-packages.md) |
| Browse GitHub releases and manually review updates | [GitHub packages](cli-packages.md#discover-packages-on-github) |
| Repair a recorded worker-authority cleanup failure | [Authority recovery](../maintainers/authority-recovery.md) |

The examples use a disposable `scratch/VolumeControl` project and package identity
`dev.example.volume-control`. Use an isolated catalog when testing removal and
version changes so you do not modify your normal installed widgets.

## Authoring loop

Change the widget, build, run a scenario, and validate. Use `wrail dev` when you
want the development package served through a running host. It watches the
declared project inputs and rebuilds a development generation.

Start with `wrail doctor .` from your project directory. It checks the selected
.NET SDK, the CLI/SDK release pairing, the WinUI executable, and its complete
Bridge/worker payload. Failures include a suggested fix. Add
`--host <path-to-OverlayFrontend.WinUI.exe>` to choose a WinUI build, or
`--json` for structured output. The check does not build your project or launch
the overlay; it does not test controller hardware or widget permissions.

`wrail dev . --log .\development.log` saves this invocation's build and lifecycle
messages to a new file. The terminal identifies each generation, its current
phase, readiness and an unexpected host exit. Failed builds retain the last
working widget. The transcript stops at 2 MiB, or on a write failure, while
terminal output continues. Existing log files are never overwritten; choose a
new filename for each run. These are CLI messages, not a capture of all host or
widget runtime logs.

The CLI launches `OverlayFrontend.WinUI.exe` directly. It discovers a complete
installation containing the CLI, then checks the current directory's ancestors,
then reads Inno Setup's `ApplicationRoot` value under
`HKCU\Software\WidgetRail\Installation`. Use `--installation-root <complete-payload>`
to select a runtime payload explicitly; its root contains the frontend executable,
`widget-catalog.json`, and `runtime` directory. A separately built debug frontend
uses both `--host` and `--installation-root`. No package registration or
certificate installation is required.

Each generation first runs a hidden probe with its own settings profile. The
last-good visible widget remains open until the exact package instance publishes
a successful snapshot/readiness acknowledgement. The interactive session uses a
separate reusable temporary profile, never your normal overlay settings. Each
launched frontend joins a unique CLI-owned kill-on-close Job Object before
starting child processes; the CLI checks its exact executable path and membership.
Ctrl+C reclaims that process tree and the temporary session catalog. Doctor only
checks discovery and files; successful launch/ownership is verified by dev.

`preview`, `render`, and `replay` are different tools: a scenario runs widget code,
render inspects presentation data, and replay applies declared inputs to that data.
They are not all graphical screenshots. Read [Scenarios](cli-scenarios.md) before
using their output as evidence of a feature.

## Inspect a running development widget

```powershell
& $wrail dev . --inspect
```

Use a WinUI host build that supports the inspector;
`--host <path-to-OverlayFrontend.WinUI.exe>` selects it. For a separately built
debug frontend, `--installation-root <complete-payload>` selects its Bridge/worker
payload. The inspector opens in a separate window from the development
overlay, opens the requested widget, and follows its committed ordinary widget
frames. When the overlay hides, the last captured frame remains available and
is labeled inactive.

Select a node in the tree or the layout map. The details pane shows rendered
native bounds, visibility, resolved styles, explicit focus links and current
focus/scroll state. Theme accent outlines mark inspected and focused elements.
Inspecting a node does not focus or activate it in the widget. Filter by element
ID to narrow the tree. The navigation summary reports the latest direction,
focus before and after, and whether the host handled that input.

Use **Pause** while reading a changing view. Close the inspector
independently; press **F12 in the overlay** to reopen it. Rendered frame display
updates at most five times a second, with at most 1,024 nodes per frame.
Capture is off in ordinary overlay sessions.

The map shows layout geometry, not captured application pixels. It excludes
text-entry values, artwork handles and media/window identifiers. Enabled inspection
adds capture overhead; the inspector does not measure rendering CPU or frame timing.
This inspector is read-only and follows the ordinary
development widget, not pinned windows or the full compositor.

For the complete command syntax, run `wrail help` or use the
[CLI reference](../../tools/WrailCli/README.md).
