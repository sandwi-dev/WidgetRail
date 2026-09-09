# OverlayPlatformInterop

`OverlayPlatformInterop` is the version-2 native presentation-platform boundary.
It owns the single production Microsoft GameInput instance, supported Guide
callback, quarantined legacy Guide adapter, controller device/sample lifecycle,
repeat and neutral priming, Guide debounce, foreground-target memory, and safe
work-area placement entrypoint.

The boundary never creates a window, renderer, focus tree, widget transport, or
managed input reader. Its caller supplies the one presentation HWND lifecycle,
marshals event-availability callbacks onto that window's UI thread, and retains
shell action and semantic focus policy. All ABI structures carry `structSize`
and `abiVersion`; callers must require `WRAIL_OVERLAY_PLATFORM_ABI_VERSION`.
The managed ABI uses only fixed-width `uint32_t` scalars for Boolean
fields, parameters, return values, and out values (`0` is false; `1` is true),
and the public header asserts every managed-facing structure size and critical
offset. Focused verification links through the generated import library and
loads the built DLL rather than compiling a private copy of its implementation.

Shutdown is idempotent. It stops and unregisters both GameInput callbacks,
atomically closes callback admission and accounts entrants under one mutex,
waits for any callback already in flight to leave the opaque owner, clears
queued events, and only then marks shutdown complete and permits destroy to
release the owner. Concurrent shutdown callers wait for that completed state.

The legacy XInput Guide ordinal remains implemented only by
`OverlayHost/GuideInputCompatibility.*` and is consumed privately by this
boundary. It is not part of the public ABI.

## Opt-in controller isolation

Controller isolation is disabled by default. When explicitly enabled, a
detached Guardian owns one authenticated local session, the exact HidHide
policy delta, and a job-contained worker. The worker owns the selected physical
GameInput controller and one ViGEm Xbox 360 target; OverlayHost consumes only
the Guardian's bounded ordered input stream while the overlay is active. Its
versioned private protocol requires an explicit
`PrepareSession` command carrying exact selected-device enrollment and routing
authority before it creates a GameInput owner or ViGEm target. `Hello` and
`Heartbeat` never activate either backend. Preparation revalidates the exact
GameInput device ID, root ID, container ID, normalized PnP-path digest,
vendor/product identity, connected gamepad capability, and non-ViGEm ancestry;
unknown ancestry fails closed. Only then may the worker create its one owned
virtual target and delegate neutral/barrier behavior to
`ControllerIsolationCore`.

Guardian startup uses GameInput's blocking connected-device enumeration and
admits exactly one uniquely identified physical gamepad. Known virtual outputs
are excluded. Zero physical candidates are unavailable; multiple physical
candidates are ambiguous; and any unclassified connected gamepad fails closed.
All three outcomes occur before the HidHide plan is authored or applied.

Physical ancestry is accepted only when Configuration Manager resolves the
interface's exact device-instance property and the walk reaches the actual
device-tree root returned by `CM_Locate_DevNode(nullptr)`. Missing, malformed,
cyclic, over-depth, or partially failed ancestry is unknown and rejected; an
arbitrary `CM_Get_Parent` error is never interpreted as a physical root.

GameInput reading, device, and Guide callbacks publish fixed records into one
preallocated bounded multi-producer queue. The worker thread is the sole
consumer and the sole caller of the routing core and virtual output. Queue
pressure, conflicting source timestamps, disconnect, Guide-sink pressure,
authority mismatch, and lease expiry retire the owned target rather than drop
or reorder transitions. Guide edges remain passive ordered signals and never
enter the gamepad report.

Each routing cycle snapshots a finite reserved-tail barrier, so continuous
callback traffic cannot monopolize control work. A producer that has reserved
but not published a cell yields `Waiting`; only the existing queue-age budget
and a final acquire check can classify it as stuck. Current-reading samples are
taken only while output is contained. Their exact timestamp/state forms a
fence that retires delayed pre-fence reading callbacks while preserving every
post-fence edge; disconnect and Guide signals are never suppressed. Worker
control replies expose transition progress without blocking the request owner:
Enter waits for its pre-barrier drain and neutral submission, while Commit/Close
advance through the bounded neutral dwell. Input readings are batched in order
across worker, Guardian, and host so short button edges are not collapsed when
the host consumes more slowly than GameInput publishes. Every overlay entry
owns a monotonic interaction generation; close, containment without a live
host, and the next entry retire older queued UI input without retiring Guide.
Retirement preserves the last positive generation as a high-water mark, so a
delayed batch from that interaction cannot reactivate after the queue is empty.

The Guardian is outside the OverlayHost job and remains the sole effect owner
for both the persistent host pipe and a separate one-shot control pipe. A pipe
failure does not prove owner death: the Guardian keeps the virtual target
contained until the exact authenticated host process handle signals. Only then
may it finish the neutral barrier and resume game-facing output. The Guardian
creates its Worker suspended, admits it to the private job, and journals its
exact PID and creation time before resume. The Guardian also receives the
creator's expected non-secret routing authority and refuses a replacement
journal. Orphan recovery requires the
journaled Guardian and Worker process identities, paths, and SHA-256 hashes to
prove both exact processes exited before restoring the recorded HidHide delta.
Foreign HidHide applications, devices, activation state, and inverse-list
policy are never overwritten.

HidHide and ViGEmBus must already be installed. Use the exact Release
`OverlayHost.exe` commands below; each command authenticates the same Guardian
artifact and never starts the ordinary overlay UI:

```text
OverlayHost.exe --controller-isolation-enable
OverlayHost.exe --controller-isolation-status
OverlayHost.exe --controller-isolation-disable
OverlayHost.exe --controller-isolation-recover
```

Enable is accepted only from a missing journal under one per-session lifecycle
mutex; an unreadable or corrupt journal fails closed. Every phase replacement
and removal compares the complete persisted record first. Disable performs the
ordinary exact-delta restore. Recover is the explicit orphan path and cannot
mutate HidHide merely because transport failed; an unpublished zero-identity
startup record is retained for explicit diagnosis rather than guessed safe.
