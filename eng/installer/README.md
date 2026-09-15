# Installer build contract

Production and Developer installers use one per-user Inno Setup AppId. Installing
either edition updates the same Start menu entry and uninstall entry. Files live
under `%LOCALAPPDATA%\Programs\WidgetRail\versions\<version-edition-commit>`.
The active application root is recorded in
`HKCU\Software\WidgetRail\Installation`. Setup preserves existing user data.
Uninstall keeps data by default and offers an explicit option to delete it.

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

Setup requires Windows 10 build 19041 or later and x64 app compatibility. Each
edition contains private .NET 8 base and Windows Desktop runtimes, selected through process-local
DOTNET_ROOT/DOTNET_ROOT_X64 by the native host. Isolated workers inherit that
selection and receive read/execute access only to the private runtime actually
hosting the bridge. Global .NET permissions and environment variables are not
changed. Both editions include the CLI. The root `wrail.cmd` uses the private runtime; building
widgets still requires a separately installed .NET SDK.

`eng/runtime-dependencies.json` pins vendor downloads and hashes. The build checks
Microsoft signatures and includes .NET license/notices. Its .NET patch version is
serviced with WidgetRail releases and should be reviewed for each public build.
The GameInput MSI must match NativeDependencies.csproj. Setup checks compatible
DLL versions in Windows and the documented GameInput RedistDir, then uses the
signed MSI with a Windows elevation prompt only when necessary. The supported
minimum is independently recorded in `eng/installer/requirements.json`:
3.3.221.0, the redistributable used for the existing controller acceptance checks.
It supports the v3 API used by the host. The SDK/package version is not the
runtime compatibility floor; Windows' legacy 0.x GameInput does not satisfy it.
The Ready page explains that a stalled vendor update may require a normal PC
restart. MSI diagnostics overwrite `%LOCALAPPDATA%\WidgetRail\logs\GameInput-setup.log`
on each attempt; setup also records the vendor exit code. Cancellation,
errors and restart-required results stop setup with retry guidance. There is no
watchdog, forced termination, automatic reboot, or overlapping MSI retry. It does not
uninstall shared Microsoft components.

WebView2 is detected in per-user and machine-wide EdgeUpdate registrations. If
missing, setup runs Microsoft's signed Evergreen bootstrapper as the installing
user. That download requires internet. Failure stops setup with retry guidance.
Neither Microsoft runtime installer is run during automated packaging tests.
Optional HidHide/ViGEm drivers remain outside basic setup. Application installers
remain unsigned until release signing is configured.

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
installation. The empty WidgetRail installation registry parent is also removed;
unrelated values or subkeys prevent that removal.

## Uninstall data choices

Interactive uninstall asks whether to delete **all WidgetRail data for this
Windows account**, including data shared with development copies. **No** is the
default; Cancel stops uninstall. Silent uninstall always keeps data. Explicit Yes
removes data only after payload removal and only when this uninstaller owns the
active installation registration:

- `%LOCALAPPDATA%\WidgetRail`: installed community widgets, settings, permissions,
  sign-ins, artwork caches, diagnostic logs and other application data.
- `%TEMP%\WidgetRail`: WidgetRail temporary files.
- WidgetRail's isolated-worker profiles: only names with the exact
  `widgetrail.widget.` prefix followed by 32 hexadecimal characters. These are
  removed using Windows' `DeleteAppContainerProfile`, not by deleting arbitrary
  package folders or registry mappings.

Directory junctions and symbolic links inside the two application data roots
are unlinked without traversing their targets. Missing paths are harmless;
locked/read-only data or profile cleanup failures are reported rather than
claiming complete deletion. Uninstall does not remove GameInput, WebView2,
optional controller drivers, Windows-owned StartupApproved entries, or other
applications' data. Standard Windows installation logs are left to Windows.

When data is kept, the completion message explains how to remove it later:
press Win+R, open `%LOCALAPPDATA%`, and delete only its `WidgetRail` folder. The
`WidgetRail` folder inside `%TEMP%` may also be removed. These manual folder
deletions do not unregister Windows sandbox profiles; for that complete cleanup,
reinstall WidgetRail and choose Yes to delete all data when uninstalling. Close
all WidgetRail/development copies before either form of cleanup.

Setup and uninstall require WidgetRail to be quit by the user. The native process
holds a lifetime mutex until cleanup finishes; it refuses a new launch while setup
is open. Setup also checks for the older host window class. Neither path force
terminates an overlay that could own controller isolation.

## Validation boundaries

Run the PlatformSettings, SettingsWidget and WidgetRuntime executable test suites using the
repository's binlog-enabled build workflow. Startup tests use fake storage only.
`scripts/Test-InstallerContract.ps1` checks the shared registry/process contracts
and installer settings. `scripts/Test-InstallerDataCleanup.ps1 -CompilerPath <ISCC.exe>`
compiles the actual Pascal deletion routines into a fixture executable that
exits before any installation or wizard UI. It exercises synthetic nested data,
root/nested junctions with outside sentinel files, preserved siblings, missing
paths, read-only failures and profile-name ownership checks. It does not call
the real profile cleanup or touch real WidgetRail data. Compile both editions for Pascal syntax
and payload validation. Installer execution, upgrades, edition switching,
Windows-level startup disablement and uninstall still require user acceptance on
a disposable Windows profile or VM; do not automate those against a live profile.

`scripts/Test-BundledRuntime.ps1 -RuntimeRoot <private-dotnet-directory>` copies
the runtime and built WidgetRuntime tests into a temporary packaged layout and
checks host startup and real AppContainer worker isolation using the private
runtime. It does not install prerequisites or change startup configuration.

After building `SpotifyPlaybackClient.Tests`, its `--private-runtime <dotnet-folder>
--playback-host <SpotifyPlaybackHost.exe>` probe starts the packaged playback helper
with that private runtime, waits for WebView2 initialization, and closes it. It
sends no account credentials or playback command. This catches a missing Desktop
framework that a base-runtime worker check cannot detect.

Release folders record their compiled source and packaging revisions separately.
Packaging-only changes can reuse verified build inputs when the native/managed
sources are unchanged; record both revisions and validate both catalogs again.
