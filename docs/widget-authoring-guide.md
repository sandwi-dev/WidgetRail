# Widget authoring guide and API map

Status: the package schema, managed SDK, additive declarative protocol versions 1–13,
controller routing, lifecycle, GBSS, local packaging/install workflow, and
typed host capabilities described as **implemented** below exist in this
repository, including exact-port local JSON and write-only private secrets.
Public NuGet packages, publisher signing/revocation, a widget gallery,
automatic updates, a graphical installer, readable/general secret storage,
and arbitrary internet/LAN/socket access are **not implemented**.

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

## Public author parity and host-owned boundaries

Audio Mixer, Network Controls, Games & Apps, and Now Playing are bundled for
discovery, but their widget assemblies use the same manifest, generic
`WidgetWorkerHost`, package-specific AppContainer, `WidgetSdk`, declarative
renderer, lifecycle, consent broker, and capability APIs available to an
installed Community package. YT Music goes further: it is packaged, installed,
enabled, and isolated as a Community addon with no trusted catalog/worker
fallback. These are the reference implementations an independent author should
copy.

An author can use today:

- the complete declarative UI/controller/lifecycle SDK and safe GBSS cascade;
- `HostServices` typed capabilities, private state, exact-port companion JSON,
  and write-only private secrets when declared/allowed;
- transport-free fakes through `WidgetTestHostServicesBuilder`;
- `gbar new|validate|preview|render|replay|dev|pack|install|enable|disable` plus immutable
  version selection/rollback, and host-owned `gbar config` management for
  bounded public package configuration; and
- the same Settings permission/package review and live catalog reload path as
  the bundled references.

The host alone owns native Windows providers, OAuth/token custody, consent
decisions, package verification/isolation, monitor/topmost windows, rendering,
focus, image fetching, and tightly correlated shell effects such as closing
after a confirmed app launch. A widget invokes only a documented typed SDK
operation; it cannot create a broker request, ask for a desktop token, choose a
native provider, open arbitrary network/OS resources, or opt out of isolation.
Public configuration such as an integration Client ID is package/publisher
scoped and host-managed; it is not a secret store and Community code does not
edit its JSON files. A generic manifest schema and controller-native editor for
author-defined configuration are still planned, so do not present
`WidgetConfigurationStore` itself as a stable worker SDK API.
If a useful reference widget appears to require something outside that boundary,
add a reviewed public SDK/broker contract instead of a first-party shortcut.

## Versions: three different contracts

Do not use these version numbers interchangeably.

| Contract | Current author-facing value | Where it appears | Meaning |
| --- | ---: | --- | --- |
| Manifest schema | `manifestVersion: 1` | `manifest.json` | Shape and validation rules of the package manifest. |
| Package host API | major `1` | `hostApi.minimum` and `hostApi.maximumMajor` | Compatibility range used when the catalog decides whether this host may load the package. |
| Declarative snapshot protocol | `1` through `13` | Generated `ViewSnapshot.ProtocolVersion` | Shape of one rendered UI snapshot. The SDK selects the highest version required by the complete tree automatically. |

A plain Stack/Row view is emitted as protocol 1. Scroll/surface hints require
v2; Slider v3; dashboard gesture authority v4; LoadingIndicator v5; inline PNG
v6; ActionSurface v7; ResponsiveGrid v8; responsive visibility v9;
activation-first Slider v10; focus-edge pagination v11; RepeatOne glyph v12;
and explicit focus persistence v13. Combining features selects the highest
required version. These additive snapshot features do **not** change the
package host API range, which remains `1.0` through major `1`.

Runtime, bridge, and capability-broker transports also have internal protocol
versions. Widget code does not set or negotiate them; use
`WidgetWorkerBootstrap` and the typed SDK.

## Tutorial 1: scaffold and run the minimal widget

Prerequisites are Windows, PowerShell, the .NET 8 SDK, and this repository.
The SDK is not yet a supported public NuGet package, so the generator creates
a source `ProjectReference` only after it discovers or receives the exact SDK
project.

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

The source checkout is auto-discovered. When running copied CLI/template
artifacts from an unrelated directory, add
`--sdk-project C:\path\to\GameBarAlternative\src\WidgetSdk\WidgetSdk.csproj`.
Missing SDK resolution fails before any scaffold files are written; `gbar new`
does not emit a placeholder NuGet reference.

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
to open, `B` to close, and `Y` to enter/exit reorder. A selected card may expose up to three
quick actions on `X`, `LB`, `RB`, `LT`, `RT`, either stick click, Menu, or
View. The mapping comes from the widget snapshot; it is not hard-coded by the
shell:

```csharp
return new WidgetView(
    root,
    InitialFocusId: "player.play",
    QuickActions:
    [
        new(ControllerButton.LeftBumper, "previous", "Previous",
            new(WidgetMediaCapabilities.Control.CapabilityId,
                WidgetMediaCapabilities.Control.OperationId)),
        new(ControllerButton.X, "toggle-playback", "Play or pause",
            new(WidgetMediaCapabilities.Control.CapabilityId,
                WidgetMediaCapabilities.Control.OperationId)),
        new(ControllerButton.RightBumper, "next", "Next",
            new(WidgetMediaCapabilities.Control.CapabilityId,
                WidgetMediaCapabilities.Control.OperationId)),
    ]);
```

Quick actions enter the same bounded serial action queue as open-widget
actions. Their `SourceElementId` is `dashboard-card`. They run while lifecycle
state is `Visible`, not `Interactive`. A local action needs no capability
metadata. A control action must name the one exact published typed operation it
will invoke, as above. The host first records a sequence-bound dormant
reservation for at most 10 seconds so an action waiting in the bounded serial
queue can reach its call. That reservation is not broker authority. When the
exact typed operation is invoked, the runtime atomically activates a one-use
broker lease lasting at most two seconds; declaration, consent, payload, and
provider checks still apply. The SDK propagates the gesture privately only
inside that queued callback. Do not cache tokens, promote lifecycle, start a
subscription, or assume a second broker call is authorized.

### Open widget

Guide/Home remains host-owned and closes the overlay. The host uses D-pad and
two-dimensional left-stick movement for focus and `A` to activate the focused
button. `B`, `X`, `Y`, bumpers, triggers, stick clicks, Menu, and View are
available to the active widget input scope.

Attach a control-local shortcut to the Button that owns it. Attach a command
that must work anywhere in the open window once to the active scope root:

```csharp
UI.Stack("player.window",
        UI.Row("player.transport",
            UI.Button("Previous", "previous", "player.previous")
                .Icon(WidgetGlyph.Previous)
                .FocusRight("player.play"),
            UI.Button("Play", "toggle-playback", "player.play")
                .Icon(WidgetGlyph.Play, "Play or pause")
                .FocusLeft("player.previous")
                .FocusRight("player.next"),
            UI.Button("Next", "next", "player.next")
                .Icon(WidgetGlyph.Next)
                .FocusLeft("player.play")))
    .InputScope("player-window")
    .Shortcut(ControllerButton.LeftBumper, "previous")
    .Shortcut(ControllerButton.X, "toggle-playback")
    .Shortcut(ControllerButton.RightBumper, "next")
```

