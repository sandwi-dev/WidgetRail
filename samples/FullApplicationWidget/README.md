# Full Application reference widget

This optional sample shows how a widget can keep an application-scale private
model while submitting only a bounded presentation to the shared host. It is
not registered as a built-in tray widget.

The deterministic `ReferenceLibrary` owns 10,000 immutable private records. Its
complete, exact-count `WidgetIndexedCollection<Query, Document>` exposes only a
lazy collection declaration in the parent snapshot. The host requests bounded
ranges (at most 64 items), realizes native rows and owns scroll/focus geometry.
Opening the widget does not read or render all 10,000 rows. Unlike a discovered
provider, this local immutable model supports arbitrary exact range access;
there is no invented remote total, cursor window or 1,024-result discovery cap.

`WidgetNavigator<T>` owns Library/Details routing. Row actions execute the item
captured by the admitted SDK lease and recheck its current query/route, rather
than parsing an element ID. Back retains `FocusedCollectionItem` and issues one
keyed collection-entry request using the navigation revision. Repeated renders
do not repeat that request. Refresh uses `UpdateContent` because this sample's
count, ordering and keys are unchanged; a real membership change must use
`PublishQuery` with its new exact count. Old leased commands are rejected.

Range failure retains the logical list and shows a safe Retry action. Retry and
Refresh retire the content revision and allow new host range demand; they do not
replace the list with an eager error/loading tree. Backgrounding cancels current
reads and retires stale actions while preserving logical membership and return
targets. Destruction relies on the SDK's already-cancelled widget lifetime. A
cancelled provider keeps its real SDK capacity until it finishes; sample reads
honor cancellation. The tests acquire actual SDK ranges and route leased actions,
including item 9,999, reverse access, refresh, error/retry and lifecycle cancellation.
Native viewport behavior is tested by the shared WinUI collection fixtures.

The sample requests no capabilities and performs no filesystem, network, or
process operations. Both sandboxed and full-trust workers use this same SDK
collection contract. This sample remains the sandboxed AppContainer example.

Use the current WinUI host and its matching SDK feed for indexed collections.
The old installed native renderer is not a compatible target for this updated
sample; exporting it from an older `wrail` feed cannot supply the new SDK types.

Export a self-contained external repository from a complete `wrail` distribution:

```powershell
pwsh -NoProfile -File .\samples\FullApplicationWidget\Export-ExternalReference.ps1 `
  -Wrail .\tools\WrailCli\bin\Release\net8.0\wrail.exe `
  -Output .\scratch\ExternalFullApplication
```

The output owns its local offline SDK feed, source, manifest, styles, and bounded
credential-free scenario. It contains no checkout path or project reference;
follow the external restore/build/validate/pack/install/scenario/remove commands
in the [quickstart](../../docs/reference/cli-packages.md).

Build and test from the repository root:

```powershell
dotnet build .\samples\FullApplicationWidget\FullApplicationWidget.csproj -c Release -bl:{{}}
dotnet test --project .\tests\FullApplicationWidget.Tests\FullApplicationWidget.Tests.csproj --configuration Release --no-ansi --progress off --output Detailed --minimum-expected-tests 5 -bl:{{}}
```

Validate and package the already-built payload with the public CLI workflow:

```powershell
New-Item -ItemType Directory -Force .\artifacts\full-application\payload | Out-Null
Copy-Item .\samples\FullApplicationWidget\bin\Release\net8.0\FullApplicationWidget.dll .\artifacts\full-application\payload\
Copy-Item .\samples\FullApplicationWidget\manifest.json .\artifacts\full-application\
Copy-Item .\samples\FullApplicationWidget\styles .\artifacts\full-application\styles -Recurse
dotnet run --project .\tools\WrailCli\WrailCli.csproj -c Release -- validate .\artifacts\full-application
dotnet run --project .\tools\WrailCli\WrailCli.csproj -c Release -- pack .\artifacts\full-application --output .\artifacts\FullApplicationReference.wrwidget
```

The installed acceptance route uses the same manifest and assembly through the
generic AppContainer worker. Application state remains private to that worker;
only protocol-bounded immutable snapshots cross into the host.
