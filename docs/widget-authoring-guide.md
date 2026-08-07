# Widget authoring guide and API map

Status: the package schema, managed SDK, declarative protocol versions 1–2,
controller routing, lifecycle, GBSS, local packaging/install workflow, and
typed audio/network capabilities described as **implemented** below exist in
this repository. Public NuGet packages, publisher signing/revocation, a widget
gallery, automatic updates, a graphical installer, general secret storage,
and arbitrary network access are **not implemented**.

This is the primary end-to-end guide for widget authors. The narrower reference
pages remain authoritative for their detailed limits and are linked throughout.
If prose and code disagree, the public types under `src/WidgetProtocol`,
`src/WidgetSdk`, and `src/WidgetRuntime` win.

## Mental model

A widget is a C# state machine that publishes a semantic tree. It does not own
an HWND or paint pixels. The platform owns:

- the overlay and active monitor;
- layout, clipping, DPI conversion, accessibility, and native rendering;
- controller focus, Scroll offsets, and safe focus restoration;
- worker launch, lifecycle, isolation, and bounded IPC; and
- consented access to closed operating-system capabilities.

The widget owns:

- stable semantic node IDs and action IDs;
- current data and optimistic/reconciled interaction state;
- dashboard quick actions and open-window scoped shortcuts;
- lifecycle-aware subscriptions and refresh work; and
- local GBSS classes and styles.

There is no browser engine in the host. Do not design around HTML, DOM APIs,
JavaScript, arbitrary SVG, custom drawing, monitor APIs, or pixel scrolling.

## Versions: three different contracts

Do not use these version numbers interchangeably.

| Contract | Current author-facing value | Where it appears | Meaning |
| --- | ---: | --- | --- |
| Manifest schema | `manifestVersion: 1` | `manifest.json` | Shape and validation rules of the package manifest. |
| Package host API | major `1` | `hostApi.minimum` and `hostApi.maximumMajor` | Compatibility range used when the catalog decides whether this host may load the package. |
| Declarative snapshot protocol | `1` or `2` | Generated `ViewSnapshot.ProtocolVersion` | Shape of one rendered UI snapshot. The SDK selects this automatically. |

A plain Stack/Row view is emitted as protocol 1. Using `UI.VerticalScroll`,
`UI.HorizontalScroll`, `UI.Scroll`, or `WidgetView.Surface` emits protocol 2.
Those additive UI features do **not** require a fictional host API 2: packages
still declare the implemented host API range `1.0` through major `1`.

Runtime, bridge, and capability-broker transports also have internal protocol
versions. Widget code does not set or negotiate them; use
`WidgetWorkerBootstrap` and the typed SDK.

## Tutorial 1: scaffold and run the minimal widget

Prerequisites are Windows, PowerShell, the .NET 8 SDK, and this repository.
The SDK is not yet a supported public NuGet package, so the generator currently
creates repository-local project references.

Build the CLI and scaffold a widget:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'

& $gbar new widget Clock `
  --output .\scratch\Clock `
  --id dev.example.clock `
  --publisher dev.example

dotnet build .\scratch\Clock\Clock.csproj -c Release
& $gbar validate .\scratch\Clock
```

The smallest useful widget is a public `Widget` subclass with a public
parameterless constructor (or one whose parameters are all optional):

```csharp
using GameBarAlternative.WidgetSdk;

namespace Dev.Example.Clock;

public sealed class ClockWidget : Widget
{
    public override WidgetView Render() => new(
        UI.Stack("clock.root",
            UI.Text(DateTimeOffset.Now.ToString("t"), "clock.time"),
            UI.Button("Refresh", "refresh", "clock.refresh")),
        InitialFocusId: "clock.refresh");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "refresh")
            Invalidate();
        return ValueTask.CompletedTask;
    }
}
```

`Render()` returns current state; it must not perform network, device, blocking
file, or long-running work. `Invalidate()` tells the host that visible state
changed. It does not mutate focus or force an immediate paint.

The checked-in [Clock sample](../samples/ClockWidget/ClockWidget.cs) is the
minimal compileable reference. A custom executable worker should delegate
startup rather than parse host arguments:

```csharp
using GameBarAlternative.WidgetRuntime;

return await WidgetWorkerBootstrap.RunAsync(args, () => new ClockWidget());
```

Installed packages normally use the platform's generic worker host and name
the widget assembly/type in their manifest; they do not need to ship a custom
worker executable.

