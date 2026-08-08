# Plugin platform plan

Status: public-contract direction with a working Phase 0 subset, 2026-08-07

This document includes future target states. For implemented API details and
limitations, use [declarative UI](declarative-ui.md),
[controller input](controller-input.md), and
[implementation status](implementation-status.md).

## Architectural bet

The SDK extends behavior and declares UI. The shell retains rendering, controller focus, themes, permissions, and lifecycle.

This is less visually unconstrained than giving every widget a browser or native child window. In return it provides consistent controller behavior, global customization, crash isolation, accessibility, predictable performance, and an API that can survive renderer changes.

## Trust tiers

### Built-in modules

Shipped with the host, but not all receive a trusted process exception. The
host catalog's ordered `bundledWidgets` entries point at ordinary manifest-
backed packages and supply only shell identity, package location, icon, and
optional presentation metadata. Audio Mixer, Network Controls, Games & Apps,
and Now Playing derive code identity, publisher, permissions, resource request,
residency, and styles from those manifests and use the same generic worker,
package-specific AppContainer, broker, lifecycle, and renderer as community
packages. A Windows conformance suite exercises that complete path for all four.

Settings and YT Music are the only temporary trusted Job-only entries because
their current desktop-user dependencies are not yet brokered. A package cannot
request that exception. Privileged OS and service integrations remain behind
authenticated, task-shaped providers; being bundled does not bypass manifest
declaration or user consent.

YT Music is intentionally transitional, not a permanent Built-in tier. It is
the planned first Community-addon conformance and migration: the same widget
must eventually package/install through the public workflow and run in the
generic package AppContainer after reusable exact-port loopback HTTP and private
per-widget secret services replace its direct desktop-user dependencies. That
broker work and migration are not implemented today, so the current trusted
worker is UI/behavior evidence rather than Community isolation evidence.

### Community executable widgets

Run lazily in a dedicated generic worker process per package. Every installed/
community worker requires a stable host-derived package AppContainer at Low
integrity with zero capability SIDs/network authority, explicit read/execute
runtime/package grants, a stripped environment, and no trusted-token fallback.
A Job Object limits memory and active process count, applies UI
restrictions, and kills the worker on close. Direct OS capability work remains
in the authenticated trusted broker.

Win32k system-call disable is an explicit residual rather than an implemented
control: the tested mitigation caused CoreCLR DLL initialization failure
(`0xC0000142`).

### Declarative-only packages

Themes, layouts, and static widgets contain no executable code. They are validated, hash-checked, and can receive the strongest trust treatment.

### Future optional tiers

- WebView2 for rare rich interfaces, created only while visible and clearly labeled for resource use
- WebAssembly/WASI for portable sandboxed logic after its component tooling and developer workflow are proven
- Native code only behind an out-of-process adapter; never a DLL loaded into the shell

## Package shape

The current local tooling uses a `.gbarwidget` ZIP-compatible bundle. Signing is
a production requirement, not current enforcement:

```text
manifest.json
payload/
assets/
styles/
signature.json          # reserved for the planned signing phase
```

Example manifest:

```json
{
  "manifestVersion": 1,
  "id": "dev.example.clock",
  "publisher": "dev.example",
  "name": "Clock",
  "version": "0.1.0",
  "hostApi": { "minimum": "1.0", "maximumMajor": 1 },
  "entrypoint": {
    "runtime": "dotnet-worker",
    "assembly": "payload/Example.Clock.dll",
    "type": "Example.Clock.ClockWidget"
  },
  "permissions": [],
  "optionalPermissions": [],
  "residencyPolicy": {
    "schemaVersion": 1,
    "mode": "keep-alive"
  },
  "resourceRequest": { "memoryMb": 48, "updateHz": 1 },
  "architectures": ["x64", "arm64"]
}
```

The current installer validates archive containment/bounds, manifest schema and
identity, host-API declaration shape, entrypoint/style paths, and resource
requests. Remote acquisition additionally requires a caller-supplied SHA-256
pin before installation; local package installation does not require a digest.
Bridge and Settings apply the shared host-API/architecture compatibility check,
and Bridge accepts only its closed capability vocabulary before execution.
Publisher signature/certificate/revocation validation and a signed content
manifest are planned, not current enforcement. Requested budgets are never
permission to exceed host maximums.

The host catalog deliberately separates `widgets` from `bundledWidgets`.
`widgets` is the small host-owned trusted-exception list. `bundledWidgets`
resolves a pinned package root and manifest through `WidgetWorkerHost` using the
same isolation policy as an installed package. This keeps a first-party
reference implementation honest: it cannot rely on an SDK or broker shortcut
that an independent author lacks.

## Worker lifecycle

```text
Runtime: Discovered -> Starting -> Created -> Background -> ... -> Destroying
Host stable states: Background <-> Visible, Background <-> Interactive,
                    Visible <-> Interactive
Any running state -> Crashed -> restart offer -> crash-loop disabled
```

