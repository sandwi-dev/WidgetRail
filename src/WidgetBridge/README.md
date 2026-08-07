# Native host to managed widget bridge

The bridge is a disposable managed sidecar. The native overlay starts it with a
random pipe name and a trusted catalog path; the bridge creates a
current-user-only named-pipe server and exits after the native connection ends.
The native host never loads the runtime, SDK, or third-party widget assemblies.

```powershell
WidgetBridge.exe --host-pipe gba-host-<random> --catalog widgets.json
```

The bridge waits 10 seconds for its native client by default. Configure
`--accept-timeout-ms` and `--max-message-bytes` when launching it. The default
message ceiling is 1 MiB and the absolute ceiling is 4 MiB.

## Framing and envelope

Every message is UTF-8 JSON preceded by a 4-byte unsigned-equivalent,
little-endian length. The length must be in `1..maximumMessageBytes`. JSON is
strict: property names are camelCase, enum strings are camelCase, and unknown
members are rejected.

```json
{
  "protocolVersion": 1,
  "type": "get-snapshot",
  "requestId": 3,
  "payload": { "widgetId": "clock" }
}
```

Native requests use nonzero monotonically increasing `requestId` values.
Responses repeat that value. Events have `requestId: 0`. The first request must
be `hello` with `{ "clientName": "OverlayHost" }`; the bridge responds with
`hello-accepted`.

## Requests

| Type | Payload | Response |
| --- | --- | --- |
| `list-widgets` | `{}` | `widgets` with public descriptors, semantic icons, and quick actions |
| `get-platform-appearance` | `{}` | `platform-appearance` with revision, selected theme, bounded settings, and typed shell styles |
| `get-snapshot` | `{ "widgetId": "clock" }` | `snapshot` with `widgetId`, validated protocol snapshot, and computed `renderStyles` |
| `set-widget-lifecycle` | `{ "widgetId": "clock", "state": "visible" }` | `acknowledged` |
| `action` | `{ "widgetId": "clock", "action": WidgetActionEvent }` | `acknowledged` |
| `quick-action` | `{ "widgetId": "clock", "quickActionId": "refresh", ... }` | `acknowledged` |
| `controller-input` | `{ "widgetId": "clock", "input": ControllerInputEvent }` | `controller-input-result` with `handled` |
| `stop` | `{}` | `acknowledged`, then clean sidecar exit |

Quick actions are catalog-defined commands shown while a dashboard card is
focused (the initial “hover” interaction). The native host sends only the
public quick-action ID. The bridge substitutes the trusted action ID, source
element ID, and controller binding before forwarding it to the lazily launched
widget worker.

`controller-input` is the normal native integration path. Its input contains
`button`, `phase`, `context`, optional `focusedElementId`, `sequence`,
`monotonicTimestampMicroseconds`, `activeInputScopeId`, and
`snapshotSequence`. For `dashboardQuickAction`, the SDK resolves the button
from the latest rendered snapshot's `quickActions`. The host owns A activation,
B close, Y reorder, D-pad/analog navigation, and Guide; widgets may declare X,
bumpers, triggers, stick clicks, Menu, or View as dashboard quick actions.

For `openWidget`, the SDK requires `activeInputScopeId` and
`snapshotSequence` to match its latest snapshot. It checks a focused-node
shortcut and then the explicitly active Stack/Row/Scroll scope. A focus ID outside the
scope, a stale sequence, or the wrong scope returns unhandled. Focus may be
absent, allowing a modal container's B shortcut to resolve. Lookup never
bubbles into parent or sibling scopes.

The MVP native host emits only `pressed`. A is reserved for focused button
activation and D-pad for focus navigation, so neither may be declared as an
open-widget shortcut. A widget may override the SDK handler for richer controls
within the semantic events the host actually transports. The older `action`
and catalog `quick-action` messages are tooling/compatibility paths; native code
must not contain per-widget action IDs.

Lifecycle state is host-authoritative, not worker-process state. The bridge
accepts only `background`, `visible`, or `interactive`; `created` and
`destroying` are runtime-owned and rejected on this transport. The native host
publishes `visible` for the selected dashboard card, `interactive` for the open
widget, and `background` before hiding or switching it. Requesting Background
for an unstarted worker remains lazy and does not launch it. Duplicate stable
transitions are idempotent.

