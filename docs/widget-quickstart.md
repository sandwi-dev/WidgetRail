# Widget quickstart

Status: implemented developer workflow inside this repository

This guide creates a controller-first C# widget, validates its manifest and
GBSS, renders a protocol snapshot, and replays input without opening the native
overlay.

## Prerequisites

- Windows and PowerShell
- .NET 8 SDK
- A checkout of this repository

The SDK is not currently published as a supported public NuGet package. Run
the quickstart inside this checkout so the generated project can use the local
`WidgetSdk.csproj` reference.

## Build the developer CLI

From the repository root:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'
& $gbar help
```

The widget commands are `new`, `validate`, `dev`, `render`, `replay`, `pack`,
`install`, `list`, `enable`, `disable`, and the `version list|select|rollback`
group. The separate `theme` group provides
`new`, `validate`, `preview`, `pack`, `inspect`, `install`, and `list` for
data-only global themes. Remote install accepts an absolute HTTPS URL or a
deterministic GitHub Release shorthand. There is no GitHub publisher, signing
command, automatic updater, or marketplace client.

## Create and build a widget

```powershell
& $gbar new widget VolumeControl `
  --output .\scratch\VolumeControl `
  --id dev.example.volume-control `
  --publisher dev.example

dotnet build .\scratch\VolumeControl\VolumeControl.csproj
```

For the normal edit/build/overlay loop, replace the manual build with:

```powershell
& $gbar dev .\scratch\VolumeControl
```

`gbar dev` validates the manifest and entry GBSS, runs a bounded child
`dotnet build`, creates an immutable package generation, and launches it through
the packaged native overlay. It does not load the widget DLL in the CLI. The
bridge treats the temporary package exactly like an installed community widget:
the generic worker host runs it in a package-specific AppContainer and the
normal lifecycle, consent broker, controller routing, GBSS compiler, and native
renderer remain in effect. Settings reads the same session catalog, so declared
capabilities can be reviewed and granted through Permissions & capabilities.

Project mode watches the root manifest/project, bounded C# sources outside
`bin`/`obj`/`.git`, nearest `Directory.Build.props/targets`, and GBSS sources.
Package-directory mode watches the complete bounded, reparse-safe input tree
that `gbar pack` consumes, including supporting payload files, assets, and
initially empty/new style directories. Handles are non-recursive per directory;
new source directories are adopted through a bounded recapture, and events are
debounced/coalesced.

A hidden candidate first connects to WidgetBridge and reconciles the exact
widget ID plus installed instance in that unique catalog generation. It owns no
hotkey or controller input, transitions that widget to `Visible`, launches the
generic worker, obtains a protocol-valid snapshot for the exact instance, and
returns it to `Background`. A widget may render a valid permission-denied state;
failure to construct the worker or render a valid snapshot rejects the
generation. Only then may the candidate atomically return its session nonce in
a private readiness record. The last-good overlay is stopped only after that
bounded handshake succeeds; the interactive replacement authenticates the same
generation before `gbar dev` prints `Ready`. Missing or forged readiness fails
closed, while validation/compilation/startup failure retains or restarts the
last-good generation. Ctrl+C verifies that the overlay/bridge/worker process
tree exited and retries temporary-catalog removal. Any unreclaimed process or
directory is reported as a cleanup failure; the normal user widget catalog is
never changed.

If automatic host discovery is not appropriate, select a complete packaged
build explicitly:

```powershell
& $gbar dev .\scratch\VolumeControl `
  --host .\src\OverlayHost\out\Release\OverlayHost.exe `
  --configuration Release `
  --build-timeout-seconds 180