## Tutorial 2: make the controller model intentional

### Dashboard card (hovered, not open)

The host reserves Guide/Home, D-pad and horizontal left-stick navigation, `A`
to open, and `Y` to enter/exit reorder. A selected card may expose up to three
quick actions on `B`, `X`, `LB`, `RB`, `LT`, `RT`, either stick click, Menu, or
View. The mapping comes from the widget snapshot; it is not hard-coded by the
shell:

```csharp
return new WidgetView(
    root,
    InitialFocusId: "player.play",
    QuickActions:
    [
        new(ControllerButton.LeftBumper, "previous", "Previous"),
        new(ControllerButton.X, "toggle-playback", "Play or pause"),
        new(ControllerButton.RightBumper, "next", "Next"),
    ]);
```

Quick actions enter the same bounded serial action queue as open-widget
actions. Their `SourceElementId` is `dashboard-card`. They run while lifecycle
state is `Visible`, not `Interactive`: current audio/network **control**
capabilities are Interactive-only, so do not advertise a dashboard capability
action that the broker will reject with `lifecycle_denied`.

### Open widget

Guide/Home remains host-owned and closes the overlay. The host uses D-pad and
two-dimensional left-stick movement for focus and `A` to activate the focused
button. `B`, `X`, `Y`, bumpers, triggers, stick clicks, Menu, and View are
available to the active widget input scope.

Attach shortcuts to the control that owns the action:

```csharp
UI.Row("player.transport",
    UI.Button("Previous", "previous", "player.previous")
        .Icon(WidgetGlyph.Previous)
        .FocusRight("player.play")
        .Shortcut(ControllerButton.LeftBumper),
    UI.Button("Play", "toggle-playback", "player.play")
        .Icon(WidgetGlyph.Play, "Play or pause")
        .FocusLeft("player.previous")
        .FocusRight("player.next")
        .Shortcut(ControllerButton.X),
    UI.Button("Next", "next", "player.next")
        .Icon(WidgetGlyph.Next)
        .FocusLeft("player.play")
        .Shortcut(ControllerButton.RightBumper))
```

The default `OnControllerInputAsync` resolves against the exact last rendered
snapshot. For open input it validates the snapshot sequence and active scope,
checks the focused node first, then a unique binding in the active scope. Stale
input, a focus ID outside that scope, or an ambiguous binding is unhandled.
Override `OnControllerInputAsync` only for semantic controls that cannot be
represented as declarative actions; Guide/Home and arbitrary HID reports are
never transported.

Only the `Pressed` shortcut phase is carried end to end today. Do not bind
`Released` or `Repeated`, and do not bind `A` or D-pad as shortcuts.

### B behavior

At the open root, an unhandled `B` returns to the dashboard; it does not close
the overlay. Inside a nested scope, an unhandled `B` neither bubbles to the
root nor dismisses anything. Bind it explicitly. On the dashboard/icon tray,
`B` closes the overlay. Guide/Home remains the global toggle from any depth.

## Tutorial 3: nested windows and scoped shortcuts

Stack, Row, and Scroll containers can start an independent input scope.
Publish the active scope explicitly; never infer it from the focused ID:

```csharp
var root = UI.Stack("settings.root",
    UI.Button("Open reset dialog", "open-reset", "settings.open-reset"),
    UI.Stack("settings.reset.container",
        UI.Text("Reset all settings?", "settings.reset.title"),
        UI.Button("Reset", "confirm-reset", "settings.reset.confirm")
            .Shortcut(ControllerButton.Y),
        UI.Button("Cancel", "close-reset", "settings.reset.cancel"))
        .InputScope("settings.reset")
        .Shortcut(ControllerButton.B, "close-reset"));

return new WidgetView(
    root,
    InitialFocusId: _showReset
        ? "settings.reset.cancel"
        : "settings.open-reset",
    ActiveInputScopeId: _showReset
        ? "settings.reset"
        : "settings.root");
```

Rules:

- The root is the default scope, even without `.InputScope(...)`.
- `ActiveInputScopeId` must identify the root scope or one published nested
  scope.
- `InitialFocusId` and explicit focus neighbors must remain inside that scope.
- Bindings must be unique per `(button, phase)` inside one scope. Separate
  scopes may reuse the same button.
- Focus is remembered independently per widget and scope. Keep IDs stable.
- Parent and sibling shortcuts are never searched while a nested scope is
  active.

This is the supported model for dialogs, settings pages, detail panes, and
other nested widget windows.