The host may move directly between any stable states; the diagram does not
require an intermediate Visible transition before Interactive.

- Do not start a worker merely to display its cached dashboard metadata.
- Prewarm only the last-used widget if measurements show a clear latency benefit.
- Keep a launched Background worker resident by default.
- Give authors separate widget/process, current-state, and combined
  Visible/Interactive lifetime tokens.
- Cancel visible/state work on the appropriate host transition while allowing
  explicitly permitted widget-lifetime background work to continue.
- Make suspension or unload an explicit manifest/user choice, never a heuristic
  applied because an idle timer elapsed.
- Preserve the last valid UI snapshot and widget state across an explicit
  suspend/unload.
- Disable automatic restart after a small crash-loop threshold.
- Provide Restart, Disable, Permissions, and Resource Use surfaces through the controller.

The runtime uses host-authoritative `Created`, `Background`,
`Visible`, `Interactive`, and `Destroying` states independent of process
residency. The runtime owns Created/Destroying; the native host publishes
Visible for a selected bridge card, Interactive for the open widget, and
Background on selection change or overlay hide. SDK lifecycle tokens and
bounded tickers stop presentation work. Process residency is a separate,
manifest-declared host policy.

`residencyPolicy` schema 1 supports `keep-alive` (default),
`suspend-when-hidden`, and `unload-after-idle`. Idle unload requires an explicit
bounded `idleSeconds`; the supervisor never infers it from CPU, memory, or an
inactive-widget heuristic. Prototype manifest-v1 `backgroundPolicy` remains a
strict migration alias: `none` resolves to `keep-alive`, and `suspend` resolves
to `suspend-when-hidden`. Declaring both old and new fields is rejected.

Suspension is logical lifecycle suspension, not OS thread suspension. Hidden
suspended widgets receive `Background`, lose broker authority and presentation
delivery, and remain responsible for canceling author work through the SDK
lifetime tokens. Idle unload additionally sends `Destroying`, retains the last
validated snapshot in the bridge, and recreates the worker lazily on visibility.

## IPC

The implemented version-1 runtime uses bounded length-prefixed UTF-8 JSON over
a local named pipe, not Protocol Buffers, gRPC, Kestrel, or HTTP/2. Every frame
has a four-byte little-endian length and a strict versioned envelope. Runtime,
bridge, and capability-broker transports use separate closed contracts; this is
the v1 decision that tooling and documentation must target.

Pipe requirements:

- Random per-launch global name and single-client endpoint
- Explicit ACL naming only the desktop host and exact AppContainer SID, plus a
  Low mandatory label and remote-client rejection
- Exact started-PID verification before protocol processing
- Runtime hello validation and broker nonce/package/publisher/instance
  authentication after the OS identity checks
- Maximum frame size, string length, collection size, image size, and message rate
- Deadlines and cancellation for every request
- Strict JSON member/casing validation; unknown or duplicate members fail the
  current protocol version rather than being silently ignored

Core messages:

- `Hello` / `Welcome`: identity, API negotiation, granted permissions, limits
- `Mount` / `Unmount`: widget instance and restored state
- `ViewSnapshot` / `ViewPatch`: semantic tree and bounded updates
- `InputEvent`: normalized controller action or analog sample
- `UiEvent`: focus, activation, value change, visibility
- `CapabilityRequest` / `CapabilityResult`
- `SaveState` / `RestoreState`
- `Suspend` / `Resume` / `Shutdown`
- `Health`: responsiveness and optional diagnostics

## Host-rendered UI

Initial primitives:

- Layout: `Stack`, `Grid`, `Scroll`, `Spacer`, `Separator`
- Content: `Text`, `Icon`, `Image`, `Badge`
- Input: `Button`, `Toggle`, `Slider`, `List`, `Tabs`
- Status: `Progress`, bounded `Chart`, connection and speaking indicators
- Composition: reusable `Card`, `Toolbar`, `Dialog`, and `EmptyState` roles

Every element has a stable semantic ID, role, label, state, style classes, and optional explicit directional neighbors. The host rejects duplicate IDs, impossible focus graphs, excessive depth, or oversized trees.

The implemented Phase 0 protocol currently renders Stack, Row, Scroll, Text,
Button, Progress, Slider, Spacer, Image, and Icon. The public SDK composes those
nodes into controller-safe ToggleButton, Stepper, IconButton, Card,
SectionHeader, StatusBadge, Divider, Alert, EmptyState, SegmentedTabs, Switch,
and ScopedDialog helpers. These helpers publish stable focus behavior,
accessibility labels, selected/disabled semantics, nested B handling, and
documented `gbar-*` theme hooks; they are not privileged renderer nodes. Grid,
Separator, List, Chart, Toolbar, and richer primitives in the target list above
remain future contract work.

