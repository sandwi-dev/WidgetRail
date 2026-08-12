# Widget quickstart

Status: implemented offline scaffold and local package workflow

This guide creates a controller-first C# widget, validates its manifest and
GBSS, executes a bounded credential-free semantic scenario in the isolated
worker, renders a protocol snapshot, and replays input without opening the
native overlay.

## Prerequisites

- Windows and PowerShell
- .NET 8 SDK
- A `gbar` build or installation that includes the controller template

The scaffold contains a matching local SDK package and a `NuGet.Config` that
clears external feeds. Once `gbar new` has created the project, its build and
generated tests are offline and do not require this repository.

## Build the developer CLI

From the repository root:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'
& $gbar help
```

The widget commands are `new`, `validate`, `dev`, `preview`, `render`, `replay`,
`pack`, `install`, `list`, `enable`, `disable`, and the
`version list|select|rollback` group. The separate `theme` group provides
`new`, `validate`, `preview`, `pack`, `inspect`, `install`, and `list` for
data-only global themes. Remote install accepts an absolute HTTPS URL or a
deterministic GitHub Release shorthand. There is no GitHub publisher, signing
command, automatic updater, or marketplace client.

The host-maintenance group is `authority-recovery list|retry`. It operates only
on the product-owned local AppContainer authority journal and accepts no
caller-selected journal path.

## Create and build a widget

The blocks marked as the **canonical offline author journey** in this guide are
kept in lockstep with one external temporary-directory fixture. That fixture
generates the project from the checked-in release unit, compiles and executes
the source shown below, and runs every marked CLI phase through removal. The
`gbar dev`, capability, AppContainer, and authority-recovery sections are
advanced or host-integration guidance. Scenario preview has its own two-fixture
external-directory AppContainer proof.

<!-- canonical-author-journey:create-build-test -->
```powershell
& $gbar new widget VolumeControl `
  --output .\scratch\VolumeControl `
  --id dev.example.volume-control `
  --publisher dev.example

dotnet build .\scratch\VolumeControl\VolumeControl.csproj -c Release
dotnet run --project .\scratch\VolumeControl\tests\VolumeControl.Tests.csproj `
  -c Release -- `
  .\scratch\VolumeControl\fixtures\ready.snapshot.json
```

`gbar new` writes `GameBarAlternative.WidgetSdk` to the relative
`.gbar\packages` feed and never writes an absolute checkout path. The generated
test proves Created/Interactive/Background lifecycle transitions, a
state-changing action, retained state, and the snapshot used below. This local
dependency bundle is an offline scaffold contract, not a public NuGet release
or a publisher-trust claim. The bundled version-1 template manifest explicitly
declares every bounded text or binary input. `gbar new` builds and validates the
complete result in a private sibling staging directory, then publishes it with
one rename. The requested output path must not already exist; malformed or
unreadable template input, validation failure, cancellation, and destination
failure leave no new output or staging residue and never alter an existing
author directory.

The current local pre-release unit is `0.1.0-dev`. The generated package adds a
content-derived `.local.<16-hex>` suffix and the project references that exact
version. CLI, SDK, and template compatibility is checked before generation;
see [Widget SDK compatibility and release unit](widget-sdk-compatibility.md)
before intentionally changing the public SDK surface.

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
capabilities can be reviewed from the exact Installed widget's **Permissions &
configuration** page.

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
- optional closed semantic button icons through `.Icon(WidgetGlyph.Refresh)`;
- a safe `styles/default.gbss` file; and
- a deterministic controller replay; and
- a sibling lifecycle/state/action test that exports a bounded snapshot.

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
The [Games & Apps reference](games-and-apps.md) shows paged installed-app reads,
authority-scoped durable SavedIds reconciled to short-lived launch IDs, and
Interactive-only exact launch. The retained
[Recent Apps reference](recent-apps.md) shows a read-only event stream with
opaque OS identity and no foreground/process authority.

### Canonical generated widget source

This is the exact `src\VolumeControl.cs` emitted and compiled by the canonical
journey, not a parallel illustrative snippet:

<!-- canonical-author-journey:widget-source -->
```csharp
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace dev.example.VolumeControl;

public sealed class VolumeControl : Widget
{
    private int _level = 2;
    private bool _active;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Text("VolumeControl", "title", "VolumeControl heading")
                .Classes("title"),
            UI.Text($"{(_active ? "Active" : "Paused")} · Level {_level}",
                    "status", "Current lifecycle and level")
                .Classes("status"),
            UI.Row("actions",
                UI.Button("Lower", "lower", "lower")
                    .Disabled(_level == 0)
                    .FocusRight("apply")
                    .Shortcut(ControllerButton.LeftBumper),
                UI.Button("Apply", "apply", "apply")
                    .FocusLeft("lower")
                    .FocusRight("raise")
                    .Shortcut(ControllerButton.X),
                UI.Button("Raise", "raise", "raise")
                    .Disabled(_level == 4)
                    .FocusLeft("apply")
                    .Shortcut(ControllerButton.RightBumper)),
            UI.Text("LB/RB adjust • X applies • Home returns to the game", "controller-help")
                .Classes("controller-help")),
        InitialFocusId: "apply");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action.ActionId)
        {
            case "lower":
                _level = Math.Max(0, _level - 1);
                Invalidate();
                break;
            case "raise":
                _level = Math.Min(4, _level + 1);
                Invalidate();
                break;
            case "apply":
                // Call a brokered capability here, then invalidate if visible state changes.
                break;
        }
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _active = true;
        Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        _active = false;
        Invalidate();
        return ValueTask.CompletedTask;
    }
}
```