## Tutorial 4: controller-owned scrolling

Use a Scroll container whenever content can exceed its clamped viewport:

```csharp
var list = UI.VerticalScroll(
    "audio.sessions.scroll",
    _sessions.Select(BuildSessionRow).ToArray())
    .Classes("session-list");
```

`UI.HorizontalScroll` is the horizontal equivalent, and
`UI.Scroll(id, ScrollAxis.Vertical|Horizontal, children)` is the explicit form.
Scroll is a protocol-2 container and supports `.InputScope(...)` and
`.Shortcut(...)` like Stack and Row.

The host owns the pixel/DIP offset, measures the full extent on one axis,
clips descendants, and reveals the complete focused control before painting.
Offscreen buttons in a semantic Scroll remain focus candidates. Widgets never
publish an offset and should not replace scrolling with a hidden selection
index or LB/RB pagination.

Keep the Scroll ID and descendant button IDs stable. The host remembers an
offset by exact widget runtime instance, active input scope, and Scroll ID.
Closing and reopening therefore restores the same surface without leaking
state across widget versions or nested windows. If a dynamic item disappears,
focus falls to the nearest surviving enabled control in tree order and its
Scroll ancestor reveals it. A runtime replacement clears focus and scroll
memory.

Ordinary Stack/Row clipping is different: a clipped button is not a navigation
candidate because the host cannot reveal it.

## Tutorial 5: choose a useful surface without hard-coding a window

`WidgetSurfaceHints` describes the useful shape of the current view in logical
DIPs. It is a hint, not a size request:

```csharp
return new WidgetView(
    root,
    InitialFocusId: "master.mute",
    Surface: new WidgetSurfaceHints
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 560,
        PreferredHeight = 420,
        MinimumWidth = 360,
        MinimumHeight = 260,
    });
```

Modes are `Adaptive`, `Compact`, `Standard`, and `Wide`. The current shell's
mode defaults are approximately 560×420, 880×520, and 1120×620 DIPs for the
three named sizes. Adaptive delegates the initial choice to host policy.

Preferred dimensions and minimum dimensions are optional **pairs**: specify
both width and height or neither. Widths must be finite and in 240–1600 DIPs;
heights must be finite and in 180–1200 DIPs. A minimum cannot exceed its
preferred value. The host may choose a smaller or larger safe surface and can
render below the requested minimum when the active work area, shell chrome,
DPI, interface scale, or accessibility scale requires it.

Surface hints belong to one `WidgetView`, so a compact status page and a wide
media page can publish different modes. Every view must still reflow and use
Scroll for overflow after host clamping.

## Declarative UI API map

| SDK call | Result | Notes |
| --- | --- | --- |
| `UI.Stack(id, children)` | vertical container | Can start an input scope and own shortcuts. |
| `UI.Row(id, children)` | horizontal container | Can start an input scope and own shortcuts. |
| `UI.VerticalScroll(id, children)` | vertical Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.HorizontalScroll(id, children)` | horizontal Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.Scroll(id, axis, children)` | explicit Scroll | Axis cannot be changed by a theme. |
| `UI.Text(text, id, accessibilityLabel?)` | text | Non-interactive. |
| `UI.Button(label, action, id)` | focusable button | `A` invokes its action. |
| `UI.ToggleButton(label, isOn, action, id)` | composed button | Emits On/Off text and selected semantics. |
| `UI.Stepper(...)` | composed row | Stable `.label`, `.decrement`, `.value`, `.increment` children. |
| `UI.Progress(value, maximum, id, label?)` | progress | Requires finite `0 <= value <= maximum`, `maximum > 0`. |
| `UI.Spacer(id)` | spacer | Layout-only. |
| `UI.Image(httpsUrl, id, alt, fit?)` | image | HTTPS only; host applies download/decode/cache limits. |
| `UI.Icon(glyph, id, label)` | semantic icon | Closed host-rendered glyph vocabulary. |

All nodes can use `.Classes("name", ...)`. Buttons additionally provide
`.FocusUp/Down/Left/Right(id)`, `.Disabled(...)`, `.Selected(...)`,
`.Busy(...)`, `.Icon(...)`, and `.Shortcut(...)`.

Current semantic glyphs are `Music`, `Play`, `Pause`, `Previous`, `Next`,
`Refresh`, `Shuffle`, `Like`, `Dislike`, `Repeat`, `Settings`, `Warning`,
`Check`, `Connection`, `Volume`, `Muted`, `Microphone`, `Wifi`, and `Ethernet`.
Widgets cannot supply SVG paths or icon-font names.

