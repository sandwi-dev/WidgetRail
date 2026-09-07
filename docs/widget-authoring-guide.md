# Widget authoring guide and API map

Status: the package schema, managed SDK, additive declarative protocol versions 1–16,
controller routing, lifecycle, WRSS, local packaging/install workflow, and
typed host capabilities described as **implemented** below exist in this
repository, including exact-port local JSON and write-only private secrets.
An externally published SDK NuGet feed, publisher signing/revocation, a widget gallery,
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
- local WRSS classes and styles.

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

The separate full-trust application runtime is deliberately not parity with
that brokered AppContainer boundary. It is for an application the user has
reviewed and explicitly trusts with their ordinary current-user Windows
authority. Its public dependency is the narrow `WidgetApplicationRuntime`
bootstrap, not the host-side `WidgetRuntime` or `PlatformBroker` assemblies.

An author can use today:

- the complete declarative UI/controller/lifecycle SDK and safe WRSS cascade;
- `HostServices` typed capabilities, private state, exact-port companion JSON,
  and write-only private secrets when declared/allowed;
- transport-free fakes through `WidgetTestHostServicesBuilder`;
- `wrail new|validate|preview|render|replay|dev|pack|install|enable|disable` plus immutable
  version selection/rollback, and host-owned `wrail config` management for
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
| Declarative snapshot protocol | `1` through `33` | Generated `ViewSnapshot.ProtocolVersion` | Shape of one rendered UI checkpoint and the optional atomic update contract. The SDK selects the highest version required by the complete tree automatically. |

A plain Stack/Row view is emitted as protocol 1. Scroll/surface hints require
v2; Slider v3; dashboard gesture authority v4; LoadingIndicator v5; inline PNG
v6; ActionSurface v7; ResponsiveGrid v8; responsive visibility v9;
activation-first Slider v10; focus-edge pagination v11; RepeatOne glyph v12;
explicit focus persistence v13; cursor collections and opaque artwork handles
v14; host-owned bounded TextEntry v15; protocol v16 is retired before release;
independent surface-axis sizing v17; atomic presentation
updates v18; and virtual collection presentation windows v19.
Combining features selects the highest
required version. These additive snapshot features do **not** change the
package host API range, which remains `1.0` through major `1`.

Protocol v18 adds an optional, capability-negotiated atomic presentation-update
transport. Widget authors still return complete immutable `WidgetView` values;
they never construct or sequence update operations. The SDK compares stable
element IDs and automatically emits typed property changes, keyed child
insert/remove/move operations, or subtree replacement. An absent capability,
missing or stale base, unstable identity, invalid/oversized batch, or cheaper
complete representation falls back deterministically to a full checkpoint.
The receiver validates the whole candidate before publication, so no partial
tree becomes visible.

Runtime, bridge, and capability-broker transports also have internal protocol
versions. Widget code does not set or negotiate them; use
`WidgetWorkerBootstrap` for sandboxed workers or the narrow
`WidgetApplicationBootstrap` described below.

## Full-trust Community application runtime

Use this path only when an application genuinely requires ordinary current-user
Windows authority that the closed capability broker does not expose. It is not
an AppContainer escape hatch for an existing sandboxed widget, and the host
never silently promotes a sandboxed manifest after a launch failure.

Declare one exact package-relative executable in the strict manifest:

```json
"entrypoint": {
  "runtime": "full-trust-application-v1",
  "executable": "payload/ExampleApplication.exe"
}
```

The executable references the `WidgetRail.WidgetSdk` package supplied
by the local author distribution and starts through its narrow application
bootstrap:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseAppHost>true</UseAppHost>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WidgetRail.WidgetSdk"
                      Version="THE-VERSION-CREATED-BY-YOUR-WRAIL-DISTRIBUTION" />
  </ItemGroup>
</Project>
```

Use the exact version and cleared local feed generated by the matching `wrail`
distribution; do not add a checkout-relative `ProjectReference`.

```csharp
using WidgetRail.WidgetRuntime;

return await WidgetApplicationBootstrap.RunAsync(
    args, () => new ExampleApplicationWidget());
```

The author artifact contains `WidgetApplicationRuntime.dll`, `WidgetSdk.dll`,
and `WidgetProtocol.dll`. It intentionally contains neither the host-side
`WidgetRuntime.dll` nor `PlatformBroker.dll`; full-trust applications use normal
OS APIs rather than product/domain capability contracts. Assembly/type fields
and declared capabilities conflict with this runtime and fail manifest
validation.

The [Playnite Library Community sample](../samples/PlayniteLibraryWidget/README.md) is
the complete domain example: its Community
package owns installed-game discovery, bounded caches and organization state,
opaque SavedIds, source health, and exact launch revalidation while the host owns
only generic package admission, lifecycle, IPC, and presentation. Its manifest
declares no product capability, and its payload contains no product broker or
app-library provider assembly.

Pack the immutable application, then make the trust decision explicit on both
installation and enablement:

```powershell
wrail pack .\ExampleApplication -o .\ExampleApplication.wrwidget
wrail install .\ExampleApplication.wrwidget --accept-full-trust
wrail enable dev.example.application --accept-full-trust
```

Before accepting, the CLI and Settings state that the exact package executable
runs as an ordinary current-user process, not in AppContainer. It can access
the user's files, network, registry, databases, and child processes without a
host capability grant. Package validation, a content lease, exact executable
selection, random session nonce, connected PID validation, strict bounded
messages, lifecycle, failure/restart, update selection, disable, and removal
remain host-owned. They prevent package substitution and cross-session protocol
confusion; they do not reduce the application's OS authority.

The generic runtime has no provider, OAuth, identity, package-ID, assembly/type,
element/style, or declarative-tree special cases. A full-trust application
still returns ordinary `WidgetView` snapshots and receives the same bounded
actions and lifecycle transitions as any other widget. It may create child
processes and use files, databases, and sockets, and the host imposes no private
CPU, memory, socket, file, database, or process quota beyond existing package
and protocol bounds. Disable, replacement, removal, host shutdown, or worker
failure closes the owning kill-on-close Job so the application process tree
does not outlive its admitted session.

## Tutorial 1: scaffold and run the minimal widget

Prerequisites are Windows, PowerShell, the .NET 8 SDK, and a `wrail` build or
installation containing the controller template. The generator writes its
matching `WidgetRail.WidgetSdk` package into `.widgetrail/packages` and a
`NuGet.Config` that clears external feeds. The generated project therefore
builds offline after scaffolding, without a platform checkout or an absolute
machine-specific project reference.

Run the [canonical offline author journey](widget-quickstart.md#create-and-build-a-widget)
to build the CLI, scaffold `VolumeControl`, compile and execute its generated
test, validate/render/replay, package two versions, install/select/roll back,
and remove them. Those marked blocks and the complete generated source are
checked against one external temporary-directory fixture. This longer guide
starts from that proven project and adds patterns; it does not maintain a
second getting-started command sequence.

The local package is a deterministic offline scaffold dependency bundled by
that `wrail` version; it is not an externally published feed or proof of
publisher identity. The complete built `wrail` output directory is portable as
one local artifact: keep the executable, sibling runtime/assembly files, and
`templates/ControllerWidget` together. A copied distribution can scaffold,
build, validate, and package a basic widget from an unrelated repository with
no source-tree project reference or template override. Choose the `basic`,
`data`, `media`, `embedded-media`, or `multipage`
profile with `--template`. They respectively demonstrate local lifecycle state,
`WidgetResource` loading/error/retry, package-authored pinned layouts, the
provider-neutral embedded-media lifecycle, and `WidgetNavigator` plus one
responsive `NavigationShell` tree.
Each sibling `MSTest.Sdk` 4.3.2 test executes the same credential-free semantic
scenario declared for isolated `wrail preview`. If the CLI installation lacks its SDK assemblies or the
bundled template, scaffolding fails before the target directory is written.
The bundled template's version-2 inventory is closed: it labels
bounded UTF-8 replacement templates separately from byte-preserved assets and
rejects missing, undeclared, duplicate, traversing, reparse, or oversized
entries. Generation and validation occur in a private sibling staging
directory; one final directory rename publishes the project. Choose an output
path that does not exist. On any failure, `wrail` removes its staging directory
without deleting or overwriting an author-owned path.

The current CLI/template/SDK release unit is the pre-release version
`0.3.0-dev`. The generated local package appends a deterministic
`.local.<16-hex>` content suffix and the project consumes that exact version.
Contributors changing public SDK signatures must use the checked-in
[Widget SDK compatibility workflow](widget-sdk-compatibility.md); additions,
removals, and signature changes all require an intentional baseline diff, and
breaking pre-release resets do not require retaining obsolete APIs.

For a two-source native player, generate `--template embedded-media` and follow
the [embedded-media lifecycle cookbook](embedded-media-widget.md). It stages the
canonical runtime explicitly, declares one full player plus optional compact
pinning, and demonstrates retained-hidden route changes and exact terminal
correlation without provider credentials or native-host source.

Omitting `--template` selects `basic`; an unknown profile is rejected before
staging and leaves no target. Every generated widget project builds from its
project-local SDK feed without a checkout reference. The MSTest package itself
is the standard Microsoft SDK restored by normal .NET tooling.

The complete compile-tested basic source is shown once in the
[quickstart](widget-quickstart.md#canonical-generated-widget-source). The
smallest useful widget remains a public `Widget` subclass with a public
parameterless constructor (or one whose parameters are all optional).

`Render()` returns current state; it must not perform network, device, blocking
file, or long-running work. `Invalidate()` tells the host that visible state
changed. It does not mutate focus or force an immediate paint.

The checked-in [Clock sample](../samples/ClockWidget/ClockWidget.cs) is the
minimal compiled hand-built reference.

Advanced custom executable workers are outside the canonical starter: they
should delegate startup to `WidgetWorkerBootstrap.RunAsync(args, () => new
ClockWidget())` rather than parse host arguments. Installed packages normally
use the platform's generic worker host and name the widget assembly/type in
their manifest; they do not need to ship a custom worker executable.

## Tutorial 2: make the controller model intentional

### Dashboard card (hovered, not open)

The host reserves Guide/Home, D-pad and horizontal left-stick navigation, `A`
to open, `B` to close, and tray `Y`. A `Y` tap enters/exits reorder; a fixed
700 ms hold restarts the exact revalidated selected worker once through the
host's F5 reload path. Neither gesture is a widget input. A selected card may
expose up to three quick actions on `X`, `LB`, `RB`, `LT`, `RT`, either stick
click, or Menu. View is reserved for host pinned-surface navigation and cannot
be declared as a quick action. The mapping comes from the widget snapshot; it is not
hard-coded by the shell:

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
button. `B`, `X`, `Y`, bumpers, triggers, stick clicks, and Menu are available
to the active widget input scope. View remains host-owned even while an overlay
widget is open; declaring it as a shortcut fails validation.

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
    .Shortcut(ControllerButton.LeftBumper, "previous", label: "Previous")
    .Shortcut(ControllerButton.X, "toggle-playback", label: "Play or pause")
    .Shortcut(ControllerButton.RightBumper, "next", label: "Next")
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

Give every shortcut that should appear in the open-widget controller guide an
explicit bounded `label`. The guide falls back to visible text and then the
accessibility label only when the shortcut is owned directly by that named
control. Do not use a scope root's accessibility label to describe multiple
commands, and do not duplicate a shortcut label through `QuickActions`:
dashboard QuickActions are a separate, maximum-three surface. Leave input-only
or hidden bindings unlabeled when they should not be advertised. The labeled
form is an additive overload; the original unlabeled `.Shortcut(...)` CLR
signature remains available to already-built widgets.

Every successfully resolved open-widget action carries the exact active scope
as `WidgetActionEvent.InputScopeId`: A activation, focused shortcuts,
scope-root shortcuts, and Slider value changes all use the same rule. Validate
that value for manually routed nested actions; do not infer a route or dialog
from `SourceElementId`. Dashboard quick actions are not open-scope actions and
therefore do not publish an input scope.

Declare shortcuts with the `Pressed` phase. Eligible discrete shortcuts can set
`repeatPolicy: ControllerActionRepeatPolicy.WhileHeld` to receive bounded
`Repeated` action events after the initial press; the default is edge-only.
Do not bind `Released` or `Repeated` directly, and do not bind `A` or D-pad as
shortcuts. B and host-reserved shell/navigation buttons cannot opt into repeat.
Keep a `WhileHeld` shortcut or QuickAction declaration published while its
operation is pending or busy. Pending work gates dispatch and admission, not
declaration: withdrawing the binding retires the host-owned hold without an
error.

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
- A `Stack`, `Row`, `Scroll`, or `Grid` can opt into remembered-child entry
  with `.RememberChildFocus("controls.play")`. The container remains invisible
  to focus and accessibility. An explicit directional neighbor may target the
  container ID; entry restores its last valid focused descendant, then the
  authored initial child, then the first valid focusable descendant. Ordinary
  spatial navigation is unchanged, and responsive or input-scope boundaries
  cannot be crossed. Groups may nest: focusing a descendant updates every
  opted-in ancestor, while each explicit group target resolves that group's
  memory independently. `UI.Card` is a styled `Stack` and follows this rule;
  responsive and collection wrappers are not containers.
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
known routes are bounded (32), and overflow returns `RejectedCapacity`. Pass a
page `ContainerElement` directly to `Scope`: Stack, Row, Scroll, Grid, and
future container roots preserve their concrete immutable type. Do not cast a
page root to a particular layout type; a leaf control is rejected immediately
because it cannot own the navigator input scope or its Back shortcut.

For flat root sections that also own nested routes, opt into one shared root
scope rather than maintaining a second page state beside the navigator:

```csharp
_navigation = CreateNavigatorWithOptions(
    "library.navigation",
    Route.Home,
    new WidgetNavigatorOptions<Route>
    {
        SharedRootScopeId = "library.root",
        RootRoutes = [Route.Home, Route.Browse, Route.Settings],
    });

