# Full Application reference widget

This optional sample shows how a widget can keep an application-scale private
model while submitting only a bounded presentation to the shared host. It is
not registered as a built-in tray widget.

The deterministic `ReferenceLibrary` owns 10,000 private records. A public
`WidgetCursorResource<T>` projects 32 records per request and retains at most 96
rendered records, while `WidgetNavigator<T>` owns the Library and Details route,
Back focus restoration, and route cancellation. The widget also demonstrates a
safe load error, retry, explicit refresh, and Active-lifetime cancellation and
drain. It requests no capabilities and performs no filesystem, network, or
process operations.

Export a self-contained external repository from a complete `gbar` distribution:

```powershell
pwsh -NoProfile -File .\samples\FullApplicationWidget\Export-ExternalReference.ps1 `
  -Gbar .\tools\GbarCli\bin\Release\net8.0\gbar.exe `
  -Output .\scratch\ExternalFullApplication
```

The output owns its local offline SDK feed, source, manifest, styles, and bounded
credential-free scenario. It contains no checkout path or project reference;
follow the external restore/build/validate/pack/install/scenario/remove commands
in the [quickstart](../../docs/widget-quickstart.md#prove-the-workflow-in-an-isolated-catalog).

Build and test from the repository root:

```powershell
dotnet build .\samples\FullApplicationWidget\FullApplicationWidget.csproj -c Release
dotnet test --project .\tests\FullApplicationWidget.Tests\FullApplicationWidget.Tests.csproj --configuration Release --no-ansi --progress off --output Detailed --minimum-expected-tests 4
```

Validate and package the already-built payload with the public CLI workflow:

```powershell
New-Item -ItemType Directory -Force .\artifacts\full-application\payload | Out-Null
Copy-Item .\samples\FullApplicationWidget\bin\Release\net8.0\FullApplicationWidget.dll .\artifacts\full-application\payload\
Copy-Item .\samples\FullApplicationWidget\manifest.json .\artifacts\full-application\
Copy-Item .\samples\FullApplicationWidget\styles .\artifacts\full-application\styles -Recurse
dotnet run --project .\tools\GbarCli\GbarCli.csproj -c Release -- validate .\artifacts\full-application
dotnet run --project .\tools\GbarCli\GbarCli.csproj -c Release -- pack .\artifacts\full-application --output .\artifacts\FullApplicationReference.gbarwidget
```

The installed acceptance route uses the same manifest and assembly through the
generic AppContainer worker. Application state remains private to that worker;
only protocol-bounded immutable snapshots cross into the host.