Every node ID must be unique in the snapshot, at most 128 characters, and use
only ASCII letters, digits, `.`, `-`, and `_`. Do not derive IDs from list
positions or displayed text; changing an ID discards host focus/scroll memory.
Protocol limits are 2,048 nodes, depth 32, strings up to 4,096 characters, and
three dashboard quick actions.

## Interaction state and reconciliation

Disabled and Busy buttons remain visible but do not activate through the
default router. Selected buttons remain actionable and expose semantic state to
the renderer and GBSS.

For a remote toggle:

1. Store the prior value.
2. Publish the intended value with `.Selected(newValue).Busy(true)` and call
   `Invalidate()` before awaiting I/O.
3. Reconcile authoritative events without allowing an older poll to overwrite
   the pending intent.
4. Clear Busy on confirmation or a bounded deadline.
5. On failure, restore only that feature's prior state, publish a short safe
   error, and invalidate again.

The [YT Music reference](../samples/YtMusicWidget/README.md) demonstrates
optimistic playback/rating/shuffle/repeat state, stale-event guards, bounded
progress interpolation, and rapid ordered LB/RB actions.

## Lifecycle API

Lifecycle is host-authoritative and separate from process residency.

| State | Author contract |
| --- | --- |
| `Created` | Runtime-owned. `OnCreatedAsync` runs exactly once. Start only lightweight process-lifetime work and return promptly. |
| `Background` | Worker may remain resident, but it is not presented. Stop UI refresh, animation, controller work, and provider subscriptions. |
| `Visible` | Dashboard card is selected. Local quick actions can run; read capabilities may be used. |
| `Interactive` | Full widget is open. Scoped shortcuts and typed control capabilities may run. |
| `Destroying` | Runtime-owned bounded cleanup after widget/state/active tokens have been canceled. |

Use the shortest correct token:

- `StateLifetimeToken` or the `stateLifetime` hook argument belongs to exactly
  one stable state.
- `ActiveCancellationToken` spans Visible and Interactive and cancels before
  Background.
- `WidgetLifetimeToken` spans the resident widget until Destroying. Do not use
  it for ordinary UI polling.

```csharp
private Task _activeWork = Task.CompletedTask;

protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
{
    _activeWork = RunPeriodicUpdatesWhileActiveAsync(
        TimeSpan.FromSeconds(1),
        RefreshPresentationStateAsync,
        tickImmediately: true);
    return ValueTask.CompletedTask;
}

protected override async ValueTask OnDestroyingAsync(
    CancellationToken shutdownToken)
{
    await _activeWork.WaitAsync(shutdownToken);
}
```

`RunPeriodicUpdatesWhileActiveAsync` and
`InvalidatePeriodicallyWhileActiveAsync` accept intervals from 250 ms to one
hour. Ticks are serialized and non-overlapping. Normal lifecycle cancellation
completes the task; callback failure faults it, so retain and observe every
returned task.

Manifest `backgroundPolicy` currently accepts `none` or `suspend`, but these
are validated metadata rather than enforced final residency policies. The
worker remains resident in Background after first use by default. The current
broker independently denies audio/network operations in Background. Do not
claim suspend/unload behavior or keep presentation work alive there.

## GBSS: safe widget-local styling

Attach semantic classes in C# and ship `styles/default.gbss`:

```csharp
UI.Button("Play", "toggle", "player.play")
    .Classes("transport", "primary")
```

```css
:root {
  --widget-accent: #ff3b68;
}

button.transport {
  width: 52dip;
  height: 52dip;
  corner-radius: 26dip;
  background: rgba(32, 34, 42, 0.94);
}

button.transport:focused {
  outline-color: var(--focus);
  outline-width: 3dip;
  scale: 1.04;
}

scroll.session-list {
  overflow: clip;
  gap: 10dip;
}
```

GBSS is not browser CSS. It supports one semantic compound selector (role,
stable ID, classes, and `:focused`, `:pressed`, `:selected`, `:disabled`),
variables, safe package-relative `.gbss` imports, and an allowlist of typed,
bounded properties. Unknown properties, scripts, URLs, filesystem paths,
`calc`, expressions, and arbitrary functions are rejected. `:pressed` parses,
but the complete transient pressed/busy renderer-state pipeline is not yet an
author guarantee.