var page = BuildPage(route)
    .RememberChildFocus(DefaultContentFocus(route));
return new WidgetView(
    _navigation.Scope(page),
    InitialFocusId: _navigation.Value.InitialFocusId,
    ActiveInputScopeId: _navigation.Value.InputScopeId)
{
    FocusGroupEntryRequest = _navigation.Value.FocusGroupEntryRequest,
};
```

Call `NavigateRoot(route)` for A on a persistent header: it changes the page
without publishing an entry request, so the still-valid logical header remains
focused. Call `NavigateRoot(route, groupId)` for LB/RB section switching. That
overload emits one increasing, scope-validated request only when the root changes;
the host enters the destination's `RememberChildFocus` group once, using its
remembered valid child and then its authored default. Repeated data publications
cannot replay a consumed request. `RootRouteCancellationToken` owns the selected
page across nested routes, while `RouteCancellationToken` owns the current root
or nested route. Use `PushFromAction(route, action)` when a shortcut such as Y
opens a nested page without an opener button; Back then returns to the action's
actual `FocusedElementId`, not the ancestor that declared the shortcut.

Omitting `WidgetNavigatorOptions` preserves the existing generated per-route
scopes and focus behavior. Do not mirror a navigator value into `WidgetModel`,
publish a remembered group as `InitialFocusId`, synthesize D-pad input, or keep a
second author-owned copy of host remembered-child focus.

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

The host resolves an edge action before geometric focus can leave that Scroll.
While the adjacent load is pending, repeated edge input joins the same resource
operation instead of starting another provider call. A successful replacement
requests the entering-edge row; after the host remembers that focus, later
unrelated snapshots with the same request preserve the user's current row. A
failed adjacent load retains the last good page and suppresses automatic retry
until the user activates the visible retry action.

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

The five-second step above is an authoring choice, not a framework default.
Each widget chooses a step appropriate to its media duration and interaction
density, or publishes a direct absolute Slider target for an exact seek.

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
        Appearance = WidgetSurfaceAppearance.Transparent,
        WidthMode = WidgetSurfaceAxisMode.Preferred,
        HeightMode = WidgetSurfaceAxisMode.Content,
        PreferredWidth = 560,
        PreferredHeight = 420,
        MinimumWidth = 360,
        MinimumHeight = 260,
    });
```

`Appearance` is a closed advisory treatment: `Theme` (the default),
`Transparent`, or `Solid`. It changes only the host-owned widget surface and
grants no compositor, opacity, window, input, focus, or accessibility
authority. The user's exact per-widget override wins over the global override,
which wins over the declaration. High contrast, reduced transparency,
unavailable transparent composition, or a zero backdrop that would make
content unreadable forces a safe solid fallback. Ordinary views and
package-authored pinned layouts use the same rule.

`Mode` supplies the existing `Adaptive`, `Compact`, `Standard`, or `Wide`
fallback dimensions. `WidthMode` and `HeightMode` independently select how the
host admits each axis:

- `Preferred` (the default) uses the validated preferred extent and stays
  stable as live data changes.
- `Content` measures the immutable view with Taffy, then clamps it between the
  authored minimum and preferred extents. Use it for deliberately intrinsic,
  mostly static composition rather than changing provider lists.
- `FillAvailable` consumes the safe extent admitted from the active monitor's
  work area, DPI, interface scale, and accessibility policy.

Responsive width is admitted before intrinsic height. For example,
`Preferred` width plus `Content` height first fixes the responsive width, then
measures wrapped text and grid reflow once, and finally lays out at the admitted
viewport. The host performs at most one intrinsic measurement followed by the
ordinary final layout. The tray and controller guide retain their fixed screen
anchor while only the content envelope changes.

Preferred dimensions and minimum dimensions are optional **pairs**: specify
both width and height or neither. Widths must be finite and in 240–1600 DIPs;
heights must be finite and in 180–1200 DIPs. A minimum cannot exceed its
preferred value. The host may choose a smaller or larger safe surface and can
render below the requested minimum when the active work area, shell chrome,
DPI, interface scale, or accessibility scale requires it.

Surface hints belong to one `WidgetView`, so a compact status page and a wide
media page can publish different modes. Every view must still reflow and use
Scroll for overflow after host clamping.

The current production widgets provide concrete policy references. Their
first-page contracts are intentionally stable unless a row notes otherwise:

