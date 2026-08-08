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
selected. Background denies broker capabilities. Widgets cannot promote their
own lifecycle or override host residency over IPC.

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
  valid under keep-alive; broker capabilities are still unavailable in
  Background.
- Treat `OnDestroyingAsync` as bounded cleanup, not a final save opportunity.
- Persist required durable state as it changes through an approved facility;
  an unloaded widget is reconstructed as a new object.
- Keep all semantic IDs stable so focus and scroll restoration remain useful.
- Test lifecycle callbacks with `WidgetTestHost`; test a packaged worker when
  process teardown/recreation behavior matters.

## Legacy manifest migration

Manifest-v1 prototypes remain deterministic:

| Legacy field | Resolved schema-1 mode |
| --- | --- |
| omitted or `"backgroundPolicy": "none"` | `keep-alive` |
| `"backgroundPolicy": "suspend"` | `suspend-when-hidden` |

New packages should emit `residencyPolicy`. A manifest cannot declare both the
legacy field and the versioned object.

Settings > Installed widgets shows the resolved policy, including the exact
idle duration and an explicit warning that suspension is cooperative rather
than thread suspension.