Use percentages, `vw`, `vh`, flex growth/shrink, min/max dimensions, line
limits, and `overflow: clip` for responsive layout. The host applies global
themes and accessibility after widget styles; a widget cannot override forced
contrast, text scale, reduced motion, or reduced transparency policy.

Run `gbar validate <widget-directory>` after every style change. See the
[GBSS reference](gbss.md) for the exact property catalog and safety limits.

## Typed audio and network capabilities

Installed/community widgets run without ambient OS/network capability. Ask for
the smallest closed broker authority in `manifest.json` and call the typed
`HostServices` APIs.

| Manifest ID | Typed service | Allowed state |
| --- | --- | --- |
| `system.audio.sessions.read.v1` | list/watch application sessions | Visible or Interactive |
| `system.audio.sessions.control.v1` | set session volume/mute | Interactive |
| `system.audio.output.read.v1` | get/watch master output | Visible or Interactive |
| `system.audio.output.control.v1` | set master volume/mute | Interactive |
| `system.network.read.v1` | status, saved profiles, status events | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | switch to a saved profile | Interactive |

Required capabilities are not auto-granted. Put core authority in
`permissions`, degradable features in `optionalPermissions`, then render
denied/unavailable states for both.

### Master output example

Open the acknowledged subscription **before** fetching current state so a
change during the read cannot be lost:

```csharp
private WidgetAudioOutput? _output;

private async Task ObserveOutputAsync(CancellationToken cancellationToken)
{
    try
    {
        await using var subscription = await HostServices.Audio
            .OpenOutputSubscriptionAsync(cancellationToken);
        _output = await HostServices.Audio.GetOutputAsync(cancellationToken);
        Invalidate();

        await foreach (var change in subscription.ReadAllAsync(cancellationToken))
        {
            _output = change.IsAvailable ? change.Output : null;
            Invalidate();
        }
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
        // Normal lifecycle exit.
    }
    catch (WidgetCapabilityUnavailableException)
    {
        _output = null;
        Invalidate();
    }
    catch (WidgetCapabilityException exception)
    {
        _statusCode = exception.ErrorCode;
        Invalidate();
    }
}

public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId == "output-mute" && _output is { } output)
        await HostServices.Audio.SetOutputMutedAsync(
            !output.IsMuted, cancellationToken);
}
```

Session volume uses values from 0 through 1 with
`SetSessionVolumeAsync(sessionId, volume)`. Master volume uses the same range
with `SetOutputVolumeAsync(volume)`. Treat an unavailable event differently
from a healthy empty session list.

### Network example

Use `GetStatusAsync`, `GetSavedProfilesAsync`, and an acknowledged
`OpenStatusSubscriptionAsync`. A switch accepts only a broker-issued saved
profile ID:

```csharp
await HostServices.Network.SwitchSavedProfileAsync(
    profile.ProfileId, cancellationToken);
```

Render `WirelessAvailability`, `DetailsAccess`, and
`ConnectionAttemptState` explicitly. Privacy-restricted identity, no adapter,
radio off, and unavailable WLAN service are normal bounded states; do not
retry them on a timer, request elevation, inspect WLAN XML, or log SSIDs.

Handle `WidgetCapabilityUnavailableException`, `WidgetCapabilityException`
using its stable `ErrorCode`, and normal lifecycle cancellation. Common codes
include `permission_denied`, `capability_not_declared`, `lifecycle_denied`,
`capability_revoked`, and `platform_unavailable`. Treat unknown codes as a
generic bounded provider failure.

The YT Music trusted reference currently declares `network.loopback:13091`,
but general loopback/network brokering is not implemented for community
workers. Installed AppContainer workers have no network capability. Do not
copy the trusted built-in's direct Credential Manager or socket access; a
general secret broker and network API remain planned.

## Manifest reference

```json
{
  "manifestVersion": 1,
  "id": "dev.example.audio-control",
  "publisher": "dev.example",
  "name": "Audio Control",
  "version": "1.0.0",
  "hostApi": {
    "minimum": "1.0",
    "maximumMajor": 1
  },
  "entrypoint": {
    "runtime": "dotnet-worker",
    "assembly": "payload/AudioControl.dll",
    "type": "Dev.Example.AudioControl.AudioControlWidget"
  },
  "permissions": [
    "system.audio.output.read.v1"
  ],
  "optionalPermissions": [
    "system.audio.output.control.v1"
  ],
  "backgroundPolicy": "suspend",
  "resourceRequest": {
    "memoryMb": 48,
    "updateHz": 4
  },
  "architectures": ["x64", "arm64"]
}
```

