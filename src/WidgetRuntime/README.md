# Widget runtime spike

## Native .NET worker bootstrap

Custom workers must use `WidgetWorkerBootstrap`; do not parse the host command
line or open runtime/broker pipes directly. A complete executable entrypoint is:

```csharp
using GameBarAlternative.WidgetRuntime;

return await WidgetWorkerBootstrap.RunAsync(args, () => new MyWidget());
```

The bootstrap validates the standard runtime arguments and treats the five
broker arguments as an all-or-none set. It authenticates the identity-bound
broker channel before invoking the factory, attaches the resulting typed
`WidgetHostServices` before `OnCreatedAsync`, handles Ctrl+C and caller
cancellation, and disposes transport state on every exit. Without broker
arguments, capabilities fail closed through the normal unavailable client.
Widgets never receive pipe names, nonces, broker JSON, or OS handles.

The factory parameter supports explicit constructor injection when useful:

```csharp
return await WidgetWorkerBootstrap.RunAsync(
    args, services => new MyWidget(services.Audio));
```

Unhandled startup errors are reduced to a generic process-safe diagnostic.
Factories with a closed, non-sensitive startup failure may throw
`WidgetWorkerBootstrapException`; its code and message are strictly bounded and
must never contain paths, credentials, or user data. Host-owned lifecycle
messages remain the only way to transition a running widget.

`WidgetProcessClient` is the host-side lifecycle/API boundary. Construction is
inert; the configured worker starts on the first render, action, controller
input, or activation request. Deactivation of an unstarted worker is the one
transition that stays inert. Each worker receives one random, current-user-only
named pipe in the trusted Job-only path, or one random SID/Low-label global pipe
in the installed/community AppContainer path, and must run `WidgetWorkerServer`
with the supplied command-line values.

`WidgetProcessOptions.IsolationPolicy` is trusted host policy, never manifest or
worker input. `RequireAppContainer` also requires a bounded host-owned
`IsolationKey`; for unsigned installations the bridge derives it from a
host-computed authority ID bound to the asserted publisher, package ID, and
exact immutable version, then supplies that version root through
`ReadOnlyPaths`. A different unsigned version receives a different profile and
cannot inherit the prior version's broker consent.
`HostTrustedJobOnly` remains a temporary platform-owned exception for bundled
Settings and YT Music workers that need desktop-user resources. The generic
installed-package bridge path always selects `RequireAppContainer`; a community
package cannot request the exception.

On Windows, `RequireAppContainer` opens or creates one stable profile derived
from that isolation key, grants its SID read/execute access only to the worker
executable directory and explicit read-only roots, and launches with a stripped
allowlisted environment. The token must be Low integrity, carry the exact
AppContainer SID, and contain zero capability SIDs; network is therefore denied.
Any profile, ACL, launch, or token-verification failure aborts startup with no
Job-only or desktop-token fallback.

The transport is little-endian 32-bit length-prefixed JSON. Both the runtime
envelope and renderer-neutral snapshots are independently versioned and reject
unknown JSON members. The default message ceiling is 1 MiB and all connect and
request waits are bounded. Unexpected exits, malformed traffic, transport
failures, and timeouts raise `Failed`; a later request lazily restarts the
worker until `MaximumRestartAttempts` is exhausted.

`SendControllerInputAsync` carries raw semantic controller input to the worker.
The SDK resolves dashboard inputs against the latest snapshot's `QuickActions`
and open-widget inputs first against the focused node, then against the
snapshot's explicit `ActiveInputScopeId`. The tree root is the default scope;
nested Stack/Row scopes may reuse bindings and do not leak lookup into their
parent or siblings. Input is rejected when its scope ID or rendered snapshot
sequence is stale, or when its focus ID is outside the active scope.
Widgets may override that default for richer controls. Dashboard quick actions
remain separate, and Guide is never transported to a worker. The native shell
therefore never needs per-widget button mappings.

Focus is optional for an open-widget event. This permits a shortcut declared on
the active Stack/Row scope—most importantly B on a focusless modal—to resolve
without inventing a focus target. The runtime never searches a parent/sibling
scope. A is reserved for focused button activation, D-pad is host focus
navigation, and the MVP accepts only `Pressed` shortcut bindings.

Direct, quick, dashboard, and open-widget actions enter the same FIFO with at
most 16 pending items plus one currently executing action.
`AdmitActionAsync` returns typed admission before potentially network-backed
work completes; `SendActionAsync` is the compatibility wrapper. One action runs
at a time, preserving cross-ingress order. Saturation or `Background` rejects
immediately; entering Background cancels current work, drops pending items, and
drains cooperative execution before deactivation completes. Later failures
raise `WidgetProcessClient.ActionFailed` without crashing the worker.
`ControllerActionFailed`, the `controller-action-failed` notification type, and
empty protocol-v1 acknowledgement payloads remain accepted compatibility
surfaces. The bridge also retains its protocol-v1 `controllerActionFailed`
reason string while routing failures from every ingress through it.

Only snapshot-correlated `ControllerInput` can carry dashboard gesture
authority. The legacy catalog `QuickAction` request is admitted to the same
queue but is deliberately non-authorizing. Worker dispatch creates ambient
gesture context only for the closed `PhysicalController` origin, and broker
requests carry its sequences only after the companion accepts the exact
capability/operation activation.

