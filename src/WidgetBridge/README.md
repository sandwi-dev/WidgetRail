# Native host to managed widget bridge

The bridge is a disposable managed sidecar. The native overlay starts it with a
random pipe name and a host catalog path; the bridge creates a
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

Dashboard quick actions are widget-snapshot commands shown while a dashboard
card is selected (the initial “hover” interaction). The normal native path is a
`controller-input` whose dashboard context, button, positive input sequence,
and snapshot sequence identify one action in the bridge's latest validated
cached snapshot. The catalog `quick-action` request remains only as a bounded
tooling/compatibility path for host-owned catalog metadata; native code must not
know widget action IDs.

`controller-input` is the normal native integration path. Its input contains
`button`, `phase`, `context`, optional `focusedElementId`, `sequence`,
`monotonicTimestampMicroseconds`, `activeInputScopeId`, and
`snapshotSequence`. For `dashboardQuickAction`, the SDK resolves the button
from the latest rendered snapshot's `quickActions`. The host owns A activation,
B close, Y reorder, D-pad/analog navigation, and Guide; widgets may declare X,
bumpers, triggers, stick clicks, Menu, or View as dashboard quick actions.

A snapshot quick action may optionally name one `WidgetQuickActionCapability`
(capability ID plus operation ID). For a Pressed event while the widget remains
Visible, the bridge revalidates the exact cached snapshot/button/sequence,
closed control operation, package declaration, and worker session before
recording a dormant host-owned reservation for at most 10 seconds. The
reservation is not broker authority; it only allows bounded serial widget work
to reach its exact call. When the exact typed operation is invoked, the runtime
atomically matches and removes the reservation and asks the identity/PID-bound
companion to activate one broker lease for at most two seconds. The broker
consumes the exact operation once and still enforces consent, payload, provider,
and revocation checks. Neither stage promotes lifecycle or authorizes
subscriptions/background work, and neither can be minted by widget IPC.

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

`WidgetBridgeServer` owns only the pipe session, framing, request routing,
reserved Stop handling, replies, and serialized writes. One internal client
registry owns the current catalog revision and every worker generation,
including operation serialization, residency admission, cached snapshots,
idle unload, restart, catalog replacement/removal, and terminal disposal.
Request handlers consume typed registry operations and immutable results; they
never retain mutable registration objects. Each generation has one bounded
notification lane: it retains only the latest queued invalidation and at most
32 ordered, non-coalescible action/runtime failures. Overflow is counted with a
saturating counter, never converted into an unbounded task or exception list.
Only an accepted lane item receives a publication lease; coalesced, full, and
closed admissions do not create one.

A retiring generation remains the reserved widget slot, closed to new
admission, until its exact-generation publication leases, tracked idle work,
client, and residency lease drain. Retirement closes and cancels its
notification lane before waiting for admitted sends. Lane cancellation may
withdraw an event while it waits for the server's serialized writer. One
internal event-write boundary owns writer admission, the fixed four-second
in-flight deadline, and session abort through one adapter to the real frame
channel. Once a frame starts, publication cancellation no longer interrupts
it; a stalled frame terminates the session before another frame can use the
possibly partial stream. This changes only internal pipe-session behavior, not
the framing format or public protocol. Registry identity changes
happen under the catalog gate, while external client cancellation and disposal
start only after leaving that gate. A cancelled or timed-out restart transfers
the reserved generation to the same tracked exact-once retirement path, so
concurrent requests cannot create a competing worker. Replies and admitted
events retain their internal generation lease through the server's serialized
send. Restart prepares and lifecycle-restores one fresh client before
publication; every unpublished client is disposed on failure. Terminal
disposal owns active retirement tasks in one registry set, with each
registration's resource and terminal completion outcomes shared by waiters. It
attempts every client and retains only a saturating failure count plus the first
failure before completing its one shared outcome; registrations do not retain
an unconsumed duplicate retirement task.

A launched Background worker remains resident under the default `keep-alive`
policy. Entering Background cancels the shared Visible/Interactive lifetime
used by presentation work, but explicitly permitted widget-lifetime background
work may continue. Manifest `residencyPolicy` schema 1 also supports
`suspend-when-hidden` and an explicit 5–86,400-second `unload-after-idle`.
Suspension is cooperative lifecycle cancellation, not Windows thread
suspension. Idle unload serializes with operations, caches the last validated
snapshot, sends bounded Destroying, releases the worker and companion, and
recreates the worker lazily when it becomes visible. Intentional unload does not
consume crash budget. The capability broker denies normal operations and every
subscription in Background.

Process launch is additionally gated by a supervisor-owned aggregate envelope:
eight application workers and 512 MiB of declared Job memory by default.
`--max-resident-workers` and `--max-resident-memory-mb` provide bounded trusted
launch-time overrides. Admission is serialized before launch; capacity refusal
does not evict an existing `keep-alive` worker. Reservations are released by
failed launch/crash, idle unload, restart retirement, catalog removal, and
shutdown. The exact trusted Settings identity uses one separate control-plane
slot and reports both envelopes through private diagnostics.