| Widget | Width / height policy | Preferred DIPs | Minimum DIPs | Rationale |
| --- | --- | ---: | ---: | --- |
| Settings | Preferred / Content on root; Preferred / Preferred on nested pages | 880x520 | 520x360 | The bounded root category grid is intrinsic; changing nested inventories and diagnostics remain stable. |
| Audio Mixer | Preferred / Preferred | 520x520 | 320x360 | Provider-driven session rows keep a stable surface; cards and slider rows stretch to the admitted width while each slider owns the flexible remainder. |
| Network Controls | Preferred / Preferred | 560x700 | 320x420 | The first Scan action/status fit at preferred height; dynamic scan/Bluetooth rows stay stable and scroll-reveal at constrained height. |
| Games & Apps | Preferred / Preferred | 820x600 library; 820x280 bounded state page | 420x300 library; 420x250 state page | Library/catalog content is paged and dynamic; bounded status pages keep explicit stable extents. |
| Playnite Library | Preferred / Preferred | 980x700 | 420x340 | Its large cursor collection, hero rail, and details routes share one stable envelope. |
| Now Playing | Preferred / Preferred | 580x400 | 360x330 | Media-session availability and metadata change independently of host placement. |
| Spotify | Preferred / Preferred | 980x560 | 620x400 | Playback, queue, devices, and cursor collections are provider-driven. |
| YT Music | Preferred / Preferred | 760x440 | 480x340 | One full-width raised media panel reflows at compact width while playback state remains live. |

These values are authored content envelopes, not HWND dimensions. Do not copy a
row merely to imitate another widget: choose Content only when direct evidence
shows a bounded, mostly static tree benefits from intrinsic sizing without
live-data resize churn.

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

If the compact navigation must share an author-owned header with separate
noninteractive status or controller help, request its two named parts. Do not
index `NavigationShell` children or copy its destination construction:

```csharp
var parts = UI.NavigationShellParts(
    id: "library.shell",
    selectedDestinationId: "library",
    contentEntryFocusId: "library.content.first",
    content: content,
    destinations: destinations,
    compactLeadingAdornment: previousHint,
    compactTrailingAdornment: nextHint);

var header = UI.Row(
        "library.header",
        parts.CompactNavigation.AddClasses("library-header-navigation"),
        UI.ControllerHint(ControllerButton.Y, "Settings", "library.settings.hint")
            .AddClasses("library-header-status"))
    .Classes("library-header");

return new WidgetView(
    UI.Stack("library.root", header, parts.Body),
    InitialFocusId: "library.content.first");
```

```css
.library-header { width: 100%; min-width: 0; align: center; gap: 12px; }
.library-header-navigation { min-width: 0; flex-grow: 1; flex-shrink: 1; }
.library-header-status { flex-shrink: 0; }
```

`CompactNavigation` retains compact-only visibility. `Body` retains the
expanded rail, optional persistent pane, and exactly one shared content
subtree. The ordinary `UI.NavigationShell` overloads arrange those same parts
inside the existing shell root and remain source- and binary-compatible. Keep
both parts in the same input scope because generated navigation focus links
target descendants in `Body`. Let navigation absorb constrained-width shrink;
do not add fixed or percentage header heights.

When an asynchronously entered destination has no real focusable content yet,
pass an explicit unavailable entry instead of inventing a focusable loading
button:

```csharp
var contentEntry = pageIsLoading
    ? NavigationShellContentEntry.Unavailable
    : NavigationShellContentEntry.Available("library.content.first");

var parts = UI.NavigationShellParts(
    id: "library.shell",
    selectedDestinationId: "library",
    contentEntry: contentEntry,
    content: pageIsLoading
        ? UI.LoadingIndicator("library.loading", "Loading library")
        : BuildLibraryContent(),
    destinations: destinations);
```

`Unavailable` omits the compact `Down` and expanded-rail `Right` links that
would otherwise name missing content. An independently available
`expandedPaneEntryFocusId` remains reachable from the expanded rail. Publish
`Available(focusId)` as soon as the real target exists. This value controls only
shell composition: it does not create a route, focus request, focus memory, or
loading state. Keep those responsibilities in `WidgetNavigator`, the host, and
the widget's immutable render state respectively.