### Advanced custom workers and capability tests

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

<!-- canonical-author-journey:validate -->
```powershell
& $gbar validate .\scratch\VolumeControl
```

Directory validation checks the strict manifest and every `.gbss` file. You
can also validate a single `manifest.json` or `.gbss` file. Warnings such as
safe numeric clamping do not fail validation; malformed or unsafe input does.

## Inspect a data-only snapshot

The generated test above persists the same validated protocol tree it asserts.
Inspect or canonicalize only that data file:

<!-- canonical-author-journey:render -->
```powershell
& $gbar render `
  .\scratch\VolumeControl\fixtures\ready.snapshot.json `
  --output .\scratch\VolumeControl\fixtures\ready.canonical.json
```

`gbar render` bounds and validates snapshot JSON; it never loads a widget DLL.
DLL input fails closed before type resolution or output handling. Use `gbar dev`
for executable integration through the AppContainer worker. Run the test-based
export only for code you authored and control; a test process is not a sandbox
for downloaded repository code.

## List named scenario declarations

Create `scratch\VolumeControl\gbar.scenarios.json` to describe a bounded set of
credential-free semantic states:

```json
{
  "version": 1,
  "assembly": "bin/Release/net8.0/VolumeControl.dll",
  "providerType": "Dev.Example.VolumeControl.VolumeControlScenarios",
  "scenarios": [
    { "name": "muted", "factory": "Muted", "description": "Muted local fixture" }
  ]
}
```

Validate and list the declarations:

```powershell
& $gbar preview .\scratch\VolumeControl
```

The listing path validates manifest syntax and limits but does not load the
assembly, resolve `providerType`, invoke `factory`, or prove that the referenced
code exists. Build the widget, then execute exactly one declared scenario:

```powershell
& $gbar preview .\scratch\VolumeControl `
  --scenario muted `
  --output .\scratch\VolumeControl\fixtures\muted.scenario.json
```