For a widget with closed declared capabilities, the bridge creates a fresh
`BrokerWidgetProcessCompanion` on every worker start/restart. Package,
publisher, instance, declarations, consent store, backend, random pipe, and
nonce are bridge-owned. The runtime propagates only host lifecycle to its
`BrokerPipeServer`; worker messages cannot promote broker lifecycle. The worker
bootstrap authenticates the nonce/full identity and attaches typed
`WidgetHostServices` before widget creation. For installed/community workers,
the companion pipe is additionally ACLed to the runtime's exact AppContainer
SID, labeled for Low-integrity access, and bound to the exact started PID before
accept. The production bridge composes the narrow real Core Audio, Windows
network/Bluetooth, foreground-activity, Start Menu app-library, and GSMTC
media-session providers plus constrained loopback JSON and write-only package
secret services;
deterministic tests use `SimulatedPlatformBrokerBackend`.
See [widget capabilities](../../docs/capabilities.md) and [local companion HTTP
and private secrets](../../docs/community-companion-services.md).

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
  "genericWorkerExecutable": "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
  "widgets": [],
  "bundledWidgets": [{
    "id": "media-sessions",
    "packageId": "org.gbar.firstparty.media-sessions",
    "instanceId": "media-sessions.default",
    "packageRoot": "runtime/MediaSessions",
    "icon": "music",
    "quickActions": []
  }]
}
```

For a trusted `widgets` entry, `styleFile` is optional and must be a normalized
package-relative `.gbss` path under the catalog directory. For a
`bundledWidgets` or installed package, the bridge discovers
`styles/default.gbss` under its immutable package root. The styling package
loader rejects absolute paths, schemes, backslashes, `.`/`..` traversal,
reparse-point escapes, oversized sources, unsafe imports, and invalid GBSS. A
configured invalid style prevents that entry from publishing, with bounded
relative-file, line, column, code, and single-line diagnostics; absolute
package paths are not disclosed. Widgets without a style file receive empty
`base` and `focused` maps for every node.

`icon` is an optional closed `WidgetGlyph` semantic value (`music`, `settings`,
`connection`, and the other SDK glyphs). It defaults to `connection`. Unknown
values fail catalog loading; widgets cannot supply SVG, font, file, or drawing
payloads through the descriptor.

The host catalog has two distinct sections. `widgets` contains the small set of
trusted Job-only exceptions with host-owned executable, arguments, and policy.
`bundledWidgets` contains platform-shipped packages intentionally held to the
community execution model: the catalog pins shell ID, package ID/root, instance,
icon, and optional presentation metadata, while the package manifest supplies
publisher, entrypoint, name, capabilities, memory request, residency, and
styles. These entries use the packaged generic worker and mandatory host-owned
AppContainer identity; a manifest or worker message cannot request Job-only
execution.

At startup the bridge also discovers the current-user `WidgetCatalog` and joins
enabled, host/architecture-compatible packages in persisted order. It assigns
each installed package a fixed host-owned 64 MiB cap, exact read-only package
root, package-specific AppContainer identity, and the packaged
`WidgetWorkerHost`. Listing remains lazy and does not launch workers. Disabled
packages stay inert. Invalid installed state or package integrity publishes a
trusted-only catalog revision; Community registrations are removed
synchronously and running workers retire before a stale ID can relaunch. A
conflicting, incompatible, unsupported-capability, or invalid-GBSS installed
package is skipped with a bounded warning. Supported
required and optional manifest declarations are combined into the broker
declaration set; neither kind is auto-granted.

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

Trusted bundled Settings temporarily remains Job-only for desktop-user
resources not yet brokered. YT Music is no longer an entry in this trusted
section: it installs through the Community catalog and uses the generic worker,
AppContainer, loopback/secret broker, lifecycle, and consent path. The Settings
exception is host policy and cannot be introduced through catalog JSON or a
package manifest.

Audio Mixer, Network Controls, Games & Apps, and Now Playing are
`bundledWidgets`, not trusted-worker shortcuts. A Windows conformance suite
builds and installs those same four package layouts, merges them through
`WidgetCatalog`/`BridgeCatalog`, launches the generic worker in the package
AppContainer, drives lifecycle, validates a snapshot, and observes a simulated
brokered action. `gbar dev` points `--installed-catalog-root` at a unique
session catalog, so local author builds traverse this same bridge path without
mutating the user's installed catalog. YT Music adds a fifth Community-package
conformance case built and installed by the public CLI rather than appearing in
`bundledWidgets`.

The bridge watches current-user catalog state and package changes without
polling, publishes complete semantic revisions, preserves compatible workers,
and retires workers whose identity, code, declarations, or isolation policy
changes. Reload/list remains lazy. This integration is still not a marketplace
or publisher-trust guarantee: production needs package signatures/revocation,
CPU quotas, disk/profile quotas and cleanup, and a broker security audit/history
surface.