The default `OnControllerInputAsync` resolves against the exact last rendered
snapshot. For open input it validates the snapshot sequence and active scope,
checks the focused node first, then the active scope-root container. It never
searches an arbitrary unfocused child. Stale input or a focus ID outside that
scope is unhandled.
Therefore a Button shortcut is exact-focus-only; sibling Buttons do not make it
window-wide. Scope-root shortcuts work with any focus in that active scope and
with no focus, but never leak into a different nested active scope.
Override `OnControllerInputAsync` only for semantic controls that cannot be
represented as declarative actions; Guide/Home and arbitrary HID reports are
never transported.

Every successfully resolved open-widget action carries the exact active scope
as `WidgetActionEvent.InputScopeId`: A activation, focused shortcuts,
scope-root shortcuts, and Slider value changes all use the same rule. Validate
that value for manually routed nested actions; do not infer a route or dialog
from `SourceElementId`. Dashboard quick actions are not open-scope actions and
therefore do not publish an input scope.

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
- One node may bind each `(button, phase)` only once. Separate focused controls
  in one scope and separate scopes may reuse the same button.
- Focus is remembered independently per widget and scope. Keep IDs stable.
- Parent and sibling shortcuts are never searched while a nested scope is
  active.

This is the supported model for dialogs, settings pages, detail panes, and
other nested widget windows.

For more than one nested page, prefer `CreateNavigator<TRoute>` over manually
tracking pages, scope strings, Back actions, cancellation sources, and return
focus:

```csharp
private readonly WidgetNavigator<Route> _navigation;

public SettingsWidget() => _navigation = CreateNavigator(
    "settings.navigation", Route.Root);

// Render the current route through _navigation.Scope(root), then publish:
return new WidgetView(
    _navigation.Scope(root),
    InitialFocusId: _navigation.Value.InitialFocusId ?? DefaultFocus(route),
    ActiveInputScopeId: _navigation.Value.InputScopeId);
```

Use `Navigate(route, sourceFocusId)` for a root destination, `Push` for a
nested route, `Back` for an authored close, and `TryHandleBack(action)` before
other dispatch. The navigator publishes B only for nested routes and accepts
it only as a pressed action from the exact current input scope. Leaving a route
cancels `Value.RouteCancellationToken` before invalidating the new snapshot;
bind route-specific reads to that token. It remembers focus per route and the
parent source focus for Back. Depth is bounded (eight by default, 16 maximum),
known routes are bounded (32), and overflow returns `RejectedCapacity`.

Use `WidgetIds` for large stable hierarchies:

```csharp
var ids = WidgetIds.Scope("settings").Scope("devices");
var refreshId = ids.Id("refresh");
var deviceId = ids.KeyedId("device", providerDeviceKey);
```

Segments allow only ASCII letters, digits, `-`, and `_`; the complete ID is
validated against the protocol limit. `KeyedId` hashes the bounded durable key
into a deterministic opaque suffix, avoiding provider text or identity in the
snapshot. It does not make list positions stable—supply a real durable domain
key. A build-time duplicate/handler analyzer is not implemented yet.

## Tutorial 4: controller-owned scrolling

Use a Scroll container whenever content can exceed its clamped viewport:

```csharp
var list = UI.VerticalScroll(
    "activity.items.scroll",
    _items.Select(BuildActivityRow).ToArray())
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
focus falls to the nearest surviving focusable control in tree order and its
Scroll ancestor reveals it. A runtime replacement clears focus and scroll
memory.

Ordinary Stack/Row clipping is different: a clipped button is not a navigation
candidate because the host cannot reveal it.

### Focus-edge pagination (protocol v11)

Large remote collections should render a bounded page instead of accumulating
every item in one snapshot. Add `.Paginate(...)` to the Scroll; the host then
emits an ordinary widget action after controller focus moves into the first or
last configured number of direct children in the matching navigation
direction:

```csharp
var page = UI.VerticalScroll(
        "library.items.scroll",
        _page.Items.Select(BuildLibraryRow).ToArray())
    .Paginate(
        nearStartActionId: _page.Offset > 0 ? "library.previous-page" : null,
        nearEndActionId: _page.Offset + _page.Limit < _page.Total
            ? "library.next-page"
            : null,
        threshold: 1);
```

At least one action ID is required and `threshold` must be 1–8 (the default is
2). For a vertical Scroll, Up can emit the near-start action and Down the
near-end action; a horizontal Scroll uses Left and Right. The source element ID
is the Scroll ID, the active input-scope ID is preserved, and the action enters
the same serialized widget action route as a Button. No sentinel row or visible
**Load more** button is added by the host.

This is the low-level trigger contract. For an offset-based remote collection,
prefer `WidgetPagedResource<TItem>` below; it owns fetching coordination,
end-of-list checks, duplicate suppression, bounded caching, safe error state,
and entering-edge focus. Cursor paging and append/infinite-feed semantics are
not implemented. When using `Paginate` directly, keep only a small current page
in the render tree and derive row IDs from stable item identity or absolute
collection offset rather than the slot within the page. Do not append an
unbounded remote collection: snapshots still have a 2,048-node limit and the
worker/bridge default framed-message ceiling is 1 MiB.

## Controller-native value controls

Use `UI.Slider` instead of composing minus/progress/plus controls. A focused
Slider owns left/right D-pad and left-stick input; Up/Down still navigates and
optional A activation can perform a related discrete action:

```csharp
var volumeSlider = UI.Slider(
        value: _volume,
        minimum: 0,
        maximum: 1,
        step: 0.05,
        valueChangedAction: "audio.volume.set",
        id: "audio.volume",
        accessibilityLabel: "Application volume. Press A to mute",
        accessibilityValue: $"{Math.Round(_volume * 100)}%",
        activationAction: "audio.mute.toggle")
    .FocusUp("audio.master.volume")
    .FocusDown("audio.next.volume");
```

The change action receives an absolute, validated target. Never apply it as a
relative delta:

```csharp
if (action is { ActionId: "audio.volume.set", RequestedValue: { } requested })
{
    _volume = requested;
    Invalidate();
    await _audio.SetVolumeAsync(requested, cancellationToken);
}
```

The host supplies immediate transient feedback while the SDK serializes
actions. A contiguous pending tail for the same active lifetime, scope, Slider
ID, and action ID is latest-wins coalesced. A button or different Slider is an
ordering boundary. When the widget leaves its active Visible/Interactive
lifetime, the current controller operation is canceled and pending operations
are dropped.

Keep Slider and action IDs stable across snapshots. In the default direct mode,
do not publish horizontal focus neighbors: Left/Right belongs to value
adjustment even at a bound or while Disabled/Busy. Those states remain
focusable but suppress activation and adjustment, so focus cannot jump merely
because work became pending.

For a seek bar or other value that should not change during ordinary traversal,
opt into protocol-v10 activation-first behavior:

```csharp
var seek = UI.Scrubber(
        _position, _duration, TimeSpan.FromSeconds(5),
        "player.seek", "player.timeline")
    .RequireControllerActivation()
    .FocusLeft("player.previous")
    .FocusRight("player.next");
