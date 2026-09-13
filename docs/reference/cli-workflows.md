# CLI workflows

Use the installed `wrail.cmd` or a complete source-built CLI directory. Both
WidgetRail editions include the CLI; it is not automatically added to PATH.
[Your first widget](../developers/widget-quickstart.md) shows how to locate it.

## Choose a task

| Task | Commands and examples |
|---|---|
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

`preview`, `render`, and `replay` are different tools: a scenario runs widget code,
render inspects presentation data, and replay applies declared inputs to that data.
They are not all graphical screenshots. Read [Scenarios](cli-scenarios.md) before
using their output as evidence of a feature.

For the complete command syntax, run `wrail help` or use the
[CLI reference](../../tools/WrailCli/README.md).