| Field | Current contract |
| --- | --- |
| `manifestVersion` | Exactly `1`. Unknown JSON members are rejected. |
| `id` | Lowercase reverse-DNS identifier; `_` and `-` are allowed. For packages it must equal `publisher` or start with `publisher.`. |
| `publisher` | Lowercase reverse-DNS publisher claim. It is not cryptographic proof. |
| `name` | 1–80 characters. |
| `version` | Canonical dotted numeric `System.Version` text such as `1.0.0`. Installed versions are immutable. |
| `hostApi` | Current compatible range is minimum `1.0`, maximum major `1`. This is independent of snapshot protocol 2. |
| `entrypoint.runtime` | Only `dotnet-worker`. |
| `entrypoint.assembly` | Exact-case normalized package-relative path with `/`, no traversal. |
| `entrypoint.type` | Namespace-qualified public concrete `Widget` type with a public constructor whose parameters are all optional. |
| `permissions` | Required declarations. Required still means explicit user consent. |
| `optionalPermissions` | Degradable declarations; may not duplicate a required ID. |
| `backgroundPolicy` | `none` or `suspend`; validated metadata, not current residency enforcement. |
| `resourceRequest.memoryMb` | 16–256. The host owns the effective limit. |
| `resourceRequest.updateHz` | 1–60 metadata request. SDK periodic helpers are independently limited to at most 4 Hz. |
| `architectures` | One or both of `x64`, `arm64`; the current machine architecture must be listed. |

Unknown capability IDs are syntactically valid at manifest parse time but an
installed package declaring unsupported authority is omitted by the bridge.
Use only the published closed IDs above.

## Validate, render, replay, and test

Use the same compiler/validators as production:

```powershell
& $gbar validate .\scratch\Clock

& $gbar render `
  .\scratch\Clock\bin\Release\net8.0\Clock.dll `
  --type Dev.Example.Clock.ClockWidget `
  --instance development.preview `
  --output .\scratch\Clock\snapshot.json

& $gbar replay `
  .\scratch\Clock\snapshot.json `
  .\scratch\Clock\replays\smoke.json
```

DLL rendering executes the assembly in the CLI process. Use it only for code
you wrote or reviewed; it is not the production AppContainer boundary.

For transport-free unit tests, configure typed fakes and drive real lifecycle
hooks:

```csharp
var services = new WidgetTestHostServicesBuilder()
    .WithResponse(
        WidgetAudioCapabilities.GetOutput,
        new WidgetAudioOutput(0.75, IsMuted: false))
    .WithEvents(
        WidgetAudioCapabilities.OutputChanged,
        [new WidgetAudioOutputChanged(
            new WidgetAudioOutput(0.50, IsMuted: false))])
    .Build();

var widget = WidgetTestHost.Attach(new AudioControlWidget(), services);
await WidgetTestHost.InitializeAsync(widget);
await WidgetTestHost.SetLifecycleStateAsync(
    widget, WidgetLifecycleState.Interactive);
// Render and assert stable IDs, state, focus, and actions.
await WidgetTestHost.DestroyAsync(widget);
```

Test at least:

- manifest and snapshot validation;
- dashboard quick actions separately from open-window shortcuts;
- root and nested scope routing, including focusless `B` close;
- stable focus and Scroll IDs across list churn;
- surface hints at small, portrait, ultrawide, and high-scale viewports;
- disabled/busy/selected state and optimistic rollback;
- lifecycle cancellation and fault observation;
- permission denied/revoked, provider unavailable, healthy empty data, and
  event bursts;
- worker restart and stale snapshot/event rejection; and
- long labels, missing images, reduced motion/transparency, high contrast, and
  150% text.

The repository's `WidgetSdk.Tests`, Audio Mixer, Network Controls, Settings,
and YT Music suites are the current executable references. Run the complete
gate with:

```powershell
dotnet run --project `
  .\tests\Documentation.Tests\Documentation.Tests.csproj `
  -c Release

.\scripts\Verify.ps1 -Configuration Release
```

The documentation contract checks local Markdown targets, the author-guide
section contract, and its root/index/sample entry points. It does not validate
external URLs or substitute for compiling the samples and SDK tests.

## Package and install locally

`gbar pack` includes every file under its input directory. Always stage a clean
package root rather than packing source, `obj`, secrets, or unrelated files:

```powershell
$packageRoot = '.\artifacts\Clock-package'
New-Item -ItemType Directory -Force `
  -Path "$packageRoot\payload", "$packageRoot\styles"

dotnet publish .\scratch\Clock\Clock.csproj `
  -c Release `
  -o "$packageRoot\payload"
Copy-Item .\scratch\Clock\manifest.json "$packageRoot\manifest.json"
Copy-Item .\scratch\Clock\styles\default.gbss `
  "$packageRoot\styles\default.gbss"

& $gbar validate $packageRoot
& $gbar pack $packageRoot `
  --output .\artifacts\dev.example.clock-1.0.0.gbarwidget
& $gbar install .\artifacts\dev.example.clock-1.0.0.gbarwidget
& $gbar list
```

The archive is ZIP-compatible and contains exact-case root `manifest.json`,
the declared entrypoint under `payload/`, and optional `styles/` and `assets/`.
General package-asset resolution is not wired to UI yet; use HTTPS Image nodes
and semantic glyphs. Do not add `signature.json`: it is reserved for the later
signing phase.

New widget IDs install disabled. In Overlay Settings:

1. Open **Installed widgets** and review identity, version, host range,
   architecture, and declarations.
2. Enable the reviewed package.
3. Open **Permissions & capabilities** and independently grant only the
   capabilities you accept.

The default catalog is
`%LOCALAPPDATA%\GameBarAlternative\widgets`. The bridge watches accepted
changes and refreshes the dashboard without eagerly launching the worker. A
custom CLI `--catalog` root is isolated test state and does not appear in the
packaged overlay.

Installed versions coexist immutably. Updates and version selection are
disabled-only review operations:

```powershell
& $gbar disable dev.example.clock
& $gbar install .\artifacts\dev.example.clock-1.1.0.gbarwidget
& $gbar version list dev.example.clock
& $gbar version select dev.example.clock 1.1.0
# Review in Settings, then:
& $gbar enable dev.example.clock
```

`version rollback` selects an installed older version and also leaves the ID
disabled. Never edit catalog JSON or installed directories by hand.

## Share from a GitHub repository

The implemented sharing unit is a deterministic `.gbarwidget` attached to an
exact GitHub Release tag. Publish its SHA-256 through an independently trusted
channel. A recipient installs the exact asset with:

```powershell
& $gbar install `
  github:example/widgets@v1.0.0/dev.example.clock-1.0.0.gbarwidget `
  --sha256 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
