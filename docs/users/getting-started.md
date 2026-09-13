# Build and run WidgetRail

WidgetRail currently ships as source. A public binary release and installer
have not been published.

## Choose what you want to build

**Writing a widget?** Start with the [SDK quickstart](../developers/widget-quickstart.md).
The developer CLI can scaffold and test widgets without compiling the native overlay.

**Trying the overlay?** Follow the full build below.

## Full overlay prerequisites

- Windows x64. The Windows-facing managed projects target Windows 10 build
  19041 APIs or later; this API floor is not a complete hardware support matrix.
- PowerShell 7.
- .NET SDK **10.0.302**, selected by the repository's `global.json`.
- .NET **8 runtime** for managed tools and workers. Installing the .NET 8
  SDK alongside SDK 10 is one way to provide it.
- Visual Studio or Build Tools with **Desktop development with C++**, MSVC x64
  tools and a Windows SDK.
- Rust through rustup, including the **1.97.1** toolchain and
  `x86_64-pc-windows-msvc` target for the native layout library.
- Network access for the initial NuGet and Cargo restores.

The GameInput runtime must be available for its controller backend. Embedded
web media also needs the Microsoft Edge WebView2 runtime. Build-time SDK
packages do not establish that these runtimes are installed on a clean PC.
Clean-machine runtime provisioning is still a [release task](../maintainers/release-checklist.md).

HidHide and ViGEmBus are optional prerequisites for **Exclusive control**.
They are not required for ordinary controller navigation; leave Exclusive
control off unless you need it.

## Build

From PowerShell:

```powershell
git clone https://github.com/sandwi-dev/WidgetRail.git
Set-Location WidgetRail
rustup toolchain install 1.97.1 --profile minimal --target x86_64-pc-windows-msvc
$env:RUSTUP_TOOLCHAIN = '1.97.1'
pwsh -NoProfile -File .\src\OverlayHost\build.ps1 -Configuration Release -SkipTests
```

The explicit Rust selection matters when building from the repository root:
the toolchain file lives in the nested layout crate. This command builds the
host and managed runtime together. `-SkipTests` makes a runnable build; it
does not certify a release. See [build execution](../maintainers/build-execution.md)
and [Contributing](../../CONTRIBUTING.md) for verification.

## Run

```powershell
.\src\OverlayHost\out\Release\OverlayHost.exe --show
```

Keep the complete output directory together. Copying only `OverlayHost.exe`
omits its workers, libraries, widget packages and resources.

Use **View + Menu** to show or hide the overlay. Settings lets you choose
Guide instead. **F1** is a keyboard fallback.

Open **Settings → Installed widgets** to review widget permissions. Built-in
widgets also require consent for Windows operations. Some service widgets
must be packaged and installed separately; their READMEs describe setup.

Continue with [Controls](controls.md) and [Known limitations](known-limitations.md).
