# Third-party dependencies

The WinUI frontend uses Microsoft Windows App SDK for XAML layout, controls,
composition and accessibility. WebView2 presents embedded web media. Exact NuGet
versions are declared in the consuming projects and audited during restore.

Capture Library uses Microsoft.Data.Sqlite and SQLitePCLRaw's bundled native
SQLite for its persistent, paged index and incremental storage totals. The files
remain ordinary PNG/MP4 captures in Pictures; the index and thumbnail cache stay
in the profile. Package versions are pinned in `PlatformBroker.csproj`; their
licenses are retained under `third_party/SQLite` and copied with the outputs.

The C++ boundary in `src/OverlayPlatformInterop` consumes Microsoft.GameInput
from `NativeDependencies.csproj`. Controller isolation also uses the vendored
ViGEmClient sources under `third_party/ViGEmClient`. Runtime and driver
installation are separate from compilation and verification.

Window previews use Windows Graphics Capture and Direct3D through
`src/WinUiWindowPreviewNative`. Both native DLLs use the static C runtime and
are published explicitly with the matching frontend build.

The frontend ships self-contained .NET 10. Managed widget services retain their
private .NET 8 Core runtime. Spotify and YouTube Music use the host WebView2
environment for playback instead of separate Windows Desktop playback helpers.
The distribution still carries its existing Desktop runtime payload.

Taffy and the Rust layout bridge are no longer part of the product or build.
WinUI owns layout. Redistribution notices are retained in
[`THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md).
