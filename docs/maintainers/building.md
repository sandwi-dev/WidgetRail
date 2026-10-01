# Build WidgetRail from source

WidgetRail uses a WinUI 3 frontend, a small C++ Windows platform boundary,
and isolated managed widget services.

## Development tools

- Windows x64 and PowerShell 7.
- .NET SDK 10.0.302 selected by `global.json`, and .NET 8 Core and Desktop runtimes for widget services and playback helpers.
- Visual Studio or Build Tools with Desktop development with C++, MSVC x64 tools, and a Windows SDK.
- Network access for audited NuGet restore.

The frontend uses Windows App SDK. GameInput supplies the controller backend;
embedded web media uses WebView2. Building does not install or register runtimes,
change certificate trust, or install controller drivers.

## Build and verify

From the repository root:

```powershell
.\scripts\Build-WinUiVerification.ps1 -Configuration Release
```

This builds the platform and preview DLLs from source, runs their safe native
policy checks, and publishes the trimmed, self-contained .NET 10 frontend to a
fresh directory under `artifacts/winui-verification`. Its receipt verifies that
the published native DLLs match those just built. A metadata-only check also
rejects the known linker regression that removes async session lock cleanup.
The targeted optimizer workaround and removal criteria are documented in
[Release readiness](winui-release-readiness.md). It does not launch the overlay.
Rust, Taffy, and the retired native renderer are not in this build graph.

Managed widget services have their own coherent installation check:

```powershell
.\scripts\Test-WidgetBridgeRuntime.ps1 -Configuration Release
```

For local UI development, build a workspace and launch the unpackaged frontend:

```powershell
.\scripts\Build-OverlayPlatform.ps1 -Configuration Release
.\scripts\Build-WinUiWindowPreview.ps1 -Configuration Release
.\scripts\New-WinUiBundledWorkspace.ps1 -WidgetId media-sessions -IncludeSettings -Configuration Release `
  -OutputDirectory .\artifacts\local-widget-workspace
dotnet run --project .\src\OverlayFrontend.WinUI\OverlayFrontend.WinUI.csproj -c Release -p:Platform=x64 `
  -- "--shell-config=$((Resolve-Path .\artifacts\local-widget-workspace\shell-options.json).Path)"
```

The native commands produce the frontend's default inputs at
`artifacts/winui-platform/Release/OverlayPlatformInterop.dll` and
`artifacts/winui-preview/Release/WinUiWindowPreviewNative.dll`. The verification
script above uses its own fresh output directory; it does not populate these
development paths. Build and publish fail early when a native input is missing;
restore and IDE design-time evaluation can run before native compilation.
To use another freshly built pair, pass `-p:OverlayPlatformInteropPath=<absolute-dll>`
and `-p:WindowPreviewNativePath=<absolute-dll>` to `dotnet run`.

Choose a fresh workspace directory and use the absolute shell-configuration path
reported by the script. This isolates widget settings and catalog data from your
installed profile. Launching requires the matching native libraries, coherent
widget installation and shell configuration. See the frontend
[README](../../src/OverlayFrontend.WinUI/README.md) for its development modes.
Do not copy a single executable out of its runtime directory.

## Build only the CLI

```powershell
dotnet build .\tools\WrailCli\WrailCli.csproj -c Release "-bl:cli-$([guid]::NewGuid().ToString('N')).binlog"
& .\tools\WrailCli\bin\Release\net8.0\wrail.exe help
```

Keep the complete output directory. Widget authors can continue with
[Your first widget](../developers/widget-quickstart.md).

## Verify and package

[Contributing](../../CONTRIBUTING.md) explains managed, native, and focused lanes.
[Build execution](build-execution.md) covers restores and diagnostic logs.
[Release preparation](release-checklist.md) describes coherent installer payloads.