Keep the navigator's same entry request published while the destination moves
from unavailable loading content to its real remembered-focus group. Protocol
v45 hosts retain that request without focusing loading UI, then resolve the
remembered child or authored initial child once real content is ready. A newer
deliberate navigation, activation, pointer, or accessibility choice cancels the
pending entry so late content cannot steal focus; automatic layout and reopen
reconciliation do not cancel it.

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
the shell with an optionally shared-root `WidgetNavigator<TRoute>` when root
destinations also own nested routes, exact-scope B handling, return focus, or
page/route cancellation. The SDK Gallery is the copyable production-style
reference, including A header retention, LB/RB remembered-group entry, and a
Y-opened nested route.

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
| `UI.NavigationShell(id, selectedDestinationId, contentEntryFocusId or contentEntry, content, destinations, expandedPane?, expandedPaneEntryFocusId?)` | compact tabs or expanded rail around one shared page subtree | Protocol 13; two to eight stable destinations with shell-owned explicit focus persistence. Use `NavigationShellContentEntry.Unavailable` only while no real content target exists. Action IDs need not be unique. |
| `UI.NavigationShellParts(id, selectedDestinationId, contentEntryFocusId or contentEntry, content, destinations, expandedPane?, expandedPaneEntryFocusId?, compactLeadingAdornment?, compactTrailingAdornment?)` | named compact navigation and shell body for an author-owned outer header | Same protocol/tree owners as `NavigationShell`; keep `Body` below the header rather than beside it. |
| `element.VisibleWhen(mode)` / `UI.ResponsiveBranch(mode, element)` | host-resolved conditional subtree | Protocol 9; use distinct compact/expanded branch IDs and keep the root unconditional. |
| `UI.ResponsiveGrid(id, minimumColumnWidth, maximumColumns?, children)` | responsive row-major Grid | Protocol 8; host derives bounded columns from final logical width. |
| `UI.VerticalScroll(id, children)` | vertical Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.HorizontalScroll(id, children)` | horizontal Scroll | Protocol 2; host-owned focus-follow offset. |
| `UI.Scroll(id, axis, children)` | explicit Scroll | Axis cannot be changed by a theme. |
| `scroll.Paginate(nearStartActionId, nearEndActionId, threshold?)` | focus-edge page actions | Protocol 11; threshold 1–8, at least one action, and no visible sentinel control. |
| `UI.Text(text, id, accessibilityLabel?)` | text | Non-interactive. |
| `UI.CodeText(text, id, accessibilityLabel?)` | semantic monospace text | Nonfocusable, whitespace-preserving, and bounded to 4,096 characters. |
| `UI.Button(label, action, id)` | focusable button | `A` invokes its action. |
| `UI.Select(label, options, id, accessibilityLabel?)` | anchored single-select | Protocol 41; one focus stop, 1–128 stable options, host-owned A/Up/Down/B popup interaction, and exact option actions without page reflow. |
| `focusable.PersistFocusAs(id)` | explicit cross-presentation focus identity | Protocol 13; use only on mutually exclusive controls representing one logical destination. Omission preserves legacy behavior. |
| `UI.Switch(label, isOn, action, id)` | composed button | Emits On/Off text and selected semantics. |
| `UI.Stepper(...)` | composed row | Stable `.label`, `.decrement`, `.value`, `.increment` children. |
| `UI.Progress(value, maximum, id, label?)` | progress | Requires finite `0 <= value <= maximum`, `maximum > 0`. |
| `UI.Slider(value, minimum, maximum, step, valueChangedAction, id, label, value?, activation?)` | focusable value control | Protocol 3; absolute requested values and direct L/R adjustment. `.RequireControllerActivation()` opts into protocol-v10 A-to-adjust behavior. |
| `UI.Spacer(id)` | spacer | Layout-only. |
| `UI.Image(httpsUrl, id, alt, fit?)` | image | HTTPS only; host applies download/decode/cache limits. |
| `UI.Icon(glyph, id, label)` | semantic icon | Closed host-rendered glyph vocabulary. |
| `UI.LoadingIndicator(id, label, size?)` | indeterminate status | Protocol 5; native, nonfocusable, static under reduced motion. |
| `UI.ActionSurface(action, id, label, orientation, children...)` | rich full-surface action | Protocol 7; one focus/pointer/action target with bounded presentational children. |
| `UI.Tile(...)` | rich tile ActionSurface | Optional `TileArtwork`, multiline copy, visible state, one full-tile action, and protocol-v34 bounded contextual actions. |
| `UI.PosterTile(...)` | fixed-aspect poster ActionSurface | Protocol 37; optional bounded Cover artwork fills the card behind a themeable bottom scrim and fixed copy rows while the whole poster remains one action/accessibility target. |
| `UI.FocusPresentationSurface(content, defaultPresentation, id)` | focus-associated presentation consumer | Protocol 40; native focus selects one bounded admitted display-only fragment without worker or input authority. |
| `UI.Toast(title, message, tone, id, duration?, glyph?)` | transient feedback | No focus or host timer; remove widget-owned state with `WidgetTimedMutation`. |
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

All nodes can use `.Classes("name", ...)` to replace author-owned classes and
`.AddClasses("variant")` to append them. SDK composites keep their intrinsic
`wrail-*` root, state, and generated part classes through either operation;
primitives with no intrinsic semantics still replace their complete authored
list. Valid author classes may also use a `wrail-*` name; required ownership is
stored intrinsically by the component rather than inferred from the name. Buttons additionally provide
`.FocusUp/Down/Left/Right(id)`, `.Disabled(...)`, `.Selected(...)`,
`.Busy(...)`, `.Icon(...)`, `.LeadingInlinePng(...)`, and `.Shortcut(...)`.
`LeadingInlinePng` keeps trusted broker-projected artwork inside the complete
Button focus target; it accepts only the same bounded canonical PNG contract as
`UI.InlinePngImage` and replaces, rather than combines with, a semantic glyph.
Sliders provide
`.FocusUp/Down(id)`, `.Disabled(...)`, `.Busy(...)`, and `.Activate(...)`;
horizontal focus links are invalid because the control owns Left/Right.
ActionSurfaces provide directional focus helpers, Disabled/Selected/Busy,
shortcuts, and up to eight `.ContextAction(actionId, label, style?, disabled?, busy?)`
items on their one stable root. Controller Menu/Options, Shift+F10, the keyboard
menu key, right-click, and UI Automation open the same host-owned anchored menu.
Use `WidgetContextActionStyle.Danger` for destructive commands; disabled and
busy items remain announced but cannot dispatch. Keep IDs stable and publish
the complete current collection in every snapshot—the host retires an open menu
on any widget, instance, runtime, presentation, sequence, scope, node, binding,
or enabled/busy authority change. Their descendants cannot own input,
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
Invoke and SetValue carry the explicit `AccessibilityAutomation` origin. They
may enter the visible widget's Interactive lifecycle and use its ordinary typed
action path after exact revalidation, but they are not proof of a physical
controller press and never receive the Visible-state dashboard gesture
exception. Declaration, consent, lifecycle, payload, and provider checks still
apply to every capability call. The worker creates no private gesture context
for automation-origin input, and the broker adapter attaches gesture sequences
only after the host accepts the exact capability/operation activation.
Your next immutable render is authoritative; do not depend on synchronous state
mutation during an accessibility request. Keep labels concise, supply a human
readable Slider value, and keep IDs stable for the lifetime of one logical
control. While a controller adjustment is awaiting acknowledgement, the host
uses one bounded optimistic numeric value for both the visible thumb and UIA
RangeValue; a matching newer snapshot or the bounded timeout reconciles both.

The host coalesces renders and announces the newest immutable state through UIA
structure, logical-focus, and closed property events outside paint. Stable IDs
let name, value, enabled/selected, range, and physical-bounds changes remain
property updates; adding/removing controls or changing supported patterns
invalidates structure. Authors do not raise native events themselves.

UIA identity is namespaced by owner: your nodes publish as
`widget:<stable-node-id>`, while host chrome and tray elements use separate
`host:` and `tray:` domains. Do not reserve or avoid source-ID prefixes for the
host. The owner domain participates in runtime identity, lookup, events, and
queued action authority, so even an author ID identical to a shell ID cannot
alias shell behavior. IDs must still be unique within your widget snapshot.

The composite root exposes a scope-correct Back command. At your root scope it
returns to the tray. At a nested scope it exists only when that exact scope root
binds a pressed-B shortcut reachable through the SDK's current-focus resolution,
as `WidgetNavigator`, `UI.Picker`, and `UI.ActionSheet` do. Focused controls may
own B themselves; an unavailable focused B suppresses ancestor fallback exactly
as controller input does. UIA invocation rechecks the current generation,
snapshot, active scope, and focus before sending automation-origin B. Missing or
stale nested Back never falls through to the tray, so custom scopes must author
their own one-level B action.

Open-widget UIA is currently an implemented preview rather than the completed
screen-reader ship gate. One composite root retains the active widget controls,
closed host Back/Close commands, exact visible quiet footer help or polite
transient feedback, and visible tray ListItems while controller focus changes
between widget and tray. The dashboard publishes a level-one title heading,
non-live controller help, and polite transient action feedback. The desktop-
automation/AppContainer smoke evidence, typed choice semantics, legacy MSAA,
and packaged Narrator evidence remain pending. The gesture-origin policy itself
is explicit and defense-in-depth enforced. Semantic snapshot tests therefore
remain required for widget acceptance.

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
the renderer and WRSS. Disabled/Busy must not be used as a way to remove a
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
[SDK Gallery Community addon](../samples/SdkGalleryWidget/README.md). Its five
controller-native pages exercise the shipped modern components, stable state,
responsive Grid/Scroll behavior, deterministic BackgroundSurface artwork,
nested Picker/ActionSheet B scopes, a
non-focus-stealing Toast, package-local WRSS, and the generic Community worker.
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

Before launch, the bridge atomically accounts for application workers without a
framework-selected count limit. A user or administrator may opt into an exact
positive cap with `--max-resident-workers`; only then can count capacity reject a
new launch. Optional memory guidance is reported but neither reserved nor
enforced as a private-size limit. A configured cap never silently evicts a
`keep-alive` worker. When that cap is full, a new launch fails with a remediation
message until a resident worker is disabled, removed, crashes, or reaches its
explicit idle-unload bound. The trusted Settings worker has one separate
control-plane slot so diagnostics remain reachable.

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

private readonly WidgetTimedMutation _filterConfirmationExpiry;

public LibraryWidget()
{
    _filterConfirmationExpiry = CreateTimedMutation(
        WidgetOperationLifetime.Active, TimeProvider.System);
}

var clearConfirmation = _filterConfirmationExpiry.ScheduleLatest(
    TimeSpan.FromSeconds(4),
    () =>
    {
        _filterConfirmation = null;
        Invalidate();
    },
    navigation.RouteCancellationToken);
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
- `WidgetTimedMutation.ScheduleLatest` is a quiet one-shot latest-wins mutation.
  Create one slot for each independently replaceable piece of timed state. Delays
  must be positive and no longer than `WidgetTimedMutation.MaximumDelay` (one
  day). A replacement gets its full delay and immediately makes the older callback stale. The optional
  owner token binds route/session work in addition to the selected widget
  lifetime; cancellation, route exit, lifecycle retirement, and disposal
  prevent the callback from running. Inject a `TimeProvider` in deterministic
  tests and update ordinary widget state plus invalidation inside the callback.
  The slot does not publish operation Busy state or invalidate on scheduling or
  completion, and invokes author callbacks outside SDK locks. If another thread
  can publish a replacement before a due callback updates state, capture a unique
  immutable notice identity and clear only when that exact notice still owns the
  state slot. Use `Cancel` and `WhenIdleAsync` for explicit retirement and tests;
  callback failures are contained in the returned completion and `Failed` event.

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

For the complete owner-selection table, equality and collection rules,
revision/publication semantics, result-bearing update pattern, lifecycle
boundary, and multi-field migration recipe, see
[Immutable widget models](widget-model.md).

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

SDK unit tests that do not need runtime invalidation may construct an isolated
model with `WidgetModel<TState>.CreateForTesting(initial)`. Production widgets
must use protected `CreateModel` so changed state schedules a render.

Now Playing's `MediaSessionsWidget` is the medium production reference: all of
its render-facing state shares one model, and repeated selection of the current
session is equality-suppressed while a real selection change invalidates once.
Its transport path combines that model with an Active-lifetime SingleFlight
operation: initial activation and repeated Retry join one current generation,
deactivation drains the subscription/read, and cancellation-ignoring results
are rejected before publication. Snapshot reads and event subscriptions remain
independent, so a subscription failure can retain a valid current snapshot and
a transient refresh failure retains last-good sessions with a reconnect action.
An identity-less or duplicate session update is an invalid provider generation,
not an authoritative empty library, so it also retains the current selected
session. A real empty snapshot remains empty. Late command success or failure
can update status or rollback only while the exact run and provider snapshot
revision that admitted it are still current.
Domain projection, provider-event merge, error copy, and rollback stay explicit
widget policy.

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
        Execute = (command, context) =>
            Provider.ControlAsync(command, context.CancellationToken),
        Reconcile = (current, command, result) =>
            current.MergeProviderResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = MapSafeCommandError,
        Fail = (current, baseline, command, error) =>
            current.RemoveProjection(baseline, command).WithError(error),
    });
```

`Apply`, `Reconcile`, `Rollback`, and `Fail` are quick, side-effect-free
reducers that run under the model lock. They must not call the model or command
recursively, invoke providers, acquire unrelated locks, or block. `Apply`
returns the optimistic state and provider input derived from the exact same
prior revision. `Execute` runs without the model or command lock, may await,
and receives the actual `WidgetOperationContext`; honor its cancellation token
and inspect `IsCurrent` before committing adjacent provider-side work.
`MapError` also runs without those locks and must remain bounded,
side-effect-free, and non-throwing.
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
Ownership is installed or retired before model invalidation and `Changed`
observers run, so a synchronous observer may start a newer command without an
older publication clearing the new owner. Observers still must not become a
second state owner.

Map exceptions to `WidgetCommandError`, whose code is a stable identifier and
whose safe UI message is limited to 256 visible characters. A mapper failure
uses `WidgetCommandError.Unexpected`. Mutations run once; the coordinator never
automatically retries. Without `Fail`, failure invokes `Rollback`; use `Fail`
when the model also needs the mapped safe error.

Now Playing is the first production consumer. It uses SingleFlight transport
commands, immediately projects Play/Pause, retains sibling control presentation,
and rolls back the affected session only when its Active run, provider snapshot
revision, and exact selected session all still match. Its failure reducer first
returns current state wholesale on any mismatch, so mapped error text cannot
overwrite a replacement generation after the rollback itself correctly declines.

Its Active-lifetime read/subscription owner also keeps failure provenance at the
consumer boundary. Safe status distinguishes `snapshot-read`,
`subscription-open`, and `subscription-read`, followed by a bounded typed code.
Provider text, filesystem paths, media identities, and response bodies are
never projected. Retry starts a fresh generation, while a transient refresh
keeps last-good sessions and their controls available.

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

