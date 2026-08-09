# Clock widget sample

This is the smallest compileable widget-author reference. It demonstrates a
deterministic `Widget.Render()`, stable IDs, explicit controller focus, an `X`
shortcut, an action handler, semantic GBSS classes, and a strict package
manifest without platform capabilities.

Build and validate it from the repository root:

```powershell
dotnet build .\samples\ClockWidget\ClockWidget.csproj -c Release
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release

$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'
& $gbar validate .\samples\ClockWidget
```

Use `gbar dev .\samples\ClockWidget --configuration Release` for executable
integration through the generic AppContainer worker. `gbar render` accepts only
an existing data-only `snapshot.json`; it never loads this sample's DLL.

The separate `ClockWidget.Worker` project demonstrates the public custom-worker
runtime boundary. Installed `.gbarwidget` packages normally use the platform's
generic worker host and need only name this public Widget type in their
manifest.

Continue with the [complete authoring guide](../../docs/widget-authoring-guide.md)
for nested input scopes, host-owned Scroll, surface hints, lifecycle work,
capabilities, packaging, GitHub Releases, isolation, and responsive layout.
Use [YT Music](../YtMusicWidget/README.md) as the advanced state-reconciliation
reference.