The SDK should offer idiomatic builders so developers do not manually serialize protocol messages:

```csharp
public sealed class ClockWidget : Widget
{
    public override WidgetView Render() =>
        View.Stack(
            View.Text(DateTimeOffset.Now.ToString("t"), id: "time"),
            View.Button("Refresh", action: "refresh", id: "refresh"));

    public override ValueTask OnAction(ActionEvent action)
    {
        Invalidate();
        return ValueTask.CompletedTask;
    }
}
```

## Controller and focus contract

- Guide is handled only by the shell and is never sent to workers.
- On the dashboard, the shell owns A activation, B close, Y reorder,
  D-pad/analog navigation, and Guide. A widget card may expose at most three
  quick actions on X, bumpers, triggers, stick clicks, Menu, or View.
- In an open widget, the shell owns D-pad/analog focus navigation and A focused
  activation. B, X, Y, bumpers, triggers, stick clicks, Menu, and View are
  available to the active widget input scope.
- The active widget scope receives `B` first; root fallback returns to the
  dashboard and dashboard B closes. `LB` and `RB` remain ordinary widget inputs.
- MVP button events carry button, Pressed phase, input sequence, monotonic
  timestamp, optional focus ID, active scope ID, and rendered snapshot
  sequence. Released/Repeated shortcuts and normalized analog events are
  future protocol work.
- Host-managed controls can use standard spatial focus; widgets can declare explicit focus neighbors.
- The activation press is quarantined until release so an `A` press does not both open and activate.
- Focusable elements require stable IDs. The host owns and restores live focus
  per widget and input scope; widgets provide `InitialFocusId` only as a
  fallback.
- Shortcut ownership is per active input scope, not widget-global. The root is
  the default scope. Every snapshot explicitly publishes
  `ActiveInputScopeId`; the host echoes it with `SnapshotSequence`, and stale or
  mismatched events are rejected. Nested scopes may reuse bindings and never
  bubble into a parent or sibling.
- Stack/Row containers can bind a scope-level shortcut, so a focusless modal can
  own B without a fake focus target. Unhandled nested B does not dismiss the
  widget or bubble to the root.
- Dashboard quick actions remain a distinct bounded host surface and do not
  participate in open-widget scope lookup.
- A quick action may optionally name one typed control capability and operation.
  While that card remains Visible, the bridge may record a dormant host-owned
  reservation for at most 10 seconds, bound to the exact widget/session, cached
  snapshot, button, positive input sequence, capability, and operation. This is
  not broker authority. Only when the exact typed operation is invoked does the
  runtime atomically activate a one-use identity/PID-bound broker lease for at
  most two seconds. This never promotes the widget to Interactive, authorizes a
  subscription/background task, or bypasses manifest declaration, user consent,
  payload validation, or provider policy; widget code cannot mint either stage.
- A widget cannot open the controller device itself or register global input.
- If a widget becomes unresponsive, the host retakes shell focus and offers recovery.

## Capability model

Default deny. Prefer task-shaped broker operations over broad access.

The implemented closed set currently covers typed audio, Wi-Fi/network,
Bluetooth, recent-activity, and system-media-session reads/controls; see
[widget capabilities](capabilities.md) for the authoritative IDs and lifecycle
rules. Candidate capability domains are:

- `storage.own`: private bounded widget storage
- `storage.secret.own`: host-protected per-widget secrets whose values are
  returned only to the same authenticated package instance under explicit
  policy; no cross-widget enumeration
- `network.loopback.client`: brokered access to a declared local companion. The
  manifest supplies bounded endpoint constraints such as protocol and one port
  or port range; those are enforcement parameters, not separate user-facing
  permission IDs. The initial design allows no subnet/LAN authority, unsafe
  redirects, proxy inheritance, or ambient sockets.
- `network.internet.client`: brokered HTTPS whose manifest declaration carries
  one or more allowlisted domains as constraints rather than minting a separate
  permission ID for each host
- `network.local-network.client`: separately reviewed access to declared LAN
  services; never inherited from loopback or internet authority
- `system.audio.sessions.read` / `system.audio.sessions.control`
- `system.performance.read`
- `system.processes.read-summary`
- `system.notifications.post`
- `system.capture.request`
- `system.launch:<declared-target>`
- `microphone.use`
- `clipboard.read` / `clipboard.write`

The broker owns OAuth tokens and other secrets. Widgets receive scoped results or opaque handles, never raw host credentials. Permission prompts explain the concrete action and support Allow Once, Always Allow, and Deny where meaningful.

The first community SDK should omit capabilities that cannot yet be sandboxed and audited safely.

## GBSS

GBSS borrows familiar CSS syntax without becoming a browser engine:

```css
:root {
  --accent: #8b5cf6;
  --surface: rgba(18, 18, 24, 0.86);
  --radius-lg: 18px;
  --motion-fast: 140ms;
}

widget-card {
  background: var(--surface);
  corner-radius: var(--radius-lg);
}

widget-card:focused {
  outline-color: var(--accent);
  scale: 1.04;
  transition-duration: var(--motion-fast);
}
```

Rules:

- Support variables, declarations, a small cascade, imports within the package, and predictable error recovery.
- Expose documented semantic roles and pseudo-states, never private renderer class names.
- Use a typed property allowlist and clamp size, scale, opacity, blur, and animation duration.
- Resolve all assets relative to the package; verify hashes and bound dimensions/decoded size.
- Disallow scripts, network URLs, arbitrary file URLs, shader code, and native extensions.
- Parse and validate away from the render thread, then atomically swap.
- Keep the last valid theme and return controller-readable file/line errors.
- Accessibility overrides always win.
- Version semantic tokens and provide deprecation periods.

## Developer experience

The SDK succeeds only if the safe path is also the easiest path.

Available now:

- `gbar new widget` scaffolds a strict manifest, typed C# widget, test/replay,
  and safe starter GBSS;
- `gbar validate`, `render`, and `replay` exercise manifests, styles,
  snapshots, focus, and controller actions;
- `gbar pack` creates a deterministic `.gbarwidget` bundle;
- local/HTTPS/GitHub Release install, catalog list/enable/disable, and immutable
  `gbar version list|select|rollback` commands;
- `gbar theme new|validate|preview|pack|inspect|install|list` for deterministic,
  bounded, data-only global themes;
- `gbar dev` for an unsigned, session-only source/project/package edit loop.
  It watches the complete bounded pack input, builds with a deadline, and uses
  the normal generic worker/AppContainer/broker/lifecycle/renderer path. A
  controller/hotkey-free candidate must authenticate its exact catalog, widget,
  instance, and nonce and render a protocol-valid snapshot before the last-good
  interactive generation is replaced. Failed generations retain or restart
  last good, and shutdown reports unreclaimed process/directory state;
- `WidgetWorkerBootstrap`, `WidgetTestHost`, and typed fake host services; and
- first-party Audio Mixer, Network Controls, Games & Apps, and Now Playing
  projects as generic-AppContainer, public-SDK, brokered-capability reference
  implementations, plus a packaged 4/4 conformance harness.

Remaining tooling:

- desktop/controller simulation with visual focus-graph inspection;
- manifest/theme schemas for editors and CI;
- resource-budget, responsiveness, accessibility, and controller-only
  conformance harnesses;
- a published supported SDK/NuGet/template workflow outside this repository;
- native graphical theme preview plus package remove/update discovery; and
- an SDK Gallery widget covering the complete public primitive/state matrix.

`gbar dev` is local development tooling, not an install, signature, or publisher
trust decision. It does not mutate the user's installed catalog, grant consent,
or weaken AppContainer policy. A persistent shell warning and richer controller
diagnostics remain UX work.

## Versioning and distribution

- Semantic host API versioning with one supported major and additive minor evolution
- Manifest declares minimum API and maximum understood major
- Protocol negotiation occurs before a widget is mounted
- Install to immutable versioned directories
- Stage and validate without replacing installed bytes; select versions only
  while disabled, then review and enable separately
- Persist an exact active-version pin and retain installed older versions for
  CLI rollback; a missing pin fails closed
- Production executable packages require publisher identity and tamper-evident signing
- Start with local sideloading; add a curated catalog only after publisher
  trust/revocation, resource quotas, auditability, and rollback are proven
- A future catalog can link to Microsoft Store or WinGet packages rather than becoming an immediate hosting/payment platform

Signing proves publisher identity and integrity, not safety. Mandatory
AppContainer isolation and broker permissions remain independent requirements.

## Explicitly rejected initial designs

- In-process community `.dll` loading with `AssemblyLoadContext`: useful dependency organization, not a security or crash boundary
- Electron or one WebView2 instance per widget
- A native plugin ABI inside the host
- Direct controller access from plugins
- Arbitrary XAML/HTML injection into the shell
- A permission named `full-system-access`
- Undeclared or unbounded background work

## Open questions for prototypes

1. What worker cold-start latency does profile creation/lookup and AppContainer
   launch add across the supported Windows matrix?
2. Is a cached widget tree sufficient to make on-demand workers feel instantaneous?
3. Which UI primitives cover the first-party widgets without encouraging bespoke escape hatches?
4. Which remaining Settings desktop-user dependencies should become brokered so
   it can move from Job-only to AppContainer policy after the planned YT Music
   Community-addon migration?
5. Which additional permissions can be safely brokered without elevating the host?
6. What disk/profile quotas, stale-profile cleanup, and user controls are
   required before public distribution?