```

Outside adjustment mode, every direction remains normal focus navigation and
the Slider may declare horizontal neighbors. A enters host-owned adjustment
mode; Left/Right then changes the value, and A or B exits without dispatching a
widget action. Moving focus, leaving the surface, or replacing the widget also
clears the transient mode. Because A and B are reserved, activation-first
Sliders and Scrubbers cannot also use `.Activate(...)` or the constructor's
`activationAction`. Disabled and Busy controls remain focusable but cannot enter
adjustment mode.

See the [declarative UI Slider contract](declarative-ui.md#controller-native-slider-protocol-v3-and-v10)
and [controller component patterns](controller-ui-components.md#audio-icon-slider-percentage-pattern).

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

For a multipage root that changes between compact tabs and an expanded rail,
prefer `UI.NavigationShell`. Author the destination model and page content
once; the SDK creates presentation-specific controls while the host chooses the
active branch from logical-DIP surface dimensions:

```csharp
var destinations = new NavigationShellDestination[]
{
    new("home", "Home", "navigate.home", WidgetGlyph.Play),
    new("library", "Library", "navigate.library", WidgetGlyph.Music),
    new("settings", "Settings", "navigate.settings", WidgetGlyph.Settings),
};

var content = UI.VerticalScroll(
    "library.page.scroll",
    UI.Button("Open album", "album.open", "library.content.first"));

var shell = UI.NavigationShell(
    id: "library.shell",
    selectedDestinationId: "library",
    contentEntryFocusId: "library.content.first",
    content: content,
    destinations: destinations);

return new WidgetView(
    shell,
    InitialFocusId: "library.content.first");
```

The shell accepts two to eight destinations. Destination IDs describe logical
destinations; compact and rail element IDs are generated separately. Action IDs
describe routing intent and may be shared when the handler distinguishes
`SourceElementId`; they are never used as focus identity. `contentEntryFocusId`
must identify a focusable descendant of the shared content. An optional
`expandedPane` may add one persistent expanded-only subtree and an
`expandedPaneEntryFocusId` for navigation from the rail.

Compact means the final widget surface is less than 960 DIPs wide or 540 DIPs
high; otherwise the expanded branch is active. The inactive navigation branch
is absent from layout, paint, hit testing, focus, shortcuts, and accessibility.
The content subtree is authored once and remains present in both modes. Combine
the shell with `WidgetNavigator<TRoute>` when root destinations also own nested
routes, exact-scope B handling, return focus, or route cancellation. The SDK
Gallery is the copyable production-style reference.

Each shell destination receives one protocol-v13 focus-persistence ID shared by
its compact and rail controls. This ID exists only to preserve the logical focus
destination across mutually exclusive presentations. Action IDs remain routing
intent: distinct destinations may share one and distinguish the source element
in their handler without affecting focus recovery.

Use the lower-level protocol-v9 visibility modifier when a responsive structure
is not a navigation shell:

```csharp
var compact = BuildCompactSummary()
    .VisibleWhen(ResponsiveVisibility.CompactOnly);
var expanded = BuildExpandedSummary()
    .VisibleWhen(ResponsiveVisibility.ExpandedOnly);