```

The starter contains:

- a strict `manifest.json`;
- a typed `Widget` subclass;
- stable element IDs and focus neighbors;
- controller shortcuts and bounded disabled states;
- optional closed semantic button icons through `.Icon(WidgetGlyph.X)`;
- a safe `styles/default.gbss` file; and
- a deterministic controller replay.

The generated widget uses the root input scope. For a nested modal or component
surface, call `.InputScope("scope-id")` on its Stack/Row, bind focus-independent
actions with `.Shortcut(button, actionId)`, and return that ID through
`WidgetView.ActiveInputScopeId`. Keep root and nested bindings independent; the
runtime never bubbles between scopes.

Publisher names must be lowercase reverse-DNS identifiers that are also valid
C# namespaces. Widget IDs use lowercase reverse-DNS components and may also
contain `_` or `-`.

If the widget needs a platform service, declare only a supported closed ID in
manifest `permissions` (core requirement) or `optionalPermissions` (degradable
feature), then call the typed `HostServices.Audio`, `HostServices.Network`,
`HostServices.AppLibrary`, `HostServices.Media`, or `HostServices.RecentActivity`
API. Required capabilities are not auto-granted. See [widget
capabilities](capabilities.md); do not create raw broker messages or hard-code
operation IDs. The [Network Controls reference](network-controls.md) is the
complete example for event-driven status/available-Wi-Fi subscriptions,
explicit Interactive scanning, current saved/open result connection, Windows
privacy degradation, and transport-free tests.
The [Games & Apps reference](games-and-apps.md) shows paged installed-app reads
and Interactive-only launch using broker-issued opaque IDs. The retained
[Recent Apps reference](recent-apps.md) shows a read-only event stream with
opaque OS identity and no foreground/process authority.

Custom worker executables should delegate host startup to the public runtime
bootstrap instead of parsing pipe or broker arguments:

```csharp
using GameBarAlternative.WidgetRuntime;

return await WidgetWorkerBootstrap.RunAsync(args, () => new VolumeControl());
```

For unit tests, create typed fake capability responses/events with
`WidgetTestHostServicesBuilder`, attach them through `WidgetTestHost.Attach`,
then drive the same Created/Background/Visible/Interactive lifecycle with the
`WidgetTestHost` helpers. This keeps tests transport-free without reflection or
private runtime APIs. See the SDK and runtime READMEs for complete examples.

## Validate

```powershell
& $gbar validate .\scratch\VolumeControl
```

Directory validation checks the strict manifest and every `.gbss` file. You
can also validate a single `manifest.json` or `.gbss` file. Warnings such as
safe numeric clamping do not fail validation; malformed or unsafe input does.

## Render a snapshot

```powershell
& $gbar render `
  .\scratch\VolumeControl\bin\Debug\net8.0\VolumeControl.dll `
  --type dev.example.VolumeControl.VolumeControl `
  --instance development.preview `
  --output .\scratch\VolumeControl\snapshot.json
```

`gbar render` loads the DLL in a collectible development-only load context and
calls `Render()`. It is not the production worker boundary and must not be used
to inspect untrusted binaries. The command can also preview an existing
snapshot with `gbar render <snapshot.json>`.

## Replay controller input

```powershell
& $gbar replay `
  .\scratch\VolumeControl\snapshot.json `
  .\scratch\VolumeControl\replays\smoke.json
```

Replay follows declared focus edges, activates a focused button with A, and
resolves declared non-Guide shortcuts. It catches navigation/action contract
errors before host integration.

## Authoring loop

1. Keep `Render()` deterministic for current widget state.
2. Build the tree from `UI.Stack`, `UI.Row`, and leaf elements.
3. Give every node a stable ID; never derive IDs from list position.
4. Add explicit focus neighbors where spatial navigation is ambiguous.
5. For a nested surface, publish `ActiveInputScopeId` and keep
   `InitialFocusId` inside it. The host owns/restores current focus.
6. Handle semantic action IDs in `OnActionAsync`.
7. Call `Invalidate()` only when visible state changes.
8. Rebuild, validate, render, and replay.

Author in logical DIPs and responsive GBSS. Do not assume a fixed physical
resolution, DPI, aspect ratio, widget width, or positive desktop coordinates.
The host supplies the current viewport and owns monitor placement. See
[display and resolution behavior](display-and-resolution.md).

## Install and review locally

First stage a clean package root containing `manifest.json`, the published
assembly at the manifest's exact entrypoint path, and GBSS. The complete staging
example is in [publishing and installation](publishing-and-installation.md).
Then use the deterministic pack/install commands:

```powershell
& $gbar pack $packageRoot `
  --output .\scratch\VolumeControl.gbarwidget
& $gbar install .\scratch\VolumeControl.gbarwidget
```

