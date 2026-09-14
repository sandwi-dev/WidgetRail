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
.NET SDK, the CLI/SDK release pairing, and the packaged host files. Failures
include a suggested fix. Add `--host <OverlayHost.exe>` to choose a host, or
`--json` for structured output. The check does not build your project or launch
the overlay; it does not test controller hardware or widget permissions.

`wrail dev . --log .\development.log` saves this invocation's build and lifecycle
messages to a new file. The terminal identifies each generation, its current
phase, readiness and an unexpected host exit. Failed builds retain the last
working widget. The transcript stops at 2 MiB, or on a write failure, while
terminal output continues. Existing log files are never overwritten; choose a
new filename for each run. These are CLI messages, not a capture of all host or
widget runtime logs.

`preview`, `render`, and `replay` are different tools: a scenario runs widget code,
render inspects presentation data, and replay applies declared inputs to that data.
They are not all graphical screenshots. Read [Scenarios](cli-scenarios.md) before
using their output as evidence of a feature.

## Inspect a running development widget

```powershell
& $wrail dev . --inspect
```

Use a host build that supports the inspector; `--host <OverlayHost.exe>` selects
a particular packaged build. The inspector opens beside the development
overlay, opens the requested widget, and follows its committed ordinary widget
frames. When the overlay hides, the last captured frame remains available and
is labeled inactive.

Select a node in the tree or the layout map. The details pane shows rendered
bounds, clipping, resolved layout and paint styles, explicit focus links,
navigation eligibility, and scroll/cursor state. Blue marks the inspected node;
green marks widget focus. Inspecting a node does not focus or activate it in
the widget. The navigation summary reports the host's latest directional
resolution, including geometry, explicit links and cursor waits.

Use **Pause capture** while reading a changing view. Close the inspector
independently; press **F12 in the overlay** to reopen it. Rendered frame display
updates at most four times a second, with at most 2,048 nodes per frame.
Capture is off in ordinary overlay sessions.

The map shows layout geometry, not captured application pixels. It excludes
text-entry values, artwork handles and media/window identifiers. Render timings
are CPU timings from the production renderer; enabled inspection adds its own
capture overhead. This first inspector is read-only and follows the ordinary
development widget, not pinned windows or the full compositor.

For the complete command syntax, run `wrail help` or use the
[CLI reference](../../tools/WrailCli/README.md).
