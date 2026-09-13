# Installer build contract

Production and Developer installers use one per-user Inno Setup AppId. Installing
either edition updates the same Start menu entry and uninstall entry. Files live
under `%LOCALAPPDATA%\Programs\WidgetRail\versions\<version-edition-commit>`.
The active application root is recorded in
`HKCU\Software\WidgetRail\Installation`. Existing user data under
`%LOCALAPPDATA%\WidgetRail` is never removed by setup or uninstall.

Previous version payloads remain installed until uninstall. This avoids deleting
working binaries during an interrupted upgrade and keeps file removal restricted
to Inno's installation log. Only the newest edition is registered and launched by
the Start menu or startup entry. Automatic old-version cleanup is not implemented.

## Build

Run `scripts/Get-InstallerCompiler.ps1` once to extract the checksum-pinned,
signed Inno Setup 6.7.3 compiler into an ignored tools directory. It does not
install a system tool. The NuGet tools package is an upstream compiler repack;
the entire archive checksum is pinned before extraction, and the executable's
Authenticode signature is checked before use.

From a clean committed checkout, run `scripts/Build-Release.ps1`, then
`scripts/Build-Installer.ps1 -ReleaseRoot <version-directory> -CompilerPath <ISCC.exe>`.
The installer builder validates both editions and copies them to short private
temporary paths for Inno's source-path limits. It validates them again after
compilation and publishes both installers with checksums atomically. Existing
outputs cannot be replaced. Build metadata records application and packaging
source revisions separately. Temporary build evidence is retained.

Setup requires Windows 10 build 19041 or later, x64 app compatibility, the
machine-registered .NET 8 x64 runtime, and GameInput.dll in System32. Missing
prerequisites stop installation with download guidance. Custom private .NET
installations are not detected. WebView2 is additionally required by web media.
Runtime provisioning, signing and optional controller driver installation are not
part of this installer. Developer tools need the .NET SDK to compile widgets.

## Startup and process lifetime

The checkbox and Settings > Overlay share `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WidgetRail`.
Its value is exactly the quoted active `OverlayHost.exe` path, with no `--show`.
Fresh installation defaults off; upgrades read the actual entry instead of a
saved installer task choice. Windows-owned StartupApproved values are never
written or deleted, including on uninstall. Settings reports known disabled
states and treats unrecognized approval data as unknown.

Only the trusted Settings worker gets this service; the public widget capability
surface does not gain registry access. The service checks that the worker's
application directory matches the registered installation. Development copies
cannot redirect the installed startup entry. Registration failures become UI
feedback. Uninstall removes the startup entry only when it exactly matches this
installation, and retains user settings, widgets and caches.

Setup and uninstall require WidgetRail to be quit by the user. The native process
holds a lifetime mutex until cleanup finishes; it refuses a new launch while setup
is open. Setup also checks for the older host window class. Neither path force
terminates an overlay that could own controller isolation.

## Validation boundaries

Run the PlatformSettings and SettingsWidget executable test suites using the
repository's binlog-enabled build workflow. Startup tests use fake storage only.
`scripts/Test-InstallerContract.ps1` checks the shared registry/process contracts
and non-destructive installer settings. Compile both editions for Pascal syntax
and payload validation. Installer execution, upgrades, edition switching,
Windows-level startup disablement and uninstall still require user acceptance on
a disposable Windows profile or VM; do not automate those against a live profile.