New widget IDs are disabled. Open the overlay's Settings → Installed widgets
page to review the package ID, publisher, version, runtime, and required versus
optional capability declarations, host-API range, architectures, and
compatibility result, then enable it. Incompatible packages remain disabled;
Settings shows a bounded reason. There is no file-picker installer: acquisition
remains a CLI workflow. The bridge watches accepted catalog changes and updates
the overlay without a restart; listing/reload does not eagerly start the worker.

Enablement is not capability consent. If the package declares a brokered
service, review and grant it separately under Settings → Permissions &
capabilities. Invalid catalog updates retain the last-good running catalog.

On first use, every installed/community package is launched in a mandatory
package-specific AppContainer selected by trusted host policy. It is Low
integrity, has no OS capability SIDs or network access, receives a stripped
environment, and can read/execute only the generic worker runtime and its exact
immutable package root. Job Object policy limits memory, active processes,
desktop UI access, and cleanup. A manifest or widget
argument cannot opt into the temporary Job-only policy used by trusted bundled
Settings and YT Music workers. If isolation or authenticated IPC cannot be
established, the widget does not start; there is no desktop-token fallback.

Author against the SDK boundary: do not depend on arbitrary user-profile paths,
ambient environment secrets, direct sockets, raw Core Audio/WLAN calls, child
processes, or desktop UI. Request only published `HostServices` capabilities
and handle unavailable/denied states. The AppContainer profile may retain
private OS-managed data, but it is not yet a portable storage API and currently
has no platform disk quota or user-facing cleanup control.
Win32k system-call disable is not active because it prevents CoreCLR from
initializing in the tested configuration (`0xC0000142`); do not mistake Job UI
restrictions for that stronger mitigation.

Installed package versions are immutable and coexist. Before changing the
active version, disable the widget. In Settings → Installed widgets, open the
package, choose **Manage versions**, select the exact version, return to
details, and review it. A compatible selection can then use **Enable reviewed
widget** as a separate action; an incompatible selection remains reviewable
but cannot be enabled. LB/RB page the five-row version list and B returns one
scope. The equivalent CLI flow is:

```powershell
& $gbar disable dev.example.volume-control
& $gbar version list dev.example.volume-control
& $gbar version select dev.example.volume-control 1.1.0
# Review Settings → Installed widgets, then:
& $gbar enable dev.example.volume-control
```

`version rollback` without `--to` chooses the greatest installed version older
than the active version. `version rollback --to <version>` requires a specific
installed older version. Both operations leave the widget disabled; moving to
a newer version uses `version select`. If the catalog pins a version whose
immutable directory is missing, discovery fails closed instead of silently
running another version.

The MVP accepts only Pressed shortcuts. A and D-pad are reserved for focused
activation and navigation. Dashboard quick actions are separate: the host owns
A, B, Y, D-pad/analog, and Guide, while cards may expose X, bumpers, triggers,
stick clicks, Menu, or View.

Continue with the [declarative UI reference](declarative-ui.md), [controller
input model](controller-input.md), [widget capabilities](capabilities.md), and
[GBSS reference](gbss.md). For packaging, GitHub Release publishing, hash
pinning, and local or remote installation, read [publishing and
installation](publishing-and-installation.md). Theme authors should instead use
the dedicated [theme packaging and distribution](theme-packaging.md) workflow.
