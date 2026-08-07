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

Trusted, signed, and shipped with the host. They may run in-process for efficiency but implement the public widget interfaces. Privileged OS and service integrations remain behind internal providers.

### Community executable widgets

Run lazily in a dedicated `WidgetRunner` process per package. The process boundary provides crash isolation; AppContainer/Win32 app isolation and capability brokering provide the security boundary. A Job Object accounts for and limits the process tree.

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
  "backgroundPolicy": "none",
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

The implemented subset uses host-authoritative `Created`, `Background`,
`Visible`, `Interactive`, and `Destroying` states independent of process
residency. The runtime owns Created/Destroying; the native host publishes
Visible for a selected bridge card, Interactive for the open widget, and
Background on selection change or overlay hide. SDK lifecycle tokens and
bounded tickers stop presentation work, but the supervisor keeps Background
processes resident by default.

The final policy vocabulary is `keep-alive` (default),
`suspend-when-hidden`, and `unload-after-idle` (both opt-in). Current manifest
`backgroundPolicy` validation accepts prototype `none`/`suspend` metadata, but
does not enforce these lifecycle choices or background permissions yet. A
schema migration is required before the final policy names become public API.

## IPC

Use a length-prefixed Protocol Buffers protocol over a local named pipe rather than full gRPC/Kestrel in v1. This retains generated, language-neutral contracts without adding an HTTP/2 server to each worker.

Pipe requirements:

- Random per-launch name and nonce handshake
- Explicit ACL restricted to the current logon/session and expected AppContainer identity
- Local-only namespace; deny network access
- Mutual protocol and package identity validation
- Maximum frame size, string length, collection size, image size, and message rate
- Deadlines and cancellation for every request
- Unknown-field compatibility and field numbers that are never reused

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
- On the dashboard, the shell owns A activation, Y reorder, D-pad/analog
  navigation, and Guide. A widget card may expose at most three quick actions
  on B, X, bumpers, triggers, stick clicks, Menu, or View.
- In an open widget, the shell owns D-pad/analog focus navigation and A focused
  activation. B, X, Y, bumpers, triggers, stick clicks, Menu, and View are
  available to the active widget input scope.
- `B`, `LB`, and `RB` are ordinary widget inputs; the shell does not close or switch widgets with them.
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
- A widget cannot open the controller device itself or register global input.
- If a widget becomes unresponsive, the host retakes shell focus and offers recovery.

## Capability model

Default deny. Prefer task-shaped broker operations over broad access.

Candidate capabilities:

- `storage.own`: private bounded widget storage
- `network.client:<declared-domain>`: brokered HTTPS to an allowlisted domain
- `system.media.read` / `system.media.control`
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
- local/HTTPS/GitHub Release install plus catalog list/enable/disable commands;
- `gbar theme new|validate|preview|pack|inspect|install|list` for deterministic,
  bounded, data-only global themes;
- `WidgetWorkerBootstrap`, `WidgetTestHost`, and typed fake host services; and
- first-party Audio Mixer and Network Controls projects as public-SDK,
  brokered-capability reference implementations.

Remaining tooling:

- `gbar dev` for an unsigned development bundle, file watching, and restart of
  only that worker;
- desktop/controller simulation with visual focus-graph inspection;
- manifest/theme schemas for editors and CI;
- resource-budget, responsiveness, accessibility, and controller-only
  conformance harnesses;
- a published supported SDK/NuGet/template workflow outside this repository;
- native graphical theme preview plus remove/update/rollback commands; and
- an SDK Gallery widget covering the complete public primitive/state matrix.

Developer mode permits unsigned local packages but keeps process isolation, displays a persistent warning, and disables automatic background activation.

## Versioning and distribution

- Semantic host API versioning with one supported major and additive minor evolution
- Manifest declares minimum API and maximum understood major
- Protocol negotiation occurs before a widget is mounted
- Install to immutable versioned directories
- Stage, validate, handshake, and switch versions atomically
- Retain one known-good rollback version
- Production executable packages require publisher identity and tamper-evident signing
- Start with local sideloading; add a curated catalog only after security and rollback are proven
- A future catalog can link to Microsoft Store or WinGet packages rather than becoming an immediate hosting/payment platform

Signing proves publisher identity and integrity, not safety. Sandboxing and permissions remain mandatory.

## Explicitly rejected initial designs

- In-process community `.dll` loading with `AssemblyLoadContext`: useful dependency organization, not a security or crash boundary
- Electron or one WebView2 instance per widget
- A native plugin ABI inside the host
- Direct controller access from plugins
- Arbitrary XAML/HTML injection into the shell
- A permission named `full-system-access`
- Undeclared or unbounded background work

## Open questions for prototypes

1. Can AppContainer workers communicate through the selected named-pipe setup on every supported Windows version?
2. What worker cold-start latency is achievable with a framework-dependent .NET runtime?
3. Is a cached widget tree sufficient to make on-demand workers feel instantaneous?
4. Which UI primitives cover the first-party widgets without encouraging bespoke escape hatches?
5. Can a first-party module use the same public contract in-process without divergent behavior?
6. Should Windows 10 allow only trusted executable plugins if Windows 11 isolation is materially stronger?
7. Which permissions can be safely brokered without elevating the host?
