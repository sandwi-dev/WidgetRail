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

For the complete command syntax, run `wrail help` or use the
[CLI reference](../../tools/WrailCli/README.md).