return new WidgetView(UI.Stack("summary.root", compact, expanded));
```

Keep that root unconditional, assign different stable IDs to mutually exclusive
branches, and keep focus targets valid in the active branch. Prefer Row wrapping
or `ResponsiveGrid` when only placement, not hierarchy, changes.
`UI.ResponsiveBranch(...)` is the equivalent non-fluent form.

## Declarative UI API map

| SDK call | Result | Notes |
| --- | --- | --- |
| `UI.Stack(id, children)` | vertical container | Can start an input scope and own shortcuts. |
| `UI.Row(id, children)` | horizontal container | Can start an input scope and own shortcuts. |
| `UI.NavigationShell(id, selectedDestinationId, contentEntryFocusId, content, destinations, expandedPane?, expandedPaneEntryFocusId?)` | compact tabs or expanded rail around one shared page subtree | Protocol 13; two to eight stable destinations with shell-owned explicit focus persistence. Action IDs need not be unique. |
| `element.VisibleWhen(mode)` / `UI.ResponsiveBranch(mode, element)` | host-resolved conditional subtree | Protocol 9; use distinct compact/expanded branch IDs and keep the root unconditional. |
| `UI.ResponsiveGrid(id, minimumColumnWidth, maximumColumns?, children)` | responsive row-major Grid | Protocol 8; host derives bounded columns from final logical width. |
| `UI.VerticalScroll(id, children)` | vertical Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.HorizontalScroll(id, children)` | horizontal Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.Scroll(id, axis, children)` | explicit Scroll | Axis cannot be changed by a theme. |
| `scroll.Paginate(nearStartActionId, nearEndActionId, threshold?)` | focus-edge page actions | Protocol 11; threshold 1–8, at least one action, and no visible sentinel control. |
| `UI.Text(text, id, accessibilityLabel?)` | text | Non-interactive. |
| `UI.CodeText(text, id, accessibilityLabel?)` | semantic monospace text | Nonfocusable, whitespace-preserving, and bounded to 4,096 characters. |
| `UI.Button(label, action, id)` | focusable button | `A` invokes its action. |
| `focusable.PersistFocusAs(id)` | explicit cross-presentation focus identity | Protocol 13; use only on mutually exclusive controls representing one logical destination. Omission preserves legacy behavior. |
| `UI.ToggleButton(label, isOn, action, id)` | composed button | Emits On/Off text and selected semantics. |
| `UI.Stepper(...)` | composed row | Stable `.label`, `.decrement`, `.value`, `.increment` children. |
| `UI.Progress(value, maximum, id, label?)` | progress | Requires finite `0 <= value <= maximum`, `maximum > 0`. |
| `UI.Slider(value, minimum, maximum, step, valueChangedAction, id, label, value?, activation?)` | focusable value control | Protocol 3; absolute requested values and direct L/R adjustment. `.RequireControllerActivation()` opts into protocol-v10 A-to-adjust behavior. |
| `UI.Spacer(id)` | spacer | Layout-only. |
| `UI.Image(httpsUrl, id, alt, fit?)` | image | HTTPS only; host applies download/decode/cache limits. |
| `UI.Icon(glyph, id, label)` | semantic icon | Closed host-rendered glyph vocabulary. |
| `UI.LoadingIndicator(id, label, size?)` | indeterminate status | Protocol 5; native, nonfocusable, static under reduced motion. |
| `UI.ActionSurface(action, id, label, orientation, children...)` | rich full-surface action | Protocol 7; one focus/pointer/action target with bounded presentational children. |
| `UI.MediaTile(...)`, `UI.AppTile(...)` | rich tile ActionSurface | Optional `TileArtwork`, multiline copy, visible state, and one full-tile action. |
| `UI.Toast(title, message, tone, id, duration?, glyph?)` | transient feedback | No focus or timer; remove through lifecycle-owned widget state. |
| `UI.IconButton(...)` | icon-only button | Required accessible name plus stable size/variant classes. |
| `UI.Card(...)`, `UI.SectionHeader(...)`, `UI.Divider(...)` | nonfocusable hierarchy | Theme-respecting grouping, heading, and separator compositions. |
| `UI.StatusBadge(...)`, `UI.Alert(...)`, `UI.EmptyState(...)` | status and recovery | Visible non-color semantics; Alert/EmptyState permit at most one recovery action. |
| `UI.SegmentedTabs(...)` | bounded tab row | Stable author IDs, selected state, and explicit horizontal focus neighbors. |
| `UI.Switch(...)` | one-stop two-state setting | Visible/accessibility On/Off state; Disabled remains focusable. |
| `UI.ScopedDialog(...)` | nested input scope | Scope-owned B; publish its scope and an initial descendant focus while open. |
| `UI.ValueRow(...)`, `UI.ChoiceRow(...)` | metadata or full-row choice | ValueRow is read-only; ChoiceRow is one complete focus/action target. |
| `UI.SettingsRow(...)` | responsive actionable setting | Copy reflows independently; only `id.action` enters focus. |
| `UI.ActionSheet(...)`, `UI.Picker(...)` | bounded nested list | Host-owned vertical Scroll, stable option IDs, and scope-owned B. |
| `UI.ControllerHint(...)` | display-only input hint | Does not bind a shortcut; use it only when a visible hint is useful. |

All nodes can use `.Classes("name", ...)`. Buttons additionally provide
`.FocusUp/Down/Left/Right(id)`, `.Disabled(...)`, `.Selected(...)`,
`.Busy(...)`, `.Icon(...)`, `.LeadingInlinePng(...)`, and `.Shortcut(...)`.
`LeadingInlinePng` keeps trusted broker-projected artwork inside the complete
Button focus target; it accepts only the same bounded canonical PNG contract as
`UI.InlinePngImage` and replaces, rather than combines with, a semantic glyph.
Sliders provide
`.FocusUp/Down(id)`, `.Disabled(...)`, `.Busy(...)`, and `.Activate(...)`;
horizontal focus links are invalid because the control owns Left/Right.
ActionSurfaces provide directional focus helpers, Disabled/Selected/Busy, and
shortcuts on their one stable root. Their descendants cannot own input,
actions, focus, scopes, scrolling, shortcuts, or interaction state.

Current semantic glyphs are `Music`, `Play`, `Pause`, `Previous`, `Next`,
`Refresh`, `Shuffle`, `Like`, `Dislike`, `Repeat`, `Settings`, `Warning`,
`Check`, `Connection`, `Volume`, `Muted`, `Microphone`, `Wifi`, and `Ethernet`.
Widgets cannot supply SVG paths or icon-font names.

## Screen-reader and automation contract

When an open widget is queried by Windows UI Automation, the host exposes only
semantic nodes that survived final responsive layout, clipping, and active
input-scope selection. Text, images, icons, loading state, and progress publish
their accessible names; Button and ActionSurface publish Invoke; Slider
publishes writable RangeValue; bounded Progress publishes read-only RangeValue.
ActionSurface descendants remain presentation-only so a rich tile is announced
once. Disabled or Busy controls remain discoverable but cannot invoke.

Provider calls never enter widget code directly. Invoke, SetValue, and SetFocus
are queued asynchronously, and the host rechecks the exact runtime generation,
snapshot sequence, scope, node, action, and enabled state on its window thread.
Invoke and SetValue are treated as explicit accessibility user gestures only
for the currently visible widget; they enter its Interactive lifecycle and use
the same typed controller-action and capability-admission path as local input.
Your next immutable render is authoritative; do not depend on synchronous state
mutation during an accessibility request. Keep labels concise, supply a human
readable Slider value, and keep IDs stable for the lifetime of one logical
control.

The host coalesces renders and announces the newest immutable state through UIA
structure, logical-focus, and closed property events outside paint. Stable IDs
let name, value, enabled/selected, range, and physical-bounds changes remain
property updates; adding/removing controls or changing supported patterns
invalidates structure. Authors do not raise native events themselves.

Open-widget UIA is currently an implemented preview rather than the completed
screen-reader ship gate. The host tray publishes its visible widget tiles as a
single-selection ListItem set with Invoke; inactive widget controls are replaced
by that tray surface while controller focus is there. Dashboard title/status
nodes are not yet published, legacy MSAA is not implemented, and packaged
Narrator evidence is pending. Semantic snapshot tests therefore remain required
for widget acceptance.

Every node ID must be unique in the snapshot, at most 128 characters, and use
only ASCII letters, digits, `.`, `-`, and `_`. Do not derive IDs from list
positions or displayed text; changing an ID discards host focus/scroll memory.
Protocol limits are 2,048 nodes, depth 32, strings up to 4,096 characters, and
three dashboard quick actions. An ActionSurface additionally permits 1–8 direct
children, at most 32 descendants, and four relative content levels; a snapshot
that contains one automatically selects protocol v7 and fails closed on hosts
that do not understand it. A snapshot containing ResponsiveGrid selects
protocol v8; its minimum column width is 44–1600 DIPs and optional maximum is
1–32 columns. Responsive visibility selects protocol v9, activate-to-adjust
Slider behavior selects v10, and a paginated Scroll selects v11. Grid itself
is not focusable and preserves child IDs/order across reflow.

## Interaction state and reconciliation

Disabled and Busy Buttons and Sliders remain visible and focusable but do not
activate through the default router; a Slider also suppresses value changes in
either state. Selected buttons remain actionable and expose semantic state to
the renderer and GBSS. Disabled/Busy must not be used as a way to remove a
control from controller navigation. Stable focused IDs survive those state
changes without falling back to an unrelated control.

For a remote toggle, prefer the `WidgetOptimisticCommand` helper described
below. Its domain callbacks should:

1. Store the prior value.
2. Publish the intended value with `.Selected(newValue).Busy(true)` before
   awaiting I/O; the model/coordinator owns the resulting invalidation.
3. Reconcile authoritative events without allowing an older poll to overwrite
   the pending intent.
4. Clear Busy on confirmation or a bounded deadline.
5. On failure, restore only that feature's prior state, publish a short safe
   error, and invalidate again.

The [YT Music Community reference](../samples/YtMusicWidget/README.md)
demonstrates optimistic playback/rating/shuffle/repeat state, stale-event
guards, bounded progress interpolation, rapid ordered LB/RB actions, exact-port
local companion access, write-only pairing secrets, dashboard gesture
authority, and the public pack/install/AppContainer path. It has no trusted
catalog or custom desktop-worker fallback.

After the companion accepts Play/Pause, keep bounded reconciliation on the
widget lifecycle rather than the transient input-action token. Do not inspect a
render snapshot with optimistic state to decide that the remote system has
confirmed it; track the pending feature against an unmerged authoritative
snapshot. The YT Music regressions also show repeated playback commands
superseding an older refresh burst without serially blocking controller input.

For a smaller capability-free reference, install or render the
[SDK Gallery Community addon](../samples/SdkGalleryWidget/README.md). Its four
controller-native pages exercise the shipped modern components, stable state,
responsive Grid/Scroll behavior, nested Picker/ActionSheet B scopes, a
non-focus-stealing Toast, package-local GBSS, and the generic Community worker.
The gallery intentionally stays out of the built-in tray and uses no private
host API, so each composition is valid copyable author guidance.

## Lifecycle API

Lifecycle is host-authoritative and separate from process residency.

| State | Author contract |
| --- | --- |
| `Created` | Runtime-owned. `OnCreatedAsync` runs exactly once. Start only lightweight process-lifetime work and return promptly. |
| `Background` | Worker may remain resident, but it is not presented. Stop UI refresh, animation, controller work, and provider subscriptions. Only the bounded private-state service remains available for persistence. |
| `Visible` | Dashboard card is selected. Local quick actions and one exact declared control operation per capability-bearing user gesture can run; read capabilities may be used. |
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

Manifest `residencyPolicy` schema 1 is enforced separately from lifecycle:

- `keep-alive` is the default and retains the process in Background;
- `suspend-when-hidden` keeps the process resident but cooperatively cancels
  visible/state work and suppresses hidden rendering, input, invalidation, and
  broker access; and
- `unload-after-idle` requires a bounded `idleSeconds` from 5 through 86,400.
  The bridge caches the last good snapshot, sends bounded `Destroying`, tears
  down the process tree, then lazily recreates it when Visible again.

The protocol default remains `keep-alive` for compatibility, but the standard
controller scaffold selects `unload-after-idle` with a five-minute bound. Use
that bounded policy for ordinary widgets. Choose `keep-alive` only when a
documented process-lifetime Background operation genuinely requires it.

Before launch, the bridge also admits application workers against a host-wide
default envelope of eight processes and 512 MiB of declared Job memory. It
never silently evicts a `keep-alive` worker. When capacity is full, a new launch
fails with a remediation message until a resident worker is disabled, removed,
crashes, or reaches its explicit idle-unload bound. The trusted Settings worker
has one separate control-plane slot so diagnostics remain reachable.

The host never calls undocumented process/thread suspension APIs and never
infers unload from CPU or memory. Your callbacks and cancellation tokens still
own author work. An idle-unloaded widget is reconstructed, not resumed in
memory: keep stable element IDs for host focus restoration and persist only
approved durable state. Legacy `backgroundPolicy: none` maps to `keep-alive`;
legacy `suspend` maps to `suspend-when-hidden`. Do not declare both fields.

For small readable preferences or UI state, use
`HostServices.PrivateState`; do not declare it in the manifest. Restore once in
the first `OnActivatedAsync`, keep the value in memory for that worker, and
write meaningful changes with the last observed revision when conflict
detection matters. Do not read storage from `OnCreatedAsync`, save per render/
animation tick, or begin a final save in `OnDestroyingAsync`. Tokens,
passwords, and cookies belong in the separate private-secret service. The full
64 KiB JSON, rate, CAS, identity/update, uninstall-retention, and public test
fixture contract is in [Private widget state](private-widget-state.md).

## Runtime-owned async operations

Use the protected `Operations` coordinator for user-triggered or route-loading
work instead of owning task fields, cancellation sources, or semaphores:

```csharp
var refresh = Operations.RunSingleFlight(
    "library.refresh",
    async context => await RefreshAsync(context.CancellationToken));