SDK owners publish independently. A model update followed by
`resource.EnsureLoaded()` can request two adjacent invalidations, which the
runtime coalesces before rendering. That is not a reason to copy the resource
snapshot into the model or add a manual `Invalidate()`. Likewise, the SDK
always republishes after `OnEmbeddedMediaPlaybackEventAsync` so field-backed
widgets remain valid; a model or command updated by that callback still owns
and publishes its own transition.

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
paging across compact and wide Scroll IDs, cached reverse navigation, joined
repeated edge input, retained failure state, and no visible **Load more** row.
The deterministic 29-item contract exercises 12/12/5 forward and reverse
navigation for both playlists and detail tracks with exactly three provider
page calls per collection and responsive mode. Queue remains non-paged state
owned by the widget.
### Continuous cursor collections (protocol v14)

Use `WidgetCursorResource<TItem>` when transport pages must compose into one
continuous controller collection. Cursors and item keys are typed opaque
values; never derive keys from a title or visible ordinal.

The [Full Application reference](../samples/FullApplicationWidget/README.md)
shows the scale boundary end to end: its worker owns 10,000 deterministic
private records, while `WidgetCursorResource<TItem>` exposes 32-record pages and
retains at most 96 render-facing records. It combines that projection with
`WidgetNavigator<T>`, safe retry, and Active-lifetime cancellation without
provider authority or a database dependency. The sample is optional and is not
part of the production tray catalog.

```csharp
_items = CreateCursorResource<Item>("library.items", new()
{
    PageSize = 64,
    MaximumRetainedItems = 192,
    LoadPage = LoadCursorPageAsync,
    MapError = _ => new("library_unavailable", "The library is unavailable."),
    Viewports =
    [
        new("library.list", item => new(item.SavedId),
            item => $"library.item.{item.StableFocusId}", "library.empty"),
    ],
});
```

Render only `Snapshot.Items`, mark every root item with
`resource.PresentItem(item, element)`, wrap the Scroll with
`resource.Present(scroll)`, and use `Snapshot.RequestedFocusId`. Route
`TryHandlePagination` before unrelated actions. `Move(Before/After)` prepends
or appends a whole page; the resource retains complete page segments, evicts
from the opposite edge, and never exceeds 256 items. `Refresh` reloads the
retained segment containing the anchor. Existing keys preserve anchor and
focus; deletion uses a deterministic nearest retained fallback.

The provider has one Latest request lane. Cancellation-ignoring and stale
completions cannot publish. Duplicate keys, a returned cursor loop, null or
oversized pages, and unsafe errors fail without partially changing the window.
Page size is 1–100, retained items are at least two pages and at most 256,
cursor history is capped at 256, pagination threshold is 1–8, and the protocol
accepts at most 256 serialized collection items. The logical provider may have
thousands of items; only this window reaches the host.
One same-direction traversal admits at most 256 distinct cursor identities and
then fails closed; changing direction or refreshing starts a new bounded
traversal, so an evicted page can be fetched again without weakening loop
detection.

### Virtual collection presentation windows (protocol v19)

Large logical collections can opt into host-owned virtual extent without
serializing off-window items. Set `EstimatedItemExtent` on each configured
`WidgetCursorViewport<TItem>`, then return `FirstItemIndex` and
`TotalItemCount` on each `WidgetCursorPage<TItem>` when the logical extent is
known:

```csharp
Viewports =
[
    new WidgetCursorViewport<Item>(
        "library.list",
        item => new WidgetCollectionItemKey(item.SavedId),
        item => $"library.item.{item.StableFocusId}",
        "library.empty")
    {
        EstimatedItemExtent = 56,
    },
],
LoadPage = async (cursor, direction, limit, cancellationToken) =>
{
    var page = await LoadPrivatePageAsync(cursor, direction, limit, cancellationToken);
    return new WidgetCursorPage<Item>(page.Items, page.Before, page.After)
    {
        FirstItemIndex = page.FirstItemIndex,
        TotalItemCount = page.TotalItemCount,
    };
},
```

The widget still owns private item data and materializes only the retained
cursor pages. The SDK publishes one typed request generation, replace/append/
prepend disposition, exact before/after availability, and the logical index
metadata. The host keeps the one existing vertical or horizontal Scroll as the
sole offset, clipping, focus-follow, controller, pointer, UIA, Taffy, and paint
authority. It measures admitted rows normally and uses the estimate only for
the two off-window extents. Off-window items do not become protocol nodes,
layout nodes, paint work, hit targets, focus targets, or UIA providers.

The estimate is 1–512 DIPs, a known total is 1–1,000,000 items, and the
estimated logical extent is capped at 1,000,000 DIPs. Known totals require a
zero-based first index and every retained page must remain contiguous with the
same total. Unknown positions must publish `replace`; indexed `append` and
`prepend` must move contiguously in their declared direction, retain compatible
known-total authority, and preserve identical keys at every overlapping logical
position. Provider insert/remove/move and other arbitrary window changes use
`replace`. The request generation is positive, monotonic for the runtime, and
bounded to JSON's exact integer range. Malformed, stale-generation, failed, or
cancelled windows retain the last valid checkpoint; they never partially replace
the visible collection. Exact retained keys preserve focus and anchor position,
while removal uses the existing deterministic nearest retained fallback.
Omitting `EstimatedItemExtent` keeps the protocol-v14 eager Scroll behavior
unchanged.

`WidgetArtworkHandle` was introduced in protocol v14. The trusted encoded
resource contract is protocol v36. `UI.Artwork(handle, ...)` and
`ButtonElement.LeadingArtwork(handle, ...)` publish a bounded opaque identity.
It is not a URL or path and grants no file, network, decode, or launch
authority. Until a trusted host resolver supplies pixels, authors must retain
accessible text or a semantic fallback. Do not embed artwork bytes in cursor
snapshots.

Override `OnResolveArtworkAsync` and return a value-owned
`WidgetEncodedArtwork` with `Png`, `Jpeg`, or `WebP` for an exact handle declared by the
current snapshot. The runtime preserves accepted bytes without resizing,
recompression, transcoding, or metadata rewriting; the native host decodes and
scales them only for presentation. Honor cancellation and keep lookup bounded.
Never encode a path, URL, credential, provider identity, title, or other private
body data into the handle.

Animated WebP is presented as its first decoded frame only; WidgetRail does not
play artwork animation or rewrite the encoded resource. WebP decode uses the
platform codec when available. A missing WebP codec fails only that artwork item,
so authors must keep the same accessible semantic fallback used while any trusted
artwork is pending or unavailable.

The encoded resource limit is 8 MiB. Native admission permits an axis through
4096 pixels and at most 16,777,216 decoded pixels, with bounded decode,
concurrency, and cache ownership. The declared MIME type must match the PNG,
JPEG, or bounded RIFF/WebP envelope. Malformed, oversized, stale, cancelled, unavailable, or
evicted resources fail independently without invalidating the last good widget
presentation. The legacy inline-PNG contract remains available for small icons
and retains its separate 12 KiB and 64 by 64 pixel limits.

`WidgetPagedResource<TItem>` remains the offset-based replacement-window API;
its compatibility behavior is unchanged. Use `WidgetResource<TValue>` for one
non-paged current value.

## WRSS: safe widget-local styling

Attach semantic classes in C# and ship `styles/default.wrss`:

```csharp
UI.Button("Play", "toggle", "player.play")
    .Classes("transport", "primary")
```

`Classes(...)` replaces only the author-owned portion of the class list.
Required classes emitted by SDK composites are always serialized first and
cannot be removed; `AddClasses(...)` appends author variants with ordinal
deduplication. `Classes(...)`, `AddClasses(...)`, and direct `StyleClasses`
initialization retain each authored token's first occurrence, including valid
`wrail-*` names.

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

WRSS is not browser CSS. It supports one semantic compound selector (role,
stable ID, classes, and `:focused`, `:pressed`, `:selected`, `:disabled`,
`:busy`), variables, safe package-relative `.wrss` imports, and an allowlist of
typed, bounded properties. Unknown properties, scripts, URLs, filesystem
paths, `calc`, expressions, and arbitrary functions are rejected. Snapshot
`:busy` state is resolved into the published maps. `:pressed` is activated only
for the exact physically held controller action and is canceled on focus,
surface, or snapshot changes.

Use percentages, `vw`, `vh`, flex growth/shrink, min/max dimensions, line
limits, and `overflow: clip` for responsive layout. The host applies global
themes and accessibility after widget styles; a widget cannot override forced
contrast, text scale, reduced motion, or reduced transparency policy.

Run `wrail validate <widget-directory>` after every style change. See the
[WRSS reference](wrss.md) for the exact property catalog and safety limits.

## Typed audio and network capabilities

Installed/community widgets run without ambient OS/network capability. Ask for
the smallest closed broker authority in `manifest.json` and call the typed
`HostServices` APIs.