Executing a scenario provider inside the CLI would give developer code the
process's ambient filesystem, network, process, and user authority. An
in-process timeout is not a sandbox and cannot safely terminate arbitrary work.
The preview command therefore loads the selected public static factory only in
a forcibly terminable AppContainer/Job worker. The worker receives the pinned
exact build-output tree and the typed fake services returned by
`WidgetScenarioDefinition`; it has no broker companion or ambient OS
capability. It transitions Visible -> Interactive -> Background, validates two
identical semantic snapshots, destroys the worker, and emits a bounded v1
`WidgetScenarioResult`. This is not native rendering or a sandbox for arbitrary
downloaded repositories. See the
[widget authoring guide](widget-authoring-guide.md#validate-list-scenarios-render-replay-and-test)
for the complete contract.

## Replay controller input

<!-- canonical-author-journey:replay -->
```powershell
& $gbar replay `
  .\scratch\VolumeControl\fixtures\ready.snapshot.json `
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
8. Rebuild, validate, run credential-free scenarios, inspect data-only
   snapshots, and replay.

Author in logical DIPs and responsive GBSS. Do not assume a fixed physical
resolution, DPI, aspect ratio, widget width, or positive desktop coordinates.
The host supplies the current viewport and owns monitor placement. See
[display and resolution behavior](display-and-resolution.md).

## Install and review locally

Pack the source project directly. Source mode validates manifest and GBSS,
runs a bounded Release build with private intermediates, omits compiler symbols
and machine-specific PDB paths, stages the declared entrypoint and dependencies,
and then applies the deterministic package contract:

<!-- canonical-author-journey:pack-install -->
```powershell
& $gbar pack .\scratch\VolumeControl `
  --configuration Release `
  --output .\scratch\dev.example.volume-control-0.1.0.gbarwidget
& $gbar install .\scratch\dev.example.volume-control-0.1.0.gbarwidget
```

An already-staged package directory remains a supported low-level `pack`
input; in that mode every bounded file under the directory is included. Do not
pass a source tree that lacks a project: stage only intended release content.
If a source build does not produce the manifest's exact entrypoint, `pack`
names that path and tells you to align `AssemblyName` or
`entrypoint.assembly`; it never publishes a partial archive.

New widget IDs are disabled. Open the overlay's Settings → Installed widgets
page to review the package ID, publisher, version, runtime, and required versus
optional capability declarations, host-API range, architectures, and
compatibility result, then enable it. Incompatible packages remain disabled;
Settings shows a bounded reason. There is no file-picker installer: acquisition
remains a CLI workflow. The bridge watches accepted catalog changes and updates
the overlay without a restart; listing/reload does not eagerly start the worker.

Enablement is not capability consent. If the package declares a brokered
service, open that package under Settings → Installed widgets and use
**Permissions & configuration**. Invalid trusted shell updates retain last-good. Invalid installed
state/integrity removes Community registrations and retires their workers until
the catalog is valid again.

On first use, every installed/community package is launched in a mandatory
package-specific AppContainer selected by trusted host policy. It is Low
integrity, has no OS capability SIDs or network access, receives a stripped
environment, and can read/execute only the generic worker runtime and its exact
verified files and traversal directories. A session lease pins those objects,
and runtime compares their catalog-captured Windows identities before granting
authority; this is not a claim that the user-owned package root is physically
immutable. Job Object policy limits memory, active processes,
desktop UI access, and cleanup. A manifest or widget
argument cannot opt into the temporary Job-only policy used by trusted bundled
Settings. YT Music is an ordinary Community addon and cannot opt out either. If
isolation or authenticated IPC cannot be
established, the widget does not start; there is no desktop-token fallback.

Author against the SDK boundary: do not depend on arbitrary user-profile paths,
ambient environment secrets, direct sockets, raw Core Audio/WLAN calls, child
processes, or desktop UI. Request only published `HostServices` capabilities
and handle unavailable/denied states. Overlay-owned
`HostServices.PrivateState` remains package-scoped. To discard it, open
**Settings → Installed widgets**, select the exact built-in or Community widget,
choose **Clear local data**, and confirm the nested destructive action. The host
retires that widget's current worker before clearing its exact private state and
then starts a fresh generation. Settings receives only an exists/no-state
indication and an opaque confirmation token—never the stored document. This
action does not remove packages, credentials, provider data, themes, overlay
preferences, geometry, artwork caches, logs, or user files. The store still has
no platform disk quota.
For a local desktop companion, use only the implemented [exact-port JSON and
write-only private-secret services](community-companion-services.md); those
narrow broker grants do not make direct sockets or Credential Manager available
to the worker.
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

<!-- canonical-author-journey:version-lifecycle -->
```powershell
& $gbar disable dev.example.volume-control
& $gbar version list dev.example.volume-control
& $gbar version select dev.example.volume-control 0.2.0
# Review Settings → Installed widgets, then:
& $gbar enable dev.example.volume-control
& $gbar disable dev.example.volume-control
& $gbar version rollback dev.example.volume-control
& $gbar version select dev.example.volume-control 0.2.0
```

`version rollback` without `--to` chooses the greatest installed version older
than the active version. `version rollback --to <version>` requires a specific
installed older version. Both operations leave the widget disabled; moving to
a newer version uses `version select`. If the catalog pins a version whose
immutable directory is missing, discovery fails closed instead of silently
running another version.

If accumulated rollback versions exceed the catalog quota, open
**Settings → Installed widgets → Catalog recovery**, choose an inactive,
non-selected version, and confirm its exact removal. The CLI equivalent is
`gbar repair list` followed by
`gbar repair remove <widget-id> <version>`. Recovery reads bounded canonical
directory names and catalog state only; it never executes or trusts candidate
package contents, never removes the selected generation, and can retire inactive
history while that selected version remains enabled. It has no force or
caller-supplied recursive path.

After local review, remove the disabled widget and all of its immutable
versions:

<!-- canonical-author-journey:remove -->
```powershell
& $gbar disable dev.example.volume-control
& $gbar uninstall dev.example.volume-control
```

If worker admission reports that AppContainer content authority is quarantined,
inspect the local host journal and retry one exact transaction:

```powershell
& $gbar authority-recovery list
& $gbar authority-recovery retry <confirmation-token-from-list>
```

Listing exposes only the confirmation token, validated AppContainer profile,
target count, and journal format. Retry compares the token against current
state, restores and verifies the recorded DACLs, and clears the record only
after verification. It does not accept a journal path, content path, security
descriptor, raw clear, or force option. A stale token is expected after another
host instance has already recovered or replaced that transaction; list again
instead of bypassing the check.

The trusted **Settings → Diagnostics** page presents the same bounded recovery
state with a sanitized widget name and generation. Selecting a record opens an
explicit controller confirmation whose initial focus is **Cancel**. Settings
never renders or speaks the opaque confirmation token; it sends the current
token over its private PID- and nonce-authenticated diagnostics channel and
refreshes authoritative state after the recovered, still-pending, stale,
unavailable, or refused result. Community widgets cannot request this channel.

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
