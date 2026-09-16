# Widget lifecycle and process residency

Status: `residencyPolicy` schema 1 is implemented for bundled and installed
workers.

Lifecycle answers **what work is currently valid**. Residency answers **whether
the worker process remains allocated while that work is not visible**. They are
deliberately separate contracts.

## Lifecycle contract

The host alone moves a widget through `Created`, `Background`, `Visible`,
`Interactive`, and `Destroying`. Authors respond through `OnCreatedAsync`,
`OnLifecycleStateChangedAsync`, `OnActivatedAsync`, `OnDeactivatedAsync`, and
`OnDestroyingAsync`, together with `WidgetLifetimeToken`, `StateLifetimeToken`,
and `ActiveCancellationToken`.

Every policy enters `Background` when the overlay hides or another widget is
selected, unless a pinned surface still needs it Visible or Interactive.
Background denies ordinary manifest-declared broker capabilities.
The bounded host-granted `HostServices.PrivateState` service is the explicit
persistence exception; it does not authorize provider polling, UI refresh, or
lifecycle promotion. Widgets cannot promote their own lifecycle or override
host residency over IPC.

## Manifest schema

Omitting `residencyPolicy` means keep-alive.

```json
"residencyPolicy": {
  "schemaVersion": 1,
  "mode": "unload-after-idle",
  "idleSeconds": 120
}
```

| Mode | Process while hidden | Author-visible behavior | Host behavior |
| --- | --- | --- | --- |
| `keep-alive` | Resident after first launch | Background callback/tokens; explicitly authored widget-lifetime work may continue | No inferred eviction |
| `suspend-when-hidden` | Resident | Background callback/tokens; visible/state work must stop | Suppresses hidden snapshots, invalidations, input, and broker access |
| `unload-after-idle` | Resident until the explicit idle bound, then destroyed | Background first; `Destroying` if the bound expires | Caches last validated snapshot, bounded teardown, lazy recreation on visibility |

The idle duration measures time in Background, not time since the last input.
Unexpected failures of the bridge's idle-unload task record a widget lifetime
diagnostic with `failure=idle-unload-failed`. Normal cancellation when a widget
becomes visible or is retired does not produce that failure diagnostic.

The bridge also owns host-wide worker accounting. Application-worker count has
no framework limit by default. Admission is atomic and happens before launch.
When a user or administrator supplies the optional positive
`--max-resident-workers` cap and that selected limit is full, the requested
worker remains stopped and the host receives an actionable error; an existing
`keep-alive` worker is never silently evicted.
Crash, failed launch, idle unload, restart retirement, catalog removal, and
bridge shutdown release the reservation exactly once. The exact trusted
Settings worker has one separate control-plane slot so the diagnostics surface
remains reachable when the application envelope is full. Optional manifest
memory guidance is reported for both envelopes but is not reserved capacity.

The bridge process accepts the trusted launch-time option
`--max-resident-workers` as an explicit positive application-worker cap. Zero,
negative, missing, duplicate, and non-integer values are rejected. Omitting the
option leaves application-worker count uncapped. The former
`--max-resident-memory-mb` option is rejected rather than pretending to enforce
a full-application memory ceiling.
Every admitted worker and helper remains in one non-breakaway accounting Job
with kill-on-close cleanup.

`idleSeconds` is required only for `unload-after-idle` and must be an integer
from 5 through 86,400. Unknown schema versions, unknown modes, a stray/missing
duration, and duplicate old/new policy declarations fail manifest validation.

## Safe suspension

Suspend-when-hidden is cooperative. It does not call undocumented Windows
process suspension, freeze arbitrary threads, or stop a process while it owns
locks. Authors must bind presentation and provider work to lifecycle tokens and
make callbacks return promptly. The host provides the boundary; it cannot make
uncooperative author code correct.

## Idle unload sequence

1. The native host publishes `Background`.
2. The bridge caches every last-good validated declarative snapshot and starts
   the manifest's exact idle delay only after a running worker is Background.
3. Visibility, input, or another allowed operation cancels that generation of
   the delay. Cancellation is not a worker failure.
4. At expiry, the bridge serializes against worker operations, publishes
   `Destroying` to the capability companion, asks the runtime to stop, and
   terminates the Job Object process tree if bounded cleanup does not finish.
5. The last snapshot remains available while hidden. A Visible/Interactive
   transition creates a new worker and companion, restores lifecycle, and asks
   for a fresh snapshot. Stable semantic element IDs allow native focus memory
   to resolve against that snapshot.

An intentional unload does not consume the crash-loop restart allowance.
Crashes, protocol violations, request timeouts, catalog replacement, user
disable, and bridge shutdown keep their existing independent failure paths.

## Author checklist

- Use `ActiveCancellationToken` for work shared by Visible and Interactive.
- Use `StateLifetimeToken` for state-exclusive work.
- Use the widget token only for intentionally process-lifetime work that is
  valid under keep-alive; ordinary manifest-declared broker capabilities are
  still unavailable in Background.
- Treat `OnDestroyingAsync` as bounded cleanup, not a final save opportunity.
- Persist small durable preferences as meaningful changes occur through
  `HostServices.PrivateState`; restore once on first `OnActivatedAsync`, not
  `OnCreatedAsync`. An unloaded widget is reconstructed as a new object.
- Keep all semantic IDs stable so focus and scroll restoration remain useful.
- Test lifecycle callbacks with `WidgetTestHost`; test a packaged worker when
  process teardown/recreation behavior matters.
- Prefer `unload-after-idle` for ordinary widgets. The controller-widget
  scaffold and Clock sample use a five-minute bound. Choose `keep-alive` only
  when process-lifetime Background continuity is an explicit requirement.

## Legacy manifest migration

Manifest-v1 prototypes remain deterministic:

| Legacy field | Resolved schema-1 mode |
| --- | --- |
| omitted or `"backgroundPolicy": "none"` | `keep-alive` |
| `"backgroundPolicy": "suspend"` | `suspend-when-hidden` |

New packages should emit `residencyPolicy`. A manifest cannot declare both the
legacy field and the versioned object.

Settings > Widgets shows the resolved policy, including the exact
idle duration and an explicit warning that suspension is cooperative rather
than thread suspension.