| Manifest ID | Typed service | Allowed state |
| --- | --- | --- |
| `system.audio.sessions.read.v1` | list/watch application sessions | Visible or Interactive |
| `system.audio.sessions.control.v1` | set session volume/mute | Interactive |
| `system.audio.output.read.v1` | get/watch master output | Visible or Interactive |
| `system.audio.output.control.v1` | set master volume/mute | Interactive, or one exact declared dashboard gesture while Visible |
| `system.audio.devices.read.v1` | list/watch sanitized input/output devices and default markers | Visible or Interactive |
| `system.audio.input.read.v1` | get/watch current default microphone volume/mute | Visible or Interactive |
| `system.audio.input.control.v1` | set current default microphone volume/mute | Interactive |
| `system.network.read.v1` | status, saved profiles, status events | Visible or Interactive |
| `system.network.details.read.v1` | bounded current IP/gateway/DNS display values plus revision-only invalidation | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | switch to a saved profile | Interactive |
| `system.network.wifi.read.v1` | cached available-network snapshot/events; one explicit scan | read/events Visible or Interactive; scan Interactive |
| `system.network.wifi.connect.v1` | connect one current saved/open scan result | Interactive |
| `system.network.wifi.radio.read.v1` | get/watch software Wi-Fi radio state | Visible or Interactive |
| `system.network.wifi.radio.control.v1` | request software Wi-Fi radio On/Off | Interactive |
| `system.network.bluetooth.read.v1` | get/watch sanitized Bluetooth radio/discovery/device state | Visible or Interactive |
| `system.network.bluetooth.radio.control.v1` | request Bluetooth software radio On/Off | Interactive |
| `system.network.bluetooth.pair.v1` | pair one current opaque Bluetooth association endpoint and receive a typed outcome | Interactive |
| `system.network.bluetooth.unpair.v1` | remove one explicitly confirmed current paired opaque Bluetooth endpoint and reconcile authoritative disappearance | Interactive |
| `system.network.bluetooth.manage.v1` | open Windows Bluetooth Settings after validating one current opaque device | Interactive |
| `system.activity.recent.read.v1` | list/watch bounded recent running applications | Visible or Interactive |
| `system.apps.library.read.v1` | page installed-app names/kinds, observe bounded sanitized source health, and resolve authority-scoped durable SavedIds to current launch IDs | Visible or Interactive |
| `system.apps.running.read.v1` | on demand, list visible programs that exactly match one installed registration and confirm one short-lived observation | Visible or Interactive |
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

Current connection details require the separate
`system.network.details.read.v1` grant. Subscribe with
`OpenConnectionDetailsSubscriptionAsync`, then call `GetConnectionDetailsAsync`
for current truth after each revision-only invalidation. The closed result is
already display-bounded and never contains adapter IDs, MAC addresses, route
tables, traffic, public-IP lookups, or Wi-Fi identity. Treat `Ambiguous`,
`PrivacyDenied`, `Offline`, and `Unavailable` as complete states; do not guess a
route or add polling.

Bluetooth IDs are equally opaque and current-snapshot-only. Pair through
`PairBluetoothDeviceAsync`; do not claim success or profile connectivity until
the returned outcome and following authoritative snapshot support it. Use
`OpenBluetoothDeviceSettingsAsync` only as a Windows-owned management fallback.
There is no public unpair or generic Connect/Disconnect API.

### Installed app-library example

Use the typed `HostServices.AppLibrary` surface; do not turn a display name,
path, command line, or launcher-specific ID into an action target:

```csharp
var page = await HostServices.AppLibrary.QueryAsync(
    new WidgetAppLibraryQuery(
        InstalledOnly: true,
        Kind: WidgetAppLibraryKind.Game,
        SourceAttribution: "Steam",
        Sort: WidgetAppLibrarySortOrder.SourceThenDisplayName)
    {
        SearchText = "co-op",
        FavoriteSavedIds = favoriteSavedIds.Take(
            WidgetAppLibraryQuery.MaximumFavoriteSavedIds).ToArray(),
    },
    limit: 32,
    refresh: true,
    cancellationToken);

var savedIds = page.Items.Select(item => item.SavedId).ToArray();
// Persist SavedIds—not AppIds—in HostServices.PrivateState.
var restored = await HostServices.AppLibrary.ResolveSavedAsync(
    savedIds,
    cancellationToken);

if (LifecycleState == WidgetLifecycleState.Interactive && restored.Count != 0)
{
    var item = restored[0];
    if (item.Presentation.Availability.IsLaunchable &&
        item.Presentation.Capabilities.Supports(WidgetAppLibraryAction.Launch))
    {
        var observation = await HostServices.AppLibrary.LaunchObservedAsync(
            item.AppId,
            WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
            cancellationToken);
        // Render only the sanitized observation; it is not durable authority.
    }
}
```

`QueryAsync` permits 1–64 items per request and returns opaque Before/After
cursors plus a provider revision. Supply a cursor only with its matching
`WidgetCursorDirection`; do not parse or persist cursors as durable identity.
Queries normalize bounded search text, may filter by conservative kind,
sanitized source attribution, or at most 128 opaque SavedIds, and use only the
closed display-name/source sort values. Every criterion is bound into the
opaque provider cursor; changing one requires an offset-zero request. The
broker resolves favorite SavedIds to current provider identity without exposing
that identity to the widget. `ResolveSavedAsync`
accepts at most 64 unique host-issued SavedIds, preserves request order, and omits apps
that are no longer available. Read is allowed only in Visible or Interactive;
launch is separately declared/consented and Interactive-only. Treat `AppId` as
an opaque provider-lifetime token and never persist it. `SavedId` is the
non-reversible publisher/package-scoped value for private state. The current
`LaunchObservedAsync` result is public to every declared Community consumer and
reports only bounded `RequestAccepted`, `LauncherStarted`, `Running`, or `Ended`
evidence with support flags. It exposes no process, path, launcher, or operating-
system identity and does not replace exact SavedId re-resolution.
The current
Windows provider exposes bounded Start Menu `.lnk` plus current-user
AppsFolder/AUMID registrations and conservatively reports them as Application.
Each item has one versioned `Presentation`: sanitized title and closed kind,
opaque source reference, closed availability and launchability, role-keyed
artwork, optional revisioned/attributed metadata, a closed capability set, and
an optional operation. There are no legacy scalar aliases. Treat missing
metadata and artwork roles as explicit absence; never infer capabilities from
title, source, artwork, or availability text. Unknown versions, enum values,
duplicate roles/actions, inconsistent launchability, and malformed provenance
fail closed as `malformed_response`.

Validation is value-scoped: source, availability, artwork, metadata,
capabilities, and operation records each own their local enum, identifier,
count, status, and timestamp rules. The presentation relationship check only
composes those already-valid values. Launch requires `Installed`, explicit
launchability, and the `Launch` capability together; `Unavailable`,
`StaleSource`, and retained last-good rows never authorize launch.

Current entries may include opaque artwork handles under `Presentation.Artwork`;
select the required role with `Find(WidgetAppLibraryArtworkRole.Tile)` (or
`Cover`, `Hero`, or `Logo`). The native host resolves a handle lazily only
against the exact current trusted registration and keeps decoded pixels in a
bounded memory cache. No image bytes enter snapshots or private state. Paths,
AUMIDs, arguments, launcher identifiers, and activation PIDs remain
provider-private. The provider includes reviewed Steam manifests as games and
uses semantic fallback when a role is absent. Xbox and other store adapters
remain unsupported. See the
[Games & Apps reference](games-and-apps.md).

For an optional **Add running app** route, declare
`system.apps.running.read.v1`, call `ObserveRunningAsync` only when the user
opens or refreshes that route, and retain its revision only in memory.
`ConfirmRunningAsync(savedId, revision)` is a read-only exact-current check; it
does not remember a portable executable. To offer an explicit **Remember**
action, separately declare and obtain consent for
`system.apps.running.register.v1`, require Interactive lifecycle, and call:

```csharp
var observation = await HostServices.AppLibrary.ObserveRunningAsync(
    cancellationToken);
var candidate = observation.Items.FirstOrDefault();
if (candidate is not null)
{
    var registered = await HostServices.AppLibrary.RegisterRunningAsync(
        candidate.SavedId,
        observation.Revision,
        cancellationToken);
    // Persist only registered.Item.SavedId in package-private state.
}

// A user-owned Forget action is safe to repeat.
await HostServices.AppLibrary.ForgetRunningAsync(savedId, cancellationToken);
```

Registration re-observes the exact process instance and either returns the
current installed-catalog item or stores one provider-private portable record
for this publisher/package. New portable records accept only a local ordinary
`.exe`; relative, UNC, device, remote-drive, reparse, and alias paths fail
closed. The widget never receives a process ID, window, path, file identity,
command, or working directory. `ForgetRunningAsync` is
idempotent, cannot remove installed catalog authority, and cannot address
another package's records. Portable records are capped at 64 with explicit
capacity failure and no eviction. After restart, use `ResolveSavedAsync`; it
omits missing, moved, or replaced executables. Launch still requires the
separate launch grant and a fresh resolved AppId. The host then rechecks the
canonical path and Windows file identity under a short-lived retained path/file
lease and starts only that exact executable, with no arguments, Shell execution,
elevation, or provider-specific command behavior. This file-identity contract
does not claim content hashing or signature verification.

Use `UI.TextEntry(value, placeholder, action, id, maximumLength)` when a
controller-first surface needs bounded text. Activating it opens the host-owned
themed keyboard/modal and makes the underlying widget/tray inert; widgets never
receive raw keys, edit-control handles, clipboard contents, or intermediate
values. The `placeholder` is prompt guidance, not an initial value. The authored
`value` initializes the separate live edit buffer, which visibly tracks the
caret and every edit.