A launched Background worker remains resident by default. Entering Background
cancels the shared Visible/Interactive lifetime used by periodic UI updates,
but explicitly permitted widget-lifetime background work may continue. Future
`suspend-when-hidden` or `unload-after-idle` behavior must be an opt-in
manifest/user policy, not a bridge heuristic. The current bridge does not
enforce those residency policies. The current capability broker separately
denies every audio/network capability in Background.

For a widget with closed declared capabilities, the bridge creates a fresh
`BrokerWidgetProcessCompanion` on every worker start/restart. Package,
publisher, instance, declarations, consent store, backend, random pipe, and
nonce are bridge-owned. The runtime propagates only host lifecycle to its
`BrokerPipeServer`; worker messages cannot promote broker lifecycle. The worker
bootstrap authenticates the nonce/full identity and attaches typed
`WidgetHostServices` before widget creation. For installed/community workers,
the companion pipe is additionally ACLed to the runtime's exact AppContainer
SID, labeled for Low-integrity access, and bound to the exact started PID before
accept. The production bridge composes the narrow real Core Audio and Windows
network providers; deterministic tests use `SimulatedPlatformBrokerBackend`.
See [widget capabilities](../../docs/capabilities.md).

Asynchronous `widget-invalidated` and `widget-failed` events identify the widget
by catalog ID. `platform-appearance-changed` instead contains only the newly
published global revision. Worker executable paths and arguments are never
returned to the native process.

## Platform appearance and live theme revisions

At startup the bridge loads strict current-user appearance settings and an
exact theme ID/version through `PlatformSettings`. It watches only
`platform-settings.json` and the versioned theme tree with `FileSystemWatcher`;
there is no scan or timer poll while files are unchanged. Write/create/delete/
rename bursts are coalesced for 200 ms.

A valid reload publishes one immutable revision, clears per-widget layered-
theme caches, and emits:

```json
{
  "protocolVersion": 1,
  "type": "platform-appearance-changed",
  "requestId": 0,
  "payload": { "revision": 4 }
}
```

Invalid settings, missing/invalid theme versions, unsafe imports, or GBSS
errors retain the last valid appearance and revision. A client retrieves the
complete current value with `get-platform-appearance`; the response includes
the exact theme ID/version, finite bounded interface/text scale and backdrop
opacity, motion preference, and typed maps for 12 semantic shell states. This
request never launches a widget worker.

For widget snapshots, the bridge compiles explicit platform → widget → user
layers. Higher layer priority wins before selector specificity, so a user
semantic-role rule can override a widget ID rule. Static selected/disabled
snapshot state participates in both complete `base` and `focused` maps. The
cache key includes the global revision.

The native client consumes the initial platform appearance and later revision
events, coalesces the latest announced revision, ignores stale values, and
retains its last good state after a failed refresh. It applies supported shell
styles, interface geometry, shell DirectWrite text scale, backdrop opacity, and
motion without launching or restarting widget workers. It also passes bounded
platform text scale through the native post-style accessibility policy into
generic declarative widget text and layout, preserving non-compounding `em`
inheritance. A global change reaches widget pixels when the host requests that
widget's next snapshot.

## Computed render styles

Native code never reads or parses GBSS. The bridge loads each configured widget
style package, layers it with the current platform/user theme, and compiles
typed results per global revision. Every `snapshot` response has
this exact additional shape:

```json
{
  "protocolVersion": 1,
  "type": "snapshot",
  "requestId": 3,
  "payload": {
    "widgetId": "clock",
    "snapshot": { "protocolVersion": 1, "sequence": 1 },
    "renderStyles": {
      "refresh": {
        "base": {
          "font-size": {
            "kind": "length",
            "text": "18px",
            "number": 18,
            "unit": "px"
          }
        },
        "focused": {
          "scale": {
            "kind": "number",
            "text": "1.04",
            "number": 1.04,
            "unit": null
          }
        }
      }
    }
  }
}
```