var page = Operations.RunLatest(
    "library.page",
    async context => await LoadPageAsync(route, context),
    WidgetOperationLifetime.Active);

var save = Operations.RunSerial(
    "library.save",
    async context => await SaveAsync(context.CancellationToken),
    WidgetOperationLifetime.Widget);
```

Each stable key is one lane with one policy and lifetime while it is busy:

- `RunSingleFlight` starts once; another call with the same key joins the
  running completion instead of invoking its delegate.
- `RunLatest` starts once, then keeps at most one pending replacement. A newer
  call synchronously makes the active context non-current, requests its
  cancellation, supersedes any older pending call, and starts the newest only
  after the active delegate exits. Delegates never overlap for that key.
- `RunSerial` preserves FIFO order with one active delegate and at most 16
  pending calls per key.

Choose the shortest `WidgetOperationLifetime`: `Active` spans Visible and
Interactive, `State` belongs only to the exact current Background/Visible/
Interactive state, and `Widget` spans creation through Destroying for
intentional work such as an already-started authorization flow. Work is
rejected before creation, after Destroying begins, or when its requested
lifetime is unavailable. On a lifecycle boundary, the SDK cancels and awaits
all operations owned by the ending lifetime before it calls the corresponding
lifecycle transition callbacks; delegates must therefore honor
`context.CancellationToken` and finish promptly.

Admission and completion are separate. `WidgetOperationHandle.Admission` is
`Completed`, `Started`, `Joined`, `Replaced`, `Enqueued`,
`RejectedInactive`, or `RejectedCapacity`; `Completed` means the request was
satisfied synchronously without scheduling work, and `IsAccepted` is false
only for the two rejection cases.
`Completion` never faults and returns `Succeeded`, `Canceled`, `Superseded`,
`Failed`, or `Rejected`, with the exception attached only to `Failed`.
`OperationFailed` publishes that failure once. For latest-wins work, check
`context.IsCurrent` immediately before committing state; its generation becomes
non-current synchronously when a replacement is admitted.

The coordinator admits at most 32 keys and 64 total active/pending operations.
Capacity rejection is a result, not an invitation to spin or create a fallback
task. A busy key cannot change policy or lifetime. Use `IsBusy`, `Cancel`,
`WhenIdleAsync`, `WhenAllIdleAsync`, and `BusyChanged` for presentation and
tests. Busy-edge changes auto-invalidate the widget. Higher-level SDK resources
also own the synchronous cache-hit, reset, and snapshot-change invalidations
that do not come from an operation busy edge.

### Immutable widget models

When a group of fields forms one render state, create a model in the widget
constructor:

```csharp
private readonly WidgetModel<PlayerState> _model;

public PlayerWidget()
{
    _model = CreateModel(PlayerState.Initial);
}

public override WidgetView Render() => BuildPlayer(_model.Value);
```

`WidgetModel<TState>` serializes `Set` and `Update`, exposes an atomic
`Snapshot` with a monotonic revision, and invalidates once only when the
configured equality comparer reports a changed value. Treat state as immutable;
the SDK does not clone it. Update delegates execute under the model lock and
must stay quick and side-effect free. A result-bearing update can derive an
operation input from the exact committed revision:

```csharp
var mutation = _model.Update(state =>
{
    var next = state with { IsPlaying = !state.IsPlaying };
    return (next, next.IsPlaying ? PlayerCommand.Play : PlayerCommand.Pause);
});
```

Use its `Result` with `Operations`; do not reread unrelated widget fields.
`Changed` is a contained observer for diagnostics/tests, not a place to create
another mutable state graph. Simple widgets can continue using ordinary fields
and explicit `Invalidate()`.

Now Playing's `MediaSessionsWidget` is the medium production reference: all of
its render-facing state shares one model, and repeated selection of the current
session is equality-suppressed while a real selection change invalidates once.
Its transport path combines that model with the coordinator below. Domain
projection, provider-event merge, error copy, and rollback stay explicit widget
policy.

### Optimistic commands

Create one command over the model in the widget constructor:

```csharp
_playback = CreateOptimisticCommand(
    "player.playback",
    _model,
    new WidgetOptimisticCommandOptions<State, PlaybackRequest, ProviderCommand, Playback>
    {
        Policy = WidgetCommandPolicy.Latest,
        Lifetime = WidgetOperationLifetime.Active,
        Apply = (state, request) => new(
            state.Project(request),
            ProviderCommand.From(state, request)),
        Execute = (command, token) => Provider.ControlAsync(command, token),
        Reconcile = (current, command, result) =>
            current.MergeProviderResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = MapSafeCommandError,
        Fail = (current, baseline, command, error) =>
            current.RemoveProjection(baseline, command).WithError(error),
    });