D-pad/left stick moves key focus, A inserts the focused character, X
backspaces, B cancels, right trigger performs Enter, and LB/RB moves the caret.
Shift and symbol layers provide uppercase and bounded punctuation. The shortcut
legend is not another focus stop. Physical keyboard, pointer, UI Automation,
and ordinary non-password Unicode paste use the same host-owned modal; protected
input blocks clipboard operations.

Only Enter can deliver the final value, once, in
`WidgetActionEvent.CommittedText`. Cancel or window close preserves the authored
value and dispatches no action. The maximum is 96 UTF-16 code units, control
characters are rejected, and `.Disabled()` keeps the control focusable without
admitting activation. After the modal closes, the host revalidates the exact
widget, runtime/presentation generation, input scope, node, action, authored
value/bound, and enabled/busy state. Harmless newer presentation sequences may
complete when that authority is unchanged; stale or replaced authority fails
closed. This is protocol v15; widgets that do not author `TextEntry` retain
their earlier protocol.

This is an intentional pre-release replacement for
`GetPageAsync(offset, limit)`. There is no compatibility facade: rebuild against
the current SDK, replace stored offset traversal with the returned opaque
cursors, and retain only `SavedId` as durable application identity.

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
  "pinningSupported": true,
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
| `pinningSupported` | Optional boolean, default `false`. When `true`, the host may project the widget's validated declarative snapshot into its own bounded pinned tool window. It grants no HWND, topmost, focus, input, renderer, provider, or compositor authority to the widget. |
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
| `resourceRequest.memoryMb` | Optional positive advisory estimate for diagnostics. It is not reserved capacity or a hard private-memory limit. |
| `resourceRequest.updateHz` | 1–60 metadata request. SDK periodic helpers are independently limited to at most 4 Hz. |
| `architectures` | One or both of `x64`, `arm64`; the current machine architecture must be listed. |

Unknown capability IDs are syntactically valid at manifest parse time but an
installed package declaring unsupported authority is omitted by the bridge.
Use only the published closed IDs above.

For a supporting widget, focus its tray item and press controller Menu/Options
or right-click that exact tray item. The host-owned context menu exposes
**Pin**, or **Adjust pinned widget**, **Opacity — N%**, and **Unpin** for the current pin; UI
Automation invokes those same typed menu actions. The complete vertical menu
always opens above the bottom tray while retaining the same top-to-bottom visual,
controller, pointer, and semantic order. An unavailable Pin item names
loading, unsupported, or one-pin-cap authority without replacing the existing
pin. `P` remains the keyboard interaction/pin fallback while the widget is open,
and `U` remains the unpin fallback. From tray focus, controller View enters the
single pin independent of tray selection; View remains package-owned while
widget content has focus. The surface starts click-through; a second
`P` or a bare right-stick click while the same widget is open explicitly makes
it Interactive. Menu while widget content owns focus opens the focused
ActionSurface's declared context actions when present; it does not reuse or
replace the tray-owned Pin menu. Other package-declared bumper, trigger,
stick-click, and View actions keep their ordinary routing.
Supporting widgets may also set the optional `WidgetView.PinnedLayouts` init
property. Each `PinnedPresentationLayout` has a stable ID, visible name, and
ordinary bounded `WidgetSurfaceHints`; use `WidgetView.PinnedLayout(...)` when
the layout also needs its own declarative root, active input scope, and initial
focus. The host validates every projection and the aggregate catalog, accepts
at most eight, and always prepends its own **Full widget** fallback. Omitting a
root preserves the original size-profile behavior for the full responsive
tree. A projection is content for the same pinned HWND, renderer, focus router,
and action owner; it is not another window or navigation authority. During Pin or
Adjust setup, `LT`/`RT` cycles the visible name/index; outside setup those
triggers remain package actions. The selected layout is persisted independently
of ordinary view refreshes, while a removed ID falls back safely to Full widget.
After explicit selection, `OnPinnedLayoutSelectionChangedAsync` receives the
selected package layout ID; null revokes that demand on removal, replacement,
unpin, or shutdown. The notification grants no provider, capability, focus, or
window authority.

When a layout owns selection-scoped data demand, prefer the optional typed
handle over a package-maintained string or boolean:

```csharp
private readonly PinnedLayoutHandle _details;

public MyWidget()
{
    _details = CreatePinnedLayoutHandle(
        "details", "Details", DetailsSurface,
        initialFocusId: "details.play",
        activeInputScopeId: "details.scope");
}

public override WidgetView Render() => new(UI.Stack("full.root"))
{
    PinnedLayouts =
    [
        _details.Present(
            UI.Stack("details.root", DetailsContent()).InputScope("details.scope")),
    ],
};
```

`IsSelected` is updated before the compatible
`OnPinnedLayoutSelectionChangedAsync` callback. Each effective selection gets a
fresh `SelectionCancellationToken`; deselection, layout removal, runtime
replacement, unpin, or widget teardown cancels it. Repeated notification of the
same selection is idempotent. Use `WidgetTestHost.CreatePinnedLayoutHost(...)`
to select/restore/revoke layouts, replace the current immutable snapshot, and
route actions against the exact selected projection in deterministic tests.
The handle remains optional: callback-only and `WidgetView.PinnedLayout(...)`
authoring continue to use the same protocol-v21 ingress.

For a compiled high-level example, generate the `media` profile. It registers
**Compact media** and **Media and queue** handles once, keeps the ordinary root
as the host-injected Full widget fallback, and binds deterministic queue demand
to the detailed handle's selection token. Its focused generated tests drive the
public pinned-layout test host through loading, ready, empty, error, revocation,
and late-result rejection:

```powershell
& $wrail new widget MediaDeck --template media --output .\scratch\MediaDeck
& $wrail preview .\scratch\MediaDeck --scenario ready --pinned-layout media.compact
& $wrail preview .\scratch\MediaDeck --scenario ready --pinned-layout media.detailed
& $wrail preview .\scratch\MediaDeck --scenario ready --pinned-layout @all
```

The example's media source is a bounded fake seam, not a provider or credential
contract. Replace it inside the package while retaining one widget lifecycle,
one typed selection owner, stable actions/focus/scopes, and the host Full
fallback.

When one layout renders different state roots, use
`Present(root, initialFocusId)` to choose a focusable target that exists in that
exact immutable root. Passing `null` preserves ordinary focus fallback for a
loading or informational root with no focusable child. The handle still supplies
its registered stable ID, visible name, surface, and active input scope;
`Present(root)` continues to use its registered default initial focus.

Overlay close preserves the surface but restores click-through. Package
removal/replacement, worker restart/loss, pinned close, and host exit tear it
down. Authors publish ordinary immutable snapshots and lifecycle behavior only;
they do not receive window or z-order callbacks. See
[Host-owned pinned surfaces](pinned-surfaces.md).

## Validate, list scenarios, render, replay, and test

Use the same compiler/validators as production:

```powershell
& $wrail validate .\scratch\Clock

& $wrail render .\scratch\Clock\snapshot.json

& $wrail replay `
  .\scratch\Clock\snapshot.json `
  .\scratch\Clock\replays\smoke.json
```

`wrail render` accepts only a bounded existing snapshot, validates it, and prints
the semantic tree. DLL input fails closed without resolving a type or touching
an output path. Persist snapshot fixtures from author-controlled typed-fake
tests with `SnapshotJson.Serialize`; use `wrail dev` when widget code must execute
through the production AppContainer/worker boundary. Running a downloaded
repository's test code is not a sandbox.

Named scenario manifests are a bounded discovery and isolated-execution
contract. Add `widgetrail.scenarios.json` at the widget root:

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
& $wrail preview .\scratch\Clock
```

Listing validates the version, bounded relative assembly path, provider/factory
names, unique scenario names, descriptions, and manifest limits. It deliberately
does not load the assembly, resolve the provider type, invoke a factory, produce
a snapshot, activate a `Widget`, inject services, drive actions, select a
viewport, or render native pixels. The assembly, provider, and factory fields
reserve the version-1 declaration shape; listing alone does not prove that the
referenced code exists or implements a valid factory.

Declare a public static, parameterless factory returning the versioned SDK
contract and select it by its manifest name:

```csharp
public static WidgetScenarioDefinition Running()
{
    var services = new WidgetTestHostServicesBuilder()
        .WithResponse(
            WidgetAudioCapabilities.GetOutput,
            new WidgetAudioOutput(0.50, IsMuted: false))
        .Build();
    return new WidgetScenarioDefinition(new ClockWidget(), services);
}
```

```powershell
& $wrail preview .\scratch\Clock --scenario running `
  --output .\scratch\Clock\fixtures\running.scenario.json
```

The CLI process never loads the assembly. A dedicated capability-free
AppContainer/Job worker pins the exact build-output tree, invokes only the
declared factory, drives Created -> Visible -> Interactive -> Background ->
Destroyed, validates repeated deterministic snapshots, and emits a bounded v1
`WidgetScenarioResult` with sanitized diagnostics. A crash, hang, cancellation,
bad factory, stale result, or malformed snapshot terminates that process tree.
No broker companion, credentials, native pixels, or neighboring filesystem
authority are supplied. This remains author-controlled executable code, not a
general sandbox for downloaded repositories.

When that validated scenario declares package-authored pinned layouts, inspect
one exact projection or all projections without opening the overlay:

```powershell
& $wrail preview .\scratch\Clock --scenario running --pinned-layout compact
& $wrail preview .\scratch\Clock --scenario running --pinned-layout @all
```