Lifecycle state is host-authoritative and separate from process lifetime:

| `WidgetLifecycleState` | Contract |
| --- | --- |
| `Created` | Runtime-owned initialization state. `OnCreatedAsync` runs exactly once, then the runtime transitions to `Background`. |
| `Background` | Default resident state after launch. The widget is not selected/open; visible UI work and controller-action admission stop, while explicitly permitted widget-lifetime background work may continue. |
| `Visible` | The widget's dashboard card is selected and may expose quick actions. It is visible but not the open input surface. |
| `Interactive` | The widget is open and owns its scoped non-Guide controller actions. |
| `Destroying` | Runtime-owned terminal shutdown. Widget-owned tokens are canceled before bounded cleanup hooks run. |

Only `Background`, `Visible`, and `Interactive` are host-requestable through
`WidgetProcessClient.SetLifecycleStateAsync`. A `Background` request for an
unstarted worker is a lazy no-op. `Created` and `Destroying` cannot be requested
through the host transport. Repeating a stable state is idempotent, and a
destroying widget cannot transition again. `SetActiveAsync` remains only a
compatibility mapping (`true` to `Interactive`, `false` to `Background`).

The SDK exposes three different cancellation lifetimes:

- `WidgetLifetimeToken` spans `Created`, `Background`, `Visible`, and
  `Interactive`, then cancels before `Destroying` cleanup. Use it only for
  explicitly permitted process/widget-lifetime work.
- `StateLifetimeToken` belongs to exactly one state. The previous token is
  canceled before `OnLifecycleStateChangedAsync(previous, current,
  stateLifetime)` runs with the new token.
- `ActiveCancellationToken` is the compatibility visible-lifetime token. It
  spans both `Visible` and `Interactive`, survives transitions between them,
  and cancels before the transition callback into `Background`.

Hook order is deterministic: `OnCreatedAsync`, then
`OnLifecycleStateChangedAsync(Created, Background)`; every stable transition
then calls the lifecycle-changed hook. Entering the visible lifetime calls
legacy `OnActivatedAsync` after that hook, and returning to `Background` calls
legacy `OnDeactivatedAsync` after it. `Visible` to/from `Interactive` calls only
the lifecycle-changed hook. Destruction cancels state, visible, and widget
tokens, optionally calls legacy deactivation if still visible, then calls
`OnDestroyingAsync`; it does not report a terminal lifecycle-changed event.
Worker destruction has a bounded two-second shutdown window.

Visible UI refresh work should use `RunPeriodicUpdatesWhileActiveAsync` or
`InvalidatePeriodicallyWhileActiveAsync`. Ticker callbacks are serial and
non-overlapping; cancellation completes normally, while callback failures fault
the returned task and must be observed. Work exclusive to one state should use
its state token. A widget with a legitimate background capability may own
widget-lifetime work, but permission and residency-policy enforcement are not
implied by the lifecycle API. The current audio/network broker denies all
capabilities in Background. On Windows, trusted host policy separately assigns
the complete worker process tree to one accounting Job before resume and keeps
kill-on-close cleanup for every worker generation.

The planned policy choices are `keep-alive` (default),
`suspend-when-hidden`, and `unload-after-idle`. The latter two must be explicit
manifest/user choices. The supervisor must never infer idle unload from a timer
or resource heuristic. The current manifest's `none`/`suspend` strings remain
validated metadata rather than enforcement of these final policy names.

`WidgetBridge` is the narrow native-facing sidecar around
`WidgetProcessClient`. Windows workers are created suspended. Their token is
verified, they are assigned to a Job Object, and only then are they resumed.
The job admits child processes while preserving process-tree accounting,
kill-on-close, die-on-unhandled-exception, and basic UI restrictions. It does
not impose an arbitrary private-memory or one-process ceiling. Win32k system-call
disable was tested but is not enabled because CoreCLR failed DLL initialization
with `0xC0000142`. CPU quotas, disk/profile quotas and profile cleanup,
publisher verification/revocation, audit UI, and lifecycle residency-policy
enforcement are not claimed by this transport.

`WidgetProcessOptions.CompanionSessionFactory` is trusted host policy invoked
afresh for every worker start/restart. The bridge uses it to create an
identity/declaration/consent/backend-fixed broker companion and append only its
bounded bootstrap arguments. Companion lifecycle is updated before the worker
lifecycle request. The generic worker authenticates that channel and attaches
typed SDK host services before widget lifecycle creation. Widget protocol messages cannot
provide a companion or choose its identity/backend. See [widget
capabilities](../../docs/capabilities.md).

For an AppContainer session the companion receives the runtime-derived SID and
must expose a host-secured plain pipe name. Both the main and broker endpoints
are random global, single-client pipes whose ACLs name only the desktop host and
that SID and whose mandatory label permits Low-integrity access. The runtime
binds the companion to the exact started PID before either server accepts a
client. PID checks are additive to the main runtime hello and broker nonce plus
package/publisher/instance authentication; they do not replace protocol
identity validation.
