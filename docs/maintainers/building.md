# Build WidgetRail from source

Use this guide when you want to work on the platform or try a build before a
download is published. Widget authors can normally use the installed CLI instead.

## Install the development tools

- Windows x64 and PowerShell 7.
- .NET SDK 10.0.302, as selected by `global.json`, plus the .NET 8 runtime.
- Visual Studio or Build Tools with Desktop development with C++, MSVC x64 tools, and a Windows SDK.
- Rust through rustup, including toolchain 1.97.1 and the `x86_64-pc-windows-msvc` target.
- Network access for the first NuGet and Cargo restores.

GameInput is needed for its controller backend. Embedded web media uses
Microsoft WebView2. The application installer can provision missing runtimes;
compiling against an SDK alone does not install the corresponding runtime.

## Build and run the overlay

```powershell
git clone https://github.com/sandwi-dev/WidgetRail.git
Set-Location WidgetRail
rustup toolchain install 1.97.1 --profile minimal --target x86_64-pc-windows-msvc
$env:RUSTUP_TOOLCHAIN = '1.97.1'
pwsh -NoProfile -File .\src\OverlayHost\build.ps1 -Configuration Release -SkipTests
.\src\OverlayHost\out\Release\OverlayHost.exe --show
```

The build script generates the native host and managed runtime together.
Keep the whole output folder; the executable needs its libraries, workers,
catalog, and assets. `-SkipTests` produces a runnable build, not release certification.

Use View + Menu or F1 to open the overlay. Review permissions in Settings → Widgets.

## Build only the CLI

```powershell
dotnet build .\tools\WrailCli\WrailCli.csproj -c Release "-bl:cli-$([guid]::NewGuid().ToString('N')).binlog"
$wrail = '.\tools\WrailCli\bin\Release\net8.0\wrail.exe'
& $wrail help
```

Keep that complete output directory together too. For normal widget development,
continue with [Your first widget](../developers/widget-quickstart.md).

## Verify and package

[Contributing](../../CONTRIBUTING.md) explains the managed, native, and focused
verification lanes. [Build execution](build-execution.md) covers restore failures
and diagnostic logs. Use [Release preparation](release-checklist.md) when producing
an installer rather than a local developer build.