`@all` cannot collide with a valid authored stable ID and preserves declaration
order. The report identifies each layout and visible name, its typed width and
height policy, preferred/minimum DIP extents, declarative root, active input
scope, initial focus, actions, shortcuts, focus edges, and accessibility
metadata. It is produced from the production-validated snapshot through the
same semantic tree formatter as `wrail render`; there is no second renderer or
tree model. An unknown exact ID or invalid snapshot fails with a bounded
diagnostic. Semantically identical authored roots produce a deterministic
warning because they are often an accidental copy, but remain valid. The
ordinary command without `--pinned-layout` continues to emit its versioned
scenario result, and `--output` continues to write that result even when pinned
diagnostics are requested.

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

When a case needs several lifecycle and semantic steps, use the optional
high-level runner rather than rebuilding orchestration:

```csharp
var result = await new WidgetScenario(
        "volume-action",
        new WidgetScenarioDefinition(widget, services))
    .Activate()
    .ExpectFocus("initial-focus", "raise")
    .ExpectText("initial-value", "value", "Volume 50%")
    .Action("raise", new WidgetActionEvent("raise", "raise"))
    .ExpectText("raised-value", "value", "Volume 60%")
    .Deactivate()
    .RunAsync();

Assert.IsTrue(result.Passed);
```

For delayed typed services, register
`WidgetScenarioOperationBarrier<TRequest,TResponse>.InvokeAsync` with
`WidgetTestHostServicesBuilder.WithHandler`, await the exact invocation, then
complete or fail it. `CancellationObserved` and `HandlerCompleted` are explicit
barriers for replacement, revoked/denied, stale-result, and lifecycle-drain
cases. `ExpectBusy` and `ExpectIdle` assert semantic and no-work state without
sleeps, hidden polling, virtual time, or provider-specific helpers. A failure
returns the exact `StepId`, safe code, and bounded semantic mismatch.

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

For the normal scaffold, give `wrail pack` the source directory or `.csproj`.
Source mode validates manifest and WRSS, runs a bounded isolated Release build,
omits compiler symbols and machine-specific debug paths, stages the manifest,
declared entrypoint, dependencies, and styles, then applies deterministic
package validation:

```powershell
& $wrail validate .\scratch\Clock
& $wrail pack .\scratch\Clock `
  --configuration Release `
  --output .\artifacts\dev.example.clock-1.0.0.wrwidget
& $wrail install .\artifacts\dev.example.clock-1.0.0.wrwidget
& $wrail list
```

An already-staged package directory remains the low-level escape hatch. In
that mode `pack` includes every bounded file under its input directory, so use
only an exact release root containing root `manifest.json`, the declared
entrypoint, and intentional styles/assets—never source, `obj`, secrets, or
unrelated files. If a source build does not produce the declared entrypoint,
the command identifies it and directs the author to align `AssemblyName` or
`entrypoint.assembly`; no partial archive is published.

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
`%LOCALAPPDATA%\WidgetRail\widgets`. The bridge watches accepted
changes and refreshes the dashboard without eagerly launching the worker. A
custom CLI `--catalog` root is isolated test state and does not appear in the
packaged overlay.

Installed versions coexist immutably. Updates and version selection are
disabled-only review operations:

```powershell
& $wrail disable dev.example.clock
& $wrail install .\artifacts\dev.example.clock-1.1.0.wrwidget
& $wrail version list dev.example.clock
& $wrail version select dev.example.clock 1.1.0
# Review in Settings, then:
& $wrail enable dev.example.clock
```

`version rollback` selects an installed older version and also leaves the ID
disabled. Never edit catalog JSON or installed directories by hand.

If an older installation already exceeds a newer catalog quota, normal listing
and launch validation fail closed. Recover without manual filesystem edits:

```powershell
& $wrail repair list
& $wrail repair remove dev.example.clock 0.8.0
& $wrail list
```

The repair projection uses only canonical package/version directory names and
validated catalog state; it does not parse candidate manifests or load widget
code. It protects the selected generation while allowing inactive history to
be retired even when that selected version is enabled. Removal uses an exact
atomic retirement under the catalog operation lock. Settings exposes
the same candidates behind a separate confirmation page. There is deliberately
no broad `--force` deletion option.

For a real local-companion addon using this same staging/catalog workflow, run
the [YT Music Community package helper](../samples/YtMusicWidget/README.md#build-and-tests).
It produces only the manifest, entry DLL, and WRSS needed by `wrail pack`, then
optionally performs install and enable. Capability consent remains a separate
Settings step.

## Share from a GitHub repository

The implemented sharing unit is a deterministic `.wrwidget` attached to an
exact GitHub Release tag. Publish its SHA-256 through an independently trusted
channel. A recipient installs the exact asset with:

```powershell
& $wrail install `
  github:example/widgets@v1.0.0/dev.example.clock-1.0.0.wrwidget `
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

Sandboxed installed/community workers are mandatory package-specific AppContainers at
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
implemented. Sandboxed widgets must not depend on ambient user files,
environment secrets,
Credential Manager, direct sockets, child processes, registry access, or
desktop UI. Keep secrets out of manifests, WRSS, logs, issue reports, and
command lines.

Full-trust Community applications are outside that containment boundary and
must be reviewed as ordinary desktop applications. Their explicit trust prompt
does not prove publisher identity, review their code, or make their ambient
authority safe.

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
coordinates, one taskbar position, or one monitor. Use responsive WRSS,
semantic Scroll, stable focus neighbors, line limits, and ellipsis. Surface
hints do not remove this requirement.

The geometry contract has deterministic coverage from tiny and handheld-sized
viewports through portrait, ultrawide, 4K, 5K, and 8K inputs across multiple
DPI/interface scales. Physical mixed-DPI migration, hot-plug, HDR/hybrid-GPU,
long localization, combined accessibility, and supported-game presentation
matrices remain release evidence—not completed universal compatibility.

## Diagnostics and recovery

## Image-backed sections

Use `UI.BackgroundSurface` only when one bounded page or section needs an image
behind its existing content. The foreground subtree remains independently
sized and is the only focus, input, and accessibility owner. Supply artwork
through `BackgroundSurfaceArtwork.FromHttps`, `FromInlinePng`, or `FromHandle`;
never publish local paths, arbitrary CSS URLs, or image-sized layout values.
Style crop position, tint, scrim, and radius with WRSS. Always author a safe
surface color and readable foreground treatment so missing, late, or rejected
artwork preserves the same usable geometry and semantics.

For a controller rail whose background follows focus, attach an opaque trusted
artwork handle to each eligible control with `.FocusBackground(handle)` and opt
the nearest surface in with `.UseFocusedDescendantArtwork()` (protocol v39).
This is a paint/resource declaration only: it does not notify the worker or add
focus, input, layout, or accessibility authority. A nested BackgroundSurface
owns its own descendants and prevents an outer surface from consuming their
focus artwork, including when the nested surface does not opt into focused
artwork itself. Within the same opted-in surface, a focused node without a
declaration, a pending or failed replacement, or temporary host/tray focus
retains the last admitted image without retaining a focus ID. Selecting a
different nested surface retires the prior surface override; stale completions
cannot overwrite the current selection.
Resolve every declared handle through the same bounded
`OnResolveArtworkAsync` path as ordinary trusted artwork.
The capability-free [SDK Gallery Backgrounds page](../samples/SdkGalleryWidget/README.md)
shows the complete contract with PNGs embedded in the widget assembly under
stable manifest-resource names: default Cover artwork,
focus-driven replacement and retention, nested Contain ownership, Fill, and an
unresolved-handle fallback. It depends on neither a provider nor a prior cache
and is suitable for first-run and restart verification.

For a controller rail whose details panel follows focus, wrap the ordinary
content in `UI.FocusPresentationSurface(content, defaultPresentation, id)` and
attach a bounded fragment to each eligible actionable descendant with
`.PresentOnFocus(fragment)` (protocol v40). The native host selects the exact
focused descendant's already-admitted fragment immediately; no render callback,
worker trip, action, script, or alternate input owner is involved. Fragments
may contain only presentational layout, text, progress, image, icon, loading,
and spacer nodes, and remain subject to global ID, resource, node, depth, and
accessibility bounds. A missing declaration selects the required default.
Nested consumers are hard boundaries: the nearest consumer owns resolution,
and an outer consumer does not inspect focus through it.

During local development:

- `wrail validate` reports manifest/WRSS JSON paths and source-located styling
  diagnostics.
- `wrail preview` lists declarations without loading their assembly and executes
  one selected factory in the bounded AppContainer/Job semantic worker.
- `wrail render` catches snapshot validation and displays the semantic tree.
- `wrail replay` isolates focus/action contract failures without the overlay.
- Overlay Settings → **Diagnostics** reports bounded bridge, catalog,
  appearance, consent, and worker status for local troubleshooting.
- `%LOCALAPPDATA%\WidgetRail\overlay.log` is the host diagnostic log;
  `%LOCALAPPDATA%\WidgetRail\startup-error.log` records the latest host
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
- [WRSS](wrss.md)
- [Capabilities](capabilities.md)
- [Display and resolution](display-and-resolution.md)
- [Packaging contract](widget-packaging.md)
- [Publishing and installation](publishing-and-installation.md)
- [Security and trust](security-and-trust.md)
- [Performance](performance.md)
- [Diagnostics and recovery](diagnostics-and-recovery.md)
- [Troubleshooting](troubleshooting.md)
- [Known issues](known-issues.md)