```

The shorthand maps directly to that release asset. It does not select
`latest`, call the GitHub API, clone/build the repository, or execute release
scripts. Remote HTTPS/GitHub install requires a 64-hex SHA-256 pin and applies
bounded redirect, timeout, size, archive, and path validation.

A digest proves exact bytes, not author identity. Publisher signing,
revocation, automatic update discovery, a public gallery, and a graphical
installer remain planned. Until those exist, users should install only widgets
they wrote, reviewed, or obtained from a developer they already trust and
should prefer building from source.

## Isolation and security expectations

Installed/community workers are mandatory package-specific AppContainers at
Low integrity with zero OS capability SIDs and no network authority. The host
grants read/execute only to the generic runtime and exact immutable package
root, supplies a stripped environment, and assigns a Job Object before resume.
Current job policy limits memory, active processes to one, desktop UI access,
and kill-on-close cleanup. Isolation, token, ACL, PID, or authenticated IPC
failure aborts launch; there is no desktop-token fallback.

Typed audio/network services cross a separate identity-, declaration-,
consent-, and lifecycle-bound broker. A manifest cannot request raw pipe
details, arbitrary operation IDs, an AppContainer capability SID, or the
trusted Job-only launch used temporarily by bundled Settings and YT Music.

This is meaningful containment, not proof that an unsigned publisher is safe.
CPU quotas, disk/profile quotas and cleanup, Win32k system-call disable,
publisher verification/revocation, and a complete security audit UI are not
implemented. Do not depend on ambient user files, environment secrets,
Credential Manager, direct sockets, child processes, registry access, or
desktop UI. Keep secrets out of manifests, GBSS, logs, issue reports, and
command lines.

## Performance expectations

The product runs beside a game, so obvious polling and unbounded work are API
misuse even if they fit under the current approximately 200 MB prototype
allowance.

- Keep `Render()` deterministic, synchronous, and allocation-conscious.
- Subscribe to platform changes; do not scan devices or files on a timer.
- Open an acknowledged subscription before fetching current state.
- Use state/active cancellation for all presentation work.
- Coalesce event bursts and invalidate only when rendered state changes.
- Keep periodic interpolation at or below the SDK's four-Hz bound.
- Serialize commands or use bounded feature-specific concurrency; never start
  an unbounded task per controller press.
- Bound lists, labels, histories, retries, decoded media, and caches.
- Retain/observe tasks so faults do not become silent retry loops.
- Request an honest memory budget; the host policy remains authoritative.

The current engineering budgets are validation targets, not marketing claims.
Read [performance evidence](performance.md) before claiming latency, CPU, GPU,
or memory results.

## Resolution and monitor contract

Author in logical DIPs. The Per-Monitor-V2 host targets the monitor containing
the active external foreground window, derives the logical viewport from that
monitor's current DPI and interface scale, and handles display/work-area/DPI
changes without asking the widget to move a window.

Never assume 1920×1080, 96 DPI, 16:9, a fixed panel width, positive desktop
coordinates, one taskbar position, or one monitor. Use responsive GBSS,
semantic Scroll, stable focus neighbors, line limits, and ellipsis. Surface
hints do not remove this requirement.

The geometry contract has deterministic coverage from tiny and handheld-sized
viewports through portrait, ultrawide, 4K, 5K, and 8K inputs across multiple
DPI/interface scales. Physical mixed-DPI migration, hot-plug, HDR/hybrid-GPU,
long localization, combined accessibility, and supported-game presentation
matrices remain release evidence—not completed universal compatibility.

## Diagnostics and recovery

During local development:

- `gbar validate` reports manifest/GBSS JSON paths and source-located styling
  diagnostics.
- `gbar render` catches snapshot validation and displays the semantic tree.
- `gbar replay` isolates focus/action contract failures without the overlay.
- Overlay Settings → **Diagnostics** reports bounded bridge, catalog,
  appearance, consent, and worker status for local troubleshooting.
- `%LOCALAPPDATA%\GameBarAlternative\overlay.log` is the host diagnostic log;
  `%LOCALAPPDATA%\GameBarAlternative\startup-error.log` records the latest host
  initialization failure.

Runtime diagnostics are intentionally not a community capability. Widget code
cannot read worker tables, process IDs, pipe details, logs, or other widgets.
For widget-visible failures, publish a small stable status, preserve controller
focus, and recover from an explicit action or bounded lifecycle restart. Never
render exception text, tokens, paths, SSIDs, session identifiers, or API bodies.

An action acknowledgement means it entered the bounded queue, not that its I/O
completed. Later failures are surfaced by the host/runtime
`ControllerActionFailed` event and must also produce safe widget state. Worker
restarts are bounded and lazy.

Before filing or closing a bug, check the [known-issues ledger](known-issues.md)
and preserve its acceptance criteria. Do not treat a green narrow unit test as
proof that a hardware/provider/presentation issue is closed.

## Current non-features

Do not design or advertise a widget around any of these yet:

- public SDK/runtime NuGet packages or a stable external template feed;
- publisher signing, certificate identity, revocation, or a public gallery;
- automatic update discovery, graphical install/remove, or repository builds;
- arbitrary outbound/loopback network access for community workers;
- a community secret/token store;
- package-relative Image/font asset resolution;
- HTML, browser CSS, JavaScript, SVG, shaders, or native drawing payloads;
- sliders/scrubbing, text entry, arbitrary pointer UI, or arbitrary raw HID;
- `Released`/`Repeated` shortcut routing end to end;
- enforced suspend/unload background policies, CPU/disk quotas, or profile
  cleanup; or
- universal Fullscreen Exclusive, anti-cheat, Guide-device, input-suppression,
  mixed-monitor, or game compatibility.

## Reference map

- [Widget quickstart](widget-quickstart.md)
- [Declarative UI](declarative-ui.md)
- [Controller input](controller-input.md)
- [GBSS](gbss.md)
- [Capabilities](capabilities.md)
- [Display and resolution](display-and-resolution.md)
- [Packaging contract](widget-packaging.md)
- [Publishing and installation](publishing-and-installation.md)
- [Security and trust](security-and-trust.md)
- [Performance](performance.md)
- [Diagnostics and recovery](diagnostics-and-recovery.md)
- [Troubleshooting](troubleshooting.md)
- [Known issues](known-issues.md)
