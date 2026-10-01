# Third-party dependencies

The WinUI frontend uses Microsoft Windows App SDK for XAML layout, controls,
composition and accessibility. WebView2 presents embedded web media. Exact NuGet
versions are declared in the consuming projects and audited during restore.

The C++ boundary in `src/OverlayPlatformInterop` consumes Microsoft.GameInput
from `NativeDependencies.csproj`. Controller isolation also uses the vendored
ViGEmClient sources under `third_party/ViGEmClient`. Runtime and driver
installation are separate from compilation and verification.

Window previews use Windows Graphics Capture and Direct3D through
`src/WinUiWindowPreviewNative`. Both native DLLs use the static C runtime and
are published explicitly with the matching frontend build.

The frontend ships self-contained .NET 10. Managed widget services retain their
private .NET 8 Core runtime; Spotify and YouTube Music playback helpers also
need the .NET 8 Desktop runtime. Removing the retired renderer does not remove
those active helper dependencies.

Taffy and the Rust layout bridge are no longer part of the product or build.
WinUI owns layout. Redistribution notices are retained in
[`THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md).
