# Scenarios, rendering, and replay

A scenario creates a widget with a known state and test host services.
`WidgetScenarioDefinition` describes that setup; `WidgetScenarioResult` records
its outcome. Use it to check behavior without live accounts or hardware.

The CLI process never loads the assembly into itself. Scenario execution uses
the isolated AppContainer/Job worker path. A failed isolation setup is a failed
test, not permission to run with broader access.

## Choose a declared scenario

Use `wrail preview <project> --scenario <name>`. The basic template declares
`ready`. Other templates or examples can declare names such as `muted` or
`running`; use `--scenario muted` or `--scenario running` only when that scenario
exists in the package. Use the CLI's scenario listing before guessing a name.

Lifecycle transitions are part of behavior, not just rendering. Include creation,
visibility, interaction, background, and destruction where the scenario requires
them. Inspect the generated scenario tests as a starting point.

## Inspect presentation data

The [project example](cli-projects.md) writes a `ready.snapshot.json` fixture.
Use it here:

<!-- canonical-author-journey:render -->
```powershell
& $wrail render `
  .\scratch\VolumeControl\fixtures\ready.snapshot.json `
  --output .\scratch\VolumeControl\fixtures\ready.canonical.json
```

This produces a data representation of the snapshot. It does not certify native
pixels, real controller timing, or a provider connection.

## Replay an input sequence

<!-- canonical-author-journey:replay -->
```powershell
& $wrail replay `
  .\scratch\VolumeControl\fixtures\ready.snapshot.json `
  .\scratch\VolumeControl\replays\smoke.json
```

The replay checks behavior expressed by that fixture. It is useful for stable
actions, focus, and supported navigation, but cannot replace physical acceptance
for Windows, audio devices, video playback, or controller compatibility.

Keep fixtures free of credentials and user data. For full command options, see
the [CLI reference](../../tools/WrailCli/README.md).