```

`Apply` is a quick, side-effect-free serialized model update. It returns the
optimistic state and provider input derived from the exact same prior revision.
Call `_playback.Run(request)` and inspect the ordinary
`WidgetOperationHandle`; do not create a parallel task or input queue.

Choose policy per stable key:

- `SingleFlight` joins an in-flight command. A joined request neither executes
  nor projects duplicate state.
- `Latest` cancels/supersedes older work, projects the new request before `Run`
  returns, and retains the first baseline through a replacement chain.
- `Serial` preserves bounded FIFO execution and applies each optimistic
  projection only when that request's turn begins.

All policies use `WidgetOperations` limits and Active, State, or Widget
lifetime cancellation/draining. Inactive or capacity-rejected requests do not
call `Apply` and cannot mutate the model. `ShouldExecute: false` is different:
the request was admitted, so its projection may publish an unavailable or no-op
state, but the provider mutation is skipped.

`Reconcile`, `Rollback`, and optional `Fail` receive the **current** state, the
execution input, and either result, baseline, or error. Merge only the feature
owned by that command so subscription/provider events that arrived while I/O
was pending survive. For a Latest replacement chain, rollback receives the
first pre-chain baseline. The SDK rejects non-current late completion, but it
cannot infer domain identity, confirmation deadlines, or merge semantics.

Map exceptions to `WidgetCommandError`, whose code is a stable identifier and
whose safe UI message is limited to 256 visible characters. A mapper failure
uses `WidgetCommandError.Unexpected`. Mutations run once; the coordinator never
automatically retries. Without `Fail`, failure invokes `Rollback`; use `Fail`
when the model also needs the mapped safe error.

Now Playing is the first production consumer. It uses SingleFlight transport
commands, immediately projects Play/Pause, retains sibling control presentation,
and rolls back the affected session only when the provider snapshot revision
still matches.

### Non-paged resources

Create a `WidgetResource<TValue>` once when one provider read produces one
current value rather than an offset page:

```csharp
_status = CreateResource<PlayerStatus>("player.status", new()
{
    Load = token => LoadPlayerStatusAsync(token),
    MapError = _ => new WidgetResourceError(
        "player_unavailable", "Player status could not be loaded."),
    CacheDuration = TimeSpan.FromSeconds(30),
});
```

Call `EnsureLoaded()` from lifecycle or action code, never from `Render`.
`EnsureLoaded` reuses a fresh successful value; `Refresh` and `Retry` force a
read. The immutable snapshot exposes `NotLoaded`, `Loading`, `Ready`,
`Refreshing`, and `Error`, plus `Value`, `Error`, `HasValue`, and `Revision`.
An identical in-flight request joins the current operation. Errors must map to
a stable code and at most 256 presentation-safe characters; mapper failure
uses the generic safe fallback.

`RetainLastGoodValue` defaults to true, the cache duration defaults to five
minutes, and lifetime defaults to `Active`. `Publish(value)` is the
authoritative subscription/event path: it cancels an older read so that read
cannot overwrite the newer event. `Reset` cancels work and clears value,
cache, and error state. Both own invalidation, with a non-invalidating overload
only for an owner that immediately publishes one composed update.
`WhenIdleAsync` supports deterministic tests. The resource starts no polling,
subscription, or retry by itself and never infers domain merge policy.

### Bounded offset-paged resources

Create a `WidgetPagedResource<TItem>` once from the widget constructor for an
offset/limit provider:

```csharp
_items = CreatePagedResource<Item>("library.items", new()
{
    PageSize = 12,
    MaximumCachedPages = 6,
    MaximumCachedItems = 72,
    LoadPage = async (offset, limit, token) =>
    {
        var page = await LoadLibraryPageAsync(offset, limit, token);
        return new WidgetPage<Item>(page.Items, page.Offset, page.Limit, page.Total);
    },
    MapError = _ => new WidgetResourceError(
        "library_unavailable", "This library could not be loaded. Try again."),
    Viewports =
    [
        new("library.items.scroll",
            (_, absoluteIndex) => $"library.item.{absoluteIndex}",
            "library.empty.action"),
    ],
});
```

Call `EnsureLoaded()` from an appropriate lifecycle/action path. Render the
immutable `Snapshot.Page`, wrap the matching Scroll with
`_items.Paginate(scroll)`, and publish `Snapshot.RequestedFocusId` as the
view's initial focus when it is non-null. Route actions before other dispatch:

```csharp
if (_items.TryHandlePagination(action, out _)) return;
```

`EnsureLoaded(forceRefresh?)`, `Refresh`, `Move`, and `Retry` return observed
`WidgetOperationHandle` values. An identical in-flight request is `Joined`; a
fresh cache hit or an already-satisfied boundary is `Completed` with a
`Succeeded` completion. `TryGetCurrentItem` resolves an absolute index without
exposing the cache; `ClearRequestedFocus` acknowledges a consumed focus request;
`Reset` cancels work, rejects late publication, clears the LRU, and returns to
`NotLoaded`; `WhenIdleAsync` supports deterministic tests. `Reset(false)` and
`ClearRequestedFocus(false)` are only for an owning widget that immediately
publishes one composed route/state invalidation.

Render states are `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
`LoadingAdjacent`, and `Error`. `RetainLastGoodPage` defaults to true. Error
mapping must produce a stable identifier plus at most 256 visible characters;
never pass provider exceptions to UI. `Snapshot.HasValue`, `HasPrevious`, and
`HasNext` are render helpers, while `Revision` changes only with published
state. The resource invalidates the widget when its snapshot changes.

Bounds are explicit: page size 1–100, cached pages 1–8, cached items 1–512 and
at least one page, pagination threshold 1–8, and one or more unique configured
viewports. Cache duration defaults to five minutes; eviction is deterministic
LRU by both page and item limits. Returned pages must match the requested
offset, stay within the configured limit, contain no null items, and report a
consistent total. The default lifetime is `Active`; choose `State` or `Widget`
only when the provider operation truly needs that shorter or longer authority.
Sparse pages are valid when a provider filters unavailable server entries;
adjacent offsets therefore advance by the returned page limit, not the number
of rendered items. Render a bounded focusable placeholder for an empty
non-terminal page so focus-edge paging can continue.