`renderStyles` contains every snapshot node keyed by its stable node ID, even
when both state maps are empty. `base` and `focused` are independently complete
computed styles; native must select the appropriate map rather than implement a
cascade or merge pseudo-state rules itself. Resolution uses the lowercase node
kind (`stack`, `row`, `scroll`, `text`, `button`, `progress`, `spacer`, `image`, or
`icon`) as its role plus the node ID and style classes. Value `kind` is one of
`color`, `length`, `lengthList`, `number`, `integer`, `ratio`, `duration`,
`keyword`, or `fontFamily`. `text`, `number`, and `unit` preserve the compiler's
typed canonical value; numeric and unit fields are explicitly `null` when they
do not apply.

The bridge enforces the protocol's 2,048-node ceiling, at most 64 properties per
node state, at most 32,768 properties per response, bounded property/value
strings, and the negotiated length-prefixed message ceiling.

## Catalog

```json
{
  "catalogVersion": 1,
  "widgets": [{
    "id": "clock",
    "packageId": "dev.example.clock",
    "publisherId": "dev.example",
    "name": "Clock",
    "instanceId": "clock.default",
    "icon": "connection",
    "workerExecutable": "workers/ClockWidget.Worker.exe",
    "workerArguments": [],
    "declaredCapabilities": [],
    "memoryLimitMb": 64,
    "styleFile": "workers/styles/default.gbss",
    "quickActions": [{
      "id": "refresh",
      "label": "Refresh",
      "actionId": "refresh",
      "sourceElementId": "refresh",
      "controllerButton": "x"
    }]
  }]
}
```

`styleFile` is optional and must be a normalized package-relative `.gbss` path
under the catalog directory. The styling package loader rejects absolute paths,
schemes, backslashes, `.`/`..` traversal, reparse-point escapes, oversized
sources, unsafe imports, and invalid GBSS. A configured invalid theme prevents
bridge startup with bounded relative-file, line, column, code, and single-line
diagnostics; absolute package paths are not disclosed. Widgets without a style
file receive empty `base` and `focused` maps for every node.

`icon` is an optional closed `WidgetGlyph` semantic value (`music`, `settings`,
`connection`, and the other SDK glyphs). It defaults to `connection`. Unknown
values fail catalog loading; widgets cannot supply SVG, font, file, or drawing
payloads through the descriptor.

The JSON catalog above is trusted bundled installation state. At startup the
bridge also discovers the current-user `WidgetCatalog` and joins enabled,
host/architecture-compatible packages in persisted order. It assigns each a
fixed trusted 64 MiB worker policy, mandatory host-owned AppContainer identity,
exact read-only package root, and the packaged `WidgetWorkerHost`; no manifest
or worker message can request the trusted Job-only policy. Listing the catalog
remains lazy and does not launch workers. Disabled packages stay inert. A
malformed catalog falls back to bundled widgets, while a conflicting, tampered,
incompatible, unsupported-capability, or invalid-GBSS installed package is
skipped with a bounded warning. Supported required and optional manifest
declarations are combined into the fixed broker channel declaration set;
neither kind is auto-granted.

`WidgetWorkerHost` loads the manifest entrypoint only from the immutable package
root, rejects path escape/reparse points, requires a public concrete SDK
`Widget` type, and resolves dependencies inside the package. Installed package
workers run in a package-specific, capability-free Low-integrity AppContainer
with a stripped environment, explicit read/execute runtime and package grants,
and Job Object memory/process/UI/cleanup restrictions. Main and
broker pipes verify the exact worker PID in addition to their protocol
authentication. Isolation setup is fail-closed; there is no Job-only fallback.
The native host and bridge never load the widget assembly.

Win32k system-call disable is not active: testing that mitigation caused
CoreCLR DLL initialization failure (`0xC0000142`). Job Object UI restrictions
remain enabled.

Trusted bundled Settings and YT Music entries temporarily remain Job-only
because they require desktop-user resources not yet brokered. This exception is
bundled host policy and cannot be introduced through catalog JSON or a package
manifest.

The bridge watches current-user catalog state and package changes without
polling, publishes complete semantic revisions, preserves compatible workers,
and retires workers whose identity, code, declarations, or isolation policy
changes. Reload/list remains lazy. This integration is still not a marketplace
or publisher-trust guarantee: production needs package signatures/revocation,
CPU quotas, disk/profile quotas and cleanup, and a broker security audit/history
surface.
