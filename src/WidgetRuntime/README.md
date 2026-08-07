# Widget runtime spike

`WidgetProcessClient` is the host-side lifecycle/API boundary. Construction is
inert; the configured worker starts on the first render, action, controller
input, or activation request. Deactivation of an unstarted worker is the one
transition that stays inert. Each worker receives one random, current-user-only
named pipe and must run
`WidgetWorkerServer` with the supplied command-line values.

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

Resolved dashboard and open-widget actions enter the same FIFO with at most 16
pending items plus one currently executing action, and acknowledge when
accepted, before potentially network-backed action work completes. One action
runs at a time, preserving rapid input order. Saturation or `Background` state
returns unhandled immediately; entering Background cancels current work and
drops pending items. Post-acknowledgement failures raise
`WidgetProcessClient.ControllerActionFailed` without crashing the worker.

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
capabilities in Background. On Windows, trusted host policy separately applies
a Job Object memory ceiling, one-active-process limit, and kill-on-close cleanup
to every worker.

The planned policy choices are `keep-alive` (default),
`suspend-when-hidden`, and `unload-after-idle`. The latter two must be explicit
manifest/user choices. The supervisor must never infer idle unload from a timer
or resource heuristic. The current manifest's `none`/`suspend` strings remain
validated metadata rather than enforcement of these final policy names.

`WidgetBridge` is the narrow native-facing sidecar around
`WidgetProcessClient`. Windows workers are created suspended, assigned to their
Job Object before any worker code runs, and then resumed. AppContainer launch,
CPU quotas, publisher verification, and lifecycle residency-policy enforcement
are not claimed by this transport.

`WidgetProcessOptions.CompanionSessionFactory` is trusted host policy invoked
afresh for every worker start/restart. The bridge uses it to create an
identity/declaration/consent/backend-fixed broker companion and append only its
bounded bootstrap arguments. Companion lifecycle is updated before the worker
lifecycle request. The generic worker authenticates that channel and attaches
typed SDK host services before widget creation. Widget protocol messages cannot
provide a companion or choose its identity/backend. See [widget
capabilities](../../docs/capabilities.md).