Spotify 0.2.10 is the first migration. Its playlist and playlist-item resources
use 12-row windows, a six-page/72-item LRU, automatic protocol-v11 focus-edge
paging across compact and wide Scroll IDs, cached reverse navigation, and no
visible **Load more** row. Queue remains non-paged state owned by the widget.
Cursor/append collections are not supplied by this API; use
`WidgetResource<TValue>` only for one non-paged current value.

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
stable ID, classes, and `:focused`, `:pressed`, `:selected`, `:disabled`,
`:busy`), variables, safe package-relative `.gbss` imports, and an allowlist of
typed, bounded properties. Unknown properties, scripts, URLs, filesystem
paths, `calc`, expressions, and arbitrary functions are rejected. Snapshot
`:busy` state is resolved into the published maps. `:pressed` is activated only
for the exact physically held controller action and is canceled on focus,
surface, or snapshot changes.

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
| `system.audio.devices.read.v1` | list/watch sanitized input/output devices and default markers | Visible or Interactive |
| `system.audio.input.read.v1` | get/watch current default microphone volume/mute | Visible or Interactive |
| `system.audio.input.control.v1` | set current default microphone volume/mute | Interactive |
| `system.network.read.v1` | status, saved profiles, status events | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | switch to a saved profile | Interactive |
| `system.network.wifi.read.v1` | cached available-network snapshot/events; one explicit scan | read/events Visible or Interactive; scan Interactive |
| `system.network.wifi.connect.v1` | connect one current saved/open scan result | Interactive |
| `system.network.wifi.radio.read.v1` | get/watch software Wi-Fi radio state | Visible or Interactive |
| `system.network.wifi.radio.control.v1` | request software Wi-Fi radio On/Off | Interactive |
| `system.network.bluetooth.read.v1` | get/watch sanitized Bluetooth radio/discovery/device state | Visible or Interactive |
| `system.network.bluetooth.radio.control.v1` | request Bluetooth software radio On/Off | Interactive |
| `system.network.bluetooth.pair.v1` | pair one current opaque Bluetooth association endpoint and receive a typed outcome | Interactive |
| `system.network.bluetooth.manage.v1` | open Windows Bluetooth Settings after validating one current opaque device | Interactive |
| `system.activity.recent.read.v1` | list/watch bounded recent running applications | Visible or Interactive |
| `system.apps.library.read.v1` | page installed-app names/kinds and resolve authority-scoped durable SavedIds to current launch IDs | Visible or Interactive |
| `system.apps.library.launch.v1` | launch one current broker-issued opaque app ID | Interactive only |
| `system.media.sessions.read.v1` | list/watch sanitized system media sessions | Visible or Interactive |
| `system.media.sessions.control.v1` | control one broker-issued media session | Interactive, or one exact declared dashboard gesture while Visible |
| `network.loopback:<port>` | bounded JSON GET/POST to one exact IPv4 loopback port | GET Visible/Interactive; POST Interactive or one exact dashboard gesture while Visible |
| `storage.private-secrets.v1` | write-only package secret slots and metadata; optional host-side Bearer injection | metadata Visible/Interactive; save/delete Interactive |

Required capabilities are not auto-granted. Put core authority in
`permissions`, degradable features in `optionalPermissions`, then render
denied/unavailable states for both.

These are four separate gates: the manifest declares; the user allows or
blocks in Settings; the broker enforces fixed identity, declaration, consent,
and lifecycle; then the trusted provider/Windows enforce API, privacy,
hardware, and policy rules. Declaring a capability never bypasses another gate.
For example, nearby Wi-Fi still needs Windows precise-location permission.

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

Bluetooth IDs are equally opaque and current-snapshot-only. Pair through
`PairBluetoothDeviceAsync`; do not claim success or profile connectivity until
the returned outcome and following authoritative snapshot support it. Use
`OpenBluetoothDeviceSettingsAsync` only as a Windows-owned management fallback.
There is no public unpair or generic Connect/Disconnect API.

### Installed app-library example

Use the typed `HostServices.AppLibrary` surface; do not turn a display name,
path, command line, or launcher-specific ID into an action target:

```csharp
var page = await HostServices.AppLibrary.GetPageAsync(
    offset: 0,
    limit: 32,
    cancellationToken);

var savedIds = page.Items.Select(item => item.SavedId).ToArray();
// Persist SavedIds—not AppIds—in HostServices.PrivateState.
var restored = await HostServices.AppLibrary.ResolveSavedAsync(
    savedIds,
    cancellationToken);

if (LifecycleState == WidgetLifecycleState.Interactive && restored.Count != 0)
    await HostServices.AppLibrary.LaunchAsync(
        restored[0].AppId,
        cancellationToken);
```

`GetPageAsync` permits 1–64 items per request. `ResolveSavedAsync` accepts at
most 64 unique host-issued SavedIds, preserves request order, and omits apps
that are no longer available. Read is allowed only in Visible or Interactive;
launch is separately declared/consented and Interactive-only. Treat `AppId` as
an opaque provider-lifetime token and never persist it. `SavedId` is the
non-reversible publisher/package-scoped value for private state. The current
Windows provider exposes bounded Start Menu `.lnk` plus current-user
AppsFolder/AUMID registrations and conservatively reports them as Application.
Resolved curated entries may include a bounded host-rasterized
`IconPngBase64`; broad discovery remains text-only. Paths, AUMIDs, arguments,
and activation PIDs remain provider-private. The provider does not yet support
Steam/Xbox/other launcher aggregation or authoritative game detection. See the
[Games & Apps reference](games-and-apps.md).

Handle `WidgetCapabilityUnavailableException`, `WidgetCapabilityException`
using its stable `ErrorCode`, and normal lifecycle cancellation. Common codes
include `permission_denied`, `capability_not_declared`, `lifecycle_denied`,
`capability_revoked`, `platform_unavailable`, `app_not_found`, and
`launch_failed`. Treat unknown codes as a
generic bounded provider failure.

Exact-port local companions and private secrets are now public typed services;
they do not give the AppContainer worker ambient network or Credential Manager
access. Declare one `network.loopback:<port>` plus the separate
`storage.private-secrets.v1` grant when host-side Bearer injection is needed,
then call only `HostServices.Loopback` and `HostServices.PrivateSecrets`.
Secrets can be saved, replaced, deleted, or checked for presence, but never read
back into widget code. For companions that reject credentials with HTTP 401,
set `WidgetLoopbackRequestOptions.InvalidateBearerSecretOnUnauthorized` with the
Bearer slot so the host removes that exact rejected value before returning;
clear local state without racing a second delete. The [local companion service reference](community-companion-services.md)
documents lifecycle, consent/dashboard authority, exact SDK calls, identity
scope, limits, errors, and tests. YT Music is the first migration consumer.

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
  "presentation": { "icon": "connection" },
  "permissions": [
    "system.audio.output.read.v1"
  ],
  "optionalPermissions": [
    "system.audio.output.control.v1"
  ],
  "residencyPolicy": {
    "schemaVersion": 1,
    "mode": "unload-after-idle",
    "idleSeconds": 120
  },
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
| `hostApi` | Current compatible range is minimum `1.0`, maximum major `1`. This is independent of additive snapshot protocol versions 1–13. |
| `entrypoint.runtime` | Only `dotnet-worker`. |
| `entrypoint.assembly` | Exact-case normalized package-relative path with `/`, no traversal. |
| `entrypoint.type` | Namespace-qualified public concrete `Widget` type with a public constructor whose parameters are all optional. |
| `presentation.icon` | Optional closed semantic `WidgetGlyph` name, default `connection`; never a file, SVG, font, or drawing payload. |
| `permissions` | Required declarations. Required still means explicit user consent. |
| `optionalPermissions` | Degradable declarations; may not duplicate a required ID. |
| `residencyPolicy.schemaVersion` | `1`. Unknown versions fail validation. |
| `residencyPolicy.mode` | `keep-alive`, `suspend-when-hidden`, or `unload-after-idle`. Omit the object for keep-alive default. |
| `residencyPolicy.idleSeconds` | Required only for `unload-after-idle`; integer 5–86,400. |
| `backgroundPolicy` | Legacy manifest-v1 migration alias only: `none` → keep-alive, `suspend` → suspend-when-hidden. It cannot coexist with `residencyPolicy`. |
| `resourceRequest.memoryMb` | 16–256. The host owns the effective limit. |
| `resourceRequest.updateHz` | 1–60 metadata request. SDK periodic helpers are independently limited to at most 4 Hz. |
| `architectures` | One or both of `x64`, `arm64`; the current machine architecture must be listed. |

Unknown capability IDs are syntactically valid at manifest parse time but an
installed package declaring unsupported authority is omitted by the bridge.
Use only the published closed IDs above.

## Validate, list scenarios, render, replay, and test

Use the same compiler/validators as production:

```powershell
& $gbar validate .\scratch\Clock

& $gbar render .\scratch\Clock\snapshot.json

& $gbar replay `
  .\scratch\Clock\snapshot.json `
  .\scratch\Clock\replays\smoke.json
```

`gbar render` accepts only a bounded existing snapshot, validates it, and prints
the semantic tree. DLL input fails closed without resolving a type or touching
an output path. Persist snapshot fixtures from author-controlled typed-fake
tests with `SnapshotJson.Serialize`; use `gbar dev` when widget code must execute
through the production AppContainer/worker boundary. Running a downloaded
repository's test code is not a sandbox.

Named scenario manifests are implemented as a bounded discovery contract. Add
`gbar.scenarios.json` at the widget root to describe the states an eventual
isolated preview worker should expose:

```json
{
  "version": 1,
  "assembly": "bin/Release/net8.0/Clock.dll",
  "providerType": "Dev.Example.Clock.ClockScenarios",
  "scenarios": [
    {
      "name": "running",
      "factory": "Running",
      "description": "Deterministic local clock fixture"
    }
  ]
}
```

List and validate declarations without loading the provider assembly:

```powershell
& $gbar preview .\scratch\Clock
```

Listing validates the version, bounded relative assembly path, provider/factory
names, unique scenario names, descriptions, and manifest limits. It deliberately
does not load the assembly, resolve the provider type, invoke a factory, produce
a snapshot, activate a `Widget`, inject services, drive actions, select a
viewport, or render native pixels. The assembly, provider, and factory fields
reserve the version-1 declaration shape; listing alone does not prove that the
referenced code exists or implements a valid factory.

Selected scenario execution currently fails closed. Do not publish or depend on
`gbar preview --scenario ...` or `--output` as a working author workflow. A
scenario provider is developer code: executing it in the CLI process would give
it that process's ambient filesystem, network, process, and user authority, and
an in-process timeout could not safely terminate arbitrary managed work. That is
not a sandbox. Factory execution and snapshot output remain disabled until the
platform has a dedicated AppContainer preview worker with bounded IPC,
lifecycle, termination, and output validation. Continue using transport-free
unit tests with typed fakes for executable state coverage.

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
- root and nested scope routing, including one-level B, focusless root recovery,
  and responsive root Down-to-tray fallback;
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
Media Sessions, and YT Music suites are the current executable references. Run
the complete gate with:

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
3. On the same widget management page, open **Permissions & configuration**
   and independently grant only the capabilities you accept.

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

For a real local-companion addon using this same staging/catalog workflow, run
the [YT Music Community package helper](../samples/YtMusicWidget/README.md#build-and-tests).
It produces only the manifest, entry DLL, and GBSS needed by `gbar pack`, then
optionally performs install and enable. Capability consent remains a separate
Settings step.

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

Typed host services cross a separate identity-, declaration-, consent-, and
lifecycle-bound broker. A manifest cannot request raw pipe details, arbitrary
operation IDs, an AppContainer capability SID, or a trusted Job-only launch.
Loopback and secret services remain constrained broker authorities: the worker
receives no socket, URI authority, proxy, Credential Manager handle, or stored
secret value. See the [local companion reference](community-companion-services.md).

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
- `gbar preview` validates and lists named scenario declarations without loading
  their provider assembly. Selected execution and snapshot output fail closed
  until an AppContainer preview worker exists.
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

An action acknowledgement means it entered the active-lifetime bounded queue,
not that its I/O completed. Direct, quick, and controller-resolved actions share
the same serial order and cancellation. `AdmitActionAsync` exposes typed
`Enqueued`, slider-tail `Replaced`, `RejectedInactive`, and
`RejectedCapacity` results; `SendActionAsync` remains a compatibility wrapper
that throws on rejection. Later failures are surfaced by the host/runtime
`ActionFailed` event and must also produce safe widget state. The former
`ControllerActionFailed` name remains a compatibility alias. Worker restarts
are bounded and lazy.

Migration rule: do not detach ordinary provider commands merely to return from
`OnActionAsync` quickly. Await the command with the supplied token; the runtime
acknowledges admission first, observes failures, preserves FIFO order, and
drains cooperative work on deactivation. Retain a separate task only for a
different, explicit lifetime such as an already-authorized browser OAuth flow.
Protocol version 1 is unchanged: old empty action acknowledgements are treated
as `Enqueued`, and legacy failure notification names remain readable.

Before filing or closing a bug, check the [known-issues ledger](known-issues.md)
and preserve its acceptance criteria. Do not treat a green narrow unit test as
proof that a hardware/provider/presentation issue is closed.

## Current non-features

Do not design or advertise a widget around any of these yet:

- public SDK/runtime NuGet packages or a stable external template feed;
- publisher signing, certificate identity, revocation, or a public gallery;
- automatic update discovery, graphical install/remove, or repository builds;
- ambient internet/LAN access or loopback outside the exact-port JSON broker;
- a readable/general-purpose community secret or OAuth-token store;
- package-relative Image/font asset resolution;
- HTML, browser CSS, JavaScript, SVG, shaders, or native drawing payloads;
- text entry, arbitrary pointer UI, or arbitrary raw HID (`UI.Slider` and the
  purpose-built `UI.Scrubber` media composite are implemented);
- `Released`/`Repeated` shortcut routing end to end;
- CPU/disk quotas or AppContainer profile cleanup (versioned keep-alive,
  cooperative suspend, and bounded idle-unload policies are implemented); or
- universal Fullscreen Exclusive, anti-cheat, Guide-device, input-suppression,
  mixed-monitor, or game compatibility.

## Reference map

- [Widget quickstart](widget-quickstart.md)
- [Local companion HTTP and private secrets](community-companion-services.md)
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
